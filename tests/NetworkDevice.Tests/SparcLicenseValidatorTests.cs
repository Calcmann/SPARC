using System;
using NetworkDevice.Core.Licensing;
using Xunit;

namespace NetworkDevice.Tests;

public class SparcLicenseValidatorTests
{
    [Fact]
    public void BuildRequest_GeneratesValidSpbReqToken()
    {
        var req = SparcLicenseValidator.BuildRequest(
            "guid-12345",
            "fingerprint-67890",
            "Carlos",
            "Silva",
            "11999998888",
            "carlos@claro.com.br",
            "SP-INTERIOR",
            "SP",
            "0.8.30");

        Assert.NotNull(req);
        Assert.StartsWith("SPBREQ.", req);
        Assert.True(req.Length > 20);
    }

    [Fact]
    public void TryValidate_RejectsEmptyOrInvalidTokens()
    {
        var okEmpty = SparcLicenseValidator.TryValidate("", out var infoEmpty);
        Assert.False(okEmpty);
        Assert.Null(infoEmpty);

        var okInvalid = SparcLicenseValidator.TryValidate("INVALID.TOKEN.HERE", out var infoInvalid);
        Assert.False(okInvalid);
        Assert.Null(infoInvalid);
    }

    [Fact]
    public void IsAuthorized_ValidatesMachineAndExpiration()
    {
        var expires = DateTime.UtcNow.AddDays(15);
        var license = new SparcValidatedLicense("machine-1", "fingerprint-1", expires);

        var ok = SparcLicenseValidator.IsAuthorized(license, "machine-1", "fingerprint-1", DateTime.UtcNow, out var reason);
        Assert.True(ok);
        Assert.Equal("", reason);

        // Dispositivo diferente
        var okWrong = SparcLicenseValidator.IsAuthorized(license, "machine-OTHER", "fingerprint-OTHER", DateTime.UtcNow, out var reasonWrong);
        Assert.False(okWrong);
        Assert.Contains("outro dispositivo", reasonWrong);

        // Expirado
        var okExpired = SparcLicenseValidator.IsAuthorized(license, "machine-1", "fingerprint-1", DateTime.UtcNow.AddDays(20), out var reasonExpired);
        Assert.False(okExpired);
        Assert.Contains("expirada", reasonExpired);
    }
}
