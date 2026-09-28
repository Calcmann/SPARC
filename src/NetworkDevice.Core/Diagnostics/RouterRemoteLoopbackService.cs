using System.Text;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Core.Diagnostics;

public enum RouterLoopbackMethod
{
    // Cisco IOS / IOS-XE
    CiscoInterfaceLoopbackInternal,  // Loopback PHY/Hardware na interface
    CiscoCarrierMacSwap,             // Carrier Ethernet L2 MAC-Swap (ASR / Metro / ME)
    CiscoIpSlaResponderUdpEcho,      // IP SLA Responder UDP Echo para QT (L3/L4)
    CiscoIpSlaResponderGlobal,       // IP SLA Responder Global

    // HPE Comware (MSR 954 / 930 / 1002 / Comware 5 e 7)
    HpeInterfaceLoopbackInternal,    // Loopback interno na interface
    HpeNqaServerUdpEcho,             // NQA Server UDP Echo para QT (Equivalente ao IP SLA)
    HpeInterfaceLoopbackExternal,    // Loopback externo na interface

    // Fortinet FortiGate (FortiGate 40F / FortiOS)
    FortiGateNicLoopbackInternal,    // diagnose hardware device nic loopback enable <iface> internal
    FortiGateProbeResponsePing,      // config system interface -> allowaccess ping probe-response
    FortiGateSnifferMonitor,         // diagnose sniffer packet <iface> 'udp port <port>' 4 0 l

    // Genérico / Datacom Carrier Ethernet
    GenericCarrierMacSwap,           // Carrier Ethernet loopback mac-swap
    GenericInterfaceLoopback         // Loopback PHY interno
}

public sealed record RouterLoopbackMethodInfo(
    RouterLoopbackMethod Method,
    string Name,
    string Description,
    string LayerCategory,
    bool RequiresIp,
    bool RequiresPort,
    string TypicalInterface
);

public sealed record RouterLoopbackProfile(
    string Id,
    string DisplayName,
    DeviceManufacturer Manufacturer,
    IReadOnlyList<RouterLoopbackMethodInfo> SupportedMethods,
    string DefaultInterface,
    string Description
);

public sealed record RouterLoopbackConfig(
    RouterLoopbackProfile Profile,
    RouterLoopbackMethod Method,
    string InterfaceName,
    string? TargetIp = null,
    int Port = 5001,
    int SafetyRollbackMinutes = 30
);

public sealed record RouterLoopbackScriptResult(
    IReadOnlyList<string> EnableCommands,
    IReadOnlyList<string> DisableCommands,
    string Explanation,
    string? RollbackTimerCommand = null,
    string? RollbackCancelCommand = null
);

public sealed record RouterExecutionResult(
    bool Success,
    string Output,
    string? ErrorMessage = null
);

