using NetworkDevice.Core.Detection;
using Xunit;

namespace NetworkDevice.Tests;

public sealed class WanPortInspectorTests
{
    [Theory]
    [InlineData("cisco.c841.break", false, "GE0/4 (Porta 4)")]
    [InlineData("cisco.c900.ctrl-c", false, "GE4 (Porta 4)")]
    [InlineData("cisco.c1900.break", false, "GE0/0")]
    [InlineData("cisco.c2900.break", false, "GE0/0")]
    [InlineData("hpe.msr954.ctrl-b", true, "GE0/0")]
    [InlineData("hpe.msr930.ctrl-b", true, "GE0/0")]
    [InlineData("hpe.msr1002.ctrl-b", true, "GE0/0")]
    public void PorTagModelo_MapeiaTodos(string tag, bool isHpe, string iface)
    {
        var info = WanPortInspector.PorTagModelo(tag);
        Assert.NotNull(info);
        Assert.Equal(isHpe, info.IsHpe);
        Assert.Equal(iface, info.InterfaceExibicao);
        Assert.NotEmpty(info.NomesBusca);
    }

    [Theory]
    [InlineData(NetworkDevice.Core.Domain.DeviceSeries.Isr841, "GE0/4 (Porta 4)", false)]
    [InlineData(NetworkDevice.Core.Domain.DeviceSeries.Isr921, "GE4 (Porta 4)", false)]
    [InlineData(NetworkDevice.Core.Domain.DeviceSeries.Series1900, "GE0/0", false)]
    [InlineData(NetworkDevice.Core.Domain.DeviceSeries.Series2900, "GE0/0", false)]
    [InlineData(NetworkDevice.Core.Domain.DeviceSeries.Msr930, "GE0/0", true)]
    [InlineData(NetworkDevice.Core.Domain.DeviceSeries.Msr954, "GE0/0", true)]
    [InlineData(NetworkDevice.Core.Domain.DeviceSeries.Msr1002, "GE0/0", true)]
    [InlineData(NetworkDevice.Core.Domain.DeviceSeries.FortiGate40F, "WAN (Porta WAN)", false)]
    public void ObterPorSerie_MapeiaCorretamente(NetworkDevice.Core.Domain.DeviceSeries series, string iface, bool isHpe)
    {
        var info = WanPortInspector.ObterPorSerie(series);
        Assert.NotNull(info);
        Assert.Equal(iface, info.InterfaceExibicao);
        Assert.Equal(isHpe, info.IsHpe);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("desconhecido")]
    public void PorTagModelo_Desconhecido_RetornaNull(string? tag)
    {
        Assert.Null(WanPortInspector.PorTagModelo(tag));
    }

    [Fact]
    public void Cisco_DetectaDown()
    {
        const string brief = "Interface                  IP-Address      OK? Method Status                Protocol\n" +
            "GigabitEthernet0/4         192.168.10.2    YES manual up                    up\n" +
            "GigabitEthernet0/5         10.10.10.1      YES manual up                    down\n";
        var names = new[] { "gigabitethernet0/4" };
        var (enc, down) = WanPortInspector.CiscoWanStatus(brief, names);
        Assert.True(enc);
        Assert.False(down);
        var (enc2, down2) = WanPortInspector.CiscoWanStatus(brief, new[] { "gigabitethernet0/5" });
        Assert.True(enc2);
        Assert.True(down2);
    }

    [Fact]
    public void Cisco_AdminDown_EAusente()
    {
        const string brief = "GigabitEthernet0/4         unassigned      YES unset  administratively down down\n";
        var (enc, down) = WanPortInspector.CiscoWanStatus(brief, new[] { "gigabitethernet0/4" });
        Assert.True(enc);
        Assert.True(down);
        var (enc2, down2) = WanPortInspector.CiscoWanStatus(brief, new[] { "gigabitethernet0/0" });
        Assert.False(enc2);
        Assert.False(down2);
    }

