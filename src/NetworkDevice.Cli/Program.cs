using System.Text;
using NetworkDevice.Cisco;
using NetworkDevice.Core.Backup;
using NetworkDevice.Core.Device;
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
