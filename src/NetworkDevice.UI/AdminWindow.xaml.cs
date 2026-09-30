using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Firmware;
using NetworkDevice.Core.Licensing;
using NetworkDevice.Core.Validation;

namespace NetworkDevice.UI;

public sealed class AdminFirmwareModelRow
{
    public DeviceSeries Series { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string LocalFileName { get; set; } = string.Empty;
    public string LocalVersion { get; set; } = string.Empty;
    public string LocalDisplaySize { get; set; } = string.Empty;
    public string StatusDescription { get; set; } = string.Empty;
    public string? FullLocalPath { get; set; }
}

public partial class AdminWindow : Window
{
    private readonly LicenseSignerService _licenseService = new();
    private readonly CloudLicenseService _cloudLicenseService = new();
    private readonly FirmwareRepositoryService _firmwareRepoService = new();
    private ActivationRequestData? _currentDecodedRequest;
    private GeneratedLicenseResult? _lastGeneratedLicense;

    public AdminWindow()
    {
        InitializeComponent();
        Loaded += AdminWindow_Loaded;
    }

    private void AdminWindow_Loaded(object sender, RoutedEventArgs e)
    {
        VerificarChaveRsa();
        CarregarSolicitacoesOnlineAsync();
        CarregarHistoricoChaves();
        CarregarCopiasCampo();
        CarregarModelosFirmware();

        if (!string.IsNullOrWhiteSpace(_firmwareRepoService.GitHubToken))
        {
            TxtGitHubToken.Text = _firmwareRepoService.GitHubToken;
        }
    }

    private void VerificarChaveRsa()
    {
        if (_licenseService.HasPrivateKey)
        {
            TxtStatusChaveRsa.Text = "🔑 Chave RSA Privada: Pronta (C:\\SPARC\\beta\\keys\\private.pem)";
            TxtStatusChaveRsa.Foreground = UiBrushes.Get("#4ADE80");
            BtnGerarChave.IsEnabled = true;
        }
        else
        {
            TxtStatusChaveRsa.Text = "⚠️ Chave RSA Privada Não Encontrada em C:\\SPARC\\beta\\keys\\private.pem";
            TxtStatusChaveRsa.Foreground = UiBrushes.Get("#F87171");
            BtnGerarChave.IsEnabled = false;
        }
    }

    #region Aba 1: Solicitações de Ativação Online (1 Clique)

    private List<OnlineActivationRequest> _cachedSolicitacoes = new();

    private async void CarregarSolicitacoesOnlineAsync()
    {
        try
        {
            BtnAtualizarSolicitacoesOnline.IsEnabled = false;
            BtnAtualizarSolicitacoesOnline.Content = "⏳ Buscando...";

            _cachedSolicitacoes = await _cloudLicenseService.ListActivationRequestsAsync();
            AtualizarMetricasSolicitacoes(_cachedSolicitacoes);
            FiltrarSolicitacoes();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao carregar solicitações online: {ex.Message}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            BtnAtualizarSolicitacoesOnline.IsEnabled = true;
            BtnAtualizarSolicitacoesOnline.Content = "🔄 Buscar da Nuvem / Atualizar";
        }
    }

    private void AtualizarMetricasSolicitacoes(List<OnlineActivationRequest> list)
    {
        var pendentes = list.Count(r => string.Equals(r.Status, "Pending", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(r.Status));
        var aprovadas = list.Count(r => string.Equals(r.Status, "Approved", StringComparison.OrdinalIgnoreCase));
        var rejeitadas = list.Count(r => string.Equals(r.Status, "Rejected", StringComparison.OrdinalIgnoreCase));

        TxtSolicitacoesPendentesCount.Text = $"⏳ Pendentes: {pendentes}";
        TxtSolicitacoesAprovadasCount.Text = $"✅ Aprovadas: {aprovadas}";
        TxtSolicitacoesRejeitadasCount.Text = $"❌ Rejeitadas: {rejeitadas}";
    }

    private void FiltrarSolicitacoes()
    {
        var filtro = TxtFiltroSolicitacoes?.Text?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrWhiteSpace(filtro))
        {
            DgSolicitacoesOnline.ItemsSource = _cachedSolicitacoes;
        }
        else
        {
            DgSolicitacoesOnline.ItemsSource = _cachedSolicitacoes.Where(r =>
                r.FullName.ToLowerInvariant().Contains(filtro) ||
                r.Cluster.ToLowerInvariant().Contains(filtro) ||
                r.Uf.ToLowerInvariant().Contains(filtro) ||
                r.Phone.Contains(filtro) ||
                r.Email.ToLowerInvariant().Contains(filtro) ||
                r.MachineGuid.ToLowerInvariant().Contains(filtro) ||
                r.Status.ToLowerInvariant().Contains(filtro)).ToList();
        }
    }

    private void TxtFiltroSolicitacoes_TextChanged(object sender, TextChangedEventArgs e)
    {
        FiltrarSolicitacoes();
    }

    private void BtnAtualizarSolicitacoesOnline_Click(object sender, RoutedEventArgs e)
    {
        CarregarSolicitacoesOnlineAsync();
    }

