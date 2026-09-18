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
                    return "You are forced to change your password. Please input a new password.\r\nNew Password: ";
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
}


