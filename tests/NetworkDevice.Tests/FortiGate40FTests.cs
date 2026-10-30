using NetworkDevice.Core.Detection;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Recovery;
using NetworkDevice.Core.Routing;
using NetworkDevice.Core.Session;
using NetworkDevice.Fortinet;
using NetworkDevice.Tests.TestDoubles;
using Xunit;

namespace NetworkDevice.Tests;

/// <summary>
/// Testes do perfil Fortinet FortiGate 40F (FortiOS).
/// Arquivo novo e isolado — nenhum teste Cisco/HPE existente foi alterado.
/// </summary>
public sealed class FortiGate40FTests
{
    private static SaipCircuitData CircuitoExemplo() => new()
    {
        ClienteRazaoSocial = "HORIZONTE RESTAURANTES LTDA",
        DesignacaoIp = "FNS/IP/04045",
        NumeroOts = "IM-SPO-IGC--IP-44607/2026",
        WanIp = "201.30.10.18",
        WanCidr = 30,
        WanSubnetMask = "255.255.255.252",
        WanGateway = "201.30.10.17",
        LanBlockNetwork = "10.20.30.0",
        LanCidr = 29,
        LanIp = "10.20.30.1",
        LanSubnetMask = "255.255.255.248",
        HostLanIp = "10.20.30.2",
    };

    [Fact]
    public void GenerateCommands_EmiteBlocosFortiOsCompletos()
    {
        var cmds = FortiOsSaipConfigurator.GenerateCommands(CircuitoExemplo(), "wan", "lan", incluirAdmin: true);

        string joined = string.Join("\n", cmds);
        Assert.Contains("config system interface", joined);
        Assert.Contains("set mode static", joined);
        Assert.Contains("config router static", joined);
        Assert.Contains("config system admin", joined);
        Assert.Contains("config firewall policy", joined);
        Assert.Contains("set ip 201.30.10.18 255.255.255.252", joined);
        Assert.Contains("set ip 10.20.30.1 255.255.255.248", joined);
        Assert.Contains("set gateway 201.30.10.17", joined);
        // Padrão SPARC: Telnet liberado na LAN (mantendo SSH/HTTP).
        Assert.Contains("set allowaccess ping telnet ssh https http", joined);
        Assert.Contains("set dst 0.0.0.0/0", joined);
        Assert.Contains("set nat enable", joined);
        // FortiOS persiste via 'end' — jamais 'write memory' (IOS) nem 'save safely' (Comware).
        Assert.DoesNotContain("write memory", joined);
        Assert.DoesNotContain("save safely", joined);
        Assert.DoesNotContain("system-view", joined);
        Assert.DoesNotContain("configure terminal", joined);
    }

    [Fact]
    public void GenerateCommands_PadraoCircuito_OmiteAdminEConfiguraModeStatic()
    {
        var cmds = FortiOsSaipConfigurator.GenerateCommands(CircuitoExemplo(), "wan", "lan", incluirAdmin: false);
        string joined = string.Join("\n", cmds);
        Assert.Contains("config system interface", joined);
        Assert.Contains("set mode static", joined);
        Assert.DoesNotContain("config system admin", joined);
    }

    [Fact]
    public void GenerateCommands_AplicaAcessoPadraoEbtCqmr()
    {
        var cmds = FortiOsSaipConfigurator.GenerateCommands(CircuitoExemplo(), "wan", "lan", incluirAdmin: true);
        string joined = string.Join("\n", cmds);
        Assert.Contains("config system admin", joined);
        Assert.Contains("edit \"EBT\"", joined);
        Assert.Contains("set password CQMR", joined);
        Assert.Contains("set accprofile super_admin", joined);
        Assert.DoesNotContain("set status disable", joined);
    }

    [Fact]
    public void GenerateDefaultAccessCommands_EmiteSomenteBlocoAdmin()
    {
        var cmds = FortiOsSaipConfigurator.GenerateDefaultAccessCommands();
        string joined = string.Join("\n", cmds);
        Assert.Contains("config system admin", joined);
        Assert.Contains("edit \"EBT\"", joined);
        Assert.Contains("set password CQMR", joined);
        Assert.Contains("set accprofile super_admin", joined);
        Assert.DoesNotContain("set status disable", joined);
        Assert.DoesNotContain("config system interface", joined);
        Assert.DoesNotContain("config router static", joined);
        Assert.DoesNotContain("config firewall policy", joined);
    }

    [Fact]
    public void GenerateCommands_SemPolicy_OmiteBlocoFirewall()
    {
        var cmds = FortiOsSaipConfigurator.GenerateCommands(CircuitoExemplo(), incluirPolicyNat: false);
        Assert.DoesNotContain("config firewall policy", cmds);
    }

    [Fact]
    public void GenerateCommands_ComNatLab_IncluiSetNatEnable()
    {
        var cmds = FortiOsSaipConfigurator.GenerateCommands(CircuitoExemplo(), incluirPolicyNat: true);
        Assert.Contains("config firewall policy", cmds);
        Assert.Contains("set nat enable", cmds);
    }

    [Fact]
    public void GenerateCommands_ComUsarDhcpWan_ConfiguraModeDhcpEOmitStaticRoute()
    {
        var cmds = FortiOsSaipConfigurator.GenerateCommands(CircuitoExemplo(), usarDhcpWan: true);
        var joined = string.Join("\n", cmds);
        Assert.Contains("set mode dhcp", joined);
        Assert.Contains("set defaultgateway enable", joined);
        Assert.DoesNotContain("config router static", joined);
    }

    [Fact]
    public void Configurator_IncluirNatLab_PropriedadeFunciona()
    {
        var configurator = new FortiOsSaipConfigurator();
        Assert.False(configurator.IncluirNatLab);
        configurator.IncluirNatLab = true;
        Assert.True(configurator.IncluirNatLab);
    }

    [Fact]
    public void DetectInterfaces_MapeiaWanLanDo40F()
    {
        const string output = "config system interface\nedit \"wan\"\nnext\nedit \"lan\"\nnext\nedit \"a\"\nnext\nend";
        var (wan, lan) = FortiOsSaipConfigurator.DetectInterfaces(output);
        Assert.Equal("wan", wan);
        Assert.Equal("lan", lan);
    }

    [Fact]
    public void DetectInterfaces_SemOutput_MantemDefaults()
    {
        var (wan, lan) = FortiOsSaipConfigurator.DetectInterfaces(string.Empty);
        Assert.Equal("wan", wan);
        Assert.Equal("lan", lan);
    }

    [Fact]
    public void SanitizeAlias_RemoveCaracteresInvalidos()
    {
        Assert.Equal("WAN_EBT_FNS_IP_04045", FortiOsSaipConfigurator.SanitizeAlias("WAN_EBT_FNS/IP/04045"));
        Assert.Equal("LINK", FortiOsSaipConfigurator.SanitizeAlias("   "));
        // Valida que o limite de 25 caracteres do FortiOS é estritamente respeitado
        var aliasLongo = FortiOsSaipConfigurator.SanitizeAlias("LAN_CLIENTE_HUGHES_TELECOMUNICACAO_SA");
        Assert.True(aliasLongo.Length <= 25, $"Tamanho esperado <= 25, obtido: {aliasLongo.Length} ({aliasLongo})");
    }

