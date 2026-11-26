# -*- coding: utf-8 -*-
"""
E2E 测试：登录页（iOS UI 重构 · 22 号 ticket）——沉浸式渐变（铺到屏幕最上面）+ 玻璃 Logo + 底部白色圆角面板（手机号输入 + 「登录 / 注册」）。

只覆盖网页层——真机上的软键盘（键盘弹出后面板与按钮是否真的被顶到可视区内）、状态栏白色图标真正切换、自动填充 / 号码键盘的实际表现
见 ticket 22 的人工验收清单。认证本身没有改动（手机号手动输入、无验证码、首次使用自动注册、会话存在本机），这里验证它在新外观下行为不变。

  A. 外壳与结构：未登录访问首页被带到登录页；没有 Tab 栏；渐变铺满整屏（含状态栏之下）、声明深色背景 → 状态栏白色图标；
     应用名 / 标语 / 玻璃 Logo（居中）；白色面板（贴底、上圆角 40）；面板标题与说明（无需验证码、首次使用自动注册）；
     手机号输入框（tel / 数字键盘 / 11 位 / 读屏名称 / 字号 ≥ 16）；没有验证码输入；主按钮「登录 / 注册」；玻璃层 ≤ 3（只有 Logo 一层）；不再使用 MASA 表单控件
  B. 手机号校验：空 / 全空白 / 位数不对 / 不是 1 开头——红字提示（role=alert）、输入条红描边、焦点回到输入框、不登录；提示出现不挪动按钮（预留一行高度）；
     继续输入后旧提示消失；首尾空格先去掉再校验（10 位数字 + 空格不会被当成 11 位放行）；输入框最多 11 位（沿用旧页面）
  C. 登录成功：登录中按钮转圈并置灰（用 MutationObserver 抓这一瞬间）、回到首页（Tab 栏出现、登录页消失）、localStorage 写入 userId / phoneNumber、本机库里有该用户；回车同样提交；
     同一手机号再次登录是同一个用户（不重复注册）
  D. 沉浸式：模拟刘海（状态栏 47）+ 手势条（34）：品牌区让出状态栏、主按钮避开手势条、面板背景仍贴到屏幕底
  E. 软键盘（可视高度变小）：品牌区先被压缩，面板与按钮始终在可视范围内；极矮的可视高度下整页可滚动、滚到底按钮可见；恢复后布局复原
  F. 窄屏（320 × 568）：不溢出、品牌区完整、元素都在屏幕内
  G. 无障碍：控件的角色与名称、Tab 键顺序、提示与输入框的关联、标题层级

可观察属性（已登记到 README）：login-page（data-statusbar-bg）、login-title、login-panel、login-phone、login-error（role=alert）、login-submit。
"""
import sys

from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

from e2e_base import (
    GLASS_LAYERS_JS,
    PHONE_VIEWPORT,
    LocalDb,
    TestRun,
    active_tab,
    open_app,
    require_server,
    session,
    shot,
    tab_bar,
    unique_phone,
    wait_animations_settled,
    wait_tab_bar,
)

SCREEN_W = PHONE_VIEWPORT["width"]
SCREEN_H = PHONE_VIEWPORT["height"]

RGB_DANGER_TEXT = "rgb(211, 34, 26)"  # --ft-danger-text #D3221A
EMPTY_TEXT = "手机号不能为空"
LENGTH_TEXT = "手机号必须是11位"
FORMAT_TEXT = "手机号格式不正确"

# ---------------------------------------------------------------------------------------------- 页面状态

