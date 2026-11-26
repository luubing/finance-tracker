using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 预算页的展示规则：「日均可用」的计算（月中 / 月末 / 已超支 / 无总预算 / 应用时区的日期边界）、
/// 总览取哪一个总预算、预算行的名称与「已用 / 额度」、分类预算的排序、表单校验
/// </summary>
public class BudgetPagePresentationTests
{
    private static BudgetStatus Total(decimal amount, decimal used, Guid? ledgerId = null, string? ledgerName = null) => new()
    {
        BudgetId = Guid.NewGuid(),
        Amount = amount,
        UsedAmount = used,
        CategoryId = null,
        CategoryName = "总预算",
        CategoryIcon = "mdi-wallet",
        LedgerId = ledgerName is null ? ledgerId : ledgerId ?? Guid.NewGuid(),
        LedgerName = ledgerName,
    };

    private static BudgetStatus Category(string name, decimal amount, decimal used, Guid? ledgerId = null, string? ledgerName = null) => new()
    {
        BudgetId = Guid.NewGuid(),
        Amount = amount,
        UsedAmount = used,
        CategoryId = Guid.NewGuid(),
        CategoryName = name,
        CategoryIcon = "mdi-silverware-fork-knife",
        LedgerId = ledgerName is null ? ledgerId : ledgerId ?? Guid.NewGuid(),
        LedgerName = ledgerName,
    };

    // ------------------------------------------------------------------ 日均可用

    [Fact]
    public void 日均可用_月中_剩余额度除以含今天的剩余天数()
    {
        // 设计稿的数字：2026-11-25，总预算 10,000、已用 8,713.50 → 剩余 1,286.50，还剩 6 天（25、26 … 30）
        var total = Total(10000m, 8713.5m);

        var daily = BudgetPagePresentation.DailyAvailableOf(2026, 11, new DateTime(2026, 11, 25, 10, 30, 0), total);

        Assert.NotNull(daily);
        Assert.Equal(6, daily.DaysLeft);
        Assert.Equal(214.42m, daily.PerDay);
        Assert.Equal("还剩 6 天（含今天），日均可用 ¥214.42", BudgetPagePresentation.DailyAvailableText(daily));
    }

    [Fact]
    public void 日均可用_月末最后一天_剩余一天_日均就是全部剩余额度()
    {
        var daily = BudgetPagePresentation.DailyAvailableOf(2026, 11, new DateTime(2026, 11, 30, 23, 59, 0), Total(3000m, 2500m));

        Assert.NotNull(daily);
        Assert.Equal(1, daily.DaysLeft);
        Assert.Equal(500m, daily.PerDay);
    }

    [Fact]
    public void 日均可用_本月第一天_剩余天数是整月天数()
    {
        var daily = BudgetPagePresentationTests.DailyAt(new DateTime(2026, 11, 1, 0, 0, 0), Total(3000m, 0m));

        Assert.NotNull(daily);
        Assert.Equal(30, daily.DaysLeft);
        Assert.Equal(100m, daily.PerDay);
    }

    [Theory]
    [InlineData(2028, 2, 15, 15)] // 闰年二月 29 天：29 - 15 + 1
    [InlineData(2027, 2, 15, 14)] // 平年二月 28 天：28 - 15 + 1
    [InlineData(2026, 12, 31, 1)]
    [InlineData(2026, 1, 1, 31)]
    public void 日均可用_剩余天数按当月实际天数计算(int year, int month, int day, int expectedDays)
    {
        var daily = BudgetPagePresentation.DailyAvailableOf(year, month, new DateTime(year, month, day, 12, 0, 0), Total(10000m, 0m));

        Assert.NotNull(daily);
        Assert.Equal(expectedDays, daily.DaysLeft);
    }

