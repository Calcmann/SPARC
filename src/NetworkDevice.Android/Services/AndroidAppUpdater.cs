using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using NetworkDevice.Core.Firmware;

namespace NetworkDevice.Android.Services;

/// <summary>
/// Gerencia o download autenticado e a instalação autônoma de atualizações OTA do SPARC Mobile no Android.
/// </summary>
public static class AndroidAppUpdater
{
    public static async Task DownloadAndInstallAsync(Page page, SparcPlatformRelease release)
    {
        if (release == null || string.IsNullOrWhiteSpace(release.DownloadUrl))
        {
            await page.DisplayAlert("Atualização", "Link de download inválido ou não disponível.", "OK");
            return;
        }

        var updateService = new SparcAppUpdateService();
        var targetFileName = !string.IsNullOrWhiteSpace(release.FileName) ? release.FileName : $"SPARC-Mobile-v{release.Version}.apk";
        var targetPath = Path.Combine(FileSystem.CacheDirectory, targetFileName);

        try
        {
            // Confirmação com o usuário
            var answer = await page.DisplayAlert(
                $"Atualização SPARC Mobile v{release.Version}",
                $"Tamanho estimado: {targetFileName}\n\n" +
                (string.IsNullOrWhiteSpace(release.ReleaseNotes) ? "" : $"Notas da Versão:\n{release.ReleaseNotes}\n\n") +
                "Deseja iniciar o download e instalação agora?",
                "BAIXAR E INSTALAR",
                "CANCELAR");

            if (!answer) return;

            // Inicia download
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            
            // Notificação inicial
            await page.DisplayAlert(
                "Baixando Atualização",
                "O download do pacote de atualização oficial foi iniciado em segundo plano.\n\nO instalador do Android será aberto automaticamente assim que o download for concluído.",
                "OK");

            var downloadedFile = await updateService.DownloadUpdateFileAsync(
                release.DownloadUrl,
                targetPath,
                null,
                cts.Token);

            if (!File.Exists(downloadedFile))
            {
                await page.DisplayAlert("Erro no Download", "O arquivo baixado não foi encontrado no dispositivo.", "OK");
                return;
            }

            // Dispara o instalador nativo do Android
            TriggerApkInstall(downloadedFile);
        }
        catch (OperationCanceledException)
        {
            await page.DisplayAlert("Atualização Cancelada", "O download da atualização foi cancelado ou atingiu o tempo limite.", "OK");
        }
        catch (Exception ex)
        {
            await page.DisplayAlert("Falha na Atualização", $"Não foi possível concluir o download da atualização homologada:\n\n{ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Dispara a Intent nativa do instalador de pacotes do Android via FileProvider.
    /// </summary>
    public static void TriggerApkInstall(string apkFilePath)
    {
#if ANDROID
        try
        {
            var context = global::Android.App.Application.Context;
            var file = new Java.IO.File(apkFilePath);
            var authority = $"{context.PackageName}.fileprovider";
            var apkUri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, authority, file);

            var intent = new global::Android.Content.Intent(global::Android.Content.Intent.ActionView);
            intent.SetDataAndType(apkUri, "application/vnd.android.package-archive");
            intent.AddFlags(global::Android.Content.ActivityFlags.GrantReadUriPermission);
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);

            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Falha ao disparar o instalador de pacotes do Android: {ex.Message}", ex);
        }
#endif
    }
}
