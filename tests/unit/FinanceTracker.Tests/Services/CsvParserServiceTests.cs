using FinanceTracker.Core.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 账单 CSV 解析：微信 / 支付宝导出文件。
/// 支付宝的各列按<b>表头名称</b>定位（新旧版导出的列数与顺序不同）——样例都是按支付宝官方导出文件的真实格式写的
/// （前面一大段导出说明、字段末尾带制表符、行尾有多余的逗号），不是按解析器内部的列位置倒推出来的。
/// </summary>
public class CsvParserServiceTests
{
    private readonly CsvParserService _parser = new();

    /// <summary>新版支付宝导出：12 列（交易时间 … 备注），3 笔有效 + 不计收支 / 交易关闭 / 坏时间各一笔</summary>
    private const string RealAlipayExport =
        "------------------------------------------------------------------------------------\r\n" +
        "导出信息：\r\n" +
        "姓名：测试\r\n" +
        "支付宝账户：test@example.com\r\n" +
        "起始时间：[2024-05-01 00:00:00]    终止时间：[2024-05-31 23:59:59]\r\n" +
        "导出交易类型：[全部]\r\n" +
        "导出时间：[2024-06-01 10:00:00]\r\n" +
        "\r\n" +
        "共6笔记录\r\n" +
        "收入：1笔，共5000.00元\r\n" +
        "支出：3笔，共1545.50元\r\n" +
        "不计收支：2笔，共2000.00元\r\n" +
        "\r\n" +
        "特别提示：\r\n" +
        "1.本明细仅展示当前账单中的交易，不包括已删除的记录；\r\n" +
        "2.本明细仅供个人对账使用；\r\n" +
        "------------------------支付宝（中国）网络技术有限公司  电子客户回单------------------------\r\n" +
        "交易时间,交易分类,交易对方,对方账号,商品说明,收/支,金额,收/付款方式,交易状态,交易订单号,商家订单号,备注,\r\n" +
        "2024-05-01 08:40:44,餐饮美食,某早餐店,abc***@qq.com,早餐,支出,12.50,余额宝,交易成功,2024050122001400001\t,M001\t,,\r\n" +
        "2024-05-02 09:00:00,收入,某科技公司,hr***@corp.com,五月工资,收入,5000.00,账户余额,交易成功,2024050222001400002\t,,,\r\n" +
        "2024-05-03 12:00:00,信用卡还款,招商银行,,信用卡还款,不计收支,2000.00,余额,还款成功,2024050322001400003\t,,,\r\n" +
        "2024-05-04 13:00:00,日用百货,某超市,xyz***@qq.com,/,支出,\"1,234.00\",花呗,交易成功,2024050422001400004\t,M004\t,,\r\n" +
        "2024-05-05 14:00:00,数码电器,某商城,shop***@mall.com,耳机,支出,299.00,花呗,交易关闭,2024050522001400005\t,M005\t,,\r\n" +
        "坏时间,餐饮美食,某店,,午餐,支出,20.00,余额,交易成功,2024050622001400006\t,,,\r\n";

    private const string RealAlipayHeader =
        "交易时间,交易分类,交易对方,对方账号,商品说明,收/支,金额,收/付款方式,交易状态,交易订单号,商家订单号,备注,\r\n";

    // ───────────────────────── 支付宝：真实导出格式 ─────────────────────────

    [Fact]
    public async Task 支付宝_真实导出格式_商户取交易对方_描述取商品说明_订单号取交易订单号()
    {
        var bills = await _parser.ParseAlipayCsvAsync(RealAlipayExport);

        var first = bills[0];
        Assert.Equal(new DateTime(2024, 5, 1, 8, 40, 44), first.TransactionTime);
        Assert.Equal("早餐", first.Description);            // 商品说明
        Assert.Equal("某早餐店", first.MerchantName);        // 交易对方（不是「收/支」列的「支出」）
        Assert.Equal(12.50m, first.Amount);
        Assert.False(first.IsIncome);
        Assert.Equal("支付宝", first.PaymentChannel);
        Assert.Equal("2024050122001400001", first.TransactionId); // 交易订单号（不是「交易状态」列的「交易成功」），末尾的制表符被去掉
    }

