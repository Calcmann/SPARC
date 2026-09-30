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
}
