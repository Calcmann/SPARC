using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using NetworkDevice.Core.Licensing;

namespace NetworkDevice.Android.Services;

public sealed class TechnicianProfile
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Cluster { get; set; } = string.Empty;
    public string Uf { get; set; } = string.Empty;

    public string FullName => $"{FirstName} {LastName}".Trim();

    public string DisplaySummary =>
        $"{FullName} • {Company} (Mat: {EmployeeId}) • Tel: {Phone} • {Cluster}/{Uf}";
}

public sealed class AndroidLicenseManager
{
    private static readonly Lazy<AndroidLicenseManager> _instance = new(() => new AndroidLicenseManager());
    public static AndroidLicenseManager Instance => _instance.Value;

    private readonly CloudLicenseService _cloudService = new();

    private AndroidLicenseManager() { }

    public string GetMachineGuid()
    {
        var guid = Preferences.Default.Get("sparc_mobile_machine_guid", string.Empty);
        if (string.IsNullOrWhiteSpace(guid))
        {
            guid = Guid.NewGuid().ToString("D").ToLowerInvariant();
            Preferences.Default.Set("sparc_mobile_machine_guid", guid);
        }
        return guid;
    }

    public string GetFingerprint()
    {
        var guid = GetMachineGuid();
        var model = DeviceInfo.Model ?? "AndroidDevice";
        var mfr = DeviceInfo.Manufacturer ?? "Mobile";
        var raw = $"{guid}|{mfr}|{model}|SPARC-MOBILE";
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        var sb = new StringBuilder();
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public string GetDisplayId()
    {
        var fp = GetFingerprint();
        if (fp.Length >= 16)
            return $"{fp[..4]}-{fp.Substring(4, 4)}-{fp.Substring(8, 4)}-{fp.Substring(12, 4)}".ToUpperInvariant();
        return fp.ToUpperInvariant();
    }

    public TechnicianProfile GetProfile()
    {
        return new TechnicianProfile
        {
            FirstName = Preferences.Default.Get("sparc_tech_firstname", string.Empty),
            LastName = Preferences.Default.Get("sparc_tech_lastname", string.Empty),
            Company = Preferences.Default.Get("sparc_tech_company", string.Empty),
            EmployeeId = Preferences.Default.Get("sparc_tech_employee_id", string.Empty),
            Phone = Preferences.Default.Get("sparc_tech_phone", string.Empty),
            Email = Preferences.Default.Get("sparc_tech_email", string.Empty),
            Cluster = Preferences.Default.Get("sparc_tech_cluster", string.Empty),
            Uf = Preferences.Default.Get("sparc_tech_uf", string.Empty)
        };
    }

    public void SaveProfile(TechnicianProfile profile)
    {
        var existingFirst = Preferences.Default.Get("sparc_tech_firstname", string.Empty);
        var existingLast = Preferences.Default.Get("sparc_tech_lastname", string.Empty);

        // Nome e sobrenome são imutáveis após o primeiro registro; demais campos são atualizáveis
        var finalFirst = !string.IsNullOrWhiteSpace(existingFirst) ? existingFirst : profile.FirstName;
        var finalLast = !string.IsNullOrWhiteSpace(existingLast) ? existingLast : profile.LastName;

        Preferences.Default.Set("sparc_tech_firstname", SparcTextSanitizer.FormatPersonOrCompanyName(finalFirst));
        Preferences.Default.Set("sparc_tech_lastname", SparcTextSanitizer.FormatPersonOrCompanyName(finalLast));
        Preferences.Default.Set("sparc_tech_company", SparcTextSanitizer.FormatPersonOrCompanyName(profile.Company));
        Preferences.Default.Set("sparc_tech_employee_id", SparcTextSanitizer.FormatEmployeeId(profile.EmployeeId));
        Preferences.Default.Set("sparc_tech_phone", SparcTextSanitizer.FormatPhone(profile.Phone));
        Preferences.Default.Set("sparc_tech_email", SparcTextSanitizer.FormatEmail(profile.Email));
        Preferences.Default.Set("sparc_tech_cluster", SparcTextSanitizer.FormatCluster(profile.Cluster));
        Preferences.Default.Set("sparc_tech_uf", SparcTextSanitizer.FormatUf(profile.Uf));
    }

    public string? GetStoredLicenseToken()
    {
        var token = Preferences.Default.Get("sparc_mobile_license_token", string.Empty);
        return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }

    public bool IsActivated(out string statusMessage)
    {
        var profile = GetProfile();
        if (string.IsNullOrWhiteSpace(profile.FirstName) || 
            string.IsNullOrWhiteSpace(profile.Company) ||
            string.IsNullOrWhiteSpace(profile.EmployeeId) ||
            string.IsNullOrWhiteSpace(profile.Phone) ||
            string.IsNullOrWhiteSpace(profile.Cluster) || 
            string.IsNullOrWhiteSpace(profile.Uf))
        {
            statusMessage = "Identificação do técnico incompleta.";
            return false;
        }

        var token = GetStoredLicenseToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            statusMessage = "Aplicativo não ativado.";
            return false;
        }

        if (!SparcLicenseValidator.TryValidate(token, out var info) || info == null)
        {
            statusMessage = "Chave de ativação inválida ou corrompida.";
            return false;
        }

        if (!SparcLicenseValidator.IsAuthorized(info, GetMachineGuid(), GetFingerprint(), DateTime.UtcNow, out var reason))
        {
            statusMessage = reason;
            return false;
        }

        var diasRestantes = (info.ExpiresUtc.Date - DateTime.UtcNow.Date).Days;
        statusMessage = $"Ativado até {info.ExpiresUtc:dd/MM/yyyy} ({diasRestantes} dia(s) restante(s)).";
        return true;
    }

