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

    public static readonly Regex HpePromptRegex = new(
        @"^[ \t]*(?!\<[0-9]\>)(?!\<EXTEND)(?!\<BASIC)(?!\<MAIN)(?!\<BOOT)(?!\<ETHERNET)(?!\<FILE)(?!\<SERIAL)(?!\<Storage)[\<\[][A-Za-z][A-Za-z0-9_\-\.\/]*[\>\]]\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public static readonly Regex CiscoPromptRegex = new(
        @"^[ \t]*[A-Za-z0-9_\-\.\/]+(?:\([A-Za-z0-9_\-\.\/]+\))?[>#]\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static readonly Regex CiscoConfigPromptRegex = new(
        @"^[ \t]*[A-Za-z0-9_\-\.\/]+(?:\([A-Za-z0-9_\-\.\/]*config[A-Za-z0-9_\-\.\/]*\))[>#]\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static readonly Regex FortiPromptRegex = new(
        @"(?im)^[ \t]*(?:(?:FortiGate|FGT|FG)[A-Za-z0-9_\-]*\s*(?:\([^()\r\n]*\))?|[A-Za-z0-9_\-\.]+\s*\((?!(?:config|cfg))[A-Za-z0-9_\-]+\)|[A-Za-z0-9_\-\.]+)\s+[#$]\s*$",
        RegexOptions.Compiled);

    public static readonly Regex Cisco1900ModelRegex = new(
        @"(?i)(?:\bC19[0-9]{2}\b|\bCISCO\s*19[0-9]{2}(?:[A-Za-z0-9\-\/]+)?\b|\bcisco\s+19[0-9]{2}\b|\b(?:C19[0-9]{2}|CISCO19[0-9]{2}|19[0-9]{2})\s*(?:BR|[A-Z]{2})?\s*(?:platform|Series|with|Integrated|router)\b|\bBootstrap,\s*Version\s*15\.0\(1r\)M|\bc1900-universalk9|\bc1900-|\bCISCO1905\b|\bCISCO1921\b|\bCISCO1941\b|\bC1905\b|\bC1921\b|\bC1941\b)",
        RegexOptions.Compiled);

    public static readonly Regex Cisco2900ModelRegex = new(
        @"(?i)(?:\bC29[0-9]{2}\b|\bCISCO\s*29[0-9]{2}(?:[A-Za-z0-9\-\/]+)?\b|\bcisco\s+29[0-9]{2}\b|\b(?:C29[0-9]{2}|CISCO29[0-9]{2}|29[0-9]{2})\s*(?:BR|[A-Z]{2})?\s*(?:platform|Series|with|Integrated|router)\b|\bc2900-universalk9|\bc2900-|\bCISCO2901\b|\bCISCO2911\b|\bCISCO2921\b|\bCISCO2951\b|\bC2901\b|\bC2911\b|\bC2921\b|\bC2951\b)",
        RegexOptions.Compiled);

    public static readonly Regex Cisco900ModelRegex = new(
        @"(?i)(?:\bC900\b|\bC92[0-9](?:-[A-Za-z0-9]+)?\b|\bCISCO92[0-9](?:[A-Za-z0-9\-\/]+)?\b|\bcisco\s+C?92[0-9]\b|\b(?:C92[0-9]|921|900)\s*(?:BR|[A-Z]{2})?\s*(?:platform|Series|with|Integrated)\b|\bBootstrap,\s*Version\s*15\.[68]\([0-9]+r\)M|\bISR\s*9[0-9]{2}\\b|\bc900-universalk9|\bc900-)",
        RegexOptions.Compiled);

    public static readonly Regex Cisco841ModelRegex = new(
        @"(?i)(?:\bC841[A-Za-z0-9\-]*\b|\bCISCO\s*841[A-Za-z0-9\-]*\b|\bC800M\b|\bcisco\s+C?841\b|\b(?:C841|841|800M)\s*(?:BR|[A-Z]{2})?\s*(?:platform|Series|with|Integrated|Software)\b|\bISR\s*841\b|\bc841-|\bc800-universalk9|\bc800m-|\bc841m-)",
        RegexOptions.Compiled);

    public static readonly Regex Cisco1100ModelRegex = new(
        @"(?i)(?:\bC1111[A-Za-z0-9\-]*\b|\bCISCO\s*1111[A-Za-z0-9\-]*\b|\bC1100[A-Za-z0-9\-]*\b|\bcisco\s+C?1111\b|\b(?:C1111|1111|C1100|1100)\s*(?:BR|[A-Z]{2})?\s*(?:platform|Series|with|Integrated|Software|router)\b|\bISR\s*11[0-9]{2}\b|\bc1100-universalk9|\bc1100-|\bc1111-)",
        RegexOptions.Compiled);

    public static readonly Regex Hpe1002ModelRegex = new(
        @"(?i)(?:\bMSR[-_\s]*100[0-9Xx](?:-[0-9A-Za-z]+)?\b|\b100[0-9]-[0-9A-Za-z]+\b|\b1002\b|\b1003\b|\b1004\b|\b1000\b|\bmsr100[0-9Xx]\b)",
        RegexOptions.Compiled);

    public static readonly Regex Hpe930ModelRegex = new(
        @"(?i)(?:\bMSR[-_\s]*93[0-9](?:-[0-9A-Za-z]+)?\b|\b93[0-9]-[0-9A-Za-z]+\b|\b930\b|\b931\b|\b935\b|\bmsr93[0-9]\b)",
        RegexOptions.Compiled);

    public static readonly Regex Hpe954ModelRegex = new(
        @"(?i)(?:\bMSR[-_\s]*95[0-9](?:-[0-9A-Za-z]+)?\b|\b95[0-9]-[0-9A-Za-z]+\b|\b954\b|\b958\b|\bmsr95[0-9]\b)",
        RegexOptions.Compiled);

    // Fortinet FortiGate 40F (aditivo — não altera os padrões Cisco/HPE acima).
    public static readonly Regex FortiGate40FModelRegex = new(
        @"(?i)(?:\bFortiGate(?:-|\s*)40F\b|\bFGT-?40F\b|\bFGT40F\b|\bFG-?40F\b|\bFG40F\b|\bFortiOS\b.*\b40F\b)",
        RegexOptions.Compiled);

    public static readonly Regex FortiDisclaimerRegex = new(
        @"(?i)(?:\(Press\s+['""]?a['""]?\s+to\s+accept.*?\)|Press\s+['""]?a['""]?\s+to\s+accept|\bto\s+accept\s*\):?|PRE\s+WARNING:.*?(?:\(Press|Press)\s+['""]?a['""]?)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public static bool IsDisclaimerPrompt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return FortiDisclaimerRegex.IsMatch(text) ||
               text.Contains("Press 'a' to accept", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("press 'a' to accept", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Press \"a\" to accept", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Press a to accept", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("to accept):", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("to accept:", StringComparison.OrdinalIgnoreCase) ||
               (text.Contains("PRE WARNING", StringComparison.OrdinalIgnoreCase) && text.Contains("accept", StringComparison.OrdinalIgnoreCase)) ||
               Regex.IsMatch(text, @"(?i)press\s+['""]?a['""]?\s+to\s+accept");
    }

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

                // Termo de uso / PRE WARNING FortiGate ((Press 'a' to accept):): aceita imediatamente com 'a' puro
                if (IsDisclaimerPrompt(current))
                {
                    rxAccumulator.Clear();
                    await transport.WriteAsync(new byte[] { 0x61 }, ct);
                    await Task.Delay(350, ct);
                    silenceDeadline = DateTime.UtcNow.AddSeconds(2.5);
                    continue;
                }

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

                if (current.Contains("Please enter q/Q to quit", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("quit,other key continue", StringComparison.OrdinalIgnoreCase))
                {
                    rxAccumulator.Clear();
                    await transport.WriteAsync(Encoding.UTF8.GetBytes("q"), ct);
                    await Task.Delay(300, ct);
                    await transport.WriteAsync(Encoding.UTF8.GetBytes("\r\n"), ct);
                    await Task.Delay(300, ct);
                    silenceDeadline = DateTime.UtcNow.AddSeconds(2.0);
                    continue;
                }

                if (CiscoConfigPromptRegex.IsMatch(current) ||
                    Regex.IsMatch(current, @"\([A-Za-z0-9_\-\.\/]*config[A-Za-z0-9_\-\.\/]*\)\s*[>#]\s*$"))
                {
                    rxAccumulator.Clear();
                    await transport.WriteAsync(Encoding.UTF8.GetBytes("end\r\n"), ct);
                    await Task.Delay(250, ct);
                    await transport.WriteAsync(Encoding.UTF8.GetBytes("\r\n"), ct);
                    silenceDeadline = DateTime.UtcNow.AddSeconds(1.5);
                    continue;
                }

                if (PasswordPromptRegex.IsMatch(current) ||
                    HpePromptRegex.IsMatch(current) ||
                    CiscoPromptRegex.IsMatch(current) ||
                    CiscoConfigPromptRegex.IsMatch(current) ||
                    FortiPromptRegex.IsMatch(current) ||
                    current.Contains("Verifying password", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("choice", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("rommon", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("BootWare", StringComparison.OrdinalIgnoreCase) ||
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
        var hasLoginOrPasswordAfterDisclaimer = PasswordPromptRegex.IsMatch(lastLine)
                                             || UserAuthPromptRegex.IsMatch(lastLine)
                                             || PasswordOnlyPromptRegex.IsMatch(lastLine)
                                             || rawPrompt.Contains("Login incorrect", StringComparison.OrdinalIgnoreCase)
                                             || rawPrompt.Contains("Login failed", StringComparison.OrdinalIgnoreCase)
                                             || rawPrompt.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase)
                                             || rawPrompt.Contains("Verifying password", StringComparison.OrdinalIgnoreCase);

        var isDisclaimer = IsDisclaimerPrompt(lastLine) && !hasLoginOrPasswordAfterDisclaimer;

        // Se a última linha de saída do console for um prompt de autenticação, tem precedência absoluta (exceto disclaimer 'Press a to accept')
        var isAuthAtEnd = (PasswordPromptRegex.IsMatch(lastLine)
                       || UserAuthPromptRegex.IsMatch(lastLine)
                       || PasswordOnlyPromptRegex.IsMatch(lastLine)
                       || lastLine.EndsWith(":", StringComparison.Ordinal))
                       && !isDisclaimer;

        // Procura a última linha que corresponda a um prompt HPE/Cisco/Fortinet em todo o buffer,
        // apenas se o console não estiver parado pedindo credenciais no final.
        var promptLine = isAuthAtEnd
            ? lastLine
            : (lines.LastOrDefault(l => HpePromptRegex.IsMatch(l) || CiscoPromptRegex.IsMatch(l) || FortiPromptRegex.IsMatch(l)) ?? lastLine);

        var isOpenPrompt = !isAuthAtEnd
            && (HpePromptRegex.IsMatch(promptLine) || CiscoPromptRegex.IsMatch(promptLine) || FortiPromptRegex.IsMatch(promptLine))
            && !PasswordPromptRegex.IsMatch(promptLine);

        var isUserAuth = !isOpenPrompt && !isDisclaimer && (UserAuthPromptRegex.IsMatch(rawPrompt) || rawPrompt.Contains("Login incorrect", StringComparison.OrdinalIgnoreCase) || rawPrompt.Contains("Login failed", StringComparison.OrdinalIgnoreCase));
        var isPasswordOnly = !isOpenPrompt && !isDisclaimer && !isUserAuth && PasswordOnlyPromptRegex.IsMatch(rawPrompt);
        var isPasswordLocked = !isOpenPrompt && !isDisclaimer && (isUserAuth || isPasswordOnly || isAuthAtEnd || PasswordPromptRegex.IsMatch(rawPrompt) || rawPrompt.Contains("Verifying password", StringComparison.OrdinalIgnoreCase));

        var isComwareReady = isOpenPrompt
                          || rawPrompt.Contains("Press ENTER to get started", StringComparison.OrdinalIgnoreCase)
                          || rawPrompt.Contains("Line con0 is available", StringComparison.OrdinalIgnoreCase)
                          || rawPrompt.Contains("Please enter q/Q to quit", StringComparison.OrdinalIgnoreCase)
                          || rawPrompt.Contains("quit,other key continue", StringComparison.OrdinalIgnoreCase);

        var isBootware = rawPrompt.Contains("choice(0-", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("choice (0-", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("choice:", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("EXTENDED-BOOTWARE", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("EXTENDED ASSISTANT MENU", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("BASIC BOOT MENU", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("<MAIN MENU>", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("Enter your choice", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("Format File System", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("Image program does not exist", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("Loading images fails", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("The image does not exist", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("Loading boot image fails", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("The main application file does not exist", StringComparison.OrdinalIgnoreCase)
                      || rawPrompt.Contains("Booting App fails", StringComparison.OrdinalIgnoreCase);

        if (isBootware)
        {
            isOpenPrompt = false;
            isComwareReady = false;
        }

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

        // 3. Detecção de Fabricante com Precedência Absoluta para Hardware Físico
        var isHpePhysical = isBootware
            || HpePromptRegex.IsMatch(rawPrompt)
            || HpePromptRegex.IsMatch(promptLine)
            || rawPrompt.Contains("HPE", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("Comware", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("Hewlett Packard", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("SubMenu", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("<HPE", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("[HPE", StringComparison.OrdinalIgnoreCase)
            || (promptLine.StartsWith("<") && promptLine.EndsWith(">"))
            || (promptLine.StartsWith("[") && promptLine.EndsWith("]"));

        // Bloco Fortinet (aditivo — assinaturas exclusivas FortiOS/FGT/Fortinet; não colidem com HPE/Cisco).
        var isFortiPhysical = !isHpePhysical && (isFortiBios
            || isDisclaimer
            || IsDisclaimerPrompt(rawPrompt)
            || FortiGate40FModelRegex.IsMatch(rawPrompt)
            || rawPrompt.Contains("FortiGate", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("Fortinet", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("FortiOS", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("FortiBootLoader", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("config system", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("config firewall", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("FGT", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("FG40F", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("FG-40F", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("FOS", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("Verifying password", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(rawPrompt, @"(?i)(?:FortiGate|FGT|FG)[A-Za-z0-9_\-]*\s+login\s*[:?]")
            || FortiPromptRegex.IsMatch(rawPrompt));

        var isCiscoPhysical = !isHpePhysical && !isFortiPhysical && (isRommon
            || rawPrompt.Contains("User Access Verification", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("Bad passwords", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("Bad secrets", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("cisco", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("IOS", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("initial configuration dialog", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("terminate autoinstall", StringComparison.OrdinalIgnoreCase)
            || rawPrompt.Contains("(config", StringComparison.OrdinalIgnoreCase)
            || CiscoPromptRegex.IsMatch(rawPrompt)
            || CiscoConfigPromptRegex.IsMatch(rawPrompt)
            || Cisco841ModelRegex.IsMatch(rawPrompt)
            || Cisco900ModelRegex.IsMatch(rawPrompt)
            || Cisco1100ModelRegex.IsMatch(rawPrompt)
            || Cisco1900ModelRegex.IsMatch(rawPrompt)
            || Cisco2900ModelRegex.IsMatch(rawPrompt));

        var isHpe = isHpePhysical || (!isFortiPhysical && !isCiscoPhysical && (
            userSelectedSeries == DeviceSeries.Msr954 ||
            userSelectedSeries == DeviceSeries.Msr930 ||
            userSelectedSeries == DeviceSeries.Msr1002));

        var isFortinet = isFortiPhysical || (!isHpePhysical && !isCiscoPhysical && userSelectedSeries == DeviceSeries.FortiGate40F);

        var isCisco = isCiscoPhysical || (!isHpePhysical && !isFortiPhysical && (
            userSelectedSeries == DeviceSeries.Series1900 ||
            userSelectedSeries == DeviceSeries.Series2900 ||
            userSelectedSeries == DeviceSeries.Isr921 ||
            userSelectedSeries == DeviceSeries.Isr841 ||
            userSelectedSeries == DeviceSeries.Isr1100));

        var manufacturer = isFortinet ? DeviceManufacturer.Fortinet :
                           isHpe ? DeviceManufacturer.Hpe :
                           isCisco ? DeviceManufacturer.Cisco :
                           DeviceManufacturer.Generic;

        // 4. Detecção de Série
        var series = DeviceSeries.Unknown;
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
            else if (userSelectedSeries == DeviceSeries.Msr954 || userSelectedSeries == DeviceSeries.Msr930 || userSelectedSeries == DeviceSeries.Msr1002)
            {
                series = userSelectedSeries;
            }
            else
            {
                series = DeviceSeries.Unknown;
            }
        }
        else if (Cisco1100ModelRegex.IsMatch(rawPrompt))
        {
            series = DeviceSeries.Isr1100;
        }
        else if (Cisco841ModelRegex.IsMatch(rawPrompt))
        {
            series = DeviceSeries.Isr841;
        }
        else if (Cisco900ModelRegex.IsMatch(rawPrompt))
        {
            series = DeviceSeries.Isr921;
        }
        else if (Cisco2900ModelRegex.IsMatch(rawPrompt))
        {
            series = DeviceSeries.Series2900;
        }
        else if (Cisco1900ModelRegex.IsMatch(rawPrompt))
        {
            series = DeviceSeries.Series1900;
        }
        else if (isCisco)
        {
            if (userSelectedSeries == DeviceSeries.Isr1100 || userSelectedSeries == DeviceSeries.Isr841 || userSelectedSeries == DeviceSeries.Isr921 || userSelectedSeries == DeviceSeries.Series2900 || userSelectedSeries == DeviceSeries.Series1900)
            {
                series = userSelectedSeries;
            }
            else
            {
                series = DeviceSeries.Unknown;
            }
        }
        else
        {
            series = userSelectedSeries;
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
        else if (isDisclaimer)
        {
            // Fortinet em tela de aviso prévio / termo de uso (Press 'a' to accept) — aceitação de termo, não bloqueio por senha
            opState = DeviceOperatingState.Ready;
            accessState = AccessState.Open;
            bootState = BootState.Normal;
            fwState = FirmwareState.Ready;
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
        else if (isFortinet && !FortiPromptRegex.IsMatch(promptLine) && !Regex.IsMatch(promptLine, @"[#$]\s*$"))
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
