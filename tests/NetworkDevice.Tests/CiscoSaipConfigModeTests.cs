using NetworkDevice.Cisco;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Session;
using NetworkDevice.Tests.TestDoubles;

namespace NetworkDevice.Tests;

/// <summary>
/// Regressão do bug do 1905 no Android: com configuração existente ou console parado em
/// (config)# / (config-if)#, o provisionamento falhava ('% Invalid input' nos 'show' e
/// fallback para GE4/GE5 inexistentes). Com config zerada (prompt # limpo) aplicava ok.
/// </summary>
public class CiscoSaipConfigModeTests
{
    private static SaipCircuitData SampleCircuit() => new()
    {
        ClienteRazaoSocial = "CLIENTE TESTE LTDA",
        DesignacaoIp = "SP-TESTE-001",
        NumeroOts = "OTS123",
        WanIp = "10.200.0.2",
        WanSubnetMask = "255.255.255.252",
        WanGateway = "10.200.0.1",
        LanIp = "192.168.10.1",
        LanSubnetMask = "255.255.255.248",
        LanBlockNetwork = "192.168.10.0",
        WanCidr = 30,
        LanCidr = 29,
    };

    private const string Brief1905 =
        "Interface              IP-Address      OK? Method Status                Protocol\r\n" +
        "GigabitEthernet0/0     10.200.0.2      YES unset  up                    up\r\n" +
        "GigabitEthernet0/1     192.168.10.1    YES unset  down                  down\r\n";

    /// <summary>
    /// Fake de IOS 1905 com estado de modo (priv / config / config-if).
    /// O modo inicial precisa acompanhar o prompt inicial entregue ao ConnectAsync.
    /// </summary>
    private sealed class Ios1905Fake
    {
        public string Mode;

        public Ios1905Fake(string initialMode = "priv") => Mode = initialMode;

        public readonly List<string> Commands = new();

        public string Prompt => Mode switch
        {
            "config" => "Router(config)#\r\n",
            "if" => "Router(config-if)#\r\n",
            _ => "Router#\r\n",
        };

        public string Respond(string cmd)
        {
            Commands.Add(cmd);
            switch (cmd)
            {
                case "\x03":
                    // Ctrl+C não gera resposta com prompt (evita dessincronizar a fila do fake)
                    return "";
                case "configure terminal":
                case "config t":
                    Mode = "config";
                    return Prompt;
                case string s when s.StartsWith("interface ", StringComparison.OrdinalIgnoreCase):
                    Mode = "if";
                    return Prompt;
                case "end":
                    Mode = "priv";
                    return Prompt;
                case "exit":
                    Mode = Mode == "if" ? "config" : "priv";
                    return Prompt;
                case "enable":
                    Mode = "priv";
                    return Prompt;
                case "":
                    return Prompt;
                case string s when s.Contains("show ip interface brief"):
                    return Brief1905 + Prompt;
                case string s when s.Contains("show running-config"):
                    return "ip route 0.0.0.0 0.0.0.0 10.200.0.1\r\n" + Prompt;
                default:
                    return Prompt;
            }
        }
    }

    private static (ScriptedTransport, Ios1905Fake) BuildTransport(string initialPrompt, string initialMode = "priv")
    {
        var fake = new Ios1905Fake(initialMode);
        var transport = new ScriptedTransport(fake.Respond, initialOutput: initialPrompt);
        return (transport, fake);
    }

    /// <summary>
    /// Descarta ecos de wake-up pendentes do Connect (na serial real os bytes coalescem
    /// na mesma leitura; no fake cada resposta chega em um Read isolado).
    /// </summary>
    private static async Task<DeviceSession> ConnectDrainedAsync(ScriptedTransport transport)
    {
        var session = new DeviceSession(transport, new SessionOptions());
        await session.ConnectAsync();
        var buf = new byte[4096];
        while (await transport.ReadAsync(buf) > 0) { }
        return session;
    }

