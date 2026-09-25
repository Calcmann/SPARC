using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NetworkDevice.Protocols.Serial;

namespace NetworkDevice.UI;

public partial class FortiGateAutoRecoveryWindow : Window
{
    private static readonly Regex AnsiEscape = new(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])", RegexOptions.Compiled);
    private readonly string _porta;
    private readonly int _baud;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private SerialTransport? _activeTransport;
    private readonly bool _modoDiretoFormatacao;
    private bool _monitorando;
    private volatile bool _forcarPromptAtual;
    private volatile bool _forcarEnvioImediato;
    private volatile bool _interceptarBios;
    private volatile bool _forcarInterrupcaoBios;
    private volatile bool _biosMenuDetectado;
    private volatile bool _biosFormatRequested;
    private volatile bool _flashFormatada;

    public bool Sucesso { get; private set; }
    public string SerialCapturado => TxtSerialNumber.Text.Trim().ToUpperInvariant();

    public FortiGateAutoRecoveryWindow(string porta, int baud, string? initialSerial = null, bool modoDiretoFormatacao = true)
    {
        InitializeComponent();
        _porta = porta;
        _baud = baud;
        _modoDiretoFormatacao = modoDiretoFormatacao;

        if (!string.IsNullOrWhiteSpace(initialSerial))
        {
            TxtSerialNumber.Text = initialSerial.Trim().ToUpperInvariant();
            BorderSerialNumber.Visibility = Visibility.Visible;
        }

        if (_modoDiretoFormatacao)
        {
            Title = "SPARC — Quebra de Senha e Reset de Fábrica (FortiGate 40F)";
            TxtTituloCabecalho.Text = "FortiGate 40F — Quebra de Senha e Reset de Fábrica";
            TxtSubtituloCabecalho.Text = "Reinicie o 40F. Quando o LED Status piscar devagar, aperte RESET (~3s) até piscar rápido.";
            BorderAvisoFormatacao.Visibility = Visibility.Visible;
            BorderMetodosManuais.Visibility = Visibility.Collapsed;
            BorderBiosPanel.Visibility = Visibility.Collapsed;
            GridConsoleInput.Visibility = Visibility.Collapsed;
            BtnForcarPromptAtual.Visibility = Visibility.Collapsed;
            Height = Math.Min(600, Math.Max(460, SystemParameters.WorkArea.Height - 30));
            _interceptarBios = false;
            _biosFormatRequested = false;
        }
        else
        {
            Title = "SPARC — Painel Avançado de Bootloader (FortiGate 40F)";
            TxtTituloCabecalho.Text = "FortiGate 40F — Painel Avançado de Bootloader e Recuperação";
            TxtSubtituloCabecalho.Text = "Controle total da BIOS serial, seleção de partição de boot, formatação e console interativo.";
            BorderAvisoFormatacao.Visibility = Visibility.Collapsed;
            BorderMetodosManuais.Visibility = Visibility.Visible;
            BorderBiosPanel.Visibility = Visibility.Visible;
            GridConsoleInput.Visibility = Visibility.Visible;
            BtnForcarPromptAtual.Visibility = Visibility.Visible;
            Height = Math.Min(640, Math.Max(480, SystemParameters.WorkArea.Height - 30));
            _interceptarBios = false;
            _biosFormatRequested = false;
        }

        Loaded += async (s, e) =>
        {
            try
            {
                var workArea = SystemParameters.WorkArea;
                MaxHeight = workArea.Height;
                MaxWidth = workArea.Width;
                if (Height > workArea.Height - 20)
                    Height = Math.Max(MinHeight, workArea.Height - 20);
                if (Width > workArea.Width - 20)
                    Width = Math.Max(MinWidth, workArea.Width - 20);
            }
            catch { }

            if (!_monitorando)
            {
                _monitorando = true;
                await IniciarMonitoramentoAutonomoAsync();
            }
        };

        Closing += (s, e) =>
        {
            _cts.Cancel();
        };
    }

    private void TxtSerialNumber_TextChanged(object sender, TextChangedEventArgs e)
    {
        var caret = TxtSerialNumber.CaretIndex;
        var upper = TxtSerialNumber.Text.ToUpperInvariant();
        if (TxtSerialNumber.Text != upper)
        {
            TxtSerialNumber.Text = upper;
            TxtSerialNumber.CaretIndex = caret;
        }
    }

    private void AppendLog(string text)
    {
        Dispatcher.Invoke(() =>
        {
            TxtTerminalLog.AppendText(text + "\n");
            TxtTerminalLog.ScrollToEnd();
        });
    }

    private void AppendRawConsole(string text)
    {
        Dispatcher.Invoke(() =>
        {
            TxtTerminalLog.AppendText(text);
            TxtTerminalLog.ScrollToEnd();
        });
    }

    private void SetStatus(string message, double percent, Brush? brush = null)
    {
        Dispatcher.Invoke(() =>
        {
            TxtStatusBadge.Text = message;
            if (brush != null) TxtStatusBadge.Foreground = brush;
            ProgressBarRecovery.Value = percent;
            TxtPercentBadge.Text = $"{(int)percent}%";
        });
    }

    private void UpdateBiosUiState(bool menuAtivo, string statusText, Brush? statusColor = null)
    {
        Dispatcher.Invoke(() =>
        {
            _biosMenuDetectado = menuAtivo;
            BtnBiosBootBackup.IsEnabled = menuAtivo;
            BtnBiosFormat.IsEnabled = menuAtivo;
            BtnBiosSysInfo.IsEnabled = menuAtivo;
            BtnBiosQuit.IsEnabled = menuAtivo;

            TxtBiosStatus.Text = statusText;
            if (statusColor != null)
                TxtBiosStatus.Foreground = statusColor;
        });
    }

    private async Task SendSerialAsync(string data, bool echo = false)
    {
        if (_activeTransport == null || !_activeTransport.IsOpen)
            return;

        await _writeLock.WaitAsync(_cts.Token);
        try
        {
            var bytes = Encoding.ASCII.GetBytes(data);
            await _activeTransport.WriteAsync(bytes, _cts.Token);
            if (echo)
            {
                AppendRawConsole(data);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[!] Erro ao enviar para serial: {ex.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task IniciarMonitoramentoAutonomoAsync()
    {
        try
        {
            AppendLog($"[*] [SPARC AUTÔNOMO] Abrindo porta {_porta} @ {_baud} baud...");
            SetStatus("🔌 Reinicie o 40F. Quando LED STATUS piscar devagar, segure RESET (~3s) até piscar rápido...", 15, Brushes.Gold);

            _activeTransport = new SerialTransport(_porta, _baud, readTimeout: TimeSpan.FromMilliseconds(120));
            await _activeTransport.OpenAsync(_cts.Token);
            AppendLog($"[OK] Porta {_porta} monitorada com sucesso.");

            if (_modoDiretoFormatacao)
            {
                AppendLog("\n=================================================================");
                AppendLog("⚠️ PROCEDIMENTO DE RESET E QUEBRA DE SENHA — FORTIGATE 40F");
                AppendLog("   1. Reinicie o equipamento (desligue e ligue a fonte de energia).");
                AppendLog("   2. Aguarde a inicialização: observe o LED 'STATUS' no painel frontal.");
                AppendLog("   3. Quando o LED STATUS começar a piscar devagar, aperte e segure o botão no furo 'RESET' (traseira).");
                AppendLog("   4. Segure o botão reset (~3s) até o LED STATUS começar a piscar rápido, e então solte.");
                AppendLog("   5. O roteador detectará o reset físico e reiniciará com as configurações de fábrica.");
                AppendLog("   6. Quebra de senha realizada: a senha retornou à default. Siga com o processo padrão de provisionamento.");
                AppendLog("=================================================================\n");
            }
            else
            {
                AppendLog("Mantenha a janela aberta e reinicie a alimentação física do aparelho para acessar a BIOS ou recuperar acesso.");
            }

            var rxBuffer = new byte[2048];
            var accumulator = new StringBuilder();
            var ct = _cts.Token;

            var bootDetected = false;
            var loginSent = false;
            var passwordSent = false;
            var passwordResetSent = false;
            var promptVelhoAvisado = false;

            var swMaintainerWait = new System.Diagnostics.Stopwatch();
            var maintainerRetries = 0;
            var swPasswordWait = new System.Diagnostics.Stopwatch();
            var passwordRetries = 0;

            // Envia \r\n suave para testar se console já está ativo
            try { await SendSerialAsync("\r\n"); } catch { }

            while (!ct.IsCancellationRequested && !Sucesso)
            {
                if (_forcarEnvioImediato)
                {
                    _forcarEnvioImediato = false;
                    loginSent = false;
                    passwordSent = false;
                    bootDetected = true;
                    accumulator.Clear();
                    try { await SendSerialAsync("\r\n"); } catch { }
                }

                if (_forcarInterrupcaoBios)
                {
                    _forcarInterrupcaoBios = false;
                    AppendLog("[*] Enviando sinal de interrupção (Espaços/Enter) para a BIOS...");
                    try
                    {
                        await SendSerialAsync("   \r\n");
                    }
                    catch { }
                }

                int bytesRead = 0;
                try
                {
                    bytesRead = await _activeTransport.ReadAsync(rxBuffer, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppendLog($"[!] Erro de leitura serial: {ex.Message}");
                    await Task.Delay(200, ct);
                    continue;
                }

                if (bytesRead > 0)
                {
                    var chunk = Encoding.UTF8.GetString(rxBuffer, 0, bytesRead).Replace("\uFFFD", "");
                    accumulator.Append(chunk);
                    AppendRawConsole(chunk);
                }

                var current = accumulator.ToString();
                if (string.IsNullOrWhiteSpace(current))
                {
                    await Task.Delay(50, ct);
                    continue;
                }

                var clean = AnsiEscape.Replace(current, "").Trim();

                // 1. Detecção automática de Número de Série no stream de boot
                if (string.IsNullOrWhiteSpace(TxtSerialNumber.Text))
                {
                    var matchSn = Regex.Match(current, @"(?i)(?:Serial\s*number|S/N|SN)\s*[:=]?\s*([A-Z0-9]{16})");
                    if (!matchSn.Success)
                    {
                        matchSn = Regex.Match(current, @"\b(FGT40F[A-Z0-9]{10})\b");
                    }
                    if (matchSn.Success)
                    {
                        var snFound = matchSn.Groups[1].Value.ToUpperInvariant();
                        Dispatcher.Invoke(() =>
                        {
                            TxtSerialNumber.Text = snFound;
                            BorderSerialNumber.Visibility = Visibility.Visible;
                        });
                        AppendLog($"\n[>] Serial Number detectado automaticamente no console: {snFound}");
                    }
                }

                // 2. Confirmação de Formatação de Flash [yes/no] ou [Y/N]
                // Prompt: "It will erase data in boot device. Continue? [yes/no]:"
                bool isEraseConfirmation = current.Contains("erase data in boot device", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("[yes/no]", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("Continue? [yes/no]", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("(y/n)", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("format boot device", StringComparison.OrdinalIgnoreCase);

                if (isEraseConfirmation)
                {
                    if (_biosFormatRequested || _modoDiretoFormatacao)
                    {
                        _biosFormatRequested = false;
                        _flashFormatada = true;
                        AppendLog("\n[OK] Confirmação da BIOS detectada ('Continue? [yes/no]'). Enviando 'yes'...");
                        SetStatus("🧹 Confirmando formatação da flash ('yes')...", 50, Brushes.Orange);
                        accumulator.Clear();
                        await Task.Delay(250, ct);
                        await SendSerialAsync("yes\r");
                        SetStatus("🧹 Memória flash sendo formatada... Aguarde a conclusão.", 55, Brushes.Orange);
                        continue;
                    }
                }

                // 3. Detecção e Interrupção do Bootloader (BIOS)
                // Prompt clássico: "Press any key to display configuration menu..."
                if (current.Contains("Press any key", StringComparison.OrdinalIgnoreCase))
                {
                    if (_interceptarBios || _forcarInterrupcaoBios)
                    {
                        _forcarInterrupcaoBios = false;
                        AppendLog("\n[⚡ BOOTLOADER] Detectada janela de interrupção da BIOS! Enviando teclas...");
                        SetStatus("🛠️ Interrompendo bootloader da BIOS...", 30, Brushes.Gold);
                        UpdateBiosUiState(false, "Interrompendo BIOS...", Brushes.Gold);

                        // Envia espaços consecutivos e quebras de linha para garantir a captura
                        await SendSerialAsync("   \r\n");
                        await Task.Delay(200, ct);
                        await SendSerialAsync(" ");
                        accumulator.Clear();
                        continue;
                    }
                    else
                    {
                        UpdateBiosUiState(false, "Janela de interrupção aberta (3s)", Brushes.LightSalmon);
                    }
                }

                // 4. Detecção do Menu da BIOS ativo (exibe opções [C], [F], [B], [Q], etc.)
                // Exemplos: "Enter Selection:", "Enter C,R,T,F,I,B,Q,or H:"
                bool isBiosMenuPrompt = current.Contains("Enter Selection:", StringComparison.OrdinalIgnoreCase) ||
                                        current.Contains("Enter C,R,T,F,I,B,Q,or H:", StringComparison.OrdinalIgnoreCase) ||
                                        Regex.IsMatch(current, @"(?i)Enter\s+.*[C,R,T,F,I,B,Q,H].*:") ||
                                        (current.Contains("[B]:", StringComparison.OrdinalIgnoreCase) &&
                                         current.Contains("[F]:", StringComparison.OrdinalIgnoreCase));

                if (isBiosMenuPrompt)
                {
                    if (_modoDiretoFormatacao)
                    {
                        if (!_flashFormatada)
                        {
                            _biosFormatRequested = true;
                            AppendLog("\n[🧹 FORMATAÇÃO FLASH] Menu da BIOS detectado. Enviando [F] para formatar flash e apagar configurações...");
                            SetStatus("🧹 Formatando flash (apagando configurações anteriores)...", 45, Brushes.Orange);
                            accumulator.Clear();
                            await Task.Delay(250, ct);
                            await SendSerialAsync("F\r");
                            continue;
                        }
                        else
                        {
                            AppendLog("\n[▶️ CONTINUAR BOOT] Saindo do menu da BIOS para boot do FortiOS pós-formatação ('Q')...");
                            SetStatus("⚙️ Memória limpa. Iniciando boot do FortiOS...", 65, Brushes.Gold);
                            accumulator.Clear();
                            await Task.Delay(250, ct);
                            await SendSerialAsync("Q\r");
                            continue;
                        }
                    }
                    else if (!_biosMenuDetectado)
                    {
                        UpdateBiosUiState(true, "Menu BIOS Ativo", Brushes.LightGreen);
                        SetStatus("🛠️ Menu da BIOS acessado! Selecione uma opção no painel acima.", 35, Brushes.Cyan);
                        AppendLog("\n[OK] Menu da BIOS / Bootloader ativo no console serial!");
                        AppendLog("     Você pode usar os botões acima ([B] Boot Backup, [F] Formatar Flash, [Q] Continuar)");
                        AppendLog("     ou digitar comandos diretamente na linha de entrada do console.");
                    }
                }

                    // 3. Detecção de Boot do equipamento (reboot físico, kernel linux, u-boot, mmcblk)
                    bool bootMsgDetected = current.Contains("Scanning /dev/", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("mmcblk", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("Verifying Checksum", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("Verifying image", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("U-Boot", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("Reading boot image", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("System is starting", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("FortiOS kernel", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("Booting", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("Linux version", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("INIT:", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("Starting system", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("runlevel", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("Press any key", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("Loading kernel", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("unpacking", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("EXT4-fs", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("squashfs", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("formatting", StringComparison.OrdinalIgnoreCase);

                    if (bootMsgDetected)
                    {
                        if (_biosMenuDetectado)
                        {
                            UpdateBiosUiState(false, "Boot do FortiOS em andamento...", Brushes.MediumSlateBlue);
                        }

                        if (!bootDetected)
                        {
                            bootDetected = true;
                            if (_modoDiretoFormatacao)
                            {
                                SetStatus("⚙️ Boot detectado! Segure o botão RESET traseiro até o LED STATUS piscar rápido!", 50, Brushes.Gold);
                                AppendLog("\n[>] Boot do FortiGate 40F em andamento!");
                                AppendLog("    >> AÇÃO: Segure o botão 'RESET' (traseira) com clipe até o LED 'STATUS' piscar rapidamente.");
                            }
                            else
                            {
                                SetStatus("⚙️ Boot do FortiGate 40F detectado! Aguardando término da inicialização...", 40, Brushes.Gold);
                                AppendLog("\n[>] Boot do FortiOS em andamento! O SPARC interceptará o login assim que estiver pronto.");
                            }
                        }

                        // Se ainda estamos recebendo mensagens de boot, o sistema ainda está carregando.
                        // Limpa o acumulador para descartar qualquer prompt antigo que estivesse no buffer.
                        loginSent = false;
                        passwordSent = false;
                        maintainerRetries = 0;
                        accumulator.Clear();
                        continue;
                    }

                    // 4. Detecção de Prompt de Login (FortiGate-40F login:)
                    if (!loginSent && Regex.IsMatch(current, @"(?i)(?:FortiGate|FGT)[A-Za-z0-9_\-]*\s+login\s*[:?]"))
                    {
                        // Se ainda NÃO detectou o reinício (boot) do equipamento e o operador não forçou
                        if (!bootDetected && !_forcarPromptAtual)
                        {
                            if (!promptVelhoAvisado)
                            {
                                promptVelhoAvisado = true;
                                SetStatus("🔌 Roteador ligado. Desligue e ligue a fonte para iniciar o reset físico!", 20, Brushes.Gold);
                                AppendLog("\n[*] Terminal em espera: Por favor, reinicie a fonte do FortiGate 40F.");
                                AppendLog("    >> Quando o LED STATUS começar a piscar, aperte e segure o botão RESET traseiro!");
                            }
                            // Não tenta login: aguarda o usuário reiniciar o aparelho!
                            accumulator.Clear();
                            await Task.Delay(200, ct);
                            continue;
                        }

                        // Boot ocorreu ou o operador forçou o teste: agora sim valida se o reset de fábrica funcionou
                        loginSent = true;
                        _forcarPromptAtual = false;

                        SetStatus("🔑 Boot concluído! Validando se o reset foi aplicado: enviando 'admin'...", 75, Brushes.Cyan);
                        AppendLog("\n[>] Prompt de login pós-boot detectado!");
                        AppendLog("    >> Validando se o reset de fábrica funcionou (enviando 'admin' com senha em branco)...");
                        accumulator.Clear();
                        await Task.Delay(300, ct);
                        await SendSerialAsync("admin\r");
                        swMaintainerWait.Restart();
                        maintainerRetries = 1;
                    }

                    // 5. Detecção do Prompt de Senha
                    if (loginSent && !passwordSent)
                    {
                        if (Regex.IsMatch(current, @"(?i)Password\s*[:?]"))
                        {
                            passwordSent = true;
                            SetStatus("🔑 Enviando senha em branco de fábrica...", 85, Brushes.LightGreen);
                            AppendLog("[>] Enviando confirmação de senha em branco (padrão de fábrica)...");
                            accumulator.Clear();
                            await Task.Delay(150, ct);
                            await SendSerialAsync("\r");
                            swPasswordWait.Restart();
                            passwordRetries = 1;
                            continue;
                        }
                        else if (swMaintainerWait.ElapsedMilliseconds > 2000 && maintainerRetries < 4)
                        {
                            maintainerRetries++;
                            swMaintainerWait.Restart();
                            await SendSerialAsync("\r");
                            await Task.Delay(100, ct);
                            await SendSerialAsync("admin\r");
                            continue;
                        }
                    }

                    // 6. Watchdog de Senha enviada
                    if (loginSent && passwordSent && !passwordResetSent)
                    {
                        if (swPasswordWait.ElapsedMilliseconds > 2500 && passwordRetries < 3 &&
                            !current.Contains("Login incorrect", StringComparison.OrdinalIgnoreCase) &&
                            !current.Contains("Login failed", StringComparison.OrdinalIgnoreCase))
                        {
                            passwordRetries++;
                            swPasswordWait.Restart();
                            await SendSerialAsync("\r");
                            continue;
                        }
                    }

                    // 7. Falha de login (Reset físico NÃO foi acionado a tempo)
                    if (loginSent && passwordSent && !passwordResetSent &&
                        (current.Contains("Login incorrect", StringComparison.OrdinalIgnoreCase) ||
                         current.Contains("Login failed", StringComparison.OrdinalIgnoreCase)))
                    {
                        AppendLog("\n=================================================================");
                        AppendLog("⚠️ RESET FÍSICO NÃO DETECTADO — A SENHA ANTIGA AINDA ESTÁ ATIVA.");
                        AppendLog("   O botão RESET não foi mantido pressionado a tempo ou foi solto antes de piscar rápido.");
                        AppendLog("   >> AÇÃO: Desligue e ligue o cabo de energia da fonte do FortiGate 40F.");
                        AppendLog("   >> Assim que o LED STATUS começar a piscar, segure o RESET até piscar rápido!");
                        AppendLog("=================================================================\n");

                        SetStatus("⚠️ Reset não detectado. Reinicie o 40F e segure o RESET até o LED piscar rápido!", 25, Brushes.Gold);

                        loginSent = false;
                        passwordSent = false;
                        bootDetected = false;
                        accumulator.Clear();
                        await Task.Delay(400, ct);
                        await SendSerialAsync("\r\n");
                        continue;
                    }

                    // 8. Confirmação de Sucesso: Login com Senha em Branco (Padrão de Fábrica)
                    // Se o FortiOS pediu troca de senha (forced to change your password / New Password:)
                    // ou exibiu Welcome! ou prompt operacional (# ou $), o login de fábrica foi aceito com sucesso!
                    bool isStrictCliPrompt = Regex.IsMatch(clean, @"(?m)(?:^|[\r\n])(?:FortiGate|FGT|[A-Za-z0-9_\-]{3,30})\s*(?:\([^()\r\n]*\))?\s*[#$]\s*$");
                    bool hasWelcomeOrCli = clean.Contains("Welcome!") || clean.EndsWith("#") || clean.EndsWith("$") || isStrictCliPrompt;
                    bool hasForcedPasswordChange = current.Contains("forced to change your password", StringComparison.OrdinalIgnoreCase) ||
                                                   current.Contains("New Password", StringComparison.OrdinalIgnoreCase) ||
                                                   current.Contains("Please change your password", StringComparison.OrdinalIgnoreCase) ||
                                                   current.Contains("Please input a new password", StringComparison.OrdinalIgnoreCase);

                    bool loginIncorrect = current.Contains("Login incorrect", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("Login failed", StringComparison.OrdinalIgnoreCase);

                    bool factoryDefaultConfirmed = !bootMsgDetected && !loginIncorrect && (hasForcedPasswordChange || hasWelcomeOrCli);

                    if (loginSent && passwordSent && !passwordResetSent && factoryDefaultConfirmed)
                    {
                        passwordResetSent = true;
                        accumulator.Clear();

                        AppendLog("\n=================================================================");
                        AppendLog("✅ [SUCESSO] RESET DE FÁBRICA CONFIRMADO!");
                        AppendLog("   O FortiGate 40F aceitou a credencial padrão de fábrica ('admin' sem senha).");
                        AppendLog("   A senha anterior foi removida com sucesso!");
                        AppendLog("   O processo padrão do SPARC assumirá todas as configurações.");
                        AppendLog("");
                        AppendLog("👉 PRÓXIMO PASSO:");
                        AppendLog("   Clique no botão destacado abaixo para seguir com o provisionamento padrão");
                        AppendLog("   (carregamento dos dados do circuito / Ficha SAIP e Firmware).");
                        AppendLog("=================================================================\n");

                        SetStatus("✅ Senha em branco (padrão de fábrica) confirmada! Siga com o processo padrão.", 100, Brushes.Lime);

                        // Envia cancelamento suave (Ctrl+C) para deixar a console no prompt de login limpo para a esteira
                        try { await SendSerialAsync("\x03\r\n"); } catch { }

                        Sucesso = true;
                        AtivarDestaquePiscanteBotaoConcluir();
                        break;
                    }


                    await Task.Delay(60, ct);
                }
            }
        catch (OperationCanceledException)
        {
            AppendLog("\n[*] Monitoramento cancelado pelo operador.");
        }
        catch (Exception ex)
        {
            AppendLog($"\n[!] Erro durante a recuperação autônoma: {ex.Message}");
            SetStatus($"❌ Erro: {ex.Message}", 0, Brushes.Red);
        }
        finally
        {
            if (_activeTransport != null)
            {
                try { await _activeTransport.DisposeAsync(); } catch { }
                _activeTransport = null;
            }
        }
    }

    #region Bootloader Handlers
    private void ChkInterceptBios_Checked(object sender, RoutedEventArgs e)
    {
        _interceptarBios = true;
        AppendLog("[*] Interrupção automática da BIOS ATIVADA. Ao religar o aparelho, o SPARC parará no menu de boot.");
        TxtBiosStatus.Text = "Interrupção ativada (Aguardando boot)";
        TxtBiosStatus.Foreground = Brushes.Gold;
    }

    private void ChkInterceptBios_Unchecked(object sender, RoutedEventArgs e)
    {
        _interceptarBios = false;
        AppendLog("[*] Interrupção automática da BIOS desativada. O boot prosseguirá direto para o FortiOS.");
        TxtBiosStatus.Text = "Bootloader inativo";
        TxtBiosStatus.Foreground = Brushes.SlateGray;
    }

    private async void BtnInterromperBiosAgora_Click(object sender, RoutedEventArgs e)
    {
        _forcarInterrupcaoBios = true;
        _interceptarBios = true;
        ChkInterceptBios.IsChecked = true;
        AppendLog("[*] Interrupção da BIOS forçada pelo operador. Enviando comandos...");
        await SendSerialAsync("   \r\n");
    }

    private async void BtnBiosBootBackup_Click(object sender, RoutedEventArgs e)
    {
        AppendLog("[*] [BIOS] Selecionada opção [B]: Boot com firmware de backup e definir como padrão.");
        UpdateBiosUiState(false, "Iniciando firmware de backup...", Brushes.DeepSkyBlue);
        await SendSerialAsync("B\r");
    }

    private async void BtnBiosFormat_Click(object sender, RoutedEventArgs e)
    {
        var resp = MessageBox.Show(
            "ATENÇÃO: A formatação da memória flash apagará as partições e configurações do FortiGate 40F.\n\nDeseja realmente prosseguir com a formatação via Bootloader?",
            "Confirmação de Formatação (Bootloader)",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (resp == MessageBoxResult.Yes)
        {
            _biosFormatRequested = true;
            AppendLog("[*] [BIOS] Selecionada opção [F]: Formatar memória flash (boot device)...");
            UpdateBiosUiState(false, "Formatando flash...", Brushes.Tomato);
            await SendSerialAsync("F\r");
        }
    }

    private async void BtnBiosSysInfo_Click(object sender, RoutedEventArgs e)
    {
        AppendLog("[*] [BIOS] Selecionada opção [I]: Informações do Sistema.");
        await SendSerialAsync("I\r");
    }

    private async void BtnBiosQuit_Click(object sender, RoutedEventArgs e)
    {
        AppendLog("[*] [BIOS] Selecionada opção [Q]: Saindo do menu e continuando boot do FortiOS.");
        UpdateBiosUiState(false, "Continuando boot do FortiOS...", Brushes.SlateBlue);
        await SendSerialAsync("Q\r");
    }
    #endregion

    #region Interactive Console Handlers
    private async void TxtConsoleInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await EnviarTextoConsoleAsync();
        }
    }

    private async void BtnSendConsole_Click(object sender, RoutedEventArgs e)
    {
        await EnviarTextoConsoleAsync();
    }

    private async Task EnviarTextoConsoleAsync()
    {
        var cmd = TxtConsoleInput.Text;
        TxtConsoleInput.Clear();
        await SendSerialAsync(cmd + "\r", echo: false);
    }
    #endregion

    #region Action Buttons
    private void BtnForcarPromptAtual_Click(object sender, RoutedEventArgs e)
    {
        AppendLog("[*] Operador solicitou envio imediato no prompt atual.");
        _forcarPromptAtual = true;
        _forcarEnvioImediato = true;
    }

    private void BtnConcluir_Click(object sender, RoutedEventArgs e)
    {
        Sucesso = true;
        DialogResult = true;
        Close();
    }

    private void AtivarDestaquePiscanteBotaoConcluir()
    {
        Dispatcher.Invoke(() =>
        {
            BtnForcarPromptAtual.Visibility = Visibility.Collapsed;
            BtnConcluir.Content = "👉 SEGUIR COM CONFIGURAÇÃO PADRÃO ➔";
            BtnConcluir.FontSize = 14;
            BtnConcluir.FontWeight = FontWeights.Black;
            BtnConcluir.Padding = new Thickness(22, 10, 22, 10);
            BtnConcluir.Background = UiBrushes.Get("#15803D");
            BtnConcluir.Foreground = Brushes.White;
            BtnConcluir.BorderBrush = UiBrushes.Get("#FDE047");
            BtnConcluir.BorderThickness = new Thickness(2.5);
            BtnConcluir.IsEnabled = true;
            BtnConcluir.Focus();

            var animOpacity = new DoubleAnimation
            {
                From = 1.0,
                To = 0.35,
                Duration = TimeSpan.FromMilliseconds(550),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            BtnConcluir.BeginAnimation(UIElement.OpacityProperty, animOpacity);
        });
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        DialogResult = false;
        Close();
    }
    #endregion
}
