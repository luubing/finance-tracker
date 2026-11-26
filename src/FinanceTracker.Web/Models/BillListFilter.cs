using System.Globalization;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Models;
using FinanceTracker.Core.Time;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 账单筛选的四个维度（筛选 Sheet 的四组标签，也是账单 Tab 条件条上的四颗标签）。
/// 账单类型（全部 / 支出 / 收入）不在其中——它有自己的分段控件。
/// </summary>
public enum BillFilterDimension
{
    /// <summary>时间：今天 / 本周 / 本月 / 本年 / 自定义起止日期</summary>
    Time,

    /// <summary>账本：某个账本（含共享账本），或「未归属账本」</summary>
    Ledger,

    /// <summary>分类</summary>
    Category,

    /// <summary>支付渠道</summary>
    Channel,
}

/// <summary>
/// 时间维度的快捷选项。预设只存「哪一个」，查询时才按当时的应用时区日期换算成起止——
/// 页面常驻（Tab 根页面）跨过午夜后，「今天」跟着变，不会停在昨天。
/// </summary>
public enum BillTimePreset
{
    /// <summary>没有选预设（可能没有时间条件，也可能是自定义起止日期）</summary>
    None = 0,

    /// <summary>今天</summary>
    Today,

    /// <summary>本周（周一至周日，全应用统一以周一为一周之始）</summary>
    Week,

    /// <summary>本月</summary>
    Month,

    /// <summary>本年</summary>
    Year,
}

/// <summary>时间预设的文案与换算</summary>
public static class BillTimePresets
{
    /// <summary>筛选 Sheet 里依次展示的预设</summary>
    public static IReadOnlyList<BillTimePreset> All { get; } =
        [BillTimePreset.Today, BillTimePreset.Week, BillTimePreset.Month, BillTimePreset.Year];

    /// <summary>预设的显示文案</summary>
    public static string Label(BillTimePreset preset) => preset switch
    {
        BillTimePreset.Today => "今天",
        BillTimePreset.Week => "本周",
        BillTimePreset.Month => "本月",
        BillTimePreset.Year => "本年",
        _ => string.Empty,
    };

    /// <summary>
    /// 预设对应的起止（含端点，应用时区的墙上时间）：起点是当天 / 周一 / 当月 1 日 / 当年 1 月 1 日的零点，
    /// 终点是对应那一天 / 周日 / 当月最后一天 / 12 月 31 日的最后一刻（<see cref="TimeBoundaries"/>）。
    /// 整段都是完整的日历周期，不是「到现在为止」——今天稍晚记的账（含手动改成今天晚些时候的时间）同样算在内。
    /// </summary>
    /// <param name="preset">预设（不能是 <see cref="BillTimePreset.None"/>）</param>
    /// <param name="nowLocal">应用时区的「现在」</param>
    public static (DateTime Start, DateTime End) RangeOf(BillTimePreset preset, DateTime nowLocal)
    {
        var today = nowLocal.Date;

        switch (preset)
        {
            case BillTimePreset.Today:
                return (today, TimeBoundaries.EndOfDay(today));

            case BillTimePreset.Week:
                var monday = TimeBoundaries.StartOfWeek(today);
                return (monday, TimeBoundaries.EndOfDay(monday.AddDays(6)));

            case BillTimePreset.Month:
                return (new DateTime(today.Year, today.Month, 1), TimeBoundaries.EndOfMonth(today.Year, today.Month));

            case BillTimePreset.Year:
                return (new DateTime(today.Year, 1, 1), TimeBoundaries.EndOfMonth(today.Year, 12));

            default:
                throw new ArgumentOutOfRangeException(nameof(preset), preset, "没有选时间预设时不存在对应的起止");
        }
    }
}

