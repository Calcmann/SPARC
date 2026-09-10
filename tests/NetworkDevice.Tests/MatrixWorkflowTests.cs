using NetworkDevice.Core.Detection;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Routing;
using Xunit;

namespace NetworkDevice.Tests;

public class MatrixWorkflowTests
{
    [Theory]
    // HPE MSR954 (3 Estados)
    [InlineData(DeviceManufacturer.Hpe, DeviceSeries.Msr954, DeviceOperatingState.Ready, WorkflowType.Provisioning)]
    [InlineData(DeviceManufacturer.Hpe, DeviceSeries.Msr954, DeviceOperatingState.PasswordProtected, WorkflowType.PasswordRecovery)]
    [InlineData(DeviceManufacturer.Hpe, DeviceSeries.Msr954, DeviceOperatingState.BootFailure, WorkflowType.FirmwareRecovery)]
    // HPE MSR930 (3 Estados)
    [InlineData(DeviceManufacturer.Hpe, DeviceSeries.Msr930, DeviceOperatingState.Ready, WorkflowType.Provisioning)]
    [InlineData(DeviceManufacturer.Hpe, DeviceSeries.Msr930, DeviceOperatingState.PasswordProtected, WorkflowType.PasswordRecovery)]
    [InlineData(DeviceManufacturer.Hpe, DeviceSeries.Msr930, DeviceOperatingState.BootFailure, WorkflowType.FirmwareRecovery)]
    // HPE MSR1002 (3 Estados)
    [InlineData(DeviceManufacturer.Hpe, DeviceSeries.Msr1002, DeviceOperatingState.Ready, WorkflowType.Provisioning)]
    [InlineData(DeviceManufacturer.Hpe, DeviceSeries.Msr1002, DeviceOperatingState.PasswordProtected, WorkflowType.PasswordRecovery)]
    [InlineData(DeviceManufacturer.Hpe, DeviceSeries.Msr1002, DeviceOperatingState.BootFailure, WorkflowType.FirmwareRecovery)]
    // Cisco 1900 (3 Estados)
    [InlineData(DeviceManufacturer.Cisco, DeviceSeries.Series1900, DeviceOperatingState.Ready, WorkflowType.Provisioning)]
    [InlineData(DeviceManufacturer.Cisco, DeviceSeries.Series1900, DeviceOperatingState.PasswordProtected, WorkflowType.PasswordRecovery)]
    [InlineData(DeviceManufacturer.Cisco, DeviceSeries.Series1900, DeviceOperatingState.BootFailure, WorkflowType.FirmwareRecovery)]
    // Cisco 921 (3 Estados)
    [InlineData(DeviceManufacturer.Cisco, DeviceSeries.Isr921, DeviceOperatingState.Ready, WorkflowType.Provisioning)]
    [InlineData(DeviceManufacturer.Cisco, DeviceSeries.Isr921, DeviceOperatingState.PasswordProtected, WorkflowType.PasswordRecovery)]
    [InlineData(DeviceManufacturer.Cisco, DeviceSeries.Isr921, DeviceOperatingState.BootFailure, WorkflowType.FirmwareRecovery)]
    public void WorkflowRouter_ResolvesAll9MatrixCellsCorrectly(
        DeviceManufacturer manufacturer,
        DeviceSeries series,
        DeviceOperatingState state,
        WorkflowType expectedWorkflow)
    {
        var resolved = WorkflowRouter.ResolveWorkflow(manufacturer, series, state);
        Assert.Equal(expectedWorkflow, resolved);

        var desc = WorkflowRouter.GetWorkflowDescription(manufacturer, series, resolved);
        Assert.False(string.IsNullOrWhiteSpace(desc));
    }

    [Fact]
    public void DeviceDetector_ClassifiesHpeReadyState()
    {
        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt("<HPE>");

        Assert.Equal(DeviceManufacturer.Hpe, result.Manufacturer);
        Assert.Equal(DeviceSeries.Unknown, result.Series);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(WorkflowType.Provisioning, result.RecommendedWorkflow);
        Assert.Equal(AccessState.Open, result.AccessState);
    }

