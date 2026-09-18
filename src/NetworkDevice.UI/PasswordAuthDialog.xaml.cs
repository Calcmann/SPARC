using System.Windows;

namespace NetworkDevice.UI;

public enum PasswordAuthChoice
{
    Cancel,
    LoginDirect,
    FactoryReset
}

public partial class PasswordAuthDialog : Window
{
    public PasswordAuthChoice Choice { get; private set; } = PasswordAuthChoice.Cancel;
    public bool RequiresUsername { get; }
    public bool AllowBlankPassword { get; }
    public bool IsFortiGate { get; }
    public string Username => TxtUser.Text.Trim();
    public string Password => TxtPass.Password.Trim();

    public PasswordAuthDialog() : this(false, null, null, null)
    {
    }

    public PasswordAuthDialog(bool requiresUsername, string? deviceName = null, string? errorMessage = null, string? previousUser = null, bool allowBlankPassword = false, bool isFortiGate = false)
    {
        InitializeComponent();
        RequiresUsername = requiresUsername;
        AllowBlankPassword = allowBlankPassword;
        IsFortiGate = isFortiGate;

        var devLabel = string.IsNullOrWhiteSpace(deviceName) ? (isFortiGate ? "FortiGate 40F" : "Roteador") : deviceName;

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            BorderErrorBanner.Visibility = Visibility.Visible;
            TxtErrorMessage.Text = errorMessage;
        }

