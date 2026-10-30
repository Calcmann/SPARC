using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NetworkDevice.Core.Provisioning;

/// <summary>
/// Informações de homologação de roteador/CPE empresarial homologado pela Claro
/// conforme planilha oficial 'CPE''s Empresariais.xlsx'.
/// </summary>
public sealed record ClaroCpeModelInfo(
    string Id,
    string Fabricante,
    string Portfolio,
    string Kit,
    string PartNumber,
    string ModeloNome,
    string Sap,
    string TipoAcesso,
    int MplsMaxMbps,
    int BldMaxMbps,
    string BldSegurancaMax,
    string Descricao,
    IReadOnlyList<string> TagsSpacCompatíveis
)
{
    public string CapacidadeFormatada => TipoAcesso.Equals("TDM", StringComparison.OrdinalIgnoreCase)
        ? $"TDM: {MplsMaxMbps} Mbps"
        : $"BLD: {BldMaxMbps} Mbps | MPLS: {MplsMaxMbps} Mbps";

    public bool SuportaBanda(double bandaMbps, bool isMpls = false)
    {
        var limite = isMpls ? MplsMaxMbps : BldMaxMbps;
        return bandaMbps <= limite;
    }
}

public sealed record CpeBandwidthValidationResult(
    bool IsCompativel,
    string ModeloNome,
    double BandaContratadaMbps,
    int LimiteMaximoMbps,
    string TipoServico,
    string Mensagem,
    IReadOnlyList<ClaroCpeModelInfo> ModelosRecomendados
);

