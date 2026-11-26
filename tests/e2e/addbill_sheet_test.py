# -*- coding: utf-8 -*-
"""
E2E 测试：记一笔 Sheet（iOS UI 重构 · 15 号 ticket，手机视口）。

前置条件：
  1. pip install -r tests/e2e/requirements.txt && python -m playwright install chromium
  2. FinanceTracker.Web.Server 已用 http 启动配置运行：powershell tests/e2e/server.ps1 -Action start
     （共享账本一节要用假云端，端口 E2E_CLOUD_PORT，默认 5271；端口被占用时该节跳过，其余照常）

这是第一次能稳定地经界面完整保存一笔账单的用例：自定义数字键盘 + 分类网格 + 账本 / 支付渠道弹出菜单，都是普通按钮，
不再受「MASA 下拉在无头浏览器里选不了」的限制。验证点：

  A. 呈现与结构：Tab 栏「记一笔」圆钮升起全高 Sheet（地址不变），背景页面缩小，Tab 栏被遮罩盖住；标题 / 类型分段 / 金额大字 /
     分类网格（支出 10 项 + 新建分类，两页）/ 四行设置 / 数字键盘与高大的保存键；新用户没有账本 → 保存置灰
  B. 数字键盘规则：第三位小数被忽略、多个小数点被忽略、不出现前导零、00 键、退格、上限 99,999,999.99（含字号收缩、不溢出）、
     长按退格清空；金额不是输入框（不唤起系统键盘）
  C. 缺账本的引导：点按置灰的保存键 → 账本行抖动 + 弹出账本菜单（只有「新建账本…」）；Esc / 点外面收起且不误触下面的行；
     内联新建账本后自动选中，保存键点亮
  D. 分类网格：左右翻页与分页点；内联新建分类后自动选中并翻到它所在的页
  E. 有输入时关闭需要二次确认（✕ / 点遮罩）：继续编辑保留输入，放弃后什么也没保存；再次打开是全新的一次记账
  F. 保存：金额 / 分类 / 账本 / 支付渠道 / 备注 / 时间落库一致，数据来源为手动录入、同步状态待同步；Sheet 收起、Toast「已保存到〈账本〉」，
     背景页面复位；首页、账单 Tab 随之刷新；超支提醒沿用
  G. 收入：切换后分类网格（5 项 + 新建分类）、金额与分段文字颜色同步；保存为收入账单
  H. 时间与备注：时间按应用时区显示 / 输入（昨天 HH:mm）、落库为 UTC；聚焦备注时自定义键盘让位、回车后恢复
  I. 基座与入口：create_bill(page=…) 经新界面保存；旧地址 /add-bill 302 到 /?add-bill=1 并升起 Sheet；语音入口存在（无头环境降级为置灰 + 说明）
  J. 共享账本：账本菜单 = 自己的账本 + 有写权限的共享账本（带「共享」徽标），只读成员的共享账本不出现；可记入共享账本
  K. 减弱动态效果：背景页面不缩放、金额光标不闪
  L. 缺项引导：金额为空 / 为 0 时保存键置灰，点按提示「请输入金额」并抖动金额区（不弹账本菜单）；敲了金额后点亮
  M. 来源页状态保留：Sheet 是覆盖层不是路由页——从账单 Tab 升起、关闭、保存之后，分段、滚动位置、大标题折叠、地址都原样
  N. 用户默认值：默认账本 / 支出分类 / 收入分类 / 支付渠道打开时预选，切类型时换成对应类型的默认；默认账本不可用时退回第一个自己的账本
  O. 玻璃层预算：四个 Tab 上打开 Sheet，屏幕上只剩标题栏的两个玻璃圆钮（≤ 3 层），收起后页面与 Tab 栏的毛玻璃恢复

只覆盖网页层——Android / iOS 上的真机手感（Sheet 升起与背景缩放、系统键盘与自定义键盘的切换、麦克风按住说话）见 ticket 15 的人工验收清单。
"""
import sys
from datetime import datetime, timedelta, timezone

from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

from e2e_base import (
    ACTIVE_ROOT_SELECTOR,
    BASE_URL,
    BILL_TYPE_EXPENSE,
    BILL_TYPE_INCOME,
    PHONE_VIEWPORT,
    ROLE_EDITOR,
    ROLE_VIEWER,
    SOURCE_MANUAL,
    SYNC_PENDING,
    LocalDb,
    TestRun,
    active_root,
    active_tab,
    addbill_sheet,
    amount_text,
    close_add_bill_sheet,
    create_bill,
    login,
    long_press,
    open_add_bill_sheet,
    open_app,
    open_tab,
    pick_category,
    press_key,
    press_keys,
    pull_refresh,
    require_server,
    save_bill_via_sheet,
    scroll_root_to,
    session,
    shot,
    swipe,
    tab_bar,
    to_db_time,
    unique_name,
    wait_amount_text,
    wait_animations_settled,
    wait_attr,
    wait_scroll_root_stable,
)
from fake_cloud import CloudPortBusy, CloudWorld, FakeCloud

SHEET = '[data-testid="addbill-sheet"]'
VIEW_H = PHONE_VIEWPORT["height"]
VIEW_W = PHONE_VIEWPORT["width"]

# 主题色（tokens.css）：支出红 / 收入绿（文字用加深的色）
RGB_EXPENSE_TEXT = "rgb(211, 34, 26)"
RGB_INCOME_TEXT = "rgb(24, 133, 64)"

TOAST_JS = "(t) => [...document.querySelectorAll('.m-enqueued-snackbars .m-snack__wrapper')].some((e) => e.textContent.includes(t))"

STACK_JS = """() => {
  const el = document.querySelector('.m-page-stack');
  const cs = getComputedStyle(el);
  return { transform: cs.transform, radius: cs.borderTopLeftRadius };
}"""

COVERED_JS = """() => {
  const b = document.querySelector('[data-testid="tab-bar-add"]').getBoundingClientRect();
  const hit = document.elementFromPoint(b.x + b.width / 2, b.y + b.height / 2);
  return !!hit && !hit.closest('[data-testid="tab-bar"]');
}"""

# 在点按之前装好观察器：抖动只持续 460ms，轮询很可能错过，所以记录「曾经出现过」。
# window.__shakeSeen 是抖动过的元素清单（有 data-testid 记 testid，否则记 class）：金额区 = amount-display、
# 分类网格外层 = ft-addbill__cats、账本行外层 = ft-addbill__anchor
SHAKE_OBSERVER_JS = """() => {
  window.__shakeSeen = [];
  const note = () => document.querySelectorAll('[data-shake="true"]').forEach((el) => {
    const who = el.getAttribute('data-testid') || String(el.className);
    if (!window.__shakeSeen.includes(who)) window.__shakeSeen.push(who);
  });
  new MutationObserver(note).observe(document.body, { subtree: true, attributes: true, attributeFilter: ['data-shake'], childList: true });
}"""

SHAKEN_JS = "(who) => (window.__shakeSeen || []).some((w) => w.includes(who))"

DOT_COLORS_JS = """() => [...document.querySelectorAll('[data-testid="category-dots"] > span')].map((d) => getComputedStyle(d).backgroundColor)"""


# ---------------------------------------------------------------------------------------------- 小工具


def poll(page, expression: str, arg=None, timeout: int = 5000) -> bool:
    """在页面里反复求值直到为真；超时返回 False（让断言记失败，而不是抛异常打断后面的用例）。"""
    try:
        page.wait_for_function(expression, arg=arg, timeout=timeout)
        return True
    except PlaywrightTimeoutError:
        return False


