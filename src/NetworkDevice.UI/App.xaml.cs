using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace NetworkDevice.UI;

public partial class App : Application
{
    private static Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            if (ev.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"Erro fatal na inicialização:\n{ex.Message}\n\n{ex.StackTrace}", "Erro SPARC", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        DispatcherUnhandledException += (s, ev) =>
        {
            MessageBox.Show($"Erro na interface gráfica:\n{ev.Exception.Message}\n\n{ev.Exception.StackTrace}", "Erro SPARC", MessageBoxButton.OK, MessageBoxImage.Error);
            ev.Handled = true;
        };

        base.OnStartup(e);

        var args = Environment.GetCommandLineArgs();

        // Verifica se o aplicativo está sendo executado fora da pasta de trabalho e oferece assistente de instalação
        if (WorkspaceSetupManager.CheckAndOfferSetup(args))
        {
            Shutdown();
            return;
        }

        bool isAdminMode = args.Any(a => a.Equals("--admin", StringComparison.OrdinalIgnoreCase) || 
                                         a.Equals("-admin", StringComparison.OrdinalIgnoreCase));

        var mutexId = isAdminMode ? @"Local\SPARC_Admin_SingleInstance" : @"Local\SPARC_Claro_SingleInstance";
        var appTitle = isAdminMode ? "SPARC Admin" : "SPARC";

        _singleInstance = new Mutex(true, mutexId, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Mutex ocupado: verifica se o processo anterior possui UI visível ou se é um processo morto/órfão
            if (!HandleExistingInstance(appTitle))
            {
                Shutdown();
                return;
            }

            // O processo morto foi finalizado; readquire o mutex para a nova instância
            try { _singleInstance?.Dispose(); } catch { }
            _singleInstance = new Mutex(true, mutexId, out _);
        }

        EncerraProcessosAntigos();

        if (isAdminMode)
        {
            var adminWin = new AdminWindow();
            MainWindow = adminWin;
            adminWin.Show();
        }
        else
        {
            var mainWin = new MainWindow();
            MainWindow = mainWin;
            mainWin.Show();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_RESTORE = 9;

    /// <summary>
    /// Avalia se existem outros processos SPARC em execução.
    /// Se houver processo com interface gráfica visível, traz a janela para a frente e avisa o operador.
    /// Se o processo for morto / zumbi / sem UI visível, encerra-o silenciosamente e permite abrir nova instância.
    /// </summary>
    private static bool HandleExistingInstance(string appTitle)
    {
        try
        {
            var currentPid = Environment.ProcessId;
            var currentProc = Process.GetCurrentProcess();
            var currentName = currentProc.ProcessName;

            var otherProcesses = Process.GetProcessesByName(currentName)
                .Where(p => p.Id != currentPid)
                .ToList();

            if (!otherProcesses.Any() && !currentName.Equals("NetworkDevice.UI", StringComparison.OrdinalIgnoreCase))
            {
                otherProcesses = Process.GetProcessesByName("NetworkDevice.UI")
                    .Where(p => p.Id != currentPid)
                    .ToList();
            }

            // Também procura por nomes de binários de teste gerados
            if (!otherProcesses.Any())
            {
                otherProcesses = Process.GetProcesses()
                    .Where(p => p.Id != currentPid &&
                               (p.ProcessName.StartsWith("SPARC-Beta", StringComparison.OrdinalIgnoreCase) ||
                                p.ProcessName.StartsWith("SPARC", StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }

            Process? visibleProcess = null;
            foreach (var proc in otherProcesses)
            {
                try
                {
                    proc.Refresh();
                    if (proc.MainWindowHandle != IntPtr.Zero && IsWindowVisible(proc.MainWindowHandle))
                    {
                        visibleProcess = proc;
                        break;
                    }
                }
                catch { }
            }

            if (visibleProcess != null)
            {
                try
                {
                    var hwnd = visibleProcess.MainWindowHandle;
                    ShowWindow(hwnd, SW_RESTORE);
                    SetForegroundWindow(hwnd);
                }
                catch { }

                MessageBox.Show(
                    $"O {appTitle} já está em execução.\nUma janela ativa foi encontrada na sua área de trabalho.",
                    $"{appTitle} — Instância Única",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }

            // Nenhum processo possui UI visível na tela: finaliza os processos mortos/zumbis
            foreach (var proc in otherProcesses)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                }
                catch { }
            }

            EncerraProcessosAntigos();
            return true;
        }
        catch
        {
            return true;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Restauração silenciosa da placa ao sair (cobre todos os caminhos, inclusive fases avulsas).
        // Só age se ESTA sessão alterou a placa; tudo registrado em netbackup\restore.log.
        try
        {
            if (NetworkDevice.Core.Provisioning.HostNetworkManager.NeedsRestoreOnExit)
            {
                NetworkDevice.Core.Provisioning.HostNetworkManager.NetLog("Saida do app: restaurando rede...");
                // Task.Run: evita deadlock do .Wait() com a UI thread (continuations no pool).
                var t = System.Threading.Tasks.Task.Run(
                    () => NetworkDevice.Core.Provisioning.HostNetworkManager.RestoreLastAsync(null, null));
                if (t.Wait(TimeSpan.FromSeconds(90)))
                    NetworkDevice.Core.Provisioning.HostNetworkManager.NetLog("Saida: " + t.Result.log.Replace("\n", " | "));
                else
                    NetworkDevice.Core.Provisioning.HostNetworkManager.NetLog("Saida: timeout na restauracao.");
            }
        }
        catch (Exception ex)
        {
            try { NetworkDevice.Core.Provisioning.HostNetworkManager.NetLog("Saida excecao: " + ex.Message); } catch { }
        }
        try { _singleInstance?.ReleaseMutex(); _singleInstance?.Dispose(); } catch { }
        base.OnExit(e);
    }

    private static void EncerraProcessosAntigos()
    {
        // Primeiro tenta kill direto; se falhar (instância elevada) tenta via taskkill elevado?
        // SPARC roda como Administrador por padrão (netsh) — portanto o process.Kill() normal
        // recebe "Acesso negado" para matar outras instâncias elevadas. Usa taskkill /F /T.
        try
        {
            var names = new[] { "NetworkDevice.UI", "NetworkDevice.Cli" };

            foreach (var name in names.Distinct())
            {
                var stillAlive = Process.GetProcessesByName(name).Any(p => p.Id != Environment.ProcessId);
                if (!stillAlive) continue;

                var psi = new ProcessStartInfo("taskkill")
                {
                    Arguments = $"/F /IM {name}.exe /T",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var killer = Process.Start(psi))
                    killer?.WaitForExit(5000);

                // Se ainda restarem (sem privilégio), tenta uma vez elevado
                stillAlive = Process.GetProcessesByName(name).Any(p => p.Id != Environment.ProcessId);
                if (stillAlive)
                {
                    var psiElev = new ProcessStartInfo("taskkill")
                    {
                        Arguments = $"/F /IM {name}.exe /T",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        Verb = "runas"
                    };
                    try
                    {
                        using (var killer = Process.Start(psiElev))
                            killer?.WaitForExit(8000);
                    }
                    catch { /* UAC negado — segue com a instância atual mesmo assim */ }
                }
            }
        }
        catch
        {
            // Proteção contra falhas de API do Windows
        }
    }
}