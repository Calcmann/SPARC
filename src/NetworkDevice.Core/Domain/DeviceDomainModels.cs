namespace NetworkDevice.Core.Domain;

public enum DeviceManufacturer
{
    Unknown = 0,
    Hpe,
    Cisco,
    Generic
}

public enum DeviceSeries
{
    Unknown = 0,
    Msr954,
    Series1900,
    Isr921,
    Generic
}

public enum DeviceOperatingState
{
    Unknown = 0,
    Ready,
    PasswordProtected,
    BootFailure
}

public enum WorkflowType
{
    Provisioning = 1,
    PasswordRecovery = 2,
    FirmwareRecovery = 3
}

public enum AccessState
{
    Unknown = 0,
    Open,
    PasswordRequired,
    UserAndPasswordRequired,
    RommonOrBootware,
    Disconnected
}

public enum BootState
{
    Unknown = 0,
    Normal,
    Bootware,
    Rommon,
    Corrupted
}

public enum FirmwareState
{
    Unknown = 0,
    Ready,
    Upgradable,
    CorruptedOrMissing
}

/// <summary>
/// Resultado da análise e diagnóstico inteligente do equipamento conectado na serial.
/// </summary>
public sealed record DeviceDetectionResult(
    DeviceManufacturer Manufacturer,
    DeviceSeries Series,
    DeviceOperatingState OperatingState,
    WorkflowType RecommendedWorkflow,
    AccessState AccessState,
    BootState BootState,
    FirmwareState FirmwareState,
    string RawPrompt,
    string Details)
{
    public string DisplayName => $"{Manufacturer} {Series} — {OperatingState}";
    public bool RequiresUserAndPassword => AccessState == AccessState.UserAndPasswordRequired;
    public bool RequiresPasswordOnly => AccessState == AccessState.PasswordRequired;
    public bool IsAuthenticationRequired => OperatingState == DeviceOperatingState.PasswordProtected;
}
