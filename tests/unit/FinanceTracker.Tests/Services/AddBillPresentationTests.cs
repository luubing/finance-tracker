using FinanceTracker.Core.Enums;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 「记一笔」Sheet 的展示与选择规则：默认分类 / 支付渠道的挑选、保存前校验、关闭确认、时间文案、分类网格分页。
/// 键盘规则见 <see cref="AmountInputTests"/>。
/// </summary>
public class AddBillPresentationTests
{
    private static readonly Guid Food = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Traffic = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Salary = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid Bonus = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private static readonly CategoryOption[] Categories =
    [
        new(Food, "餐饮", "mdi-food", BillType.Expense),
        new(Traffic, "交通", "mdi-car", BillType.Expense),
        new(Salary, "工资", "mdi-cash", BillType.Income),
        new(Bonus, "奖金", "mdi-gift", BillType.Income),
    ];

    // ------------------------------------------------------------------ 默认分类

    [Fact]
    public void 默认分类_用户设置的默认分类优先()
    {
        Assert.Equal(Traffic, AddBillPresentation.PickCategory(Categories, BillType.Expense, Traffic));
        Assert.Equal(Bonus, AddBillPresentation.PickCategory(Categories, BillType.Income, Bonus));
    }

    [Fact]
    public void 默认分类_没有设置就取该类型的第一项()
    {
        Assert.Equal(Food, AddBillPresentation.PickCategory(Categories, BillType.Expense, null));
        Assert.Equal(Salary, AddBillPresentation.PickCategory(Categories, BillType.Income, null));
    }

    [Fact]
    public void 默认分类_设置的默认分类属于另一种类型或已不存在_按第一项处理()
    {
        // 支出的默认分类不能被当成收入的默认分类
        Assert.Equal(Salary, AddBillPresentation.PickCategory(Categories, BillType.Income, Food));
        Assert.Equal(Food, AddBillPresentation.PickCategory(Categories, BillType.Expense, Guid.NewGuid()));
    }

    [Fact]
    public void 默认分类_该类型一个分类都没有时是空Guid()
    {
        var onlyExpense = Categories.Where(c => c.Type == BillType.Expense).ToList();

        Assert.Equal(Guid.Empty, AddBillPresentation.PickCategory(onlyExpense, BillType.Income, null));
        Assert.Equal(Guid.Empty, AddBillPresentation.PickCategory([], BillType.Expense, Food));
    }

    // ------------------------------------------------------------------ 默认支付渠道

    [Fact]
    public void 默认支付渠道_用户设置的优先_否则第一项_没有渠道是空Guid()
    {
        var wechat = new ChannelOption(Guid.NewGuid(), "微信", "mdi-wechat");
        var cash = new ChannelOption(Guid.NewGuid(), "现金", "mdi-cash");
        ChannelOption[] channels = [wechat, cash];

        Assert.Equal(cash.Id, AddBillPresentation.PickChannel(channels, cash.Id));
        Assert.Equal(wechat.Id, AddBillPresentation.PickChannel(channels, null));
        Assert.Equal(wechat.Id, AddBillPresentation.PickChannel(channels, Guid.NewGuid()));
        Assert.Equal(Guid.Empty, AddBillPresentation.PickChannel([], cash.Id));
    }

    // ------------------------------------------------------------------ 保存前校验

    private static readonly Guid SomeId = Guid.Parse("00000000-0000-0000-0000-00000000ffff");

