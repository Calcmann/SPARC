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

        // Instância única: evita duas janelas do SPARC competindo pela mesma COM serial
        // (causa comum de "seleção 1 solta" e binários antigos sendo usados em paralelo).
        _singleInstance = new Mutex(true, @"Local\SPARC_Claro_SingleInstance", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("O SPARC já está em execução.\nFeche a janela existente e tente novamente.", "SPARC — Instância única", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        EncerraProcessosAntigos();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
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