using System.Globalization;
using FinanceTracker.Core.Models;
using FinanceTracker.Web.Components.Ui;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>自定义报表页的展示规则（21 号）：快捷区间、起止日期校验、结果说明、分类环比各行、导出的 CSV 与分享的文字摘要。</summary>
public sealed class CustomReportPresentationTests
{
    private static readonly DateTime Today = new(2026, 11, 25);

    private static CategoryComparisonData Comparison(string name, decimal current, decimal previous, string icon = "mdi-food") => new()
    {
        CategoryId = Guid.NewGuid(),
        CategoryName = name,
        CategoryIcon = icon,
        CurrentAmount = current,
        PreviousAmount = previous,
    };

    private static CategoryStatistics Category(string name, decimal amount, int count, decimal percentage) => new()
    {
        CategoryId = Guid.NewGuid(),
        CategoryName = name,
        CategoryIcon = "mdi-food",
        Amount = amount,
        Count = count,
        Percentage = percentage,
    };

    private static CustomStatistics Sample(params CategoryStatistics[] expense) => new()
    {
        StartDate = new DateTime(2026, 11, 1),
        EndDate = new DateTime(2026, 11, 25),
        TotalExpense = 1234.5m,
        TotalIncome = 5000m,
        BillCount = 12,
        ExpenseCategoryStats = expense.ToList(),
    };

    // ───────────────────────── 快捷区间 ─────────────────────────

    [Fact]
    public void 四个快捷区间按本月_上月_近30天_近90天的顺序()
    {
        Assert.Equal(
            new[] { ("this-month", "本月"), ("last-month", "上月"), ("last-30", "近30天"), ("last-90", "近90天") },
            CustomReportPresentation.QuickRanges.Select(r => (r.Key, r.Label)));
    }

    [Theory]
    [InlineData("this-month", "2026-11-01", "2026-11-25")]
    [InlineData("last-month", "2026-10-01", "2026-10-31")]
    [InlineData("last-30", "2026-10-26", "2026-11-25")]
    [InlineData("last-90", "2026-08-27", "2026-11-25")]
    public void 快捷区间的起止日期都含当天(string key, string start, string end)
    {
        var bounds = CustomReportPresentation.QuickRangeBounds(key, Today);

        Assert.Equal(DateTime.Parse(start, CultureInfo.InvariantCulture), bounds.Start);
        Assert.Equal(DateTime.Parse(end, CultureInfo.InvariantCulture), bounds.End);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("last-year")]
    public void 不认识的快捷区间按本月处理(string? key)
    {
        Assert.Equal(CustomReportPresentation.QuickRangeBounds("this-month", Today), CustomReportPresentation.QuickRangeBounds(key, Today));
    }

    [Fact]
    public void 打开页面时的默认区间是本月1日到今天()
    {
        var (start, end) = CustomReportPresentation.DefaultRange(new DateTime(2026, 11, 25, 15, 30, 0));

        Assert.Equal(new DateTime(2026, 11, 1), start);
        Assert.Equal(new DateTime(2026, 11, 25), end);
        Assert.Equal(TimeSpan.Zero, start.TimeOfDay);
        Assert.Equal(TimeSpan.Zero, end.TimeOfDay);
    }

    [Theory]
    [InlineData("2026-01-15", "last-month", "2025-12-01", "2025-12-31")]   // 跨年
    [InlineData("2026-03-31", "last-month", "2026-02-01", "2026-02-28")]   // 31 日往前推一个月不溢出
    [InlineData("2028-03-31", "last-month", "2028-02-01", "2028-02-29")]   // 闰年
    [InlineData("2026-03-31", "this-month", "2026-03-01", "2026-03-31")]
    [InlineData("2026-03-01", "this-month", "2026-03-01", "2026-03-01")]   // 1 日当天：本月就是一天
    public void 月份边界上的快捷区间(string today, string key, string start, string end)
    {
        var bounds = CustomReportPresentation.QuickRangeBounds(key, DateTime.Parse(today, CultureInfo.InvariantCulture));

        Assert.Equal((DateTime.Parse(start, CultureInfo.InvariantCulture), DateTime.Parse(end, CultureInfo.InvariantCulture)), bounds);
    }

