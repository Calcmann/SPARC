using System.Windows;

namespace NetworkDevice.UI.Beta;

// Ponto unico de enforcement, chamado no inicio do OnStartup da COPIA beta.
// Texto 100% ASCII de proposito: evita mojibake em qualquer codepage.
internal static class BetaLicenseGuard
{
    public static bool Enforce(Application app)
    {
        try
        {
            var now = BetaClock.EffectiveUtcNow();
            var machine = MachineId.Current();

            if (now.Date > BetaConfig.ExpiresUtc.Date)
            {
                MessageBox.Show(
                    "Esta versao " + BetaConfig.Tag + " expirou em " + BetaConfig.ExpiresUtc.ToString("dd/MM/yyyy") + ".\n\nSolicite um novo build beta ao responsavel.",
                    "SPARC Beta - Versao expirada", MessageBoxButton.OK, MessageBoxImage.Stop);
                return false;
            }

            if (!BetaClock.CheckAndUpdateLastSeen(now))
            {
                MessageBox.Show(
                    "Relogio do sistema anterior ao ultimo uso registrado.\n\nAjuste a data/hora e tente novamente.",
                    "SPARC Beta - Relogio invalido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var saved = BetaLicenseStore.Load();
            if (saved != null && LicenseCrypto.TryValidate(saved, out var info) && info != null
                && LicenseCrypto.IsAuthorizedForThisMachine(info, now, out _))
            {
                BetaClock.Touch(now);
                return true;
            }

            var reason = saved == null
                ? "Nenhuma chave de ativacao encontrada nesta maquina."
                : "A chave salva e invalida, expirou ou pertence a outro computador.";
            // ShowDialog fecha a unica Window aberta: sem isto o WPF (OnLastWindowClose)
            // encerra o app sozinho e o SPARC nunca abre apos ativar.
            var prevMode = app.ShutdownMode;
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var dlg = new ActivationWindow(reason, machine, BetaConfig.ExpiresUtc);
                return dlg.ShowDialog() == true;
            }
            finally
            {
                app.ShutdownMode = prevMode;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Falha na verificacao da licenca beta:\n" + ex.Message, "SPARC Beta", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}