    [Fact]
    public async Task 支付宝_收入看收支列_不看金额符号_也不看对方账号()
    {
        var bills = await _parser.ParseAlipayCsvAsync(RealAlipayExport);

        // 真实导出里金额没有正负号，「对方账号」列也不会写「收入」：旧实现按位置取第 3 列判断，收入会被当成支出
        var income = bills[1];
        Assert.True(income.IsIncome);
        Assert.Equal(5000.00m, income.Amount);
        Assert.Equal("某科技公司", income.MerchantName);
        Assert.Equal("五月工资", income.Description);
        Assert.Equal("2024050222001400002", income.TransactionId);
    }

    [Fact]
    public async Task 支付宝_不计收支_交易关闭_坏行都不计入()
    {
        var bills = await _parser.ParseAlipayCsvAsync(RealAlipayExport);

        // 6 行里：2 笔正常 + 千分位那笔 = 3 笔有效；信用卡还款（不计收支）、耳机（交易关闭）、坏时间各丢掉一笔
        Assert.Equal(3, bills.Count);
        Assert.DoesNotContain(bills, b => b.Amount == 2000.00m);
        Assert.DoesNotContain(bills, b => b.Amount == 299.00m);
        Assert.DoesNotContain(bills, b => b.Amount == 20.00m);
    }

    [Fact]
    public async Task 支付宝_商品说明是占位符_描述回退到交易分类_带千分位的金额照常解析()
    {
        var bills = await _parser.ParseAlipayCsvAsync(RealAlipayExport);

        var market = bills[2];
        Assert.Equal(1234.00m, market.Amount);
        Assert.Equal("日用百货", market.Description);
        Assert.Equal("某超市", market.MerchantName);
        Assert.False(market.IsIncome);
    }

    [Fact]
    public async Task 支付宝_交易对方是占位符_商户回退到交易分类_都没有时是支付宝交易()
    {
        var csv = RealAlipayHeader +
                  "2024-05-01 10:00:00,餐饮美食,/,,/,支出,8.00,余额,交易成功,A1\t,,,\r\n" +
                  "2024-05-01 11:00:00,,,,,支出,9.00,余额,交易成功,A2\t,,,\r\n";

        var bills = await _parser.ParseAlipayCsvAsync(csv);

        Assert.Equal(2, bills.Count);
        Assert.Equal(("餐饮美食", "餐饮美食"), (bills[0].Description, bills[0].MerchantName));
        Assert.Equal(("支付宝交易", "支付宝交易"), (bills[1].Description, bills[1].MerchantName));
    }

    [Fact]
    public async Task 支付宝_金额为0的行不计入()
    {
        var csv = RealAlipayHeader +
                  "2024-05-01 10:00:00,餐饮美食,某店,,零元,支出,0.00,余额,交易成功,A1\t,,,\r\n" +
                  "2024-05-01 11:00:00,餐饮美食,某店,,一元,支出,1.00,余额,交易成功,A2\t,,,\r\n";

        var bills = await _parser.ParseAlipayCsvAsync(csv);

        Assert.Equal(new[] { "一元" }, bills.Select(b => b.Description));
    }

    // ───────────────────────── 支付宝：按表头名称定位 ─────────────────────────

    [Fact]
    public async Task 支付宝_列的顺序变了也按表头名称取列()
    {
        // 倒过来排、中间夹一个不认识的列：按位置取就全错位了
        var csv =
            "备注,交易订单号,交易状态,金额,未知列,收/支,商品说明,交易对方,交易时间\r\n" +
            ",ORDER-1\t,交易成功,45.60,x,收入,红包,朋友,2024-06-01 20:00:00\r\n" +
            ",ORDER-2\t,交易成功,15.00,x,支出,奶茶,某茶饮店,2024-06-02 15:30:00\r\n";

        var bills = await _parser.ParseAlipayCsvAsync(csv);

        Assert.Equal(2, bills.Count);
        Assert.Equal((new DateTime(2024, 6, 1, 20, 0, 0), "红包", "朋友", 45.60m, true, "ORDER-1"),
            (bills[0].TransactionTime, bills[0].Description, bills[0].MerchantName, bills[0].Amount, bills[0].IsIncome, bills[0].TransactionId));
        Assert.Equal((new DateTime(2024, 6, 2, 15, 30, 0), "奶茶", "某茶饮店", 15.00m, false, "ORDER-2"),
            (bills[1].TransactionTime, bills[1].Description, bills[1].MerchantName, bills[1].Amount, bills[1].IsIncome, bills[1].TransactionId));
    }

