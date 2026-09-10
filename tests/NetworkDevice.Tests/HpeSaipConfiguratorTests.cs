using NetworkDevice.Core.Provisioning;
using Xunit;

namespace NetworkDevice.Tests;

public sealed class HpeSaipConfiguratorTests
{
    private static readonly SaipCircuitData SampleCircuit = new()
    {
        ClienteRazaoSocial = "SOLDI PROMOTORA DE VENDAS LTDA",
        DesignacaoIp = "FNS/IP/03977",
        NumeroOts = "IM-SPO-IGC--IP-50631/2026",
        WanIp = "201.90.204.22",
        WanSubnetMask = "255.255.255.252",
        WanGateway = "201.90.204.21",
        LanIp = "200.182.245.17",
        LanSubnetMask = "255.255.255.240"
    };

    [Fact]
    public void DetectView_AccuratelyIdentifiesAllComwareViews()
    {
        Assert.Equal(HpeComwareView.UserView, HpeSaipConfigurator.DetectView("<HPE>"));
        Assert.Equal(HpeComwareView.UserView, HpeSaipConfigurator.DetectView("<HPE-MSR954>"));
        Assert.Equal(HpeComwareView.SystemView, HpeSaipConfigurator.DetectView("[HPE]"));
        Assert.Equal(HpeComwareView.InterfaceView, HpeSaipConfigurator.DetectView("[HPE-GigabitEthernet0/0]"));
        Assert.Equal(HpeComwareView.InterfaceView, HpeSaipConfigurator.DetectView("[HPE-GE0/1]"));
        Assert.Equal(HpeComwareView.InterfaceView, HpeSaipConfigurator.DetectView("[HPE-Vlan-interface1]"));
        Assert.Equal(HpeComwareView.LocalUserView, HpeSaipConfigurator.DetectView("[HPE-luser-manage-EBT]"));
        Assert.Equal(HpeComwareView.LineView, HpeSaipConfigurator.DetectView("[HPE-line-vty0-63]"));
        Assert.Equal(HpeComwareView.Unknown, HpeSaipConfigurator.DetectView("Router#"));
        Assert.Equal(HpeComwareView.Unknown, HpeSaipConfigurator.DetectView(string.Empty));
    }

    [Fact]
    public void ParseStaticRoutes_ExtractsExactRouteLines()
    {
        var config = @"
#
interface GigabitEthernet0/0
 ip address 201.90.204.22 255.255.255.252
#
ip route-static 0.0.0.0 0.0.0.0 201.90.204.21
ip route-static 10.0.0.0 255.0.0.0 192.168.1.1 description BACKUP
#
return
";
        var routes = HpeSaipConfigurator.ParseStaticRoutes(config);

        Assert.Equal(2, routes.Count);
        Assert.Contains("ip route-static 0.0.0.0 0.0.0.0 201.90.204.21", routes);
        Assert.Contains("ip route-static 10.0.0.0 255.0.0.0 192.168.1.1 description BACKUP", routes);
    }

    [Fact]
    public void GenerateUndoStaticRoutes_GeneratesExactUndoCommands()
    {
        var routes = new[]
        {
            "ip route-static 0.0.0.0 0.0.0.0 201.90.204.21",
            "ip route-static 10.0.0.0 255.0.0.0 192.168.1.1"
        };

        var undos = HpeSaipConfigurator.GenerateUndoStaticRoutes(routes);

        Assert.Equal(2, undos.Count);
        Assert.Equal("undo ip route-static 0.0.0.0 0.0.0.0 201.90.204.21", undos[0]);
        Assert.Equal("undo ip route-static 10.0.0.0 255.0.0.0 192.168.1.1", undos[1]);
    }

