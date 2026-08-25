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

            var chunk = Encoding.UTF8.GetString(_readBuffer, 0, bytesRead);
            chunk = chunk.Replace("\r", "");
            chunk = AnsiEscape.Replace(chunk, "");

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

            // Checa imediatamente o buffer pendente (prompts não terminam em \n)
            var pendingTail = lineBuffer.ToString().Trim();
            if (!string.IsNullOrEmpty(pendingTail))
            {
                foreach (var regex in _profile.RommonPatterns)
                {
                    if (regex.IsMatch(pendingTail))
                    {
                        EventReceived?.Invoke(new BootEvent(
                            BootEventType.RommonDetected,
                            pendingTail,
                            MatchedPattern: regex.ToString(),
                            Line: pendingTail));
                        return;
                    }
                }
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                // 1. Checa se casou com ROMMON
                foreach (var regex in _profile.RommonPatterns)
                {
                    if (regex.IsMatch(line))
                    {
                        EventReceived?.Invoke(new BootEvent(
                            BootEventType.RommonDetected,
                            line,
                            MatchedPattern: regex.ToString(),
                            Line: line));
                        return;
                    }
                }

                // 2. Checa se casou com Boot do SO (OS Boot)
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
