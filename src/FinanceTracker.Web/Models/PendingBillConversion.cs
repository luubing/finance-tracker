using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 把自动识别到的支付（待确认账单）确认成正式账单的规则（纯函数，单元测试覆盖）：
/// 选哪个分类、匹配哪个支付渠道、账单各字段怎么填。
/// 短信识别页读到的短信也是先放进待确认账单（<see cref="SmsCapture"/>），再走这里确认入账——页面里不再有第二套转换规则
/// （过去短信识别页自己写了一套，给「收入」账单选分类时还会取到名叫「其他支出」的支出分类）。
/// </summary>
public static class PendingBillConversion
{
    /// <summary>自动入账时选分类的关键字：识别结果里没有分类，用该类型下的「其他…」分类兜底</summary>
    public const string OtherCategoryKeyword = "其他";

    /// <summary>
    /// 选出入账用的分类：该账单类型下优先取预设的「其他支出」/「其他收入」，其次名称含「其他」的第一个（用户自建的「其他…」分类），
    /// 都没有就取该类型的第一个；该类型一个分类都没有返回 null。
    /// 分类一定与账单类型同类：收入账单不会取到支出分类。
    /// </summary>
    /// <param name="categories">用户可用的分类（预设 + 自定义，保持分类管理里的顺序）</param>
    /// <param name="type">账单类型</param>
    public static Category? PickCategory(IEnumerable<Category> categories, BillType type)
    {
        var sameType = categories.Where(c => c.Type == type).ToList();
        var preset = type == BillType.Income ? "其他收入" : "其他支出";

        return sameType.FirstOrDefault(c => string.Equals(c.Name, preset, StringComparison.Ordinal))
               ?? sameType.FirstOrDefault(c => c.Name.Contains(OtherCategoryKeyword, StringComparison.Ordinal))
               ?? sameType.FirstOrDefault();
    }

    /// <summary>
    /// 把识别出的支付渠道名（微信支付 / 支付宝 / 京东支付 / 美团支付 / 云闪付 / 银行卡）匹配到用户的支付渠道：
    /// 名称完全一致优先，其次名称包含它；渠道名为空或匹配不到返回 null（由用户在「确认账单」里选）。
    /// </summary>
    /// <param name="channels">用户可用的支付渠道</param>
    /// <param name="name">识别出的渠道名</param>
    public static PaymentChannel? MatchChannel(IEnumerable<PaymentChannel> channels, string? name)
    {
        var wanted = name?.Trim();
        if (string.IsNullOrEmpty(wanted))
        {
            return null;
        }

        var all = channels.ToList();

        return all.FirstOrDefault(c => string.Equals(c.Name, wanted, StringComparison.Ordinal))
               ?? all.FirstOrDefault(c => c.Name.Contains(wanted, StringComparison.Ordinal));
    }

    /// <summary>
    /// 组装要写入的账单。交易时间、备注沿用识别结果，数据来源按识别来源标记（通知栏 / 短信识别），同步状态为待同步；
    /// 交易时间是应用时区的墙上时间，由 <c>BillService</c> 经 <c>AppTimeZone</c> 换成 UTC 入库，这里不换算。
    /// </summary>
    public static Bill NewBill(
        Guid userId,
        decimal amount,
        BillType type,
        Guid categoryId,
        Guid paymentChannelId,
        Guid ledgerId,
        DateTime transactionTime,
        string? note,
        BillSource source) => new()
        {
            UserId = userId,
            Amount = amount,
            Type = type,
            CategoryId = categoryId,
            PaymentChannelId = paymentChannelId,
            LedgerId = ledgerId,
            TransactionTime = transactionTime,
            Note = string.IsNullOrWhiteSpace(note) ? null : note,
            Source = source,
            SyncStatus = SyncStatus.Pending,
        };
}