    [Fact]
    public void Hpe_DetectaDown()
    {
        const string brief = "Interface            Link Protocol Main IP\n" +
            "GE0/0                DOWN DOWN    --\n" +
            "GE0/1                UP   UP      10.10.10.1\n";
        var (enc, down) = WanPortInspector.HpeWanStatus(brief, new[] { "ge0/0" });
        Assert.True(enc);
        Assert.True(down);
        var (enc2, down2) = WanPortInspector.HpeWanStatus(brief, new[] { "ge0/1" });
        Assert.True(enc2);
        Assert.False(down2);
    }

    [Fact]
    public void Forti_DetectaDown_Physical()
    {
        const string physical = """
            ==[wan]
                    mode: static
                    ip: 201.30.10.70 255.255.255.252
                    status: down
                    speed: 0Mbps (Half Duplex)
            ==[lan1]
                    mode: static
                    ip: 192.168.1.99 255.255.255.0
                    status: up
                    speed: 1000Mbps (Full Duplex)
            """;

        var (enc, down) = WanPortInspector.FortiWanStatus(physical, new[] { "wan" });
        Assert.True(enc);
        Assert.True(down);

        var (encLan, downLan) = WanPortInspector.FortiWanStatus(physical, new[] { "lan1" });
        Assert.True(encLan);
        Assert.False(downLan);
    }

    [Fact]
    public void Forti_DetectaUp_Physical()
    {
        const string physical = """
            ==[wan]
                    mode: static
                    ip: 201.30.10.70 255.255.255.252
                    status: up
                    speed: 1000Mbps (Full Duplex)
            """;

        var (enc, down) = WanPortInspector.FortiWanStatus(physical, new[] { "wan" });
        Assert.True(enc);
        Assert.False(down);
    }

    [Fact]
    public void Forti_DetectaDown_NetlinkFallback()
    {
        const string netlink = "if=wan family=2 type=1 index=3 mtu=1500 flags=up,broadcast,multicast state: DOWN carrier: OFF no_carrier";
        var (enc, down) = WanPortInspector.FortiWanStatus(netlink, new[] { "wan" });
        Assert.True(enc);
        Assert.True(down);
    }

    [Fact]
    public void Cisco_AvaliarPortas_SemInversao_WanDown()
    {
        var info = WanPortInspector.ObterRoteadorPortasInfo("921")!;
        Assert.NotNull(info);

        const string brief = """
            Interface                  IP-Address      OK? Method Status                Protocol
            GigabitEthernet0           unassigned      YES unset  up                    up
            GigabitEthernet1           unassigned      YES unset  down                  down
            GigabitEthernet2           unassigned      YES unset  down                  down
            GigabitEthernet3           unassigned      YES unset  down                  down
            GigabitEthernet4           201.90.204.22   YES manual down                  down
            Vlan1                      192.168.1.1     YES manual up                    up
            """;

        var res = WanPortInspector.AvaliarPortasCisco(brief, info, sourceIp: "192.168.1.2");
        Assert.True(res.LanUp);
        Assert.False(res.WanUp);
        Assert.False(res.PossivelInversaoCabos);
    }

    [Fact]
    public void Cisco_AvaliarPortas_DetectaInversao_WanUpLanDown()
    {
        var info = WanPortInspector.ObterRoteadorPortasInfo("921")!;
        Assert.NotNull(info);

        const string brief = """
            Interface                  IP-Address      OK? Method Status                Protocol
            GigabitEthernet0           unassigned      YES unset  down                  down
            GigabitEthernet1           unassigned      YES unset  down                  down
            GigabitEthernet2           unassigned      YES unset  down                  down
            GigabitEthernet3           unassigned      YES unset  down                  down
            GigabitEthernet4           201.90.204.22   YES manual up                    up
            Vlan1                      192.168.1.1     YES manual down                  down
            """;

        var res = WanPortInspector.AvaliarPortasCisco(brief, info, sourceIp: "192.168.1.2");
        Assert.False(res.LanUp);
        Assert.True(res.WanUp);
        Assert.True(res.PossivelInversaoCabos);
        Assert.Contains("INVERSÃO", res.MensagemDiagnostico);
    }

