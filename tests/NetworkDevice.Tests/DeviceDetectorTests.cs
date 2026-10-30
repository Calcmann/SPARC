using NetworkDevice.Core.Detection;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Session;
using NetworkDevice.Tests.TestDoubles;
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

    [Fact]
    public void ClassifyPrompt_DetectFortiGateLogin_AsPasswordProtected()
    {
        var prompt = "FortiGate-40F login: ";
        var result = _detector.ClassifyPrompt(prompt, DeviceSeries.FortiGate40F);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.PasswordProtected, result.OperatingState);
        Assert.Equal(AccessState.UserAndPasswordRequired, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_DetectFortiGateLoginFailed_AsPasswordProtected()
    {
        var prompt = "FortiGate-40F login: admin\r\nPassword: \r\nVerifying password...\r\n\r\nLogin incorrect\r\nFortiGate-40F login: ";
        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.PasswordProtected, result.OperatingState);
        Assert.Equal(AccessState.UserAndPasswordRequired, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_DetectFortiGateWithoutPrompt_AsPasswordProtected()
    {
        var prompt = "FortiGate-40F\r\nVerifying password...";
        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.PasswordProtected, result.OperatingState);
        Assert.Equal(AccessState.UserAndPasswordRequired, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_DetectFortiGateAuthenticatedPrompt_AsReadyAndOpen()
    {
        var prompt = "FortiGate-40F # ";
        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(AccessState.Open, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_DetectFortiGateFG40FPrompt_AsReadyAndOpen()
    {
        var prompt = "FG40FTK21000000 # ";
        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(AccessState.Open, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_DetectFortiGateDisclaimerBanner_AsReadyAndNotPasswordLocked()
    {
        var prompt = @"PRE WARNING:
This is a private computer system. Unauthorized access or use
is prohibited and subject to prosecution and/or disciplinary
action. Any use of this system constitutes consent to
monitoring at all times and users are not entitled to any
expectation of privacy. If monitoring reveals possible evidence
of violation of criminal statutes, this evidence and any other
related information, including identification information about
the user, may be provided to law enforcement officials.
If monitoring reveals violations of security regulations or
unauthorized use, employees who violate security regulations or
make unauthorized use of this system are subject to appropriate
disciplinary action.
(Press 'a' to accept): ";

        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(AccessState.Open, result.AccessState);
        Assert.False(result.RequiresPasswordOnly);
        Assert.False(result.RequiresUserAndPassword);
    }

    [Fact]
    public void ClassifyPrompt_DetectFortiGateDisclaimerWithHistoricalLogin_AsReadyAndNotPasswordLocked()
    {
        var prompt = @"FortiOS v7.0.0
Previous login: Wed Oct 14 16:00:00
PRE WARNING:
This is a private computer system. Unauthorized access or use
is prohibited and subject to prosecution and/or disciplinary
action. Any use of this system constitutes consent to
monitoring at all times and users are not entitled to any
expectation of privacy. If monitoring reveals possible evidence
of violation of criminal statutes, this evidence and any other
related information, including identification information about
the user, may be provided to law enforcement officials.
If monitoring reveals violations of security regulations or
unauthorized use, employees who violate security regulations or
make unauthorized use of this system are subject to appropriate
disciplinary action.
(Press 'a' to accept):";

        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(AccessState.Open, result.AccessState);
        Assert.False(result.RequiresPasswordOnly);
        Assert.False(result.RequiresUserAndPassword);
    }

    [Theory]
    [InlineData("(Press 'a' to accept):")]
    [InlineData("(Press 'a' to accept): ")]
    [InlineData("Press 'a' to accept")]
    [InlineData("(Press 'a' to accept, 'd' to decline):")]
    [InlineData("PRE WARNING:\r\nSome text\r\n(Press 'a' to accept):")]
    [InlineData("to accept):")]
    [InlineData("to accept): ")]
    [InlineData("to accept:")]
    [InlineData("disciplinary action.\r\n(Press 'a' to accept): ")]
    public void DeviceDetector_IsDisclaimerPrompt_MatchesAllVariants(string input)
    {
        Assert.True(DeviceDetector.IsDisclaimerPrompt(input));
    }

    [Fact]
    public void ClassifyPrompt_DetectFortiGateFG40FLogin_AsPasswordProtected()
    {
        var prompt = "FG40FTK21000000 login: ";
        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.PasswordProtected, result.OperatingState);
        Assert.Equal(AccessState.UserAndPasswordRequired, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_DetectFortiGateVerifyingPassword_AsPasswordProtected()
    {
        var prompt = "FortiGate-40F login: admin\r\nPassword:\r\nVerifying password...\r\n";
        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.PasswordProtected, result.OperatingState);
        Assert.Equal(AccessState.UserAndPasswordRequired, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_DetectFortiGateDisclaimerFollowedByLogin_AsPasswordProtected()
    {
        var prompt = 
            "PRE WARNING:\r\n" +
            "This is a private computer system.\r\n" +
            "(Press 'a' to accept):\r\n" +
            "LDA-IP-02580 login: ";
        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.PasswordProtected, result.OperatingState);
        Assert.Equal(AccessState.UserAndPasswordRequired, result.AccessState);
        Assert.True(result.RequiresUserAndPassword);
    }

    [Fact]
    public void ClassifyPrompt_DetectFortiGateDisclaimerFollowedByLoginIncorrect_AsPasswordProtected()
    {
        var prompt = 
            "PRE WARNING:\r\n" +
            "This is a private computer system.\r\n" +
            "(Press 'a' to accept):\r\n" +
            "LDA-IP-02580 login: admin\r\n" +
            "Password:\r\n" +
            "Login incorrect\r\n";
        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.PasswordProtected, result.OperatingState);
        Assert.Equal(AccessState.UserAndPasswordRequired, result.AccessState);
        Assert.True(result.RequiresUserAndPassword);
    }

    [Fact]
    public void ClassifyPrompt_DetectFortiGateCustomHostnameWithFortiSelected_AsPasswordProtected()
    {
        var prompt = "LDA-IP-02580 login: ";
        var result = _detector.ClassifyPrompt(prompt, DeviceSeries.FortiGate40F);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.PasswordProtected, result.OperatingState);
        Assert.Equal(AccessState.UserAndPasswordRequired, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_HpePrompt_WithFortiGateSelectedInUserSeries_PrioritizesPhysicalHpePrompt()
    {
        var prompt = "<HPE>";
        var result = _detector.ClassifyPrompt(prompt, DeviceSeries.FortiGate40F);

        Assert.Equal(DeviceManufacturer.Hpe, result.Manufacturer);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(AccessState.Open, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_CiscoPrompt_WithFortiGateSelectedInUserSeries_PrioritizesPhysicalCiscoPrompt()
    {
        var prompt = "Router#";
        var result = _detector.ClassifyPrompt(prompt, DeviceSeries.FortiGate40F);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(AccessState.Open, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_CiscoConfigurationProfessionalBanner_DetectsPasswordProtected()
    {
        var prompt = 
            "Cisco Configuration Professional (Cisco CP) is installed on this device.\r\n" +
            "This feature requires the one-time use of the username \"cisco\" with the \r\n" +
            "password \"cisco\". These default credentials have a privilege level of 15.\r\n" +
            "YOU MUST USE CISCO CP or the CISCO IOS CLI TO CHANGE THESE\r\n" +
            "PUBLICLY-KNOWN CREDENTIALS\r\n" +
            " \r\n" +
            "Here are the Cisco IOS commands.\r\n" +
            " \r\n" +
            "username <myuser>  privilege 15 secret 0 <mypassword>\r\n" +
            "no username cisco\r\n" +
            " \r\n" +
            "Replace <myuser> and <mypassword> with the username and password you want \r\n" +
            "to use.\r\n" +
            " \r\n" +
            "Username: ";

        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceOperatingState.PasswordProtected, result.OperatingState);
        Assert.Equal(AccessState.UserAndPasswordRequired, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_Cisco841_DetectsIsr841SeriesFromPrompt()
    {
        var prompt = "C841>";
        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceSeries.Isr841, result.Series);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
    }

    [Fact]
    public void ClassifyPrompt_Cisco841_DetectsIsr841FromShowVersionOutput()
    {
        var output = @"Router>
terminal length 0
show version
Cisco IOS Software, C800M Software (C800M-UNIVERSALK9-M), Version 15.7(3)M9, RELEASE SOFTWARE (fc2)
Technical Support: http://www.cisco.com/techsupport
Copyright (c) 1986-2021 by Cisco Systems, Inc.

Cisco C841M-4X-JSEC/K9 (revision 1.0) with 490496K/33792K bytes of memory.
Processor board ID FGL223523XX
Configuration register is 0x2102
Router>";

        var result = _detector.ClassifyPrompt(output);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceSeries.Isr841, result.Series);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
    }

    [Fact]
    public void ClassifyPrompt_Cisco841_DetectsIsr841FromRommonDirFlash()
    {
        var rommonOutput = @"rommon 1 > dir flash:
Directory of flash:/
1  -rw-  32185208  c841-universalk9-mz.SPA.157-3.M9.bin
rommon 2 > ";

        var result = _detector.ClassifyPrompt(rommonOutput);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceSeries.Isr841, result.Series);
        Assert.Equal(DeviceOperatingState.BootFailure, result.OperatingState);
    }

    [Fact]
    public void ClassifyPrompt_UserAccessVerification_DetectsCiscoAndUserAndPasswordRequired()
    {
        var prompt = "User Access Verification\r\n\r\nUsername: ";
        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceOperatingState.PasswordProtected, result.OperatingState);
        Assert.Equal(AccessState.UserAndPasswordRequired, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_Cisco841_UserAccessVerification_DetectsIsr841Series()
    {
        var prompt = "C841M-ROUTER\r\nUser Access Verification\r\n\r\nUsername: ";
        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceSeries.Isr841, result.Series);
        Assert.Equal(DeviceOperatingState.PasswordProtected, result.OperatingState);
        Assert.Equal(AccessState.UserAndPasswordRequired, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_Cisco841_WithNumbersLike1941InOutput_RemainsIsr841AndDoesNotFallInto1900()
    {
        var output = @"Router>
show version
Cisco IOS Software, C800M Software (C800M-UNIVERSALK9-M), Version 15.6(3)M2, RELEASE SOFTWARE (fc2)
Technical Support: http://www.cisco.com/techsupport
Copyright (c) 1986-2016 by Cisco Systems, Inc.

Cisco C841M-4X-JSEC/K9 (revision 1.0) with 490496K/33792K bytes of memory.
Processor board ID FGL 1941 23XX
Configuration register is 0x2102
Router>";

        var result = _detector.ClassifyPrompt(output);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceSeries.Isr841, result.Series);
        Assert.NotEqual(DeviceSeries.Series1900, result.Series);
    }

    [Fact]
    public void ClassifyPrompt_Cisco1921_DetectsSeries1900()
    {
        var output = @"Router>
show version
Cisco IOS Software, C1900 Software (C1900-UNIVERSALK9-M), Version 15.7(3)M2
Cisco CISCO1921/K9 (revision 1.0) with 491520K/32768K bytes of memory.
Processor board ID FGL12345678
Router>";

        var result = _detector.ClassifyPrompt(output);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceSeries.Series1900, result.Series);
    }

    [Fact]
    public void ClassifyPrompt_Cisco2900_DetectsSeries2900()
    {
        var output = @"Router>
show version
Cisco IOS Software, C2900 Software (C2900-UNIVERSALK9-M), Version 15.7(3)M2
Cisco CISCO2911/K9 (revision 1.0) with 491520K/32768K bytes of memory.
Processor board ID FGL87654321
Router>";

        var result = _detector.ClassifyPrompt(output);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceSeries.Series2900, result.Series);
    }

    [Fact]
    public void ClassifyPrompt_CiscoConfigMode_DetectsCiscoAndReadyOpenState()
    {
        var prompt = "LDA-IP-02580(config)#";
        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(AccessState.Open, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_CiscoConfigSubmode_DetectsCiscoAndReadyOpenState()
    {
        var prompt = "Router(config-if)#";
        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(AccessState.Open, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_CiscoConfigMode_WithShowVersion_EnrichesSeries1900()
    {
        var output = @"LDA-IP-02580(config)#
end
LDA-IP-02580#
show version
Cisco IOS Software, C1900 Software (C1900-UNIVERSALK9-M), Version 15.7(3)M2
Cisco CISCO1905/K9 (revision 1.0) with 491520K/32768K bytes of memory.
Processor board ID FGL12345678
Configuration register is 0x2102
LDA-IP-02580#";

        var result = _detector.ClassifyPrompt(output);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceSeries.Series1900, result.Series);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
    }

    [Fact]
    public void ClassifyPrompt_HpeBootWareMenu_ClassifiesAsHpeAndBootFailure()
    {
        var output = @"
======================<EXTEND ASSISTANT MENU>======================
|<1> Boot System                                                   |
|<2> Enter Extended Assistant Menu                                 |
|<3> Restore Factory Default Configuration                         |
|<4> Enter Serial SubMenu                                          |
|<5> Enter Ethernet SubMenu                                        |
|<6> Format File System                                            |
|<7> Reboot                                                        |
===================================================================
Enter your choice(0-7): ";

        var result = _detector.ClassifyPrompt(output);

        Assert.Equal(DeviceManufacturer.Hpe, result.Manufacturer);
        Assert.Equal(DeviceOperatingState.BootFailure, result.OperatingState);
        Assert.Equal(BootState.Bootware, result.BootState);
        Assert.Equal(AccessState.RommonOrBootware, result.AccessState);
        Assert.NotEqual(DeviceManufacturer.Cisco, result.Manufacturer);
    }

    [Fact]
    public void ClassifyPrompt_HpeBootWare_MissingFirmwareError_ClassifiesAsHpeBootFailure()
    {
        var output = @"
Basic BootWare Version: 1.68
Loading boot image fails: flash:/msr954-cmw710-boot.bin does not exist!
Enter your choice(0-9): ";

        var result = _detector.ClassifyPrompt(output);

        Assert.Equal(DeviceManufacturer.Hpe, result.Manufacturer);
        Assert.Equal(DeviceOperatingState.BootFailure, result.OperatingState);
        Assert.Equal(BootState.Bootware, result.BootState);
    }

    [Fact]
    public void Test_FortiGateBiosOutput_Lines()
    {
        var matcher = RegexPromptMatcher.Universal();
        var lines = new[]
        {
            "Enter C,R,T,F,I,B,Q,or H:",
            "60s time out, rebooting...",
            "resetting ...",
            "FortiGate-40F (03:00-06.02.2023)",
            "Ver:05000100",
            "Serial number: FGT40FTK2409C36Z",
            "CPU: 1200MHz",
            "Total RAM: 2 GB",
            "Initializing boot device...",
            "Initializing MAC... NP6XLITE#0",
            "Initializing MAC... NP6XLITE#",
            "NP6XLITE#",
            "NP6XLITE#0",
            "Please wait for OS to boot, or press any key to display configuration menu......",
            "Booting OS...",
            "Initializing firewall..."
        };

        foreach (var line in lines)
        {
            var match = matcher.TryMatch(line);
            Assert.True(match == null, $"Line '{line}' matched prompt unexpectedly: '{match?.Prompt}'");
        }
    }

    [Fact]
    public async Task Test_WaitForAsync_DoesNotMatchBiosOutputAsPrompt()
    {
        var biosStream = 
            "Enter C,R,T,F,I,B,Q,or H:\r\n" +
            "60s time out, rebooting... resetting ...\r\n" +
            "FortiGate-40F (03:00-06.02.2023)\r\n" +
            "Initializing boot device...\r\n" +
            "Initializing MAC... NP6XLITE#0\r\n" +
            "Please wait for OS to boot, or press any key to display configuration menu......\r\n" +
            "Booting OS... Initializing firewall...\r\n" +
            "FortiGate-40F login: ";

        var transport = new ScriptedTransport(
            _ => string.Empty,
            initialOutput: biosStream);

        await using var session = new DeviceSession(transport, new SessionOptions
        {
            PromptMatcher = RegexPromptMatcher.Universal(),
            ConnectTimeout = TimeSpan.FromSeconds(2),
            CommandTimeout = TimeSpan.FromSeconds(2)
        });

        await session.ConnectRawAsync();

        var bootCheck = await session.WaitForAsync(
            new StopCondition[]
            {
                new StopCondition.Prompt(),
                new StopCondition.Contains("LoginPrompt", "login:"),
            },
            TimeSpan.FromSeconds(2));

        Assert.NotNull(bootCheck.Matched);
        Assert.IsType<StopCondition.Contains>(bootCheck.Matched);
        Assert.Equal("LoginPrompt", ((StopCondition.Contains)bootCheck.Matched).Name);
    }

    [Theory]
    [InlineData("FortiGate-40F (console) # ")]
    [InlineData("FortiGate-40F (console) #")]
    [InlineData("FortiGate-40F (root) #")]
    [InlineData("FortiGate-40F (global) #")]
    [InlineData("FortiGate-40F (interface) #")]
    [InlineData("FortiGate-40F #")]
    [InlineData("FortiGate-40F $")]
    public void ClassifyPrompt_FortiGateShellPrompts_ClassifiesAsReadyAndOpen(string prompt)
    {
        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(AccessState.Open, result.AccessState);
        Assert.False(result.RequiresPasswordOnly);
        Assert.False(result.RequiresUserAndPassword);
    }

    [Theory]
    [InlineData("FortiGate-40F (console) #", ExecMode.PrivilegedExec)]
    [InlineData("FortiGate-40F (global) #", ExecMode.PrivilegedExec)]
    [InlineData("FortiGate-40F (interface) #", ExecMode.PrivilegedExec)]
    [InlineData("FortiGate-40F #", ExecMode.PrivilegedExec)]
    [InlineData("FortiGate-40F (console) $", ExecMode.UserExec)]
    [InlineData("FortiGate-40F $", ExecMode.UserExec)]
    public void UniversalPromptMatcher_MatchesFortiOSPromptsWithSubmodes(string prompt, ExecMode expectedMode)
    {
        var matcher = RegexPromptMatcher.Universal();
        var match = matcher.TryMatch(prompt);

        Assert.NotNull(match);
        Assert.Equal(expectedMode, match.Mode);
    }

    [Fact]
    public void ClassifyPrompt_Cisco1111_DetectsIsr1100FromPrompt()
    {
        var prompt = "C1111#";
        var result = _detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceSeries.Isr1100, result.Series);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(AccessState.Open, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_Cisco1111_DetectsIsr1100FromShowVersion()
    {
        var output = @"Router#show version
Cisco IOS XE Software, Version 17.06.01a
Cisco IOS Software [Cupertino], C1100 Software (ARM64_LINUX_IOSD-UNIVERSALK9-M), Version 17.6.1a, RELEASE SOFTWARE (fc3)
Technical Support: http://www.cisco.com/techsupport
Copyright (c) 1986-2021 by Cisco Systems, Inc.

cisco C1111-4P (1RU) processor with 1795791K/6147K bytes of memory.
Processor board ID FGL23450000
1 Gigabit Ethernet interface
4 FastEthernet interfaces
Configuration register is 0x2102
Router#";

        var result = _detector.ClassifyPrompt(output);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceSeries.Isr1100, result.Series);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
    }
}