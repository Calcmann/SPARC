using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
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
                CheckOnlineRenewalInBackground(machine, saved);
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

    private static void CheckOnlineRenewalInBackground(MachineIdentity machine, string currentToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                client.DefaultRequestHeaders.Add("User-Agent", "SPARC-Beta");
                if (!string.IsNullOrWhiteSpace(BetaConfig.GitHubReadOnlyToken) && !BetaConfig.GitHubReadOnlyToken.StartsWith("%%BETA_"))
                {
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", BetaConfig.GitHubReadOnlyToken);
                }
                var url = "https://raw.githubusercontent.com/Calcmann/repo/main/devices.json";
                var resp = await client.GetAsync(url).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return;

                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var g = item.TryGetProperty("MachineGuid", out var gp) ? gp.GetString() : null;
                    var f = item.TryGetProperty("MachineFingerprint", out var fp) ? fp.GetString() : null;
                    if (string.Equals(g, machine.MachineGuid, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(f, machine.Fingerprint, StringComparison.OrdinalIgnoreCase))
                    {
                        var status = item.TryGetProperty("Status", out var sp) ? sp.GetString() : null;
                        if (string.Equals(status, "Revoked", StringComparison.OrdinalIgnoreCase))
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                MessageBox.Show(
                                    "Esta copia do SPARC foi suspensa ou revogada pelo Administrador.\nEntre em contato com o suporte para regularizacao.",
                                    "SPARC - Acesso Revogado", MessageBoxButton.OK, MessageBoxImage.Stop);
                                Application.Current.Shutdown();
                            });
                            return;
                        }

                        var token = item.TryGetProperty("AuthorizedToken", out var tp) ? tp.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(token) && !token.Equals(currentToken, StringComparison.Ordinal))
                        {
                            if (LicenseCrypto.TryValidate(token, out var newInfo) && newInfo != null)
                            {
                                File.WriteAllText(BetaConfig.LicensePath, token);
                            }
                        }
                        break;
                    }
                }
            }
            catch { }
        });
    }
}