    [Theory]
    [InlineData(10.05, 29, 5.03)] // 11 月还剩 2 天：10.05 / 2 = 5.025，四舍五入（远离零）到 5.03
    [InlineData(100, 28, 33.33)] // 还剩 3 天：33.333… → 33.33
    [InlineData(200, 28, 66.67)] // 66.666… → 66.67
    [InlineData(0.01, 1, 0.00)] // 剩余一分钱：还剩 30 天，每天不到一分 → ¥0.00（仍然显示，因为额度确实还没用完）
    public void 日均可用_四舍五入到分(double remaining, int day, double expectedPerDay)
    {
        var daily = BudgetPagePresentationTests.DailyAt(new DateTime(2026, 11, day, 9, 0, 0), Total((decimal)remaining, 0m));

        Assert.NotNull(daily);
        Assert.Equal((decimal)expectedPerDay, daily.PerDay);
    }

    [Fact]
    public void 日均可用_已超支不显示()
    {
        Assert.Null(BudgetPagePresentationTests.DailyAt(new DateTime(2026, 11, 25), Total(1000m, 1000.01m)));
    }

    [Fact]
    public void 日均可用_刚好用完不显示()
    {
        Assert.Null(BudgetPagePresentationTests.DailyAt(new DateTime(2026, 11, 25), Total(1000m, 1000m)));
    }

    [Fact]
    public void 日均可用_没有总预算不显示()
    {
        Assert.Null(BudgetPagePresentation.DailyAvailableOf(2026, 11, new DateTime(2026, 11, 25), null));
    }

    [Theory]
    [InlineData(2026, 10)] // 过去的月份
    [InlineData(2026, 12)] // 未来的月份
    [InlineData(2025, 11)] // 去年的同一个月
    [InlineData(2027, 11)] // 明年的同一个月
    public void 日均可用_只在查看当前月份时显示(int year, int month)
    {
        Assert.Null(BudgetPagePresentation.DailyAvailableOf(year, month, new DateTime(2026, 11, 25), Total(1000m, 0m)));
    }

    [Fact]
    public void 日均可用_今天是几号按应用时区算_不按运行机器的系统时区()
    {
        // 同一个 UTC 时刻 2026-11-30 20:00：UTC+8 的应用时区里已经是 12 月 1 日凌晨 4 点
        var instant = new DateTime(2026, 11, 30, 20, 0, 0, DateTimeKind.Utc);
        var total = Total(3100m, 0m);

        using (AppTimeZone.UseZone(TestZones.UtcPlus8))
        {
            var local = AppTimeZone.ToLocal(instant);

            // 查看 11 月：应用时区里 11 月已经结束，不是「当前月份」
            Assert.Null(BudgetPagePresentation.DailyAvailableOf(2026, 11, local, total));

            // 查看 12 月：今天是 12 月 1 日，还剩整月 31 天
            var december = BudgetPagePresentation.DailyAvailableOf(2026, 12, local, total);
            Assert.NotNull(december);
            Assert.Equal(31, december.DaysLeft);
            Assert.Equal(100m, december.PerDay);
        }

        using (AppTimeZone.UseZone(TestZones.UtcMinus5))
        {
            var local = AppTimeZone.ToLocal(instant);

            // UTC-5：同一时刻是 11 月 30 日 15 点，还在 11 月，剩 1 天
            var november = BudgetPagePresentation.DailyAvailableOf(2026, 11, local, total);
            Assert.NotNull(november);
            Assert.Equal(1, november.DaysLeft);
            Assert.Equal(3100m, november.PerDay);
            Assert.Null(BudgetPagePresentation.DailyAvailableOf(2026, 12, local, total));
        }
    }

    private static DailyAvailable? DailyAt(DateTime nowLocal, BudgetStatus total) =>
        BudgetPagePresentation.DailyAvailableOf(nowLocal.Year, nowLocal.Month, nowLocal, total);

    // ------------------------------------------------------------------ 总览取哪一个总预算

    [Fact]
    public void 总览_全部账本视图用全部账本的总预算()
    {
        var global = Total(10000m, 100m);

        var primary = BudgetPagePresentation.PrimaryTotal([global, Category("餐饮美食", 500m, 10m)], ledgerId: null);

        Assert.Same(global, primary);
    }

