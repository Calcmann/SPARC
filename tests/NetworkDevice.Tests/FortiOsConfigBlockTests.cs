using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Session;
using NetworkDevice.Fortinet;
using NetworkDevice.Tests.TestDoubles;

namespace NetworkDevice.Tests;

/// <summary>
/// Regressão análoga ao bug do Cisco 1905 em (config)#: console FortiOS parado dentro
/// de um bloco ('FGT40F (interface) #' etc. após provisionamento interrompido) fazia
/// 'get system interface' retornar 'Command fail' e os blocos 'config ...' aninharem
/// com erro. O ApplyConfigAsync agora normaliza para o shell raiz com 'end' antes de tudo.
/// </summary>
public sealed class FortiOsConfigBlockTests
{
    private static SaipCircuitData SampleCircuit() => new()
    {
        ClienteRazaoSocial = "CLIENTE TESTE LTDA",
        DesignacaoIp = "FNS-IP-TESTE-001",
        NumeroOts = "OTS123",
        WanIp = "201.30.10.18",
        WanSubnetMask = "255.255.255.252",
        WanGateway = "201.30.10.17",
        LanIp = "10.20.30.1",
        LanSubnetMask = "255.255.255.248",
        LanBlockNetwork = "10.20.30.0",
        WanCidr = 30,
        LanCidr = 29,
    };

    private const string GetSystemInterface =
        "config system interface\n" +
        "    edit \"wan\"\n" +
        "        set ip 201.30.10.18 255.255.255.252\n" +
        "    next\n" +
        "    edit \"lan\"\n" +
        "        set ip 10.20.30.1 255.255.255.248\n" +
        "    next\n" +
        "end\n";

    /// <summary>
    /// Fake de FortiOS com estado de bloco de configuração.
    /// </summary>
    private sealed class FortiFake
    {
        public string Mode;

        public FortiFake(string initialMode = "root") => Mode = initialMode;

        public readonly List<string> Commands = new();

        public string Prompt => Mode switch
        {
            "interface" => "FGT40F (interface) #\r\n",
            "admin" => "FGT40F (admin) #\r\n",
            "static" => "FGT40F (static) #\r\n",
            "policy" => "FGT40F (policy) #\r\n",
            "dhcp" => "FGT40F (dhcp server) #\r\n",
            _ => "FGT40F #\r\n",
        };

        public string Respond(string cmd)
        {
            Commands.Add(cmd);
            if (cmd == "\x03")
                return "";
            if (cmd == "end")
            {
                Mode = "root";
                return Prompt;
            }
            if (cmd.StartsWith("config system interface", StringComparison.OrdinalIgnoreCase)) { Mode = "interface"; return Prompt; }
            if (cmd.StartsWith("config system admin", StringComparison.OrdinalIgnoreCase)) { Mode = "admin"; return Prompt; }
            if (cmd.StartsWith("config router static", StringComparison.OrdinalIgnoreCase)) { Mode = "static"; return Prompt; }
            if (cmd.StartsWith("config firewall policy", StringComparison.OrdinalIgnoreCase)) { Mode = "policy"; return Prompt; }
            if (cmd.StartsWith("config system dhcp server", StringComparison.OrdinalIgnoreCase)) { Mode = "dhcp"; return Prompt; }
            if (cmd.StartsWith("config ", StringComparison.OrdinalIgnoreCase)) { Mode = "root"; return Prompt; }
            if (cmd.StartsWith("get system interface", StringComparison.OrdinalIgnoreCase))
                return Mode == "root" ? GetSystemInterface + Prompt : "Command fail. Unknown action.\r\n" + Prompt;
            if (cmd.StartsWith("show router static", StringComparison.OrdinalIgnoreCase))
                return "config router static\n    edit 1\n    next\nend\n" + Prompt;
            if (cmd.StartsWith("show ", StringComparison.OrdinalIgnoreCase) || cmd.StartsWith("get ", StringComparison.OrdinalIgnoreCase))
                return "config\nend\n" + Prompt;
            return Prompt;
        }
    }

    private static (ScriptedTransport, FortiFake) BuildTransport(string initialPrompt, string initialMode)
    {
        var fake = new FortiFake(initialMode);
        return (new ScriptedTransport(fake.Respond, initialOutput: initialPrompt), fake);
    }

    private static async Task<DeviceSession> ConnectDrainedAsync(ScriptedTransport transport)
    {
        var session = new DeviceSession(transport, new SessionOptions());
        await session.ConnectAsync();
        var buf = new byte[4096];
        while (await transport.ReadAsync(buf) > 0) { }
        return session;
    }

    [Fact]
    public async Task EnsureRootShellAsync_FromInterfaceBlock_SendsEndUntilRoot()
    {
        var (transport, fake) = BuildTransport("FGT40F (interface) #\r\n", "interface");

        await using var session = await ConnectDrainedAsync(transport);

        await FortiOsSaipConfigurator.EnsureRootShellAsync(session);

        Assert.Contains(fake.Commands, c => c == "end");
        Assert.Equal("FGT40F #", (session.CurrentPrompt ?? string.Empty).Trim());
    }

    [Fact]
    public async Task ApplyConfigAsync_FromStuckConfigBlock_NormalizesBeforeGetAndCompletes()
    {
        var (transport, fake) = BuildTransport("FGT40F (interface) #\r\n", "interface");

        await using var session = await ConnectDrainedAsync(transport);

        var cfg = new FortiOsSaipConfigurator(_ => Task.CompletedTask);
        await cfg.ApplyConfigAsync(session, SampleCircuit(), "wan", "lan");

        var cmds = fake.Commands;
        var firstEnd = cmds.FindIndex(c => c == "end");
        var firstGet = cmds.FindIndex(c => c.StartsWith("get system interface", StringComparison.OrdinalIgnoreCase));
        Assert.True(firstEnd >= 0, "Deveria enviar 'end' para sair do bloco (interface).");
        Assert.True(firstGet >= 0, "Deveria executar 'get system interface'.");
        Assert.True(firstEnd < firstGet, "'end' deve vir antes do primeiro 'get'.");

        // Bloco principal aplicado e nenhuma falha de aninhamento no mapeamento
        Assert.Contains(cmds, c => c == "config system interface");
        Assert.DoesNotContain(cmds, c => c == "Command fail. Unknown action.");
    }
}