    /// <summary>
    /// Verificação prioritária de licença online ao iniciar no Android.
    /// Se online: bloqueia se revogado pelo gestor, ou atualiza token se prorrogado.
    /// Se offline: permite acesso caso a chave salva esteja válida.
    /// </summary>
    public async Task<(bool Allowed, bool Revoked, string StatusMessage)> VerifyLicenseStartupAsync(CancellationToken ct = default)
    {
        var localToken = GetStoredLicenseToken();
        var (allowed, revoked, newToken, msg) = await _cloudService.VerifyLicenseStartupAsync(
            GetMachineGuid(),
            GetFingerprint(),
            localToken,
            token =>
            {
                if (string.IsNullOrWhiteSpace(token)) return false;
                if (!SparcLicenseValidator.TryValidate(token, out var info) || info == null) return false;
                return SparcLicenseValidator.IsAuthorized(info, GetMachineGuid(), GetFingerprint(), DateTime.UtcNow, out _);
            },
            ct);

        if (revoked)
        {
            Preferences.Default.Remove("sparc_mobile_license_token");
            return (false, true, "Esta cópia do SPARC foi suspensa ou revogada pelo Administrador corporativo.");
        }

        if (!string.IsNullOrWhiteSpace(newToken))
        {
            Preferences.Default.Set("sparc_mobile_license_token", newToken);
        }

        if (allowed)
        {
            IsActivated(out var fullStatus);
            return (true, false, fullStatus);
        }

        return (false, false, msg);
    }

    public string BuildActivationRequestString()
    {
        var p = GetProfile();
        return SparcLicenseValidator.BuildRequest(
            GetMachineGuid(),
            GetFingerprint(),
            p.FirstName,
            p.LastName,
            p.Phone,
            p.Email,
            p.Cluster,
            p.Uf,
            AppInfo.VersionString,
            platform: "Android",
            company: p.Company,
            employeeId: p.EmployeeId);
    }

    public async Task<(bool Success, string Message)> SubmitOnlineActivationAsync(CancellationToken ct = default)
    {
        var p = GetProfile();
        if (string.IsNullOrWhiteSpace(p.FirstName))
            return (false, "Preencha o Nome do técnico.");
        if (string.IsNullOrWhiteSpace(p.Company))
            return (false, "Preencha a Empresa de atuação.");
        if (string.IsNullOrWhiteSpace(p.EmployeeId))
            return (false, "Preencha a Matrícula do técnico.");
        if (string.IsNullOrWhiteSpace(p.Phone))
            return (false, "Preencha o Telefone / WhatsApp do técnico.");
        if (string.IsNullOrWhiteSpace(p.Cluster))
            return (false, "Preencha o Cluster de atuação.");
        if (string.IsNullOrWhiteSpace(p.Uf))
            return (false, "Preencha a UF.");

        var reqString = BuildActivationRequestString();
        var req = new OnlineActivationRequest
        {
            MachineGuid = GetMachineGuid(),
            MachineFingerprint = GetFingerprint(),
            Platform = "Android",
            FirstName = p.FirstName,
            LastName = p.LastName,
            Company = p.Company,
            EmployeeId = p.EmployeeId,
            Phone = p.Phone,
            Email = p.Email,
            Cluster = p.Cluster,
            Uf = p.Uf,
            ClientVersion = AppInfo.VersionString,
            RawRequestCode = reqString,
            RequestedAtUtc = DateTime.UtcNow,
            Status = "Pending",
            AdminNotes = $"Empresa: {p.Company} • Matrícula: {p.EmployeeId} • ID: {GetDisplayId()}"
        };

        return await _cloudService.SubmitActivationRequestAsync(req, ct);
    }

    public async Task<(bool Approved, string Message)> CheckOnlineApprovalAsync(CancellationToken ct = default)
    {
        var (found, req, msg) = await _cloudService.CheckActivationRequestStatusAsync(GetMachineGuid(), ct);
        if (!found || req == null)
        {
            return (false, "Nenhuma solicitação encontrada na nuvem para este dispositivo.");
        }

        if (string.Equals(req.Status, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(req.ApprovedToken))
            {
                var activateOk = ActivateWithKey(req.ApprovedToken, out var actMsg);
                if (activateOk)
                {
                    return (true, "Solicitação aprovada pelo Administrador! O SPARC Mobile foi ativado com sucesso.");
                }
                return (false, $"Chave aprovada, mas falhou ao validar: {actMsg}");
            }
            return (false, "Solicitação consta como aprovada, mas o token ainda não foi gerado.");
        }

        if (string.Equals(req.Status, "Rejected", StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"Solicitação recusada pelo Administrador. Motivo: {req.RejectionReason ?? "Não informado"}.");
        }

        return (false, "Solicitação ainda está pendente de aprovação pelo Administrador no SPARC Admin.");
    }

    public bool ActivateWithKey(string licenseKey, out string message)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            message = "Chave não informada.";
            return false;
        }

        licenseKey = licenseKey.Trim();
        if (!SparcLicenseValidator.TryValidate(licenseKey, out var info) || info == null)
        {
            message = "Formato de chave inválido ou assinatura digital incompatível.";
            return false;
        }

        if (!SparcLicenseValidator.IsAuthorized(info, GetMachineGuid(), GetFingerprint(), DateTime.UtcNow, out var reason))
        {
            message = reason;
            return false;
        }

        Preferences.Default.Set("sparc_mobile_license_token", licenseKey);
        message = $"Ativação concluída com sucesso! Válida até {info.ExpiresUtc:dd/MM/yyyy}.";
        return true;
    }
}
