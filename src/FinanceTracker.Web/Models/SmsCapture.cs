using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;
using Microsoft.Extensions.Logging;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 短信识别页读到的一条短信放进「待确认账单」之后的结果
/// </summary>
public enum SmsCaptureOutcome
{
    /// <summary>新加入了待确认账单</summary>
    Added,

    /// <summary>待确认列表里已经有同一笔（按来源 + 金额 + 交易时间 ±1 分钟去重，规则见 <see cref="PendingBillDeduplication"/>；有空位时由 <see cref="IPendingBillService.AddAsync"/> 判定，列表已满时在内存里判定）</summary>
    Duplicate,

    /// <summary>没识别出金额：不进待确认列表（与实时短信监听一致——验证码、营销短信不会混进来）</summary>
    NoAmount,

    /// <summary>待确认账单已满（<see cref="IPendingBillService.MaxCount"/>），没有加入——免得把用户还没处理的待确认账单顶掉</summary>
    OverCapacity,

    /// <summary>写入待确认账单时出错</summary>
    Failed,
}

/// <summary>
/// 一条短信与它的去向
/// </summary>
/// <param name="Sms">短信</param>
/// <param name="Outcome">去向</param>
public sealed record SmsCaptureItem(SmsMessage Sms, SmsCaptureOutcome Outcome);

/// <summary>
/// 一次读取的结果：每条短信的去向（与读取到的顺序一致）与各类的条数
/// </summary>
public sealed class SmsCaptureResult
{
    /// <summary>每条短信与它的去向，顺序与传入的短信一致</summary>
    public IReadOnlyList<SmsCaptureItem> Items { get; }

    public SmsCaptureResult(IReadOnlyList<SmsCaptureItem> items)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
    }

    /// <summary>某种去向的条数</summary>
    public int Count(SmsCaptureOutcome outcome) => Items.Count(i => i.Outcome == outcome);

    /// <summary>新加入待确认账单的条数</summary>
    public int Added => Count(SmsCaptureOutcome.Added);

    /// <summary>待确认列表里已有、没有重复加入的条数</summary>
    public int Duplicate => Count(SmsCaptureOutcome.Duplicate);

    /// <summary>没识别出金额、已跳过的条数</summary>
    public int NoAmount => Count(SmsCaptureOutcome.NoAmount);

    /// <summary>待确认账单已满、没加入的条数</summary>
    public int OverCapacity => Count(SmsCaptureOutcome.OverCapacity);

    /// <summary>写入出错的条数</summary>
    public int Failed => Count(SmsCaptureOutcome.Failed);
}

/// <summary>
/// 短信识别页的「读取结果 → 待确认账单」规则（单元测试覆盖）。
/// 读到的支付短信不再直接创建正式账单：识别出金额的都放进待确认账单（与实时短信监听 <c>SmsBroadcastReceiver</c> 同一份解析规则、
/// 同一个服务、同样的去重），到「待确认账单」页再确认入账（金额 / 类型 / 支付渠道可修正，账本在那里选）。
/// </summary>
public static class SmsCapture
{
    /// <summary>
    /// 一条短信 → 待确认账单。金额、类型、渠道取解析结果；备注是短信原文；交易时间是短信时间（设备本地墙上时间，不换算，ADR 0005）；
    /// 数据来源是短信识别。捕获时间也取短信时间：批量读取的是历史短信，待确认列表按捕获时间倒序，这样列表仍按「付款发生的先后」排，
    /// 与实时捕获（捕获时间 ≈ 付款时间）的条目自然穿插；列表满了需要腾位置时，最早的付款最先让位。
    /// </summary>
    public static PendingBill ToPendingBill(SmsMessage sms, PaymentContentParser.PaymentParseResult parsed)
    {
        ArgumentNullException.ThrowIfNull(sms);
        ArgumentNullException.ThrowIfNull(parsed);

        return new PendingBill
        {
            Amount = parsed.Amount,
            Type = parsed.IsIncome ? BillType.Income : BillType.Expense,
            Channel = parsed.Channel,
            Note = sms.Body,
            TransactionTime = sms.Date,
            Source = BillSource.SmsRecognition,
            CapturedAt = sms.Date,
        };
    }