/// <summary>
/// 账单列表页的筛选状态：账单 Tab 与筛选 Sheet（编辑它的副本）共用。
/// 查库时用 <see cref="ToQuery(DateTime)"/> 转成服务层的 <see cref="BillFilter"/>——列表、笔数、汇总、
/// Sheet 底部「查看 N 笔账单」的预估数全部走同一个转换，口径不会分叉。
/// <para>
/// 四个维度（时间 / 账本 / 分类 / 支付渠道）里<b>分类可以多选</b>（<see cref="CategoryIds"/>，属于其中任意一个即符合），其余三个都是单选；
/// 账单类型由分段控件设置、搜索关键字由搜索框设置，都不算维度。
/// <b>时间</b>维度二选一：<see cref="TimePreset"/>（快捷选项）或自定义起止日期（<see cref="StartDate"/> / <see cref="EndDate"/>）——
/// 用 <see cref="SelectPreset"/> / <see cref="SetCustomRange"/> 修改，不要两边同时设置（设置了预设，自定义日期不再生效）。
/// <b>账本</b>维度三选一：不限 / 某个账本（<see cref="LedgerId"/>）/「未归属账本」（<see cref="UnassignedLedger"/>）。
/// </para>
/// <para>
/// <b>默认视图</b>：账单 Tab 打开时看的是「本月」（<see cref="DefaultTimePreset"/>，设计稿 02 / 13），其余维度不限、账单类型全部。
/// 默认的「本月」<b>不算筛选条件</b>——不计入角标数（<see cref="PanelCount"/>）、条件条上是中性标签而不是已设置的标签、「重置」回到它；
/// 选了别的时间范围、或者把时间取消成「不限时间」（<see cref="BillTimePreset.None"/>），才算偏离默认视图。
/// 用 <see cref="ResetPanel"/> / <see cref="ResetAll"/> 回到默认视图（<c>new BillListFilter()</c> 本身是「什么条件都没有」，页面负责在打开时调用 <see cref="ResetPanel"/>）；
/// <c>Clear</c> 系列是「去掉条件」，时间维度去掉后是不限时间。
/// </para>
/// <para>
/// 自定义日期是 <c>&lt;input type="date"&gt;</c> 的文本（<c>yyyy-MM-dd</c>），表示<b>应用时区</b>的日历日；
/// 结束日期含当天（取当天最后一个 tick，见 <see cref="TimeBoundaries.EndOfDay"/>）。
/// 解析不了的文本（用户键盘输入到一半）当作「没填」，不抛异常。
/// </para>
/// </summary>
public sealed class BillListFilter
{
    /// <summary>日期框的文本格式</summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>账单 Tab 打开时的时间范围（默认视图）：本月。它不算筛选条件，见类说明</summary>
    public const BillTimePreset DefaultTimePreset = BillTimePreset.Month;

    private readonly List<Guid> _categoryIds = [];

    /// <summary>时间维度：快捷选项；不是 <see cref="BillTimePreset.None"/> 时忽略自定义起止日期</summary>
    public BillTimePreset TimePreset { get; set; }

    /// <summary>自定义开始日期（含）</summary>
    public string? StartDate { get; set; }

    /// <summary>自定义结束日期（含当天）</summary>
    public string? EndDate { get; set; }

    /// <summary>
    /// 分类（可多选，按选中的先后排列）：账单属于其中任意一个即符合；空 = 不限分类。
    /// 用 <see cref="ToggleCategory"/> / <see cref="RetainCategories"/> / <see cref="Clear(BillFilterDimension)"/> 修改。
    /// </summary>
    public IReadOnlyList<Guid> CategoryIds => _categoryIds;

    public Guid? PaymentChannelId { get; set; }

    /// <summary>账本；选了共享账本时，账本内全部成员的账单可见</summary>
    public Guid? LedgerId { get; set; }

    /// <summary>只看「未归属账本」的账单（历史上没有账本的账单）；与 <see cref="LedgerId"/> 互斥</summary>
    public bool UnassignedLedger { get; set; }

    /// <summary>账单类型；页面上由「全部 / 支出 / 收入」分段控件控制</summary>
    public BillType? Type { get; set; }

    private string? _keyword;

