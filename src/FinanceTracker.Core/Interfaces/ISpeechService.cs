namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 语音识别服务接口：负责"语音 → 文本"，文本 → 账单的解析由 <see cref="IBillVoiceParser"/> 完成。
/// 各宿主提供各自实现：
/// - MAUI App：Android SpeechRecognizer / iOS SFSpeechRecognizer 原生实现；
/// - Blazor Server：浏览器 Web Speech API（通过 <c>FinanceTracker.Web.BillVoiceInputService</c> 的 JS interop，服务端进程注册空实现）。
/// </summary>
public interface ISpeechService
{
    /// <summary>
    /// 当前环境是否具备语音识别能力（含权限就绪）
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 请求麦克风/语音识别权限
    /// </summary>
    /// <returns>是否授权成功</returns>
    Task<bool> RequestPermissionAsync();

    /// <summary>
    /// 完成一段语音的识别，返回识别文本
    /// </summary>
    /// <param name="cancellationToken">取消令牌（超时或用户取消）</param>
    /// <returns>识别文本；失败/取消/未识别到内容时返回 null，实现不应抛出未处理异常</returns>
    Task<string?> RecognizeOnceAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 语音识别空实现：用于不支持语音识别的宿主（如 Blazor Server 服务端进程、桌面端），
/// 保证 DI 注册完整、页面注入不报错。
/// </summary>
public class NoOpSpeechService : ISpeechService
{
    /// <summary>不支持语音识别</summary>
    public bool IsAvailable => false;

    /// <summary>不支持权限请求，始终返回 false</summary>
    public Task<bool> RequestPermissionAsync() => Task.FromResult(false);

    /// <summary>不支持识别，始终返回 null</summary>
    public Task<string?> RecognizeOnceAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}