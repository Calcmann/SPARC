using System;
using System.Linq;

namespace NetworkDevice.Core.Licensing;

public enum ActivationRequestStatus
{
    Pending,
    Approved,
    Rejected
}

/// <summary>
/// Representa uma solicitação de ativação gerada pelo técnico em campo e enviada para aprovação do Administrador.
/// </summary>
public sealed class OnlineActivationRequest
{
    public string MachineGuid { get; set; } = string.Empty;
    public string MachineFingerprint { get; set; } = string.Empty;
    public string RawRequestCode { get; set; } = string.Empty; // SPBREQ...
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Cluster { get; set; } = string.Empty;
    public string Uf { get; set; } = string.Empty;
    public string ClientVersion { get; set; } = "0.8";
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Pending"; // "Pending", "Approved", "Rejected"
    public string ApprovedToken { get; set; } = string.Empty; // SPBLIK...
    public string ExpirationDateIso { get; set; } = string.Empty;
    public int ValidDays { get; set; } = 30;
    public string? RejectionReason { get; set; }
    public string? AdminNotes { get; set; }
    public string? RemoteSha { get; set; }

    public string FullName
    {
        get
        {
            var combined = $"{FirstName} {LastName}".Trim();
            return string.IsNullOrWhiteSpace(combined) ? "(Técnico não informado)" : combined;
        }
    }

    public string RegionInfo
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Cluster) && string.IsNullOrWhiteSpace(Uf)) return "Não informado";
            if (string.IsNullOrWhiteSpace(Cluster)) return Uf;
            if (string.IsNullOrWhiteSpace(Uf)) return Cluster;
            return $"{Cluster} / {Uf}";
        }
    }

    public string StatusBadge => Status switch
    {
        "Approved" => "✅ Aprovada",
        "Rejected" => "❌ Rejeitada",
        _ => "⏳ Pendente de Aprovação"
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
