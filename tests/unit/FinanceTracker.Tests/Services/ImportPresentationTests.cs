using System.Globalization;
using System.Net;
using FinanceTracker.Core.Exceptions;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>导入账单页的展示规则（20 号）：来源与导出说明、文件与预览的文字、汇总、账本一行、导入与结果的提示。</summary>
public sealed class ImportPresentationTests
{
    private static ImportPreviewBill Bill(
        string description = "午餐",
        string merchant = "美团",
        string channel = "微信支付",
        decimal amount = 35m,
        bool income = false,
        DateTime? time = null) => new()
    {
        Description = description,
        MerchantName = merchant,
        PaymentChannel = channel,
        Amount = amount,
        IsIncome = income,
        TransactionTime = time ?? new DateTime(2026, 10, 1, 12, 30, 0),
    };

    // ───────────── 来源与导出说明 ─────────────

    [Fact]
    public void 来源分段微信在前_取值与文案()
    {
        Assert.Equal(new[] { ("wechat", "微信"), ("alipay", "支付宝") }, ImportPresentation.Sources);
    }

    [Theory]
    [InlineData(ImportSource.WeChat, "wechat", "微信")]
    [InlineData(ImportSource.Alipay, "alipay", "支付宝")]
    public void 来源取值往返_平台名(ImportSource source, string value, string name)
    {
        Assert.Equal(value, ImportPresentation.SourceValue(source));
        Assert.Equal(name, ImportPresentation.SourceName(source));
        Assert.True(ImportPresentation.TryParseSource(value, out var parsed));
        Assert.Equal(source, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("WeChat")]
    [InlineData("bank")]
    public void 不认识的来源取值解析失败(string? value)
    {
        Assert.False(ImportPresentation.TryParseSource(value, out _));
    }

    [Fact]
    public void 导出路径与格式说明沿用旧页面的文案()
    {
        Assert.Equal("微信 → 我 → 服务 → 钱包 → 账单 → 常见问题 → 下载账单", ImportPresentation.ExportPath(ImportSource.WeChat));
        Assert.Equal("支付宝 → 我的 → 账单 → 右上角 ··· → 开具交易流水证明", ImportPresentation.ExportPath(ImportSource.Alipay));
        Assert.Equal("支持微信导出的 CSV 格式账单文件。", ImportPresentation.FormatHint(ImportSource.WeChat));
        Assert.Equal("支持支付宝导出的 CSV 格式账单文件。", ImportPresentation.FormatHint(ImportSource.Alipay));
        Assert.Equal("如何导出微信账单", ImportPresentation.GuideTitle(ImportSource.WeChat));
        Assert.Equal("如何导出支付宝账单", ImportPresentation.GuideTitle(ImportSource.Alipay));
    }

    [Fact]
    public void 云端接口地址与导入控制器一致()
    {
        Assert.Equal("api/import/wechat/preview", ImportPresentation.PreviewEndpoint(ImportSource.WeChat));
        Assert.Equal("api/import/alipay/preview", ImportPresentation.PreviewEndpoint(ImportSource.Alipay));
        Assert.Equal("api/import/wechat", ImportPresentation.ImportEndpoint(ImportSource.WeChat));
        Assert.Equal("api/import/alipay", ImportPresentation.ImportEndpoint(ImportSource.Alipay));
    }

    // ───────────── 选文件 ─────────────

    [Theory]
    [InlineData(-5L, "0 B")]
    [InlineData(0L, "0 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1048575L, "1024 KB")]
    [InlineData(1048576L, "1 MB")]
    [InlineData(2411724L, "2.3 MB")]
    [InlineData(10485760L, "10 MB")]
    public void 文件大小文字(long bytes, string expected)
    {
        Assert.Equal(expected, ImportPresentation.FileSizeText(bytes));
    }

    [Fact]
    public void 文件大小的小数点不随设备区域设置变化()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            Assert.Equal("1.5 KB", ImportPresentation.FileSizeText(1536));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void 文件上限是10MB_与旧页面一致()
    {
        Assert.Equal(10 * 1024 * 1024, ImportPresentation.MaxFileBytes);
        Assert.Equal("文件太大（12.5 MB），最多支持 10 MB", ImportPresentation.FileTooLargeMessage(13107200));
        Assert.Equal("支持微信导出的 CSV 账单，不超过 10 MB。选好文件后点「解析预览」先看一遍，确认无误再导入。", ImportPresentation.FileFooter(ImportSource.WeChat));
    }

    [Fact]
    public void 读取与解析各有各的提示_不把读文件说成解析()
    {
        Assert.Equal("正在读取文件…", ImportPresentation.ReadingText);
        Assert.Equal("正在解析…", ImportPresentation.ParsingText);
    }

    [Fact]
    public void 文件行的提示按来源区分()
    {
        Assert.Equal("选择微信导出的账单文件", ImportPresentation.FileRowEmptySubtitle(ImportSource.WeChat));
        Assert.Equal("选择支付宝导出的账单文件", ImportPresentation.FileRowEmptySubtitle(ImportSource.Alipay));
    }

    [Theory]
    [InlineData(ImportSource.WeChat, 0, "未解析出任何账单，请确认文件为微信导出的 CSV 账单")]
    [InlineData(ImportSource.Alipay, 0, "未解析出任何账单，请确认文件为支付宝导出的 CSV 账单")]
    [InlineData(ImportSource.WeChat, 7, "未解析出任何账单，请确认文件为微信导出的 CSV 账单（已跳过 7 条中性交易 / 无效交易）")]
    public void 没解析出账单的提示_有跳过时一并说明(ImportSource source, int skipped, string expected)
    {
        Assert.Equal(expected, ImportPresentation.EmptyParseMessage(source, skipped));
    }

    [Fact]
    public void 读取失败与空响应的提示()
    {
        Assert.Equal("读取文件失败：磁盘错误", ImportPresentation.ReadFailedMessage("磁盘错误"));
        Assert.Equal("解析失败：云端返回了空结果", ImportPresentation.EmptyResponseMessage("解析"));
        Assert.Equal("导入失败：云端返回了空结果", ImportPresentation.EmptyResponseMessage("导入"));
    }

    // ───────────── 预览汇总 ─────────────

    [Fact]
    public void 没有账单的汇总全是零()
    {
        var summary = ImportPresentation.Summarize([]);

        Assert.Equal(new ImportSummary(0, 0m, 0m, null, null), summary);
        Assert.Equal("共 0 笔账单", ImportPresentation.SummaryCaption(summary));
    }

    [Fact]
    public void 汇总在全部账单上算_支出收入分开_带时间范围()
    {
        var bills = new[]
        {
            Bill(amount: 35m, time: new DateTime(2026, 10, 3, 9, 0, 0)),
            Bill(amount: 12.5m, time: new DateTime(2026, 10, 1, 8, 15, 0)),
            Bill(amount: 8000m, income: true, time: new DateTime(2026, 10, 2, 18, 0, 0)),
        };

        var summary = ImportPresentation.Summarize(bills);

        Assert.Equal(3, summary.Count);
        Assert.Equal(47.5m, summary.Expense);
        Assert.Equal(8000m, summary.Income);
        Assert.Equal(new DateTime(2026, 10, 1, 8, 15, 0), summary.Earliest);
        Assert.Equal(new DateTime(2026, 10, 3, 9, 0, 0), summary.Latest);
        Assert.Equal("共 3 笔账单 · 2026-10-01 至 2026-10-03", ImportPresentation.SummaryCaption(summary));
    }

    [Fact]
    public void 同一天的账单只写一个日期()
    {
        var summary = ImportPresentation.Summarize([
            Bill(time: new DateTime(2026, 10, 1, 8, 0, 0)),
            Bill(time: new DateTime(2026, 10, 1, 21, 0, 0)),
        ]);

        Assert.Equal("共 2 笔账单 · 2026-10-01", ImportPresentation.SummaryCaption(summary));
    }

    [Fact]
    public void 跳过说明只在有跳过时出现()
    {
        Assert.Null(ImportPresentation.SkippedText(0));
        Assert.Null(ImportPresentation.SkippedText(-1));
        Assert.Equal("另有 12 条中性交易 / 无效交易已跳过", ImportPresentation.SkippedText(12));
    }

    // ───────────── 预览行 ─────────────

    [Fact]
    public void 预览行标题描述优先_再商户_再占位()
    {
        Assert.Equal("午餐", ImportPresentation.RowTitle(Bill(description: "  午餐 ", merchant: "美团")));
        Assert.Equal("美团", ImportPresentation.RowTitle(Bill(description: "", merchant: "美团")));
        Assert.Equal("美团", ImportPresentation.RowTitle(Bill(description: "   ", merchant: " 美团 ")));
        Assert.Equal(ImportPresentation.UntitledRow, ImportPresentation.RowTitle(Bill(description: "", merchant: "")));
    }

    [Fact]
    public void 预览行副标题是商户加渠道_重复或为空就省略()
    {
        Assert.Equal("美团 · 微信支付", ImportPresentation.RowSubtitle(Bill(description: "午餐", merchant: "美团", channel: "微信支付")));
        Assert.Equal("微信支付", ImportPresentation.RowSubtitle(Bill(description: "美团", merchant: "美团", channel: "微信支付")));
        Assert.Equal("微信支付", ImportPresentation.RowSubtitle(Bill(description: "午餐", merchant: "", channel: "微信支付")));
        Assert.Equal("美团", ImportPresentation.RowSubtitle(Bill(description: "午餐", merchant: "美团", channel: " ")));
        Assert.Equal(string.Empty, ImportPresentation.RowSubtitle(Bill(description: "午餐", merchant: "", channel: "")));
    }

    [Fact]
    public void 预览行金额带符号_时间原样显示不换算时区()
    {
        Assert.Equal("-¥35.00", ImportPresentation.RowAmount(Bill(amount: 35m)));
        Assert.Equal("+¥12,000.00", ImportPresentation.RowAmount(Bill(amount: 12000m, income: true)));

        // CSV 里的交易时间是不带时区的用户本地时间：不管 Kind 是什么都原样显示（ADR 0005：界面不自行换算）
        var utcKind = new DateTime(2026, 10, 1, 12, 30, 0, DateTimeKind.Utc);
        var localKind = new DateTime(2026, 10, 1, 12, 30, 0, DateTimeKind.Local);

        Assert.Equal("2026-10-01 12:30", ImportPresentation.RowTime(Bill(time: utcKind)));
        Assert.Equal("2026-10-01 12:30", ImportPresentation.RowTime(Bill(time: localKind)));
    }

    [Fact]
    public void 列表只显示前100条_说明其余在导入时一并导入()
    {
        var few = Enumerable.Range(0, 100).Select(_ => Bill()).ToList();
        var many = Enumerable.Range(0, 250).Select(i => Bill(description: $"账单{i}")).ToList();

        Assert.Equal(100, ImportPresentation.VisibleRows(few).Count);
        Assert.Null(ImportPresentation.OverflowText(100));

        var visible = ImportPresentation.VisibleRows(many);
        Assert.Equal(ImportPresentation.PreviewRowLimit, visible.Count);
        Assert.Equal("账单0", visible[0].Description);
        Assert.Equal("账单99", visible[^1].Description);
        Assert.Equal("仅显示前 100 条，其余 150 条会在导入时一并导入", ImportPresentation.OverflowText(250));
    }

    // ───────────── 账本一行 ─────────────

    [Fact]
    public void 账本行的右值_读取中_读取失败_账本名_请选择()
    {
        Assert.Equal("加载中…", ImportPresentation.LedgerValue(optionsLoaded: false, ledgersReady: false, selectedName: null));
        Assert.Equal("加载中…", ImportPresentation.LedgerValue(optionsLoaded: false, ledgersReady: true, selectedName: "日常"));
        Assert.Equal("加载失败，点按重试", ImportPresentation.LedgerValue(optionsLoaded: true, ledgersReady: false, selectedName: null));
        Assert.Equal("日常", ImportPresentation.LedgerValue(optionsLoaded: true, ledgersReady: true, selectedName: "日常"));
        Assert.Equal("选择账本", ImportPresentation.LedgerValue(optionsLoaded: true, ledgersReady: true, selectedName: null));
    }

    // ───────────── 导入按钮 ─────────────

    // 预览出来之前主按钮就是显式的「解析预览」（第 2 轮评审后用户拍板恢复；选好文件不再自动解析）：
    // 没选文件 / 读取中 / 已选文件 / 解析失败都是它，解析中是「解析中」，预览出来变「导入 N 条账单」，导入完成「继续导入」
    [Theory]
    [InlineData(ImportStage.Idle, 0, "解析预览")]
    [InlineData(ImportStage.Reading, 0, "解析预览")]
    [InlineData(ImportStage.Ready, 0, "解析预览")]
    [InlineData(ImportStage.Parsing, 0, "解析中")]
    [InlineData(ImportStage.Failed, 0, "解析预览")]
    [InlineData(ImportStage.Preview, 0, "解析预览")]
    [InlineData(ImportStage.Preview, 38, "导入 38 条账单")]
    [InlineData(ImportStage.Importing, 38, "导入中")]
    [InlineData(ImportStage.Done, 38, "继续导入")]
    public void 主按钮的文字随阶段变化(ImportStage stage, int count, string expected)
    {
        Assert.Equal(expected, ImportPresentation.SubmitLabel(stage, count));
    }

    [Fact]
    public void 解析要求文件已读入_且在已选文件或失败阶段()
    {
        // 选好文件（Ready）与解析失败（Failed，文件还在）可以解析 / 重试
        Assert.True(ImportPresentation.CanParse(ImportStage.Ready, hasFile: true));
        Assert.True(ImportPresentation.CanParse(ImportStage.Failed, hasFile: true));

        // 读取失败 / 文件太大：没有文件内容，只能换文件，不能「重试」
        Assert.False(ImportPresentation.CanParse(ImportStage.Failed, hasFile: false));
        Assert.False(ImportPresentation.CanParse(ImportStage.Ready, hasFile: false));

        // 还没选文件 / 读取中 / 解析中（连点两下不会发出两次请求）/ 已有预览 / 导入中 / 完成
        Assert.False(ImportPresentation.CanParse(ImportStage.Idle, hasFile: false));
        Assert.False(ImportPresentation.CanParse(ImportStage.Reading, hasFile: true));
        Assert.False(ImportPresentation.CanParse(ImportStage.Parsing, hasFile: true));
        Assert.False(ImportPresentation.CanParse(ImportStage.Preview, hasFile: true));
        Assert.False(ImportPresentation.CanParse(ImportStage.Importing, hasFile: true));
        Assert.False(ImportPresentation.CanParse(ImportStage.Done, hasFile: true));
    }

    [Fact]
    public void 导入要求有预览_已选账本_且不在忙()
    {
        var ledger = Guid.NewGuid();

        Assert.True(ImportPresentation.CanSubmit(ImportStage.Preview, 1, ledger));

        // 账本必填：没选账本（或选了空 Id）不能导入
        Assert.False(ImportPresentation.CanSubmit(ImportStage.Preview, 1, null));
        Assert.False(ImportPresentation.CanSubmit(ImportStage.Preview, 1, Guid.Empty));

        // 没有预览 / 不在预览阶段
        Assert.False(ImportPresentation.CanSubmit(ImportStage.Preview, 0, ledger));
        Assert.False(ImportPresentation.CanSubmit(ImportStage.Idle, 1, ledger));
        Assert.False(ImportPresentation.CanSubmit(ImportStage.Reading, 1, ledger));
        Assert.False(ImportPresentation.CanSubmit(ImportStage.Ready, 1, ledger));
        Assert.False(ImportPresentation.CanSubmit(ImportStage.Parsing, 1, ledger));
        Assert.False(ImportPresentation.CanSubmit(ImportStage.Failed, 1, ledger));
        Assert.False(ImportPresentation.CanSubmit(ImportStage.Importing, 1, ledger));
        Assert.False(ImportPresentation.CanSubmit(ImportStage.Done, 1, ledger));
    }

    // ───────────── 失败提示 ─────────────

    private static Exception Cloud(HttpStatusCode status, string body) => new($"请求失败: {status} - {body}");

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"message":"请选择要导入到的账本"}""", "导入失败：请选择要导入到的账本")]
    [InlineData(HttpStatusCode.BadRequest, """{"message":"CSV 内容不能为空"}""", "导入失败：CSV 内容不能为空")]
    [InlineData(HttpStatusCode.NotFound, """{"message":"账本不存在"}""", "导入失败：账本不存在")]
    public void 云端的业务错误取出message_并算被明确拒绝(HttpStatusCode status, string body, string expected)
    {
        var failure = ImportPresentation.DescribeFailure("导入", Cloud(status, body));

        Assert.Equal(expected, failure.Message);
        Assert.True(failure.Rejected);
        Assert.Equal(expected, ImportPresentation.ImportFailureMessage(failure));
    }

    [Fact]
    public void 没有写权限是无权限_也算被明确拒绝_不会让用户登出()
    {
        var failure = ImportPresentation.DescribeFailure("导入", Cloud(HttpStatusCode.Forbidden, """{"message":"您没有该账本的编辑权限"}"""));

        Assert.Equal("无权限：您没有该账本的编辑权限", failure.Message);
        Assert.True(failure.Rejected);
    }

    [Fact]
    public void 服务层的权限错误同样是无权限()
    {
        var failure = ImportPresentation.DescribeFailure("导入", new ForbiddenAccessException("只有账本所有者可以导入"));

        Assert.Equal("无权限：只有账本所有者可以导入", failure.Message);
        Assert.True(failure.Rejected);
    }

    [Fact]
    public void 认证失败发生在写入之前_算被明确拒绝()
    {
        var failure = ImportPresentation.DescribeFailure("导入", new UnauthorizedAccessException("登录已过期，请重新登录"));

        Assert.Equal("登录已过期，请重新登录", failure.Message);
        Assert.True(failure.Rejected);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, """{"message":"System.NullReferenceException at Foo.Bar()"}""")]
    [InlineData(HttpStatusCode.BadGateway, "<html>502</html>")]
    public void 云端服务异常无法确定有没有写入_补一句提醒_且不露出内部细节(HttpStatusCode status, string body)
    {
        var failure = ImportPresentation.DescribeFailure("导入", Cloud(status, body));

        Assert.Equal("导入失败：云端服务暂时不可用，请稍后重试", failure.Message);
        Assert.False(failure.Rejected);
        Assert.Equal(
            "导入失败：云端服务暂时不可用，请稍后重试。云端可能已经收下了部分账单，请先到账单列表确认，再决定要不要重新导入",
            ImportPresentation.ImportFailureMessage(failure));
        Assert.DoesNotContain("NullReference", ImportPresentation.ImportFailureMessage(failure));
    }

    [Fact]
    public void 连不上云端时提示检查网络_解析与导入各用各的操作名()
    {
        var parse = ImportPresentation.DescribeFailure("解析", new HttpRequestException("connection refused"));
        var import = ImportPresentation.DescribeFailure("导入", new TaskCanceledException("timeout"));

        Assert.Equal("解析失败：无法连接云端，请检查网络后重试", parse.Message);
        Assert.False(parse.Rejected);
        Assert.Equal("导入失败：无法连接云端，请检查网络后重试", import.Message);
        Assert.False(import.Rejected);
    }

    // ───────────── 结果 ─────────────

    [Theory]
    [InlineData(38, 0, "导入完成")]
    [InlineData(36, 2, "部分账单导入失败")]
    [InlineData(0, 38, "没有账单导入成功")]
    public void 结果标题(int success, int fail, string expected)
    {
        Assert.Equal(expected, ImportPresentation.ResultTitle(new ImportBillsResult { TotalCount = success + fail, SuccessCount = success, FailCount = fail }));
    }

    [Fact]
    public void 结果说明带账本名_有失败时补失败数()
    {
        var ok = new ImportBillsResult { TotalCount = 12, SuccessCount = 12 };
        var partial = new ImportBillsResult { TotalCount = 14, SuccessCount = 12, FailCount = 2 };

        Assert.Equal("成功导入 12 条账单到「日常」", ImportPresentation.ResultSummary(ok, "日常"));
        Assert.Equal("成功导入 12 条账单", ImportPresentation.ResultSummary(ok, null));
        Assert.Equal("成功导入 12 条账单到「日常」，失败 2 条", ImportPresentation.ResultSummary(partial, "日常"));
        Assert.Equal("12 条", ImportPresentation.CountValue(12));
    }

    [Fact]
    public void 完成Toast_全部成功是成功提示_有失败是警告()
    {
        var ok = ImportPresentation.DoneToast(ImportSource.WeChat, new ImportBillsResult { TotalCount = 12, SuccessCount = 12 });
        var partial = ImportPresentation.DoneToast(ImportSource.Alipay, new ImportBillsResult { TotalCount = 14, SuccessCount = 12, FailCount = 2 });
        var none = ImportPresentation.DoneToast(ImportSource.WeChat, new ImportBillsResult { TotalCount = 3, FailCount = 3 });

        Assert.Equal(("微信账单导入完成：成功 12 条", false), ok);
        Assert.Equal(("支付宝账单导入完成：成功 12 条，失败 2 条", true), partial);
        Assert.Equal(("微信账单没有导入成功（失败 3 条）", true), none);
    }

    [Fact]
    public void 失败详情的脚注只在云端没列全时出现()
    {
        var all = new ImportBillsResult { FailCount = 2, Errors = ["第 1 条: a", "第 2 条: b"] };
        var truncated = new ImportBillsResult { FailCount = 25, Errors = Enumerable.Range(1, 10).Select(i => $"第 {i} 条: x").ToList() };
        var none = new ImportBillsResult { FailCount = 3, Errors = null };

        Assert.Null(ImportPresentation.ErrorsFooter(all));
        Assert.Equal("共 25 条失败，仅显示前 10 条原因", ImportPresentation.ErrorsFooter(truncated));
        Assert.Null(ImportPresentation.ErrorsFooter(none));
    }

    [Fact]
    public void 同步一行的右值与提示沿用旧页面的文案()
    {
        Assert.Equal("同步中", ImportPresentation.SyncValue(ImportSyncState.Syncing));
        Assert.Equal("已完成", ImportPresentation.SyncValue(ImportSyncState.Synced));
        Assert.Equal("未完成", ImportPresentation.SyncValue(ImportSyncState.Failed));
        Assert.Equal("未登录", ImportPresentation.SyncValue(ImportSyncState.NotLoggedIn));
        Assert.Equal(string.Empty, ImportPresentation.SyncValue(ImportSyncState.None));

        Assert.Equal("已从云端拉取到本地，账单列表即可查看", ImportPresentation.SyncSucceededMessage);
        Assert.Equal("导入成功，但拉取到本地失败：无网络连接；可稍后在“我的”页点击“立即同步”", ImportPresentation.SyncFailedMessage("无网络连接"));
        Assert.Equal("导入成功，但当前未登录，请登录后在“我的”页点击“立即同步”", ImportPresentation.SyncNotLoggedInMessage);
    }

    [Fact]
    public void 账本相关的提示沿用旧页面的文案()
    {
        Assert.Equal("请选择要导入到的账本", ImportPresentation.LedgerRequiredMessage);
        Assert.Equal("加载账本失败: 超时", ImportPresentation.LedgersFailedMessage("超时"));
    }
}