/// <summary>
/// Catálogo de roteadores e CPEs empresariais homologados pela Claro,
/// com suporte à checagem de limites de banda e recomendação de modelos.
/// </summary>
public static class ClaroCpeCatalogService
{
    private static readonly List<ClaroCpeModelInfo> _catalog = new()
    {
        // ================= PORTFÓLIO ANTIGO (CISCO) =================
        new ClaroCpeModelInfo(
            Id: "cisco.c841.kit0",
            Fabricante: "Cisco",
            Portfolio: "Portfólio Antigo",
            Kit: "KIT 0",
            PartNumber: "C841M-4X/K9-BR-001 / CAB-SS-V35MT=",
            ModeloNome: "Cisco C841M-4X (1x V.35 Serial TDM)",
            Sap: "30060996 / 30055333",
            TipoAcesso: "TDM",
            MplsMaxMbps: 2,
            BldMaxMbps: 2,
            BldSegurancaMax: "N.A",
            Descricao: "Bundle composto de: 1x C841M-4X/K9, 1x WIM-1T, 1x WIM-BLANK + Cabo V.35 10ft",
            TagsSpacCompatíveis: new[] { "cisco.c841.tdm" }
        ),
        new ClaroCpeModelInfo(
            Id: "cisco.c841.kit1",
            Fabricante: "Cisco",
            Portfolio: "Portfólio Antigo",
            Kit: "KIT 1",
            PartNumber: "C841M-4X/K9-BR-003 / 2x CAB-SS-V35MT=",
            ModeloNome: "Cisco C841M-4X (2x V.35 Serial TDM)",
            Sap: "30060997 / 30055333",
            TipoAcesso: "TDM",
            MplsMaxMbps: 4,
            BldMaxMbps: 4,
            BldSegurancaMax: "N.A",
            Descricao: "Bundle composto de: 1x C841M-4X/K9, 2x WIM-1T + 2x Cabos V.35",
            TagsSpacCompatíveis: new[] { "cisco.c841.tdm2" }
        ),
        new ClaroCpeModelInfo(
            Id: "cisco.c841.kit2",
            Fabricante: "Cisco",
            Portfolio: "Portfólio Antigo",
            Kit: "KIT 2",
            PartNumber: "C841M-4X/K9-BR-004",
            ModeloNome: "Cisco C841M-4X Ethernet",
            Sap: "30060998",
            TipoAcesso: "Ethernet",
            MplsMaxMbps: 150,
            BldMaxMbps: 250,
            BldSegurancaMax: "N.A",
            Descricao: "Bundle composto de: 1x C841M-4X/K9, 2x WIM-BLANK (Ethernet)",
            TagsSpacCompatíveis: new[] { "cisco.c841.break", "cisco.c841" }
        ),
        new ClaroCpeModelInfo(
            Id: "cisco.c1111.kit3",
            Fabricante: "Cisco",
            Portfolio: "Portfólio Antigo",
            Kit: "KIT 3",
            PartNumber: "C1111-4P-BR",
            ModeloNome: "Cisco ISR 1111-4P",
            Sap: "30060203",
            TipoAcesso: "Ethernet",
            MplsMaxMbps: 650,
            BldMaxMbps: 850,
            BldSegurancaMax: "N.A",
            Descricao: "ISR 1100 4 Ports Dual GE Ethernet Router",
            TagsSpacCompatíveis: new[] { "cisco.c1111.break", "cisco.c1100" }
        ),
        new ClaroCpeModelInfo(
            Id: "cisco.isr4321.kit4",
            Fabricante: "Cisco",
            Portfolio: "Portfólio Antigo",
            Kit: "KIT 4",
            PartNumber: "ISR4321BR/K9 + FL-4320-BOOST-K9",
            ModeloNome: "Cisco ISR 4321 (Booster)",
            Sap: "30055326 / 9012292",
            TipoAcesso: "Ethernet",
            MplsMaxMbps: 1000,
            BldMaxMbps: 1000,
            BldSegurancaMax: "N.A",
            Descricao: "CISCO ISR 4321 (2GE, 2NIM, 4G FLASH, 4G DRAM, IP Base) + Licença Booster Performance",
            TagsSpacCompatíveis: new[] { "cisco.c4321.break", "cisco.c4000" }
        ),
        new ClaroCpeModelInfo(
            Id: "cisco.asr1001.kit5",
            Fabricante: "Cisco",
            Portfolio: "Portfólio Antigo",
            Kit: "KIT 5",
            PartNumber: "ASR1001-X-BR-010",
            ModeloNome: "Cisco ASR 1001-X",
            Sap: "30055328",
            TipoAcesso: "Ethernet",
            MplsMaxMbps: 10000,
            BldMaxMbps: 10000,
            BldSegurancaMax: "N.A",
            Descricao: "ASR Bundle composto de: 1x ASR1001-X, 1x FLSA1-1X-2.5-20G, 1x SLASR1-IPB (10 Gbps)",
            TagsSpacCompatíveis: new[] { "cisco.asr1001" }
        ),

        // ================= PORTFÓLIO NOVO (CISCO) =================
        new ClaroCpeModelInfo(
            Id: "cisco.c1101.kit0",
            Fabricante: "Cisco",
            Portfolio: "Portfólio Novo",
            Kit: "KIT 0",
            PartNumber: "C1101-4PLTEP / CAB-SS-V35MT= / P-1T=",
            ModeloNome: "Cisco ISR 1101-4PLTEP (Serial TDM)",
            Sap: "40001374 / 30055333 / 40001376",
            TipoAcesso: "TDM",
            MplsMaxMbps: 2,
            BldMaxMbps: 2,
            BldSegurancaMax: "N.A",
            Descricao: "ISR 1101 4P GE Ethernet + Módulo High Speed Serial Pluggable + Cabo V.35",
            TagsSpacCompatíveis: new[] { "cisco.c1101" }
        ),
        new ClaroCpeModelInfo(
            Id: "cisco.c921.kit1",
            Fabricante: "Cisco",
            Portfolio: "Portfólio Novo",
            Kit: "KIT 1",
            PartNumber: "C921-4P",
            ModeloNome: "Cisco C921-4P (Série 900)",
            Sap: "40000864",
            TipoAcesso: "Ethernet",
            MplsMaxMbps: 500,
            BldMaxMbps: 700,
            BldSegurancaMax: "N.A",
            Descricao: "Cisco 900 Series Integrated Services Router (4x GE LAN, 2x GE WAN)",
            TagsSpacCompatíveis: new[] { "cisco.c900.ctrl-c" }
        ),
        new ClaroCpeModelInfo(
            Id: "cisco.c8200l.kit2",
            Fabricante: "Cisco",
            Portfolio: "Portfólio Novo",
            Kit: "KIT 2",
            PartNumber: "C8200L-1N-4T / DNA-P-T0-E-3Y",
            ModeloNome: "Cisco Catalyst 8200L",
            Sap: "40001350 / 9011015",
            TipoAcesso: "Ethernet",
            MplsMaxMbps: 1000,
            BldMaxMbps: 1000,
            BldSegurancaMax: "N.A",
            Descricao: "Cisco Catalyst 8200L with 1-NIM slot and 4x 1G WAN ports + DNA Essentials",
            TagsSpacCompatíveis: new[] { "cisco.c8200l", "cisco.catalyst8200" }
        ),
        new ClaroCpeModelInfo(
            Id: "cisco.c8300.kit3",
            Fabricante: "Cisco",
            Portfolio: "Portfólio Novo",
            Kit: "KIT 3",
            PartNumber: "C8300-1N1S-4T2X / DNA-P-T0-E-3Y",
            ModeloNome: "Cisco Catalyst 8300-1N1S-4T2X",
            Sap: "40001382 / 9011015",
            TipoAcesso: "Ethernet",
            MplsMaxMbps: 5000,
            BldMaxMbps: 5000,
            BldSegurancaMax: "N.A",
            Descricao: "Cisco Catalyst C8300-1N1S-4T2X Router (4x 1GE, 2x 10GE)",
            TagsSpacCompatíveis: new[] { "cisco.c8300", "cisco.catalyst8300" }
        ),
        new ClaroCpeModelInfo(
            Id: "cisco.c8500l.kit4",
            Fabricante: "Cisco",
            Portfolio: "Portfólio Novo",
            Kit: "KIT 4",
            PartNumber: "C8500L-8S4X / DNA-P-T2-A-3Y",
            ModeloNome: "Cisco Catalyst 8500L-8S4X",
            Sap: "40001307 / 9011015",
            TipoAcesso: "Ethernet",
            MplsMaxMbps: 10000,
            BldMaxMbps: 10000,
            BldSegurancaMax: "N.A",
            Descricao: "Cisco Catalyst 8500 Series 4x 10GE SFP+ and 8x 1GE SFP + DNA Advantage",
            TagsSpacCompatíveis: new[] { "cisco.c8500l", "cisco.catalyst8500" }
        ),

        // ================= PORTFÓLIO NOVO (HPE / HP) =================
        new ClaroCpeModelInfo(
            Id: "hpe.msr1002.kit0",
            Fabricante: "HPE",
            Portfolio: "Portfólio Novo",
            Kit: "KIT 0",
            PartNumber: "JG875A / JD523A",
            ModeloNome: "HPE MSR1002-AC (1x V.35 TDM)",
            Sap: "30054234 / 22050389",
            TipoAcesso: "TDM",
            MplsMaxMbps: 2,
            BldMaxMbps: 2,
            BldSegurancaMax: "N.A",
            Descricao: "HPE MSR1002 4 AC Router + HP X200 V.35 DTE 3m Serial Port Cable",
            TagsSpacCompatíveis: new[] { "hpe.msr1002.ctrl-b" }
        ),
        new ClaroCpeModelInfo(
            Id: "hpe.msr1002.kit1",
            Fabricante: "HPE",
            Portfolio: "Portfólio Novo",
            Kit: "KIT 1",
            PartNumber: "JG875A / JG737A / 2x JD523A",
            ModeloNome: "HPE MSR1002-AC (2x V.35 TDM)",
            Sap: "30054234 / 30054243 / 22050389",
            TipoAcesso: "TDM",
            MplsMaxMbps: 4,
            BldMaxMbps: 4,
            BldSegurancaMax: "N.A",
            Descricao: "HPE MSR1002 4 AC Router + Módulo 4p Serial SIC + 2x Cabos V.35",
            TagsSpacCompatíveis: new[] { "hpe.msr1002.ctrl-b" }
        ),
        new ClaroCpeModelInfo(
            Id: "hpe.msr954.kit2",
            Fabricante: "HPE",
            Portfolio: "Portfólio Novo",
            Kit: "KIT 2",
            PartNumber: "JH296A",
            ModeloNome: "HPE MSR954 1GbE SFP",
            Sap: "30054372",
            TipoAcesso: "Ethernet",
            MplsMaxMbps: 90,
            BldMaxMbps: 150,
            BldSegurancaMax: "N.A",
            Descricao: "HPE MSR954 1GbE SFP Router (WAN GbE SFP / Combo + 4x LAN GbE)",
            TagsSpacCompatíveis: new[] { "hpe.msr954.ctrl-b", "hpe.msr930.ctrl-b" }
        ),
        new ClaroCpeModelInfo(
            Id: "hpe.msr3012.kit3",
            Fabricante: "HPE",
            Portfolio: "Portfólio Novo",
            Kit: "KIT 3",
            PartNumber: "JG409B",
            ModeloNome: "HPE MSR3012 AC",
            Sap: "30055389",
            TipoAcesso: "Ethernet",
            MplsMaxMbps: 800,
            BldMaxMbps: 800,
            BldSegurancaMax: "N.A",
            Descricao: "HPE MSR3012 AC Router (3x GbE WAN, HMIM / SIC slots)",
            TagsSpacCompatíveis: new[] { "hpe.msr3012" }
        ),
        new ClaroCpeModelInfo(
            Id: "hpe.msr3044.kit4",
            Fabricante: "HPE",
            Portfolio: "Portfólio Novo",
            Kit: "KIT 4",
            PartNumber: "JG405A / JG527A",
            ModeloNome: "HPE MSR3044 Router",
            Sap: "30055388 / 30055391",
            TipoAcesso: "Ethernet",
            MplsMaxMbps: 1000,
            BldMaxMbps: 1000,
            BldSegurancaMax: "N.A",
            Descricao: "HPE MSR3044 Router + HP X351 300W AC Power Supply",
            TagsSpacCompatíveis: new[] { "hpe.msr3044" }
        ),

        // ================= PORTFÓLIO NOVO (FORTINET) =================
        new ClaroCpeModelInfo(
            Id: "fortinet.fg40f.kit0",
            Fabricante: "Fortinet",
            Portfolio: "Portfólio Novo",
            Kit: "KIT 0",
            PartNumber: "FG-40F",
            ModeloNome: "Fortinet FortiGate 40F",
            Sap: "16008418",
            TipoAcesso: "Ethernet",
            MplsMaxMbps: 1000,
            BldMaxMbps: 1000,
            BldSegurancaMax: "N.A",
            Descricao: "5x GE RJ45 ports (including 4x Internal Ports, 1x WAN Port) - Até 1 Gbps",
            TagsSpacCompatíveis: new[] { "fortinet.fgt40f.fortios" }
        ),
        new ClaroCpeModelInfo(
            Id: "fortinet.fg100f.kit1",
            Fabricante: "Fortinet",
            Portfolio: "Portfólio Novo",
            Kit: "KIT 1",
            PartNumber: "FG-100F / 2x FG-TRAN-SFP+SR",
            ModeloNome: "Fortinet FortiGate 100F",
            Sap: "40000579 / 30051554",
            TipoAcesso: "Ethernet",
            MplsMaxMbps: 2700,
            BldMaxMbps: 10000,
            BldSegurancaMax: "N.A",
            Descricao: "FORTIGATE-100F + 2x Transceptores 10GE SFP+ SR (Até 10 Gbps BLD / 2.7 Gbps MPLS)",
            TagsSpacCompatíveis: new[] { "fortinet.fg100f" }
        ),
        new ClaroCpeModelInfo(
            Id: "fortinet.fg200f.kit2",
            Fabricante: "Fortinet",
            Portfolio: "Portfólio Novo",
            Kit: "KIT 2",
            PartNumber: "FG-200F / 2x FG-TRAN-SFP+SR",
            ModeloNome: "Fortinet FortiGate 200F",
            Sap: "16008560 / 30051554",
            TipoAcesso: "Ethernet",
            MplsMaxMbps: 10000,
            BldMaxMbps: 10000,
            BldSegurancaMax: "N.A",
            Descricao: "FORTIGATE-200F + 2x Transceptores 10GE SFP+ SR (Até 10 Gbps simétrico)",
            TagsSpacCompatíveis: new[] { "fortinet.fg200f" }
        ),

        // ================= MODELOS LEGADOS CLARO NO SPARC =================
        new ClaroCpeModelInfo(
            Id: "cisco.c1900.legado",
            Fabricante: "Cisco",
            Portfolio: "Legado Claro",
            Kit: "Série 1900",
            PartNumber: "CISCO1905 / 1921 / 1941",
            ModeloNome: "Cisco Série 1900 / 1905 / 1921 / 1941",
            Sap: "30053656",
            TipoAcesso: "Ethernet / TDM",
            MplsMaxMbps: 50,
            BldMaxMbps: 100,
            BldSegurancaMax: "25 Mbps",
            Descricao: "Roteador legado Claro (Substituído por C841M / C921). Limite 100M Ethernet / 2-4M TDM.",
            TagsSpacCompatíveis: new[] { "cisco.c1900.break" }
        ),
        new ClaroCpeModelInfo(
            Id: "cisco.c2900.legado",
            Fabricante: "Cisco",
            Portfolio: "Legado Claro",
            Kit: "Série 2900",
            PartNumber: "CISCO2911 / 2921 / 2951",
            ModeloNome: "Cisco Série 2900 / 2911 / 2921",
            Sap: "30049383",
            TipoAcesso: "Ethernet",
            MplsMaxMbps: 150,
            BldMaxMbps: 300,
            BldSegurancaMax: "75 Mbps",
            Descricao: "Roteador modular para agregação e voz. Capacidade de comutação nominal até 300 Mbps.",
            TagsSpacCompatíveis: new[] { "cisco.c2900.break" }
        ),
        new ClaroCpeModelInfo(
            Id: "hpe.msr930.legado",
            Fabricante: "HPE",
            Portfolio: "Legado Claro",
            Kit: "Série MSR930",
            PartNumber: "JG511A / JG514A / JG515A",
            ModeloNome: "HPE MSR 930 / 931 / 935",
            Sap: "30054234",
            TipoAcesso: "Ethernet",
            MplsMaxMbps: 75,
            BldMaxMbps: 100,
            BldSegurancaMax: "N.A",
            Descricao: "Roteador compacto HPE FastEthernet/GbE (Até 100 Mbps)",
            TagsSpacCompatíveis: new[] { "hpe.msr930.ctrl-b" }
        )
    };

