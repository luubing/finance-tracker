using System.Linq.Expressions;
using FinanceTracker.Core.Entities;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 待确认账单的去重规则：同一条支付可能同时触发短信 + 通知（如银行扣款短信 + 微信通知）、系统对同一通知多次回调、
/// 短信识别页重复读取同一批短信——按「来源 + 金额 + 交易时间 ±1 分钟」判重。
/// 规则只定义在这里：<c>PendingBillService.AddAsync</c> 用它查数据库（原子判重 + 写入），
/// 短信识别页在列表已满、不能再调 <c>AddAsync</c>（它会丢弃最早的一条）时用它在内存里判断「是不是已经在列表里」。
/// </summary>
public static class PendingBillDeduplication
{
    /// <summary>交易时间相差不超过这个时长（含）就算同一笔</summary>
    public static readonly TimeSpan TimeWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 「与 <paramref name="candidate"/> 是同一笔」的条件，能直接交给 EF Core 翻译成查询，也能 <c>Compile()</c> 后在内存里用。
    /// </summary>
    public static Expression<Func<PendingBill, bool>> IsDuplicateOf(PendingBill candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        // 先取出局部量：表达式里只引用这几个值，EF Core 会把它们当作查询参数，而不是去翻译整个 candidate
        var source = candidate.Source;
        var amount = candidate.Amount;
        var earliest = candidate.TransactionTime - TimeWindow;
        var latest = candidate.TransactionTime + TimeWindow;

        return b => b.Source == source
                    && b.Amount == amount
                    && b.TransactionTime >= earliest
                    && b.TransactionTime <= latest;
    }

    /// <summary>
    /// <paramref name="existing"/> 里是否已经有与 <paramref name="candidate"/> 同一笔的待确认账单（内存判断）
    /// </summary>
    public static bool ContainsDuplicate(IEnumerable<PendingBill> existing, PendingBill candidate)
    {
        ArgumentNullException.ThrowIfNull(existing);

        return existing.Any(IsDuplicateOf(candidate).Compile());
    }
}