public static class RouterRemoteLoopbackService
{
    public static readonly IReadOnlyList<RouterLoopbackProfile> Profiles = new List<RouterLoopbackProfile>
    {
        new(
            Id: "cisco_ios",
            DisplayName: "🔵 Cisco IOS / IOS-XE (ISR 1900 / 921 / 841 / ASR / Catalyst)",
            Manufacturer: DeviceManufacturer.Cisco,
            DefaultInterface: "GigabitEthernet0/0/0",
            Description: "Roteadores Cisco das famílias ISR G2 (1900), ISR 900 (921), 800M (841), ISR 4000 e ASR 900/920.",
            SupportedMethods: new List<RouterLoopbackMethodInfo>
            {
                new(
                    RouterLoopbackMethod.CiscoInterfaceLoopbackInternal,
                    "Loopback de Hardware / PHY (loopback internal)",
                    "Espelha os sinais físicos de RX diretamente para o TX no chip PHY da porta Ethernet. Ideal para testes de camada 1/2 com o QT.",
                    "L1/L2 Físico (PHY)",
                    RequiresIp: false,
                    RequiresPort: false,
                    TypicalInterface: "GigabitEthernet0/0/0"
                ),
                new(
                    RouterLoopbackMethod.CiscoCarrierMacSwap,
                    "Carrier Ethernet MAC-Swap (ethernet loopback local)",
                    "Inverte o MAC de origem e destino nos frames Ethernet em hardware (Line-rate / 1 Gbps sem CPU). Padrão Metro/Carrier.",
                    "L2 Carrier (MAC-Swap)",
                    RequiresIp: false,
                    RequiresPort: false,
                    TypicalInterface: "GigabitEthernet0/0/1"
                ),
                new(
                    RouterLoopbackMethod.CiscoIpSlaResponderUdpEcho,
                    "IP SLA Responder UDP-Echo (Específico por IP e Porta)",
                    "Responde a testes UDP de precisão do QT/JDSU/VIAVI com timestamps precisos de ida e volta sem sobrecarregar o roteador.",
                    "L3/L4 UDP (IP SLA)",
                    RequiresIp: true,
                    RequiresPort: true,
                    TypicalInterface: "GigabitEthernet0/0/0"
                ),
                new(
                    RouterLoopbackMethod.CiscoIpSlaResponderGlobal,
                    "IP SLA Responder Global (ip sla responder)",
                    "Ativa o responder IP SLA em todas as interfaces do roteador para escutar pacotes de controle e dados do testador.",
                    "L3/L4 UDP Global",
                    RequiresIp: false,
                    RequiresPort: false,
                    TypicalInterface: "GigabitEthernet0/0/0"
                )
            }
        ),
        new(
            Id: "hpe_comware",
            DisplayName: "🟣 HPE Comware (MSR 954 / 930 / 1002 / Comware 5 e 7)",
            Manufacturer: DeviceManufacturer.Hpe,
            DefaultInterface: "GigabitEthernet0/0",
            Description: "Roteadores HPE MSR Series (MSR954, MSR930, MSR1002) e switches Comware.",
            SupportedMethods: new List<RouterLoopbackMethodInfo>
            {
                new(
                    RouterLoopbackMethod.HpeInterfaceLoopbackInternal,
                    "Loopback Interno de Interface (loopback internal)",
                    "Coloca a porta Ethernet do MSR em loopback interno elétrico. Todos os quadros recebidos retornam à fonte.",
                    "L1/L2 Físico (Interno)",
                    RequiresIp: false,
                    RequiresPort: false,
                    TypicalInterface: "GigabitEthernet0/0"
                ),
                new(
                    RouterLoopbackMethod.HpeNqaServerUdpEcho,
                    "NQA Server UDP-Echo (Equivalente ao IP SLA Responder)",
                    "Habilita o servidor NQA (Network Quality Analyzer) do Comware para responder aos pacotes UDP de teste do QT com alta fidelidade.",
                    "L3/L4 UDP (NQA Server)",
                    RequiresIp: true,
                    RequiresPort: true,
                    TypicalInterface: "GigabitEthernet0/0"
                ),
                new(
                    RouterLoopbackMethod.HpeInterfaceLoopbackExternal,
                    "Loopback Externo de Interface (loopback external)",
                    "Configura a interface Ethernet para loopback externo (útil com plug ou acoplador externo).",
                    "L1/L2 Físico (Externo)",
                    RequiresIp: false,
                    RequiresPort: false,
                    TypicalInterface: "GigabitEthernet0/1"
                )
            }
        ),
        new(
            Id: "fortinet_fortigate",
            DisplayName: "🔴 Fortinet FortiGate (FortiGate 40F / FortiOS 6.x e 7.x)",
            Manufacturer: DeviceManufacturer.Fortinet,
            DefaultInterface: "internal1",
            Description: "Firewalls e Roteadores FortiGate (FortiGate 40F, 60F, FortiOS).",
            SupportedMethods: new List<RouterLoopbackMethodInfo>
            {
                new(
                    RouterLoopbackMethod.FortiGateNicLoopbackInternal,
                    "Diagnose NIC Loopback (Hardware Internal)",
                    "Comando direto de hardware na placa de rede do FortiGate para devolver tráfego no nível de driver (diagnose hardware device nic loopback).",
                    "L1/L2 Hardware NIC",
                    RequiresIp: false,
                    RequiresPort: false,
                    TypicalInterface: "internal1"
                ),
                new(
                    RouterLoopbackMethod.FortiGateProbeResponsePing,
                    "Probe Response & Ping na Interface (L3 Echo)",
                    "Habilita allowaccess ping e probe-response para resposta imediata a sondas de SLA e testes de link do QT.",
                    "L3 Gestão / Probe",
                    RequiresIp: false,
                    RequiresPort: false,
                    TypicalInterface: "wan"
                ),
                new(
                    RouterLoopbackMethod.FortiGateSnifferMonitor,
                    "Monitor Sniffer de Tráfego do QT em Tempo Real",
                    "Comando de inspeção de tráfego que exibe em tempo real os pacotes UDP do QT chegando na interface.",
                    "Diagnóstico / Sniffer",
                    RequiresIp: false,
                    RequiresPort: true,
                    TypicalInterface: "wan"
                )
            }
        ),
        new(
            Id: "generic_carrier",
            DisplayName: "⚪ Genérico / Datacom (Carrier Ethernet L2/L3)",
            Manufacturer: DeviceManufacturer.Generic,
            DefaultInterface: "eth-0-1",
            Description: "Equipamentos de acesso e demarcadores MetroEthernet / EDD (Datacom DmOS, Ciena, etc.).",
            SupportedMethods: new List<RouterLoopbackMethodInfo>
            {
                new(
                    RouterLoopbackMethod.GenericCarrierMacSwap,
                    "Carrier Ethernet MAC-Swap (L2 Reflector)",
                    "Inverte os endereços MAC para devolução de quadros L2 sem processamento de software.",
                    "L2 Carrier (MAC-Swap)",
                    RequiresIp: false,
                    RequiresPort: false,
                    TypicalInterface: "eth-0-1"
                ),
                new(
                    RouterLoopbackMethod.GenericInterfaceLoopback,
                    "Loopback de Porta (loopback internal)",
                    "Loopback de hardware padrão na porta de demarcador.",
                    "L1/L2 Físico",
                    RequiresIp: false,
                    RequiresPort: false,
                    TypicalInterface: "eth-0-1"
                )
            }
        )
    };

