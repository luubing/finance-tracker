using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 支付渠道服务实现
/// </summary>
public class PaymentChannelService : IPaymentChannelService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<PaymentChannelService> _logger;

    public PaymentChannelService(IApplicationDbContext context, ILogger<PaymentChannelService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<PaymentChannel>> GetPaymentChannelsAsync(Guid? userId)
    {
        var query = _context.PaymentChannels
            .Where(c => !c.IsDeleted && (c.IsPreset || (userId.HasValue && c.UserId == userId.Value)));

        return await query
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
    }

    public async Task<PaymentChannel?> GetPaymentChannelByIdAsync(Guid channelId)
    {
        return await _context.PaymentChannels
            .FirstOrDefaultAsync(c => c.Id == channelId && !c.IsDeleted);
    }

    public async Task<PaymentChannel> CreatePaymentChannelAsync(PaymentChannel channel)
    {
        try
        {
            // 确保是用户自定义渠道
            channel.IsPreset = false;
            channel.Id = Guid.NewGuid();

            _context.PaymentChannels.Add(channel);
            await _context.SaveChangesAsync();

            return channel;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建支付渠道失败，用户: {UserId}, 渠道名: {Name}", channel.UserId, channel.Name);
            throw;
        }
    }

    public async Task<PaymentChannel> UpdatePaymentChannelAsync(PaymentChannel channel)
    {
        try
        {
            var existingChannel = await _context.PaymentChannels
                .FirstOrDefaultAsync(c => c.Id == channel.Id && c.UserId == channel.UserId);

            if (existingChannel == null)
            {
                throw new ArgumentException("支付渠道不存在");
            }

            if (existingChannel.IsPreset)
            {
                throw new UnauthorizedAccessException("不能修改预设支付渠道");
            }

            existingChannel.Name = channel.Name;
            existingChannel.Icon = channel.Icon;
            existingChannel.SortOrder = channel.SortOrder;

            await _context.SaveChangesAsync();

            return existingChannel;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新支付渠道失败，渠道Id: {ChannelId}, 用户: {UserId}", channel.Id, channel.UserId);
            throw;
        }
    }

    public async Task<bool> DeletePaymentChannelAsync(Guid channelId, Guid userId)
    {
        try
        {
            var channel = await _context.PaymentChannels
                .FirstOrDefaultAsync(c => c.Id == channelId && c.UserId == userId);

            if (channel == null)
            {
                return false;
            }

            if (channel.IsPreset)
            {
                throw new UnauthorizedAccessException("不能删除预设支付渠道");
            }

            // 检查是否有关联的账单
            var hasBills = await _context.Bills
                .AnyAsync(b => b.PaymentChannelId == channelId);

            if (hasBills)
            {
                throw new InvalidOperationException("该支付渠道下有账单记录，无法删除");
            }

            channel.IsDeleted = true;
            await _context.SaveChangesAsync();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除支付渠道失败，渠道Id: {ChannelId}, 用户: {UserId}", channelId, userId);
            throw;
        }
    }
}
