namespace FinanceTracker.Web.Models;

/// <summary>
/// 账单导入的来源平台（导出账单文件的 App）
/// </summary>
public enum ImportSource
{
    /// <summary>微信支付账单</summary>
    WeChat,

    /// <summary>支付宝账单</summary>
    Alipay,
}

/// <summary>
/// 导入账单页所处的阶段（页面按它决定显示哪些步骤）
/// </summary>
public enum ImportStage
{
    /// <summary>还没选文件</summary>
    Idle,

    /// <summary>正在读取选中的文件（读出内容、检测编码；还没有向云端解析）</summary>
    Reading,

    /// <summary>
    /// 文件已读入，等用户点「解析预览」。选好文件、换来源之后都停在这里——页面不会自动向云端解析，
    /// 换来源时原来的预览作废（那是按旧来源解析出来的），也回到这里。
    /// </summary>
    Ready,

    /// <summary>用户点了「解析预览」，正在向云端解析</summary>
    Parsing,

    /// <summary>读取文件 / 解析预览失败（文件还在，可以重试或换文件）</summary>
    Failed,

    /// <summary>预览已出，等待选账本并导入</summary>
    Preview,

    /// <summary>正在导入</summary>
    Importing,

    /// <summary>导入完成，显示结果</summary>
    Done,
}

/// <summary>
/// 导入完成之后「同步到本机」这一步的状态（导入写进云端数据库，账单列表读的是本地库，所以导入后要拉一次）
/// </summary>
public enum ImportSyncState
{
    /// <summary>没有需要同步的（没有一条导入成功）</summary>
    None,

    /// <summary>正在同步</summary>
    Syncing,

    /// <summary>已拉取到本地</summary>
    Synced,

    /// <summary>同步没成功（可稍后手动同步）</summary>
    Failed,

    /// <summary>当前没有登录，无法同步</summary>
    NotLoggedIn,
}

/// <summary>
/// 云端「预览」接口解析出来的一条账单（只解析、不入库；字段与 REST 响应一致，按不区分大小写反序列化）。
/// <see cref="TransactionTime"/> 是 CSV 里的交易时间，不带时区的用户本地时间——界面原样显示，不换算（ADR 0005）。
/// </summary>
public sealed class ImportPreviewBill
{
    /// <summary>交易时间（CSV 原样的墙上时间）</summary>
    public DateTime TransactionTime { get; set; }

    /// <summary>金额（恒为正数，方向看 <see cref="IsIncome"/>）</summary>
    public decimal Amount { get; set; }

    /// <summary>是否收入</summary>
    public bool IsIncome { get; set; }

    /// <summary>描述（商品 / 备注）</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>商户 / 交易对方</summary>
    public string MerchantName { get; set; } = string.Empty;

    /// <summary>平台上的支付方式（已归一化为应用内的支付渠道名）</summary>
    public string PaymentChannel { get; set; } = string.Empty;

    /// <summary>平台交易单号</summary>
    public string TransactionId { get; set; } = string.Empty;
}

/// <summary>
/// 云端「预览」接口的响应：解析出的账单，以及被跳过的条数（只有微信会跳过：中性交易与已撤销等无效交易）
/// </summary>
public sealed class ImportPreviewResult
{
    /// <summary>解析出的账单数</summary>
    public int TotalCount { get; set; }

    /// <summary>跳过的中性交易 / 无效交易数（支付宝接口不返回这一项，恒为 0）</summary>
    public int SkippedCount { get; set; }

    /// <summary>解析出的账单</summary>
    public List<ImportPreviewBill> Bills { get; set; } = new();
}

/// <summary>
/// 云端「导入」接口的响应（账单已写入云端数据库）
/// </summary>
public sealed class ImportBillsResult
{
    /// <summary>参与导入的账单数</summary>
    public int TotalCount { get; set; }

    /// <summary>导入成功的账单数</summary>
    public int SuccessCount { get; set; }

    /// <summary>导入失败的账单数</summary>
    public int FailCount { get; set; }

    /// <summary>失败原因（云端最多返回前 10 条）</summary>
    public List<string>? Errors { get; set; }
}

/// <summary>
/// 预览列表的汇总：笔数、支出合计、收入合计与时间范围（在已解析出的全部账单上算，不受列表只显示前若干条影响）
/// </summary>
/// <param name="Count">账单笔数</param>
/// <param name="Expense">支出合计（正数）</param>
/// <param name="Income">收入合计（正数）</param>
/// <param name="Earliest">最早的交易时间（没有账单为 null）</param>
/// <param name="Latest">最晚的交易时间（没有账单为 null）</param>
public sealed record ImportSummary(int Count, decimal Expense, decimal Income, DateTime? Earliest, DateTime? Latest);

/// <summary>
/// 导入失败的说明（给用户看的文案）
/// </summary>
/// <param name="Message">提示文案</param>
/// <param name="Rejected">
/// 云端明确拒绝了这次请求（账本不存在、没有写权限、文件内容不对等业务错误）——这时一定没有写入任何账单，
/// 用户可以直接换个账本 / 文件再试；为 false 时（网络中断、服务异常……）无法确定云端有没有收下，
/// 重试前最好先到账单列表确认，免得重复导入。
/// </param>
public sealed record ImportFailure(string Message, bool Rejected);
