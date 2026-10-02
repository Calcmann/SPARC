using System;
using System.IO;
using System.Text;
using NetworkDevice.Core.Licensing;
using Xunit;

namespace NetworkDevice.Tests;

public class LicenseSignerServiceTests
{
    private static string SampleRequest()
    {
        var json = "{\"g\":\"guid-12345-abcde\",\"f\":\"fingerprint-67890-xyz\"}";
        var b64u = LicenseSignerService.Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        return "SPBREQ." + b64u;
    }

    [Fact]
    public void DecodeRequest_ValidRequest_ReturnsExtractedFields()
    {
        var svc = new LicenseSignerService(@"C:\SPARC\beta");
        var reqStr = SampleRequest();

        var decoded = svc.DecodeRequest(reqStr);

        Assert.True(decoded.IsValid);
        Assert.Equal("guid-12345-abcde", decoded.MachineGuid);
        Assert.Equal("fingerprint-67890-xyz", decoded.MachineFingerprint);
        Assert.Null(decoded.ErrorMessage);
    }

    [Fact]
    public void DecodeRequest_InvalidPrefix_ReturnsError()
    {
        var svc = new LicenseSignerService(@"C:\SPARC\beta");
        var decoded = svc.DecodeRequest("INVALID_PREFIX.123");

        Assert.False(decoded.IsValid);
        Assert.NotNull(decoded.ErrorMessage);
    }

    [Fact]
    public void GenerateAndVerifyLicense_WithRealKeys_Succeeds()
    {
        var svc = new LicenseSignerService(@"C:\SPARC\beta");
        if (!svc.HasPrivateKey)
        {
            // Pula se estiver em ambiente sem a chave
            return;
        }

        var reqStr = SampleRequest();
        var decoded = svc.DecodeRequest(reqStr);

        var licenseResult = svc.GenerateLicense(decoded, 30, "Tecnico Teste", "Homologacao SPARC");

        Assert.True(licenseResult.Success);
        Assert.StartsWith("SPB1.", licenseResult.LicenseToken);
        Assert.Equal(30, licenseResult.ValidDays);

        var verifyOk = svc.VerifyLicense(licenseResult.LicenseToken, out var payload);
        Assert.True(verifyOk);
        Assert.NotNull(payload);
        Assert.Contains("guid-12345-abcde", payload);
    }

    [Fact]
    public void DecodeRequest_WithTechnicianProfile_ExtractsAllFields()
    {
        var json = "{\"g\":\"guid-999\",\"f\":\"fp-999\",\"n\":\"Carlos\",\"s\":\"Eduardo\",\"p\":\"11999998888\",\"e\":\"carlos@claro.com.br\",\"v\":\"0.8.2\",\"t\":\"2026-09-30T12:00:00Z\"}";
        var b64u = LicenseSignerService.Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        var reqStr = "SPBREQ." + b64u;

        var svc = new LicenseSignerService(@"C:\SPARC\beta");
        var decoded = svc.DecodeRequest(reqStr);

        Assert.True(decoded.IsValid);
        Assert.Equal("guid-999", decoded.MachineGuid);
        Assert.Equal("fp-999", decoded.MachineFingerprint);
        Assert.Equal("Carlos", decoded.FirstName);
        Assert.Equal("Eduardo", decoded.LastName);
        Assert.Equal("Carlos Eduardo", decoded.FullName);
        Assert.Equal("(11) 99999-8888", decoded.Phone);
        Assert.Equal("carlos@claro.com.br", decoded.Email);
        Assert.Equal("0.8.2", decoded.ClientVersion);
        Assert.NotNull(decoded.RequestTimeUtc);
    }

