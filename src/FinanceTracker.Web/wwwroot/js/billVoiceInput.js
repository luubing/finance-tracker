// 语音录入：浏览器端 Web Speech API 封装（Chrome / Edge 支持，Firefox/Safari 部分支持）。
// 由 BillVoiceInputService 通过 JS interop 动态 import。
// 注意：Blazor Hybrid (MAUI) 的 WebView 不支持 Web Speech API，App 端由原生 SpeechService 提供。

/// <summary>
/// 当前浏览器是否支持语音识别
/// </summary>
export function isSupported() {
  return Boolean(window.SpeechRecognition || window.webkitSpeechRecognition);
}

/// <summary>
/// 启动一次语音识别。
/// 结果通过回调返回：成功 → dotNetRef.invokeMethodAsync('OnVoiceText', text)；
/// 失败 → dotNetRef.invokeMethodAsync('OnError', errorMessage)。
/// Promise 返回 true 表示已正常结束（无论成功失败），false 表示无法启动。
/// </summary>
export function startRecognition(dotNetRef) {
  return new Promise((resolve) => {
    const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!SR) {
      resolve(false);
      dotNetRef.invokeMethodAsync('OnError', '当前浏览器不支持语音识别，请使用 Chrome 或 Edge');
      return;
    }

    const recognition = new SR();
    recognition.lang = 'zh-CN';
    recognition.interimResults = false;
    recognition.maxAlternatives = 1;

    let settled = false;
    const finish = () => {
      if (settled) {
        return;
      }
      settled = true;
      try {
        recognition.stop();
      } catch {
        // 忽略停止异常
      }
      resolve(true);
    };

    const errorMessages = {
      'not-allowed': '麦克风权限被拒绝，请在浏览器设置中允许使用麦克风后重试',
      'service-not-allowed': '语音识别服务不可用，请检查系统麦克风权限',
      'no-speech': '没有听到说话内容，请重试',
      'audio-capture': '未检测到麦克风设备',
      network: '语音识别服务网络异常，请重试',
      aborted: '语音识别已取消',
    };

    recognition.onresult = (event) => {
      const transcript = event.results?.[0]?.[0]?.transcript ?? '';
      if (transcript.trim()) {
        dotNetRef.invokeMethodAsync('OnVoiceText', transcript.trim());
      } else {
        dotNetRef.invokeMethodAsync('OnError', '没有听到说话内容，请重试');
      }
      finish();
    };
    recognition.onerror = (event) => {
      const error = event?.error ?? 'unknown';
      dotNetRef.invokeMethodAsync('OnError', errorMessages[error] ?? `语音识别失败：${error}`);
      finish();
    };
    // 用户停止说话后自动触发；若 onresult/onerror 已处理则忽略
    recognition.onend = () => {
      if (!settled) {
        dotNetRef.invokeMethodAsync('OnError', '没有听到说话内容，请重试');
      }
      finish();
    };

    try {
      recognition.start();
    } catch (e) {
      resolve(false);
      dotNetRef.invokeMethodAsync('OnError', `无法启动语音识别：${e.message}`);
    }
  });
}