    public static RouterLoopbackProfile GetProfileForDevice(DeviceManufacturer manufacturer, DeviceSeries series)
    {
        return manufacturer switch
        {
            DeviceManufacturer.Cisco => Profiles[0],
            DeviceManufacturer.Hpe => Profiles[1],
            DeviceManufacturer.Fortinet => Profiles[2],
            _ => Profiles[3]
        };
    }

    public static RouterLoopbackScriptResult GenerateScript(RouterLoopbackConfig config)
    {
        var enable = new List<string>();
        var disable = new List<string>();
        string explanation;
        string? rollbackCmd = null;
        string? rollbackCancelCmd = null;

        var iface = string.IsNullOrWhiteSpace(config.InterfaceName) ? "GigabitEthernet0/0/0" : config.InterfaceName.Trim();
        var ip = string.IsNullOrWhiteSpace(config.TargetIp) ? "192.168.1.1" : config.TargetIp.Trim();
        var port = config.Port > 0 ? config.Port : 5001;

        switch (config.Method)
        {
            case RouterLoopbackMethod.CiscoInterfaceLoopbackInternal:
                if (config.SafetyRollbackMinutes > 0)
                {
                    rollbackCmd = $"reload in {config.SafetyRollbackMinutes}";
                    rollbackCancelCmd = "reload cancel";
                }
                enable.AddRange(new[]
                {
                    "configure terminal",
                    $"interface {iface}",
                    " no shutdown",
                    " loopback internal",
                    "end"
                });
                disable.AddRange(new[]
                {
                    "configure terminal",
                    $"interface {iface}",
                    " no loopback",
                    "end"
                });
                explanation = $"Coloca a porta {iface} em loopback elétrico interno PHY. Qualquer frame enviado pelo QT/JDSU nesta porta será imediatamente refletido na camada física.";
                break;

            case RouterLoopbackMethod.CiscoCarrierMacSwap:
                if (config.SafetyRollbackMinutes > 0)
                {
                    rollbackCmd = $"reload in {config.SafetyRollbackMinutes}";
                    rollbackCancelCmd = "reload cancel";
                }
                enable.AddRange(new[]
                {
                    "configure terminal",
                    $"interface {iface}",
                    " ethernet loopback local mac-swap",
                    "end"
                });
                disable.AddRange(new[]
                {
                    "configure terminal",
                    $"interface {iface}",
                    " no ethernet loopback local",
                    "end"
                });
                explanation = $"Habilita o refletor L2 de hardware na porta {iface}. Os quadros do QT terão os campos MAC Origem e Destino invertidos e reenviados em Line-Rate (1 Gbps) com zero sobrecarga de CPU.";
                break;

            case RouterLoopbackMethod.CiscoIpSlaResponderUdpEcho:
                enable.AddRange(new[]
                {
                    "configure terminal",
                    "ip sla responder",
                    $"ip sla responder udp-echo ipaddress {ip} port {port}",
                    "end"
                });
                disable.AddRange(new[]
                {
                    "configure terminal",
                    $"no ip sla responder udp-echo ipaddress {ip} port {port}",
                    "no ip sla responder",
                    "end"
                });
                explanation = $"Ativa o responder L3 UDP na porta {port} e IP {ip}. Responde aos fluxos de teste do QT/VIAVI/JDSU com carimbos precisos de timestamp.";
                break;

            case RouterLoopbackMethod.CiscoIpSlaResponderGlobal:
                enable.AddRange(new[]
                {
                    "configure terminal",
                    "ip sla responder",
                    "end"
                });
                disable.AddRange(new[]
                {
                    "configure terminal",
                    "no ip sla responder",
                    "end"
                });
                explanation = "Habilita o responder IP SLA globalmente no roteador Cisco para escutar testes de medição ativa.";
                break;

            case RouterLoopbackMethod.HpeInterfaceLoopbackInternal:
                if (config.SafetyRollbackMinutes > 0)
                {
                    rollbackCmd = $"schedule reboot delay {config.SafetyRollbackMinutes}";
                    rollbackCancelCmd = "undo schedule reboot";
                }
                enable.AddRange(new[]
                {
                    "system-view",
                    $"interface {iface}",
                    " undo shutdown",
                    " loopback internal",
                    "quit",
                    "return"
                });
                disable.AddRange(new[]
                {
                    "system-view",
                    $"interface {iface}",
                    " undo loopback",
                    "quit",
                    "return"
                });
                explanation = $"Coloca a interface {iface} do roteador HPE MSR em loopback interno elétrico de camada física.";
                break;

            case RouterLoopbackMethod.HpeNqaServerUdpEcho:
                enable.AddRange(new[]
                {
                    "system-view",
                    "nqa server enable",
                    $"nqa server udp-echo {ip} {port}",
                    "quit",
                    "return"
                });
                disable.AddRange(new[]
                {
                    "system-view",
                    $"undo nqa server udp-echo {ip} {port}",
                    "undo nqa server enable",
                    "quit",
                    "return"
                });
                explanation = $"Habilita o servidor NQA Comware para responder a pacotes UDP do QT na porta {port} e IP {ip}.";
                break;

            case RouterLoopbackMethod.HpeInterfaceLoopbackExternal:
                enable.AddRange(new[]
                {
                    "system-view",
                    $"interface {iface}",
                    " loopback external",
                    "quit",
                    "return"
                });
                disable.AddRange(new[]
                {
                    "system-view",
                    $"interface {iface}",
                    " undo loopback",
                    "quit",
                    "return"
                });
                explanation = $"Configura a interface {iface} do HPE para loopback externo de porta.";
                break;

            case RouterLoopbackMethod.FortiGateNicLoopbackInternal:
                enable.AddRange(new[]
                {
                    $"diagnose hardware device nic loopback enable {iface} internal"
                });
                disable.AddRange(new[]
                {
                    $"diagnose hardware device nic loopback disable {iface}"
                });
                explanation = $"Ativa o loopback de hardware no controlador NIC da porta {iface} do FortiGate. Retorna imediatamente o tráfego físico.";
                break;

            case RouterLoopbackMethod.FortiGateProbeResponsePing:
                enable.AddRange(new[]
                {
                    "config system interface",
                    $"edit \"{iface}\"",
                    "append allowaccess ping probe-response",
                    "next",
                    "end"
                });
                disable.AddRange(new[]
                {
                    "config system interface",
                    $"edit \"{iface}\"",
                    "unselect allowaccess probe-response",
                    "next",
                    "end"
                });
                explanation = $"Libera respostas de sonda e ping na porta {iface} do FortiGate para medição do QT.";
                break;

            case RouterLoopbackMethod.FortiGateSnifferMonitor:
                enable.AddRange(new[]
                {
                    $"diagnose sniffer packet {iface} \"udp port {port}\" 4 0 l"
                });
                disable.AddRange(new[]
                {
                    "!"
                });
                explanation = $"Executa o sniffer no FortiGate capturando os pacotes de teste do QT na porta {port} da interface {iface}.";
                break;

            case RouterLoopbackMethod.GenericCarrierMacSwap:
                enable.AddRange(new[]
                {
                    "config",
                    $"interface {iface}",
                    " loopback mac-swap",
                    "exit",
                    "exit"
                });
                disable.AddRange(new[]
                {
                    "config",
                    $"interface {iface}",
                    " no loopback",
                    "exit",
                    "exit"
                });
                explanation = $"Habilita o MAC-Swap L2 na interface {iface} do demarcador / roteador Carrier Ethernet.";
                break;

            case RouterLoopbackMethod.GenericInterfaceLoopback:
            default:
                enable.AddRange(new[]
                {
                    "config",
                    $"interface {iface}",
                    " loopback internal",
                    "exit",
                    "exit"
                });
                disable.AddRange(new[]
                {
                    "config",
                    $"interface {iface}",
                    " no loopback",
                    "exit",
                    "exit"
                });
                explanation = $"Habilita loopback interno na interface {iface}.";
                break;
        }

        return new RouterLoopbackScriptResult(
            EnableCommands: enable,
            DisableCommands: disable,
            Explanation: explanation,
            RollbackTimerCommand: rollbackCmd,
            RollbackCancelCommand: rollbackCancelCmd
        );
    }