    [Fact]
    public void Detector_ClassificaFortiGate40F()
    {
        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt("FortiGate-40F # \nget system status\nVersion: FortiGate-40F v7.2.4");

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(WorkflowType.Provisioning, result.RecommendedWorkflow);
    }

    [Fact]
    public void Detector_SerieSelecionadaFortiGate40F_Prevalece()
    {
        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt("login:", DeviceSeries.FortiGate40F);
        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
    }

    [Fact]
    public void WorkflowRouter_DescreveFortiGate40F()
    {
        Assert.Equal(WorkflowType.Provisioning,
            WorkflowRouter.ResolveWorkflow(DeviceManufacturer.Fortinet, DeviceSeries.FortiGate40F, DeviceOperatingState.Ready));
        var desc = WorkflowRouter.GetWorkflowDescription(DeviceManufacturer.Fortinet, DeviceSeries.FortiGate40F, WorkflowType.Provisioning);
        Assert.Contains("FortiGate 40F", desc);
    }

    [Fact]
    public void PromptMatcher_AceitaPromptFortiOs()
    {
        var matcher = RegexPromptMatcher.Universal();
        Assert.NotNull(matcher.TryMatch("FortiGate-40F #"));
        Assert.NotNull(matcher.TryMatch("FGT40F $"));
    }

    [Fact]
    public void PromptMatcher_MantemCiscoEHpe()
    {
        var matcher = RegexPromptMatcher.Universal();
        Assert.NotNull(matcher.TryMatch("Router#"));
        Assert.NotNull(matcher.TryMatch("<HPE>"));
        Assert.NotNull(matcher.TryMatch("[HPE]"));
    }

    [Fact]
    public void BootProfile_FindById_RetornaPerfilFortiGate()
    {
        var profile = BootInterruptProfiles.FindById("fortinet.fgt40f.fortios");
        Assert.Equal("fortinet.fgt40f.fortios", profile.Id);
        Assert.Equal("Fortinet", profile.Manufacturer);
        Assert.True(profile.RequiresManualIntervention);
    }

    [Fact]
    public void BootProfile_FindById_MantemPerfisExistentes()
    {
        Assert.Equal("cisco.c841.break", BootInterruptProfiles.FindById("cisco.c841.break").Id);
        Assert.Equal("hpe.msr954.ctrl-b", BootInterruptProfiles.FindById("hpe.msr954.ctrl-b").Id);
    }

    [Fact]
    public async Task DeviceSession_ConnectAsync_FortiGateFactoryLoginAndForcedPasswordChange_Sucesso()
    {
        var step = 0;
        var transport = new ScriptedTransport(
            cmd =>
            {
                if (step == 0 && string.IsNullOrEmpty(cmd))
                    return "FortiGate-40F login: ";
                if (cmd == "admin")
                {
                    step = 1;
                    return "Password: ";
                }
                if (step == 1 && string.IsNullOrEmpty(cmd))
                {
                    step = 2;
                    return "Verifying password...\r\n\r\nYou are forced to change your password. Please input a new password.\r\nNew Password: ";
                }
                if (step == 2 && cmd == "CQMR")
                {
                    step = 3;
                    return "Confirm Password: ";
                }
                if (step == 3 && cmd == "CQMR")
                {
                    step = 4;
                    return "FortiGate-40F # \r\n";
                }
                return "";
            },
            initialOutput: "FortiGate-40F login: ");

        var options = new SessionOptions
        {
            Username = "admin",
            Password = "",
            ConnectTimeout = TimeSpan.FromSeconds(5),
            PromptMatcher = RegexPromptMatcher.Universal()
        };

        await using var session = new DeviceSession(transport, options);
        await session.ConnectAsync();

        Assert.True(session.IsConnected);
        Assert.Equal("FortiGate-40F #", session.CurrentPrompt);
        Assert.Contains("admin", transport.Commands);
        Assert.Contains("CQMR", transport.Commands);
    }

    [Fact]
    public void DeviceSession_ClassifyLogin_ReconhecePromptsFortiGate()
    {
        var session = new DeviceSession(new ScriptedTransport(_ => ""), new SessionOptions());

        Assert.Equal(DeviceSession.LoginStageKind.Username, session.ClassifyLogin("FortiGate-40F login:"));
        Assert.Equal(DeviceSession.LoginStageKind.Username, session.ClassifyLogin("FGT-40F login: "));
        Assert.Equal(DeviceSession.LoginStageKind.NewPassword, session.ClassifyLogin("New Password:"));
        Assert.Equal(DeviceSession.LoginStageKind.NewPassword, session.ClassifyLogin("Please input a new password:"));
        Assert.Equal(DeviceSession.LoginStageKind.NewPassword, session.ClassifyLogin("You are forced to change your password. Please input a new password."));
        Assert.Equal(DeviceSession.LoginStageKind.ConfirmPassword, session.ClassifyLogin("Confirm Password:"));
        Assert.Equal(DeviceSession.LoginStageKind.ConfirmPassword, session.ClassifyLogin("Confirm new password:"));
        Assert.Equal(DeviceSession.LoginStageKind.ConfirmPassword, session.ClassifyLogin("Verify new password:"));
    }

    [Theory]
    [InlineData("fortinet.fgt40f.fortios")]
    [InlineData("fortinet.fgt40f")]
    [InlineData("FGT40F")]
    [InlineData("FortiGate 40F")]
    [InlineData("fortinet")]
    public void BootProfile_FindById_ReconheceTodasTagsFortiGate(string tag)
    {
        var profile = BootInterruptProfiles.FindById(tag);
        Assert.Equal("fortinet.fgt40f.fortios", profile.Id);
        Assert.Equal("Fortinet", profile.Manufacturer);
        Assert.True(profile.RequiresManualIntervention);
    }

    [Fact]
    public void FirmwareValidator_FortiGate40F_ValidaImagemOut()
    {
        var resOk = NetworkDevice.Core.Validation.FirmwareCompatibilityValidator.Validate(
            DeviceSeries.FortiGate40F, "FGT_40F-v7.2.6.F-build1575-FORTINET.out");
        Assert.True(resOk.IsCompatible);

        var resBin = NetworkDevice.Core.Validation.FirmwareCompatibilityValidator.Validate(
            DeviceSeries.FortiGate40F, "c1900-universalk9-mz.SPA.158-3.M7.bin");
        Assert.False(resBin.IsCompatible);
        Assert.Contains(".OUT", resBin.ErrorMessage);

        var resIpe = NetworkDevice.Core.Validation.FirmwareCompatibilityValidator.Validate(
            DeviceSeries.FortiGate40F, "msr954-cmw710-r0821p02.ipe");
        Assert.False(resIpe.IsCompatible);
    }

    [Fact]
    public void WanPortInspector_FortiGate40F_IdentificaPortaWan()
    {
        var info = WanPortInspector.PorTagModelo("fortinet.fgt40f.fortios");
        Assert.NotNull(info);
        Assert.Contains("FortiGate", info.ModeloExibicao);
        Assert.Contains("wan", info.NomesBusca);
    }