    [Fact]
    public void 总览_指定账本时优先用该账本自己的总预算()
    {
        var travelId = Guid.NewGuid();
        var global = Total(10000m, 100m);
        var travel = Total(3000m, 100m, travelId, "旅行");

        var primary = BudgetPagePresentation.PrimaryTotal([global, travel], travelId);

        Assert.Same(travel, primary);
    }

    [Fact]
    public void 总览_指定账本但该账本没有总预算时退回全部账本的总预算()
    {
        var global = Total(10000m, 100m);

        var primary = BudgetPagePresentation.PrimaryTotal([global, Category("餐饮美食", 500m, 10m)], Guid.NewGuid());

        Assert.Same(global, primary);
    }

    [Fact]
    public void 总览_没有任何总预算时为空_只有分类预算也不凭空造出总览()
    {
        Assert.Null(BudgetPagePresentation.PrimaryTotal([Category("餐饮美食", 500m, 10m)], ledgerId: null));
        Assert.Null(BudgetPagePresentation.PrimaryTotal([], ledgerId: Guid.NewGuid()));
    }

    // ------------------------------------------------------------------ 环形总览的文字

    [Fact]
    public void 总览模型_接近上限_剩余是绿色的剩余文字_环中心写已使用()
    {
        // 设计稿 screens 09-budgets：总预算 ¥10,000，已用 ¥8,713.50 → 87%、剩余 ¥1,286.50
        var overview = BudgetPagePresentation.ToOverview(Total(10000m, 8713.5m));

        Assert.Equal("总预算", overview.Title);
        Assert.Equal(BudgetState.Warning, overview.State);
        Assert.Equal(87, overview.ShownPercent);
        Assert.Equal("87%", overview.PercentText);
        Assert.Equal(87.1m, overview.RingPercent);
        Assert.Equal("已使用", overview.Caption);
        Assert.Equal(10000m, overview.Amount);
        Assert.Equal(8713.5m, overview.Used);
        Assert.Equal("剩余", overview.RemainLabel);
        Assert.Equal(1286.5m, overview.RemainAmount);
        Assert.Equal("已用 87%，接近上限", overview.AriaValueText);
    }

    [Fact]
    public void 总览模型_超支_标签是超支_金额取正值_环撑满_环中心写已超支()
    {
        var overview = BudgetPagePresentation.ToOverview(Total(2000m, 2360m));

        Assert.Equal(BudgetState.Over, overview.State);
        Assert.Equal(118, overview.ShownPercent);
        Assert.Equal("118%", overview.PercentText);
        Assert.Equal(100m, overview.RingPercent);
        Assert.Equal("已超支", overview.Caption);
        Assert.Equal("超支", overview.RemainLabel);
        Assert.Equal(360m, overview.RemainAmount);
        Assert.Equal("已用 118%，已超支", overview.AriaValueText);
    }

    [Fact]
    public void 总览模型_刚好用完_环中心写已用完_剩余零元()
    {
        var overview = BudgetPagePresentation.ToOverview(Total(500m, 500m));

        Assert.Equal(BudgetState.Over, overview.State);
        Assert.Equal("已用完", overview.Caption);
        Assert.Equal("剩余", overview.RemainLabel);
        Assert.Equal(0m, overview.RemainAmount);
    }

    [Fact]
    public void 总览模型_账本级总预算的标题带账本名()
    {
        Assert.Equal("旅行 · 总预算", BudgetPagePresentation.ToOverview(Total(3000m, 0m, ledgerName: "旅行")).Title);
    }

    [Theory]
    [InlineData(0, "0%")]
    [InlineData(87, "87%")]
    [InlineData(999, "999%")]
    [InlineData(1000, "999%+")]
    [InlineData(100000, "999%+")]
    public void 环中心的百分比文字封顶_位数太多不挤出环的孔(int shown, string expected) =>
        Assert.Equal(expected, BudgetPagePresentation.PercentText(shown));

    // ------------------------------------------------------------------ 预算行

