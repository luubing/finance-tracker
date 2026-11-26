namespace FinanceTracker.Web.Services;

/// <summary>
/// 安全区边距（上、右、下、左），单位为 CSS 像素（Android 的 dp、iOS 的 pt）。
/// 状态栏、刘海 / 灵动岛、底部手势条、系统键盘等被系统占用的区域，页面内容不应被它们遮挡。
/// </summary>
/// <param name="BottomIndicator">
/// <see cref="Bottom"/> 里「只是一条指示条」的那一部分（iOS 的 Home 指示条）。指示条只是一条细线，不挡内容、也不接收点按，
/// 悬浮在页面之上的控件（Tab 栏）可以压住这块区域，只需要避开 <c>Bottom - BottomIndicator</c>；页面内容（列表、固定在底部的按钮、Sheet）仍然要避开整个 <see cref="Bottom"/>。
/// 系统键盘、Android 的三键导航栏是实心的，不算指示条。默认 0 = 整个底部边距都要避开（Android 沿用这个行为）。
/// </param>
public readonly record struct SafeAreaInsets(double Top, double Right, double Bottom, double Left, double BottomIndicator = 0)
{
    /// <summary>四边都没有被系统占用</summary>
    public static SafeAreaInsets Zero => default;
}
