namespace NetworkDevice.Android;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute("RecoveryPage", typeof(Views.RecoveryPage));
		Routing.RegisterRoute("ActivationPage", typeof(Views.ActivationPage));
		Routing.RegisterRoute("ReportsHistoryPage", typeof(Views.ReportsHistoryPage));

		var specialEnabled = Preferences.Default.Get("sparc_special_functions", false);
		TabY1564.IsVisible = specialEnabled;
	}

	public void SetSpecialFunctionsVisibility(bool isVisible)
	{
		TabY1564.IsVisible = isVisible;
	}
}
