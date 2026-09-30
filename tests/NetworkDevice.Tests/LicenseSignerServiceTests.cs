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
        Assert.Equal("11999998888", decoded.Phone);
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
        Assert.Equal("19988776655", decoded.Phone);
        Assert.Equal("roberto@empresa.com", decoded.Email);
    }
}
