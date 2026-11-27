using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Time;
using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 账单行的展示规则（标题 / 副标题 / 金额 / 时间文案、分类图标与配色）：规则集中在 <see cref="BillPresentation"/>，
/// 首页「最近账单」与账单 Tab 共用
/// </summary>
public class BillPresentationTests
{
    // 2026-11-25 20:00（应用时区的「现在」）
    private static readonly DateTime Now = new(2026, 11, 25, 20, 0, 0);

    private static Bill NewBill(
        decimal amount = 35m,
        BillType type = BillType.Expense,
        string? note = "午饭",
        DateTime? utc = null,
        string category = "餐饮美食",
        string? categoryIcon = "mdi-silverware-fork-knife",
        string channel = "微信支付",
        string? ledger = "日常开销")
    {
        var categoryId = Guid.NewGuid();
        return new Bill
        {
            Id = Guid.NewGuid(),
            Amount = amount,
            Type = type,
            Note = note,
            TransactionTime = utc ?? new DateTime(2026, 11, 25, 6, 30, 0, DateTimeKind.Utc),
            CategoryId = categoryId,
            Category = new Category { Id = categoryId, Name = category, Icon = categoryIcon ?? string.Empty },
            PaymentChannel = new PaymentChannel { Name = channel },
            Ledger = ledger is null ? null : new Ledger { Name = ledger },
        };
    }

    // ---- 标题 / 副标题 ----

    [Theory]
    [InlineData("餐饮美食", "日常开销", "餐饮美食 · 日常开销")]
    [InlineData("餐饮美食", "  日常开销  ", "餐饮美食 · 日常开销")]
    [InlineData("餐饮美食", "日常\n开销", "餐饮美食 · 日常 开销")]
    [InlineData("餐饮美食", null, "餐饮美食 · 未归属账本")]
    [InlineData("餐饮美食", "", "餐饮美食 · 未归属账本")]
    [InlineData("餐饮美食", "   ", "餐饮美食 · 未归属账本")]
    public void 标题是分类加账本_历史无账本的账单标注未归属账本(string category, string? ledger, string expected) =>
        Assert.Equal(expected, BillPresentation.Title(category, ledger));

    [Theory]
    [InlineData("微信支付", "午饭", "微信支付 · 午饭")]
    [InlineData("微信支付", "  午饭  ", "微信支付 · 午饭")]
    [InlineData("微信支付", "午饭\n晚饭", "微信支付 · 午饭 晚饭")]
    [InlineData("微信支付", null, "微信支付")]
    [InlineData("微信支付", "", "微信支付")]
    [InlineData("微信支付", "  ", "微信支付")]
    [InlineData(null, "午饭", "未知渠道 · 午饭")]
    [InlineData(null, null, "未知渠道")]
    public void 副标题是支付渠道加备注_没有备注时只有支付渠道(string? channel, string? note, string expected) =>
        Assert.Equal(expected, BillPresentation.Subtitle(channel, note));

    [Theory]
    [InlineData("餐饮美食", "午饭", "餐饮美食 · 午饭")]
    [InlineData("餐饮美食", "  午饭  ", "餐饮美食 · 午饭")]
    [InlineData("餐饮美食", "午饭\n晚饭", "餐饮美食 · 午饭 晚饭")]
    [InlineData("餐饮美食", null, "餐饮美食")]
    [InlineData("餐饮美食", "", "餐饮美食")]
    [InlineData("餐饮美食", "   ", "餐饮美食")]
    public void 指代账单的文字是分类加备注_没有备注时只有分类(string category, string? note, string expected) =>
        Assert.Equal(expected, BillPresentation.Describe(category, note));

    // ---- 时间 ----

    [Fact]
    public void 不带日期的时间只显示时分()
    {
        Assert.Equal("14:30", BillPresentation.TimeText(new DateTime(2026, 11, 25, 14, 30, 0), Now, includeDate: false));
        Assert.Equal("09:05", BillPresentation.TimeText(new DateTime(2026, 11, 20, 9, 5, 0), Now, includeDate: false));
    }

