using System.Collections.Generic;
using NetworkDevice.Core.Diagnostics;
using NetworkDevice.Core.Domain;
using Xunit;

namespace NetworkDevice.Tests;

public class RouterRemoteLoopbackServiceTests
{
    [Fact]
    public void Profiles_ContainsAllSupportedManufacturers()
    {
        var profiles = RouterRemoteLoopbackService.Profiles;
        Assert.NotEmpty(profiles);

        Assert.Contains(profiles, p => p.Manufacturer == DeviceManufacturer.Cisco);
        Assert.Contains(profiles, p => p.Manufacturer == DeviceManufacturer.Hpe);
        Assert.Contains(profiles, p => p.Manufacturer == DeviceManufacturer.Fortinet);
        Assert.Contains(profiles, p => p.Manufacturer == DeviceManufacturer.Generic);
    }

    [Theory]
    [InlineData(DeviceManufacturer.Cisco, DeviceSeries.Series1900, "cisco_ios")]
    [InlineData(DeviceManufacturer.Cisco, DeviceSeries.Series2900, "cisco_ios")]
    [InlineData(DeviceManufacturer.Cisco, DeviceSeries.Isr921, "cisco_ios")]
    [InlineData(DeviceManufacturer.Hpe, DeviceSeries.Msr954, "hpe_comware")]
    [InlineData(DeviceManufacturer.Fortinet, DeviceSeries.FortiGate40F, "fortinet_fortigate")]
    public void GetProfileForDevice_ResolvesCorrectProfile(DeviceManufacturer mfg, DeviceSeries series, string expectedId)
    {
        var profile = RouterRemoteLoopbackService.GetProfileForDevice(mfg, series);
        Assert.Equal(expectedId, profile.Id);
    }

    [Fact]
    public void CiscoInterfaceLoopbackInternal_GeneratesCorrectCommandsAndRollback()
    {
        var profile = RouterRemoteLoopbackService.Profiles[0];
        var config = new RouterLoopbackConfig(
            Profile: profile,
            Method: RouterLoopbackMethod.CiscoInterfaceLoopbackInternal,
            InterfaceName: "GigabitEthernet0/0/1",
            SafetyRollbackMinutes: 15
        );

        var script = RouterRemoteLoopbackService.GenerateScript(config);

        Assert.Contains("configure terminal", script.EnableCommands);
        Assert.Contains("interface GigabitEthernet0/0/1", script.EnableCommands);
        Assert.Contains(" loopback internal", script.EnableCommands);
        Assert.Equal("reload in 15", script.RollbackTimerCommand);

        Assert.Contains("interface GigabitEthernet0/0/1", script.DisableCommands);
        Assert.Contains(" no loopback", script.DisableCommands);
        Assert.Equal("reload cancel", script.RollbackCancelCommand);
    }

    [Fact]
    public void CiscoCarrierMacSwap_GeneratesMacSwapCommands()
    {
        var profile = RouterRemoteLoopbackService.Profiles[0];
        var config = new RouterLoopbackConfig(
            Profile: profile,
            Method: RouterLoopbackMethod.CiscoCarrierMacSwap,
            InterfaceName: "GigabitEthernet0/0/2",
            SafetyRollbackMinutes: 0
        );

        var script = RouterRemoteLoopbackService.GenerateScript(config);

        Assert.Contains(" ethernet loopback local mac-swap", script.EnableCommands);
        Assert.Null(script.RollbackTimerCommand);
        Assert.Contains(" no ethernet loopback local", script.DisableCommands);
    }

    [Fact]
    public void CiscoIpSlaResponderUdpEcho_GeneratesUdpEchoCommands()
    {
        var profile = RouterRemoteLoopbackService.Profiles[0];
        var config = new RouterLoopbackConfig(
            Profile: profile,
            Method: RouterLoopbackMethod.CiscoIpSlaResponderUdpEcho,
            InterfaceName: "Gi0/0/0",
            TargetIp: "200.182.245.2",
            Port: 5001
        );

        var script = RouterRemoteLoopbackService.GenerateScript(config);

        Assert.Contains("ip sla responder", script.EnableCommands);
        Assert.Contains("ip sla responder udp-echo ipaddress 200.182.245.2 port 5001", script.EnableCommands);
        Assert.Contains("no ip sla responder udp-echo ipaddress 200.182.245.2 port 5001", script.DisableCommands);
    }

    [Fact]
    public void HpeInterfaceLoopbackInternal_GeneratesComwareCommandsAndScheduleReboot()
    {
        var profile = RouterRemoteLoopbackService.Profiles[1];
        var config = new RouterLoopbackConfig(
            Profile: profile,
            Method: RouterLoopbackMethod.HpeInterfaceLoopbackInternal,
            InterfaceName: "GigabitEthernet0/0",
            SafetyRollbackMinutes: 30
        );

        var script = RouterRemoteLoopbackService.GenerateScript(config);

        Assert.Contains("system-view", script.EnableCommands);
        Assert.Contains("interface GigabitEthernet0/0", script.EnableCommands);
        Assert.Contains(" loopback internal", script.EnableCommands);
        Assert.Equal("schedule reboot delay 30", script.RollbackTimerCommand);
        Assert.Equal("undo schedule reboot", script.RollbackCancelCommand);

        Assert.Contains(" undo loopback", script.DisableCommands);
    }

    [Fact]
    public void HpeNqaServerUdpEcho_GeneratesNqaServerCommands()
    {
        var profile = RouterRemoteLoopbackService.Profiles[1];
        var config = new RouterLoopbackConfig(
            Profile: profile,
            Method: RouterLoopbackMethod.HpeNqaServerUdpEcho,
            InterfaceName: "GigabitEthernet0/0",
            TargetIp: "10.0.0.1",
            Port: 5800
        );

        var script = RouterRemoteLoopbackService.GenerateScript(config);

        Assert.Contains("nqa server enable", script.EnableCommands);
        Assert.Contains("nqa server udp-echo 10.0.0.1 5800", script.EnableCommands);
        Assert.Contains("undo nqa server udp-echo 10.0.0.1 5800", script.DisableCommands);
    }

    [Fact]
    public void FortiGateNicLoopbackInternal_GeneratesFortiOSDiagnoseCommands()
    {
        var profile = RouterRemoteLoopbackService.Profiles[2];
        var config = new RouterLoopbackConfig(
            Profile: profile,
            Method: RouterLoopbackMethod.FortiGateNicLoopbackInternal,
            InterfaceName: "internal1"
        );

        var script = RouterRemoteLoopbackService.GenerateScript(config);

        Assert.Contains("diagnose hardware device nic loopback enable internal1 internal", script.EnableCommands);
        Assert.Contains("diagnose hardware device nic loopback disable internal1", script.DisableCommands);
    }
}