    [Theory]
    [InlineData(2360, "¥2,360")]
    [InlineData(2000, "¥2,000")]
    [InlineData(131.5, "¥131.50")]
    [InlineData(0, "¥0")]
    [InlineData(1234567.89, "¥1,234,567.89")]
    [InlineData(19.999, "¥20")] // 先四舍五入到分，凑成整数就不带小数
    public void 紧凑金额_整数不带小数_否则两位小数(double amount, string expected) =>
        Assert.Equal(expected, BudgetPagePresentation.CompactMoney((decimal)amount));

    [Fact]
    public void 预算行_名称后的灰色小字是已用除以额度()
    {
        var row = BudgetPagePresentation.ToPageRow(Category("餐饮美食", 2000m, 2360m));

        Assert.Equal("餐饮美食", row.Name);
        Assert.Equal("¥2,360 / ¥2,000", row.Tag);
        Assert.Equal(BudgetState.Over, row.State);
        Assert.Equal("超支 ¥360.00", row.RemainText);
    }

    [Fact]
    public void 预算行_名称带账本前缀_全部账本的分类预算不带前缀()
    {
        Assert.Equal("餐饮美食", BudgetPagePresentation.ToPageRow(Category("餐饮美食", 100m, 0m)).Name);
        Assert.Equal("旅行 · 餐饮美食", BudgetPagePresentation.ToPageRow(Category("餐饮美食", 100m, 0m, ledgerName: "旅行")).Name);
        Assert.Equal("旅行 · 总预算", BudgetPagePresentation.ToPageRow(Total(100m, 0m, ledgerName: "旅行")).Name);
        Assert.Equal("全部账本 · 总预算", BudgetPagePresentation.ToPageRow(Total(100m, 0m)).Name);
    }

    [Fact]
    public void 预算行的读屏名称_带上名称与状态文字()
    {
        var row = BudgetPagePresentation.ToPageRow(Category("交通出行", 800m, 486m));

        Assert.Equal("编辑预算：交通出行，剩余 ¥314.00", BudgetPagePresentation.OpenAriaLabel(row));
    }

    // ------------------------------------------------------------------ 整页内容

