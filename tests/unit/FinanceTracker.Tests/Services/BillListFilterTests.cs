using System.Globalization;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 账单 Tab 的筛选状态（筛选 Sheet 与条件条共用）：时间预设（今天 / 本周 / 本月 / 本年，周一为一周之始）的换算、
/// 各维度内部互斥、转成服务层查询条件、副本与「有效条件相同」判断，以及条件条 / Sheet 上的文案
/// </summary>
public class BillListFilterTests
{
    // 2026-11-25（周三）20:00，应用时区的「现在」
    private static readonly DateTime Now = new(2026, 11, 25, 20, 0, 0);

    // Now 所在那天（2026-11-25）的最后一刻
    private static readonly DateTime EndOfToday = new DateTime(2026, 11, 26).AddTicks(-1);

    // ───────────────────────── 时间预设的换算 ─────────────────────────

    [Fact]
    public void 今天_从零点到当天最后一刻_与此刻几点无关()
    {
        var (start, end) = BillTimePresets.RangeOf(BillTimePreset.Today, Now);

        Assert.Equal(new DateTime(2026, 11, 25), start);
        Assert.Equal(EndOfToday, end);

        // 凌晨与深夜的「今天」是同一段
        Assert.Equal((start, end), BillTimePresets.RangeOf(BillTimePreset.Today, new DateTime(2026, 11, 25, 0, 0, 1)));
        Assert.Equal((start, end), BillTimePresets.RangeOf(BillTimePreset.Today, new DateTime(2026, 11, 25, 23, 59, 59)));
    }

    [Fact]
    public void 本周_周一到周日的完整七天_不是到现在为止()
    {
        var (start, end) = BillTimePresets.RangeOf(BillTimePreset.Week, Now);

        Assert.Equal(new DateTime(2026, 11, 23), start); // 周一
        Assert.Equal(DayOfWeek.Monday, start.DayOfWeek);
        Assert.Equal(new DateTime(2026, 11, 30).AddTicks(-1), end); // 周日 11-29 的最后一刻
    }

    [Theory]
    [InlineData("2026-11-23")] // 周一
    [InlineData("2026-11-26")] // 周四
    [InlineData("2026-11-29")] // 周日：仍属于本周，不是下一周的开头
    public void 本周在周一到周日之间的每一天结果相同(string today)
    {
        var (start, end) = BillTimePresets.RangeOf(BillTimePreset.Week, DateTime.Parse(today, CultureInfo.InvariantCulture));

        Assert.Equal(new DateTime(2026, 11, 23), start);
        Assert.Equal(new DateTime(2026, 11, 30).AddTicks(-1), end);
    }

    [Fact]
    public void 本周可以跨月跨年()
    {
        // 2026-12-31 是周四：这一周从 12-28 到 2027-01-03
        var (start, end) = BillTimePresets.RangeOf(BillTimePreset.Week, new DateTime(2026, 12, 31, 9, 0, 0));

        Assert.Equal(new DateTime(2026, 12, 28), start);
        Assert.Equal(new DateTime(2027, 1, 4).AddTicks(-1), end);
    }

    [Fact]
    public void 本月_当月一号到最后一天的最后一刻()
    {
        var (start, end) = BillTimePresets.RangeOf(BillTimePreset.Month, Now);

        Assert.Equal(new DateTime(2026, 11, 1), start);
        Assert.Equal(new DateTime(2026, 12, 1).AddTicks(-1), end);
    }

    [Fact]
    public void 本月_二月按平闰年算()
    {
        var (_, leapEnd) = BillTimePresets.RangeOf(BillTimePreset.Month, new DateTime(2028, 2, 10));
        var (_, commonEnd) = BillTimePresets.RangeOf(BillTimePreset.Month, new DateTime(2026, 2, 10));

        Assert.Equal(new DateTime(2028, 3, 1).AddTicks(-1), leapEnd);
        Assert.Equal(new DateTime(2026, 3, 1).AddTicks(-1), commonEnd);
    }

    [Fact]
    public void 本年_一月一日到十二月三十一日的最后一刻()
    {
        var (start, end) = BillTimePresets.RangeOf(BillTimePreset.Year, Now);

        Assert.Equal(new DateTime(2026, 1, 1), start);
        Assert.Equal(new DateTime(2027, 1, 1).AddTicks(-1), end);
    }

