using System.Text.RegularExpressions;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Core.Provisioning;

public sealed class HpeSaipConfigurator
{
    private static readonly Regex InterfaceLineRegex = new(
        @"^(?<iface>(?:GigabitEthernet|GE|Ten-GigabitEthernet|Vlan-interface|Bridge-Aggregation)\S*)\s+",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private readonly Func<string, Task>? _progress;

    public HpeSaipConfigurator(Func<string, Task>? progress = null)
    {
        _progress = progress;
    }

    /// <summary>
    /// Detecta os nomes exatos das interfaces WAN (GE0) e LAN (GE1) no HPE Comware.
    /// </summary>
    public static (string wanIface, string lanIface) DetectInterfaces(string displayBriefOutput, string preferredWan = "GigabitEthernet0/0", string preferredLan = "GigabitEthernet0/1")
    {
        var matches = InterfaceLineRegex.Matches(displayBriefOutput);
        var ifaces = new List<string>();
        foreach (Match m in matches)
        {
            var name = m.Groups["iface"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name))
                ifaces.Add(name);
        }

        if (ifaces.Count == 0)
            return (preferredWan, preferredLan);

        // WAN (GE 0)
        string resolvedWan = preferredWan;
        var foundWan = ifaces.FirstOrDefault(i => i.Equals("GigabitEthernet0/0", StringComparison.OrdinalIgnoreCase) ||
                                                  i.Equals("GigabitEthernet0", StringComparison.OrdinalIgnoreCase) ||
                                                  i.Equals("GE0/0", StringComparison.OrdinalIgnoreCase) ||
                                                  i.Equals("GE0", StringComparison.OrdinalIgnoreCase));
        if (foundWan != null)
            resolvedWan = foundWan;
        else if (ifaces.Count > 0)
            resolvedWan = ifaces[0];

        // LAN (GE 1)
        string resolvedLan = preferredLan;
        var foundLan = ifaces.FirstOrDefault(i => i.Equals("GigabitEthernet0/1", StringComparison.OrdinalIgnoreCase) ||
                                                  i.Equals("GigabitEthernet1", StringComparison.OrdinalIgnoreCase) ||
                                                  i.Equals("GE0/1", StringComparison.OrdinalIgnoreCase) ||
                                                  i.Equals("GE1", StringComparison.OrdinalIgnoreCase));
        if (foundLan != null)
            resolvedLan = foundLan;
        else if (ifaces.Count > 1)
            resolvedLan = ifaces[1];

        return (resolvedWan, resolvedLan);
    }

    /// <summary>
    /// Gera a lista de comandos CLI HPE Comware para provisionamento da Ficha SAIP com Telnet EBT/PRO1AN.
    /// </summary>
    public static IReadOnlyList<string> GenerateCommands(
        SaipCircuitData circuit,
        string wanInterface = "GigabitEthernet0/0",
        string lanInterface = "GigabitEthernet0/1")
    {
        var wanDesc = SanitizeDescription(circuit.DesignacaoIp ?? circuit.NumeroOts ?? "LINK");
        var lanDesc = SanitizeDescription(circuit.ClienteRazaoSocial);

        return new List<string>
        {
            "system-view",

            // 1. WAN (GE 0 - Modo Router)
            $"interface {wanInterface}",
            "port link-mode route",
            $"description WAN_EBT_{wanDesc}",
            $"ip address {circuit.WanIp} {circuit.WanSubnetMask}",
            "undo shutdown",
            "quit",

            // 2. LAN (GE 1 - Mudar modo para Router)
            $"interface {lanInterface}",
            "port link-mode route",
            $"description LAN_CLIENTE_{lanDesc}",
            $"ip address {circuit.LanIp} {circuit.LanSubnetMask}",
            "undo shutdown",
            "quit",

            // 3. Rota Default (Gateway)
            $"ip route-static 0.0.0.0 0.0.0.0 {circuit.WanGateway}",

            // 4. Usuário e Acesso Remoto Telnet (EBT / PRO1AN)
            "telnet server enable",
            "local-user EBT class manage",
            "password simple PRO1AN",
            "service-type telnet terminal",
            "authorization-attribute user-role network-admin",
            "authorization-attribute user-role level-15",
            "authorization-attribute user-role level-3",
            "quit",

            // Linha VTY (Comware 7)
            "line vty 0 4",
            "authentication-mode scheme",
            "user-role network-admin",
            "user-role level-15",
            "user-role level-3",
            "protocol inbound telnet",
            "quit",

            // Linha VTY (Comware 5 / Legado)
            "user-interface vty 0 4",
            "authentication-mode scheme",
            "user-role level-3",
            "user-role level-15",
            "protocol inbound telnet",
            "quit",

            // 5. Salvar Configuração
            "return",
            "save force"
        };
    }

