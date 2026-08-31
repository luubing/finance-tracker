using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 支付渠道服务实现
/// </summary>
public class PaymentChannelService : IPaymentChannelService
{
    private readonly IApplicationDbContext _context;

    public PaymentChannelService(IApplicationDbContext context)
    {
        _context = context;
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
        // 确保是用户自定义渠道
        channel.IsPreset = false;
        channel.Id = Guid.NewGuid();

        _context.PaymentChannels.Add(channel);
        await _context.SaveChangesAsync();

        return channel;
    }

    public async Task<PaymentChannel> UpdatePaymentChannelAsync(PaymentChannel channel)
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

    public async Task<bool> DeletePaymentChannelAsync(Guid channelId, Guid userId)
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
}
