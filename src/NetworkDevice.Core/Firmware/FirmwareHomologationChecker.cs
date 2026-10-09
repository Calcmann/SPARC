using System;
using System.Collections.Generic;
using System.IO;
using NetworkDevice.Core.Domain;

namespace NetworkDevice.Core.Firmware;

/// <summary>
/// Verificador central de conformidade e homologação de versões de firmware pela Claro / Embratel.
/// Permite alertar o operador quando uma versão alternativa é indicada manualmente.
/// </summary>
public static class FirmwareHomologationChecker
{
    /// <summary>
    /// Catálogo canônico oficial de firmwares homologados pela Claro / Embratel por modelo de equipamento.
    /// Fonte da verdade alinhada com as releases oficiais de engenharia.
    /// </summary>
    private static readonly Dictionary<DeviceSeries, string> DefaultHomologatedFiles = new()
    {
        [DeviceSeries.Series1900] = "c1900-universalk9-mz.SPA.157-3.M9.bin",
        [DeviceSeries.Isr921]     = "c900-universalk9-mz.SPA.159-3.M12.bin",
        [DeviceSeries.Isr841]     = "c800m-universalk9-mz.SPA.159-3.M12.bin",
        [DeviceSeries.Series2900] = "c2900-universalk9-mz.SPA.157-3.M8.bin",
        [DeviceSeries.FortiGate40F] = "FGT_40F-v7.2.11.M-build1740-FORTINET.out",
        [DeviceSeries.Msr954]     = "MSR954-CMW710-R6749P43.ipe",
        [DeviceSeries.Msr930]     = "MSR93X-CMW520-R2512P04.BIN",
        [DeviceSeries.Msr1002]    = "MSR100X-CMW710-R6749P43.ipe"
    };

    /// <summary>
    /// Retorna o nome do arquivo de firmware homologado para a série.
    /// Prioriza o arquivo presente no repositório local em cache; caso não exista, retorna a referência canônica padrão.
    /// </summary>
    public static string? ObterNomeFirmwareHomologado(DeviceSeries series, FirmwareRepositoryService? repoService = null)
    {
        if (repoService != null)
        {
            try
            {
                var local = repoService.GetLocalFirmware(series);
                if (local != null && !string.IsNullOrWhiteSpace(local.FileName))
                {
                    return local.FileName;
                }
            }
            catch
            {
                // Fallback para catálogo canônico padrão
            }
        }

        return DefaultHomologatedFiles.TryGetValue(series, out var def) ? def : null;
    }

    /// <summary>
    /// Valida se o arquivo de firmware indicado pelo operador corresponde à versão homologada pela Claro / Embratel.
    /// </summary>
    /// <param name="series">Série do equipamento em operação.</param>
    /// <param name="filePathOrName">Caminho ou nome do arquivo de firmware indicado.</param>
    /// <param name="repoService">Serviço de repositório opcional para consulta de cache local.</param>
    /// <param name="homologadoEsperado">Retorna o nome do firmware homologado esperado caso divirja.</param>
    /// <returns>True se a versão é a homologada; False se difere da versão homologada.</returns>
    public static bool IsVersaoHomologada(
        DeviceSeries series,
        string? filePathOrName,
        FirmwareRepositoryService? repoService,
        out string? homologadoEsperado)
    {
        homologadoEsperado = ObterNomeFirmwareHomologado(series, repoService);

        if (string.IsNullOrWhiteSpace(filePathOrName) || string.IsNullOrWhiteSpace(homologadoEsperado))
        {
            return true;
        }

        var fileName = Path.GetFileName(filePathOrName);
        return string.Equals(fileName, homologadoEsperado, StringComparison.OrdinalIgnoreCase);
    }
}
