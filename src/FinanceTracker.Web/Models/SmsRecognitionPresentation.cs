using System.Globalization;
using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 一次读取之后弹出的提示：文字与级别
/// </summary>
/// <param name="Message">提示文字</param>
/// <param name="Level">级别（成功 / 信息 / 警告 / 错误）</param>
public readonly record struct SmsToast(string Message, SmsToastLevel Level);

/// <summary>
/// 提示的级别（页面把它映射成对应颜色的 Snackbar）
/// </summary>
public enum SmsToastLevel
{
    Success,
    Info,
    Warning,
    Error,
}

/// <summary>
/// 短信识别页的展示规则（纯函数，单元测试覆盖）：读取范围的日期、读取结果摘要与每条短信的去向文字、各处提示。
/// 读到的支付短信放进「待确认账单」的规则见 <see cref="SmsCapture"/>；权限状态的文案与待确认账单页共用（<see cref="PendingBillPresentation"/>）。
/// </summary>
public static class SmsRecognitionPresentation
{
    /// <summary>读取范围默认回溯的天数</summary>
    public const int DefaultRangeDays = 30;

    /// <summary>短信正文在结果行里最多显示的字数（超出加「…」）</summary>
    public const int BodyMaxLength = 120;

    /// <summary>日期输入控件的格式</summary>
    public const string InputDateFormat = DateInputPresentation.InputFormat;

    /// <summary>页面标题</summary>
    public const string Title = "短信识别";

    /// <summary>权限分组的小标题</summary>
    public const string PermissionGroupTitle = "权限";

    /// <summary>读取范围分组的小标题</summary>
    public const string RangeGroupTitle = "读取范围";

    /// <summary>开始日期一行的标题</summary>
    public const string FromDateTitle = "开始日期";

    /// <summary>读取范围分组的脚注：读到的短信去哪、重复读取会怎样（权限已授予时）</summary>
    public const string RangeFooter = "读到的支付短信会加入「待确认账单」，已经在待确认列表里的不会重复加入";

    /// <summary>权限未授予时，读取范围分组的脚注</summary>
    public const string NeedPermissionFooter = "授予短信读取权限后，才能读取其中的支付短信";

    /// <summary>底部固定的主按钮</summary>
    public const string ReadLabel = "读取短信";

    /// <summary>读取中的按钮文字</summary>
    public const string ReadingLabel = "读取中";

    /// <summary>读取完没有结果的标题（空状态与提示共用）</summary>
    public const string NoResultText = "未找到支付类短信";

    /// <summary>还没读取过时，结果区的提示</summary>
    public const string IdleText = "选好开始日期，点「读取短信」，把其中的支付短信加入待确认账单";

    /// <summary>短信权限一行的标题</summary>
    public const string PermissionTitle = "短信读取权限";

    /// <summary>短信权限已授予的副标题</summary>
    public const string PermissionGrantedText = "已开启，可以读取支付短信";

    /// <summary>权限未授予时的操作</summary>
    public const string GrantLabel = "去授权";

    /// <summary>权限授予之后的提示</summary>
    public const string GrantedMessage = "权限已授予";

    /// <summary>读取结果摘要分组的小标题</summary>
    public const string SummaryGroupTitle = "读取结果";

    /// <summary>摘要行：新加入待确认账单（点按去待确认账单页）</summary>
    public const string AddedRowTitle = "新加入待确认账单";

    /// <summary>摘要行：待确认列表里已有，没有重复加入</summary>
    public const string DuplicateRowTitle = "已在待确认列表里";

    /// <summary>摘要行：没识别出金额，已跳过</summary>
    public const string NoAmountRowTitle = "没识别出金额，已跳过";

    /// <summary>摘要行：待确认账单已满，没加入</summary>
    public const string OverCapacityRowTitle = "待确认账单已满，未加入";

    /// <summary>摘要行：写入出错</summary>
    public const string FailedRowTitle = "加入失败";

    /// <summary>默认的开始日期：今天往前 <see cref="DefaultRangeDays"/> 天（参数是应用时区的现在）</summary>
    public static DateTime DefaultFromDate(DateTime nowLocal) => nowLocal.Date.AddDays(-DefaultRangeDays);

    /// <summary>开始日期一行右侧的文字：「2026年10月26日」（规则与自定义报表共用，见 <see cref="DateInputPresentation"/>）</summary>
    public static string DateLabel(DateTime date) => DateInputPresentation.Label(date);

    /// <summary>日期 → 日期输入控件的值（yyyy-MM-dd，不随设备区域设置变化）</summary>
    public static string ToInputValue(DateTime date) => DateInputPresentation.ToInputValue(date);

    /// <summary>日期输入控件的值 → 日期。空或格式不对返回 false，调用方沿用原来的日期。</summary>
    public static bool TryParseInputValue(string? value, out DateTime date) => DateInputPresentation.TryParse(value, out date);

    /// <summary>结果列表的小标题：「识别到 3 条支付短信」</summary>
    public static string ResultsHeader(int count) => $"识别到 {count} 条支付短信";

