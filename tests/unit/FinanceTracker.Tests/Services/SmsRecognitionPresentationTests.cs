using FinanceTracker.Core.Interfaces;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>短信识别页的展示规则（19 号）：读取范围的日期、结果列表文字、读取结果摘要与每条短信的去向、各处提示。</summary>
public sealed class SmsRecognitionPresentationTests
{
    private static SmsCaptureResult Result(params SmsCaptureOutcome[] outcomes) =>
        new(outcomes
            .Select((outcome, i) => new SmsCaptureItem(
                new SmsMessage { Id = i + 1, Address = "95588", Body = $"短信{i + 1}", Date = new DateTime(2026, 11, 25, 12, 0, 0).AddMinutes(i) },
                outcome))
            .ToList());

    [Fact]
    public void 默认开始日期是应用时区今天往前30天的零点()
    {
        var from = SmsRecognitionPresentation.DefaultFromDate(new DateTime(2026, 11, 25, 15, 30, 0));

        Assert.Equal(new DateTime(2026, 10, 26), from);
        Assert.Equal(TimeSpan.Zero, from.TimeOfDay);
    }

    [Fact]
    public void 日期文字与输入控件值()
    {
        var date = new DateTime(2026, 3, 5);

        Assert.Equal("2026年3月5日", SmsRecognitionPresentation.DateLabel(date));
        Assert.Equal("2026-03-05", SmsRecognitionPresentation.ToInputValue(date));
    }