    [Fact]
    public async Task ApplyConfig_FromGlobalConfigMode_NormalizesToPrivilegedBeforeShow()
    {
        var (transport, fake) = BuildTransport("Router(config)#\r\n", "config");

        await using var session = await ConnectDrainedAsync(transport);
        Assert.Equal(ExecMode.GlobalConfig, session.Mode);

        var cfg = new CiscoSaipConfigurator(_ => Task.CompletedTask);
        await cfg.ApplyConfigAsync(session, SampleCircuit(), "GigabitEthernet 0/0", "GigabitEthernet 0/1");

        var cmds = fake.Commands;
        var firstShow = cmds.FindIndex(c => c.Contains("show ip interface brief"));
        var firstEnd = cmds.FindIndex(c => c == "end");
        Assert.True(firstEnd >= 0, "Deveria enviar 'end' para sair do (config)#.");
        Assert.True(firstShow >= 0, "Deveria executar 'show ip interface brief'.");
        Assert.True(firstEnd < firstShow, "'end' deve vir antes do primeiro 'show'.");

        // Interfaces do 1905 aplicadas, nunca o fallback GE4/GE5
        Assert.Contains(cmds, c => c == "interface GigabitEthernet 0/0");
        Assert.Contains(cmds, c => c == "interface GigabitEthernet 0/1");
        Assert.DoesNotContain(cmds, c => c.Contains("GigabitEthernet 4") || c.Contains("GigabitEthernet 5"));
    }

    [Fact]
    public async Task ApplyConfig_FromSubInterfaceMode_ExitsToPrivilegedAndCompletes()
    {
        var (transport, fake) = BuildTransport("Router(config-if)#\r\n", "if");

        await using var session = await ConnectDrainedAsync(transport);
        Assert.Equal(ExecMode.ConfigSubmode, session.Mode);

        var cfg = new CiscoSaipConfigurator(_ => Task.CompletedTask);
        await cfg.ApplyConfigAsync(session, SampleCircuit(), "GigabitEthernet 0/0", "GigabitEthernet 0/1");

        Assert.Contains(fake.Commands, c => c == "end");
        Assert.Contains(fake.Commands, c => c == "write memory");
        Assert.Equal(ExecMode.PrivilegedExec, session.Mode);
    }

    [Fact]
    public async Task SendShowAsync_InConfigMode_PrefixesDo()
    {
        var (transport, fake) = BuildTransport("Router(config)#\r\n", "config");

        await using var session = await ConnectDrainedAsync(transport);

        var output = await CiscoSaipConfigurator.SendShowAsync(session, "show ip interface brief");

        Assert.Contains(fake.Commands, c => c == "do show ip interface brief");
        Assert.Contains("GigabitEthernet0/0", output);
    }

    [Fact]
    public async Task SendShowAsync_InPrivilegedMode_SendsPlainShow()
    {
        var (transport, fake) = BuildTransport("Router#\r\n");

        await using var session = await ConnectDrainedAsync(transport);

        await CiscoSaipConfigurator.SendShowAsync(session, "show ip interface brief");

        Assert.Contains(fake.Commands, c => c == "show ip interface brief");
        Assert.DoesNotContain(fake.Commands, c => c.StartsWith("do "));
    }

