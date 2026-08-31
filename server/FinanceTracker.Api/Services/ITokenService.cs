using FinanceTracker.Core.Entities;

namespace FinanceTracker.Api.Services;

/// <summary>
/// Token 服务接口
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// 生成 JWT Token
    /// </summary>
    /// <param name="user">用户信息</param>
    /// <returns>JWT Token</returns>
    string GenerateToken(User user);
}