        if (isFortiGate)
        {
            TxtHeaderTitle.Text = "FortiGate 40F — Equipamento Protegido por Senha";
            TxtHeaderSubtitle.Text = "O FortiGate 40F está solicitando autenticação no console serial.";
            TxtExplanation.Text = "Se você possui as credenciais de acesso deste equipamento, informe-as abaixo para fazer login imediato e pular a etapa de zeramento. Caso contrário, o SPARC executará a quebra de senha e recuperação de fábrica de forma autônoma.";
            TxtOption1Title.Text = "OPÇÃO 1: Informar Credenciais de Acesso (Login Direto)";
            TxtOption2Title.Text = "OPÇÃO 2: Não tenho as Credenciais (Quebrar Senha / Zerar)";
            TxtOption2Subtitle.Text = "O SPARC executará a recuperação de senha autônoma via conta maintainer no boot, redefinindo o acesso e limpando a configuração antiga.";
            TxtOption2Steps.Text = "👉 O assistente autônomo do SPARC monitorará a porta serial e efetuará todo o procedimento automaticamente.";
            BtnZerar.Content = "⚡ Quebrar Senha e Zerar Configuração";
            LblUser.Visibility = Visibility.Visible;
            TxtUser.Visibility = Visibility.Visible;
            BtnLogin.Content = "🔑 Testar Credenciais e Pular Zeramento";
            TxtUser.Text = !string.IsNullOrWhiteSpace(previousUser) ? previousUser : "";
            Loaded += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(TxtUser.Text))
                    TxtUser.Focus();
                else
                    TxtPass.Focus();
            };
        }
        else if (requiresUsername)
        {
            TxtHeaderTitle.Text = "Equipamento Protegido por Usuário e Senha";
            TxtHeaderSubtitle.Text = $"O roteador {devLabel} está solicitando Usuário (login) e Senha no console serial.";
            TxtExplanation.Text = "Se você possui o Usuário e a Senha de acesso deste equipamento, informe-os abaixo para fazer login imediato e pular a etapa de zeramento. Caso contrário, opte pelo zeramento de fábrica para limpar as credenciais antigas.";
            TxtOption1Title.Text = "OPÇÃO 1: Informar Credenciais de Acesso (Login Direto)";
            TxtOption2Title.Text = "OPÇÃO 2: Não tenho as Credenciais (Zerar de Fábrica)";
            TxtOption2Subtitle.Text = "O SPARC executará a quebra de senha via BootWare (HPE Ctrl+B) ou ROMMON (Cisco Break/Ctrl+C), apagando a configuração antiga e criando o acesso limpo.";
            TxtOption2Steps.Text = "👉 Ao optar pelo zeramento, siga estes passos:\n  1. Selecione o Modelo do Equipamento no Passo 1;\n  2. Carregue a Ficha SAIP (.pdf / .txt) no Passo 2;\n  3. Clique em 'INICIAR PROVISIONAMENTO AUTOMÁTICO'.";
            LblUser.Visibility = Visibility.Visible;
            TxtUser.Visibility = Visibility.Visible;
            BtnLogin.Content = "🔑 Testar Credenciais e Pular Zeramento";
            BtnZerar.Content = "⚡ Zerar Configuração e Quebrar Senha";
            if (!string.IsNullOrWhiteSpace(previousUser))
            {
                TxtUser.Text = previousUser;
                Loaded += (s, e) => TxtPass.Focus();
            }
            else
            {
                Loaded += (s, e) => TxtUser.Focus();
            }
        }
        else
        {
            TxtHeaderTitle.Text = "Equipamento Protegido por Senha";
            TxtHeaderSubtitle.Text = $"O roteador {devLabel} está solicitando Senha de Acesso no console serial.";
            TxtExplanation.Text = "Se você possui a Senha de acesso deste equipamento, informe-as abaixo para fazer login imediato e pular a etapa de zeramento. Caso contrário, opte pelo zeramento de fábrica para limpar a configuração antiga.";
            TxtOption1Title.Text = "OPÇÃO 1: Informar a Senha de Acesso (Login Direto)";
            TxtOption2Title.Text = "OPÇÃO 2: Não tenho a Senha (Zerar de Fábrica)";
            TxtOption2Subtitle.Text = "O SPARC executará a quebra de senha via BootWare (HPE Ctrl+B) ou ROMMON (Cisco Break/Ctrl+C), apagando a configuração antiga e criando o acesso limpo.";
            TxtOption2Steps.Text = "👉 Ao optar pelo zeramento, siga estes passos:\n  1. Selecione o Modelo do Equipamento no Passo 1;\n  2. Carregue a Ficha SAIP (.pdf / .txt) no Passo 2;\n  3. Clique em 'INICIAR PROVISIONAMENTO AUTOMÁTICO'.";
            LblUser.Visibility = Visibility.Collapsed;
            TxtUser.Visibility = Visibility.Collapsed;
            BtnLogin.Content = "🔑 Testar Senha e Pular Zeramento";
            BtnZerar.Content = "⚡ Zerar Configuração e Quebrar Senha";

            Loaded += (s, e) => TxtPass.Focus();
        }

        // FortiGate com padrão de fábrica (admin / senha em branco): libera o campo de senha vazio.
        if (allowBlankPassword && !isFortiGate)
        {
            TxtExplanation.Text += "\n\n🛡️ FortiGate em padrão de fábrica: usuário 'admin' com senha em branco — basta clicar em entrar sem digitar senha.";
            BtnLogin.Content = "🔑 Entrar (senha em branco liberada)";
            LblPass.Text = "Senha de Acesso (pode ficar em branco):";
        }
    }

    private void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        if (RequiresUsername && string.IsNullOrWhiteSpace(Username))
        {
            MessageBox.Show(this, "Por favor, digite o nome de Usuário (login) para efetuar o acesso.", "Usuário Não Informado", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtUser.Focus();
            return;
        }

        if (string.IsNullOrEmpty(Password) && !AllowBlankPassword)
        {
            MessageBox.Show(this, "Por favor, digite a Senha de acesso para efetuar o login.", "Senha Não Informada", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtPass.Focus();
            return;
        }

        Choice = PasswordAuthChoice.LoginDirect;
        DialogResult = true;
        Close();
    }

    private void BtnZerar_Click(object sender, RoutedEventArgs e)
    {
        Choice = PasswordAuthChoice.FactoryReset;
        DialogResult = true;
        Close();
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        Choice = PasswordAuthChoice.Cancel;
        DialogResult = false;
        Close();
    }
}