    [Theory]
    [InlineData("GigabitEthernet 0/0", "GigabitEthernet 0/1", "c1900-universalk9-mz.SPA.157-3.M7.bin", true)]
    [InlineData("GigabitEthernet 0/0", "GigabitEthernet 0/1", "c2900-universalk9-mz.SPA.157-3.M7.bin", false)]
    [InlineData("GigabitEthernet0/4", "GigabitEthernet0/5", "c800m-universalk9-mz.SPA.159-3.M10.bin", false)]
    [InlineData("GigabitEthernet 4", "GigabitEthernet 5", "c900-universalk9-mz.SPA.159-3.M9.bin", false)]
    public void GenerateCommands_GaranteBootImageCustomizadaParaTodosOsModelos(
        string wan, string lan, string customBoot, bool expectUsbflash0)
    {
        var cmds = CiscoSaipConfigurator.GenerateCommands(SampleCircuit(), wan, lan, bootImage: customBoot);

        Assert.Contains(cmds, c => c == $"boot system flash:{customBoot}");
        if (expectUsbflash0)
        {
            Assert.Contains(cmds, c => c == $"boot system usbflash0:{customBoot}");
        }
        else
        {
            Assert.DoesNotContain(cmds, c => c.StartsWith("boot system usbflash0:"));
        }
    }

    [Fact]
    public void GenerateCommands_GaranteAclBloqueioTelnetComRedeCalculadaEHostLan()
    {
        var circuit = new SaipCircuitData
        {
            ClienteRazaoSocial = "CLIENTE TESTE LTDA",
            DesignacaoIp = "SP-TESTE-001",
            NumeroOts = "OTS123",
            WanIp = "10.200.0.2",
            WanSubnetMask = "255.255.255.252",
            WanGateway = "10.200.0.1",
            WanCidr = 30,
            LanIp = "200.250.163.41",
            LanSubnetMask = "255.255.255.248",
            LanCidr = 29,
            LanBlockNetwork = null // Simula quando Ficha SAIP não traz o bloco pré-calculado
        };

        var cmds = CiscoSaipConfigurator.GenerateCommands(circuit, "GigabitEthernet 0/0", "GigabitEthernet 0/1");

        // Deve calcular o endereço base da rede 200.250.163.40 / 0.0.0.7 e liberar o host do roteador
        Assert.Contains(cmds, c => c.Contains("permit ip 200.250.163.40 0.0.0.7 any"));
        Assert.Contains(cmds, c => c.Contains("permit ip host 200.250.163.41 any"));
    }

    [Fact]
    public async Task RemoverDhcpTemporarioAsync_EnviaComandosCorretosEGravaNvram()
    {
        var (transport, fake) = BuildTransport("Router#\r\n");
        await using var session = await ConnectDrainedAsync(transport);

        await CiscoSaipConfigurator.RemoverDhcpTemporarioAsync(session, SampleCircuit());

        Assert.Contains(fake.Commands, c => c == "no ip dhcp pool SPARC_LAN");
        Assert.Contains(fake.Commands, c => c == "no ip dhcp excluded-address 192.168.10.1");
        Assert.Contains(fake.Commands, c => c == "write memory");
    }

