# -*- coding: utf-8 -*-
"""
E2E 测试：语音记账覆盖层（iOS UI 重构 · 17 号 ticket，手机视口）。

前置条件：
  1. pip install -r tests/e2e/requirements.txt && python -m playwright install chromium
  2. FinanceTracker.Web.Server 已用 http 启动配置运行：powershell tests/e2e/server.ps1 -Action start

无头浏览器里没有真实的麦克风与语音服务，所以这里在每个页面加载之前注入一个**假的 SpeechRecognition**（FAKE_SPEECH_JS）：
应用走的仍是真实的 billVoiceInput.js → BillVoiceInputService → 记一笔 Sheet → VoiceOverlay 全链路，
测试用 window.__speech.say(...) / fail(...) 代替「用户说话 / 识别器出错」。它模拟 Chrome 的行为：
stop() 之后把已听到的内容作为最终结果交出再 end；abort() 触发 aborted 错误再 end；最终结果之后识别器结束。

验证点（Blazor Server 宿主走「点按」形态；MAUI 原生「按住说话」形态只在真机上有，见 ticket 17 的人工验收清单）：

  A. 呈现与结构：点麦克风升起全屏覆盖层（盖住 Sheet 与它的所有控件，在 Sheet 之上）；状态「正在聆听…」+ 录音红点、
     转写区占位提示、波形（25 根，钟形、动画在跑）、芯片区预留、「说完后点一下麦克风结束」、品牌色大麦克风（104，水平居中）、说明、取消；
     对话框语义（role=dialog / aria-modal / 读屏名称）、焦点进入覆盖层；整屏玻璃层恰好 3 个（Sheet 标题栏 2 个 + 覆盖层 1 个）；
     样式分支（取消区泛红、按住形态不接收点按且手指下的麦克风仍收得到事件、淡出）——Blazor Server 里触发不了按住形态，用脚本改标记验证样式本身
  B. 实时转写：部分结果实时上屏，解析认出的金额 / 分类 / 支付渠道 / 时间用淡紫色 + 下划线高亮并列成芯片（金额、分类、渠道、时间的固定顺序）；
     这只是预览——表单不被预填；识别器修正前面的字后高亮与芯片跟着变；说得很长只显示末尾；转写与芯片出现时下面的布局不跳动
  C. 最终结果：标题「识别完成，正在预填…」，覆盖层停留片刻后收起，表单预填（金额 / 分类 / 支付渠道 / 时间 / 备注），
     Sheet 里出现「已按「…」预填账单」与「已识别：…」；保存后数据来源为语音识别、同步状态待同步，金额 / 分类 / 渠道 / 账本 / 时间一致
  D. 点「说完了」：覆盖层转入「正在识别…」（红点与波形停住、大麦克风置灰、手势提示撤掉），识别器收到 stop() 后交出最终结果并预填；
     一句话没说就点「说完了」→ Toast「没有听到说话内容」、覆盖层收起
  E. 取消：点「取消」/ Esc → 覆盖层收起，识别器被 abort()，不预填、不提示；取消之后识别器迟到的最终结果被丢弃；可以重新开始
  F. 失败：识别器报错（麦克风被拒绝）/ 启动失败 → Toast 说明、覆盖层收起、麦克风可再次使用，手动填写不受影响
  G. 缺金额：「未能识别金额」Toast；其余已识别字段（分类 / 渠道 / 时间）仍预填，保存键保持置灰；不算语音预填成功——
     手动补上金额再保存，库里的数据来源是手动录入而不是语音识别
  H. 收入：识别出收入 → 类型切换为收入、分类取收入分类、金额芯片带「收入」前缀
  I. 减弱动态效果：红点 / 波形 / 光圈静态，覆盖层与内容照常显示
  J. 降低透明度：覆盖层不模糊，改为不透明底，仍可读

只覆盖网页层：真实麦克风、MAUI 原生按住说话（按住 / 上滑取消 / 滑开取消 / 权限弹窗）、识别服务的网络环境见 ticket 17 的人工验收清单。
"""
import sys
from datetime import datetime, timedelta, timezone

from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

from e2e_base import (
    BILL_TYPE_EXPENSE,
    PHONE_VIEWPORT,
    SOURCE_MANUAL,
    SOURCE_VOICE,
    SYNC_PENDING,
    LocalDb,
    TestRun,
    amount_text,
    login,
    open_add_bill_sheet,
    open_app,
    pick_ledger,
    press_keys,
    require_server,
    session,
    shot,
    wait_animations_settled,
)

SHEET = '[data-testid="addbill-sheet"]'
VIEW_W = PHONE_VIEWPORT["width"]
VIEW_H = PHONE_VIEWPORT["height"]

# 用预设分类 / 渠道的全名来说：口语同义词表按短名（餐饮、交通……）建，预设名是「餐饮美食」「工资薪酬」，全名一定命中
SPEECH_EXPENSE = "昨天餐饮美食花了三十五块，用微信支付"
SPEECH_INCOME = "今天收到工资薪酬八千块"
SPEECH_NO_AMOUNT = "昨天餐饮美食，用微信支付"

TOAST_JS = "(t) => [...document.querySelectorAll('.m-enqueued-snackbars .m-snack__wrapper')].some((e) => e.textContent.includes(t))"

# ---------------------------------------------------------------------------------------------- 假的 SpeechRecognition

FAKE_SPEECH_JS = r"""(() => {
  const instances = [];
  class FakeSpeechRecognition {
    constructor() {
      this.lang = '';
      this.interimResults = false;
      this.continuous = false;
      this.maxAlternatives = 1;
      this.onresult = null;
      this.onerror = null;
      this.onend = null;
      this.state = 'new';   // new → listening → ended
      this.calls = [];      // 应用对识别器做过的调用：start / stop / abort
      this.heard = '';      // 到目前为止「听到」的内容：stop() 时作为最终结果交出
      instances.push(this);
    }
    start() {
      this.calls.push('start');
      if (window.__speechStartError) throw new Error(window.__speechStartError);
      this.state = 'listening';
    }
    stop() {
      this.calls.push('stop');
      if (this.state !== 'listening') return;
      setTimeout(() => {
        if (this.state !== 'listening') return;
        if (this.heard) this._result(this.heard, true);
        this._end();
      }, 20);
    }
    abort() {
      this.calls.push('abort');
      if (this.state !== 'listening') return;
      setTimeout(() => {
        if (this.state !== 'listening') return;
        this._fire('onerror', { error: 'aborted' });
        this._end();
      }, 20);
    }
    _result(text, isFinal) {
      this.heard = text;
      const result = Object.assign([{ transcript: text, confidence: 0.9 }], { isFinal });
      this._fire('onresult', { resultIndex: 0, results: [result] });
    }
    _end() {
      if (this.state === 'ended') return;
      this.state = 'ended';
      this._fire('onend', {});
    }
    _fire(name, event) {
      if (typeof this[name] === 'function') this[name](event);
    }
  }
  window.SpeechRecognition = FakeSpeechRecognition;
  window.webkitSpeechRecognition = FakeSpeechRecognition;
  window.__speech = {
    instances,
    last: () => instances[instances.length - 1],
    // 用户说了一段话：isFinal=false 是实时的部分结果；true 是最终结果（之后识别器结束）
    say: (text, isFinal) => {
      const rec = window.__speech.last();
      rec._result(text, !!isFinal);
      if (isFinal) rec._end();
    },
    // 识别器出错（如 not-allowed / no-speech / network）
    fail: (error) => {
      const rec = window.__speech.last();
      rec._fire('onerror', { error });
      rec._end();
    },
  };
})();"""

