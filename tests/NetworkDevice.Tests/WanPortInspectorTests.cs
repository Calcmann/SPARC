using NetworkDevice.Core.Detection;
using Xunit;

namespace NetworkDevice.Tests;

public sealed class WanPortInspectorTests
{
    [Theory]
    [InlineData("cisco.c841.break", false, "GE0/4 (Porta 4)")]
    [InlineData("cisco.c900.ctrl-c", false, "GE4 (Porta 4)")]
    [InlineData("cisco.c1900.break", false, "GE0/0")]
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
}
