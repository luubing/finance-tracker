using FinanceTracker.Core.Enums;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 分类 / 支付渠道管理页的展示规则（名称校验、账单数文案、图标候选、分段、默认菜单文案）：
/// 规则集中在 <see cref="ManagedItemPresentation"/> / <see cref="CategoryPresentation"/> / <see cref="PaymentChannelPresentation"/>，页面只负责取数与交互
/// </summary>
public class ManagedItemPresentationTests
{
    // ------------------------------------------------------------------ 共用规则

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void 名称为空_提示请输入_带上条目的叫法(string? name)
    {
        Assert.Equal("请输入分类名称", ManagedItemPresentation.ValidateName(name, "分类"));
        Assert.Equal("请输入支付渠道名称", ManagedItemPresentation.ValidateName(name, "支付渠道"));
    }

    [Fact]
    public void 名称超过上限_提示最多多少个字_恰好上限和首尾空白都放行()
    {
        Assert.Equal(50, ManagedItemPresentation.NameMaxLength);
        Assert.Null(ManagedItemPresentation.ValidateName(new string('宠', 50), "分类"));
        Assert.Null(ManagedItemPresentation.ValidateName($"  {new string('宠', 50)}  ", "分类"));
        Assert.Equal("分类名称最多 50 个字", ManagedItemPresentation.ValidateName(new string('宠', 51), "分类"));
        Assert.Equal("支付渠道名称最多 50 个字", ManagedItemPresentation.ValidateName(new string('卡', 51), "支付渠道"));
    }

    [Theory]
    [InlineData(0, "0 笔")]
    [InlineData(1, "1 笔")]
    [InlineData(6, "6 笔")]
    [InlineData(12345, "12345 笔")]
    [InlineData(-3, "0 笔")] // 不会出现负数；万一出现也不把负号露给用户
    public void 账单数文案(int billCount, string expected) =>
        Assert.Equal(expected, ManagedItemPresentation.UsageText(billCount));

    [Theory]
    [InlineData("mdi-paw", "mdi-paw")]
    [InlineData("  mdi-credit-card-outline  ", "mdi-credit-card-outline")]
    public void 合法图标类名原样使用_去掉首尾空白(string input, string expected) =>
        Assert.Equal(expected, ManagedItemPresentation.NormalizeIcon(input, "mdi-tag"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("paw")]
    [InlineData("MDI-PAW")]
    [InlineData("mdi-paw;color:red")]
    public void 空值或非法图标类名回退给定的图标_不会渲染出空白色块(string? input) =>
        Assert.Equal("mdi-tag", ManagedItemPresentation.NormalizeIcon(input, "mdi-tag"));

    [Fact]
    public void 表单目标与保存值是只带表单字段的记录()
    {
        var id = Guid.NewGuid();
        var target = new NameIconTarget(id, "宠物", "mdi-paw", 3);
        var values = new NameIconValues("宠物", "mdi-paw", 3);

        Assert.Equal(id, target.Id);
        Assert.Equal(values, new NameIconValues(target.Name, target.Icon, target.SortOrder));
    }

    // ------------------------------------------------------------------ 分类

    [Fact]
    public void 分类页分段只有支出和收入_取值与账单类型同名()
    {
        Assert.Equal(
            [("Expense", "支出"), ("Income", "收入")],
            CategoryPresentation.Segments.ToArray());
        Assert.Equal(nameof(BillType.Expense), CategoryPresentation.SegmentExpense);
        Assert.Equal(nameof(BillType.Income), CategoryPresentation.SegmentIncome);
    }

    [Theory]
    [InlineData("Expense", BillType.Expense)]
    [InlineData("Income", BillType.Income)]
    [InlineData(null, BillType.Expense)]
    [InlineData("", BillType.Expense)]
    [InlineData("income", BillType.Expense)] // 取值区分大小写：不认识的一律按支出
    [InlineData("All", BillType.Expense)]
    public void 分段取值转账单类型_不认识的按支出(string? segment, BillType expected) =>
        Assert.Equal(expected, CategoryPresentation.TypeOf(segment));

    [Theory]
    [InlineData(BillType.Expense, "Expense")]
    [InlineData(BillType.Income, "Income")]
    public void 账单类型转分段取值_与分段互为反函数(BillType type, string expected)
    {
        Assert.Equal(expected, CategoryPresentation.SegmentOf(type));
        Assert.Equal(type, CategoryPresentation.TypeOf(CategoryPresentation.SegmentOf(type)));
    }

    [Fact]
    public void 分类候选图标都合法_互不重复_铺满网格_包含默认图标()
    {
        var choices = CategoryPresentation.IconChoices;

        AssertValidChoices(choices, CategoryPresentation.DefaultIcon, fallbackForCheck: CategoryPresentation.DefaultIcon);
        Assert.Equal(0, choices.Count % 6); // 图标选择器是 6 列：最后一行不留空位
    }

    [Fact]
    public void 分类默认图标就是账单行用的分类回退图标()
    {
        Assert.Equal(BillPresentation.DefaultCategoryIcon, CategoryPresentation.DefaultIcon);
        Assert.Equal("mdi-tag", CategoryPresentation.NormalizeIcon(null));
        Assert.Equal("mdi-paw", CategoryPresentation.NormalizeIcon("mdi-paw"));
    }

    [Fact]
    public void 分类配色与账单行里同一个分类的颜色一致()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        Assert.Equal(BillPresentation.CategoryTone("宠物", id), CategoryPresentation.ToneFor("宠物", id));
        Assert.Equal("orange", CategoryPresentation.ToneFor("餐饮美食", Guid.NewGuid())); // 预设分类固定配色
        Assert.Equal(CategoryPresentation.ToneFor("宠物", id), CategoryPresentation.ToneFor("宠物", id));
    }

