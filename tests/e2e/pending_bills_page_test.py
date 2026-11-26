# -*- coding: utf-8 -*-
"""
E2E 测试：待确认账单页 + 短信识别页（iOS UI 重构 · 19 号 ticket）——自动识别状态、待确认卡片、一键确认入账、「确认账单」Sheet、忽略、
账本必填拦截，以及短信识别页的权限 / 读取范围 / 读取结果进入待确认账单（去重、容量上限、没识别出金额的跳过）。

只覆盖网页层——真机上的通知栏捕获、短信广播、系统权限弹窗、系统设置页往返见 ticket 19 的人工验收清单。
浏览器里没有「通知使用权」，也读不到短信：Web.Server 以 E2E__FakeCapture=true 启动（server.ps1 会设置），
换成 E2ECapture.cs 里的假实现，本脚本经 set_capture() 设置权限状态与「读到的短信」、经 capture_state() 观察「点了去开启 / 去授权」真的走到了平台服务。

  A. 空状态与自动识别状态：从「我的」进入、二级页面外壳、两项都没开启的权限卡、「去开启」（通知使用权跳设置页 / 短信权限被拒绝与授予）、
     两项都开启后的绿色状态条与「设置」菜单（通知使用权 / 短信识别页）
  B. 卡片内容：来源行、时间、分类（入账时会使用的）、备注摘要（折叠空白、40 字截断）、金额（支出负号 / 收入绿色正号 / 长金额缩一档）、账本芯片（默认账本）、
     点击区域与读屏名称
  C. 一键确认入账：沿用金额 / 类型 / 数据来源 / 交易时间 / 备注，分类取「其他…」，支付渠道按名称匹配；卡片上换账本（账本菜单）后入账记进所选账本；
     待确认项从库里移除、列表与计数更新；首页角标与横幅随之一致
  D. 修改后入账（Sheet）：预填与来源 / 备注原文、改类型 / 金额 / 支付渠道、账本菜单、入账后以修改后的值落库
  E. 支付渠道没匹配到：点「确认入账」不猜、打开 Sheet 并给出提示；置灰的「入账」键引导选支付渠道；关闭后再打开按原值重置
  F. 忽略：单条忽略、全部忽略（确认 Alert，取消保留），都不产生账单
  G. 防重复入账：同一张卡片的确认连点只产生一笔账单
  H. 账本必填（没有账本的用户）：卡片路径——账本芯片是「选择账本」、点「确认入账」不入账而是弹出账本菜单、行内建好账本后再入账；
     Sheet 路径——置灰的「入账」键引导：账本行抖动并弹出账本菜单、行内建好账本后入账
  I. 窄屏（320）：卡片 / 导航栏 / 状态条不溢出、不重叠；每屏玻璃层 ≤ 3
  J. 短信识别页：权限行（去授权、被拒绝、授予）、读取范围（默认 30 天前、改日期）、固定在底部的主按钮、读取结果进待确认账单——
     摘要分组（新加入 / 已在列表里 / 没识别出金额 / 列表已满 / 加入失败）与每张卡片的去向标签、库里待确认账单的字段、没有创建正式账单、
     摘要里去待确认账单页、重复读取去重、实时捕获过的同一笔去重、容量上限（剩余容量由新到旧、不顶掉已有的）、没有账本的用户照样能读

可观察属性（已登记到 README）：pending-page、pending-content（data-state / data-count）、pending-count、pending-list、pending-card（data-pending-id / data-type / data-source / data-busy）、
pending-source / pending-time / pending-edit / pending-amount（data-long）/ pending-ledger（data-missing / data-value）/ pending-ledger-text / pending-ignore / pending-confirm、
pending-ignore-all、pending-ignore-all-alert、pending-ledger-menu、pending-empty、pending-error / pending-retry、
capture-status（data-state）、capture-text、capture-settings、capture-settings-sheet / -notification / -sms、capture-notification / capture-sms（data-on）及其 -enable / -state、
confirm-sheet、confirm-source / confirm-time / confirm-note / confirm-hint、confirm-type、confirm-amount（data-value / data-empty / data-shake）、
confirm-channel-row / confirm-ledger-row（data-value / data-missing）、confirm-channel-menu / confirm-ledger-menu、confirm-save、
sms-page、sms-content（data-state / data-permission / data-ready / data-reads / data-count）、sms-permission / sms-permission-grant、sms-date-row / sms-date、
sms-read、sms-foot、sms-summary（data-added / data-duplicate / data-no-amount / data-over-capacity / data-failed）与它的行 sms-summary-added / -duplicate / -no-amount / -over-capacity / -failed、
sms-results-header、sms-result（data-sms-id / data-outcome）/ sms-body / sms-meta / sms-outcome、sms-idle / sms-empty。
"""
import sys
import time
from datetime import datetime, timedelta
from decimal import Decimal

from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

from e2e_base import (
    GLASS_LAYERS_JS,
    BILL_TYPE_EXPENSE,
    BILL_TYPE_INCOME,
    PHONE_VIEWPORT,
    SOURCE_NOTIFICATION,
    SOURCE_SMS,
    SYNC_PENDING,
    LocalDb,
    TestRun,
    capture_state,
    login,
    open_app,
    open_tab,
    press_key,
    press_keys,
    require_server,
    session,
    set_capture,
    shot,
    to_db_time,
    to_wall_time,
    unique_name,
    wait_animations_settled,
    wait_attr,
    wait_tab_bar,
    wait_url,
)

SCREEN_W = PHONE_VIEWPORT["width"]
SCREEN_H = PHONE_VIEWPORT["height"]
CONTENT = '[data-testid="pending-content"]'
SMS_CONTENT = '[data-testid="sms-content"]'

CARDS_JS = """() => [...document.querySelectorAll('[data-testid="pending-card"]')].map((card) => {
  const q = (id) => card.querySelector(`[data-testid="${id}"]`);
  const text = (node) => (node ? node.textContent.trim() : null);
  const box = (node) => { const r = node.getBoundingClientRect(); return { l: r.left, r: r.right, t: r.top, b: r.bottom, w: r.width, h: r.height }; };
  const edit = q('pending-edit');
  const amount = q('pending-amount');
  const ledger = q('pending-ledger');
  return {
    id: card.getAttribute('data-pending-id'),
    type: card.getAttribute('data-type'),
    source: card.getAttribute('data-source'),
    busy: card.getAttribute('data-busy'),
    sourceLine: text(q('pending-source')),
    time: text(q('pending-time')),
    category: text(edit.querySelector('.ft-cell__title')),
    note: text(edit.querySelector('.ft-cell__subtitle')),
    amount: text(amount),
    amountLong: amount.getAttribute('data-long'),
    amountColor: getComputedStyle(amount).color,
    ledgerText: text(q('pending-ledger-text')),
    ledgerMissing: ledger.getAttribute('data-missing'),
    ledgerId: ledger.getAttribute('data-value'),
    editAria: edit.getAttribute('aria-label'),
    confirmAria: q('pending-confirm').getAttribute('aria-label'),
    card: box(card), edit: box(edit), amountBox: box(amount), ledger: box(ledger), ignore: box(q('pending-ignore')), confirm: box(q('pending-confirm')),
  };
})"""

SHEET_JS = """() => {
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  const text = (id) => { const n = q(id); return n ? n.textContent.trim() : null; };
  const sheet = q('confirm-sheet');
  if (!sheet) return null;
  const amount = q('confirm-amount');
  const save = q('confirm-save');
  const channel = q('confirm-channel-row');
  const ledger = q('confirm-ledger-row');
  const value = (row) => { const n = row.querySelector('.ft-cell__value'); return n ? n.textContent.trim() : null; };
  return {
    title: text('sheet-title'),
    source: text('confirm-source'),
    time: text('confirm-time'),
    note: text('confirm-note'),
    hint: text('confirm-hint'),
    type: q('confirm-type').getAttribute('data-value'),
    amount: amount.getAttribute('data-value'),
    amountEmpty: amount.getAttribute('data-empty'),
    channel: value(channel),
    channelMissing: channel.getAttribute('data-missing'),
    channelId: channel.getAttribute('data-value'),
    ledger: value(ledger),
    ledgerMissing: ledger.getAttribute('data-missing'),
    ledgerId: ledger.getAttribute('data-value'),
    saveDisabled: save.getAttribute('aria-disabled'),
    saveText: save.textContent.trim(),
  };
}"""

# 页面外壳的几何：标题取文字本身的范围（h1 的盒子可能比文字宽），与右上角「全部忽略」、左上角返回按钮比较是否重叠
PAGE_BOX_JS = """() => {
  const rect = (el) => { if (!el) return null; const r = el.getBoundingClientRect(); return { l: r.left, r: r.right, t: r.top, b: r.bottom, w: r.width, h: r.height }; };
  const textRect = (el) => { if (!el) return null; const range = document.createRange(); range.selectNodeContents(el); return rect(range); };
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  return {
    overflowX: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    back: rect(q('nav-back')),
    title: textRect(q('nav-title')),
    titleTag: q('nav-title') ? q('nav-title').tagName : null,
    ignoreAll: rect(q('pending-ignore-all')),
    capture: rect(q('capture-status')),
  };
}"""

# 监听一个属性是否「曾经」变成某个值（引导抖动只持续 460ms，轮询可能错过，用 MutationObserver 记下来）
WATCH_JS = """([selector, attr, value]) => {
  const el = document.querySelector(selector);
  if (window.__ftWatchObserver) window.__ftWatchObserver.disconnect();
  window.__ftWatchSeen = false;
  const check = () => {
    if (el.getAttribute(attr) === value || el.querySelector(`[${attr}="${value}"]`)) window.__ftWatchSeen = true;
  };
  window.__ftWatchObserver = new MutationObserver(check);
  window.__ftWatchObserver.observe(el, { attributes: true, attributeFilter: [attr], subtree: true });
  check();
}"""

# 把指定页面里最近的可滚动祖先滚到底，并返回「最后一张结果卡片」与「底部固定按钮」的位置
SCROLL_TO_END_JS = """(pageTestId) => {
  let el = document.querySelector(`[data-testid="${pageTestId}"]`);
  while (el && el !== document.documentElement) {
    const cs = getComputedStyle(el);
    if ((cs.overflowY === 'auto' || cs.overflowY === 'scroll') && el.scrollHeight > el.clientHeight) break;
    el = el.parentElement;
  }
  const scroller = el && el !== document.documentElement ? el : document.scrollingElement;
  scroller.scrollTop = scroller.scrollHeight;
  return new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => {
    const items = document.querySelectorAll('[data-testid="sms-result"]');
    const foot = document.querySelector('[data-testid="sms-foot"]').getBoundingClientRect();
    resolve({
      lastBottom: items.length ? items[items.length - 1].getBoundingClientRect().bottom : null,
      footTop: foot.top,
      scrolled: scroller.scrollTop,
    });
  })));
}"""


# ---------------------------------------------------------------------------------------------- 小工具


def tid(page, testid: str):
    return page.get_by_test_id(testid)


def visible(locator, timeout: int = 4000) -> bool:
    try:
        locator.wait_for(state="visible", timeout=timeout)
        return True
    except PlaywrightTimeoutError:
        return False


def gone(locator, timeout: int = 4000) -> bool:
    try:
        locator.wait_for(state="hidden", timeout=timeout)
        return True
    except PlaywrightTimeoutError:
        return False


def poll(page, js: str, arg=None, timeout: int = 6000) -> bool:
    """等页面内的 JS 谓词成立；超时返回 False（让断言记失败，而不是抛异常打断后面的用例）。"""
    try:
        page.wait_for_function(js, arg=arg, timeout=timeout)
        return True
    except PlaywrightTimeoutError:
        return False


