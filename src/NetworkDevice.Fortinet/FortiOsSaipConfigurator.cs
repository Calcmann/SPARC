using System.Text.RegularExpressions;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Fortinet;

/// <summary>
/// Provisionamento SAIP para Fortinet FortiGate 40F (FortiOS).
/// Projeto isolado — não compartilha código com os configuradores Cisco/HPE.
/// Sintaxe FortiOS: config system interface / config router static /
/// config system admin / config firewall policy (blocos edit/next/end).
/// </summary>
public sealed class FortiOsSaipConfigurator
{
    private static readonly Regex EditInterfaceRegex = new(
        @"(?im)^\s*edit\s+""?(?<iface>[A-Za-z0-9_\-\.]+)""?",
        RegexOptions.Compiled);

    private readonly Func<string, Task>? _progress;

    /// <summary>
    /// Opção secreta NAT (LAB) ativada via menu/atalho de laboratório.
    /// Quando ativa, aplica política de firewall com NAT ('set nat enable') para testes em bancada com modem de acesso 4G.
    /// Default: false (roteamento corporativo padrão Claro sem NAT).
    /// </summary>
    public bool IncluirNatLab { get; set; }

    public FortiOsSaipConfigurator(Func<string, Task>? progress = null)
    {
        _progress = progress;
    }

    /// <summary>
    /// Detecta os nomes das interfaces WAN/LAN a partir de 'get system interface' ou 'show system interface'.
    /// Defaults do 40F: wan (física) e lan (hard-switch lan1+lan2+lan3).
    /// </summary>
    public static (string wanIface, string lanIface) DetectInterfaces(
        string systemInterfaceOutput,
        string preferredWan = "wan",
        string preferredLan = "lan")
    {
        if (string.IsNullOrWhiteSpace(systemInterfaceOutput))
            return (preferredWan, preferredLan);

        var ifaces = new List<string>();
        foreach (Match m in EditInterfaceRegex.Matches(systemInterfaceOutput))
        {
            var name = m.Groups["iface"].Value.Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(name)
                && !ifaces.Contains(name, StringComparer.OrdinalIgnoreCase))
                ifaces.Add(name);
        }

        if (ifaces.Count == 0)
            return (preferredWan, preferredLan);

        var wan = ifaces.FirstOrDefault(i =>
                i.Equals("wan", StringComparison.OrdinalIgnoreCase) ||
                i.Equals("wan1", StringComparison.OrdinalIgnoreCase) ||
                i.Equals("wan2", StringComparison.OrdinalIgnoreCase))
            ?? ifaces.FirstOrDefault(i => i.StartsWith("wan", StringComparison.OrdinalIgnoreCase))
            ?? preferredWan;

        var lan = ifaces.FirstOrDefault(i =>
                i.Equals("lan", StringComparison.OrdinalIgnoreCase) ||
                i.Equals("internal", StringComparison.OrdinalIgnoreCase))
            ?? ifaces.FirstOrDefault(i =>
                !i.Equals(wan, StringComparison.OrdinalIgnoreCase) &&
                (i.StartsWith("lan", StringComparison.OrdinalIgnoreCase) ||
                 i.StartsWith("port", StringComparison.OrdinalIgnoreCase) ||
                 i.StartsWith("internal", StringComparison.OrdinalIgnoreCase)))
            ?? preferredLan;

