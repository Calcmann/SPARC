using System;
using NetworkDevice.Core.Domain;

namespace NetworkDevice.Core.Firmware;

/// <summary>
/// Representa um arquivo de firmware disponível no repositório remoto (ex: GitHub).
/// </summary>
public sealed record RemoteFirmwareInfo(
    DeviceSeries Series,
    string FolderName,
    string FileName,
    string DownloadUrl,
    long SizeBytes,
    string? GitSha,
    string? DetectedVersion)
{
    public double SizeMb => SizeBytes / (1024.0 * 1024.0);

    public string DisplaySize => $"{SizeMb:N1} MB";
}

/// <summary>
/// Representa um arquivo de firmware presente no repositório local do técnico.
/// </summary>
public sealed record LocalFirmwareInfo(
    DeviceSeries Series,
    string FolderName,
    string LocalFilePath,
    string FileName,
    long SizeBytes,
    DateTime LastModifiedUtc,
    string? DetectedVersion)
{
    public double SizeMb => SizeBytes / (1024.0 * 1024.0);

    public string DisplaySize => $"{SizeMb:N1} MB";
}

public enum FirmwareComparisonStatus
{
    /// <summary>
    /// Arquivo local coincide com a versão disponível remotamente.
    /// </summary>
    UpToDate,

    /// <summary>
    /// Versão mais recente disponível no repositório remoto ou arquivo remoto foi atualizado.
    /// </summary>
    UpdateAvailable,

    /// <summary>
    /// Arquivo não existe no repositório local do técnico.
    /// </summary>
    MissingLocally,

    /// <summary>
    /// Existe apenas localmente (sem correspondente no repositório remoto no momento).
    /// </summary>
    LocalOnly
}

/// <summary>
/// Item comparativo de um modelo específico entre local e remoto.
/// </summary>
public sealed record FirmwareSyncItem(
    FirmwareModelDefinition Model,
    FirmwareComparisonStatus Status,
    LocalFirmwareInfo? Local,
    RemoteFirmwareInfo? Remote,
    string StatusDescription);

/// <summary>
/// Relatório consolidado de sincronização do repositório de firmwares.
/// </summary>
public sealed record FirmwareSyncReport(
    IReadOnlyList<FirmwareSyncItem> Items,
    DateTime CheckedAtUtc,
    bool HasInternetAccess,
    string? GeneralMessage)
{
    public bool HasAnyUpdates => Items.Any(i => i.Status is FirmwareComparisonStatus.UpdateAvailable or FirmwareComparisonStatus.MissingLocally);

    public IReadOnlyList<FirmwareSyncItem> PendingUpdates =>
        Items.Where(i => i.Status is FirmwareComparisonStatus.UpdateAvailable or FirmwareComparisonStatus.MissingLocally).ToList();
}
