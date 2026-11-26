using System.Globalization;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 导入账单页的展示规则（纯函数，单元测试覆盖）：来源、导出路径说明、文件与预览的文字、汇总、结果与各种提示。
/// 云端的解析规则、导入接口与时区口径都不在这里（见 ADR 0005 与导入控制器）。
/// </summary>
public static class ImportPresentation
{
    /// <summary>页面标题</summary>
    public const string Title = "导入账单";

    /// <summary>可选文件的大小上限（字节，与旧页面一致）</summary>
    public const long MaxFileBytes = 10 * 1024 * 1024;

    /// <summary>预览列表最多显示的账单条数：几千行的账单一次渲染出来会拖垮页面；汇总与导入都是针对解析出的全部账单</summary>
    public const int PreviewRowLimit = 100;

    // ------------------------------------------------------------------ 步骤

    /// <summary>第 1 步的小标题</summary>
    public const string SourceStepTitle = "1 · 选择来源";

    /// <summary>第 2 步的小标题</summary>
    public const string FileStepTitle = "2 · 选择文件";

    /// <summary>第 3 步的小标题</summary>
    public const string PreviewStepTitle = "3 · 解析预览";

    /// <summary>第 4 步的小标题</summary>
    public const string LedgerStepTitle = "4 · 导入到账本";

    /// <summary>来源分段控件的读屏名称</summary>
    public const string SourceAriaLabel = "账单来源";

    // ------------------------------------------------------------------ 来源与导出说明

    /// <summary>来源分段（取值, 文案）：微信在前，与旧页面一致</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> Sources =
    [
        ("wechat", "微信"),
        ("alipay", "支付宝"),
    ];

    /// <summary>来源 → 分段控件的取值（也是 E2E 观察的 data-value）</summary>
    public static string SourceValue(ImportSource source) => source == ImportSource.WeChat ? "wechat" : "alipay";

    /// <summary>分段控件的取值 → 来源；不认识的取值返回 false</summary>
    public static bool TryParseSource(string? value, out ImportSource source)
    {
        switch (value)
        {
            case "wechat":
                source = ImportSource.WeChat;
                return true;
            case "alipay":
                source = ImportSource.Alipay;
                return true;
            default:
                source = default;
                return false;
        }
    }

    /// <summary>平台名：「微信」「支付宝」</summary>
    public static string SourceName(ImportSource source) => source == ImportSource.WeChat ? "微信" : "支付宝";

    /// <summary>折叠说明那一行的标题</summary>
    public static string GuideTitle(ImportSource source) => $"如何导出{SourceName(source)}账单";

    /// <summary>导出路径（沿用旧页面的文案）</summary>
    public static string ExportPath(ImportSource source) => source == ImportSource.WeChat
        ? "微信 → 我 → 服务 → 钱包 → 账单 → 常见问题 → 下载账单"
        : "支付宝 → 我的 → 账单 → 右上角 ··· → 开具交易流水证明";

    /// <summary>折叠说明里「导出路径」这个小标题</summary>
    public const string ExportPathLabel = "导出路径";

    /// <summary>支持的文件格式说明（沿用旧页面的文案）</summary>
    public static string FormatHint(ImportSource source) => $"支持{SourceName(source)}导出的 CSV 格式账单文件。";

    /// <summary>云端预览接口（只解析、不入库）</summary>
    public static string PreviewEndpoint(ImportSource source) => $"api/import/{SourceValue(source)}/preview";

    /// <summary>云端导入接口（写入云端数据库）</summary>
    public static string ImportEndpoint(ImportSource source) => $"api/import/{SourceValue(source)}";

    // ------------------------------------------------------------------ 选文件

    /// <summary>文件行的标题：还没选文件</summary>
    public const string FileRowEmptyTitle = "选择 CSV 文件";

    /// <summary>文件行右值：还没选文件</summary>
    public const string FileRowChooseValue = "选择";

    /// <summary>文件行右值：已选文件，可以换一个</summary>
    public const string FileRowReplaceValue = "重新选择";

