using FinanceTracker.Web.Services;
using Foundation;
using Microsoft.Maui.ApplicationModel;
using UIKit;

namespace FinanceTracker.App.Platforms.iOS.Services;

/// <summary>
/// iOS 安全区服务：读取关键窗口的 <c>SafeAreaInsets</c>（状态栏 / 灵动岛 / 刘海 / 底部 Home 指示条，单位 pt = CSS 像素），
/// 并把系统键盘高度并入底部边距。
/// 触发时机：系统键盘弹出 / 收起（键盘通知）；旋转等视口变化由 Web 层 <c>resize</c> 事件回调
/// <see cref="ImmersiveCoordinator.OnViewportChanged"/> 后重新读取 <see cref="Current"/>。
/// 注意：必须配合 <c>MainPage</c> 里把 <c>WKWebView.ScrollView.ContentInsetAdjustmentBehavior</c> 设为 Never，
/// 否则系统会再给网页内容加一层安全区边距，造成双重避让。
/// </summary>
public sealed class SafeAreaService : ISafeAreaService
{
    private readonly object _gate = new();
    private readonly List<NSObject> _observers = new();
    private SafeAreaInsets? _current;
    private double _keyboardHeight;

    public SafeAreaService()
    {
        _observers.Add(NSNotificationCenter.DefaultCenter.AddObserver(UIKeyboard.WillShowNotification, OnKeyboardWillChange));
        _observers.Add(NSNotificationCenter.DefaultCenter.AddObserver(UIKeyboard.WillHideNotification, OnKeyboardWillChange));
    }

    public SafeAreaInsets? Current
    {
        get
        {
            // UIKit 只能在主线程访问：主线程上即时测量，其他线程返回最近一次的缓存
            if (MainThread.IsMainThread)
            {
                Measure();
            }

            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event EventHandler<SafeAreaInsets>? Changed;

    private void OnKeyboardWillChange(NSNotification notification)
    {
        var showing = UIKeyboard.WillShowNotification.Equals(notification.Name);
        var frame = (notification.UserInfo?[UIKeyboard.FrameEndUserInfoKey] as NSValue)?.CGRectValue;
        _keyboardHeight = showing && frame is { } rect ? (double)rect.Height : 0;

        if (Measure() is { } insets)
        {
            Changed?.Invoke(this, insets);
        }
    }

    /// <summary>测量并更新缓存；仅在数值变化时返回新值（用于触发 Changed）</summary>
    private SafeAreaInsets? Measure()
    {
        var window = GetKeyWindow();
        if (window is null)
        {
            return null;
        }

        var safe = window.SafeAreaInsets;
        var insets = new SafeAreaInsets(
            (double)safe.Top,
            (double)safe.Right,
            Math.Max((double)safe.Bottom, _keyboardHeight),
            (double)safe.Left);

        lock (_gate)
        {
            if (_current == insets)
            {
                return null;
            }

            _current = insets;
            return insets;
        }
    }

    private static UIWindow? GetKeyWindow() =>
        UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(window => window.IsKeyWindow);
}
