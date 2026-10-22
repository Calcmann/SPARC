using System;
using System.IO;
using System.Threading.Tasks;
using NetworkDevice.Cisco;
using NetworkDevice.Core.Provisioning;
using Xunit;
using Xunit.Abstractions;

namespace NetworkDevice.Tests;

public class AuditCiscoConfig
{
    private readonly ITestOutputHelper _output;

    public AuditCiscoConfig(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task AuditGeneratedConfig()
    {
        var pdfPath = @"C:\Users\User\Downloads\LDA-IP-02580.pdf";
        if (!File.Exists(pdfPath))
        {
            _output.WriteLine("Sample PDF not present at " + pdfPath + ". Skipping inspection test.");
            return;
        }
        var data = await SaipParser.ParseFileAsync(pdfPath);
        var cmds = CiscoSaipConfigurator.GenerateCommands(data, "GigabitEthernet 0/0", "GigabitEthernet 0/1");
        
        _output.WriteLine($"=== TOTAL COMANDOS GERADOS: {cmds.Count} ===");
        foreach (var cmd in cmds)
        {
            if (cmd.StartsWith("interface") || cmd.StartsWith("ip route") || cmd.StartsWith("ipv6 route") || cmd.StartsWith("hostname") || cmd.Contains("policy-map") || cmd.Contains("bandwidth"))
            {
                _output.WriteLine(cmd);
            }
        }
    }
}