def toast(page, text: str):
    return page.locator(".m-enqueued-snackbars").get_by_text(text)


def wait_toast(page, text: str) -> bool:
    return visible(toast(page, text).first, 8000)


def wait_toasts_gone(page) -> None:
    # MASA 提示条在鼠标悬停时会暂停消失倒计时；Playwright 的鼠标停在上一次点击处，而点底部菜单项的位置正好在提示条下面
    # （真机是触摸，没有悬停）→ 先把鼠标挪开，否则提示条会一直留着
    page.mouse.move(1, 1)
    page.wait_for_function("() => document.querySelectorAll('.m-enqueued-snackbars .m-snack__wrapper').length === 0", timeout=20000)


def watch_attr(page, selector: str, attr: str, value: str) -> None:
    page.evaluate(WATCH_JS, [selector, attr, value])


def watched(page, timeout: int = 2000) -> bool:
    return poll(page, "() => window.__ftWatchSeen === true", timeout=timeout)


def card_loc(page, pending_id: str):
    return page.locator(f'[data-testid="pending-card"][data-pending-id="{pending_id.lower()}"]')


def cards(page) -> "list[dict]":
    return page.evaluate(CARDS_JS)


def sheet_state(page) -> "dict | None":
    return page.evaluate(SHEET_JS)


def count_text_is(page, expected: str) -> bool:
    return poll(page, "(t) => document.querySelector('[data-testid=\"pending-count\"]')?.textContent.trim() === t", arg=expected)


def open_pending(page, state: str = "ready") -> None:
    """整页进入待确认账单页并等到加载完成。"""
    open_app(page, "/pending-bills")
    wait_attr(page, CONTENT, "data-state", state)
    wait_animations_settled(page)


def open_sms(page) -> None:
    """整页进入短信识别页并等到权限状态读完（data-ready=true：之后看到的「未授权」才是真的没权限，而不是还没读完）。"""
    open_app(page, "/sms-recognition")
    tid(page, "sms-page").wait_for(state="visible")
    wait_attr(page, SMS_CONTENT, "data-ready", "true")
    wait_animations_settled(page)


def open_sheet_from_row(page, pending_id: str) -> None:
    card_loc(page, pending_id).get_by_test_id("pending-edit").click()
    tid(page, "confirm-sheet").wait_for(state="visible")
    wait_animations_settled(page)


def wait_capture(predicate, timeout: float = 6.0) -> dict:
    """轮询假实现的状态直到 predicate 成立（超时返回最后一次状态，由断言报告）。"""
    deadline = time.time() + timeout
    state = capture_state()
    while time.time() < deadline:
        state = capture_state()
        if predicate(state):
            return state
        time.sleep(0.1)
    return state


def wait_bills(db: LocalDb, user_id: str, count: int, timeout: float = 8.0) -> "list[dict]":
    deadline = time.time() + timeout
    bills = db.bills_of_user(user_id)
    while time.time() < deadline:
        bills = db.bills_of_user(user_id)
        if len(bills) >= count:
            return bills
        time.sleep(0.15)
    return bills


def settle(seconds: float = 0.6) -> None:
    """等一小会儿（用于断言「没有发生」类的事情：没有新账单、没有打开菜单）。"""
    time.sleep(seconds)


def money(value) -> str:
    return f"¥{Decimal(str(value)):,.2f}"


def time_label(moment: datetime, now: datetime) -> str:
    """与 AddBillPresentation.TimeLabel 一致：今天 / 昨天 + HH:mm；同年「M月d日 HH:mm」；跨年带年份。"""
    clock = moment.strftime("%H:%M")
    if moment.date() == now.date():
        return f"今天 {clock}"
    if moment.date() == (now - timedelta(days=1)).date():
        return f"昨天 {clock}"
    if moment.year == now.year:
        return f"{moment.month}月{moment.day}日 {clock}"
    return f"{moment.year}年{moment.month}月{moment.day}日 {clock}"


def note_summary(note: str, max_length: int = 40) -> str:
    """与 PendingBillPresentation.NoteSummary 一致：折叠空白，超出加「…」。"""
    text = " ".join(note.split())
    return text if len(text) <= max_length else text[:max_length] + "…"


def token_color(page, token: str) -> str:
    """把令牌（如 --ft-label）解析成计算后的颜色文本，用来和元素的计算样式比。"""
    return page.evaluate(
        """(token) => { const p = document.createElement('i'); p.style.color = `var(${token})`; document.body.appendChild(p);
           const c = getComputedStyle(p).color; p.remove(); return c; }""",
        token,
    )


def same_id(a: "str | None", b: "str | None") -> bool:
    return bool(a) and bool(b) and a.lower() == b.lower()


def amount_is(value, expected) -> bool:
    """库里的金额是 REAL（读出来像 '35.0'），按数值比。"""
    try:
        return abs(float(value) - float(expected)) < 1e-9
    except (TypeError, ValueError):
        return False


def new_bills(db: LocalDb, user_id: str, before_ids: "set[str]") -> "list[dict]":
    return [b for b in db.bills_of_user(user_id) if b["Id"] not in before_ids]


def bill_matches(bill: dict, *, amount, bill_type: int, source: int, ledger_id: str, channel_id: str, category_id: str, note: str, when: datetime) -> bool:
    return (
        amount_is(bill["Amount"], amount)
        and bill["Type"] == bill_type
        and bill["Source"] == source
        and same_id(bill["LedgerId"], ledger_id)
        and same_id(bill["PaymentChannelId"], channel_id)
        and same_id(bill["CategoryId"], category_id)
        and bill["Note"] == note
        and bill["TransactionTime"] == to_db_time(when)
        and bill["SyncStatus"] == SYNC_PENDING
    )


def bill_detail(bill: dict) -> str:
    keys = ("Amount", "Type", "Source", "LedgerId", "PaymentChannelId", "CategoryId", "Note", "TransactionTime", "SyncStatus")
    return str({k: bill.get(k) for k in keys})


def minute(moment: datetime) -> datetime:
    return moment.replace(second=0, microsecond=0)


def create_ledger_in_sheet(page, name: str) -> bool:
    """在已经打开的「新建账本」Sheet 里填名称并保存；返回 Sheet 是否收起。"""
    visible(tid(page, "ledger-sheet"))
    tid(page, "ledger-name").fill(name)
    tid(page, "ledger-save").click()
    return gone(tid(page, "ledger-sheet"), 6000)


# ---------------------------------------------------------------------------------------------- A. 空状态与自动识别状态


def check_empty_and_capture(run: TestRun, page, db: LocalDb) -> None:
    set_capture(reset=True)
    db.delete_pending_bills()

    open_tab(page, "我的")
    tid(page, "profile-entry-pending-bills").click()
    wait_url(page, r"^/pending-bills$")
    wait_tab_bar(page, visible=False)
    wait_attr(page, CONTENT, "data-state", "empty")
    wait_animations_settled(page)
    run.check("从「我的」的「待确认账单」进入二级页面：地址 /pending-bills、Tab 栏隐藏", page.evaluate("() => location.pathname") == "/pending-bills" and tid(page, "tab-bar").is_hidden())

    info = page.evaluate(PAGE_BOX_JS)
    run.check("导航栏标题是 h1「待确认账单」", info["titleTag"] == "H1" and tid(page, "nav-title").inner_text().strip() == "待确认账单", str(info["titleTag"]))
    run.check("没有横向溢出", info["overflowX"] <= 0, str(info["overflowX"]))
    empty_text = tid(page, "pending-empty").inner_text()
    run.check("空状态：标题与说明", "暂无待确认账单" in empty_text and "微信/支付宝通知与银行扣款短信会自动出现在这里" in empty_text, empty_text)
    run.check("空状态没有「N 笔待确认」、列表与「全部忽略」", tid(page, "pending-count").count() == 0 and tid(page, "pending-list").count() == 0 and tid(page, "pending-ignore-all").count() == 0)
    glass = page.evaluate(GLASS_LAYERS_JS)
    run.check("每屏玻璃层 ≤ 3", glass <= 3, str(glass))

    # ---- 两项都没开启：权限卡 ----
    status = tid(page, "capture-status")
    run.check(
        "都没开启：状态区是两行权限卡（data-state=off）",
        status.get_attribute("data-state") == "off" and tid(page, "capture-notification").get_attribute("data-on") == "false" and tid(page, "capture-sms").get_attribute("data-on") == "false",
    )
    run.check("两行各带「去开启」", tid(page, "capture-notification-enable").inner_text().strip() == "去开启" and tid(page, "capture-sms-enable").inner_text().strip() == "去开启")
    run.check("未开启的行给出开启后的好处", "通知使用权" in tid(page, "capture-notification").inner_text() and "银行扣款短信" in tid(page, "capture-sms").inner_text(), tid(page, "capture-notification").inner_text())
    run.check(
        "权限行读屏名称带上状态（通知栏识别，未开启）",
        tid(page, "capture-notification").get_attribute("aria-label") == "通知栏识别，未开启" and tid(page, "capture-sms").get_attribute("aria-label") == "短信识别，未开启",
        str(tid(page, "capture-notification").get_attribute("aria-label")),
    )
    boxes = page.evaluate("""() => ['capture-notification-enable', 'capture-sms-enable'].map((id) => { const r = document.querySelector(`[data-testid="${id}"]`).getBoundingClientRect(); return { w: r.width, h: r.height }; })""")
    run.check("「去开启」点击区域 ≥ 44 × 44", all(b["w"] >= 44 and b["h"] >= 44 for b in boxes), str(boxes))
    shot(page, "pending-off")

    # ---- 通知栏识别：通知使用权只能在系统设置里开，点击后跳设置页并提示 ----
    tid(page, "capture-notification-enable").click()
    state = wait_capture(lambda s: s["notificationSettingsOpened"] == 1)
    run.check("点通知栏识别的「去开启」→ 走到平台的「打开通知使用权设置」", state["notificationSettingsOpened"] == 1, str(state))
    run.check("提示用户开启后回到这里", wait_toast(page, "通知使用权"))
    run.check("没开启就仍是未开启（权限要回到应用后才会重新读取）", tid(page, "capture-notification").get_attribute("data-on") == "false")
    wait_toasts_gone(page)

    # ---- 短信识别：被拒绝 → 仍未开启；授予 → 已开启 ----
    set_capture(sms_grantable=False)
    tid(page, "capture-sms-enable").click()
    state = wait_capture(lambda s: s["smsPermissionRequests"] == 1)
    run.check("点短信识别的「去开启」→ 走到平台的「申请短信权限」", state["smsPermissionRequests"] == 1, str(state))
    run.check("被拒绝：提示没有获得短信权限", wait_toast(page, "没有获得短信权限"))
    run.check("被拒绝：短信识别仍是未开启", tid(page, "capture-sms").get_attribute("data-on") == "false")
    wait_toasts_gone(page)

    set_capture(sms_grantable=True)
    tid(page, "capture-sms-enable").click()
    run.check("授予：提示「短信权限已授予」", wait_toast(page, "短信权限已授予"))
    run.check(
        "授予后短信识别行变为「已开启」，整体状态变为 partial",
        poll(page, "() => document.querySelector('[data-testid=\"capture-sms\"]')?.getAttribute('data-on') === 'true' && document.querySelector('[data-testid=\"capture-status\"]')?.getAttribute('data-state') === 'partial'"),
    )
    run.check("已开启的行显示「已开启」徽标，另一行仍有「去开启」", tid(page, "capture-sms-state").inner_text().strip() == "已开启" and tid(page, "capture-notification-enable").is_visible())
    wait_toasts_gone(page)

    # ---- 两项都开启：绿色细状态条 + 设置 ----
    set_capture(notification=True)
    open_pending(page, "empty")
    status = tid(page, "capture-status")
    run.check("两项都开启：一条细状态条（data-state=ready），没有权限行", status.get_attribute("data-state") == "ready" and tid(page, "capture-notification").count() == 0)
    run.check("状态条文字与「设置」入口", tid(page, "capture-text").inner_text().strip() == "通知栏识别、短信识别均已开启" and tid(page, "capture-settings").inner_text().strip() == "设置")
    style = page.evaluate(
        """() => { const el = document.querySelector('[data-testid="capture-status"]'); const cs = getComputedStyle(el); const r = el.getBoundingClientRect();
           return { bg: cs.backgroundColor, color: cs.color, h: r.height, role: el.getAttribute('role') }; }"""
    )
    run.check(
        "状态条是 15% 绿浅底 + 深绿字（令牌），高度 ≥ 44",
        style["bg"] == token_color(page, "--ft-capture-ready-bg") and style["color"] == token_color(page, "--ft-capture-ready-text") and style["h"] >= 44,
        str(style),
    )
    run.check("状态条是 role=status（读屏播报）", style["role"] == "status")
    link = page.evaluate("() => { const r = document.querySelector('[data-testid=\"capture-settings\"]').getBoundingClientRect(); return { w: r.width, h: r.height }; }")
    run.check("「设置」点击区域 ≥ 44 × 44", link["w"] >= 44 and link["h"] >= 44, str(link))
    shot(page, "pending-ready")

    tid(page, "capture-settings").click()
    sheet = tid(page, "capture-settings-sheet")
    run.check(
        "点「设置」→ 菜单：通知使用权（系统设置）/ 短信识别（读取历史短信）",
        visible(sheet) and "通知使用权" in tid(page, "capture-settings-notification").inner_text() and "短信识别" in tid(page, "capture-settings-sms").inner_text(),
    )
    tid(page, "capture-settings-notification").click()
    state = wait_capture(lambda s: s["notificationSettingsOpened"] == 2)
    run.check("菜单里的「通知使用权」→ 再次走到平台的设置页，菜单收起", state["notificationSettingsOpened"] == 2 and gone(sheet), str(state))
    wait_toasts_gone(page)

    tid(page, "capture-settings").click()
    visible(sheet)
    tid(page, "capture-settings-sms").click()
    wait_url(page, r"^/sms-recognition$")
    tid(page, "sms-page").wait_for(state="visible")
    run.check("菜单里的「短信识别」→ 进入短信识别页（页面栈推入）", page.evaluate("() => location.pathname") == "/sms-recognition")
    page.go_back()
    wait_url(page, r"^/pending-bills$")
    run.check("返回待确认账单页（短信识别页退出）", gone(tid(page, "sms-page"), 6000) and visible(tid(page, "pending-page"), 3000))

    set_capture(reset=True)


