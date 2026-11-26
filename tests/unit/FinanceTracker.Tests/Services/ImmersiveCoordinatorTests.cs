using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Services;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 沉浸式协调器：连接「宿主原生安全区 / 状态栏」与「Web 层 immersive.js」的 C# 一侧。
/// 原生实现（Android / iOS）无法在单元测试里运行，这里用假实现钉住协调逻辑本身：
/// 初始同步、变化推送与去重、状态栏回调、释放后不再响应，以及连接断开时静默放弃。
/// </summary>
public sealed class ImmersiveCoordinatorTests
{
    private const string ModulePath = "./_content/FinanceTracker.Web/js/immersive.js";

    private static readonly SafeAreaInsets Notch = new(47, 0, 34, 0);

    private readonly FakeImmersiveJsRuntime _js = new();
    private readonly FakeSafeAreaService _safeArea = new();
    private readonly FakeStatusBarService _statusBar = new();
    private readonly ListLogger<ImmersiveCoordinator> _logger = new();

    private ImmersiveCoordinator CreateCoordinator() => new(_js, _safeArea, _statusBar, _logger);

    private FakeImmersiveModule Module => _js.Module;

    [Fact]
    public async Task 启动时加载模块_先推送初始安全区_再开始监听页面()
    {
        _safeArea.Current = Notch;

        await CreateCoordinator().StartAsync();

        Assert.Equal(new[] { ModulePath }, _js.ImportedPaths);
        Assert.Equal(new[] { "setSafeArea", "start" }, Module.Calls.Select(call => call.Identifier));
        // 单位是 CSS 像素，顺序 上、右、下、左
        Assert.Equal(new object?[] { 47d, 0d, 34d, 0d }, Module.Calls[0].Args);
    }

    [Fact]
    public async Task 宿主没有原生安全区时_既不推送也不清除_沿用CSS_env回落()
    {
        _safeArea.Current = null;

        await CreateCoordinator().StartAsync();

        Assert.Equal(0, Module.CountOf("setSafeArea"));
        Assert.Equal(0, Module.CountOf("clearSafeArea"));
        Assert.Equal(1, Module.CountOf("start"));
    }

    [Fact]
    public async Task 重复启动只加载一次模块()
    {
        var coordinator = CreateCoordinator();

        await coordinator.StartAsync();
        await coordinator.StartAsync();

        Assert.Single(_js.ImportedPaths);
        Assert.Equal(1, Module.CountOf("start"));
        Assert.Equal(1, _safeArea.SubscriberCount);
    }

    [Fact]
    public async Task 安全区变化时推送新值_数值相同不重复推送()
    {
        _safeArea.Current = Notch;
        await CreateCoordinator().StartAsync();
        Assert.Equal(1, Module.CountOf("setSafeArea"));

        // 系统键盘弹出：底部边距变大
        var withKeyboard = new SafeAreaInsets(47, 0, 336, 0);
        _safeArea.Raise(withKeyboard);
        Assert.Equal(2, Module.CountOf("setSafeArea"));
        Assert.Equal(new object?[] { 47d, 0d, 336d, 0d }, Module.Calls.Last(call => call.Identifier == "setSafeArea").Args);

        // 原生在布局过程中可能重复通知同一个数值
        _safeArea.Raise(withKeyboard);
        Assert.Equal(2, Module.CountOf("setSafeArea"));
    }

    [Fact]
    public async Task 原生安全区由有变无时清除注入值()
    {
        _safeArea.Current = Notch;
        var coordinator = CreateCoordinator();
        await coordinator.StartAsync();

        _safeArea.Current = null;
        await coordinator.OnViewportChanged();

        Assert.Equal(1, Module.CountOf("clearSafeArea"));

        // 之后仍然没有原生值：不重复清除
        await coordinator.OnViewportChanged();
        Assert.Equal(1, Module.CountOf("clearSafeArea"));
    }

    [Fact]
    public async Task 视口尺寸变化回调会重新读取原生安全区()
    {
        // iOS 的旋转没有独立事件：靠 Web 层 resize 回调后重新读取 Current
        _safeArea.Current = Notch;
        var coordinator = CreateCoordinator();
        await coordinator.StartAsync();

        _safeArea.Current = new SafeAreaInsets(0, 47, 21, 47); // 横屏
        await coordinator.OnViewportChanged();

        Assert.Equal(2, Module.CountOf("setSafeArea"));
        Assert.Equal(new object?[] { 0d, 47d, 21d, 47d }, Module.Calls.Last(call => call.Identifier == "setSafeArea").Args);

        // 再次回调但数值没变：不重复推送
        await coordinator.OnViewportChanged();
        Assert.Equal(2, Module.CountOf("setSafeArea"));
    }

    [Fact]
    public async Task 状态栏图标回调转交原生服务_相同风格只转交一次()
    {
        var coordinator = CreateCoordinator();
        await coordinator.StartAsync();

        coordinator.OnStatusBarIconsChanged("light");
        coordinator.OnStatusBarIconsChanged("light");
        coordinator.OnStatusBarIconsChanged("dark");
        coordinator.OnStatusBarIconsChanged("light");

        Assert.Equal(
            new[] { StatusBarIconStyle.Light, StatusBarIconStyle.Dark, StatusBarIconStyle.Light },
            _statusBar.Styles);
    }