    [Fact]
    public void DeviceDetector_ClassifiesHpe1002ReadyState()
    {
        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt("HPE MSR1002-4\r\n<HPE>");

        Assert.Equal(DeviceManufacturer.Hpe, result.Manufacturer);
        Assert.Equal(DeviceSeries.Msr1002, result.Series);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(WorkflowType.Provisioning, result.RecommendedWorkflow);
        Assert.Equal(AccessState.Open, result.AccessState);
    }

    [Fact]
    public void DeviceDetector_ClassifiesHpePasswordOnlyProtectedState()
    {
        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt("Password: ", DeviceSeries.Msr954);

        Assert.Equal(DeviceManufacturer.Hpe, result.Manufacturer);
        Assert.Equal(DeviceSeries.Msr954, result.Series);
        Assert.Equal(DeviceOperatingState.PasswordProtected, result.OperatingState);
        Assert.Equal(WorkflowType.PasswordRecovery, result.RecommendedWorkflow);
        Assert.Equal(AccessState.PasswordRequired, result.AccessState);
        Assert.True(result.RequiresPasswordOnly);
        Assert.False(result.RequiresUserAndPassword);
    }

    [Theory]
    [InlineData("login: ")]
    [InlineData("Username: ")]
    [InlineData("\r\n************************************************\r\nlogin: ")]
    public void DeviceDetector_ClassifiesHpeUserAndPasswordProtectedState(string prompt)
    {
        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt(prompt, DeviceSeries.Msr954);

        Assert.Equal(DeviceManufacturer.Hpe, result.Manufacturer);
        Assert.Equal(DeviceSeries.Msr954, result.Series);
        Assert.Equal(DeviceOperatingState.PasswordProtected, result.OperatingState);
        Assert.Equal(WorkflowType.PasswordRecovery, result.RecommendedWorkflow);
        Assert.Equal(AccessState.UserAndPasswordRequired, result.AccessState);
        Assert.True(result.RequiresUserAndPassword);
        Assert.False(result.RequiresPasswordOnly);
    }

