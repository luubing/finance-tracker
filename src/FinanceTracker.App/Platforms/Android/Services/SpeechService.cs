using Android.Content;
using Android.OS;
using Android.Speech;
using FinanceTracker.Core.Interfaces;
using Microsoft.Maui.ApplicationModel;

namespace FinanceTracker.App.Platforms.Android.Services;

/// <summary>
/// Android 语音识别服务：基于系统 SpeechRecognizer（中文，具体识别引擎取决于设备厂商/Google 语音服务）。
/// 参考 SmsService 的做法：权限 API 与识别器创建统一调度到主线程调用。
/// </summary>
public class SpeechService : ISpeechService
{
    /// <summary>识别语言</summary>
    private const string SpeechLocale = "zh-CN";

    /// <summary>持续录制模式下等待最终识别结果的上限（秒）：松开按钮后识别器应很快产出结果</summary>
    private const int FinalizeTimeoutSeconds = 10;

    /// <summary>当前识别器引用（用于超时/取消时停止识别）</summary>
    private SpeechRecognizer? _recognizer;

    /// <summary>按住录音使用的识别器（与一次性识别分离，避免相互销毁）</summary>
    private SpeechRecognizer? _holdRecognizer;

    /// <summary>按住录音的最终结果通知（松开按钮后等待）</summary>
    private TaskCompletionSource<string?>? _holdFinalTcs;

    /// <summary>最终结果是否已回调（避免超时兜底时误取旧部分结果）</summary>
    private volatile bool _holdFinalReceived;

    /// <summary>按住录音期间收到的最新部分识别文本</summary>
    private volatile string? _latestPartial;

    public bool SupportsHoldToTalk => true;

    /// <summary>持续录制过程中的部分识别文本（按住录音时实时上屏）</summary>
    public event Action<string>? PartialResultReceived;

    public bool IsAvailable => SpeechRecognizer.IsRecognitionAvailable(Platform.AppContext);

