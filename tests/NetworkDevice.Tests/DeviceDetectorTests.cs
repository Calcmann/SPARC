using NetworkDevice.Core.Detection;
using NetworkDevice.Core.Domain;
using Xunit;

namespace NetworkDevice.Tests;

public sealed class DeviceDetectorTests
{
    private static readonly DeviceDetector _detector = new();

    [Fact]
    public void ClassifyPrompt_DetectOpenHPEPrompt_WhenOnlyPromptPresent()
    {
        var result = _detector.ClassifyPrompt("<HPE>", DeviceSeries.Msr930);

        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(AccessState.Open, result.AccessState);
        Assert.False(result.RequiresUserAndPassword);
        Assert.False(result.RequiresPasswordOnly);
    }

    [Fact]
    public void ClassifyPrompt_DetectOpenHPEPrompt_WhenDisplayVersionOutputAppended()
    {
        var prompt = @"<HPE>
HPE Comware Platform Software
Comware Software, Version 5.20.106, Release 2514P14
Copyright (c) 2010-2015 Hewlett Packard Enterprise Development LP
HPE MSR931 uptime is 0 week, 0 day, 16 hours, 0 minute
CPU type: FREESCALE P1016 533MHz
256M bytes DDR3 SDRAM Memory
128M bytes Flash Memory
[SLOT  0]GE0/0          (Hardware)3.0,	(Driver)1.0,	(Cpld)1.0
[SLOT  0]CELLULAR0/0    (Hardware)3.0,	(Driver)1.0,	(Cpld)1.0";

        var result = _detector.ClassifyPrompt(prompt, DeviceSeries.Msr930);

        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(AccessState.Open, result.AccessState);
        Assert.False(result.RequiresUserAndPassword, "Dispositivo aberto com <HPE> não deve ser classificado como UserAndPasswordRequired");
        Assert.False(result.RequiresPasswordOnly);
    }

    [Fact]
    public void ClassifyPrompt_DetectUserAndPassword_WhenUsernamePromptPresent()
    {
        var prompt = @"Username:
Password:";

        var result = _detector.ClassifyPrompt(prompt, DeviceSeries.Msr930);

        Assert.Equal(DeviceOperatingState.PasswordProtected, result.OperatingState);
        Assert.Equal(AccessState.UserAndPasswordRequired, result.AccessState);
        Assert.True(result.RequiresUserAndPassword);
    }

    [Fact]
    public void ClassifyPrompt_DetectPasswordOnly_WhenPasswordPromptPresent()
    {
        var prompt = @"Password:";

        var result = _detector.ClassifyPrompt(prompt, DeviceSeries.Msr930);

        Assert.Equal(DeviceOperatingState.PasswordProtected, result.OperatingState);
        Assert.Equal(AccessState.PasswordRequired, result.AccessState);
        Assert.True(result.RequiresPasswordOnly);
    }

    [Fact]
    public void ClassifyPrompt_DetectOpenHPEBracketPrompt()
    {
        var result = _detector.ClassifyPrompt("[HPE]", DeviceSeries.Msr930);

        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(AccessState.Open, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_DetectOpenPromptAfterDisplayVersion()
    {
        var prompt = @"<HPE>
screen-length disable
% Screen-length configuration is disabled for current user.
<HPE>display version
HPE Comware Platform Software
Comware Software, Version 5.20.106, Release 2514P14
HPE MSR931 uptime is 0 week, 0 day, 16 hours, 0 minute
[SLOT  0]GE0/0          (Hardware)3.0";

        var result = _detector.ClassifyPrompt(prompt, DeviceSeries.Msr930);

        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(AccessState.Open, result.AccessState);
        Assert.False(result.RequiresUserAndPassword);
    }
}