def bill_row_text(page, bill_id: str, parts: "list[str]", timeout: int = 8000) -> str:
    """账单 Tab 里这笔账单那一行的文字：反复读，直到包含全部期望片段（超时返回最后读到的文字，让断言带着它失败）。

    账单 Tab 在数据到达后会把列表重新渲染一次（行节点被整个换掉）；「行可见」之后紧接着单次读文字，
    可能正好撞上换节点的瞬间而读到空串（全量回归里出现过一次），所以不能单次读取。"""
    poll(
        page,
        """([rootSelector, billId, parts]) => {
            const root = document.querySelector(rootSelector);
            const row = root && root.querySelector(`[data-testid="bill-row"][data-bill-id="${billId}"]`);
            return !!row && parts.every((part) => row.innerText.includes(part));
        }""",
        arg=[ACTIVE_ROOT_SELECTOR, bill_id, parts],
        timeout=timeout,
    )
    row = active_root(page).locator(f'[data-testid="bill-row"][data-bill-id="{bill_id}"]')
    return row.first.inner_text() if row.count() else ""


def toast(page, text: str, timeout: int = 8000) -> bool:
    return poll(page, TOAST_JS, arg=text, timeout=timeout)


def wait_no_toasts(page, timeout: int = 20000) -> bool:
    """等屏幕上的 Toast 都消失（它们是短暂的玻璃胶囊，会混进「屏幕上有几层玻璃」的统计，也会挡住后面要点的东西）。"""
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


def tid(page, testid: str):
    return page.get_by_test_id(testid)


def row_value(page, testid: str) -> str:
    return tid(page, testid).locator(".ft-cell__value").inner_text().strip()


def box(locator) -> dict:
    return locator.first.bounding_box() or {"x": 0, "y": 0, "width": 0, "height": 0}


def selected_category(page) -> str:
    tile = page.locator('[data-testid="category-tile"][data-selected="true"]')
    return tile.first.get_attribute("data-name") if tile.count() else ""


def first_category(page) -> str:
    """网格里排第一的分类名——没有用户默认分类时，新开一笔默认选中的就是它。"""
    tile = page.locator('[data-testid="category-tile"]')
    return tile.first.get_attribute("data-name") if tile.count() else ""


def amount_equals(value, expected: float) -> bool:
    """库里的金额是 REAL，读出来是 '12000.0' 这样的文本，按数值比。"""
    try:
        return abs(float(value) - expected) < 1e-9
    except (TypeError, ValueError):
        return False


def clear_amount(page) -> None:
    """长按退格清空金额。"""
    long_press(page, tid(page, "key-backspace"), hold_ms=700)
    wait_amount_text(page, "")


def home_expense(page) -> str:
    return active_root(page).get_by_test_id("home-expense").get_attribute("data-amount")


def utc_text_to_dt(text: str) -> datetime:
    return datetime.strptime(text.split(".")[0], "%Y-%m-%d %H:%M:%S").replace(tzinfo=timezone.utc)


# ---------------------------------------------------------------------------------------------- A. 呈现与结构


def check_structure(run: TestRun, page, db: LocalDb, user_id: str) -> None:
    run.check("登录后停在首页", active_tab(page) == "home" and page.evaluate("() => location.pathname") == "/")
    sheet = open_add_bill_sheet(page)
    run.check("点按「记一笔」圆钮升起 Sheet：地址不变，仍停在首页", page.evaluate("() => location.pathname") == "/" and active_tab(page) == "home")

    sheet_box = box(sheet)
    run.check(
        "全高 Sheet：贴底、宽度铺满，顶部留出 12 的缝（露出缩小的背景页面）",
        abs(sheet_box["y"] - 12) <= 1 and abs(sheet_box["y"] + sheet_box["height"] - VIEW_H) <= 1 and abs(sheet_box["width"] - VIEW_W) <= 1,
        str(sheet_box),
    )
    stack = page.evaluate(STACK_JS)
    run.check("背景页面轻微缩小（0.94）并带圆角（28）", stack["transform"] == "matrix(0.94, 0, 0, 0.94, 0, 0)" and stack["radius"] == "28px", str(stack))
    run.check("Sheet 升起时 Tab 栏被遮罩盖住，「记一笔」圆钮点不到", page.evaluate(COVERED_JS))

    run.check("标题「记一笔」", tid(page, "sheet-title").inner_text().strip() == "记一笔")
    run.check("标题栏左关闭、右语音入口", sheet.get_by_test_id("sheet-close").is_visible() and tid(page, "addbill-voice").is_visible())

    segments = tid(page, "addbill-type").get_by_test_id("segment")
    run.check(
        "类型分段「支出 | 收入」，默认选中支出",
        segments.all_inner_texts() == ["支出", "收入"] and segments.nth(0).get_attribute("aria-pressed") == "true" and segments.nth(1).get_attribute("aria-pressed") == "false",
    )

    amount = tid(page, "amount-display")
    run.check(
        "金额大字：还没敲时是浅色的 0，带 ¥ 前缀；它是展示区而不是输入框（不唤起系统键盘）",
        amount.get_attribute("data-empty") == "true" and amount_text(page) == "" and "¥" in amount.inner_text() and amount.evaluate("el => el.tagName") == "DIV",
    )
    run.check("没有任何输入控件获得焦点（金额靠自定义键盘）", page.evaluate("() => !['INPUT', 'TEXTAREA'].includes(document.activeElement && document.activeElement.tagName)"))
    run.check("金额颜色：支出红", amount.evaluate("el => getComputedStyle(el).color") == RGB_EXPENSE_TEXT)

    presets = db.preset_category_names()
    tiles = page.locator('[data-testid="category-tile"]')
    run.check("分类网格：支出 10 个预设分类 + 末尾「新建分类」", tiles.count() == 10 and tid(page, "category-new").count() == 1 and len(presets) >= 10, f"{tiles.count()} / {presets}")
    run.check("分类网格 2 页、2 个分页点", tid(page, "category-pager").get_attribute("data-pages") == "2" and page.locator('[data-testid="category-dots"] > span').count() == 2)
    run.check("默认选中第一个分类", selected_category(page) == presets[0], f"{selected_category(page)} / {presets[:2]}")
    run.check("分类网格是单选组（radiogroup + radio）", tid(page, "category-pager").get_attribute("role") == "radiogroup" and tiles.first.get_attribute("role") == "radio")
    run.check("第一页分类图标圆在视口内，5 列", len({round(t["x"]) for t in [box(tiles.nth(i)) for i in range(10)]}) == 5)

    run.check("账本行：新用户没有账本 → 「请选择账本」并标记缺失", row_value(page, "ledger-row") == "请选择账本" and tid(page, "ledger-row").get_attribute("data-missing") == "true")
    run.check("账本行是带菜单的按钮（aria-haspopup=menu）", tid(page, "ledger-row").get_attribute("aria-haspopup") == "menu" and tid(page, "ledger-row").evaluate("el => el.tagName") == "BUTTON")
    run.check("支付渠道行：默认第一个预设渠道", row_value(page, "channel-row") == db.preset_channel_names()[0], row_value(page, "channel-row"))
    run.check("时间行：默认当前时间「今天 HH:mm」", row_value(page, "time-row").startswith("今天 "), row_value(page, "time-row"))
    run.check("备注行：输入框，占位「添加备注」，系统键盘字号 16（不触发 iOS 聚焦放大）", tid(page, "addbill-note").get_attribute("placeholder") == "添加备注" and tid(page, "addbill-note").evaluate("el => getComputedStyle(el).fontSize") == "16px")

    keys = [tid(page, f"key-{k}") for k in ("1", "2", "3", "4", "5", "6", "7", "8", "9", "dot", "0", "00", "backspace")]
    run.check("数字键盘：0–9、小数点、00、退格都在", all(visible(k, 2000) for k in keys))
    save = tid(page, "addbill-save")
    save_box, key_box = box(save), box(tid(page, "key-5"))
    run.check(
        "「保存」键位于键盘右侧，跨三行（比普通键高得多）",
        save_box["x"] > key_box["x"] + key_box["width"] and save_box["height"] >= key_box["height"] * 2.9,
        f"{save_box} / {key_box}",
    )
    run.check("每个键高度 ≥ 44（点击热区）", key_box["height"] >= 44 and box(tid(page, "key-backspace"))["height"] >= 44, str(key_box))
    # 注意：Playwright 的 is_disabled() 会把 aria-disabled 也算进去，这里要核对的是「没有原生 disabled 属性」
    run.check("没选账本：保存键置灰（aria-disabled），但没有被禁用（仍可点按引导）", save.get_attribute("aria-disabled") == "true" and save.get_attribute("disabled") is None)
    run.check("整页没有横向溢出", page.evaluate("() => document.documentElement.scrollWidth - document.documentElement.clientWidth") <= 0)
    run.check("键盘下方 Sheet 内容区正好放得下（不需要滚动）", page.evaluate("() => { const b = document.querySelector('.ft-sheet__body'); return b.scrollHeight <= b.clientHeight + 1; }"))
    shot(page, "addbill-initial")