    /// <summary>文件行的读屏名称（透明的文件输入覆盖在整行上）</summary>
    public const string FileInputAriaLabel = "选择 CSV 文件";

    /// <summary>正在读取选中文件的提示（读出内容、检测编码，还没有向云端解析）</summary>
    public const string ReadingText = "正在读取文件…";

    /// <summary>正在解析的提示（用户点了「解析预览」之后）</summary>
    public const string ParsingText = "正在解析…";

    /// <summary>解析失败后提示条里的「重试」（与底部主按钮「解析预览」是同一个动作，贴着错误提示更顺手）</summary>
    public const string RetryLabel = "重试";

    /// <summary>文件行的副标题：还没选文件时提示该选哪个平台的文件</summary>
    public static string FileRowEmptySubtitle(ImportSource source) => $"选择{SourceName(source)}导出的账单文件";

    /// <summary>文件步骤的脚注：格式与大小限制，以及「选好文件后点解析预览」的流程说明</summary>
    public static string FileFooter(ImportSource source) =>
        $"支持{SourceName(source)}导出的 CSV 账单，不超过 {MaxFileBytes / (1024 * 1024)} MB。选好文件后点「{ParseLabel}」先看一遍，确认无误再导入。";

    /// <summary>文件大小：512 B / 1.5 KB / 2.3 MB（小数点固定为 .，不随设备区域设置变化）</summary>
    public static string FileSizeText(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{Math.Max(0, bytes)} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{(bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture)} KB";
        }