STATE_JS = r"""() => {
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  const box = (el) => { if (!el) return null; const r = el.getBoundingClientRect(); return { l: r.left, r: r.right, t: r.top, b: r.bottom, w: r.width, h: r.height }; };
  const cs = (el, prop) => (el ? getComputedStyle(el)[prop] : null);
  const text = (el) => (el ? el.textContent.replace(/\s+/g, ' ').trim() : null);
  const root = q('login-page');
  const panel = q('login-panel');
  const input = q('login-phone');
  const submit = q('login-submit');
  const error = q('login-error');
  const find = (selector) => (root ? root.querySelector(selector) : null);
  const field = find('.ft-login__field');
  const hero = find('.ft-login__hero');
  const logo = find('.ft-login__logo');
  const active = document.activeElement;
  return {
    path: location.pathname,
    exists: !!root,
    statusbarBg: root ? root.getAttribute('data-statusbar-bg') : null,
    icons: document.documentElement.getAttribute('data-statusbar-icons'),
    overflowX: document.documentElement.scrollWidth - window.innerWidth,
    inner: { w: window.innerWidth, h: window.innerHeight },
    root: box(root), hero: box(hero), panel: box(panel), field: box(field), input: box(input), submit: box(submit), error: box(error),
    logo: box(logo), title: box(q('login-title')), slogan: box(find('.ft-login__slogan')),
    titleText: text(q('login-title')),
    sloganText: text(find('.ft-login__slogan')),
    panelTitle: text(find('.ft-login__title')),
    hint: text(find('.ft-login__hint')),
    errorText: error ? error.textContent.trim() : null,
    errorColor: cs(error, 'color'),
    errorRole: error ? error.getAttribute('role') : null,
    errorId: error ? error.id : null,
    fieldError: field ? field.classList.contains('ft-login__field--error') : null,
    input_: input ? {
      type: input.type, inputmode: input.getAttribute('inputmode'), autocomplete: input.getAttribute('autocomplete'), maxlength: input.maxLength,
      ariaLabel: input.getAttribute('aria-label'), ariaInvalid: input.getAttribute('aria-invalid'), describedby: input.getAttribute('aria-describedby'),
      placeholder: input.placeholder, value: input.value, fontSize: parseFloat(cs(input, 'fontSize')),
    } : null,
    submit_: submit ? { type: submit.type, text: text(submit), disabled: submit.disabled, busy: submit.getAttribute('aria-busy') } : null,
    rootBg: cs(root, 'backgroundImage'),
    panelBg: cs(panel, 'backgroundColor'),
    panelRadius: panel ? [cs(panel, 'borderTopLeftRadius'), cs(panel, 'borderTopRightRadius'), cs(panel, 'borderBottomLeftRadius'), cs(panel, 'borderBottomRightRadius')] : null,
    scroll: root ? { top: root.scrollTop, height: root.scrollHeight, client: root.clientHeight } : null,
    inputCount: root ? root.querySelectorAll('input').length : 0,
    masaControls: root ? root.querySelectorAll('.m-input, .m-btn, .m-text-field').length : 0,
    orbs: root ? [...root.querySelectorAll('.ft-login__orb')].map((o) => o.getAttribute('aria-hidden')) : [],
    h1: root ? root.querySelectorAll('h1').length : 0,
    h2: root ? root.querySelectorAll('h2').length : 0,
    activeTestId: active ? active.getAttribute('data-testid') : null,
    tabBar: !!document.querySelector('[data-testid="tab-bar"]'),
    storage: { userId: localStorage.getItem('userId'), phoneNumber: localStorage.getItem('phoneNumber') },
  };
}"""

SET_SAFE_JS = """([top, bottom]) => {
  const style = document.documentElement.style;
  style.setProperty('--ft-safe-top', top + 'px');
  style.setProperty('--ft-safe-bottom', bottom + 'px');
}"""

# 记录主按钮「曾经」进入过忙态（点按钮 → 登录 → 换页，中间态只存在很短的一瞬间）
WATCH_BUSY_JS = """() => {
  window.__ftBusy = { busy: false, disabled: false, label: null };
  const record = () => {
    const el = document.querySelector('[data-testid="login-submit"]');
    if (el && el.getAttribute('aria-busy') === 'true') {
      window.__ftBusy.busy = true;
      window.__ftBusy.disabled = window.__ftBusy.disabled || el.disabled;
      window.__ftBusy.label = el.getAttribute('aria-label');
    }
  };
  new MutationObserver(record).observe(document.body, { subtree: true, childList: true, attributes: true });
}"""

