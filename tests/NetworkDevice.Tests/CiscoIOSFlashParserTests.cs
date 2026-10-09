using NetworkDevice.Cisco;
using Xunit;

namespace NetworkDevice.Tests;

public class CiscoIOSFlashParserTests
{
    private const string UserRealFlashLog = @"
LDA-IP-02580#sh flash:
-#- --length-- -----date/time------ path
1     85054748 Nov 30 1983 01:00:00 +01:00 c1900-universalk9-mz.SPA.157-3.M9.bin
2     85053880 Oct 9 2026 13:28:54 +01:00 c1900-universalk9-mz.SPA.157-3.M7.bin
3           34 Oct 9 2026 14:08:50 +01:00 pnp-tech-time
4        48789 Oct 9 2026 14:09:04 +01:00 pnp-tech-discovery-summary

85368832 bytes available (170168320 bytes used)

LDA-IP-02580#";

    [Fact]
    public void ExtractFirmwareFiles_ExtraiApenasImagensDeFirmwareValidas()
    {
        var files = CiscoIOSFlashParser.ExtractFirmwareFiles(UserRealFlashLog);

        Assert.Equal(2, files.Count);
        Assert.Contains("c1900-universalk9-mz.SPA.157-3.M9.bin", files);
        Assert.Contains("c1900-universalk9-mz.SPA.157-3.M7.bin", files);
        Assert.DoesNotContain("pnp-tech-time", files);
        Assert.DoesNotContain("pnp-tech-discovery-summary", files);
    }

    [Fact]
    public void ParseAvailableBytes_ExtraiBytesDisponiveisCorretamente()
    {
        var bytes = CiscoIOSFlashParser.ParseAvailableBytes(UserRealFlashLog);
        Assert.Equal(85368832L, bytes);
    }

    [Fact]
    public void GetConflictingFirmwareFiles_IdentificaVersoesConcorrentesParaAlvoM7()
    {
        var target = "c1900-universalk9-mz.SPA.157-3.M7.bin";
        var conflicting = CiscoIOSFlashParser.GetConflictingFirmwareFiles(UserRealFlashLog, target);

        Assert.Single(conflicting);
        Assert.Equal("c1900-universalk9-mz.SPA.157-3.M9.bin", conflicting[0]);
    }

    [Theory]
    [InlineData("System image file is \"usbflash0:c1900-universalk9-mz.SPA.157-3.M9.bin\"", "usbflash0:")]
    [InlineData("249840K bytes of USB Flash usbflash0 (Read/Write)", "usbflash0:")]
    [InlineData("Directory of flash0:/", "flash0:")]
    [InlineData("Directory of flash:/", "flash:")]
    public void DetectFlashFilesystemPrefix_DetectaCorretamente(string output, string expectedPrefix)
    {
        var prefix = CiscoIOSFlashParser.DetectFlashFilesystemPrefix(output);
        Assert.Equal(expectedPrefix, prefix);
    }
}
