using System;
using System.IO;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using NetworkDevice.Android.Services;

namespace NetworkDevice.Android.Views;

public partial class ReportsHistoryPage : ContentPage
{
    private readonly ReportsStorageService _storage = ReportsStorageService.Instance;

    public ReportsHistoryPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadReports();
    }

    private void OnRefreshClicked(object? sender, EventArgs e)
    {
        LoadReports();
    }

    private void LoadReports()
    {
        try
        {
            var reports = _storage.ListReports();
            ReportsCollection.ItemsSource = reports;
            FooterInfoLabel.Text = $"Total de relatórios salvos: {reports.Count} • Diretório: Reports/";
        }
        catch (Exception ex)
        {
            DisplayAlert("Erro", $"Falha ao listar relatórios: {ex.Message}", "OK");
        }
    }

    private async void OnShareReportClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string filePath)
        {
            if (!File.Exists(filePath))
            {
                await DisplayAlert("Aviso", "O arquivo selecionado não foi encontrado.", "OK");
                LoadReports();
                return;
            }

            try
            {
                var title = Path.GetFileNameWithoutExtension(filePath);
                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = $"Relatório SPARC - {title}",
                    File = new ShareFile(filePath)
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("Erro ao Compartilhar", ex.Message, "OK");
            }
        }
    }

    private async void OnDeleteReportClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string filePath)
        {
            var confirm = await DisplayAlert("Excluir Relatório",
                $"Deseja realmente remover o arquivo '{Path.GetFileName(filePath)}'?",
                "SIM, EXCLUIR", "CANCELAR");

            if (confirm)
            {
                _storage.DeleteReport(filePath);
                LoadReports();
            }
        }
    }
}
