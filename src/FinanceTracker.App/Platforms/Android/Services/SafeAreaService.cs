using Android.App;
using AndroidX.Core.View;
using FinanceTracker.Web.Services;
using AView = Android.Views.View;

namespace FinanceTracker.App.Platforms.Android.Services;

/// <summary>
/// Android 安全区服务（沉浸式 / 边到边布局下的系统栏、刘海与系统键盘占用区域）。
/// 数据来源：<see cref="ViewCompat.GetRootWindowInsets"/>（状态栏 + 导航栏 + 刘海 + 输入法），单位换算为 dp（= CSS 像素）。
/// 触发时机：窗口布局变化（旋转 / 分屏 / 系统栏变化，<c>GlobalLayout</c>）与系统键盘动画结束（<c>WindowInsetsAnimation</c>）。
/// 由 <c>MainActivity.OnCreate</c> 调用 <see cref="Attach"/> 绑定窗口；绑定前 <see cref="Current"/> 为 null（Web 层回落 0）。
/// </summary>
public sealed class SafeAreaService : ISafeAreaService
{
    private readonly object _gate = new();
    private Activity? _activity;
    private AView? _decorView;
    private ImeAnimationCallback? _imeCallback;
    private SafeAreaInsets? _current;

    public SafeAreaInsets? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event EventHandler<SafeAreaInsets>? Changed;

    /// <summary>绑定 Activity 窗口并立即测量一次（必须在主线程调用）</summary>
    public void Attach(Activity activity)
    {
        Detach();

        var decorView = activity.Window?.DecorView;
        if (decorView?.ViewTreeObserver is null)
        {
            return;
        }

        _activity = activity;
        _decorView = decorView;
        decorView.ViewTreeObserver.GlobalLayout += OnGlobalLayout;

        // 系统键盘弹出 / 收起不一定触发布局，监听 Insets 动画结束
        _imeCallback = new ImeAnimationCallback(this);
        ViewCompat.SetWindowInsetsAnimationCallback(decorView, _imeCallback);

        Update();
    }

    /// <summary>解除窗口绑定（Activity 销毁时调用）</summary>
    public void Detach()
    {
        if (_decorView is not null)
        {
            if (_decorView.ViewTreeObserver is { IsAlive: true } observer)
            {
                observer.GlobalLayout -= OnGlobalLayout;
            }

            ViewCompat.SetWindowInsetsAnimationCallback(_decorView, null);
        }

        _decorView = null;
        _activity = null;
        _imeCallback = null;
    }

    private void OnGlobalLayout(object? sender, EventArgs e) => Update();

    private void Update()
    {
        var activity = _activity;
        var decorView = _decorView;
        if (activity is null || decorView is null)
        {
            return;
        }

        var rootInsets = ViewCompat.GetRootWindowInsets(decorView);
        if (rootInsets is null)
        {
            return;
        }

        var bars = rootInsets.GetInsets(WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout());
        var ime = rootInsets.GetInsets(WindowInsetsCompat.Type.Ime());
        if (bars is null || ime is null)
        {
            return;
        }

        var density = activity.Resources?.DisplayMetrics?.Density ?? 1f;
        if (density <= 0)
        {
            density = 1f;
        }

        var insets = new SafeAreaInsets(
            bars.Top / density,
            bars.Right / density,
            Math.Max(bars.Bottom, ime.Bottom) / density,
            bars.Left / density);

        lock (_gate)
        {
            if (_current == insets)
            {
                return;
            }

            _current = insets;
        }

        Changed?.Invoke(this, insets);
    }

    /// <summary>系统键盘（输入法）动画结束时重新测量</summary>
    private sealed class ImeAnimationCallback : WindowInsetsAnimationCompat.Callback
    {
        private readonly SafeAreaService _owner;

        public ImeAnimationCallback(SafeAreaService owner)
            : base(DispatchModeContinueOnSubtree)
        {
            _owner = owner;
        }

        public override WindowInsetsCompat OnProgress(WindowInsetsCompat? insets, IList<WindowInsetsAnimationCompat>? runningAnimations) => insets!;

        public override void OnEnd(WindowInsetsAnimationCompat? animation)
        {
            base.OnEnd(animation);
            _owner.Update();
        }
    }
}
