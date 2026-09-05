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

    /// <summary>
    /// 是否支持"按住录音、松开结束"的持续录制模式（原生流式识别支持，浏览器 Web Speech 不支持）
    /// </summary>
    bool SupportsHoldToTalk { get; }

    /// <summary>
    /// 持续录制模式：开始录音与识别（对应 UI 上的按下动作）。
    /// 识别过程中通过 <see cref="PartialResultReceived"/> 持续回调部分识别文本。
    /// </summary>
    /// <returns>是否成功开始录音；已在录音中时返回 true（幂等）</returns>
    Task<bool> StartRecordingAsync();

    /// <summary>
    /// 持续录制模式：结束录音并等待最终识别结果（对应 UI 上的松开动作）。
    /// 实现必须显式结束音频流（如 iOS 的 EndAudio）以促使识别器立即产出最终结果。
    /// </summary>
    /// <param name="cancellationToken">取消令牌（等待最终结果的超时控制）</param>
    /// <returns>识别文本；未在录音中/失败/未识别到内容时返回 null，实现不应抛出未处理异常</returns>
    Task<string?> StopRecordingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 持续录制模式：取消本次录音并丢弃结果（对应 UI 上的手指滑出等取消手势），幂等
    /// </summary>
    Task CancelRecordingAsync();

    /// <summary>
    /// 持续录制过程中的部分识别文本回调（实时上屏展示）；一次性识别模式不触发
    /// </summary>
    event Action<string>? PartialResultReceived;
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

    /// <summary>不支持按住录音模式</summary>
    public bool SupportsHoldToTalk => false;

    /// <summary>不支持开始录音，始终返回 false</summary>
    public Task<bool> StartRecordingAsync() => Task.FromResult(false);

    /// <summary>不支持结束录音，始终返回 null</summary>
    public Task<string?> StopRecordingAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    /// <summary>不支持取消录音，空操作</summary>
    public Task CancelRecordingAsync() => Task.CompletedTask;

    /// <summary>不会触发部分结果回调</summary>
    public event Action<string>? PartialResultReceived
    {
        add { }
        remove { }
    }
}