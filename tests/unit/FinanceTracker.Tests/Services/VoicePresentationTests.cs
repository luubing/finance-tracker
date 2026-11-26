using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 语音记账覆盖层展示规则的单元测试（17 号）：转写文本按命中片段切段高亮、已识别芯片、上滑取消阈值、文案。
/// </summary>
public class VoicePresentationTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 15, 40, 0, DateTimeKind.Unspecified);

    private static string Joined(IEnumerable<VoiceSegment> segments) => string.Concat(segments.Select(s => s.Text));

    // ------------------------------------------------------------------ 转写文本切段

    [Fact]
    public void 没有命中片段_整段都是普通文字()
    {
        var segments = VoicePresentation.Segments("今天天气不错", []);

        Assert.Equal([new VoiceSegment("今天天气不错", null)], segments);
    }

    [Fact]
    public void 命中的片段单独成段_其余是普通文字()
    {
        const string text = "昨天午饭花了三十五块，用微信支付";
        var segments = VoicePresentation.Segments(
            text,
            [
                new VoiceMatchSpan(0, 2, VoiceMatchKind.Time),
                new VoiceMatchSpan(2, 2, VoiceMatchKind.Category),
                new VoiceMatchSpan(6, 4, VoiceMatchKind.Amount),
                new VoiceMatchSpan(12, 4, VoiceMatchKind.PaymentChannel),
            ]);

        Assert.Equal(
            [
                new VoiceSegment("昨天", VoiceMatchKind.Time),
                new VoiceSegment("午饭", VoiceMatchKind.Category),
                new VoiceSegment("花了", null),
                new VoiceSegment("三十五块", VoiceMatchKind.Amount),
                new VoiceSegment("，用", null),
                new VoiceSegment("微信支付", VoiceMatchKind.PaymentChannel),
            ],
            segments);
        Assert.Equal(text, Joined(segments));
    }

    [Fact]
    public void 片段乱序传入_按位置切段()
    {
        var segments = VoicePresentation.Segments(
            "花了35块，用微信",
            [
                new VoiceMatchSpan(7, 2, VoiceMatchKind.PaymentChannel),
                new VoiceMatchSpan(2, 3, VoiceMatchKind.Amount),
            ]);

        Assert.Equal(
            [
                new VoiceSegment("花了", null),
                new VoiceSegment("35块", VoiceMatchKind.Amount),
                new VoiceSegment("，用", null),
                new VoiceSegment("微信", VoiceMatchKind.PaymentChannel),
            ],
            segments);
    }

    [Fact]
    public void 片段在开头和结尾_没有多余的空段()
    {
        var segments = VoicePresentation.Segments(
            "35块现金",
            [
                new VoiceMatchSpan(0, 3, VoiceMatchKind.Amount),
                new VoiceMatchSpan(3, 2, VoiceMatchKind.PaymentChannel),
            ]);

        Assert.Equal(
            [new VoiceSegment("35块", VoiceMatchKind.Amount), new VoiceSegment("现金", VoiceMatchKind.PaymentChannel)],
            segments);
    }

    [Fact]
    public void 重叠的片段_保留先出现的_同起点取更长的()
    {
        // 「京东白条」里「京东」(渠道) 与更长的「京东白条」同起点：取更长的；后面压在它上面的片段丢弃
        var segments = VoicePresentation.Segments(
            "京东白条花了20块",
            [
                new VoiceMatchSpan(0, 2, VoiceMatchKind.PaymentChannel),
                new VoiceMatchSpan(0, 4, VoiceMatchKind.PaymentChannel),
                new VoiceMatchSpan(2, 2, VoiceMatchKind.Category),
            ]);

        Assert.Equal(
            [new VoiceSegment("京东白条", VoiceMatchKind.PaymentChannel), new VoiceSegment("花了20块", null)],
            segments);
        Assert.Equal("京东白条花了20块", Joined(segments));
    }

    [Fact]
    public void 越界或长度非正的片段_被忽略_不会抛异常()
    {
        const string text = "花了35块";
        var segments = VoicePresentation.Segments(
            text,
            [
                new VoiceMatchSpan(-1, 2, VoiceMatchKind.Time),
                new VoiceMatchSpan(2, 0, VoiceMatchKind.Time),
                new VoiceMatchSpan(4, 9, VoiceMatchKind.Amount),
                new VoiceMatchSpan(2, 3, VoiceMatchKind.Amount),
            ]);

        Assert.Equal([new VoiceSegment("花了", null), new VoiceSegment("35块", VoiceMatchKind.Amount)], segments);
        Assert.Equal(text, Joined(segments));
    }

    [Fact]
    public void 转写文本变短_旧的片段超出范围_被忽略()
    {
        // 实时转写会修正前面的字：上一轮的片段位置可能已经越过当前文本
        var segments = VoicePresentation.Segments("花了", [new VoiceMatchSpan(2, 3, VoiceMatchKind.Amount)]);

        Assert.Equal([new VoiceSegment("花了", null)], segments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 空转写_没有任何段(string? transcript)
    {
        Assert.Empty(VoicePresentation.Segments(transcript, [new VoiceMatchSpan(0, 1, VoiceMatchKind.Amount)]));
    }

    [Fact]
    public void 片段为null_当作没有片段()
    {
        Assert.Equal([new VoiceSegment("你好", null)], VoicePresentation.Segments("你好", null));
    }

    // ------------------------------------------------------------------ 已识别芯片

    private static ParsedBillDraft Draft(decimal? amount = null, BillType type = BillType.Expense, DateTime? time = null) =>
        new() { Amount = amount, Type = type, TransactionTime = time };

    [Fact]
    public void 四项都识别_按金额_分类_渠道_时间的顺序出芯片()
    {
        var yesterday = new DateTime(2026, 11, 24, 12, 0, 0);
        var chips = VoicePresentation.Chips(Draft(35m, time: yesterday), "餐饮美食", "mdi-food", "微信支付", "mdi-wechat", Now);

        Assert.Equal(
            [
                new VoiceChip(VoiceMatchKind.Amount, "mdi-currency-cny", "¥35.00"),
                new VoiceChip(VoiceMatchKind.Category, "mdi-food", "餐饮美食"),
                new VoiceChip(VoiceMatchKind.PaymentChannel, "mdi-wechat", "微信支付"),
                new VoiceChip(VoiceMatchKind.Time, "mdi-clock-outline", "昨天 12:00"),
            ],
            chips);
    }

    [Fact]
    public void 只识别到金额_只有一枚芯片()
    {
        var chips = VoicePresentation.Chips(Draft(12.5m), null, null, null, null, Now);

        Assert.Equal([new VoiceChip(VoiceMatchKind.Amount, "mdi-currency-cny", "¥12.50")], chips);
    }

    [Fact]
    public void 收入金额带收入前缀_千分位与两位小数()
    {
        var chips = VoicePresentation.Chips(Draft(8000m, BillType.Income), null, null, null, null, Now);

        Assert.Equal("收入 ¥8,000.00", Assert.Single(chips).Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void 没有金额或金额不是正数_不出金额芯片(int? amount)
    {
        var chips = VoicePresentation.Chips(Draft(amount), "餐饮美食", "mdi-food", null, null, Now);

        Assert.DoesNotContain(chips, c => c.Kind == VoiceMatchKind.Amount);
        Assert.Contains(chips, c => c.Kind == VoiceMatchKind.Category);
    }

    [Fact]
    public void 分类与渠道名称为空白_不出芯片()
    {
        var chips = VoicePresentation.Chips(Draft(10m), " ", "mdi-food", "", "mdi-wechat", Now);

        Assert.Equal([VoiceMatchKind.Amount], chips.Select(c => c.Kind).ToArray());
    }

    [Fact]
    public void 图标缺失或不合法_回退到默认图标_不会渲染出空白()
    {
        var chips = VoicePresentation.Chips(Draft(), "自定义", null, "自定义渠道", "  ", Now);

        Assert.All(chips, c => Assert.StartsWith("mdi-", c.Icon));
        Assert.Equal(2, chips.Count);
    }

    [Fact]
    public void 草稿为null_没有芯片()
    {
        Assert.Empty(VoicePresentation.Chips(null, "餐饮美食", "mdi-food", "微信支付", "mdi-wechat", Now));
    }

    [Fact]
    public void 时间芯片沿用记一笔里时间行的文案()
    {
        var today = VoicePresentation.Chips(Draft(time: Now), null, null, null, null, Now);
        var older = VoicePresentation.Chips(Draft(time: new DateTime(2026, 5, 20, 9, 5, 0)), null, null, null, null, Now);
        var lastYear = VoicePresentation.Chips(Draft(time: new DateTime(2025, 12, 30, 18, 30, 0)), null, null, null, null, Now);

        Assert.Equal("今天 15:40", Assert.Single(today).Text);
        Assert.Equal("5月20日 09:05", Assert.Single(older).Text);
        Assert.Equal("2025年12月30日 18:30", Assert.Single(lastYear).Text);
    }

    // ------------------------------------------------------------------ 上滑取消

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, -55, false)]
    [InlineData(0, -56, true)]
    [InlineData(0, -200, true)]
    [InlineData(30, -30, false)]
    [InlineData(-40, -57, true)]
    [InlineData(100, 0, false)]
    [InlineData(-119, 0, false)]
    [InlineData(120, 0, true)]
    [InlineData(-120, 0, true)]
    [InlineData(0, 120, true)]
    [InlineData(60, 60, false)]
    [InlineData(85, 85, true)]
    public void 滑动位移进入取消区的判定(double dx, double dy, bool expected)
    {
        Assert.Equal(expected, VoicePresentation.SlideCancels(dx, dy));
    }

    [Fact]
    public void 向下小幅滑动不取消_手指自然抖动不会误取消()
    {
        Assert.False(VoicePresentation.SlideCancels(8, 14));
        Assert.False(VoicePresentation.SlideCancels(-12, 6));
    }

    // ------------------------------------------------------------------ 文案

    [Theory]
    [InlineData(false, false, "正在聆听…")]
    [InlineData(false, true, "正在聆听…")]
    [InlineData(true, false, "正在识别…")]
    [InlineData(true, true, "识别完成，正在预填…")]
    public void 状态标题(bool parsing, bool final, string expected)
    {
        Assert.Equal(expected, VoicePresentation.Title(parsing, final));
    }

    [Fact]
    public void 按住模式的提示_上滑取消_滑过阈值后变成松手取消()
    {
        Assert.Equal("上滑取消", VoicePresentation.Hint(holdMode: true, cancelling: false, parsing: false));
        Assert.Equal("松开手指，取消本次语音", VoicePresentation.Hint(holdMode: true, cancelling: true, parsing: false));
    }

    [Fact]
    public void 点按模式的提示_点一下麦克风结束()
    {
        Assert.Equal("说完后点一下麦克风结束", VoicePresentation.Hint(holdMode: false, cancelling: false, parsing: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 识别阶段不再给手势提示(bool holdMode)
    {
        Assert.Equal(string.Empty, VoicePresentation.Hint(holdMode, cancelling: false, parsing: true));
    }

    [Fact]
    public void 底部说明_沿用设计稿的措辞_取消状态下换成放弃的说明()
    {
        Assert.Equal("松开手指，识别结果将预填到表单", VoicePresentation.Footer(holdMode: true, cancelling: false));
        Assert.Equal("识别结果将预填到表单，确认后才会保存", VoicePresentation.Footer(holdMode: false, cancelling: false));
        Assert.Contains("丢弃", VoicePresentation.Footer(holdMode: true, cancelling: true));
    }

    [Fact]
    public void 种类的观察属性值()
    {
        Assert.Equal("amount", VoicePresentation.KindKey(VoiceMatchKind.Amount));
        Assert.Equal("category", VoicePresentation.KindKey(VoiceMatchKind.Category));
        Assert.Equal("channel", VoicePresentation.KindKey(VoiceMatchKind.PaymentChannel));
        Assert.Equal("time", VoicePresentation.KindKey(VoiceMatchKind.Time));
    }

    // ------------------------------------------------------------------ 实时解析：高亮与芯片口径一致

    private static ParsedBillDraft LiveDraft(IReadOnlyList<VoiceMatchSpan> matches, decimal? amount = 35m, DateTime? time = null) =>
        new() { Amount = amount, Type = BillType.Expense, TransactionTime = time, Matches = matches };

    [Fact]
    public void 实时解析_高亮只保留有芯片的字段()
    {
        var draft = LiveDraft(
            [
                new VoiceMatchSpan(0, 2, VoiceMatchKind.Time),
                new VoiceMatchSpan(2, 2, VoiceMatchKind.Category),
                new VoiceMatchSpan(6, 4, VoiceMatchKind.Amount),
                new VoiceMatchSpan(12, 4, VoiceMatchKind.PaymentChannel),
            ]);

        // 没有 TransactionTime → 没有时间芯片；分类不属于识别出的账单类型时调用方传 null 名称 → 没有分类芯片；对应的高亮都要去掉
        var live = VoicePresentation.Live(draft, null, null, "微信支付", "mdi-wechat", Now);

        Assert.Equal(new[] { VoiceMatchKind.Amount, VoiceMatchKind.PaymentChannel }, live.Chips.Select(c => c.Kind).ToArray());
        Assert.Equal(new[] { VoiceMatchKind.Amount, VoiceMatchKind.PaymentChannel }, live.Matches.Select(m => m.Kind).ToArray());
    }

    [Fact]
    public void 实时解析_四项都有_高亮与芯片一一对应()
    {
        var draft = LiveDraft(
            [
                new VoiceMatchSpan(0, 2, VoiceMatchKind.Time),
                new VoiceMatchSpan(2, 2, VoiceMatchKind.Category),
                new VoiceMatchSpan(6, 4, VoiceMatchKind.Amount),
                new VoiceMatchSpan(12, 4, VoiceMatchKind.PaymentChannel),
            ],
            time: new DateTime(2026, 11, 24, 12, 0, 0));

        var live = VoicePresentation.Live(draft, "餐饮美食", "mdi-food", "微信支付", "mdi-wechat", Now);

        Assert.Equal(4, live.Chips.Count);
        Assert.Equal(4, live.Matches.Count);
        Assert.Equal(
            live.Chips.Select(c => c.Kind).OrderBy(k => k).ToArray(),
            live.Matches.Select(m => m.Kind).OrderBy(k => k).ToArray());
    }

    [Fact]
    public void 实时解析_草稿为null或什么都没识别_是空结果()
    {
        Assert.Same(VoiceLive.Empty, VoicePresentation.Live(null, "餐饮美食", "mdi-food", null, null, Now));

        var nothing = VoicePresentation.Live(LiveDraft([], amount: null), null, null, null, null, Now);
        Assert.Empty(nothing.Chips);
        Assert.Empty(nothing.Matches);
    }

    // ------------------------------------------------------------------ 波形

    [Fact]
    public void 波形高度在合法范围内_中间高两边低()
    {
        var heights = Enumerable.Range(0, VoicePresentation.WaveBarCount).Select(VoicePresentation.WaveHeight).ToArray();

        Assert.All(heights, h => Assert.InRange(h, 0.12, 1.0));
        Assert.True(VoicePresentation.WaveHeight(VoicePresentation.WaveBarCount / 2) > VoicePresentation.WaveHeight(0), "中间的条应该比最左边的高");
        Assert.True(VoicePresentation.WaveHeight(VoicePresentation.WaveBarCount / 2) > VoicePresentation.WaveHeight(VoicePresentation.WaveBarCount - 1), "中间的条应该比最右边的高");
        Assert.True(heights.Distinct().Count() > 8, "波形不能是一排整齐的等高条");
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(999, 24)]
    public void 波形下标越界_按最近的一根取值(int index, int clamped)
    {
        Assert.Equal(VoicePresentation.WaveHeight(clamped), VoicePresentation.WaveHeight(index));
    }

    [Fact]
    public void 波形动画偏移为负且互相错开()
    {
        var delays = Enumerable.Range(0, VoicePresentation.WaveBarCount).Select(VoicePresentation.WaveDelay).ToArray();

        Assert.All(delays, d => Assert.InRange(d, -839, 0));
        Assert.True(delays.Distinct().Count() > 10, "各根条的动画偏移要错开");
    }

    [Fact]
    public void 波形内联样式恒用点作小数点_不受区域设置影响()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            Assert.Equal("--ft-wave-h:0.22;--ft-wave-delay:0ms", VoicePresentation.WaveStyle(0));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    // ------------------------------------------------------------------ 转写区只显示末尾

    [Fact]
    public void 不超长的转写原样显示()
    {
        var text = new string('字', VoicePresentation.TranscriptMaxChars);
        var segments = VoicePresentation.Segments(text, []);

        Assert.Equal([new VoiceSegment(text, null)], segments);
    }

    [Fact]
    public void 超长的转写只留末尾_前面补省略号()
    {
        var text = string.Concat(Enumerable.Range(0, 40).Select(i => (char)('一' + i)));
        var segments = VoicePresentation.Segments(text, []);

        Assert.Equal(new VoiceSegment("…", null), segments[0]);
        Assert.Equal("…" + text[^VoicePresentation.TranscriptMaxChars..], Joined(segments));
    }

    [Fact]
    public void 截断落在高亮片段里_该片段留下剩余部分并保留种类()
    {
        // 「三十五块」被截掉前两个字，剩「五块」仍是金额高亮
        var text = "三十五块" + new string('字', VoicePresentation.TranscriptMaxChars - 2);
        var segments = VoicePresentation.Segments(text, [new VoiceMatchSpan(0, 4, VoiceMatchKind.Amount)]);

        Assert.Equal(new VoiceSegment("…", null), segments[0]);
        Assert.Equal(new VoiceSegment("五块", VoiceMatchKind.Amount), segments[1]);
    }

    [Fact]
    public void 截断不会把表情的代理对从中间切开()
    {
        var text = "😀" + new string('x', VoicePresentation.TranscriptMaxChars - 1);
        var segments = VoicePresentation.Segments(text, []);

        Assert.Equal("…" + new string('x', VoicePresentation.TranscriptMaxChars - 1), Joined(segments));
        Assert.False(Joined(segments).Any(c => char.IsSurrogate(c)), "不能留下落单的代理字符");
    }

    [Fact]
    public void 上限非正_不截断()
    {
        IReadOnlyList<VoiceSegment> input = [new VoiceSegment(new string('字', 50), null)];

        Assert.Same(input, VoicePresentation.Tail(input, 0));
    }
}