        return (wan, lan);
    }

    /// <summary>
    /// Gera a lista de linhas CLI FortiOS para provisionamento da Ficha SAIP.
    /// Cada item é uma linha a enviar na sessão (blocos config/edit/next/end inclusos).
    /// FortiOS persiste automaticamente a cada 'end' — sem 'write memory'.
    /// Acesso padrão SPARC para FortiGate: usuário EBT / senha CQMR.
    /// </summary>
    public static IReadOnlyList<string> GenerateCommands(
        SaipCircuitData circuit,
        string wanInterface = "wan",
        string lanInterface = "lan",
        string adminUser = "EBT",
        string adminPassword = "CQMR",
        bool incluirPolicyNat = true,
        bool incluirAdmin = false)
    {
        ArgumentNullException.ThrowIfNull(circuit);

        var wanAlias = SanitizeAlias($"WAN_EBT_{circuit.DesignacaoIp ?? circuit.NumeroOts ?? "LINK"}");
        var lanAlias = SanitizeAlias($"LAN_CLIENTE_{circuit.ClienteRazaoSocial ?? "CIRCUITO"}");

        var cmds = new List<string>
        {
            "config system interface",
            $"edit \"{wanInterface}\"",
            "set mode static",
            $"set ip {circuit.WanIp} {circuit.WanSubnetMask}",
            $"set alias \"{wanAlias}\"",
            "set role wan",
            "set allowaccess ping ssh",
            "next",
            $"edit \"{lanInterface}\"",
            "set mode static",
            $"set ip {circuit.LanIp} {circuit.LanSubnetMask}",
            $"set alias \"{lanAlias}\"",
            "set role lan",
            "set allowaccess ping telnet ssh https http",
            "next",
            "end",

            "config router static",
            "edit 1",
            "set dst 0.0.0.0/0",
            $"set gateway {circuit.WanGateway}",
            $"set device \"{wanInterface}\"",
            "set status enable",
            "next",
            "end",
        };

        if (incluirAdmin)
        {
            cmds.AddRange(new[]
            {
                "config system admin",
                $"edit \"{adminUser}\"",
                $"set password {adminPassword}",
                "set accprofile super_admin",
                "next",
                "end",
            });
        }

        if (incluirPolicyNat)
        {
            cmds.AddRange(new[]
            {
                "config firewall policy",
                "edit 1",
                $"set name \"SAIP_LAN_WAN_{wanAlias}\"",
                $"set srcintf \"{lanInterface}\"",
                $"set dstintf \"{wanInterface}\"",
                "set srcaddr \"all\"",
                "set dstaddr \"all\"",
                "set action accept",
                "set schedule \"always\"",
                "set service \"ALL\"",
                "set nat enable",
                "next",
                "end",
            });
        }

        return cmds;
    }

    /// <summary>
    /// Gera apenas o bloco de acesso padrão (usuário EBT / senha CQMR), para aplicar
    /// logo após o primeiro login num FortiGate em padrão de fábrica, sem provisionar o circuito.
    /// </summary>
    public static IReadOnlyList<string> GenerateDefaultAccessCommands(
        string adminUser = "EBT",
        string adminPassword = "CQMR")
    {
        return new List<string>
        {
            "config system admin",
            $"edit \"{adminUser}\"",
            $"set password {adminPassword}",
            "set accprofile super_admin",
            "next",
            "end",
        };
    }

    /// <summary>
    /// Aplica somente o acesso padrão (EBT/CQMR) no FortiGate conectado.
    /// </summary>
    public async Task ApplyDefaultAccessAsync(
        DeviceSession session,
        string adminUser = "EBT",
        string adminPassword = "CQMR",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await ProgressAsync($"[*] Aplicando acesso padrão FortiGate (usuário '{adminUser}')...");
        foreach (var cmd in GenerateDefaultAccessCommands(adminUser, adminPassword))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await session.SendCommandAsync(cmd, TimeSpan.FromSeconds(20), cancellationToken);
            if (IsFortiError(response))
                await ProgressAsync($"    [AVISO] FortiOS retornou erro na linha '{cmd}':\n    {response.Trim()}");
            await Task.Delay(200, cancellationToken);
        }
        await ProgressAsync("[OK] Acesso padrão FortiGate aplicado (persistência automática via 'end').");
    }

    /// <summary>
    /// Aplica a configuração SAIP no FortiGate conectado, de forma sequencial e validada.
    /// Não executa 'default interface', 'save' nem conceitos IOS/Comware.
    /// </summary>
    public async Task ApplyConfigAsync(
        DeviceSession session,
        SaipCircuitData circuit,
        string wanInterface = "wan",
        string lanInterface = "lan",
        string adminUser = "EBT",
        string adminPassword = "CQMR",
        bool? incluirPolicyNat = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(circuit);

        var aplicarNat = incluirPolicyNat ?? IncluirNatLab;

        await ProgressAsync($"[*] INICIANDO PROVISIONAMENTO SAIP NO FORTIGATE 40F ({circuit.DesignacaoIp ?? circuit.NumeroOts})...");
        if (aplicarNat)
            await ProgressAsync("[*] Opção secreta NAT (LAB) ATIVA: política de firewall com NAT ('set nat enable') habilitada para modem 4G.");
        else
            await ProgressAsync("[*] Provisionamento corporativo padrão Claro: NAT de laboratório desativado (modo roteamento direto).");

        // 1. Acorda o terminal (Ctrl+C + Enter, best-effort).
        try
        {
            await session.Transport.WriteAsync(new byte[] { 0x03 }, cancellationToken);
            await Task.Delay(100, cancellationToken);
            await session.WriteLineAsync(string.Empty, cancellationToken);
            await Task.Delay(200, cancellationToken);
        }
        catch { }

        // 2. Mapeia interfaces reais (best-effort; mantém defaults wan/lan em caso de falha).
        try
        {
            var getIntf = await session.SendCommandAsync("get system interface", TimeSpan.FromSeconds(15), cancellationToken);
            var (detectedWan, detectedLan) = DetectInterfaces(getIntf, wanInterface, lanInterface);
            wanInterface = detectedWan;
            lanInterface = detectedLan;
            await ProgressAsync($"[*] Interfaces mapeadas: WAN -> '{wanInterface}' | LAN -> '{lanInterface}'");
        }
        catch
        {
            await ProgressAsync("[AVISO] 'get system interface' falhou — usando defaults wan/lan.");
        }

        // 3. Limpeza pré-provisionamento — elimina vestígios de configuração anterior (baseline limpo)
        await CleanupPreviousConfigAsync(session, wanInterface, lanInterface, cancellationToken);

        // 4. Envia bloco FortiOS linha a linha (cadência 200ms, como nos perfis existentes).
        var commands = GenerateCommands(circuit, wanInterface, lanInterface, adminUser, adminPassword, aplicarNat);
        await ProgressAsync("[*] Aplicando comandos FortiOS (cadência: 200ms por linha)...");
        foreach (var cmd in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var response = await session.SendCommandAsync(cmd, TimeSpan.FromSeconds(20), cancellationToken);
                if (IsFortiError(response))
                    await ProgressAsync($"    [AVISO] FortiOS retornou erro na linha '{cmd}':\n    {response.Trim()}");
            }
            catch (Exception ex)
            {
                await ProgressAsync($"    [!] Falha ao executar '{cmd}': {ex.Message}");
            }

            await Task.Delay(200, cancellationToken);
        }

        // 4. Validação pós-provisionamento (best-effort, somente leitura).
        try
        {
            var status = await session.SendCommandAsync("get system interface", TimeSpan.FromSeconds(15), cancellationToken);
            await ProgressAsync("\n=================================================================\n" +
                                "        INTERFACES APOS PROVISIONAMENTO (FortiOS)                \n" +
                                "=================================================================\n" +
                                status +
                                "=================================================================\n");
        }
        catch { }

        await ProgressAsync("[OK] PROVISIONAMENTO FORTIGATE 40F CONCLUIDO (persistencia automatica via 'end').");
    }

    internal static bool IsFortiError(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return false;
        return output.Contains("Command fail", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Unknown action", StringComparison.OrdinalIgnoreCase)
            || output.Contains("entry not found", StringComparison.OrdinalIgnoreCase)
            || output.Contains("not valid", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Parse error", StringComparison.OrdinalIgnoreCase)
            || output.Contains("invalid", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Alias FortiOS: letras, números, '_', '-' e '.'. Demais viram '_'.
    /// Limite do FortiOS para interface alias: 25 caracteres (evita o erro 'string value is too long. The size is 32').
    /// </summary>
    public static string SanitizeAlias(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "LINK";

        var clean = text.Trim();
        clean = Regex.Replace(clean, @"[^\u0000-\u007F]+", string.Empty);
        clean = Regex.Replace(clean, @"[^A-Za-z0-9_\-\.]", "_");
        clean = Regex.Replace(clean, @"_+", "_").Trim('_', '.', '-');

        if (clean.Length > 25)
            clean = clean[..25].Trim('_', '.', '-');

        return string.IsNullOrWhiteSpace(clean) ? "LINK" : clean;
    }

    /// <summary>
    /// Captura apenas a configuração relevante aplicada no FortiGate (interfaces, rotas estáticas, firewall e DNS).
    /// NUNCA executa 'show' raiz, pois isso despeja dezenas de milhares de linhas de 'internet-service-definition'
    /// e definições de fábrica a 9600 bps, saturando o buffer serial por minutos.
    /// </summary>
    public static async Task<string> GetAppliedRunningConfigAsync(DeviceSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var sb = new System.Text.StringBuilder();
        var cmds = new[]
        {
            "show system interface",
            "show router static",
            "show firewall policy",
            "show system dns"
        };

        foreach (var cmd in cmds)
        {
            try
            {
                var output = await session.SendCommandAsync(cmd, TimeSpan.FromSeconds(8), cancellationToken);
                sb.AppendLine($"# ========================================");
                sb.AppendLine($"# {cmd}");
                sb.AppendLine($"# ========================================");
                sb.AppendLine(output.Trim());
                sb.AppendLine();
            }
            catch
            {
                // Continua para o próximo bloco caso um sub-bloco falhe ou atinja timeout
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Limpeza pré-provisionamento determinística no FortiGate (baseline limpo).
    /// Remove DHCP server conflitante na LAN, rotas estáticas anteriores, políticas antigas
    /// e aliases residuais nas interfaces WAN/LAN antes de aplicar a nova configuração da Ficha SAIP.
    /// </summary>
    public async Task CleanupPreviousConfigAsync(
        DeviceSession session,
        string wanInterface = "wan",
        string lanInterface = "lan",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await ProgressAsync("[*] [FASE C] Limpando vestígios de configuração anterior no FortiGate (baseline limpo)...");

        // a) Remove servidores DHCP residuais (evita conflito de subnet ao reconfigurar IP da LAN)
        try
        {
            var dhcpOutput = await session.SendCommandAsync("show system dhcp server", TimeSpan.FromSeconds(8), cancellationToken);
            var dhcpIds = ExtractTableEntryIds(dhcpOutput);
            if (dhcpIds.Count > 0)
            {
                await ProgressAsync($"    [-] Removendo {dhcpIds.Count} servidor(es) DHCP anterior(es) para liberar subnet LAN...");
                foreach (var id in dhcpIds)
                {
                    await session.SendCommandAsync("config system dhcp server", TimeSpan.FromSeconds(5), cancellationToken);
                    await session.SendCommandAsync($"delete {id}", TimeSpan.FromSeconds(5), cancellationToken);
                    await session.SendCommandAsync("end", TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            await ProgressAsync($"    [AVISO] Limpeza de DHCP server: {ex.Message}");
        }

        // b) Remove rotas estáticas anteriores (evita rotas default conflitantes ou gateway fantasma)
        try
        {
            var routesOutput = await session.SendCommandAsync("show router static", TimeSpan.FromSeconds(8), cancellationToken);
            var routeIds = ExtractTableEntryIds(routesOutput);
            if (routeIds.Count > 0)
            {
                await ProgressAsync($"    [-] Removendo {routeIds.Count} rota(s) estática(s) anterior(es)...");
                foreach (var id in routeIds)
                {
                    await session.SendCommandAsync("config router static", TimeSpan.FromSeconds(5), cancellationToken);
                    await session.SendCommandAsync($"delete {id}", TimeSpan.FromSeconds(5), cancellationToken);
                    await session.SendCommandAsync("end", TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            await ProgressAsync($"    [AVISO] Limpeza de rotas estáticas: {ex.Message}");
        }

        // c) Remove políticas de firewall anteriores
        try
        {
            var policyOutput = await session.SendCommandAsync("show firewall policy", TimeSpan.FromSeconds(8), cancellationToken);
            var policyIds = ExtractTableEntryIds(policyOutput);
            if (policyIds.Count > 0)
            {
                await ProgressAsync($"    [-] Removendo {policyIds.Count} política(s) de firewall anterior(es)...");
                foreach (var id in policyIds)
                {
                    await session.SendCommandAsync("config firewall policy", TimeSpan.FromSeconds(5), cancellationToken);
                    await session.SendCommandAsync($"delete {id}", TimeSpan.FromSeconds(5), cancellationToken);
                    await session.SendCommandAsync("end", TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            await ProgressAsync($"    [AVISO] Limpeza de políticas de firewall: {ex.Message}");
        }

        // d) Limpa aliases e descrições anteriores nas interfaces WAN e LAN
        try
        {
            foreach (var iface in new[] { wanInterface, lanInterface })
            {
                if (string.IsNullOrWhiteSpace(iface)) continue;
                await session.SendCommandAsync("config system interface", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync($"edit \"{iface}\"", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync("set mode static", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync("unset alias", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync("unset description", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync("set allowaccess ping ssh", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync("next", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync("end", TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            await ProgressAsync($"    [AVISO] Limpeza de interfaces WAN/LAN: {ex.Message}");
        }

        await ProgressAsync("[OK] Vestígios de configuração anterior limpos com sucesso.");
    }

    /// <summary>
    /// Extrai os IDs numéricos de blocos de tabela FortiOS gerados por 'show' (ex.: 'edit 1', 'edit 2').
    /// </summary>
    public static List<int> ExtractTableEntryIds(string output)
    {
        var ids = new List<int>();
        if (string.IsNullOrWhiteSpace(output)) return ids;

        var matches = Regex.Matches(output, @"(?im)^\s*edit\s+(\d+)");
        foreach (Match m in matches)
        {
            if (int.TryParse(m.Groups[1].Value, out var id) && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }
        return ids;
    }

    private async Task ProgressAsync(string message)
    {
        if (_progress is not null)
            await _progress(message);
    }

    /// <summary>
    /// Crítica de link da porta LAN no FortiGate 40F (a exemplo do fluxo Cisco / HPE).
    /// Verifica se a porta LAN (Porta 1 / lan1 / Giga 1) ou o switch LAN está UP.
    /// Se estiver DOWN (ou conectado na WAN por engano), alerta o operador para conectar o cabo.
    /// </summary>
    public static async Task<bool> EnforceLanPortConnectedAsync(
        DeviceSession session,
        string lanInterface = "lan",
        Func<string, CancellationToken, Task>? requestOperatorAction = null,
        Func<string, Task>? progress = null,
        CancellationToken cancellationToken = default,
        int pollDelayMs = 2000)
    {
        ArgumentNullException.ThrowIfNull(session);

        // Janela de estabilização pós-configuração (evita falso DOWN inicial)
        if (pollDelayMs > 50)
            await Task.Delay(pollDelayMs, cancellationToken);

        for (var attempt = 1; attempt <= 15; attempt++)
        {
            var output = await session.SendCommandAsync("get system interface physical", TimeSpan.FromSeconds(8), cancellationToken);

            // Verifica se alguma das portas LAN físicas (lan1, lan2, lan3) ou o switch lan está UP
            var isLan1Up = IsPhysicalInterfaceUp(output, "lan1") || IsPhysicalInterfaceUp(output, "port1") || IsPhysicalInterfaceUp(output, "internal1");
            var isLan2Up = IsPhysicalInterfaceUp(output, "lan2") || IsPhysicalInterfaceUp(output, "port2") || IsPhysicalInterfaceUp(output, "internal2");
            var isLan3Up = IsPhysicalInterfaceUp(output, "lan3") || IsPhysicalInterfaceUp(output, "port3") || IsPhysicalInterfaceUp(output, "internal3");
            var isWanUp  = IsPhysicalInterfaceUp(output, "wan")  || IsPhysicalInterfaceUp(output, "wan1")  || IsPhysicalInterfaceUp(output, "port4");

            var isAnyLanUp = isLan1Up || isLan2Up || isLan3Up;

            // Se 'get system interface physical' não detalhou portas (ex: outro modelo), faz fallback com diagnose
            if (!isAnyLanUp && !isWanUp)
            {
                var diagNet = await session.SendCommandAsync("diagnose netlink interface list", TimeSpan.FromSeconds(8), cancellationToken);
                isLan1Up = IsNetlinkInterfaceUp(diagNet, "lan1") || IsNetlinkInterfaceUp(diagNet, "port1");
                isLan2Up = IsNetlinkInterfaceUp(diagNet, "lan2") || IsNetlinkInterfaceUp(diagNet, "port2");
                isLan3Up = IsNetlinkInterfaceUp(diagNet, "lan3") || IsNetlinkInterfaceUp(diagNet, "port3");
                isWanUp  = IsNetlinkInterfaceUp(diagNet, "wan")  || IsNetlinkInterfaceUp(diagNet, "wan1");
                isAnyLanUp = isLan1Up || isLan2Up || isLan3Up || IsNetlinkInterfaceUp(diagNet, "lan") || IsNetlinkInterfaceUp(diagNet, "internal");
            }

            if (isAnyLanUp)
            {
                var portaAtiva = isLan1Up ? "Porta 1 (Giga 1 / lan1)" :
                                 isLan2Up ? "Porta 2 (Giga 2 / lan2)" :
                                 isLan3Up ? "Porta 3 (Giga 3 / lan3)" : "LAN";
                if (progress is not null)
                    await progress($"[OK] Link físico confirmado na porta LAN do FortiGate: {portaAtiva} (1000 Mbps Full-Duplex).");
                return true;
            }

            if (isWanUp && !isAnyLanUp)
            {
                if (progress is not null)
                    await progress("[CRÍTICA DE PORTA] Cabo de rede detectado na porta WAN (Uplink) ao invés da porta LAN (Giga 1)!");

                if (requestOperatorAction is not null && (attempt == 1 || attempt % 5 == 0))
                {
                    await requestOperatorAction(
                        "❌ CABO DE REDE CONECTADO NA PORTA INCORRETA (WAN)!\n\n" +
                        "O cabo Ethernet está conectado na porta WAN do FortiGate 40F.\n\n" +
                        "👉 POR FAVOR, ALTERE O CABO DE REDE PARA A PORTA:\n" +
                        "🟢 PORTA 1 (LAN / Giga 1 - lan1) do FortiGate 40F\n\n" +
                        "Todos os procedimentos de provisionamento, teste ICMP e banda são executados EXCLUSIVAMENTE pela porta LAN.\n\n" +
                        "Clique em OK após conectar o cabo na Porta 1 (LAN).",
                        cancellationToken);
                }
            }
            else if (!isAnyLanUp)
            {
                if (progress is not null)
                {
                    if (attempt == 1)
                        await progress("[AVISO] Porta LAN (Giga 1 / Porta 1) sem link físico (DOWN). Aguardando conexão do cabo de rede...");
                    else
                        await progress($"    ⏳ [LINK LAN] Tentativa {attempt}/15: aguardando estabelecimento de link físico na Porta 1 (lan1)...");
                }

                if (requestOperatorAction is not null && (attempt == 1 || attempt % 5 == 0))
                {
                    await requestOperatorAction(
                        "⚠️ CABO DE REDE ETHERNET DESCONECTADO (PORTA LAN DOWN)!\n\n" +
                        "A porta LAN do FortiGate 40F está sem sinal de link (DOWN).\n\n" +
                        "👉 POR FAVOR, CONECTE O CABO DE REDE ETHERNET NA PORTA:\n" +
                        "🟢 PORTA 1 (Giga 1 / LAN - lan1) do FortiGate 40F\n\n" +
                        "Certifique-se de que a outra ponta está conectada na placa de rede do computador e que o LED da porta física está aceso.\n\n" +
                        "Clique em OK assim que o cabo estiver conectado.",
                        cancellationToken);
                }
            }

            if (pollDelayMs > 0)
                await Task.Delay(pollDelayMs, cancellationToken);
        }

        if (progress is not null)
            await progress("[AVISO] Tempo limite de espera para link na porta LAN excedido. Prosseguindo...");
        return false;
    }

    private static bool IsPhysicalInterfaceUp(string output, string ifName)
    {
        if (string.IsNullOrWhiteSpace(output)) return false;
        // Localiza o bloco ==[ifName] até o próximo ==[ ou fim da string
        var match = Regex.Match(output, $@"(?i)==\s*\[\s*{Regex.Escape(ifName)}\s*\](?<content>[\s\S]*?)(?:==\s*\[|\z)");
        if (match.Success)
        {
            var content = match.Groups["content"].Value;
            var isUp = Regex.IsMatch(content, @"(?i)\bstatus:\s*up\b");
            var hasSpeed = Regex.IsMatch(content, @"(?i)\bspeed:\s*\d+");
            return isUp || hasSpeed;
        }
        return false;
    }

    private static bool IsNetlinkInterfaceUp(string diagNet, string ifName)
    {
        if (string.IsNullOrWhiteSpace(diagNet)) return false;
        // Localiza o bloco if=ifName até o próximo if= ou fim da string
        var match = Regex.Match(diagNet, $@"(?i)\bif={Regex.Escape(ifName)}\b(?<content>[\s\S]*?)(?:\bif=|\z)");
        if (match.Success)
        {
            var content = match.Groups["content"].Value;
            var hasCarrier = !content.Contains("no_carrier", StringComparison.OrdinalIgnoreCase);
            var hasRun = Regex.IsMatch(content, @"(?i)\bflags=[^\r\n]*\brun\b") ||
                         Regex.IsMatch(content, @"(?i)\bstate:\s*UP\b") ||
                         Regex.IsMatch(content, @"(?i)\bcarrier:\s*ON\b");
            return hasCarrier && hasRun;
        }
        return false;
    }
}
