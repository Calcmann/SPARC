using System.Text;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Cli;

internal sealed class MockCiscoDevice
{
    public static ITransport CreateTransport()
    {
        var pending = new Queue<string>();
        pending.Enqueue("SW-DEPTO-01>\r\n");
        return new ScriptedTransport(Respond, pending);
    }

    private static string Respond(string command) =>
        command switch
        {
            "enable" => "Password:\r\n",
            "admin123" => "SW-DEPTO-01#\r\n",
            "terminal length 0" => "SW-DEPTO-01#\r\n",
            "terminal width 0" => "SW-DEPTO-01#\r\n",
            "show version" => ShowVersion,
            "show running-config" => RunningConfig,
            "write memory" => "Building configuration...\r\n[OK]\r\nSW-DEPTO-01#\r\n",
            _ => "\r\n"
        };

    private const string ShowVersion =
        """
        Cisco IOS Software, C2960X Software (C2960X-UNIVERSALK9-M), Version 15.2(7)E6, RELEASE SOFTWARE (fc3)
        Technical Support: http://www.cisco.com/techsupport
        Copyright (c) 1986-2020 by Cisco Systems, Inc.

        ROM: Bootstrap program is C2960X boot loader
        BOOTLDR: C2960X Boot Loader (C2960X-HBOOT-M) Version 15.2(2r)E, RELEASE SOFTWARE (fc1)

        SW-DEPTO-01 uptime is 2 weeks, 4 days, 6 hours, 12 minutes
        System image file is "flash:/c2960x-universalk9-mz.152-7.E6.bin"

        cisco WS-C2960X-48FPS-L (APM86XXX) processor with 512000K bytes of memory.

        Cisco WS-C2960X-48FPS-L (revision E0) with 504219K bytes of memory.
        Processor board ID FOC1234ABCD

        1 Virtual Ethernet interface
        52 Gigabit Ethernet interfaces
        4 Ten Gigabit Ethernet interfaces
        48 VLAN interfaces

        Configuration register is 0xF

        System model number            : WS-C2960X-48FPS-L
        System serial number           : FOC1234ABCD
        System software version        : 15.2(7)E6

        SW-DEPTO-01#
        """;

    private const string RunningConfig =
        """
        show running-config
        Building configuration...

        Current configuration : 1876 bytes
        !
        version 15.2
        no service pad
        service timestamps debug datetime msec
        service timestamps log datetime msec
        no service password-encryption
        !
        hostname SW-DEPTO-01
        !
        enable secret 5 $1$abc$defg
        !
        vlan 10
         name ADMIN
        vlan 20
         name USERS
        vlan 99
         name MANAGEMENT
        !
        interface GigabitEthernet0/1
         switchport mode access
         switchport access vlan 10
        !
        interface Vlan99
         ip address 192.168.10.20 255.255.255.0
         no shutdown
        !
        ip default-gateway 192.168.10.1
        ip http server
        !
        line con 0
        line vty 0 4
         transport input ssh
        !
        end

        SW-DEPTO-01#
        """;
}

internal sealed class ScriptedTransport : ITransport
{
    private readonly Func<string, string> _responder;
    private readonly Queue<string> _pending;
    private string? _remainder;

    public ScriptedTransport(Func<string, string> responder, Queue<string> pending)
    {
        _responder = responder;
        _pending = pending;
    }

    public IReadOnlyList<string> Commands { get; } = new List<string>();

    public bool IsOpen => true;

    public Task OpenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendBreakAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_remainder) && _pending.Count == 0)
            return Task.FromResult(0);

        if (string.IsNullOrEmpty(_remainder))
            _remainder = _pending.Dequeue();

        var bytes = Encoding.UTF8.GetBytes(_remainder);
        if (bytes.Length <= buffer.Length)
        {
            var chunk = _remainder;
            _remainder = null;
            Encoding.UTF8.GetBytes(chunk).CopyTo(buffer);
            return Task.FromResult(chunk.Length);
        }

        var head = bytes.AsSpan(0, buffer.Length).ToArray();
        head.CopyTo(buffer);
        _remainder = Encoding.UTF8.GetString(bytes.AsSpan(buffer.Length));
        return Task.FromResult(head.Length);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var text = Encoding.UTF8.GetString(buffer.Span);
        var command = text.TrimEnd('\r', '\n');
        if (command.Length > 0)
        {
            ((List<string>)Commands).Add(command);
            var response = _responder(command);
            if (!string.IsNullOrEmpty(response))
                _pending.Enqueue(response);
        }
        return ValueTask.CompletedTask;
    }

    public Task CloseAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