    public static IReadOnlyList<ClaroCpeModelInfo> GetCatalog() => _catalog;

    /// <summary>
    /// Localiza o modelo no catálogo por tag do SPARC ou nome.
    /// </summary>
    public static ClaroCpeModelInfo? FindByTagOrName(string? tagOrName)
    {
        if (string.IsNullOrWhiteSpace(tagOrName))
            return null;

        var clean = tagOrName.Trim();

        // 1. Busca por tag exata do ComboBoxItem do SPARC
        var matchTag = _catalog.FirstOrDefault(m =>
            m.TagsSpacCompatíveis.Any(t => t.Equals(clean, StringComparison.OrdinalIgnoreCase)));
        if (matchTag != null)
            return matchTag;

        // 2. Busca por Id ou PartNumber
        var matchId = _catalog.FirstOrDefault(m =>
            m.Id.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
            m.PartNumber.Contains(clean, StringComparison.OrdinalIgnoreCase));
        if (matchId != null)
            return matchId;

        // 3. Busca por similaridade no nome do modelo
        if (clean.Contains("40F", StringComparison.OrdinalIgnoreCase))
            return _catalog.FirstOrDefault(m => m.Id == "fortinet.fg40f.kit0");
        if (clean.Contains("100F", StringComparison.OrdinalIgnoreCase))
            return _catalog.FirstOrDefault(m => m.Id == "fortinet.fg100f.kit1");
        if (clean.Contains("200F", StringComparison.OrdinalIgnoreCase))
            return _catalog.FirstOrDefault(m => m.Id == "fortinet.fg200f.kit2");
        if (clean.Contains("954", StringComparison.OrdinalIgnoreCase) || clean.Contains("958", StringComparison.OrdinalIgnoreCase))
            return _catalog.FirstOrDefault(m => m.Id == "hpe.msr954.kit2");
        if (clean.Contains("930", StringComparison.OrdinalIgnoreCase) || clean.Contains("931", StringComparison.OrdinalIgnoreCase))
            return _catalog.FirstOrDefault(m => m.Id == "hpe.msr930.legado");
        if (clean.Contains("1002", StringComparison.OrdinalIgnoreCase) || clean.Contains("1003", StringComparison.OrdinalIgnoreCase))
            return _catalog.FirstOrDefault(m => m.Id == "hpe.msr1002.kit0");
        if (clean.Contains("841", StringComparison.OrdinalIgnoreCase))
            return _catalog.FirstOrDefault(m => m.Id == "cisco.c841.kit2");
        if (clean.Contains("921", StringComparison.OrdinalIgnoreCase) || clean.Contains("Série 900", StringComparison.OrdinalIgnoreCase))
            return _catalog.FirstOrDefault(m => m.Id == "cisco.c921.kit1");
        if (clean.Contains("1111", StringComparison.OrdinalIgnoreCase) || clean.Contains("1100", StringComparison.OrdinalIgnoreCase))
            return _catalog.FirstOrDefault(m => m.Id == "cisco.c1111.kit3");
        if (clean.Contains("1900", StringComparison.OrdinalIgnoreCase) || clean.Contains("1905", StringComparison.OrdinalIgnoreCase) || clean.Contains("1921", StringComparison.OrdinalIgnoreCase) || clean.Contains("1941", StringComparison.OrdinalIgnoreCase))
            return _catalog.FirstOrDefault(m => m.Id == "cisco.c1900.legado");
        if (clean.Contains("2900", StringComparison.OrdinalIgnoreCase) || clean.Contains("2911", StringComparison.OrdinalIgnoreCase))
            return _catalog.FirstOrDefault(m => m.Id == "cisco.c2900.legado");
        if (clean.Contains("4321", StringComparison.OrdinalIgnoreCase))
            return _catalog.FirstOrDefault(m => m.Id == "cisco.isr4321.kit4");
        if (clean.Contains("8200", StringComparison.OrdinalIgnoreCase))
            return _catalog.FirstOrDefault(m => m.Id == "cisco.c8200l.kit2");
        if (clean.Contains("8300", StringComparison.OrdinalIgnoreCase))
            return _catalog.FirstOrDefault(m => m.Id == "cisco.c8300.kit3");
        if (clean.Contains("8500", StringComparison.OrdinalIgnoreCase))
            return _catalog.FirstOrDefault(m => m.Id == "cisco.c8500l.kit4");

        return null;
    }

