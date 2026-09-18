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
        @"(?i)(?:\bUsername\b|\blogin\b|\bUser Access Verification\b)\s*[:?]",
        RegexOptions.Compiled);

    private static readonly Regex PasswordOnlyPromptRegex = new(
        @"(?i)(?:(?:\bLogin\b|\bEnter\b)?\s*\bPassword\b)\s*[:?]",
        RegexOptions.Compiled);

    private static readonly Regex PasswordPromptRegex = new(
        @"(?i)(?:\bPassword\b|\bUsername\b|\blogin\b|\bUser Access Verification\b)\s*[:?]",
        RegexOptions.Compiled);

    private static readonly Regex HpePromptRegex = new(
        @"[\<\[][^\r\n>\]]+[\>\]]\s*$",
        RegexOptions.Compiled);

    private static readonly Regex CiscoPromptRegex = new(
        @"[^\r\n>#]+[>#]\s*$",
        RegexOptions.Compiled);

    public static readonly Regex FortiPromptRegex = new(
        @"(?i)(?:FortiGate|FGT)[A-Za-z0-9_\-]*\s*(?:\([^()\r\n]*\))?\s*[#$]\s*$",
        RegexOptions.Compiled);

    public static readonly Regex Cisco1900ModelRegex = new(
        @"(?i)(?:\bC19[0-9]{2}\b|\bCISCO19[0-9]{2}(?:[A-Za-z0-9\-\/]+)?\b|\bcisco\s+19[0-9]{2}\b|\b19[0-9]{2}\s*(?:BR|[A-Z]{2})?\s*(?:platform|Series|with|Integrated)\b|\bBootstrap,\s*Version\s*15\.0\(1r\)M|\bc1900-universalk9|\bc1900-)",
        RegexOptions.Compiled);

    public static readonly Regex Cisco900ModelRegex = new(
        @"(?i)(?:\bC900\b|\bC92[0-9](?:-[A-Za-z0-9]+)?\b|\bCISCO92[0-9](?:[A-Za-z0-9\-\/]+)?\b|\bcisco\s+C?92[0-9]\b|\b(?:C92[0-9]|921|900)\s*(?:BR|[A-Z]{2})?\s*(?:platform|Series|with|Integrated)\b|\bBootstrap,\s*Version\s*15\.[68]\([0-9]+r\)M|\bISR\s*9[0-9]{2}\\b|\bc900-universalk9|\bc900-)",
        RegexOptions.Compiled);

    public static readonly Regex Cisco841ModelRegex = new(
        @"(?i)(?:\bC841\b|\bC841M\b|\bCISCO841\b|\bcisco\s+C?841\b|\b(?:C841|841|800M)\s*(?:BR|[A-Z]{2})?\s*(?:platform|Series|with|Integrated)\b|\bISR\s*841\b|\bc841-|\bc800-universalk9|\bc800m-|\bc841m-)",
        RegexOptions.Compiled);

    public static readonly Regex Hpe1002ModelRegex = new(
        @"(?i)(?:\bMSR\s*100[0-9](?:-[0-9A-Za-z]+)?\b|\bMSR100[0-9](?:-[0-9A-Za-z]+)?\b|\b1002\b|\b1003\b|\b1004\b|\b1000\b|\bmsr100[0-9]|\bmsr100x)",
        RegexOptions.Compiled);

    public static readonly Regex Hpe930ModelRegex = new(
        @"(?i)(?:\bMSR\s*93[0-9](?:-[0-9A-Za-z]+)?\b|\bMSR93[0-9](?:-[0-9A-Za-z]+)?\b|\b930\b|\b931\b|\b935\b|\bmsr93[0-9])",
        RegexOptions.Compiled);

    public static readonly Regex Hpe954ModelRegex = new(
        @"(?i)(?:\bMSR\s*95[0-9](?:-[0-9A-Za-z]+)?\b|\bMSR95[0-9](?:-[0-9A-Za-z]+)?\b|\b954\b|\b958\b|\bmsr95[0-9])",
        RegexOptions.Compiled);

    // Fortinet FortiGate 40F (aditivo — não altera os padrões Cisco/HPE acima).
    public static readonly Regex FortiGate40FModelRegex = new(
        @"(?i)(?:\bFortiGate(?:-|\s*)40F\b|\bFGT-?40F\b|\bFGT40F\b|\bFortiOS\b.*\b40F\b)",
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
                if (current.Contains("Press ENTER to get started", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("Press RETURN to get started", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("Line con0 is available", StringComparison.OrdinalIgnoreCase))
                {
                    rxAccumulator.Clear();
                    await transport.WriteAsync(Encoding.UTF8.GetBytes("\r\n"), ct);
                    await Task.Delay(300, ct);
                    silenceDeadline = DateTime.UtcNow.AddSeconds(1.5);
                    continue;
                }

                if (PasswordPromptRegex.IsMatch(current) ||
                    HpePromptRegex.IsMatch(current) ||
                    CiscoPromptRegex.IsMatch(current) ||
                    current.Contains("choice", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("rommon", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("BootWare", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("Please enter q/Q to quit", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("EXTENDED-BOOTWARE", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("BASIC BOOT MENU", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("FOS boot failed", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("Neither DEFAULT nor BACKUP", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("Please install valid FOS", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("System halted", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("CTRL+D", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("Enter C,R,T,F,I,B,Q,or H:", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("Enter Selection:", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("FortiBootLoader", StringComparison.OrdinalIgnoreCase))
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

        // 1. Detecção de BootWare / ROMMON / Falha de Imagem Flash (BootFailure - Cenário 1 e 4)
        var isRommon = rawPrompt.Contains("rommon", StringComparison.OrdinalIgnoreCase)
                    || rawPrompt.Contains("switch:", StringComparison.OrdinalIgnoreCase)
                    || rawPrompt.Contains("cannot determine first executable", StringComparison.OrdinalIgnoreCase)
                    || rawPrompt.Contains("bad checksum", StringComparison.OrdinalIgnoreCase)
                    || rawPrompt.Contains("checksum failed", StringComparison.OrdinalIgnoreCase)
                    || rawPrompt.Contains("No bootable image found", StringComparison.OrdinalIgnoreCase)
                    || rawPrompt.Contains("boot: cannot load", StringComparison.OrdinalIgnoreCase)
                    || rawPrompt.Contains("autoboot failed", StringComparison.OrdinalIgnoreCase);

        // 2. Detecção de Senha / Bloqueio / Prompt Aberto (PasswordProtected / OpenPrompt)
        var lines = rawPrompt.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var lastLine = lines.LastOrDefault()?.Trim() ?? rawPrompt;
        // Procura a última linha que corresponda a um prompt HPE/Cisco/Fortinet em todo o buffer,
        // pois o buffer pode conter output de comandos (ex.: 'display version' ou 'get system status') após o prompt.
        var promptLine = lines.LastOrDefault(l => HpePromptRegex.IsMatch(l) || CiscoPromptRegex.IsMatch(l) || FortiPromptRegex.IsMatch(l)) ?? lastLine;
        var isOpenPrompt = (HpePromptRegex.IsMatch(promptLine) || CiscoPromptRegex.IsMatch(promptLine) || FortiPromptRegex.IsMatch(promptLine)) && !PasswordPromptRegex.IsMatch(promptLine);

        var isUserAuth = !isOpenPrompt && (UserAuthPromptRegex.IsMatch(rawPrompt) || rawPrompt.Contains("Login incorrect", StringComparison.OrdinalIgnoreCase) || rawPrompt.Contains("Login failed", StringComparison.OrdinalIgnoreCase));
        var isPasswordOnly = !isOpenPrompt && PasswordOnlyPromptRegex.IsMatch(rawPrompt);
        var isPasswordLocked = !isOpenPrompt && (isUserAuth || isPasswordOnly || PasswordPromptRegex.IsMatch(rawPrompt) || rawPrompt.Contains("Verifying password", StringComparison.OrdinalIgnoreCase));

        var isComwareReady = isOpenPrompt
                          || rawPrompt.Contains("Press ENTER to get started", StringComparison.OrdinalIgnoreCase)
                          || rawPrompt.Contains("Line con0 is available", StringComparison.OrdinalIgnoreCase);

        var isBootware = !isOpenPrompt && !isComwareReady && (
                          rawPrompt.Contains("choice(0-", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("choice (0-", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("EXTENDED-BOOTWARE", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("BASIC BOOT MENU", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("<MAIN MENU>", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("Enter your choice", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("Please enter q/Q to quit", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("Image program does not exist", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("Loading images fails", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("The image does not exist", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("Loading boot image fails", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("The main application file does not exist", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("Booting App fails", StringComparison.OrdinalIgnoreCase));

        // 1b. Detecção de BIOS / Bootloader / Falha de Imagem Fortinet (FortiGate 40F)
        var isFortiBios = rawPrompt.Contains("FOS boot failed", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("Neither DEFAULT nor BACKUP", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("Please install valid FOS", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("You may try backup", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("System halted", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("CTRL+D", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("Enter C,R,T,F,I,B,Q,or H:", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("Enter Selection:", StringComparison.OrdinalIgnoreCase)
                       || rawPrompt.Contains("Reading boot image ... failed", StringComparison.OrdinalIgnoreCase)
                       || (rawPrompt.Contains("Press any key to display configuration menu", StringComparison.OrdinalIgnoreCase) && !isOpenPrompt)
                       || (rawPrompt.Contains("FortiBootLoader", StringComparison.OrdinalIgnoreCase) && !isOpenPrompt)
                       || (rawPrompt.Contains("Verifying image", StringComparison.OrdinalIgnoreCase) && rawPrompt.Contains("bad checksum", StringComparison.OrdinalIgnoreCase));

        // 3. Detecção de Fabricante
        var isHpe = isBootware
            || rawPrompt.Contains("HPE", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("Comware", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("Hewlett Packard", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("SubMenu", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("<HPE", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("[HPE", StringComparison.OrdinalIgnoreCase)
            || userSelectedSeries == DeviceSeries.Msr954
            || userSelectedSeries == DeviceSeries.Msr930
            || userSelectedSeries == DeviceSeries.Msr1002;

        // Bloco Fortinet (aditivo — assinaturas exclusivas FortiOS/FGT/Fortinet; não colidem com HPE/Cisco).
        var isFortinet = isFortiBios
            || FortiGate40FModelRegex.IsMatch(rawPrompt)
            || rawPrompt.Contains("FortiGate", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("Fortinet", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("FortiOS", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("FortiBootLoader", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("config system", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("config firewall", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("FGT", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("FOS", StringComparison.OrdinalIgnoreCase)
            || userSelectedSeries == DeviceSeries.FortiGate40F;

        var isCisco = !isFortinet && (isRommon
            || rawPrompt.Contains("cisco", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("IOS", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("initial configuration dialog", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("terminate autoinstall", StringComparison.OrdinalIgnoreCase)
            || CiscoPromptRegex.IsMatch(rawPrompt)
            || userSelectedSeries == DeviceSeries.Series1900
            || userSelectedSeries == DeviceSeries.Isr921
            || userSelectedSeries == DeviceSeries.Isr841);

        var manufacturer = isFortinet ? DeviceManufacturer.Fortinet :
                           isHpe ? DeviceManufacturer.Hpe :
                           isCisco ? DeviceManufacturer.Cisco :
                           DeviceManufacturer.Generic;

        // 4. Detecção de Série
        var series = userSelectedSeries;
        if (series == DeviceSeries.Unknown)
        {
            if (isFortinet)
            {
                series = DeviceSeries.FortiGate40F;
            }
            else if (isHpe)
            {
                if (Hpe1002ModelRegex.IsMatch(rawPrompt))
                {
                    series = DeviceSeries.Msr1002;
                }
                else if (Hpe930ModelRegex.IsMatch(rawPrompt))
                {
                    series = DeviceSeries.Msr930;
                }
                else if (Hpe954ModelRegex.IsMatch(rawPrompt))
                {
                    series = DeviceSeries.Msr954;
                }
                else
                {
                    series = DeviceSeries.Unknown;
                }
            }
            else if (Cisco1900ModelRegex.IsMatch(rawPrompt))
            {
                series = DeviceSeries.Series1900;
            }
            else if (Cisco900ModelRegex.IsMatch(rawPrompt))
            {
                series = DeviceSeries.Isr921;
            }
            else if (Cisco841ModelRegex.IsMatch(rawPrompt))
            {
                series = DeviceSeries.Isr841;
            }
            else if (isCisco)
            {
                series = DeviceSeries.Unknown;
            }
        }

        // 5. Determinação de Estado Operacional
        DeviceOperatingState opState;
        AccessState accessState;
        BootState bootState;
        FirmwareState fwState;

        if (isBootware || isRommon || isFortiBios)
        {
            opState = DeviceOperatingState.BootFailure;
            accessState = AccessState.RommonOrBootware;
            bootState = isBootware ? BootState.Bootware : (isRommon ? BootState.Rommon : BootState.Bootware);
            fwState = FirmwareState.CorruptedOrMissing;
        }
        else if (isUserAuth || (isFortinet && isPasswordLocked))
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
        else if (isFortinet && !FortiPromptRegex.IsMatch(promptLine))
        {
            // Fortinet sem prompt autenticado (# ou $) está categoricamente protegido por senha
            opState = DeviceOperatingState.PasswordProtected;
            accessState = AccessState.UserAndPasswordRequired;
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