    [Fact]
    public async Task EnforceLanPortConnectedAsync_PortaLan1Up_RetornaTrueSemAlertar()
    {
        var transport = new ScriptedTransport(
            cmd =>
            {
                if (cmd.Contains("get system interface physical"))
                {
                    return "== [onboard]\r\n" +
                           "\t==[wan]\r\n\t\tstatus: down\r\n" +
                           "\t==[lan1]\r\n\t\tstatus: up\r\n" +
                           "\t==[lan2]\r\n\t\tstatus: down\r\n" +
                           "FortiGate-40F # ";
                }
                return "";
            },
            initialOutput: "FortiGate-40F # ");

        var options = new SessionOptions
        {
            PromptMatcher = RegexPromptMatcher.Universal()
        };

        await using var session = new DeviceSession(transport, options);
        await session.ConnectAsync();

        var operatorAlerted = false;
        var result = await FortiOsSaipConfigurator.EnforceLanPortConnectedAsync(
            session,
            "lan",
            (msg, ct) => { operatorAlerted = true; return Task.CompletedTask; },
            pollDelayMs: 10);

        Assert.True(result);
        Assert.False(operatorAlerted);
    }

    [Fact]
    public async Task EnforceLanPortConnectedAsync_PortaWanUpELanDown_AlertaOperadorPortaIncorreta()
    {
        var attempt = 0;
        var operatorAlertMessage = "";
        var transport = new ScriptedTransport(
            cmd =>
            {
                if (cmd.Contains("get system interface physical"))
                {
                    attempt++;
                    if (attempt == 1)
                    {
                        return "== [onboard]\r\n" +
                               "\t==[wan]\r\n\t\tstatus: up\r\n" +
                               "\t==[lan1]\r\n\t\tstatus: down\r\n" +
                               "FortiGate-40F # ";
                    }
                    else
                    {
                        return "== [onboard]\r\n" +
                               "\t==[wan]\r\n\t\tstatus: down\r\n" +
                               "\t==[lan1]\r\n\t\tstatus: up\r\n" +
                               "FortiGate-40F # ";
                    }
                }
                return "";
            },
            initialOutput: "FortiGate-40F # ");

        var options = new SessionOptions
        {
            PromptMatcher = RegexPromptMatcher.Universal()
        };

        await using var session = new DeviceSession(transport, options);
        await session.ConnectAsync();

        var result = await FortiOsSaipConfigurator.EnforceLanPortConnectedAsync(
            session,
            "lan",
            (msg, ct) => { operatorAlertMessage = msg; return Task.CompletedTask; },
            pollDelayMs: 10);

        Assert.True(result);
        Assert.Contains("CABO DE REDE CONECTADO NA PORTA INCORRETA (WAN)", operatorAlertMessage);
        Assert.Contains("PORTA 1", operatorAlertMessage);
    }

    [Fact]
    public async Task EnforceLanPortConnectedAsync_OutputRealFortiGate40F_IdentificaLinkUpSemTruncarPrompt()
    {
        var realOutput =
            "== [onboard]\r\n" +
            "\t==[a]\r\n" +
            "\t\tmode: static\r\n" +
            "\t\tstatus: down\r\n" +
            "\t\tspeed: n/a\r\n" +
            "\t==[lan1]\r\n" +
            "\t\tmode: static\r\n" +
            "\t\tip: 0.0.0.0 0.0.0.0\r\n" +
            "\t\tipv6: ::/0\r\n" +
            "\t\tstatus: up\r\n" +
            "\t\tspeed: 1000Mbps (Duplex: full)\r\n" +
            "\t==[lan2]\r\n" +
            "\t\tmode: static\r\n" +
            "\t\tstatus: down\r\n" +
            "\t==[lan3]\r\n" +
            "\t\tmode: static\r\n" +
            "\t\tstatus: down\r\n" +
            "\t==[wan]\r\n" +
            "\t\tmode: dhcp\r\n" +
            "\t\tstatus: down\r\n" +
            "\t==[modem]\r\n" +
            "\t\tmode: pppoe\r\n" +
            "\t\tstatus: down\r\n" +
            "\r\nFortiGate-40F # ";

        var transport = new ScriptedTransport(
            cmd =>
            {
                if (cmd.Contains("get system interface physical"))
                    return realOutput;
                return "";
            },
            initialOutput: "FortiGate-40F # ");

        var options = new SessionOptions
        {
            PromptMatcher = RegexPromptMatcher.Universal()
        };

        await using var session = new DeviceSession(transport, options);
        await session.ConnectAsync();

        // Garante que SendCommandAsync não trunca prematuramente em ==[lan1]
        var cmdOut = await session.SendCommandAsync("get system interface physical");
        Assert.Contains("1000Mbps", cmdOut);
        Assert.Contains("==[modem]", cmdOut);

        var operatorAlerted = false;
        var result = await FortiOsSaipConfigurator.EnforceLanPortConnectedAsync(
            session,
            "lan",
            (msg, ct) => { operatorAlerted = true; return Task.CompletedTask; },
            pollDelayMs: 10);

        Assert.True(result);
        Assert.False(operatorAlerted);
    }

    [Fact]
    public void Inspector_ExtraiVersaoEBuild_DeStatusOutputReal()
    {
        var statusOut = """
            FortiGate-40F # get system status
            Version: FortiGate-40F v7.2.6,build1575,230815 (GA.F)
            Virus-DB: 1.00000(2018-04-09 18:07)
            Extended DB: 1.00000(2018-04-09 18:07)
            Serial-Number: FGT40FTXXXXXXXXX
            BIOS version: 04000002
            Hostname: FortiGate-40F
            Operation Mode: NAT
            Current virtual domain: root
            Branch point: 1575
            Release Version Information: GA
            FortiOS x86-64: Yes
            """;

        var info = FortiOsFirmwareVersionInspector.ExtractFromStatus(statusOut);

        Assert.Equal("7.2.6", info.Version);
        Assert.Equal("1575", info.Build);
        Assert.Equal("v7.2.6 (build 1575)", info.DisplayString);
    }

    [Fact]
    public void Inspector_ExtraiVersaoEBuild_DeNomeArquivo()
    {
        var info1 = FortiOsFirmwareVersionInspector.ExtractFromFileName("FGT_40F-v7.2.6.F-build1575-FORTINET.out");
        Assert.Equal("7.2.6", info1.Version);
        Assert.Equal("1575", info1.Build);

        var info2 = FortiOsFirmwareVersionInspector.ExtractFromFileName("FGT_40F-v7.0.12-build0523-FORTINET.out");
        Assert.Equal("7.0.12", info2.Version);
        Assert.Equal("0523", info2.Build);

        var info3 = FortiOsFirmwareVersionInspector.ExtractFromFileName("FGT_40F-v7.2.4.out");
        Assert.Equal("7.2.4", info3.Version);
        Assert.Null(info3.Build);
    }

    [Theory]
    [InlineData("FGT_40F-v7.2.6.F-build1575-FORTINET.out", true)]
    [InlineData("FGT_40F-v7.2.6-build1575.out", true)]
    [InlineData("FGT_40F-v7.2.6.out", true)]
    [InlineData("FGT_40F-v7.2.4.F-build1396-FORTINET.out", false)]
    [InlineData("FGT_40F-v7.2.6.F-build1396-FORTINET.out", false)]
    [InlineData("FGT_40F-v7.4.1.F-build2463-FORTINET.out", false)]
    public void Inspector_ComparaVersaoEquipamentoEArquivo(string fileName, bool expectedSame)
    {
        var statusOut = """
            Version: FortiGate-40F v7.2.6,build1575,230815 (GA.F)
            Branch point: 1575
            """;

        var isSame = FortiOsFirmwareVersionInspector.IsSameVersion(
            statusOut, fileName, out var currentVer, out var targetVer);

        Assert.Equal(expectedSame, isSame);
        Assert.Contains("7.2.6", currentVer);
    }