# ---------------------------------------------------------------------------------------------- B. 数字键盘规则


def check_keypad(run: TestRun, page) -> None:
    for key, expected in [("1", "1"), ("2", "12"), ("3", "123"), (".", "123."), ("5", "123.5"), ("0", "123.50")]:
        press_key(page, key)
        run.check(f"键盘：按 {key} → {expected}", wait_amount_text(page, expected), amount_text(page) or "")
    run.check("输入后金额显示区不再是「空」状态", tid(page, "amount-display").get_attribute("data-empty") == "false")

    # 被忽略的按键：按下之后再退一格，能看出前一次按键有没有被接受
    press_key(page, "7")
    press_key(page, "backspace")
    run.check("第三位小数被忽略（再退一格得 123.5）", wait_amount_text(page, "123.5"), amount_text(page) or "")
    press_key(page, "0")
    press_key(page, ".")
    press_key(page, "backspace")
    run.check("多个小数点被忽略（再退一格得 123.5）", wait_amount_text(page, "123.5"), amount_text(page) or "")

    clear_amount(page)
    run.check("长按退格清空金额，显示区回到「还没敲」", amount_text(page) == "" and tid(page, "amount-display").get_attribute("data-empty") == "true")

    press_key(page, "00")
    run.check("空金额按 00 → 0（不出现 00）", wait_amount_text(page, "0"), amount_text(page) or "")
    press_key(page, "5")
    run.check("0 之后按 5 → 5（没有前导零）", wait_amount_text(page, "5"), amount_text(page) or "")
    press_key(page, "00")
    run.check("5 之后按 00 → 500", wait_amount_text(page, "500"), amount_text(page) or "")
    clear_amount(page)
    press_key(page, ".")
    run.check("空金额按小数点 → 0.", wait_amount_text(page, "0."), amount_text(page) or "")
    press_key(page, "5")
    run.check("0. 之后按 5 → 0.5", wait_amount_text(page, "0.5"), amount_text(page) or "")
    for expected in ("0.", "0", ""):
        press_key(page, "backspace")
        run.check(f"退格一格 → {expected or '（空）'}", wait_amount_text(page, expected), amount_text(page) or "")

    # 上限 99,999,999.99
    press_keys(page, "99999999")
    run.check("整数部分 8 位", wait_amount_text(page, "99999999"), amount_text(page) or "")
    press_key(page, "9")
    press_key(page, "backspace")
    run.check("整数部分最多 8 位：第 9 位被忽略（再退一格得 9999999）", wait_amount_text(page, "9999999"), amount_text(page) or "")
    press_key(page, "9")
    press_keys(page, ".99")
    run.check("金额上限 99999999.99", wait_amount_text(page, "99999999.99"), amount_text(page) or "")
    press_key(page, "1")
    press_key(page, "backspace")
    run.check("到上限后再按数字被忽略（再退一格得 99999999.9）", wait_amount_text(page, "99999999.9"), amount_text(page) or "")
    press_key(page, "9")
    wait_amount_text(page, "99999999.99")
    amount = tid(page, "amount-display")
    run.check("上限金额显示带千分位 99,999,999.99", page.locator(".ft-addbill__num").inner_text() == "99,999,999.99", page.locator(".ft-addbill__num").inner_text())
    run.check("位数多了字号收缩一档（md），仍在一行内放得下", amount.get_attribute("data-size") == "md" and amount.evaluate("el => el.scrollWidth <= el.clientWidth + 1"))
    shot(page, "addbill-amount-max")

    clear_amount(page)
    press_keys(page, "123456")
    wait_amount_text(page, "123456")
    clear_amount(page)
    run.check("长按退格清空多位金额", amount_text(page) == "")
    run.check("按键没有让任何输入控件获得焦点（不唤起系统键盘）", page.evaluate("() => !['INPUT', 'TEXTAREA'].includes(document.activeElement && document.activeElement.tagName)"))


# ---------------------------------------------------------------------------------------------- C. 缺账本的引导 + 内联新建账本


def check_missing_ledger(run: TestRun, page, db: LocalDb, user_id: str) -> str:
    """返回新建的账本名。"""
    press_keys(page, "35")
    wait_amount_text(page, "35")
    save = tid(page, "addbill-save")

    page.evaluate(SHAKE_OBSERVER_JS)
    save.click(force=True)  # 置灰的保存键（aria-disabled）Playwright 视为不可点，force 跳过可操作性检查
    menu = tid(page, "ledger-menu")
    run.check("点按置灰的保存键 → 弹出账本菜单", visible(menu))
    run.check("账本行抖动（data-shake 出现过）", poll(page, SHAKEN_JS, arg="ft-addbill__anchor", timeout=3000))
    run.check("账本菜单里没有账本，只有「新建账本…」", tid(page, "ledger-option").count() == 0 and tid(page, "ledger-option-new").is_visible() and "新建账本" in tid(page, "ledger-option-new").inner_text())
    menu_box, row_box = box(menu), box(tid(page, "ledger-row"))
    run.check(
        "菜单贴着账本行弹出：在屏幕内，右缘与行对齐（留 12 边距）",
        menu_box["x"] >= 0 and menu_box["x"] + menu_box["width"] <= VIEW_W and abs((menu_box["x"] + menu_box["width"]) - (row_box["x"] + row_box["width"] - 12)) <= 20 and abs(menu_box["y"] - (row_box["y"] + row_box["height"])) <= 24,
        f"{menu_box} / {row_box}",
    )
    run.check("账本行 aria-expanded=true", tid(page, "ledger-row").get_attribute("aria-expanded") == "true")
    shot(page, "addbill-ledger-menu-empty")

    page.keyboard.press("Escape")
    run.check("Esc 收起账本菜单，Sheet 与金额保持不变", gone(menu) and visible(addbill_sheet(page), 2000) and amount_text(page) == "35")
    run.check("收起后 aria-expanded=false", tid(page, "ledger-row").get_attribute("aria-expanded") == "false")

    save.click(force=True)
    visible(menu)
    layer = tid(page, "ledger-menu-layer")
    layer.click(position={"x": 24, "y": 24})
    run.check(
        "点菜单外面只是收起，不会误触下面的行（支付渠道菜单没有弹出）",
        gone(menu) and tid(page, "channel-menu").count() == 0 and amount_text(page) == "35",
    )

    # 内联新建账本
    save.click(force=True)
    visible(menu)
    tid(page, "ledger-option-new").click()
    ledger_sheet = tid(page, "ledger-sheet")
    run.check("点「新建账本…」→ 复用的新建账本 Sheet 叠在记一笔 Sheet 之上", visible(ledger_sheet) and gone(menu) and addbill_sheet(page).is_visible())
    name = unique_name("记一笔账本")
    tid(page, "ledger-name").fill(name)
    tid(page, "ledger-save").click()
    run.check("保存账本后新建 Sheet 收起，记一笔 Sheet 保留", gone(ledger_sheet) and addbill_sheet(page).is_visible() and amount_text(page) == "35")
    ledgers = db.ledgers_of_user(user_id)
    run.check("账本落库（归当前用户）", [l["Name"] for l in ledgers] == [name], str(ledgers))
    run.check("新账本自动选中：账本行显示它、不再标记缺失", poll(page, "(n) => document.querySelector('[data-testid=\"ledger-row\"] .ft-cell__value')?.textContent.trim() === n", arg=name) and tid(page, "ledger-row").get_attribute("data-missing") == "false")
    run.check("账本行 data-value 是新账本 Id", ledgers and (tid(page, "ledger-row").get_attribute("data-value") or "").lower() == ledgers[0]["Id"].lower())
    run.check("账本选好后保存键点亮（没有 aria-disabled）", attr_is(page, '[data-testid="addbill-save"]', "aria-disabled", None))
    return name