    [Fact]
    public async Task 支付宝_旧版导出格式_交易创建时间_商品名称_金额带括号_交易号()
    {
        var csv =
            "支付宝交易记录明细查询\r\n账号:[test@example.com]\r\n" +
            "交易号,商家订单号,交易创建时间,付款时间,最近修改时间,交易来源地,类型,交易对方,商品名称,金额（元）,收/支,交易状态,服务费（元）,成功退款（元）,备注,资金状态\r\n" +
            "2019010122001400001\t,M1\t,2019-01-01 10:00:00,2019-01-01 10:00:05,2019-01-01 10:00:05,其他（包括阿里巴巴和外部商家）,即时到账交易,某商家,某商品,25.00,支出,交易成功,0.00,0.00,,已支付\r\n" +
            "2019010222001400002\t,M2\t,2019-01-02 09:00:00,2019-01-02 09:00:03,2019-01-02 09:00:03,其他（包括阿里巴巴和外部商家）,即时到账交易,某买家,某商品,60.00,收入,交易成功,0.00,0.00,,已收入\r\n" +
            "2019010322001400003\t,M3\t,2019-01-03 08:00:00,,2019-01-03 08:30:00,其他（包括阿里巴巴和外部商家）,即时到账交易,某商家,某商品,10.00,支出,交易关闭,0.00,0.00,,\r\n";

        var bills = await _parser.ParseAlipayCsvAsync(csv);

        Assert.Equal(2, bills.Count);   // 交易关闭的那笔不计入
        Assert.Equal((new DateTime(2019, 1, 1, 10, 0, 0), "某商品", "某商家", 25.00m, false, "2019010122001400001"),
            (bills[0].TransactionTime, bills[0].Description, bills[0].MerchantName, bills[0].Amount, bills[0].IsIncome, bills[0].TransactionId));
        Assert.True(bills[1].IsIncome);
        Assert.Equal(60.00m, bills[1].Amount);
    }

    [Fact]
    public async Task 支付宝_表头前带BOM也能定位到列()
    {
        var csv = "\uFEFF" + RealAlipayHeader +
                  "2024-05-01 08:40:44,餐饮美食,某早餐店,,早餐,支出,12.50,余额宝,交易成功,A1\t,,,\r\n";

        var bills = await _parser.ParseAlipayCsvAsync(csv);

        var bill = Assert.Single(bills);
        Assert.Equal(("某早餐店", 12.50m), (bill.MerchantName, bill.Amount));
    }

