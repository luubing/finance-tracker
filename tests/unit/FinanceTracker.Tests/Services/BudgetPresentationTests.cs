using FinanceTracker.Core.Interfaces;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 预算展示规则（状态判定、文字说明、行模型）：规则集中在 <see cref="BudgetPresentation"/>，
/// 阈值与既有预警口径一致（使用率 ≥ 80% 预警、≥ 100% 超支）
/// </summary>
public class BudgetPresentationTests
{
    private static BudgetStatus Total(decimal amount, decimal used, string? ledgerName = null) => new()
    {
        BudgetId = Guid.NewGuid(),
        Amount = amount,
        UsedAmount = used,
        CategoryId = null,
        CategoryName = "总预算",
        LedgerId = ledgerName is null ? null : Guid.NewGuid(),
        LedgerName = ledgerName,
    };

    private static BudgetStatus Category(string name, decimal amount, decimal used, string icon = "mdi-silverware-fork-knife", string? ledgerName = null) => new()
    {
        BudgetId = Guid.NewGuid(),
        Amount = amount,
        UsedAmount = used,
        CategoryId = Guid.NewGuid(),
        CategoryName = name,
        CategoryIcon = icon,
        LedgerId = ledgerName is null ? null : Guid.NewGuid(),
        LedgerName = ledgerName,
    };

    [Theory]
    [InlineData(0, BudgetState.Normal)]
    [InlineData(50, BudgetState.Normal)]
    [InlineData(79.99, BudgetState.Normal)]
    [InlineData(80, BudgetState.Warning)]
    [InlineData(99.99, BudgetState.Warning)]
    [InlineData(100, BudgetState.Over)]
    [InlineData(250, BudgetState.Over)]
    public void 使用率80以上预警_100以上超支(double usage, BudgetState expected) =>
        Assert.Equal(expected, BudgetPresentation.StateOf((decimal)usage));

    [Theory]
    [InlineData(79.6, 79)]
    [InlineData(99.6, 99)]
    [InlineData(100, 100)]
    [InlineData(124.9, 124)]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    public void 显示百分比向下取整_显示值与状态严格对应(double usage, int expected) =>
        Assert.Equal(expected, BudgetPresentation.ShownPercent((decimal)usage));

    [Fact]
    public void 显示百分比有上限_极端数据不会撑出超长文字()
    {
        Assert.Equal(100_000, BudgetPresentation.ShownPercent(5_000_000m));
    }

    [Fact]
    public void 显示值与状态一致_79点6显示79且仍是正常_99点6显示99且尚未超支()
    {
        Assert.Equal(BudgetState.Normal, BudgetPresentation.StateOf(79.6m));
        Assert.Equal(79, BudgetPresentation.ShownPercent(79.6m));
        Assert.Equal(BudgetState.Warning, BudgetPresentation.StateOf(99.6m));
        Assert.Equal(99, BudgetPresentation.ShownPercent(99.6m));
    }

    [Theory]
    [InlineData(87.34, 87.3)]
    [InlineData(99.99, 99.9)]
    [InlineData(100, 100)]
    [InlineData(120, 100)]
    [InlineData(-3, 0)]
    [InlineData(0, 0)]
    public void 进度条宽度限制在0到100_保留一位小数且不进位(double usage, double expected) =>
        Assert.Equal((decimal)expected, BudgetPresentation.BarPercent((decimal)usage));

    [Theory]
    [InlineData(BudgetState.Normal, 1286.5, "剩余 ¥1,286.50")]
    [InlineData(BudgetState.Normal, 314, "剩余 ¥314.00")]
    [InlineData(BudgetState.Warning, 120, "仅剩 ¥120.00")]
    [InlineData(BudgetState.Over, -360, "超支 ¥360.00")]
    [InlineData(BudgetState.Over, 0, "已用完")]
    [InlineData(BudgetState.Warning, 0, "已用完")]
    public void 右上角文字随状态变化_颜色之外还有文字线索(BudgetState state, double remaining, string expected) =>
        Assert.Equal(expected, BudgetPresentation.RemainText(state, (decimal)remaining));