    [Fact]
    public void 校验_都齐了可以保存()
    {
        Assert.Equal(AddBillBlock.None, AddBillPresentation.CheckSave(35m, SomeId, SomeId, SomeId));
        Assert.Equal(AddBillBlock.None, AddBillPresentation.CheckSave(0.01m, SomeId, SomeId, SomeId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void 校验_金额为空或不大于零不能保存(int? amount)
    {
        decimal? value = amount;
        Assert.Equal(AddBillBlock.Amount, AddBillPresentation.CheckSave(value, SomeId, SomeId, SomeId));
    }

    [Fact]
    public void 校验_缺分类_缺支付渠道_缺账本_各自拦下()
    {
        Assert.Equal(AddBillBlock.Category, AddBillPresentation.CheckSave(35m, Guid.Empty, SomeId, SomeId));
        Assert.Equal(AddBillBlock.Channel, AddBillPresentation.CheckSave(35m, SomeId, Guid.Empty, SomeId));
        Assert.Equal(AddBillBlock.Ledger, AddBillPresentation.CheckSave(35m, SomeId, SomeId, null));
        Assert.Equal(AddBillBlock.Ledger, AddBillPresentation.CheckSave(35m, SomeId, SomeId, Guid.Empty));
    }

    [Fact]
    public void 校验_多项都缺时按操作顺序先报金额_再分类_再支付渠道_最后账本()
    {
        Assert.Equal(AddBillBlock.Amount, AddBillPresentation.CheckSave(null, Guid.Empty, Guid.Empty, null));
        Assert.Equal(AddBillBlock.Category, AddBillPresentation.CheckSave(35m, Guid.Empty, Guid.Empty, null));
        Assert.Equal(AddBillBlock.Channel, AddBillPresentation.CheckSave(35m, SomeId, Guid.Empty, null));
    }

    [Fact]
    public void 缺项提示文案_沿用原记账页的措辞()
    {
        Assert.Equal("请输入金额", AddBillPresentation.BlockMessage(AddBillBlock.Amount));
        Assert.Equal("请选择分类", AddBillPresentation.BlockMessage(AddBillBlock.Category));
        Assert.Equal("请选择支付渠道", AddBillPresentation.BlockMessage(AddBillBlock.Channel));
        Assert.Equal("请选择账本", AddBillPresentation.BlockMessage(AddBillBlock.Ledger));
        Assert.Equal(string.Empty, AddBillPresentation.BlockMessage(AddBillBlock.None));
    }

    // ------------------------------------------------------------------ 关闭确认

    [Theory]
    [InlineData("", null, false)]
    [InlineData(null, null, false)]
    [InlineData("", "", false)]
    [InlineData("", "   ", false)]
    [InlineData("0", null, true)]
    [InlineData("35", null, true)]
    [InlineData("", "午餐", true)]
    [InlineData("35", "午餐", true)]
    public void 已输入内容_金额或备注非空才需要关闭确认(string? amount, string? note, bool expected) =>
        Assert.Equal(expected, AddBillPresentation.HasInput(amount, note));

    // ------------------------------------------------------------------ 时间

    private static readonly DateTime Now = new(2026, 11, 25, 12, 8, 0);

    [Fact]
    public void 时间文案_今天和昨天带相对日期()
    {
        Assert.Equal("今天 12:08", AddBillPresentation.TimeLabel(new DateTime(2026, 11, 25, 12, 8, 0), Now));
        Assert.Equal("今天 00:00", AddBillPresentation.TimeLabel(new DateTime(2026, 11, 25, 0, 0, 0), Now));
        Assert.Equal("昨天 09:30", AddBillPresentation.TimeLabel(new DateTime(2026, 11, 24, 9, 30, 0), Now));
    }

    [Fact]
    public void 时间文案_相对日期按应用时区的日历日_不是二十四小时()
    {
        // 现在是 00:05：十分钟前（昨天 23:55）显示「昨天」，而不是「今天」
        var justAfterMidnight = new DateTime(2026, 11, 25, 0, 5, 0);
        Assert.Equal("昨天 23:55", AddBillPresentation.TimeLabel(new DateTime(2026, 11, 24, 23, 55, 0), justAfterMidnight));
    }

    [Fact]
    public void 时间文案_同年显示月日_跨年带年份()
    {
        Assert.Equal("11月23日 09:30", AddBillPresentation.TimeLabel(new DateTime(2026, 11, 23, 9, 30, 0), Now));
        Assert.Equal("1月5日 18:00", AddBillPresentation.TimeLabel(new DateTime(2026, 1, 5, 18, 0, 0), Now));
        Assert.Equal("2025年12月31日 23:59", AddBillPresentation.TimeLabel(new DateTime(2025, 12, 31, 23, 59, 0), Now));
    }

    [Fact]
    public void 时间文案_未来的时间也能显示()
    {
        // 用户可以给明天补一笔预付：不特殊处理，按日期显示
        Assert.Equal("11月26日 08:00", AddBillPresentation.TimeLabel(new DateTime(2026, 11, 26, 8, 0, 0), Now));
    }

    [Fact]
    public void 时间输入控件的值_往返一致_精确到分钟()
    {
        var local = new DateTime(2026, 11, 25, 9, 5, 42);

        var text = AddBillPresentation.ToInputValue(local);
        Assert.Equal("2026-11-25T09:05", text);

        Assert.True(AddBillPresentation.TryParseInputValue(text, out var parsed));
        Assert.Equal(new DateTime(2026, 11, 25, 9, 5, 0), parsed);
        Assert.Equal(DateTimeKind.Unspecified, parsed.Kind);
    }

    [Fact]
    public void 时间输入控件的值_带秒也接受_格式不对返回false()
    {
        Assert.True(AddBillPresentation.TryParseInputValue("2026-11-25T09:05:30", out var withSeconds));
        Assert.Equal(new DateTime(2026, 11, 25, 9, 5, 30), withSeconds);

        Assert.False(AddBillPresentation.TryParseInputValue("", out _));
        Assert.False(AddBillPresentation.TryParseInputValue(null, out _));
        Assert.False(AddBillPresentation.TryParseInputValue("2026-11-25", out _));
        Assert.False(AddBillPresentation.TryParseInputValue("昨天 12:00", out _));
        Assert.False(AddBillPresentation.TryParseInputValue("2026-13-45T25:61", out _));
    }

    // ------------------------------------------------------------------ 分类网格

    [Fact]
    public void 网格每页五列两行_十个格子()
    {
        Assert.Equal(5, AddBillPresentation.CategoryColumns);
        Assert.Equal(2, AddBillPresentation.CategoryRowsPerPage);
        Assert.Equal(10, AddBillPresentation.CategoryPageSize);
    }

    [Theory]
    [InlineData(-3, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(6, 1)]    // 收入：5 个预设 + 新建分类
    [InlineData(10, 1)]
    [InlineData(11, 2)]   // 支出：10 个预设 + 新建分类
    [InlineData(20, 2)]
    [InlineData(21, 3)]
    public void 网格页数_至少一页_超出一页放不下就翻页(int cells, int pages) =>
        Assert.Equal(pages, AddBillPresentation.CategoryPageCount(cells));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    [InlineData(19, 1)]
    [InlineData(20, 2)]
    [InlineData(-1, 0)]
    public void 格子所在页_每页十个(int index, int page) =>
        Assert.Equal(page, AddBillPresentation.CategoryPageOf(index));

    // ------------------------------------------------------------------ 编辑模式：标题与文案

    [Fact]
    public void 标题_新建是记一笔_编辑是编辑账单()
    {
        Assert.Equal("记一笔", AddBillPresentation.CreateTitle);
        Assert.Equal("编辑账单", AddBillPresentation.EditTitle);
    }

    [Fact]
    public void 关闭确认文案_编辑说放弃修改_新建说放弃本次记账()
    {
        Assert.Equal("放弃本次记账？", AddBillPresentation.DiscardTitle(false));
        Assert.Equal("已输入的内容不会保存。", AddBillPresentation.DiscardMessage(false));
        Assert.Equal("放弃修改？", AddBillPresentation.DiscardTitle(true));
        Assert.Equal("本次的修改不会保存。", AddBillPresentation.DiscardMessage(true));
    }

    [Fact]
    public void 编辑后的提示文案_与账单页的删除提示措辞一致()
    {
        Assert.Equal("账单已更新", AddBillPresentation.UpdatedText);
        Assert.Equal("账单已删除", AddBillPresentation.DeletedText);
        Assert.Equal("账单不存在或已删除", AddBillPresentation.BillGoneText);
    }

    [Fact]
    public void 无权限提示_点明无权限_带上服务层给的原因_没有原因也不空()
    {
        Assert.Equal("无权限编辑这笔账单：只读成员不能在共享账本中记账", AddBillPresentation.NoPermissionText("只读成员不能在共享账本中记账"));
        Assert.Equal("无权限编辑这笔账单：无权向该账本记账", AddBillPresentation.NoPermissionText("  无权向该账本记账 "));
        Assert.Equal("无权限编辑这笔账单", AddBillPresentation.NoPermissionText(null));
        Assert.Equal("无权限编辑这笔账单", AddBillPresentation.NoPermissionText("   "));
    }

    [Fact]
    public void 删除确认说明_与账单页左滑删除同一句话()
    {
        // 记一笔 Sheet 的编辑模式里删除，措辞要和账单页左滑删除完全一致（同一个函数的两个入口）
        Assert.Equal(
            "餐饮美食 · 午餐，-¥35.00。删除后不再显示，并会随同步传播到其他设备。",
            BillListPresentation.DeleteMessage("餐饮美食 · 午餐", "-¥35.00"));
    }

    // ------------------------------------------------------------------ 编辑模式：内容快照（有没有改过）

    private static readonly Guid Wechat = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid Alipay = Guid.Parse("00000000-0000-0000-0000-0000000000c2");
    private static readonly Guid LedgerHome = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid LedgerTrip = Guid.Parse("00000000-0000-0000-0000-0000000000d2");
    private static readonly DateTime Noon = new(2026, 11, 25, 12, 8, 0, DateTimeKind.Unspecified);

    private static BillDraft Draft(
        decimal? amount = 35m,
        BillType type = BillType.Expense,
        Guid? categoryId = null,
        Guid? channelId = null,
        Guid? ledgerId = null,
        DateTime? time = null,
        string? note = "午餐") =>
        BillDraft.Of(amount, type, categoryId ?? Food, channelId ?? Wechat, ledgerId ?? LedgerHome, time ?? Noon, note);

    [Fact]
    public void 内容快照_同样的内容相等()
    {
        Assert.Equal(Draft(), Draft());
        Assert.Equal(Draft().GetHashCode(), Draft().GetHashCode());
    }

    [Fact]
    public void 内容快照_金额取两位小数_用户看不见的零头不算改动()
    {
        Assert.Equal(Draft(amount: 35m), Draft(amount: 35.00m));
        Assert.Equal(Draft(amount: 35.5m), Draft(amount: 35.504m));
        Assert.NotEqual(Draft(amount: 35m), Draft(amount: 35.01m));
    }

    [Fact]
    public void 内容快照_没有金额记为零()
    {
        Assert.Equal(0m, Draft(amount: null).Amount);
        Assert.Equal(Draft(amount: null), Draft(amount: 0m));
    }

    [Fact]
    public void 内容快照_时间只到分钟_秒和毫秒不算改动_差一分钟算改动()
    {
        var withSeconds = new DateTime(2026, 11, 25, 12, 8, 41, 123, DateTimeKind.Unspecified);

        Assert.Equal(Draft(time: Noon), Draft(time: withSeconds));
        Assert.NotEqual(Draft(time: Noon), Draft(time: Noon.AddMinutes(1)));
    }

    [Fact]
    public void 内容快照_时间的Kind不影响比较()
    {
        var local = new DateTime(2026, 11, 25, 12, 8, 0, DateTimeKind.Local);
        var utc = new DateTime(2026, 11, 25, 12, 8, 0, DateTimeKind.Utc);

        // 比较的是应用时区的墙上时间：调用方传入的是同一个墙上时间，Kind 只是标记
        Assert.Equal(Draft(time: Noon), Draft(time: local));
        Assert.Equal(Draft(time: Noon), Draft(time: utc));
        Assert.Equal(DateTimeKind.Unspecified, Draft(time: utc).Time.Kind);
    }

    [Theory]
    [InlineData("午餐", "午餐")]
    [InlineData("午餐", "  午餐 ")]
    [InlineData(null, "")]
    [InlineData(null, "   ")]
    [InlineData("", null)]
    public void 内容快照_备注去掉首尾空白_空白等同于没有备注(string? a, string? b) =>
        Assert.Equal(Draft(note: a), Draft(note: b));

    [Fact]
    public void 内容快照_备注内容不同算改动()
    {
        Assert.NotEqual(Draft(note: "午餐"), Draft(note: "晚餐"));
        Assert.NotEqual(Draft(note: "午餐"), Draft(note: null));
        Assert.Null(Draft(note: "  ").Note);
        Assert.Equal("午餐", Draft(note: " 午餐 ").Note);
    }

    [Fact]
    public void 内容快照_类型_分类_渠道_账本任何一项不同都算改动()
    {
        Assert.NotEqual(Draft(), Draft(type: BillType.Income));
        Assert.NotEqual(Draft(), Draft(categoryId: Traffic));
        Assert.NotEqual(Draft(), Draft(channelId: Alipay));
        Assert.NotEqual(Draft(), Draft(ledgerId: LedgerTrip));
    }

    [Fact]
    public void 内容快照_没有账本的历史账单账本是空_空Guid等同于没有()
    {
        var legacy = BillDraft.Of(35m, BillType.Expense, Food, Wechat, null, Noon, "午餐");

        Assert.Null(legacy.LedgerId);
        Assert.Equal(legacy, BillDraft.Of(35m, BillType.Expense, Food, Wechat, Guid.Empty, Noon, "午餐"));
        // 补选了账本就是改动
        Assert.NotEqual(legacy, Draft());
    }
}
