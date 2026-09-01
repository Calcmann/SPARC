using NetworkDevice.Core.Domain;

namespace NetworkDevice.Core.Routing;

/// <summary>
/// Roteador determinístico da Matriz de Fluxos SPARC (Fabricante x Série x Estado).
/// </summary>
public static class WorkflowRouter
{
    public static WorkflowType ResolveWorkflow(
        DeviceManufacturer manufacturer,
        DeviceSeries series,
        DeviceOperatingState state)
    {
        return state switch
        {
            DeviceOperatingState.Ready => WorkflowType.Provisioning,
            DeviceOperatingState.PasswordProtected => WorkflowType.PasswordRecovery,
            DeviceOperatingState.BootFailure => WorkflowType.FirmwareRecovery,
            _ => WorkflowType.Provisioning
        };
    }

    public static string GetWorkflowDescription(
        DeviceManufacturer manufacturer,
        DeviceSeries series,
        WorkflowType workflow)
    {
        return (manufacturer, series, workflow) switch
        {
            (DeviceManufacturer.Hpe, DeviceSeries.Msr954, WorkflowType.Provisioning) =>
                "HPE MSR954 — Provisionamento Canônico Comware 7",
            (DeviceManufacturer.Hpe, DeviceSeries.Msr954, WorkflowType.PasswordRecovery) =>
                "HPE MSR954 — Recuperação de Acesso via BootWare (Skip Config/Auth)",
            (DeviceManufacturer.Hpe, DeviceSeries.Msr954, WorkflowType.FirmwareRecovery) =>
                "HPE MSR954 — Recuperação de Imagem Flash via BootWare Ethernet TFTP",

            (DeviceManufacturer.Cisco, DeviceSeries.Series1900, WorkflowType.Provisioning) =>
                "Cisco Série 1900 — Provisionamento Canônico Cisco IOS",
            (DeviceManufacturer.Cisco, DeviceSeries.Series1900, WorkflowType.PasswordRecovery) =>
                "Cisco Série 1900 — Recuperação de Senha ROMMON (0x2142)",
            (DeviceManufacturer.Cisco, DeviceSeries.Series1900, WorkflowType.FirmwareRecovery) =>
                "Cisco Série 1900 — Recuperação de IOS via ROMMON TFTP / Xmodem",

            (DeviceManufacturer.Cisco, DeviceSeries.Isr921, WorkflowType.Provisioning) =>
                "Cisco ISR 921 — Provisionamento Canônico Cisco IOS",
            (DeviceManufacturer.Cisco, DeviceSeries.Isr921, WorkflowType.PasswordRecovery) =>
                "Cisco ISR 921 — Recuperação de Senha ROMMON com Interrupção Ctrl+C",
            (DeviceManufacturer.Cisco, DeviceSeries.Isr921, WorkflowType.FirmwareRecovery) =>
                "Cisco ISR 921 — Recuperação de IOS ISR921 via ROMMON",

            _ => $"{manufacturer} {series} — {workflow}"
        };
    }
}