# ---------------------------------------------------------------------------------------------- D. 分类网格


def check_category_grid(run: TestRun, page, db: LocalDb, user_id: str) -> str:
    """返回新建的分类名。"""
    pager = tid(page, "category-pager")
    run.check("初始在第一页", pager.get_attribute("data-page") == "0")
    first_page_dots = page.evaluate(DOT_COLORS_JS)
    run.check("分页点：当前页（第一个）的点比另一个深", len(first_page_dots) == 2 and first_page_dots[0] != first_page_dots[1], str(first_page_dots))

    y = box(tid(page, "category-grid"))["y"] + 60
    swipe(page, (VIEW_W - 50, y), (50, y))
    run.check("向左滑：翻到第二页（页码由脚本写在 data-page 上）", attr_is(page, '[data-testid="category-pager"]', "data-page", "1"))
    wait_animations_settled(page)
    new_box = box(tid(page, "category-new"))
    run.check("第二页是「新建分类」，已滑进视口", 0 <= new_box["x"] and new_box["x"] + new_box["width"] <= VIEW_W, str(new_box))
    run.check("分页点跟着换：第二个点变深、第一个变浅", page.evaluate(DOT_COLORS_JS) == [first_page_dots[1], first_page_dots[0]], str(page.evaluate(DOT_COLORS_JS)))
    swipe(page, (50, y), (VIEW_W - 50, y))
    run.check("向右滑：回到第一页", attr_is(page, '[data-testid="category-pager"]', "data-page", "0"))

    # 内联新建分类：「新建分类」在第二页，先翻过去再点
    swipe(page, (VIEW_W - 50, y), (50, y))
    attr_is(page, '[data-testid="category-pager"]', "data-page", "1")
    wait_animations_settled(page)
    tid(page, "category-new").click()
    run.check("点「新建分类」→ 打开共用的新建分类面板", visible(tid(page, "category-sheet")))
    name = unique_name("自建分类")
    tid(page, "category-name").fill(name)
    tid(page, "category-save").click()
    run.check("保存后面板收起，记一笔 Sheet 与金额保留", gone(tid(page, "category-sheet")) and addbill_sheet(page).is_visible() and amount_text(page) == "35")
    customs = db.custom_categories(user_id)
    run.check("新分类落库：支出类型、归当前用户", [(c["Name"], c["Type"]) for c in customs] == [(name, BILL_TYPE_EXPENSE)], str(customs))
    run.check("新分类自动选中", poll(page, "(n) => document.querySelector('[data-testid=\"category-tile\"][data-selected=\"true\"]')?.getAttribute('data-name') === n", arg=name), selected_category(page))
    run.check("网格 11 个分类（10 预设 + 1 自建），仍是 2 页", tid(page, "category-grid").get_attribute("data-count") == "11" and tid(page, "category-pager").get_attribute("data-pages") == "2")
    # 自建分类的排序数字默认 0，与预设按「排序数字」一起排（沿用原记账页 / 分类服务的顺序），所以排在最前、落在第一页；
    # 创建前用户在第二页（「新建分类」格子在那里），选中后网格要自己翻回新分类所在的页
    run.check("新分类按排序数字排在最前", first_category(page) == name)
    run.check("自动翻到新分类所在的页（第一页）", attr_is(page, '[data-testid="category-pager"]', "data-page", "0"))
    wait_animations_settled(page)
    selected_box = box(page.locator('[data-testid="category-tile"][data-selected="true"]'))
    run.check("选中的新分类在视口内", 0 <= selected_box["x"] and selected_box["x"] + selected_box["width"] <= VIEW_W, str(selected_box))
    shot(page, "addbill-category-created")
    return name


# ---------------------------------------------------------------------------------------------- E. 有输入时关闭需确认


def check_guarded_close(run: TestRun, page, db: LocalDb, user_id: str, ledger_name: str) -> None:
    close = addbill_sheet(page).get_by_test_id("sheet-close")
    close.click()
    alert = tid(page, "alert")
    run.check("有输入时点 ✕：先弹出放弃确认，Sheet 不关", visible(alert) and addbill_sheet(page).is_visible())
    run.check("确认文案：「放弃本次记账？」/ 继续编辑 / 放弃", tid(page, "alert-title").inner_text().strip() == "放弃本次记账？" and tid(page, "alert-cancel").inner_text().strip() == "继续编辑" and tid(page, "alert-confirm").inner_text().strip() == "放弃")
    run.check("Alert 盖在 Sheet 之上（能点到它的按钮）", page.evaluate("() => { const b = document.querySelector('[data-testid=\"alert-confirm\"]').getBoundingClientRect(); const hit = document.elementFromPoint(b.x + b.width / 2, b.y + b.height / 2); return !!hit && !!hit.closest('[data-testid=\"alert\"]'); }"))
    shot(page, "addbill-discard-alert")
    tid(page, "alert-cancel").click()
    run.check("「继续编辑」：Alert 收起，Sheet 与输入保留", gone(alert) and addbill_sheet(page).is_visible() and amount_text(page) == "35" and row_value(page, "ledger-row") == ledger_name)

    page.mouse.click(VIEW_W / 2, 5)  # Sheet 上沿之上的那条缝：遮罩
    run.check("点遮罩同样先确认", visible(alert) and addbill_sheet(page).is_visible())
    tid(page, "alert-cancel").click()
    gone(alert)

    close.click()
    visible(alert)
    tid(page, "alert-confirm").click()
    run.check("「放弃」：Sheet 收起", gone(addbill_sheet(page)))
    run.check("放弃后背景页面复位（不再缩小）", poll(page, "() => getComputedStyle(document.querySelector('.m-page-stack')).transform === 'none'"), str(page.evaluate(STACK_JS)))
    run.check("放弃后什么也没保存", db.bills_of_user(user_id) == [])
    run.check("关闭后 Tab 栏恢复可点", tab_bar(page).is_visible() and not page.evaluate(COVERED_JS))

    sheet = open_add_bill_sheet(page)
    run.check("再次打开是全新的一次记账：金额清空、类型回到支出", amount_text(page) == "" and sheet.get_attribute("data-type") == "expense")
    run.check("有账本后默认选中自己的账本、默认选中网格第一个分类", row_value(page, "ledger-row") == ledger_name and selected_category(page) == first_category(page) != "", f"{row_value(page, 'ledger-row')} / {selected_category(page)} / {first_category(page)}")

    close_add_bill_sheet(page)
    run.check("没有输入时点 ✕ 直接收起，不弹确认", tid(page, "alert").count() == 0)