    /// <summary>
    /// Aplica a configuração do circuito SAIP no roteador HPE conectado com cadência de 1s entre comandos.
    /// </summary>
    public async Task ApplyConfigAsync(
        DeviceSession session,
        SaipCircuitData circuit,
        string wanInterface = "GigabitEthernet0/0",
        string lanInterface = "GigabitEthernet0/1",
        CancellationToken cancellationToken = default)
    {
        await ProgressAsync($"[*] INICIANDO PROVISIONAMENTO HPE COMWARE ({circuit.DesignacaoIp ?? circuit.NumeroOts})...");

        // 1. Acorda o terminal
        var promptResp = await session.SendCommandAsync(string.Empty, TimeSpan.FromSeconds(5), cancellationToken);
        await Task.Delay(500, cancellationToken);

        // Se estiver em submodo (ex: [HPE-GigabitEthernet0/0]), envia return para voltar à raiz
        if (promptResp.Contains("-") || (session.CurrentPrompt != null && session.CurrentPrompt.Contains("-")))
        {
            await session.SendCommandAsync("return", TimeSpan.FromSeconds(5), cancellationToken);
            await Task.Delay(500, cancellationToken);
            promptResp = await session.SendCommandAsync(string.Empty, TimeSpan.FromSeconds(5), cancellationToken);
        }

        // 2. Garante que está no system-view
        var isInSystemView = promptResp.Contains("[") || (session.CurrentPrompt != null && session.CurrentPrompt.StartsWith("["));
        if (!isInSystemView)
        {
            await ProgressAsync("[*] Entrando em modo de sistema (system-view)...");
            await session.SendCommandAsync("system-view", TimeSpan.FromSeconds(5), cancellationToken);
            await Task.Delay(1000, cancellationToken);
        }

        // 3. Verifica interfaces disponíveis com display interface brief
        string briefOutput = string.Empty;
        try
        {
            briefOutput = await session.SendCommandAsync("display interface brief", TimeSpan.FromSeconds(15), cancellationToken);
            var (detectedWan, detectedLan) = DetectInterfaces(briefOutput, wanInterface, lanInterface);
            wanInterface = detectedWan;
            lanInterface = detectedLan;
            await ProgressAsync($"[*] Interfaces HPE detectadas: WAN -> '{wanInterface}' | LAN -> '{lanInterface}'");
        }
        catch
        {
            // Usa as portas padrão
        }
        await Task.Delay(1000, cancellationToken);

        // 4. Monta e envia os comandos com tratamento de [Y/N] e fallback de senha
        var commands = GenerateCommands(circuit, wanInterface, lanInterface);

        await ProgressAsync("[*] Aplicando comandos no roteador HPE (intervalo: 1 segundo por comando)...");
        foreach (var cmd in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (cmd == "system-view")
                continue;

            try
            {
                var response = await session.SendExpectAsync(cmd,
                    new StopCondition[] { new StopCondition.Contains("[Y/N]:", "[Y/N]:"), new StopCondition.Prompt() },
                    TimeSpan.FromSeconds(15), cancellationToken);

                if (response.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase))
                {
                    await session.WriteLineAsync("Y", cancellationToken);
                    await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(10), cancellationToken);
                    // Reenvia o próximo comando (ip address) já será enviado na próxima iteração após o Y
                    if (cmd.Contains("port link-mode route", StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Delay(800, cancellationToken);
                        continue;
                    }
                }

                bool IsError(string o) => o.Contains("Wrong parameter", StringComparison.OrdinalIgnoreCase)
                    || o.Contains("Unrecognized", StringComparison.OrdinalIgnoreCase)
                    || o.Contains("Too many parameters", StringComparison.OrdinalIgnoreCase)
                    || o.Contains("Error", StringComparison.OrdinalIgnoreCase)
                    || o.Contains("%", StringComparison.OrdinalIgnoreCase);

                // Fallback de senha: 'password simple PRO1AN' rejeitada em Comware 7.1.064 (Wrong parameter / Too many parameters no EBT)
                if (IsError(response.Output) && cmd.StartsWith("password simple", StringComparison.OrdinalIgnoreCase))
                {
                    await ProgressAsync($"    [AVISO] Senha 'PRO1AN' rejeitada pelo Comware ({response.Output.Trim().Split('\n').LastOrDefault()?.Trim()}). Tentando alternativa...");
                    string senhaAplicada = string.Empty;
                    // Comware 7 no MSR954 rejeitou PRO1AN/PRO1AN123/EBT@2024 -> conforme solicitado usa PRO1ANPRO1AN
                    var alts = new[] { ("password simple PRO1ANPRO1AN", "PRO1ANPRO1AN") };
                    foreach (var (altCmd, senha) in alts)
                    {
                        var r2 = await session.SendExpectAsync(altCmd,
                            new StopCondition[] { new StopCondition.Contains("[Y/N]:", "[Y/N]:"), new StopCondition.Prompt() },
                            TimeSpan.FromSeconds(10), cancellationToken);
                        if (!IsError(r2.Output))
                        {
                            senhaAplicada = senha;
                            await ProgressAsync($"    [INFO] Senha alternativa aceita: '{senha}' (comando: {altCmd})");
                            break;
                        }
                        else
                        {
                            await ProgressAsync($"    [AVISO] Alternativa '{altCmd}' também rejeitada: {r2.Output.Trim().Split('\n').LastOrDefault()?.Trim()}");
                        }
                    }
                    if (!string.IsNullOrEmpty(senhaAplicada))
                    {
                        await ProgressAsync($"\n=================================================================");
                        await ProgressAsync($"   🔑 SENHA APLICADA NO EQUIPAMENTO: {senhaAplicada} (usuário EBT)   ");
                        await ProgressAsync($"=================================================================");
                    }
                    else
                        await ProgressAsync($"[!] Nenhuma sintaxe de senha foi aceita para o usuário EBT - verifique política de senha do Comware.");
                }
                else if (cmd.StartsWith("password simple", StringComparison.OrdinalIgnoreCase) && !IsError(response.Output))
                {
                    await ProgressAsync($"\n=================================================================");
                    await ProgressAsync($"   🔑 SENHA APLICADA NO EQUIPAMENTO: PRO1AN (usuário EBT)       ");
                    await ProgressAsync($"=================================================================");
                }
                else if (response.Output.Contains("Unrecognized command", StringComparison.OrdinalIgnoreCase) && cmd.StartsWith("ip address", StringComparison.OrdinalIgnoreCase))
                {
                    // Se ip address ainda falha após port link-mode, reenvia uma vez após delay
                    await Task.Delay(1000, cancellationToken);
                    var r2 = await session.SendCommandAsync(cmd, TimeSpan.FromSeconds(10), cancellationToken);
                    if (r2.Contains("Unrecognized", StringComparison.OrdinalIgnoreCase))
                        await ProgressAsync($"    [AVISO] Resposta do HPE ao comando '{cmd}': {r2.Trim()}");
                }
                else if (response.Output.Contains("Wrong parameter", StringComparison.OrdinalIgnoreCase) ||
                         response.Output.Contains("Unrecognized command", StringComparison.OrdinalIgnoreCase))
                {
                    await ProgressAsync($"    [AVISO] Resposta do HPE ao comando '{cmd}': {response.Output.Trim()}");
                }
            }
            catch (Exception ex)
            {
                await ProgressAsync($"    [!] Falha ao executar '{cmd}': {ex.Message}");
            }

            await Task.Delay(1000, cancellationToken);
        }

        await ProgressAsync("[OK] PROVISIONAMENTO HPE CONCLUÍDO COM SUCESSO!");
        await ProgressAsync("    -> Acesso Telnet: usuário 'EBT' com senha informada acima | Telnet server enable ativo");
        await ProgressAsync("    -> Guarde a senha exibida (PRO1AN ou alternativa) para acesso Telnet/SSH ao equipamento.");
    }

    private async Task ProgressAsync(string message)
    {
        if (_progress is not null)
            await _progress(message);
    }

    public static string SanitizeDescription(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "LINK";

        var clean = text.Trim();
        clean = Regex.Replace(clean, @"[^\u0000-\u007F]+", string.Empty);
        clean = Regex.Replace(clean, @"[^A-Za-z0-9_\-\./]", "_");
        clean = Regex.Replace(clean, @"_+", "_").Trim('_');

        if (clean.Length > 28)
            clean = clean[..28];

        return string.IsNullOrWhiteSpace(clean) ? "LINK" : clean;
    }
}
