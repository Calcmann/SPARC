using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using NetworkDevice.Android.Services;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Firmware;

namespace NetworkDevice.Android.Views;

public partial class FirmwarePage : ContentPage
{
    private readonly DeviceConnectionManager _connManager = DeviceConnectionManager.Instance;
    private readonly FirmwareRepositoryService _firmwareRepo = new();
    private RemoteFirmwareInfo? _cachedOfficialRemote;

    public FirmwarePage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateDeviceDisplay();
        CheckEthernetStatus();
    }

    private void UpdateDeviceDisplay()
    {
        var detected = _connManager.LastDetectionResult;
        if (detected != null && detected.Series != DeviceSeries.Unknown)
        {
            AuditModelLabel.Text = $"{detected.Manufacturer} {detected.Series}";
        }
        else
        {
            AuditModelLabel.Text = "Nenhum equipamento identificado no console.";
        }
    }

    private async void OnAuditarFwClicked(object? sender, EventArgs e)
    {
        if (!_connManager.IsConnected)
        {
            await DisplayAlert("Aviso", "Conecte o console serial USB na aba Provisionamento antes de auditar.", "OK");
            return;
        }

        AuditarFwBtn.IsEnabled = false;
        AuditStatusMsgLabel.Text = "Auditando firmware, WAN e consultando repositório central...";
        AppendLog("[*] =================================================================");
        AppendLog("[*]          AUDITORIA DE FIRMWARE & DIAGNÓSTICO WAN (SPARC)         ");
        AppendLog("[*] =================================================================");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));

            // 1. Identifica o equipamento se ainda não identificado
            if (_connManager.LastDetectionResult == null || _connManager.LastDetectionResult.Series == DeviceSeries.Unknown)
            {
                AppendLog("[*] Identificando série do roteador na serial...");
                await _connManager.IdentifyDeviceAsync(cts.Token);
            }

            var detected = _connManager.LastDetectionResult;
            if (detected == null || detected.Series == DeviceSeries.Unknown)
            {
                AppendLog("[!] Não foi possível identificar o modelo do roteador.");
                await DisplayAlert("Erro", "Não foi possível identificar o modelo do roteador.", "OK");
                return;
            }

            AuditModelLabel.Text = $"{detected.Manufacturer} {detected.Series}";

            // 2. Consulta o repositório central de firmwares homologados
            AppendLog($"[*] Consultando repositório central para '{detected.Series}'...");
            try
            {
                _cachedOfficialRemote = await _firmwareRepo.QueryRemoteForSeriesAsync(detected.Series, cts.Token);
            }
            catch (Exception ex)
            {
                AppendLog($"[AVISO] Consulta ao repositório central falhou ({ex.Message}).");
            }

            if (_cachedOfficialRemote != null)
            {
                AuditOfficialLabel.Text = $"{_cachedOfficialRemote.FileName} ({_cachedOfficialRemote.DisplaySize})";
                FirmwareFileNameEntry.Text = _cachedOfficialRemote.FileName;
                FirmwareUrlEntry.Text = RouterDirectFirmwareUpdater.BuildDirectDownloadUrl(detected.Series);
            }
            else
            {
                AuditOfficialLabel.Text = "Nenhum arquivo homologado cadastrado.";
            }

            // 3. Executa a auditoria no roteador
            if (_connManager.CurrentSession == null)
            {
                AppendLog("[!] Sessão serial interativa não disponível. Conecte na aba Provisionamento.");
                return;
            }

            var updater = new RouterDirectFirmwareUpdater(msg =>
            {
                MainThread.BeginInvokeOnMainThread(() => AppendLog(msg));
                return Task.CompletedTask;
            });

            var result = await updater.AuditComplianceAsync(_connManager.CurrentSession, detected.Series, _cachedOfficialRemote, cts.Token);

            AuditCurrentVersionLabel.Text = result.CurrentVersion;
            AuditStatusMsgLabel.Text = result.Message;

            if (result.IsCompliant)
            {
                ComplianceBadge.Text = "CONFORME";
                ComplianceBadge.TextColor = Color.FromArgb("#4ADE80");
            }
            else
            {
                ComplianceBadge.Text = "ATUALIZAÇÃO RECOMENDADA";
                ComplianceBadge.TextColor = Color.FromArgb("#F87171");
            }

            AppendLog($"[✓] Versão no Roteador: {result.CurrentVersion}");
            AppendLog($"[✓] Homologada Nuvem : {result.OfficialFirmwareName ?? "N/D"}");

            // 4. Diagnóstico de Porta WAN e Internet
            var wanDiag = await updater.CheckWanAndInternetAsync(_connManager.CurrentSession, detected.Series, cts.Token);
            AtualizarStatusWanVisual(wanDiag);

            // Cenário 1-c: WAN física está DOWN
            if (!wanDiag.IsPhysicalUp)
            {
                bool loopingDown = true;
                while (loopingDown)
                {
                    var opt = await DisplayActionSheet(
                        $"Porta WAN {wanDiag.InterfaceName} está DOWN",
                        null,
                        null,
                        "🔌 Conectar Porta WAN no Acesso e Retestar",
                        "📦 Permanecer em Bancada (Atualização via WAN Indisponível)");

                    if (opt == "🔌 Conectar Porta WAN no Acesso e Retestar")
                    {
                        AppendLog($"[*] Retestando porta WAN {wanDiag.InterfaceName}...");
                        wanDiag = await updater.CheckWanAndInternetAsync(_connManager.CurrentSession, detected.Series, cts.Token);
                        AtualizarStatusWanVisual(wanDiag);
                        if (wanDiag.IsPhysicalUp)
                        {
                            loopingDown = false;
                            AppendLog($"[✓] Porta WAN {wanDiag.InterfaceName} detectada UP!");
                        }
                        else
                        {
                            AppendLog($"[!] Porta WAN {wanDiag.InterfaceName} permanece DOWN.");
                            var tentarNovamente = await DisplayAlert(
                                "Link Físico DOWN",
                                $"A porta WAN {wanDiag.InterfaceName} permanece sem link físico.\n\nVerifique se o cabo está conectado à porta correta do equipamento de acesso.\n\nDeseja tentar novamente?",
                                "TENTAR NOVAMENTE", "SEGUIR COMO BANCADA");

                            if (!tentarNovamente) loopingDown = false;
                        }
                    }
                    else
                    {
                        loopingDown = false;
                    }
                }

                if (!wanDiag.IsPhysicalUp)
                {
                    if (!result.IsCompliant)
                    {
                        var tentarLocal = await DisplayAlert(
                            "⚠️ WAN DOWN — Alternativa Bancada (OTG + ETH)",
                            $"A porta WAN física ({wanDiag.InterfaceName}) está DOWN.\n\n" +
                            $"O firmware em execução ({result.CurrentVersion}) está desatualizado em relação ao homologado ({result.OfficialFirmwareName}).\n\n" +
                            $"Deseja realizar a atualização em bancada via OTG + Cabo Ethernet RJ45 (usando o servidor local do celular conectado via HUB USB-C)?",
                            "SIM, ATUALIZAR VIA OTG/ETH", "APENAS AUDITAR");

                        if (tentarLocal)
                        {
                            await PrepararEExecutarAtualizacaoLocalAsync(detected, result, cts.Token);
                        }
                    }
                    return;
                }
            }

            // Cenário 1-b: WAN UP, mas sem conectividade com a Internet
            if (!wanDiag.HasInternet)
            {
                bool loopReteste = true;
                while (loopReteste)
                {
                    var retestar = await DisplayAlert(
                        "WAN Conectada sem Internet",
                        $"A porta física WAN ({wanDiag.InterfaceName}) está CONECTADA (UP), porém não há conectividade com a internet.\n\n" +
                        $"• Revise as conexões físicas e o enlace do acesso.\n" +
                        $"• Caso necessário, acione o suporte da operadora para validação do circuito.\n\n" +
                        $"Toque em 'Retestar Conectividade' após normalização do link para checar a internet e prosseguir com a atualização de firmware via WAN.",
                        "🔄 RETESTAR CONECTIVIDADE", "CANCELAR");

                    if (retestar)
                    {
                        AppendLog("[*] Retestando conectividade com a internet a partir do roteador...");
                        var okInternet = await updater.TestWanReachabilityAsync(_connManager.CurrentSession, detected.Series, cts.Token);
                        if (okInternet)
                        {
                            wanDiag = new WanDiagnosticsResult(wanDiag.InterfaceName, true, true, "Conectividade Internet OK");
                            AtualizarStatusWanVisual(wanDiag);
                            loopReteste = false;
                            AppendLog("[✓] Conectividade com a internet confirmada com sucesso!");
                        }
                        else
                        {
                            AppendLog("[!] Internet ainda não respondeu ao teste no roteador.");
                        }
                    }
                    else
                    {
                        loopReteste = false;
                        if (!result.IsCompliant)
                        {
                            var tentarLocal = await DisplayAlert(
                                "WAN sem Internet — Alternativa Bancada (OTG + ETH)",
                                $"A porta WAN está conectada (UP), mas sem internet da operadora.\n\n" +
                                $"Deseja realizar a atualização em bancada via OTG + Cabo Ethernet (RJ45)?",
                                "SIM, ATUALIZAR VIA OTG/ETH", "CANCELAR");

                            if (tentarLocal)
                            {
                                await PrepararEExecutarAtualizacaoLocalAsync(detected, result, cts.Token);
                            }
                        }
                        return;
                    }
                }
            }

            // Cenário 1-a: WAN UP e Conectividade Internet OK
            if (result.IsCompliant)
            {
                await DisplayAlert("Firmware Homologado", "A porta WAN está conectada, a internet está ativa e o equipamento já está com a versão homologada instalada!", "OK");
                return;
            }

            var confirmUpdate = await DisplayAlert(
                "Atualização de Firmware via WAN",
                $"Porta WAN conectada e conectividade com a internet confirmada!\n\n" +
                $"Versão Atual: {result.CurrentVersion}\n" +
                $"Versão Homologada: {result.OfficialFirmwareName}\n\n" +
                $"Deseja instruir o roteador a baixar e gravar a versão oficial diretamente pela WAN agora?",
                "SIM, ATUALIZAR AGORA", "NÃO, APENAS AUDITAR");

            if (confirmUpdate)
            {
                var url = FirmwareUrlEntry.Text?.Trim() ?? RouterDirectFirmwareUpdater.BuildDirectDownloadUrl(detected.Series);
                var fileName = FirmwareFileNameEntry.Text?.Trim() ?? result.OfficialFirmwareName ?? "firmware.bin";
                await ExecuteRouterDownloadAsync(detected.Series, url, fileName, cts.Token);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[!] Erro durante auditoria: {ex.Message}");
            await DisplayAlert("Falha na Auditoria", ex.Message, "OK");
        }
        finally
        {
            AuditarFwBtn.IsEnabled = true;
        }
    }

    private async void OnRetestarWanClicked(object? sender, EventArgs e)
    {
        if (!_connManager.IsConnected || _connManager.CurrentSession == null)
        {
            await DisplayAlert("Aviso", "Conecte o console serial USB primeiro.", "OK");
            return;
        }

        RetestarWanBtn.IsEnabled = false;
        AppendLog("[*] Executando reteste rápido de enlace WAN e Internet...");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var detected = _connManager.LastDetectionResult;
            var series = detected?.Series ?? DeviceSeries.Isr841;

            var updater = new RouterDirectFirmwareUpdater(msg =>
            {
                MainThread.BeginInvokeOnMainThread(() => AppendLog(msg));
                return Task.CompletedTask;
            });

            var wanDiag = await updater.CheckWanAndInternetAsync(_connManager.CurrentSession, series, cts.Token);
            AtualizarStatusWanVisual(wanDiag);
            await DisplayAlert("Diagnóstico WAN", wanDiag.Details, "OK");
        }
        catch (Exception ex)
        {
            AppendLog($"[!] Falha no reteste: {ex.Message}");
            await DisplayAlert("Erro", ex.Message, "OK");
        }
        finally
        {
            RetestarWanBtn.IsEnabled = true;
        }
    }

    private void AtualizarStatusWanVisual(WanDiagnosticsResult diag)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (diag.IsPhysicalUp)
            {
                WanPhysicalStatusLabel.Text = $"UP ({diag.InterfaceName})";
                WanPhysicalStatusLabel.TextColor = Color.FromArgb("#4ADE80");
            }
            else
            {
                WanPhysicalStatusLabel.Text = $"DOWN ({diag.InterfaceName})";
                WanPhysicalStatusLabel.TextColor = Color.FromArgb("#EF4444");
            }

            if (diag.HasInternet)
            {
                WanInternetStatusLabel.Text = "OK (Internet Ativa)";
                WanInternetStatusLabel.TextColor = Color.FromArgb("#4ADE80");
            }
            else
            {
                WanInternetStatusLabel.Text = diag.IsPhysicalUp ? "Offline (Sem Rota/Link)" : "Desconectado";
                WanInternetStatusLabel.TextColor = Color.FromArgb("#EF4444");
            }
        });
    }

    private async void OnTriggerRouterDownloadClicked(object? sender, EventArgs e)
    {
        if (!_connManager.IsConnected)
        {
            await DisplayAlert("Aviso", "Conecte o console serial USB primeiro.", "OK");
            return;
        }

        var url = FirmwareUrlEntry.Text?.Trim();
        var fileName = FirmwareFileNameEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(fileName))
        {
            await DisplayAlert("Campos Obrigatórios", "Informe a URL do pacote e o nome do arquivo na flash.", "OK");
            return;
        }

        var confirm = await DisplayAlert("Confirmar Transferência",
            $"Deseja instruir o roteador a baixar '{fileName}' diretamente pelo link WAN?\n\nEsta operação transfere o arquivo direto para a flash e atualiza o comando de boot.",
            "SIM, EXECUTAR", "CANCELAR");

        if (!confirm) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var detected = _connManager.LastDetectionResult;
        var series = detected?.Series ?? DeviceSeries.Isr841;
        await ExecuteRouterDownloadAsync(series, url, fileName, cts.Token);
    }

    private async Task ExecuteRouterDownloadAsync(DeviceSeries series, string url, string fileName, CancellationToken ct)
    {
        TriggerRouterDownloadBtn.IsEnabled = false;
        AuditarFwBtn.IsEnabled = false;
        AppendLog("[*] Iniciando processo de download no roteador...");

        try
        {
            if (_connManager.CurrentSession == null)
            {
                await DisplayAlert("Aviso", "Sessão serial interativa não disponível.", "OK");
                return;
            }

            var updater = new RouterDirectFirmwareUpdater(msg =>
            {
                MainThread.BeginInvokeOnMainThread(() => AppendLog(msg));
                return Task.CompletedTask;
            });

            var ok = await updater.TriggerDownloadOnRouterAsync(_connManager.CurrentSession, series, url, fileName, ct);
            if (ok)
            {
                AppendLog("\n[✓] PROCESSO DE FIRMWARE FINALIZADO COM SUCESSO NO ROTEADOR!");
                ComplianceBadge.Text = "ATUALIZADO";
                ComplianceBadge.TextColor = Color.FromArgb("#4ADE80");
                await DisplayAlert("Sucesso", "Download concluído e gravado na flash do roteador!\nO boot system foi atualizado.", "OK");
            }
            else
            {
                AppendLog("\n[!] Transferência não confirmada pelo roteador. Verifique o log.");
                await DisplayAlert("Aviso", "O roteador não confirmou o término da transferência. Confira as mensagens no registro.", "OK");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"\n[X] Falha na operação: {ex.Message}");
            await DisplayAlert("Erro", ex.Message, "OK");
        }
        finally
        {
            TriggerRouterDownloadBtn.IsEnabled = true;
            AuditarFwBtn.IsEnabled = true;
        }
    }

    private string? _selectedLocalFirmwarePath;

    private void CheckEthernetStatus()
    {
        var ethManager = AndroidEthernetManager.Instance;
        var hasEth = ethManager.IsEthernetConnected();
        var ethIp = ethManager.GetEthernetIpAddress();

        if (hasEth)
        {
            EthStatusBadge.Text = string.IsNullOrWhiteSpace(ethIp) ? "🔌 ETH UP (Sem IP)" : $"🔌 ETH UP ({ethIp})";
            EthStatusBadge.TextColor = Color.FromArgb("#4ADE80");
            if (!string.IsNullOrWhiteSpace(ethIp) && string.IsNullOrWhiteSpace(PhoneEthIpEntry.Text))
            {
                PhoneEthIpEntry.Text = ethIp;
            }
        }
        else
        {
            EthStatusBadge.Text = "❌ ETH Desconectada";
            EthStatusBadge.TextColor = Color.FromArgb("#EF4444");
        }
    }

    private void OnRefreshEthernetClicked(object? sender, EventArgs e)
    {
        CheckEthernetStatus();
    }

    private async void OnSelectLocalFirmwareClicked(object? sender, EventArgs e)
    {
        try
        {
            var res = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Selecione o arquivo de Firmware (.bin, .ipe, .out)",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "application/octet-stream", "*/*" } }
                })
            });

            if (res != null)
            {
                _selectedLocalFirmwarePath = res.FullPath;
                var fi = new System.IO.FileInfo(res.FullPath);
                var sizeMb = fi.Exists ? (fi.Length / (1024.0 * 1024.0)) : 0.0;
                SelectedLocalFwLabel.Text = $"{res.FileName} ({sizeMb:F1} MB)";
                SelectedLocalFwLabel.TextColor = Color.FromArgb("#4ADE80");
                AppendLog($"[*] Firmware local selecionado: {res.FileName} ({sizeMb:F1} MB)");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro ao selecionar arquivo", ex.Message, "OK");
        }
    }

    private async void OnSendLocalFirmwareClicked(object? sender, EventArgs e)
    {
        if (!_connManager.IsConnected)
        {
            await DisplayAlert("Aviso", "Conecte o console serial USB na aba Ativação primeiro.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedLocalFirmwarePath) || !System.IO.File.Exists(_selectedLocalFirmwarePath))
        {
            await DisplayAlert("Aviso", "Selecione um arquivo de firmware local (.bin, .ipe ou .out) antes de enviar.", "OK");
            return;
        }

        var phoneIp = PhoneEthIpEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(phoneIp))
        {
            phoneIp = AndroidEthernetManager.Instance.GetEthernetIpAddress();
        }

        if (string.IsNullOrWhiteSpace(phoneIp))
        {
            var configure = await DisplayAlert("IP Ethernet Ausente",
                "A interface Ethernet do celular não possui um IP atribuído.\n\n" +
                "Em bancada com IP estático, configure o IP na interface Ethernet do Android (ou digite o IP do celular no campo acima).\n\nDeseja abrir as configurações de rede do Android?",
                "ABRIR CONFIGURAÇÕES", "CANCELAR");

            if (configure)
            {
                await AndroidEthernetManager.Instance.OpenEthernetSettingsAsync();
            }
            return;
        }

        var detected = _connManager.LastDetectionResult;
        var series = detected?.Series ?? DeviceSeries.Unknown;
        var isForti = detected?.Manufacturer == DeviceManufacturer.Fortinet;
        var isHpe = detected?.Manufacturer == DeviceManufacturer.Hpe;

        var confirm = await DisplayAlert("Transferência Local OTG + ETH",
            $"Deseja iniciar a transferência direta do firmware via cabo de rede?\n\n" +
            $"• Arquivo: {System.IO.Path.GetFileName(_selectedLocalFirmwarePath)}\n" +
            $"• IP Celular na LAN: {phoneIp}\n" +
            $"• Roteador: {detected?.Manufacturer} {series}\n\n" +
            $"O tráfego será vinculado estritamente à interface Ethernet OTG.",
            "SIM, ENVIAR AGORA", "CANCELAR");

        if (!confirm) return;

        SendLocalFwBtn.IsEnabled = false;
        AppendLog("[*] =================================================================");
        AppendLog("[*]       TRANSFERÊNCIA LOCAL DE FIRMWARE EM BANCADA (OTG + ETH)      ");
        AppendLog("[*] =================================================================");

        var ethBound = AndroidEthernetManager.Instance.BindProcessToEthernet(AppendLog);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));

        try
        {
            Func<string, Task> logger = msg =>
            {
                MainThread.BeginInvokeOnMainThread(() => AppendLog(msg));
                return Task.CompletedTask;
            };

            bool success = false;
            if (isHpe)
            {
                AppendLog($"[*] Iniciando transferência HPE via FTP embarcado (:2121) a partir de {phoneIp}...");
                success = await _connManager.UpgradeFirmwareHpeFtpAsync(
                    _selectedLocalFirmwarePath, phoneIp, "sparc", "claro123", logger, cts.Token);
            }
            else if (isForti)
            {
                AppendLog($"[*] Iniciando restauração FortiOS via FTP embarcado (:2121) a partir de {phoneIp}...");
                success = await _connManager.RestoreFirmwareFortiFtpAsync(
                    _selectedLocalFirmwarePath, phoneIp, "sparc", "claro123", logger, cts.Token);
            }
            else
            {
                // Cisco IOS
                var routerIp = _connManager.LoadedCircuit?.LanIp ?? "192.168.1.1";
                AppendLog($"[*] Iniciando transferência Cisco via HTTP embarcado (:8080) a partir de {phoneIp}...");
                success = await _connManager.UpgradeFirmwareCiscoHttpAsync(
                    _selectedLocalFirmwarePath,
                    phoneIp: phoneIp,
                    routerIp: routerIp,
                    telnetUser: "EBT",
                    telnetPass: "PRO1AN",
                    routerTempIp: null,
                    routerTempMask: null,
                    lanInterface: null,
                    expectedMd5: null,
                    progress: logger,
                    ct: cts.Token);
            }

            if (success)
            {
                AppendLog("\n[✓] FIRMWARE ENVIADO E GRAVADO NA FLASH COM SUCESSO VIA OTG + ETH!");
                await DisplayAlert("Sucesso", "Firmware enviado com sucesso via cabo de rede!", "OK");
            }
            else
            {
                AppendLog("\n[!] A transferência não foi confirmada pelo roteador. Verifique o log.");
                await DisplayAlert("Aviso", "O processo foi finalizado, mas o roteador não confirmou. Verifique o log.", "OK");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"\n[X] Falha na transferência local: {ex.Message}");
            await DisplayAlert("Erro na Transferência", ex.Message, "OK");
        }
        finally
        {
            if (ethBound)
            {
                AndroidEthernetManager.Instance.UnbindProcessFromNetwork(AppendLog);
            }
            SendLocalFwBtn.IsEnabled = true;
        }
    }

    private async Task PrepararEExecutarAtualizacaoLocalAsync(DeviceDetectionResult detected, RouterFirmwareStatus result, CancellationToken ct)
    {
        AppendLog("\n[*] =================================================================");
        AppendLog("[*]   PREPARANDO ATUALIZAÇÃO EM BANCADA VIA OTG + ETHERNET (LOCAL)   ");
        AppendLog("[*] =================================================================");

        // 1. Localiza ou baixa o firmware homologado
        var localFw = _firmwareRepo.GetLocalFirmware(detected.Series);
        if (localFw == null)
        {
            AppendLog($"[*] Firmware homologado não encontrado no armazenamento local. Consultando repositório...");
            try
            {
                var officialRemote = await _firmwareRepo.QueryRemoteForSeriesAsync(detected.Series, ct);
                if (officialRemote != null)
                {
                    AppendLog($"[*] Baixando {officialRemote.FileName} ({officialRemote.SizeBytes / (1024.0 * 1024.0):F1} MB) via conexão celular/Wi-Fi...");
                    SelectedLocalFwLabel.Text = $"Baixando: {officialRemote.FileName}...";
                    SelectedLocalFwLabel.TextColor = Color.FromArgb("#38BDF8");

                    var prog = new Progress<FirmwareDownloadProgress>(p =>
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            SelectedLocalFwLabel.Text = $"Baixando da nuvem: {p.Percentage:F0}% ({p.BytesReceived / 1048576.0:F1}/{p.TotalBytes / 1048576.0:F1} MB)";
                        });
                    });

                    var path = await _firmwareRepo.DownloadFirmwareAsync(officialRemote, prog, ct);
                    _selectedLocalFirmwarePath = path;
                    localFw = _firmwareRepo.GetLocalFirmware(detected.Series);
                }
            }
            catch (Exception exDl)
            {
                AppendLog($"[!] Falha ao baixar firmware da nuvem: {exDl.Message}");
            }
        }
        else
        {
            _selectedLocalFirmwarePath = localFw.LocalFilePath;
        }

        if (string.IsNullOrWhiteSpace(_selectedLocalFirmwarePath) || !File.Exists(_selectedLocalFirmwarePath))
        {
            await DisplayAlert("Firmware Ausente",
                $"O firmware homologado ({result.OfficialFirmwareName}) não está presente no celular.\n\n" +
                $"Toque em 'Selecionar Imagem' abaixo para escolher o arquivo (.bin, .ipe ou .out) da memória do celular.",
                "OK");
            return;
        }

        var fi = new FileInfo(_selectedLocalFirmwarePath);
        SelectedLocalFwLabel.Text = $"{fi.Name} ({fi.Length / (1024.0 * 1024.0):F1} MB)";
        SelectedLocalFwLabel.TextColor = Color.FromArgb("#4ADE80");
        AppendLog($"[✓] Imagem pronta: {fi.Name} ({fi.Length / (1024.0 * 1024.0):F1} MB)");

        // 2. Checa status da interface Ethernet
        CheckEthernetStatus();
        var ethManager = AndroidEthernetManager.Instance;
        if (!ethManager.IsEthernetConnected())
        {
            var conectou = await DisplayAlert("Conectar Cabo Ethernet",
                "Conecte o cabo de rede RJ45 do adaptador USB/ETH (no HUB USB-C) a uma porta LAN do roteador (ex: Gi0/0 ou Fe0/1).\n\n" +
                "Toque em OK após conectar o cabo para iniciar a transferência.",
                "CONECTADO, PROSSEGUIR", "CANCELAR");

            if (!conectou) return;
            CheckEthernetStatus();
        }

        // 3. Obtém ou solicita o IP do celular na Ethernet
        var phoneIp = PhoneEthIpEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(phoneIp))
        {
            phoneIp = ethManager.GetEthernetIpAddress();
        }

        if (string.IsNullOrWhiteSpace(phoneIp))
        {
            var ipSugerido = _connManager.LoadedCircuit?.HostLanIp ?? "192.168.1.2";
            var ipInput = await DisplayPromptAsync("IP do Celular na LAN",
                "Informe o endereço IP da interface Ethernet do celular (porta RJ45):\n(Deve estar na mesma sub-rede da porta LAN do roteador)",
                "OK", "CANCELAR",
                placeholder: ipSugerido,
                initialValue: ipSugerido);

            if (string.IsNullOrWhiteSpace(ipInput)) return;
            phoneIp = ipInput.Trim();
            PhoneEthIpEntry.Text = phoneIp;
        }

        // 4. Executa a transferência local
        OnSendLocalFirmwareClicked(this, EventArgs.Empty);
    }

    private void AppendLog(string message)
    {
        FwLogEditor.Text += message + Environment.NewLine;
    }
}