    [Theory]
    [InlineData("")]
    [InlineData("white")]
    [InlineData("LIGHT")]
    public async Task 未知的图标风格值被忽略(string icons)
    {
        var coordinator = CreateCoordinator();
        await coordinator.StartAsync();

        coordinator.OnStatusBarIconsChanged(icons);

        Assert.Empty(_statusBar.Styles);
    }

    [Fact]
    public async Task 释放后停止JS监听_释放模块_且不再响应原生变化()
    {
        _safeArea.Current = Notch;
        var coordinator = CreateCoordinator();
        await coordinator.StartAsync();

        await coordinator.DisposeAsync();

        Assert.Equal(1, Module.CountOf("stop"));
        Assert.True(Module.IsDisposed);
        Assert.Equal(0, _safeArea.SubscriberCount);

        var pushedBefore = Module.CountOf("setSafeArea");
        _safeArea.Raise(new SafeAreaInsets(10, 0, 10, 0));
        coordinator.OnStatusBarIconsChanged("light");
        await coordinator.OnViewportChanged();

        Assert.Equal(pushedBefore, Module.CountOf("setSafeArea"));
        Assert.Empty(_statusBar.Styles);
    }

    [Fact]
    public async Task 重复释放是安全的()
    {
        var coordinator = CreateCoordinator();
        await coordinator.StartAsync();

        await coordinator.DisposeAsync();
        await coordinator.DisposeAsync();

        Assert.Equal(1, Module.CountOf("stop"));
    }

    [Fact]
    public async Task 释放后再启动不会加载模块()
    {
        var coordinator = CreateCoordinator();
        await coordinator.DisposeAsync();

        await coordinator.StartAsync();

        Assert.Empty(_js.ImportedPaths);
        Assert.Equal(0, _safeArea.SubscriberCount);
    }

    [Fact]
    public async Task 电路已断开时启动静默放弃_不抛异常()
    {
        _js.ThrowDisconnectedOnImport = true;

        var exception = await Record.ExceptionAsync(() => CreateCoordinator().StartAsync());

        Assert.Null(exception);
        Assert.Empty(Module.Calls);
        Assert.Empty(_logger.Entries);
    }

    // ImmersiveHost 在 Main.razor 根部、不在任何 ErrorBoundary 里：从协调器抛出的 JS 异常会直接变成整个应用的「未处理异常」提示条
    // （iOS 真机上登录页底部的 error 就是这个提示条的位置）。沉浸式只是外观增强，脚本出错必须被兜住，但要留下错误日志

    [Fact]
    public async Task 脚本加载失败时启动不抛异常_记一条错误日志()
    {
        _js.ThrowScriptErrorOnImport = true;

        var exception = await Record.ExceptionAsync(() => CreateCoordinator().StartAsync());

        Assert.Null(exception);
        Assert.Empty(Module.Calls);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<JSException>(entry.Exception);
        Assert.Contains("启动", entry.Message);
    }

    [Fact]
    public async Task 开始监听页面的脚本函数出错时启动不抛异常_记一条错误日志()
    {
        Module.ThrowScriptErrorOn = "start";

        var exception = await Record.ExceptionAsync(() => CreateCoordinator().StartAsync());

        Assert.Null(exception);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("启动", entry.Message);
    }

    [Fact]
    public async Task 推送安全区的脚本函数出错时不抛异常_也不影响后面开始监听页面()
    {
        _safeArea.Current = Notch;
        Module.ThrowScriptErrorOn = "setSafeArea";

        var exception = await Record.ExceptionAsync(() => CreateCoordinator().StartAsync());

        Assert.Null(exception);
        Assert.Equal(1, Module.CountOf("start"));
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("同步安全区", entry.Message);
    }

    [Fact]
    public async Task 原生安全区变化时脚本出错同样不抛异常()
    {
        _safeArea.Current = Notch;
        var coordinator = CreateCoordinator();
        await coordinator.StartAsync();
        Module.ThrowScriptErrorOn = "setSafeArea";

        // 系统键盘弹出：底部边距变大，需要重新推送；这一次脚本出错
        _safeArea.Current = new SafeAreaInsets(47, 0, 336, 0);
        var exception = await Record.ExceptionAsync(() => coordinator.OnViewportChanged());

        Assert.Null(exception);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("同步安全区", entry.Message);
    }

    [Fact]
    public async Task 停止监听时脚本出错也不抛异常_释放照常完成()
    {
        _safeArea.Current = Notch;
        var coordinator = CreateCoordinator();
        await coordinator.StartAsync();
        Module.ThrowScriptErrorOn = "stop";

        var exception = await Record.ExceptionAsync(() => coordinator.DisposeAsync().AsTask());

        Assert.Null(exception);
        Assert.Equal(0, _safeArea.SubscriberCount);
        var entry = Assert.Single(_logger.Entries);
        Assert.Contains("停止", entry.Message);
    }
}
