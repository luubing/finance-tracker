using System.Text;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 待确认账单页的展示规则（纯函数，单元测试覆盖）：卡片文字、来源行、权限状态文案、校验与各处提示。
/// 页面只负责取数与交互，字符串拼接、截断、状态判断都集中在这里。
/// 「怎样把待确认项变成正式账单」的规则（分类 / 支付渠道的匹配、账单字段）在 <see cref="PendingBillConversion"/>。
/// </summary>
public static class PendingBillPresentation
{
    /// <summary>备注摘要的最大字数（超出加「…」），与旧页面一致</summary>
    public const int NoteMaxLength = 40;

    /// <summary>卡片金额文字的字符数达到它就缩小一档，八位数的金额也放得进一行</summary>
    public const int LongAmountLength = 11;

    // ---- 页面文案 ----

    /// <summary>页面标题</summary>
    public const string Title = "待确认账单";

    /// <summary>列表上方右侧的说明：待确认的账单不进统计</summary>
    public const string StatsNote = "确认前不计入统计";

    /// <summary>空状态标题</summary>
    public const string EmptyTitle = "暂无待确认账单";

    /// <summary>空状态说明</summary>
    public const string EmptyText = "支付后，微信/支付宝通知与银行扣款短信会自动出现在这里";

    /// <summary>右上角「全部忽略」</summary>
    public const string IgnoreAllLabel = "全部忽略";

    /// <summary>「全部忽略」确认 Alert 的标题</summary>
    public const string IgnoreAllTitle = "全部忽略？";

    /// <summary>卡片上的「确认入账」</summary>
    public const string ConfirmLabel = "确认入账";

    /// <summary>卡片上的「忽略」</summary>
    public const string IgnoreLabel = "忽略";

    /// <summary>账本芯片没有选中账本时的文字</summary>
    public const string ChooseLedgerText = "选择账本";

    /// <summary>「确认账单」Sheet 键盘上保存键的文字</summary>
    public const string SheetSaveLabel = "入账";

    /// <summary>「确认账单」Sheet 键盘上保存键在保存中的文字</summary>
    public const string SheetSavingLabel = "入账中";

    /// <summary>「确认账单」Sheet 的标题</summary>
    public const string ConfirmSheetTitle = "确认账单";

    /// <summary>入账成功的提示</summary>
    public const string ConfirmedMessage = "账单创建成功";

    /// <summary>忽略一笔的提示</summary>
    public const string IgnoredMessage = "已忽略";

    /// <summary>全部忽略之后的提示</summary>
    public const string IgnoredAllMessage = "已全部忽略";

    /// <summary>金额没输入或不是正数</summary>
    public const string InvalidAmountMessage = "请输入有效的金额";

    /// <summary>没有选支付渠道</summary>
    public const string ChooseChannelMessage = "请选择支付渠道";

    /// <summary>没有选账本（账本必填）</summary>
    public const string ChooseLedgerMessage = "请选择账本";

    /// <summary>支付渠道没能自动匹配到时，Sheet 顶部的提示</summary>
    public const string ChannelNotMatchedHint = "没能匹配到支付渠道，请选择一个再入账";

    // ---- 权限状态 ----

    /// <summary>两项自动识别都开启时状态条上的一句话</summary>
    public const string CaptureReadyText = "通知栏识别、短信识别均已开启";

    /// <summary>状态条右侧的入口（两项都开启时）</summary>
    public const string CaptureSettingsLabel = "设置";

    /// <summary>未开启时的操作</summary>
    public const string EnableLabel = "去开启";

    /// <summary>已开启的状态文字</summary>
    public const string EnabledText = "已开启";

    /// <summary>通知栏识别</summary>
    public const string NotificationTitle = "通知栏识别";

    /// <summary>通知栏识别未开启时的说明（沿用旧页面的文案）</summary>
    public const string NotificationHint = "开启“通知使用权”后，微信/支付宝的支付通知会自动填入待确认账单";

    /// <summary>短信识别</summary>
    public const string SmsTitle = "短信识别";

    /// <summary>短信识别未开启时的说明（沿用旧页面的文案）</summary>
    public const string SmsHint = "授予短信权限后，银行扣款短信会自动填入待确认账单";

    /// <summary>短信权限授予之后的提示</summary>
    public const string SmsGrantedMessage = "短信权限已授予";

    /// <summary>短信权限没有授予（用户拒绝，或当前平台不支持读取短信）</summary>
    public const string SmsNotGrantedMessage = "没有获得短信权限，可以稍后再试，或在系统设置里开启";

    /// <summary>跳去系统「通知使用权」设置页之后的提示（开启后回到应用即可）</summary>
    public const string NotificationSettingsMessage = "请在系统设置里开启“通知使用权”，开启后回到这里";

    /// <summary>「设置」菜单的标题</summary>
    public const string SettingsSheetTitle = "自动识别设置";

    /// <summary>「设置」菜单：系统的通知使用权设置</summary>
    public const string SettingsNotificationItem = "通知使用权（系统设置）";

