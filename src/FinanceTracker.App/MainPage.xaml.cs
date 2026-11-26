namespace FinanceTracker.App;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

#if IOS
        blazorWebView.BlazorWebViewInitialized += OnBlazorWebViewInitialized;
#endif
    }

#if IOS
    /// <summary>
    /// 沉浸式：网页铺满整个屏幕（含状态栏、灵动岛与底部手势条之下），安全区由 Web 层通过 CSS 变量 --ft-safe-* 自行避让。
    /// WKWebView 默认会按安全区自动给 ScrollView 加内边距，这里关闭，避免双重避让。
    /// </summary>
    private void OnBlazorWebViewInitialized(object? sender, Microsoft.AspNetCore.Components.WebView.BlazorWebViewInitializedEventArgs e)
    {
        e.WebView.ScrollView.ContentInsetAdjustmentBehavior = UIKit.UIScrollViewContentInsetAdjustmentBehavior.Never;
    }
#endif
}
