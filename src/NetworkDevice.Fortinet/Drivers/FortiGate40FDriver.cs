using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Engines;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Fortinet.Drivers;

/// <summary>
/// Engine de provisionamento FortiGate 40F. Delega ao FortiOsSaipConfigurator isolado.
/// </summary>
public sealed class FortiGate40FProvisioningEngine : IProvisioningEngine
{
    private readonly FortiOsSaipConfigurator _configurator;

    public FortiGate40FProvisioningEngine(FortiOsSaipConfigurator configurator)
    {
        _configurator = configurator;
    }

    public async Task<bool> ProvisionAsync(
        DeviceSession session,
        SaipCircuitData saip,
        Func<int, string, string, Task>? progress = null,
        CancellationToken ct = default)
    {
        if (progress != null) await progress(10, "Provisionamento FortiGate 40F", "Aplicando configuração FortiOS (wan/lan + rota + admin + policy NAT)...");
        await _configurator.ApplyConfigAsync(session, saip, "wan", "lan", cancellationToken: ct);
        if (progress != null) await progress(100, "Provisionamento Concluído", "Configuração FortiOS aplicada.");
        return true;
    }
}

/// <summary>
/// Recuperação de acesso FortiGate: conta 'maintainer' (bcpb+SERIAL, janela de ~14s
/// pós-boot via console) foi removida no FortiOS 7.2.4+. Sem credencial válida ou
/// backup .conf, o caminho suportado é restore via FortiBootLoader (físico, com reboot).
/// Este engine tenta login com credencial informada e orienta o operador caso contrário —
/// nunca executa format/restore automático.
/// </summary>
public sealed class FortiGate40FPasswordRecoveryEngine : IPasswordRecoveryEngine
{
    public async Task<bool> RecoverPasswordAsync(
        DeviceSession session,
        bool hasPassword,
        string? knownPassword = null,
        string? knownUsername = null,
        Func<string, CancellationToken, Task>? instructOperator = null,
        Func<int, string, string, Task>? progress = null,
        CancellationToken ct = default)
    {
        if (hasPassword && !string.IsNullOrEmpty(knownPassword))
        {
            if (progress != null) await progress(10, "Login Console FortiGate", "Efetuando login...");
            if (!string.IsNullOrWhiteSpace(knownUsername))
            {
                await session.WriteLineAsync(knownUsername.Trim(), ct);
                await Task.Delay(300, ct);
            }
            await session.WriteLineAsync(knownPassword, ct);
            await Task.Delay(500, ct);
            if (progress != null) await progress(100, "Login Concluído", "Credencial enviada ao FortiGate.");
            return true;
        }

        if (instructOperator != null)
        {
            await instructOperator(
                "Recuperação de acesso FortiGate 40F requer presença física:\n\n" +
                "1) Conecte o console (9600 8-N-1) e reinicie o equipamento;\n" +
                "2) Em FortiOS < 7.2.4, no prompt de login digite usuário 'maintainer' e senha 'bcpb+SERIAL (maiúsculas)' em até ~14s;\n" +
                "3) Em FortiOS >= 7.2.4 a conta maintainer foi removida: restaure um backup .conf sem a linha 'set password' via FortiBootLoader (TFTP) ou faça login com outro admin super_admin.\n\n" +
                "Nenhuma ação automática será executada. Clique OK após concluir.",
                ct);
        }

        if (progress != null) await progress(100, "Orientação Concluída", "Recuperação FortiGate é manual/guiada.");
        return false;
    }
}

/// <summary>
/// Recuperação de firmware FortiGate via FortiBootLoader ([G] TFTP / [F] format / [B] backup).
/// Por segurança (risco de 'format boot device'), este engine é guiado: orienta o
/// operador e não executa o flash automaticamente.
/// </summary>
public sealed class FortiGate40FFirmwareRecoveryEngine : IFirmwareRecoveryEngine
{
    public async Task<bool> RecoverFirmwareAsync(
        DeviceSession session,
        string firmwarePath,
        string hostIp,
        string routerIp,
        string subnetMask,
        Func<string, CancellationToken, Task>? instructOperator = null,
        Func<int, string, string, Task>? progress = null,
        CancellationToken ct = default)
    {
        if (instructOperator != null)
        {
            await instructOperator(
                $"Flash FortiGate 40F (imagem .out) via console + TFTP:\n\n" +
                $"1) Sirva '{firmwarePath}' num TFTP acessível em {hostIp};\n" +
                $"2) Reinicie e interrompa o boot (qualquer tecla / Ctrl+B) até o menu FortiBootLoader;\n" +
                $"3) Opção [G]: informe TFTP={hostIp}, local={routerIp}/{subnetMask} e o nome da imagem .out;\n" +
                $"4) Após o boot, restaure o backup de configuração.\n\n" +
                "Nenhum flash automático será executado por este assistente.",
                ct);
        }

        if (progress != null) await progress(100, "Orientação Concluída", "Recovery FortiGate é manual via BIOS+TFTP.");
        return false;
    }
}

/// <summary>
/// Driver FortiGate 40F. Validator HPE não se aplica — retorna null.
/// </summary>
public sealed class FortiGate40FDriver : IDeviceDriver
{
    public DeviceManufacturer Manufacturer => DeviceManufacturer.Fortinet;
    public DeviceSeries Series => DeviceSeries.FortiGate40F;

    public IProvisioningEngine Provisioning { get; }
    public IPasswordRecoveryEngine PasswordRecovery { get; }
    public IFirmwareRecoveryEngine FirmwareRecovery { get; }
    public HpeProvisioningValidator? Validator => null;

    public FortiGate40FDriver(Func<string, Task>? logAsync = null)
    {
        var configurator = new FortiOsSaipConfigurator(logAsync);
        Provisioning = new FortiGate40FProvisioningEngine(configurator);
        PasswordRecovery = new FortiGate40FPasswordRecoveryEngine();
        FirmwareRecovery = new FortiGate40FFirmwareRecoveryEngine();
    }
}
