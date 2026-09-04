using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace FinanceTracker.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);

		SetStatusBarAppearance();
	}

	/// <summary>
	/// 将系统状态栏（顶部时间/电量区域）的文字与图标设置为白色，
	/// 并将其背景设为主题主色（与顶部 MASA 页面栏 Color="primary" 一致），保证白色文字清晰可读。
	/// </summary>
	private void SetStatusBarAppearance()
	{
		if (Window is null)
			return;

		// 状态栏背景：与 MASA 主题 primary (#4318FF) 保持一致，保证白色文字清晰可读。
		// 注：Android 15 (API 35) 起强制 Edge-to-Edge，状态栏背景为透明，SetStatusBarColor 仅对旧版本生效。
#pragma warning disable CA1422
		Window.SetStatusBarColor(Android.Graphics.Color.ParseColor("#4318FF"));
#pragma warning restore CA1422

		// 状态栏文字/图标设为白色：关闭“浅色状态栏”(深色图标)
		var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
		controller?.AppearanceLightStatusBars = false;
	}
}