# ---------------------------------------------------------------------------------------------- B–G 共用数据


class Seed:
    """B–E 共用的一组数据：两个账本 + 三张待确认账单（列表自上而下 c1、c2、c3，后插入的排在前面）。"""

    def __init__(self, db: LocalDb, user_id: str, now: datetime) -> None:
        self.db, self.user_id, self.now = db, user_id, now
        self.ledger_a_name, self.ledger_b_name = unique_name("日常"), unique_name("旅行")
        self.ledger_a = db.seed_ledger(user_id, self.ledger_a_name, sort_order=1)
        self.ledger_b = db.seed_ledger(user_id, self.ledger_b_name, sort_order=2)
        db.set_user_defaults(user_id, ledger_id=self.ledger_a)

        self.c1_note = "微信支付 向 星巴克(国贸店)付款\n优惠  5 元"
        self.c2_note = "【招商银行】您尾号1234的账户于11月24日收入工资12,345,678.90元，当前余额98,765.43元，请注意查收，谢谢使用招商银行"
        self.c3_note = "未匹配的渠道"
        self.c1_when = minute(now)
        self.c2_when = minute((now - timedelta(days=3)).replace(hour=9, minute=5))
        self.c3_when = minute((now - timedelta(days=1)).replace(hour=23, minute=59))

        db.delete_pending_bills()
        self.c3 = db.seed_pending_bill(amount="8.5", channel="其他渠道", note=self.c3_note, when=self.c3_when)
        self.c2 = db.seed_pending_bill(amount="12345678.9", bill_type=BILL_TYPE_INCOME, channel="银行卡", note=self.c2_note, when=self.c2_when, source=SOURCE_SMS)
        self.c1 = db.seed_pending_bill(amount="35", channel="微信支付", note=self.c1_note, when=self.c1_when, source=SOURCE_NOTIFICATION)

        self.expense_category = db.category_id_by_name("其他支出", BILL_TYPE_EXPENSE)
        self.income_category = db.category_id_by_name("其他收入", BILL_TYPE_INCOME)
        self.wechat = db.channel_id_by_name("微信支付")
        self.alipay = db.channel_id_by_name("支付宝")
        self.bank_card = db.channel_id_by_name("银行卡")
        self.cash = db.channel_id_by_name("现金")


# ---------------------------------------------------------------------------------------------- B. 卡片内容


def check_cards(run: TestRun, page, d: Seed) -> None:
    open_pending(page)
    wait_attr(page, CONTENT, "data-count", "3")
    content_text = tid(page, "pending-content").inner_text()
    run.check("三张待确认账单：计数「3 笔待确认」+ 说明「确认前不计入统计」", tid(page, "pending-count").inner_text().strip() == "3 笔待确认" and "确认前不计入统计" in content_text, tid(page, "pending-count").inner_text())
    glass = page.evaluate(GLASS_LAYERS_JS)
    run.check("有待确认项时右上角出现「全部忽略」，每屏玻璃层 ≤ 3", tid(page, "pending-ignore-all").is_visible() and glass <= 3, str(glass))

    rows = cards(page)
    run.check("列表顺序：后捕获的在上（c1、c2、c3）", [r["id"].lower() for r in rows] == [d.c1.lower(), d.c2.lower(), d.c3.lower()], str([r["id"] for r in rows]))
    if len(rows) != 3:
        return
    c1, c2, c3 = rows
    now = d.now

    run.check("c1 来源行「微信支付 · 通知栏」、数据来源 notification、类型 expense", c1["sourceLine"] == "微信支付 · 通知栏" and c1["source"] == "notification" and c1["type"] == "expense", str(c1))
    run.check("c1 时间「今天 HH:mm」", c1["time"] == time_label(d.c1_when, now), f"{c1['time']} vs {time_label(d.c1_when, now)}")
    run.check(
        "c1 金额「-¥35.00」、中性色（不是红 / 绿）",
        c1["amount"] == "-¥35.00" and c1["amountColor"] == token_color(page, "--ft-label") and c1["amountLong"] is None,
        f"{c1['amount']} {c1['amountColor']} {c1['amountLong']}",
    )
    run.check("c1 分类显示「其他支出」（入账时会使用的分类），备注摘要折叠了换行与连续空白", c1["category"] == "其他支出" and c1["note"] == note_summary(d.c1_note), f"{c1['category']} | {c1['note']}")

    run.check("c2 来源行「银行卡 · 短信识别」、数据来源 sms、类型 income", c2["sourceLine"] == "银行卡 · 短信识别" and c2["source"] == "sms" and c2["type"] == "income", str(c2))
    run.check("c2 时间按日期显示（三天前 09:05）", c2["time"] == time_label(d.c2_when, now), f"{c2['time']} vs {time_label(d.c2_when, now)}")
    run.check(
        "c2 收入金额「+¥12,345,678.90」用收入绿，位数多缩一档（data-long）",
        c2["amount"] == "+¥12,345,678.90" and c2["amountColor"] == token_color(page, "--ft-income-text") and c2["amountLong"] == "true",
        f"{c2['amount']} {c2['amountColor']} {c2['amountLong']}",
    )
    run.check("c2 分类显示「其他收入」；备注超过 40 字被截断加「…」", c2["category"] == "其他收入" and c2["note"] == note_summary(d.c2_note) and c2["note"].endswith("…") and len(c2["note"]) == 41, f"{c2['category']} | {c2['note']}")

    run.check("c3 来源行「其他渠道 · 通知栏」（识别出的渠道名原样显示）、昨天 23:59", c3["sourceLine"] == "其他渠道 · 通知栏" and c3["time"] == "昨天 23:59", f"{c3['sourceLine']} {c3['time']}")

    for index, row in enumerate(rows, start=1):
        run.check(f"c{index} 账本芯片显示默认账本、未标记缺失", row["ledgerText"] == d.ledger_a_name and row["ledgerMissing"] == "false" and same_id(row["ledgerId"], d.ledger_a), f"{row['ledgerText']} {row['ledgerMissing']}")
    boxes_ok = all(r["ledger"]["h"] >= 44 and r["ignore"]["h"] >= 44 and r["confirm"]["h"] >= 44 and r["edit"]["h"] >= 44 and r["ignore"]["w"] >= 44 and r["confirm"]["w"] >= 44 for r in rows)
    run.check("卡片上的账本芯片 / 忽略 / 确认入账 / 主行点击区域 ≥ 44", boxes_ok, str([(r["ledger"]["h"], r["ignore"]["h"], r["confirm"]["h"], r["edit"]["h"]) for r in rows]))
    run.check(
        "忽略与确认入账在同一行、确认入账在右（主操作靠右）",
        all(abs(r["ignore"]["t"] - r["confirm"]["t"]) <= 2 and r["ignore"]["r"] <= r["confirm"]["l"] + 0.5 for r in rows),
        str([(r["ignore"], r["confirm"]) for r in rows[:1]]),
    )
    run.check(
        "卡片都在屏幕内、金额不冲出卡片",
        all(r["card"]["l"] >= 0 and r["card"]["r"] <= SCREEN_W and r["amountBox"]["r"] <= r["card"]["r"] and r["amountBox"]["l"] >= r["edit"]["l"] for r in rows),
        str([(r["card"], r["amountBox"]) for r in rows[:2]]),
    )
    summary1 = f"支出 {money(35)}，微信支付 · 通知栏，{time_label(d.c1_when, now)}"
    run.check(
        "读屏名称：主行「修改后入账：…」、确认「确认入账：…」带摘要（类型 金额，来源，时间）",
        c1["editAria"] == f"修改后入账：{summary1}" and c1["confirmAria"] == f"确认入账：{summary1}",
        f"{c1['editAria']} | {c1['confirmAria']}",
    )
    overflow = page.evaluate(PAGE_BOX_JS)["overflowX"]
    run.check("没有横向溢出", overflow <= 0, str(overflow))
    shot(page, "pending-cards")


# ---------------------------------------------------------------------------------------------- C. 一键确认入账


