using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetworkDevice.Core.Provisioning;

namespace NetworkDevice.UI;

/// <summary>
/// Janela com a matriz técnica de CPEs empresariais homologados pela Claro,
/// indicando capacidade de banda (BLD / MPLS / TDM), part numbers, kits e compatibilidade com o circuito.
/// </summary>
public partial class TabelaCpeHomologadosWindow : Window
{
    private readonly SaipCircuitData? _circuit;
    private readonly string? _initialModelTag;
    private List<CpeModelDisplayItem> _allDisplayItems = new();
    private double _simulatedBandwidthMbps = 100.0;
    private bool _isMplsSimulated;
    private bool _isInitialized;

    public string? SelectedModelTag { get; private set; }
    public ClaroCpeModelInfo? SelectedModelInfo { get; private set; }

    public TabelaCpeHomologadosWindow(SaipCircuitData? circuit = null, string? currentModelTag = null)
    {
        InitializeComponent();
        _circuit = circuit;
        _initialModelTag = currentModelTag;

        InicializarDadosCircuito();
        CarregarCatalogo();
        _isInitialized = true;
        AplicarFiltros();
        TentarSelecionarModeloInicial();
    }

    private void InicializarDadosCircuito()
    {
        if (_circuit != null)
        {
            var desig = _circuit.DesignacaoIp ?? _circuit.NumeroOts ?? "Circuito Carregado";
            var cliente = _circuit.ClienteRazaoSocial ?? "Cliente SAIP";
            var banda = _circuit.BandaMbpsNominal ?? 0;
            var isMpls = _circuit.TipoServico == SaipServiceType.IpVpn;

            TxtCircuitoIdentificacao.Text = $"{desig} — {cliente}";
            TxtCircuitoDetalhes.Text = $"Banda Contratada no SAIP: {(banda > 0 ? $"{banda:F0} Mbps" : "Não informada")} | Categoria: {_circuit.TipoServicoDescricao}";

            if (banda > 0)
            {
                _simulatedBandwidthMbps = banda;
            }

            _isMplsSimulated = isMpls;
            if (CmbTipoServicoSimulado != null)
            {
                CmbTipoServicoSimulado.SelectedIndex = isMpls ? 1 : 0;
            }
        }
        else
        {
            TxtCircuitoIdentificacao.Text = "Nenhuma Ficha SAIP carregada";
            TxtCircuitoDetalhes.Text = "Simulando banda avulsa de 100 Mbps (BLD Internet). Digite um valor para testar outros limites.";
        }

        if (TxtSimularBanda != null)
        {
            TxtSimularBanda.Text = _simulatedBandwidthMbps.ToString("F0");
        }
    }

    private void CarregarCatalogo()
    {
        var rawCatalog = ClaroCpeCatalogService.GetCatalog();
        _allDisplayItems = rawCatalog.Select(m => new CpeModelDisplayItem(m)).ToList();
        AtualizarCompatibilidadeSimulada();
    }

    private void TentarSelecionarModeloInicial()
    {
        if (!string.IsNullOrEmpty(_initialModelTag) && DgModelos != null)
        {
            var match = _allDisplayItems.FirstOrDefault(i =>
                i.Model.TagsSpacCompatíveis.Any(t => t.Equals(_initialModelTag, StringComparison.OrdinalIgnoreCase)));
            if (match != null)
            {
                DgModelos.SelectedItem = match;
                DgModelos.ScrollIntoView(match);
            }
        }
    }

    private void AtualizarCompatibilidadeSimulada()
    {
        if (_allDisplayItems == null) return;
        foreach (var item in _allDisplayItems)
        {
            item.AtualizarCompatibilidade(_simulatedBandwidthMbps, _isMplsSimulated);
        }
    }

    private void AplicarFiltros()
    {
        if (!_isInitialized || DgModelos == null || _allDisplayItems == null)
            return;

        var query = TxtBusca?.Text?.Trim() ?? string.Empty;
        var filtro = _allDisplayItems.AsEnumerable();

        // Filtro por rádio chip
        if (RbFiltroCisco?.IsChecked == true)
            filtro = filtro.Where(i => i.Fabricante.Equals("Cisco", StringComparison.OrdinalIgnoreCase));
        else if (RbFiltroHpe?.IsChecked == true)
            filtro = filtro.Where(i => i.Fabricante.IndexOf("HPE", StringComparison.OrdinalIgnoreCase) >= 0 || i.Fabricante.IndexOf("HP", StringComparison.OrdinalIgnoreCase) >= 0);
        else if (RbFiltroFortinet?.IsChecked == true)
            filtro = filtro.Where(i => i.Fabricante.Equals("Fortinet", StringComparison.OrdinalIgnoreCase));
        else if (RbFiltroNovo?.IsChecked == true)
            filtro = filtro.Where(i => i.Portfolio.IndexOf("Novo", StringComparison.OrdinalIgnoreCase) >= 0);
        else if (RbFiltroAntigo?.IsChecked == true)
            filtro = filtro.Where(i => i.Portfolio.IndexOf("Antigo", StringComparison.OrdinalIgnoreCase) >= 0);
        else if (RbFiltroCompatíveis?.IsChecked == true)
            filtro = filtro.Where(i => i.IsCompativelComSimulacao);

        // Busca por texto
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtro = filtro.Where(i =>
                i.ModeloNome.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.PartNumber.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.Sap.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.Descricao.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.Kit.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.Fabricante.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        DgModelos.ItemsSource = filtro.ToList();
    }

    private void TxtSimularBanda_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized || TxtSimularBanda == null)
            return;