    /// <summary>
    /// 把读到的短信放进待确认账单。
    /// <list type="bullet">
    /// <item>没识别出金额（<see cref="PaymentContentParser.Parse"/> 的金额 ≤ 0）：不加入，记为 <see cref="SmsCaptureOutcome.NoAmount"/>。</item>
    /// <item>其余按时间由新到旧逐条交给 <see cref="IPendingBillService.AddAsync"/>：它按「来源 + 金额 + 交易时间 ±1 分钟」去重
    /// （<see cref="SmsCaptureOutcome.Duplicate"/>），所以重复读取同一批短信不会产生重复的待确认账单。</item>
    /// <item>容量：待确认账单最多 <see cref="IPendingBillService.MaxCount"/> 条，满了之后 <c>AddAsync</c> 会丢弃最早的一条。
    /// 批量读取不能因此顶掉用户还没处理的待确认账单，所以先按剩余容量算「预算」——新加入的条数用完预算后，剩下的记为
    /// <see cref="SmsCaptureOutcome.OverCapacity"/>（由新到旧，优先保留最近的付款；重复的不占预算）。
    /// 预算用完后不再调 <c>AddAsync</c>（它会丢弃最早的一条），但已经在列表里的短信（读过的、实时捕获过的、这一批里刚加入的）
    /// 仍按 <see cref="PendingBillDeduplication"/> 的同一条规则在内存里判成 <see cref="SmsCaptureOutcome.Duplicate"/>，
    /// 只有真的没地方放的才是列表已满。</item>
    /// <item>单条写入出错记为 <see cref="SmsCaptureOutcome.Failed"/> 并继续处理下一条；读不到现有条数时整批记为 Failed（宁可不加也不冒顶掉别人的风险）。</item>
    /// </list>
    /// 返回的 <see cref="SmsCaptureResult.Items"/> 顺序与传入的短信一致。
    /// </summary>
    /// <param name="pending">待确认账单服务</param>
    /// <param name="messages">读到的支付短信</param>
    /// <param name="logger">写入出错时记录日志（可空）</param>
    public static async Task<SmsCaptureResult> CaptureAsync(IPendingBillService pending, IReadOnlyList<SmsMessage> messages, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(messages);

        var outcomes = new SmsCaptureOutcome[messages.Count];
        var parsedByIndex = new PaymentContentParser.PaymentParseResult?[messages.Count];
        var candidates = new List<int>();

        for (var i = 0; i < messages.Count; i++)
        {
            var parsed = PaymentContentParser.Parse(messages[i].Body ?? string.Empty);
            if (parsed.Amount <= 0)
            {
                outcomes[i] = SmsCaptureOutcome.NoAmount;
                continue;
            }

            parsedByIndex[i] = parsed;
            candidates.Add(i);
        }

        if (candidates.Count > 0)
        {
            List<PendingBill> known;
            int budget;
            try
            {
                known = (await pending.GetPendingBillsAsync()).ToList();
                budget = Math.Max(0, IPendingBillService.MaxCount - known.Count);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "读取待确认账单现有条数失败，这批短信都不加入");
                foreach (var index in candidates)
                {
                    outcomes[index] = SmsCaptureOutcome.Failed;
                }

                return Build(messages, outcomes);
            }

            // 由新到旧（时间相同保持读到的顺序）：容量不够时优先保留最近的付款
            var ordered = candidates
                .OrderByDescending(i => messages[i].Date)
                .ThenBy(i => i)
                .ToList();

            foreach (var index in ordered)
            {
                var bill = ToPendingBill(messages[index], parsedByIndex[index]!);

                if (budget <= 0)
                {
                    // 列表已满：不能再调 AddAsync（会顶掉最早的一条）；已经在列表里的仍如实记为重复，真没地方放的才是列表已满
                    outcomes[index] = PendingBillDeduplication.ContainsDuplicate(known, bill)
                        ? SmsCaptureOutcome.Duplicate
                        : SmsCaptureOutcome.OverCapacity;
                    continue;
                }

                try
                {
                    var added = await pending.AddAsync(bill);
                    if (added)
                    {
                        budget--;
                        known.Add(bill);
                        outcomes[index] = SmsCaptureOutcome.Added;
                    }
                    else
                    {
                        outcomes[index] = SmsCaptureOutcome.Duplicate;
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "把短信 {SmsId} 加入待确认账单失败", messages[index].Id);
                    outcomes[index] = SmsCaptureOutcome.Failed;
                }
            }
        }

        return Build(messages, outcomes);
    }

    private static SmsCaptureResult Build(IReadOnlyList<SmsMessage> messages, SmsCaptureOutcome[] outcomes) =>
        new(messages.Select((sms, i) => new SmsCaptureItem(sms, outcomes[i])).ToList());
}
