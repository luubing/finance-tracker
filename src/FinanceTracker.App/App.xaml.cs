using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FinanceTracker.App;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		// 捕获 MAUI 应用级别的未处理异常
		AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
		TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// 直接使用 MainPage 而不是 AppShell，避免双重导航
		return new Window(new MainPage());
	}

	private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		var exception = e.ExceptionObject as Exception;
		Log.Fatal(exception, "MAUI 应用未处理异常（AppDomain.UnhandledException）");
		Log.CloseAndFlush();
	}

	private void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		Log.Error(e.Exception, "未观察到的任务异常（TaskScheduler.UnobservedTaskException）");
		e.SetObserved();
	}

	protected override void OnStart()
	{
		base.OnStart();
	}

	protected override void OnSleep()
	{
		base.OnSleep();
	}

	protected override void OnResume()
	{
		base.OnResume();
	}
}