using Android.App;
using FinanceTracker.Web.Services;

namespace FinanceTracker.App.Platforms.Android.Services;

/// <summary>
/// Android 渲染档位：低端设备把毛玻璃降级为不透明实色（23 号 ticket，ADR 0007 §26）。
/// 判据见 <see cref="LowEndDeviceCriteria"/>（系统标记的低内存设备，或总内存低于 3 GiB）：
/// 这里只负责从 <see cref="ActivityManager"/> 读出两个原始信号，判定本身是可单元测试的纯函数。
/// 设备的内存在进程生命周期内不会变，所以构造时判定一次、永不触发变化事件；读取失败按「不降级」处理。
/// Android 没有系统级的「降低透明度」设置，所以这条路只看设备能力。
/// </summary>
public sealed class RenderingProfileService : IRenderingProfileService
{
    public RenderingProfileService()
    {
        PreferSolidMaterials = Detect();
    }

    public bool PreferSolidMaterials { get; }

    // 判定结果在进程生命周期内不变：空访问器（订阅无效果），避免"声明了从不触发的事件"的 CS0067 警告
    public event EventHandler<bool>? PreferSolidMaterialsChanged
    {
        add { }
        remove { }
    }

    private static bool Detect()
    {
        try
        {
            if (global::Android.App.Application.Context.GetSystemService(global::Android.Content.Context.ActivityService) is not ActivityManager manager)
            {
                return false;
            }

            var memory = new ActivityManager.MemoryInfo();
            manager.GetMemoryInfo(memory);
            return LowEndDeviceCriteria.IsLowEnd(manager.IsLowRamDevice, memory.TotalMem);
        }
        catch (Exception ex)
        {
            // 判定只是体验优化：失败时保持完整质感，不影响功能
            System.Diagnostics.Debug.WriteLine($"低端设备判定失败，按不降级处理: {ex.Message}");
            return false;
        }
    }
}
