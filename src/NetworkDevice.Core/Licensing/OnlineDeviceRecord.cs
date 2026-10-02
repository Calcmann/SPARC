using System;

namespace NetworkDevice.Core.Licensing;

public enum DeviceLicenseStatus
{
    Active,
    ExpiringSoon,
    Expired,
    Revoked
}

/// <summary>
/// Representa o registro cadastral e de licenciamento de uma máquina/cópia do SPARC em campo.
/// </summary>
public sealed class OnlineDeviceRecord
{
    public string MachineGuid { get; set; } = string.Empty;
    public string MachineFingerprint { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string Cluster { get; set; } = string.Empty;
    public string Uf { get; set; } = string.Empty;
    public string ClientVersion { get; set; } = "0.8";
    public DateTime FirstRegisteredUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public string ExpirationDateIso { get; set; } = string.Empty;
    public int ValidDays { get; set; } = 30;
    public string Status { get; set; } = "Active"; // "Active", "Expired", "Revoked"
    public string AuthorizedToken { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Platform { get; set; } = "Windows";

    public string PlatformBadge
    {
        get
        {
            if (string.Equals(Platform, "Android", StringComparison.OrdinalIgnoreCase) ||
                Notes?.Contains("Android", StringComparison.OrdinalIgnoreCase) == true ||
                ClientVersion?.Contains("Android", StringComparison.OrdinalIgnoreCase) == true)
            {
                return "📱 Android";
            }
            return "🪟 Windows";
        }
    }

    public string FullName
    {
        get
        {
            var combined = $"{FirstName} {LastName}".Trim();
            return string.IsNullOrWhiteSpace(combined) ? "(Técnico não identificado)" : combined;
        }
    }

    public DeviceLicenseStatus CalculatedStatus
    {
        get
        {
            if (string.Equals(Status, "Revoked", StringComparison.OrdinalIgnoreCase))
            {
                return DeviceLicenseStatus.Revoked;
            }

            if (DateTime.TryParse(ExpirationDateIso, out var exp))
            {
                var now = DateTime.UtcNow.Date;
                if (exp.Date < now) return DeviceLicenseStatus.Expired;
                if ((exp.Date - now).TotalDays <= 7) return DeviceLicenseStatus.ExpiringSoon;
                return DeviceLicenseStatus.Active;
            }

            return DeviceLicenseStatus.Expired;
        }
    }

    public string StatusBadge => CalculatedStatus switch
    {
        DeviceLicenseStatus.Active => "🟢 Ativo",
        DeviceLicenseStatus.ExpiringSoon => "🟡 A Expirar (<7d)",
        DeviceLicenseStatus.Expired => "🔴 Expirado",
        DeviceLicenseStatus.Revoked => "🚫 Revogado",
        _ => "⚪ Desconhecido"
    };

    public string WhatsAppUrl
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Phone)) return string.Empty;
            var digitsOnly = new string(Phone.Where(char.IsDigit).ToArray());
            if (digitsOnly.Length == 10 || digitsOnly.Length == 11)
            {
                digitsOnly = "55" + digitsOnly;
            }
            return $"https://wa.me/{digitsOnly}";
        }
    }
}
