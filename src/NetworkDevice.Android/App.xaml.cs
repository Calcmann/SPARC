namespace NetworkDevice.Android;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		// Pluga os inspetores canônicos de firmware para auditoria estrita em qualquer fluxo
		NetworkDevice.Core.Firmware.RouterDirectFirmwareUpdater.ExternalComplianceEvaluator = (series, rawOutput, targetFileName) =>
		{
			if (series is NetworkDevice.Core.Domain.DeviceSeries.Isr841 or NetworkDevice.Core.Domain.DeviceSeries.Isr921 or NetworkDevice.Core.Domain.DeviceSeries.Series1900)
			{
				return NetworkDevice.Cisco.CiscoIOSFirmwareVersionInspector.IsSameVersion(rawOutput, targetFileName, out _, out _);
			}
			if (series == NetworkDevice.Core.Domain.DeviceSeries.FortiGate40F)
			{
				return NetworkDevice.Fortinet.FortiOsFirmwareVersionInspector.IsSameVersion(rawOutput, targetFileName, out _, out _);
			}
			return NetworkDevice.Core.Firmware.RouterDirectFirmwareUpdater.EvaluateHpeCompliance(rawOutput, targetFileName);
		};

		if (Services.AndroidLicenseManager.Instance.IsActivated(out _))
		{
			MainPage = new AppShell();
		}
		else
		{
			MainPage = new NavigationPage(new Views.ActivationPage());
		}
	}

	protected override async void OnStart()
	{
		base.OnStart();
		try
		{
			using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
			var (allowed, revoked, msg) = await Services.AndroidLicenseManager.Instance.VerifyLicenseStartupAsync(cts.Token);
			if (!allowed && MainPage is not NavigationPage)
			{
				MainPage = new NavigationPage(new Views.ActivationPage());
				if (Current?.Windows.Count > 0 && Current.Windows[0].Page != null)
				{
					await Current.Windows[0].Page!.DisplayAlert("Acesso ao SPARC", msg, "OK");
				}
			}
		}
		catch
		{
			// Offline ou timeout: preserva funcionamento local se já validado
		}

		// Verificação de nova versão homologada no repositório oficial
		try
		{
			using var updateCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
			var updateSvc = new NetworkDevice.Core.Firmware.SparcAppUpdateService();
			var (hasUpdate, release, _) = await updateSvc.CheckForUpdateAsync(AppInfo.VersionString, "android", updateCts.Token);
			if (hasUpdate && release != null && !string.IsNullOrWhiteSpace(release.DownloadUrl))
			{
				if (Current?.Windows.Count > 0 && Current.Windows[0].Page != null)
				{
					var page = Current.Windows[0].Page!;
					await Services.AndroidAppUpdater.DownloadAndInstallAsync(page, release);
				}
			}
		}
		catch
		{
			// Offline ou timeout: ignora
		}
	}
}
