using System.Diagnostics;
using System.Windows;

namespace NetworkDevice.UI;

public partial class App : Application
{
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

        EncerraProcessosAntigos();
        base.OnStartup(e);
    }

    private static void EncerraProcessosAntigos()
    {
        try
        {
            var currentProcess = Process.GetCurrentProcess();
            var currentProcessName = currentProcess.ProcessName;

            var processNamesToClean = new[] { currentProcessName, "NetworkDevice.UI", "NetworkDevice.Cli" };

            foreach (var name in processNamesToClean.Distinct())
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    if (process.Id != currentProcess.Id)
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(1500);
                        }
                        catch
                        {
                            // Ignora processos que já fecharam ou sem permissão
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
            }
        }
        catch
        {
            // Proteção contra falhas de API do Windows
        }
    }
}