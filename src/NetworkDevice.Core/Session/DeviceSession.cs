using System.Text;
using System.Text.RegularExpressions;

namespace NetworkDevice.Core.Session;

public sealed class DeviceSession : IAsyncDisposable
{
    private static readonly Regex AnsiEscape = new(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])", RegexOptions.Compiled);
    private const string MoreMarker = "--More--";

    private readonly ITransport _transport;
    private readonly SessionOptions _options;
    private readonly byte[] _readBuffer = new byte[4096];
    private bool _connected;

    public DeviceSession(ITransport transport, SessionOptions options)
    {
        _transport = transport;
        _options = options;
    }

    public bool IsConnected => _connected && _transport.IsOpen;

    public SessionOptions Options => _options;

    public ITransport Transport => _transport;

    public string? CurrentPrompt { get; private set; }

    public ExecMode Mode { get; private set; } = ExecMode.UserExec;

    public event Action<string>? RawOutput;

    public void EmitRawOutput(string raw) => RawOutput?.Invoke(raw);

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _transport.OpenAsync(cancellationToken);
        try
        {
            // 1. Aguarda estabilização dos sinais DTR/RTS do conversor USB-Serial UART (150ms)
            await Task.Delay(150, cancellationToken);

            // 2. Envia CRLF para limpar buffers e acordar a console
            await _transport.WriteAsync(Text("\r\n"), cancellationToken);
            await Task.Delay(100, cancellationToken);

            var deadline = DateTime.UtcNow.Add(_options.ConnectTimeout);
            var maxAttempts = Math.Max(25, (int)(_options.ConnectTimeout.TotalSeconds / 1.2));
            var attempts = 0;

            while (attempts++ < maxAttempts && DateTime.UtcNow < deadline)
            {
                if (attempts > 1)
                {
                    // Envia Enter periódico para forçar redesenho de prompt
                    await _transport.WriteAsync(Text("\r\n"), cancellationToken);
                    await Task.Delay(200, cancellationToken);
                }

                (LoginStageKind Kind, string Tail, string Full) stage;
                try
                {
                    stage = await ReadUntilLoginOrPromptAsync(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (SessionTimeoutException)
                {
                    // Tenta novamente na próxima iteração do laço até atingir deadline
                    continue;
                }

                if (stage.Kind == LoginStageKind.Prompt)
                {
                    _connected = true;
                    return;
                }
                else if (stage.Kind == LoginStageKind.Username)
                {
                    if (_options.Username is null)
                        throw new LoginException("Dispositivo pediu usuário, mas nenhuma credencial foi fornecida.");
                    await _transport.WriteAsync(Text(_options.Username + "\r\n"), cancellationToken);
                }
                else if (stage.Kind == LoginStageKind.Password)
                {
                    if (_options.Password is null)
                        throw new LoginException("Dispositivo pediu senha, mas nenhuma credencial foi fornecida.");
                    await _transport.WriteAsync(Text(_options.Password + "\r\n"), cancellationToken);
                }
                else if (stage.Kind == LoginStageKind.InteractiveYesNo)
                {
                    await _transport.WriteAsync(Text("N\r\n"), cancellationToken);
                    await Task.Delay(400, cancellationToken);
                }
                else if (stage.Kind == LoginStageKind.InitialDialogNo)
                {
                    await _transport.WriteAsync(Text("no\r\n"), cancellationToken);
                    await Task.Delay(400, cancellationToken);
                }
                else if (stage.Kind == LoginStageKind.PressEnter)
                {
                    await _transport.WriteAsync(Text("\r\n"), cancellationToken);
                    await Task.Delay(400, cancellationToken);
                }
                else
                {
                    await _transport.WriteAsync(Text("\r\n"), cancellationToken);
                    await Task.Delay(300, cancellationToken);
                }
            }

            if (CurrentPrompt != null && (CurrentPrompt.EndsWith(">") || CurrentPrompt.EndsWith("#") || CurrentPrompt.StartsWith("<") || CurrentPrompt.StartsWith("[")))
            {
                _connected = true;
                return;
            }

            throw new LoginException("Não foi possível confirmar o prompt do dispositivo dentro do limite de tentativas.");
        }
        catch
        {
            await SafeCloseAsync();
            throw;
        }
    }

    public async Task ConnectRawAsync(CancellationToken cancellationToken = default)
    {
        await _transport.OpenAsync(cancellationToken);
        try
        {
            await _transport.WriteAsync(Text("\r"), cancellationToken);
            _connected = true;
        }
        catch
        {
            await SafeCloseAsync();
            throw;
        }
    }

    public async Task SendCtrlBAsync(CancellationToken cancellationToken = default)
    {
        await _transport.WriteAsync(new ReadOnlyMemory<byte>(new byte[] { 0x02 }), cancellationToken);
    }

    public async Task SendCtrlCAsync(CancellationToken cancellationToken = default)
    {
        await _transport.WriteAsync(new ReadOnlyMemory<byte>(new byte[] { 0x03 }), cancellationToken);
    }

    public async Task<string> SendCommandAsync(string command, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        await _transport.WriteAsync(Text(command + "\r"), cancellationToken);
        return await ReadUntilPromptAsync(timeout ?? _options.CommandTimeout, cancellationToken);
    }

    public async Task WriteLineAsync(string text, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        await _transport.WriteAsync(Text(text + "\r\n"), cancellationToken);
    }

    public async Task WriteRawAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        await _transport.WriteAsync(data, cancellationToken);
    }

    public async Task SendRawAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        await WriteRawAsync(data, cancellationToken);
    }

    public async Task SendRawAsync(string text, CancellationToken cancellationToken = default)
    {
        await WriteRawAsync(Text(text), cancellationToken);
    }

    public async Task<ExpectResult> SendExpectAsync(
        string command,
        IReadOnlyList<StopCondition> conditions,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        if (!string.IsNullOrEmpty(command))
            await _transport.WriteAsync(Text(command + "\r"), cancellationToken);
        return await WaitForAsync(conditions, timeout, cancellationToken);
    }

    public async Task<ExpectResult> WaitForAsync(
        IReadOnlyList<StopCondition> conditions,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        var effectiveTimeout = timeout ?? _options.CommandTimeout;
        var effectiveConditions = conditions == null || conditions.Count == 0
            ? new StopCondition[] { new StopCondition.Prompt() }
            : conditions;
        var deadline = DateTime.UtcNow.Add(effectiveTimeout);
        var output = new StringBuilder();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var read = await _transport.ReadAsync(_readBuffer, cancellationToken);
            if (read > 0)
            {
                AppendCleaned(output, read);
                await HandlePaginationAsync(output, cancellationToken);

                var current = output.ToString();
                var lines = current.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                // Varre as últimas linhas (até 6 linhas recentes) para não ser enganado por logs de syslog
                for (int i = lines.Length - 1; i >= Math.Max(0, lines.Length - 6); i--)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    foreach (var cond in effectiveConditions)
                    {
                        if (cond is StopCondition.LineRegex lr && lr.Regex.IsMatch(line))
                            return new ExpectResult(current, cond);
                        if (cond is StopCondition.Contains c && current.Contains(c.Text, c.Comparison))
                            return new ExpectResult(current, cond);
                        if (cond is StopCondition.Prompt)
                        {
                            if (_options.PromptMatcher.TryMatch(line) is { } pm)
                            {
                                CurrentPrompt = pm.Prompt;
                                Mode = pm.Mode;
                                return new ExpectResult(current, cond);
                            }

                            var ciscoMatch = Regex.Match(line, @"(?<prompt>[A-Za-z0-9_.+()/-]+?(?:\([A-Za-z0-9_.+()/-]+\))?[#>])\s*$");
                            if (ciscoMatch.Success && _options.PromptMatcher.TryMatch(ciscoMatch.Groups["prompt"].Value) is { } cpm)
                            {
                                CurrentPrompt = cpm.Prompt;
                                Mode = cpm.Mode;
                                return new ExpectResult(current, cond);
                            }

                            var pMatch = Regex.Match(line, @"(?<prompt>[<\[][A-Za-z0-9_\-\.]+[>\]])");
                            if (pMatch.Success && _options.PromptMatcher.TryMatch(pMatch.Groups["prompt"].Value) is { } pm2)
                            {
                                CurrentPrompt = pm2.Prompt;
                                Mode = pm2.Mode;
                                return new ExpectResult(current, cond);
                            }
                        }
                    }
                }
            }
            else if (DateTime.UtcNow >= deadline)
            {
                throw new SessionTimeoutException(
                    $"Tempo esgotado aguardando condições de parada. Última saída: {Truncate(output.ToString())}");
            }
            else
            {
                await Task.Delay(30, cancellationToken);
            }
        }
    }

    public async Task CloseAsync()
    {
        _connected = false;
        await _transport.CloseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
    }

    private async Task<string> ReadUntilPromptAsync(TimeSpan timeout, CancellationToken ct)
    {
        var output = new StringBuilder();
        var deadline = DateTime.UtcNow.Add(timeout);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var read = await _transport.ReadAsync(_readBuffer, ct);
            if (read > 0)
            {
                AppendCleaned(output, read);
                await HandlePaginationAsync(output, ct);

                var text = output.ToString();
                var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                for (int i = lines.Length - 1; i >= Math.Max(0, lines.Length - 4); i--)
                {
                    var line = lines[i].Trim();
                    if (_options.PromptMatcher.TryMatch(line) is { } match)
                    {
                        CurrentPrompt = match.Prompt;
                        Mode = match.Mode;
                        return text;
                    }

                    var ciscoMatch = Regex.Match(line, @"(?<prompt>[A-Za-z0-9_.+()/-]+?(?:\([A-Za-z0-9_.+()/-]+\))?[#>])\s*$");
                    if (ciscoMatch.Success && _options.PromptMatcher.TryMatch(ciscoMatch.Groups["prompt"].Value) is { } cmatch)
                    {
                        CurrentPrompt = cmatch.Prompt;
                        Mode = cmatch.Mode;
                        return text;
                    }

                    var promptMatch = Regex.Match(line, @"(?<prompt>[<\[][A-Za-z0-9_\-\.]+[>\]])");
                    if (promptMatch.Success && _options.PromptMatcher.TryMatch(promptMatch.Groups["prompt"].Value) is { } ematch)
                    {
                        CurrentPrompt = ematch.Prompt;
                        Mode = ematch.Mode;
                        return text;
                    }
                }
            }
            else if (DateTime.UtcNow >= deadline)
            {
                throw new SessionTimeoutException(
                    $"Tempo esgotado aguardando prompt do dispositivo. Última saída: {Truncate(output.ToString())}");
            }
            else
            {
                await Task.Delay(30, ct);
            }
        }
    }

    private async Task<(LoginStageKind Kind, string Tail, string Full)> ReadUntilLoginOrPromptAsync(TimeSpan timeout, CancellationToken ct)
    {
        var output = new StringBuilder();
        var deadline = DateTime.UtcNow.Add(timeout);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var read = await _transport.ReadAsync(_readBuffer, ct);
            if (read > 0)
            {
                AppendCleaned(output, read);
                await HandlePaginationAsync(output, ct);

                var text = output.ToString();
                // Varre todas as linhas recentes procurando prompt HPE/Cisco mesmo quando banner termina com Press ENTER
                var lines = text.Split('\n');
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    var l = lines[i].Trim();
                    if (string.IsNullOrEmpty(l)) continue;
                    if (_options.PromptMatcher.TryMatch(l) is { } pm2)
                    {
                        CurrentPrompt = pm2.Prompt;
                        Mode = pm2.Mode;
                        return (LoginStageKind.Prompt, Truncate(text), text);
                    }
                    // só avalia a última linha não-vazia para estados de login
                    break;
                }
                var kind = ClassifyLogin(LastNonEmptyLine(text));
                if (kind != LoginStageKind.Unknown)
                    return (kind, Truncate(text), text);
            }
            else if (DateTime.UtcNow >= deadline)
            {
                throw new SessionTimeoutException(
                    $"Tempo esgotado aguardando resposta do dispositivo. Última saída: {Truncate(output.ToString())}");
            }
            else
            {
                await Task.Delay(30, ct);
            }
        }
    }

    private LoginStageKind ClassifyLogin(string lastLine)
    {
        // Prompt tem prioridade: se a última linha é prompt (<HPE>, [HPE], <HPE-Gigabit...>, Cisco#/>), retorna imediatamente
        if (_options.PromptMatcher.TryMatch(lastLine) is { } pm)
        {
            CurrentPrompt = pm.Prompt;
            Mode = pm.Mode;
            return LoginStageKind.Prompt;
        }
        if (Regex.IsMatch(lastLine, @"(?i)^(user|username|user\s*name|login)\s*[:?]"))
            return LoginStageKind.Username;
        if (Regex.IsMatch(lastLine, @"(?i)^password\s*[:?]"))
            return LoginStageKind.Password;
        if (Regex.IsMatch(lastLine, @"(?i)(?:Before pressing ENTER you must choose|stop automatic configuration|auto-configuration|autoinstall|press ENTER to get started).*?\[Y/N\]"))
            return LoginStageKind.InteractiveYesNo;
        if (Regex.IsMatch(lastLine, @"(?i)initial configuration dialog\?\s*\[yes/no\]"))
            return LoginStageKind.InitialDialogNo;
        if (Regex.IsMatch(lastLine, @"(?i)to get started"))
            return LoginStageKind.PressEnter;
        if (Regex.IsMatch(lastLine, @"(?i)press\s+enter"))
            return LoginStageKind.PressEnter;
        if (Regex.IsMatch(lastLine, @"(?i)line\s+con0\s+is\s+available"))
            return LoginStageKind.PressEnter;
        if (Regex.IsMatch(lastLine, @"(?i)\[Y/N\]\s*[:?]?\s*$"))
            return LoginStageKind.InteractiveYesNo;
        return LoginStageKind.Unknown;
    }

    private async Task HandlePaginationAsync(StringBuilder output, CancellationToken ct)
    {
        while (TrimmedEndsWithMore(output))
        {
            StripSuffix(output, MoreMarker);
            while (output.Length > 0 && char.IsWhiteSpace(output[^1]))
                output.Length--;
            await _transport.WriteAsync(new ReadOnlyMemory<byte>(new byte[] { 0x20 }), ct);
        }
    }

    private static bool TrimmedEndsWithMore(StringBuilder sb)
    {
        var end = sb.Length;
        while (end > 0 && char.IsWhiteSpace(sb[end - 1]))
            end--;
        return end >= MoreMarker.Length
            && sb.ToString(end - MoreMarker.Length, MoreMarker.Length).Equals(MoreMarker, StringComparison.Ordinal);
    }

    private static void StripSuffix(StringBuilder sb, string suffix)
    {
        if (sb.Length >= suffix.Length)
            sb.Length -= suffix.Length;
    }

    private void AppendCleaned(StringBuilder output, int count)
    {
        var raw = Encoding.UTF8.GetString(_readBuffer, 0, count);
        output.Append(raw);
        output.Replace("\r", "");
        var text = output.ToString();
        if (AnsiEscape.IsMatch(text))
        {
            output.Clear();
            output.Append(AnsiEscape.Replace(text, ""));
        }
        RawOutput?.Invoke(raw);
    }

    private static string LastNonEmptyLine(string text)
    {
        var lines = text.Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length > 0)
                return line;
        }
        return string.Empty;
    }

    private static string Truncate(string text, int max = 240)
    {
        var flat = text.Replace("\r", "").Replace("\n", " ").Trim();
        return flat.Length <= max ? flat : "..." + flat[^max..];
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new DeviceSessionException("Sessão não está conectada.");
    }

    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);

    private async Task SafeCloseAsync()
    {
        try { await CloseAsync(); }
        catch { /* ignorar ao limpar após falha */ }
    }

    private enum LoginStageKind
    {
        Unknown = 0,
        Prompt,
        Username,
        Password,
        InteractiveYesNo,
        InitialDialogNo,
        PressEnter
    }

    public sealed record ExpectResult(string Output, StopCondition? Matched);
}
