using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 用户默认值服务实现
/// </summary>
public class UserDefaultService : IUserDefaultService
{
    private readonly IApplicationDbContext _context;

    public UserDefaultService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<UserDefault?> GetUserDefaultAsync(Guid userId)
    {
        return await _context.UserDefaults
            .FirstOrDefaultAsync(u => u.UserId == userId);
    }

    public async Task<bool> SetDefaultExpenseCategoryAsync(Guid userId, Guid? categoryId)
    {
        var userDefault = await GetOrCreateUserDefaultAsync(userId);
        userDefault.DefaultExpenseCategoryId = categoryId;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetDefaultIncomeCategoryAsync(Guid userId, Guid? categoryId)
    {
        var userDefault = await GetOrCreateUserDefaultAsync(userId);
        userDefault.DefaultIncomeCategoryId = categoryId;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetDefaultPaymentChannelAsync(Guid userId, Guid? channelId)
    {
        var userDefault = await GetOrCreateUserDefaultAsync(userId);
        userDefault.DefaultPaymentChannelId = channelId;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetDefaultLedgerAsync(Guid userId, Guid? ledgerId)
    {
        var userDefault = await GetOrCreateUserDefaultAsync(userId);
        userDefault.DefaultLedgerId = ledgerId;
        await _context.SaveChangesAsync();
        return true;
    }

    private async Task<UserDefault> GetOrCreateUserDefaultAsync(Guid userId)
    {
        var userDefault = await _context.UserDefaults
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (userDefault == null)
        {
            userDefault = new UserDefault
            {
                UserId = userId
            };
            _context.UserDefaults.Add(userDefault);
            // 不在这里保存，让调用方统一保存
        }

        return userDefault;
    }
}