    [Theory]
    [InlineData("2026-11-25 14:30", "14:30")]
    [InlineData("2026-11-25 00:00", "00:00")]
    [InlineData("2026-11-24 23:59", "昨天 23:59")]
    [InlineData("2026-11-24 00:00", "昨天 00:00")]
    [InlineData("2026-11-23 23:59", "11-23 23:59")]
    [InlineData("2026-01-02 08:09", "01-02 08:09")]
    [InlineData("2025-12-31 23:59", "2025-12-31 23:59")]
    public void 带日期的时间按今天_昨天_同年_更早分档(string local, string expected) =>
        Assert.Equal(expected, BillPresentation.TimeText(DateTime.Parse(local, System.Globalization.CultureInfo.InvariantCulture), Now, includeDate: true));

    [Fact]
    public void 跨年的昨天仍然是昨天()
    {
        var newYearMorning = new DateTime(2027, 1, 1, 8, 0, 0);

        Assert.Equal("昨天 23:30", BillPresentation.TimeText(new DateTime(2026, 12, 31, 23, 30, 0), newYearMorning, includeDate: true));
    }

    // ---- 图标 / 配色 ----

    [Theory]
    [InlineData("mdi-silverware-fork-knife", "mdi-silverware-fork-knife")]
    [InlineData(null, "mdi-tag")]
    [InlineData("", "mdi-tag")]
    [InlineData("fork", "mdi-tag")]
    [InlineData("mdi-x;color:red", "mdi-tag")]
    public void 分类图标合法就用_否则回退通用标签(string? icon, string expected) =>
        Assert.Equal(expected, BillPresentation.CategoryIcon(icon));

    [Theory]
    [InlineData("餐饮美食", "orange")]
    [InlineData("交通出行", "blue")]
    [InlineData("日用百货", "teal")]
    [InlineData("购物消费", "pink")]
    [InlineData("娱乐休闲", "purple")]
    [InlineData("医疗健康", "red")]
    [InlineData("教育培训", "blue")]
    [InlineData("居住生活", "green")]
    [InlineData("通讯物流", "teal")]
    [InlineData("其他支出", "gray")]
    [InlineData("工资薪酬", "green")]
    [InlineData("奖金补贴", "orange")]
    [InlineData("投资理财", "purple")]
    [InlineData("兼职副业", "blue")]
    [InlineData("其他收入", "gray")]
    public void 预设分类固定配色(string name, string expected) =>
        Assert.Equal(expected, BillPresentation.CategoryTone(name, Guid.NewGuid()));

    [Fact]
    public void 自定义分类按分类Id稳定取色_同一分类每次一致且分散在色板内()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        Assert.Equal(BillPresentation.CategoryTone("宠物", id), BillPresentation.CategoryTone("宠物", id));

        var tones = Enumerable.Range(0, 200).Select(_ => BillPresentation.CategoryTone("自定义", Guid.NewGuid())).ToHashSet();