        if (double.TryParse(TxtSimularBanda.Text?.Trim(), out var val) && val >= 0)
        {
            _simulatedBandwidthMbps = val;
            AtualizarCompatibilidadeSimulada();
            AplicarFiltros();
        }
    }

    private void CmbTipoServicoSimulado_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || CmbTipoServicoSimulado == null)
            return;

        _isMplsSimulated = CmbTipoServicoSimulado.SelectedIndex == 1;
        AtualizarCompatibilidadeSimulada();
        AplicarFiltros();
    }

    private void BtnUsarBandaSaip_Click(object sender, RoutedEventArgs e)
    {
        if (_circuit != null && _circuit.BandaMbpsNominal.HasValue && _circuit.BandaMbpsNominal.Value > 0)
        {
            TxtSimularBanda.Text = _circuit.BandaMbpsNominal.Value.ToString("F0");
            _isMplsSimulated = _circuit.TipoServico == SaipServiceType.IpVpn;
            CmbTipoServicoSimulado.SelectedIndex = _isMplsSimulated ? 1 : 0;
        }
        else
        {
            MessageBox.Show(this,
                "Não há Ficha SAIP carregada com velocidade nominal definida.",
                "Simulador de Banda",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void RbFiltro_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
            return;

        if (_allDisplayItems.Count > 0)
        {
            AplicarFiltros();
        }
    }

    private void TxtBusca_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized)
            return;

        AplicarFiltros();
    }

    private void BtnLimparBusca_Click(object sender, RoutedEventArgs e)
    {
        TxtBusca.Text = string.Empty;
    }

    private void BtnAbrirPlanilhaExcel_Click(object sender, RoutedEventArgs e)
    {
        var paths = new[]
        {
            @"C:\Users\User\Desktop\SPARC\CPE's Empresariais.xlsx",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SPARC", "CPE's Empresariais.xlsx"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CPE's Empresariais.xlsx"),
            @"C:\SPARC\CPE's Empresariais.xlsx"
        };

        var found = paths.FirstOrDefault(File.Exists);
        if (found != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(found) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Não foi possível abrir o arquivo Excel: {ex.Message}",
                    "Abrir Planilha",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        else
        {
            MessageBox.Show(this,
                "O arquivo 'CPE''s Empresariais.xlsx' não foi localizado no caminho padrão:\nC:\\Users\\User\\Desktop\\SPARC\\CPE's Empresariais.xlsx",
                "Arquivo Não Encontrado",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BtnSelecionarModeloNoSparc_Click(object sender, RoutedEventArgs e)
    {
        AplicarSelecaoModelo();
    }

    private void DgModelos_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        AplicarSelecaoModelo();
    }

    private void AplicarSelecaoModelo()
    {
        if (DgModelos.SelectedItem is CpeModelDisplayItem item)
        {
            SelectedModelInfo = item.Model;
            SelectedModelTag = item.Model.TagsSpacCompatíveis.FirstOrDefault();
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show(this,
                "Selecione uma linha na tabela para aplicar o modelo no SPARC.",
                "Seleção de Modelo",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void BtnFechar_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

/// <summary>
/// Item de exibição para a DataGrid com formatação de cores e capacidades visuais.
/// </summary>
public sealed class CpeModelDisplayItem
{
    public ClaroCpeModelInfo Model { get; }

    public string Fabricante => Model.Fabricante;
    public string ModeloNome => Model.ModeloNome;
    public string Portfolio => Model.Portfolio;
    public string Kit => Model.Kit;
    public string PartNumber => Model.PartNumber;
    public string PartNumberPrincipal => Model.PartNumber.Split('\n').FirstOrDefault() ?? Model.PartNumber;
    public string Sap => Model.Sap;
    public string SapFormatado => "SAP: " + (Model.Sap.Split('\n').FirstOrDefault() ?? Model.Sap);
    public string TipoAcesso => Model.TipoAcesso;
    public int BldMaxMbps => Model.BldMaxMbps;
    public int MplsMaxMbps => Model.MplsMaxMbps;
    public string Descricao => Model.Descricao;

    public string BldFormatado => Model.TipoAcesso.Equals("TDM", StringComparison.OrdinalIgnoreCase)
        ? $"{Model.BldMaxMbps} Mbps (TDM)"
        : Model.BldMaxMbps >= 1000 ? $"{Model.BldMaxMbps / 1000.0:F0} Gbps" : $"{Model.BldMaxMbps} Mbps";

    public string MplsFormatado => Model.TipoAcesso.Equals("TDM", StringComparison.OrdinalIgnoreCase)
        ? $"{Model.MplsMaxMbps} Mbps (TDM)"
        : Model.MplsMaxMbps >= 1000 ? $"{Model.MplsMaxMbps / 1000.0:F0} Gbps" : $"{Model.MplsMaxMbps} Mbps";

    public Brush TipoAcessoColor => Model.TipoAcesso.Equals("TDM", StringComparison.OrdinalIgnoreCase)
        ? new SolidColorBrush(Color.FromRgb(251, 146, 60)) // Laranja
        : new SolidColorBrush(Color.FromRgb(56, 189, 248)); // Azul ciano

    public bool IsCompativelComSimulacao { get; private set; } = true;

    public CpeModelDisplayItem(ClaroCpeModelInfo model)
    {
        Model = model;
    }

    public void AtualizarCompatibilidade(double bandaSimuladaMbps, bool isMpls)
    {
        IsCompativelComSimulacao = Model.SuportaBanda(bandaSimuladaMbps, isMpls);
    }
}
