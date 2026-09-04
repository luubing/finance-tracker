using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;

namespace FinanceTracker.Core.Models;

/// <summary>
/// 云端同步 - 推送请求体
/// </summary>
public class SyncPushRequest
{
    public List<BillSyncDto> Bills { get; set; } = new();
}

/// <summary>
/// 云端同步 - 推送响应体（每个账单的冲突裁决结果）
/// </summary>
public class SyncPushResponse
{
    public List<CloudSyncPushItemResult> Results { get; set; } = new();
    public int SyncedCount { get; set; }
    public int FailedCount { get; set; }
}

/// <summary>
/// 单个账单的推送冲突裁决结果
/// </summary>
public record CloudSyncPushItemResult(
    Guid BillId,
    string Action,   // "pushed" | "pulled" | "failed"
    BillSyncDto? AuthoritativeBill,
    string? Error = null);

/// <summary>
/// 云端同步 - 拉取请求体
/// </summary>
public class SyncPullRequest
{
    /// <summary>
    /// 只拉取该时间之后更新的账单（为空则拉取该用户全部云端账单）
    /// </summary>
    public DateTime? Since { get; set; }
}

/// <summary>
/// 云端同步 - 拉取响应体
/// </summary>
public class SyncPullResponse
{
    public List<BillSyncDto> Bills { get; set; } = new();
}

/// <summary>
/// 分类同步传输对象（UserId 不在 DTO 中传输：服务端以 JWT 为准，客户端以本地用户为准）
/// </summary>
public class CategorySyncDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public BillType Type { get; set; }
    public bool IsPreset { get; set; }
    public int SortOrder { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static CategorySyncDto FromEntity(Category category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        Icon = category.Icon,
        Type = category.Type,
        IsPreset = category.IsPreset,
        SortOrder = category.SortOrder,
        IsDeleted = category.IsDeleted,
        CreatedAt = category.CreatedAt,
        UpdatedAt = category.UpdatedAt
    };

    /// <summary>
    /// 转为本地/云端实体（IsPreset 强制为 false：预设由 PresetDataService 统一维护，不参与同步）
    /// </summary>
    public Category ToEntity(Guid userId) => new()
    {
        Id = Id,
        UserId = userId,
        Name = Name,
        Icon = Icon,
        Type = Type,
        IsPreset = false,
        SortOrder = SortOrder,
        IsDeleted = IsDeleted
    };

    /// <summary>
    /// 用对端权威数据覆盖已有实体（不改动 Id/UserId）
    /// </summary>
    public void ApplyTo(Category category)
    {
        category.Name = Name;
        category.Icon = Icon;
        category.Type = Type;
        category.SortOrder = SortOrder;
        category.IsDeleted = IsDeleted;
    }

    /// <summary>
    /// 内容是否与实体一致（用于跳过无变化的写入，避免 UpdatedAt 空转递增）
    /// </summary>
    public bool ContentEquals(Category category) =>
        category.Name == Name &&
        category.Icon == Icon &&
        category.Type == Type &&
        category.SortOrder == SortOrder &&
        category.IsDeleted == IsDeleted;
}

/// <summary>
/// 支付渠道同步传输对象（UserId 处理同 CategorySyncDto）
/// </summary>
public class PaymentChannelSyncDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool IsPreset { get; set; }
    public int SortOrder { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static PaymentChannelSyncDto FromEntity(PaymentChannel channel) => new()
    {
        Id = channel.Id,
        Name = channel.Name,
        Icon = channel.Icon,
        IsPreset = channel.IsPreset,
        SortOrder = channel.SortOrder,
        IsDeleted = channel.IsDeleted,
        CreatedAt = channel.CreatedAt,
        UpdatedAt = channel.UpdatedAt
    };

    public PaymentChannel ToEntity(Guid userId) => new()
    {
        Id = Id,
        UserId = userId,
        Name = Name,
        Icon = Icon,
        IsPreset = false,
        SortOrder = SortOrder,
        IsDeleted = IsDeleted
    };

    public void ApplyTo(PaymentChannel channel)
    {
        channel.Name = Name;
        channel.Icon = Icon;
        channel.SortOrder = SortOrder;
        channel.IsDeleted = IsDeleted;
    }

    public bool ContentEquals(PaymentChannel channel) =>
        channel.Name == Name &&
        channel.Icon == Icon &&
        channel.SortOrder == SortOrder &&
        channel.IsDeleted == IsDeleted;
}

/// <summary>
/// 分类同步 - 推送请求体
/// </summary>
public class CategorySyncPushRequest
{
    public List<CategorySyncDto> Categories { get; set; } = new();
}

/// <summary>
/// 分类同步 - 推送响应体（逐条冲突裁决结果）
/// </summary>
public class CategorySyncPushResponse
{
    public List<CategorySyncItemResult> Results { get; set; } = new();
}

/// <summary>
/// 单个分类的推送裁决结果（Action: "pushed" | "pulled" | "skipped"）
/// </summary>
public record CategorySyncItemResult(
    Guid CategoryId,
    string Action,
    CategorySyncDto? AuthoritativeCategory = null,
    string? Error = null);

/// <summary>
/// 分类同步 - 拉取请求体（Since 为空则拉取全部自定义分类，含软删除）
/// </summary>
public class CategorySyncPullRequest
{
    public DateTime? Since { get; set; }
}

/// <summary>
/// 分类同步 - 拉取响应体
/// </summary>
public class CategorySyncPullResponse
{
    public List<CategorySyncDto> Categories { get; set; } = new();
}

/// <summary>
/// 支付渠道同步 - 推送请求体
/// </summary>
public class PaymentChannelSyncPushRequest
{
    public List<PaymentChannelSyncDto> PaymentChannels { get; set; } = new();
}

/// <summary>
/// 支付渠道同步 - 推送响应体
/// </summary>
public class PaymentChannelSyncPushResponse
{
    public List<PaymentChannelSyncItemResult> Results { get; set; } = new();
}

/// <summary>
/// 单个支付渠道的推送裁决结果（Action: "pushed" | "pulled" | "skipped"）
/// </summary>
public record PaymentChannelSyncItemResult(
    Guid PaymentChannelId,
    string Action,
    PaymentChannelSyncDto? AuthoritativeChannel = null,
    string? Error = null);

/// <summary>
/// 支付渠道同步 - 拉取请求体
/// </summary>
public class PaymentChannelSyncPullRequest
{
    public DateTime? Since { get; set; }
}

/// <summary>
/// 支付渠道同步 - 拉取响应体
/// </summary>
public class PaymentChannelSyncPullResponse
{
    public List<PaymentChannelSyncDto> PaymentChannels { get; set; } = new();
}
