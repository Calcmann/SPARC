using System;
using System.Windows;
using NetworkDevice.Core.Licensing;

namespace NetworkDevice.UI;

public partial class EditDeviceDialog : Window
{
    private readonly OnlineDeviceRecord _device;
    public bool Saved { get; private set; }

    public EditDeviceDialog(OnlineDeviceRecord device)
    {
        InitializeComponent();
        _device = device ?? throw new ArgumentNullException(nameof(device));
        Loaded += EditDeviceDialog_Loaded;
    }

    private void EditDeviceDialog_Loaded(object sender, RoutedEventArgs e)
    {
        TxtSubHeader.Text = $"Editando: {_device.FullName} ({_device.PlatformBadge}) — GUID: {_device.MachineGuid}";
        TxtFirstName.Text = _device.FirstName;
        TxtLastName.Text = _device.LastName;
        TxtCompany.Text = _device.Company;
        TxtEmployeeId.Text = _device.EmployeeId;
        TxtPhone.Text = _device.Phone;
        TxtEmail.Text = _device.Email;
        TxtCluster.Text = _device.Cluster;
        TxtUf.Text = _device.Uf;
        TxtNotes.Text = _device.Notes;
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnSalvar_Click(object sender, RoutedEventArgs e)
    {
        var firstName = SparcTextSanitizer.FormatPersonOrCompanyName(TxtFirstName.Text);
        var lastName = SparcTextSanitizer.FormatPersonOrCompanyName(TxtLastName.Text);
        var company = SparcTextSanitizer.FormatPersonOrCompanyName(TxtCompany.Text);
        var employeeId = SparcTextSanitizer.FormatEmployeeId(TxtEmployeeId.Text);
        var phone = SparcTextSanitizer.FormatPhone(TxtPhone.Text);
        var email = SparcTextSanitizer.FormatEmail(TxtEmail.Text);
        var cluster = SparcTextSanitizer.FormatCluster(TxtCluster.Text);
        var uf = SparcTextSanitizer.FormatUf(TxtUf.Text);
        var notes = TxtNotes.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(firstName))
        {
            MessageBox.Show("Informe o Nome do técnico.", "Campo Obrigatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtFirstName.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(company))
        {
            MessageBox.Show("Informe a Empresa (terceirizada ou própria).", "Campo Obrigatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtCompany.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(employeeId))
        {
            MessageBox.Show("Informe a Matrícula funcional.", "Campo Obrigatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtEmployeeId.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(phone))
        {
            MessageBox.Show("Informe o WhatsApp / Telefone com DDD.", "Campo Obrigatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtPhone.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(cluster))
        {
            MessageBox.Show("Informe o Cluster de atuação.", "Campo Obrigatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtCluster.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(uf))
        {
            MessageBox.Show("Informe a UF (2 letras maiúsculas).", "Campo Obrigatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtUf.Focus();
            return;
        }

        // Aplica os valores sanitizados ao objeto
        _device.FirstName = firstName;
        _device.LastName = lastName;
        _device.Company = company;
        _device.EmployeeId = employeeId;
        _device.Phone = phone;
        _device.Email = email;
        _device.Cluster = cluster;
        _device.Uf = uf;
        _device.Notes = notes;

        Saved = true;
        DialogResult = true;
        Close();
    }
}