# ---------------------------------------------------------------------------------------------- F. 保存


def check_save(run: TestRun, page, db: LocalDb, user_id: str, ledger_name: str) -> str:
    """返回保存的备注（用于后续核对）。"""
    ledger_id = db.ledger_id_by_name(user_id, ledger_name)
    channel_name = db.preset_channel_names()[1]  # 第二个预设渠道（默认选中的是第一个）
    category_name = "交通出行"
    note = unique_name("E2E记一笔")

    sheet = open_add_bill_sheet(page)
    press_keys(page, "35.5")
    pick_category(page, category_name)
    run.check("选分类：点中的分类被选中，其余取消", selected_category(page) == category_name and page.locator('[data-testid="category-tile"][data-selected="true"]').count() == 1)

    channel_row = tid(page, "channel-row")
    channel_row.click()
    menu = tid(page, "channel-menu")
    run.check("点支付渠道行 → 弹出支付渠道菜单", visible(menu))
    options = tid(page, "channel-option")
    run.check("菜单列出全部支付渠道，当前的带选中标记", options.count() == db.visible_channel_count(user_id) and options.first.get_attribute("data-selected") == "true", f"{options.count()} / {db.visible_channel_count(user_id)}")
    menu.get_by_test_id("channel-option").filter(has_text=channel_name).click()
    run.check("选支付渠道：菜单收起，行显示所选渠道", gone(menu) and row_value(page, "channel-row") == channel_name, row_value(page, "channel-row"))

    note_field = tid(page, "addbill-note")
    note_field.click()
    run.check("聚焦备注：自定义键盘让位（Sheet 标记 data-typing，键盘区隐藏）", attr_is(page, SHEET, "data-typing", "true") and not tid(page, "keypad").is_visible())
    note_field.fill(note)
    note_field.press("Enter")
    run.check("回车收起系统键盘，自定义键盘回来", attr_is(page, SHEET, "data-typing", None) and visible(tid(page, "keypad"), 3000))
    run.check("备注已写入（保存键点亮）", note_field.input_value() == note and attr_is(page, '[data-testid="addbill-save"]', "aria-disabled", None))
    shot(page, "addbill-filled")

    before = datetime.now(timezone.utc)
    tid(page, "addbill-save").click()
    run.check("保存后 Sheet 收起", gone(sheet, 8000))
    run.check(f"Toast「已保存到「{ledger_name}」」", toast(page, f"已保存到「{ledger_name}」"))
    run.check("背景页面复位（不再缩小）", poll(page, "() => getComputedStyle(document.querySelector('.m-page-stack')).transform === 'none'"))

    saved = db.bills_by_note(note)
    run.check("本机库里恰有一笔该备注的账单", len(saved) == 1, str(len(saved)))
    bill = saved[0] if saved else {}
    run.check("金额 35.5、类型支出", amount_equals(bill.get("Amount"), 35.5) and bill.get("Type") == BILL_TYPE_EXPENSE, str(bill.get("Amount")))
    run.check("分类 / 账本 / 支付渠道一致", bill.get("CategoryId") == db.category_id_by_name(category_name) and bill.get("LedgerId") == ledger_id and bill.get("PaymentChannelId") == db.channel_id_by_name(channel_name))
    run.check("数据来源为手动录入、同步状态待同步、未软删除", bill.get("Source") == SOURCE_MANUAL and bill.get("SyncStatus") == SYNC_PENDING and bill.get("IsDeleted") == 0)
    run.check("归当前用户", bill.get("UserId") == user_id)
    moment = utc_text_to_dt(bill["TransactionTime"]) if bill else before
    run.check("交易时间默认当前时间（库内为 UTC，精确到分钟）", abs((moment - before).total_seconds()) < 180, f"{bill.get('TransactionTime')} vs {before}")

    # 各页数据刷新
    run.check("首页本月支出随之刷新为 35.50", poll(page, "() => document.querySelector('[data-testid=\"home-expense\"]')?.getAttribute('data-amount') === '35.50'", timeout=8000), str(home_expense(page)))
    open_tab(page, "账单")
    row = active_root(page).locator(f'[data-testid="bill-row"][data-bill-id="{bill["Id"].lower()}"]')
    run.check("账单 Tab 出现这笔账单", visible(row, 8000))
    parts = ["35.50", category_name, ledger_name, note]
    text = bill_row_text(page, bill["Id"].lower(), parts)
    run.check("账单行的金额、分类、账本与所填一致", all(part in text for part in parts), text)
    open_tab(page, "首页")
    return note


def check_over_budget(run: TestRun, page, db: LocalDb, user_id: str, ledger_name: str) -> None:
    db.seed_budget(user_id, amount=100)
    save_note = unique_name("超支")
    open_add_bill_sheet(page)
    press_keys(page, "120")
    tid(page, "addbill-note").fill(save_note)
    tid(page, "addbill-note").press("Enter")
    wait_attr(page, SHEET, "data-typing", None)
    tid(page, "addbill-save").click()
    run.check("超出总预算：沿用「预算超支提醒」Toast", toast(page, "预算超支提醒", timeout=10000))
    run.check("超支的账单照常保存", len(db.bills_by_note(save_note)) == 1)
    page.wait_for_function("() => !document.querySelector('.m-enqueued-snackbars .m-snack__wrapper')", timeout=20000)

    # 月初边界：账单时间在本月 1 日 00:30（应用时区）。库内是 UTC，应用时区早于 UTC 时那一刻还在上个月——
    # 预算月份必须按用户输入的墙上时间取（本月），不能读保存后已被换成 UTC 的 bill.TransactionTime（会去查上个月的预算）
    first = datetime.now().replace(day=1, hour=0, minute=30, second=0, microsecond=0)
    if first > datetime.now():
        run.skip("月初边界的预算提醒", "现在还没到本月 1 日 00:30，那个时刻在未来，记不了")
        return

    boundary_note = unique_name("月初")
    sheet = open_add_bill_sheet(page)
    sheet.get_by_test_id("addbill-time").fill(first.strftime("%Y-%m-%dT%H:%M"))
    wait_attr(page, '[data-testid="time-row"]', "data-value", first.strftime("%Y-%m-%dT%H:%M"))
    press_keys(page, "5")
    tid(page, "addbill-note").fill(boundary_note)
    tid(page, "addbill-note").press("Enter")
    wait_attr(page, SHEET, "data-typing", None)
    tid(page, "addbill-save").click()
    run.check("本月 1 日 00:30 的支出：预算月份按应用时区的本月取，本月预算已超支 →「预算超支提醒」Toast", toast(page, "预算超支提醒", timeout=10000))
    boundary_bill = (db.bills_by_note(boundary_note) or [{}])[0]
    run.check("该账单照常保存，库内时间是换算后的 UTC", boundary_bill.get("TransactionTime") == to_db_time(first), f"{boundary_bill.get('TransactionTime')} vs {to_db_time(first)}")
    page.wait_for_function("() => !document.querySelector('.m-enqueued-snackbars .m-snack__wrapper')", timeout=20000)


# ---------------------------------------------------------------------------------------------- G. 收入


