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
    [InlineData("hpe.msr")]
    [InlineData("MSR954")]
    [InlineData("954")]
    [InlineData("HP 954")]
    [InlineData("HPE 954")]
    public void FindById_FindsHpeMsrProfile(string idOrName)
    {
        var profile = BootInterruptProfiles.FindById(idOrName);
        Assert.Equal("hpe.msr.ctrl-b", profile.Id);
        Assert.Equal(BootInterruptMethod.CtrlB, profile.Method);
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
}