def check_one_tap_confirm(run: TestRun, page, d: Seed) -> None:
    db, user_id = d.db, d.user_id
    before = {b["Id"] for b in db.bills_of_user(user_id)}

    # 先在 c1 的卡片上把账本换成「旅行」：账本菜单里是自己的账本，当前账本带对勾；另外两张卡片不受影响
    card = card_loc(page, d.c1)
    card.get_by_test_id("pending-ledger").click()
    menu = tid(page, "pending-ledger-menu")
    run.check("点账本芯片 → 账本菜单：自己的两个账本 + 「新建账本…」", visible(menu) and menu.get_by_test_id("ledger-option").count() == 2 and menu.get_by_test_id("ledger-option-new").is_visible())
    selected = menu.locator('[data-testid="ledger-option"][data-selected="true"]')
    run.check("当前使用的账本（默认账本）带对勾", selected.count() == 1 and selected.first.inner_text().strip().startswith(d.ledger_a_name), str(selected.count()))
    menu.get_by_test_id("ledger-option").filter(has_text=d.ledger_b_name).click()
    run.check(
        "选了「旅行」→ c1 的账本芯片变成它，菜单收起",
        poll(page, "(n) => document.querySelector('[data-testid=\"pending-card\"] [data-testid=\"pending-ledger-text\"]')?.textContent.trim() === n", arg=d.ledger_b_name) and gone(menu),
    )
    others = cards(page)[1:]
    run.check("其它两张卡片仍用默认账本", all(r["ledgerText"] == d.ledger_a_name for r in others), str([r["ledgerText"] for r in others]))

    card.get_by_test_id("pending-confirm").click()
    run.check("点「确认入账」→ 提示「账单创建成功」", wait_toast(page, "账单创建成功"))
    run.check("c1 的卡片立刻消失，计数变「2 笔待确认」", gone(card, 6000) and count_text_is(page, "2 笔待确认"))

    created = wait_bills(db, user_id, len(before) + 1)
    fresh = [b for b in created if b["Id"] not in before]
    run.check("只新增一笔账单", len(fresh) == 1, str(len(fresh)))
    bill = fresh[0] if fresh else {}
    run.check(
        "账单：金额 / 类型 / 数据来源（通知栏）/ 账本（卡片上选的旅行）/ 支付渠道（按名称匹配到微信支付）/ 分类（其他支出）/ 备注（通知原文）/ 交易时间（应用时区 → UTC）",
        bool(bill) and bill_matches(bill, amount=35, bill_type=BILL_TYPE_EXPENSE, source=SOURCE_NOTIFICATION, ledger_id=d.ledger_b, channel_id=d.wechat, category_id=d.expense_category, note=d.c1_note, when=d.c1_when),
        bill_detail(bill),
    )
    remaining = {p["Id"] for p in db.pending_bills()}
    run.check("待确认项从库里移除（另两条还在）", remaining == {d.c2.upper(), d.c3.upper()}, str(remaining))
    wait_toasts_gone(page)

    # 其它页面随之刷新：回首页，角标与待确认数一致，横幅还在
    open_app(page, "/")
    wait_tab_bar(page)
    run.check(
        "首页铃铛角标 = 剩余待确认数（2）",
        poll(page, "() => document.querySelector('[data-testid=\"home-bell-badge\"]')?.textContent.trim() === '2'"),
        str(page.evaluate("() => document.querySelector('[data-testid=\"home-bell-badge\"]')?.textContent")),
    )
    run.check("首页出现待确认横幅", visible(tid(page, "home-pending-banner"), 4000))
    tid(page, "home-pending-banner").click()
    wait_url(page, r"^/pending-bills$")
    tid(page, "pending-page").wait_for(state="visible")
    wait_attr(page, CONTENT, "data-state", "ready")
    wait_animations_settled(page)
    run.check("点首页横幅 → 待确认账单页（页面栈推入），剩 2 张卡片", len(cards(page)) == 2, str(len(cards(page))))
    page.go_back()
    wait_url(page, r"^/$")
    run.check("返回首页（待确认账单页退出）", gone(tid(page, "pending-page"), 6000))
    open_pending(page)


# ---------------------------------------------------------------------------------------------- D. 修改后入账（Sheet）


def check_confirm_sheet(run: TestRun, page, d: Seed) -> None:
    db, user_id = d.db, d.user_id
    before = {b["Id"] for b in db.bills_of_user(user_id)}

    open_sheet_from_row(page, d.c2)
    s = sheet_state(page)
    run.check("Sheet 标题「确认账单」、来源行「银行卡 · 短信识别」+ 时间", bool(s) and s["title"] == "确认账单" and s["source"] == "银行卡 · 短信识别" and s["time"] == time_label(d.c2_when, d.now), str(s))
    run.check("备注原文摘要只读展示；渠道匹配上了没有提示", bool(s) and s["note"] == note_summary(d.c2_note) and s["hint"] is None, str(s))
    run.check(
        "预填：收入、金额 12345678.9、支付渠道「银行卡」、账本「日常」",
        bool(s) and s["type"] == "Income" and s["amount"] in ("12345678.9", "12345678.90") and s["channel"] == "银行卡" and s["ledger"] == d.ledger_a_name,
        str(s),
    )
    run.check("预填齐全：保存键点亮（没有 aria-disabled）、文字「入账」", bool(s) and s["saveDisabled"] is None and s["saveText"] == "入账", str(s))
    glass = page.evaluate(GLASS_LAYERS_JS)
    run.check("Sheet 打开时每屏玻璃层 ≤ 3", glass <= 3, str(glass))
    shot(page, "pending-confirm-sheet")

    # 账本菜单：打开看一眼（两个账本，当前账本带对勾），Esc 收起不改
    tid(page, "confirm-ledger-row").click()
    menu = tid(page, "confirm-ledger-menu")
    run.check(
        "账本行 → 账本菜单（两个账本 + 新建账本…）",
        visible(menu) and menu.get_by_test_id("ledger-option").count() == 2 and menu.get_by_test_id("ledger-option-new").is_visible(),
    )
    page.keyboard.press("Escape")
    run.check("Esc 只收起菜单，Sheet 与账本保持不变", gone(menu) and tid(page, "confirm-sheet").is_visible() and sheet_state(page)["ledger"] == d.ledger_a_name)

    # 改类型 → 支出；金额改成 88.5；支付渠道改成支付宝
    tid(page, "confirm-type").get_by_test_id("segment").nth(0).click()
    run.check("切到「支出」", poll(page, "() => document.querySelector('[data-testid=\"confirm-type\"]').getAttribute('data-value') === 'Expense'"))
    for _ in range(len(sheet_state(page)["amount"])):
        press_key(page, "backspace")
    run.check(
        "退格清空金额：金额为空、保存键置灰",
        poll(page, "() => document.querySelector('[data-testid=\"confirm-amount\"]').getAttribute('data-empty') === 'true'") and sheet_state(page)["saveDisabled"] == "true",
    )
    # 没金额就点（置灰的）「入账」：不保存，金额抖动 + 提示（过去在短信识别页的「手动补全」里验证；短信现在先进待确认账单，这条校验只剩这个 Sheet 走得到）
    watch_attr(page, '[data-testid="confirm-amount"]', "data-shake", "true")
    tid(page, "confirm-save").click(force=True)
    run.check(
        "没金额就点「入账」：提示「请输入有效的金额」、金额抖动过、没有产生账单",
        wait_toast(page, "请输入有效的金额") and watched(page) and len(new_bills(db, user_id, before)) == 0,
    )
    wait_toasts_gone(page)
    press_keys(page, "88.5")
    run.check(
        "键盘输入 88.5：金额 88.5、保存键点亮",
        poll(page, "() => document.querySelector('[data-testid=\"confirm-amount\"]').getAttribute('data-value') === '88.5'") and sheet_state(page)["saveDisabled"] is None,
        str(sheet_state(page)),
    )
    tid(page, "confirm-channel-row").click()
    channel_menu = tid(page, "confirm-channel-menu")
    run.check("支付渠道行 → 渠道菜单（预设渠道）", visible(channel_menu) and channel_menu.get_by_test_id("channel-option").count() >= 6, str(channel_menu.get_by_test_id("channel-option").count()))
    channel_menu.get_by_test_id("channel-option").filter(has_text="支付宝").first.click()
    run.check("选支付宝：渠道行显示它、菜单收起", poll(page, "() => document.querySelector('[data-testid=\"confirm-channel-row\"] .ft-cell__value')?.textContent.trim() === '支付宝'") and gone(channel_menu))

    tid(page, "confirm-save").click()
    run.check("点「入账」→ 提示「账单创建成功」、Sheet 收起", wait_toast(page, "账单创建成功") and gone(tid(page, "confirm-sheet"), 6000))
    run.check("c2 的卡片消失，计数变「1 笔待确认」", gone(card_loc(page, d.c2), 6000) and count_text_is(page, "1 笔待确认"))

    created = wait_bills(db, user_id, len(before) + 1)
    fresh = [b for b in created if b["Id"] not in before]
    bill = fresh[0] if fresh else {}
    run.check(
        "账单以修改后的值落库：支出 88.5、支付宝、其他支出分类；数据来源仍是短信识别、账本是默认账本、备注与交易时间沿用待确认项",
        len(fresh) == 1 and bill_matches(bill, amount=88.5, bill_type=BILL_TYPE_EXPENSE, source=SOURCE_SMS, ledger_id=d.ledger_a, channel_id=d.alipay, category_id=d.expense_category, note=d.c2_note, when=d.c2_when),
        bill_detail(bill),
    )
    run.check("待确认项从库里移除（只剩 c3）", {p["Id"] for p in db.pending_bills()} == {d.c3.upper()}, str([p["Id"] for p in db.pending_bills()]))
    wait_toasts_gone(page)


# ---------------------------------------------------------------------------------------------- E. 支付渠道没匹配到


