using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace FinanceTracker.Web.Services;

/// <summary>
/// 沉浸式协调器：把「宿主原生安全区」同步给 Web 层，并把 Web 层判定的「状态栏图标风格」交回宿主原生设置。
/// 由全局唯一的 <c>ImmersiveHost</c> 组件创建和释放；所有 JS 交互都在 <c>wwwroot/js/immersive.js</c> 中。
/// <list type="number">
/// <item>安全区：<see cref="ISafeAreaService"/> → JS <c>setSafeArea</c> → CSS 变量 <c>--ft-native-safe-*</c>
/// （页面统一读 <c>--ft-safe-*</c>，原生没有提供时回落到 <c>env(safe-area-inset-*)</c>，再回落到 0）；
/// 底部边距里「只是指示条」的部分（<see cref="SafeAreaInsets.BottomIndicator"/>）另写到 <c>--ft-native-safe-indicator</c>，Tab 栏据此压住 Home 指示条。</item>
/// <item>状态栏：JS 对屏幕顶部像素采样得到图标风格（或读取页面声明的 <c>data-statusbar-bg</c>），
/// 回调 <see cref="OnStatusBarIconsChanged"/> → <see cref="IStatusBarService"/>。</item>
/// </list>
/// <para>
/// 沉浸式只是外观增强，不能因为它出问题让应用报「未处理异常」：<c>ImmersiveHost</c> 放在 <c>Main.razor</c> 根部、不在任何
/// <c>ErrorBoundary</c> 里，从这里抛出去的异常会直接变成整个应用的未处理异常提示条。所以连接断开（页面卸载）静默放弃，
/// 脚本加载 / 执行失败（<see cref="JSException"/>）记一条错误日志后放弃——不吞掉，错误日志页能看到原因。
/// </para>
/// </summary>
public sealed class ImmersiveCoordinator : IAsyncDisposable
{
    private const string ModulePath = "./_content/FinanceTracker.Web/js/immersive.js";

    private readonly IJSRuntime _js;
    private readonly ISafeAreaService _safeArea;
    private readonly IStatusBarService _statusBar;
    private readonly ILogger? _logger;

    /// <summary>串行化「读取最新安全区 → 推给 JS」，保证最后一次推送一定对应最新读数</summary>
    private readonly SemaphoreSlim _pushGate = new(1, 1);

    private IJSObjectReference? _module;
    private DotNetObjectReference<ImmersiveCoordinator>? _selfReference;
    private SafeAreaInsets? _pushedInsets;
    private StatusBarIconStyle? _lastStyle;
    private bool _started;
    private volatile bool _disposed;

    /// <param name="logger">记录脚本失败；不传则静默放弃（测试用）</param>
    public ImmersiveCoordinator(IJSRuntime js, ISafeAreaService safeArea, IStatusBarService statusBar, ILogger? logger = null)
    {
        _js = js;
        _safeArea = safeArea;
        _statusBar = statusBar;
        _logger = logger;
    }

    /// <summary>加载 JS 模块、推送初始安全区并开始监听页面（只能在首次渲染完成后调用）</summary>
    public async Task StartAsync()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        try
        {
            _module = await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _selfReference = DotNetObjectReference.Create(this);
            _safeArea.Changed += OnSafeAreaChanged;
            await PushSafeAreaAsync();
            await _module.InvokeVoidAsync("start", _selfReference);
        }
        catch (Exception ex) when (IsInteropUnavailable(ex))
        {
            // 电路已断开 / 页面已卸载：沉浸式只是外观增强，直接放弃即可
        }
        catch (JSException ex)
        {
            ReportScriptFailure(ex, "启动");
        }
    }

    /// <summary>JS 通知：状态栏图标风格变化（<c>"light"</c> 浅色图标 / <c>"dark"</c> 深色图标）</summary>
    [JSInvokable]
    public void OnStatusBarIconsChanged(string icons)
    {
        var style = icons switch
        {
            "light" => StatusBarIconStyle.Light,
            "dark" => StatusBarIconStyle.Dark,
            _ => (StatusBarIconStyle?)null,
        };

        if (style is null || style == _lastStyle || _disposed)
        {
            return;
        }

        _lastStyle = style;
        _statusBar.SetIconStyle(style.Value);
    }

    /// <summary>JS 通知：视口尺寸变化（旋转 / 分屏 / 系统键盘），重新读取原生安全区</summary>
    [JSInvokable]
    public Task OnViewportChanged() => PushSafeAreaAsync();

    private void OnSafeAreaChanged(object? sender, SafeAreaInsets insets) => _ = PushSafeAreaAsync();

    private async Task PushSafeAreaAsync()
    {
        var module = _module;
        if (module is null || _disposed)
        {
            return;
        }

        await _pushGate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            var latest = _safeArea.Current;
            if (latest == _pushedInsets)
            {
                return;
            }

            _pushedInsets = latest;
            if (latest is { } insets)
            {
                await module.InvokeVoidAsync("setSafeArea", insets.Top, insets.Right, insets.Bottom, insets.Left, insets.BottomIndicator);
            }
            else
            {
                await module.InvokeVoidAsync("clearSafeArea");
            }
        }
        catch (Exception ex) when (IsInteropUnavailable(ex))
        {
        }
        catch (JSException ex)
        {
            ReportScriptFailure(ex, "同步安全区");
        }
        finally
        {
            _pushGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _safeArea.Changed -= OnSafeAreaChanged;

        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("stop");
                await _module.DisposeAsync();
            }
        }
        catch (Exception ex) when (IsInteropUnavailable(ex))
        {
        }
        catch (JSException ex)
        {
            ReportScriptFailure(ex, "停止");
        }
        finally
        {
            _selfReference?.Dispose();
            _module = null;
        }
    }

    /// <summary>脚本加载 / 执行失败：记一条错误日志后放弃（沉浸式是外观增强，应用其余部分照常可用）</summary>
    private void ReportScriptFailure(JSException ex, string action) =>
        _logger?.LogError(ex, "沉浸式脚本（immersive.js）{Action}失败，安全区与状态栏图标风格可能不会自动适配", action);

    private static bool IsInteropUnavailable(Exception ex) =>
        ex is JSDisconnectedException or TaskCanceledException or ObjectDisposedException or OperationCanceledException;
}