    [Theory]
    [InlineData(SaipServiceType.IpVpn)]
    [InlineData(SaipServiceType.VozDigitalE1)]
    [InlineData(SaipServiceType.VozSipIp)]
    public void GenerateCommands_ParaMplsEVoz_UsaConfiguracaoBasicaComAcessoTelnetLocalSemBld(SaipServiceType tipoServico)
    {
        var circuit = new SaipCircuitData
        {
            TipoServico = tipoServico,
            DesignacaoIp = "CIRCUITO-TESTE-01",
            WanIp = "10.10.10.2",
            WanSubnetMask = "255.255.255.252",
            WanGateway = "10.10.10.1",
            WanCidr = 30,
            LanIp = "192.168.1.1",
            LanSubnetMask = "255.255.255.0",
            LanCidr = 24
        };

        var cmds = CiscoSaipConfigurator.GenerateCommands(circuit, "GigabitEthernet 0/0", "GigabitEthernet 0/1");

        // 1. Sobe o link: WAN, LAN e Rota
        Assert.Contains(cmds, c => c == "interface GigabitEthernet 0/0");
        Assert.Contains(cmds, c => c == " ip address 10.10.10.2 255.255.255.252");
        Assert.Contains(cmds, c => c == "interface GigabitEthernet 0/1");
        Assert.Contains(cmds, c => c == " ip address 192.168.1.1 255.255.255.0");
        Assert.Contains(cmds, c => c == "ip route 0.0.0.0 0.0.0.0 10.10.10.1");

        // 2. Disponibiliza acesso remoto via Telnet (usuário privilege 15, login local, sem ACL de bloqueio)
        Assert.Contains(cmds, c => c == "username EBT privilege 15 secret CQMR");
        Assert.Contains(cmds, c => c == "enable secret PRO1AN");
        Assert.Contains(cmds, c => c == " transport input telnet ssh");
        Assert.Contains(cmds, c => c == " login local");
        Assert.Contains(cmds, c => c == "write memory");

        // 3. Omite componentes pesados específicos do BLD (TACACS, QoS Shaping, NTP, SNMP, ACLs restritivas)
        Assert.DoesNotContain(cmds, c => c.StartsWith("aaa new-model"));
        Assert.DoesNotContain(cmds, c => c.Contains("TACACS-SERVER-CLARO"));
        Assert.DoesNotContain(cmds, c => c.Contains("policy-map SHAPE_OUT"));
        Assert.DoesNotContain(cmds, c => c.Contains("service-policy output SHAPE_OUT"));
        Assert.DoesNotContain(cmds, c => c.Contains("BLOQUEIO_TELNET"));
        Assert.DoesNotContain(cmds, c => c.StartsWith("ntp server"));
        Assert.DoesNotContain(cmds, c => c.StartsWith("snmp-server"));
    }

    [Fact]
    public void GenerateCommands_ParaBLD_MantemTemplateCompletoProducaoClaro()
    {
        var circuit = new SaipCircuitData
        {
            TipoServico = SaipServiceType.BusinessLinkDirect,
            DesignacaoIp = "BLD-PRODUCAO-01",
            WanIp = "200.200.10.2",
            WanSubnetMask = "255.255.255.252",
            WanGateway = "200.200.10.1",
            WanCidr = 30,
            LanIp = "201.201.10.1",
            LanSubnetMask = "255.255.255.248",
            LanCidr = 29
        };

        var cmds = CiscoSaipConfigurator.GenerateCommands(circuit, "GigabitEthernet 0/0", "GigabitEthernet 0/1");

        // BLD completo deve conter TACACS+, QoS Shaping, NTP, SNMP e ACL BLOQUEIO_TELNET
        Assert.Contains(cmds, c => c == "aaa new-model");
        Assert.Contains(cmds, c => c.Contains("TACACS-SERVER-CLARO"));
        Assert.Contains(cmds, c => c.Contains("policy-map SHAPE_OUT"));
        Assert.Contains(cmds, c => c.Contains("service-policy output SHAPE_OUT"));
        Assert.Contains(cmds, c => c.Contains("BLOQUEIO_TELNET"));
        Assert.Contains(cmds, c => c.StartsWith("ntp server"));
        Assert.Contains(cmds, c => c.StartsWith("snmp-server"));
        Assert.Contains(cmds, c => c.Contains("access-class BLOQUEIO_TELNET in"));
    }

    [Fact]
    public void GenerateCommands_Cisco841And921_IncludesNoDescriptionOnUnusedSwitchportsAndVlan()
    {
        var circuit = SampleCircuit();

        // 921
        var cmds921 = CiscoSaipConfigurator.GenerateCommands(circuit, "GigabitEthernet 4", "GigabitEthernet 5");
        Assert.Contains("interface GigabitEthernet0", cmds921);
        Assert.Contains("interface Vlan1", cmds921);
        Assert.Contains(" no description", cmds921);

        // 841
        var cmds841 = CiscoSaipConfigurator.GenerateCommands(circuit, "GigabitEthernet0/4", "GigabitEthernet0/5");
        Assert.Contains("interface GigabitEthernet0/0", cmds841);
        Assert.Contains("interface Vlan1", cmds841);
        Assert.Contains(" no description", cmds841);
    }
}

