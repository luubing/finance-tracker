using FinanceTracker.Core.Entities;

namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 认证服务接口
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// 用户注册或登录（根据手机号自动判断）
    /// </summary>
    /// <param name="phoneNumber">手机号</param>
    /// <returns>用户信息</returns>
    Task<User> RegisterOrLoginAsync(string phoneNumber);

    /// <summary>
    /// 根据用户ID获取用户
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>用户信息</returns>
    Task<User?> GetUserByIdAsync(Guid userId);

    /// <summary>
    /// 根据手机号获取用户
    /// </summary>
    /// <param name="phoneNumber">手机号</param>
    /// <returns>用户信息</returns>
    Task<User?> GetUserByPhoneNumberAsync(string phoneNumber);
}
