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
        Assert.Equal(DeviceSeries.Msr954, result.Series);
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
        Assert.Equal(DeviceSeries.Msr954, result.Series);
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
}