    [Theory]
    [InlineData(BillType.Expense, false, "设为默认支出分类")]
    [InlineData(BillType.Income, false, "设为默认收入分类")]
    [InlineData(BillType.Expense, true, "取消默认")]
    [InlineData(BillType.Income, true, "取消默认")]
    public void 预设分类菜单里的默认项文案_默认值按账单类型分开(BillType type, bool isDefault, string expected) =>
        Assert.Equal(expected, CategoryPresentation.DefaultMenuLabel(type, isDefault));

    [Fact]
    public void 设为或取消默认分类的提示()
    {
        Assert.Equal("已设置为默认分类", CategoryPresentation.DefaultChangedMessage(true));
        Assert.Equal("已取消默认分类", CategoryPresentation.DefaultChangedMessage(false));
    }

    [Fact]
    public void 新建分类的名称占位提示按账单类型举例()
    {
        Assert.Equal("例如：宠物", CategoryPresentation.NamePlaceholder(BillType.Expense));
        Assert.Equal("例如：稿费", CategoryPresentation.NamePlaceholder(BillType.Income));
    }

    // ------------------------------------------------------------------ 支付渠道

    [Fact]
    public void 支付渠道候选图标都合法_互不重复_铺满网格_包含默认图标()
    {
        var choices = PaymentChannelPresentation.IconChoices;

        AssertValidChoices(choices, PaymentChannelPresentation.DefaultIcon, fallbackForCheck: PaymentChannelPresentation.DefaultIcon);
        Assert.Equal(0, choices.Count % 6);
        Assert.DoesNotContain(choices, c => c.Icon == "mdi-alipay"); // 字体里已经没有这个图标
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("credit-card")]
    public void 支付渠道图标缺失或非法回退默认图标(string? icon) =>
        Assert.Equal("mdi-credit-card", PaymentChannelPresentation.NormalizeIcon(icon));

    [Fact]
    public void 预设支付渠道里字体已移除的图标换成替代图标_历史数据不显示空白()
    {
        // 预设渠道「支付宝」的图标 mdi-alipay 在 MDI 7.2.96 里已不存在
        Assert.Equal("mdi-qrcode-scan", PaymentChannelPresentation.NormalizeIcon("mdi-alipay"));
        Assert.Equal("mdi-wechat", PaymentChannelPresentation.NormalizeIcon("mdi-wechat"));
    }

    [Fact]
    public void 支付渠道配色按渠道Id稳定取色_同一渠道每次一致且都在色板内()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        Assert.Equal(PaymentChannelPresentation.ToneFor(id), PaymentChannelPresentation.ToneFor(id));

        var tones = Enumerable.Range(0, 200)
            .Select(_ => PaymentChannelPresentation.ToneFor(Guid.NewGuid()))
            .ToHashSet();

        Assert.All(tones, tone => Assert.Contains(tone, BillPresentation.Tones));
        Assert.True(tones.Count >= 4, "不同支付渠道应分散在多种颜色上，而不是全部同色");
    }

    [Theory]
    [InlineData(false, "设为默认支付渠道")]
    [InlineData(true, "取消默认")]
    public void 预设渠道菜单里的默认项文案(bool isDefault, string expected) =>
        Assert.Equal(expected, PaymentChannelPresentation.DefaultMenuLabel(isDefault));

    [Fact]
    public void 设为或取消默认支付渠道的提示()
    {
        Assert.Equal("已设置为默认支付渠道", PaymentChannelPresentation.DefaultChangedMessage(true));
        Assert.Equal("已取消默认支付渠道", PaymentChannelPresentation.DefaultChangedMessage(false));
    }

    // ------------------------------------------------------------------ 小工具

    private static void AssertValidChoices(IReadOnlyList<IconChoice> choices, string defaultIcon, string fallbackForCheck)
    {
        Assert.Contains(choices, c => c.Icon == defaultIcon);
        Assert.Equal(choices.Count, choices.Select(c => c.Icon).Distinct().Count());
        Assert.Equal(choices.Count, choices.Select(c => c.Label).Distinct().Count());
        Assert.All(choices, c =>
        {
            // 每个候选图标本身都是合法类名：规范化后不变（不会被当成非法值回退）
            Assert.Equal(c.Icon, ManagedItemPresentation.NormalizeIcon(c.Icon, fallbackForCheck));
            Assert.False(string.IsNullOrWhiteSpace(c.Label), $"{c.Icon} 缺少读屏名称");
        });
    }
}
