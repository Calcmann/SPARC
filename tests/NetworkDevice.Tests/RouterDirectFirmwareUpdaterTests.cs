using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Firmware;
using Xunit;

namespace NetworkDevice.Tests;

public class RouterDirectFirmwareUpdaterTests
{
    [Fact]
    public void RouterFirmwareStatus_PropertiesRecordCorrectly()
    {
        var status = new RouterFirmwareStatus(
            IsWanReachable: true,
            CurrentVersion: "15.9(3)M6",
            OfficialFirmwareName: "c800m-universalk9-mz.SPA.159-3.M12.bin",
            OfficialSizeBytes: 65302036,
            IsCompliant: false,
            Message: "Atualização recomendada.");

        Assert.True(status.IsWanReachable);
        Assert.Equal("15.9(3)M6", status.CurrentVersion);
        Assert.Equal("c800m-universalk9-mz.SPA.159-3.M12.bin", status.OfficialFirmwareName);
        Assert.Equal(65302036, status.OfficialSizeBytes);
        Assert.False(status.IsCompliant);
    }

    [Theory]
    [InlineData(DeviceSeries.Isr841, "c841")]
    [InlineData(DeviceSeries.Isr921, "c921")]
    [InlineData(DeviceSeries.Series1900, "c1900")]
    [InlineData(DeviceSeries.Series2900, "c2900")]
    [InlineData(DeviceSeries.FortiGate40F, "fgt40f")]
    [InlineData(DeviceSeries.Msr954, "msr954")]
    [InlineData(DeviceSeries.Msr930, "msr930")]
    [InlineData(DeviceSeries.Msr1002, "msr1002")]
    public void BuildDirectDownloadUrl_GeneratesValidWorkerUrl(DeviceSeries series, string expectedSlug)
    {
        var url = RouterDirectFirmwareUpdater.BuildDirectDownloadUrl(series);
        Assert.Contains(expectedSlug, url);
        Assert.Contains("key=CR%40PS", url);
        Assert.StartsWith("http://sparc-firmware-proxy.calcmann.workers.dev", url);
    }

    private static async Task<NetworkDevice.Core.Session.DeviceSession> ConnectDrainedAsync(TestDoubles.ScriptedTransport transport)
    {
        var session = new NetworkDevice.Core.Session.DeviceSession(transport, new NetworkDevice.Core.Session.SessionOptions());
        await session.ConnectAsync();
        var buf = new byte[4096];
        while (await transport.ReadAsync(buf) > 0) { }
        return session;
    }

    [Fact]
    public async Task CheckWanAndInternetAsync_WhenWanDown_ReturnsPhysicalDown()
    {
        var transport = new TestDoubles.ScriptedTransport(
            cmd =>
            {
                if (cmd.Contains("show ip interface brief"))
                {
                    return "\r\nGigabitEthernet0/4   unassigned   YES unset   down   down\r\nRouter#";
                }
                return "\r\nRouter#";
            },
            initialOutput: "Router#");

        await using var session = await ConnectDrainedAsync(transport);
        var updater = new RouterDirectFirmwareUpdater(_ => Task.CompletedTask);

        var result = await updater.CheckWanAndInternetAsync(session, DeviceSeries.Isr841);

        Assert.False(result.IsPhysicalUp);
        Assert.False(result.HasInternet);
        Assert.Contains("DOWN", result.Details);
    }

    [Fact]
    public async Task CheckWanAndInternetAsync_WhenWanUpNoInternet_ReturnsPhysicalUpNoInternet()
    {
        var transport = new TestDoubles.ScriptedTransport(
            cmd =>
            {
                if (cmd.Contains("show ip interface brief"))
                {
                    return "\r\nGigabitEthernet0/4   192.168.1.1   YES manual   up   up\r\nRouter#";
                }
                if (cmd.Contains("ping 8.8.8.8"))
                {
                    return "\r\nSending 3, 100-byte ICMP Echos to 8.8.8.8, timeout is 2 seconds:\r\n.....\r\nSuccess rate is 0 percent (0/3)\r\nRouter#";
                }
                return "\r\nRouter#";
            },
            initialOutput: "Router#");

        await using var session = await ConnectDrainedAsync(transport);
        var updater = new RouterDirectFirmwareUpdater(_ => Task.CompletedTask);

        var result = await updater.CheckWanAndInternetAsync(session, DeviceSeries.Isr841);

        Assert.True(result.IsPhysicalUp);
        Assert.False(result.HasInternet);
        Assert.Contains("sem conectividade", result.Details);
    }

