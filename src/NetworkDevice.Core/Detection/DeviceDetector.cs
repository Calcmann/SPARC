using System.Text;
using System.Text.RegularExpressions;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Engines;
using NetworkDevice.Core.Routing;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Core.Detection;

public sealed class DeviceDetector : IDeviceDetector
{
    private static readonly Regex UserAuthPromptRegex = new(
        @"(?i)(?:Username|login|User Access Verification)\s*[:?]",
        RegexOptions.Compiled);

    private static readonly Regex PasswordOnlyPromptRegex = new(
        @"(?i)(?:(?:Login|Enter)?\s*Password)\s*[:?]",
        RegexOptions.Compiled);

    private static readonly Regex PasswordPromptRegex = new(
        @"(?i)(?:Password|Username|login|User Access Verification)\s*[:?]",
        RegexOptions.Compiled);

    private static readonly Regex HpePromptRegex = new(
        @"[\<\[][^\r\n>\]]+[\>\]]\s*$",
        RegexOptions.Compiled);

    private static readonly Regex CiscoPromptRegex = new(
        @"[^\r\n>#]+[>#]\s*$",
        RegexOptions.Compiled);

    public async Task<DeviceDetectionResult> DetectAsync(ITransport transport, CancellationToken ct = default)
    {
        if (!transport.IsOpen)
        {
            await transport.OpenAsync(ct);
        }

        var rxBuffer = new byte[2048];
        var rxAccumulator = new StringBuilder();
        var bytesReceived = false;

        // Envia retorno de linha para despertar o console
        await transport.WriteAsync(Encoding.UTF8.GetBytes("\r\n"), ct);

        var silenceDeadline = DateTime.UtcNow.AddSeconds(2.5);
        while (DateTime.UtcNow < silenceDeadline && !ct.IsCancellationRequested)
        {
            var read = await transport.ReadAsync(rxBuffer, ct);
            if (read > 0)
            {
                bytesReceived = true;
                var chunk = Encoding.UTF8.GetString(rxBuffer, 0, read).Replace("\uFFFD", "");
                rxAccumulator.Append(chunk);

                var current = rxAccumulator.ToString();
                if (PasswordPromptRegex.IsMatch(current) ||
                    HpePromptRegex.IsMatch(current) ||
                    CiscoPromptRegex.IsMatch(current) ||
                    current.Contains("choice", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("rommon", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("BootWare", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("Please enter q/Q to quit", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("EXTENDED-BOOTWARE", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("BASIC BOOT MENU", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                silenceDeadline = DateTime.UtcNow.AddMilliseconds(400);
            }
            else if (bytesReceived && DateTime.UtcNow >= silenceDeadline)
            {
                break;
            }

            await Task.Delay(30, ct);
        }

        if (!bytesReceived)
        {
            await transport.WriteAsync(Encoding.UTF8.GetBytes("\r\n"), ct);
            await Task.Delay(350, ct);
            var read2 = await transport.ReadAsync(rxBuffer, ct);
            if (read2 > 0)
            {
                var chunk = Encoding.UTF8.GetString(rxBuffer, 0, read2).Replace("\uFFFD", "");
                rxAccumulator.Append(chunk);
            }
        }

        var rawPrompt = rxAccumulator.ToString().Trim();
        return ClassifyPrompt(rawPrompt);
    }

    public DeviceDetectionResult ClassifyPrompt(string rawPrompt, DeviceSeries userSelectedSeries = DeviceSeries.Unknown)
    {
        if (string.IsNullOrWhiteSpace(rawPrompt))
        {
            return new DeviceDetectionResult(
                DeviceManufacturer.Unknown,
                DeviceSeries.Unknown,
                DeviceOperatingState.Unknown,
                WorkflowType.Provisioning,
                AccessState.Disconnected,
                BootState.Unknown,
                FirmwareState.Unknown,
                rawPrompt,
                "Nenhum dado recebido da porta serial.");
        }

        // 1. Detecção de BootWare / ROMMON (BootFailure)
        var isRommon = rawPrompt.Contains("rommon", StringComparison.OrdinalIgnoreCase)
                    || rawPrompt.Contains("switch:", StringComparison.OrdinalIgnoreCase)
                    || rawPrompt.Contains("cannot determine first executable", StringComparison.OrdinalIgnoreCase);

        var isBootware = rawPrompt.Contains("BootWare", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("choice(0-", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("choice (0-", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("EXTENDED-BOOTWARE", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("BASIC BOOT MENU", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("<MAIN MENU>", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("Enter your choice", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("Please enter q/Q to quit", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("Hewlett Packard Enterprise", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("Image program does not exist", StringComparison.OrdinalIgnoreCase);

        // 2. Detecção de Senha / Bloqueio (PasswordProtected)
        var isUserAuth = UserAuthPromptRegex.IsMatch(rawPrompt);
        var isPasswordOnly = PasswordOnlyPromptRegex.IsMatch(rawPrompt);
        var isPasswordLocked = isUserAuth || isPasswordOnly || PasswordPromptRegex.IsMatch(rawPrompt);

        // 3. Detecção de Fabricante
        var isHpe = isBootware
            || rawPrompt.Contains("HPE", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("Comware", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("Hewlett Packard", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("SubMenu", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("<HPE", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("[HPE", StringComparison.OrdinalIgnoreCase)
            || userSelectedSeries == DeviceSeries.Msr954;

        var isCisco = isRommon
            || rawPrompt.Contains("cisco", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("IOS", StringComparison.OrdinalIgnoreCase)
            || userSelectedSeries == DeviceSeries.Series1900
            || userSelectedSeries == DeviceSeries.Isr921;

        var manufacturer = isHpe ? DeviceManufacturer.Hpe :
                           isCisco ? DeviceManufacturer.Cisco :
                           DeviceManufacturer.Generic;

        // 4. Detecção de Série
        var series = userSelectedSeries;
        if (series == DeviceSeries.Unknown)
        {
            if (isHpe)
            {
                series = DeviceSeries.Msr954;
            }
            else if (rawPrompt.Contains("1900", StringComparison.OrdinalIgnoreCase) ||
                     rawPrompt.Contains("1941", StringComparison.OrdinalIgnoreCase) ||
                     rawPrompt.Contains("1921", StringComparison.OrdinalIgnoreCase))
            {
                series = DeviceSeries.Series1900;
            }
            else if (rawPrompt.Contains("921", StringComparison.OrdinalIgnoreCase) ||
                     rawPrompt.Contains("C921", StringComparison.OrdinalIgnoreCase) ||
                     rawPrompt.Contains("ISR921", StringComparison.OrdinalIgnoreCase))
            {
                series = DeviceSeries.Isr921;
            }
            else if (isCisco)
            {
                series = DeviceSeries.Series1900; // Padrão Cisco caso não discriminado
            }
        }

        // 5. Determinação de Estado Operacional
        DeviceOperatingState opState;
        AccessState accessState;
        BootState bootState;
        FirmwareState fwState;

        if (isBootware || isRommon)
        {
            opState = DeviceOperatingState.BootFailure;
            accessState = AccessState.RommonOrBootware;
            bootState = isBootware ? BootState.Bootware : BootState.Rommon;
            fwState = FirmwareState.CorruptedOrMissing;
        }
        else if (isUserAuth)
        {
            opState = DeviceOperatingState.PasswordProtected;
            accessState = AccessState.UserAndPasswordRequired;
            bootState = BootState.Normal;
            fwState = FirmwareState.Ready;
        }
        else if (isPasswordLocked)
        {
            opState = DeviceOperatingState.PasswordProtected;
            accessState = AccessState.PasswordRequired;
            bootState = BootState.Normal;
            fwState = FirmwareState.Ready;
        }
        else
        {
            opState = DeviceOperatingState.Ready;
            accessState = AccessState.Open;
            bootState = BootState.Normal;
            fwState = FirmwareState.Ready;
        }

        var workflow = WorkflowRouter.ResolveWorkflow(manufacturer, series, opState);
        var details = WorkflowRouter.GetWorkflowDescription(manufacturer, series, workflow);

        return new DeviceDetectionResult(
            manufacturer,
            series,
            opState,
            workflow,
            accessState,
            bootState,
            fwState,
            rawPrompt,
            details);
    }
}
