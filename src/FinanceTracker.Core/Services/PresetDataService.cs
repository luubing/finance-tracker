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
        // 检查是否已有预设数据
        if (await _context.Categories.AnyAsync(c => c.IsPreset))
        {
            return; // 已有预设数据，跳过
        }

        // 初始化预设分类
        await InitializePresetCategoriesAsync();

        // 初始化预设支付渠道
        await InitializePresetPaymentChannelsAsync();

        await _context.SaveChangesAsync();
    }

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

    private async Task InitializePresetCategoriesAsync()
    {
        var expenseCategories = new List<Category>
        {
            new() { Name = "餐饮美食", Icon = "mdi-food", Type = BillType.Expense, IsPreset = true, SortOrder = 1 },
            new() { Name = "交通出行", Icon = "mdi-car", Type = BillType.Expense, IsPreset = true, SortOrder = 2 },
            new() { Name = "日用百货", Icon = "mdi-shopping", Type = BillType.Expense, IsPreset = true, SortOrder = 3 },
            new() { Name = "购物消费", Icon = "mdi-cart", Type = BillType.Expense, IsPreset = true, SortOrder = 4 },
            new() { Name = "娱乐休闲", Icon = "mdi-gamepad-variant", Type = BillType.Expense, IsPreset = true, SortOrder = 5 },
            new() { Name = "医疗健康", Icon = "mdi-hospital-box", Type = BillType.Expense, IsPreset = true, SortOrder = 6 },
            new() { Name = "教育培训", Icon = "mdi-school", Type = BillType.Expense, IsPreset = true, SortOrder = 7 },
            new() { Name = "居住生活", Icon = "mdi-home", Type = BillType.Expense, IsPreset = true, SortOrder = 8 },
            new() { Name = "通讯物流", Icon = "mdi-phone", Type = BillType.Expense, IsPreset = true, SortOrder = 9 },
            new() { Name = "其他支出", Icon = "mdi-dots-horizontal", Type = BillType.Expense, IsPreset = true, SortOrder = 10 }
        };

        var incomeCategories = new List<Category>
        {
            new() { Name = "工资薪酬", Icon = "mdi-cash", Type = BillType.Income, IsPreset = true, SortOrder = 1 },
            new() { Name = "奖金补贴", Icon = "mdi-gift", Type = BillType.Income, IsPreset = true, SortOrder = 2 },
            new() { Name = "投资理财", Icon = "mdi-chart-line", Type = BillType.Income, IsPreset = true, SortOrder = 3 },
            new() { Name = "兼职副业", Icon = "mdi-briefcase", Type = BillType.Income, IsPreset = true, SortOrder = 4 },
            new() { Name = "其他收入", Icon = "mdi-dots-horizontal", Type = BillType.Income, IsPreset = true, SortOrder = 5 }
        };

        _context.Categories.AddRange(expenseCategories);
        _context.Categories.AddRange(incomeCategories);
    }

    private async Task InitializePresetPaymentChannelsAsync()
    {
        var channels = new List<PaymentChannel>
        {
            new() { Name = "微信支付", Icon = "mdi-wechat", IsPreset = true, SortOrder = 1 },
            new() { Name = "支付宝", Icon = "mdi-alipay", IsPreset = true, SortOrder = 2 },
            new() { Name = "京东支付", Icon = "mdi-shopping", IsPreset = true, SortOrder = 3 },
            new() { Name = "美团支付", Icon = "mdi-food", IsPreset = true, SortOrder = 4 },
            new() { Name = "云闪付", Icon = "mdi-credit-card", IsPreset = true, SortOrder = 5 },
            new() { Name = "Apple Pay", Icon = "mdi-apple", IsPreset = true, SortOrder = 6 },
            new() { Name = "现金", Icon = "mdi-cash", IsPreset = true, SortOrder = 7 },
            new() { Name = "银行卡", Icon = "mdi-credit-card-outline", IsPreset = true, SortOrder = 8 },
            new() { Name = "信用卡", Icon = "mdi-credit-card", IsPreset = true, SortOrder = 9 }
        };

        _context.PaymentChannels.AddRange(channels);
    }
}
