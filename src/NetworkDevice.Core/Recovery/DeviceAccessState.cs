namespace NetworkDevice.Core.Recovery;

/// <summary>
/// Representa o estado inicial de acesso e segurança do equipamento no momento da conexão serial.
/// </summary>
public enum DeviceAccessState
{
    /// <summary>Nenhuma resposta recebida pela porta serial (cabo desconectado, porta errada ou equipamento desligado).</summary>
    NoResponse,

    /// <summary>Equipamento bloqueado por autenticação (pede Password, Username ou User Access Verification).</summary>
    PasswordLocked,

    /// <summary>Equipamento acessível diretamente em modo usuário ou privilegiado (prompt > ou # sem senha).</summary>
    UnlockedPrompt,

    /// <summary>Equipamento já se encontra no modo de recuperação/bootloader (ROMMON / switch:).</summary>
    AlreadyInRommon
}