    [Fact]
    public async Task CheckWanAndInternetAsync_WhenWanUpWithInternet_ReturnsPhysicalUpWithInternet()
    {
        var transport = new TestDoubles.ScriptedTransport(
            cmd =>
            {
                if (cmd.Contains("show ip interface brief"))
                {
                    return "\r\nGigabitEthernet0/4   192.168.1.1   YES manual   up   up\r\nRouter#";
                }
                if (cmd.Contains("ping 8.8.8.8"))
                {
                    return "\r\nSending 3, 100-byte ICMP Echos to 8.8.8.8, timeout is 2 seconds:\r\n!!!\r\nSuccess rate is 100 percent (3/3)\r\nRouter#";
                }
                return "\r\nRouter#";
            },
            initialOutput: "Router#");

        await using var session = await ConnectDrainedAsync(transport);
        var updater = new RouterDirectFirmwareUpdater(_ => Task.CompletedTask);

        var result = await updater.CheckWanAndInternetAsync(session, DeviceSeries.Isr841);

        Assert.True(result.IsPhysicalUp);
        Assert.True(result.HasInternet);
        Assert.Contains("Internet OK", result.Details);
    }

    [Fact]
    public void EvaluateComplianceStrict_Cisco841_M11_Against_M12_ReturnsFalse()
    {
        var showVerM11 = "Cisco IOS Software, C800 Software (C800-UNIVERSALK9-M), Version 15.9(3)M11, RELEASE SOFTWARE (fc2)\r\n" +
                         "System image file is \"flash:c800m-universalk9-mz.SPA.159-3.M11.bin\"";

        var isCompliant = RouterDirectFirmwareUpdater.EvaluateComplianceStrict(
            DeviceSeries.Isr841,
            showVerM11,
            "15.9(3)M11 (c800m-universalk9-mz.SPA.159-3.M11.bin)",
            "c800m-universalk9-mz.SPA.159-3.M12.bin");

        Assert.False(isCompliant);
    }

    [Fact]
    public void EvaluateComplianceStrict_Cisco841_M12_Against_M12_ReturnsTrue()
    {
        var showVerM12 = "Cisco IOS Software, C800 Software (C800-UNIVERSALK9-M), Version 15.9(3)M12, RELEASE SOFTWARE (fc2)\r\n" +
                         "System image file is \"flash:c800m-universalk9-mz.SPA.159-3.M12.bin\"";

        var isCompliant = RouterDirectFirmwareUpdater.EvaluateComplianceStrict(
            DeviceSeries.Isr841,
            showVerM12,
            "15.9(3)M12 (c800m-universalk9-mz.SPA.159-3.M12.bin)",
            "c800m-universalk9-mz.SPA.159-3.M12.bin");

        Assert.True(isCompliant);
    }

    [Fact]
    public void EvaluateComplianceStrict_FortiGate40F_726_Against_7211_ReturnsFalse()
    {
        var status726 = "Version: FortiGate-40F v7.2.6,build1575,230814 (GA.F)\r\nVirus-DB: 1.00000(2018-04-09 18:07)";

        var isCompliant = RouterDirectFirmwareUpdater.EvaluateComplianceStrict(
            DeviceSeries.FortiGate40F,
            status726,
            "v7.2.6,build1575",
            "FGT_40F-v7.2.11.M-build1740-FORTINET.out");

        Assert.False(isCompliant);
    }

    [Fact]
    public void EvaluateComplianceStrict_FortiGate40F_7211_Against_7211_ReturnsTrue()
    {
        var status7211 = "Version: FortiGate-40F v7.2.11,build1740,240502 (GA.M)\r\nVirus-DB: 1.00000(2018-04-09 18:07)";

        var isCompliant = RouterDirectFirmwareUpdater.EvaluateComplianceStrict(
            DeviceSeries.FortiGate40F,
            status7211,
            "v7.2.11,build1740",
            "FGT_40F-v7.2.11.M-build1740-FORTINET.out");

        Assert.True(isCompliant);
    }

    [Fact]
    public void EvaluateComplianceStrict_HpeMsr954_OldRelease_Against_NewRelease_ReturnsFalse()
    {
        var dispVerOld = "HPE Comware Software, Version 7.1.064, Release 0413P03\r\nBoot Image: flash:/msr954-cmw710-boot-r0413p03.bin";

        var isCompliant = RouterDirectFirmwareUpdater.EvaluateComplianceStrict(
            DeviceSeries.Msr954,
            dispVerOld,
            "Release 0413P03",
            "MSR954-CMW710-R6749P43.ipe");

        Assert.False(isCompliant);
    }

    [Fact]
    public void EvaluateComplianceStrict_HpeMsr954_SameRelease_Against_NewRelease_ReturnsTrue()
    {
        var dispVerNew = "HPE Comware Software, Version 7.1.064, Release 6749P43\r\nBoot Image: flash:/msr954-cmw710-boot-r6749p43.bin";

        var isCompliant = RouterDirectFirmwareUpdater.EvaluateComplianceStrict(
            DeviceSeries.Msr954,
            dispVerNew,
            "Release 6749P43",
            "MSR954-CMW710-R6749P43.ipe");

        Assert.True(isCompliant);
    }
}
