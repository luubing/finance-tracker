namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 短信服务接口
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// 检查是否有短信读取权限
    /// </summary>
    /// <returns>是否有权限</returns>
    Task<bool> HasPermissionAsync();

    /// <summary>
    /// 请求短信读取权限
    /// </summary>
    /// <returns>是否授权成功</returns>
    Task<bool> RequestPermissionAsync();

    /// <summary>
    /// 读取支付类短信
    /// </summary>
    /// <param name="fromDate">开始日期</param>
    /// <returns>短信列表</returns>
    Task<List<SmsMessage>> ReadPaymentSmsAsync(DateTime fromDate);
}

/// <summary>
/// 短信消息
/// </summary>
public class SmsMessage
{
    /// <summary>
    /// 短信ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 发送方
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// 短信内容
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// 短信时间
    /// </summary>
    public DateTime Date { get; set; }
}
