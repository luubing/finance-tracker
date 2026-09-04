using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 预设数据服务实现
/// </summary>
public class PresetDataService : IPresetDataService
{
    private readonly IApplicationDbContext _context;

    public PresetDataService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task InitializePresetDataAsync()
    {
        // 按“固定 GUID 逐项补建”自愈：历史上预设可能以随机 GUID 建过（导致同步外键违例），
        // 不能再简单以“存在任意预设”为由整体跳过。
        await InitializePresetCategoriesAsync();
        await InitializePresetPaymentChannelsAsync();
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// 按固定 GUID 补建缺失的预设分类；迁移旧随机 GUID 预设的账单引用后软删遗留项。
    /// </summary>
    private async Task InitializePresetCategoriesAsync()
    {
        foreach (var preset in BuildPresetCategories())
        {
            if (await _context.Categories
                    .IgnoreQueryFilters()
                    .AnyAsync(c => c.Id == preset.Id))
            {
                continue;
            }

            // 先持久化固定 GUID 预设，确保随后账单重指时外键目标已存在（避免 SQLite FK 违例）
            _context.Categories.Add(preset);
            await _context.SaveChangesAsync();

            // 旧随机 GUID 的同名预设：先把它名下的账单重指到固定 GUID 预设，再软删遗留项
            var legacy = await _context.Categories
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.IsPreset && c.UserId == null && c.Name == preset.Name && c.Id != preset.Id);

            if (legacy != null)
            {
                var legacyBills = await _context.Bills
                    .IgnoreQueryFilters()
                    .Where(b => b.CategoryId == legacy.Id)
                    .ToListAsync();

                foreach (var bill in legacyBills)
                {
                    bill.CategoryId = preset.Id;
                    bill.SyncStatus = SyncStatus.Pending;
                }

                legacy.IsDeleted = true;
                await _context.SaveChangesAsync();
            }
        }
    }

