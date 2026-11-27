using Microsoft.Maui.Platform;

namespace FinanceTracker.App;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

#if ANDROID || IOS
        blazorWebView.BlazorWebViewInitialized += OnBlazorWebViewInitialized;
#endif
    }

#if ANDROID
    /// <summary>
    /// 冷启动不闪白：原生 WebView 在网页第一次绘制之前画的是它自己的默认底色（Chromium 的白色），
    /// 会盖住下面 MainPage 的紫色，于是系统启动画面与网页加载页之间露出一帧白屏。
    /// 这里把底色改成页面背景（XAML 里的启动底色，与启动画面 / 加载页同一个颜色）；
    /// 网页绘制之后，加载页与页面自己的背景会盖住它，之后看不到。
    /// 事件在 WebView 创建之后、开始加载页面之前触发，所以赶在第一帧之前。
    /// </summary>
    private void OnBlazorWebViewInitialized(object? sender, Microsoft.AspNetCore.Components.WebView.BlazorWebViewInitializedEventArgs e)
    {
        if (BackgroundColor is { } launchColor)
            e.WebView.SetBackgroundColor(launchColor.ToPlatform());
    }
#elif IOS
    /// <summary>
    /// 沉浸式：网页铺满整个屏幕（含状态栏、灵动岛与底部手势条之下），安全区由 Web 层通过 CSS 变量 --ft-safe-* 自行避让。
    /// WKWebView 默认会按安全区自动给 ScrollView 加内边距，这里关闭，避免双重避让。
    ///
    /// 冷启动不闪白：WKWebView 默认不透明，网页第一次绘制之前画的是白色，会盖住下面 MainPage 的紫色（MAUI 只把它的背景色设成了 Clear，没有关掉不透明）。
    /// 这里关掉不透明，并把 WebView、ScrollView 与「页面下方」的底色都设成页面背景（XAML 里的启动底色，与启动画面 / 加载页同一个颜色）；
    /// 网页绘制之后，加载页与页面自己的背景会盖住它，之后看不到。
    /// </summary>
    private void OnBlazorWebViewInitialized(object? sender, Microsoft.AspNetCore.Components.WebView.BlazorWebViewInitializedEventArgs e)
    {
        var webView = e.WebView;
        webView.ScrollView.ContentInsetAdjustmentBehavior = UIKit.UIScrollViewContentInsetAdjustmentBehavior.Never;

        if (BackgroundColor is not { } launchColor)
            return;

        var color = launchColor.ToPlatform();
        webView.Opaque = false;
        webView.BackgroundColor = color;
        webView.ScrollView.BackgroundColor = color;
        if (OperatingSystem.IsIOSVersionAtLeast(15))
            webView.UnderPageBackgroundColor = color;
    }
#endif
}
