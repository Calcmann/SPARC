using System;
using System.IO;
using NetworkDevice.Core.Licensing;
using Xunit;

namespace NetworkDevice.Tests;

public class CloudLicenseServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _tempFile;

    public CloudLicenseServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sparc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _tempFile = Path.Combine(_tempDir, "devices.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
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
        Assert.Equal("(11) 98765-4321", record.Phone);
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

    [Fact]
    public async System.Threading.Tasks.Task SubmitActivationRequest_And_CheckStatus_WorksLocally()
    {
        var svc = new CloudLicenseService(_tempFile);
        svc.ForceLocalOnly = true;
        svc.GitHubToken = "none";
        var req = new OnlineActivationRequest
        {
            MachineGuid = "test-machine-guid-123",
            MachineFingerprint = "test-fp-123",
            FirstName = "Carlos",
            LastName = "Ferreira",
            Company = "Claro Telecom",
            EmployeeId = "MAT12345",
            Phone = "19988776655",
            Email = "carlos@empresa.com",
            Cluster = "Campinas",
            Uf = "SP",
            ClientVersion = "0.8.28",
            RawRequestCode = "SPBREQ.test.request"
        };

        var (subOk, subMsg) = await svc.SubmitActivationRequestAsync(req);
        Assert.True(subOk);

        var (found, fetched, msg) = await svc.CheckActivationRequestStatusAsync("test-machine-guid-123");
        Assert.True(found);
        Assert.NotNull(fetched);
        Assert.Equal("Carlos", fetched.FirstName);
        Assert.Equal("Campinas", fetched.Cluster);
        Assert.Equal("SP", fetched.Uf);
        Assert.Equal("Pending", fetched.Status);
        Assert.Equal("⏳ Pendente de Aprovação", fetched.StatusBadge);

        var list = await svc.ListActivationRequestsAsync();
        Assert.Contains(list, r => r.MachineGuid == "test-machine-guid-123");
    }

    [Fact]
    public async System.Threading.Tasks.Task ApproveActivationRequest_GeneratesSignedLicense_And_UpdatesRecord()
    {
        var svc = new CloudLicenseService(_tempFile);
        svc.ForceLocalOnly = true;
        svc.GitHubToken = "none";
        var signer = new LicenseSignerService();

        var req = new OnlineActivationRequest
        {
            MachineGuid = "approve-guid-456",
            MachineFingerprint = "approve-fp-456",
            FirstName = "Renato",
            LastName = "Alves",
            Company = "Claro Telecom",
            EmployeeId = "MAT99999",
            Phone = "21988887777",
            Email = "renato@teste.com",
            Cluster = "Rio Centro",
            Uf = "RJ",
            ClientVersion = "0.8.28",
            RawRequestCode = "SPBREQ.dummy.req"
        };

        await svc.SubmitActivationRequestAsync(req);

        var (appOk, appMsg, token) = await svc.ApproveActivationRequestAsync(req, 30, signer, "Aprovado no teste");
        Assert.True(appOk);
        Assert.False(string.IsNullOrWhiteSpace(token));

        var (found, updated, _) = await svc.CheckActivationRequestStatusAsync("approve-guid-456");
        Assert.True(found);
        Assert.NotNull(updated);
        Assert.Equal("Approved", updated.Status);
        Assert.Equal(token, updated.ApprovedToken);
        Assert.Equal(30, updated.ValidDays);

        // Dispositivo deve ter sido adicionado à lista de cópias ativas monitoradas (devices.json)
        var devices = svc.LoadLocalDevices();
        Assert.Contains(devices, d => d.MachineGuid == "approve-guid-456" && d.Status == "Active");
    }

    [Fact]
    public async System.Threading.Tasks.Task RejectActivationRequest_UpdatesStatusToRejected()
    {
        var svc = new CloudLicenseService(_tempFile);
        svc.ForceLocalOnly = true;
        svc.GitHubToken = "none";
        var req = new OnlineActivationRequest
        {
            MachineGuid = "reject-guid-789",
            FirstName = "Pedro",
            LastName = "Santos"
        };

        await svc.SubmitActivationRequestAsync(req);
        var (rejOk, rejMsg) = await svc.RejectActivationRequestAsync(req, "Documentação incompleta");
        Assert.True(rejOk);

        var (found, updated, _) = await svc.CheckActivationRequestStatusAsync("reject-guid-789");
        Assert.True(found);
        Assert.NotNull(updated);
        Assert.Equal("Rejected", updated.Status);
        Assert.Equal("Documentação incompleta", updated.RejectionReason);
        Assert.Equal("❌ Rejeitada", updated.StatusBadge);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteActivationRequest_And_DeleteDevice_RemovesRecords()
    {
        var svc = new CloudLicenseService(_tempFile);
        svc.ForceLocalOnly = true;
        svc.GitHubToken = "none";
        var req = new OnlineActivationRequest
        {
            MachineGuid = "del-test-guid-001",
            FirstName = "Teste",
            LastName = "Excluir"
        };

        await svc.SubmitActivationRequestAsync(req);
        var (found, _, _) = await svc.CheckActivationRequestStatusAsync("del-test-guid-001");
        Assert.True(found);

        var (delOk, delMsg) = await svc.DeleteActivationRequestAsync(req);
        Assert.True(delOk);

        var (foundAfter, _, _) = await svc.CheckActivationRequestStatusAsync("del-test-guid-001");
        Assert.False(foundAfter);

        // Teste de remoção de dispositivo
        var actReq = new ActivationRequestData("req", "dev-to-delete", "fp", true, null, "Nome", "Sobrenome");
        var lic = new GeneratedLicenseResult(true, "tok", "2026-10-30", DateTime.UtcNow.AddDays(30), 30, null);
        svc.RegisterOrUpdateDevice(actReq, lic);

        var devices = svc.LoadLocalDevices();
        Assert.Contains(devices, d => d.MachineGuid == "dev-to-delete");

        var (delDevOk, _) = svc.DeleteDevice("dev-to-delete");
        Assert.True(delDevOk);

        devices = svc.LoadLocalDevices();
        Assert.DoesNotContain(devices, d => d.MachineGuid == "dev-to-delete");
    }

    [Fact]
    public void RegisterOrUpdateDevice_PreservesPlatform()
    {
        var svc = new CloudLicenseService(_tempFile);
        var req = new ActivationRequestData(
            RawRequest: "req",
            MachineGuid: "and-guid-002",
            MachineFingerprint: "and-fp-002",
            IsValid: true,
            ErrorMessage: null,
            FirstName: "Carlos",
            LastName: "Android",
            Phone: "11988889999",
            Email: "carlos@android.com",
            Cluster: "Campinas",
            Uf: "SP",
            ClientVersion: "0.8.38",
            Platform: "Android");

        var lic = new GeneratedLicenseResult(true, "SPB1.dummy.token", "2026-10-30", DateTime.UtcNow.AddDays(30), 30, null);

        var record = svc.RegisterOrUpdateDevice(req, lic, "Android Test");

        Assert.Equal("Android", record.Platform);
        Assert.Equal("📱 Android", record.PlatformBadge);

        var loaded = svc.LoadLocalDevices();
        Assert.Single(loaded);
        Assert.Equal("Android", loaded[0].Platform);
        Assert.Equal("📱 Android", loaded[0].PlatformBadge);
    }

    [Fact]
    public void UnrevokeDevice_RestoresStatusToActive_AndGeneratesValidToken()
    {
        var svc = new CloudLicenseService(_tempFile);
        var signer = new LicenseSignerService();
        var req = new ActivationRequestData("req", "guid-unrevoke", "fp-unrevoke", true, null, "Lucas", "Silva", "11999998888", "lucas@teste.com");
        var lic = signer.GenerateLicense(req, 30, "Lucas Silva", "Inicial");
        svc.RegisterOrUpdateDevice(req, lic);

        // Revoga
        svc.RevokeDevice("guid-unrevoke", "Suspeita de extravio");
        var listRevoked = svc.LoadLocalDevices();
        Assert.Equal("Revoked", listRevoked[0].Status);

        // Desbloqueia / Libera
        var (ok, msg, dev) = svc.UnrevokeDevice("guid-unrevoke", signer, 30, "Liberado pelo administrador");
        Assert.True(ok);
        Assert.NotNull(dev);
        Assert.Equal("Active", dev.Status);
        Assert.Equal(DeviceLicenseStatus.Active, dev.CalculatedStatus);
        Assert.Contains("[LIBERADO]", dev.Notes);
        Assert.False(string.IsNullOrWhiteSpace(dev.AuthorizedToken));

        var listAfter = svc.LoadLocalDevices();
        Assert.Equal("Active", listAfter[0].Status);
        Assert.Equal(dev.AuthorizedToken, listAfter[0].AuthorizedToken);
    }

    [Fact]
    public void ExtendLicense_OnRevokedDevice_ClearsRevokedAndRestoresActive()
    {
        var svc = new CloudLicenseService(_tempFile);
        var signer = new LicenseSignerService();
        var req = new ActivationRequestData("req", "guid-extend-revoked", "fp-extend-revoked", true, null, "Marcos", "Mendes");
        var lic = signer.GenerateLicense(req, 30, "Marcos Mendes");
        svc.RegisterOrUpdateDevice(req, lic);

        // Revoga
        svc.RevokeDevice("guid-extend-revoked", "Suspenso");
        Assert.Equal("Revoked", svc.LoadLocalDevices()[0].Status);

        // Amplia prazo em +60 dias
        var (ok, msg, updated) = svc.ExtendLicense("guid-extend-revoked", 60, signer);
        Assert.True(ok);
        Assert.NotNull(updated);
        Assert.Equal("Active", updated.Status);
        Assert.Equal(DeviceLicenseStatus.Active, updated.CalculatedStatus);
        Assert.Contains("[REATIVADO/ESTENDIDO]", updated.Notes);
    }

    [Fact]
    public async System.Threading.Tasks.Task VerifyLicenseStartup_BlocksWhenDeviceIsRevoked()
    {
        var svc = new CloudLicenseService(_tempFile);
        svc.ForceLocalOnly = true;
        var signer = new LicenseSignerService();
        var req = new ActivationRequestData("req", "guid-verify-revoked", "fp-verify-revoked", true, null, "João", "Teste");
        var lic = signer.GenerateLicense(req, 30, "João Teste");
        svc.RegisterOrUpdateDevice(req, lic);

        svc.RevokeDevice("guid-verify-revoked", "Revogado por justa causa");

        var (allowed, revoked, newToken, msg) = await svc.VerifyLicenseStartupAsync(
            "guid-verify-revoked",
            "fp-verify-revoked",
            lic.LicenseToken,
            token => true);

        Assert.False(allowed);
        Assert.True(revoked);
        Assert.Contains("revogada", msg, StringComparison.OrdinalIgnoreCase);
    }
}