# ---------------------------------------------------------------------------------------------- 页面里的取数脚本

RECTS_JS = """() => {
  const rect = (id) => {
    const el = document.querySelector(`[data-testid="${id}"]`);
    if (!el) return null;
    const b = el.getBoundingClientRect();
    return { x: b.x, y: b.y, w: b.width, h: b.height, bottom: b.bottom, right: b.right };
  };
  const out = {};
  for (const id of ['voice-overlay', 'voice-status', 'voice-transcript', 'voice-wave', 'voice-chips', 'voice-hint', 'voice-finish', 'voice-foot', 'voice-cancel']) out[id] = rect(id);
  return out;
}"""

STYLES_JS = """() => {
  const q = (sel) => document.querySelector(sel);
  const cs = (el, pseudo) => getComputedStyle(el, pseudo);
  const overlay = cs(q('[data-testid="voice-overlay"]'));
  const bar = cs(q('.ft-voice__bar'));
  const dot = cs(q('.ft-voice__dot'));
  const mic = q('.ft-voice__mic');
  return {
    bg: overlay.backgroundColor,
    filter: overlay.backdropFilter || overlay.webkitBackdropFilter || 'none',
    z: overlay.zIndex,
    position: overlay.position,
    color: overlay.color,
    barAnim: bar.animationName,
    barState: bar.animationPlayState,
    dotAnim: dot.animationName,
    dotState: dot.animationPlayState,
    ringBefore: cs(mic, '::before').animationName,
    ringAfter: cs(mic, '::after').animationName,
    micBg: cs(mic).backgroundImage,
    micFilter: cs(mic).backdropFilter || 'none',
  };
}"""

# 直接压在覆盖层上的小字：状态 / 转写占位 / 手势提示 / 底部说明 / 点按形态的「取消」
SMALL_TEXT_JS = """() => ['[data-testid="voice-status"]', '.ft-voice__placeholder', '[data-testid="voice-hint"]', '[data-testid="voice-foot"]', '[data-testid="voice-cancel"]'].map((sel) => {
  const el = document.querySelector(sel);
  if (!el) return { sel, missing: true };
  const cs = getComputedStyle(el);
  return { sel, size: parseFloat(cs.fontSize), weight: parseInt(cs.fontWeight, 10) };
})"""

BARS_JS = """() => [...document.querySelectorAll('.ft-voice__bar')].map((b) => ({
  h: parseFloat(b.style.getPropertyValue('--ft-wave-h')),
  delay: b.style.getPropertyValue('--ft-wave-delay'),
}))"""

CHIPS_JS = """() => [...document.querySelectorAll('[data-testid="voice-chip"]')].map((c) => ({
  kind: c.dataset.kind, text: c.textContent.trim(), icon: (c.querySelector('i')?.className || '').replace('mdi ', ''),
  h: c.getBoundingClientRect().height, backdrop: getComputedStyle(c).backdropFilter || 'none',
}))"""

MARKS_JS = """() => [...document.querySelectorAll('[data-testid="voice-mark"]')].map((m) => ({
  kind: m.dataset.kind, text: m.textContent,
  underline: getComputedStyle(m).textDecorationLine, color: getComputedStyle(m).color,
}))"""

COVER_JS = """() => {
  const inside = (sel) => {
    const el = document.querySelector(sel);
    if (!el) return null;
    const b = el.getBoundingClientRect();
    const hit = document.elementFromPoint(b.x + b.width / 2, b.y + b.height / 2);
    return !!hit && !!hit.closest('[data-testid="voice-overlay"]');
  };
  return {
    mic: inside('[data-testid="addbill-voice"]'),
    close: inside('[data-testid="sheet-close"]'),
    save: inside('[data-testid="addbill-save"]'),
    key5: inside('[data-testid="key-5"]'),
  };
}"""

# 屏幕上（视口内、可见、有尺寸）带 backdrop-filter 的元素
GLASS_LAYERS_JS = """() => {
  const out = [];
  for (const el of document.querySelectorAll('*')) {
    const cs = getComputedStyle(el);
    const bf = cs.backdropFilter || cs.webkitBackdropFilter;
    if (!bf || bf === 'none' || cs.visibility === 'hidden' || cs.display === 'none') continue;
    const r = el.getBoundingClientRect();
    if (r.width < 2 || r.height < 2 || r.bottom < 0 || r.top > innerHeight || r.right < 0 || r.left > innerWidth) continue;
    out.push(el.getAttribute('data-testid') || String(el.className).split(' ').filter((c) => c.startsWith('ft-'))[0] || el.tagName);
  }
  return out;
}"""

# 在动作之前装好观察器：覆盖层的「识别完成，正在预填…」只停留不到半秒，轮询很可能错过，所以记录「曾经出现过」
SEEN_OBSERVER_JS = """() => {
  window.__voiceSeen = { titles: [], phases: [], chips: [], marks: [] };
  const note = () => {
    const seen = window.__voiceSeen;
    const title = document.querySelector('[data-testid="voice-title"]');
    if (title && !seen.titles.includes(title.textContent)) seen.titles.push(title.textContent);
    const overlay = document.querySelector('[data-testid="voice-overlay"]');
    if (overlay) {
      const phase = overlay.getAttribute('data-phase');
      if (!seen.phases.includes(phase)) seen.phases.push(phase);
    }
    for (const chip of document.querySelectorAll('[data-testid="voice-chip"]')) {
      const key = chip.dataset.kind + ':' + chip.textContent.trim();
      if (!seen.chips.includes(key)) seen.chips.push(key);
    }
    for (const mark of document.querySelectorAll('[data-testid="voice-mark"]')) {
      const key = mark.dataset.kind + ':' + mark.textContent;
      if (!seen.marks.includes(key)) seen.marks.push(key);
    }
  };
  new MutationObserver(note).observe(document.body, { subtree: true, childList: true, characterData: true, attributes: true, attributeFilter: ['data-phase'] });
  note();
}"""