    /// <summary>
    /// 搜索关键字（账单 Tab 顶部的搜索框，10 号 ticket）：按备注、分类名称匹配，整体是数字时再匹配金额（匹配规则见 <see cref="BillKeyword"/>）。
    /// 赋值时首尾空白被去掉，空白 = 没有关键字条件（读出来是 null）。
    /// 它<b>不是</b>筛选维度——不在筛选 Sheet 里、不计入角标数、条件条上也没有标签（搜索框里的文字本身就是它的展示），
    /// 但与其他条件叠加（取交集），并随页面状态一起跨 Tab 切换保留。
    /// </summary>
    public string? Keyword
    {
        get => _keyword;
        set => _keyword = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>设置了搜索关键字</summary>
    public bool HasKeyword => _keyword is not null;

    /// <summary>设置了自定义起止日期中的任意一个（解析得了的才算）</summary>
    public bool HasDateRange => ParseDate(StartDate) is not null || ParseDate(EndDate) is not null;

    /// <summary>有没有时间范围的限制：选了快捷选项（含默认的「本月」），或设置了自定义起止日期</summary>
    public bool HasTime => TimePreset != BillTimePreset.None || HasDateRange;

    /// <summary>当前的时间范围是不是默认的「本月」（选了预设才算；自定义起止日期即使恰好是整月也不算）</summary>
    public bool IsDefaultTime => TimePreset == DefaultTimePreset;

    /// <summary>
    /// 有没有<b>偏离默认视图</b>的时间条件：限定了时间、而且不是默认的「本月」。
    /// 「不限时间」不属于这里——它是没有时间限制，不是一个时间条件（条件条上显示中性的「时间」标签）。
    /// </summary>
    public bool HasTimeCondition => HasTime && !IsDefaultTime;

    /// <summary>有没有生效的账本条件：某个账本，或「未归属账本」</summary>
    public bool HasLedger => LedgerId.HasValue || UnassignedLedger;

    /// <summary>有没有生效的分类条件（至少选了一个分类）</summary>
    public bool HasCategory => _categoryIds.Count > 0;

    /// <summary>
    /// 筛选条件的维度数：时间、账本、分类、支付渠道各算一项（时间里起止任填一个、预设都只算一项；默认的「本月」不算；分类选几个都只算一项）。
    /// 账单类型与搜索关键字不计——它们各有自己的控件（分段控件、搜索框），一眼就能看出来
    /// </summary>
    public int PanelCount =>
        (HasTimeCondition ? 1 : 0)
        + (HasLedger ? 1 : 0)
        + (HasCategory ? 1 : 0)
        + (PaymentChannelId.HasValue ? 1 : 0);

    /// <summary>四个维度是否都在默认状态：时间是「本月」，账本 / 分类 / 支付渠道都不限。筛选 Sheet 的「重置」据此置灰</summary>
    public bool IsDefaultPanel => IsDefaultTime && PanelCount == 0;

    /// <summary>有没有筛选条件（四个维度或账单类型；不含搜索关键字）</summary>
    public bool HasConditions => PanelCount > 0 || Type.HasValue;

    /// <summary>有没有任何条件（含账单类型与搜索关键字）：空状态据此决定说「没有账单」还是「没有符合条件的账单」</summary>
    public bool HasAny => HasConditions || HasKeyword;

    /// <summary>某个维度当前有没有条件（时间维度只算偏离默认「本月」的；条件条据此决定显示已设置的标签还是中性标签）</summary>
    public bool Has(BillFilterDimension dimension) => dimension switch
    {
        BillFilterDimension.Time => HasTimeCondition,
        BillFilterDimension.Ledger => HasLedger,
        BillFilterDimension.Category => HasCategory,
        BillFilterDimension.Channel => PaymentChannelId.HasValue,
        _ => false,
    };

    /// <summary>自定义起止日期（解析不了的当作没填）</summary>
    public (DateTime? Start, DateTime? End) CustomRange => (ParseDate(StartDate), ParseDate(EndDate));

    /// <summary>
    /// 当前时间条件换算成的起止（含端点，应用时区墙上时间）；没有时间条件时两端都是空，只填了一端时另一端为空。
    /// </summary>
    /// <param name="nowLocal">应用时区的「现在」：预设据此换算</param>
    public (DateTime? Start, DateTime? End) ResolveRange(DateTime nowLocal)
    {
        if (TimePreset != BillTimePreset.None)
        {
            var (start, end) = BillTimePresets.RangeOf(TimePreset, nowLocal);
            return (start, end);
        }

        var (customStart, customEnd) = CustomRange;
        return (customStart?.Date, customEnd is { } day ? TimeBoundaries.EndOfDay(day) : null);
    }

    /// <summary>转成服务层的查询条件；日期 / 预设按应用时区理解，结束时刻取当天最后一刻</summary>
    /// <param name="nowLocal">应用时区的「现在」：预设据此换算</param>
    public BillFilter ToQuery(DateTime nowLocal)
    {
        var (start, end) = ResolveRange(nowLocal);

        return new BillFilter
        {
            StartDate = start,
            EndDate = end,
            CategoryIds = _categoryIds.Count > 0 ? _categoryIds.ToArray() : null,
            PaymentChannelId = PaymentChannelId,
            LedgerId = LedgerId,
            UnassignedLedger = UnassignedLedger,
            Type = Type,
            Keyword = Keyword,
        };
    }

    /// <summary>转成服务层的查询条件（以此刻的应用时区日期换算时间预设）</summary>
    public BillFilter ToQuery() => ToQuery(AppTimeZone.ToLocal(DateTime.UtcNow));

    // ------------------------------------------------------------------ 修改（保持每个维度内部互斥）

    /// <summary>选一个时间预设（同时清掉自定义起止日期）；传 <see cref="BillTimePreset.None"/> 等于清除时间条件</summary>
    public void SelectPreset(BillTimePreset preset)
    {
        TimePreset = preset;
        StartDate = null;
        EndDate = null;
    }

    /// <summary>设置自定义起止日期（同时取消时间预设）</summary>
    public void SetCustomRange(string? start, string? end)
    {
        TimePreset = BillTimePreset.None;
        StartDate = start;
        EndDate = end;
    }

    /// <summary>选一个具体账本（取消「未归属账本」）</summary>
    public void SelectLedger(Guid ledgerId)
    {
        LedgerId = ledgerId;
        UnassignedLedger = false;
    }

    /// <summary>选「未归属账本」（取消具体账本）</summary>
    public void SelectUnassignedLedger()
    {
        LedgerId = null;
        UnassignedLedger = true;
    }

    /// <summary>切换一个分类的选中状态（多选）：没选的加进来，已选的取消；全部取消后等于不限分类</summary>
    public void ToggleCategory(Guid categoryId)
    {
        if (!_categoryIds.Remove(categoryId))
        {
            _categoryIds.Add(categoryId);
        }
    }

    /// <summary>
    /// 只保留<b>仍然存在</b>的分类（分类被删除后，条件里指向它的那几项没有名字可显示、也不会再匹配到账单，应当去掉）；
    /// 其余已选分类不受影响。返回是否去掉了东西。
    /// </summary>
    /// <param name="existingIds">现存的分类 Id</param>
    public bool RetainCategories(IEnumerable<Guid> existingIds)
    {
        var existing = existingIds.ToHashSet();
        return _categoryIds.RemoveAll(id => !existing.Contains(id)) > 0;
    }

    /// <summary>清掉某个维度的条件（时间维度清掉后是「不限时间」，不是默认的「本月」，要回默认用 <see cref="ResetPanel"/>）</summary>
    public void Clear(BillFilterDimension dimension)
    {
        switch (dimension)
        {
            case BillFilterDimension.Time:
                SelectPreset(BillTimePreset.None);
                break;
            case BillFilterDimension.Ledger:
                LedgerId = null;
                UnassignedLedger = false;
                break;
            case BillFilterDimension.Category:
                _categoryIds.Clear();
                break;
            case BillFilterDimension.Channel:
                PaymentChannelId = null;
                break;
        }
    }

    /// <summary>清掉四个维度的条件（保留账单类型与搜索关键字）；时间是「不限时间」</summary>
    public void ClearPanel()
    {
        foreach (var dimension in Enum.GetValues<BillFilterDimension>())
        {
            Clear(dimension);
        }
    }

    /// <summary>清掉全部条件（含账单类型与搜索关键字）；时间是「不限时间」</summary>
    public void Clear()
    {
        ClearPanel();
        Type = null;
        Keyword = null;
    }

    /// <summary>
    /// 去掉所有<b>生效的条件</b>（偏离默认的维度、账单类型、搜索关键字），时间维度没有条件时原样保留——
    /// 默认的「本月」不是条件，「清除搜索」「清除筛选」不该顺手把它取消；偏离默认的时间条件（本年、自定义）去掉后是「不限时间」。
    /// 账单 Tab 空状态里的清除入口用它。
    /// </summary>
    public void ClearConditions()
    {
        foreach (var dimension in Enum.GetValues<BillFilterDimension>().Where(Has))
        {
            Clear(dimension);
        }

        Type = null;
        Keyword = null;
    }

    /// <summary>
    /// 四个维度回到默认视图（时间「本月」，账本 / 分类 / 支付渠道不限），保留账单类型与搜索关键字。
    /// 筛选 Sheet 的「重置」，以及账单 Tab 打开时的初始状态。
    /// </summary>
    public void ResetPanel()
    {
        ClearPanel();
        SelectPreset(DefaultTimePreset);
    }

    /// <summary>全部回到默认视图（含账单类型与搜索关键字）：空状态里的「清除筛选」、换账号</summary>
    public void ResetAll()
    {
        ResetPanel();
        Type = null;
        Keyword = null;
    }

    // ------------------------------------------------------------------ 副本

    /// <summary>复制一份：筛选 Sheet 编辑副本，点「查看」才写回页面的筛选状态</summary>
    public BillListFilter Clone()
    {
        var copy = new BillListFilter();
        copy.CopyFrom(this);
        return copy;
    }

    /// <summary>用另一份筛选状态覆盖自己</summary>
    public void CopyFrom(BillListFilter other)
    {
        TimePreset = other.TimePreset;
        StartDate = other.StartDate;
        EndDate = other.EndDate;
        LedgerId = other.LedgerId;
        UnassignedLedger = other.UnassignedLedger;
        var categoryIds = other._categoryIds.ToArray();
        _categoryIds.Clear();
        _categoryIds.AddRange(categoryIds);
        PaymentChannelId = other.PaymentChannelId;
        Type = other.Type;
        Keyword = other.Keyword;
    }

    /// <summary>
    /// 两份状态的<b>有效条件</b>是否相同：只比较会影响查询的内容（预设生效时自定义日期不参与；解析不了的日期等于没填；
    /// 多选的分类只看选了哪些，不看先后顺序）。筛选 Sheet 点「查看」时用它判断要不要重新查询。
    /// </summary>
    public bool SameConditionsAs(BillListFilter other)
    {
        var (thisStart, thisEnd) = EffectiveCustomRange();
        var (otherStart, otherEnd) = other.EffectiveCustomRange();

        return TimePreset == other.TimePreset
               && thisStart == otherStart
               && thisEnd == otherEnd
               && LedgerId == other.LedgerId
               && UnassignedLedger == other.UnassignedLedger
               && _categoryIds.ToHashSet().SetEquals(other._categoryIds)
               && PaymentChannelId == other.PaymentChannelId
               && Type == other.Type
               && Keyword == other.Keyword;
    }

    private (DateTime? Start, DateTime? End) EffectiveCustomRange() =>
        TimePreset == BillTimePreset.None ? CustomRange : (null, null);

    private static DateTime? ParseDate(string? text) =>
        DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
