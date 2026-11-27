using Android.App;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.OS;
using AndroidX.Core.View;
using FinanceTracker.App.Platforms.Android.Services;
using FinanceTracker.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceTracker.App;

[Activity(Theme = "@style/Ft.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);

		ApplyLaunchBackground();

		EnableEdgeToEdge();
		AttachSafeAreaService();
	}

	protected override void OnDestroy()
	{
		GetSafeAreaService()?.Detach();
		base.OnDestroy();
	}

	/// <summary>
	/// 冷启动不闪白：<c>MauiAppCompatActivity.OnCreate</c> 会把启动主题（Ft.SplashTheme）换成 Maui.MainTheme，窗口背景随之变成主题默认的白色（深色模式下是黑色）。
	/// 系统启动画面一结束、网页还没画出第一帧，露出来的就是窗口背景，这是白屏闪动的来源之一（另一处是原生 WebView 自己的底色，见 MainPage）。
	/// 这里把窗口背景压成启动画面的底色（MAUI 按 csproj 里 MauiSplashScreen 的 Color 生成的 maui_splash_color）：
	/// 启动画面 → 窗口 → 页面 → WebView → 加载页，全程是同一个颜色。
	/// 必须紧跟在 base.OnCreate 之后（主题切换发生在里面），并且赶在第一帧之前——在 OnCreate 里两者都满足。
	/// </summary>
	private void ApplyLaunchBackground()
	{
		var color = Resources?.GetColor(Resource.Color.maui_splash_color, Theme);
		if (color is { } launchColor)
			Window?.SetBackgroundDrawable(new ColorDrawable(launchColor));
	}

	/// <summary>
	/// 沉浸式：内容延伸到状态栏与导航栏之下，两者透明；
	/// 页面通过 Web 层注入的 CSS 变量 <c>--ft-safe-*</c> 自行避让（见 ISafeAreaService）。
	/// 状态栏图标颜色不再写死白色，由 Web 层按状态栏下方的页面背景明暗动态设置（见 IStatusBarService）。
	/// </summary>
	private void EnableEdgeToEdge()
	{
		if (Window is null)
			return;

		WindowCompat.SetDecorFitsSystemWindows(Window, false);

		// Android 15 (API 35) 起系统强制边到边且忽略这些颜色 / 遮罩设置；低版本需要显式设为透明，
		// 并关闭系统在 3 键导航栏 / 手势条后面自动补的半透明遮罩（API 29+），否则底部会出现一条灰带。
#pragma warning disable CA1422
		Window.SetStatusBarColor(Android.Graphics.Color.Transparent);
		Window.SetNavigationBarColor(Android.Graphics.Color.Transparent);
		if (OperatingSystem.IsAndroidVersionAtLeast(29))
		{
			Window.StatusBarContrastEnforced = false;
			Window.NavigationBarContrastEnforced = false;
		}
#pragma warning restore CA1422

		// 初始图标：冷启动先是紫色启动画面 / 加载页 → 状态栏白色图标（冷启动设计稿：状态栏图标从第一帧起就是白色）。
		// 之后由 Web 层按状态栏下方页面的背景明暗接管（加载页声明了 data-statusbar-bg="dark"，首页 / 登录页的深色顶部同理，浅色页面会切回深色图标）。
		// 导航栏图标 Web 层不管，保持深色：绝大多数页面底部是浅色，白色手势条会看不见。
		var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
		if (controller is not null)
		{
			controller.AppearanceLightStatusBars = false;
			controller.AppearanceLightNavigationBars = true;
		}
	}

	private void AttachSafeAreaService() => GetSafeAreaService()?.Attach(this);

	private static SafeAreaService? GetSafeAreaService() =>
		IPlatformApplication.Current?.Services.GetService<ISafeAreaService>() as SafeAreaService;
}
