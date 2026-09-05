using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Services;
using Microsoft.JSInterop;

namespace FinanceTracker.Web.Services;

/// <summary>
/// 语音录入服务：统一的"语音 → 文本"入口。
/// - MAUI App 宿主：优先使用注册进来的原生 <see cref="ISpeechService"/>（Android SpeechRecognizer / iOS SFSpeechRecognizer）；
/// - Blazor Server 宿主：注入的 ISpeechService 为空实现（NoOpSpeechService），自动降级为浏览器 Web Speech API（JS interop）。
/// 识别结果通过 <see cref="VoiceTextReceived"/> / <see cref="ErrorOccurred"/> 事件回传给页面。
/// </summary>
public sealed class BillVoiceInputService : IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask;
    private DotNetObjectReference<BillVoiceInputService>? _dotNetReference;
    private ISpeechService? _voiceService;

    public BillVoiceInputService(IJSRuntime jsRuntime)
    {
        _moduleTask = new Lazy<Task<IJSObjectReference>>(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import",
            "./_content/FinanceTracker.Web/js/billVoiceInput.js").AsTask());
    }

    /// <summary>识别到语音文本时触发</summary>
    public event Func<string, Task>? VoiceTextReceived;

    /// <summary>语音识别出错时触发（含用户可读的错误信息）</summary>
    public event Func<string, Task>? ErrorOccurred;

    /// <summary>按住录音过程中收到部分识别文本时触发（实时上屏）</summary>
    public event Func<string, Task>? PartialTextReceived;

    /// <summary>
    /// 页面初始化时调用：注册宿主的原生语音服务。
    /// 传入 null（或 <see cref="NoOpSpeechService"/>）表示无原生能力，识别走浏览器 Web Speech API。
    /// </summary>
    public void RegisterVoiceService(ISpeechService? voiceService)
    {
        _voiceService = voiceService is NoOpSpeechService or { IsAvailable: false } ? null : voiceService;
    }

    /// <summary>当前是否使用原生语音服务（否则走浏览器 Web Speech API）</summary>
    public bool IsUsingNativeVoice => _voiceService is not null;

    /// <summary>
    /// 当前浏览器是否支持 Web Speech API（仅浏览器模式下有效）。
    /// 加载失败（如 MAUI WebView 中无该 JS 能力）视为不支持。
    /// </summary>
    public async Task<bool> IsBrowserSupportedAsync()
    {
        try
        {
            var module = await _moduleTask.Value;
            return await module.InvokeAsync<bool>("isSupported");
        }
        catch (JSException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// 启动一次语音识别，结果通过事件返回。并发调用安全：识别中重复调用直接忽略。
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRecognizing)
        {
            return;
        }

        _isRecognizing = true;
        try
        {
            var nativeService = _voiceService;
            if (nativeService is not null)
            {
                if (!await nativeService.RequestPermissionAsync())
                {
                    await NotifyErrorAsync("未获得麦克风权限，请到系统设置中开启后重试");
                    return;
                }

                var nativeText = await nativeService.RecognizeOnceAsync(cancellationToken);
                await PublishResultAsync(nativeText);
                return;
            }

            _dotNetReference ??= DotNetObjectReference.Create(this);
            var module = await _moduleTask.Value;
            var started = await module.InvokeAsync<bool>("startRecognition", cancellationToken, _dotNetReference);
            if (!started)
            {
                // 无法启动时 JS 侧已通过 OnError 回调报告原因
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // 用户取消或超时，静默结束
        }
        catch (JSDisconnectedException)
        {
            // 电路已断开（页面关闭），无需处理
        }
        catch (JSException ex)
        {
            await NotifyErrorAsync($"无法启动语音识别：{ex.Message}");
        }
        finally
        {
            _isRecognizing = false;
        }
    }

    /// <summary>识别中标记（防止并发识别）</summary>
    private bool _isRecognizing;

    /// <summary>原生语音服务是否支持"按住录音、松开结束"模式</summary>
    public bool SupportsHoldToTalk => _voiceService is { SupportsHoldToTalk: true };

    /// <summary>
    /// 按住录音：开始录音（内部先请求权限）。返回 false 表示未开始（原因已通过 <see cref="ErrorOccurred"/> 通知）。
    /// </summary>
    public async Task<bool> StartHoldRecordingAsync()
    {
        if (_isRecognizing)
        {
            return false;
        }

        var nativeService = _voiceService;
        if (nativeService is null || !nativeService.SupportsHoldToTalk)
        {
            return false;
        }

        _isRecognizing = true;
        try
        {
            if (!await nativeService.RequestPermissionAsync())
            {
                await NotifyErrorAsync("未获得麦克风权限，请到系统设置中开启后重试");
                return false;
            }

            nativeService.PartialResultReceived -= OnNativePartialResult;
            nativeService.PartialResultReceived += OnNativePartialResult;

            var started = await nativeService.StartRecordingAsync();
            if (!started)
            {
                nativeService.PartialResultReceived -= OnNativePartialResult;
                await NotifyErrorAsync("无法启动录音，请检查设备后重试");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            nativeService.PartialResultReceived -= OnNativePartialResult;
            _isRecognizing = false;
            await NotifyErrorAsync($"无法启动录音：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 按住录音：结束录音并等待最终识别结果，结果通过 <see cref="VoiceTextReceived"/> / <see cref="ErrorOccurred"/> 返回。
    /// </summary>
    public async Task StopHoldRecordingAsync(CancellationToken cancellationToken = default)
    {
        var nativeService = _voiceService;
        if (nativeService is null)
        {
            return;
        }

        try
        {
            var text = await nativeService.StopRecordingAsync(cancellationToken);
            await PublishResultAsync(text);
        }
        catch (OperationCanceledException)
        {
            // 等待最终结果被取消，按未识别到内容处理
            await PublishResultAsync(null);
        }
        catch (Exception ex)
        {
            await NotifyErrorAsync($"语音识别失败：{ex.Message}");
        }
        finally
        {
            nativeService.PartialResultReceived -= OnNativePartialResult;
            _isRecognizing = false;
        }
    }

    /// <summary>按住录音：取消本次录音并丢弃结果（手指滑出按钮等取消手势），幂等</summary>
    public async Task CancelHoldRecordingAsync()
    {
        var nativeService = _voiceService;
        if (nativeService is null)
        {
            return;
        }

        try
        {
            await nativeService.CancelRecordingAsync();
        }
        catch
        {
            // 取消失败不影响页面状态恢复
        }
        finally
        {
            nativeService.PartialResultReceived -= OnNativePartialResult;
            _isRecognizing = false;
        }
    }

    /// <summary>原生服务部分识别文本 → 页面事件转发</summary>
    private void OnNativePartialResult(string text)
    {
        var handler = PartialTextReceived;
        _ = handler is null ? Task.CompletedTask : handler(text);
    }

    /// <summary>发布识别结果（null/空白视为未识别到内容）</summary>
    private async Task PublishResultAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            await NotifyErrorAsync("没有听到说话内容，请重试");
            return;
        }

        var handler = VoiceTextReceived;
        if (handler is not null)
        {
            await handler(text.Trim());
        }
    }

    private async Task NotifyErrorAsync(string message)
    {
        var handler = ErrorOccurred;
        if (handler is not null)
        {
            await handler(message);
        }
    }

    /// <summary>JS 回调：识别到文本</summary>
    [JSInvokable]
    public Task OnVoiceText(string text) => PublishResultAsync(text);

    /// <summary>JS 回调：识别失败</summary>
    [JSInvokable]
    public Task OnError(string message) => NotifyErrorAsync(message);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_moduleTask.IsValueCreated)
            {
                var module = await _moduleTask.Value;
                await module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // 电路已断开，模块引用由运行时回收
        }

        _dotNetReference?.Dispose();
    }
}