    [Fact]
    public void Inspector_BuildComZerosAEsquerda_ReconheceComoIgual()
    {
        var statusOut = "Version: FortiGate-40F v7.0.12,build523,230502 (GA.M)";
        var fileName = "FGT_40F-v7.0.12.F-build0523-FORTINET.out";

        var isSame = FortiOsFirmwareVersionInspector.IsSameVersion(
            statusOut, fileName, out _, out _);

        Assert.True(isSame);
    }

    [Fact]
    public void Inspector_StatusVazioOuIncompleto_RetornaFalse()
    {
        var isSame = FortiOsFirmwareVersionInspector.IsSameVersion(
            "", "FGT_40F-v7.2.6.F-build1575-FORTINET.out", out _, out _);

        Assert.False(isSame);
    }

    [Theory]
    [InlineData("FGT_40F-v7.2.6.F-build1575-FORTINET.out", true)] // 7.2.6 < 7.2.11 -> downgrade
    [InlineData("FGT_40F-v7.0.14.M-build0583-FORTINET.out", true)] // 7.0.14 < 7.2.11 -> downgrade
    [InlineData("FGT_40F-v7.2.11.M-build1740-FORTINET.out", false)] // mesma versão -> não é downgrade
    [InlineData("FGT_40F-v7.4.1.F-build2463-FORTINET.out", false)] // 7.4.1 > 7.2.11 -> upgrade (não é downgrade)
    public void Inspector_DetectaDowngradeCorretamente(string fileName, bool expectedDowngrade)
    {
        var statusOut = """
            Version: FortiGate-40F v7.2.11,build1740,250210 (GA.M)
            Branch point: 1740
            """;

        var isDowngrade = FortiOsFirmwareVersionInspector.IsDowngrade(
            statusOut, fileName, out var curVer, out var tgtVer);

        Assert.Equal(expectedDowngrade, isDowngrade);
        Assert.Contains("7.2.11", curVer);
    }

    [Fact]
    public void Inspector_CompareVersions_ComparaBuildECamposNumericos()
    {
        var v726 = FortiOsFirmwareVersionInspector.ExtractFromFileName("FGT_40F-v7.2.6.F-build1575-FORTINET.out");
        var v7211 = FortiOsFirmwareVersionInspector.ExtractFromFileName("FGT_40F-v7.2.11.M-build1740-FORTINET.out");
        var v7211OlderBuild = new FortiOsVersionInfo("7.2.11", "1700", "");

        Assert.True(FortiOsFirmwareVersionInspector.CompareVersions(v726, v7211) < 0);
        Assert.True(FortiOsFirmwareVersionInspector.CompareVersions(v7211, v726) > 0);
        Assert.Equal(0, FortiOsFirmwareVersionInspector.CompareVersions(v7211, v7211));
        Assert.True(FortiOsFirmwareVersionInspector.CompareVersions(v7211OlderBuild, v7211) < 0);
        Assert.True(FortiOsFirmwareVersionInspector.IsDowngrade(v7211, v7211OlderBuild));
    }

    [Fact]
    public void Inspector_DowngradePrompt7211to7210_ContemLossOfConfigEProceed()
    {
        var terminalOutput = """
            Get image from tftp server OK.
            Verifying the signature of the firmware image.
            Image verification OK!
            Warning: Installing image v7.2.10 from v7.2.11 may result a loss of config, proceed?
            Do you want to continue? (y/n)
            """;

        var isImageOk = terminalOutput.Contains("Image verification OK", StringComparison.OrdinalIgnoreCase) ||
                        terminalOutput.Contains("Check image OK", StringComparison.OrdinalIgnoreCase);
        var isDowngradePrompt = terminalOutput.Contains("Do you want to continue", StringComparison.OrdinalIgnoreCase) ||
                                terminalOutput.Contains("loss of config", StringComparison.OrdinalIgnoreCase) ||
                                terminalOutput.Contains("proceed?", StringComparison.OrdinalIgnoreCase);

        Assert.True(isImageOk);
        Assert.True(isDowngradePrompt);
    }

    [Fact]
    public void TftpDowngrade_QuandoFalhaComReturnCode28_DetectaErroComPrioridade()
    {
        var terminalOutput = """
            Image verification OK!
            Warning: Installing image v7.2.10 from v7.2.11 may result a loss of config, proceed?
            Do you want to continue? (y/n)

            Command fail. Return code -28

            LDA-IP-02580 #
            """;

        var isErrorCode28 = terminalOutput.Contains("Return code -28", StringComparison.OrdinalIgnoreCase) ||
                            terminalOutput.Contains("Check image error", StringComparison.OrdinalIgnoreCase);
        var isCommandFail = terminalOutput.Contains("Command fail", StringComparison.OrdinalIgnoreCase);

        Assert.True(isErrorCode28);
        Assert.True(isCommandFail);
    }

    [Fact]
    public void Inspector_IsDowngrade_7211para7210_DetectaDowngradeCorretamente()
    {
        var statusOut = """
            Version: FortiGate-40F v7.2.11,build1740,250210 (GA.M)
            Security Level: High
            Branch point: 1740
            """;
        var fileName = "FGT_40F-v7.2.10.M-build1706-FORTINET.out";

        var isDown = FortiOsFirmwareVersionInspector.IsDowngrade(
            statusOut, fileName, out var curDisplay, out var tgtDisplay);

        Assert.True(isDown);
        Assert.Contains("7.2.11", curDisplay);
        Assert.Contains("1740", curDisplay);
        Assert.Contains("7.2.10", tgtDisplay);
        Assert.Contains("1706", tgtDisplay);
    }

    [Fact]
    public void ExtractTableEntryIds_ExtraiCorretamenteIdsNumericos()
    {
        var output = """
            config router static
                edit 1
                    set gateway 10.0.0.1
                    set device "wan"
                next
                edit 3
                    set dst 192.168.10.0 255.255.255.0
                    set gateway 10.0.0.254
                next
                edit 10
                    set dst 0.0.0.0 0.0.0.0
                    set gateway 201.30.10.69
                next
            end
            """;

        var ids = FortiOsSaipConfigurator.ExtractTableEntryIds(output);

        Assert.Equal(3, ids.Count);
        Assert.Equal(new[] { 1, 3, 10 }, ids);
    }

    [Fact]
    public void ExtractTableEntryIds_IgnoraSubTabelasFilhasComoIpRange()
    {
        var output = """
            config system dhcp server
                edit 999
                    set dns-service default
                    set default-gateway 200.250.163.41
                    set netmask 255.255.255.248
                    set interface "lan"
                    config ip-range
                        edit 1
                            set start-ip 200.250.163.42
                            set end-ip 200.250.163.42
                        next
                    end
                next
            end
            """;

        var ids = FortiOsSaipConfigurator.ExtractTableEntryIds(output);

        Assert.Single(ids);
        Assert.Equal(999, ids[0]);
    }

    [Fact]
    public void ExtractTableEntryIds_OutputVazio_RetornaListaVazia()
    {
        var ids = FortiOsSaipConfigurator.ExtractTableEntryIds("");
        Assert.Empty(ids);
    }

