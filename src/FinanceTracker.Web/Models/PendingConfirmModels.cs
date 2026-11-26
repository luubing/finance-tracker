namespace FinanceTracker.Web.Models;

/// <summary>
/// 自动识别（通知栏识别 + 短信识别）的开启状态。
/// </summary>
public enum CaptureStatus
{
    /// <summary>两项都已开启</summary>
    Ready,

    /// <summary>只开启了其中一项</summary>
    Partial,

    /// <summary>两项都没开启（含不支持通知栏 / 短信读取的平台：Web 与 iOS 宿主使用空实现，权限恒为未开启）</summary>
    Off,
}

/// <summary>
/// 待确认账单卡片需要的全部展示内容（由 <see cref="PendingBillPresentation.BuildCard"/> 一次算好；卡片组件只负责排版与点按）。
/// </summary>
/// <param name="Id">待确认账单 Id</param>
/// <param name="SourceLine">来源行：「微信支付 · 通知栏」</param>
/// <param name="ChannelIcon">来源行前的图标：与应用里同名支付渠道同一个图标；匹配不到渠道时用通用银行卡图标</param>
/// <param name="TimeText">时间：「今天 12:08」「昨天 20:15」「11月23日 09:30」</param>
/// <param name="CategoryName">确认入账时会使用的分类名（识别结果里没有分类，沿用既有规则：该类型下的「其他…」分类）</param>
/// <param name="CategoryIcon">分类图标</param>
/// <param name="Tone">分类图标块配色</param>
/// <param name="NoteText">备注摘要（单行，最多 40 字）；没有备注为空串</param>
/// <param name="AmountText">金额：支出「-¥35.00」，收入「+¥35.00」</param>
/// <param name="IsIncome">是否收入</param>
/// <param name="IsSms">数据来源是否短信识别（否则是通知栏）</param>
/// <param name="Summary">读屏摘要：「支出 ¥35.00，微信支付 · 通知栏，今天 12:08」（卡片上的按钮读屏时用它区分是哪一笔）</param>
public sealed record PendingCardModel(
    Guid Id,
    string SourceLine,
    string ChannelIcon,
    string TimeText,
    string CategoryName,
    string CategoryIcon,
    string Tone,
    string NoteText,
    string AmountText,
    bool IsIncome,
    bool IsSms,
    string Summary);

/// <summary>
/// 「确认账单」Sheet 打开时的初始内容：待确认账单页（修正后确认入账）使用；短信识别页读到的短信先进待确认账单，也是从这里确认。
/// </summary>
/// <param name="Title">Sheet 标题：「确认账单」</param>
/// <param name="SourceLine">来源行：「微信支付 · 通知栏」「短信识别 · 95588」</param>
/// <param name="TimeText">时间文字（只读）</param>
/// <param name="NoteText">备注摘要（只读）；为空不显示</param>
/// <param name="Hint">顶部的提示条（例如「没有匹配到支付渠道，请选择」）；为空不显示</param>
/// <param name="Amount">初始金额；为空表示让用户自己输入</param>
/// <param name="IsIncome">初始类型</param>
/// <param name="ChannelId">初始支付渠道；为空表示没能匹配到，需要用户选择</param>
/// <param name="LedgerId">初始账本；为空表示还没有可用的账本，需要用户选择或新建</param>
/// <param name="SaveLabel">键盘上保存键的文字</param>
public sealed record PendingConfirmDraft(
    string Title,
    string SourceLine,
    string TimeText,
    string? NoteText,
    string? Hint,
    decimal? Amount,
    bool IsIncome,
    Guid? ChannelId,
    Guid? LedgerId,
    string SaveLabel = PendingBillPresentation.SheetSaveLabel);

/// <summary>
/// 用户在「确认账单」Sheet 里最终确认的内容（金额、类型、支付渠道、账本都已通过校验）。
/// </summary>
public readonly record struct PendingConfirmValues(decimal Amount, bool IsIncome, Guid ChannelId, Guid LedgerId);

/// <summary>入账结果的三种走向</summary>
public enum ConfirmStatus
{
    /// <summary>账单已保存：Sheet 收起（<see cref="ConfirmResult.Message"/> 非空表示保存成功但有话要说）</summary>
    Saved,

    /// <summary>业务校验没通过（文案面向用户、用户能立刻改，例如该类型下没有分类）：Sheet 保持打开</summary>
    Invalid,

    /// <summary>意外失败：Sheet 保持打开，用户可以重试</summary>
    Failed,
}

/// <summary>
/// 入账的结果：页面做完「创建账单」之后，既用它决定提示什么（<see cref="Message"/> 是要告诉用户的话），
/// 也告诉 Sheet 该收起还是留着（只有 <see cref="ConfirmStatus.Saved"/> 才收起）。
/// </summary>
public readonly record struct ConfirmResult(ConfirmStatus Status, string? Message = null)
{
    /// <summary>账单已保存；<paramref name="warning"/> 非空表示保存成功、但有话要说（例如待确认项没能从列表移除），此时不再提示「创建成功」</summary>
    public static ConfirmResult Saved(string? warning = null) => new(ConfirmStatus.Saved, warning);

    /// <summary>业务校验没通过，<paramref name="message"/> 是要告诉用户的话</summary>
    public static ConfirmResult Invalid(string message) => new(ConfirmStatus.Invalid, message);

    /// <summary>意外失败，<paramref name="message"/> 是完整的提示（含原因）</summary>
    public static ConfirmResult Failed(string message) => new(ConfirmStatus.Failed, message);
}
