namespace NetworkDevice.Core.Recovery;

/// <summary>
/// Estados formais da máquina de estados de recuperação de equipamento.
/// </summary>
public enum RecoveryState
{
    /// <summary>Sessão não conectada.</summary>
    Disconnected,

    /// <summary>Abrindo canal de transporte (RS-232 / Console).</summary>
    Connecting,

    /// <summary>Verificando atividade do terminal serial (RS-232 handshake/echo).</summary>
    VerifyingTerminal,

    /// <summary>Aguardando o operador realizar o ciclo de energia (power-cycle / reload).</summary>
    WaitingReload,

    /// <summary>Monitorando início de sinal/boot pós-reload.</summary>
    WaitingBoot,

    /// <summary>TX Scheduler e RX Monitor ativos enviando interrupções controladas.</summary>
    Interrupting,

    /// <summary>Prompt do ROMMON detectado com sucesso; TX suspenso imediatamente.</summary>
    RommonDetected,

    /// <summary>Executando passos de recuperação (confreg 0x2142 / rename config.text / write erase).</summary>
    ExecutingRecovery,

    /// <summary>Recuperação concluída e validada com sucesso.</summary>
    Completed,

    /// <summary>Falha na recuperação (ex: BOOT_INTERRUPTION_FAILED, timeout ou erro de comando).</summary>
    Failed
}
