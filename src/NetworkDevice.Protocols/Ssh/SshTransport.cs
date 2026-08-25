using NetworkDevice.Core.Session;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace NetworkDevice.Protocols.Ssh;

public sealed class SshTransport : ITransport
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string _password;
    private readonly SshHostKeyPolicy _hostKeyPolicy;

    private SshClient? _client;
    private Renci.SshNet.ShellStream? _shell;

    public SshTransport(string host, int port, string username, string password, SshHostKeyPolicy hostKeyPolicy)
    {
        _host = host;
        _port = port;
        _username = username;
        _password = password;
        _hostKeyPolicy = hostKeyPolicy;
    }

    public bool IsOpen => _client?.IsConnected == true && _shell is not null;

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var client = new SshClient(
            new ConnectionInfo(_host, _port, _username, new PasswordAuthenticationMethod(_username, _password)));
        client.HostKeyReceived += OnHostKeyReceived;
        client.Connect();

        _client = client;
        _shell = client.CreateShellStream("dumb", 0, 0, 0, 0, 2048);
        return Task.CompletedTask;
    }

    public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_shell is null || _client is null || !_client.IsConnected)
            throw new DeviceSessionException("Sessão SSH não conectada.");

        if (!_shell.DataAvailable)
        {
            await Task.Delay(40, cancellationToken);
            return 0;
        }

        return await _shell.ReadAsync(buffer, cancellationToken);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_shell is null || _client is null || !_client.IsConnected)
            throw new DeviceSessionException("Sessão SSH não conectada.");
        return _shell.WriteAsync(buffer, cancellationToken);
    }

    public Task SendBreakAsync(CancellationToken cancellationToken = default) =>
        throw new DeviceSessionException("Break não é suportado em conexão SSH.");

    public Task CloseAsync()
    {
        try { _shell?.Close(); }
        catch { /* já fechada */ }
        try { _client?.Disconnect(); }
        catch { /* já desconectado */ }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        try { _shell?.Dispose(); }
        catch { /* já liberado */ }
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnHostKeyReceived(object? sender, HostKeyEventArgs e)
    {
        var fingerprint = Convert.ToHexString(e.FingerPrint).ToLowerInvariant();
        e.CanTrust = _hostKeyPolicy.AcceptUnknownHosts
                      || _hostKeyPolicy.TrustedSha256Fingerprints.Contains(fingerprint);
    }
}
