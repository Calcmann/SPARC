namespace NetworkDevice.Android;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		if (Services.AndroidLicenseManager.Instance.IsActivated(out _))
		{
			MainPage = new AppShell();
		}
		else
		{
			MainPage = new NavigationPage(new Views.ActivationPage());
		}
	}
}