def check_channel_not_matched(run: TestRun, page, d: Seed) -> None:
    db, user_id = d.db, d.user_id
    before = {b["Id"] for b in db.bills_of_user(user_id)}

    card_loc(page, d.c3).get_by_test_id("pending-confirm").click()
    tid(page, "confirm-sheet").wait_for(state="visible")
    wait_animations_settled(page)
    s = sheet_state(page)
    run.check("渠道没匹配到：点「确认入账」不猜、不入账，打开「确认账单」Sheet", s is not None and len(new_bills(db, user_id, before)) == 0, str(s))
    run.check("Sheet 顶部提示「没能匹配到支付渠道，请选择一个再入账」", bool(s) and s["hint"] == "没能匹配到支付渠道，请选择一个再入账", str(s))
    run.check("支付渠道行标记为缺失（选择支付渠道），保存键置灰", bool(s) and s["channel"] == "选择支付渠道" and s["channelMissing"] == "true" and s["saveDisabled"] == "true", str(s))
    run.check(
        "其余预填：支出 8.50（非整数金额固定两位小数，同编辑账单）、账本「日常」、来源「其他渠道 · 通知栏」、昨天 23:59",
        bool(s) and s["type"] == "Expense" and s["amount"] == "8.50" and s["ledger"] == d.ledger_a_name and s["source"] == "其他渠道 · 通知栏" and s["time"] == "昨天 23:59",
        str(s),
    )
    shot(page, "pending-confirm-channel-missing")

    # 点按置灰的保存键：不保存，引导选渠道（渠道行抖动 + 弹出菜单）
    watch_attr(page, '[data-testid="confirm-rows"]', "data-shake", "true")
    tid(page, "confirm-save").click(force=True)
    channel_menu = tid(page, "confirm-channel-menu")
    run.check("点按置灰的「入账」→ 弹出支付渠道菜单（引导，不是静默失败）", visible(channel_menu))
    run.check("渠道行抖动过（data-shake 曾为 true）", watched(page))
    page.keyboard.press("Escape")
    run.check("Esc 收起渠道菜单，Sheet 仍在，引导期间没有产生账单", gone(channel_menu) and tid(page, "confirm-sheet").is_visible() and len(new_bills(db, user_id, before)) == 0)

    # 改了金额再关闭，重新打开应按待确认项原值重置
    press_keys(page, "9")
    run.check("已经两位小数的 8.50 再敲 9 敲不进第三位（金额不变）", sheet_state(page)["amount"] == "8.50")
    press_key(page, "backspace")
    press_keys(page, "9")
    run.check("退格再敲 9 → 金额 8.59", poll(page, "() => document.querySelector('[data-testid=\"confirm-amount\"]').getAttribute('data-value') === '8.59'"))
    tid(page, "sheet-close").click()
    run.check("点 ✕ 关闭 Sheet，卡片仍在、没有账单", gone(tid(page, "confirm-sheet"), 6000) and card_loc(page, d.c3).count() == 1 and len(new_bills(db, user_id, before)) == 0)
    open_sheet_from_row(page, d.c3)
    s = sheet_state(page)
    run.check("再次打开按待确认项原值重置（金额回到 8.50、渠道仍缺）", bool(s) and s["amount"] == "8.50" and s["channelMissing"] == "true", str(s))

    # 选渠道（现金）→ 入账
    tid(page, "confirm-channel-row").click()
    channel_menu = tid(page, "confirm-channel-menu")
    visible(channel_menu)
    channel_menu.get_by_test_id("channel-option").filter(has_text="现金").first.click()
    run.check(
        "选现金 → 渠道行显示它、不再缺失、保存键点亮",
        poll(page, "() => document.querySelector('[data-testid=\"confirm-channel-row\"]').getAttribute('data-missing') === 'false' && document.querySelector('[data-testid=\"confirm-save\"]').getAttribute('aria-disabled') === null"),
    )
    tid(page, "confirm-save").click()
    run.check("入账成功：提示、Sheet 收起", wait_toast(page, "账单创建成功") and gone(tid(page, "confirm-sheet"), 6000))
    fresh = new_bills(db, user_id, before)
    bill = fresh[0] if fresh else {}
    run.check(
        "账单：渠道是用户选的现金、金额 8.5、备注原文、数据来源通知栏、昨天 23:59",
        len(fresh) == 1 and bill_matches(bill, amount=8.5, bill_type=BILL_TYPE_EXPENSE, source=SOURCE_NOTIFICATION, ledger_id=d.ledger_a, channel_id=d.cash, category_id=d.expense_category, note=d.c3_note, when=d.c3_when),
        bill_detail(bill),
    )
    run.check(
        "待确认项都处理完：列表变空状态、库里没有待确认项",
        poll(page, "() => document.querySelector('[data-testid=\"pending-content\"]').getAttribute('data-state') === 'empty'") and db.pending_bills() == [],
        str(db.pending_bills()),
    )
    wait_toasts_gone(page)


# ---------------------------------------------------------------------------------------------- F. 忽略


def check_ignore(run: TestRun, page, d: Seed) -> None:
    db, user_id = d.db, d.user_id
    before = {b["Id"] for b in db.bills_of_user(user_id)}
    now = datetime.now()
    db.delete_pending_bills()
    for amount in ("11", "22", "33"):
        db.seed_pending_bill(amount=amount, channel="支付宝", note=unique_name("忽略"), when=minute(now))
    open_pending(page)
    wait_attr(page, CONTENT, "data-count", "3")
    top = cards(page)[0]["id"]

    card_loc(page, top).get_by_test_id("pending-ignore").click()
    run.check("点「忽略」→ 提示「已忽略」", wait_toast(page, "已忽略"))
    run.check("那张卡片消失，计数变「2 笔待确认」，库里少一条", gone(card_loc(page, top), 6000) and count_text_is(page, "2 笔待确认") and len(db.pending_bills()) == 2)
    run.check("忽略不产生账单", len(new_bills(db, user_id, before)) == 0)
    wait_toasts_gone(page)

    tid(page, "pending-ignore-all").click()
    alert = tid(page, "pending-ignore-all-alert")
    run.check(
        "点「全部忽略」→ 确认 Alert：标题与说明带上数量",
        visible(alert) and tid(page, "alert-title").inner_text().strip() == "全部忽略？" and tid(page, "alert-message").inner_text().strip() == "2 笔待确认账单将被移除，不会计入账单。",
        tid(page, "alert-message").inner_text(),
    )
    run.check("Alert 是 alertdialog，破坏性操作的确认钮文字「全部忽略」", alert.get_attribute("role") == "alertdialog" and tid(page, "alert-confirm").inner_text().strip() == "全部忽略", str(alert.get_attribute("role")))
    tid(page, "alert-cancel").click()
    run.check("取消：Alert 收起，两张卡片都还在", gone(alert) and len(cards(page)) == 2 and len(db.pending_bills()) == 2)

    tid(page, "pending-ignore-all").click()
    visible(alert)
    tid(page, "alert-confirm").click()
    run.check("确认：提示「已全部忽略」", wait_toast(page, "已全部忽略"))
    run.check(
        "列表变空状态，库里没有待确认项，「全部忽略」消失",
        poll(page, "() => document.querySelector('[data-testid=\"pending-content\"]').getAttribute('data-state') === 'empty'") and db.pending_bills() == [] and tid(page, "pending-ignore-all").count() == 0,
        str(db.pending_bills()),
    )
    run.check("全部忽略也不产生账单", len(new_bills(db, user_id, before)) == 0)
    wait_toasts_gone(page)


# ---------------------------------------------------------------------------------------------- G. 防重复入账


def check_no_duplicate(run: TestRun, page, d: Seed) -> None:
    db, user_id = d.db, d.user_id
    before = {b["Id"] for b in db.bills_of_user(user_id)}
    db.delete_pending_bills()
    note = unique_name("连点")
    pending_id = db.seed_pending_bill(amount="66", channel="微信支付", note=note, when=minute(datetime.now()))
    open_pending(page)
    wait_attr(page, CONTENT, "data-count", "1")

    # 同一个事件循环里连点两次（比人手快得多）：第二下到达时第一下还在入账中
    page.evaluate("() => { const b = document.querySelector('[data-testid=\"pending-confirm\"]'); b.click(); b.click(); }")
    wait_bills(db, user_id, len(before) + 1)
    settle(1.0)
    fresh = new_bills(db, user_id, before)
    run.check("「确认入账」连点两下只产生一笔账单", len(fresh) == 1 and fresh[0]["Note"] == note, str(len(fresh)))
    run.check("待确认项被移除、卡片消失", db.pending_bills() == [] and gone(card_loc(page, pending_id), 6000))
    wait_toasts_gone(page)


# ---------------------------------------------------------------------------------------------- H. 账本必填（新用户没有账本，各开一个会话）


def run_ledger_required_chip(run: TestRun) -> None:
    """卡片路径：账本芯片是「选择账本」，点「确认入账」不入账而是弹出账本菜单，行内建好账本后再入账。"""
    db = LocalDb()
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        db.delete_pending_bills()
        note = unique_name("无账本卡片")
        db.seed_pending_bill(amount="45", channel="支付宝", note=note, when=minute(datetime.now()))
        open_pending(page)
        wait_attr(page, CONTENT, "data-count", "1")

        row = cards(page)[0]
        run.check("没有账本：账本芯片是「选择账本」、标记缺失", row["ledgerText"] == "选择账本" and row["ledgerMissing"] == "true" and not row["ledgerId"], str(row))
        chip_style = page.evaluate("() => { const cs = getComputedStyle(document.querySelector('[data-testid=\"pending-ledger\"]')); return { bg: cs.backgroundColor, color: cs.color }; }")
        run.check(
            "缺失的账本芯片：浅红底 + 深红字（令牌）",
            chip_style["bg"] == token_color(page, "--ft-pcard-missing-bg") and chip_style["color"] == token_color(page, "--ft-danger-text"),
            str(chip_style),
        )
        shot(page, "pending-ledger-missing")

        tid(page, "pending-confirm").click()
        menu = tid(page, "pending-ledger-menu")
        run.check("点「确认入账」→ 不入账，弹出账本菜单", visible(menu))
        run.check("账本菜单里没有账本，只有「新建账本…」", menu.get_by_test_id("ledger-option").count() == 0 and "新建账本" in menu.get_by_test_id("ledger-option-new").inner_text())
        settle()
        run.check("没有产生账单、待确认项还在", db.bills_of_user(user_id) == [] and len(db.pending_bills()) == 1)

        # 行内新建账本
        menu.get_by_test_id("ledger-option-new").click()
        name = unique_name("行内账本")
        run.check("点「新建账本…」→ 新建账本 Sheet，菜单收起", visible(tid(page, "ledger-sheet")) and gone(menu))
        run.check("填名称保存后 Sheet 收起", create_ledger_in_sheet(page, name))
        run.check(
            "卡片的账本芯片显示新账本、不再缺失",
            poll(page, "(n) => document.querySelector('[data-testid=\"pending-ledger-text\"]')?.textContent.trim() === n && document.querySelector('[data-testid=\"pending-ledger\"]').getAttribute('data-missing') === 'false'", arg=name),
        )
        ledgers = db.ledgers_of_user(user_id)
        run.check("账本落库（归当前用户）", [l["Name"] for l in ledgers] == [name], str(ledgers))

        tid(page, "pending-confirm").click()
        run.check("再点「确认入账」→ 入账成功", wait_toast(page, "账单创建成功"))
        bills = wait_bills(db, user_id, 1)
        run.check(
            "账单记进新建的账本、备注是通知原文、数据来源通知栏",
            len(bills) == 1 and bool(ledgers) and same_id(bills[0]["LedgerId"], ledgers[0]["Id"]) and bills[0]["Note"] == note and bills[0]["Source"] == SOURCE_NOTIFICATION,
            str(bills),
        )
        wait_toasts_gone(page)

        db.seed_pending_bill(amount="12", channel="微信支付", note=unique_name("刷新后"), when=minute(datetime.now()))
        open_pending(page)
        run.check("刷新后账本芯片用唯一的账本（默认账本回退到第一个自己的账本）", cards(page)[0]["ledgerText"] == name, str(cards(page)[0]["ledgerText"]))
        run.check_clean(s.errors, "H 节卡片路径会话无页面异常 / 控制台错误 / 失败请求")
    db.delete_pending_bills()