CLEAR_SAFE_JS = """() => {
  const style = document.documentElement.style;
  style.removeProperty('--ft-safe-top');
  style.removeProperty('--ft-safe-bottom');
}"""


# ---------------------------------------------------------------------------------------------- 小工具


def tid(page, testid: str):
    return page.get_by_test_id(testid)


def poll(page, js: str, arg=None, timeout: int = 8000) -> bool:
    """等页面内的 JS 谓词成立；超时返回 False（让断言记失败，而不是抛异常打断后面的用例）。"""
    try:
        page.wait_for_function(js, arg=arg, timeout=timeout)
        return True
    except PlaywrightTimeoutError:
        return False


def state(page) -> dict:
    return page.evaluate(STATE_JS)


def approx(actual, expected, tolerance: float = 1.0) -> bool:
    return actual is not None and abs(actual - expected) <= tolerance


def open_login(page, path: str = "/login") -> None:
    open_app(page, path)
    page.wait_for_selector("[data-testid='login-phone']", timeout=25_000)


def wait_light_icons(page) -> bool:
    return poll(page, "() => document.documentElement.getAttribute('data-statusbar-icons') === 'light'")


def wait_error(page, text: "str | None") -> bool:
    """等校验提示变成指定文字；text=None 表示提示消失（空串）。"""
    return poll(
        page,
        "(t) => { const e = document.querySelector('[data-testid=\"login-error\"]'); return !!e && e.textContent.trim() === (t || ''); }",
        text,
    )


def wait_focus(page, testid: str) -> bool:
    return poll(page, "(id) => { const a = document.activeElement; return !!a && a.getAttribute('data-testid') === id; }", testid)


def wait_logged_in(page) -> bool:
    """等「登录成功」落地：userId 写入、登录页卸载、首页壳（Tab 栏）就绪。超时返回 False（断言记失败，不打断后面的用例）。"""
    try:
        page.wait_for_function("() => !!localStorage.getItem('userId')", timeout=15000)
        page.wait_for_selector("[data-testid='login-phone']", state="detached", timeout=15000)
        wait_tab_bar(page)
        return True
    except PlaywrightTimeoutError:
        return False


def user_id_of(db: LocalDb, phone: str) -> "str | None":
    """本机库里该手机号对应的用户 Id；没有这个用户返回 None（LocalDb.user_id 在找不到时抛 LookupError）。"""
    try:
        return db.user_id(phone)
    except LookupError:
        return None


def same_id(a: "str | None", b: "str | None") -> bool:
    """Guid 文本比较不区分大小写（localStorage 里是小写，SQLite 里可能是大写）。"""
    return a is not None and b is not None and a.lower() == b.lower()


# ---------------------------------------------------------------------------------------------- A. 外壳与结构


