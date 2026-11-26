// 语音录入：浏览器端 Web Speech API 封装（Chrome / Edge 支持，Firefox/Safari 部分支持）。
// 由 BillVoiceInputService 通过 JS interop 动态 import。
// 注意：Blazor Hybrid (MAUI) 的 WebView 不支持 Web Speech API，App 端由原生 SpeechService 提供。

// 当前正在进行的一次识别（同一时刻最多一次）：stopRecognition / cancelRecognition 作用于它
let active = null;

/// <summary>
/// 回调 .NET。.NET 一侧的引用已释放 / 电路已断开（Sheet 被关、页面离开）时调用会被拒绝：
/// 此时识别结果已经没有人要了，吞掉拒绝，免得在控制台留下未处理的 Promise 拒绝。
/// </summary>
function notify(dotNetRef, method, argument) {
  try {
    dotNetRef.invokeMethodAsync(method, argument)?.catch?.(() => {});
  } catch {
    // 同步抛出（引用已被释放）同样忽略
  }
}

/// <summary>
/// 当前浏览器是否支持语音识别
/// </summary>
export function isSupported() {
  return Boolean(window.SpeechRecognition || window.webkitSpeechRecognition);
}

/// <summary>
/// 启动一次语音识别。
/// 识别过程中的部分结果（实时转写）→ dotNetRef.invokeMethodAsync('OnPartialText', text)；
/// 最终结果 → dotNetRef.invokeMethodAsync('OnVoiceText', text)；
/// 失败 → dotNetRef.invokeMethodAsync('OnError', errorMessage)。
/// Promise 返回 true 表示已正常结束（无论成功失败、是否被取消），false 表示无法启动。
/// </summary>
export function startRecognition(dotNetRef) {
  return new Promise((resolve) => {
    const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!SR) {
      resolve(false);
      notify(dotNetRef, 'OnError', '当前浏览器不支持语音识别，请使用 Chrome 或 Edge');
      return;
    }

    const recognition = new SR();
    recognition.lang = 'zh-CN';
    // 实时转写：说话过程中不断给出部分结果，覆盖层据此实时显示文字与识别出的字段；不连续识别，停顿后给出最终结果并结束
    recognition.interimResults = true;
    recognition.continuous = false;
    recognition.maxAlternatives = 1;

    let settled = false;
    const state = { recognition, cancelled: false, finish: null };
    const finish = () => {
      if (settled) {
        return;
      }
      settled = true;
      if (active === state) {
        active = null;
      }
      try {
        recognition.stop();
      } catch {
        // 忽略停止异常
      }
      resolve(true);
    };
    state.finish = finish;
    active = state;

    const errorMessages = {
      'not-allowed': '麦克风权限被拒绝，请在浏览器设置中允许使用麦克风后重试',
      'service-not-allowed': '语音识别服务不可用，请检查系统麦克风权限',
      'no-speech': '没有听到说话内容，请重试',
      'audio-capture': '未检测到麦克风设备',
      network: '语音识别服务网络异常，请重试',
      aborted: '语音识别已取消',
    };

    recognition.onresult = (event) => {
      if (state.cancelled) {
        return;
      }

      // 把已有的所有片段拼起来：中间的片段是已确定的，最后一段可能还是临时结果
      let transcript = '';
      for (let i = 0; i < event.results.length; i++) {
        transcript += event.results[i]?.[0]?.transcript ?? '';
      }
      transcript = transcript.trim();

      const last = event.results[event.results.length - 1];
      const isFinal = last ? Boolean(last.isFinal) : true;
      if (!isFinal) {
        if (transcript) {
          notify(dotNetRef, 'OnPartialText', transcript);
        }
        return;
      }

      if (transcript) {
        notify(dotNetRef, 'OnVoiceText', transcript);
      } else {
        notify(dotNetRef, 'OnError', '没有听到说话内容，请重试');
      }
      finish();
    };
    recognition.onerror = (event) => {
      if (!state.cancelled) {
        const error = event?.error ?? 'unknown';
        notify(dotNetRef, 'OnError', errorMessages[error] ?? `语音识别失败：${error}`);
      }
      finish();
    };
    // 用户停止说话后自动触发；若 onresult/onerror 已处理则忽略
    recognition.onend = () => {
      if (!settled && !state.cancelled) {
        notify(dotNetRef, 'OnError', '没有听到说话内容，请重试');
      }
      finish();
    };

    try {
      recognition.start();
    } catch (e) {
      if (active === state) {
        active = null;
      }
      settled = true;
      resolve(false);
      notify(dotNetRef, 'OnError', `无法启动语音识别：${e.message}`);
    }
  });
}

/// <summary>
/// 说完了：让正在进行的识别结束并给出已听到内容的最终结果（覆盖层里点大麦克风）。
/// 没有正在进行的识别时什么也不做。
/// </summary>
export function stopRecognition() {
  if (!active || active.cancelled) {
    return false;
  }

  try {
    active.recognition.stop();
    return true;
  } catch {
    return false;
  }
}

/// <summary>
/// 取消正在进行的识别并丢弃结果（覆盖层里点「取消」/ 按 Esc / 超时 / Sheet 被关）：不回调任何结果与错误。
/// 没有正在进行的识别时什么也不做。
/// </summary>
export function cancelRecognition() {
  const current = active;
  if (!current) {
    return false;
  }

  current.cancelled = true;
  try {
    current.recognition.abort();
  } catch {
    // 忽略取消异常
  }
  current.finish?.();
  return true;
}
