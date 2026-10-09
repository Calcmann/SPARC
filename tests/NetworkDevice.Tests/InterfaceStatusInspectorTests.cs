using NetworkDevice.Core.Detection;
using NetworkDevice.Core.Domain;
using Xunit;

namespace NetworkDevice.Tests;

public sealed class InterfaceStatusInspectorTests
{
    [Theory]
    [InlineData(DeviceSeries.Series1900, "GigabitEthernet0/0", "GigabitEthernet0/1")]
    [InlineData(DeviceSeries.Series2900, "GigabitEthernet0/0", "GigabitEthernet0/1")]
    [InlineData(DeviceSeries.Isr841, "GigabitEthernet0/4", "GigabitEthernet0/5")]
    [InlineData(DeviceSeries.Isr921, "GigabitEthernet4", "Vlan1")]
    [InlineData(DeviceSeries.Msr954, "GE0/0", "GE0/1")]
    [InlineData(DeviceSeries.FortiGate40F, "wan", "lan")]
    public void ResolveExpectedInterfaces_RetornaCorretamentePorSerie(DeviceSeries series, string expectedWan, string expectedLan)
    {
        var (wan, lan) = InterfaceStatusInspector.ResolveExpectedInterfaces(series);
        Assert.Equal(expectedWan, wan);
        Assert.Equal(expectedLan, lan);
    }

    [Fact]
    public void CiscoBrief_DetectaWanUpELanDownComIps()
    {
        const string ciscoOutput = @"Interface                  IP-Address      OK? Method Status                Protocol
GigabitEthernet0/0         201.90.204.22   YES manual up                    up      
GigabitEthernet0/1         200.182.245.17  YES manual up                    down    
Serial0/0/0                unassigned      YES unset  administratively down down    
";
        var res = InterfaceStatusInspector.ParseCiscoBrief(
            ciscoOutput,
            "GigabitEthernet0/0",
            "GigabitEthernet0/1",
            "201.90.204.22/30",
            "200.182.245.17/29");

        Assert.NotNull(res.Wan);
        Assert.NotNull(res.Lan);
        Assert.True(res.WanIpMatches);
        Assert.True(res.LanIpMatches);
        Assert.True(res.Wan.IsPhysicalUp);
        Assert.False(res.Lan.IsPhysicalUp);
        Assert.True(res.Lan.IsAdminUp);
        Assert.Equal("201.90.204.22", res.Wan.IpAddress);
        Assert.Equal("200.182.245.17", res.Lan.IpAddress);
    }

    [Fact]
    public void CiscoBrief_SuportaAbreviacoesGi()
    {
        const string ciscoOutput = @"Interface                  IP-Address      OK? Method Status                Protocol
Gi0/0                      201.90.204.22   YES manual up                    up      
Gi0/1                      200.182.245.17  YES manual up                    up      
";
        var res = InterfaceStatusInspector.ParseCiscoBrief(
            ciscoOutput,
            "GigabitEthernet0/0",
            "GigabitEthernet0/1",
            "201.90.204.22",
            "200.182.245.17");

        Assert.NotNull(res.Wan);
        Assert.NotNull(res.Lan);
        Assert.True(res.Wan.IsPhysicalUp);
        Assert.True(res.Lan.IsPhysicalUp);
    }

    [Fact]
    public void HpeBrief_DetectaGe0EGe1()
    {
        const string hpeOutput = @"Brief information on interfaces in route mode:
Link: ADM - administratively down; Stby - standby
Protocol: (s) - spoofing
Interface              Link Protocol Primary IP      Description
GE0/0                  UP   UP       201.90.204.22   --
GE0/1                  UP   DOWN     200.182.245.17  --
InLoop0                UP   UP(s)    --              --
";
        var res = InterfaceStatusInspector.ParseHpeBrief(
            hpeOutput,
            "GE0/0",
            "GE0/1",
            "201.90.204.22",
            "200.182.245.17");

        Assert.NotNull(res.Wan);
        Assert.NotNull(res.Lan);
        Assert.True(res.WanIpMatches);
        Assert.True(res.LanIpMatches);
        Assert.True(res.Wan.IsPhysicalUp);
        Assert.False(res.Lan.IsPhysicalUp);
        Assert.True(res.Lan.IsAdminUp);
    }

    [Fact]
    public void Fortinet_DetectaWanELanComIpEStatus()
    {
        const string physOutput = @"== [wan]
   status: up
   speed: 1000Mbps
== [lan]
   status: down
";
        const string ipOutput = @"IP=201.90.204.22->201.90.204.22/255.255.255.252 index=3 devname=wan
IP=200.182.245.17->200.182.245.17/255.255.255.248 index=4 devname=lan
";
        var res = InterfaceStatusInspector.ParseFortinetInterfaces(
            physOutput,
            ipOutput,
            "wan",
            "lan",
            "201.90.204.22",
            "200.182.245.17");

        Assert.NotNull(res.Wan);
        Assert.NotNull(res.Lan);
        Assert.True(res.WanIpMatches);
        Assert.True(res.LanIpMatches);
        Assert.True(res.Wan.IsPhysicalUp);
        Assert.False(res.Lan.IsPhysicalUp);
    }
}
