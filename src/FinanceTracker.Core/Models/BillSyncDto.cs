using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Core.Models;

/// <summary>
/// 账单同步传输对象（用于客户端与云端 API 之间的数据交换）
/// </summary>
public class BillSyncDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public BillType Type { get; set; }
    public Guid CategoryId { get; set; }
    public Guid PaymentChannelId { get; set; }
    public Guid? LedgerId { get; set; }
    public DateTime TransactionTime { get; set; }
    public string? Note { get; set; }
    public BillSource Source { get; set; }
    public SyncStatus SyncStatus { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ---- 分类/支付渠道/账本附带信息（用于对端"缺则补建"，避免账单外键违例） ----
    public string? CategoryName { get; set; }
    public string? CategoryIcon { get; set; }
    public bool CategoryIsPreset { get; set; }
    public string? PaymentChannelName { get; set; }
    public string? PaymentChannelIcon { get; set; }
    public bool PaymentChannelIsPreset { get; set; }
    public string? LedgerName { get; set; }
    public string? LedgerIcon { get; set; }

    /// <summary>
    /// 由实体转换为传输对象（需已 Include Category/PaymentChannel 导航属性，否则附带信息为空）
    /// </summary>
    public static BillSyncDto FromEntity(Bill bill) => new()
    {
        Id = bill.Id,
        UserId = bill.UserId,
        Amount = bill.Amount,
        Type = bill.Type,
        CategoryId = bill.CategoryId,
        PaymentChannelId = bill.PaymentChannelId,
        LedgerId = bill.LedgerId,
        TransactionTime = bill.TransactionTime,
        Note = bill.Note,
        Source = bill.Source,
        SyncStatus = bill.SyncStatus,
        IsDeleted = bill.IsDeleted,
        CreatedAt = bill.CreatedAt,
        UpdatedAt = bill.UpdatedAt,
        CategoryName = bill.Category?.Name,
        CategoryIcon = bill.Category?.Icon,
        CategoryIsPreset = bill.Category?.IsPreset ?? false,
        PaymentChannelName = bill.PaymentChannel?.Name,
        PaymentChannelIcon = bill.PaymentChannel?.Icon,
        PaymentChannelIsPreset = bill.PaymentChannel?.IsPreset ?? false,
        LedgerName = bill.Ledger?.Name,
        LedgerIcon = bill.Ledger?.Icon
    };

    /// <summary>
    /// 由传输对象转换为实体（UserId 以服务端为准，避免越权）
    /// </summary>
    public Bill ToEntity(Guid userId) => new()
    {
        Id = Id,
        UserId = userId,
        Amount = Amount,
        Type = Type,
        CategoryId = CategoryId,
        PaymentChannelId = PaymentChannelId,
        LedgerId = LedgerId,
        TransactionTime = ToUtc(TransactionTime),
        Note = Note,
        Source = Source,
        SyncStatus = SyncStatus,
        IsDeleted = IsDeleted,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };

    /// <summary>
    /// 将云端权威数据覆盖到已有本地实体（保留 Id/UserId，不触发导航属性）
    /// </summary>
    public void ApplyTo(Bill bill)
    {
        bill.Amount = Amount;
        bill.Type = Type;
        bill.CategoryId = CategoryId;
        bill.PaymentChannelId = PaymentChannelId;
        bill.LedgerId = LedgerId;

        bill.TransactionTime = ToUtc(TransactionTime);

        bill.Note = Note;
        bill.Source = Source;
        bill.IsDeleted = IsDeleted;
    }

    /// <summary>
    /// 归一化为 UTC。约定各端一律以 UTC 存储/传输：
    /// - Utc：原样；
    /// - Local：转换为 UTC；
    /// - Unspecified（SQLite 读出/无时区后缀 JSON 反序列化的典型情况）：按已是 UTC 处理，
    ///   绝不能用 ToUniversalTime()（会按服务器本地时区解释，造成时间偏移）。
    /// Npgsql 写 timestamptz 只接受 Kind=Utc，否则抛 ArgumentException。
    /// </summary>
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>
    /// 确保目标库中存在账单引用的分类；不存在则按 DTO 携带的信息补建（避免外键违例）。
    /// 立即 SaveChanges：保证后续 AnyAsync 能查到，避免同批重复 Add 相同主键。
    /// </summary>
    public async Task EnsureCategoryExistsAsync(IApplicationDbContext context, Guid userId)
    {
        // IgnoreQueryFilters：只要行存在（含软删除）即满足外键，避免重复 Add 相同主键
        var exists = await context.Categories
            .IgnoreQueryFilters()
            .AnyAsync(c => c.Id == CategoryId);
        if (exists)
        {
            return;
        }

        context.Categories.Add(new Category
        {
            Id = CategoryId,
            UserId = CategoryIsPreset ? null : userId,
            Name = string.IsNullOrWhiteSpace(CategoryName) ? "未命名分类" : CategoryName!,
            Icon = string.IsNullOrWhiteSpace(CategoryIcon) ? "mdi-tag" : CategoryIcon!,
            Type = Type,
            IsPreset = CategoryIsPreset,
            SortOrder = 99
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// 确保目标库中存在账单引用的支付渠道；不存在则按 DTO 携带的信息补建（避免外键违例）。
    /// 立即 SaveChanges：保证后续 AnyAsync 能查到，避免同批重复 Add 相同主键。
    /// </summary>
    public async Task EnsurePaymentChannelExistsAsync(IApplicationDbContext context, Guid userId)
    {
        // IgnoreQueryFilters：只要行存在（含软删除）即满足外键，避免重复 Add 相同主键
        var exists = await context.PaymentChannels
            .IgnoreQueryFilters()
            .AnyAsync(c => c.Id == PaymentChannelId);
        if (exists)
        {
            return;
        }

        context.PaymentChannels.Add(new PaymentChannel
        {
            Id = PaymentChannelId,
            UserId = PaymentChannelIsPreset ? null : userId,
            Name = string.IsNullOrWhiteSpace(PaymentChannelName) ? "未命名渠道" : PaymentChannelName!,
            Icon = string.IsNullOrWhiteSpace(PaymentChannelIcon) ? "mdi-credit-card" : PaymentChannelIcon!,
            IsPreset = PaymentChannelIsPreset,
            SortOrder = 99
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// 确保目标库中存在账单归属的账本；不存在则按 DTO 携带的信息补建（避免外键违例）。
    /// LedgerId 为空表示未归属账本，直接返回。
    /// 立即 SaveChanges：保证后续 AnyAsync 能查到，避免同批重复 Add 相同主键。
    /// </summary>
    public async Task EnsureLedgerExistsAsync(IApplicationDbContext context, Guid userId)
    {
        if (LedgerId == null || LedgerId.Value == Guid.Empty)
        {
            return;
        }

        // IgnoreQueryFilters：只要行存在（含软删除）即满足外键，避免重复 Add 相同主键
        var exists = await context.Ledgers
            .IgnoreQueryFilters()
            .AnyAsync(l => l.Id == LedgerId.Value);
        if (exists)
        {
            return;
        }

        context.Ledgers.Add(new Ledger
        {
            Id = LedgerId.Value,
            UserId = userId,
            Name = string.IsNullOrWhiteSpace(LedgerName) ? "未命名账本" : LedgerName!,
            Icon = string.IsNullOrWhiteSpace(LedgerIcon) ? "mdi-book" : LedgerIcon!,
            SortOrder = 99
        });
        await context.SaveChangesAsync();
    }
}
