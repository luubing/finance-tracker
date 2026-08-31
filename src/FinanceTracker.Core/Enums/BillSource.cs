namespace FinanceTracker.Core.Enums;

/// <summary>
/// 账单数据来源
/// </summary>
public enum BillSource
{
    /// <summary>
    /// 手动录入
    /// </summary>
    Manual = 0,

    /// <summary>
    /// 导入（CSV等）
    /// </summary>
    Import = 1,

    /// <summary>
    /// 短信识别
    /// </summary>
    SmsRecognition = 2,

    /// <summary>
    /// 通知栏识别
    /// </summary>
    Notification = 3
}