        return $"{(bytes / (1024d * 1024d)).ToString("0.#", CultureInfo.InvariantCulture)} MB";
    }

    /// <summary>文件超过大小上限</summary>
    public static string FileTooLargeMessage(long bytes) =>
        $"文件太大（{FileSizeText(bytes)}），最多支持 {MaxFileBytes / (1024 * 1024)} MB";

    /// <summary>读文件内容失败</summary>
    public static string ReadFailedMessage(string reason) => $"读取文件失败：{reason}";

    /// <summary>
    /// 云端没有解析出任何账单：文件不是对应平台导出的 CSV（选错了来源也会这样）。
    /// 微信接口还会告知跳过了多少条中性交易 / 无效交易，一并说明——可能整份账单都是这类交易。
    /// </summary>
    public static string EmptyParseMessage(ImportSource source, int skippedCount)
    {
        var message = $"未解析出任何账单，请确认文件为{SourceName(source)}导出的 CSV 账单";
        return skippedCount > 0 ? $"{message}（已跳过 {skippedCount} 条中性交易 / 无效交易）" : message;
    }

    /// <summary>云端返回了空响应</summary>
    public static string EmptyResponseMessage(string action) => $"{action}失败：云端返回了空结果";

    // ------------------------------------------------------------------ 预览

    /// <summary>汇总卡片：支出一栏的标题</summary>
    public const string ExpenseLabel = "支出";

    /// <summary>汇总卡片：收入一栏的标题</summary>
    public const string IncomeLabel = "收入";

    /// <summary>汇总卡片：结余一栏的标题（收入 − 支出）</summary>
    public const string BalanceLabel = "结余";

    /// <summary>预览行没有描述也没有商户时的标题</summary>
    public const string UntitledRow = "（无描述）";

    /// <summary>在已解析出的全部账单上汇总：笔数、支出合计、收入合计、时间范围</summary>
    public static ImportSummary Summarize(IReadOnlyCollection<ImportPreviewBill> bills)
    {
        ArgumentNullException.ThrowIfNull(bills);

        if (bills.Count == 0)
        {
            return new ImportSummary(0, 0m, 0m, null, null);
        }

        var expense = 0m;
        var income = 0m;
        var earliest = DateTime.MaxValue;
        var latest = DateTime.MinValue;

        foreach (var bill in bills)
        {
            if (bill.IsIncome)
            {
                income += bill.Amount;
            }
            else
            {
                expense += bill.Amount;
            }

            if (bill.TransactionTime < earliest)
            {
                earliest = bill.TransactionTime;
            }

            if (bill.TransactionTime > latest)
            {
                latest = bill.TransactionTime;
            }
        }

        return new ImportSummary(bills.Count, expense, income, earliest, latest);
    }

    /// <summary>汇总卡片下的说明：「共 38 笔账单 · 2026-10-01 至 2026-10-31」（同一天只写一个日期）</summary>
    public static string SummaryCaption(ImportSummary summary)
    {
        var text = $"共 {summary.Count} 笔账单";
        if (summary.Earliest is not { } earliest || summary.Latest is not { } latest)
        {
            return text;
        }

        var from = DateText(earliest);
        var to = DateText(latest);

        return from == to ? $"{text} · {from}" : $"{text} · {from} 至 {to}";
    }

    /// <summary>被跳过的中性交易 / 无效交易的说明；没有跳过的返回 null</summary>
    public static string? SkippedText(int skippedCount) =>
        skippedCount > 0 ? $"另有 {skippedCount} 条中性交易 / 无效交易已跳过" : null;

    /// <summary>预览行标题：描述优先，没有就用商户，再没有用占位</summary>
    public static string RowTitle(ImportPreviewBill bill)
    {
        var description = bill.Description?.Trim();
        if (!string.IsNullOrEmpty(description))
        {
            return description;
        }

        var merchant = bill.MerchantName?.Trim();

        return string.IsNullOrEmpty(merchant) ? UntitledRow : merchant;
    }

    /// <summary>预览行副标题：「商户 · 渠道」；商户为空或与标题重复就只显示渠道</summary>
    public static string RowSubtitle(ImportPreviewBill bill)
    {
        var merchant = bill.MerchantName?.Trim();
        var channel = bill.PaymentChannel?.Trim();
        var parts = new List<string>(2);

        if (!string.IsNullOrEmpty(merchant) && merchant != RowTitle(bill))
        {
            parts.Add(merchant);
        }

        if (!string.IsNullOrEmpty(channel))
        {
            parts.Add(channel);
        }

        return string.Join(" · ", parts);
    }

    /// <summary>预览行金额：支出 -¥35.00，收入 +¥12,000.00</summary>
    public static string RowAmount(ImportPreviewBill bill) => MoneyPresentation.Signed(bill.Amount, bill.IsIncome);

    /// <summary>预览行时间：CSV 里的交易时间，原样显示「2026-10-01 12:30」（不换算时区）</summary>
    public static string RowTime(ImportPreviewBill bill) => bill.TransactionTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>列表里要显示的账单：前 <see cref="PreviewRowLimit"/> 条</summary>
    public static IReadOnlyList<ImportPreviewBill> VisibleRows(IReadOnlyList<ImportPreviewBill> bills) =>
        bills.Count <= PreviewRowLimit ? bills : bills.Take(PreviewRowLimit).ToList();

    /// <summary>超过显示上限时列表下的说明；没超过返回 null</summary>
    public static string? OverflowText(int total) =>
        total > PreviewRowLimit
            ? $"仅显示前 {PreviewRowLimit} 条，其余 {total - PreviewRowLimit} 条会在导入时一并导入"
            : null;

    // ------------------------------------------------------------------ 账本

    /// <summary>账本一行的标题</summary>
    public const string LedgerRowTitle = "导入到账本";

    /// <summary>还没选账本时账本一行的右值（红色提示「还差这一项」）</summary>
    public const string ChooseLedgerText = "选择账本";

    /// <summary>账本读取期间账本一行的占位（读完之前不能显示「加载失败」，也不能显示红色的「选择账本」）</summary>
    public const string LedgerLoadingText = "加载中…";

    /// <summary>账本读取失败时账本一行的文字（点按重试）</summary>
    public const string LedgerLoadFailedText = "加载失败，点按重试";

    /// <summary>账本步骤的脚注：账本必选，以及刚建的账本导入前会先同步</summary>
    public const string LedgerFooter =
        "导入的账单必须归属一个账本，都会记入所选账本。如果所选账本刚在本机新建、还没同步到云端，导入前会先同步一次账本。";

    /// <summary>没选账本就导入的提示（账本必填；界面与接口各拦一道）</summary>
    public const string LedgerRequiredMessage = "请选择要导入到的账本";

    /// <summary>读取账本失败的提示（沿用旧页面的文案）</summary>
    public static string LedgersFailedMessage(string reason) => $"加载账本失败: {reason}";

    /// <summary>账本一行的右值：读取中 / 读取失败 / 账本名 / 请选择</summary>
    /// <param name="optionsLoaded">这一轮读取已经结束（成功或失败）</param>
    /// <param name="ledgersReady">账本读取成功</param>
    /// <param name="selectedName">已选账本的名称（没选为 null）</param>
    public static string LedgerValue(bool optionsLoaded, bool ledgersReady, string? selectedName)
    {
        if (!optionsLoaded)
        {
            return LedgerLoadingText;
        }

        if (!ledgersReady)
        {
            return LedgerLoadFailedText;
        }

        return selectedName ?? ChooseLedgerText;
    }

    // ------------------------------------------------------------------ 导入

    /// <summary>底部主按钮：还没有预览时的文字——显式的「解析预览」（点了才向云端解析；还没选文件时置灰）</summary>
    public const string ParseLabel = "解析预览";

    /// <summary>底部主按钮：解析中</summary>
    public const string ParseBusyLabel = "解析中";

    /// <summary>底部主按钮：导入中</summary>
    public const string SubmitBusyLabel = "导入中";

    /// <summary>底部主按钮：导入完成后「再导入一份」</summary>
    public const string AgainLabel = "继续导入";

    /// <summary>
    /// 底部主按钮的文字：预览出来之前是「解析预览」（解析中是「解析中」），预览出来之后是「导入 N 条账单」（导入中是「导入中」），
    /// 导入完成后是「继续导入」。同一个位置按步骤换动作，用户始终只有一个主按钮要点。
    /// </summary>
    public static string SubmitLabel(ImportStage stage, int count) => stage switch
    {
        ImportStage.Parsing => ParseBusyLabel,
        ImportStage.Importing => SubmitBusyLabel,
        ImportStage.Done => AgainLabel,
        ImportStage.Preview when count > 0 => $"导入 {count} 条账单",
        _ => ParseLabel,
    };

    /// <summary>
    /// 能不能点「解析预览」：文件已经读入（选好文件后，或解析失败后），且不在读取 / 解析 / 导入的路上。
    /// 底部主按钮与失败提示条里的「重试」共用这条规则。
    /// </summary>
    /// <param name="stage">页面阶段</param>
    /// <param name="hasFile">文件内容已读入（读取失败 / 文件太大时没有内容，只能换文件）</param>
    public static bool CanParse(ImportStage stage, bool hasFile) =>
        hasFile && stage is (ImportStage.Ready or ImportStage.Failed);

    /// <summary>
    /// 能不能点「导入」：有预览、账本已选、不在忙。账本必填——没选账本时按钮置灰（点击处理里还会再拦一道）。
    /// </summary>
    public static bool CanSubmit(ImportStage stage, int previewCount, Guid? ledgerId) =>
        stage == ImportStage.Preview && previewCount > 0 && ledgerId.HasValue && ledgerId.Value != Guid.Empty;

    /// <summary>导入失败（没有收到云端的响应）后，无法确定云端有没有收下的补充说明</summary>
    public const string UncertainImportHint = "云端可能已经收下了部分账单，请先到账单列表确认，再决定要不要重新导入";

    /// <summary>
    /// 把失败的解析 / 导入翻译成给用户看的提示：云端业务错误取响应体里的 message，网络与服务异常给固定说明，
    /// 不把「请求失败: BadRequest - {…}」整串露给用户（与成员页同一套规则，见 <see cref="LedgerMemberPresentation.DescribeFailure"/>）。
    /// </summary>
    /// <param name="action">操作名：「解析」或「导入」</param>
    /// <param name="exception">捕获到的异常</param>
    public static ImportFailure DescribeFailure(string action, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var failure = LedgerMemberPresentation.DescribeFailure(action, exception);

        // 业务错误（IsWarning）与 401 都发生在写入之前：一定没有账单被写入
        return new ImportFailure(failure.Message, failure.IsWarning || exception is UnauthorizedAccessException);
    }

    /// <summary>导入失败提示：没被云端明确拒绝时补一句「云端可能已收下部分账单」</summary>
    public static string ImportFailureMessage(ImportFailure failure) =>
        failure.Rejected ? failure.Message : $"{failure.Message}。{UncertainImportHint}";

    // ------------------------------------------------------------------ 结果

    /// <summary>结果区的标题</summary>
    public static string ResultTitle(ImportBillsResult result)
    {
        if (result.SuccessCount <= 0)
        {
            return "没有账单导入成功";
        }

        return result.FailCount > 0 ? "部分账单导入失败" : "导入完成";
    }

    /// <summary>结果区的一句话说明：「成功导入 12 条账单到「日常」」；有失败时补「，失败 2 条」</summary>
    public static string ResultSummary(ImportBillsResult result, string? ledgerName)
    {
        var target = string.IsNullOrEmpty(ledgerName) ? string.Empty : $"到「{ledgerName}」";
        var text = $"成功导入 {result.SuccessCount} 条账单{target}";

        return result.FailCount > 0 ? $"{text}，失败 {result.FailCount} 条" : text;
    }

    /// <summary>导入完成的 Toast：（文案, 是否警告）。全部成功是成功提示；有失败（含全部失败）是警告</summary>
    public static (string Message, bool IsWarning) DoneToast(ImportSource source, ImportBillsResult result)
    {
        var name = SourceName(source);

        if (result.SuccessCount <= 0)
        {
            return ($"{name}账单没有导入成功（失败 {result.FailCount} 条）", true);
        }

        return result.FailCount > 0
            ? ($"{name}账单导入完成：成功 {result.SuccessCount} 条，失败 {result.FailCount} 条", true)
            : ($"{name}账单导入完成：成功 {result.SuccessCount} 条", false);
    }

    /// <summary>结果行的右值：「12 条」</summary>
    public static string CountValue(int count) => $"{count} 条";

    /// <summary>失败详情的小标题</summary>
    public const string ErrorsHeader = "失败详情";

    /// <summary>失败详情的脚注：云端只返回前若干条原因，没列全时说明</summary>
    public static string? ErrorsFooter(ImportBillsResult result)
    {
        var shown = result.Errors?.Count ?? 0;
        return shown > 0 && result.FailCount > shown ? $"共 {result.FailCount} 条失败，仅显示前 {shown} 条原因" : null;
    }

    /// <summary>同步一行的标题</summary>
    public const string SyncRowTitle = "同步到本机";

    /// <summary>同步一行的右值</summary>
    public static string SyncValue(ImportSyncState state) => state switch
    {
        ImportSyncState.Syncing => "同步中",
        ImportSyncState.Synced => "已完成",
        ImportSyncState.Failed => "未完成",
        ImportSyncState.NotLoggedIn => "未登录",
        _ => string.Empty,
    };

    /// <summary>同步成功（沿用旧页面的文案）</summary>
    public const string SyncSucceededMessage = "已从云端拉取到本地，账单列表即可查看";

    /// <summary>同步失败：导入已经成功，只是还没拉到本地（沿用旧页面的文案）</summary>
    public static string SyncFailedMessage(string reason) =>
        $"导入成功，但拉取到本地失败：{reason}；可稍后在“我的”页点击“立即同步”";

    /// <summary>没有登录，没法同步（沿用旧页面的文案）</summary>
    public const string SyncNotLoggedInMessage = "导入成功，但当前未登录，请登录后在“我的”页点击“立即同步”";

    private static string DateText(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
