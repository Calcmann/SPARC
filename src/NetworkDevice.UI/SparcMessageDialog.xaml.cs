using System;
using System.Windows;
using System.Windows.Media;

namespace NetworkDevice.UI;

/// <summary>
/// Caixa de diálogo modal padronizada no Design System do SPARC (estilo dark, cards,
/// fontes Segoe UI, badges com ícones e botões idênticos à janela de diagnóstico WAN / Modo Bancada).
/// </summary>
public partial class SparcMessageDialog : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;
    private MessageBoxButton _buttons = MessageBoxButton.OK;

    public SparcMessageDialog()
    {
        InitializeComponent();
    }

    #region Overloads compatíveis com System.Windows.MessageBox.Show

    public static MessageBoxResult Show(string messageBoxText) =>
        Show(null, messageBoxText, "SPARC — Notificação", MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string messageBoxText, string caption) =>
        Show(null, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button) =>
        Show(null, messageBoxText, caption, button, MessageBoxImage.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) =>
        Show(null, messageBoxText, caption, button, icon);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult) =>
        Show(null, messageBoxText, caption, button, icon);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options) =>
        Show(null, messageBoxText, caption, button, icon);

    public static MessageBoxResult Show(Window? owner, string messageBoxText) =>
        Show(owner, messageBoxText, "SPARC — Notificação", MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption) =>
        Show(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton button) =>
        Show(owner, messageBoxText, caption, button, MessageBoxImage.None);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon) =>
        Show(owner, messageBoxText, caption, button, icon, null, null, null);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult) =>
        Show(owner, messageBoxText, caption, button, icon, null, null, null);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options) =>
        Show(owner, messageBoxText, caption, button, icon, null, null, null);

    #endregion

    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string title = "SPARC — Instrução ao Operador",
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.Information,
        string? subtitle = null,
        string? customOkText = null,
        string? customCancelText = null)
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => Show(owner, message, title, buttons, icon, subtitle, customOkText, customCancelText));
        }

        var dlg = new SparcMessageDialog();

        var effectiveOwner = owner ?? Application.Current?.MainWindow;
        if (effectiveOwner != null && effectiveOwner.IsVisible && effectiveOwner != dlg)
        {
            dlg.Owner = effectiveOwner;
        }

        dlg.Configurar(message, title, buttons, icon, subtitle, customOkText, customCancelText);
        dlg.ShowDialog();
        return dlg.Result;
    }

    public void Configurar(
        string message,
        string title,
        MessageBoxButton buttons,
        MessageBoxImage icon,
        string? subtitle,
        string? customOkText,
        string? customCancelText)
    {
        _buttons = buttons;
        Title = string.IsNullOrWhiteSpace(title) ? "SPARC — Instrução ao Operador" : title;
        TxtTituloPrincipal.Text = Title;
        TxtMessage.Text = message ?? string.Empty;

        // Subtítulo contextual didático
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            TxtSubHeader.Text = subtitle;
            TxtSubHeader.Visibility = Visibility.Visible;
        }
        else
        {
            var msgUpper = (message ?? "").ToUpperInvariant();
            var titleUpper = (title ?? "").ToUpperInvariant();

            if (titleUpper.Contains("WAN") || msgUpper.Contains("PORTA WAN") || titleUpper.Contains("CABO") || msgUpper.Contains("CONECTE") || msgUpper.Contains("TROCA"))
            {
                TxtSubHeader.Text = "Instrução de conexão e alinhamento de portas físicas";
                TxtSubHeader.Visibility = Visibility.Visible;
            }
            else if (titleUpper.Contains("REINICIE") || msgUpper.Contains("ZERAMENTO") || titleUpper.Contains("PERDA DE DADOS"))
            {
                TxtSubHeader.Text = "Atenção necessária antes da continuidade do procedimento";
                TxtSubHeader.Visibility = Visibility.Visible;
            }
            else if (titleUpper.Contains("BANCADA") || msgUpper.Contains("BANCADA"))
            {
                TxtSubHeader.Text = "Ambiente de laboratório / pré-configuração sem link da operadora";
                TxtSubHeader.Visibility = Visibility.Visible;
            }
            else if (titleUpper.Contains("BOOT-LOADER") || msgUpper.Contains("BOOT-LOADER") || titleUpper.Contains("FIRMWARE"))
            {
                TxtSubHeader.Text = "Auditoria e atualização de software operacional / bootloader";
                TxtSubHeader.Visibility = Visibility.Visible;
            }
            else
            {
                TxtSubHeader.Text = "Interação necessária para continuidade do fluxo";
                TxtSubHeader.Visibility = Visibility.Visible;
            }
        }

        // Configuração de Badge e Paleta Visual (seguindo fielmente o estilo WanDiagnosticsWindow)
        AplicarTemaVisual(title, message, icon);

        // Configuração dos Botões
        ConfigurarBotoes(buttons, customOkText, customCancelText);
    }

    private void AplicarTemaVisual(string? title, string? message, MessageBoxImage icon)
    {
        var textCombined = ((title ?? "") + " " + (message ?? "")).ToUpperInvariant();

        if (icon == MessageBoxImage.Error || icon == MessageBoxImage.Hand || icon == MessageBoxImage.Stop || textCombined.Contains("ERRO CRÍTICO") || textCombined.Contains("FALHA"))
        {
            BadgeIconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#450A0A"));
            TxtIcon.Text = "🚨";
            TxtTituloPrincipal.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171"));
        }
        else if (icon == MessageBoxImage.Warning || icon == MessageBoxImage.Exclamation || textCombined.Contains("ATENÇÃO") || textCombined.Contains("AVISO") || textCombined.Contains("PERDA DE DADOS"))
        {
            BadgeIconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#78350F"));
            TxtIcon.Text = "⚠️";
            TxtTituloPrincipal.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24"));
        }
        else if (textCombined.Contains("BANCADA"))
        {
            BadgeIconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#451A03"));
            TxtIcon.Text = "🔬";
            TxtTituloPrincipal.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24"));
        }
        else if (icon == MessageBoxImage.Question || _buttons == MessageBoxButton.YesNo || _buttons == MessageBoxButton.YesNoCancel || textCombined.Contains("DESEJA") || textCombined.Contains("CONFIRMAR"))
        {
            BadgeIconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A"));
            TxtIcon.Text = "❓";
            TxtTituloPrincipal.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60A5FA"));
        }
        else if (textCombined.Contains("CABO") || textCombined.Contains("PORTA") || textCombined.Contains("GE0") || textCombined.Contains("WAN") || textCombined.Contains("LAN") || textCombined.Contains("CONECTE"))
        {
            BadgeIconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#064E3B"));
            TxtIcon.Text = "🔌";
            TxtTituloPrincipal.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
        }
        else if (textCombined.Contains("SUCESSO") || textCombined.Contains("CONCLUÍD") || textCombined.Contains("PRONTO") || textCombined.Contains("SALVO"))
        {
            BadgeIconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#064E3B"));
            TxtIcon.Text = "✅";
            TxtTituloPrincipal.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
        }
        else
        {
            BadgeIconBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0C4A6E"));
            TxtIcon.Text = "📢";
            TxtTituloPrincipal.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"));
        }
    }

    private void ConfigurarBotoes(MessageBoxButton buttons, string? customOkText, string? customCancelText)
    {
        BtnOk.Visibility = Visibility.Collapsed;
        BtnYes.Visibility = Visibility.Collapsed;
        BtnNo.Visibility = Visibility.Collapsed;
        BtnCancel.Visibility = Visibility.Collapsed;

        BtnOk.IsDefault = false;
        BtnYes.IsDefault = false;
        BtnCancel.IsCancel = false;
        BtnNo.IsCancel = false;

        switch (buttons)
        {
            case MessageBoxButton.OK:
                BtnOk.Content = customOkText ?? "OK (Continuar)";
                BtnOk.Visibility = Visibility.Visible;
                BtnOk.IsDefault = true;
                break;

            case MessageBoxButton.OKCancel:
                BtnCancel.Content = customCancelText ?? "Cancelar";
                BtnCancel.Visibility = Visibility.Visible;
                BtnCancel.IsCancel = true;

                BtnOk.Content = customOkText ?? "OK (Continuar)";
                BtnOk.Visibility = Visibility.Visible;
                BtnOk.IsDefault = true;
                break;

            case MessageBoxButton.YesNo:
                BtnNo.Content = customCancelText ?? "Não";
                BtnNo.Visibility = Visibility.Visible;
                BtnNo.IsCancel = true;

                BtnYes.Content = customOkText ?? "Sim (Prosseguir)";
                BtnYes.Visibility = Visibility.Visible;
                BtnYes.IsDefault = true;
                break;

            case MessageBoxButton.YesNoCancel:
                BtnCancel.Content = "Cancelar";
                BtnCancel.Visibility = Visibility.Visible;
                BtnCancel.IsCancel = true;

                BtnNo.Content = customCancelText ?? "Não";
                BtnNo.Visibility = Visibility.Visible;

                BtnYes.Content = customOkText ?? "Sim (Prosseguir)";
                BtnYes.Visibility = Visibility.Visible;
                BtnYes.IsDefault = true;
                break;
        }
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.OK;
        DialogResult = true;
        Close();
    }

    private void BtnYes_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Yes;
        DialogResult = true;
        Close();
    }

    private void BtnNo_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.No;
        DialogResult = false;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Cancel;
        DialogResult = false;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (Result == MessageBoxResult.None)
        {
            if (_buttons == MessageBoxButton.OKCancel || _buttons == MessageBoxButton.YesNoCancel)
            {
                Result = MessageBoxResult.Cancel;
            }
            else if (_buttons == MessageBoxButton.YesNo)
            {
                Result = MessageBoxResult.No;
            }
            else
            {
                Result = MessageBoxResult.OK;
            }
        }

        base.OnClosing(e);
    }
}