# ---------------------------------------------------------------------------------------------- 小工具


def tid(page, testid: str):
    return page.get_by_test_id(testid)


def poll(page, expression: str, arg=None, timeout: int = 5000) -> bool:
    """在页面里反复求值直到为真；超时返回 False（让断言记失败，而不是抛异常打断后面的用例）。"""
    try:
        page.wait_for_function(expression, arg=arg, timeout=timeout)
        return True
    except PlaywrightTimeoutError:
        return False


def toast(page, text: str, timeout: int = 8000) -> bool:
    return poll(page, TOAST_JS, arg=text, timeout=timeout)


def wait_no_toasts(page, timeout: int = 20000) -> bool:
    return poll(page, "() => !document.querySelector('.m-enqueued-snackbars .m-snack__wrapper')", timeout=timeout)


def visible(locator, timeout: int = 4000) -> bool:
    try:
        locator.first.wait_for(state="visible", timeout=timeout)
        return True
    except PlaywrightTimeoutError:
        return False


def gone(locator, timeout: int = 4000) -> bool:
    try:
        locator.first.wait_for(state="detached", timeout=timeout)
        return True
    except PlaywrightTimeoutError:
        return False


def attr_is(page, selector: str, name: str, value, timeout: int = 4000) -> bool:
    """等元素属性等于 value（None 表示属性不存在）；超时返回 False。"""
    return poll(
        page,
        """([sel, name, value]) => {
            const el = document.querySelector(sel);
            if (!el) return false;
            return value === null ? !el.hasAttribute(name) : el.getAttribute(name) === value;
        }""",
        arg=[selector, name, value],
        timeout=timeout,
    )


def overlay(page):
    return tid(page, "voice-overlay")


def say(page, text: str, final: bool = False) -> None:
    page.evaluate("([t, f]) => window.__speech.say(t, f)", [text, final])


def fail(page, error: str) -> None:
    page.evaluate("(e) => window.__speech.fail(e)", error)


def calls(page, index: int = -1) -> "list[str]":
    return page.evaluate("(i) => (window.__speech.instances.at(i) || { calls: [] }).calls", index)


def instance_count(page) -> int:
    return page.evaluate("() => window.__speech.instances.length")


def transcript_is(page, text: str, timeout: int = 5000) -> bool:
    return poll(
        page,
        "(t) => (document.querySelector('[data-testid=\"voice-transcript\"]')?.textContent || '').trim() === t",
        arg=text,
        timeout=timeout,
    )


def chips_are(page, kinds: "list[str]", timeout: int = 5000) -> bool:
    return poll(
        page,
        "(k) => JSON.stringify([...document.querySelectorAll('[data-testid=\"voice-chip\"]')].map((c) => c.dataset.kind)) === JSON.stringify(k)",
        arg=kinds,
        timeout=timeout,
    )


def reset(page) -> None:
    """整页重新进入首页：丢掉上一段用例留下的 Sheet / 表单 / 假识别器状态。"""
    open_app(page, "/")
    tid(page, "tab-bar").wait_for(state="visible")


def start_listening(page):
    """打开记一笔 Sheet → 点麦克风 → 等覆盖层升起、假识别器开始收音。返回覆盖层。"""
    open_add_bill_sheet(page)
    before = instance_count(page)
    tid(page, "addbill-voice").click()
    ov = overlay(page)
    ov.wait_for(state="visible")
    poll(page, "(n) => window.__speech.instances.length === n + 1 && window.__speech.last().state === 'listening'", arg=before)
    wait_animations_settled(page)
    return ov


def row_value(page, testid: str) -> str:
    return tid(page, testid).locator(".ft-cell__value").inner_text().strip()


def selected_category(page) -> str:
    tile = page.locator('[data-testid="category-tile"][data-selected="true"]')
    return tile.first.get_attribute("data-name") if tile.count() else ""


def utc_text_to_local(text: str) -> datetime:
    return datetime.strptime(text.split(".")[0], "%Y-%m-%d %H:%M:%S").replace(tzinfo=timezone.utc).astimezone().replace(tzinfo=None)


def approx(a: float, b: float, tol: float = 1.0) -> bool:
    return abs(a - b) <= tol


def amount_equals(value, expected: float) -> bool:
    try:
        return abs(float(value) - expected) < 1e-9
    except (TypeError, ValueError):
        return False


# ---------------------------------------------------------------------------------------------- A. 呈现与结构


