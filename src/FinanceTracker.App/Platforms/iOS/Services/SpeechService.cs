using AVFoundation;
using FinanceTracker.Core.Interfaces;
using Foundation;
using Microsoft.Maui.ApplicationModel;
using Speech;

namespace FinanceTracker.App.Platforms.iOS.Services;

/// <summary>
/// iOS 语音识别服务：基于 SFSpeechRecognizer（中文，识别在设备端或 Apple 服务端完成）。
/// 需要在 Info.plist 声明 NSMicrophoneUsageDescription 与 NSSpeechRecognitionUsageDescription。
/// </summary>
public class SpeechService : ISpeechService
{
    /// <summary>识别语言</summary>
    private const string SpeechLocale = "zh-CN";

    /// <summary>单次识别超时（秒）</summary>
    private const int RecognitionTimeoutSeconds = 20;

    public bool IsAvailable
    {
        get
        {
            var recognizer = SFSpeechRecognizer.FromLocale(new NSLocale(SpeechLocale));
            return recognizer?.Available == true;
        }
    }

    public async Task<bool> RequestPermissionAsync()
    {
        try
        {
            var micGranted = await AVAudioSession.SharedInstance().RequestRecordPermissionAsync();
            if (!micGranted)
            {
                return false;
            }

            var speechStatus = await SFSpeechRecognizer.RequestAuthorizationAsync();
            return speechStatus == SFSpeechRecognizerAuthorizationStatus.Authorized;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"请求语音识别权限失败: {ex.Message}");
            return false;
        }
    }

    public async Task<string?> RecognizeOnceAsync(CancellationToken cancellationToken = default)
    {
        var recognizer = SFSpeechRecognizer.FromLocale(new NSLocale(SpeechLocale));
        if (recognizer is null || !recognizer.Available)
        {
            return null;
        }

        var audioEngine = new AVAudioEngine();
        var request = new SFSpeechAudioBufferRecognitionRequest
        {
            ShouldReportPartialResults = false
        };

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recognitionTask = recognizer.GetRecognitionTask(request, (result, error) =>
        {
            // 最终结果：Finished 标记为 true 时返回完整转写文本
            if (result?.Finished == true)
            {
                var best = result.BestTranscription.FormattedString;
                tcs.TrySetResult(string.IsNullOrWhiteSpace(best) ? null : best);
            }
            else if (error is not null)
            {
                tcs.TrySetResult(null);
            }
        });

        // 音频会话与录音引擎需在主线程配置启动
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var session = AVAudioSession.SharedInstance();
            session.SetCategory(AVAudioSessionCategory.PlayAndRecord, AVAudioSessionCategoryOptions.DefaultToSpeaker);
            session.SetActive(true);

            var inputNode = audioEngine.InputNode;
            var recordingFormat = inputNode.GetBusOutputFormat(0);
            inputNode.InstallTapOnBus(0, 1024, recordingFormat, (buffer, _) => request.Append(buffer));
            audioEngine.Prepare();

            if (!audioEngine.StartAndReturnError(out var startError) || startError is not null)
            {
                tcs.TrySetResult(null);
            }
        });

        try
        {
            // 页面层传入 15 秒取消令牌，此处再兜底 20 秒超时
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(RecognitionTimeoutSeconds), cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                audioEngine.Stop();
                audioEngine.InputNode.RemoveTapOnBus(0);
                try
                {
                    AVAudioSession.SharedInstance().SetActive(false);
                }
                catch
                {
                    // 忽略会话释放异常
                }
            });
            request.EndAudio();
            recognitionTask?.Finish();
        }
    }
}