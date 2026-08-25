using NetworkDevice.Core.Provisioning;
using Xunit;

namespace NetworkDevice.Tests;

public sealed class HpeSaipConfiguratorTests
{
    [Fact]
    public void GenerateCommands_ProducesValidHpeComwareConfig()
    {
        var circuit = new SaipCircuitData
        {
            ClienteRazaoSocial = "HORIZONTE RESTAURANTES LTDA",
            DesignacaoIp = "FNS/IP/04045",
            NumeroOts = "IM-SPO-IGC--IP-44607/2026",
            WanIp = "201.30.10.18",
            WanSubnetMask = "255.255.255.252",
            WanGateway = "201.30.10.17",
            LanIp = "189.16.20.81",
            LanSubnetMask = "255.255.255.248"
        };

        var cmds = HpeSaipConfigurator.GenerateCommands(circuit, "GigabitEthernet0/0", "GigabitEthernet0/1");

        Assert.Contains("system-view", cmds);
        Assert.Contains("interface GigabitEthernet0/0", cmds);
        Assert.Contains("port link-mode route", cmds);
        Assert.Contains("ip address 201.30.10.18 255.255.255.252", cmds);
        Assert.Contains("interface GigabitEthernet0/1", cmds);
        Assert.Contains("ip address 189.16.20.81 255.255.255.248", cmds);
        Assert.Contains("ip route-static 0.0.0.0 0.0.0.0 201.30.10.17", cmds);
        Assert.Contains("local-user EBT class manage", cmds);
        Assert.Contains("password simple PRO1AN", cmds);
        Assert.Contains("telnet server enable", cmds);
        Assert.Contains("line con 0", cmds);
        Assert.Contains("line vty 0 63", cmds);
        Assert.Contains("save force", cmds);
    }
}
