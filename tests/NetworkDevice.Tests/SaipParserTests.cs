using NetworkDevice.Cisco;
using NetworkDevice.Core.Provisioning;
using Xunit;

namespace NetworkDevice.Tests;

public sealed class SaipParserTests
{
    private const string ExemploFichaSaip = @"
SaIP Consultas Gerais
voltar   menu


1 de 1 OTS's - Detalhes da OTS IM-SPO-IGC--IP-44607/2026  [Status no SGAPlus]

Resultado 1 de um total de 1.

DADOS CLIENTE
Produto	Business Link Direct 	
Razão Social	HORIZONTE RESTAURANTES LTDA 	
Conta Corrente	00237577612/0013 (GC/CS) 	
CNPJ (CLE)	58.891.504/0001-29 	
CNPJ Registro	58.891.504/0001-29 (Inválido!) 	
Administrador	 	
Telefone	 	
Email	 	
Designação IP	FNS/IP/04045 	
Estação de Acesso	SOONS 	
Designação de Acesso	FNS 00001101813 	
Desig. de Acesso Redund.	GRADE 185339 	
Número Ots	IM-SPO-IGC--IP-44607/2026  [Status no SGAPlus] 	
Data Alocação	29/07/2026 11:27:12 	
Responsável Alocação	rpa02 	
Tipo de Ots	ATV 	
Designação E1	- 	
Fac. E1 (CLI - EBT)	- 	
Tellabs (CLI - EBT)	- 	
DataCom (CLI - EBT)	- 	
CONVERSOR DE PROTOCOLO	Não 	
PROTOCOLO L2	 	
Domínio	58891504 	
Designação Associada	Nenhum 	
IP Banda Larga	 	
Roteador	AGG01.SOONS 	
Porta	TENGIGA 0/5/1/0/8.106625 	
Vlan Cliente	25 	
Vlan Embratel	1066 	
SERVICE_ID	- 	
CUSTOMER_ID	- 	
IP Serial Usuário (IPv4)	201.030.010.018/30 	
IP Serial Usuário (IPv6)	2804:00A8:0002:00DA:0000:0000:0000:20FA/126 	
Protocolo	ARPA 	
Banda	50000 	
Garantia de Banda	 	
Cir	 	
Pir	 	
Vci	(-) - (-) 	
Encap	 	
DLCI Cliente	 	
LMI Cliente	 	
DLCI EBT	 	
LMI EBT	 	
Roteamento	ESTATICO 	
Tipo Sla	Não 	
Gpa	Não 	
HPOpenView	Não 	
Blocos IPv4	189.016.020.080/29    	
Blocos IPv6	2804:00A8:DACE:0000:0000:0000:0000:0000/56    	
Extranets	Este circuito não participa de extranets. 	
CPE	O circuito não possui CPE 	
Description Roteador	58891504 | 50000K | FNS/IP/04045 | (FNS/IP/04045) 	
Dados de QoS	Não tem QoS 	
Multilink	Não participa de grupo multilink. 	
Multicast	Não possui Multicast configurado. 	
Observações	- 	
Bloco de Notas	Nenhum registro no bloco de notas.2083509 	
OTS's cadastradas	Não existem OTSs de cancelamento cadastradas para esta OTS. 	
Exceções	- 	
Valor Adicionado	Não possui facilidade de valor adicionado ativada 	
AntiDDOS	Não 	
MPLS Turbinado	Não Possui! 	
Informações de Acesso	
Tipo: 	GPON
Designação: 	VU_FNS1ST_000
Link: 	
PE: 	AGG01.SOONS
Porta: 	TE0/5/1/0/8
VLAN: 	
SWITCH Concentrador: 	
Porta:: 	
 	
topo      consultas      menu
";