    private void DgSolicitacoesOnline_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var req = DgSolicitacoesOnline.SelectedItem as OnlineActivationRequest;
        if (req != null)
        {
            var reg = !string.IsNullOrWhiteSpace(req.Cluster) ? $" [{req.Cluster}/{req.Uf}]" : "";
            TxtSolicitacaoSelecionadaInfo.Text = $"Técnico: {req.FullName}{reg} ({req.Phone}) - Status: {req.StatusBadge}";
            BtnAprovarSolicitacao30.IsEnabled = _licenseService.HasPrivateKey;
            BtnAprovarSolicitacao60.IsEnabled = _licenseService.HasPrivateKey;
            BtnAprovarSolicitacao90.IsEnabled = _licenseService.HasPrivateKey;
            BtnRejeitarSolicitacao.IsEnabled = true;
            BtnWhatsAppSolicitante.IsEnabled = !string.IsNullOrWhiteSpace(req.Phone);
        }
        else
        {
            TxtSolicitacaoSelecionadaInfo.Text = "Selecione uma solicitação na lista acima";
            BtnAprovarSolicitacao30.IsEnabled = false;
            BtnAprovarSolicitacao60.IsEnabled = false;
            BtnAprovarSolicitacao90.IsEnabled = false;
            BtnRejeitarSolicitacao.IsEnabled = false;
            BtnWhatsAppSolicitante.IsEnabled = false;
        }
    }

