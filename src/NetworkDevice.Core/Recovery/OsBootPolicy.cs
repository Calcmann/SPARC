namespace NetworkDevice.Core.Recovery;

/// <summary>
/// Define a política da State Machine ao detectar strings indicativas de boot do SO (ex: descompressão, loading, login).
/// </summary>
public enum OsBootPolicy
{
    /// <summary>Declara falha imediata da interrupção (BOOT_INTERRUPTION_FAILED) e cancela TX.</summary>
    TerminalFail,

    /// <summary>Emite aviso no log e continua tentando a interrupção até a janela limite.</summary>
    Warning,

    /// <summary>Ignora padrões de boot do SO e aguarda exclusivamente ROMMON ou timeout global.</summary>
    Ignore
}
