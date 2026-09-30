using System;
using System.IO;
using NetworkDevice.Core.Licensing;
using Xunit;

namespace NetworkDevice.Tests;

public class CloudLicenseServiceTests : IDisposable
{
    private readonly string _tempFile;

    public CloudLicenseServiceTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"devices_test_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            try { File.Delete(_tempFile); } catch { }
        }
    }

    [Fact]
    public void RegisterOrUpdateDevice_CreatesNewEntry_WhenDeviceDoesNotExist()
    {
        var svc = new CloudLicenseService(_tempFile);
        var req = new ActivationRequestData(
            RawRequest: "req",
            MachineGuid: "guid-001",
            MachineFingerprint: "fp-001",
            IsValid: true,
            ErrorMessage: null,
            FirstName: "Lucas",
            LastName: "Moraes",
            Phone: "11987654321",
            Email: "lucas@claro.com.br",
            Cluster: "Grande SP Leste",
            Uf: "SP",
            ClientVersion: "0.8.2");

        var lic = new GeneratedLicenseResult(true, "SPB1.dummy.token", "2026-10-30", DateTime.UtcNow.AddDays(30), 30, null);

        var record = svc.RegisterOrUpdateDevice(req, lic, "Região SP");

        Assert.NotNull(record);
        Assert.Equal("Lucas", record.FirstName);
        Assert.Equal("Moraes", record.LastName);
        Assert.Equal("Lucas Moraes", record.FullName);
        Assert.Equal("Grande SP Leste", record.Cluster);
        Assert.Equal("SP", record.Uf);
        Assert.Equal("11987654321", record.Phone);
        Assert.Equal("lucas@claro.com.br", record.Email);
        Assert.Equal("SPB1.dummy.token", record.AuthorizedToken);
        Assert.Equal("Active", record.Status);

        var list = svc.LoadLocalDevices();
        Assert.Single(list);
        Assert.Equal("guid-001", list[0].MachineGuid);
        Assert.Equal("Grande SP Leste", list[0].Cluster);
        Assert.Equal("SP", list[0].Uf);
    }

    [Fact]
    public void RevokeDevice_ChangesStatusToRevoked()
    {
        var svc = new CloudLicenseService(_tempFile);
        var req = new ActivationRequestData("req", "guid-002", "fp-002", true, null, "Maria", "Silva", "21999998888", "maria@claro.com.br");
        var lic = new GeneratedLicenseResult(true, "token-2", "2026-10-30", DateTime.UtcNow.AddDays(30), 30, null);
        svc.RegisterOrUpdateDevice(req, lic);

        var (success, msg) = svc.RevokeDevice("guid-002", "Técnico desligado");

        Assert.True(success);
        var list = svc.LoadLocalDevices();
        Assert.Single(list);
        Assert.Equal("Revoked", list[0].Status);
        Assert.Equal(DeviceLicenseStatus.Revoked, list[0].CalculatedStatus);
        Assert.Equal("🚫 Revogado", list[0].StatusBadge);
    }

    [Fact]
    public void OnlineDeviceRecord_WhatsAppUrl_FormatsCountryCodeCorrectly()
    {
        var dev = new OnlineDeviceRecord { Phone = "(11) 98765-4321" };
        Assert.Equal("https://wa.me/5511987654321", dev.WhatsAppUrl);
    }
}