    [Fact]
    public void DeviceDetector_ClassifiesHpeBootWareFailureState()
    {
        var detector = new DeviceDetector();
        var prompt = "==========================<EXTENDED-BOOTWARE MENU>==========================\n|<1> Boot System\nchoice(0-9):";
        var result = detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Hpe, result.Manufacturer);
        Assert.Equal(DeviceSeries.Unknown, result.Series);
        Assert.Equal(DeviceOperatingState.BootFailure, result.OperatingState);
        Assert.Equal(WorkflowType.FirmwareRecovery, result.RecommendedWorkflow);
        Assert.Equal(AccessState.RommonOrBootware, result.AccessState);
    }

    [Fact]
    public void DeviceDetector_ClassifiesHpe1002BootWareFailureState()
    {
        var detector = new DeviceDetector();
        var prompt = "HPE MSR1002-4 BootWare, Version 2.74\n==========================<EXTENDED-BOOTWARE MENU>==========================\n|<1> Boot System\nchoice(0-9):";
        var result = detector.ClassifyPrompt(prompt);

        Assert.Equal(DeviceManufacturer.Hpe, result.Manufacturer);
        Assert.Equal(DeviceSeries.Msr1002, result.Series);
        Assert.Equal(DeviceOperatingState.BootFailure, result.OperatingState);
        Assert.Equal(WorkflowType.FirmwareRecovery, result.RecommendedWorkflow);
        Assert.Equal(AccessState.RommonOrBootware, result.AccessState);
    }

    [Fact]
    public void DeviceDetector_ClassifiesCisco1900ReadyState()
    {
        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt("Router#", DeviceSeries.Series1900);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceSeries.Series1900, result.Series);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(WorkflowType.Provisioning, result.RecommendedWorkflow);
        Assert.Equal(AccessState.Open, result.AccessState);
    }

    [Fact]
    public void DeviceDetector_ClassifiesCisco921PasswordState()
    {
        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt("User Access Verification\nPassword: ", DeviceSeries.Isr921);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceSeries.Isr921, result.Series);
        Assert.Equal(DeviceOperatingState.PasswordProtected, result.OperatingState);
        Assert.Equal(WorkflowType.PasswordRecovery, result.RecommendedWorkflow);
    }

    [Fact]
    public void DeviceDetector_ClassifiesCiscoRommonState()
    {
        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt("rommon 1 > ", DeviceSeries.Isr921);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceSeries.Isr921, result.Series);
        Assert.Equal(DeviceOperatingState.BootFailure, result.OperatingState);
        Assert.Equal(WorkflowType.FirmwareRecovery, result.RecommendedWorkflow);
        Assert.Equal(AccessState.RommonOrBootware, result.AccessState);
    }

    [Theory]
    [InlineData("Cisco CISCO1905/K9 (revision 1.0) with 487424K/36864K bytes of memory.\nRouter>")]
    [InlineData("CISCO1905BR/K9 platform with 524288 Kbytes of main memory\nRouter#")]
    [InlineData("System Bootstrap, Version 15.0(1r)M16, RELEASE SOFTWARE (fc1)\nTechnical Support: http://www.cisco.com/techsupport\nCopyright (c) 2012 by cisco Systems, Inc.\n\nTotal memory size = 512 MB")]
    [InlineData("Cisco IOS Software, C1900 Software (C1900-UNIVERSALK9-M), Version 15.2(4)M5\nRouter#")]
    [InlineData("cisco 1921 (revision 1.0) with 487424K/36864K bytes of memory.\nRouter>")]
    [InlineData("cisco 1941 with 512MB memory\nRouter#")]
    [InlineData("CISCO1905/K9 platform with 524288 Kbytes of main memory\nrommon 1 > ")]
    public void DeviceDetector_CorrectlyIdentifiesCisco1900Family_Not921(string output)
    {
        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt(output);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceSeries.Series1900, result.Series);
    }

    [Theory]
    [InlineData("Cisco IOS Software [Fuji], C900 Software (C900-UNIVERSALK9-M), Version 16.9.4\ncisco C921-4P (revision 1.0) with 1048576K bytes of memory.\nProcessor board ID FGL19212345\nRouter>")]
    [InlineData("cisco C921-4P (revision 1.0) with 1048576K bytes of memory.\nProcessor board ID FGL19059999\nRouter#")]
    [InlineData("C921-4P platform with 1048576 Kbytes of main memory\nrommon 1 > ")]
    [InlineData("cisco C927-4P with 1048576K bytes of memory.\nRouter>")]
    public void DeviceDetector_CorrectlyIdentifiesCisco900Family_EvenWithYear2015SerialNumber(string output)
    {
        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt(output);

        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
        Assert.Equal(DeviceSeries.Isr921, result.Series);
    }

    [Theory]
    [InlineData("HPE Comware Software, Version 7.1.064, Release 0614P01\nHPE MSR1002-4 Router\n<HPE>")]
    [InlineData("HP MSR1002-8 Router uptime is 1 week\n<HPE>")]
    [InlineData("HPE MSR1003-8 Router with 1024M bytes memory\n[HPE]")]
    [InlineData("msr1000-cmw710-boot.bin\n==========================<EXTENDED-BOOTWARE MENU>==========================\nchoice(0-9):")]
    public void DeviceDetector_CorrectlyIdentifiesHpe1002Family(string output)
    {
        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt(output);

        Assert.Equal(DeviceManufacturer.Hpe, result.Manufacturer);
        Assert.Equal(DeviceSeries.Msr1002, result.Series);
    }
}