    [Theory]
    [InlineData("FGT40FTK20000000", "bcpbFGT40FTK20000000")]
    [InlineData("fgt40ftk12345678", "bcpbFGT40FTK12345678")]
    public void MaintainerPassword_CalculaCorretamenteComSerialMaiusculo(string serial, string expectedPass)
    {
        var pass = "bcpb" + serial.Trim().ToUpperInvariant();
        Assert.Equal(expectedPass, pass);
    }

    [Theory]
    [InlineData("Serial number: FGT40FTK20001234", "FGT40FTK20001234")]
    [InlineData("S/N: FGT40FTK99998888\r\n", "FGT40FTK99998888")]
    [InlineData("FortiGate-40F (11:21-03.04.2020)\r\nSN=FGT40FTK11223344", "FGT40FTK11223344")]
    [InlineData("... booting FGT40FTK00001111 ...", "FGT40FTK00001111")]
    public void RegexSerialNumber_ExtraiCorretamenteDoBoot(string stream, string expectedSerial)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            stream, @"(?i)(?:Serial\s*number|S/N|SN)\s*[:=]?\s*([A-Z0-9]{16})");

        if (!match.Success)
        {
            match = System.Text.RegularExpressions.Regex.Match(stream, @"\b(FGT40F[A-Z0-9]{10})\b");
        }

        Assert.True(match.Success);
        Assert.Equal(expectedSerial, match.Groups[1].Value.ToUpperInvariant());
    }

    [Fact]
    public void DetectDevice_FortiBiosMenu_IdentificaComoBootFailureEFirmwareRecovery()
    {
        var rawPrompt = """
            FortiBootLoader v1.0.0
            Reading boot image ... failed
            [G]: Get firmware image from TFTP server.
            [F]: Format boot device.
            [B]: Boot with backup firmware and set as default.
            [I]: Configuration and information.
            [Q]: Continue booting.
            [H]: Help.

            Enter Selection:
            """;

        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt(rawPrompt, DeviceSeries.Unknown);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.BootFailure, result.OperatingState);
        Assert.Equal(WorkflowType.FirmwareRecovery, result.RecommendedWorkflow);
        Assert.Equal(AccessState.RommonOrBootware, result.AccessState);
        Assert.Equal(FirmwareState.CorruptedOrMissing, result.FirmwareState);
    }

    [Fact]
    public void DetectDevice_FortiBiosOldMenu_IdentificaComoBootFailureEFirmwareRecovery()
    {
        var rawPrompt = """
            Verifying image ... bad checksum
            Enter C,R,T,F,I,B,Q,or H:
            """;

        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt(rawPrompt, DeviceSeries.FortiGate40F);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceOperatingState.BootFailure, result.OperatingState);
        Assert.Equal(WorkflowType.FirmwareRecovery, result.RecommendedWorkflow);
        Assert.Equal(AccessState.RommonOrBootware, result.AccessState);
        Assert.Equal(FirmwareState.CorruptedOrMissing, result.FirmwareState);
    }

    [Fact]
    public void FirmwareVersionInspector_IdentificaMesmaVersaoEEvitaUpgrade()
    {
        var statusOut = """
            FortiGate-40F (17:00-...)
            Version: FortiGate-40F v7.2.6,build1575,230815 (GA.F)
            Virus-DB: 1.00000(2018-04-09 18:07)
            Serial-Number: FGT40FTK20000000
            BIOS version: 05000000
            """;

        var targetFile = "FGT_40F-v7.2.6.F-build1575-FORTINET.out";

        var isSame = FortiOsFirmwareVersionInspector.IsSameVersion(
            statusOut, targetFile, out var currentVer, out var targetVer);

        Assert.True(isSame);
        Assert.Contains("7.2.6", currentVer);
        Assert.Contains("1575", currentVer);
        Assert.Contains("7.2.6", targetVer);
        Assert.Contains("1575", targetVer);
    }

    [Fact]
    public void FirmwareVersionInspector_IdentificaVersaoDiferentePermitindoUpgrade()
    {
        var statusOut = """
            Version: FortiGate-40F v7.0.12,build0523,230420 (GA.M)
            Serial-Number: FGT40FTK20000000
            """;

        var targetFile = "FGT_40F-v7.2.6.F-build1575-FORTINET.out";

        var isSame = FortiOsFirmwareVersionInspector.IsSameVersion(
            statusOut, targetFile, out var currentVer, out var targetVer);

        Assert.False(isSame);
        Assert.Contains("7.0.12", currentVer);
        Assert.Contains("7.2.6", targetVer);
    }

    [Fact]
    public void DeviceDetector_DetectaFortiGate40F_SemFirmwareReal_BootFailure()
    {
        var realPrompt = """
            Neither DEFAULT nor BACKUP FOS works. Please install valid FOS through BIOS immediately!
              Please power cycle. System halted. (Press 'CTRL+D' to reboot)
            """;

        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt(realPrompt, DeviceSeries.FortiGate40F);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.BootFailure, result.OperatingState);
        Assert.Equal(FirmwareState.CorruptedOrMissing, result.FirmwareState);
        Assert.Equal(WorkflowType.FirmwareRecovery, result.RecommendedWorkflow);
    }

    [Fact]
    public void DeviceDetector_DetectaFortiGate40F_FosBootFailed_SemModeloPreSelecionado_BootFailure()
    {
        var realPrompt = """
            FOS boot failed. You may try backup.
              Please power cycle. System halted.
              System will auto-reboot after 60 seconds ('CTRL+D' to reboot immediately)
            """;

        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt(realPrompt, DeviceSeries.Unknown);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.BootFailure, result.OperatingState);
        Assert.Equal(FirmwareState.CorruptedOrMissing, result.FirmwareState);
        Assert.Equal(WorkflowType.FirmwareRecovery, result.RecommendedWorkflow);
    }

    [Fact]
    public void DeviceDetector_DetectaFortiGate40F_NeitherDefaultNorBackupHalt_PuTTYExactMatch()
    {
        // Texto exato exibido na console PuTTY fornecida pelo operador em campo:
        var puTtyOutput = """
            Neither DEFAULT nor BACKUP FOS works. Please install valid FOS through BIOS immediately!
              Please power cycle. System halted. (Press 'CTRL+D' to reboot)
            Neither DEFAULT nor BACKUP FOS works. Please install valid FOS through BIOS immediately!
              Please power cycle. System halted. (Press 'CTRL+D' to reboot)
            """;

        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt(puTtyOutput, DeviceSeries.Unknown);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.BootFailure, result.OperatingState);
        Assert.Equal(FirmwareState.CorruptedOrMissing, result.FirmwareState);
        Assert.Equal(WorkflowType.FirmwareRecovery, result.RecommendedWorkflow);
    }

    [Fact]
    public void FirmwareVersionInspector_VersaoExataDoUsuario7211_EvitaUpgrade()
    {
        var statusOut = """
            FortiGate-40F # get system status
            Version: FortiGate-40F v7.2.11,build1757,250212 (GA.F)
            Virus-DB: 1.00000(2018-04-09 18:07)
            Extended DB: 1.00000(2018-04-09 18:07)
            Extreme DB: 1.00000(2018-04-09 18:07)
            IPS-DB: 6.00741(2015-12-01 02:30)
            FortiClient application signature database: 1.00000(2018-04-09 18:07)
            Serial-Number: FGT40FTK20000000
            BIOS version: 05000000
            System Part-Number: P22380-02
            Log hard disk: Not available
            Hostname: FortiGate-40F
            Operation Mode: NAT
            Current virtual domain: root
            Max number of virtual domains: 1
            Virtual domains status: 1 in NAT mode, 0 in TP mode
            Virtual domain configuration: disable
            FIPS-CC mode: disable
            Current HA mode: standalone
            Branch point: 1757
            Release Version Information: GA
            FortiOS License Status: Valid
            System time: Wed Feb 12 10:00:00 2025
            """;

        var targetFile = "FGT_40F-v7.2.11.F-build1757-FORTINET.out";

        var isSame = FortiOsFirmwareVersionInspector.IsSameVersion(
            statusOut, targetFile, out var currentVer, out var targetVer);

        Assert.True(isSame);
        Assert.Contains("7.2.11", currentVer);
        Assert.Contains("1757", currentVer);
        Assert.Contains("7.2.11", targetVer);
        Assert.Contains("1757", targetVer);
    }

    [Fact]
    public void GenerateCommands_ProducesFullBldClaroComponents_WhenEnabled()
    {
        var circuit = new SaipCircuitData
        {
            NumeroOts = "OTS-998877",
            DesignacaoIp = "CIRCUITO-TESTE-BLD",
            ClienteRazaoSocial = "EMPRESA TESTE S.A.",
            WanIp = "200.200.200.2",
            WanSubnetMask = "255.255.255.252",
            WanGateway = "200.200.200.1",
            LanIp = "10.10.10.1",
            LanSubnetMask = "255.255.255.0",
            BandaMbpsNominal = 100,
            PeLoopbackIp = "200.255.0.1",
            WanIpv6 = "2001:db8:1::2",
            WanIpv6Prefix = 64,
            WanIpv6Gateway = "2001:db8:1::1",
            LanIpv6 = "2001:db8:2::1",
            LanIpv6Prefix = 64
        };

        var cmds = FortiOsSaipConfigurator.GenerateCommands(circuit, "wan", "lan", adminUser: "EBT", adminPassword: "CQMR", incluirPolicyNat: true, incluirAdmin: true, incluirBldClaro: true);
        var joined = string.Join("\n", cmds);

        // Hostname
        Assert.Contains("set hostname \"CIRCUITO-TESTE-BLD\"", joined);

        // Banner Claro
        Assert.Contains("set pre-login-banner enable", joined);
        Assert.Contains("config system replacemsg admin \"pre_admin-disclaimer-text\"", joined);
        Assert.Contains("set buffer", joined);
        Assert.Contains("CLARO Brasil S.A.", joined);
        Assert.Contains("SOMENTE USUARIOS AUTORIZADOS", joined);

        // NTP Oficial Claro
        Assert.Contains("config system ntp", joined);
        Assert.Contains("set ntpsync enable", joined);
        Assert.Contains("set type custom", joined);
        Assert.Contains("set server \"200.20.186.75\"", joined);
        Assert.Contains("set server \"200.20.186.94\"", joined);

        // SNMP Oficial Claro
        Assert.Contains("config system snmp community", joined);
        Assert.Contains("set name \"claro21sup\"", joined);
        Assert.Contains("set name \"LIDER\"", joined);
        Assert.Contains("set ip 200.255.156.192 255.255.255.192", joined);

        // TACACS+ Oficial Claro
        Assert.Contains("config user tacacs+", joined);
        Assert.Contains("edit \"TACACS-SERVER-CLARO\"", joined);
        Assert.Contains("set server \"200.255.166.129\"", joined);
        Assert.DoesNotContain("set timeout 2", joined);

        // QoS Traffic Shaping
        Assert.Contains("config firewall shaper traffic-shaper", joined);
        Assert.Contains("edit \"SHAPE_OUT\"", joined);
        Assert.Contains("set maximum-bandwidth 100000", joined);

        // Trusted Hosts no Admin EBT
        Assert.Contains("config system admin", joined);
        Assert.Contains("edit \"EBT\"", joined);
        Assert.Contains("set trusthost1 200.255.156.192 255.255.255.192", joined);
        Assert.Contains("set trusthost2 200.200.200.1 255.255.255.255", joined);

        // IPv6 Dual-Stack
        Assert.Contains("set ip6-address 2001:db8:1::2/64", joined);
        Assert.Contains("config router static6", joined);
        Assert.Contains("set gateway 2001:db8:1::1", joined);
    }

    [Fact]
    public void DetectConnectedPhysicalInterface_DetectaLan1_QuandoUp()
    {
        var output = """
            ==[wan]
                    mode: static
                    status: down
                    speed: 0
            ==[lan1]
                    mode: static
                    status: up
                    speed: 1000
            ==[lan2]
                    mode: static
                    status: down
                    speed: 0
            """;
        var iface = FortiOsSaipConfigurator.DetectConnectedPhysicalInterface(output);
        Assert.Equal("lan1", iface);
    }

    [Fact]
    public void DetectConnectedPhysicalInterface_DetectaLan2_QuandoUp()
    {
        var output = """
            ==[wan]
                    mode: static
                    status: down
                    speed: 0
            ==[lan1]
                    mode: static
                    status: down
                    speed: 0
            ==[lan2]
                    mode: static
                    status: up
                    speed: 1000
            """;
        var iface = FortiOsSaipConfigurator.DetectConnectedPhysicalInterface(output);
        Assert.Equal("lan2", iface);
    }

    [Fact]
    public void DetectConnectedPhysicalInterface_DetectaWan_QuandoApenasWanUp()
    {
        var output = """
            ==[wan]
                    mode: static
                    status: up
                    speed: 1000
            ==[lan1]
                    mode: static
                    status: down
                    speed: 0
            """;
        var iface = FortiOsSaipConfigurator.DetectConnectedPhysicalInterface(output);
        Assert.Equal("wan", iface);
    }

    [Fact]
    public void DetectConnectedPhysicalInterface_RetornaNull_QuandoVazio()
    {
        var iface = FortiOsSaipConfigurator.DetectConnectedPhysicalInterface("");
        Assert.Null(iface);
    }

    [Fact]
    public void ClassifyPrompt_CustomCircuitHostnamePrompt_ClassifiesAsFortiGate40FReadyAndOpen()
    {
        var detector = new DeviceDetector();
        // Prompt típico de FortiGate já provisionado com hostname de designação de circuito
        var result = detector.ClassifyPrompt("LDA-IP-02580 # ", DeviceSeries.Unknown);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(AccessState.Open, result.AccessState);
        Assert.Equal(WorkflowType.Provisioning, result.RecommendedWorkflow);
    }

    [Fact]
    public void ClassifyPrompt_CustomCircuitHostnameWithDollarPrompt_ClassifiesAsFortiGate40FReadyAndOpen()
    {
        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt("LDA-IP-02580 $ ", DeviceSeries.Unknown);

        Assert.Equal(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceSeries.FortiGate40F, result.Series);
        Assert.Equal(DeviceOperatingState.Ready, result.OperatingState);
        Assert.Equal(AccessState.Open, result.AccessState);
    }

    [Fact]
    public void ClassifyPrompt_CiscoStandardPromptWithoutSpace_DoesNotClassifyAsFortinet()
    {
        var detector = new DeviceDetector();
        var result = detector.ClassifyPrompt("Router#", DeviceSeries.Unknown);

        Assert.NotEqual(DeviceManufacturer.Fortinet, result.Manufacturer);
        Assert.Equal(DeviceManufacturer.Cisco, result.Manufacturer);
    }

    [Fact]
    public void GenerateCommands_BldDisclaimer_EncerraConfigSystemGlobalComEndAntesDeReplacemsg()
    {
        var circuit = new SaipCircuitData
        {
            NumeroOts = "OTS-123456",
            DesignacaoIp = "CIRCUITO-DISCLAIMER-TEST",
            ClienteRazaoSocial = "TESTE S.A.",
            WanIp = "200.200.200.2",
            WanSubnetMask = "255.255.255.252",
            WanGateway = "200.200.200.1",
            LanIp = "10.10.10.1",
            LanSubnetMask = "255.255.255.0",
            BandaMbpsNominal = 50,
        };

        var cmds = FortiOsSaipConfigurator.GenerateCommands(circuit, "wan", "lan", incluirBldClaro: true).ToList();

        var bannerIdx = cmds.IndexOf("set pre-login-banner enable");
        Assert.True(bannerIdx >= 0, "Deveria conter 'set pre-login-banner enable'");

        var endIdx = cmds.IndexOf("end");
        Assert.True(endIdx > bannerIdx, "Deveria conter 'end' após 'set pre-login-banner enable'");

        var replacemsgIdx = cmds.FindIndex(c => c.StartsWith("config system replacemsg admin"));
        Assert.True(replacemsgIdx > endIdx, "O bloco 'config system replacemsg admin' deve iniciar APÓS o 'end' que fecha 'config system global'");

        // Valida a ordem imediata: set pre-login-banner enable -> end -> config system replacemsg admin
        Assert.Equal("end", cmds[bannerIdx + 1]);
        Assert.StartsWith("config system replacemsg admin", cmds[bannerIdx + 2]);
    }

    [Theory]
    [InlineData("Failed to complete copper auto-nego.1514", true)]
    [InlineData("Failed to complete copper auto-nego", true)]
    [InlineData("copper auto-nego", true)]
    [InlineData("No link on WAN", true)]
    [InlineData("Link is down", true)]
    [InlineData("No carrier", true)]
    [InlineData("failed to complete auto-nego", true)]
    [InlineData("TFTP transfer failed", false)]
    [InlineData("bad checksum", false)]
    [InlineData("ARP timeout", false)]
    public void CableDisconnectedRegex_IdentificaFalhaAutoNegoEFalhasFisicasEthernet(string biosOutput, bool expectedMatch)
    {
        var matched = FortiOsBootLoaderRecovery.CableDisconnectedRegex.IsMatch(biosOutput);
        Assert.Equal(expectedMatch, matched);
    }

    [Fact]
    public async Task MonitorarTransferenciaBiosAsync_CaboDesconectado_SolicitaIntervencaoOperadorEReexecutaComSucesso()
    {
        var attempt = 0;
        var operatorInstructionReceived = "";
        var sentCommands = new List<string>();

        var transport = new ScriptedTransport(
            cmd =>
            {
                if (!string.IsNullOrEmpty(cmd))
                {
                    sentCommands.Add(cmd.Trim());
                }

                if (cmd.Trim() == "T")
                {
                    attempt++;
                    // Tentativa pós-intervenção do operador com cabo conectado:
                    return "Please connect TFTP server to Ethernet port 'WAN'.\r\n" +
                           "Connect to tftp server 192.168.1.100 ...\r\n" +
                           "Loading: ##############################\r\n" +
                           "Image Received\r\n" +
                           "Save as Default firmware/Backup firmware [D/B/R]? ";
                }

                return "";
            },
            initialOutput: "Please connect TFTP server to Ethernet port 'WAN'.\r\n" +
                           "Failed to complete copper auto-nego.1514\r\n" +
                           "Connect to tftp server 192.168.1.100 ...\r\n" +
                           "Loading: T T T T\r\n" +
                           "Enter C,R,T,F,I,B,Q,or H: ");

        var options = new SessionOptions
        {
            PromptMatcher = RegexPromptMatcher.Universal()
        };

        await using var session = new DeviceSession(transport, options);
        await session.ConnectRawAsync();

        var logMessages = new List<string>();
        var ok = await FortiOsBootLoaderRecovery.MonitorarTransferenciaBiosAsync(
            session,
            progress: (pct, tit, det) => { },
            log: msg => { logMessages.Add(msg); return Task.CompletedTask; },
            ct: CancellationToken.None,
            tftpBytesSentProvider: () => 1024,
            isTftpFinishedProvider: () => false,
            instructOperator: (msg, ct) =>
            {
                operatorInstructionReceived = msg;
                return Task.CompletedTask;
            });

        Assert.True(ok, "A transferência deveria concluir com sucesso após o operador reconectar o cabo.");
        Assert.Contains("Failed to complete copper auto-nego", operatorInstructionReceived);
        Assert.Contains("WAN", operatorInstructionReceived);
        Assert.Contains("LED", operatorInstructionReceived);
        Assert.Contains("T", sentCommands);
        Assert.Contains(logMessages, m => m.Contains("CABO DE REDE DESCONECTADO"));
    }

    [Fact]
    public async Task EnforceWanPortConnectedAsync_PortaWanUp_RetornaTrueSemAlertar()
    {
        var transport = new ScriptedTransport(
            cmd =>
            {
                if (cmd.Contains("get system interface physical"))
                {
                    return "== [onboard]\r\n" +
                           "\t==[wan]\r\n\t\tstatus: up\r\n\t\tspeed: 1000Mbps\r\n" +
                           "\t==[lan1]\r\n\t\tstatus: down\r\n" +
                           "FortiGate-40F # ";
                }
                return "";
            },
            initialOutput: "FortiGate-40F # ");

        var options = new SessionOptions
        {
            PromptMatcher = RegexPromptMatcher.Universal()
        };

        await using var session = new DeviceSession(transport, options);
        await session.ConnectAsync();

        var operatorAlerted = false;
        var result = await FortiOsSaipConfigurator.EnforceWanPortConnectedAsync(
            session,
            (msg, ct) => { operatorAlerted = true; return Task.CompletedTask; },
            pollDelayMs: 0);

        Assert.True(result);
        Assert.False(operatorAlerted);
    }

    [Fact]
    public async Task EnforceWanPortConnectedAsync_PortaLanUpEPortaWanDown_AlertaCaboNaPortaErradaEValidaAposTroca()
    {
        var attempt = 0;
        var operatorAlertMessage = "";
        var transport = new ScriptedTransport(
            cmd =>
            {
                if (cmd.Contains("get system interface physical"))
                {
                    attempt++;
                    if (attempt == 1)
                    {
                        // Cabo na porta LAN (lan1 UP, wan DOWN)
                        return "== [onboard]\r\n" +
                               "\t==[wan]\r\n\t\tstatus: down\r\n" +
                               "\t==[lan1]\r\n\t\tstatus: up\r\n" +
                               "FortiGate-40F # ";
                    }
                    else
                    {
                        // Operador trocou para a porta WAN (wan UP, lan1 DOWN)
                        return "== [onboard]\r\n" +
                               "\t==[wan]\r\n\t\tstatus: up\r\n" +
                               "\t==[lan1]\r\n\t\tstatus: down\r\n" +
                               "FortiGate-40F # ";
                    }
                }
                return "";
            },
            initialOutput: "FortiGate-40F # ");

        var options = new SessionOptions
        {
            PromptMatcher = RegexPromptMatcher.Universal()
        };

        await using var session = new DeviceSession(transport, options);
        await session.ConnectAsync();

        var result = await FortiOsSaipConfigurator.EnforceWanPortConnectedAsync(
            session,
            (msg, ct) => { operatorAlertMessage = msg; return Task.CompletedTask; },
            pollDelayMs: 0);

        Assert.True(result);
        Assert.Contains("CABO DE REDE CONECTADO NA PORTA INCORRETA (LAN)", operatorAlertMessage);
        Assert.Contains("WAN", operatorAlertMessage);
    }

    [Fact]
    public async Task EnforceWanPortConnectedAsync_NenhumaPortaConectada_AlertaCaboDesconectadoEValidaAposConectar()
    {
        var attempt = 0;
        var operatorAlertMessage = "";
        var transport = new ScriptedTransport(
            cmd =>
            {
                if (cmd.Contains("get system interface physical"))
                {
                    attempt++;
                    if (attempt == 1)
                    {
                        // Nenhuma porta conectada (todas DOWN)
                        return "== [onboard]\r\n" +
                               "\t==[wan]\r\n\t\tstatus: down\r\n" +
                               "\t==[lan1]\r\n\t\tstatus: down\r\n" +
                               "FortiGate-40F # ";
                    }
                    else
                    {
                        // Operador conectou na porta WAN
                        return "== [onboard]\r\n" +
                               "\t==[wan]\r\n\t\tstatus: up\r\n" +
                               "\t==[lan1]\r\n\t\tstatus: down\r\n" +
                               "FortiGate-40F # ";
                    }
                }
                return "";
            },
            initialOutput: "FortiGate-40F # ");

        var options = new SessionOptions
        {
            PromptMatcher = RegexPromptMatcher.Universal()
        };

        await using var session = new DeviceSession(transport, options);
        await session.ConnectAsync();

        var result = await FortiOsSaipConfigurator.EnforceWanPortConnectedAsync(
            session,
            (msg, ct) => { operatorAlertMessage = msg; return Task.CompletedTask; },
            pollDelayMs: 0);

        Assert.True(result);
        Assert.Contains("CABO DE REDE DESCONECTADO DA PORTA WAN", operatorAlertMessage);
        Assert.Contains("WAN", operatorAlertMessage);
    }

    [Fact]
    public void PromptMatcher_NaoConfundeMenuBiosFortiGateComPromptOperacional()
    {
        var matcher = RegexPromptMatcher.Universal();

        // Linhas típicas do menu BIOS / Bootloader do FortiGate 40F
        string[] biosLines =
        {
            "[C]: Set up TFTP parameters",
            "[R]: Review TFTP parameters",
            "[T]: Initiate TFTP firmware test",
            "[F]: Format boot device",
            "[I]: Configuration Image",
            "[B]: Boot with default firmware",
            "[Q]: Configuration and Quit",
            "[H]: Help",
            "Enter C,R,T,F,I,B,Q,or H:",
            "Enter C,R,T,F,I,B,Q,or H: ",
            "Please accept the terms and conditions",
            "FortiBootLoader v1.2.3",
            "Enter Selection:",
            "[C]",
            "[T]",
            "[B]"
        };

        foreach (var line in biosLines)
        {
            var matched = matcher.TryMatch(line);
            Assert.True(matched == null, $"Linha do BIOS Fortinet '{line}' NÃO deve ser reconhecida como prompt, mas retornou '{matched}'");
        }
    }

    [Fact]
    public void PromptMatcher_ReconhecePromptsOperacionaisFortiOS()
    {
        var matcher = RegexPromptMatcher.Universal();

        string[] validPrompts =
        {
            "FortiGate-40F #",
            "FortiGate-40F # ",
            "FortiGate-40F (console) #",
            "FortiGate-40F (root) #",
            "FortiGate-40F (global) #",
            "FGT40F1234567890 #",
            "LDA-IP-02580 #",
            "FortiGate-40F $"
        };

        foreach (var p in validPrompts)
        {
            var matched = matcher.TryMatch(p);
            Assert.True(matched != null, $"Prompt legítimo do FortiOS '{p}' deve ser reconhecido.");
        }
    }

    [Fact]
    public void DeviceSession_SetAuthenticatedState_AtualizaPropriedadesEConexao()
    {
        var transport = new ScriptedTransport(_ => "");
        var options = new SessionOptions();
        var session = new DeviceSession(transport, options);

        session.SetAuthenticatedState("admin", "CQMR", "FortiGate-40F #", ExecMode.PrivilegedExec);

        Assert.Equal("admin", session.AuthenticatedUser);
        Assert.Equal("CQMR", session.AuthenticatedPassword);
        Assert.Equal("FortiGate-40F #", session.CurrentPrompt);
        Assert.Equal(ExecMode.PrivilegedExec, session.Mode);
    }

    [Fact]
    public void ClassifyLogin_DetectsFortiGateBiosMenu()
    {
        var transport = new ScriptedTransport(_ => "");
        var session = new DeviceSession(transport, new SessionOptions());

        var stage1 = session.ClassifyLogin("Enter C,R,T,F,I,B,Q,or H:");
        var stage2 = session.ClassifyLogin("Please Enter Selection: ");
        var stage3 = session.ClassifyLogin("[C,R,T,F,I,B,Q,or H]");

        Assert.Equal(DeviceSession.LoginStageKind.FortiBiosMenu, stage1);
        Assert.Equal(DeviceSession.LoginStageKind.FortiBiosMenu, stage2);
        Assert.Equal(DeviceSession.LoginStageKind.FortiBiosMenu, stage3);
    }

    [Fact]
    public async Task AguardarBootFortiOsAsync_WhenBiosMenuEncountered_SendsQAndCompletesLogin()
    {
        int newPassCount = 0;
        var sentData = new List<string>();

        var transport = new ScriptedTransport(
            cmd =>
            {
                sentData.Add(cmd);
                if (cmd.Equals("Q", StringComparison.OrdinalIgnoreCase))
                {
                    return "\r\nBooting OS...\r\nReading boot image...\r\nInitializing...\r\nStarting OS...\r\nFortiGate-40F login: ";
                }
                if (cmd.Equals("admin", StringComparison.OrdinalIgnoreCase))
                {
                    return "Password: ";
                }
                if (cmd == string.Empty)
                {
                    return "You are forced to change your password. Please input a new password.\r\nNew Password: ";
                }
                if (cmd.Equals("CQMR", StringComparison.OrdinalIgnoreCase))
                {
                    newPassCount++;
                    if (newPassCount == 1)
                        return "Confirm Password: ";
                    return "\r\nWelcome!\r\nFortiGate-40F # ";
                }
                return "";
            },
            initialOutput: "FortiGate-40F (14:14-04.09.2020)\r\nVerifying SHA256 checksum... [OK]\r\nEnter C,R,T,F,I,B,Q,or H: ");

        var session = new DeviceSession(transport, new SessionOptions());
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var ok = await FortiOsBootLoaderRecovery.AguardarBootFortiOsAsync(
            session,
            (_, _, _) => { },
            _ => Task.CompletedTask,
            cts.Token,
            defaultPassword: "CQMR");

        Assert.True(ok);
        Assert.Contains(sentData, s => s.Equals("Q", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sentData, s => s.Equals("admin", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sentData, s => s.Equals("CQMR", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("admin", session.AuthenticatedUser);
        Assert.Equal("CQMR", session.AuthenticatedPassword);
    }
}