        Assert.All(tones, tone => Assert.Contains(tone, BillPresentation.Tones));
        Assert.True(tones.Count >= 4, "不同的自定义分类应分散在多种颜色上，而不是全部同色");
    }

    [Fact]
    public void 没有分类信息时用灰色()
    {
        Assert.Equal("gray", BillPresentation.CategoryTone(null, null));
        Assert.Equal("gray", BillPresentation.CategoryTone("宠物", Guid.Empty));
        Assert.Equal("gray", BillPresentation.CategoryTone(string.Empty, Guid.Empty));
    }

    // ---- 整行 ----

    [Fact]
    public void 支出账单转成行_金额负号_时间换算到应用时区()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);
        var bill = NewBill();

        var row = BillPresentation.ToRow(bill, Now, includeDate: true);

        Assert.Equal(bill.Id, row.Id);
        Assert.Equal("餐饮美食 · 日常开销", row.Title);
        Assert.Equal("微信支付 · 午饭", row.Subtitle);
        Assert.Equal("-¥35.00", row.AmountText);
        Assert.False(row.IsIncome);
        // UTC 06:30 在 UTC+8 是 14:30（今天，所以只显示时分）
        Assert.Equal("14:30", row.TimeText);
        Assert.Equal("mdi-silverware-fork-knife", row.Icon);
        Assert.Equal("orange", row.Tone);
        Assert.Equal("餐饮美食 · 日常开销，微信支付 · 午饭，支出 ¥35.00，14:30", row.AriaLabel);
    }

    [Fact]
    public void 行里把分类名和账本分开给_账单行据此排成分类加粗_账本灰色()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);

        var withLedger = BillPresentation.ToRow(NewBill(ledger: "  日常开销  "), Now, includeDate: true);
        var noLedger = BillPresentation.ToRow(NewBill(ledger: null), Now, includeDate: true);
        var unknown = NewBill();
        unknown.Category = null!;
        var unknownCategory = BillPresentation.ToRow(unknown, Now, includeDate: true);

        // 分开的两段拼回去就是整句标题（E2E 与读屏看到的文字不变）
        Assert.Equal("餐饮美食", withLedger.CategoryName);
        Assert.Equal("日常开销", withLedger.LedgerText);
        Assert.Equal(withLedger.Title, $"{withLedger.CategoryName} · {withLedger.LedgerText}");
        // 没有账本（历史账单）：账本那一段标注「未归属账本」，不留空
        Assert.Equal("餐饮美食", noLedger.CategoryName);
        Assert.Equal(BillPresentation.UnassignedLedgerLabel, noLedger.LedgerText);
        Assert.Equal(noLedger.Title, $"{noLedger.CategoryName} · {noLedger.LedgerText}");
        // 分类缺失：分类名是「未知分类」，账本照常
        Assert.Equal(BillPresentation.UnknownCategoryLabel, unknownCategory.CategoryName);
        Assert.Equal("日常开销", unknownCategory.LedgerText);
    }

    [Fact]
    public void 备注只出现在副标题里_不再跟在分类后面()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);

        var withNote = BillPresentation.ToRow(NewBill(note: "  午饭  "), Now, includeDate: true);
        var noNote = BillPresentation.ToRow(NewBill(note: "   "), Now, includeDate: true);

        Assert.Equal("微信支付 · 午饭", withNote.Subtitle);
        Assert.DoesNotContain("午饭", withNote.Title);
        // 行里不单独画备注，但模型留着它（折成单行）给删除确认点名用
        Assert.Equal("午饭", withNote.NoteText);
        Assert.Null(noNote.NoteText);
        // 没有备注：副标题只有支付渠道，不留悬空的「 · 」
        Assert.Equal("微信支付", noNote.Subtitle);
        Assert.Equal("餐饮美食 · 日常开销", noNote.Title);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("午饭", "午饭")]
    [InlineData("  午饭 \n", "午饭")]
    [InlineData("午饭\n晚饭", "午饭 晚饭")]
    [InlineData("午饭  \t 晚饭\r\n夜宵", "午饭 晚饭 夜宵")]
    public void 单行文字去首尾空白_中间空白折成一个空格_空白当没有(string? text, string? expected) =>
        Assert.Equal(expected, BillPresentation.SingleLine(text));

    [Theory]
    [InlineData("日常开销", "日常开销")]
    [InlineData("  日常开销\n", "日常开销")]
    [InlineData("日常\n开销", "日常 开销")]
    [InlineData(null, "未归属账本")]
    [InlineData("", "未归属账本")]
    [InlineData("  \t", "未归属账本")]
    public void 账本文字是账本名折成单行_没有账本时标注未归属账本(string? ledger, string expected) =>
        Assert.Equal(expected, BillPresentation.LedgerLabel(ledger));

    [Fact]
    public void 收入账单转成行_金额正号_读屏名称写收入()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);
        var bill = NewBill(amount: 12000m, type: BillType.Income, note: null, category: "工资薪酬", categoryIcon: "mdi-briefcase", channel: "银行卡");

        var row = BillPresentation.ToRow(bill, Now, includeDate: true);

        Assert.Equal("工资薪酬 · 日常开销", row.Title);
        Assert.Equal("银行卡", row.Subtitle);
        Assert.Equal("+¥12,000.00", row.AmountText);
        Assert.True(row.IsIncome);
        Assert.Equal("green", row.Tone);
        Assert.Contains("收入 ¥12,000.00", row.AriaLabel);
    }

    [Fact]
    public void 历史无账本的账单在行里标注未归属账本()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);

        var row = BillPresentation.ToRow(NewBill(ledger: null), Now, includeDate: true);

        Assert.Equal("餐饮美食 · 未归属账本", row.Title);
        Assert.Equal("微信支付 · 午饭", row.Subtitle);
        Assert.Contains("未归属账本", row.AriaLabel);
    }

    [Fact]
    public void 本地日期以应用时区为准_UTC的昨天可能是应用时区的今天()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);

        // UTC 2026-11-24 17:00 = UTC+8 2026-11-25 01:00（今天），按系统时区或 UTC 判断都会误判成昨天
        var bill = NewBill(utc: new DateTime(2026, 11, 24, 17, 0, 0, DateTimeKind.Utc));

        Assert.Equal("01:00", BillPresentation.ToRow(bill, Now, includeDate: true).TimeText);
    }

    [Fact]
    public void 分类缺失时不抛异常_显示未知分类与灰色()
    {
        using var _ = AppTimeZone.UseZone(TestZones.UtcPlus8);
        var bill = NewBill();
        bill.Category = null!;
        bill.PaymentChannel = null!;

        var row = BillPresentation.ToRow(bill, Now, includeDate: false);

        Assert.Equal("未知分类 · 日常开销", row.Title);
        Assert.Equal("未知渠道 · 午饭", row.Subtitle);
        Assert.Equal("mdi-tag", row.Icon);
    }
}

