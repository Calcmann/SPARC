using System.Text;
using NetworkDevice.Cisco;
using NetworkDevice.Core.Backup;
using NetworkDevice.Core.Device;
using NetworkDevice.Core.Detection;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Routing;
using NetworkDevice.Core.Session;
using NetworkDevice.Protocols.Serial;
using NetworkDevice.Protocols.Ssh;

namespace NetworkDevice.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 2;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "serial":
                    return await RunSerialAsync(args[1..]);
                case "ssh":
                    return await RunSshAsync(args[1..]);
                case "ports":
                    return RunPorts();
                case "mock":
                    return await RunMockAsync();
                case "recover":
                    return await RunRecoverAsync(args[1..]);
                case "delete-forti-firmware":
                case "format-forti-bios":
                    return await RunDeleteFortiFirmwareAsync(args[1..]);
                case "detect":
                    return await RunDetectAsync(args[1..]);
                case "test-bios":
                    return await RunTestBiosAsync(args[1..]);
                default:
                    PrintUsage();
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERRO: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunSerialAsync(string[] args)
    {
        if (args.Length < 1)
        {
            PrintUsage();
            return 2;
        }

        var port = args[0];
        var baud = args.Length > 1 && int.TryParse(args[1], out var b) ? b : 9600;
        var user = Arg(args, 2);
        var password = Arg(args, 3);
        var enable = Arg(args, 4);
        var outDir = Arg(args, 5) ?? "backups";

        var transport = new SerialTransport(port, baud);
        await using var session = new DeviceSession(transport, CiscoIOSAdapter.CreateSessionOptions(enable, user, password));
        await session.ConnectAsync();
        return await DoWorkAsync(session, enable, outDir);
    }

    private static async Task<int> RunSshAsync(string[] args)
    {
        if (args.Length < 3)
        {
            PrintUsage();
            return 2;
        }

        var host = args[0];
        var user = args[1];
        var password = args[2];
        var port = args.Length > 3 && int.TryParse(args[3], out var p) ? p : 22;
        var enable = Arg(args, 4);
        var outDir = Arg(args, 5) ?? "backups";

        var policy = new SshHostKeyPolicy { AcceptUnknownHosts = true };
        var transport = new SshTransport(host, port, user, password, policy);
        await using var session = new DeviceSession(transport, CiscoIOSAdapter.CreateSessionOptions(enable));
        await session.ConnectAsync();
        return await DoWorkAsync(session, enable, outDir);
    }

    private static int RunPorts()
    {
        var ports = SerialPorts.Available();
        if (ports.Count == 0)
        {
            Console.WriteLine("Nenhuma porta COM/USB encontrada.");
            return 0;
        }

        Console.WriteLine("Portas COM/USB disponíveis:");
        foreach (var port in ports)
            Console.WriteLine($"  {port}");

        return 0;
    }

    private static async Task<int> RunMockAsync()
    {
        Console.WriteLine("[*] Demonstração com dispositivo Cisco IOS simulado (sem hardware).");
        var transport = MockCiscoDevice.CreateTransport();
        await using var session = new DeviceSession(transport, CiscoIOSAdapter.CreateSessionOptions("admin123"));
        await session.ConnectAsync();
        return await DoWorkAsync(session, "admin123", "backups");
    }

    private static async Task<int> RunRecoverAsync(string[] args)
    {
        if (args.Length < 1)
        {
            PrintUsage();
            return 2;
        }

        var port = args[0];
        var baud = args.Length > 1 && int.TryParse(args[1], out var b) ? b : 9600;
        var profileKey = args.Length > 2 ? args[2] : null;
        var profile = NetworkDevice.Core.Recovery.BootInterruptProfiles.FindById(profileKey);

        var transport = new SerialTransport(port, baud);
        await using var session = new DeviceSession(transport, CiscoIOSAdapter.CreateSessionOptions(null));

        var recovery = new CiscoIOSRecovery(
            async message =>
            {
                Console.WriteLine($"[rec] {message}");
                await Task.CompletedTask;
            },
            profile: profile);

        Console.WriteLine($"[*] Recuperação de senha e zeramento de configuração em {port} @ {baud} baud.");
        Console.WriteLine($"[*] Perfil selecionado: {profile.Name} (Id: {profile.Id}, Método: {profile.Method}).");
        Console.WriteLine("[!] O aplicativo solicitará o reload do equipamento. Siga as instruções.");
        await recovery.RecoverAndResetAsync(session, async (message, ct) =>
        {
            Console.WriteLine($"[reload] {message}");
            Console.WriteLine("    Pressione ENTER quando o reload for iniciado...");
            await Task.Run(() => Console.ReadLine(), ct);
        });
        Console.WriteLine("[*] Procedimento concluído.");
        return 0;
    }

    private static async Task<int> DoWorkAsync(DeviceSession session, string? enableSecret, string outDir)
    {
        var adapter = new CiscoIOSAdapter(enableSecret);

        Console.WriteLine("[*] Identificando equipamento...");
        var info = await adapter.IdentifyAsync(session);
        Console.WriteLine($"    {info.DisplayName}");
        Console.WriteLine($"    Vendor : {info.Vendor}");
        Console.WriteLine($"    Model  : {info.Model}");
        Console.WriteLine($"    OS     : {info.OsName} {info.OsVersion}");
        Console.WriteLine($"    Serial : {info.SerialNumber}");

        Console.WriteLine("[*] Obtendo running-config...");
        var config = await adapter.GetRunningConfigAsync(session);
        Console.WriteLine($"    {config.Count(c => c == '\n')} linhas de configuração.");

        Console.WriteLine("[*] Salvando backup com hash e relatório...");
        var backup = new ConfigBackupService(outDir);
        var result = await backup.SaveAsync(info, config, Environment.UserName);
        Console.WriteLine($"    Arquivo : {Path.GetFullPath(result.FilePath)}");
        Console.WriteLine($"    SHA256  : {result.Sha256}");
        Console.WriteLine($"    MD5     : {result.Md5}");
        Console.WriteLine($"    Report  : {Path.GetFullPath(result.ReportPath)}");

        Console.WriteLine("[*] OK.");
        return 0;
    }

    private static async Task<int> RunDeleteFortiFirmwareAsync(string[] args)
    {
        var port = args.Length > 0 ? args[0] : "COM1";
        var baud = args.Length > 1 && int.TryParse(args[1], out var b) ? b : 9600;

        Console.WriteLine("=================================================================");
        Console.WriteLine(" 🧹 FORMATAÇÃO DO BOOT DEVICE / FIRMWARE DO FORTIGATE 40F");
        Console.WriteLine($" Porta: {port} @ {baud} baud");
        Console.WriteLine("=================================================================");
        Console.WriteLine("Este procedimento acessará a BIOS (FortiBootLoader) e executará [F]");
        Console.WriteLine("para formatar o boot device e apagar as partições de firmware.");
        Console.WriteLine();

        var transport = new SerialTransport(port, baud);
        await transport.OpenAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var ct = cts.Token;

        var rxBuffer = new byte[2048];
        var accumulator = new StringBuilder();
        bool rebootSent = false;
        bool formatSent = false;
        bool confirmSent = false;
        bool formatCompleted = false;

        int loginAttempts = 0;
        var lastLoginTry = DateTime.MinValue;

        Console.WriteLine("\n=================================================================");
        Console.WriteLine("  🚀 MODO EXCLUSÃO DE FIRMWARE DO FORTIGATE 40F");
        Console.WriteLine("  Aguardando prompt de login ou reinicialização da BIOS...");
        Console.WriteLine("  💡 DICA: Se o roteador estiver no FortiOS com senha desconhecida,");
        Console.WriteLine("     DESLIGUE E LIGUE O CABO DE FORÇA DO FORTIGATE 40F AGORA.");
        Console.WriteLine("     O script interceptará a BIOS assim que a energia voltar!");
        Console.WriteLine("=================================================================\n");

        await transport.WriteAsync(Encoding.ASCII.GetBytes("\r\n"), ct);

        var lastKeepalive = DateTime.UtcNow;

        while (!ct.IsCancellationRequested && !formatCompleted)
        {
            var read = await transport.ReadAsync(rxBuffer, ct);
            if (read > 0)
            {
                var text = Encoding.ASCII.GetString(rxBuffer, 0, read);
                Console.Write(text);
                accumulator.Append(text);

                var current = accumulator.ToString();

                // 1. Detecção de Login do FortiOS
                if (!rebootSent && current.Contains("login:", StringComparison.OrdinalIgnoreCase) && (DateTime.UtcNow - lastLoginTry).TotalSeconds > 2)
                {
                    lastLoginTry = DateTime.UtcNow;
                    loginAttempts++;
                    Console.WriteLine($"\n[*] [LOGIN tentativa {loginAttempts}] Enviando 'admin'...");
                    accumulator.Clear();
                    await Task.Delay(200, ct);
                    await transport.WriteAsync(Encoding.ASCII.GetBytes("admin\r"), ct);
                    continue;
                }

                // 2. Envio da Senha
                if (!rebootSent && current.Contains("Password:", StringComparison.OrdinalIgnoreCase) && !current.Contains("New Password:", StringComparison.OrdinalIgnoreCase))
                {
                    accumulator.Clear();
                    await Task.Delay(200, ct);
                    if (loginAttempts <= 1)
                    {
                        Console.WriteLine("[*] [SENHA] Testando senha em branco (padrão de fábrica)...");
                        await transport.WriteAsync(Encoding.ASCII.GetBytes("\r"), ct);
                    }
                    else if (loginAttempts == 2)
                    {
                        Console.WriteLine("[*] [SENHA] Testando senha 'CQMR'...");
                        await transport.WriteAsync(Encoding.ASCII.GetBytes("CQMR\r"), ct);
                    }
                    else
                    {
                        Console.WriteLine("[*] [SENHA] Testando senha 'admin'...");
                        await transport.WriteAsync(Encoding.ASCII.GetBytes("admin\r"), ct);
                    }
                    continue;
                }

                // 3. Se pedir troca de senha forçada
                if (!rebootSent && current.Contains("New Password:", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("\n[*] [SENHA] Troca forçada detectada. Configurando senha temporária 'CQMR'...");
                    accumulator.Clear();
                    await Task.Delay(300, ct);
                    await transport.WriteAsync(Encoding.ASCII.GetBytes("CQMR\r"), ct);
                    await Task.Delay(500, ct);
                    await transport.WriteAsync(Encoding.ASCII.GetBytes("CQMR\r"), ct);
                    continue;
                }

                // 4. Se estiver no prompt operacional do FortiOS (# ou $)
                if (!rebootSent && (current.EndsWith("# ") || current.EndsWith("#") || current.EndsWith("$ ") || current.EndsWith("$") || current.Contains("# ") || current.Contains("$ ")))
                {
                    rebootSent = true;
                    Console.WriteLine("\n[*] [CLI] Prompt do FortiOS autenticado! Enviando 'execute reboot'...");
                    accumulator.Clear();
                    await Task.Delay(300, ct);
                    await transport.WriteAsync(Encoding.ASCII.GetBytes("execute reboot\r"), ct);
                    await Task.Delay(800, ct);
                    await transport.WriteAsync(Encoding.ASCII.GetBytes("y\r"), ct);
                    Console.WriteLine("[*] Comando de reboot enviado. Aguardando a BIOS subir...");
                    continue;
                }

                // 5. Interrupção do Boot da BIOS
                if (current.Contains("Press any key to display configuration menu", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("FortiBootLoader", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("BootLoader", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("Initializing boot device", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("Reading boot image", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("Ver:0", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("Serial number:", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("\n[⚡ BIOS] Sinais de inicialização da BIOS detectados! Enviando espaço para pausar...");
                    for (int i = 0; i < 5; i++)
                    {
                        await transport.WriteAsync(Encoding.ASCII.GetBytes(" "), ct);
                        await Task.Delay(50, ct);
                    }
                    accumulator.Clear();
                    continue;
                }

                // 6. Menu da BIOS ativo
                bool isBiosMenuPrompt = current.Contains("Enter Selection:", StringComparison.OrdinalIgnoreCase) ||
                                        current.Contains("Enter C,R,T,F,I,B,Q,or H:", StringComparison.OrdinalIgnoreCase);

                if (isBiosMenuPrompt && !formatSent)
                {
                    formatSent = true;
                    Console.WriteLine("\n[🧹 BIOS] Menu da BIOS detectado! Enviando comando [F] (Format boot device)...");
                    accumulator.Clear();
                    await Task.Delay(400, ct);
                    await transport.WriteAsync(Encoding.ASCII.GetBytes("F\r"), ct);
                    continue;
                }

                // 7. Confirmação de Formatação da Flash
                bool isEraseConfirm = current.Contains("It will erase data in boot device", StringComparison.OrdinalIgnoreCase) ||
                                      current.Contains("Continue? [yes/no]", StringComparison.OrdinalIgnoreCase) ||
                                      current.Contains("[yes/no]:", StringComparison.OrdinalIgnoreCase);

                if (isEraseConfirm && !confirmSent)
                {
                    confirmSent = true;
                    Console.WriteLine("\n[⚠️ CONFIRMAÇÃO] Pergunta de formatação detectada. Enviando 'yes'...");
                    accumulator.Clear();
                    await Task.Delay(400, ct);
                    await transport.WriteAsync(Encoding.ASCII.GetBytes("yes\r"), ct);
                    continue;
                }

                // 8. Formatação Concluída
                if (confirmSent && (current.Contains("Done.", StringComparison.OrdinalIgnoreCase) ||
                                    current.Contains("format done", StringComparison.OrdinalIgnoreCase) ||
                                    (isBiosMenuPrompt && (DateTime.UtcNow - lastLoginTry).TotalSeconds > 5)))
                {
                    formatCompleted = true;
                    Console.WriteLine("\n=================================================================");
                    Console.WriteLine("  ✅ FORMATAÇÃO DO BOOT DEVICE CONCLUÍDA COM SUCESSO!");
                    Console.WriteLine("  O firmware e partições do FortiGate 40F foram completamente apagados.");
                    Console.WriteLine("  O roteador está agora em MODO BIOS / SEM FIRMWARE (BootFailure).");
                    Console.WriteLine("=================================================================\n");
                    break;
                }
            }
            else
            {
                await Task.Delay(100, ct);
                if (!rebootSent && (DateTime.UtcNow - lastKeepalive).TotalSeconds >= 3)
                {
                    lastKeepalive = DateTime.UtcNow;
                    await transport.WriteAsync(Encoding.ASCII.GetBytes("\r"), ct);
                }
            }
        }

        await transport.CloseAsync();
        return formatCompleted ? 0 : 1;
    }

    private static async Task<int> RunDetectAsync(string[] args)
    {
        var port = args.Length > 0 ? args[0] : "COM1";
        var baud = args.Length > 1 && int.TryParse(args[1], out var b) ? b : 9600;

        Console.WriteLine($"[DETECT] Conectando em {port} @ {baud}...");
        await using var transport = new SerialTransport(port, baud);
        await transport.OpenAsync();

        await transport.WriteAsync(Encoding.ASCII.GetBytes("\r\n\r\n"));
        var buffer = new byte[2048];
        var accumulator = new StringBuilder();
        var deadline = DateTime.UtcNow.AddSeconds(4);

        while (DateTime.UtcNow < deadline)
        {
            var read = await transport.ReadAsync(buffer, CancellationToken.None);
            if (read > 0)
            {
                var text = Encoding.ASCII.GetString(buffer, 0, read);
                accumulator.Append(text);
                Console.Write(text);
            }
            else
            {
                await Task.Delay(100);
            }
        }

        var raw = accumulator.ToString();
        var detected = new DeviceDetector().ClassifyPrompt(raw, DeviceSeries.FortiGate40F);
        Console.WriteLine("\n--------------------------------------------------");
        Console.WriteLine($"Fabricante: {detected.Manufacturer}");
        Console.WriteLine($"Série:      {detected.Series}");
        Console.WriteLine($"Estado:     {detected.OperatingState}");
        Console.WriteLine($"Firmware:   {detected.FirmwareState}");
        Console.WriteLine($"Workflow:   {detected.RecommendedWorkflow} - {detected.Details}");
        Console.WriteLine("--------------------------------------------------");

        await transport.CloseAsync();
        return 0;
    }

    private static async Task<int> RunTestBiosAsync(string[] args)
    {
        var port = args.Length > 0 ? args[0] : "COM1";
        var baud = args.Length > 1 && int.TryParse(args[1], out var b) ? b : 9600;

        Console.WriteLine($"[TEST-BIOS] Conectando em {port} @ {baud}...");
        await using var transport = new SerialTransport(port, baud);
        await transport.OpenAsync();

        Console.WriteLine("[*] Enviando CTRL+D para sair do halt e reiniciar...");
        await transport.WriteAsync(new byte[] { 0x04 });

        var buffer = new byte[2048];
        var accumulator = new StringBuilder();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        bool menuCaught = false;

        while (DateTime.UtcNow < deadline)
        {
            var read = await transport.ReadAsync(buffer, CancellationToken.None);
            if (read > 0)
            {
                var text = Encoding.ASCII.GetString(buffer, 0, read);
                accumulator.Append(text);
                Console.Write(text);

                var cur = accumulator.ToString();

                if (!menuCaught && (cur.Contains("Press any key", StringComparison.OrdinalIgnoreCase) ||
                                   cur.Contains("Initializing boot device", StringComparison.OrdinalIgnoreCase) ||
                                   cur.Contains("FortiBootLoader", StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine("\n[⚡] Interrompendo bootloader...");
                    for (int i = 0; i < 5; i++)
                    {
                        await transport.WriteAsync(Encoding.ASCII.GetBytes(" "));
                        await Task.Delay(50);
                    }
                    accumulator.Clear();
                    continue;
                }

                if (cur.Contains("Enter P,D,I,S,G,V,T,F,E,R,N,Q,or H:", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("\n[🎯 SUBMENU TFTP ALCANÇADO!] Enviando [R] (Review parameters)...");
                    accumulator.Clear();
                    await Task.Delay(300);
                    await transport.WriteAsync(Encoding.ASCII.GetBytes("R\r"));
                    break;
                }

                if (cur.Contains("Enter C,R,T,F,I,B,Q,or H:", StringComparison.OrdinalIgnoreCase) ||
                    cur.Contains("Enter Selection:", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("\n[🎯 MENU ALCANÇADO!] Testando comando [C] (Configure TFTP)...");
                    menuCaught = true;
                    accumulator.Clear();
                    await Task.Delay(500);
                    await transport.WriteAsync(Encoding.ASCII.GetBytes("C\r"));
                    continue;
                }
            }
            else
            {
                await Task.Delay(100);
            }
        }

        // Ler a resposta ao comando C e enviar R para ver parâmetros
        var postDeadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < postDeadline)
        {
            var read = await transport.ReadAsync(buffer, CancellationToken.None);
            if (read > 0)
            {
                var text = Encoding.ASCII.GetString(buffer, 0, read);
                Console.Write(text);
            }
            else
            {
                await Task.Delay(100);
            }
        }

        Console.WriteLine("\n[🔍 PING TEST] Entrando em [C] e depois [N] (Diagnose networking)...");
        await transport.WriteAsync(Encoding.ASCII.GetBytes("C\r"));
        await Task.Delay(400);
        await transport.WriteAsync(Encoding.ASCII.GetBytes("N\r"));

        var nDeadline = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < nDeadline)
        {
            var read = await transport.ReadAsync(buffer, CancellationToken.None);
            if (read > 0)
            {
                var text = Encoding.ASCII.GetString(buffer, 0, read);
                Console.Write(text);
            }
            else
            {
                await Task.Delay(100);
            }
        }

        Console.WriteLine("\n[↩️ QUIT] Enviando [Q] para voltar ao menu principal...");
        await transport.WriteAsync(Encoding.ASCII.GetBytes("Q\r"));

        var qDeadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < qDeadline)
        {
            var read = await transport.ReadAsync(buffer, CancellationToken.None);
            if (read > 0)
            {
                var text = Encoding.ASCII.GetString(buffer, 0, read);
                Console.Write(text);
            }
            else
            {
                await Task.Delay(100);
            }
        }

        await transport.CloseAsync();
        return 0;
    }

    private static string? Arg(string[] args, int index) =>

        index < args.Length ? args[index] : null;

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            NetworkDevice.Cli - provisionamento/diagnóstico de equipamentos de rede

            Uso:
              NetworkDevice.Cli serial <COM> [baud] [usuario] [senha] [enable] [pasta]
              NetworkDevice.Cli ssh <host> <usuario> <senha> [porta] [enable] [pasta]
              NetworkDevice.Cli ports
              NetworkDevice.Cli mock
              NetworkDevice.Cli recover <COM> [baud] [perfil: c900|break|catalyst|generic]

            Exemplos:
              NetworkDevice.Cli serial COM3 9600 admin senha123 enable123 backups
              NetworkDevice.Cli ssh 192.168.1.10 admin senha123 22 enable123 backups
              NetworkDevice.Cli ports
              NetworkDevice.Cli mock
              NetworkDevice.Cli recover COM1 9600
              NetworkDevice.Cli recover COM1 9600 c900      (C921-4P / série 900 Ctrl+C)
              NetworkDevice.Cli recover COM1 9600 catalyst  (Catalyst Botão MODE)
            """);
    }
}
