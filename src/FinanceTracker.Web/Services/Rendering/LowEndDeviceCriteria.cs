namespace FinanceTracker.Web.Services;

/// <summary>
/// 低端设备判定准则（纯函数，Android 宿主用；单独放在这里是为了能脱离设备做单元测试）。
/// <para>
/// 为什么看内存：毛玻璃（<c>backdrop-filter</c>）每一层都要多一次离屏渲染与整块模糊，掉帧最先出现在内存小、GPU 弱的机型上，
/// 而内存大小与这两者高度相关、又是 Android 上唯一稳定可读、不需要权限的信号。
/// </para>
/// <para>
/// 判据（满足任一即低端）：① 系统把它标记为低内存设备（<c>ActivityManager.isLowRamDevice</c>，Android Go 版本等）；
/// ② 系统报告的总内存低于 <see cref="LowMemoryThresholdBytes"/>。Android 报告的总内存比标称值少一截
/// （标称 2 GB 的机型约报 1.8 GiB，标称 3 GB 约报 2.7–2.9 GiB，标称 4 GB 约报 3.6–3.8 GiB），
/// 所以 3 GiB 的门槛刚好把「标称 3 GB 及以下」判为低端、「标称 4 GB 及以上」放过。
/// 读不到内存（≤ 0）时不判低端——宁可保持完整质感，也不因为读取失败误降级。
/// </para>
/// </summary>
public static class LowEndDeviceCriteria
{
    /// <summary>总内存低于该字节数判为低端设备：3 GiB。</summary>
    public const long LowMemoryThresholdBytes = 3L * 1024 * 1024 * 1024;

    /// <summary>按系统的低内存标记与总内存字节数判定是否低端设备。</summary>
    public static bool IsLowEnd(bool isLowRamDevice, long totalMemoryBytes) =>
        isLowRamDevice || (totalMemoryBytes > 0 && totalMemoryBytes < LowMemoryThresholdBytes);
}