    [Fact]
    public void 整页_分类预算按已用金额从多到少排_已用相同按分类名()
    {
        var statuses = new[]
        {
            Category("娱乐休闲", 600m, 469m),
            Category("餐饮美食", 2000m, 2360m),
            Category("交通出行", 800m, 486m),
            Category("购物消费", 1500m, 1299m),
            Category("居住生活", 3000m, 0m),
            Category("医疗健康", 300m, 0m),
        };

        var model = BudgetPagePresentation.Build(statuses, null, 2026, 11, new DateTime(2026, 11, 25));

        Assert.Equal(
            new[] { "餐饮美食", "购物消费", "交通出行", "娱乐休闲", "医疗健康", "居住生活" },
            model.Categories.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void 整页_总预算进总览_不重复出现在分类预算里_日均可用来自总览用的总预算()
    {
        var total = Total(10000m, 8713.5m);

        var model = BudgetPagePresentation.Build([total, Category("餐饮美食", 2000m, 2360m)], null, 2026, 11, new DateTime(2026, 11, 25));

        Assert.NotNull(model.Overview);
        Assert.Equal(total.BudgetId, model.Overview.BudgetId);
        Assert.Single(model.Categories);
        Assert.Empty(model.OtherTotals);
        Assert.NotNull(model.Daily);
        Assert.Equal(214.42m, model.Daily.PerDay);
        Assert.False(model.IsEmpty);
    }

    [Fact]
    public void 整页_指定账本时账本总预算进总览_全部账本的总预算归入其他总预算()
    {
        var travelId = Guid.NewGuid();
        var global = Total(10000m, 100m);
        var travel = Total(3000m, 100m, travelId, "旅行");

        var model = BudgetPagePresentation.Build([global, travel], travelId, 2026, 11, new DateTime(2026, 11, 25));

        Assert.Equal(travel.BudgetId, model.Overview!.BudgetId);
        Assert.Equal("旅行 · 总预算", model.Overview.Title);
        var other = Assert.Single(model.OtherTotals);
        Assert.Equal(global.BudgetId, other.BudgetId);
        Assert.Equal("全部账本 · 总预算", other.Name);
        // 日均可用基于总览用的总预算（旅行的 2,900 剩余额度），不是全部账本的
        Assert.Equal(Math.Round(2900m / 6, 2), model.Daily!.PerDay);
    }

    [Fact]
    public void 整页_没有总预算只有分类预算_没有总览也没有日均可用()
    {
        var model = BudgetPagePresentation.Build([Category("餐饮美食", 2000m, 100m)], null, 2026, 11, new DateTime(2026, 11, 25));

        Assert.Null(model.Overview);
        Assert.Null(model.Daily);
        Assert.Single(model.Categories);
        Assert.False(model.IsEmpty);
    }

    [Fact]
    public void 整页_一条预算也没有时为空()
    {
        var model = BudgetPagePresentation.Build([], null, 2026, 11, new DateTime(2026, 11, 25));

        Assert.True(model.IsEmpty);
        Assert.Null(model.Overview);
        Assert.Null(model.Daily);
    }

    [Fact]
    public void 整页_查看非当前月份时不显示日均可用_总览与分类照常()
    {
        var model = BudgetPagePresentation.Build([Total(10000m, 100m)], null, 2026, 10, new DateTime(2026, 11, 25));

        Assert.NotNull(model.Overview);
        Assert.Null(model.Daily);
    }

    // ------------------------------------------------------------------ 表单校验与文案

    [Theory]
    [InlineData(null, false, false, "请输入预算金额")]
    [InlineData(0d, false, false, "请输入预算金额")]
    [InlineData(-5d, false, false, "请输入预算金额")]
    [InlineData(100d, false, false, null)] // 总预算不需要分类
    [InlineData(100d, true, false, "请选择分类")] // 分类预算缺分类
    [InlineData(100d, true, true, null)]
    [InlineData(null, true, false, "请输入预算金额")] // 金额与分类都缺时先提示金额
    public void 表单校验_缺金额先提示金额_分类预算缺分类再提示分类(double? amount, bool isCategory, bool hasCategory, string? expected)
    {
        var result = BudgetFormPresentation.Validate(
            amount is { } a ? (decimal)a : null,
            isCategory,
            hasCategory ? Guid.NewGuid() : null);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void 表单与删除确认的文案_沿用旧页面的提示()
    {
        Assert.Equal("新建预算", BudgetFormPresentation.Title(editing: false));
        Assert.Equal("编辑预算", BudgetFormPresentation.Title(editing: true));
        Assert.Equal("预算创建成功", BudgetFormPresentation.SavedMessage(editing: false));
        Assert.Equal("预算更新成功", BudgetFormPresentation.SavedMessage(editing: true));
        Assert.Equal("预算删除成功", BudgetFormPresentation.DeletedMessage);
        Assert.Equal("预算不存在", BudgetFormPresentation.MissingMessage);
        Assert.Equal("删除预算「餐饮美食」？", BudgetFormPresentation.DeleteTitle("餐饮美食"));
    }

    [Fact]
    public void 表单分段_总预算与分类预算()
    {
        Assert.Equal("total", BudgetFormPresentation.ScopeOf(isCategory: false));
        Assert.Equal("category", BudgetFormPresentation.ScopeOf(isCategory: true));
        Assert.Equal(new[] { "总预算", "分类预算" }, BudgetFormPresentation.Scopes.Select(s => s.Label).ToArray());
    }

    [Fact]
    public void 表单初始值_有Id是编辑_没有Id是新建()
    {
        Assert.True(new BudgetFormTarget(Guid.NewGuid(), 2026, 11, 100m, null, null, false).IsEditing);
        Assert.False(new BudgetFormTarget(null, 2026, 11, null, null, null, false).IsEditing);
    }
}