    [Fact]
    public void 输入控件值往返不丢天()
    {
        Assert.True(SmsRecognitionPresentation.TryParseInputValue("2026-10-26", out var date));
        Assert.Equal(new DateTime(2026, 10, 26), date);
        Assert.Equal("2026-10-26", SmsRecognitionPresentation.ToInputValue(date));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    [InlineData("2026/10/26")]
    [InlineData("26-10-2026")]
    [InlineData("2026-13-01")]
    public void 输入控件值格式不对时解析失败_调用方沿用原来的日期(string? value)
    {
        Assert.False(SmsRecognitionPresentation.TryParseInputValue(value, out _));
    }

    [Fact]
    public void 结果小标题与行内元信息()
    {
        Assert.Equal("识别到 3 条支付短信", SmsRecognitionPresentation.ResultsHeader(3));
        Assert.Equal("95588 · 2026-11-25 12:08", SmsRecognitionPresentation.MetaText("95588", new DateTime(2026, 11, 25, 12, 8, 59)));
        Assert.Equal("95588 · 2026-11-25 12:08", SmsRecognitionPresentation.MetaText("  95588 ", new DateTime(2026, 11, 25, 12, 8, 0)));
        Assert.Equal("2026-11-25 12:08", SmsRecognitionPresentation.MetaText(null, new DateTime(2026, 11, 25, 12, 8, 0)));
        Assert.Equal("2026-11-25 12:08", SmsRecognitionPresentation.MetaText("", new DateTime(2026, 11, 25, 12, 8, 0)));
    }

    [Fact]
    public void 短信正文摘要折叠空白并按上限截断()
    {
        Assert.Equal("【招商银行】您尾号1234的卡 消费35.00元", SmsRecognitionPresentation.BodySummary("【招商银行】您尾号1234的卡\r\n  消费35.00元"));

        var body = new string('字', SmsRecognitionPresentation.BodyMaxLength + 5);
        var summary = SmsRecognitionPresentation.BodySummary(body);

        Assert.Equal(SmsRecognitionPresentation.BodyMaxLength + 1, summary.Length);
        Assert.EndsWith("…", summary);
    }

    [Fact]
    public void 提示与读屏名称()
    {
        Assert.Equal("读取短信失败: 无权限", SmsRecognitionPresentation.ReadFailedMessage("无权限"));
        Assert.Equal("加入待确认账单失败: 磁盘已满", SmsRecognitionPresentation.CaptureFailedMessage("磁盘已满"));
        Assert.Equal("3 条", SmsRecognitionPresentation.CountText(3));
        Assert.Equal("新加入待确认账单 3 条，前往待确认账单", SmsRecognitionPresentation.AddedRowAriaLabel(3));
    }

    // ---------------------------------------------------------------- 每条短信的去向标签

    [Theory]
    [InlineData(SmsCaptureOutcome.Added, "已加入待确认", "success", "added")]
    [InlineData(SmsCaptureOutcome.Duplicate, "已在待确认列表", "neutral", "duplicate")]
    [InlineData(SmsCaptureOutcome.NoAmount, "没识别出金额", "warning", "no-amount")]
    [InlineData(SmsCaptureOutcome.OverCapacity, "列表已满，未加入", "warning", "over-capacity")]
    [InlineData(SmsCaptureOutcome.Failed, "加入失败", "danger", "failed")]
    public void 每种去向有标签_配色和观察值(SmsCaptureOutcome outcome, string label, string tone, string value)
    {
        Assert.Equal(label, SmsRecognitionPresentation.OutcomeLabel(outcome));
        Assert.Equal(tone, SmsRecognitionPresentation.OutcomeTone(outcome));
        Assert.Equal(value, SmsRecognitionPresentation.OutcomeValue(outcome));
    }

    [Fact]
    public void 所有去向都有标签_新增去向不会漏配()
    {
        foreach (var outcome in Enum.GetValues<SmsCaptureOutcome>())
        {
            Assert.NotEmpty(SmsRecognitionPresentation.OutcomeLabel(outcome));
            Assert.NotEqual("unknown", SmsRecognitionPresentation.OutcomeValue(outcome));
        }

        // 观察值互不相同：E2E 靠它区分每条短信的去向
        var values = Enum.GetValues<SmsCaptureOutcome>().Select(SmsRecognitionPresentation.OutcomeValue).ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
    }

    // ---------------------------------------------------------------- 摘要脚注

    [Fact]
    public void 摘要脚注_列表已满优先_其次写入出错_再次是新加入的去向()
    {
        var full = SmsRecognitionPresentation.SummaryFooter(Result(SmsCaptureOutcome.Added, SmsCaptureOutcome.OverCapacity, SmsCaptureOutcome.Failed));
        Assert.Equal($"待确认账单最多保留 {IPendingBillService.MaxCount} 条：较早的付款没有加入，处理掉一些待确认账单后重新读取即可", full);

        Assert.Equal("加入失败的短信可以稍后重新读取", SmsRecognitionPresentation.SummaryFooter(Result(SmsCaptureOutcome.Added, SmsCaptureOutcome.Failed)));
        Assert.Equal("到「待确认账单」确认入账，账本在那里选", SmsRecognitionPresentation.SummaryFooter(Result(SmsCaptureOutcome.Added, SmsCaptureOutcome.Duplicate)));
    }

    [Fact]
    public void 摘要脚注_没有新加入也没有异常时不写脚注()
    {
        Assert.Null(SmsRecognitionPresentation.SummaryFooter(Result(SmsCaptureOutcome.Duplicate)));
        Assert.Null(SmsRecognitionPresentation.SummaryFooter(Result(SmsCaptureOutcome.NoAmount, SmsCaptureOutcome.Duplicate)));
    }

    // ---------------------------------------------------------------- 读取之后的提示

    [Fact]
    public void 提示_有新加入的是成功_并说明条数()
    {
        var toast = SmsRecognitionPresentation.CaptureToast(Result(SmsCaptureOutcome.Added, SmsCaptureOutcome.Added, SmsCaptureOutcome.Duplicate, SmsCaptureOutcome.NoAmount));

        Assert.Equal("已加入 2 条待确认账单", toast.Message);
        Assert.Equal(SmsToastLevel.Success, toast.Level);
    }

    [Fact]
    public void 提示_有新加入的同时有列表已满或写入出错_降为警告并带上数量()
    {
        var full = SmsRecognitionPresentation.CaptureToast(Result(SmsCaptureOutcome.Added, SmsCaptureOutcome.OverCapacity, SmsCaptureOutcome.OverCapacity));
        Assert.Equal("已加入 1 条待确认账单，2 条因待确认账单已满没加入", full.Message);
        Assert.Equal(SmsToastLevel.Warning, full.Level);

        var failed = SmsRecognitionPresentation.CaptureToast(Result(SmsCaptureOutcome.Added, SmsCaptureOutcome.Failed));
        Assert.Equal("已加入 1 条待确认账单，1 条加入失败", failed.Message);
        Assert.Equal(SmsToastLevel.Warning, failed.Level);
    }

    [Fact]
    public void 提示_一条都没加入时按原因区分()
    {
        var full = SmsRecognitionPresentation.CaptureToast(Result(SmsCaptureOutcome.OverCapacity, SmsCaptureOutcome.Failed, SmsCaptureOutcome.Duplicate));
        Assert.Equal("待确认账单已满，没有加入新的短信", full.Message);
        Assert.Equal(SmsToastLevel.Warning, full.Level);

        var failed = SmsRecognitionPresentation.CaptureToast(Result(SmsCaptureOutcome.Failed, SmsCaptureOutcome.Duplicate));
        Assert.Equal("加入待确认账单失败", failed.Message);
        Assert.Equal(SmsToastLevel.Error, failed.Level);

        var duplicate = SmsRecognitionPresentation.CaptureToast(Result(SmsCaptureOutcome.Duplicate, SmsCaptureOutcome.NoAmount));
        Assert.Equal("这些短信都已经在待确认列表里了", duplicate.Message);
        Assert.Equal(SmsToastLevel.Info, duplicate.Level);

        var noAmount = SmsRecognitionPresentation.CaptureToast(Result(SmsCaptureOutcome.NoAmount, SmsCaptureOutcome.NoAmount));
        Assert.Equal("这些短信里没有识别出金额，没有加入待确认账单", noAmount.Message);
        Assert.Equal(SmsToastLevel.Info, noAmount.Level);
    }

    [Fact]
    public void 界面文案遵守领域术语_不出现被禁用的词()
    {
        var texts = new List<string>
        {
            SmsRecognitionPresentation.Title, SmsRecognitionPresentation.RangeFooter, SmsRecognitionPresentation.NeedPermissionFooter,
            SmsRecognitionPresentation.IdleText, SmsRecognitionPresentation.SummaryGroupTitle, SmsRecognitionPresentation.AddedRowTitle,
            SmsRecognitionPresentation.DuplicateRowTitle, SmsRecognitionPresentation.NoAmountRowTitle,
            SmsRecognitionPresentation.OverCapacityRowTitle, SmsRecognitionPresentation.FailedRowTitle,
            SmsRecognitionPresentation.CaptureFailedMessage("原因"),
        };
        texts.AddRange(Enum.GetValues<SmsCaptureOutcome>().Select(SmsRecognitionPresentation.OutcomeLabel));

        foreach (var text in texts)
        {
            Assert.DoesNotContain("支付方式", text);
            Assert.DoesNotContain("标签", text);
            Assert.DoesNotContain("交易记录", text);
            Assert.DoesNotContain("流水", text);
        }
    }
}
