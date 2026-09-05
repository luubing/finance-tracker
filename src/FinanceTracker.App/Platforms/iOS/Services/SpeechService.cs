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

    /// <summary>持续录制模式下等待最终识别结果的上限（秒）：松开按钮后识别器应很快产出最终结果</summary>
    private const int FinalizeTimeoutSeconds = 10;

    /// <summary>录音状态锁（保护下列字段与开始/结束/取消的并发竞争）</summary>
    private readonly object _recordingGate = new();

    private SFSpeechRecognizer? _recordingRecognizer;
    private AVAudioEngine? _recordingEngine;
    private SFSpeechAudioBufferRecognitionRequest? _recordingRequest;
    private SFSpeechRecognitionTask? _recordingTask;
    private TaskCompletionSource<string?>? _finalResultTcs;
    public bool SupportsHoldToTalk => true;

    /// <summary>持续录制过程中的部分识别文本（按住录音时实时上屏）</summary>
    public event Action<string>? PartialResultReceived;

    public bool IsAvailable
    {
        get
        {
            try
            {
                var locale = new NSLocale(SpeechLocale);
                var recognizer = new SFSpeechRecognizer(locale);
                return recognizer?.Available == true;
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<bool> RequestPermissionAsync()
    {
        try
        {
            // 请求麦克风权限
            var micGranted = await RequestMicrophonePermissionAsync();
            if (!micGranted)
            {
                return false;
            }

            // 请求语音识别权限
            var speechStatus = await RequestSpeechAuthorizationAsync();
            return speechStatus == SFSpeechRecognizerAuthorizationStatus.Authorized;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"请求语音识别权限失败: {ex.Message}");
            return false;
        }
    }

    private Task<bool> RequestMicrophonePermissionAsync()
    {
        // iOS 17+ 上 AVAudioSession.RequestRecordPermission 已过时，改用 AVAudioApplication；
        // 旧版本回退到 AVAudioSession 的回调 API。
        if (OperatingSystem.IsIOSVersionAtLeast(17))
        {
            return AVAudioApplication.RequestRecordPermissionAsync();
        }

        var tcs = new TaskCompletionSource<bool>();
        AVAudioSession.SharedInstance().RequestRecordPermission(granted =>
        {
            tcs.TrySetResult(granted);
        });
        return tcs.Task;
    }

    private Task<SFSpeechRecognizerAuthorizationStatus> RequestSpeechAuthorizationAsync()
    {
        var tcs = new TaskCompletionSource<SFSpeechRecognizerAuthorizationStatus>();
        SFSpeechRecognizer.RequestAuthorization(status =>
        {
            tcs.TrySetResult(status);
        });
        return tcs.Task;
    }

    /// <summary>
    /// 按住录音：配置音频会话、创建识别任务并开始采集麦克风输入。
    /// 识别过程中部分结果通过 <see cref="PartialResultReceived"/> 实时回调。
    /// </summary>
    public async Task<bool> StartRecordingAsync()
    {
        lock (_recordingGate)
        {
            if (_recordingTask is not null)
            {
                // 已在录音中，幂等处理
                return true;
            }
        }

        try
        {
            var locale = new NSLocale(SpeechLocale);
            var recognizer = new SFSpeechRecognizer(locale);
            if (recognizer is null || !recognizer.Available)
            {
                System.Diagnostics.Debug.WriteLine("iOS 语音识别：识别器不可用（语言资源未下载或设备不支持）");
                return false;
            }

            var request = new SFSpeechAudioBufferRecognitionRequest
            {
                ShouldReportPartialResults = true
            };
            var finalTcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var engine = new AVAudioEngine();
            var started = false;

            // 音频会话与录音引擎需在主线程配置启动
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var session = AVAudioSession.SharedInstance();
                session.SetCategory(AVAudioSessionCategory.PlayAndRecord, AVAudioSessionCategoryOptions.DefaultToSpeaker);
                session.SetActive(true);

                var inputNode = engine.InputNode;
                var recordingFormat = inputNode.GetBusOutputFormat(0);
                inputNode.InstallTapOnBus(0, 1024, recordingFormat, (buffer, _) => request.Append(buffer));
                engine.Prepare();

                started = engine.StartAndReturnError(out var startError) && startError is null;
                if (!started)
                {
                    System.Diagnostics.Debug.WriteLine($"iOS 语音识别：录音引擎启动失败 {startError?.LocalizedDescription}");
                    inputNode.RemoveTapOnBus(0);
                }
            });

            if (!started)
            {
                request.EndAudio();
                return false;
            }

            var task = recognizer.GetRecognitionTask(request, (result, error) =>
            {
                if (error is not null)
                {
                    System.Diagnostics.Debug.WriteLine($"iOS 语音识别：识别出错 {error.LocalizedDescription}");
                    finalTcs.TrySetResult(null);
                    return;
                }

                if (result is null)
                {
                    return;
                }

                var best = result.BestTranscription.FormattedString;
                if (result.Final)
                {
                    finalTcs.TrySetResult(string.IsNullOrWhiteSpace(best) ? null : best);
                }
                else if (!string.IsNullOrWhiteSpace(best))
                {
                    PartialResultReceived?.Invoke(best);
                }
            });

            lock (_recordingGate)
            {
                _recordingRecognizer = recognizer;
                _recordingEngine = engine;
                _recordingRequest = request;
                _recordingTask = task;
                _finalResultTcs = finalTcs;
            }
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"iOS 语音识别：开始录音失败 {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 松开按钮：显式结束音频流促使识别器立即产出最终结果，并等待识别完成。
    /// </summary>
    public async Task<string?> StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        Task<string?>? waitTask;
        lock (_recordingGate)
        {
            if (_recordingTask is null || _finalResultTcs is null)
            {
                return null;
            }

            waitTask = _finalResultTcs.Task;

            // 关键：缓冲流式识别必须显式结束音频流，识别器才会产出最终结果
            // （此前一次性识别从不主动 EndAudio，导致回调永不触发、一直超时"没有听到说话内容"）
            _recordingRequest?.EndAudio();
            _recordingRequest = null;

            _recordingTask = null;
        }

        try
        {
            var text = await waitTask.WaitAsync(
                TimeSpan.FromSeconds(FinalizeTimeoutSeconds), cancellationToken);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            await CleanupRecordingAsync();
        }
    }

    /// <summary>取消录音：停止引擎并丢弃结果（手指滑出按钮等取消手势），幂等</summary>
    public Task CancelRecordingAsync()
    {
        lock (_recordingGate)
        {
            _finalResultTcs?.TrySetResult(null);
            _finalResultTcs = null;
            _recordingTask = null;
            _recordingRequest = null;
        }

        return CleanupRecordingAsync();
    }

    /// <summary>停止并释放录音引擎（必须在主线程操作 AVAudioEngine），幂等</summary>
    private async Task CleanupRecordingAsync()
    {
        AVAudioEngine? engine;
        SFSpeechRecognitionTask? task;
        lock (_recordingGate)
        {
            engine = _recordingEngine;
            task = _recordingTask;
            _recordingEngine = null;
            _recordingTask = null;
        }

        if (engine is not null)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                engine.Stop();
                engine.InputNode.RemoveTapOnBus(0);
                try
                {
                    AVAudioSession.SharedInstance().SetActive(false);
                }
                catch
                {
                    // 忽略会话释放异常
                }
            });
        }

        try
        {
            task?.Finish();
        }
        catch
        {
            // 忽略任务结束异常
        }
    }

    public async Task<string?> RecognizeOnceAsync(CancellationToken cancellationToken = default)
    {
        var locale = new NSLocale(SpeechLocale);
        var recognizer = new SFSpeechRecognizer(locale);
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
            // 最终结果：Final 标记为 true 时返回完整转写文本
            if (result?.Final == true)
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
