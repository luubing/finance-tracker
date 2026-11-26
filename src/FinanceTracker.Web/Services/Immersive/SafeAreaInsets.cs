namespace FinanceTracker.Web.Services;

/// <summary>
/// 安全区边距（上、右、下、左），单位为 CSS 像素（Android 的 dp、iOS 的 pt）。
/// 状态栏、刘海 / 灵动岛、底部手势条、系统键盘等被系统占用的区域，页面内容不应被它们遮挡。
/// </summary>
public readonly record struct SafeAreaInsets(double Top, double Right, double Bottom, double Left)
{
    /// <summary>四边都没有被系统占用</summary>
    public static SafeAreaInsets Zero => default;
}