    [Fact]
    public void ParseText_ExtractsAllFieldsCorrectly()
    {
        var data = SaipParser.ParseText(ExemploFichaSaip);

        // WAN (Giga 5)
        Assert.Equal("201.30.10.18", data.WanIp);
        Assert.Equal(30, data.WanCidr);
        Assert.Equal("255.255.255.252", data.WanSubnetMask);
        Assert.Equal("201.30.10.17", data.WanGateway);

        // LAN (Giga 4)
        Assert.Equal("189.16.20.80", data.LanBlockNetwork);
        Assert.Equal(29, data.LanCidr);
        Assert.Equal("189.16.20.81", data.LanIp);
        Assert.Equal("255.255.255.248", data.LanSubnetMask);

        // Metadados
        Assert.Equal("HORIZONTE RESTAURANTES LTDA", data.ClienteRazaoSocial);
        Assert.Equal("FNS/IP/04045", data.DesignacaoIp);
    }

    [Fact]
    public void GenerateCommands_ProducesValidCiscoIOSConfig()
    {
        var data = SaipParser.ParseText(ExemploFichaSaip);
        var cmds = CiscoSaipConfigurator.GenerateCommands(data, "GigabitEthernet 4", "GigabitEthernet 5");

        // Verifica comandos gerados no padrão oficial Claro / Embratel
        Assert.Contains("hostname FNS-IP-04045", cmds);
        Assert.Contains("enable secret PRO1AN", cmds);
        Assert.Contains("username EBT privilege 1 secret CQMR", cmds);
        Assert.Contains("tacacs server TACACS-SERVER-CLARO", cmds);
        Assert.Contains("policy-map SHAPE_OUT", cmds);
        Assert.Contains("  shape average 50000000", cmds);

        // Interfaces (Cisco 921 desliga GE0 a GE3 e Vlan1)
        Assert.Contains("interface GigabitEthernet0", cmds);
        Assert.Contains("interface GigabitEthernet3", cmds);
        Assert.Contains("interface Vlan1", cmds);

        // WAN (GE4) e LAN (GE5)
        Assert.Contains("interface GigabitEthernet 4", cmds);
        Assert.Contains(" bandwidth 50000", cmds);
        Assert.Contains(" ip address 201.30.10.18 255.255.255.252", cmds);
        Assert.Contains(" service-policy output SHAPE_OUT", cmds);

        Assert.Contains("interface GigabitEthernet 5", cmds);
        Assert.Contains(" description * LAN *", cmds);
        Assert.Contains(" ip address 189.16.20.81 255.255.255.248", cmds);

        // Roteamento, SSH, NTP, SNMP
        Assert.Contains("ip route 0.0.0.0 0.0.0.0 201.30.10.17", cmds);
        Assert.Contains("ip ssh version 2", cmds);
        Assert.Contains("crypto key generate rsa modulus 2048", cmds);
        Assert.Contains("ntp server 200.20.186.75 prefer source GigabitEthernet4", cmds);
        Assert.Contains("snmp-server community claro21sup RO 87", cmds);

        // AAA e Accounting TACACS+ (sintaxe IOS clássica sem 'action-type')
        Assert.Contains("aaa accounting exec default start-stop group tacacs+", cmds);
        Assert.Contains("aaa accounting commands 1 default start-stop group tacacs+", cmds);
        Assert.Contains("aaa accounting commands 6 default start-stop group tacacs+", cmds);
        Assert.Contains("aaa accounting commands 15 default start-stop group tacacs+", cmds);
        Assert.DoesNotContain(cmds, c => c.Contains("action-type", StringComparison.OrdinalIgnoreCase));

        // Console e VTY (line con 0 com autenticação de admin local sob aaa new-model)
        Assert.Contains("line con 0", cmds);
        Assert.Contains(" login authentication admin", cmds);
        Assert.DoesNotContain(" login local", cmds);

        // ACL e VTY
        Assert.Contains("ip access-list extended BLOQUEIO_TELNET", cmds);
        Assert.Contains(" permit ip host 201.30.10.17 any", cmds);
        Assert.Contains(" remark IP REDE LAN / BANCADA HOMOLOGACAO", cmds);
        Assert.Contains("tacacs-server timeout 2", cmds);
        Assert.Contains("line vty 0 4", cmds);
        Assert.Contains(" transport input telnet ssh", cmds);
        Assert.Contains("write memory", cmds);
    }