    [Fact]
    public void GenerateCommands_ProducesCanonicalComware7Syntax_ZeroLegacyLevelAttributes()
    {
        var cmds = HpeSaipConfigurator.GenerateCommands(SampleCircuit, "GigabitEthernet0/0", "GigabitEthernet0/1");

        // System view & Interface
        Assert.Contains("system-view", cmds);
        Assert.Contains("interface GigabitEthernet0/0", cmds);
        Assert.Contains("ip address 201.90.204.22 255.255.255.252", cmds);
        Assert.Contains("interface GigabitEthernet0/1", cmds);
        Assert.Contains("ip address 200.182.245.17 255.255.255.240", cmds);

        // Rota Default canônica
        Assert.Contains("ip route-static 0.0.0.0 0.0.0.0 201.90.204.21", cmds);

        // Usuário EBT Comware 7
        Assert.Contains("local-user EBT class manage", cmds);
        Assert.Contains("password simple PRO1ANPRO1AN", cmds);
        Assert.Contains("service-type telnet", cmds);
        Assert.Contains("authorization-attribute user-role network-admin", cmds);

        // Zero legacy level attributes
        Assert.DoesNotContain(cmds, c => c.Contains("level-15", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(cmds, c => c.Contains("level-3", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(cmds, c => c.Contains("level 15", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(cmds, c => c.Contains("level 3", StringComparison.OrdinalIgnoreCase));

        // Telnet explícito (sem ssh indiscriminado)
        Assert.Contains("telnet server enable", cmds);
        Assert.Contains("protocol inbound telnet", cmds);

        // Persistência canônica
        Assert.Contains("save safely force", cmds);
    }

    [Fact]
    public void GenerateCommands_ProducesComware5Syntax_WhenIsComware5True()
    {
        var cmds = HpeSaipConfigurator.GenerateCommands(SampleCircuit, "GigabitEthernet0/0", "GigabitEthernet0/1", isComware5: true);

        // System view & Interface
        Assert.Contains("system-view", cmds);
        Assert.Contains("interface GigabitEthernet0/0", cmds);
        Assert.Contains("ip address 201.90.204.22 255.255.255.252", cmds);
        Assert.Contains("interface GigabitEthernet0/1", cmds);
        Assert.Contains("ip address 200.182.245.17 255.255.255.240", cmds);

        // Rota Default canônica
        Assert.Contains("ip route-static 0.0.0.0 0.0.0.0 201.90.204.21", cmds);

        // Usuário EBT Comware 5 (sem "class manage")
        Assert.Contains("local-user EBT", cmds);
        Assert.DoesNotContain(cmds, c => c.Contains("class manage", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("password simple PRO1ANPRO1AN", cmds);
        Assert.Contains("service-type telnet", cmds);
        Assert.Contains("user privilege level 3", cmds);
        Assert.Contains("authorization-attribute level 3", cmds);

        // Telnet server
        Assert.Contains("telnet server enable", cmds);
        Assert.Contains("protocol inbound telnet", cmds);

        // Comware 5 usa "user-interface" em vez de "line"
        Assert.Contains("user-interface vty 0 4", cmds);
        Assert.Contains("user-interface aux 0", cmds);
        Assert.DoesNotContain(cmds, c => c.Contains("line vty 0 63", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(cmds, c => c.Contains("line con 0", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("user privilege level 3", cmds);

        // Persistência canônica
        Assert.Contains("save safely force", cmds);
    }

    [Fact]
    public void IsComware5_DetectsComware5Versions()
    {
        Assert.True(HpeSaipConfigurator.IsComware5("Comware Software, Version 5.20.106, Release 2514P14"));
        Assert.True(HpeSaipConfigurator.IsComware5("HPE Comware Platform Software\nComware Software, Version 5.20.106, Release 2514P14"));
        Assert.True(HpeSaipConfigurator.IsComware5("Comware Version 5.20"));

        Assert.False(HpeSaipConfigurator.IsComware5("Comware Software, Version 7.10, Release 1234"));
        Assert.False(HpeSaipConfigurator.IsComware5(""));
        Assert.False(HpeSaipConfigurator.IsComware5(null));
    }

    [Fact]
    public void HpeProvisioningValidator_EvaluatesPassReportAccurately()
    {
        var report = new HpeValidationReport();

        var ipBriefOutput = @"
Brief information on interfaces in route mode.
Link: ADM - administratively down; Sts - Operation status
Protocol: (s) - spoofing
Interface            IP Address/Mask      Physical Protocol
GE0/0                201.90.204.22        UP       UP
GE0/1                200.182.245.17       UP       UP
";
        HpeProvisioningValidator.AuditIpInterfaces(report, ipBriefOutput, SampleCircuit, "GigabitEthernet0/0", "GigabitEthernet0/1");
        HpeProvisioningValidator.AuditDefaultRoute(report, "ip route-static 0.0.0.0 0.0.0.0 201.90.204.21", SampleCircuit.WanGateway);
        HpeProvisioningValidator.AuditLocalUser(report, "local-user EBT class manage", "authorization-attribute user-role network-admin", "service-type telnet");
        HpeProvisioningValidator.AuditTelnet(report, "telnet server enable");
        HpeProvisioningValidator.AuditStartupConfig(report, "Startup saved-configuration file: flash:/startup.cfg");

        Assert.Equal(HpeValidationStatus.Pass, report.OverallStatus);
        Assert.All(report.Items, item => Assert.Equal(HpeValidationStatus.Pass, item.Status));
    }

    [Fact]
    public void HpeProvisioningValidator_DetectsFailuresWhenIpOrRouteMismatch()
    {
        var report = new HpeValidationReport();

        var ipBriefOutput = @"
Interface            IP Address/Mask      Physical Protocol
GE0/0                192.168.1.1          UP       UP
GE0/1                10.0.0.1             UP       UP
";
        HpeProvisioningValidator.AuditIpInterfaces(report, ipBriefOutput, SampleCircuit, "GigabitEthernet0/0", "GigabitEthernet0/1");
        HpeProvisioningValidator.AuditDefaultRoute(report, "ip route-static 0.0.0.0 0.0.0.0 192.168.1.254", SampleCircuit.WanGateway);

        Assert.Equal(HpeValidationStatus.Fail, report.OverallStatus);
        Assert.Contains(report.Items, i => i.Name == "WAN IP" && i.Status == HpeValidationStatus.Fail);
        Assert.Contains(report.Items, i => i.Name == "LAN IP" && i.Status == HpeValidationStatus.Fail);
        Assert.Contains(report.Items, i => i.Name == "Rota Default" && i.Status == HpeValidationStatus.Fail);
    }

    [Fact]
    public void HpeProvisioningValidator_AuditLocalUser_Comware5_Level3Role()
    {
        var report = new HpeValidationReport();

        // Comware 5 usa "user privilege level 3" dentro do local-user
        HpeProvisioningValidator.AuditLocalUser(report,
            "local-user EBT\n password simple PRO1ANPRO1AN\n service-type telnet\n user privilege level 3",
            "",
            "service-type telnet");

        Assert.Equal(HpeValidationStatus.Pass, report.OverallStatus);
        Assert.All(report.Items, item => Assert.Equal(HpeValidationStatus.Pass, item.Status));
    }
}