/// <summary>
/// 图标类名校验：只接受 mdi-xxx 小写连字符形式，避免把任意字符串写进 class 属性
/// </summary>
public class MdiIconsTests
{
    [Theory]
    [InlineData("mdi-wallet", "mdi-wallet")]
    [InlineData("  mdi-book-open-variant  ", "mdi-book-open-variant")]
    [InlineData("mdi-numeric-1-box", "mdi-numeric-1-box")]
    public void 合法图标类名原样使用(string input, string expected) =>
        Assert.Equal(expected, MdiIcons.Normalize(input, "mdi-tag"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tag")]
    [InlineData("MDI-TAG")]
    [InlineData("mdi-")]
    [InlineData("mdi--tag")]
    [InlineData("mdi-tag mdi-x")]
    [InlineData("mdi-tag\"onclick=\"x")]
    public void 空值或非法图标类名回退到调用方给的默认值(string? input) =>
        Assert.Equal("mdi-fallback", MdiIcons.Normalize(input, "mdi-fallback"));

    [Theory]
    [InlineData("mdi-alipay")]
    [InlineData("  mdi-alipay  ")]
    public void 字体里已不存在的图标换成替代图标_预设支付宝渠道不再是空白(string input) =>
        Assert.Equal("mdi-qrcode-scan", MdiIcons.Normalize(input, "mdi-fallback"));

    [Fact]
    public void 预设数据里的图标在应用引用的MDI字体里都有字形()
    {
        // 预设分类 / 支付渠道的图标类名都写在 PresetDataService 里，类名不在字体里的会渲染成空白色块。
        // 下面这份清单是 @mdi/font 7.2.96（App 的 index.html 引用的版本）里确实存在的字形；
        // 新增预设时请先到 https://pictogrammers.com/library/mdi/ 确认该版本里有对应图标，再把名字加进清单。
        var source = File.ReadAllText(RepoRoot.Combine("src/FinanceTracker.Core/Services/PresetDataService.cs"));
        var presetIcons = System.Text.RegularExpressions.Regex.Matches(source, @"mdi-[a-z0-9]+(-[a-z0-9]+)*")
            .Select(match => match.Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(presetIcons);

        var shown = presetIcons.Select(icon => MdiIcons.Normalize(icon, "mdi-tag")).Distinct().ToList();
        var missing = shown.Where(icon => !GlyphsInFont.Contains(icon)).ToList();

        Assert.True(missing.Count == 0, $"这些预设图标在 MDI 7.2.96 字体里没有字形（会显示成空白）：{string.Join("、", missing)}");
    }

    // @mdi/font 7.2.96 里确实存在、且被预设数据用到的字形（mdi-alipay 已被移除，由 MdiIcons 换成 mdi-qrcode-scan）
    private static readonly HashSet<string> GlyphsInFont = new(StringComparer.Ordinal)
    {
        "mdi-apple", "mdi-briefcase", "mdi-car", "mdi-cart", "mdi-cash", "mdi-chart-line", "mdi-credit-card",
        "mdi-credit-card-outline", "mdi-dots-horizontal", "mdi-food", "mdi-gamepad-variant", "mdi-gift", "mdi-home",
        "mdi-hospital-box", "mdi-phone", "mdi-qrcode-scan", "mdi-school", "mdi-shopping", "mdi-wechat",
    };
}