def check_income(run: TestRun, page, db: LocalDb, user_id: str, ledger_name: str) -> None:
    sheet = open_add_bill_sheet(page)
    income = tid(page, "addbill-type").get_by_test_id("segment").filter(has_text="收入")
    income.click()
    run.check("切到收入：Sheet 标记 data-type=income", attr_is(page, SHEET, "data-type", "income"))
    run.check("收入分类网格：5 个预设 + 「新建分类」，只有一页、没有分页点", poll(page, "() => document.querySelectorAll('[data-testid=\"category-tile\"]').length === 5") and tid(page, "category-new").count() == 1 and tid(page, "category-pager").get_attribute("data-pages") == "1" and tid(page, "category-dots").count() == 0)
    run.check("默认选中第一个收入分类", selected_category(page) == db.preset_category_names(BILL_TYPE_INCOME)[0], selected_category(page))
    run.check("金额与分段选中文字都变绿", tid(page, "amount-display").evaluate("el => getComputedStyle(el).color") == RGB_INCOME_TEXT and sheet.locator(".ft-seg__item--on").evaluate("el => getComputedStyle(el).color") == RGB_INCOME_TEXT)
    press_keys(page, "12000")
    run.check("金额显示千分位 12,000", wait_amount_text(page, "12000") and page.locator(".ft-addbill__num").inner_text() == "12,000", page.locator(".ft-addbill__num").inner_text())
    shot(page, "addbill-income")

    tid(page, "addbill-type").get_by_test_id("segment").filter(has_text="支出").click()
    run.check("切回支出：金额保留，分类换回支出的默认项，颜色变回红", attr_is(page, SHEET, "data-type", "expense") and amount_text(page) == "12000" and selected_category(page) == first_category(page) != "" and tid(page, "amount-display").evaluate("el => getComputedStyle(el).color") == RGB_EXPENSE_TEXT)
    income.click()
    attr_is(page, SHEET, "data-type", "income")

    note = unique_name("E2E收入")
    pick_category(page, "奖金补贴")
    tid(page, "addbill-note").fill(note)
    tid(page, "addbill-note").press("Enter")
    wait_attr(page, SHEET, "data-typing", None)
    tid(page, "addbill-save").click()
    run.check("保存收入后 Sheet 收起并提示", gone(sheet, 8000) and toast(page, f"已保存到「{ledger_name}」"))
    bill = (db.bills_by_note(note) or [{}])[0]
    run.check("收入账单落库：类型收入、金额 12000、分类「奖金补贴」", bill.get("Type") == BILL_TYPE_INCOME and amount_equals(bill.get("Amount"), 12000) and bill.get("CategoryId") == db.category_id_by_name("奖金补贴", BILL_TYPE_INCOME), str(bill))
    run.check("收入账单同样是手动录入", bill.get("Source") == SOURCE_MANUAL)


# ---------------------------------------------------------------------------------------------- H. 时间与备注


def check_time(run: TestRun, page, db: LocalDb, user_id: str, ledger_name: str) -> None:
    sheet = open_add_bill_sheet(page)
    when = (datetime.now() - timedelta(days=1)).replace(hour=8, minute=30, second=0, microsecond=0)
    sheet.get_by_test_id("addbill-time").fill(when.strftime("%Y-%m-%dT%H:%M"))
    run.check("改时间为昨天 08:30：时间行显示「昨天 08:30」", poll(page, "() => document.querySelector('[data-testid=\"time-row\"] .ft-cell__value')?.textContent.trim() === '昨天 08:30'"), row_value(page, "time-row"))

    older = (datetime.now() - timedelta(days=40)).replace(hour=21, minute=5, second=0, microsecond=0)
    sheet.get_by_test_id("addbill-time").fill(older.strftime("%Y-%m-%dT%H:%M"))
    expected = f"{older.month}月{older.day}日 21:05" if older.year == datetime.now().year else f"{older.year}年{older.month}月{older.day}日 21:05"
    run.check("更早的日期显示「M月d日 HH:mm」（跨年带年份）", poll(page, "(t) => document.querySelector('[data-testid=\"time-row\"] .ft-cell__value')?.textContent.trim() === t", arg=expected), row_value(page, "time-row"))

    sheet.get_by_test_id("addbill-time").fill(when.strftime("%Y-%m-%dT%H:%M"))
    wait_attr(page, '[data-testid="time-row"]', "data-value", when.strftime("%Y-%m-%dT%H:%M"))
    note = unique_name("E2E昨天")
    press_keys(page, "8.8")
    tid(page, "addbill-note").fill(note)
    tid(page, "addbill-note").press("Enter")
    wait_attr(page, SHEET, "data-typing", None)
    tid(page, "addbill-save").click()
    gone(sheet, 8000)
    bill = (db.bills_by_note(note) or [{}])[0]
    run.check("时间按应用时区输入、库内落 UTC（与本机时区换算一致）", bill.get("TransactionTime") == to_db_time(when), f"{bill.get('TransactionTime')} vs {to_db_time(when)}")


# ---------------------------------------------------------------------------------------------- I. 基座与入口


def check_facade_and_entries(run: TestRun, page, db: LocalDb, info, ledger_name: str) -> None:
    note = unique_name("门面")
    bill_id = create_bill(info, amount="8.80", ledger=ledger_name, note=note, page=page)
    bill = db.bills_by_note(note)
    run.check("create_bill(page=…) 经新界面保存：恰一笔、金额 8.8、手动录入", len(bill) == 1 and bill[0]["Id"] == bill_id and bill[0]["Amount"] == "8.8" and bill[0]["Source"] == SOURCE_MANUAL, str(bill))
    for label, kwargs in (("无账本", {"ledger": None}), ("非手动来源", {"ledger": ledger_name, "source": 4})):
        try:
            create_bill(info, page=page, **kwargs)
            run.check(f"create_bill(page=…) 拒绝{label}的组合（Sheet 做不到）", False)
        except ValueError:
            run.check(f"create_bill(page=…) 拒绝{label}的组合（Sheet 做不到）", True)

    response = page.request.get(f"{BASE_URL}/add-bill", max_redirects=0)
    run.check("旧地址 /add-bill：服务端 302 到 /?add-bill=1", response.status == 302 and response.headers.get("location") == "/?add-bill=1", f"{response.status} {response.headers.get('location')}")
    open_app(page, "/add-bill")
    run.check("深链进入后升起记一笔 Sheet（地址落在首页）", visible(addbill_sheet(page), 15000) and page.evaluate("() => location.pathname") == "/")
    wait_attr(page, SHEET, "data-ready", "true")
    run.check("深链打开的 Sheet 同样读到了数据（账本默认选中）", row_value(page, "ledger-row") == ledger_name)
    close_add_bill_sheet(page)

    open_add_bill_sheet(page)
    mic = tid(page, "addbill-voice")
    label = mic.get_attribute("aria-label") or ""
    run.check("语音入口：麦克风按钮在标题栏右侧，读屏名称写明语音记账", mic.is_visible() and "语音" in label, label)
    if mic.get_attribute("aria-disabled") == "true":
        mic.click()
        run.check("无头环境不支持语音：置灰，点按只给一句说明，手动填写不受影响", toast(page, "当前环境不支持语音识别") and tid(page, "keypad").is_visible())
    else:
        run.skip("语音入口点按", "当前环境支持语音识别：覆盖层流程见 voice_overlay_test.py（假识别器），真实麦克风见人工验收")
    close_add_bill_sheet(page)


# ---------------------------------------------------------------------------------------------- J. 共享账本