    [Fact]
    public async Task 支付宝_交易状态列缺失也能解析_只是没法按状态过滤()
    {
        var csv = "交易时间,交易对方,商品说明,收/支,金额\r\n" +
                  "2024-05-01 08:40:44,某早餐店,早餐,支出,12.50\r\n";

        var bills = await _parser.ParseAlipayCsvAsync(csv);

        var bill = Assert.Single(bills);
        Assert.Equal(("某早餐店", "早餐", 12.50m, string.Empty), (bill.MerchantName, bill.Description, bill.Amount, bill.TransactionId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n  ")]
    [InlineData("这不是账单文件\r\n只有说明文字")]
    [InlineData("交易时间,交易对方,商品说明,收/支\r\n2024-05-01 08:40:44,某店,早餐,支出\r\n")]            // 没有「金额」列：连表头都认不出
    [InlineData("交易时间,交易对方,商品说明,金额\r\n2024-05-01 08:40:44,某店,早餐,12.50\r\n")]            // 没有「收/支」列：不知道收入还是支出，整体不解析
    [InlineData("交易对方,商品说明,收/支,金额\r\n某店,早餐,支出,12.50\r\n")]                             // 没有「交易时间」列
    public async Task 支付宝_认不出表头或缺必需的列_解析不出任何账单(string csv)
    {
        Assert.Empty(await _parser.ParseAlipayCsvAsync(csv));
    }

    [Fact]
    public async Task 支付宝_把微信账单当支付宝解析_不解析出任何账单()
    {
        // 微信与支付宝有好几列同名（交易时间 / 交易对方 / 收/支 / 金额(元)）：选错来源时不能按列名对上号、悄悄导成「支付宝」渠道的账单，
        // 要明确解析失败，让用户换成微信来源（表头里有微信才有的「当前状态」列）
        Assert.Empty(await _parser.ParseAlipayCsvAsync(WeChatSample));
    }

    [Fact]
    public async Task 支付宝_数据行字段不够的行被丢弃_其余照常()
    {
        var csv = RealAlipayHeader +
                  "2024-05-01 08:40:44,餐饮美食,某早餐店\r\n" +
                  "2024-05-01 09:40:44,餐饮美食,某午餐店,,午餐,支出,20.00,余额,交易成功,A2\t,,,\r\n";

        var bills = await _parser.ParseAlipayCsvAsync(csv);

        Assert.Equal(new[] { "午餐" }, bills.Select(b => b.Description));
    }

    // ───────────────────────── 微信：共用的辅助方法改名后行为不变 ─────────────────────────

    private const string WeChatSample =
        "微信支付账单明细\r\n微信昵称：[测试]\r\n----------------------微信支付账单明细列表--------------------\r\n" +
        "交易时间,交易类型,交易对方,商品,收/支,金额(元),支付方式,当前状态,交易单号,商户单号,备注\r\n" +
        "2024/5/1 12:30,商户消费,美团外卖,午餐外卖,支出,¥38.50,零钱,支付成功,4200001,M1,\"/\"\r\n" +
        "2024/5/2 9:05,转账,小王,转账备注,收入,\"¥1,200.00\",中国银行储蓄卡(1234),已收钱,4200002,/,/\r\n" +
        "2024/5/3 18:00,微信红包,/,/,收入,¥8.80,/,已存入零钱,4200003,/,/\r\n" +
        "2024/5/4 10:00,零钱充值,/,/,/,¥100.00,招商银行信用卡(5678),充值完成,4200004,/,/\r\n" +
        "2024/5/5 11:00,商户消费,某商家,退款商品,支出,¥20.00,零钱,已全额退款,4200005,/,/\r\n" +
        "2024/5/6 11:00,商户消费,某商家,零元商品,支出,¥0.00,零钱,支付成功,4200006,/,/\r\n";

    [Fact]
    public async Task 微信_有效三笔_中性交易_全额退款_零金额三笔计入跳过()
    {
        var result = await _parser.ParseWeChatCsvWithStatsAsync(WeChatSample);

        Assert.Equal(3, result.Bills.Count);
        Assert.Equal(3, result.SkippedCount);

        var (first, second, third) = (result.Bills[0], result.Bills[1], result.Bills[2]);
        Assert.Equal((new DateTime(2024, 5, 1, 12, 30, 0), "午餐外卖", "美团外卖", 38.50m, false, "微信支付", "4200001"),
            (first.TransactionTime, first.Description, first.MerchantName, first.Amount, first.IsIncome, first.PaymentChannel, first.TransactionId));
        Assert.Equal((1200.00m, true, "银行卡", new DateTime(2024, 5, 2, 9, 5, 0)),
            (second.Amount, second.IsIncome, second.PaymentChannel, second.TransactionTime));
        // 商品 / 备注都是占位符时，描述与商户都回退到交易类型
        Assert.Equal(("微信红包", "微信红包", "微信支付"), (third.Description, third.MerchantName, third.PaymentChannel));
    }
}