    [Fact]
    public void 没有选预设时不存在对应的起止()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BillTimePresets.RangeOf(BillTimePreset.None, Now));
    }

    [Fact]
    public void 预设依次是今天_本周_本月_本年_文案固定()
    {
        Assert.Equal(
            new[] { BillTimePreset.Today, BillTimePreset.Week, BillTimePreset.Month, BillTimePreset.Year },
            BillTimePresets.All);
        Assert.Equal(new[] { "今天", "本周", "本月", "本年" }, BillTimePresets.All.Select(BillTimePresets.Label));
        Assert.Equal(string.Empty, BillTimePresets.Label(BillTimePreset.None));
    }

    [Fact]
    public void 预设在查询时才换算_页面跨过午夜后今天跟着变()
    {
        var filter = new BillListFilter();
        filter.SelectPreset(BillTimePreset.Today);

        var beforeMidnight = filter.ToQuery(new DateTime(2026, 11, 25, 23, 59, 0));
        var afterMidnight = filter.ToQuery(new DateTime(2026, 11, 26, 0, 1, 0));

        Assert.Equal(new DateTime(2026, 11, 25), beforeMidnight.StartDate);
        Assert.Equal(new DateTime(2026, 11, 26), afterMidnight.StartDate);
    }

    [Fact]
    public void 预设换算用的是应用时区的日期_不是服务器的UTC日期()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);

        // UTC 2026-11-24 17:00 = 东八区 2026-11-25 01:00
        var nowLocal = AppTimeZone.ToLocal(new DateTime(2026, 11, 24, 17, 0, 0, DateTimeKind.Utc));
        var filter = new BillListFilter();
        filter.SelectPreset(BillTimePreset.Today);

        Assert.Equal(new DateTime(2026, 11, 25), filter.ToQuery(nowLocal).StartDate);
    }

    // ───────────────────────── 时间维度：预设与自定义互斥 ─────────────────────────

    [Fact]
    public void 选预设会清掉自定义起止日期_预设算一项条件()
    {
        var filter = new BillListFilter { StartDate = "2026-11-01", EndDate = "2026-11-30" };

        filter.SelectPreset(BillTimePreset.Year);

        Assert.Equal(BillTimePreset.Year, filter.TimePreset);
        Assert.Null(filter.StartDate);
        Assert.Null(filter.EndDate);
        Assert.True(filter.HasTime);
        Assert.False(filter.HasDateRange);
        Assert.Equal(1, filter.PanelCount);
    }

    [Fact]
    public void 设置自定义起止日期会取消预设()
    {
        var filter = new BillListFilter();
        filter.SelectPreset(BillTimePreset.Week);

        filter.SetCustomRange("2026-11-01", "2026-11-10");

        Assert.Equal(BillTimePreset.None, filter.TimePreset);
        Assert.True(filter.HasTime);
        Assert.True(filter.HasDateRange);
        Assert.Equal(1, filter.PanelCount);
    }

    [Fact]
    public void 预设与自定义日期同时存在时预设生效_自定义日期被忽略()
    {
        // 正常流程不会出现，但状态对象可以被直接赋值：以预设为准，查询口径不能含糊
        var filter = new BillListFilter { TimePreset = BillTimePreset.Today, StartDate = "2020-01-01", EndDate = "2020-12-31" };

        var query = filter.ToQuery(Now);

        Assert.Equal(new DateTime(2026, 11, 25), query.StartDate);
        Assert.Equal(EndOfToday, query.EndDate);
    }

    [Fact]
    public void 自定义只填一端时另一端不设限()
    {
        var onlyStart = new BillListFilter();
        onlyStart.SetCustomRange("2026-11-10", null);
        var onlyEnd = new BillListFilter();
        onlyEnd.SetCustomRange(null, "2026-11-10");

        Assert.Equal(new DateTime(2026, 11, 10), onlyStart.ToQuery(Now).StartDate);
        Assert.Null(onlyStart.ToQuery(Now).EndDate);
        Assert.Null(onlyEnd.ToQuery(Now).StartDate);
        Assert.Equal(new DateTime(2026, 11, 11).AddTicks(-1), onlyEnd.ToQuery(Now).EndDate);
        Assert.Equal(1, onlyStart.PanelCount);
        Assert.Equal(1, onlyEnd.PanelCount);
    }

    [Fact]
    public void 清除时间维度同时清掉预设与自定义日期()
    {
        var withPreset = new BillListFilter();
        withPreset.SelectPreset(BillTimePreset.Year);
        var withCustom = new BillListFilter();
        withCustom.SetCustomRange("2026-11-01", "2026-11-10");

        withPreset.Clear(BillFilterDimension.Time);
        withCustom.Clear(BillFilterDimension.Time);

        Assert.False(withPreset.HasTime);
        Assert.False(withCustom.HasTime);
        Assert.True(withPreset.ToQuery(Now).IsEmpty);
        Assert.True(withCustom.ToQuery(Now).IsEmpty);
    }

    // ───────────────────────── 账本维度：不限 / 具体账本 / 未归属账本 ─────────────────────────

    [Fact]
    public void 选具体账本与选未归属账本互斥()
    {
        var filter = new BillListFilter();
        var ledger = Guid.NewGuid();

        filter.SelectLedger(ledger);
        Assert.Equal(ledger, filter.LedgerId);
        Assert.False(filter.UnassignedLedger);
        Assert.True(filter.HasLedger);

        filter.SelectUnassignedLedger();
        Assert.Null(filter.LedgerId);
        Assert.True(filter.UnassignedLedger);
        Assert.True(filter.HasLedger);

        filter.SelectLedger(ledger);
        Assert.Equal(ledger, filter.LedgerId);
        Assert.False(filter.UnassignedLedger);

        // 账本维度无论哪种取值都只算一项条件
        Assert.Equal(1, filter.PanelCount);
    }

    [Fact]
    public void 未归属账本转成查询条件()
    {
        var filter = new BillListFilter();
        filter.SelectUnassignedLedger();

        var query = filter.ToQuery(Now);

        Assert.True(query.UnassignedLedger);
        Assert.Null(query.LedgerId);
        Assert.False(query.IsEmpty);
    }

    [Fact]
    public void 清除账本维度同时取消未归属账本()
    {
        var filter = new BillListFilter();
        filter.SelectUnassignedLedger();

        filter.Clear(BillFilterDimension.Ledger);

        Assert.False(filter.HasLedger);
        Assert.False(filter.UnassignedLedger);
        Assert.True(filter.ToQuery(Now).IsEmpty);
    }

    // ───────────────────────── 维度判断与清除 ─────────────────────────

    [Fact]
    public void 逐个维度判断有没有条件并单独清除()
    {
        var filter = new BillListFilter { Type = BillType.Expense };
        filter.SelectPreset(BillTimePreset.Week);
        filter.SelectLedger(Guid.NewGuid());
        filter.ToggleCategory(Guid.NewGuid());
        filter.PaymentChannelId = Guid.NewGuid();

        foreach (var dimension in Enum.GetValues<BillFilterDimension>())
        {
            Assert.True(filter.Has(dimension), $"{dimension} 应当有条件");
        }

        Assert.Equal(4, filter.PanelCount);

        filter.Clear(BillFilterDimension.Category);
        Assert.False(filter.Has(BillFilterDimension.Category));
        Assert.True(filter.Has(BillFilterDimension.Time));
        Assert.True(filter.Has(BillFilterDimension.Ledger));
        Assert.True(filter.Has(BillFilterDimension.Channel));

        filter.Clear(BillFilterDimension.Channel);
        filter.Clear(BillFilterDimension.Time);
        Assert.Equal(1, filter.PanelCount);

        // 账单类型不属于任何维度：逐个清除不会动它
        Assert.Equal(BillType.Expense, filter.Type);
    }

    [Fact]
    public void 清除面板连预设与未归属账本一起清掉_账单类型与搜索关键字保留()
    {
        var filter = new BillListFilter { Type = BillType.Income, Keyword = "咖啡" };
        filter.SelectPreset(BillTimePreset.Week);
        filter.SelectUnassignedLedger();
        filter.ToggleCategory(Guid.NewGuid());
        filter.PaymentChannelId = Guid.NewGuid();

        filter.ClearPanel();

        Assert.Equal(0, filter.PanelCount);
        Assert.Equal(BillTimePreset.None, filter.TimePreset);
        Assert.False(filter.UnassignedLedger);
        Assert.Equal(BillType.Income, filter.Type);
        Assert.Equal("咖啡", filter.Keyword);

        filter.Clear();
        Assert.False(filter.HasAny);
        Assert.Null(filter.Keyword);
    }

    // ───────────────────────── 搜索关键字（10 号） ─────────────────────────

    [Theory]
    [InlineData("咖啡", "咖啡")]
    [InlineData("  咖啡 ", "咖啡")]
    [InlineData("\t午餐\n", "午餐")]
    [InlineData("中间 有空格", "中间 有空格")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void 关键字赋值时去掉首尾空白_空白等于没有关键字(string? input, string? expected)
    {
        var filter = new BillListFilter { Keyword = input };

        Assert.Equal(expected, filter.Keyword);
        Assert.Equal(expected is not null, filter.HasKeyword);
    }

    [Fact]
    public void 关键字不是筛选维度_不计入角标_条件条也没有它的标签()
    {
        var filter = new BillListFilter { Keyword = "咖啡" };

        Assert.True(filter.HasKeyword);
        Assert.Equal(0, filter.PanelCount);
        Assert.False(filter.HasConditions);
        foreach (var dimension in Enum.GetValues<BillFilterDimension>())
        {
            Assert.False(filter.Has(dimension));
        }

        // 但它是一个条件：空状态据此说「没有找到」而不是「还没有账单」
        Assert.True(filter.HasAny);
    }

    [Fact]
    public void 有条件_区分只有筛选条件_只有关键字_两者都有()
    {
        Assert.False(new BillListFilter().HasAny);

        var typeOnly = new BillListFilter { Type = BillType.Expense };
        Assert.True(typeOnly.HasConditions);
        Assert.False(typeOnly.HasKeyword);
        Assert.True(typeOnly.HasAny);

        var both = new BillListFilter { Type = BillType.Expense, Keyword = "咖啡" };
        Assert.True(both.HasConditions);
        Assert.True(both.HasKeyword);
        Assert.True(both.HasAny);
    }

    [Fact]
    public void 关键字转成查询条件_与其它条件一起带上()
    {
        var category = Guid.NewGuid();
        var filter = new BillListFilter { Keyword = " 咖啡 ", Type = BillType.Expense };
        filter.ToggleCategory(category);
        filter.SelectPreset(BillTimePreset.Month);

        var query = filter.ToQuery(Now);

        Assert.Equal("咖啡", query.Keyword);
        Assert.Equal(new[] { category }, query.CategoryIds);
        Assert.Equal(BillType.Expense, query.Type);
        Assert.Equal(new DateTime(2026, 11, 1), query.StartDate);
        Assert.False(query.IsEmpty);

        Assert.Null(new BillListFilter().ToQuery(Now).Keyword);
    }

    [Fact]
    public void 只有关键字也不是空查询()
    {
        Assert.False(new BillListFilter { Keyword = "12.5" }.ToQuery(Now).IsEmpty);
        Assert.True(new BillListFilter { Keyword = "   " }.ToQuery(Now).IsEmpty);
    }

    // ───────────────────────── 查询条件 ─────────────────────────

    [Fact]
    public void 预设与其它条件一起转成查询条件()
    {
        var category = Guid.NewGuid();
        var channel = Guid.NewGuid();
        var ledger = Guid.NewGuid();
        var filter = new BillListFilter { PaymentChannelId = channel, Type = BillType.Expense };
        filter.ToggleCategory(category);
        filter.SelectPreset(BillTimePreset.Month);
        filter.SelectLedger(ledger);

        var query = filter.ToQuery(Now);

        Assert.Equal(new DateTime(2026, 11, 1), query.StartDate);
        Assert.Equal(new DateTime(2026, 12, 1).AddTicks(-1), query.EndDate);
        Assert.Equal(ledger, query.LedgerId);
        Assert.False(query.UnassignedLedger);
        Assert.Equal(new[] { category }, query.CategoryIds);
        Assert.Equal(channel, query.PaymentChannelId);
        Assert.Equal(BillType.Expense, query.Type);
    }

    [Fact]
    public void 不带参数的查询转换以此刻的应用时区日期换算预设()
    {
        var filter = new BillListFilter();
        filter.SelectPreset(BillTimePreset.Today);

        var before = AppTimeZone.ToLocal(DateTime.UtcNow).Date;
        var query = filter.ToQuery();
        var after = AppTimeZone.ToLocal(DateTime.UtcNow).Date;

        // 恰好跨过午夜时两侧日期可能不同：结果必须落在这两天之一
        Assert.Contains(query.StartDate!.Value, new[] { before, after });
    }

    // ───────────────────────── 副本与比较 ─────────────────────────

    [Fact]
    public void 副本与原件互不影响()
    {
        var original = new BillListFilter { Type = BillType.Expense, Keyword = "咖啡" };
        original.SelectPreset(BillTimePreset.Week);
        original.SelectUnassignedLedger();
        original.ToggleCategory(Guid.NewGuid());

        var copy = original.Clone();
        Assert.True(original.SameConditionsAs(copy));
        Assert.Equal("咖啡", copy.Keyword);

        copy.SelectPreset(BillTimePreset.Year);
        copy.SelectLedger(Guid.NewGuid());
        copy.Clear(BillFilterDimension.Category);
        copy.Keyword = "午餐";

        Assert.Equal(BillTimePreset.Week, original.TimePreset);
        Assert.True(original.UnassignedLedger);
        Assert.True(original.HasCategory);
        Assert.Single(original.CategoryIds);
        Assert.Equal("咖啡", original.Keyword);
        Assert.False(original.SameConditionsAs(copy));
    }

    [Fact]
    public void 覆盖自己时整份状态都被替换()
    {
        var target = new BillListFilter();
        target.SetCustomRange("2026-01-01", "2026-01-31");
        target.PaymentChannelId = Guid.NewGuid();

        var source = new BillListFilter { Type = BillType.Income, Keyword = "咖啡" };
        source.SelectPreset(BillTimePreset.Today);
        source.SelectUnassignedLedger();

        target.Keyword = "旧的关键字";
        target.CopyFrom(source);

        Assert.True(target.SameConditionsAs(source));
        Assert.Null(target.StartDate);
        Assert.Null(target.PaymentChannelId);
        Assert.Equal(BillType.Income, target.Type);
        Assert.Equal("咖啡", target.Keyword);

        // 来源没有关键字时，目标的关键字也被清掉（整份状态被替换，不是合并）
        target.CopyFrom(new BillListFilter());
        Assert.Null(target.Keyword);
    }

    [Fact]
    public void 有效条件相同_关键字首尾空白不影响比较()
    {
        var a = new BillListFilter { Keyword = "咖啡" };
        var b = new BillListFilter { Keyword = "  咖啡  " };
        var empty = new BillListFilter { Keyword = "   " };

        Assert.True(a.SameConditionsAs(b));
        Assert.True(empty.SameConditionsAs(new BillListFilter()));
    }

    [Fact]
    public void 有效条件相同_预设生效时自定义日期不参与比较()
    {
        var a = new BillListFilter { TimePreset = BillTimePreset.Month, StartDate = "2026-01-01" };
        var b = new BillListFilter { TimePreset = BillTimePreset.Month, StartDate = "2025-05-05" };

        Assert.True(a.SameConditionsAs(b));
    }

    [Fact]
    public void 有效条件相同_解析不了的日期等于没填()
    {
        var a = new BillListFilter { StartDate = "2026-1" };
        var b = new BillListFilter();

        Assert.True(a.SameConditionsAs(b));
    }

    [Theory]
    [InlineData("preset")]
    [InlineData("custom")]
    [InlineData("ledger")]
    [InlineData("unassigned")]
    [InlineData("category")]
    [InlineData("channel")]
    [InlineData("type")]
    [InlineData("keyword")]
    public void 有效条件不同_任一条件不一样就不相同(string changed)
    {
        var a = new BillListFilter();
        var b = new BillListFilter();

        switch (changed)
        {
            case "preset": b.SelectPreset(BillTimePreset.Today); break;
            case "custom": b.SetCustomRange("2026-11-01", null); break;
            case "ledger": b.SelectLedger(Guid.NewGuid()); break;
            case "unassigned": b.SelectUnassignedLedger(); break;
            case "category": b.ToggleCategory(Guid.NewGuid()); break;
            case "channel": b.PaymentChannelId = Guid.NewGuid(); break;
            case "type": b.Type = BillType.Income; break;
            case "keyword": b.Keyword = "咖啡"; break;
        }

        Assert.False(a.SameConditionsAs(b));
        Assert.False(b.SameConditionsAs(a));
    }

    // ───────────────────────── 默认视图：本月不算条件 ─────────────────────────

    [Fact]
    public void 默认的本月不算筛选条件_不计角标_条件条上没有已设置的标签()
    {
        var filter = new BillListFilter();
        filter.ResetPanel();

        Assert.Equal(BillTimePreset.Month, filter.TimePreset);
        Assert.True(filter.IsDefaultTime);
        Assert.True(filter.HasTime); // 查询是受时间限制的
        Assert.False(filter.HasTimeCondition); // 但它不是「条件」
        Assert.Equal(0, filter.PanelCount);
        Assert.False(filter.Has(BillFilterDimension.Time));
        Assert.False(filter.HasConditions);
        Assert.False(filter.HasAny);
        Assert.True(filter.IsDefaultPanel);

        // 查询里照常带着本月的起止
        var query = filter.ToQuery(Now);
        Assert.Equal(new DateTime(2026, 11, 1), query.StartDate);
        Assert.Equal(new DateTime(2026, 12, 1).AddTicks(-1), query.EndDate);
    }

    [Theory]
    [InlineData(BillTimePreset.Today)]
    [InlineData(BillTimePreset.Week)]
    [InlineData(BillTimePreset.Year)]
    public void 本月以外的预设算一项条件_偏离了默认视图(BillTimePreset preset)
    {
        var filter = new BillListFilter();
        filter.ResetPanel();
        filter.SelectPreset(preset);

        Assert.False(filter.IsDefaultTime);
        Assert.True(filter.HasTimeCondition);
        Assert.True(filter.Has(BillFilterDimension.Time));
        Assert.Equal(1, filter.PanelCount);
        Assert.True(filter.HasConditions);
        Assert.False(filter.IsDefaultPanel);
    }

    [Fact]
    public void 自定义起止日期即使恰好是整个本月也算条件_默认的只是本月这个预设()
    {
        var filter = new BillListFilter();
        filter.SetCustomRange("2026-11-01", "2026-11-30");

        Assert.False(filter.IsDefaultTime);
        Assert.True(filter.HasTimeCondition);
        Assert.Equal(1, filter.PanelCount);
        Assert.False(filter.IsDefaultPanel);
    }

    [Fact]
    public void 不限时间不是条件_但也不是默认视图_重置后回到本月()
    {
        var filter = new BillListFilter(); // 什么都没有 = 不限时间

        Assert.False(filter.HasTime);
        Assert.False(filter.IsDefaultTime);
        Assert.False(filter.HasTimeCondition);
        Assert.Equal(0, filter.PanelCount);
        Assert.False(filter.IsDefaultPanel); // 与默认视图不同：「重置」应当可点

        filter.ResetPanel();

        Assert.True(filter.IsDefaultPanel);
        Assert.Equal(BillTimePreset.Month, filter.TimePreset);
    }

    [Fact]
    public void 重置面板回到默认视图_账单类型与搜索关键字保留_全部重置则一并清掉()
    {
        var filter = new BillListFilter { Type = BillType.Income, Keyword = "咖啡" };
        filter.SelectPreset(BillTimePreset.Year);
        filter.SelectLedger(Guid.NewGuid());
        filter.ToggleCategory(Guid.NewGuid());
        filter.PaymentChannelId = Guid.NewGuid();

        filter.ResetPanel();

        Assert.True(filter.IsDefaultPanel);
        Assert.Equal(BillTimePreset.Month, filter.TimePreset);
        Assert.False(filter.HasLedger);
        Assert.False(filter.HasCategory);
        Assert.Null(filter.PaymentChannelId);
        Assert.Equal(BillType.Income, filter.Type);
        Assert.Equal("咖啡", filter.Keyword);

        filter.ResetAll();

        Assert.True(filter.IsDefaultPanel);
        Assert.Null(filter.Type);
        Assert.Null(filter.Keyword);
        Assert.False(filter.HasAny);
    }

    [Fact]
    public void 去掉条件时默认的本月与不限时间都不动_偏离默认的时间条件去掉后是不限时间()
    {
        // 默认视图 + 分类 + 类型 + 关键字：全部去掉后，时间还是默认的本月（「清除搜索」不该顺手把时间也放开）
        var inMonth = new BillListFilter { Type = BillType.Expense, Keyword = "咖啡" };
        inMonth.ResetPanel();
        inMonth.ToggleCategory(Guid.NewGuid());

        inMonth.ClearConditions();

        Assert.Equal(BillTimePreset.Month, inMonth.TimePreset);
        Assert.False(inMonth.HasCategory);
        Assert.Null(inMonth.Type);
        Assert.Null(inMonth.Keyword);
        Assert.True(inMonth.IsDefaultPanel);

        // 不限时间时同样不动（不会被拉回本月）
        var allTime = new BillListFilter { Keyword = "咖啡" };
        allTime.ClearConditions();
        Assert.Equal(BillTimePreset.None, allTime.TimePreset);
        Assert.Null(allTime.Keyword);

        // 偏离默认的时间条件是条件，去掉后不限时间
        var year = new BillListFilter();
        year.SelectPreset(BillTimePreset.Year);
        year.ClearConditions();
        Assert.Equal(BillTimePreset.None, year.TimePreset);

        var custom = new BillListFilter();
        custom.SetCustomRange("2026-01-01", "2026-01-31");
        custom.ClearConditions();
        Assert.False(custom.HasTime);
    }

    // ───────────────────────── 分类多选 ─────────────────────────

    [Fact]
    public void 分类可多选_点一次选中_再点取消_全部取消等于不限分类()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var filter = new BillListFilter();

        filter.ToggleCategory(a);
        filter.ToggleCategory(b);

        Assert.Equal(new[] { a, b }, filter.CategoryIds); // 按选中的先后
        Assert.True(filter.HasCategory);
        Assert.True(filter.Has(BillFilterDimension.Category));
        Assert.Equal(1, filter.PanelCount); // 选几个都只算一项

        filter.ToggleCategory(a);
        Assert.Equal(new[] { b }, filter.CategoryIds);

        filter.ToggleCategory(b);
        Assert.False(filter.HasCategory);
        Assert.Empty(filter.CategoryIds);
        Assert.True(filter.ToQuery(Now).IsEmpty);
    }

    [Fact]
    public void 多选的分类转成查询条件时复制一份_之后再改不影响已经发出的查询()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var filter = new BillListFilter();
        filter.ToggleCategory(a);
        filter.ToggleCategory(b);

        var query = filter.ToQuery(Now);
        filter.ToggleCategory(Guid.NewGuid());

        Assert.Equal(new[] { a, b }, query.CategoryIds);
        Assert.False(query.IsEmpty);
    }

    [Fact]
    public void 有效条件相同_多选的分类只看选了哪些_不看先后顺序()
    {
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();
        var xy = new BillListFilter();
        xy.ToggleCategory(x);
        xy.ToggleCategory(y);
        var yx = new BillListFilter();
        yx.ToggleCategory(y);
        yx.ToggleCategory(x);
        var onlyX = new BillListFilter();
        onlyX.ToggleCategory(x);

        Assert.True(xy.SameConditionsAs(yx));
        Assert.False(xy.SameConditionsAs(onlyX));
        Assert.False(onlyX.SameConditionsAs(xy));
        Assert.False(xy.SameConditionsAs(new BillListFilter()));
    }

    [Fact]
    public void 副本里的多选分类与原件互不影响()
    {
        var a = Guid.NewGuid();
        var original = new BillListFilter();
        original.ToggleCategory(a);

        var copy = original.Clone();
        copy.ToggleCategory(Guid.NewGuid());

        Assert.Equal(new[] { a }, original.CategoryIds);
        Assert.Equal(2, copy.CategoryIds.Count);

        // 覆盖时是替换而不是合并
        copy.CopyFrom(original);
        Assert.Equal(new[] { a }, copy.CategoryIds);
        copy.CopyFrom(copy); // 自己覆盖自己不能把自己清空
        Assert.Equal(new[] { a }, copy.CategoryIds);
    }

    [Fact]
    public void 只保留仍然存在的分类_被删除的从多选条件里去掉_其余不动()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var filter = new BillListFilter();
        filter.ToggleCategory(a);
        filter.ToggleCategory(b);
        filter.ToggleCategory(c);

        Assert.True(filter.RetainCategories(new[] { a, c, Guid.NewGuid() }));
        Assert.Equal(new[] { a, c }, filter.CategoryIds);

        Assert.False(filter.RetainCategories(new[] { a, c })); // 没有要去掉的

        Assert.True(filter.RetainCategories(Array.Empty<Guid>()));
        Assert.False(filter.HasCategory);
    }

    [Fact]
    public void 分类条件文案_一个是名字_多个是第一个等N项_查不到名字时兜底()
    {
        var food = Guid.NewGuid();
        var traffic = Guid.NewGuid();
        var options = new[]
        {
            new BillFilterOption(food, "餐饮美食", "mdi-silverware-fork-knife", "orange", BillType.Expense),
            new BillFilterOption(traffic, "交通出行", "mdi-bus", "blue", BillType.Expense),
        };

        Assert.Equal(string.Empty, BillListPresentation.CategoryText(Array.Empty<Guid>(), options));
        Assert.Equal("餐饮美食", BillListPresentation.CategoryText(new[] { food }, options));
        Assert.Equal("餐饮美食等 2 项", BillListPresentation.CategoryText(new[] { food, traffic }, options));
        Assert.Equal("交通出行等 3 项", BillListPresentation.CategoryText(new[] { Guid.NewGuid(), traffic, food }, options)); // 第一个查不到名字时取能查到的第一个

        // 一个名字都查不到（选项列表还没读到）：退回通用说法
        Assert.Equal("指定分类", BillListPresentation.CategoryText(new[] { Guid.NewGuid() }, options));
        Assert.Equal("指定分类等 2 项", BillListPresentation.CategoryText(new[] { Guid.NewGuid(), Guid.NewGuid() }, Array.Empty<BillFilterOption>()));
    }

    // ───────────────────────── 文案 ─────────────────────────

    [Fact]
    public void 时间条件文案_预设是名称_自定义是起止日期()
    {
        var preset = new BillListFilter();
        preset.SelectPreset(BillTimePreset.Week);

        Assert.Equal("本周", BillListPresentation.TimeText(preset, Now));

        var custom = new BillListFilter();
        custom.SetCustomRange("2026-11-01", "2026-11-25");

        Assert.Equal("11月1日至11月25日", BillListPresentation.TimeText(custom, Now));
    }

    [Theory]
    [InlineData("2026-11-01", "2026-11-25", "11月1日至11月25日")]
    [InlineData("2026-11-01", "2026-11-01", "11月1日")]
    [InlineData("2026-11-01", null, "11月1日起")]
    [InlineData(null, "2026-11-25", "至11月25日")]
    [InlineData("2025-12-01", "2026-01-05", "2025年12月1日至1月5日")]
    [InlineData("2025-12-01", "2025-12-31", "2025年12月1日至2025年12月31日")]
    [InlineData(null, null, "")]
    public void 自定义范围文案_同年省略年份_同一天只写一次_只填一端写起或至(string? start, string? end, string expected)
    {
        var startDay = start is null ? (DateTime?)null : DateTime.Parse(start, CultureInfo.InvariantCulture);
        var endDay = end is null ? (DateTime?)null : DateTime.Parse(end, CultureInfo.InvariantCulture);

        Assert.Equal(expected, BillListPresentation.CustomRangeText(startDay, endDay, Now));
    }

    [Fact]
    public void 账本条件文案_未归属账本_账本名_共享账本带标记_找不到名字时兜底()
    {
        var own = new LedgerModel { Id = Guid.NewGuid(), Name = "日常开销" };
        var shared = new LedgerModel { Id = Guid.NewGuid(), Name = "家庭", IsShared = true };
        var ledgers = new[] { own, shared };

        var unassigned = new BillListFilter();
        unassigned.SelectUnassignedLedger();
        Assert.Equal("未归属账本", BillListPresentation.LedgerText(unassigned, ledgers));

        var ownFilter = new BillListFilter();
        ownFilter.SelectLedger(own.Id);
        Assert.Equal("日常开销", BillListPresentation.LedgerText(ownFilter, ledgers));

        var sharedFilter = new BillListFilter();
        sharedFilter.SelectLedger(shared.Id);
        Assert.Equal("家庭（共享）", BillListPresentation.LedgerText(sharedFilter, ledgers));

        var missing = new BillListFilter();
        missing.SelectLedger(Guid.NewGuid());
        Assert.Equal("指定账本", BillListPresentation.LedgerText(missing, ledgers));

        Assert.Equal(string.Empty, BillListPresentation.LedgerText(new BillListFilter(), ledgers));
    }

    [Theory]
    [InlineData(BillFilterDimension.Time, "时间")]
    [InlineData(BillFilterDimension.Ledger, "账本")]
    [InlineData(BillFilterDimension.Category, "分类")]
    [InlineData(BillFilterDimension.Channel, "支付渠道")]
    public void 维度名称与兜底文案(BillFilterDimension dimension, string name)
    {
        Assert.Equal(name, BillListPresentation.DimensionName(dimension));
        Assert.Equal($"指定{name}", BillListPresentation.UnknownConditionText(dimension));
    }

    [Fact]
    public void 条件标签的读屏名称_说清楚是哪一项_现在是什么_点了会怎样()
    {
        Assert.Equal("分类，点按设置", BillListPresentation.ChipLabel(BillFilterDimension.Category, null));
        Assert.Equal("分类，点按设置", BillListPresentation.ChipLabel(BillFilterDimension.Category, ""));
        Assert.Equal("时间：本月，点按调整", BillListPresentation.ChipLabel(BillFilterDimension.Time, "本月"));
        Assert.Equal("移除时间条件：本月", BillListPresentation.ChipRemoveLabel(BillFilterDimension.Time, "本月"));
    }

    [Theory]
    [InlineData(null, "查看账单")]
    [InlineData(0, "查看 0 笔账单")] // 设计稿 13：按钮始终是「查看 N 笔账单」，0 笔也不改文案、不置灰
    [InlineData(1, "查看 1 笔账单")]
    [InlineData(38, "查看 38 笔账单")]
    [InlineData(1234, "查看 1,234 笔账单")]
    public void 筛选Sheet底部按钮文案按笔数变化(int? count, string expected) =>
        Assert.Equal(expected, BillListPresentation.ConfirmText(count));

    [Fact]
    public void 批量归属账本的文案点明笔数与账本()
    {
        Assert.Equal("将已选的 3 笔账单归属到：", BillListPresentation.AssignHint(3));
        Assert.Equal("归属 3 笔账单", BillListPresentation.AssignConfirmText(3));
        Assert.Equal("已将 3 笔账单归属到「日常开销」", BillListPresentation.AssignedText(3, "日常开销"));
        Assert.Contains("已有账本的账单", BillListPresentation.AssignNote);
        Assert.Contains("账本管理", BillListPresentation.AssignNoLedgerText);
    }

    // ───────────────────────── 筛选选项 ─────────────────────────

    [Fact]
    public void 分类选项带图标配色与类型_支付渠道选项用中性灰()
    {
        var category = new Category { Id = Guid.NewGuid(), Name = "餐饮美食", Icon = "mdi-silverware-fork-knife", Type = BillType.Expense };
        var channel = new PaymentChannel { Id = Guid.NewGuid(), Name = "微信支付", Icon = "mdi-wechat" };

        var categoryOption = BillFilterOption.FromCategory(category);
        var channelOption = BillFilterOption.FromChannel(channel);

        Assert.Equal(category.Id, categoryOption.Id);
        Assert.Equal("餐饮美食", categoryOption.Name);
        Assert.Equal(BillType.Expense, categoryOption.Type);
        Assert.StartsWith("mdi-", categoryOption.Icon);
        Assert.False(string.IsNullOrWhiteSpace(categoryOption.Tone));

        Assert.Equal(channel.Id, channelOption.Id);
        Assert.Equal("gray", channelOption.Tone);
        Assert.Null(channelOption.Type);
    }

    [Fact]
    public void 支付渠道图标不合法时回退到默认图标()
    {
        var channel = new PaymentChannel { Id = Guid.NewGuid(), Name = "自定义渠道", Icon = "<script>" };

        Assert.Equal(BillFilterOption.DefaultChannelIcon, BillFilterOption.FromChannel(channel).Icon);
    }
}