    /// <summary>结果行的第二行：「95588 · 2026-11-25 12:08」；没有号码时只有时间</summary>
    public static string MetaText(string? address, DateTime date)
    {
        var time = date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var from = address?.Trim();

        return string.IsNullOrEmpty(from) ? time : $"{from} · {time}";
    }

    /// <summary>
    /// 短信正文摘要：折叠换行与连续空白，最多 <see cref="BodyMaxLength"/> 字，超出加「…」（不会把表情符号从中间截断）。
    /// </summary>
    public static string BodySummary(string? body) => PendingBillPresentation.NoteSummary(body, BodyMaxLength);

    /// <summary>读取失败的提示（沿用旧页面的文案）</summary>
    public static string ReadFailedMessage(string reason) => $"读取短信失败: {reason}";

    /// <summary>短信读到了，但加入待确认账单时出了意外</summary>
    public static string CaptureFailedMessage(string reason) => $"加入待确认账单失败: {reason}";

    /// <summary>摘要行右侧的条数：「3 条」</summary>
    public static string CountText(int count) => $"{count} 条";

    /// <summary>读屏名称：「新加入待确认账单 3 条」行是个按钮，点了去待确认账单页</summary>
    public static string AddedRowAriaLabel(int count) => $"{AddedRowTitle} {count} 条，前往待确认账单";

    /// <summary>
    /// 摘要分组的脚注：最需要用户知道的那一件事。
    /// 容量满了最重要（较早的付款没加入，用户要先处理待确认账单再重读）→ 写入出错（稍后重读）→ 有新加入的（去哪确认、账本在哪选）。
    /// </summary>
    public static string? SummaryFooter(SmsCaptureResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.OverCapacity > 0)
        {
            return $"待确认账单最多保留 {IPendingBillService.MaxCount} 条：较早的付款没有加入，处理掉一些待确认账单后重新读取即可";
        }

        if (result.Failed > 0)
        {
            return "加入失败的短信可以稍后重新读取";
        }

        return result.Added > 0 ? "到「待确认账单」确认入账，账本在那里选" : null;
    }

    /// <summary>每条短信右下角的去向标签</summary>
    public static string OutcomeLabel(SmsCaptureOutcome outcome) => outcome switch
    {
        SmsCaptureOutcome.Added => "已加入待确认",
        SmsCaptureOutcome.Duplicate => "已在待确认列表",
        SmsCaptureOutcome.NoAmount => "没识别出金额",
        SmsCaptureOutcome.OverCapacity => "列表已满，未加入",
        SmsCaptureOutcome.Failed => "加入失败",
        _ => string.Empty,
    };

    /// <summary>去向标签的配色（<c>ft-badge--{tone}</c>）：加入了 = 成功色；重复 = 中性；没识别 / 列表已满 = 警告；出错 = 危险</summary>
    public static string OutcomeTone(SmsCaptureOutcome outcome) => outcome switch
    {
        SmsCaptureOutcome.Added => "success",
        SmsCaptureOutcome.Duplicate => "neutral",
        SmsCaptureOutcome.NoAmount => "warning",
        SmsCaptureOutcome.OverCapacity => "warning",
        SmsCaptureOutcome.Failed => "danger",
        _ => "neutral",
    };

    /// <summary>去向的观察值（E2E 的 data-outcome）</summary>
    public static string OutcomeValue(SmsCaptureOutcome outcome) => outcome switch
    {
        SmsCaptureOutcome.Added => "added",
        SmsCaptureOutcome.Duplicate => "duplicate",
        SmsCaptureOutcome.NoAmount => "no-amount",
        SmsCaptureOutcome.OverCapacity => "over-capacity",
        SmsCaptureOutcome.Failed => "failed",
        _ => "unknown",
    };

    /// <summary>
    /// 读取并加入待确认账单之后的提示：有新加入的 = 成功（同时有「列表已满没加入」「加入失败」的降为警告）；
    /// 一条都没加入时按原因：列表已满 = 警告、写入出错 = 错误、全是重复 / 全没识别出金额 = 信息。
    /// </summary>
    public static SmsToast CaptureToast(SmsCaptureResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Added > 0)
        {
            var message = $"已加入 {result.Added} 条待确认账单";

            if (result.OverCapacity > 0)
            {
                return new SmsToast($"{message}，{result.OverCapacity} 条因待确认账单已满没加入", SmsToastLevel.Warning);
            }

            if (result.Failed > 0)
            {
                return new SmsToast($"{message}，{result.Failed} 条加入失败", SmsToastLevel.Warning);
            }

            return new SmsToast(message, SmsToastLevel.Success);
        }

        if (result.OverCapacity > 0)
        {
            return new SmsToast("待确认账单已满，没有加入新的短信", SmsToastLevel.Warning);
        }

        if (result.Failed > 0)
        {
            return new SmsToast("加入待确认账单失败", SmsToastLevel.Error);
        }

        return result.Duplicate > 0
            ? new SmsToast("这些短信都已经在待确认列表里了", SmsToastLevel.Info)
            : new SmsToast("这些短信里没有识别出金额，没有加入待确认账单", SmsToastLevel.Info);
    }
}
