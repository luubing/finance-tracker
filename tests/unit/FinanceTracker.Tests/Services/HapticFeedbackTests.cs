using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 触感反馈（Tab 点按）：共享库里只有接口与空实现，平台实现留在 MAUI 项目。
/// 触感本身无法在单元测试里发生，这里钉住「抽象是否接对」——空实现安全、两个宿主都注册了实现、
/// Android / iOS 分别用对了系统 API、Tab 栏点按时确实会调用。
/// </summary>
public sealed class HapticFeedbackTests
{
    private static string Source(string relativePath) => File.ReadAllText(RepoRoot.Combine(relativePath));

    [Fact]
    public void 空实现可以反复调用而不抛异常()
    {
        IHapticFeedbackService haptics = new NoOpHapticFeedbackService();

        for (var i = 0; i < 100; i++)
        {
            haptics.SelectionChanged();
        }
    }

    [Fact]
    public void WebServer宿主注册的是空实现()
    {
        var program = Source("server/FinanceTracker.Web.Server/Program.cs");

        Assert.Contains("AddSingleton<IHapticFeedbackService, NoOpHapticFeedbackService>()", program);
    }

    [Fact]
    public void MAUI宿主按平台注册_Android与iOS用原生实现_其余平台用空实现()
    {
        var program = Source("src/FinanceTracker.App/MauiProgram.cs");

        var android = program.IndexOf("Platforms.Android.Services.HapticFeedbackService", StringComparison.Ordinal);
        var ios = program.IndexOf("Platforms.iOS.Services.HapticFeedbackService", StringComparison.Ordinal);
        var noop = program.IndexOf("NoOpHapticFeedbackService", StringComparison.Ordinal);

        Assert.True(android >= 0, "MauiProgram 没有注册 Android 触感实现");
        Assert.True(ios > android, "MauiProgram 没有注册 iOS 触感实现（应在 #elif IOS 分支）");
        Assert.True(noop > ios, "MauiProgram 没有为其余平台注册空实现（应在 #else 分支）");
    }

    [Fact]
    public void Android实现用视图级的时钟刻度反馈_不需要震动权限()
    {
        var source = Source("src/FinanceTracker.App/Platforms/Android/Services/HapticFeedbackService.cs");

        Assert.Contains("FeedbackConstants.ClockTick", source);
        Assert.Contains("PerformHapticFeedback", source);
        Assert.DoesNotContain("Vibrator", source);
    }

    [Fact]
    public void iOS实现用轻触冲击反馈()
    {
        var source = Source("src/FinanceTracker.App/Platforms/iOS/Services/HapticFeedbackService.cs");

        Assert.Contains("UIImpactFeedbackStyle.Light", source);
        Assert.Contains("ImpactOccurred", source);
    }

    [Fact]
    public void Tab栏点按时先给触感再导航()
    {
        var razor = Source("src/FinanceTracker.Web/Components/GlassTabBar.razor");

        var haptics = razor.IndexOf("Haptics.SelectionChanged()", StringComparison.Ordinal);
        var plan = razor.IndexOf("TabNavigation.PlanTap(", StringComparison.Ordinal);

        Assert.True(haptics >= 0, "GlassTabBar 点按 Tab 时没有调用触感反馈");
        Assert.True(plan > haptics, "触感应当在导航决策之前触发，不被导航耗时拖后");
    }
}