    /// <summary>
    /// Valida se a banda informada na Ficha SAIP é compatível com o modelo de CPE selecionado.
    /// Emite alertas e lista de modelos homologados recomendados caso a banda exceda o limite.
    /// </summary>
    public static CpeBandwidthValidationResult ValidarCompatibilidadeBanda(
        string? modelTagOrName,
        SaipCircuitData? circuit)
    {
        if (circuit == null || !circuit.BandaMbpsNominal.HasValue || circuit.BandaMbpsNominal.Value <= 0)
        {
            return new CpeBandwidthValidationResult(
                IsCompativel: true,
                ModeloNome: modelTagOrName ?? "Não especificado",
                BandaContratadaMbps: 0,
                LimiteMaximoMbps: 0,
                TipoServico: "Indefinido",
                Mensagem: "Sem informação de banda na Ficha SAIP para validação.",
                ModelosRecomendados: Array.Empty<ClaroCpeModelInfo>()
            );
        }

        var bandaContratada = circuit.BandaMbpsNominal.Value;
        var isMpls = circuit.TipoServico == SaipServiceType.IpVpn;
        var tipoServicoNome = isMpls ? "MPLS" : "BLD (Internet)";

        var modelInfo = FindByTagOrName(modelTagOrName);
        if (modelInfo == null)
        {
            // Modelo genérico não cadastrado: aceita com ressalva
            return new CpeBandwidthValidationResult(
                IsCompativel: true,
                ModeloNome: modelTagOrName ?? "Genérico",
                BandaContratadaMbps: bandaContratada,
                LimiteMaximoMbps: 10000,
                TipoServico: tipoServicoNome,
                Mensagem: $"Modelo '{modelTagOrName}' não mapeado no portfólio oficial Claro.",
                ModelosRecomendados: Array.Empty<ClaroCpeModelInfo>()
            );
        }

        var limite = isMpls ? modelInfo.MplsMaxMbps : modelInfo.BldMaxMbps;

        if (bandaContratada > limite)
        {
            // Incompatível! Busca modelos que suportem essa banda
            var recomendados = _catalog
                .Where(m => m.SuportaBanda(bandaContratada, isMpls) &&
                            m.TipoAcesso.Equals("Ethernet", StringComparison.OrdinalIgnoreCase) &&
                            !m.Portfolio.Contains("Legado", StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => isMpls ? m.MplsMaxMbps : m.BldMaxMbps)
                .Take(5)
                .ToList();

            var nomesRecomendados = string.Join(", ", recomendados.Select(r => $"{r.ModeloNome} ({r.CapacidadeFormatada})"));

            var msg = $"⚠️ INCOMPATIBILIDADE DE BANDA HOMOLOGADA CLARO:\n\n" +
                      $"• Banda Contratada no SAIP: {bandaContratada:F0} Mbps ({tipoServicoNome})\n" +
                      $"• Modelo Selecionado: {modelInfo.ModeloNome}\n" +
                      $"• Capacidade Máxima Homologada Claro: {limite:F0} Mbps ({modelInfo.Portfolio})\n\n" +
                      $"O modelo selecionado NÃO SUPORTA a vazão contratada pelo cliente segundo as normas de engenharia da Claro (CPE's Empresariais.xlsx).\n\n" +
                      $"Modelos homologados recomendados para esta velocidade:\n" +
                      $"{nomesRecomendados}";

            return new CpeBandwidthValidationResult(
                IsCompativel: false,
                ModeloNome: modelInfo.ModeloNome,
                BandaContratadaMbps: bandaContratada,
                LimiteMaximoMbps: limite,
                TipoServico: tipoServicoNome,
                Mensagem: msg,
                ModelosRecomendados: recomendados
            );
        }

        return new CpeBandwidthValidationResult(
            IsCompativel: true,
            ModeloNome: modelInfo.ModeloNome,
            BandaContratadaMbps: bandaContratada,
            LimiteMaximoMbps: limite,
            TipoServico: tipoServicoNome,
            Mensagem: $"✅ Modelo {modelInfo.ModeloNome} compatível com {bandaContratada:F0} Mbps (Limite: {limite:F0} Mbps).",
            ModelosRecomendados: Array.Empty<ClaroCpeModelInfo>()
        );
    }
}
