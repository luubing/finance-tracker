using Microsoft.Extensions.DependencyInjection;

namespace FinanceTracker.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// 直接使用 MainPage 而不是 AppShell，避免双重导航
		return new Window(new MainPage());
	}
}