    [Fact]
    public void 今天带时分秒也只取日期()
    {
        var bounds = CustomReportPresentation.QuickRangeBounds("last-30", new DateTime(2026, 11, 25, 23, 59, 59));

        Assert.Equal((new DateTime(2026, 10, 26), new DateTime(2026, 11, 25)), bounds);
    }

    [Theory]
    [InlineData("2026-11-01", "2026-11-25", "this-month")]
    [InlineData("2026-10-01", "2026-10-31", "last-month")]
    [InlineData("2026-10-26", "2026-11-25", "last-30")]
    [InlineData("2026-08-27", "2026-11-25", "last-90")]
    [InlineData("2026-11-02", "2026-11-25", null)]
    [InlineData("2026-11-01", "2026-11-24", null)]
    public void 起止日期正好等于某个快捷区间时它才亮(string start, string end, string? expected)
    {
        var actual = CustomReportPresentation.QuickRangeOf(
            DateTime.Parse(start, CultureInfo.InvariantCulture), DateTime.Parse(end, CultureInfo.InvariantCulture), Today);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void 日期被清空时没有快捷区间亮_带时分秒的日期按日期比较()
    {
        Assert.Null(CustomReportPresentation.QuickRangeOf(null, new DateTime(2026, 11, 25), Today));
        Assert.Null(CustomReportPresentation.QuickRangeOf(new DateTime(2026, 11, 1), null, Today));
        Assert.Equal("this-month", CustomReportPresentation.QuickRangeOf(new DateTime(2026, 11, 1, 13, 0, 0), new DateTime(2026, 11, 25, 8, 0, 0), Today));
    }

    // ───────────────────────── 起止日期的校验与文字 ─────────────────────────

    [Fact]
    public void 合法的起止日期通过校验_同一天也行()
    {
        Assert.Null(CustomReportPresentation.Validate(new DateTime(2026, 11, 1), new DateTime(2026, 11, 25)));
        Assert.Null(CustomReportPresentation.Validate(new DateTime(2026, 11, 25), new DateTime(2026, 11, 25)));
        Assert.Null(CustomReportPresentation.Validate(new DateTime(2026, 11, 25, 23, 59, 0), new DateTime(2026, 11, 25, 0, 0, 0)));
    }

    [Fact]
    public void 没选日期时提示要选哪一个_先提示开始日期()
    {
        Assert.Equal(CustomReportPresentation.StartRequiredMessage, CustomReportPresentation.Validate(null, new DateTime(2026, 11, 25)));
        Assert.Equal(CustomReportPresentation.EndRequiredMessage, CustomReportPresentation.Validate(new DateTime(2026, 11, 1), null));
        Assert.Equal(CustomReportPresentation.StartRequiredMessage, CustomReportPresentation.Validate(null, null));
    }

    [Fact]
    public void 开始日期晚于结束日期的提示沿用原页面的文案()
    {
        Assert.Equal("开始日期不能晚于结束日期", CustomReportPresentation.Validate(new DateTime(2026, 11, 26), new DateTime(2026, 11, 25)));
    }

    [Fact]
    public void 日期超出可选范围被拦下_边界日期可以()
    {
        Assert.Equal(CustomReportPresentation.OutOfRangeMessage, CustomReportPresentation.Validate(new DateTime(1999, 12, 31), new DateTime(2026, 11, 25)));
        Assert.Equal(CustomReportPresentation.OutOfRangeMessage, CustomReportPresentation.Validate(new DateTime(2026, 11, 1), new DateTime(2101, 1, 1)));
        Assert.Null(CustomReportPresentation.Validate(new DateTime(2000, 1, 1), new DateTime(2100, 12, 31)));
        Assert.Equal("日期需在 2000 年到 2100 年之间", CustomReportPresentation.OutOfRangeMessage);
    }

    [Fact]
    public void 可选范围的下限回推一个周期也不会下溢出DateTime最小值()
    {
        // 统计服务按整数天数把「上一等长周期」往前推：最长的区间（整个可选范围）往前推也要还在 DateTime 的范围里
        var (start, end) = (CustomReportPresentation.MinDate, CustomReportPresentation.MaxDate);

        var previous = CustomReportPresentation.PreviousPeriod(start, end);

        Assert.True(previous.Start > DateTime.MinValue);
        Assert.Equal(start.AddDays(-1), previous.End);
    }

    [Fact]
    public void 日期行的文字与输入控件的值()
    {
        Assert.Equal("2026年11月5日", CustomReportPresentation.DateLabel(new DateTime(2026, 11, 5)));
        Assert.Equal("请选择", CustomReportPresentation.DateLabel(null));
        Assert.Equal("2026-03-05", CustomReportPresentation.DateInputValue(new DateTime(2026, 3, 5)));
        Assert.Equal(string.Empty, CustomReportPresentation.DateInputValue(null));
        Assert.Equal("2000-01-01", CustomReportPresentation.MinInputValue);
        Assert.Equal("2100-12-31", CustomReportPresentation.MaxInputValue);
    }

    // ───────────────────────── 结果说明 ─────────────────────────

    [Fact]
    public void 区间文字与说明行带上账本范围和账单数量()
    {
        var start = new DateTime(2026, 11, 1);
        var end = new DateTime(2026, 11, 25);

        Assert.Equal("2026-11-01 ~ 2026-11-25", CustomReportPresentation.RangeText(start, end));
        Assert.Equal("2026-11-01 ~ 2026-11-25 · 全部账本 · 共 12 笔账单", CustomReportPresentation.Caption(start, end, "全部账本", 12));
        Assert.Equal("2026-11-01 ~ 2026-11-25 · 日常开销 · 共 0 笔账单", CustomReportPresentation.Caption(start, end, "日常开销", 0));
    }

    [Theory]
    [InlineData("2026-11-08", "2026-11-14", "2026-11-01", "2026-11-07")]   // 7 天
    [InlineData("2026-11-01", "2026-11-30", "2026-10-02", "2026-10-31")]   // 30 天，不是「上个月」
    [InlineData("2026-11-25", "2026-11-25", "2026-11-24", "2026-11-24")]   // 单日
    [InlineData("2026-01-01", "2026-01-10", "2025-12-22", "2025-12-31")]   // 跨年
    public void 上一等长周期紧邻本期之前_天数相同(string start, string end, string previousStart, string previousEnd)
    {
        var previous = CustomReportPresentation.PreviousPeriod(
            DateTime.Parse(start, CultureInfo.InvariantCulture), DateTime.Parse(end, CultureInfo.InvariantCulture));

        Assert.Equal((DateTime.Parse(previousStart, CultureInfo.InvariantCulture), DateTime.Parse(previousEnd, CultureInfo.InvariantCulture)), previous);
    }

    [Fact]
    public void 环比列表下面的说明写明对比的是哪一段()
    {
        Assert.Equal(
            "对比上一等长周期（2026-11-01 ~ 2026-11-07）",
            CustomReportPresentation.ComparisonFooter(new DateTime(2026, 11, 8), new DateTime(2026, 11, 14)));
    }

    [Fact]
    public void 构成圆环没有数据时的文案区分支出与收入()
    {
        Assert.Equal("所选范围内暂无支出账单", CustomReportPresentation.CompositionEmptyText(FinanceTracker.Core.Enums.BillType.Expense));
        Assert.Equal("所选范围内暂无收入账单", CustomReportPresentation.CompositionEmptyText(FinanceTracker.Core.Enums.BillType.Income));
    }

    [Fact]
    public void 加载与导出失败的提示沿用原页面的文案()
    {
        Assert.Equal("加载报表失败: 数据库忙", CustomReportPresentation.LoadFailedMessage("数据库忙"));
        Assert.Equal("导出失败: 没有权限", CustomReportPresentation.ExportFailedMessage("没有权限"));
        Assert.Equal("报表已导出", CustomReportPresentation.ExportedMessage);
        Assert.Equal("报表摘要已复制到剪贴板", CustomReportPresentation.CopiedMessage);
        Assert.Equal("当前环境不支持分享，可截图保存", CustomReportPresentation.ShareUnsupportedMessage);
        Assert.Equal("custom_report.csv", CustomReportPresentation.CsvFileName);
    }

    [Fact]
    public void 三栏收支汇总的标题配色与结余()
    {
        var items = KpiItem.Summary(300m, 1000m, "x-expense", "x-income", "x-balance");

        Assert.Equal(
            new[] { ("支出", 300m, "expense", "x-expense"), ("收入", 1000m, "income", "x-income"), ("结余", 700m, "balance", "x-balance") },
            items.Select(i => (i.Label, i.Amount, i.Tone, i.TestId)));
        Assert.Equal(-50m, KpiItem.Summary(150m, 100m, "a", "b", "c")[2].Amount);
    }

    // ───────────────────────── 分类环比各行 ─────────────────────────

    [Fact]
    public void 环比_支出变多是坏事_红色向上箭头带加号()
    {
        var row = CustomReportPresentation.BuildComparisonRow(Comparison("餐饮", 500m, 400m));

        Assert.Equal("餐饮", row.Name);
        Assert.Equal("+25.0%", row.DeltaText);
        Assert.Equal(CustomReportPresentation.DeltaUp, row.DeltaKind);
        Assert.Equal("bad", row.DeltaTone);
        Assert.Equal("mdi-arrow-up", row.DeltaIcon);
        Assert.Equal("¥500.00（上期 ¥400.00）", row.Subtitle);
        Assert.Equal(("500.00", "400.00"), (row.CurrentPlain, row.PreviousPlain));
    }

    [Fact]
    public void 环比_支出变少是好事_绿色向下箭头带负号()
    {
        var row = CustomReportPresentation.BuildComparisonRow(Comparison("购物", 90m, 100m));

        Assert.Equal("-10.0%", row.DeltaText);
        Assert.Equal(CustomReportPresentation.DeltaDown, row.DeltaKind);
        Assert.Equal("good", row.DeltaTone);
        Assert.Equal("mdi-arrow-down", row.DeltaIcon);
    }

    [Fact]
    public void 环比_上期没有支出的分类是新增_算不出变化率_按坏事处理()
    {
        var row = CustomReportPresentation.BuildComparisonRow(Comparison("旅行", 80m, 0m));

        Assert.Equal("新增", row.DeltaText);
        Assert.Equal(CustomReportPresentation.DeltaNew, row.DeltaKind);
        Assert.Equal("bad", row.DeltaTone);
        Assert.Equal("mdi-arrow-up", row.DeltaIcon);
        Assert.Equal("¥80.00（上期 ¥0.00）", row.Subtitle);
    }

    [Fact]
    public void 环比_本期归零的分类是负百分百_仍是好事()
    {
        var row = CustomReportPresentation.BuildComparisonRow(Comparison("医疗", 0m, 50m));

        Assert.Equal("-100.0%", row.DeltaText);
        Assert.Equal(CustomReportPresentation.DeltaDown, row.DeltaKind);
        Assert.Equal("good", row.DeltaTone);
        Assert.Equal("医疗", row.Name);
    }

    [Fact]
    public void 环比_变化小到一位小数是零时是持平_中性色且不带符号()
    {
        // 1000.04 / 1000 - 1 = 0.004%，一位小数后是 0.0%：不能给一个「红色向上 +0.0%」
        var row = CustomReportPresentation.BuildComparisonRow(Comparison("房租", 1000.04m, 1000m));

        Assert.Equal("0.0%", row.DeltaText);
        Assert.Equal(CustomReportPresentation.DeltaFlat, row.DeltaKind);
        Assert.Equal("flat", row.DeltaTone);
        Assert.Equal("mdi-minus", row.DeltaIcon);
    }

    [Fact]
    public void 环比_两期都没有金额时显示破折号_不会冒出一个除零的变化率()
    {
        var row = CustomReportPresentation.BuildComparisonRow(Comparison("其他", 0m, 0m));

        Assert.Equal("—", row.DeltaText);
        Assert.Equal(CustomReportPresentation.DeltaFlat, row.DeltaKind);
        Assert.Equal("flat", row.DeltaTone);
    }

    [Fact]
    public void 环比_金额带千分位_分类名空白时显示未知分类_图标缺失时回退()
    {
        var row = CustomReportPresentation.BuildComparisonRow(Comparison("  ", 1234.5m, 1000m, icon: ""));

        Assert.Equal("未知分类", row.Name);
        Assert.Equal("¥1,234.50（上期 ¥1,000.00）", row.Subtitle);
        Assert.StartsWith("mdi-", row.Icon);
        Assert.False(string.IsNullOrWhiteSpace(row.IconTone));
    }

    [Fact]
    public void 同一个分类的图标块配色稳定()
    {
        var data = Comparison("自建分类", 10m, 5m);

        Assert.Equal(
            CustomReportPresentation.BuildComparisonRow(data).IconTone,
            CustomReportPresentation.BuildComparisonRow(data).IconTone);
    }

    [Fact]
    public void 环比各行保持统计服务给的顺序()
    {
        var rows = CustomReportPresentation.BuildComparisonRows(
        [
            Comparison("甲", 300m, 100m),
            Comparison("乙", 10m, 50m),
            Comparison("丙", 5m, 5m),
        ]);

        Assert.Equal(new[] { "甲", "乙", "丙" }, rows.Select(r => r.Name));
        Assert.Empty(CustomReportPresentation.BuildComparisonRows([]));
    }

    // ───────────────────────── 导出 CSV ─────────────────────────

    [Fact]
    public void CSV_先是汇总再空一行再是分类明细_换行固定CRLF()
    {
        var stats = Sample(Category("餐饮", 1000m, 8, 81m), Category("交通", 234.5m, 4, 19m));

        var csv = CustomReportPresentation.BuildCsv(stats);

        Assert.Equal(
            "自定义报表 2026-11-01 ~ 2026-11-25\r\n" +
            "总支出,1234.50\r\n" +
            "总收入,5000.00\r\n" +
            "净收支,3765.50\r\n" +
            "账单数量,12\r\n" +
            "\r\n" +
            "分类,金额,笔数,占比(%)\r\n" +
            "餐饮,1000.00,8,81.00\r\n" +
            "交通,234.50,4,19.00\r\n",
            csv);
    }

    [Fact]
    public void CSV_金额不带千分位_否则一个金额会被逗号劈成两列()
    {
        var stats = Sample(Category("餐饮", 1234.5m, 8, 100m));

        var csv = CustomReportPresentation.BuildCsv(stats);

        Assert.DoesNotContain("1,234", csv);
        Assert.Contains("总支出,1234.50\r\n", csv);
        Assert.Contains("餐饮,1234.50,8,100.00\r\n", csv);
    }

    [Fact]
    public void CSV_选了具体账本才多一行账本名()
    {
        var stats = Sample();

        var all = CustomReportPresentation.BuildCsv(stats).Split("\r\n");
        var one = CustomReportPresentation.BuildCsv(stats, "  工作  ").Split("\r\n");

        Assert.DoesNotContain(all, line => line.StartsWith("账本,", StringComparison.Ordinal));
        Assert.Equal("账本,工作", one[1]);
        Assert.Equal("总支出,1234.50", one[2]);
        Assert.DoesNotContain(CustomReportPresentation.BuildCsv(stats, "").Split("\r\n"), line => line.StartsWith("账本,", StringComparison.Ordinal));
    }

    [Fact]
    public void CSV_分类名含逗号引号换行时按RFC4180加引号转义()
    {
        var stats = Sample(Category("a,b\"c", 10m, 1, 50m), Category("两行\n名称", 10m, 1, 50m));

        var csv = CustomReportPresentation.BuildCsv(stats);

        Assert.Contains("\"a,b\"\"c\",10.00,1,50.00\r\n", csv);
        Assert.Contains("\"两行\n名称\",10.00,1,50.00\r\n", csv);
    }

    [Fact]
    public void CSV_没有支出分类时只有表头()
    {
        var csv = CustomReportPresentation.BuildCsv(Sample());

        Assert.EndsWith("\r\n分类,金额,笔数,占比(%)\r\n", csv);
    }

    [Fact]
    public void CSV_金额与占比不随设备区域设置变化()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var csv = CustomReportPresentation.BuildCsv(Sample(Category("餐饮", 1234.5m, 8, 12.5m)));

            Assert.Contains("总支出,1234.50\r\n", csv);
            Assert.Contains("餐饮,1234.50,8,12.50\r\n", csv);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("餐饮", "餐饮")]
    [InlineData(" 前后有空格 ", " 前后有空格 ")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("第一行\n第二行", "\"第一行\n第二行\"")]
    [InlineData("第一行\r第二行", "\"第一行\r第二行\"")]
    public void CSV字段的转义(string? value, string expected)
    {
        Assert.Equal(expected, CustomReportPresentation.CsvField(value));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "'=HYPERLINK(\"http://x\")")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\t=1", "'\t=1")]
    [InlineData("\r=1", "'\r=1")]
    [InlineData("餐饮", "餐饮")]
    [InlineData("a=b", "a=b")]
    [InlineData("1+1", "1+1")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void 以公式符号开头的文字补单引号_表格软件不会当公式执行(string? text, string expected)
    {
        Assert.Equal(expected, CustomReportPresentation.NeutralizeFormula(text));
    }

    [Fact]
    public void CSV_分类名与账本名以公式符号开头时补单引号_负数金额不受影响()
    {
        var stats = Sample(Category("=1+1", 10m, 1, 50m), Category("-负号开头", 10m, 1, 50m), Category("@cmd", 10m, 1, 0m));
        stats.TotalExpense = 5000m;
        stats.TotalIncome = 1000m;

        var csv = CustomReportPresentation.BuildCsv(stats, "=工作账本");

        Assert.Contains("账本,'=工作账本\r\n", csv);
        Assert.Contains("'=1+1,10.00,1,50.00\r\n", csv);
        Assert.Contains("'-负号开头,10.00,1,50.00\r\n", csv);
        Assert.Contains("'@cmd,10.00,1,0.00\r\n", csv);

        // 金额是数字不是文字：负数的「-」保持原样，否则表格软件里会变成文本
        Assert.Contains("净收支,-4000.00\r\n", csv);
    }

    // ───────────────────────── 分享摘要 ─────────────────────────

    private static CategoryComparisonData[] SampleComparisons() =>
    [
        Comparison("餐饮", 500m, 400m),   // +100（+25.0%）
        Comparison("购物", 90m, 150m),    // -60（-40.0%）
        Comparison("旅行", 30m, 0m),      // +30（新增）
    ];

    [Fact]
    public void 分享摘要_总览_Top3_环比增长最多与减少最多()
    {
        var stats = Sample(
            Category("餐饮", 1000m, 8, 81m),
            Category("交通", 200m, 3, 16m),
            Category("娱乐", 34.5m, 1, 3m),
            Category("其他", 1m, 1, 0m));

        var text = CustomReportPresentation.BuildShareText(stats, SampleComparisons());

        Assert.Equal(
            "【记账报表】2026-11-01 ~ 2026-11-25\n" +
            "总支出 ¥1,234.50，总收入 ¥5,000.00，净收支 ¥3,765.50（共 12 笔账单）\n" +
            "支出 Top3：餐饮 ¥1,000.00、交通 ¥200.00、娱乐 ¥34.50\n" +
            "环比增长最多：餐饮（+¥100.00，+25.0%）\n" +
            "环比减少最多：购物（-¥60.00，-40.0%）",
            text);
    }

    [Fact]
    public void 分享摘要_选了账本时标题带账本名()
    {
        var text = CustomReportPresentation.BuildShareText(Sample(), [], "  工作  ");

        Assert.StartsWith("【记账报表】2026-11-01 ~ 2026-11-25 · 账本：工作\n", text);
    }

    [Fact]
    public void 分享摘要_没有支出分类也没有环比时只有标题与总览()
    {
        var text = CustomReportPresentation.BuildShareText(Sample(), []);

        Assert.Equal(2, text.Split('\n').Length);
        Assert.DoesNotContain("Top3", text);
        Assert.DoesNotContain("环比", text);
    }

    [Fact]
    public void 分享摘要_增长最多的是新增分类时不写变化率()
    {
        var text = CustomReportPresentation.BuildShareText(Sample(), [Comparison("旅行", 80m, 0m), Comparison("餐饮", 10m, 10m)]);

        Assert.Contains("环比增长最多：旅行（+¥80.00）", text);
        Assert.DoesNotContain("环比减少最多", text);
    }

    [Fact]
    public void 分享摘要_净收支为负时用负号在前的写法()
    {
        var stats = Sample();
        stats.TotalExpense = 300m;
        stats.TotalIncome = 200m;

        var text = CustomReportPresentation.BuildShareText(stats, []);

        Assert.Contains("净收支 -¥100.00", text);
        Assert.DoesNotContain("¥-", text);
    }

    // ───────────────────────── 术语 ─────────────────────────

    [Fact]
    public void 界面文案遵守领域术语_不出现被禁用的词()
    {
        var texts = new[]
        {
            CustomReportPresentation.Title, CustomReportPresentation.QuickRangeTitle, CustomReportPresentation.RangeGroupTitle,
            CustomReportPresentation.StartDateTitle, CustomReportPresentation.EndDateTitle, CustomReportPresentation.LedgerTitle,
            CustomReportPresentation.GenerateLabel, CustomReportPresentation.IdleTitle, CustomReportPresentation.FailedTitle,
            CustomReportPresentation.FailedText, CustomReportPresentation.ComparisonTitle, CustomReportPresentation.ComparisonEmptyText,
            CustomReportPresentation.RankTitle, CustomReportPresentation.RankEmptyText, CustomReportPresentation.ActionsTitle,
            CustomReportPresentation.ExportTitle, CustomReportPresentation.ExportSubtitle, CustomReportPresentation.ShareRowTitle,
            CustomReportPresentation.ShareSubtitle, CustomReportPresentation.StartRequiredMessage, CustomReportPresentation.EndRequiredMessage,
            CustomReportPresentation.RangeOrderMessage, CustomReportPresentation.OutOfRangeMessage,
            CustomReportPresentation.CompositionEmptyText(FinanceTracker.Core.Enums.BillType.Expense),
            CustomReportPresentation.CompositionEmptyText(FinanceTracker.Core.Enums.BillType.Income),
        };

        foreach (var text in texts)
        {
            foreach (var banned in new[] { "交易", "记录", "流水", "标签", "类型", "支付方式", "付款方式", "账簿", "笔记本", "录入方式" })
            {
                Assert.DoesNotContain(banned, text);
            }
        }
    }
}
