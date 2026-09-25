using System.Text;
using System.Text.RegularExpressions;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Core.Recovery;

public sealed class BootMonitor
{
    private static readonly Regex AnsiEscape = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);

    private readonly ITransport _transport;
    private readonly BootInterruptProfile _profile;
    private readonly StringBuilder _capturedOutput = new();
    private readonly byte[] _readBuffer = new byte[2048];

    public BootMonitor(ITransport transport, BootInterruptProfile profile)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public event Action<BootEvent>? EventReceived;

    public string CapturedOutput
    {
        get
        {
            lock (_capturedOutput)
            {
                return _capturedOutput.ToString();
            }
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var lineBuffer = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            int bytesRead;
            try
            {
                bytesRead = await _transport.ReadAsync(_readBuffer, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (bytesRead <= 0)
            {
                await Task.Delay(20, cancellationToken);
                continue;
            }

            // Descarta bytes 0x00 (erros de enquadramento / ruído de Break de chips USB como CH340)
            var validBytes = 0;
            for (var i = 0; i < bytesRead; i++)
            {
                if (_readBuffer[i] != 0x00)
                {
                    _readBuffer[validBytes++] = _readBuffer[i];
                }
            }
            if (validBytes == 0)
                continue;

            var chunk = Encoding.UTF8.GetString(_readBuffer, 0, validBytes);
            chunk = chunk.Replace("\r", "").Replace("\0", "").Replace("\uFFFD", "");
            chunk = AnsiEscape.Replace(chunk, "");
            if (string.IsNullOrEmpty(chunk))
                continue;

            lock (_capturedOutput)
            {
                _capturedOutput.Append(chunk);
                if (_capturedOutput.Length > 30_000)
                {
                    var removeCount = _capturedOutput.Length - 20_000;
                    _capturedOutput.Remove(0, removeCount);
                }
            }

            EventReceived?.Invoke(new BootEvent(BootEventType.Output, chunk));

            // Processamento linha a linha e do buffer acumulado
            lineBuffer.Append(chunk);
            var content = lineBuffer.ToString();
            var lines = content.Split('\n');

            // Mantém a última linha incompleta no buffer
            lineBuffer.Clear();
            if (lines.Length > 0 && !content.EndsWith('\n'))
            {
                lineBuffer.Append(lines[^1]);
            }

            // Checa se casou com ROMMON no pendingTail, nas linhas ou no buffer acumulado recente
            var isRommon = false;
            string? matchedRommonText = null;

            var pendingTail = lineBuffer.ToString().Trim();
            if (!string.IsNullOrEmpty(pendingTail))
            {
                foreach (var regex in _profile.RommonPatterns)
                {
                    if (regex.IsMatch(pendingTail))
                    {
                        isRommon = true;
                        matchedRommonText = pendingTail;
                        break;
                    }
                }
            }

            if (!isRommon)
            {
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line))
                        continue;

                    foreach (var regex in _profile.RommonPatterns)
                    {
                        if (regex.IsMatch(line))
                        {
                            isRommon = true;
                            matchedRommonText = line;
                            break;
                        }
                    }
                    if (isRommon) break;
                }
            }

            if (!isRommon)
            {
                // Fallback: busca nos últimos 500 caracteres do buffer acumulado
                string recent;
                lock (_capturedOutput)
                {
                    var len = Math.Min(500, _capturedOutput.Length);
                    recent = _capturedOutput.ToString(_capturedOutput.Length - len, len);
                }
                foreach (var regex in _profile.RommonPatterns)
                {
                    var match = regex.Match(recent);
                    if (match.Success)
                    {
                        isRommon = true;
                        matchedRommonText = match.Value.Trim();
                        break;
                    }
                }
            }

            if (isRommon && !string.IsNullOrEmpty(matchedRommonText))
            {
                EventReceived?.Invoke(new BootEvent(
                    BootEventType.RommonDetected,
                    matchedRommonText,
                    MatchedPattern: "rommon",
                    Line: matchedRommonText));
                return;
            }

            // 2. Checa se casou com Boot do SO (OS Boot)
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                foreach (var regex in _profile.OsBootPatterns)
                {
                    if (regex.IsMatch(line))
                    {
                        EventReceived?.Invoke(new BootEvent(
                            BootEventType.OsBootDetected,
                            line,
                            MatchedPattern: regex.ToString(),
                            Line: line));
                        // Continua escutando conforme política
                    }
                }
            }
        }
    }
}