def check_shared_ledgers(run: TestRun, db: LocalDb) -> None:
    try:
        cloud = FakeCloud().start()
    except CloudPortBusy as exc:
        run.skip("共享账本菜单", str(exc))
        return

    try:
        with session() as s:
            page = s.page
            info = login(page)
            world = CloudWorld(cloud, db)
            me = world.me(info.phone)
            own_name = unique_name("自有账本")
            own_id = db.seed_ledger(me.local_id, own_name)
            world.own_ledger(me, own_id, own_name)
            editor_name = unique_name("可编辑共享账本")
            viewer_name = unique_name("只读共享账本")
            editor = world.foreign_ledger(world.person(), editor_name, me=me, my_role=ROLE_EDITOR)
            world.foreign_ledger(world.person(), viewer_name, me=me, my_role=ROLE_VIEWER)

            open_add_bill_sheet(page)
            run.check("默认选中自己的账本（共享账本不会被默认选中）", row_value(page, "ledger-row") == own_name, row_value(page, "ledger-row"))
            tid(page, "ledger-row").click()
            menu = tid(page, "ledger-menu")
            visible(menu)
            # 共享账本要先联网刷新成员关系，在表单可用之后才补进菜单
            loaded = poll(page, "() => document.querySelectorAll('[data-testid=\"ledger-option\"]').length >= 2", timeout=15000)
            options = tid(page, "ledger-option")
            names = [options.nth(i).inner_text().strip().replace("\n", " ") for i in range(options.count())]
            run.check("账本菜单 = 自己的账本 + 有写权限（可编辑）的共享账本", loaded and options.count() == 2 and own_name in names[0] and editor_name in names[1], str(names))
            run.check("只读成员的共享账本不出现在菜单里", all(viewer_name not in n for n in names), str(names))
            run.check("共享账本带「共享」徽标，自己的没有", tid(page, "ledger-option-shared").count() == 1 and "共享" in options.nth(1).inner_text() and "共享" not in options.nth(0).inner_text())
            run.check("当前账本带选中标记（对勾）", options.nth(0).get_attribute("data-selected") == "true" and options.nth(1).get_attribute("data-selected") is None)
            shot(page, "addbill-ledger-menu-shared")
            options.nth(1).click()
            run.check("选共享账本：账本行显示它，并带「共享」徽标", gone(menu) and row_value(page, "ledger-row") == editor_name and visible(tid(page, "ledger-shared-badge"), 3000))

            press_keys(page, "12")
            note = unique_name("共享记账")
            tid(page, "addbill-note").fill(note)
            tid(page, "addbill-note").press("Enter")
            wait_attr(page, SHEET, "data-typing", None)
            tid(page, "addbill-save").click()
            run.check("可编辑成员能记入共享账本：Sheet 收起并提示「已保存到〈共享账本〉」", gone(addbill_sheet(page), 10000) and toast(page, f"已保存到「{editor_name}」"))
            bill = (db.bills_by_note(note) or [{}])[0]
            run.check("账单挂在共享账本名下、作者是我", (bill.get("LedgerId") or "").upper() == editor.ledger_id.upper() and bill.get("UserId") == me.local_id, str(bill))
            run.check_clean(s.errors, "J 节会话无页面异常 / 控制台错误 / 失败请求")
    finally:
        cloud.stop()


# ---------------------------------------------------------------------------------------------- K. 减弱动态效果