    private async void AprovarSolicitacaoSelecionada(int dias)
    {
        var req = DgSolicitacoesOnline.SelectedItem as OnlineActivationRequest;
        if (req == null)
        {
            MessageBox.Show("Selecione um técnico na lista para aprovar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!_licenseService.HasPrivateKey)
        {
            MessageBox.Show("Chave privada RSA do Administrador não encontrada para assinatura!", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            var (success, msg, token) = await _cloudLicenseService.ApproveActivationRequestAsync(req, dias, _licenseService, $"Aprovado online ({dias} dias)");
            if (success)
            {
                CarregarSolicitacoesOnlineAsync();
                CarregarCopiasCampo(); // Atualiza também a lista de dispositivos monitorados
                MessageBox.Show(
                    $"✅ Solicitação de {req.FullName} aprovada com sucesso por {dias} dias!\n\n" +
                    $"A licença criptográfica foi salva na nuvem. A máquina do técnico ativará automaticamente assim que consultar a internet.",
                    "Aprovação Concluída", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Falha ao aprovar: {msg}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro: {ex.Message}", "Exceção", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnAprovarSolicitacao30_Click(object sender, RoutedEventArgs e) => AprovarSolicitacaoSelecionada(30);
    private void BtnAprovarSolicitacao60_Click(object sender, RoutedEventArgs e) => AprovarSolicitacaoSelecionada(60);
    private void BtnAprovarSolicitacao90_Click(object sender, RoutedEventArgs e) => AprovarSolicitacaoSelecionada(90);

    private async void BtnRejeitarSolicitacao_Click(object sender, RoutedEventArgs e)
    {
        var req = DgSolicitacoesOnline.SelectedItem as OnlineActivationRequest;
        if (req == null) return;

        var confirm = MessageBox.Show($"Deseja realmente rejeitar a solicitação de {req.FullName}?", "Confirmar Rejeição", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var (success, msg) = await _cloudLicenseService.RejectActivationRequestAsync(req, "Rejeitado pelo Administrador");
        if (success)
        {
            CarregarSolicitacoesOnlineAsync();
            MessageBox.Show($"Solicitação rejeitada.", "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void BtnExcluirSolicitacao_Click(object sender, RoutedEventArgs e)
    {
        var req = DgSolicitacoesOnline.SelectedItem as OnlineActivationRequest;
        if (req == null)
        {
            MessageBox.Show("Selecione uma solicitação na lista para excluir.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Deseja realmente excluir permanentemente a solicitação de {req.FullName} ({req.RegionInfo})?\n\nIsso removerá o arquivo da nuvem e do cache local de testes.",
            "Confirmar Exclusão de Teste", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            BtnExcluirSolicitacao.IsEnabled = false;
            var (ok, msg) = await _cloudLicenseService.DeleteActivationRequestAsync(req);
            CarregarSolicitacoesOnlineAsync();
            MessageBox.Show(msg, "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao excluir solicitação: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnExcluirSolicitacao.IsEnabled = true;
        }
    }

    private void BtnWhatsAppSolicitante_Click(object sender, RoutedEventArgs e)
    {
        var req = DgSolicitacoesOnline.SelectedItem as OnlineActivationRequest;
        if (req == null || string.IsNullOrWhiteSpace(req.WhatsAppUrl)) return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = req.WhatsAppUrl, UseShellExecute = true });
        }
        catch { }
    }

    #endregion

    #region Aba 2: Licenciamento / Chaves de Ativação (Manual)

    private void TxtPedidoReq_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = TxtPedidoReq.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            BorderInfoPedido.Visibility = Visibility.Collapsed;
            _currentDecodedRequest = null;
            return;
        }

        _currentDecodedRequest = _licenseService.DecodeRequest(text);
        BorderInfoPedido.Visibility = Visibility.Visible;

        if (_currentDecodedRequest.IsValid)
        {
            TxtStatusDecodificacao.Text = "✅ Pedido Válido";
            TxtStatusDecodificacao.Foreground = UiBrushes.Get("#4ADE80");

            var infoTec = $"Técnico: {_currentDecodedRequest.FullName}";
            if (!string.IsNullOrWhiteSpace(_currentDecodedRequest.Cluster)) infoTec += $" | Cluster: {_currentDecodedRequest.Cluster}";
            if (!string.IsNullOrWhiteSpace(_currentDecodedRequest.Uf)) infoTec += $" | UF: {_currentDecodedRequest.Uf}";
            if (!string.IsNullOrWhiteSpace(_currentDecodedRequest.Phone)) infoTec += $" | WhatsApp: {_currentDecodedRequest.Phone}";
            if (!string.IsNullOrWhiteSpace(_currentDecodedRequest.Email)) infoTec += $" | E-mail: {_currentDecodedRequest.Email}";
            TxtInfoTecnico.Text = infoTec;

            TxtInfoHardware.Text = $"Machine GUID: {_currentDecodedRequest.MachineGuid} | Fingerprint: {_currentDecodedRequest.MachineFingerprint}";
            BtnGerarChave.IsEnabled = _licenseService.HasPrivateKey;

            if (!string.IsNullOrWhiteSpace(_currentDecodedRequest.FullName) && string.IsNullOrWhiteSpace(TxtNomeTecnico.Text))
            {
                TxtNomeTecnico.Text = _currentDecodedRequest.FullName;
            }
        }
        else
        {
            TxtStatusDecodificacao.Text = "❌ Código de Pedido Inválido";
            TxtStatusDecodificacao.Foreground = UiBrushes.Get("#F87171");
            TxtInfoTecnico.Text = "Não foi possível extrair identificação do técnico.";
            TxtInfoHardware.Text = _currentDecodedRequest.ErrorMessage ?? "Formato inválido.";
            BtnGerarChave.IsEnabled = false;
        }
    }

    private void BtnColarPedido_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var clip = Clipboard.GetText();
            if (!string.IsNullOrWhiteSpace(clip))
            {
                TxtPedidoReq.Text = clip.Trim();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao acessar a área de transferência: {ex.Message}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CbValidadeDias_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TxtDiasCustom == null) return;
        var item = CbValidadeDias.SelectedItem as ComboBoxItem;
        var tag = item?.Tag?.ToString() ?? "";
        TxtDiasCustom.Visibility = tag == "custom" ? Visibility.Visible : Visibility.Collapsed;
    }

    private int ObterDiasSelecionados()
    {
        var item = CbValidadeDias.SelectedItem as ComboBoxItem;
        var tag = item?.Tag?.ToString() ?? "30";

        if (tag == "custom")
        {
            return int.TryParse(TxtDiasCustom.Text, out var custom) && custom > 0 ? custom : 30;
        }

        return int.TryParse(tag, out var dias) ? dias : 30;
    }

    private void BtnGerarChave_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDecodedRequest == null || !_currentDecodedRequest.IsValid)
        {
            MessageBox.Show("Cole primeiro um código de solicitação válido (SPBREQ...) do técnico.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dias = ObterDiasSelecionados();
        var tecnico = TxtNomeTecnico.Text?.Trim() ?? string.Empty;
        var notas = TxtNotasLicenca.Text?.Trim() ?? string.Empty;

        var result = _licenseService.GenerateLicense(_currentDecodedRequest, dias, tecnico, notas);

        if (!result.Success)
        {
            MessageBox.Show($"Falha ao gerar chave: {result.ErrorMessage}", "Erro Criptográfico", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _lastGeneratedLicense = result;
        TxtChaveGerada.Text = result.LicenseToken;
        TxtInfoValidadeChave.Text = $"(Válida por {result.ValidDays} dias até {result.ExpirationDateIso} 23:59 UTC)";
        BorderChaveGerada.Visibility = Visibility.Visible;

        // Auto-copia para a área de transferência
        try
        {
            Clipboard.SetText(result.LicenseToken);
        }
        catch { }

        // Registra o dispositivo no cadastro central de cópias
        _cloudLicenseService.RegisterOrUpdateDevice(_currentDecodedRequest, result, notas);
        CarregarCopiasCampo();
        CarregarHistoricoChaves();
    }

    private void BtnCopiarChave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtChaveGerada.Text)) return;
        try
        {
            Clipboard.SetText(TxtChaveGerada.Text.Trim());
            MessageBox.Show("Chave copiada para a área de transferência!", "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao copiar: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnCopiarWhatsApp_Click(object sender, RoutedEventArgs e)
    {
        if (_lastGeneratedLicense == null || string.IsNullOrWhiteSpace(_lastGeneratedLicense.LicenseToken)) return;

        var tecnico = string.IsNullOrWhiteSpace(TxtNomeTecnico.Text) ? "Técnico" : TxtNomeTecnico.Text.Trim();
        var sb = new StringBuilder();
        sb.AppendLine($"Olá, {tecnico}!");
        sb.AppendLine();
        sb.AppendLine($"Sua chave de ativação do *SPARC Beta* foi gerada com sucesso:");
        sb.AppendLine();
        sb.AppendLine($"`{_lastGeneratedLicense.LicenseToken}`");
        sb.AppendLine();
        sb.AppendLine($"📅 *Validade:* {_lastGeneratedLicense.ValidDays} dias (até {_lastGeneratedLicense.ExpirationDateIso})");
        sb.AppendLine();
        sb.AppendLine("👉 Para ativar: basta copiar a chave acima, colar na tela de ativação do SPARC e clicar em *Ativar*.");

        try
        {
            Clipboard.SetText(sb.ToString());
            MessageBox.Show("Mensagem pronta para WhatsApp copiada com sucesso!", "WhatsApp", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao copiar: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CarregarHistoricoChaves()
    {
        try
        {
            var historico = _licenseService.LoadHistory();
            DgHistoricoChaves.ItemsSource = historico;
        }
        catch { }
    }

    private void BtnAtualizarHistorico_Click(object sender, RoutedEventArgs e)
    {
        CarregarHistoricoChaves();
    }

    private void BtnExcluirChaveHistorico_Click(object sender, RoutedEventArgs e)
    {
        var item = DgHistoricoChaves.SelectedItem as ActivationKeyHistoryItem;
        if (item == null)
        {
            MessageBox.Show("Selecione uma chave na tabela para excluir.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Deseja remover esta chave do histórico?\n\nTécnico: {item.TechnicianName}\nExpira: {item.ExpirationDate}",
            "Confirmar Exclusão de Chave", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        if (_licenseService.DeleteHistoryItem(item.LicenseToken))
        {
            CarregarHistoricoChaves();
            MessageBox.Show("Chave excluída do histórico com sucesso.", "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnLimparHistoricoChaves_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Deseja realmente limpar TODO o histórico de chaves geradas para organização dos testes?",
            "Limpar Todo o Histórico", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        if (_licenseService.ClearHistory())
        {
            CarregarHistoricoChaves();
            MessageBox.Show("Histórico de chaves limpo com sucesso.", "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    #endregion

    #region Aba 2: Gestão de Cópias em Campo & Licenças Online

    private List<OnlineDeviceRecord> _cachedDevices = new();

    private void CarregarCopiasCampo()
    {
        try
        {
            _cachedDevices = _cloudLicenseService.LoadLocalDevices();
            AtualizarMetricasCopias(_cachedDevices);
            FiltrarCopias();
        }
        catch { }
    }

    private void AtualizarMetricasCopias(List<OnlineDeviceRecord> list)
    {
        var total = list.Count;
        var ativas = list.Count(d => d.CalculatedStatus == DeviceLicenseStatus.Active);
        var expirando = list.Count(d => d.CalculatedStatus == DeviceLicenseStatus.ExpiringSoon);
        var revogadas = list.Count(d => d.CalculatedStatus == DeviceLicenseStatus.Revoked);

        TxtTotalCopias.Text = $"👥 Total: {total} técnico(s)";
        TxtCopiasAtivas.Text = $"🟢 Ativas: {ativas}";
        TxtCopiasExpirando.Text = $"🟡 A Expirar (<7d): {expirando}";
        TxtCopiasRevogadas.Text = $"🚫 Revogadas: {revogadas}";
    }

    private void FiltrarCopias()
    {
        var filtro = TxtFiltroCopias?.Text?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrWhiteSpace(filtro))
        {
            DgCopiasCampo.ItemsSource = _cachedDevices;
        }
        else
        {
            DgCopiasCampo.ItemsSource = _cachedDevices.Where(d =>
                d.FullName.ToLowerInvariant().Contains(filtro) ||
                d.Cluster.ToLowerInvariant().Contains(filtro) ||
                d.Uf.ToLowerInvariant().Contains(filtro) ||
                d.Phone.Contains(filtro) ||
                d.Email.ToLowerInvariant().Contains(filtro) ||
                d.MachineGuid.ToLowerInvariant().Contains(filtro) ||
                d.Notes.ToLowerInvariant().Contains(filtro)).ToList();
        }
    }

    private void TxtFiltroCopias_TextChanged(object sender, TextChangedEventArgs e)
    {
        FiltrarCopias();
    }

    private void BtnRecarregarCopias_Click(object sender, RoutedEventArgs e)
    {
        CarregarCopiasCampo();
    }

    private void DgCopiasCampo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var dev = DgCopiasCampo.SelectedItem as OnlineDeviceRecord;
        if (dev != null)
        {
            var reg = !string.IsNullOrWhiteSpace(dev.Cluster) ? $" [{dev.Cluster}/{dev.Uf}]" : "";
            TxtCopiaSelecionadaInfo.Text = $"Técnico: {dev.FullName}{reg} ({dev.Phone}) - Expira: {dev.ExpirationDateIso}";
            BtnConceder30Dias.IsEnabled = true;
            BtnConceder60Dias.IsEnabled = true;
            BtnConceder90Dias.IsEnabled = true;
            BtnChamarWhatsApp.IsEnabled = !string.IsNullOrWhiteSpace(dev.Phone);
            BtnRevogarAcesso.IsEnabled = dev.CalculatedStatus != DeviceLicenseStatus.Revoked;
        }
        else
        {
            TxtCopiaSelecionadaInfo.Text = "Selecione um técnico na tabela acima para gerenciar a licença";
            BtnConceder30Dias.IsEnabled = false;
            BtnConceder60Dias.IsEnabled = false;
            BtnConceder90Dias.IsEnabled = false;
            BtnChamarWhatsApp.IsEnabled = false;
            BtnRevogarAcesso.IsEnabled = false;
        }
    }

    private void ConcederDiasSelecionado(int dias)
    {
        var dev = DgCopiasCampo.SelectedItem as OnlineDeviceRecord;
        if (dev == null)
        {
            MessageBox.Show("Selecione um técnico na tabela para estender o prazo.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var (success, msg, updated) = _cloudLicenseService.ExtendLicense(dev.MachineGuid, dias, _licenseService);
        if (success)
        {
            CarregarCopiasCampo();
            MessageBox.Show(
                $"Prazo estendido com sucesso para {dev.FullName}!\n\n{msg}\n\nO técnico será atualizado automaticamente ao conectar à internet.",
                "Renovação Concluída", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show($"Erro ao estender prazo: {msg}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnConceder30Dias_Click(object sender, RoutedEventArgs e) => ConcederDiasSelecionado(30);
    private void BtnConceder60Dias_Click(object sender, RoutedEventArgs e) => ConcederDiasSelecionado(60);
    private void BtnConceder90Dias_Click(object sender, RoutedEventArgs e) => ConcederDiasSelecionado(90);

    private void BtnChamarWhatsApp_Click(object sender, RoutedEventArgs e)
    {
        var dev = DgCopiasCampo.SelectedItem as OnlineDeviceRecord;
        if (dev == null || string.IsNullOrWhiteSpace(dev.WhatsAppUrl)) return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = dev.WhatsAppUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao abrir link do WhatsApp: {ex.Message}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnRevogarAcesso_Click(object sender, RoutedEventArgs e)
    {
        var dev = DgCopiasCampo.SelectedItem as OnlineDeviceRecord;
        if (dev == null) return;

        var resp = MessageBox.Show(
            $"ATENÇÃO: Deseja REVOGAR imediatamente o acesso do técnico {dev.FullName}?\n\nEsta cópia será bloqueada automaticamente na próxima conexão à rede.",
            "Confirmar Revogação", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (resp == MessageBoxResult.Yes)
        {
            var (success, msg) = _cloudLicenseService.RevokeDevice(dev.MachineGuid, "Revogado pelo administrador");
            CarregarCopiasCampo();
            MessageBox.Show(msg, "Revogação", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnExcluirCopia_Click(object sender, RoutedEventArgs e)
    {
        var dev = DgCopiasCampo.SelectedItem as OnlineDeviceRecord;
        if (dev == null)
        {
            MessageBox.Show("Selecione um técnico na tabela para remover.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Deseja realmente remover o registro de {dev.FullName} ({dev.MachineGuid}) da lista de dispositivos monitorados?",
            "Confirmar Remoção de Teste", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        var (ok, msg) = _cloudLicenseService.DeleteDevice(dev.MachineGuid);
        if (ok)
        {
            CarregarCopiasCampo();
            MessageBox.Show(msg, "Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnAbrirPastaDevices_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = Path.GetDirectoryName(_cloudLicenseService.LocalFilePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true, Verb = "open" });
            }
        }
        catch { }
    }

    #endregion

    #region Aba 3: Gestão do Repositório de Firmwares

    private void CarregarModelosFirmware()
    {
        _firmwareRepoService.EnsureLocalRepositoryStructure();
        var rows = new List<AdminFirmwareModelRow>();

        foreach (var def in FirmwareModelMap.AllDefinitions)
        {
            var local = _firmwareRepoService.GetLocalFirmware(def.Series);
            rows.Add(new AdminFirmwareModelRow
            {
                Series = def.Series,
                DisplayName = def.DisplayName,
                FolderName = def.FolderName,
                LocalFileName = local?.FileName ?? "(Nenhum arquivo homologado)",
                LocalVersion = local?.DetectedVersion ?? "-",
                LocalDisplaySize = local != null ? local.DisplaySize : "-",
                StatusDescription = local != null ? "✅ Pronto no repositório local" : "⚠️ Pasta vazia (pendente)",
                FullLocalPath = local?.LocalFilePath
            });
        }

        DgModelosFirmware.ItemsSource = rows;
    }

    private void DgModelosFirmware_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = DgModelosFirmware.SelectedItem as AdminFirmwareModelRow;
        if (row != null)
        {
            TxtModeloSelecionadoInfo.Text = $"Modelo Ativo: {row.DisplayName} (Pasta: {row.FolderName})";
            BtnImportarFirmware.IsEnabled = true;
            BtnPublicarModeloNuvem.IsEnabled = !string.IsNullOrEmpty(row.FullLocalPath) && File.Exists(row.FullLocalPath);
            BtnInspecionarChecksum.IsEnabled = !string.IsNullOrEmpty(row.FullLocalPath) && File.Exists(row.FullLocalPath);
            BtnLimparModelo.IsEnabled = !string.IsNullOrEmpty(row.FullLocalPath) && File.Exists(row.FullLocalPath);
        }
        else
        {
            TxtModeloSelecionadoInfo.Text = "Selecione um modelo na tabela acima para gerenciar";
            BtnImportarFirmware.IsEnabled = false;
            BtnPublicarModeloNuvem.IsEnabled = false;
            BtnInspecionarChecksum.IsEnabled = false;
            BtnLimparModelo.IsEnabled = false;
        }
    }

    private void BtnRecarregarModelos_Click(object sender, RoutedEventArgs e)
    {
        CarregarModelosFirmware();
    }

    private void BtnImportarFirmware_Click(object sender, RoutedEventArgs e)
    {
        var row = DgModelosFirmware.SelectedItem as AdminFirmwareModelRow;
        if (row == null)
        {
            MessageBox.Show("Selecione um modelo na tabela para importar a versão homologada.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var def = FirmwareModelMap.GetDefinition(row.Series);
        var isForti = row.Series == DeviceSeries.FortiGate40F;
        var isHpe = row.Series is DeviceSeries.Msr954 or DeviceSeries.Msr930 or DeviceSeries.Msr1002;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Importar Firmware Homologado para {row.DisplayName}",
            Filter = isForti ? "Imagens FortiOS (*.out)|*.out|Todos os Arquivos (*.*)|*.*"
                   : isHpe ? "Pacotes HPE Comware (*.ipe;*.bin)|*.ipe;*.bin|Todos os Arquivos (*.*)|*.*"
                   : "Imagens Cisco IOS (*.bin)|*.bin|Todos os Arquivos (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        var val = FirmwareCompatibilityValidator.Validate(row.Series, dlg.FileName);
        if (!val.IsCompatible)
        {
            var confirmIncomp = MessageBox.Show(
                $"Aviso de Compatibilidade:\n\n{val.ErrorMessage}\n\nDeseja forçar a importação deste arquivo mesmo assim?",
                "Atenção - Possível Incompatibilidade",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmIncomp != MessageBoxResult.Yes) return;
        }

        try
        {
            var folder = _firmwareRepoService.GetLocalFolderForSeries(row.Series);
            var fi = new FileInfo(dlg.FileName);
            var destPath = Path.Combine(folder, fi.Name);

            // Pergunta de confirmação
            var msgConfirm = $"Você está definindo o arquivo:\n\n'{fi.Name}' ({(fi.Length / (1024.0 * 1024.0)):N1} MB)\n\nComo o firmware homologado para {row.DisplayName}.\n\nQualquer versão anterior desta pasta será substituída para manter o repositório organizado com arquivo único. Confirmar?";
            var resp = MessageBox.Show(msgConfirm, "Confirmar Homologação de Firmware", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (resp != MessageBoxResult.Yes) return;

            // Remove arquivos anteriores da pasta do modelo
            var di = new DirectoryInfo(folder);
            foreach (var oldFile in di.GetFiles("*.*", SearchOption.TopDirectoryOnly))
            {
                if (oldFile.Extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) ||
                    oldFile.Extension.Equals(".out", StringComparison.OrdinalIgnoreCase) ||
                    oldFile.Extension.Equals(".ipe", StringComparison.OrdinalIgnoreCase))
                {
                    try { oldFile.Delete(); } catch { }
                }
            }

            File.Copy(dlg.FileName, destPath, true);
            CarregarModelosFirmware();

            MessageBox.Show($"Firmware homologado importado com sucesso para '{def?.FolderName}':\n\n{fi.Name}", "Homologação Concluída", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao importar arquivo: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnInspecionarChecksum_Click(object sender, RoutedEventArgs e)
    {
        var row = DgModelosFirmware.SelectedItem as AdminFirmwareModelRow;
        if (row == null || string.IsNullOrEmpty(row.FullLocalPath) || !File.Exists(row.FullLocalPath)) return;

        try
        {
            var fi = new FileInfo(row.FullLocalPath);
            string sha256;
            string md5;

            using (var stream = File.OpenRead(row.FullLocalPath))
            {
                using var sha = SHA256.Create();
                sha256 = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
            }

            using (var stream = File.OpenRead(row.FullLocalPath))
            {
                using var m = MD5.Create();
                md5 = Convert.ToHexString(m.ComputeHash(stream)).ToLowerInvariant();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Relatório de Integridade — {row.DisplayName}");
            sb.AppendLine();
            sb.AppendLine($"• Arquivo: {fi.Name}");
            sb.AppendLine($"• Tamanho: {fi.Length:N0} bytes ({(fi.Length / (1024.0 * 1024.0)):N2} MB)");
            sb.AppendLine($"• Modificado em: {fi.LastWriteTime:dd/MM/yyyy HH:mm:ss}");
            sb.AppendLine($"• MD5:    {md5}");
            sb.AppendLine($"• SHA256: {sha256}");
            sb.AppendLine();
            sb.AppendLine("Deseja copiar esses detalhes para a área de transferência?");

            var res = MessageBox.Show(sb.ToString(), "Checksum & Integridade", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (res == MessageBoxResult.Yes)
            {
                Clipboard.SetText($"Arquivo: {fi.Name}\nTamanho: {fi.Length} bytes\nMD5: {md5}\nSHA256: {sha256}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao calcular checksums: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnLimparModelo_Click(object sender, RoutedEventArgs e)
    {
        var row = DgModelosFirmware.SelectedItem as AdminFirmwareModelRow;
        if (row == null || string.IsNullOrEmpty(row.FullLocalPath) || !File.Exists(row.FullLocalPath)) return;

        var resp = MessageBox.Show($"Deseja remover o firmware '{row.LocalFileName}' do repositório local?", "Remover Arquivo", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (resp == MessageBoxResult.Yes)
        {
            try
            {
                File.Delete(row.FullLocalPath);
                CarregarModelosFirmware();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao remover: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnSalvarToken_Click(object sender, RoutedEventArgs e)
    {
        var token = TxtGitHubToken.Text.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            MessageBox.Show("Informe o token PAT gerado no GitHub para salvar.", "Token Vazio", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var betaTokenPath = @"C:\SPARC\beta\github_token.txt";
            var fwTokenPath = @"C:\SPARC\firmwares\github_token.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(betaTokenPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(fwTokenPath)!);
            File.WriteAllText(betaTokenPath, token);
            File.WriteAllText(fwTokenPath, token);

            _firmwareRepoService.GitHubToken = token;
            _cloudLicenseService.GitHubToken = token;

            TxtStatusRepoOnline.Text = "✅ Token PAT salvo com sucesso! Clique em 'Testar Conexão com GitHub'.";
            TxtStatusRepoOnline.Foreground = UiBrushes.Get("#4ADE80");
            MessageBox.Show("Token PAT do GitHub salvo com sucesso!\n\nAs consultas e downloads no repositório privado agora estão autenticados.", "Token Configurado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao salvar token: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnTestarGitHub_Click(object sender, RoutedEventArgs e)
    {
        TxtStatusRepoOnline.Text = "Testando conexão com o repositório GitHub...";
        TxtStatusRepoOnline.Foreground = UiBrushes.Get("#FBBF24");

        try
        {
            var report = await _firmwareRepoService.CheckUpdatesAsync();
            if (report.HasInternetAccess)
            {
                var remotos = report.Items.Where(i => i.Remote != null).Count();
                TxtStatusRepoOnline.Text = $"✅ Conectado ao GitHub! ({remotos} modelo(s) com arquivos remotos identificados)";
                TxtStatusRepoOnline.Foreground = UiBrushes.Get("#4ADE80");
            }
            else
            {
                TxtStatusRepoOnline.Text = "⚠️ Sem conexão com o GitHub ou repositório vazio.";
                TxtStatusRepoOnline.Foreground = UiBrushes.Get("#F87171");
            }
        }
        catch (Exception ex)
        {
            TxtStatusRepoOnline.Text = $"❌ Erro ao testar repositório: {ex.Message}";
            TxtStatusRepoOnline.Foreground = UiBrushes.Get("#F87171");
        }
    }

    private async void BtnChecarReleases_Click(object sender, RoutedEventArgs e)
    {
        BtnChecarReleases.IsEnabled = false;
        BtnChecarReleases.Content = "⏳ Consultando...";
        TxtLogGit.Text = $"[{DateTime.Now:HH:mm:ss}] Conectando à API do GitHub Releases ({_firmwareRepoService.RemoteRepoOwner}/{_firmwareRepoService.RemoteRepoName})...\n";

        try
        {
            var remotes = await _firmwareRepoService.QueryRemoteFirmwaresAsync();
            if (remotes.Count > 0)
            {
                TxtLogGit.AppendText($"[{DateTime.Now:HH:mm:ss}] ✅ Sucesso! Encontrados {remotes.Count} firmware(s) homologado(s) na Release ativa:\n");
                foreach (var r in remotes)
                {
                    TxtLogGit.AppendText($"   • [{r.Series}] {r.FileName} ({r.DisplaySize})\n     URL: {r.DownloadUrl}\n");
                }
                TxtStatusRepoOnline.Text = $"✅ Release Ativa: {remotes.Count} firmware(s) homologado(s) na nuvem";
                TxtStatusRepoOnline.Foreground = UiBrushes.Get("#4ADE80");
            }
            else
            {
                TxtLogGit.AppendText($"[{DateTime.Now:HH:mm:ss}] ℹ️ Nenhuma release com assets homologados encontrada ainda no repositório.\n");
                TxtLogGit.AppendText($"   👉 Clique no botão 'Criar / Gerenciar Release no GitHub' ao lado para criar a primeira release ('homologados') e anexar os arquivos de firmware (incluindo o HPE MSR 954 de 117 MB!).\n");
                TxtStatusRepoOnline.Text = "ℹ️ Nenhuma Release criada ainda no GitHub";
                TxtStatusRepoOnline.Foreground = UiBrushes.Get("#FBBF24");
            }
            TxtLogGit.ScrollToEnd();
        }
        catch (Exception ex)
        {
            TxtLogGit.AppendText($"[{DateTime.Now:HH:mm:ss}] ❌ Erro ao consultar Releases: {ex.Message}\n");
            TxtLogGit.ScrollToEnd();
        }
        finally
        {
            BtnChecarReleases.IsEnabled = true;
            BtnChecarReleases.Content = "🔄 Consultar Release Ativa";
        }
    }

    private void BtnAbrirCriarRelease_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            TxtLogGit.AppendText($"[{DateTime.Now:HH:mm:ss}] Abrindo página de Releases no navegador: {_firmwareRepoService.ReleasesWebUrl}\n");
            TxtLogGit.AppendText($"[{DateTime.Now:HH:mm:ss}] 💡 Dica: Crie uma nova Release com tag 'homologados', arraste os arquivos de C:\\SPARC\\firmwares e clique em 'Publish release'.\n");
            TxtLogGit.ScrollToEnd();

            Process.Start(new ProcessStartInfo
            {
                FileName = _firmwareRepoService.ReleasesWebUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao abrir navegador: {ex.Message}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void BtnPublicarModeloNuvem_Click(object sender, RoutedEventArgs e)
    {
        var row = DgModelosFirmware.SelectedItem as AdminFirmwareModelRow;
        if (row == null || string.IsNullOrEmpty(row.FullLocalPath) || !File.Exists(row.FullLocalPath))
        {
            MessageBox.Show("Selecione um modelo que possua firmware homologado em cache local para publicar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(_firmwareRepoService.GitHubToken))
        {
            MessageBox.Show("O Token PAT do GitHub com permissão 'Contents: Read and write' é necessário para realizar o upload.\n\nCole o token no campo superior e clique em 'Salvar Token'.", "Token Necessário", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var fi = new FileInfo(row.FullLocalPath);
        var resp = MessageBox.Show(
            $"Deseja publicar o firmware '{fi.Name}' ({(fi.Length / (1024.0 * 1024.0)):N1} MB) diretamente na Release 'homologados' do GitHub?\n\nO upload será realizado automaticamente via API sem abrir o navegador.",
            "Confirmar Upload para o GitHub",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (resp != MessageBoxResult.Yes) return;

        BtnPublicarModeloNuvem.IsEnabled = false;
        BtnPublicarTodosNuvem.IsEnabled = false;
        PbUploadFirmware.Visibility = Visibility.Visible;
        PbUploadFirmware.Value = 0;

        TxtLogGit.Text = $"[{DateTime.Now:HH:mm:ss}] Iniciando upload de {fi.Name} para a Release 'homologados' no GitHub...\n";

        var prog = new Progress<FirmwareDownloadProgress>(p =>
        {
            PbUploadFirmware.Value = p.Percentage;
            TxtStatusRepoOnline.Text = $"⬆️ {p.StatusText}";
            TxtStatusRepoOnline.Foreground = UiBrushes.Get("#38BDF8");
        });

        try
        {
            await _firmwareRepoService.UploadFirmwareAssetAsync(row.FullLocalPath, prog);
            PbUploadFirmware.Value = 100;
            TxtLogGit.AppendText($"[{DateTime.Now:HH:mm:ss}] ✅ Upload concluído com sucesso: {fi.Name} publicado na Release 'homologados'!\n");
            TxtStatusRepoOnline.Text = $"✅ {fi.Name} publicado com sucesso na nuvem!";
            TxtStatusRepoOnline.Foreground = UiBrushes.Get("#4ADE80");
            MessageBox.Show($"Firmware '{fi.Name}' publicado com sucesso na nuvem!\n\nAgora qualquer técnico com o SPARC poderá buscar e baixar esta versão online.", "Upload Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            TxtLogGit.AppendText($"[{DateTime.Now:HH:mm:ss}] ❌ Falha no upload: {ex.Message}\n");
            TxtStatusRepoOnline.Text = $"❌ Erro no upload: {ex.Message}";
            TxtStatusRepoOnline.Foreground = UiBrushes.Get("#F87171");
            MessageBox.Show($"Erro ao realizar upload para o GitHub:\n\n{ex.Message}", "Erro de Upload", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnPublicarModeloNuvem.IsEnabled = true;
            BtnPublicarTodosNuvem.IsEnabled = true;
            PbUploadFirmware.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnPublicarTodosNuvem_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_firmwareRepoService.GitHubToken))
        {
            MessageBox.Show("O Token PAT do GitHub com permissão 'Contents: Read and write' é necessário para realizar o upload.\n\nCole o token no campo superior e clique em 'Salvar Token'.", "Token Necessário", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Localiza todos os firmwares locais prontos para upload
        var locaisParaEnviar = new List<(DeviceSeries Series, string DisplayName, LocalFirmwareInfo Local)>();
        foreach (var def in FirmwareModelMap.AllDefinitions)
        {
            var l = _firmwareRepoService.GetLocalFirmware(def.Series);
            if (l != null && File.Exists(l.LocalFilePath))
            {
                locaisParaEnviar.Add((def.Series, def.DisplayName, l));
            }
        }

        if (locaisParaEnviar.Count == 0)
        {
            MessageBox.Show("Nenhum arquivo de firmware foi encontrado em C:\\SPARC\\firmwares para publicar.", "Repositório Local Vazio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Foram encontrados {locaisParaEnviar.Count} firmware(s) homologado(s) prontos para upload direto:\n");
        foreach (var it in locaisParaEnviar)
        {
            sb.AppendLine($"• {it.DisplayName}: {it.Local.FileName} ({it.Local.DisplaySize})");
        }
        sb.AppendLine("\nDeseja publicar todos esses arquivos diretamente na Release 'homologados' do GitHub agora?");

        var resp = MessageBox.Show(sb.ToString(), "Confirmar Publicação em Lote", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (resp != MessageBoxResult.Yes) return;

        BtnPublicarTodosNuvem.IsEnabled = false;
        BtnPublicarModeloNuvem.IsEnabled = false;
        PbUploadFirmware.Visibility = Visibility.Visible;

        TxtLogGit.Text = $"[{DateTime.Now:HH:mm:ss}] Conectando ao GitHub para publicação em lote de {locaisParaEnviar.Count} modelo(s)...\n";

        var idx = 0;
        var total = locaisParaEnviar.Count;
        var sucessos = 0;

        foreach (var it in locaisParaEnviar)
        {
            idx++;
            PbUploadFirmware.Value = 0;
            TxtLogGit.AppendText($"[{DateTime.Now:HH:mm:ss}] ({idx}/{total}) Enviando {it.DisplayName}: {it.Local.FileName} ({it.Local.DisplaySize})...\n");
            TxtLogGit.ScrollToEnd();

            var prog = new Progress<FirmwareDownloadProgress>(p =>
            {
                PbUploadFirmware.Value = p.Percentage;
                TxtStatusRepoOnline.Text = $"⬆️ [{idx}/{total}] {p.StatusText}";
                TxtStatusRepoOnline.Foreground = UiBrushes.Get("#38BDF8");
            });

            try
            {
                await _firmwareRepoService.UploadFirmwareAssetAsync(it.Local.LocalFilePath, prog);
                sucessos++;
                TxtLogGit.AppendText($"[{DateTime.Now:HH:mm:ss}]   ✅ {it.Local.FileName} publicado com sucesso!\n");
            }
            catch (Exception ex)
            {
                TxtLogGit.AppendText($"[{DateTime.Now:HH:mm:ss}]   ❌ Erro ao enviar {it.Local.FileName}: {ex.Message}\n");
            }
        }

        PbUploadFirmware.Visibility = Visibility.Collapsed;
        BtnPublicarTodosNuvem.IsEnabled = true;
        BtnPublicarModeloNuvem.IsEnabled = true;

        TxtLogGit.AppendText($"[{DateTime.Now:HH:mm:ss}] Fim do processo: {sucessos} de {total} arquivo(s) publicados no GitHub.\n");
        TxtLogGit.ScrollToEnd();

        TxtStatusRepoOnline.Text = $"✅ Publicação concluída: {sucessos}/{total} firmware(s) ativos na nuvem";
        TxtStatusRepoOnline.Foreground = UiBrushes.Get("#4ADE80");

        MessageBox.Show($"Publicação concluída com sucesso!\n\n{sucessos} de {total} arquivo(s) foram enviados diretamente para a Release oficial no GitHub.", "Publicação Concluída", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnAbrirPastaFirmwares_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _firmwareRepoService.EnsureLocalRepositoryStructure();
            Process.Start(new ProcessStartInfo
            {
                FileName = _firmwareRepoService.LocalRepositoryRoot,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao abrir pasta: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnAbrirPastaBeta_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = @"C:\SPARC\beta";
            if (Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true, Verb = "open" });
            }
        }
        catch { }
    }

    #endregion

    private void BtnFechar_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
