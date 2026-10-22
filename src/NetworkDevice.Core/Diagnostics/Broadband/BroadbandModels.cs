using System;
using System.Collections.Generic;

namespace NetworkDevice.Core.Diagnostics.Broadband;

/// <summary>
/// Informações geográficas do link e do servidor de teste.
/// </summary>
public sealed record BroadbandGeoInfo(
    string PublicIp,
    string Asn,
    string Provider,
    string ClientCity,
    string ClientCountry,
    double? ClientLatitude,
    double? ClientLongitude,
    string? ServerColoIata,
    string ServerCity,
    string ServerCountry,
    double? ServerLatitude,
    double? ServerLongitude,
    double? DistanceKm,
    DateTime Timestamp);

/// <summary>
/// Detalhes de um servidor de teste disponível.
/// </summary>
public sealed record BroadbandServerInfo(
    string Id,
    string Name,
    string Sponsor,
    string Location,
    string Country,
    string Host,
    int Port,
    double? Latitude,
    double? Longitude,
    double? DistanceKm,
    double? LatencyMs,
    string ServerType) // "Cloudflare", "Ookla", "MLab"
{
    public string DisplayText
    {
        get
        {
            var dist = DistanceKm.HasValue ? $" (~{DistanceKm.Value:F1} km)" : "";
            var lat = LatencyMs.HasValue ? $" - {LatencyMs.Value:F0} ms" : "";
            return $"{Sponsor} ({Location}){dist}{lat} [{ServerType}]";
        }
    }
}

/// <summary>
/// Ponto amostral na série temporal do teste (usado para alimentar os gráficos em tempo real e o relatório).
/// </summary>
public sealed record BroadbandPoint(
    double ElapsedSeconds,
    double SpeedMbps,
    double LatencyMs,
    double JitterMs,
    string Phase); // "Ping", "Download", "Upload"

/// <summary>
/// Diagnóstico de Bufferbloat (latência e jitter em repouso vs sob saturação de download e upload).
/// </summary>
public sealed record BroadbandBufferbloat(
    double LatencyIdleMs,
    double LatencyUnderDownloadMs,
    double LatencyUnderUploadMs,
    double JitterIdleMs,
    double JitterUnderLoadMs,
    double DeltaDownloadMs,
    double DeltaUploadMs,
    string Grade,
    string Description);

/// <summary>
/// Evento de progresso disparado para a interface do usuário.
/// </summary>
public sealed record BroadbandProgressEvent(
    string Phase,
    double Percent,
    double CurrentSpeedMbps,
    double CurrentLatencyMs,
    double CurrentJitterMs,
    string StatusText,
    BroadbandPoint? NewPoint = null);

/// <summary>
/// Registro de fallback automático de servidor.
/// </summary>
public sealed record BroadbandFallbackEvent(
    DateTime Timestamp,
    string TargetServerName,
    string TargetServerType,
    string Reason,
    string FallbackServerName,
    string FallbackServerType);

/// <summary>
/// Coeficiente de latência corrigido pela física da distância (propagação da luz em fibra óptica).
/// </summary>
public sealed record BroadbandLatencyQuality(
    double MeasuredLatencyMs,
    double DistanceKm,
    double TheoreticalFiberRttMs,
    double ResidualQueuingDelayMs,
    double RouteEfficiencyPct,
    string RouteQualityStatus,
    string DiagnosticExplanation);

/// <summary>
/// Resultado consolidado do teste de banda larga.
/// </summary>
public sealed record BroadbandTestResult(
    bool IsSuccess,
    double DownloadAvgMbps,
    double DownloadPeakMbps,
    double UploadAvgMbps,
    double UploadPeakMbps,
    double LatencyIdleMs,
    double LatencyMinMs,
    double LatencyMaxMs,
    double JitterMs,
    double PacketLossPct,
    BroadbandBufferbloat? Bufferbloat,
    BroadbandGeoInfo? GeoInfo,
    BroadbandServerInfo? PrimaryServer,
    IReadOnlyList<BroadbandFallbackEvent> FallbackHistory,
    IReadOnlyList<BroadbandPoint> TimeSeries,
    string StatusMessage,
    string? ValidationUrl,
    TimeSpan Duration,
    string? LocalAdapterName,
    string? SourceIpAddress,
    AvaliacaoBanda? AvaliacaoNominal = null,
    BroadbandLatencyQuality? LatencyQuality = null);