def run_ledger_required_sheet(run: TestRun) -> None:
    """Sheet 路径：置灰的「入账」键引导——账本行抖动并弹出账本菜单，行内建好账本后入账。"""
    db = LocalDb()
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        db.delete_pending_bills()
        note = unique_name("无账本Sheet")
        when = minute(datetime.now())
        pending_id = db.seed_pending_bill(amount="45", channel="支付宝", note=note, when=when)
        open_pending(page)
        wait_attr(page, CONTENT, "data-count", "1")

        open_sheet_from_row(page, pending_id)
        sh = sheet_state(page)
        run.check(
            "没有账本：Sheet 里账本行是「选择账本」、标记缺失，保存键置灰（渠道已匹配）",
            bool(sh) and sh["ledger"] == "选择账本" and sh["ledgerMissing"] == "true" and sh["saveDisabled"] == "true" and sh["channelMissing"] == "false",
            str(sh),
        )
        watch_attr(page, '[data-testid="confirm-rows"]', "data-shake", "true")
        tid(page, "confirm-save").click(force=True)
        menu = tid(page, "confirm-ledger-menu")
        run.check("点按置灰的「入账」→ 弹出账本菜单（引导）", visible(menu))
        run.check("账本行抖动过（data-shake 曾为 true）", watched(page))
        run.check("菜单里没有账本，只有「新建账本…」；没有产生账单", menu.get_by_test_id("ledger-option").count() == 0 and menu.get_by_test_id("ledger-option-new").is_visible() and db.bills_of_user(user_id) == [])

        menu.get_by_test_id("ledger-option-new").click()
        name = unique_name("Sheet行内账本")
        run.check("新建账本 Sheet 叠在确认账单 Sheet 之上，保存后收起", create_ledger_in_sheet(page, name))
        run.check(
            "行内建好账本后：账本行显示它、不再缺失、保存键点亮",
            poll(page, "(n) => { const r = document.querySelector('[data-testid=\"confirm-ledger-row\"]'); return r.querySelector('.ft-cell__value')?.textContent.trim() === n && r.getAttribute('data-missing') === 'false' && document.querySelector('[data-testid=\"confirm-save\"]').getAttribute('aria-disabled') === null; }", arg=name),
            str(sheet_state(page)),
        )
        tid(page, "confirm-save").click()
        run.check("入账成功：提示、Sheet 收起", wait_toast(page, "账单创建成功") and gone(tid(page, "confirm-sheet"), 6000))
        ledgers = db.ledgers_of_user(user_id)
        bills = wait_bills(db, user_id, 1)
        run.check(
            "账单记进新建的账本、渠道支付宝、备注与交易时间沿用待确认项",
            len(bills) == 1 and bool(ledgers) and bill_matches(bills[0], amount=45, bill_type=BILL_TYPE_EXPENSE, source=SOURCE_NOTIFICATION, ledger_id=ledgers[0]["Id"], channel_id=db.channel_id_by_name("支付宝"), category_id=db.category_id_by_name("其他支出", BILL_TYPE_EXPENSE), note=note, when=when),
            bill_detail(bills[0]) if bills else "no bills",
        )
        run.check_clean(s.errors, "H 节 Sheet 路径会话无页面异常 / 控制台错误 / 失败请求")
    db.delete_pending_bills()


# ---------------------------------------------------------------------------------------------- I. 窄屏 320


def run_narrow(run: TestRun) -> None:
    db = LocalDb()
    set_capture(reset=True)
    with session(viewport={"width": 320, "height": 640}) as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        db.delete_pending_bills()
        db.seed_ledger(user_id, unique_name("窄屏账本"))
        now = datetime.now()
        db.seed_pending_bill(amount="12345678.9", bill_type=BILL_TYPE_INCOME, channel="银行卡", note="【招商银行】您尾号1234的账户收到一笔很长很长很长很长很长的转入款项说明文字", source=SOURCE_SMS, when=minute(now))
        db.seed_pending_bill(amount="35", channel="微信支付", note="微信支付 向 星巴克付款", when=minute(now))
        open_pending(page)
        wait_attr(page, CONTENT, "data-count", "2")

        info = page.evaluate(PAGE_BOX_JS)
        rows = cards(page)
        run.check("320 宽：没有横向溢出", info["overflowX"] <= 0, str(info["overflowX"]))
        run.check(
            "320 宽：导航栏标题（文字）没有被「全部忽略」压住，也没有压住返回按钮",
            info["title"]["r"] <= info["ignoreAll"]["l"] + 0.5 and info["back"]["r"] <= info["title"]["l"] + 0.5,
            f"title={info['title']} ignoreAll={info['ignoreAll']} back={info['back']}",
        )
        run.check(
            "320 宽：卡片都在屏幕内、金额不冲出卡片",
            all(r["card"]["l"] >= 0 and r["card"]["r"] <= 320 and r["amountBox"]["r"] <= r["card"]["r"] for r in rows),
            str([(r["card"], r["amountBox"]) for r in rows]),
        )
        run.check(
            "320 宽：账本芯片独占一行，忽略 / 确认入账平分下一行，点击区域 ≥ 44",
            all(r["ledger"]["b"] <= r["ignore"]["t"] + 1 and r["ledger"]["w"] >= r["card"]["w"] - 2 * 16 - 2 and r["ignore"]["h"] >= 44 and r["confirm"]["h"] >= 44 and abs(r["ignore"]["w"] - r["confirm"]["w"]) <= 2 for r in rows),
            str([(r["ledger"], r["ignore"], r["confirm"]) for r in rows[:1]]),
        )
        glass = page.evaluate(GLASS_LAYERS_JS)
        run.check("320 宽：每屏玻璃层 ≤ 3", glass <= 3, str(glass))
        shot(page, "pending-320")

        # 两项都开启的状态条在窄屏下也不溢出
        set_capture(notification=True, sms=True)
        open_pending(page)
        wait_attr(page, CONTENT, "data-count", "2")
        info = page.evaluate(PAGE_BOX_JS)
        capture = info["capture"]
        run.check("320 宽：绿色状态条在屏幕内、不溢出", capture["l"] >= 0 and capture["r"] <= 320 and info["overflowX"] <= 0, str(capture))

        set_capture(
            messages=[
                {"id": 9301, "address": "95588", "body": "【招商银行】您尾号1234的信用卡消费人民币35.00元，这是一条比较长的短信正文，用来检查窄屏下结果卡片里的号码、时间和去向标签不会互相挤出卡片", "date": minute(now - timedelta(hours=1))},
                {"id": 9302, "address": "95533", "body": "【某某银行】您的账户有一笔交易，请注意查收", "date": minute(now - timedelta(hours=2))},
            ]
        )
        open_sms(page)
        foot = page.evaluate(
            """() => { const r = document.querySelector('[data-testid="sms-read"]').getBoundingClientRect();
               return { l: r.left, r: r.right, h: r.height, overflowX: document.documentElement.scrollWidth - document.documentElement.clientWidth }; }"""
        )
        run.check("320 宽（短信识别页）：底部主按钮在屏幕内、高度 ≥ 44、没有横向溢出", foot["l"] >= 0 and foot["r"] <= 320 and foot["h"] >= 44 and foot["overflowX"] <= 0, str(foot))
        run.check("320 宽（短信识别页）：读取短信 → 读取结束", read_sms(page, 1))
        st = sms_state(page)
        sm = st["summary"] or {}
        run.check(
            "320 宽（短信识别页）：摘要分组与去向标签都在屏幕内、没有横向溢出（卡片里的标签不被挤出去）",
            st["overflowX"] <= 0 and all(r["badgeInside"] for r in st["results"]) and sm.get("added") == "1" and sm.get("noAmount") == "1",
            f"{st['overflowX']} {[r['badgeInside'] for r in st['results']]} {sm}",
        )
        glass = page.evaluate(GLASS_LAYERS_JS)
        run.check("320 宽（短信识别页，有结果）：每屏玻璃层 ≤ 3", glass <= 3, str(glass))
        shot(page, "sms-320")
        run.check_clean(s.errors, "I 节会话无页面异常 / 控制台错误 / 失败请求")
    set_capture(reset=True)
    db.delete_pending_bills()


# ---------------------------------------------------------------------------------------------- J. 短信识别页

SMS_STATE_JS = """() => {
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  const text = (id) => { const n = q(id); return n ? n.textContent.trim() : null; };
  const rowValue = (id) => { const n = q(id); const v = n ? n.querySelector('.ft-cell__value') : null; return v ? v.textContent.trim() : null; };
  const content = q('sms-content');
  const read = q('sms-read');
  const foot = q('sms-foot');
  const summary = q('sms-summary');
  const fr = foot.getBoundingClientRect();
  const rr = read.getBoundingClientRect();
  return {
    state: content.getAttribute('data-state'),
    permission: content.getAttribute('data-permission'),
    ready: content.getAttribute('data-ready'),
    reads: content.getAttribute('data-reads'),
    count: content.getAttribute('data-count'),
    permissionOn: q('sms-permission').getAttribute('data-on'),
    grant: q('sms-permission-grant') ? q('sms-permission-grant').textContent.trim() : null,
    dateValue: q('sms-date-row').getAttribute('data-value'),
    dateText: q('sms-date-row').querySelector('.ft-cell__value').textContent.trim(),
    // 账本一行 / 账本菜单 / 每条短信上的「创建账单」按钮：短信读进待确认账单之后，这些都不该再出现
    legacyControls: document.querySelectorAll('[data-testid="sms-ledger-row"], [data-testid="sms-ledger-menu"], [data-testid="sms-create"], [data-testid="sms-done"], [data-testid="confirm-sheet"]').length,
    readDisabled: read.disabled || read.getAttribute('aria-disabled') === 'true',
    readText: read.textContent.trim(),
    readBox: { l: rr.left, r: rr.right, t: rr.top, b: rr.bottom, h: rr.height },
    foot: { position: getComputedStyle(foot).position, t: fr.top, b: fr.bottom, l: fr.left, r: fr.right },
    header: text('sms-results-header'),
    idle: text('sms-idle'),
    footer: [...document.querySelectorAll('.ft-group__footer')].map((n) => n.textContent.trim()),
    summary: summary ? {
      title: summary.querySelector('.ft-group__header') ? summary.querySelector('.ft-group__header').textContent.trim() : null,
      footer: summary.querySelector('.ft-group__footer') ? summary.querySelector('.ft-group__footer').textContent.trim() : null,
      added: summary.getAttribute('data-added'),
      duplicate: summary.getAttribute('data-duplicate'),
      noAmount: summary.getAttribute('data-no-amount'),
      overCapacity: summary.getAttribute('data-over-capacity'),
      failed: summary.getAttribute('data-failed'),
      rows: {
        added: rowValue('sms-summary-added'),
        duplicate: rowValue('sms-summary-duplicate'),
        noAmount: rowValue('sms-summary-no-amount'),
        overCapacity: rowValue('sms-summary-over-capacity'),
        failed: rowValue('sms-summary-failed'),
      },
      addedIsButton: q('sms-summary-added') ? q('sms-summary-added').tagName === 'BUTTON' : null,
      addedHeight: q('sms-summary-added') ? q('sms-summary-added').getBoundingClientRect().height : null,
    } : null,
    results: [...document.querySelectorAll('[data-testid="sms-result"]')].map((el) => {
      const badge = el.querySelector('[data-testid="sms-outcome"]');
      const card = el.getBoundingClientRect();
      const b = badge.getBoundingClientRect();
      return {
        id: el.getAttribute('data-sms-id'),
        outcome: el.getAttribute('data-outcome'),
        outcomeText: badge.textContent.trim(),
        badgeTone: [...badge.classList].find((c) => c.startsWith('ft-badge--')) || null,
        body: el.querySelector('[data-testid="sms-body"]').textContent.trim(),
        meta: el.querySelector('[data-testid="sms-meta"]').textContent.trim(),
        badgeInside: b.left >= card.left - 0.5 && b.right <= card.right + 0.5,
      };
    }),
    overflowX: document.documentElement.scrollWidth - document.documentElement.clientWidth,
  };
}"""


def sms_state(page) -> dict:
    return page.evaluate(SMS_STATE_JS)


def sms_result(page, sms_id: int):
    return page.locator(f'[data-testid="sms-result"][data-sms-id="{sms_id}"]')


def read_sms(page, reads: int) -> bool:
    """点「读取短信」，等到第 reads 次读取结束。
    页面的 data-reads 与这一次的结果在同一次渲染里更新，所以不会把上一次的结果当成这一次的（读取是异步的：先 loading，再换成新结果）。"""
    tid(page, "sms-read").click()
    return poll(page, "(n) => document.querySelector('[data-testid=\"sms-content\"]').getAttribute('data-reads') === n", arg=str(reads), timeout=15000)