def check_shell(run: TestRun) -> None:
    with session() as s:
        page = s.page

        # 未登录访问首页 → 登录页
        open_app(page, "/")
        page.wait_for_selector("[data-testid='login-phone']", timeout=25_000)
        st = state(page)
        run.check("未登录访问首页被带到登录页", st["path"] in ("/", "/login") and st["exists"], st["path"])
        run.check("登录页没有 Tab 栏（元素根本不存在）与「记一笔」圆钮", not st["tabBar"] and tab_bar(page).count() == 0 and page.get_by_role("button", name="记一笔").count() == 0)
        wait_animations_settled(page)

        run.check(
            "渐变背景铺满整屏（从屏幕最上沿开始，含状态栏之下）",
            approx(st["root"]["t"], 0) and approx(st["root"]["l"], 0) and approx(st["root"]["w"], SCREEN_W) and approx(st["root"]["h"], SCREEN_H)
            and "gradient" in st["rootBg"],
            f"{st['root']} {st['rootBg'][:60]}",
        )
        run.check("根节点声明深色背景（data-statusbar-bg=dark）→ 状态栏用白色图标", st["statusbarBg"] == "dark" and wait_light_icons(page), f"{st['statusbarBg']} {st['icons']}")

        run.check("应用名「记账本」（唯一的 h1）与标语", st["titleText"] == "记账本" and st["h1"] == 1 and st["sloganText"] == "简单、安心的个人记账", f"{st['titleText']} {st['sloganText']}")
        logo = st["logo"]
        run.check(
            "玻璃 Logo：方形（96）、水平居中，在应用名之上",
            approx(logo["w"], 96) and approx(logo["h"], 96) and approx((logo["l"] + logo["r"]) / 2, SCREEN_W / 2) and logo["b"] <= st["title"]["t"],
            str(logo),
        )
        run.check("背景圆球是装饰：3 个、都对读屏隐藏", st["orbs"] == ["true", "true", "true"], str(st["orbs"]))

        panel = st["panel"]
        run.check(
            "白色面板贴屏幕底、左右撑满、白底",
            approx(panel["b"], SCREEN_H) and approx(panel["l"], 0) and approx(panel["r"], SCREEN_W) and st["panelBg"] == "rgb(255, 255, 255)",
            f"{panel} {st['panelBg']}",
        )
        run.check("面板只有上圆角 40", st["panelRadius"] == ["40px", "40px", "0px", "0px"], str(st["panelRadius"]))
        run.check("面板标题「手机号登录」（唯一的 h2）", st["panelTitle"] == "手机号登录" and st["h2"] == 1, f"{st['panelTitle']} {st['h2']}")
        run.check("面板说明：无需验证码、首次使用将自动注册", st["hint"] == "无需验证码；首次使用将自动注册账号", str(st["hint"]))

        field = st["input_"]
        run.check(
            "手机号输入框：type=tel、数字键盘、自动填充用国内号码、最多 11 位、读屏名称「手机号」、占位文字",
            field["type"] == "tel" and field["inputmode"] == "numeric" and field["autocomplete"] == "tel-national" and field["maxlength"] == 11
            and field["ariaLabel"] == "手机号" and field["placeholder"] == "请输入手机号" and field["value"] == "",
            str(field),
        )
        run.check("输入框字号 ≥ 16（iOS 聚焦时不放大页面），输入条高 ≥ 44", field["fontSize"] >= 16 and st["field"]["h"] >= 44, f"{field['fontSize']} {st['field']}")
        run.check("没有验证码输入：页面只有手机号这一个输入框", st["inputCount"] == 1, str(st["inputCount"]))

        submit = st["submit_"]
        run.check(
            "主按钮「登录 / 注册」：submit、高 ≥ 48、与输入条同宽、没有置灰",
            submit["text"] == "登录 / 注册" and submit["type"] == "submit" and st["submit"]["h"] >= 48 and approx(st["submit"]["w"], st["field"]["w"])
            and not submit["disabled"] and submit["busy"] is None,
            f"{submit} {st['submit']} {st['field']}",
        )
        run.check("新版不再使用 MASA 表单控件（.m-input / .m-btn / .m-text-field）", st["masaControls"] == 0, str(st["masaControls"]))
        run.check("没有横向溢出；校验提示的位置是空的（预留了一行）", st["overflowX"] <= 0 and st["errorText"] == "" and st["error"]["h"] >= 18, f"{st['overflowX']} {st['error']}")

        glass = page.evaluate(GLASS_LAYERS_JS)
        run.check("每屏玻璃层 ≤ 3（登录页只有 Logo 一层）", glass == 1, str(glass))
        shot(page, "login-shell")

        # 直接访问 /login 同样如此
        open_login(page, "/login")
        st = state(page)
        run.check("直接访问 /login：同样是这套外观、没有 Tab 栏", st["exists"] and not st["tabBar"] and st["statusbarBg"] == "dark", str(st["path"]))
        run.check_clean(s.errors, "A 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- B. 手机号校验


def check_validation(run: TestRun) -> None:
    with session() as s:
        page = s.page
        open_login(page)
        before = state(page)

        # ---- 空提交 ----
        tid(page, "login-submit").click()
        run.check("空手机号提交：提示「手机号不能为空」", wait_error(page, EMPTY_TEXT))
        st = state(page)
        run.check("提示是红字（加深的危险文字色）且 role=alert", st["errorColor"] == RGB_DANGER_TEXT and st["errorRole"] == "alert", f"{st['errorColor']} {st['errorRole']}")
        run.check("输入条红描边、aria-invalid=true", st["fieldError"] is True and st["input_"]["ariaInvalid"] == "true", f"{st['fieldError']} {st['input_']['ariaInvalid']}")
        run.check("焦点回到输入框（直接改即可）", wait_focus(page, "login-phone"))
        run.check("没有登录：仍在登录页、localStorage 没有 userId、按钮没有置灰", st["exists"] and st["storage"]["userId"] is None and not st["submit_"]["disabled"], str(st["storage"]))
        run.check(
            "提示出现不挪动按钮与面板（提示行预留了高度——失焦先于点按钮发生，位置一动点击就落空）",
            approx(st["submit"]["t"], before["submit"]["t"], 0.5) and approx(st["panel"]["t"], before["panel"]["t"], 0.5) and approx(st["error"]["h"], before["error"]["h"], 0.5),
            f"{st['submit']} {before['submit']}",
        )
        shot(page, "login-error-empty")

        # ---- 继续输入：旧提示消失 ----
        tid(page, "login-phone").fill("1380000000")
        run.check("开始输入后旧提示消失、红描边去掉", wait_error(page, None) and state(page)["fieldError"] is False)

        # ---- 位数不对 ----
        tid(page, "login-submit").click()
        run.check("10 位手机号：提示「手机号必须是11位」", wait_error(page, LENGTH_TEXT))

        # ---- 不是 1 开头 ----
        tid(page, "login-phone").fill("23800000000")
        tid(page, "login-submit").click()
        run.check("非 1 开头的 11 位：提示「手机号格式不正确」", wait_error(page, FORMAT_TEXT))

        # ---- 全空白：与空一样 ----
        tid(page, "login-phone").fill("   ")
        tid(page, "login-submit").click()
        run.check("全是空格：提示「手机号不能为空」", wait_error(page, EMPTY_TEXT))

        # ---- 首尾空格先去掉再校验：10 位数字 + 1 个空格共 11 个字符，不去空格就会被当成 11 位手机号放行 ----
        tid(page, "login-phone").fill("1380013800 ")
        tid(page, "login-submit").click()
        run.check("10 位数字 + 尾部空格：空格先被去掉，提示「手机号必须是11位」（没有把空格算作一位放行）", wait_error(page, LENGTH_TEXT))
        run.check("校验失败从未登录（localStorage 没有 userId）", state(page)["storage"]["userId"] is None)

        # ---- 最多 11 位（真实按键输入受 maxlength 限制） ----
        tid(page, "login-phone").fill("")
        tid(page, "login-phone").press_sequentially("13800138000123")
        value = page.evaluate("() => document.querySelector('[data-testid=\"login-phone\"]').value")
        run.check("按键输入最多 11 位", value == "13800138000", value)
        run.check_clean(s.errors, "B 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- C. 登录成功


def check_login(run: TestRun, db: LocalDb) -> None:
    # ---- 点按钮登录：首次使用 = 自动注册 ----
    phone = unique_phone()
    with session() as s:
        page = s.page
        open_login(page)
        tid(page, "login-phone").fill(phone)
        page.evaluate(WATCH_BUSY_JS)  # 登录很快：用 MutationObserver 抓「登录中」那一帧，而不是靠时机去读
        tid(page, "login-submit").click()
        run.check("点「登录 / 注册」：回到首页（Tab 栏出现、登录页消失）", wait_logged_in(page))
        busy = page.evaluate("() => window.__ftBusy")
        run.check(
            "登录中：按钮转圈（aria-busy=true）并置灰（防止连点），读屏名称是「正在登录」",
            busy["busy"] and busy["disabled"] and busy["label"] == "正在登录",
            str(busy),
        )
        st = state(page)
        run.check("登录后停在首页、首页 Tab 激活", st["path"] == "/" and active_tab(page) == "home" and not st["exists"], f"{st['path']} {active_tab(page)}")
        user_id = user_id_of(db, phone)
        run.check("首次使用自动注册：本机库里有这个手机号的用户，localStorage 的 userId / phoneNumber 对得上", same_id(st["storage"]["userId"], user_id) and st["storage"]["phoneNumber"] == phone, f"{st['storage']} {user_id}")
        run.check("首页顶部是深色 Hero → 状态栏仍是白色图标", wait_light_icons(page))
        run.check_clean(s.errors, "C 节（按钮登录）会话无页面异常 / 控制台错误 / 失败请求")

    # ---- 同一手机号再次登录（新会话 = 退出后）：同一个用户 ----
    with session() as s:
        page = s.page
        open_login(page)
        tid(page, "login-phone").fill(phone)
        tid(page, "login-submit").click()
        run.check("同一手机号再次登录成功", wait_logged_in(page))
        again = state(page)["storage"]["userId"]
        run.check("再次登录是同一个用户（不重复注册）", same_id(again, user_id) and same_id(again, user_id_of(db, phone)), f"{again} {user_id}")
        run.check_clean(s.errors, "C 节（再次登录）会话无页面异常 / 控制台错误 / 失败请求")

    # ---- 回车提交（软键盘的「前往」键） ----
    phone2 = unique_phone()
    with session() as s:
        page = s.page
        open_login(page)
        tid(page, "login-phone").fill(phone2)
        tid(page, "login-phone").press("Enter")
        run.check("在输入框按回车同样提交登录", wait_logged_in(page))
        st = state(page)
        run.check("回车登录：存下的手机号对得上，库里有这个用户", st["storage"]["phoneNumber"] == phone2 and user_id_of(db, phone2) is not None, str(st["storage"]))
        run.check_clean(s.errors, "C 节（回车登录）会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- D. 沉浸式


def check_immersive(run: TestRun) -> None:
    notch, gesture = 47, 34
    with session() as s:
        page = s.page
        open_login(page)
        base = state(page)
        page.evaluate(SET_SAFE_JS, [notch, gesture])
        wait_animations_settled(page)
        st = state(page)

        run.check("【沉浸式】渐变仍从屏幕最上沿铺到底（含刘海之下与手势条之下）", approx(st["root"]["t"], 0) and approx(st["root"]["h"], SCREEN_H) and approx(st["panel"]["b"], SCREEN_H), f"{st['root']} {st['panel']}")
        run.check("【沉浸式】Logo 与应用名都在状态栏之下", st["logo"]["t"] >= notch - 0.5 and st["title"]["t"] >= notch - 0.5, f"{st['logo']} {st['title']}")
        run.check("【沉浸式】主按钮避开手势条（按钮底边在手势条之上）", st["submit"]["b"] <= SCREEN_H - gesture + 0.5, str(st["submit"]))
        run.check("【沉浸式】面板随之变高：面板底部留白比无手势条时多出手势条那一截", approx(st["panel"]["h"] - base["panel"]["h"], gesture, 1.5), f"{st['panel']['h']} {base['panel']['h']}")
        run.check("【沉浸式】深色渐变 → 状态栏图标仍是白色", wait_light_icons(page))
        shot(page, "login-immersive-notch")

        page.evaluate(CLEAR_SAFE_JS)
        wait_animations_settled(page)
        restored = state(page)
        run.check("【沉浸式】安全区还原为 0 后布局复原", approx(restored["panel"]["h"], base["panel"]["h"], 1) and approx(restored["submit"]["b"], base["submit"]["b"], 1), f"{restored['panel']} {base['panel']}")
        run.check_clean(s.errors, "D 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- E. 软键盘


def check_keyboard(run: TestRun) -> None:
    with session() as s:
        page = s.page
        open_login(page)
        base = state(page)

        # 软键盘弹出 = 可视高度变小：用缩小视口高度模拟（真机表现见 ticket 的人工验收）
        page.set_viewport_size({"width": SCREEN_W, "height": 440})
        poll(page, "() => window.innerHeight === 440")
        wait_animations_settled(page)
        st = state(page)
        run.check("可视高度 440：根节点随之变矮，面板贴在可视区底部", approx(st["root"]["h"], 440) and approx(st["panel"]["b"], 440), f"{st['root']} {st['panel']}")
        run.check("可视高度 440：输入条与主按钮都在可视范围内（没有被键盘区域顶出去）", st["field"]["t"] >= 0 and st["submit"]["b"] <= 440 + 0.5 and st["submit"]["t"] >= 0, f"{st['field']} {st['submit']}")
        run.check("可视高度 440：品牌区先被压缩（变矮的是上方的品牌区，面板高度不变）", st["hero"]["h"] < base["hero"]["h"] and approx(st["panel"]["h"], base["panel"]["h"], 0.5), f"{st['hero']} {base['hero']}")
        run.check("可视高度 440：整页不需要滚动", st["scroll"]["height"] <= st["scroll"]["client"] + 1, str(st["scroll"]))
        tid(page, "login-phone").focus()
        run.check("可视高度 440：聚焦输入框后仍在可视范围内", state(page)["input"]["b"] <= 440 + 0.5 and state(page)["input"]["t"] >= 0)
        shot(page, "login-keyboard-440")

        # 极矮（横屏 + 键盘）：面板放不下，整页滚动，滚到底按钮可见
        page.set_viewport_size({"width": SCREEN_W, "height": 260})
        poll(page, "() => window.innerHeight === 260")
        wait_animations_settled(page)
        st = state(page)
        run.check("可视高度 260：面板放不下时整页可以滚动", st["scroll"]["height"] > st["scroll"]["client"] + 1, str(st["scroll"]))
        page.evaluate("() => { const r = document.querySelector('[data-testid=\"login-page\"]'); r.scrollTop = r.scrollHeight; }")
        st = state(page)
        run.check("可视高度 260：滚到底后主按钮完整在可视范围内", st["submit"]["b"] <= 260 + 0.5 and st["submit"]["t"] >= 0, str(st["submit"]))
        run.check("可视高度 260：没有横向溢出", st["overflowX"] <= 0, str(st["overflowX"]))

        # 复原
        page.set_viewport_size({"width": SCREEN_W, "height": SCREEN_H})
        poll(page, "() => window.innerHeight === %d" % SCREEN_H)
        wait_animations_settled(page)
        restored = state(page)
        run.check(
            "可视高度复原后布局复原（品牌区回到原高度、面板回到屏幕底）",
            approx(restored["hero"]["h"], base["hero"]["h"], 1) and approx(restored["panel"]["b"], SCREEN_H),
            f"{restored['hero']} {base['hero']} {restored['panel']}",
        )
        run.check_clean(s.errors, "E 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- F. 窄屏


def check_narrow(run: TestRun) -> None:
    width, height = 320, 568
    with session(viewport={"width": width, "height": height}) as s:
        page = s.page
        open_login(page)
        wait_animations_settled(page)
        st = state(page)
        run.check("320 宽：没有横向溢出、整页不需要滚动", st["overflowX"] <= 0 and st["scroll"]["height"] <= st["scroll"]["client"] + 1, f"{st['overflowX']} {st['scroll']}")
        run.check(
            "320 宽：面板、输入条、主按钮都在屏幕内，主按钮与输入条高度 ≥ 44",
            all(st[k]["l"] >= 0 and st[k]["r"] <= width + 0.5 for k in ("panel", "field", "submit")) and st["submit"]["h"] >= 44 and st["field"]["h"] >= 44,
            f"{st['panel']} {st['field']} {st['submit']}",
        )
        run.check(
            "320 宽：品牌区完整（Logo、应用名、标语都在品牌区内，没有被面板盖住）",
            st["logo"]["t"] >= st["hero"]["t"] and st["slogan"]["b"] <= st["hero"]["b"] and st["slogan"]["b"] <= st["panel"]["t"] and st["slogan"]["l"] >= 0 and st["slogan"]["r"] <= width + 0.5,
            f"{st['logo']} {st['slogan']} {st['hero']}",
        )
        run.check("320 宽：面板说明不挤出屏幕", st["panel"]["r"] <= width + 0.5)
        run.check("320 宽：每屏玻璃层 ≤ 3", page.evaluate(GLASS_LAYERS_JS) <= 3, str(page.evaluate(GLASS_LAYERS_JS)))

        tid(page, "login-submit").click()
        wait_error(page, EMPTY_TEXT)
        st = state(page)
        run.check("320 宽：校验提示在屏幕内、没有横向溢出、整页不需要滚动", st["error"]["l"] >= 0 and st["error"]["r"] <= width + 0.5 and st["overflowX"] <= 0 and st["scroll"]["height"] <= st["scroll"]["client"] + 1, f"{st['error']} {st['scroll']}")
        shot(page, "login-320")
        run.check_clean(s.errors, "F 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- G. 无障碍


def check_accessibility(run: TestRun) -> None:
    with session() as s:
        page = s.page
        open_login(page)

        run.check("输入框的角色与名称：文本框「手机号」", page.get_by_role("textbox", name="手机号").count() == 1)
        run.check("主按钮的角色与名称：按钮「登录 / 注册」", page.get_by_role("button", name="登录 / 注册").count() == 1)

        st = state(page)
        run.check(
            "校验提示与输入框关联：aria-describedby 指向 login-error，提示是 role=alert",
            st["input_"]["describedby"] == "login-error" and st["errorId"] == "login-error" and st["errorRole"] == "alert",
            f"{st['input_']['describedby']} {st['errorId']} {st['errorRole']}",
        )
        run.check("标题层级：一个 h1（应用名）+ 一个 h2（面板标题）", st["h1"] == 1 and st["h2"] == 1)

        tid(page, "login-phone").focus()
        page.keyboard.press("Tab")
        run.check("Tab 键顺序：手机号输入框 → 「登录 / 注册」按钮", wait_focus(page, "login-submit"))
        page.keyboard.press("Shift+Tab")
        run.check("Shift+Tab 回到手机号输入框", wait_focus(page, "login-phone"))

        # 键盘用户：空提交后焦点回到输入框（不是丢在按钮上）
        page.keyboard.press("Enter")
        run.check("键盘用户在空输入框按回车：给出提示且焦点仍在输入框", wait_error(page, EMPTY_TEXT) and wait_focus(page, "login-phone"))
        run.check_clean(s.errors, "G 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- main

SECTIONS = {
    "A": lambda run, db: check_shell(run),
    "B": lambda run, db: check_validation(run),
    "C": lambda run, db: check_login(run, db),
    "D": lambda run, db: check_immersive(run),
    "E": lambda run, db: check_keyboard(run),
    "F": lambda run, db: check_narrow(run),
    "G": lambda run, db: check_accessibility(run),
}


def main() -> int:
    """不带参数跑全部小节；带小节名（如 `python login_page_test.py B C`）只跑指定小节，便于定位问题。"""
    require_server()
    run = TestRun("login-page")
    db = LocalDb()
    db.verify_schema()

    wanted = [a.upper() for a in sys.argv[1:]]
    unknown = [a for a in wanted if a not in SECTIONS]
    if unknown:
        print(f"[ENV] 不认识的小节：{unknown}；可选：{list(SECTIONS)}")
        return 2

    for name, section in SECTIONS.items():
        if not wanted or name in wanted:
            section(run, db)

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
