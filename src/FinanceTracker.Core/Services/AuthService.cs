using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 认证服务实现
/// </summary>
public class AuthService : IAuthService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IApplicationDbContext context, ILogger<AuthService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<User> RegisterOrLoginAsync(string phoneNumber)
    {
        try
        {
            // 查找现有用户
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber);

            if (user != null)
            {
                // 老用户直接登录
                return user;
            }

            // 新用户自动注册
            user = new User
            {
                PhoneNumber = phoneNumber,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "注册/登录失败，手机号: {PhoneNumber}", phoneNumber);
            throw;
        }
    }

    public async Task<User?> GetUserByIdAsync(Guid userId)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId);
    }

    public async Task<User?> GetUserByPhoneNumberAsync(string phoneNumber)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber);
    }
}