    [Fact]
    public void ParseText_ExtraiBandaNominalEmMbps()
    {
        var data = SaipParser.ParseText(ExemploFichaSaip);

        // Ficha traz "Banda 50000" (kbps) => 50 Mbps nominais
        Assert.Equal(50.0, data.BandaMbpsNominal);
        Assert.Equal(50000, data.BandaKbps);
        Assert.Equal(50000000, data.BandaBps);
    }

    [Fact]
    public void ParseText_ExtraiProdutoEIpv6_BusinessLinkDirect()
    {
        var data = SaipParser.ParseText(ExemploFichaSaip);

        Assert.Equal("Business Link Direct", data.Produto);
        Assert.Equal(SaipServiceType.BusinessLinkDirect, data.TipoServico);
        Assert.False(data.IsLanArbitrada);

        // IPv6
        Assert.Equal("2804:00A8:0002:00DA:0000:0000:0000:20FA", data.WanIpv6);
        Assert.Equal(126, data.WanIpv6Prefix);
        Assert.Equal("2804:a8:2:da::20f9", data.WanIpv6Gateway);
        Assert.Equal("2804:00A8:DACE:0000:0000:0000:0000:0000", data.LanIpv6Block);
        Assert.Equal(56, data.LanIpv6Prefix);
        Assert.Equal("2804:a8:dace::1", data.LanIpv6);
    }

    [Fact]
    public void Validar_E_ParseText_IpVpn_SemBlocoLan_ArbitraLanGenerica()
    {
        var fichaIpVpn = @"
DADOS CLIENTE
Produto	IP VPN
Razão Social	EMPRESA CLIENTE MPLS LTDA
Designação IP	SPO/IP/12345
Número Ots	IM-SPO-IGC--IP-99999/2026
Roteador	AGG01.SOONS
IP Serial Usuário (IPv4)	201.030.010.018/30
Banda	20000
";
        // 1. Validar deve aprovar IP VPN mesmo sem Bloco LAN
        var (ok, motivo) = SaipParser.Validar(fichaIpVpn);
        Assert.True(ok, $"Validação de IP VPN deveria passar: {motivo}");
        Assert.Empty(motivo);

        // 2. ParseText deve detectar IP VPN e arbitrar LAN genérica 192.168.1.1/24
        var data = SaipParser.ParseText(fichaIpVpn);
        Assert.Equal("IP VPN", data.Produto);
        Assert.Equal(SaipServiceType.IpVpn, data.TipoServico);
        Assert.True(data.IsLanArbitrada);
        Assert.Equal("192.168.1.1", data.LanIp);
        Assert.Equal("255.255.255.0", data.LanSubnetMask);
        Assert.Equal(24, data.LanCidr);
        Assert.Equal("192.168.1.2", data.HostLanIp);
        Assert.Equal("201.30.10.18", data.WanIp);
        Assert.Equal("201.30.10.17", data.WanGateway);

        // 3. Script gerado deve configurar a LAN genérica para viabilizar conexão
        var cmds = CiscoSaipConfigurator.GenerateCommands(data, "GigabitEthernet 0/0", "GigabitEthernet 0/1");
        Assert.Contains("interface GigabitEthernet 0/1", cmds);
        Assert.Contains(" ip address 192.168.1.1 255.255.255.0", cmds);
    }

    [Fact]
    public void CleanRazaoSocial_TruncatesTrailingContaCorrente()
    {
        var rawFromPdf = "SOLDI PROMOTORA DE VENDAS LTDA CONTA CORRENTE00015187188/0001 (GC/CS) CNPJ (CLE)07.249.846/0001-09 CNPJ REGISTRO07.249.846/0001-09 (RegistroBr) ADMINISTRADOR TELEFONE EMAIL DESIGNAÇÃO IPFNS/IP/03977 ESTAÇÃO DE ACESSOSOO NS DESIGNAÇÃO DE ACESSOFNS 00001101833";
        var cleaned = SaipParser.CleanRazaoSocial(rawFromPdf);

        Assert.Equal("SOLDI PROMOTORA DE VENDAS LTDA", cleaned);
    }