    [Fact]
    public void Cisco921_AvaliarPortas_Ge4Wan_Ge5Lan_IdentificaCorretamente()
    {
        var info = WanPortInspector.ObterRoteadorPortasInfo("921")!;
        Assert.NotNull(info);
        Assert.Equal("GE4 (Porta 4)", info.WanInterfaceExibicao);
        Assert.Equal("GE5 (Porta 5 / LAN)", info.LanInterfaceExibicao);

        const string brief = """
            Interface                  IP-Address      OK? Method Status                Protocol
            GigabitEthernet0           unassigned      YES unset  down                  down
            GigabitEthernet1           unassigned      YES unset  down                  down
            GigabitEthernet2           unassigned      YES unset  down                  down
            GigabitEthernet3           unassigned      YES unset  down                  down
            GigabitEthernet4           201.90.204.22   YES manual up                    up
            GigabitEthernet5           192.168.1.1     YES manual up                    up
            """;

        var res = WanPortInspector.AvaliarPortasCisco(brief, info, sourceIp: "192.168.1.2");
        Assert.True(res.LanUp);
        Assert.True(res.WanUp);
        Assert.False(res.PossivelInversaoCabos);
    }

    [Fact]
    public void Cisco_AvaliarPortas_DetectaInversao_ArpNaWan()
    {
        var info = WanPortInspector.ObterRoteadorPortasInfo("c841")!;
        Assert.NotNull(info);

        const string brief = """
            Interface                  IP-Address      OK? Method Status                Protocol
            GigabitEthernet0/0         unassigned      YES unset  down                  down
            GigabitEthernet0/4         201.90.204.22   YES manual up                    up
            Vlan1                      192.168.1.1     YES manual up                    up
            """;

        const string arp = """
            Protocol  Address          Age (min)  Hardware Addr   Type   Interface
            Internet  192.168.1.2             0   0050.56c0.0008  ARPA   GigabitEthernet0/4
            Internet  201.90.204.21           1   0010.7bb2.c001  ARPA   GigabitEthernet0/4
            """;

        var res = WanPortInspector.AvaliarPortasCisco(brief, info, arpOutput: arp, sourceIp: "192.168.1.2");
        Assert.True(res.PossivelInversaoCabos);
        Assert.NotNull(res.DetalhesArp);
        Assert.Contains("192.168.1.2", res.MensagemDiagnostico);
    }

    [Fact]
    public void Hpe_AvaliarPortas_DetectaInversao_WanUpLanDown()
    {
        var info = WanPortInspector.ObterRoteadorPortasInfo("msr954")!;
        Assert.NotNull(info);

        const string brief = """
            Interface            Link Protocol Main IP
            GE0/0                UP   UP       201.90.204.22
            GE0/1                DOWN DOWN     --
            """;

        var res = WanPortInspector.AvaliarPortasHpe(brief, info);
        Assert.True(res.WanUp);
        Assert.False(res.LanUp);
        Assert.True(res.PossivelInversaoCabos);
    }

    [Fact]
    public void Forti_AvaliarPortas_DetectaInversao_ArpNaWan()
    {
        var info = WanPortInspector.ObterRoteadorPortasInfo("forti")!;
        Assert.NotNull(info);

        const string physical = """
            ==[wan]
                    status: up
            ==[internal]
                    status: down
            """;

        const string arp = """
            192.168.1.50 0 00:0c:29:41:88:99 wan
            """;

        var res = WanPortInspector.AvaliarPortasForti(physical, info, arpOutput: arp, sourceIp: "192.168.1.50");
        Assert.True(res.PossivelInversaoCabos);
        Assert.Contains("INVERSÃO", res.MensagemDiagnostico);
    }
}
