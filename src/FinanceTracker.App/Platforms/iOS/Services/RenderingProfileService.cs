using FinanceTracker.Web.Services;
using Foundation;
using UIKit;

namespace FinanceTracker.App.Platforms.iOS.Services;

/// <summary>
/// iOS 渲染档位：跟随系统「降低透明度」（设置 → 辅助功能 → 显示与文字大小 → 降低透明度），打开时把毛玻璃降级为不透明实色
/// （23 号 ticket，ADR 0007 §26）。读取 <c>UIAccessibility.IsReduceTransparencyEnabled</c>，并监听
/// <c>ReduceTransparencyStatusDidChangeNotification</c>——用户在设置里切换后回到应用即生效，不用重启。
/// 网页引擎自己也可能按 <c>prefers-reduced-transparency</c> 媒体特性降级（取决于 WKWebView 的版本）：两条路取值相同、任一成立即不透明。
/// 读取失败按「不降级」处理。
/// </summary>
public sealed class RenderingProfileService : IRenderingProfileService
{
    // 持有观察者引用：单例生命周期内一直监听（与 SafeAreaService 一致）
    private readonly List<NSObject> _observers = new();
    private volatile bool _preferSolid;

    public RenderingProfileService()
    {
        _preferSolid = Read();
        // 通知名是 UIKit 的全局常量 UIAccessibilityReduceTransparencyStatusDidChangeNotification；绑定库把它挂在 UIApplication 等类型上（取值相同）
        _observers.Add(NSNotificationCenter.DefaultCenter.AddObserver(
            UIApplication.ReduceTransparencyStatusDidChangeNotification, _ => OnStatusChanged()));
    }

    public bool PreferSolidMaterials => _preferSolid;

    public event EventHandler<bool>? PreferSolidMaterialsChanged;

    private void OnStatusChanged()
    {
        var next = Read();
        if (next == _preferSolid)
        {
            return;
        }

        _preferSolid = next;
        PreferSolidMaterialsChanged?.Invoke(this, next);
    }

    private static bool Read()
    {
        try
        {
            return UIAccessibility.IsReduceTransparencyEnabled;
        }
        catch (Exception ex)
        {
            // 判定只是体验优化：失败时保持完整质感，不影响功能
            System.Diagnostics.Debug.WriteLine($"读取「降低透明度」失败，按不降级处理: {ex.Message}");
            return false;
        }
    }
}