    /// <summary>「设置」菜单：打开短信识别页</summary>
    public const string SettingsSmsItem = "短信识别（读取历史短信）";

    /// <summary>
    /// 两项自动识别的整体状态：都开启 / 只开了一项 / 都没开。
    /// </summary>
    public static CaptureStatus CaptureStatusOf(bool notificationEnabled, bool smsEnabled) =>
        (notificationEnabled, smsEnabled) switch
        {
            (true, true) => CaptureStatus.Ready,
            (false, false) => CaptureStatus.Off,
            _ => CaptureStatus.Partial,
        };

    // ---- 列表 ----

    /// <summary>列表上方左侧的数量：「3 笔待确认」</summary>
    public static string CountText(int count) => $"{Math.Max(0, count)} 笔待确认";

    /// <summary>
    /// 数据来源的文字：通知栏 / 短信识别。待确认账单只会来自这两种来源，别的来源（不会出现）统一叫「自动识别」。
    /// </summary>
    public static string SourceLabel(BillSource source)
    {
        var label = HomePresentation.SourceLabel(source);
        return label.Length == 0 ? "自动识别" : label;
    }

    /// <summary>来源行：「微信支付 · 通知栏」；渠道为空时只有来源</summary>
    public static string SourceLine(string? channel, BillSource source)
    {
        var label = SourceLabel(source);
        var name = channel?.Trim();

        return string.IsNullOrEmpty(name) ? label : $"{name} · {label}";
    }

    /// <summary>
    /// 备注摘要：折叠换行与连续空白，最多 <paramref name="maxLength"/> 个字，超出加「…」（不会把一个表情符号从中间截断）。
    /// 通知 / 短信的原文常常带换行，卡片上只放一行。
    /// </summary>
    public static string NoteSummary(string? note, int maxLength = NoteMaxLength)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(note.Length);
        var previousWasSpace = false;

