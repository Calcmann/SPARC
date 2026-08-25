namespace NetworkDevice.Core.Recovery;

/// <summary>
/// Método de interrupção de boot suportado pelo transporte serial/console.
/// Ações físicas manuais (como botão MODE) são tratadas via perfil de intervenção manual.
/// </summary>
public enum BootInterruptMethod
{
    /// <summary>Nenhum envio de sinal elétrico/caractere (usado quando a interrupção é manual física).</summary>
    None,

    /// <summary>Sinal de Break na linha serial TX (plataformas tradicionais).</summary>
    Break,

    /// <summary>Caractere Ctrl+C (0x03) — usado em Cisco série 900 e outras plataformas modernas.</summary>
    CtrlC,

    /// <summary>Caractere Ctrl+B (0x02) — usado em roteadores HPE / HP MSR / Comware / H3C.</summary>
    CtrlB,

    /// <summary>Caractere Ctrl+D (0x04) — usado em alguns modelos HPE / 3Com.</summary>
    CtrlD,

    /// <summary>Combinação Ctrl+Break na linha serial.</summary>
    CtrlBreak,

    /// <summary>Caractere Escape (0x1B) — usado em algumas plataformas e firewalls.</summary>
    Esc,

    /// <summary>Combinação Universal: dispara sinais de Break elétrico e Ctrl+C alternados para cobrir qualquer plataforma Cisco.</summary>
    Dual
}
