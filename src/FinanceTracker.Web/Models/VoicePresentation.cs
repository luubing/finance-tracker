using System.Globalization;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.Web.Models;

/// <summary>转写文本的一段：<see cref="Kind"/> 为 null 是普通文字，否则是解析器认出的金额 / 分类 / 支付渠道 / 时间（要高亮）</summary>
public sealed record VoiceSegment(string Text, VoiceMatchKind? Kind);

/// <summary>语音覆盖层里「已识别」的一枚芯片（金额 / 分类 / 支付渠道 / 时间各一枚，识别到才有）</summary>
public sealed record VoiceChip(VoiceMatchKind Kind, string Icon, string Text);

/// <summary>
/// 一次实时解析要显示的内容：转写文本里要高亮的片段 + 已识别字段的芯片。
/// 两者口径一致——有芯片的字段才有高亮，反之亦然（高亮的就是会被预填的）。
/// </summary>
public sealed record VoiceLive(IReadOnlyList<VoiceMatchSpan> Matches, IReadOnlyList<VoiceChip> Chips)
{
    /// <summary>什么都没识别出来（或还没开始解析）</summary>
    public static VoiceLive Empty { get; } = new(Array.Empty<VoiceMatchSpan>(), Array.Empty<VoiceChip>());
}

/// <summary>
/// 语音记账覆盖层（设计稿 screens 06-add-voice）的展示规则（纯函数，单元测试覆盖）：
/// 标题 / 提示文案、转写文本按解析命中的片段切段高亮、已识别芯片、上滑取消的手势阈值。
/// 解析本身（口语 → 草稿）由 <c>BillVoiceParser</c> 负责且规则不变，这里只决定「怎么展示」。
/// </summary>
public static class VoicePresentation
{
    /// <summary>上滑多少像素算取消（相对按下的位置）；麦克风在标题栏右上角，上方空间有限，所以不取太大</summary>
    public const double SlideUpCancelPx = 56;

    /// <summary>朝任意方向（含下、左）滑开多少像素也算取消：上方空间不够时的兜底，同时对应原来的「滑出按钮取消」</summary>
    public const double SlideAwayCancelPx = 120;

    /// <summary>覆盖层隐藏时淡出的时长（毫秒）：与 <c>--ft-dur-fade</c> 同值，到点才把覆盖层从 DOM 里移除</summary>
    public const int LeaveMs = 180;

    // ------------------------------------------------------------------ 波形

    /// <summary>波形条数</summary>
    public const int WaveBarCount = 25;

    private static readonly double[] WaveJitter = [0.64, 1.0, 0.78, 0.92, 0.56, 0.96, 0.7];

    /// <summary>
    /// 第 <paramref name="index"/> 根波形条的静态高度比例（0~1）：中间高、两边低的钟形包络乘上一组固定的起伏，
    /// 看上去像一段语音的波形（设计稿 screens 06）；动画只在这个高度上下起伏。
    /// </summary>
    public static double WaveHeight(int index)
    {
        var i = Math.Clamp(index, 0, WaveBarCount - 1);
        var envelope = 0.3 + (0.7 * Math.Sin(Math.PI * (i + 0.5) / WaveBarCount));

        return Math.Round(Math.Clamp(envelope * WaveJitter[i % WaveJitter.Length], 0.12, 1), 2);
    }

    /// <summary>第 <paramref name="index"/> 根波形条动画的起始偏移（毫秒，负数 = 一开始就在动画中途）：让各根条错开节奏，不是整齐地一起起伏</summary>
    public static int WaveDelay(int index) => -(Math.Abs(index) * 97 % 840);

    /// <summary>波形条的内联样式：两个运行时变量（高度比例、动画偏移），数值恒用 . 作小数点，不受运行环境区域设置影响</summary>
    public static string WaveStyle(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"--ft-wave-h:{WaveHeight(index)};--ft-wave-delay:{WaveDelay(index)}ms");

    // ------------------------------------------------------------------ 文案

    /// <summary>覆盖层没有听到任何内容时，转写区的占位提示</summary>
    public const string Placeholder = "请说出账单内容，例如“昨天午饭花了三十五块，用微信支付”";

