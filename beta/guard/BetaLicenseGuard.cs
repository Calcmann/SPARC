using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace NetworkDevice.UI.Beta;

// Ponto unico de enforcement, chamado no inicio do OnStartup da COPIA beta.
// Prioriza a checagem online na inicializacao para bloqueios e prorrogacoes imediatas.
internal static class BetaLicenseGuard
{
    public static bool Enforce(Application app)
    {
        try
        {
            var now = BetaClock.EffectiveUtcNow();
            var machine = MachineId.Current();

            if (!BetaClock.CheckAndUpdateLastSeen(now))
            {
                MessageBox.Show(
                    "Relogio do sistema anterior ao ultimo uso registrado.\n\nAjuste a data/hora e tente novamente.",
                    "SPARC Beta - Relogio invalido", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var saved = BetaLicenseStore.Load();

            // 1. CONSULTA PRIORITÁRIA DA LICENÇA ONLINE NA INICIALIZAÇÃO
            // Se estiver online com acesso à internet, sincroniza status com a nuvem (revogação ou prorrogação de prazo)
            try
            {
                var onlineTask = Task.Run(() => CheckOnlineStatusSync(machine, saved));
                if (onlineTask.Wait(TimeSpan.FromSeconds(4)))
                {
                    var (isOnline, isRevoked, newToken) = onlineTask.Result;
                    if (isOnline)
                    {
                        if (isRevoked)
                        {
                            try { if (File.Exists(BetaConfig.LicensePath)) File.Delete(BetaConfig.LicensePath); } catch { }
                            MessageBox.Show(
                                "Esta cópia do SPARC foi suspensa ou revogada pelo Administrador corporativo.\n\nEntre em contato com a supervisão para regularização do acesso.",
                                "SPARC - Acesso Revogado", MessageBoxButton.OK, MessageBoxImage.Stop);
                            return false;
                        }

                        // Se o administrador prorrogou ou atualizou a chave online, baixa e grava localmente
                        if (!string.IsNullOrWhiteSpace(newToken))
                        {
                            if (LicenseCrypto.TryValidate(newToken, out var newInfo) && newInfo != null)
                            {
                                saved = newToken;
                                try
                                {
                                    var dir = Path.GetDirectoryName(BetaConfig.LicensePath);
                                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                                    File.WriteAllText(BetaConfig.LicensePath, newToken);
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. VALIDAÇÃO LOCAL / OFFLINE
            if (saved != null && LicenseCrypto.TryValidate(saved, out var info) && info != null
                && LicenseCrypto.IsAuthorizedForThisMachine(info, now, out _))
            {
                BetaClock.Touch(now);
                return true;
            }

            var reason = saved == null
                ? "Nenhuma chave de ativacao encontrada nesta maquina."
                : "A chave salva e invalida, expirou ou pertence a outro computador.";

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

    /// <summary>
    /// Verificação em tempo de execução chamada durante verificação de atualizações online
    /// ou rotina periódica em segundo plano no Windows.
    /// Se a cópia foi revogada ou expirou na nuvem, bloqueia imediatamente.
    /// Se foi prorrogada ou liberada, atualiza a licença local silenciosamente.
    /// </summary>
    public static async Task<(bool Allowed, bool Revoked, bool Expired, string Message)> CheckRuntimeLicenseOnlineAsync()
    {
        try
        {
            var machine = MachineId.Current();
            var saved = BetaLicenseStore.Load();
            var now = BetaClock.EffectiveUtcNow();

            var (isOnline, isRevoked, newToken) = await Task.Run(() => CheckOnlineStatusSync(machine, saved)).ConfigureAwait(false);
            if (isOnline)
            {
                if (isRevoked)
                {
                    try { if (File.Exists(BetaConfig.LicensePath)) File.Delete(BetaConfig.LicensePath); } catch { }
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show(
                            "Esta cópia do SPARC foi suspensa ou revogada pelo Administrador corporativo.\n\nO acesso ao sistema foi bloqueado.",
                            "SPARC - Acesso Revogado", MessageBoxButton.OK, MessageBoxImage.Stop);
                        var dlg = new ActivationWindow("Esta cópia foi revogada pelo Administrador corporativo.", machine, BetaConfig.ExpiresUtc);
                        dlg.ShowDialog();
                        Application.Current.Shutdown();
                    });
                    return (false, true, false, "Licença revogada pelo Administrador.");
                }

                if (!string.IsNullOrWhiteSpace(newToken))
                {
                    if (LicenseCrypto.TryValidate(newToken, out var newInfo) && newInfo != null)
                    {
                        saved = newToken;
                        try
                        {
                            var dir = Path.GetDirectoryName(BetaConfig.LicensePath);
                            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                            File.WriteAllText(BetaConfig.LicensePath, newToken);
                        }
                        catch { }
                    }
                }
            }

            // Valida chave salva
            if (saved != null && LicenseCrypto.TryValidate(saved, out var info) && info != null
                && LicenseCrypto.IsAuthorizedForThisMachine(info, now, out var reason))
            {
                BetaClock.Touch(now);
                return (true, false, false, $"Licença ativa até {info.ExpiresUtc:dd/MM/yyyy}.");
            }

            // Expirada
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(
                    "A licença de uso do SPARC expirou.\n\nEntre em contato com o administrador para renovar o acesso.",
                    "SPARC - Licença Expirada", MessageBoxButton.OK, MessageBoxImage.Warning);
                var dlg = new ActivationWindow("A chave expirou ou é inválida.", machine, BetaConfig.ExpiresUtc);
                if (dlg.ShowDialog() != true)
                {
                    Application.Current.Shutdown();
                }
            });
            return (false, false, true, "Licença expirada.");
        }
        catch (Exception ex)
        {
            return (true, false, false, $"Offline: {ex.Message}");
        }
    }

    private static (bool IsOnline, bool IsRevoked, string? NewToken) CheckOnlineStatusSync(MachineIdentity machine, string? currentToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            client.DefaultRequestHeaders.Add("User-Agent", "SPARC-Beta");
            var token = !string.IsNullOrWhiteSpace(BetaConfig.GitHubReadOnlyToken) && !BetaConfig.GitHubReadOnlyToken.StartsWith("%%BETA_")
                ? BetaConfig.GitHubReadOnlyToken
                : NetworkDevice.Core.Security.EmbeddedTokenVault.ResolveEmbeddedToken();

            string? json = null;

            // 1. Tenta API de contents do GitHub primeiro (instantânea, sem cache CDN)
            if (!string.IsNullOrWhiteSpace(token))
            {
                try
                {
                    var apiUrl = "https://api.github.com/repos/Calcmann/repo/contents/devices.json";
                    using var apiReq = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                    apiReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    apiReq.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

                    var apiResp = client.Send(apiReq);
                    if (apiResp.IsSuccessStatusCode)
                    {
                        var respJson = new StreamReader(apiResp.Content.ReadAsStream()).ReadToEnd();
                        using var doc = JsonDocument.Parse(respJson);
                        if (doc.RootElement.TryGetProperty("content", out var contentProp))
                        {
                            var b64 = contentProp.GetString()?.Replace("\n", "").Replace("\r", "");
                            if (!string.IsNullOrEmpty(b64))
                            {
                                json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                            }
                        }
                    }
                }
                catch { }
            }

            // 2. Fallback via raw URL
            if (string.IsNullOrEmpty(json))
            {
                var rawUrl = "https://raw.githubusercontent.com/Calcmann/repo/main/devices.json";
                using var req = new HttpRequestMessage(HttpMethod.Get, rawUrl);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }
                var resp = client.Send(req);
                if (resp.IsSuccessStatusCode)
                {
                    json = new StreamReader(resp.Content.ReadAsStream()).ReadToEnd();
                }
            }

            if (string.IsNullOrEmpty(json)) return (false, false, null);

            using var devicesDoc = JsonDocument.Parse(json);
            if (devicesDoc.RootElement.ValueKind != JsonValueKind.Array) return (true, false, null);

            foreach (var item in devicesDoc.RootElement.EnumerateArray())
            {
                var g = item.TryGetProperty("MachineGuid", out var gp) ? gp.GetString() : null;
                var f = item.TryGetProperty("MachineFingerprint", out var fp) ? fp.GetString() : null;
                if (string.Equals(g, machine.MachineGuid, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(f, machine.Fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    var status = item.TryGetProperty("Status", out var sp) ? sp.GetString() : null;
                    if (string.Equals(status, "Revoked", StringComparison.OrdinalIgnoreCase))
                    {
                        return (true, true, null);
                    }

                    var authorizedToken = item.TryGetProperty("AuthorizedToken", out var tp) ? tp.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(authorizedToken) && !authorizedToken.Equals(currentToken, StringComparison.Ordinal))
                    {
                        return (true, false, authorizedToken);
                    }
                    return (true, false, null);
                }
            }

            return (true, false, null);
        }
        catch
        {
            return (false, false, null);
        }
    }
}
