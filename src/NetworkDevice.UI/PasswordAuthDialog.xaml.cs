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
    public string Username => TxtUser.Text.Trim();
    public string Password => TxtPass.Password.Trim();

    public PasswordAuthDialog() : this(false, null, null, null)
    {
    }

    public PasswordAuthDialog(bool requiresUsername, string? deviceName = null, string? errorMessage = null, string? previousUser = null)
    {
        InitializeComponent();
        RequiresUsername = requiresUsername;

        var devLabel = string.IsNullOrWhiteSpace(deviceName) ? "HPE 954" : deviceName;

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            BorderErrorBanner.Visibility = Visibility.Visible;
            TxtErrorMessage.Text = errorMessage;
        }

        if (requiresUsername)
        {
            TxtHeaderTitle.Text = "Equipamento Protegido por Usuário e Senha";
            TxtHeaderSubtitle.Text = $"O roteador {devLabel} está solicitando Usuário (login) e Senha no console serial.";
            TxtExplanation.Text = "Se você possui o Usuário e a Senha de acesso deste equipamento, informe-os abaixo para fazer login imediato e pular a etapa de zeramento. Caso contrário, opte pelo zeramento de fábrica para limpar as credenciais antigas.";
            TxtOption1Title.Text = "OPÇÃO 1: Informar Credenciais de Acesso (Login Direto)";
            TxtOption2Title.Text = "OPÇÃO 2: Não tenho as Credenciais (Zerar de Fábrica)";
            LblUser.Visibility = Visibility.Visible;
            TxtUser.Visibility = Visibility.Visible;
            BtnLogin.Content = "🔑 Testar Credenciais e Pular Zeramento";

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
            TxtExplanation.Text = "Se você possui a Senha de acesso deste equipamento, informe-a abaixo para fazer login imediato e pular a etapa de zeramento. Caso contrário, opte pelo zeramento de fábrica para limpar a configuração antiga.";
            TxtOption1Title.Text = "OPÇÃO 1: Informar a Senha de Acesso (Login Direto)";
            TxtOption2Title.Text = "OPÇÃO 2: Não tenho a Senha (Zerar de Fábrica)";
            LblUser.Visibility = Visibility.Collapsed;
            TxtUser.Visibility = Visibility.Collapsed;
            BtnLogin.Content = "🔑 Testar Senha e Pular Zeramento";

            Loaded += (s, e) => TxtPass.Focus();
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

        if (string.IsNullOrEmpty(Password))
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