    /// <summary>覆盖层顶部状态：聆听中 / 等最终结果（松手或点完成之后）/ 结果已到（正在预填）</summary>
    public static string Title(bool parsing, bool final) => parsing
        ? final ? "识别完成，正在预填…" : "正在识别…"
        : "正在聆听…";

    /// <summary>
    /// 麦克风上方的一行提示：按住说话时是「上滑取消」，滑过阈值后变成「松开手指，取消」；
    /// 点按模式没有手势，提示「点一下麦克风结束」。解析 / 识别阶段不再提示。
    /// </summary>
    public static string Hint(bool holdMode, bool cancelling, bool parsing)
    {
        if (parsing)
        {
            return string.Empty;
        }

        if (holdMode)
        {
            return cancelling ? "松开手指，取消本次语音" : "上滑取消";
        }

        return "说完后点一下麦克风结束";
    }

    /// <summary>覆盖层底部的说明：设计稿的「松开手指，识别结果将预填到表单」；取消状态下换成放弃的说明</summary>
    public static string Footer(bool holdMode, bool cancelling) => cancelling
        ? "手指已滑到取消区，松开后本次录音将被丢弃"
        : holdMode
            ? "松开手指，识别结果将预填到表单"
            : "识别结果将预填到表单，确认后才会保存";

    // ------------------------------------------------------------------ 手势

    /// <summary>
    /// 按住期间手指相对按下点的位移（<paramref name="dx"/> / <paramref name="dy"/>，向上为负）是否已进入「取消区」：
    /// 上滑超过 <see cref="SlideUpCancelPx"/>，或朝任意方向离开按下点超过 <see cref="SlideAwayCancelPx"/>。
    /// 滑回阈值内会退出取消区（松手时才按所在区域决定是取消还是预填）。
    /// </summary>
    public static bool SlideCancels(double dx, double dy) =>
        dy <= -SlideUpCancelPx || Math.Sqrt((dx * dx) + (dy * dy)) >= SlideAwayCancelPx;

    // ------------------------------------------------------------------ 转写文本高亮

    /// <summary>
    /// 转写区最多显示的字数：区域固定 3 行高，28px 粗体在最窄的 320px 屏上每行也放得下 10 个字，3 行 = 30 个字。
    /// 说得更长时只显示末尾（最新说的），前面用「…」省略；完整文本不受影响——最终结果写进备注的是 <c>RawText</c> 原文。
    /// </summary>
    public const int TranscriptMaxChars = 30;

    /// <summary>
    /// 把转写文本按解析命中的片段切成「普通文字 / 高亮文字」交替的若干段。
    /// 片段按起点排序；越界的丢弃；与已取用片段重叠的丢弃（同一起点取更长的）。拼起来恒等于原文；
    /// 超过 <see cref="TranscriptMaxChars"/> 个字时只留末尾那么多，并在最前面补一段「…」（见 <see cref="Tail"/>）。
    /// </summary>
    public static IReadOnlyList<VoiceSegment> Segments(string? transcript, IReadOnlyList<VoiceMatchSpan>? matches)
    {
        if (string.IsNullOrEmpty(transcript))
        {
            return [];
        }

        var segments = new List<VoiceSegment>();
        var cursor = 0;

        foreach (var span in (matches ?? Array.Empty<VoiceMatchSpan>()).OrderBy(m => m.Start).ThenByDescending(m => m.Length))
        {
            if (span.Length <= 0 || span.Start < cursor || span.Start + span.Length > transcript.Length)
            {
                continue;
            }

            if (span.Start > cursor)
            {
                segments.Add(new VoiceSegment(transcript[cursor..span.Start], null));
            }

            segments.Add(new VoiceSegment(transcript.Substring(span.Start, span.Length), span.Kind));
            cursor = span.Start + span.Length;
        }

        if (cursor < transcript.Length)
        {
            segments.Add(new VoiceSegment(transcript[cursor..], null));
        }

        return Tail(segments, TranscriptMaxChars);
    }