    /// <summary>
    /// 按固定 GUID 补建缺失的预设支付渠道；迁移旧随机 GUID 预设的账单引用后软删遗留项。
    /// </summary>
    private async Task InitializePresetPaymentChannelsAsync()
    {
        foreach (var channel in BuildPresetPaymentChannels())
        {
            if (await _context.PaymentChannels
                    .IgnoreQueryFilters()
                    .AnyAsync(c => c.Id == channel.Id))
            {
                continue;
            }

            // 先持久化固定 GUID 预设渠道，确保随后账单重指时外键目标已存在（避免 SQLite FK 违例）
            _context.PaymentChannels.Add(channel);
            await _context.SaveChangesAsync();

            var legacy = await _context.PaymentChannels
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.IsPreset && c.UserId == null && c.Name == channel.Name && c.Id != channel.Id);

            if (legacy != null)
            {
                var legacyBills = await _context.Bills
                    .IgnoreQueryFilters()
                    .Where(b => b.PaymentChannelId == legacy.Id)
                    .ToListAsync();

                foreach (var bill in legacyBills)
                {
                    bill.PaymentChannelId = channel.Id;
                    bill.SyncStatus = SyncStatus.Pending;
                }

                legacy.IsDeleted = true;
                await _context.SaveChangesAsync();
            }
        }
    }

    private static Category CreatePresetCategory(string name, Guid id) => new()
    {
        Id = id,
        Name = name,
        Icon = name switch
        {
            "餐饮美食" => "mdi-food",
            "交通出行" => "mdi-car",
            "日用百货" => "mdi-shopping",
            "购物消费" => "mdi-cart",
            "娱乐休闲" => "mdi-gamepad-variant",
            "医疗健康" => "mdi-hospital-box",
            "教育培训" => "mdi-school",
            "居住生活" => "mdi-home",
            "通讯物流" => "mdi-phone",
            "其他支出" => "mdi-dots-horizontal",
            "工资薪酬" => "mdi-cash",
            "奖金补贴" => "mdi-gift",
            "投资理财" => "mdi-chart-line",
            "兼职副业" => "mdi-briefcase",
            _ => "mdi-dots-horizontal"
        },
        Type = PresetCategoryIds[name] == Guid.Parse("10000000-0000-0000-0000-000000000011")
            || PresetCategoryIds[name] == Guid.Parse("10000000-0000-0000-0000-000000000012")
            || PresetCategoryIds[name] == Guid.Parse("10000000-0000-0000-0000-000000000013")
            || PresetCategoryIds[name] == Guid.Parse("10000000-0000-0000-0000-000000000014")
            || PresetCategoryIds[name] == Guid.Parse("10000000-0000-0000-0000-000000000015")
            ? BillType.Income
            : BillType.Expense,
        IsPreset = true,
        SortOrder = PresetCategoryIds.Keys.ToList().IndexOf(name) + 1
    };

    private static PaymentChannel CreatePresetPaymentChannel(string name, Guid id) => new()
    {
        Id = id,
        Name = name,
        Icon = name switch
        {
            "微信支付" => "mdi-wechat",
            "支付宝" => "mdi-alipay",
            "京东支付" => "mdi-shopping",
            "美团支付" => "mdi-food",
            "云闪付" => "mdi-credit-card",
            "Apple Pay" => "mdi-apple",
            "现金" => "mdi-cash",
            "银行卡" => "mdi-credit-card-outline",
            _ => "mdi-credit-card"
        },
        IsPreset = true,
        SortOrder = PresetPaymentChannelIds.Keys.ToList().IndexOf(name) + 1
    };

    public async Task<List<Category>> GetCategoriesAsync(Guid? userId)
    {
        var query = _context.Categories
            .Where(c => c.IsPreset || (userId.HasValue && c.UserId == userId.Value));

        return await query
            .OrderBy(c => c.Type)
            .ThenBy(c => c.SortOrder)
            .ToListAsync();
    }

    public async Task<List<PaymentChannel>> GetPaymentChannelsAsync(Guid? userId)
    {
        var query = _context.PaymentChannels
            .Where(c => c.IsPreset || (userId.HasValue && c.UserId == userId.Value));

        return await query
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
    }

    /// <summary>
    /// 预设分类使用固定的 GUID，确保任何环境（本地 SQLite / Web SQLite / 远端 PostgreSQL）
    /// 都生成完全一致的 Id，从而同步推送时外键可以正确关联，不会因两端 GUID 不同而触发外键违例。
    /// </summary>
    private static readonly Dictionary<string, Guid> PresetCategoryIds = new()
    {
        // 支出分类
        ["餐饮美食"] = Guid.Parse("10000000-0000-0000-0000-000000000001"),
        ["交通出行"] = Guid.Parse("10000000-0000-0000-0000-000000000002"),
        ["日用百货"] = Guid.Parse("10000000-0000-0000-0000-000000000003"),
        ["购物消费"] = Guid.Parse("10000000-0000-0000-0000-000000000004"),
        ["娱乐休闲"] = Guid.Parse("10000000-0000-0000-0000-000000000005"),
        ["医疗健康"] = Guid.Parse("10000000-0000-0000-0000-000000000006"),
        ["教育培训"] = Guid.Parse("10000000-0000-0000-0000-000000000007"),
        ["居住生活"] = Guid.Parse("10000000-0000-0000-0000-000000000008"),
        ["通讯物流"] = Guid.Parse("10000000-0000-0000-0000-000000000009"),
        ["其他支出"] = Guid.Parse("10000000-0000-0000-0000-000000000010"),
        // 收入分类
        ["工资薪酬"] = Guid.Parse("10000000-0000-0000-0000-000000000011"),
        ["奖金补贴"] = Guid.Parse("10000000-0000-0000-0000-000000000012"),
        ["投资理财"] = Guid.Parse("10000000-0000-0000-0000-000000000013"),
        ["兼职副业"] = Guid.Parse("10000000-0000-0000-0000-000000000014"),
        ["其他收入"] = Guid.Parse("10000000-0000-0000-0000-000000000015")
    };

    /// <summary>
    /// 预设支付渠道使用固定的 GUID，与预设分类同理，保证各端 Id 一致。
    /// </summary>
    private static readonly Dictionary<string, Guid> PresetPaymentChannelIds = new()
    {
        ["微信支付"] = Guid.Parse("20000000-0000-0000-0000-000000000001"),
        ["支付宝"] = Guid.Parse("20000000-0000-0000-0000-000000000002"),
        ["京东支付"] = Guid.Parse("20000000-0000-0000-0000-000000000003"),
        ["美团支付"] = Guid.Parse("20000000-0000-0000-0000-000000000004"),
        ["云闪付"] = Guid.Parse("20000000-0000-0000-0000-000000000005"),
        ["Apple Pay"] = Guid.Parse("20000000-0000-0000-0000-000000000006"),
        ["现金"] = Guid.Parse("20000000-0000-0000-0000-000000000007"),
        ["银行卡"] = Guid.Parse("20000000-0000-0000-0000-000000000008"),
        ["信用卡"] = Guid.Parse("20000000-0000-0000-0000-000000000009")
    };

    private static List<Category> BuildPresetCategories()
    {
        var expenseCategories = new List<Category>
        {
            new() { Id = PresetCategoryIds["餐饮美食"], Name = "餐饮美食", Icon = "mdi-food", Type = BillType.Expense, IsPreset = true, SortOrder = 1 },
            new() { Id = PresetCategoryIds["交通出行"], Name = "交通出行", Icon = "mdi-car", Type = BillType.Expense, IsPreset = true, SortOrder = 2 },
            new() { Id = PresetCategoryIds["日用百货"], Name = "日用百货", Icon = "mdi-shopping", Type = BillType.Expense, IsPreset = true, SortOrder = 3 },
            new() { Id = PresetCategoryIds["购物消费"], Name = "购物消费", Icon = "mdi-cart", Type = BillType.Expense, IsPreset = true, SortOrder = 4 },
            new() { Id = PresetCategoryIds["娱乐休闲"], Name = "娱乐休闲", Icon = "mdi-gamepad-variant", Type = BillType.Expense, IsPreset = true, SortOrder = 5 },
            new() { Id = PresetCategoryIds["医疗健康"], Name = "医疗健康", Icon = "mdi-hospital-box", Type = BillType.Expense, IsPreset = true, SortOrder = 6 },
            new() { Id = PresetCategoryIds["教育培训"], Name = "教育培训", Icon = "mdi-school", Type = BillType.Expense, IsPreset = true, SortOrder = 7 },
            new() { Id = PresetCategoryIds["居住生活"], Name = "居住生活", Icon = "mdi-home", Type = BillType.Expense, IsPreset = true, SortOrder = 8 },
            new() { Id = PresetCategoryIds["通讯物流"], Name = "通讯物流", Icon = "mdi-phone", Type = BillType.Expense, IsPreset = true, SortOrder = 9 },
            new() { Id = PresetCategoryIds["其他支出"], Name = "其他支出", Icon = "mdi-dots-horizontal", Type = BillType.Expense, IsPreset = true, SortOrder = 10 }
        };

        var incomeCategories = new List<Category>
        {
            new() { Id = PresetCategoryIds["工资薪酬"], Name = "工资薪酬", Icon = "mdi-cash", Type = BillType.Income, IsPreset = true, SortOrder = 1 },
            new() { Id = PresetCategoryIds["奖金补贴"], Name = "奖金补贴", Icon = "mdi-gift", Type = BillType.Income, IsPreset = true, SortOrder = 2 },
            new() { Id = PresetCategoryIds["投资理财"], Name = "投资理财", Icon = "mdi-chart-line", Type = BillType.Income, IsPreset = true, SortOrder = 3 },
            new() { Id = PresetCategoryIds["兼职副业"], Name = "兼职副业", Icon = "mdi-briefcase", Type = BillType.Income, IsPreset = true, SortOrder = 4 },
            new() { Id = PresetCategoryIds["其他收入"], Name = "其他收入", Icon = "mdi-dots-horizontal", Type = BillType.Income, IsPreset = true, SortOrder = 5 }
        };

        return expenseCategories.Concat(incomeCategories).ToList();
    }

    private static List<PaymentChannel> BuildPresetPaymentChannels()
    {
        var channels = new List<PaymentChannel>
        {
            new() { Id = PresetPaymentChannelIds["微信支付"], Name = "微信支付", Icon = "mdi-wechat", IsPreset = true, SortOrder = 1 },
            new() { Id = PresetPaymentChannelIds["支付宝"], Name = "支付宝", Icon = "mdi-alipay", IsPreset = true, SortOrder = 2 },
            new() { Id = PresetPaymentChannelIds["京东支付"], Name = "京东支付", Icon = "mdi-shopping", IsPreset = true, SortOrder = 3 },
            new() { Id = PresetPaymentChannelIds["美团支付"], Name = "美团支付", Icon = "mdi-food", IsPreset = true, SortOrder = 4 },
            new() { Id = PresetPaymentChannelIds["云闪付"], Name = "云闪付", Icon = "mdi-credit-card", IsPreset = true, SortOrder = 5 },
            new() { Id = PresetPaymentChannelIds["Apple Pay"], Name = "Apple Pay", Icon = "mdi-apple", IsPreset = true, SortOrder = 6 },
            new() { Id = PresetPaymentChannelIds["现金"], Name = "现金", Icon = "mdi-cash", IsPreset = true, SortOrder = 7 },
            new() { Id = PresetPaymentChannelIds["银行卡"], Name = "银行卡", Icon = "mdi-credit-card-outline", IsPreset = true, SortOrder = 8 },
            new() { Id = PresetPaymentChannelIds["信用卡"], Name = "信用卡", Icon = "mdi-credit-card", IsPreset = true, SortOrder = 9 }
        };

        return channels;
    }
}