    [Fact]
    public void GenerateCommands_SemNatPorPadrao()
    {
        var data = SaipParser.ParseText(ExemploFichaSaip);
        var cmds = CiscoSaipConfigurator.GenerateCommands(data, "GigabitEthernet 5", "GigabitEthernet 4");

        Assert.DoesNotContain("ip nat inside", cmds);
        Assert.DoesNotContain("ip nat outside", cmds);
        Assert.DoesNotContain("overload", cmds);
    }

    [Fact]
    public void GenerateCommands_ComNatLab_IncluiOverload()
    {
        var data = new SaipCircuitData
        {
            ClienteRazaoSocial = "LAB TESTE",
            DesignacaoIp = "LAB4G-SP01",
            NumeroOts = "LAB000001",
            WanIp = "192.168.10.2",
            WanCidr = 30,
            WanSubnetMask = "255.255.255.252",
            WanGateway = "192.168.10.1",
            LanBlockNetwork = "10.10.10.0",
            LanCidr = 29,
            LanIp = "10.10.10.1",
            LanSubnetMask = "255.255.255.248",
            HostLanIp = "10.10.10.2",
        };
        var cmds = CiscoSaipConfigurator.GenerateCommands(data, "GigabitEthernet0/4", "GigabitEthernet0/5", incluirNatLab: true);

        Assert.Contains(" ip nat outside", cmds);
        Assert.Contains(" ip nat inside", cmds);
        Assert.Contains("access-list 1 permit 10.10.10.0 0.0.0.7", cmds);
        Assert.Contains("ip nat inside source list 1 interface GigabitEthernet0/4 overload", cmds);
    }

    [Fact]
    public void WildcardFromMask_InverteMascara()
    {
        Assert.Equal("0.0.0.7", CiscoSaipConfigurator.WildcardFromMask("255.255.255.248", 29));
        Assert.Equal("0.0.0.3", CiscoSaipConfigurator.WildcardFromMask("255.255.255.252", 30));
    }

    [Fact]
    public void SanitizeDescription_NormalizaAcentosECaracteresEspeciais()
    {
        // 1. Remove diacríticos mantendo caracteres ASCII legíveis
        var res1 = CiscoSaipConfigurator.SanitizeDescription("SÃO PAULO DISTRIBUIÇÃO & COMÉRCIO LTDA.");
        Assert.DoesNotContain("Ã", res1);
        Assert.DoesNotContain("Ç", res1);
        Assert.DoesNotContain("&", res1);
        Assert.StartsWith("SAO PAULO", res1);

        // 2. Converte barras em hífens
        var res2 = CiscoSaipConfigurator.SanitizeDescription("FNS/IP/04045");
        Assert.Equal("FNS-IP-04045", res2);

        // 3. Remove quebras de linha e caracteres de controle
        var res3 = CiscoSaipConfigurator.SanitizeDescription("EMPRESA TESTE\r\nFILIAL 2?!");
        Assert.DoesNotContain("\r", res3);
        Assert.DoesNotContain("\n", res3);
        Assert.DoesNotContain("?", res3);
        Assert.DoesNotContain("!", res3);
        Assert.Equal("EMPRESA TESTE FILIAL 2", res3);

        // 4. Fallback padrão
        var res4 = CiscoSaipConfigurator.SanitizeDescription(null, "LINK");
        Assert.Equal("LINK", res4);
    }

    [Fact]
    public void GenerateCommands_GeraDescriptionWanELanValidos()
    {
        var circuit = new SaipCircuitData
        {
            ClienteRazaoSocial = "SUPERMERCADO & PADARIA SÃO JOSÉ LTDA.",
            DesignacaoIp = "SPO/IP/99123",
            WanIp = "200.250.153.42",
            WanSubnetMask = "255.255.255.252",
            WanGateway = "200.250.153.41",
            LanIp = "192.168.0.1",
            LanSubnetMask = "255.255.255.0",
        };

        var cmds = CiscoSaipConfigurator.GenerateCommands(circuit, "GigabitEthernet 0/0", "GigabitEthernet 0/1");

        Assert.Contains(" description SPO-IP-99123", cmds);
        Assert.Contains(" description * LAN *", cmds);
    }
}