def pending_matches(row: "dict | None", *, amount, bill_type: int, channel: str, note: str, when: datetime) -> bool:
    """待确认账单的一行是否是「这条短信」放进去的样子：金额 / 类型 / 渠道 / 备注（短信原文）/ 数据来源（短信识别）/
    交易时间 = 捕获时间 = 短信时间（设备本地墙上时间，原样存，不换算）。"""
    if not row:
        return False
    return (
        amount_is(row["Amount"], amount)
        and row["Type"] == bill_type
        and row["Channel"] == channel
        and row["Note"] == note
        and row["Source"] == SOURCE_SMS
        and row["TransactionTime"] == to_wall_time(when)
        and row["CapturedAt"] == to_wall_time(when)
    )


def pending_by_note(db: LocalDb) -> "dict[str, dict]":
    return {row["Note"]: row for row in db.pending_bills()}


def outcome_map(st: dict) -> "dict[str, str]":
    return {r["id"]: r["outcome"] for r in st["results"]}


def run_sms(run: TestRun) -> None:
    db = LocalDb()
    set_capture(reset=True)
    db.delete_pending_bills()
    now = datetime.now()

    def ago(hours: int) -> datetime:
        return minute(now - timedelta(hours=hours))

    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)

        messages = [
            {"id": 9001, "address": "95588", "body": "【微信支付】您向星巴克(国贸店)付款8.50元，请查收账单", "date": ago(1)},
            {"id": 9002, "address": "95555", "body": "【招商银行】您尾号1234的信用卡消费人民币35.00元", "date": ago(2)},
            {"id": 9003, "address": "95588", "body": "【工商银行】您尾号5678的账户收入工资12,345.50元", "date": ago(3)},
            {"id": 9004, "address": "95533", "body": "【某某银行】您的账户有一笔交易，请注意查收", "date": ago(4)},
        ]

        # ---- 权限未授予 ----
        open_sms(page)
        st = sms_state(page)
        run.check("短信识别页：二级页面（Tab 栏隐藏）、标题「短信识别」", tid(page, "tab-bar").is_hidden() and tid(page, "nav-title").inner_text().strip() == "短信识别")
        run.check(
            "未授权：权限行未开启 + 「去授权」，内容状态 idle / denied，权限状态已读完",
            st["permissionOn"] == "false" and st["grant"] == "去授权" and st["state"] == "idle" and st["permission"] == "denied" and st["ready"] == "true" and st["reads"] == "0",
            str(st),
        )
        default_from = (now - timedelta(days=30)).date()
        run.check(
            "读取范围：开始日期默认 30 天前（data-value / 文字）",
            st["dateValue"] == default_from.isoformat() and st["dateText"] == f"{default_from.year}年{default_from.month}月{default_from.day}日",
            f"{st['dateValue']} {st['dateText']}",
        )
        run.check("读取范围分组只有开始日期：没有账本行 / 账本菜单（账本在「待确认账单」里确认入账时再选），也没有「创建账单」按钮", st["legacyControls"] == 0, str(st["legacyControls"]))
        run.check(
            "没有权限：「读取短信」置灰，范围分组脚注说明要先授权",
            st["readDisabled"] and st["readText"] == "读取短信" and any("授予短信读取权限后" in f for f in st["footer"]),
            f"{st['readDisabled']} {st['footer']}",
        )
        run.check("还没读取过、没有权限时不显示读取提示", st["idle"] is None, str(st["idle"]))
        run.check(
            "「读取短信」固定在页面底部：fixed、贴底、满宽，按钮在屏幕内、高度 ≥ 44",
            st["foot"]["position"] == "fixed" and abs(st["foot"]["b"] - SCREEN_H) <= 1 and abs(st["foot"]["r"] - st["foot"]["l"] - SCREEN_W) <= 1 and st["readBox"]["h"] >= 44 and st["readBox"]["b"] <= SCREEN_H,
            str(st["foot"]),
        )
        run.check("没有横向溢出", st["overflowX"] <= 0, str(st["overflowX"]))
        glass = page.evaluate(GLASS_LAYERS_JS)
        run.check("每屏玻璃层 ≤ 3", glass <= 3, str(glass))
        shot(page, "sms-denied")

        set_capture(sms_grantable=False)
        tid(page, "sms-permission-grant").click()
        run.check("被拒绝：提示没有获得短信权限，仍是未授权", wait_toast(page, "没有获得短信权限") and sms_state(page)["permissionOn"] == "false")
        wait_toasts_gone(page)
        set_capture(sms_grantable=True, messages=messages)
        tid(page, "sms-permission-grant").click()
        run.check("授予：提示「权限已授予」", wait_toast(page, "权限已授予"))
        run.check(
            "授予后：内容状态变为 granted、读取按钮点亮",
            poll(page, "() => document.querySelector('[data-testid=\"sms-content\"]').getAttribute('data-permission') === 'granted' && !document.querySelector('[data-testid=\"sms-read\"]').disabled"),
        )
        st = sms_state(page)
        run.check(
            "授予后：权限行 data-on=true、提示「…把其中的支付短信加入待确认账单」、范围脚注「读到的支付短信会加入「待确认账单」，已经在待确认列表里的不会重复加入」",
            st["permissionOn"] == "true"
            and st["idle"] == "选好开始日期，点「读取短信」，把其中的支付短信加入待确认账单"
            and "读到的支付短信会加入「待确认账单」，已经在待确认列表里的不会重复加入" in st["footer"],
            str(st),
        )
        wait_toasts_gone(page)

        # ---- 改开始日期：读取时按所选日期的零点传给短信服务 ----
        target = (now - timedelta(days=90)).date()
        tid(page, "sms-date").fill(target.isoformat())
        wait_attr(page, '[data-testid="sms-date-row"]', "data-value", target.isoformat())
        run.check("改开始日期 → 行显示新日期", sms_state(page)["dateText"] == f"{target.year}年{target.month}月{target.day}日", sms_state(page)["dateText"])
        run.check("点「读取短信」→ 读取结束（第 1 次）", read_sms(page, 1))
        state = capture_state()
        run.check("读取时把开始日期（当天零点）交给短信服务", state["smsReads"] == 1 and (state["lastSmsReadFrom"] or "").startswith(f"{target.isoformat()}T00:00:00"), str(state))

        # ---- 读取结果：识别出金额的进待确认账单、没识别出金额的跳过 ----
        st = sms_state(page)
        run.check("读完后内容状态 results", st["state"] == "results" and st["count"] == "4", f"{st['state']} {st['count']}")
        run.check(
            "读取结果：小标题「识别到 4 条支付短信」、4 张卡片（按时间由新到旧）",
            st["header"] == "识别到 4 条支付短信" and [r["id"] for r in st["results"]] == ["9001", "9002", "9003", "9004"],
            f"{st['header']} {[r['id'] for r in st['results']]}",
        )
        sm = st["summary"] or {}
        run.check(
            "摘要分组「读取结果」：新加入 3、没识别出金额 1；已在列表里 / 列表已满 / 加入失败三行不出现",
            sm.get("title") == "读取结果" and sm.get("added") == "3" and sm.get("noAmount") == "1" and sm.get("duplicate") == "0" and sm.get("overCapacity") == "0" and sm.get("failed") == "0"
            and sm.get("rows") == {"added": "3 条", "duplicate": None, "noAmount": "1 条", "overCapacity": None, "failed": None},
            str(sm),
        )
        run.check(
            "摘要脚注「到「待确认账单」确认入账，账本在那里选」；「新加入待确认账单」一行是按钮、点击区域 ≥ 44",
            sm.get("footer") == "到「待确认账单」确认入账，账本在那里选" and sm.get("addedIsButton") is True and (sm.get("addedHeight") or 0) >= 44,
            str(sm),
        )
        run.check("提示「已加入 3 条待确认账单」", wait_toast(page, "已加入 3 条待确认账单"))
        expected_outcomes = {
            "9001": ("added", "已加入待确认", "ft-badge--success"),
            "9002": ("added", "已加入待确认", "ft-badge--success"),
            "9003": ("added", "已加入待确认", "ft-badge--success"),
            "9004": ("no-amount", "没识别出金额", "ft-badge--warning"),
        }
        run.check(
            "每张卡片右下角的去向标签：三条「已加入待确认」（成功色）、金额没识别出来的是「没识别出金额」（警告色）",
            all((r["outcome"], r["outcomeText"], r["badgeTone"]) == expected_outcomes[r["id"]] for r in st["results"]),
            str([(r["id"], r["outcome"], r["outcomeText"], r["badgeTone"]) for r in st["results"]]),
        )
        if st["results"]:
            first = st["results"][0]
            when_text = f"{messages[0]['date']:%Y-%m-%d %H:%M}"
            run.check(
                "结果卡片：正文摘要、「号码 · 时间」",
                first["body"] == note_summary(messages[0]["body"], 120) and first["meta"] == f"95588 · {when_text}",
                str(first),
            )
        run.check("去向标签都在卡片里面、没有横向溢出", all(r["badgeInside"] for r in st["results"]) and st["overflowX"] <= 0, str(st["overflowX"]))
        run.check("页面上没有账本行 / 账本菜单 / 「创建账单」按钮 / 手动补全 Sheet / 「处理完了」状态", st["legacyControls"] == 0, str(st["legacyControls"]))
        end = page.evaluate(SCROLL_TO_END_JS, "sms-page")
        run.check("滚到底后最后一张结果卡片不被底部固定按钮挡住", end["lastBottom"] is not None and end["lastBottom"] <= end["footTop"] + 1, str(end))
        page.evaluate("() => { const el = document.querySelector('[data-testid=\"sms-summary\"]'); if (el) el.scrollIntoView({ block: 'center' }); }")
        wait_animations_settled(page)
        shot(page, "sms-results")

        # ---- 库里：三条待确认账单，字段按短信来；没有创建正式账单 ----
        rows = pending_by_note(db)
        run.check(
            "待确认账单里多了 3 条（没识别出金额的那条没有加入），数据来源都是短信识别",
            len(rows) == 3 and messages[3]["body"] not in rows and all(r["Source"] == SOURCE_SMS for r in rows.values()),
            str(list(rows)),
        )
        run.check(
            "9001：8.5、支出、渠道微信支付、备注是短信原文、交易时间与捕获时间都是短信时间",
            pending_matches(rows.get(messages[0]["body"]), amount=8.5, bill_type=BILL_TYPE_EXPENSE, channel="微信支付", note=messages[0]["body"], when=messages[0]["date"]),
            str(rows.get(messages[0]["body"])),
        )
        run.check(
            "9002：35、支出、渠道银行卡",
            pending_matches(rows.get(messages[1]["body"]), amount=35, bill_type=BILL_TYPE_EXPENSE, channel="银行卡", note=messages[1]["body"], when=messages[1]["date"]),
            str(rows.get(messages[1]["body"])),
        )
        run.check(
            "9003：收入（含千分位）12345.5、收入类型、渠道银行卡",
            pending_matches(rows.get(messages[2]["body"]), amount=12345.5, bill_type=BILL_TYPE_INCOME, channel="银行卡", note=messages[2]["body"], when=messages[2]["date"]),
            str(rows.get(messages[2]["body"])),
        )
        run.check("没有创建正式账单（读取短信只进待确认账单，确认入账在「待确认账单」页）", db.bills_of_user(user_id) == [], str(db.bills_of_user(user_id)))
        wait_toasts_gone(page)

        # ---- 摘要里「新加入待确认账单」→ 待确认账单页，三张卡片就是刚加入的短信；返回后结果还在 ----
        tid(page, "sms-summary-added").click()
        wait_url(page, r"^/pending-bills$")
        tid(page, "pending-page").wait_for(state="visible")
        wait_attr(page, CONTENT, "data-state", "ready")
        wait_animations_settled(page)
        shown = cards(page)
        run.check(
            "点摘要里的「新加入待确认账单」→ 进入待确认账单页（页面栈推入）：3 张卡片，来源行「渠道 · 短信识别」、数据来源 sms",
            page.evaluate("() => location.pathname") == "/pending-bills"
            and [c["sourceLine"] for c in shown] == ["微信支付 · 短信识别", "银行卡 · 短信识别", "银行卡 · 短信识别"]
            and all(c["source"] == "sms" for c in shown),
            str([(c["sourceLine"], c["source"]) for c in shown]),
        )
        # 页面栈里短信识别页还在底下，两页各有一个返回键：点最上面（后推入的）那个
        tid(page, "nav-back").last.click()
        run.check(
            "返回短信识别页：待确认账单页退出，读取结果还在",
            gone(tid(page, "pending-page"), 6000) and visible(tid(page, "sms-page"), 3000) and sms_state(page)["state"] == "results" and sms_state(page)["count"] == "4",
            str(sms_state(page)["state"]),
        )

        # ---- 再读一次同一批：重复的不再加入 ----
        run.check("再点「读取短信」→ 读取结束（第 2 次）", read_sms(page, 2))
        st = sms_state(page)
        sm = st["summary"] or {}
        run.check(
            "重复读取：三条短信都是「已在待确认列表」（中性色），没有新加入；摘要里没有「新加入」行",
            outcome_map(st) == {"9001": "duplicate", "9002": "duplicate", "9003": "duplicate", "9004": "no-amount"}
            and all(r["outcomeText"] == "已在待确认列表" and r["badgeTone"] == "ft-badge--neutral" for r in st["results"] if r["outcome"] == "duplicate")
            and sm.get("added") == "0" and sm.get("duplicate") == "3" and sm.get("rows", {}).get("added") is None and sm.get("rows", {}).get("duplicate") == "3 条",
            f"{outcome_map(st)} {sm}",
        )
        run.check("摘要脚注：没有新加入也没有异常时不写脚注", sm.get("footer") is None, str(sm.get("footer")))
        run.check("提示「这些短信都已经在待确认列表里了」", wait_toast(page, "这些短信都已经在待确认列表里了"))
        run.check("待确认账单仍是 3 条（没有重复项）", len(db.pending_bills()) == 3, str(len(db.pending_bills())))
        wait_toasts_gone(page)

        # ---- 一批里既有重复的、也有新的 ----
        new_message = {"id": 9005, "address": "95588", "body": "【微信支付】您向便利店付款6.00元", "date": ago(0)}
        set_capture(messages=messages + [new_message])
        run.check("再读一次（第 3 次，多了一条新短信）", read_sms(page, 3))
        st = sms_state(page)
        sm = st["summary"] or {}
        run.check(
            "新的一条加入、旧的三条仍是重复：小标题「识别到 5 条支付短信」、卡片按时间由新到旧",
            outcome_map(st) == {"9005": "added", "9001": "duplicate", "9002": "duplicate", "9003": "duplicate", "9004": "no-amount"}
            and st["header"] == "识别到 5 条支付短信" and [r["id"] for r in st["results"]] == ["9005", "9001", "9002", "9003", "9004"],
            f"{st['header']} {outcome_map(st)}",
        )
        run.check("摘要：新加入 1、已在列表里 3、没识别出金额 1", sm.get("rows") == {"added": "1 条", "duplicate": "3 条", "noAmount": "1 条", "overCapacity": None, "failed": None}, str(sm.get("rows")))
        run.check("提示「已加入 1 条待确认账单」", wait_toast(page, "已加入 1 条待确认账单"))
        rows = pending_by_note(db)
        run.check(
            "待确认账单 4 条；新加入的 9005：6、支出、微信支付",
            len(rows) == 4 and pending_matches(rows.get(new_message["body"]), amount=6, bill_type=BILL_TYPE_EXPENSE, channel="微信支付", note=new_message["body"], when=new_message["date"]),
            str(list(rows)),
        )
        wait_toasts_gone(page)

        # ---- 实时监听已经捕获过的同一笔（来源同为短信识别、时间差不到一分钟）：读取时不再重复加入 ----
        live_when = ago(5)
        live_body = "【微信支付】您向书店付款7.00元"
        # 应用按金额文本去重：seed_pending_bill 已按应用写入的写法（7 → "7.0"）存金额，才会与读取时解析出的 7.00 对上
        db.seed_pending_bill(amount=7, channel="微信支付", note=live_body, when=live_when + timedelta(seconds=20), source=SOURCE_SMS)
        set_capture(messages=[{"id": 9401, "address": "95588", "body": live_body, "date": live_when}])
        run.check("读取实时监听刚捕获过的同一笔（第 4 次）", read_sms(page, 4))
        st = sms_state(page)
        run.check(
            "实时捕获过的同一笔：「已在待确认列表」，不重复加入（待确认账单仍是 5 条）",
            outcome_map(st) == {"9401": "duplicate"} and len(db.pending_bills()) == 5,
            f"{outcome_map(st)} {len(db.pending_bills())}",
        )
        wait_toasts_gone(page)

        # ---- 没有结果 ----
        set_capture(messages=[])
        run.check("读取空结果（第 5 次）", read_sms(page, 5))
        run.check(
            "读到的短信是空的：提示「未找到支付类短信」、空状态、没有摘要",
            wait_toast(page, "未找到支付类短信") and sms_state(page)["state"] == "empty" and tid(page, "sms-empty").is_visible() and tid(page, "sms-summary").count() == 0,
        )
        run.check_clean(s.errors, "J 节会话（读取、去重）无页面异常 / 控制台错误 / 失败请求")

        # ---- 容量：待确认账单最多 200 条，批量读取按剩余容量由新到旧加入，不顶掉已有的 ----
        wait_toasts_gone(page)
        db.delete_pending_bills()
        for i in range(199):
            db.seed_pending_bill(amount=1000 + i, channel="微信支付", note=f"已有的待确认{i}", when=minute(now) - timedelta(days=100, minutes=10 * i), source=SOURCE_NOTIFICATION)
        full_messages = [
            {"id": 9301, "address": "95588", "body": "【微信支付】您向面包店付款11.00元", "date": ago(1)},
            {"id": 9302, "address": "95588", "body": "【微信支付】您向水果店付款22.00元", "date": ago(2)},
            {"id": 9303, "address": "95588", "body": "【微信支付】您向药店付款33.00元", "date": ago(3)},
        ]
        set_capture(messages=full_messages)
        run.check("待确认账单还剩 1 个位置时读取 3 条（第 6 次）", read_sms(page, 6))
        st = sms_state(page)
        sm = st["summary"] or {}
        run.check(
            "只加入最近的一条（9301），较早的两条是「列表已满，未加入」（警告色）",
            outcome_map(st) == {"9301": "added", "9302": "over-capacity", "9303": "over-capacity"}
            and all(r["outcomeText"] == "列表已满，未加入" and r["badgeTone"] == "ft-badge--warning" for r in st["results"] if r["outcome"] == "over-capacity"),
            str(outcome_map(st)),
        )
        run.check(
            "摘要：新加入 1、列表已满未加入 2；脚注说明上限与怎么办",
            sm.get("rows") == {"added": "1 条", "duplicate": None, "noAmount": None, "overCapacity": "2 条", "failed": None}
            and sm.get("footer") == "待确认账单最多保留 200 条：较早的付款没有加入，处理掉一些待确认账单后重新读取即可",
            str(sm),
        )
        run.check("提示「已加入 1 条待确认账单，2 条因待确认账单已满没加入」", wait_toast(page, "已加入 1 条待确认账单，2 条因待确认账单已满没加入"))
        rows = pending_by_note(db)
        existing_notes = {f"已有的待确认{i}" for i in range(199)}
        run.check(
            "库里正好 200 条：原来的 199 条一条没丢，只多了 9301；9302 / 9303 没有加入",
            len(rows) == 200 and existing_notes <= set(rows) and full_messages[0]["body"] in rows and full_messages[1]["body"] not in rows and full_messages[2]["body"] not in rows,
            f"{len(rows)}",
        )
        wait_toasts_gone(page)

        run.check("列表已满后再读一次（第 7 次）", read_sms(page, 7))
        st = sms_state(page)
        sm = st["summary"] or {}
        run.check(
            "已满：9301 已在列表里（重复），9302 / 9303 仍是列表已满；一条都没加入，提示「待确认账单已满，没有加入新的短信」",
            outcome_map(st) == {"9301": "duplicate", "9302": "over-capacity", "9303": "over-capacity"} and sm.get("added") == "0" and wait_toast(page, "待确认账单已满，没有加入新的短信"),
            f"{outcome_map(st)} {sm}",
        )
        run.check("待确认账单仍是 200 条", len(db.pending_bills()) == 200, str(len(db.pending_bills())))
        run.check("整个 J 节没有创建过正式账单", db.bills_of_user(user_id) == [], str(db.bills_of_user(user_id)))
        run.check_clean(s.errors, "J 节会话（容量）无页面异常 / 控制台错误 / 失败请求")
    db.delete_pending_bills()

    # ---- 没有账本的用户：读取短信不要求账本（账本在待确认账单里确认入账时才选） ----
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        set_capture(reset=True, sms=True, messages=[{"id": 9201, "address": "95588", "body": "【微信支付】您向便利店付款9.00元", "date": ago(1)}])
        open_sms(page)
        run.check("没有账本的用户：页面上没有账本行 / 账本菜单（读取不要求账本）", sms_state(page)["legacyControls"] == 0)
        run.check("点「读取短信」→ 读取结束", read_sms(page, 1))
        run.check("提示「已加入 1 条待确认账单」，不会提示先选账本", wait_toast(page, "已加入 1 条待确认账单") and toast(page, "账本").count() == 0)
        rows = db.pending_bills()
        run.check(
            "没有账本照样加入待确认账单；没有创建账单，也没有凭空多出账本",
            len(rows) == 1 and rows[0]["Source"] == SOURCE_SMS and db.bills_of_user(user_id) == [] and db.ledgers_of_user(user_id) == [],
            str(rows),
        )
        run.check_clean(s.errors, "J 节会话（没有账本）无页面异常 / 控制台错误 / 失败请求")

    set_capture(reset=True)
    db.delete_pending_bills()


# ---------------------------------------------------------------------------------------------- main


def main() -> int:
    require_server()
    run = TestRun("pending-bills-page")
    db = LocalDb()
    db.verify_schema()
    set_capture(reset=True)
    db.delete_pending_bills()
    try:
        run_all_sections(run, db)
    finally:
        # 脚本中途抛异常也要把整个进程共享的「自动识别」假状态、待确认账单还原，免得带偏后面的脚本
        # （例如「我的」页断言「短信识别 · 未开启」，上一个脚本崩在授予权限之后就会误报）
        set_capture(reset=True)
        db.delete_pending_bills()
    return run.finish()


def run_all_sections(run: TestRun, db: LocalDb) -> None:
    now = datetime.now()

    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)

        check_empty_and_capture(run, page, db)
        d = Seed(db, user_id, now)
        check_cards(run, page, d)
        check_one_tap_confirm(run, page, d)
        check_confirm_sheet(run, page, d)
        check_channel_not_matched(run, page, d)
        check_ignore(run, page, d)
        check_no_duplicate(run, page, d)

        run.check_clean(s.errors)

    run_ledger_required_chip(run)
    run_ledger_required_sheet(run)
    run_narrow(run)
    run_sms(run)


if __name__ == "__main__":
    sys.exit(main())