    public async Task<bool> RequestPermissionAsync()
    {
        try
        {
            // Permissions.RequestAsync 会弹出系统授权对话框并等待用户选择后返回，
            // 必须在主线程发起（Blazor 组件常运行在后台线程），与 SmsService 保持一致
            var status = await MainThread.InvokeOnMainThreadAsync(() => Permissions.RequestAsync<Permissions.Microphone>());
            return status == PermissionStatus.Granted;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"请求麦克风权限失败: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> RecognizeOnceAsync(CancellationToken cancellationToken = default)
    {
        var activity = Platform.CurrentActivity;
        if (activity is null || !IsAvailable)
        {
            return null;
        }

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // SpeechRecognizer 必须在主线程创建和使用
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var recognizer = SpeechRecognizer.CreateSpeechRecognizer(activity);
            _recognizer = recognizer;

            if (recognizer is null)
            {
                tcs.TrySetResult(null);
                return;
            }

            var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
            intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
            intent.PutExtra(RecognizerIntent.ExtraLanguage, SpeechLocale);
            intent.PutExtra(RecognizerIntent.ExtraPartialResults, false);

            recognizer.SetRecognitionListener(new RecognitionListener(
                onResults: results =>
                {
                    var text = results?.GetString(SpeechRecognizer.ResultsRecognition);
                    tcs.TrySetResult(string.IsNullOrWhiteSpace(text) ? null : text);
                },
                onError: _ => tcs.TrySetResult(null)));

            recognizer.StartListening(intent);
        });

        try
        {
            // 页面层传入 15 秒取消令牌，此处再兜底 20 秒超时，防止识别器异常挂起
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            var recognizer = _recognizer;
            _recognizer = null;
            if (recognizer is not null)
            {
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(() => recognizer.Destroy());
                }
                catch
                {
                    // 忽略销毁异常
                }
            }
        }
    }

    /// <summary>按住录音：创建识别器并开始持续监听（对应 UI 上的按下动作）</summary>
    public async Task<bool> StartRecordingAsync()
    {
        var activity = Platform.CurrentActivity;
        if (activity is null || !IsAvailable)
        {
            return false;
        }

        if (_holdRecognizer is not null)
        {
            // 已在录音中，幂等处理
            return true;
        }

        var finalTcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _holdFinalTcs = finalTcs;
        _holdFinalReceived = false;
        _latestPartial = null;

        var started = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            try
            {
                // SpeechRecognizer 必须在主线程创建和使用
                var recognizer = SpeechRecognizer.CreateSpeechRecognizer(activity);
                if (recognizer is null)
                {
                    return false;
                }

                _holdRecognizer = recognizer;
                recognizer.SetRecognitionListener(CreateListener(finalTcs));

                var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
                intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
                intent.PutExtra(RecognizerIntent.ExtraLanguage, SpeechLocale);
                intent.PutExtra(RecognizerIntent.ExtraPartialResults, true);
                recognizer.StartListening(intent);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Android 语音识别：开始录音失败 {ex.Message}");
                return false;
            }
        });

        if (!started)
        {
            _holdRecognizer = null;
            _holdFinalTcs = null;
        }

        return started;
    }

    /// <summary>松开按钮：停止监听并等待识别器产出最终结果</summary>
    public async Task<string?> StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        var recognizer = _holdRecognizer;
        var finalTcs = _holdFinalTcs;
        if (recognizer is null || finalTcs is null)
        {
            return null;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            try
            {
                recognizer.StopListening();
            }
            catch
            {
                // 部分厂商引擎不支持 StopListening，忽略；由引擎自动断句回调结果
            }
        });

        try
        {
            var text = await finalTcs.Task.WaitAsync(TimeSpan.FromSeconds(FinalizeTimeoutSeconds), cancellationToken);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception)
        {
            // 等待超时（引擎未回调最终结果）时，尽力返回最近的部分识别文本
            return _holdFinalReceived ? null : _latestPartial;
        }
        finally
        {
            await DestroyHoldRecognizerAsync();
        }
    }

    /// <summary>取消录音：丢弃结果（手指滑出按钮等取消手势），幂等</summary>
    public async Task CancelRecordingAsync()
    {
        await DestroyHoldRecognizerAsync(destroy: true);
    }

    /// <summary>销毁按住录音使用的识别器（主线程），幂等</summary>
    private async Task DestroyHoldRecognizerAsync(bool destroy = false)
    {
        var recognizer = _holdRecognizer;
        _holdRecognizer = null;
        _holdFinalTcs = null;

        if (recognizer is null)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            try
            {
                if (destroy)
                {
                    recognizer.Cancel();
                }
                recognizer.Destroy();
            }
            catch
            {
                // 忽略取消/销毁异常
            }
        });
    }

    /// <summary>构建识别监听器：关注最终结果、部分结果与错误，其余回调为空实现</summary>
    private RecognitionListener CreateListener(TaskCompletionSource<string?> finalTcs)
    {
        return new RecognitionListener(
            onResults: results =>
            {
                var text = results?.GetString(SpeechRecognizer.ResultsRecognition);
                _holdFinalReceived = true;
                finalTcs.TrySetResult(string.IsNullOrWhiteSpace(text) ? null : text);
            },
            onPartialResults: partialResults =>
            {
                var text = partialResults?.GetString(SpeechRecognizer.ResultsRecognition);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _latestPartial = text;
                    PartialResultReceived?.Invoke(text);
                }
            },
            onError: error =>
            {
                System.Diagnostics.Debug.WriteLine($"Android 语音识别错误: {error}");
                _holdFinalReceived = true;
                finalTcs.TrySetResult(null);
            });
    }

    /// <summary>
    /// 识别监听器：只关心最终结果与错误，其余回调为空实现
    /// </summary>
    private sealed class RecognitionListener : Java.Lang.Object, IRecognitionListener
    {
        private readonly Action<Bundle?> _onResults;
        private readonly Action<Bundle?>? _onPartialResults;
        private readonly Action<SpeechRecognizerError> _onError;

        public RecognitionListener(Action<Bundle?> onResults, Action<SpeechRecognizerError> onError, Action<Bundle?>? onPartialResults = null)
        {
            _onResults = onResults;
            _onPartialResults = onPartialResults;
            _onError = onError;
        }

        public void OnResults(Bundle? results) => _onResults(results);

        public void OnPartialResults(Bundle? partialResults) => _onPartialResults?.Invoke(partialResults);

        public void OnError(SpeechRecognizerError error) => _onError(error);

        public void OnReadyForSpeech(Bundle? @params) { }

        public void OnBeginningOfSpeech() { }

        public void OnRmsChanged(float rmsdB) { }

        public void OnBufferReceived(byte[]? buffer) { }

        public void OnEndOfSpeech() { }

        public void OnEvent(int eventType, Bundle? @params) { }
    }
}