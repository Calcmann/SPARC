using NetworkDevice.Core.Recovery;
using NetworkDevice.Core.Session;
using NetworkDevice.Tests.TestDoubles;
using Xunit;

namespace NetworkDevice.Tests;

public sealed class HpeBootWareTests
{
    private static DeviceSession CreateSession()
    {
        var transport = new ScriptedTransport(_ => "");
        return new DeviceSession(transport, new SessionOptions());
    }

    [Theory]
    [InlineData("hpe.msr954")]
    [InlineData("MSR954")]
    [InlineData("954")]
    [InlineData("HP 954")]
    [InlineData("HPE 954")]
    public void FindById_FindsHpeMsrProfile(string idOrName)
    {
        var profile = BootInterruptProfiles.FindById(idOrName);
        Assert.Equal("hpe.msr954.ctrl-b", profile.Id);
        Assert.Equal(BootInterruptMethod.CtrlB, profile.Method);
    }

    [Theory]
    [InlineData("hpe.msr930")]
    [InlineData("MSR930")]
    [InlineData("930")]
    [InlineData("HP 930")]
    [InlineData("HPE 930")]
    public void FindById_FindsHpeMsr930Profile(string idOrName)
    {
        var profile = BootInterruptProfiles.FindById(idOrName);
        Assert.Equal("hpe.msr930.ctrl-b", profile.Id);
        Assert.Equal(BootInterruptMethod.CtrlB, profile.Method);
    }

    [Theory]
    [InlineData("hpe.msr1002")]
    [InlineData("MSR1002")]
    [InlineData("1002")]
    [InlineData("HP 1002")]
    [InlineData("HPE 1002")]
    [InlineData("MSR1003")]
    [InlineData("MSR1000")]
    public void FindById_FindsHpeMsr1002Profile(string idOrName)
    {
        var profile = BootInterruptProfiles.FindById(idOrName);
        Assert.Equal("hpe.msr1002.ctrl-b", profile.Id);
        Assert.Equal(BootInterruptMethod.CtrlB, profile.Method);
    }

    [Theory]
    [InlineData("cisco.c841.break")]
    [InlineData("cisco.c841.ctrl-c")]
    [InlineData("C841")]
    [InlineData("C841M")]
    [InlineData("841")]
    [InlineData("C800M")]
    public void FindById_FindsCisco841Profile(string idOrName)
    {
        var profile = BootInterruptProfiles.FindById(idOrName);
        Assert.Equal("cisco.c841.break", profile.Id);
        Assert.Equal(BootInterruptMethod.Dual, profile.Method);
    }

    [Fact]
    public void ClassifyLogin_DetectsBootWareCountdown()
    {
        var session = CreateSession();
        var buffer = "Press Ctrl+B to enter Extended BootWare...\r\n";
        var kind = session.ClassifyLogin(buffer);
        Assert.Equal(DeviceSession.LoginStageKind.BootWareCountdown, kind);
    }

    [Fact]
    public void ClassifyLogin_DetectsBootWareMainMenu()
    {
        var session = CreateSession();
        var buffer = "Enter your choice(0-9): ";
        var kind = session.ClassifyLogin(buffer);
        Assert.Equal(DeviceSession.LoginStageKind.BootWareMenu, kind);
    }

    [Fact]
    public void ClassifyLogin_DetectsBootWareEthernetSubMenu()
    {
        var session = CreateSession();
        var buffer = "Enter your choice(0-5): ";
        var kind = session.ClassifyLogin(buffer);
        Assert.Equal(DeviceSession.LoginStageKind.BootWareMenu, kind);
    }

    [Fact]
    public void ClassifyLogin_DetectsMissingFirmwareMessage()
    {
        var session = CreateSession();
        var buffer = "The image does not exist!\r\nLoading boot image fails.\r\nEnter your choice(0-9): ";
        var kind = session.ClassifyLogin(buffer);
        Assert.Equal(DeviceSession.LoginStageKind.BootWareMenu, kind);
    }

    [Fact]
    public void ClassifyPrompt_DoesNotClassifyComwareStartupBannerAsBootware()
    {
        var detector = new NetworkDevice.Core.Detection.DeviceDetector();
        var banner = "******************************************************************************\r\n" +
                     "* Copyright (c) 2010-2025 Hewlett Packard Enterprise Development LP          *\r\n" +
                     "* Without the owner's prior written consent,                                 *\r\n" +
                     "* no decompiling or reverse-engineering shall be allowed.                    *\r\n" +
                     "******************************************************************************\r\n\r\n" +
                     "Line con0 is available.\r\n\r\n\r\n" +
                     "Press ENTER to get started.\r\n";

        var result = detector.ClassifyPrompt(banner);
        Assert.Equal(NetworkDevice.Core.Domain.DeviceManufacturer.Hpe, result.Manufacturer);
        Assert.NotEqual(NetworkDevice.Core.Domain.DeviceOperatingState.BootFailure, result.OperatingState);
        Assert.Equal(NetworkDevice.Core.Domain.DeviceOperatingState.Ready, result.OperatingState);
    }

    [Theory]
    [InlineData("251904 KB total (0 KB free)", 0)]
    [InlineData("251904 KB total (32 KB free)", 32)]
    [InlineData("251904 KB total (133120 KB free)", 133120)]
    [InlineData("[251904 KB total (65536 KB free)]", 65536)]
    public void FreeSpaceRegex_MatchesBothBracketedAndUnbracketedDirOutput(string line, int expectedFreeKb)
    {
        var regex = new System.Text.RegularExpressions.Regex(
            @"(?:\[|\b)?\s*(?<total>\d+)\s*KB\s+total\s*\(\s*(?<free>\d+)\s*KB\s+free\s*\)(?:\]|\b)?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var match = regex.Match(line);
        Assert.True(match.Success);
        Assert.Equal(expectedFreeKb, int.Parse(match.Groups["free"].Value));
    }

    [Theory]
    [InlineData("MSR954-CMW710-R6749P43.ipe", "R6749P43")]
    [InlineData("MSR954-CMW710-R0605P20.IPE", "R0605P20")]
    [InlineData("msr954-cmw710-r605p20.ipe", "R605P20")]
    [InlineData("msr954-cmw710-boot-r6749p43.bin", "R6749P43")]
    public void VersionExtraction_ExtractsTargetVersionTag(string fileName, string expectedTag)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            fileName,
            @"(?i)-(?<ver>R\d+(?:P\d+)?)(?:\.ipe|\.bin|$)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        Assert.True(match.Success);
        Assert.Equal(expectedTag.ToUpperInvariant(), match.Groups["ver"].Value.ToUpperInvariant());
    }
}

