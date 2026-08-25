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
        var cmds = CiscoSaipConfigurator.GenerateCommands(data, "GigabitEthernet 5", "GigabitEthernet 4");

        // Verifica comandos gerados
        Assert.Contains("interface GigabitEthernet 5", cmds);
        Assert.Contains("ip address 201.30.10.18 255.255.255.252", cmds);
        Assert.Contains("interface GigabitEthernet 4", cmds);
        Assert.Contains("ip address 189.16.20.81 255.255.255.248", cmds);
        Assert.Contains("ip route 0.0.0.0 0.0.0.0 201.30.10.17", cmds);
        Assert.Contains("username EBT privilege 15 secret PRO1AN", cmds);
        Assert.Contains("transport input telnet", cmds);
        Assert.Contains("no username admin", cmds);
        Assert.Contains("write memory", cmds);
    }

    [Fact]
    public void CleanRazaoSocial_TruncatesTrailingContaCorrente()
    {
        var rawFromPdf = "SOLDI PROMOTORA DE VENDAS LTDA CONTA CORRENTE00015187188/0001 (GC/CS) CNPJ (CLE)07.249.846/0001-09 CNPJ REGISTRO07.249.846/0001-09 (RegistroBr) ADMINISTRADOR TELEFONE EMAIL DESIGNAÇÃO IPFNS/IP/03977 ESTAÇÃO DE ACESSOSOO NS DESIGNAÇÃO DE ACESSOFNS 00001101833";
        var cleaned = SaipParser.CleanRazaoSocial(rawFromPdf);

        Assert.Equal("SOLDI PROMOTORA DE VENDAS LTDA", cleaned);
    }
}