    [Fact]
    public void DecodeRequest_WithClusterAndUf_ExtractsCorrectly()
    {
        var json = "{\"g\":\"guid-cluster-test\",\"f\":\"fp-cluster-test\",\"n\":\"Roberto\",\"s\":\"Silva\",\"p\":\"19988776655\",\"e\":\"roberto@empresa.com\",\"c\":\"Campinas Interior\",\"u\":\"SP\",\"v\":\"0.8.2\",\"t\":\"2026-09-30T12:00:00Z\"}";
        var b64u = LicenseSignerService.Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        var reqStr = "SPBREQ." + b64u;

        var svc = new LicenseSignerService(@"C:\SPARC\beta");
        var decoded = svc.DecodeRequest(reqStr);

        Assert.True(decoded.IsValid);
        Assert.Equal("Roberto", decoded.FirstName);
        Assert.Equal("Silva", decoded.LastName);
        Assert.Equal("Campinas Interior", decoded.Cluster);
        Assert.Equal("SP", decoded.Uf);
        Assert.Equal("(19) 98877-6655", decoded.Phone);
        Assert.Equal("roberto@empresa.com", decoded.Email);
    }

    [Fact]
    public void DeleteHistoryItem_And_ClearHistory_ModifiesHistoryCorrectly()
    {
        var tempBeta = Path.Combine(Path.GetTempPath(), "sparc_lic_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempBeta);
        try
        {
            var svc = new LicenseSignerService(tempBeta);
            var req = new ActivationRequestData("req", "guid-del-1", "fp-del-1", true, null, "Lucas", "Teste");
            
            // Simula adição manual ao histórico através de SaveToHistory (gerando licença dummy)
            var histFile = Path.Combine(tempBeta, "historico_chaves.json");
            File.WriteAllText(histFile, "[{\"LicenseToken\":\"SPB1.token1\",\"TechnicianName\":\"Lucas\"},{\"LicenseToken\":\"SPB1.token2\",\"TechnicianName\":\"Outro\"}]");

            var loaded = svc.LoadHistory();
            Assert.Equal(2, loaded.Count);

            var deleted = svc.DeleteHistoryItem("SPB1.token1");
            Assert.True(deleted);

            loaded = svc.LoadHistory();
            Assert.Single(loaded);
            Assert.Equal("SPB1.token2", loaded[0].LicenseToken);

            var cleared = svc.ClearHistory();
            Assert.True(cleared);

            loaded = svc.LoadHistory();
            Assert.Empty(loaded);
        }
        finally
        {
            try { Directory.Delete(tempBeta, true); } catch { }
        }
    }

    [Fact]
    public void DecodeRequest_WithAndroidPlatform_ExtractsPlatformAndPlatformBadgeCorrectly()
    {
        var tempBeta = Path.Combine(Path.GetTempPath(), "sparc_lic_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempBeta);
        try
        {
            var svc = new LicenseSignerService(tempBeta);
            var reqStr = SparcLicenseValidator.BuildRequest(
                "and-guid-123",
                "android-fp-abc",
                "Carlos",
                "Ferreira",
                "Grande Rio",
                "RJ",
                "2199998888",
                "carlos@telecom.com",
                platform: "Android");

            var decoded = svc.DecodeRequest(reqStr);

            Assert.True(decoded.IsValid);
            Assert.Equal("Android", decoded.Platform);
            Assert.Equal("📱 Android", decoded.PlatformBadge);
            Assert.Equal("Carlos", decoded.FirstName);
            Assert.Equal("Ferreira", decoded.LastName);
        }
        finally
        {
            try { Directory.Delete(tempBeta, true); } catch { }
        }
    }

    [Fact]
    public void DecodeRequest_WithWindowsPlatform_ExtractsPlatformAndPlatformBadgeCorrectly()
    {
        var tempBeta = Path.Combine(Path.GetTempPath(), "sparc_lic_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempBeta);
        try
        {
            var svc = new LicenseSignerService(tempBeta);
            var reqStr = SparcLicenseValidator.BuildRequest(
                "win-guid-456",
                "win-fp-xyz",
                "Mariana",
                "Santos",
                "Campinas",
                "SP",
                "19988887777",
                "mariana@telecom.com",
                platform: "Windows");

            var decoded = svc.DecodeRequest(reqStr);

            Assert.True(decoded.IsValid);
            Assert.Equal("Windows", decoded.Platform);
            Assert.Equal("🪟 Windows", decoded.PlatformBadge);
        }
        finally
        {
            try { Directory.Delete(tempBeta, true); } catch { }
        }
    }
}
