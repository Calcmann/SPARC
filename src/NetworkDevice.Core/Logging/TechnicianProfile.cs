using System;
using System.Linq;
using System.Text;

namespace NetworkDevice.Core.Logging;

public sealed class TechnicianProfile
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    public string FullName
    {
        get
        {
            var parts = new[] { FirstName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s));
            var joined = string.Join(" ", parts);
            return string.IsNullOrWhiteSpace(joined) ? "Técnico Não Identificado" : joined;
        }
    }

    public string Company { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Cluster { get; set; } = string.Empty;
    public string Uf { get; set; } = string.Empty;
    public string MachineGuid { get; set; } = string.Empty;
    public string MachineDisplayId { get; set; } = string.Empty;

    public string Summary
    {
        get
        {
            var sb = new StringBuilder(FullName);
            if (!string.IsNullOrWhiteSpace(Company)) sb.Append($" ({Company})");
            if (!string.IsNullOrWhiteSpace(EmployeeId)) sb.Append($" - Mat: {EmployeeId}");
            if (!string.IsNullOrWhiteSpace(Cluster) || !string.IsNullOrWhiteSpace(Uf))
            {
                var loc = string.Join("/", new[] { Cluster, Uf }.Where(s => !string.IsNullOrWhiteSpace(s)));
                sb.Append($" [{loc}]");
            }
            return sb.ToString();
        }
    }
}