def check_reduced_motion(run: TestRun) -> None:
    with session(reduced_motion=True) as s:
        page = s.page
        login(page)
        open_add_bill_sheet(page)
        run.check("减弱动态效果：背景页面不缩放", page.evaluate(STACK_JS)["transform"] == "none", str(page.evaluate(STACK_JS)))
        run.check("减弱动态效果：金额光标不闪", page.locator(".ft-addbill__caret").evaluate("el => getComputedStyle(el).animationName") == "none")
        close_add_bill_sheet(page)
        run.check_clean(s.errors, "K 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- L. 缺项引导（金额）


def check_guidance(run: TestRun, page, db: LocalDb, user_id: str, ledger_name: str) -> None:
    """金额为空 / 为 0：保存键置灰；点按给「请输入金额」提示并让金额区抖动（不弹账本菜单）；敲了金额后点亮。"""
    open_add_bill_sheet(page)
    save = tid(page, "addbill-save")
    page.evaluate(SHAKE_OBSERVER_JS)

    run.check("还没敲金额：保存键置灰（aria-disabled）", save.get_attribute("aria-disabled") == "true")
    save.click(force=True)
    run.check("点按置灰的保存键 → Toast「请输入金额」", toast(page, "请输入金额"))
    run.check("金额区抖动", poll(page, SHAKEN_JS, arg="amount-display", timeout=3000))
    run.check("缺的不是账本：没有弹出账本菜单、账本行没抖", tid(page, "ledger-menu").count() == 0 and not page.evaluate(SHAKEN_JS, "ft-addbill__anchor"))

    press_key(page, "00")
    run.check("金额为 0：保存键仍置灰（零值不可保存）", wait_amount_text(page, "0") and save.get_attribute("aria-disabled") == "true")
    page.wait_for_function("() => !document.querySelector('[data-testid=\"amount-display\"]').hasAttribute('data-shake')")
    page.evaluate("() => { window.__shakeSeen = []; }")
    save.click(force=True)
    run.check("金额为 0 点按：同样抖动金额区", poll(page, SHAKEN_JS, arg="amount-display", timeout=3000))
    run.check("没有保存任何账单（此前各节都没有留下账单）", db.bills_of_user(user_id) == [])

    press_key(page, "5")
    run.check("敲了 5：保存键点亮（去掉 aria-disabled）", wait_amount_text(page, "5") and attr_is(page, '[data-testid="addbill-save"]', "aria-disabled", None))
    shot(page, "addbill-guidance")

    # 有输入：关闭先确认，放弃
    addbill_sheet(page).get_by_test_id("sheet-close").click()
    visible(tid(page, "alert"))
    tid(page, "alert-confirm").click()
    gone(addbill_sheet(page))
    wait_no_toasts(page)


# ---------------------------------------------------------------------------------------------- M. 来源页状态保留


def check_state_preserved(run: TestRun, page, db: LocalDb, user_id: str, ledger_name: str) -> None:
    """Sheet 是覆盖层，不是路由页：从账单 Tab 升起、关闭或保存之后，账单 Tab 的分段、滚动位置、大标题折叠状态都原样。"""
    ledger_id = db.ledger_id_by_name(user_id, ledger_name)
    # 账单 Tab 默认只看「本月」：30 笔造在本月 1 号凌晨起（每 10 分钟一笔），无论今天几号都落在默认视图里
    start = datetime.now().replace(day=1, hour=0, minute=5, second=0, microsecond=0)
    for i in range(30):
        db.seed_bill(user_id, ledger_id=ledger_id, amount=10 + i, note=f"状态保留{i}", when=start + timedelta(minutes=10 * i))

    open_tab(page, "账单")
    pull_refresh(page, "bills-ptr")  # Tab 根页面常驻，造完数据要自己（下拉）刷新一次
    poll(page, "() => document.querySelector('[data-testid=\"bills-page\"]').getAttribute('aria-busy') !== 'true'", timeout=15000)
    wait_scroll_root_stable(page)

    segment = tid(page, "bills-segment").get_by_test_id("segment").filter(has_text="支出")
    segment.click()
    run.check("账单 Tab 选了分段「支出」", poll(page, "() => [...document.querySelectorAll('[data-testid=\"bills-segment\"] [data-testid=\"segment\"]')].find((e) => e.textContent.trim() === '支出')?.getAttribute('aria-pressed') === 'true'"))
    wait_scroll_root_stable(page)
    scroll_root_to(page, 700)
    page.wait_for_function("() => document.querySelector('[data-testid=\"bills-page\"]').scrollTop >= 600")
    # 折叠标记由脚本在滚动之后的下一帧写上，滚到位的那一刻可能还没写：等它出现再取基线（否则偶发取到「未折叠」的基线）
    page.wait_for_function("() => document.querySelector('[data-testid=\"bills-page\"]').getAttribute('data-title-collapsed') === 'true'")
    wait_animations_settled(page)

    def snapshot() -> dict:
        return page.evaluate(
            """() => {
                const root = document.querySelector('[data-testid="bills-page"]');
                const pressed = [...document.querySelectorAll('[data-testid="bills-segment"] [data-testid="segment"]')].find((e) => e.getAttribute('aria-pressed') === 'true');
                return { top: root.scrollTop, collapsed: root.getAttribute('data-title-collapsed'), segment: pressed && pressed.textContent.trim(),
                         path: location.pathname, rows: document.querySelectorAll('[data-testid="bills-page"] [data-testid="bill-row"]').length };
            }"""
        )

    before = snapshot()
    run.check("准备：滚到 ≥ 600、大标题已折叠、停在 /bills", before["top"] >= 600 and before["collapsed"] == "true" and before["path"] == "/bills" and before["segment"] == "支出", str(before))

    open_add_bill_sheet(page)
    opened = snapshot()
    run.check("Sheet 升起时：地址不变、账单 Tab 的滚动位置 / 折叠 / 分段原样（Sheet 盖在上面）", opened == before, f"{opened} vs {before}")
    close_add_bill_sheet(page)
    after = snapshot()
    run.check("关闭（没保存）后：滚动位置、折叠状态、分段、已加载笔数都原样", after == before and active_tab(page) == "bills", f"{after} vs {before}")

    note = unique_name("状态保留保存")
    save_bill_via_sheet(page, amount=1, ledger=ledger_name, note=note)
    saved = poll(page, "(n) => [...document.querySelectorAll('[data-testid=\"bills-page\"] [data-testid=\"bill-row\"]')].some((r) => r.textContent.includes(n))", arg=note, timeout=10000)
    after_save = snapshot()
    run.check("保存后：地址不变、账单 Tab 重读到这笔账单、分段仍是「支出」", saved and after_save["path"] == "/bills" and after_save["segment"] == "支出" and active_tab(page) == "bills", str(after_save))
    run.check("保存后：滚动位置没有被重载拽回顶部", after_save["top"] >= 400, str(after_save))
    open_tab(page, "首页")


# ---------------------------------------------------------------------------------------------- O. 玻璃层预算

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


def check_glass_budget(run: TestRun, page) -> None:
    """玻璃层预算（每屏 ≤ 3）：Sheet 升起后，被遮罩盖住的页面与 Tab 栏关掉毛玻璃，只剩 Sheet 标题栏的两个玻璃圆钮；收起后恢复。"""
    wait_no_toasts(page)  # Toast 本身也是玻璃胶囊，但是短暂的，不算「常驻」的层数
    for tab, label in (("home", "首页"), ("bills", "账单"), ("stats", "统计"), ("me", "我的")):
        open_tab(page, label)
        wait_animations_settled(page)
        before = page.evaluate(GLASS_LAYERS_JS)
        open_add_bill_sheet(page)
        wait_animations_settled(page)
        during = page.evaluate(GLASS_LAYERS_JS)
        run.check(f"【{label}】Sheet 打开时屏幕上只有标题栏的两个玻璃圆钮（≤ 3 层）", sorted(during) == ["addbill-voice", "sheet-close"], f"{during}（打开前 {before}）")
        close_add_bill_sheet(page)
        wait_animations_settled(page)
        after = page.evaluate(GLASS_LAYERS_JS)
        run.check(f"【{label}】收起后页面与 Tab 栏的毛玻璃恢复（和打开前一样）", sorted(after) == sorted(before) and len(after) >= 1, f"{after} vs {before}")


# ---------------------------------------------------------------------------------------------- N. 用户默认值


def check_defaults(run: TestRun, page, db: LocalDb, user_id: str, ledger_name: str) -> None:
    """用户默认值（设为默认的分类 / 支付渠道 / 账本）：打开时预选；没设的退回第一项；默认账本不可用时退回第一个自己的账本。"""
    second_name = unique_name("默认账本")
    second_id = db.seed_ledger(user_id, second_name)

    expense_name = db.preset_category_names()[3]
    income_name = db.preset_category_names(BILL_TYPE_INCOME)[2]
    channel_name = db.preset_channel_names()[-1]
    db.set_user_defaults(
        user_id,
        ledger_id=second_id,
        expense_category_id=db.category_id_by_name(expense_name),
        income_category_id=db.category_id_by_name(income_name, BILL_TYPE_INCOME),
        channel_id=db.channel_id_by_name(channel_name),
    )
    try:
        sheet = open_add_bill_sheet(page)
        run.check("默认账本预选", row_value(page, "ledger-row") == second_name, row_value(page, "ledger-row"))
        run.check("默认支出分类预选（不是网格里的第一个）", selected_category(page) == expense_name and first_category(page) != expense_name, f"{selected_category(page)} / {first_category(page)}")
        run.check("默认支付渠道预选", row_value(page, "channel-row") == channel_name, row_value(page, "channel-row"))

        sheet.get_by_test_id("addbill-type").get_by_test_id("segment").filter(has_text="收入").click()
        attr_is(page, SHEET, "data-type", "income")
        run.check("切到收入：预选默认收入分类", poll(page, "(n) => document.querySelector('[data-testid=\"category-tile\"][data-selected=\"true\"]')?.getAttribute('data-name') === n", arg=income_name), selected_category(page))
        sheet.get_by_test_id("addbill-type").get_by_test_id("segment").filter(has_text="支出").click()
        run.check("切回支出：重新预选默认支出分类", poll(page, "(n) => document.querySelector('[data-testid=\"category-tile\"][data-selected=\"true\"]')?.getAttribute('data-name') === n", arg=expense_name), selected_category(page))
        close_add_bill_sheet(page)

        # 默认账本被删除（别处删了）：退回第一个自己的账本
        db.soft_delete_ledger(second_id)
        open_add_bill_sheet(page)
        run.check("默认账本已不可用：退回第一个自己的账本", row_value(page, "ledger-row") == ledger_name, row_value(page, "ledger-row"))
        close_add_bill_sheet(page)
    finally:
        db.set_user_defaults(user_id)  # 默认值清空，别影响之后的用例


# ---------------------------------------------------------------------------------------------- 主流程


def main() -> int:
    require_server()
    run = TestRun("addbill-sheet")
    db = LocalDb()
    db.verify_schema()

    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)

        check_structure(run, page, db, user_id)
        check_keypad(run, page)
        ledger_name = check_missing_ledger(run, page, db, user_id)
        check_category_grid(run, page, db, user_id)
        check_guarded_close(run, page, db, user_id, ledger_name)
        check_guidance(run, page, db, user_id, ledger_name)
        check_save(run, page, db, user_id, ledger_name)
        check_over_budget(run, page, db, user_id, ledger_name)
        check_income(run, page, db, user_id, ledger_name)
        check_time(run, page, db, user_id, ledger_name)
        check_facade_and_entries(run, page, db, info, ledger_name)
        check_state_preserved(run, page, db, user_id, ledger_name)
        check_glass_budget(run, page)
        check_defaults(run, page, db, user_id, ledger_name)

        run.check_clean(s.errors, "A–I、L–O 节会话无页面异常 / 控制台错误 / 失败请求")

    check_shared_ledgers(run, db)
    check_reduced_motion(run)

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