        foreach (var ch in note.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
            }
            else
            {
                builder.Append(ch);
                previousWasSpace = false;
            }
        }

        var text = builder.ToString();
        if (text.Length <= maxLength)
        {
            return text;
        }

        var cut = Math.Max(0, maxLength);
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return text[..cut] + "…";
    }

    /// <summary>账单类型的文字：支出 / 收入</summary>
    public static string TypeLabel(BillType type) => type == BillType.Income ? "收入" : "支出";

    /// <summary>卡片金额：支出带负号（-¥35.00），收入带正号（+¥35.00）</summary>
    public static string AmountText(decimal amount, BillType type) =>
        MoneyPresentation.Signed(amount, type == BillType.Income);

    /// <summary>金额文字很长（位数多）时卡片缩小一档字号</summary>
    public static bool IsLongAmount(string amountText) => amountText.Length >= LongAmountLength;

    /// <summary>
    /// 卡片上的时间：今天 / 昨天 + HH:mm；同年「11月23日 09:30」；跨年带年份。
    /// 待确认项的交易时间与账单录入一样，是应用时区的墙上时间（通知 / 短信捕获时就是设备本地时间），不再换算。
    /// </summary>
    /// <param name="transactionTime">交易时间（应用时区的墙上时间）</param>
    /// <param name="nowLocal">应用时区的现在（<c>AppTimeZone.ToLocal(DateTime.UtcNow)</c>）</param>
    public static string TimeText(DateTime transactionTime, DateTime nowLocal) =>
        AddBillPresentation.TimeLabel(transactionTime, nowLocal);

    /// <summary>读屏摘要：「支出 ¥35.00，微信支付 · 通知栏，今天 12:08」</summary>
    public static string Summary(BillType type, decimal amount, string sourceLine, string timeText) =>
        $"{TypeLabel(type)} {MoneyPresentation.Format(Math.Abs(amount))}，{sourceLine}，{timeText}";

    /// <summary>
    /// 一张卡片的全部展示内容。识别结果里没有分类：显示的是「确认入账时会使用的分类」（与入账规则同一个，
    /// 由 <see cref="PendingBillConversion.PickCategory"/> 选出），所以卡片上看到什么，入账后账单就是什么。
    /// </summary>
    /// <param name="bill">待确认账单</param>
    /// <param name="category">确认入账时会使用的分类；没有可用分类时为空（显示「其他支出 / 其他收入」，入账时会提示去建分类）</param>
    /// <param name="channel">与待确认项的支付渠道名匹配上的渠道；没匹配上为空</param>
    /// <param name="nowLocal">应用时区的现在</param>
    public static PendingCardModel BuildCard(PendingBill bill, Category? category, PaymentChannel? channel, DateTime nowLocal)
    {
        var isIncome = bill.Type == BillType.Income;
        var sourceLine = SourceLine(bill.Channel, bill.Source);
        var time = TimeText(bill.TransactionTime, nowLocal);

        return new PendingCardModel(
            bill.Id,
            sourceLine,
            channel is null ? PaymentChannelPresentation.DefaultIcon : PaymentChannelPresentation.NormalizeIcon(channel.Icon),
            time,
            category?.Name ?? (isIncome ? "其他收入" : "其他支出"),
            BillPresentation.CategoryIcon(category?.Icon),
            BillPresentation.CategoryTone(category?.Name, category?.Id),
            NoteSummary(bill.Note),
            AmountText(bill.Amount, bill.Type),
            isIncome,
            bill.Source == BillSource.SmsRecognition,
            Summary(bill.Type, bill.Amount, sourceLine, time));
    }

    // ---- 账本 ----

    /// <summary>账本芯片上的文字：账本名；没选中账本时「选择账本」</summary>
    public static string LedgerChipText(string? ledgerName) =>
        string.IsNullOrWhiteSpace(ledgerName) ? ChooseLedgerText : ledgerName;

    /// <summary>
    /// 一张卡片实际使用的账本：用户在这张卡片上选过的优先，其次默认账本；两者都已不在可选账本里（被删了）就是没有。
    /// 账本必填，没有时「确认入账」会被拦下并弹出账本菜单。
    /// </summary>
    /// <param name="chosen">用户在这张卡片上选的账本</param>
    /// <param name="defaultLedger">默认账本（用户设置的默认账本，否则第一个自己的账本）</param>
    /// <param name="ledgers">可选账本（用户自己的账本）</param>
    public static Guid? ResolveLedger(Guid? chosen, Guid? defaultLedger, IEnumerable<LedgerModel> ledgers)
    {
        var ids = ledgers.Select(l => l.Id).ToHashSet();

        if (chosen is { } picked && ids.Contains(picked))
        {
            return picked;
        }

        return defaultLedger is { } fallback && ids.Contains(fallback) ? fallback : null;
    }

    // ---- 校验与提示 ----

    /// <summary>
    /// 确认入账前的校验，返回第一条要告诉用户的话；都通过返回 null。
    /// 顺序：金额 → 支付渠道 → 账本（账本必填，服务层还会再校验一次）。
    /// </summary>
    public static string? Validate(decimal? amount, Guid? channelId, Guid? ledgerId)
    {
        if (amount is not > 0m)
        {
            return InvalidAmountMessage;
        }

        if (channelId is null || channelId == Guid.Empty)
        {
            return ChooseChannelMessage;
        }

        if (ledgerId is null || ledgerId == Guid.Empty)
        {
            return ChooseLedgerMessage;
        }

        return null;
    }

    /// <summary>「全部忽略」确认 Alert 的说明</summary>
    public static string IgnoreAllMessage(int count) => $"{count} 笔待确认账单将被移除，不会计入账单。";

    /// <summary>入账失败的提示（意外错误，沿用旧页面的文案）</summary>
    public static string CreateFailedMessage(string reason) => $"创建账单失败: {reason}";

    /// <summary>
    /// 账单已经保存、但从待确认列表移除失败时的提示：不能说「创建失败」（用户会重试、产生重复账单），
    /// 要明确告诉他账单已经有了，这条待确认项请点「忽略」。
    /// </summary>
    public static string RemoveFailedMessage(string reason) =>
        $"账单已创建，但从待确认列表移除失败（{reason}）。这条待确认项下次打开页面时还会出现，请点「忽略」，不要重复确认";

    /// <summary>没有可用分类（入账时无法选出分类）的提示</summary>
    public static string NoCategoryMessage(BillType type) => $"没有可用的{TypeLabel(type)}分类，请先在「分类」里新建一个";

    /// <summary>读取账本 / 分类 / 支付渠道失败的提示</summary>
    public static string LoadFailedMessage(string reason) => $"加载失败: {reason}";

    /// <summary>忽略（移除待确认项）失败的提示</summary>
    public static string IgnoreFailedMessage(string reason) => $"忽略失败: {reason}";

    /// <summary>申请权限 / 打开系统设置失败的提示</summary>
    public static string PermissionFailedMessage(string reason) => $"开启失败: {reason}";

    /// <summary>用户在「确认账单」Sheet 里改的时候，这笔待确认项已经不在了（被忽略 / 被清理）</summary>
    public const string GoneMessage = "这笔待确认账单已经不在了";

    // ---- 权限行（待确认账单页的状态条与短信识别页的权限分组共用） ----

    /// <summary>通知栏识别一行的副标题：已开启不需要说明；未开启给开启后的好处</summary>
    public static string? NotificationSubtitle(bool enabled) => enabled ? null : NotificationHint;

    /// <summary>短信识别一行的副标题：已开启不需要说明；未开启给授权后的好处</summary>
    public static string? SmsSubtitle(bool enabled) => enabled ? null : SmsHint;

    /// <summary>权限行的读屏名称：「通知栏识别，未开启」</summary>
    public static string PermissionAriaLabel(string title, bool enabled) =>
        $"{title}，{(enabled ? EnabledText : "未开启")}";
}
