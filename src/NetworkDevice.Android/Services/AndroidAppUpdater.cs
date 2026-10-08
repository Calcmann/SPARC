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
            await page.DisplayAlert("Atualização", "Link de download inválido ou não disponível no momento.", "OK");
            return;
        }

        // Confirmação prévia amigável com notas da versão
        var notesText = string.IsNullOrWhiteSpace(release.ReleaseNotes) ? "" : $"\n\nNotas da Versão:\n{release.ReleaseNotes}";
        var answer = await page.DisplayAlert(
            $"Atualização SPARC Mobile v{release.Version}",
            $"Uma nova versão oficial homologada está disponível no repositório.{notesText}\n\nDeseja baixar e instalar agora?",
            "ATUALIZAR AGORA",
            "DEPOIS");

        if (!answer) return;

        // Abre a tela modal interativa de progresso
        try
        {
            var updateModal = new Views.UpdateModalPage(release);
            await page.Navigation.PushModalAsync(updateModal, true);
        }
        catch (Exception ex)
        {
            await page.DisplayAlert("Falha na Atualização", $"Não foi possível abrir o assistente de atualização: {ex.Message}", "OK");
        }
    }

    /// <summary>
    /// Dispara a Intent nativa do instalador de pacotes do Android via FileProvider após validar a integridade.
    /// </summary>
    public static void TriggerApkInstall(string apkFilePath)
    {
        if (!File.Exists(apkFilePath))
        {
            throw new FileNotFoundException("O pacote APK baixado não foi encontrado.", apkFilePath);
        }

        var fi = new FileInfo(apkFilePath);
        if (fi.Length < 35_000_000)
        {
            throw new InvalidDataException($"Pacote APK corrompido ou incompleto ({fi.Length / (1024.0 * 1024.0):F2} MB). Tamanho mínimo esperado: 35 MB.");
        }

        // Validação de magic bytes (ZIP / APK: 0x50, 0x4B, 0x03, 0x04)
        using (var fs = File.OpenRead(apkFilePath))
        {
            var magic = new byte[4];
            var r = fs.Read(magic, 0, 4);
            if (r < 4 || magic[0] != 0x50 || magic[1] != 0x4B || magic[2] != 0x03 || magic[3] != 0x04)
            {
                throw new InvalidDataException("O arquivo baixado não é um APK válido.");
            }
        }

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
            intent.AddFlags(global::Android.Content.ActivityFlags.ClearTop);

            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Falha ao disparar o instalador de pacotes do Android: {ex.Message}", ex);
        }
#endif
    }
}