    [Theory]
    [InlineData(BudgetState.Normal, 60, "已用 60%")]
    [InlineData(BudgetState.Warning, 87, "已用 87%，接近上限")]
    [InlineData(BudgetState.Over, 120, "已用 120%，已超支")]
    public void 进度条读屏文案带上状态(BudgetState state, int percent, string expected) =>
        Assert.Equal(expected, BudgetPresentation.AriaValueText(state, percent));

    [Fact]
    public void 名称后的灰色小字_全部账本总预算标全部账本_账本级预算标账本名_分类预算没有()
    {
        Assert.Equal("全部账本", BudgetPresentation.TagOf(Total(5000, 100)));
        Assert.Equal("旅行", BudgetPresentation.TagOf(Total(5000, 100, ledgerName: "旅行")));
        Assert.Equal("旅行", BudgetPresentation.TagOf(Category("餐饮美食", 1000, 100, ledgerName: "旅行")));
        Assert.Null(BudgetPresentation.TagOf(Category("餐饮美食", 1000, 100)));
    }

    [Fact]
    public void 总预算转成行_紫色钱包图标_剩余文字与正常状态()
    {
        var status = Total(5000m, 3713.5m);

        var row = BudgetPresentation.ToRow(status);

        Assert.Equal(status.BudgetId, row.BudgetId);
        Assert.Equal("总预算", row.Name);
        Assert.Equal("全部账本", row.Tag);
        Assert.Equal("mdi-wallet", row.Icon);
        Assert.Equal("purple", row.Tone);
        Assert.Equal(BudgetState.Normal, row.State);
        Assert.Equal(74, row.ShownPercent);
        Assert.Equal("剩余 ¥1,286.50", row.RemainText);
        Assert.Equal("已用 74%", row.AriaValueText);
    }

    [Fact]
    public void 超支的分类预算转成行_红色状态_撑满进度条_超支文字()
    {
        var status = Category("餐饮美食", 1500m, 1860m);

        var row = BudgetPresentation.ToRow(status);

        Assert.Equal("餐饮美食", row.Name);
        Assert.Null(row.Tag);
        Assert.Equal("mdi-silverware-fork-knife", row.Icon);
        Assert.Equal("orange", row.Tone);
        Assert.Equal(BudgetState.Over, row.State);
        Assert.Equal(124, row.ShownPercent);
        Assert.Equal(100m, row.BarPercent);
        Assert.Equal("超支 ¥360.00", row.RemainText);
        Assert.Equal("已用 124%，已超支", row.AriaValueText);
    }

    [Fact]
    public void 接近上限的预算转成行_橙色状态_仅剩文字()
    {
        var row = BudgetPresentation.ToRow(Category("交通出行", 800m, 700m, "mdi-bus"));

        Assert.Equal(BudgetState.Warning, row.State);
        Assert.Equal(87, row.ShownPercent);
        Assert.Equal("仅剩 ¥100.00", row.RemainText);
        Assert.Equal("已用 87%，接近上限", row.AriaValueText);
        Assert.Equal("blue", row.Tone);
    }

    [Fact]
    public void 分类图标缺失或非法时回退通用图标_总预算名称缺失时叫总预算()
    {
        var category = BudgetPresentation.ToRow(Category("宠物", 500m, 100m, icon: string.Empty));
        Assert.Equal("mdi-tag", category.Icon);

        var total = Total(100m, 10m);
        total.CategoryName = "  ";
        Assert.Equal("总预算", BudgetPresentation.ToRow(total).Name);
    }

    [Fact]
    public void 状态键供样式与测试读取()
    {
        Assert.Equal("normal", BudgetPresentation.StateKey(BudgetState.Normal));
        Assert.Equal("warning", BudgetPresentation.StateKey(BudgetState.Warning));
        Assert.Equal("over", BudgetPresentation.StateKey(BudgetState.Over));
    }

    [Fact]
    public void 预算金额为零时使用率为零_不会除零也不会显示超支()
    {
        var row = BudgetPresentation.ToRow(Total(0m, 0m));

        Assert.Equal(BudgetState.Normal, row.State);
        Assert.Equal(0, row.ShownPercent);
        Assert.Equal("已用完", row.RemainText);
    }
}
