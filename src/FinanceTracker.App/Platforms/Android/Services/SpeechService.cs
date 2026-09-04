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

    /// <summary>当前识别器引用（用于超时/取消时停止识别）</summary>
    private SpeechRecognizer? _recognizer;

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

    /// <summary>
    /// 识别监听器：只关心最终结果与错误，其余回调为空实现
    /// </summary>
    private sealed class RecognitionListener : Java.Lang.Object, IRecognitionListener
    {
        private readonly Action<Bundle?> _onResults;
        private readonly Action<SpeechRecognizerError> _onError;

        public RecognitionListener(Action<Bundle?> onResults, Action<SpeechRecognizerError> onError)
        {
            _onResults = onResults;
            _onError = onError;
        }

        public void OnResults(Bundle? results) => _onResults(results);

        public void OnError(SpeechRecognizerError error) => _onError(error);

        public void OnReadyForSpeech(Bundle? @params) { }

        public void OnBeginningOfSpeech() { }

        public void OnRmsChanged(float rmsdB) { }

        public void OnBufferReceived(byte[]? buffer) { }

        public void OnEndOfSpeech() { }

        public void OnPartialResults(Bundle? partialResults) { }

        public void OnEvent(int eventType, Bundle? @params) { }
    }
}