    /// <summary>
    /// 只保留各段拼起来之后的末尾 <paramref name="maxChars"/> 个字，前面补一段「…」；没超长原样返回。
    /// 被截断的那一段保留它的高亮种类（只是少了开头几个字）；不会把代理对（如表情）从中间切开。
    /// </summary>
    public static IReadOnlyList<VoiceSegment> Tail(IReadOnlyList<VoiceSegment> segments, int maxChars)
    {
        var total = segments.Sum(s => s.Text.Length);
        if (maxChars <= 0 || total <= maxChars)
        {
            return segments;
        }

        var skip = total - maxChars;
        var result = new List<VoiceSegment>(segments.Count + 1) { new("…", null) };

        foreach (var segment in segments)
        {
            if (skip >= segment.Text.Length)
            {
                skip -= segment.Text.Length;
                continue;
            }

            if (skip > 0 && char.IsLowSurrogate(segment.Text[skip]))
            {
                skip++;
            }

            var text = segment.Text[skip..];
            if (text.Length > 0)
            {
                result.Add(skip > 0 ? segment with { Text = text } : segment);
            }

            skip = 0;
        }

        return result;
    }

    // ------------------------------------------------------------------ 已识别芯片

    /// <summary>金额芯片的图标</summary>
    public const string AmountIcon = "mdi-currency-cny";

    /// <summary>时间芯片的图标</summary>
    public const string TimeIcon = "mdi-clock-outline";

    /// <summary>
    /// 解析草稿里已识别出的字段 → 芯片（顺序固定：金额、分类、支付渠道、时间，识别到才有）。
    /// 金额是收入时带「收入」前缀，让用户一眼看出账单类型也被识别了；没有金额（或金额不是正数）不出金额芯片。
    /// 分类 / 渠道的名称与图标由调用方按「实际会被预填」的口径查好传进来（分类要属于识别后的账单类型）。
    /// </summary>
    public static IReadOnlyList<VoiceChip> Chips(
        ParsedBillDraft? draft,
        string? categoryName,
        string? categoryIcon,
        string? channelName,
        string? channelIcon,
        DateTime nowLocal)
    {
        if (draft is null)
        {
            return [];
        }

        var chips = new List<VoiceChip>(4);

        if (draft.Amount is decimal amount && amount > 0)
        {
            var text = MoneyPresentation.Format(amount);
            chips.Add(new VoiceChip(
                VoiceMatchKind.Amount,
                AmountIcon,
                draft.Type == BillType.Income ? $"收入 {text}" : text));
        }

        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            chips.Add(new VoiceChip(VoiceMatchKind.Category, CategoryPresentation.NormalizeIcon(categoryIcon), categoryName));
        }

        if (!string.IsNullOrWhiteSpace(channelName))
        {
            chips.Add(new VoiceChip(VoiceMatchKind.PaymentChannel, PaymentChannelPresentation.NormalizeIcon(channelIcon), channelName));
        }

        if (draft.TransactionTime is DateTime time)
        {
            chips.Add(new VoiceChip(VoiceMatchKind.Time, TimeIcon, AddBillPresentation.TimeLabel(time, nowLocal)));
        }

        return chips;
    }

    /// <summary>
    /// 解析草稿 → 覆盖层要显示的高亮与芯片。芯片按 <see cref="Chips"/> 的口径出；高亮只保留「有芯片的字段」的片段：
    /// 例如解析命中了分类、但分类不属于识别出的账单类型（预填时不会应用），就既没有芯片也不高亮。
    /// </summary>
    public static VoiceLive Live(
        ParsedBillDraft? draft,
        string? categoryName,
        string? categoryIcon,
        string? channelName,
        string? channelIcon,
        DateTime nowLocal)
    {
        if (draft is null)
        {
            return VoiceLive.Empty;
        }

        var chips = Chips(draft, categoryName, categoryIcon, channelName, channelIcon, nowLocal);
        var shown = chips.Select(c => c.Kind).ToHashSet();

        return new VoiceLive(draft.Matches.Where(m => shown.Contains(m.Kind)).ToArray(), chips);
    }

    /// <summary>高亮片段 / 芯片的种类 → 观察属性值（E2E 与样式共用）</summary>
    public static string KindKey(VoiceMatchKind kind) => kind switch
    {
        VoiceMatchKind.Amount => "amount",
        VoiceMatchKind.Category => "category",
        VoiceMatchKind.PaymentChannel => "channel",
        _ => "time",
    };
}