def check_structure(run: TestRun, page) -> dict:
    """A 节。结束时覆盖层仍开着（B 节接着用）；返回「空状态」下各区域的位置，供 B 节核对布局不跳动。"""
    reset(page)
    open_add_bill_sheet(page)
    mic = tid(page, "addbill-voice")
    run.check(
        "麦克风：点按形态（浏览器 Web Speech）、可用、空闲",
        mic.get_attribute("data-mode") == "tap" and mic.get_attribute("aria-disabled") is None and mic.get_attribute("data-state") == "idle",
        f"{mic.get_attribute('data-mode')} / {mic.get_attribute('aria-disabled')} / {mic.get_attribute('data-state')}",
    )
    run.check("开始之前没有覆盖层", overlay(page).count() == 0)

    mic.click()
    ov = overlay(page)
    run.check("点麦克风 → 升起语音覆盖层", visible(ov))
    poll(page, "() => window.__speech.instances.length === 1 && window.__speech.last().state === 'listening'")
    wait_animations_settled(page)
    shot(page, "voice-overlay-listening")

    info = page.evaluate("() => { const r = window.__speech.last(); return { n: window.__speech.instances.length, lang: r.lang, interim: r.interimResults, cont: r.continuous, calls: r.calls }; }")
    run.check("启动了一次识别：中文、开启实时转写（interimResults）、不连续、只调用了 start", info["n"] == 1 and info["lang"] == "zh-CN" and info["interim"] is True and info["cont"] is False and info["calls"] == ["start"], str(info))
    run.check("麦克风进入 listening 状态", attr_is(page, '[data-testid="addbill-voice"]', "data-state", "listening"))

    run.check(
        "对话框语义：role=dialog、aria-modal、读屏名称「语音记账」",
        ov.get_attribute("role") == "dialog" and ov.get_attribute("aria-modal") == "true" and ov.get_attribute("aria-label") == "语音记账",
    )
    run.check("点按形态：data-mode=tap、data-phase=listening，没有取消 / 淡出标记", ov.get_attribute("data-mode") == "tap" and ov.get_attribute("data-phase") == "listening" and ov.get_attribute("data-cancelling") is None and ov.get_attribute("data-leaving") is None)
    run.check("焦点进入覆盖层（Esc 才收得到）", poll(page, "() => document.activeElement === document.querySelector('[data-testid=\"voice-overlay\"]')"))

    run.check("状态行「正在聆听…」，是 role=status（读屏播报）", tid(page, "voice-title").inner_text().strip() == "正在聆听…" and tid(page, "voice-status").get_attribute("role") == "status")
    run.check("转写区是占位提示（data-empty=true），提示怎么说", tid(page, "voice-transcript").get_attribute("data-empty") == "true" and "昨天午饭花了三十五块，用微信支付" in tid(page, "voice-transcript").inner_text())
    run.check("没有已识别芯片", tid(page, "voice-chip").count() == 0)
    run.check("手势提示「说完后点一下麦克风结束」、底部说明、取消", tid(page, "voice-hint").inner_text().strip() == "说完后点一下麦克风结束" and tid(page, "voice-foot").inner_text().strip() == "识别结果将预填到表单，确认后才会保存" and tid(page, "voice-cancel").inner_text().strip() == "取消")
    finish = tid(page, "voice-finish")
    run.check("大麦克风是「说完了」按钮（点按形态），读屏名称写明作用", finish.is_visible() and finish.get_attribute("aria-label") == "说完了，结束并识别" and finish.evaluate("(el) => el.tagName") == "BUTTON")

    bars = page.evaluate(BARS_JS)
    middle = bars[len(bars) // 2]["h"] if bars else 0
    run.check("波形 25 根，每根都有运行时变量（高度比例、动画偏移）", len(bars) == 25 and all(0.12 <= b["h"] <= 1 and b["delay"].endswith("ms") for b in bars), str(bars[:3]))
    run.check("波形中间高、两边低（钟形），不是一排等高条", middle > bars[0]["h"] and middle > bars[-1]["h"] and len({b["h"] for b in bars}) > 8, f"{bars[0]['h']} / {middle} / {bars[-1]['h']}")

    styles = page.evaluate(STYLES_JS)
    run.check("覆盖层：固定定位、层级 127（高于 Sheet 120 / 弹出菜单 125）、白字", styles["position"] == "fixed" and styles["z"] == "127" and styles["color"] == "rgb(255, 255, 255)", str(styles))
    run.check("覆盖层底色是浅磨砂的靛紫 60%（rgba(44, 36, 100, 0.6)，第 2 轮评审：贴近设计稿的灰紫），带 28px 模糊", styles["bg"] == "rgba(44, 36, 100, 0.6)" and "blur(28px)" in styles["filter"], f"{styles['bg']} / {styles['filter']}")
    small = page.evaluate(SMALL_TEXT_JS)
    run.check(
        "直接压在浅磨砂上的小字（状态 / 占位 / 提示 / 说明 / 取消）都是大号文字：≥ 18.66px 且 ≥ 700（白字约 4:1，只能按大号文字的 3:1 要求）",
        all(not s.get("missing") and s["size"] >= 18.66 and s["weight"] >= 700 for s in small),
        str(small),
    )
    run.check("动画在跑：红点呼吸、波形起伏、光圈外扩", styles["dotAnim"] == "ft-voice-dot" and styles["dotState"] == "running" and styles["barAnim"] == "ft-voice-bar" and styles["barState"] == "running" and styles["ringBefore"] == "ft-voice-ring" and styles["ringAfter"] == "ft-voice-ring", str(styles))
    run.check("大麦克风是品牌渐变圆钮，自己不模糊", "linear-gradient" in styles["micBg"] and styles["micFilter"] == "none", f"{styles['micBg']} / {styles['micFilter']}")

    rects = page.evaluate(RECTS_JS)
    o = rects["voice-overlay"]
    run.check("覆盖层盖满整个视口", approx(o["x"], 0) and approx(o["y"], 0) and approx(o["w"], VIEW_W) and approx(o["h"], VIEW_H), str(o))
    f = rects["voice-finish"]
    run.check("大麦克风 104 × 104、水平居中", approx(f["w"], 104) and approx(f["h"], 104) and approx(f["x"] + f["w"] / 2, VIEW_W / 2), str(f))
    order = ["voice-status", "voice-transcript", "voice-wave", "voice-chips", "voice-hint", "voice-finish", "voice-foot", "voice-cancel"]
    run.check("自上而下的顺序：状态 → 转写 → 波形 → 芯片 → 提示 → 麦克风 → 说明 → 取消，彼此不重叠", all(rects[a]["bottom"] <= rects[b]["y"] + 1 for a, b in zip(order, order[1:])), str({k: (round(v["y"]), round(v["bottom"])) for k, v in rects.items() if v}))
    run.check("全部在视口内（底部的取消没被挤出屏幕）", all(rects[k]["y"] >= 0 and rects[k]["bottom"] <= VIEW_H and rects[k]["x"] >= 0 and rects[k]["right"] <= VIEW_W for k in order), str({k: round(rects[k]["bottom"]) for k in order}))
    run.check("转写区固定 3 行高（114），芯片区预留 2 行（≥ 86）", approx(rects["voice-transcript"]["h"], 114) and rects["voice-chips"]["h"] >= 86 - 0.5, f"{rects['voice-transcript']['h']} / {rects['voice-chips']['h']}")
    run.check("「取消」热区 ≥ 44", rects["voice-cancel"]["h"] >= 44 - 0.5 and rects["voice-cancel"]["w"] >= 44 - 0.5, str(rects["voice-cancel"]))

    cover = page.evaluate(COVER_JS)
    run.check("覆盖层盖住了 Sheet 的标题栏按钮、保存键和数字键盘（点这些位置命中的是覆盖层）", all(v is True for v in cover.values()), str(cover))

    glass = page.evaluate(GLASS_LAYERS_JS)
    run.check("玻璃层预算：整屏恰好 3 层（Sheet 标题栏 2 个圆钮 + 覆盖层），芯片 / 按钮不模糊", sorted(glass) == ["addbill-voice", "sheet-close", "voice-overlay"], str(glass))

    # 按住形态 / 取消区 / 淡出这几个样式分支在 Blazor Server 宿主里触发不了（没有原生按住录音，那是 MAUI 真机的事），
    # 这里用脚本直接改标记来验证样式本身：Blazor 只在渲染树变化时才改 DOM，没变的属性不会被它复位
    branches = page.evaluate(
        """async () => {
          const el = document.querySelector('[data-testid="voice-overlay"]');
          const read = () => { const cs = getComputedStyle(el); return { bg: cs.backgroundColor, pe: cs.pointerEvents, anim: cs.animationName }; };
          // 等背景色过渡真的走完。不能用固定睡眠：过渡从「改属性后的第一次样式重算」才开始计时，机器忙、首帧晚了几十毫秒时，
          // 固定睡眠结束时过渡还差一截（读到 rgba(99, 26, 43, 0.796) 这样的中间值）。getAnimations() 会先刷新样式让过渡立刻建立，再等它们 finished
          const settle = () => Promise.all(el.getAnimations().map((a) => a.finished.catch(() => undefined)));
          const micCenter = () => { const b = document.querySelector('[data-testid="addbill-voice"]').getBoundingClientRect(); return [b.x + b.width / 2, b.y + b.height / 2]; };
          const out = { base: read() };
          el.setAttribute('data-cancelling', 'true'); await settle(); out.cancelling = read();
          el.removeAttribute('data-cancelling'); await settle();
          el.setAttribute('data-mode', 'hold'); out.hold = read();
          const [x, y] = micCenter();
          out.holdHit = document.elementFromPoint(x, y)?.closest('[data-testid]')?.getAttribute('data-testid') || null;
          el.setAttribute('data-mode', 'tap');
          el.setAttribute('data-leaving', 'true'); out.leaving = read();
          el.removeAttribute('data-leaving');
          return out;
        }"""
    )
    run.check("取消区（data-cancelling）：整层泛红 rgba(110, 24, 32, 0.62)", branches["cancelling"]["bg"] == "rgba(110, 24, 32, 0.62)" and branches["base"]["bg"] == "rgba(44, 36, 100, 0.6)", str(branches))
    run.check("按住形态（data-mode=hold）：覆盖层不接收点按，手指下的麦克风按钮仍能收到事件", branches["hold"]["pe"] == "none" and branches["holdHit"] == "addbill-voice" and branches["base"]["pe"] != "none", str(branches))
    run.check("淡出（data-leaving）：不再响应点按，播放淡出动画", branches["leaving"]["pe"] == "none" and branches["leaving"]["anim"] == "ft-voice-out", str(branches["leaving"]))
    wait_animations_settled(page)
    run.check("样式分支验证完毕后恢复原状（点按形态、聆听、可点按）", overlay(page).get_attribute("data-mode") == "tap" and overlay(page).get_attribute("data-leaving") is None and page.evaluate(STYLES_JS)["bg"] == "rgba(44, 36, 100, 0.6)")
    return rects


# ---------------------------------------------------------------------------------------------- B. 实时转写


def check_live_preview(run: TestRun, page, empty_rects: dict) -> None:
    """B 节：接着 A 节开着的覆盖层。"""
    page.evaluate(SEEN_OBSERVER_JS)

    say(page, "昨天", False)
    run.check("部分结果实时上屏：转写区显示「昨天」，占位提示消失", transcript_is(page, "昨天") and tid(page, "voice-transcript").get_attribute("data-empty") == "false")
    run.check("认出的「昨天」高亮为时间，并出现时间芯片", chips_are(page, ["time"]) and [(m["kind"], m["text"]) for m in page.evaluate(MARKS_JS)] == [("time", "昨天")])

    say(page, SPEECH_EXPENSE, False)
    run.check("继续说：转写区实时更新为完整的一句", transcript_is(page, SPEECH_EXPENSE))
    run.check("四项都识别：芯片按「金额、分类、支付渠道、时间」的固定顺序出现", chips_are(page, ["amount", "category", "channel", "time"]))
    wait_animations_settled(page)  # 芯片出现时有 180ms 的弹入缩放，量尺寸要等它结束
    chips = page.evaluate(CHIPS_JS)
    texts = {c["kind"]: c["text"] for c in chips}
    run.check("芯片文案：¥35.00 / 餐饮美食 / 微信支付 / 昨天 HH:mm", texts.get("amount") == "¥35.00" and texts.get("category") == "餐饮美食" and texts.get("channel") == "微信支付" and texts.get("time", "").startswith("昨天 "), str(texts))
    run.check("芯片图标：金额 ¥、时间时钟，分类 / 渠道用各自的图标", chips[0]["icon"] == "mdi-currency-cny" and chips[3]["icon"] == "mdi-clock-outline" and chips[1]["icon"].startswith("mdi-") and chips[2]["icon"].startswith("mdi-"), str([c["icon"] for c in chips]))
    run.check("芯片高 38，且不模糊（不占玻璃层）", all(abs(c["h"] - 38) <= 0.5 and c["backdrop"] == "none" for c in chips), str([(c["h"], c["backdrop"]) for c in chips]))

    marks = page.evaluate(MARKS_JS)
    run.check(
        "高亮的片段：昨天（时间）、餐饮美食（分类）、三十五块（金额）、微信支付（渠道）",
        [(m["kind"], m["text"]) for m in marks] == [("time", "昨天"), ("category", "餐饮美食"), ("amount", "三十五块"), ("channel", "微信支付")],
        str(marks),
    )
    run.check("高亮样式：淡紫色 + 下划线（不只靠颜色）", all(m["underline"] == "underline" and m["color"] == "rgb(234, 230, 255)" for m in marks), str(marks))
    font = page.evaluate("() => { const cs = getComputedStyle(document.querySelector('.ft-voice__text')); return { size: cs.fontSize, weight: cs.fontWeight, color: cs.color }; }")
    run.check("转写文字：28px 粗体白字", font["size"] == "28px" and int(font["weight"]) >= 800 and font["color"] == "rgb(255, 255, 255)", str(font))
    shot(page, "voice-overlay-live")

    run.check("只是预览：表单没有被预填（金额仍为空、Sheet 里没有预填提示）", amount_text(page) == "" and tid(page, "addbill-voice-status").count() == 0)
    run.check("仍在聆听阶段", overlay(page).get_attribute("data-phase") == "listening")

    now = page.evaluate(RECTS_JS)
    stable = all(approx(now[k]["y"], empty_rects[k]["y"], 0.6) and approx(now[k]["h"], empty_rects[k]["h"], 0.6) for k in ("voice-status", "voice-transcript", "voice-wave", "voice-chips", "voice-hint", "voice-finish", "voice-foot", "voice-cancel"))
    run.check("转写与芯片出现时下面的布局不跳动（各区域位置与高度和空状态一致）", stable, str({k: (round(now[k]["y"]), round(empty_rects[k]["y"])) for k in now if now[k]}))

    say(page, "昨天餐饮美食花了", False)
    run.check("识别器修正前面的字：「三十五块」没了，金额的高亮与芯片跟着消失", transcript_is(page, "昨天餐饮美食花了") and chips_are(page, ["category", "time"]) and "amount" not in [m["kind"] for m in page.evaluate(MARKS_JS)])

    long_text = "昨天餐饮美食花了三十五块，用微信支付，还有很多很多很多很多很多别的话要说"
    say(page, long_text, False)
    tail = long_text[-30:]
    run.check(
        "说得很长：只显示末尾 30 个字，前面用「…」省略（区域固定 3 行，不撑高布局）",
        transcript_is(page, "…" + tail) and approx(page.evaluate(RECTS_JS)["voice-transcript"]["h"], 114, 0.6),
        (page.get_by_test_id("voice-transcript").text_content() or "").strip(),
    )

    # 回到完整的一句，给 C 节做最终结果
    say(page, SPEECH_EXPENSE, False)
    transcript_is(page, SPEECH_EXPENSE)


# ---------------------------------------------------------------------------------------------- C. 最终结果 → 预填 → 保存


def check_final_and_save(run: TestRun, page, db: LocalDb, user_id: str, ledger_name: str) -> None:
    """C 节：接着 B 节开着的覆盖层。"""
    page.evaluate(SEEN_OBSERVER_JS)
    say(page, SPEECH_EXPENSE, True)

    run.check(
        "最终结果：标题经过「识别完成，正在预填…」，阶段经过 parsing",
        poll(page, "() => window.__voiceSeen.titles.includes('识别完成，正在预填…') && window.__voiceSeen.phases.includes('parsing')", timeout=4000),
        str(page.evaluate("() => window.__voiceSeen")),
    )
    run.check("覆盖层停留片刻后收起（淡出后从页面移除）", gone(overlay(page), 4000))
    run.check("麦克风回到空闲", attr_is(page, '[data-testid="addbill-voice"]', "data-state", "idle"))

    run.check("表单预填：金额 35", amount_text(page) == "35", str(amount_text(page)))
    run.check("表单预填：分类「餐饮美食」", selected_category(page) == "餐饮美食", selected_category(page))
    run.check("表单预填：支付渠道「微信支付」", row_value(page, "channel-row") == "微信支付", row_value(page, "channel-row"))
    time_text = row_value(page, "time-row")
    seen_time = [c.split(":", 1)[1] for c in page.evaluate("() => window.__voiceSeen.chips") if c.startswith("time:")]
    run.check("表单预填：时间是昨天，且与覆盖层上的时间芯片同一个文案", time_text.startswith("昨天 ") and time_text in seen_time, f"{time_text} / {seen_time}")
    run.check("表单预填：备注是说的那句话", tid(page, "addbill-note").input_value() == SPEECH_EXPENSE, tid(page, "addbill-note").input_value())

    status = tid(page, "addbill-voice-status").inner_text() if tid(page, "addbill-voice-status").count() else ""
    run.check("Sheet 里出现「已按「…」预填账单，请确认后保存」", f"已按「{SPEECH_EXPENSE}」预填账单，请确认后保存" in status, status)
    hint = tid(page, "addbill-voice-hint").inner_text() if tid(page, "addbill-voice-hint").count() else ""
    run.check("并列出识别到的字段「已识别：金额、分类「…」、支付渠道「…」、交易时间」", hint == "已识别：金额、分类「餐饮美食」、支付渠道「微信支付」、交易时间", hint)
    shot(page, "voice-prefilled")

    pick_ledger(page, ledger_name)
    run.check("选了账本后保存键点亮", attr_is(page, '[data-testid="addbill-save"]', "aria-disabled", None))
    tid(page, "addbill-save").click()
    run.check("保存后 Sheet 收起", gone(page.locator(SHEET), 8000))

    # 备注是固定的一句话（不像别的用例带时间戳），本机库里别的测试用户可能留有同样备注的账单，所以只看当前用户的
    saved = [b for b in db.bills_of_user(user_id) if b.get("Note") == SPEECH_EXPENSE]
    run.check("当前用户的账单里恰有一笔该备注的账单", len(saved) == 1, str(len(saved)))
    bill = saved[0] if saved else {}
    run.check("金额 35、类型支出", amount_equals(bill.get("Amount"), 35) and bill.get("Type") == BILL_TYPE_EXPENSE, str(bill.get("Amount")))
    run.check("分类 / 支付渠道 / 账本与预填一致", bill.get("CategoryId") == db.category_id_by_name("餐饮美食") and bill.get("PaymentChannelId") == db.channel_id_by_name("微信支付") and bill.get("LedgerId") == db.ledger_id_by_name(user_id, ledger_name))
    run.check("数据来源为语音识别、同步状态待同步、未软删除", bill.get("Source") == SOURCE_VOICE and bill.get("SyncStatus") == SYNC_PENDING and bill.get("IsDeleted") == 0, f"{bill.get('Source')} / {bill.get('SyncStatus')}")
    when = utc_text_to_local(bill["TransactionTime"]) if bill else datetime.now()
    yesterday = (datetime.now() - timedelta(days=1)).date()
    run.check("交易时间是昨天（按应用时区）", when.date() == yesterday, str(when))


# ---------------------------------------------------------------------------------------------- D. 点「说完了」


def check_finish(run: TestRun, page) -> None:
    reset(page)
    start_listening(page)
    page.evaluate(SEEN_OBSERVER_JS)
    say(page, SPEECH_EXPENSE, False)
    transcript_is(page, SPEECH_EXPENSE)

    tid(page, "voice-finish").click()
    run.check(
        "点「说完了」：覆盖层转入 parsing，标题「正在识别…」",
        poll(page, "() => window.__voiceSeen.phases.includes('parsing') && window.__voiceSeen.titles.includes('正在识别…')", timeout=4000),
        str(page.evaluate("() => window.__voiceSeen")),
    )
    run.check("应用对识别器调用了 stop()（让它交出已听到的内容）", "stop" in calls(page), str(calls(page)))
    run.check("识别器交出最终结果后覆盖层收起、表单预填", gone(overlay(page), 6000) and amount_text(page) == "35" and selected_category(page) == "餐饮美食", f"{amount_text(page)} / {selected_category(page)}")

    # 一句话没说就点「说完了」
    wait_no_toasts(page)
    reset(page)
    start_listening(page)
    tid(page, "voice-finish").click()
    run.check("没说话就点「说完了」：Toast「没有听到说话内容」", toast(page, "没有听到说话内容"))
    run.check("覆盖层收起，表单原样（金额为空、没有预填提示）", gone(overlay(page), 4000) and amount_text(page) == "" and tid(page, "addbill-voice-status").count() == 0)
    run.check("麦克风回到空闲，可以再来一次", attr_is(page, '[data-testid="addbill-voice"]', "data-state", "idle"))

    # parsing 阶段的细节：红点与波形停住、大麦克风置灰、手势提示撤掉
    wait_no_toasts(page)
    reset(page)
    start_listening(page)
    # 让识别器「卡住」：stop() 之后不交结果，覆盖层就停在「正在识别…」
    page.evaluate("() => { const r = window.__speech.last(); r.stop = function () { this.calls.push('stop'); }; }")
    say(page, SPEECH_EXPENSE, False)
    transcript_is(page, SPEECH_EXPENSE)
    tid(page, "voice-finish").click()
    attr_is(page, '[data-testid="voice-overlay"]', "data-phase", "parsing")
    styles = page.evaluate(STYLES_JS)
    run.check("识别中：红点与波形停住（paused）", styles["dotState"] == "paused" and styles["barState"] == "paused", f"{styles['dotState']} / {styles['barState']}")
    run.check("识别中：大麦克风置灰（aria-disabled）、手势提示撤掉、仍可取消", tid(page, "voice-finish").get_attribute("aria-disabled") == "true" and tid(page, "voice-hint").get_attribute("data-empty") == "true" and tid(page, "voice-cancel").is_visible())
    tid(page, "voice-finish").click(force=True)
    run.check("识别中再点大麦克风：不重复触发", "stop" in calls(page) and calls(page).count("stop") == 1, str(calls(page)))
    shot(page, "voice-overlay-parsing")
    tid(page, "voice-cancel").click()
    run.check("识别中点「取消」：覆盖层收起", gone(overlay(page), 4000))


# ---------------------------------------------------------------------------------------------- E. 取消


def check_cancel(run: TestRun, page) -> None:
    wait_no_toasts(page)
    reset(page)
    start_listening(page)
    say(page, SPEECH_EXPENSE, False)
    transcript_is(page, SPEECH_EXPENSE)

    tid(page, "voice-cancel").click()
    run.check("点「取消」：覆盖层收起", gone(overlay(page), 4000))
    run.check("识别器被 abort()", poll(page, "() => window.__speech.instances[0].calls.includes('abort')"), str(calls(page, 0)))
    page.wait_for_timeout(800)
    run.check("取消是静默的：没有 Toast、不预填、没有预填提示", not page.evaluate("() => !!document.querySelector('.m-enqueued-snackbars .m-snack__wrapper')") and amount_text(page) == "" and tid(page, "addbill-voice-status").count() == 0)

    page.evaluate("(t) => window.__speech.instances[0]._result(t, true)", "花了五块")
    page.wait_for_timeout(900)
    run.check("取消之后识别器迟到的最终结果被丢弃（不预填、覆盖层不会再弹出）", amount_text(page) == "" and overlay(page).count() == 0 and tid(page, "addbill-voice-status").count() == 0)
    run.check("麦克风回到空闲", attr_is(page, '[data-testid="addbill-voice"]', "data-state", "idle"))

    # 可以重新开始：新的识别器
    tid(page, "addbill-voice").click()
    overlay(page).wait_for(state="visible")
    poll(page, "() => window.__speech.instances.length === 2 && window.__speech.last().state === 'listening'")
    run.check("取消之后可以重新开始：新建了第二个识别器，覆盖层又是干净的聆听状态", instance_count(page) == 2 and calls(page) == ["start"] and tid(page, "voice-transcript").get_attribute("data-empty") == "true" and tid(page, "voice-chip").count() == 0)

    # Esc 同样取消
    wait_animations_settled(page)
    page.keyboard.press("Escape")
    run.check("按 Esc：覆盖层收起", gone(overlay(page), 4000))
    run.check("Esc 同样 abort() 了识别器", poll(page, "() => window.__speech.instances[1].calls.includes('abort')"), str(calls(page, 1)))
    run.check("Esc 关的是覆盖层，不是下面的记一笔 Sheet", page.locator(SHEET).count() == 1 and visible(page.locator(SHEET)))


# ---------------------------------------------------------------------------------------------- F. 失败


def check_failures(run: TestRun, page) -> None:
    wait_no_toasts(page)
    reset(page)
    start_listening(page)
    fail(page, "not-allowed")
    run.check("识别器报「麦克风被拒绝」：Toast 说明原因", toast(page, "麦克风权限被拒绝"))
    run.check("覆盖层收起、麦克风回到空闲，手动填写不受影响（数字键盘可用）", gone(overlay(page), 4000) and attr_is(page, '[data-testid="addbill-voice"]', "data-state", "idle") and tid(page, "keypad").is_visible())

    wait_no_toasts(page)
    page.evaluate("() => { window.__speechStartError = 'boom'; }")
    tid(page, "addbill-voice").click()
    run.check("识别器启动失败：Toast「无法启动语音识别：boom」", toast(page, "无法启动语音识别：boom"))
    run.check("启动失败时覆盖层不留在屏幕上", gone(overlay(page), 4000) and attr_is(page, '[data-testid="addbill-voice"]', "data-state", "idle"))

    wait_no_toasts(page)
    page.evaluate("() => { window.__speechStartError = null; }")
    tid(page, "addbill-voice").click()
    overlay(page).wait_for(state="visible")
    say(page, "", True)
    run.check("最终结果为空：Toast「没有听到说话内容」、覆盖层收起", toast(page, "没有听到说话内容") and gone(overlay(page), 4000))


# ---------------------------------------------------------------------------------------------- G. 缺金额


def check_no_amount(run: TestRun, page, db: LocalDb, user_id: str, ledger_name: str) -> None:
    wait_no_toasts(page)
    reset(page)
    start_listening(page)
    page.evaluate(SEEN_OBSERVER_JS)
    say(page, SPEECH_NO_AMOUNT, False)
    run.check("缺金额的一句话：只有分类、渠道、时间三枚芯片（没有金额）", chips_are(page, ["category", "channel", "time"]))
    say(page, SPEECH_NO_AMOUNT, True)

    run.check("Toast「未能识别金额」", toast(page, "未能识别金额"))
    run.check("覆盖层收起", gone(overlay(page), 4000))
    run.check("其余已识别字段仍预填：分类、渠道、时间", selected_category(page) == "餐饮美食" and row_value(page, "channel-row") == "微信支付" and row_value(page, "time-row").startswith("昨天 "), f"{selected_category(page)} / {row_value(page, 'channel-row')} / {row_value(page, 'time-row')}")
    run.check("金额仍为空，保存键保持置灰", amount_text(page) == "" and attr_is(page, '[data-testid="addbill-save"]', "aria-disabled", "true"))
    hint = tid(page, "addbill-voice-hint").inner_text() if tid(page, "addbill-voice-hint").count() else ""
    status = tid(page, "addbill-voice-status").inner_text() if tid(page, "addbill-voice-status").count() else ""
    run.check("提示里只列出识别到的字段（没有「金额」），也没有「已按…预填账单」", "金额" not in hint and "分类「餐饮美食」" in hint and "预填账单" not in status, f"{hint} / {status}")

    # 缺金额不算语音预填成功：用户手动补上金额再保存，数据来源是手动录入，而不是语音识别
    wait_no_toasts(page)
    press_keys(page, "20")
    pick_ledger(page, ledger_name)
    run.check("手动补上金额、选了账本后保存键点亮", attr_is(page, '[data-testid="addbill-save"]', "aria-disabled", None))
    tid(page, "addbill-save").click()
    run.check("保存后 Sheet 收起", gone(page.locator(SHEET), 8000))
    saved = [b for b in db.bills_of_user(user_id) if b.get("Note") == SPEECH_NO_AMOUNT]
    bill = saved[0] if saved else {}
    run.check(
        "缺金额的那次语音不算预填成功：保存后数据来源是手动录入（不是语音识别），金额 20，分类 / 渠道仍是识别出来的",
        len(saved) == 1
        and bill.get("Source") == SOURCE_MANUAL
        and amount_equals(bill.get("Amount"), 20)
        and bill.get("CategoryId") == db.category_id_by_name("餐饮美食")
        and bill.get("PaymentChannelId") == db.channel_id_by_name("微信支付"),
        f"{len(saved)} / {bill.get('Source')} / {bill.get('Amount')}",
    )


# ---------------------------------------------------------------------------------------------- H. 收入


def check_income(run: TestRun, page) -> None:
    wait_no_toasts(page)
    reset(page)
    start_listening(page)
    page.evaluate(SEEN_OBSERVER_JS)
    say(page, SPEECH_INCOME, False)
    run.check("收入：金额芯片带「收入」前缀并千分位（收入 ¥8,000.00）", poll(page, "() => window.__voiceSeen.chips.includes('amount:收入 ¥8,000.00')", timeout=4000), str(page.evaluate("() => window.__voiceSeen.chips")))
    say(page, SPEECH_INCOME, True)
    run.check("覆盖层收起，类型切换为收入", gone(overlay(page), 4000) and attr_is(page, SHEET, "data-type", "income"))
    run.check("分类取收入分类「工资薪酬」，金额 8000", selected_category(page) == "工资薪酬" and amount_text(page) == "8000", f"{selected_category(page)} / {amount_text(page)}")


# ---------------------------------------------------------------------------------------------- I. 减弱动态效果 / J. 降低透明度


def check_reduced_motion(run: TestRun, db: LocalDb) -> None:
    with session(reduced_motion=True) as s:
        s.context.add_init_script(FAKE_SPEECH_JS)
        page = s.page
        login(page)
        start_listening(page)
        say(page, SPEECH_EXPENSE, False)
        transcript_is(page, SPEECH_EXPENSE)
        styles = page.evaluate(STYLES_JS)
        run.check("【减弱动态效果】红点 / 波形 / 光圈都不再动画", styles["dotAnim"] == "none" and styles["barAnim"] == "none" and styles["ringBefore"] == "none" and styles["ringAfter"] == "none", str(styles))
        run.check("【减弱动态效果】覆盖层、转写、芯片照常显示", overlay(page).is_visible() and tid(page, "voice-chip").count() == 4 and tid(page, "voice-mark").count() == 4)
        bar_transform = page.evaluate("() => getComputedStyle(document.querySelectorAll('.ft-voice__bar')[12]).transform")
        run.check("【减弱动态效果】波形是静态的钟形（中间那根按它的高度比例缩放，不是 none）", bar_transform not in ("none", "matrix(1, 0, 0, 1, 0, 0)"), bar_transform)
        tid(page, "voice-cancel").click()
        run.check("【减弱动态效果】取消照常工作", gone(overlay(page), 4000))
        run.check_clean(s.errors, "【减弱动态效果】会话无页面异常 / 控制台错误 / 失败请求")


def check_reduced_transparency(run: TestRun) -> None:
    with session(reduced_transparency=True) as s:
        s.context.add_init_script(FAKE_SPEECH_JS)
        page = s.page
        login(page)
        start_listening(page)
        say(page, SPEECH_EXPENSE, False)
        transcript_is(page, SPEECH_EXPENSE)
        styles = page.evaluate(STYLES_JS)
        run.check("【降低透明度】覆盖层不模糊，底是不透明的深靛紫（rgb(58, 49, 120)）", styles["filter"] == "none" and styles["bg"] == "rgb(58, 49, 120)", f"{styles['filter']} / {styles['bg']}")
        run.check("【降低透明度】文字仍是白色，高亮与芯片照常", styles["color"] == "rgb(255, 255, 255)" and tid(page, "voice-mark").count() == 4 and tid(page, "voice-chip").count() == 4)
        glass = page.evaluate(GLASS_LAYERS_JS)
        run.check("【降低透明度】覆盖层不占玻璃层", "voice-overlay" not in glass, str(glass))
        shot(page, "voice-overlay-reduced-transparency")
        tid(page, "voice-cancel").click()
        gone(overlay(page), 4000)
        run.check_clean(s.errors, "【降低透明度】会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- 主流程


def main() -> int:
    require_server()
    run = TestRun("voice-overlay")
    db = LocalDb()
    db.verify_schema()

    with session() as s:
        s.context.add_init_script(FAKE_SPEECH_JS)
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_name = f"语音账本{datetime.now():%H%M%S}"
        db.seed_ledger(user_id, ledger_name)

        empty_rects = check_structure(run, page)
        check_live_preview(run, page, empty_rects)
        check_final_and_save(run, page, db, user_id, ledger_name)
        check_finish(run, page)
        check_cancel(run, page)
        check_failures(run, page)
        check_no_amount(run, page, db, user_id, ledger_name)
        check_income(run, page)

        run.check_clean(s.errors, "A–H 节会话无页面异常 / 控制台错误 / 失败请求")

    check_reduced_motion(run, db)
    check_reduced_transparency(run)

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
