namespace NetworkDevice.Core.Recovery;

/// <summary>
/// Define a linha de operação / modo de fluxo no piloto.
/// </summary>
public enum WorkflowMode
{
    /// <summary>Linha 1: Equipamento Novo (Sem senha / Fabril) - Conexão direta sem interrupção de bootloader.</summary>
    NewDevice,

    /// <summary>Linha 2: Equipamento Reutilizado (Com senha) - Quebra de senha via ROMMON e limpeza completa (write erase).</summary>
    RepurposedDevice
}