    public static async Task<RouterExecutionResult> ExecuteCommandsAsync(
        ITransport transport,
        IReadOnlyList<string> commands,
        Action<string>? logCallback = null,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        try
        {
            if (!transport.IsOpen)
            {
                await transport.OpenAsync(cancellationToken);
            }

            // Envia Enter inicial para acordar o console
            await SendLineAsync(transport, "", cancellationToken);
            await Task.Delay(200, cancellationToken);
            var initialRead = await DrainReadAsync(transport, cancellationToken);
            if (!string.IsNullOrEmpty(initialRead))
            {
                sb.Append(initialRead);
                logCallback?.Invoke(initialRead);
            }

            foreach (var cmd in commands)
            {
                cancellationToken.ThrowIfCancellationRequested();

                logCallback?.Invoke($"> {cmd}\r\n");
                sb.AppendLine($"> {cmd}");

                await SendLineAsync(transport, cmd, cancellationToken);
                await Task.Delay(250, cancellationToken);

                var response = await DrainReadAsync(transport, cancellationToken);
                if (!string.IsNullOrEmpty(response))
                {
                    sb.Append(response);
                    logCallback?.Invoke(response);
                }
            }

            return new RouterExecutionResult(true, sb.ToString());
        }
        catch (Exception ex)
        {
            var err = $"Erro ao comunicar com o roteador: {ex.Message}";
            logCallback?.Invoke($"❌ {err}\r\n");
            return new RouterExecutionResult(false, sb.ToString(), err);
        }
    }

    private static async Task SendLineAsync(ITransport transport, string line, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        await transport.WriteAsync(bytes, ct);
    }

    private static async Task<string> DrainReadAsync(ITransport transport, CancellationToken ct)
    {
        var buffer = new byte[2048];
        var sb = new StringBuilder();
        var deadline = DateTime.UtcNow.AddMilliseconds(500);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                var read = await transport.ReadAsync(buffer, ct);
                if (read > 0)
                {
                    var text = Encoding.UTF8.GetString(buffer, 0, read).Replace("\uFFFD", "");
                    sb.Append(text);
                    deadline = DateTime.UtcNow.AddMilliseconds(300); // prolonga se estiver chegando dados
                }
                else
                {
                    break;
                }
            }
            catch (TimeoutException)
            {
                break;
            }
            catch
            {
                break;
            }
        }

        return sb.ToString();
    }
}
