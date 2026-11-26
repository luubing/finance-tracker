namespace FinanceTracker.Web.Components.Ui;

/// <summary>
/// 按钮样式（对应设计稿 06 的按钮：玻璃圆按钮 / 主要 / 次要 / 取消 / 删除 / 文字）
/// </summary>
public enum ButtonKind
{
    /// <summary>玻璃（导航栏里的返回、重置、更多等；浅色背景之上）</summary>
    Glass,

    /// <summary>主要：品牌渐变，每屏一个</summary>
    Brand,

    /// <summary>次要：品牌色浅底</summary>
    Soft,

    /// <summary>取消 / 中性：灰色填充</summary>
    Neutral,

    /// <summary>危险（删除）：红色浅底</summary>
    Danger,

    /// <summary>纯文字按钮</summary>
    Text,

    /// <summary>玻璃 + 红字（导航栏里破坏性的文字操作，例如待确认账单页的「全部忽略」）</summary>
    GlassDanger,
}

/// <summary>
/// 左滑操作按钮的配色
/// </summary>
public enum ActionTone
{
    /// <summary>品牌色（编辑 / 成员 / 共享）</summary>
    Brand,

    /// <summary>危险红（删除 / 移除）</summary>
    Danger,

    /// <summary>中性灰（更多）</summary>
    Neutral,

    /// <summary>警示橙（归档等）</summary>
    Warning,
}

/// <summary>
/// 徽标样式
/// </summary>
public enum BadgeKind
{
    /// <summary>品牌色（默认 / 共享）</summary>
    Brand,

    /// <summary>中性灰（只读等）</summary>
    Neutral,

    /// <summary>成功绿（已同步等）</summary>
    Success,

    /// <summary>警告橙</summary>
    Warning,

    /// <summary>危险红</summary>
    Danger,
}

/// <summary>
/// 金额展示的字号档位（整数部分大字，小数部分按档位缩小）
/// </summary>
public enum AmountSize
{
    /// <summary>特大（首页 Hero 的主指标；小数部分明显缩小、变淡）</summary>
    Xl,

    /// <summary>卡片（Hero 小卡、汇总卡片；整数小数同大，20px 粗体）</summary>
    Lg,

    /// <summary>列表（单行金额；整数小数同大，16.5px 半粗）</summary>
    Md,
}

/// <summary>
/// 进度条的配色（与业务含义无关；预算的「正常 / 接近上限 / 超支」由调用方映射）
/// </summary>
public enum ProgressTone
{
    /// <summary>品牌色</summary>
    Brand,

    /// <summary>警示橙</summary>
    Warning,

    /// <summary>危险红</summary>
    Danger,
}
