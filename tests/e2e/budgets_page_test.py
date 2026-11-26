# -*- coding: utf-8 -*-
"""
E2E 测试：预算页（iOS UI 重构 · 18 号 ticket）——二级页面模板 + 环形总览 + 「日均可用」+ 分类预算进度行 + 新建 / 编辑 Sheet + 左滑 / 删除确认。

只覆盖网页层——Android / iOS 上的真机手感（左滑跟手、Sheet 弹簧、键盘避让、返回手势、超支本地通知）见 ticket 18 的人工验收清单。

  A. 空状态与外壳：新用户（没有账本、没有预算）——二级页面模板（Tab 栏隐藏、导航栏）、月份胶囊、没有账本时不出现账本胶囊、空状态；
     经界面新建第一条总预算（没有账本时表单不出现账本一栏）：数字键盘（. / 00 / 退格 / 千分位 / 两位小数）、保存键置灰与点亮、落库、环形总览与「日均可用」出现
  B. 总览与分类行：全部账本视图的口径（只含全部账本的预算）、环形总览（百分比、已用、剩余、读屏文案）、「日均可用」按应用时区日期计算、
     分类行按已用金额排序、三种状态（正常 / 接近上限 / 超支）的文字 + 颜色、整行可点的点击区域与读屏名称
  C. 账本筛选：账本胶囊 → 菜单 → 切到某账本（该账本的预算 + 全部账本的预算，总览取该账本自己的总预算，其它总预算单列）；
     账本没有自己的总预算时总览退回全部账本的总预算
  D. 月份切换：上一月 / 下一月（没有预算的月份是空状态；非当前月份没有「日均可用」）；表单沿用页面当前月份
  E. 新建分类预算：「＋」默认分类预算（已有总预算时）、选分类 / 账本、落库；重复预算被服务层拦下并把提示留在表单里；换账本后保存成功并跳到该账本；
     表单校验（缺金额 / 缺分类）、切换分段不丢金额
  F. 编辑：点按整行 / 左滑「编辑」/ 总览「编辑」进入同一个 Sheet（预填、模式、分类与账本选中）；改额度落库并刷新；取消不落库、再次打开是干净的；
     编辑改成重复预算同样被拦下
  G. 删除：左滑「删除」→ Alert（取消保留 / 确认软删除）；Sheet 里的「删除预算」（Alert 盖在 Sheet 之上）；删掉总预算后出现「设置总预算」引导、「＋」默认总预算
  H. 保存后跳转：表单里改了月份保存后，页面切到保存的月份
  I. 总览三种状态（正常 / 接近上限 / 超支 / 刚好用完）：环色、环中心文字、剩余 / 超支标签、没有剩余额度时不显示「日均可用」
  J. 窄屏（320）：总览卡改成上下排、筛选行折行、八位数金额不冲出卡片、没有横向溢出
  K. 降级：减少动态效果时进度环弧长不做过渡
  L. 记一笔后进度更新：经「记一笔」Sheet 真实记几笔支出（含超出分类预算的、没有预算的分类），再进预算页——已用 / 使用率 / 状态 / 「日均可用」随之更新，
     分类预算超支不影响总预算，没有预算的分类不会冒出新行

可观察属性（已登记到 README）：budgets-page、budgets-content（data-state / data-year / data-month / data-ledger）、budget-add、budget-period（-prev / -next / -label）、
budget-ledger-pill、budget-ledger-sheet / budget-ledger-all / budget-ledger-option[data-ledger-id]、budget-overview（data-state / data-percent）、budget-ring（role=progressbar）、
budget-ring-percent / budget-ring-caption、budget-total-title / budget-total-amount / budget-used / budget-remain-label / budget-remain / budget-total-edit、
budget-daily（data-days / data-per-day）、budget-total-guide、budget-other-totals、budget-category-group、budget-swipe[data-budget-id][data-open]、budget-row 系列、
budget-category-add、budget-empty / budget-empty-add、budgets-error / budgets-retry、budget-delete-alert、
budget-sheet（budget-form[data-mode / data-scope]、budget-scope、budget-amount[data-value / data-empty / data-size]、budget-form-period、budget-form-ledger-any / -chip、
budget-form-category-chip、budget-delete-cell、budget-form-error、budget-save）、swipe-edit / swipe-delete、alert-*。
"""
import calendar
import re
import sys
from datetime import datetime, timedelta
from decimal import ROUND_HALF_EVEN, ROUND_HALF_UP, Decimal
from math import pi

from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

from e2e_base import (
    GLASS_LAYERS_JS,
    BILL_TYPE_EXPENSE,
    BILL_TYPE_INCOME,
    PHONE_VIEWPORT,
    LocalDb,
    TestRun,
    login,
    open_app,
    press_key,
    press_keys,
    require_server,
    save_bill_via_sheet,
    session,
    shot,
    swipe_by,
    tap,
    unique_name,
    wait_animations_settled,
    wait_attr,
    wait_state,
    wait_tab_bar,
)

SCREEN_W = PHONE_VIEWPORT["width"]
CONTENT = '[data-testid="budgets-content"]'
SHEET = "budget-sheet"
RING_RADIUS = 44.0  # 与 RingGeometry.Radius 一致
CHANGED_PREFIX = "预算"

PAGE_JS = """() => {
  const q = (id, root = document) => root.querySelector(`[data-testid="${id}"]`);
  const text = (id, root = document) => { const n = q(id, root); return n ? n.textContent.trim() : null; };
  const rowInfo = (el) => {
    const bar = q('budget-progress', el);
    const name = q('budget-name', el);
    return {
      id: el.getAttribute('data-budget-id'),
      name: name ? name.textContent.trim() : null,
      nameTag: name ? name.tagName : null,
      aria: name ? name.getAttribute('aria-label') : null,
      tag: text('budget-tag', el),
      remain: text('budget-remain', el),
      state: el.getAttribute('data-state'),
      percent: el.getAttribute('data-percent'),
      tone: bar ? bar.getAttribute('data-tone') : null,
      now: bar ? bar.getAttribute('aria-valuenow') : null,
      valuetext: bar ? bar.getAttribute('aria-valuetext') : null,
    };
  };
  const content = q('budgets-content');
  const overview = q('budget-overview');
  const ring = q('budget-ring');
  const daily = q('budget-daily');
  return {
    state: content ? content.getAttribute('data-state') : null,
    year: content ? content.getAttribute('data-year') : null,
    month: content ? content.getAttribute('data-month') : null,
    ledger: content ? (content.getAttribute('data-ledger') || '') : null,
    busy: content ? content.getAttribute('aria-busy') : null,
    period: text('budget-period-label'),
    pill: text('budget-ledger-pill'),
    overview: overview ? {
      id: overview.getAttribute('data-budget-id'),
      state: overview.getAttribute('data-state'),
      percent: overview.getAttribute('data-percent'),
      title: text('budget-total-title'),
      amount: text('budget-total-amount'),
      used: text('budget-used'),
      remainLabel: text('budget-remain-label'),
      remain: text('budget-remain'),
      pct: text('budget-ring-percent'),
      caption: text('budget-ring-caption'),
      ringTone: ring.getAttribute('data-tone'),
      ringPercent: ring.getAttribute('data-percent'),
      ringRole: ring.getAttribute('role'),
      ringNow: ring.getAttribute('aria-valuenow'),
      ringText: ring.getAttribute('aria-valuetext'),
      ringLabel: ring.getAttribute('aria-label'),
      arcs: ring.querySelectorAll('.ft-ring__arc').length,
      editAria: q('budget-total-edit') ? q('budget-total-edit').getAttribute('aria-label') : null,
    } : null,
    daily: daily ? { text: text('budget-daily-text'), days: daily.getAttribute('data-days'), perDay: daily.getAttribute('data-per-day'), role: daily.getAttribute('role') } : null,
    other: [...document.querySelectorAll('[data-testid="budget-other-totals"] [data-testid="budget-row"]')].map(rowInfo),
    categories: [...document.querySelectorAll('[data-testid="budget-category-group"] [data-testid="budget-row"]')].map(rowInfo),
    guide: !!q('budget-total-guide'),
    addRow: !!q('budget-category-add'),
  };
}"""

SHEET_JS = """() => {
  const q = (id, root = document) => root.querySelector(`[data-testid="${id}"]`);
  const sheet = q('budget-sheet');
  if (!sheet) return null;
  const form = q('budget-form', sheet);
  const amount = q('budget-amount', sheet);
  const save = q('budget-save', sheet);
  const chips = (id) => [...sheet.querySelectorAll(`[data-testid="${id}"]`)].map((c) => ({
    id: c.getAttribute('data-id'), label: c.textContent.trim(), selected: c.getAttribute('data-selected') === 'true', pressed: c.getAttribute('aria-pressed'),
  }));
  const segs = [...sheet.querySelectorAll('[data-testid="budget-scope"] [data-testid="segment"]')];
  const any = q('budget-form-ledger-any', sheet);
  const err = q('budget-form-error', sheet);
  const title = document.querySelector('[data-testid="sheet-title"]');
  return {
    title: title ? title.textContent.trim() : null,
    mode: form.getAttribute('data-mode'),
    scope: form.getAttribute('data-scope'),
    segLabels: segs.map((s) => s.textContent.trim()),
    segPressed: segs.filter((s) => s.getAttribute('aria-pressed') === 'true').map((s) => s.getAttribute('data-value')),
    segMinHeight: Math.min(...segs.map((s) => s.getBoundingClientRect().height)),
    amount: amount.getAttribute('data-value'),
    empty: amount.getAttribute('data-empty'),
    size: amount.getAttribute('data-size'),
    shown: amount.querySelector('.ft-amountentry__num').textContent.trim(),
    spoken: amount.querySelector('.ft-sr-only').textContent.trim(),
    period: q('budget-form-period-label', sheet).textContent.trim(),
    ledgerSection: !!q('budget-form-ledger', sheet),
    anyLedger: any ? any.getAttribute('data-selected') === 'true' : null,
    ledgers: chips('budget-form-ledger-chip'),
    categorySection: !!q('budget-form-category', sheet),
    categories: chips('budget-form-category-chip'),
    hasDelete: !!q('budget-delete-cell', sheet),
    error: err ? err.textContent.trim() : null,
    errorRole: err ? err.getAttribute('role') : null,
    saveDisabled: save ? save.getAttribute('aria-disabled') : null,
  };
}"""


# ---------------------------------------------------------------------------------------------- 小工具


def tid(page, testid: str):
    return page.get_by_test_id(testid)


def dec(value) -> Decimal:
    return Decimal(str(value))


def money(value) -> str:
    return f"¥{dec(value):,.2f}"


def compact(value) -> str:
    d = dec(value)
    return f"¥{d:,.0f}" if d == d.to_integral_value() else f"¥{d:,.2f}"


def shown_percent(used, amount) -> int:
    """页面显示的百分比：服务层先按一位小数舍入（银行家舍入，decimal 默认），展示再向下取整。"""
    pct = (dec(used) / dec(amount) * 100).quantize(Decimal("0.1"), rounding=ROUND_HALF_EVEN)
    return int(pct // 1)


def remain_text(used, amount) -> str:
    """预算行右上角文字：剩余 / 仅剩（接近上限）/ 超支 / 已用完。"""
    left = dec(amount) - dec(used)
    if left < 0:
        return f"超支 {money(-left)}"
    if left == 0:
        return "已用完"
    return f"{'仅剩' if shown_percent(used, amount) >= 80 else '剩余'} {money(left)}"


def state_of(used, amount) -> str:
    pct = shown_percent(used, amount)
    return "over" if pct >= 100 else "warning" if pct >= 80 else "normal"


def daily_of(remaining, now: datetime) -> "tuple[int, Decimal]":
    """日均可用：剩余额度 ÷ 本月剩余天数（含今天），四舍五入（远离零）到分。"""
    days = calendar.monthrange(now.year, now.month)[1] - now.day + 1
    return days, (dec(remaining) / days).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)


def daily_text(remaining, now: datetime) -> str:
    days, per_day = daily_of(remaining, now)
    return f"还剩 {days} 天（含今天），日均可用 {money(per_day)}"


def shift_month(year: int, month: int, delta: int) -> "tuple[int, int]":
    index = year * 12 + (month - 1) + delta
    return index // 12, index % 12 + 1


def month_label(year: int, month: int) -> str:
    return f"{year}年{month}月"


def snapshot(page) -> dict:
    return page.evaluate(PAGE_JS)


def sheet_state(page) -> "dict | None":
    return page.evaluate(SHEET_JS)


def row_named(rows: "list[dict]", name: str) -> "dict | None":
    return next((r for r in rows if r["name"] == name), None)


def wait_loaded(page, *, year: int, month: int, ledger_id: "str | None" = None) -> None:
    """等预算加载完成：筛选对应的年 / 月 / 账本已经读完（data-* 在读完后才更新），且不在加载中。"""
    page.wait_for_function(
        """([y, m, l]) => {
          const el = document.querySelector('[data-testid="budgets-content"]');
          return !!el && el.getAttribute('data-state') !== 'loading'
            && el.getAttribute('data-year') === String(y) && el.getAttribute('data-month') === String(m)
            && (el.getAttribute('data-ledger') || '') === l;
        }""",
        arg=[year, month, (ledger_id or "").lower()],
    )
    wait_animations_settled(page)


def open_page(page, *, year: int, month: int, state: str = "ready") -> None:
    """整页进入预算页并等到加载完成（当前月份、全部账本）。"""
    open_app(page, "/budgets")
    wait_attr(page, CONTENT, "data-state", state)
    wait_loaded(page, year=year, month=month)


def toast(page, text: str):
    return page.locator(".m-enqueued-snackbars").get_by_text(text)


def wait_toast(page, text: str) -> None:
    toast(page, text).first.wait_for(state="visible")


def wait_toasts_gone(page) -> None:
    """等排队的提示都消失（同一句提示连着出现两次时，免得把上一条当成这一条）。"""
    page.wait_for_function("() => document.querySelectorAll('.m-enqueued-snackbars .m-snack__wrapper').length === 0", timeout=20000)


def sheet(page):
    return tid(page, SHEET)


def open_sheet(page, trigger: str) -> None:
    """点触发控件（budget-add / budget-empty-add / budget-category-add / budget-total-guide / budget-total-edit）升起表单 Sheet。"""
    tid(page, trigger).click()
    sheet(page).wait_for(state="visible")
    wait_animations_settled(page)


def open_edit_by_name(page, name: str) -> None:
    """点按预算行的名称进入编辑 Sheet。"""
    page.locator('[data-testid="budget-name"]').filter(has_text=re.compile(rf"^{re.escape(name)}$")).click()
    sheet(page).wait_for(state="visible")
    wait_animations_settled(page)


def close_sheet(page) -> None:
    sheet(page).get_by_test_id("sheet-close").click()
    sheet(page).wait_for(state="detached")


def wait_amount(page, expected: str, timeout: int = 4000) -> bool:
    """等表单里的金额文本变成 expected；超时返回 False（让断言记失败，而不是抛异常打断后面的用例）。"""
    try:
        page.wait_for_function(
            "(v) => { const el = document.querySelector('[data-testid=\"budget-amount\"]'); return !!el && el.getAttribute('data-value') === v; }",
            arg=expected,
            timeout=timeout,
        )
        return True
    except PlaywrightTimeoutError:
        return False


def clear_amount(page) -> None:
    """把金额清空：按退格直到空（键盘没有「清除」键，长按退格才是清除）。"""
    for _ in range(14):
        text = sheet_state(page)["amount"]
        if not text:
            return
        press_key(page, "backspace")
        wait_amount(page, text[:-1], timeout=2000)


def choose_scope(page, label: str) -> None:
    sheet(page).get_by_test_id("budget-scope").get_by_test_id("segment").filter(has_text=label).click()
    page.wait_for_function(
        "(l) => [...document.querySelectorAll('[data-testid=\"budget-scope\"] [data-testid=\"segment\"][aria-pressed=\"true\"]')].some((s) => s.textContent.trim() === l)",
        arg=label,
    )
    wait_animations_settled(page)


def choose_category(page, name: str) -> None:
    chip = sheet(page).get_by_test_id("budget-form-category-chip").filter(has_text=re.compile(rf"^{re.escape(name)}$"))
    chip.click()
    page.wait_for_function(
        "(n) => [...document.querySelectorAll('[data-testid=\"budget-form-category-chip\"][data-selected=\"true\"]')].some((c) => c.textContent.trim() === n)",
        arg=name,
    )


def choose_ledger(page, name: "str | None") -> None:
    """表单里选账本：None = 全部账本。"""
    if name is None:
        sheet(page).get_by_test_id("budget-form-ledger-any").click()
        wait_state(page, "() => document.querySelector('[data-testid=\"budget-form-ledger-any\"]')?.getAttribute('data-selected') === 'true'")
        return
    sheet(page).get_by_test_id("budget-form-ledger-chip").filter(has_text=re.compile(rf"^{re.escape(name)}$")).click()
    page.wait_for_function(
        "(n) => [...document.querySelectorAll('[data-testid=\"budget-form-ledger-chip\"][data-selected=\"true\"]')].some((c) => c.textContent.trim() === n)",
        arg=name,
    )


def submit(page) -> None:
    """点保存键（键盘右侧高大的那一格）。"""
    tid(page, "budget-save").click()


def submit_and_wait_closed(page) -> None:
    submit(page)
    sheet(page).wait_for(state="detached")


def swipe_row_open(page, name: str):
    """触摸左滑展开名为 name 的预算行，返回这一行（budget-swipe）。"""
    row = page.locator('[data-testid="budget-swipe"]').filter(has=page.locator('[data-testid="budget-name"]', has_text=re.compile(rf"^{re.escape(name)}$")))
    swipe_by(page, row.get_by_test_id("budget-row"), -170, 0, steps=10, duration_ms=220, release_hold_ms=150)
    page.wait_for_function("(el) => el.getAttribute('data-open') === 'true'", arg=row.element_handle())
    wait_animations_settled(page)
    page.wait_for_timeout(400)  # 松手后 350ms 内行内的点击会被吞掉（防误触），脚本要等过去
    return row


def pick_ledger_filter(page, ledger_id: "str | None", *, year: int, month: int) -> None:
    """页面上的账本胶囊 → 菜单 → 选账本（None = 全部账本），等筛选对应的预算读完。"""
    tid(page, "budget-ledger-pill").click()
    menu = tid(page, "budget-ledger-sheet")
    menu.wait_for(state="visible")
    wait_animations_settled(page)
    if ledger_id is None:
        tid(page, "budget-ledger-all").click()
    else:
        page.locator(f'[data-testid="budget-ledger-option"][data-ledger-id="{ledger_id.lower()}"]').click()
    menu.wait_for(state="detached")
    wait_loaded(page, year=year, month=month, ledger_id=ledger_id)


def budget_amount(row: "dict | None") -> "Decimal | None":
    return Decimal(row["Amount"]) if row else None


def covers_row(page, name: str) -> dict:
    """某一行的整行点击区域：行内几个位置（标题行、进度条上、行的左 / 右内边距）取到的元素是不是这一行的名称按钮（伪元素铺满整行）。"""
    page.locator('[data-testid="budget-name"]').filter(has_text=re.compile(rf"^{re.escape(name)}$")).scroll_into_view_if_needed()
    return page.evaluate(
        """(name) => {
          const btn = [...document.querySelectorAll('[data-testid="budget-name"]')].find((b) => b.textContent.trim() === name);
          if (!btn) return null;
          const row = btn.closest('[data-testid="budget-row"]');
          const r = row.getBoundingClientRect();
          const bar = row.querySelector('[data-testid="budget-progress"]').getBoundingClientRect();
          const hit = (x, y) => { const el = document.elementFromPoint(x, y); return !!el && el.closest('.ft-budgetrow__open') === btn; };
          return {
            tag: btn.tagName,
            onBar: hit(bar.left + bar.width / 2, bar.top + bar.height / 2),
            onRight: hit(r.right - 6, r.top + r.height / 2),
            onLeft: hit(r.left + 6, r.top + r.height / 2),
            onTitle: hit(r.left + r.width / 2, r.top + 14),
            rowHeight: r.height,
          };
        }""",
        name,
    )


def token_color(page, token: str) -> str:
    return page.evaluate(
        """(token) => { const p = document.createElement('i'); p.style.color = `var(${token})`; document.body.appendChild(p);
           const c = getComputedStyle(p).color; p.remove(); return c; }""",
        token,
    )


# ---------------------------------------------------------------------------------------------- A. 空状态 + 经界面新建总预算


def check_empty_and_first_budget(run: TestRun, page, db: LocalDb, user_id: str, now: datetime) -> None:
    open_page(page, year=now.year, month=now.month, state="empty")
    wait_tab_bar(page, visible=False)
    snap = snapshot(page)

    run.check("预算页：二级页面模板——导航栏标题「预算」，返回 / 新建按钮点击区域 ≥ 44，Tab 栏已隐藏",
              tid(page, "nav-title").inner_text().strip() == "预算" and tid(page, "nav-back").bounding_box()["width"] >= 44
              and tid(page, "budget-add").bounding_box()["width"] >= 44 and tid(page, "budget-add").get_attribute("aria-label") == "新建预算")
    run.check("没有预算：空状态「还没有预算」+「设定额度，快超支时提醒你」+ 主操作「新建预算」",
              snap["state"] == "empty" and "还没有预算" in tid(page, "budget-empty").inner_text() and "设定额度，快超支时提醒你" in tid(page, "budget-empty").inner_text()
              and tid(page, "budget-empty-add").is_visible() and tid(page, "budget-empty-add").inner_text().strip() == "新建预算")
    run.check("月份胶囊显示当前月份（按应用时区），上一月 / 下一月都可点",
              snap["period"] == month_label(now.year, now.month)
              and not tid(page, "budget-period-prev").is_disabled() and not tid(page, "budget-period-next").is_disabled())
    run.check("没有账本：不显示账本胶囊（没有可筛选的账本）", tid(page, "budget-ledger-pill").count() == 0)
    run.check("空状态下没有环形总览 / 日均可用 / 分类分组", snap["overview"] is None and snap["daily"] is None and tid(page, "budget-category-group").count() == 0)
    overflow = page.evaluate("() => document.documentElement.scrollWidth - document.documentElement.clientWidth")
    run.check("没有横向溢出", overflow <= 0, str(overflow))
    glass = page.evaluate(GLASS_LAYERS_JS)
    run.check("整屏玻璃层 ≤ 3（导航栏的两颗圆钮 + 空状态主按钮）", glass <= 3, str(glass))
    shot(page, "budgets-empty")

    # --- 空状态的「新建预算」→ Sheet：没有账本、没有分类预算的入口默认「总预算」
    open_sheet(page, "budget-empty-add")
    st = sheet_state(page)
    run.check("新建 Sheet：标题「新建预算」，模式 create，默认「总预算」一段",
              st["title"] == "新建预算" and st["mode"] == "create" and st["scope"] == "total" and st["segLabels"] == ["总预算", "分类预算"] and st["segPressed"] == ["total"], str(st))
    run.check("分段按钮点击区域高度 ≥ 44", st["segMinHeight"] >= 44, str(st["segMinHeight"]))
    run.check("金额还没敲：显示「0」（浅色）、读屏读「预算金额 ¥0.00」、保存键置灰（aria-disabled）",
              st["empty"] == "true" and st["amount"] == "" and st["shown"] == "0" and st["spoken"] == "预算金额 ¥0.00" and st["saveDisabled"] == "true", str(st))
    run.check("月份沿用页面当前月份", st["period"] == month_label(now.year, now.month), st["period"])
    run.check("没有账本时表单不出现账本一栏；总预算不出现分类一栏；新建没有「删除预算」",
              not st["ledgerSection"] and not st["categorySection"] and not st["hasDelete"] and st["error"] is None)
    shot(page, "budget-sheet-total")

    # --- 数字键盘：. / 00 / 退格 / 千分位 / 两位小数
    press_key(page, ".")
    run.check("先按小数点：补成「0.」", wait_amount(page, "0."), str(sheet_state(page)["amount"]))
    press_keys(page, "555")
    run.check("小数最多两位：第三位小数被忽略（0.55）", wait_amount(page, "0.55") and sheet_state(page)["shown"] == "0.55", str(sheet_state(page)))
    for _ in range(4):
        press_key(page, "backspace")
    run.check("退格逐位删除，删空后回到「还没敲」", wait_amount(page, "") and sheet_state(page)["empty"] == "true", str(sheet_state(page)))
    press_keys(page, "12000")
    run.check("敲 12000：显示千分位「12,000」，保存键点亮（去掉 aria-disabled）",
              wait_amount(page, "12000") and sheet_state(page)["shown"] == "12,000" and sheet_state(page)["saveDisabled"] is None, str(sheet_state(page)))
    press_key(page, "00")
    run.check("「00」键一次补两个零（1200000）", wait_amount(page, "1200000") and sheet_state(page)["shown"] == "1,200,000", str(sheet_state(page)))
    press_key(page, "backspace")
    press_key(page, "backspace")
    press_key(page, ".")
    press_key(page, "5")
    run.check("退格删回 12000 后再敲「.5」→ 12,000.5（保留刚敲的小数）", wait_amount(page, "12000.5") and sheet_state(page)["shown"] == "12,000.5", str(sheet_state(page)))
    press_key(page, "backspace")
    press_key(page, "backspace")
    run.check("连续退格删掉小数与小数点 → 12000", wait_amount(page, "12000"), str(sheet_state(page)["amount"]))

    # --- 保存：落库、Toast、总览出现
    submit_and_wait_closed(page)
    wait_toast(page, "预算创建成功")
    wait_state(page, "() => !!document.querySelector('[data-testid=\"budget-overview\"]')")
    wait_loaded(page, year=now.year, month=now.month)
    rows = db.budgets_of_user(user_id)
    saved = rows[0] if len(rows) == 1 else None
    run.check("保存后落库一条总预算：本月、金额 12000、全部账本、没有分类",
              saved is not None and (saved["Year"], saved["Month"]) == (now.year, now.month) and budget_amount(saved) == Decimal("12000")
              and saved["LedgerId"] is None and saved["CategoryId"] is None and saved["IsDeleted"] == 0 and saved["UserId"].upper() == user_id.upper(), str(rows))
    snap = snapshot(page)
    ov = snap["overview"]
    run.check("总览卡出现：标题「总预算」、额度 ¥12,000.00、已用 ¥0.00、剩余 ¥12,000.00、环中心 0% / 已使用",
              ov is not None and ov["title"] == "总预算" and ov["amount"] == money(12000) and ov["used"] == money(0) and ov["remainLabel"] == "剩余"
              and ov["remain"] == money(12000) and ov["pct"] == "0%" and ov["caption"] == "已使用" and ov["state"] == "normal", str(ov))
    run.check("零使用率：环上没有进度弧，读屏进度 0", ov is not None and ov["arcs"] == 0 and ov["ringNow"] == "0" and ov["ringRole"] == "progressbar", str(ov))
    run.check("「日均可用」出现：剩余额度 ÷ 本月剩余天数（含今天），文案与计算一致",
              snap["daily"] is not None and snap["daily"]["text"] == daily_text(12000, now) and snap["daily"]["role"] == "status", f"{snap['daily']} 期望 {daily_text(12000, now)}")
    run.check("只有总预算时：分类分组只有末尾的「新建分类预算」行，没有其它总预算分组",
              snap["categories"] == [] and snap["addRow"] and tid(page, "budget-other-totals").count() == 0 and not snap["guide"])
    shot(page, "budgets-first-total")
    wait_toasts_gone(page)


# ---------------------------------------------------------------------------------------------- 造数据


class Data:
    """B–H 节共用的数据（本月支出：餐饮 2360（账本 1）、交通 1260（账本 2）、购物 500（账本 1）；收入与上月账单不计入）。"""

    def __init__(self, db: LocalDb, user_id: str, now: datetime) -> None:
        self.now = now
        self.year, self.month = now.year, now.month
        self.prev_year, self.prev_month = shift_month(now.year, now.month, -1)
        self.next_year, self.next_month = shift_month(now.year, now.month, 1)
        self.l1_name = unique_name("日常账本")
        self.l2_name = unique_name("旅行账本")
        self.l1 = db.seed_ledger(user_id, self.l1_name, sort_order=1)
        self.l2 = db.seed_ledger(user_id, self.l2_name, sort_order=2)

        cat = db.category_id_by_name
        db.seed_bill(user_id, amount=2360, ledger_id=self.l1, category_id=cat("餐饮美食"), note=unique_name("餐饮"))
        db.seed_bill(user_id, amount=1260, ledger_id=self.l2, category_id=cat("交通出行"), note=unique_name("交通"))
        db.seed_bill(user_id, amount=500, ledger_id=self.l1, category_id=cat("购物消费"), note=unique_name("购物"))
        db.seed_bill(user_id, amount=5000, ledger_id=self.l1, bill_type=BILL_TYPE_INCOME, note=unique_name("收入"))
        last_month_day = now.replace(day=1, hour=12, minute=0, second=0, microsecond=0) - timedelta(days=3)
        db.seed_bill(user_id, amount=999, ledger_id=self.l1, category_id=cat("餐饮美食"), when=last_month_day, note=unique_name("上月餐饮"))

        # 全部账本的分类预算 3 条；账本 1 自己的总预算与餐饮预算；上月全部账本的总预算
        db.seed_budget(user_id, amount=2000, category_name="餐饮美食")
        db.seed_budget(user_id, amount=1500, category_name="交通出行")
        db.seed_budget(user_id, amount=3000, category_name="购物消费")
        db.seed_budget(user_id, amount=5000, ledger_id=self.l1)
        db.seed_budget(user_id, amount=800, ledger_id=self.l1, category_name="餐饮美食")
        db.seed_budget(user_id, amount=3000, year=self.prev_year, month=self.prev_month)

        self.total = Decimal("12000")  # A 节经界面新建的全部账本总预算
        self.used_all = Decimal("4120")  # 本月全部支出 2360 + 1260 + 500
        self.used_l1 = Decimal("2860")  # 账本 1：2360 + 500


# ---------------------------------------------------------------------------------------------- B. 全部账本视图


def check_overview_and_rows(run: TestRun, page, d: Data) -> None:
    open_page(page, year=d.year, month=d.month)
    snap = snapshot(page)
    ov = snap["overview"]
    pct = shown_percent(d.used_all, d.total)

    run.check("全部账本视图：胶囊显示「全部账本」", snap["pill"] == "全部账本", str(snap["pill"]))
    run.check("总览：只用「全部账本」的总预算（账本 1 自己的总预算在该账本视图里才出现）——额度 / 已用 / 剩余",
              ov is not None and ov["title"] == "总预算" and ov["amount"] == money(d.total) and ov["used"] == money(d.used_all)
              and ov["remain"] == money(d.total - d.used_all) and ov["remainLabel"] == "剩余", str(ov))
    run.check(f"总览：使用率 {pct}%（收入与上月账单不计入），状态正常，环中心「{pct}%」/「已使用」",
              ov is not None and ov["pct"] == f"{pct}%" and ov["percent"] == str(pct) and ov["state"] == "normal" and ov["caption"] == "已使用", str(ov))
    run.check("环：role=progressbar，读屏名称「总预算」，进度整数，读屏文案带百分比与状态",
              ov is not None and ov["ringRole"] == "progressbar" and ov["ringLabel"] == "总预算" and ov["ringNow"] == str(pct) and str(pct) in ov["ringText"], str(ov))
    run.check("总览卡的「编辑」按钮读屏名称带上目标「编辑总预算」", ov is not None and ov["editAria"] == "编辑总预算", str(ov))
    run.check("「日均可用」：剩余 7,880 ÷ 本月剩余天数，按应用时区的今天计算",
              snap["daily"] is not None and snap["daily"]["text"] == daily_text(d.total - d.used_all, d.now)
              and (int(snap["daily"]["days"]), Decimal(snap["daily"]["perDay"])) == daily_of(d.total - d.used_all, d.now), f"{snap['daily']} 期望 {daily_text(d.total - d.used_all, d.now)}")

    arc = page.evaluate(
        """() => { const a = document.querySelector('[data-testid="budget-ring"] .ft-ring__arc'); if (!a) return null;
           const cs = getComputedStyle(a); return { dash: cs.strokeDasharray, transform: a.getAttribute('transform'), cap: a.getAttribute('stroke-linecap'),
             stroke: cs.stroke, transition: cs.transitionProperty + ' ' + cs.transitionDuration }; }"""
    )
    expected_arc = 2 * pi * RING_RADIUS * float((dec(d.used_all) / dec(d.total) * 100).quantize(Decimal("0.1"), rounding=ROUND_HALF_EVEN)) / 100
    got_arc = float(re.findall(r"[\d.]+", arc["dash"])[0]) if arc else -1
    run.check("进度弧从 12 点钟方向起（rotate -90）、圆角端点，弧长 = 周长 × 使用率，品牌色，弧长变化有过渡",
              arc is not None and "rotate(-90" in arc["transform"] and arc["cap"] == "round" and abs(got_arc - expected_arc) < 0.5
              and arc["stroke"] == token_color(page, "--ft-brand") and "stroke-dasharray" in arc["transition"] and "0.5s" in arc["transition"], f"{arc} 期望弧长 {expected_arc:.2f}")

    cats = snap["categories"]
    names = [c["name"] for c in cats]
    run.check("分类行：只含全部账本的分类预算，按已用金额从多到少排（餐饮 2360 → 交通 1260 → 购物 500）",
              names == ["餐饮美食", "交通出行", "购物消费"], str(names))
    run.check("全部账本视图没有「其他总预算」分组", tid(page, "budget-other-totals").count() == 0)
    for name, used, amount in [("餐饮美食", 2360, 2000), ("交通出行", 1260, 1500), ("购物消费", 500, 3000)]:
        row = row_named(cats, name)
        pct_row = shown_percent(used, amount)
        tone = {"over": "danger", "warning": "warning", "normal": "brand"}[state_of(used, amount)]
        run.check(f"「{name}」：灰色小字「{compact(used)} / {compact(amount)}」，右上角「{remain_text(used, amount)}」，状态 {state_of(used, amount)}，进度条 {tone}",
                  row is not None and row["tag"] == f"{compact(used)} / {compact(amount)}" and row["remain"] == remain_text(used, amount)
                  and row["state"] == state_of(used, amount) and row["percent"] == str(pct_row) and row["tone"] == tone, str(row))
    over_row = row_named(cats, "餐饮美食")
    run.check("超支行的进度条撑满（读屏进度 100），读屏文案带「超支」状态；不只靠颜色——右上角文字是「超支 ¥…」",
              over_row is not None and over_row["now"] == "100" and "超支" in (over_row["valuetext"] or "") and over_row["remain"].startswith("超支"), str(over_row))
    warn_row = row_named(cats, "交通出行")
    run.check("接近上限行：文字「仅剩 ¥…」+ 读屏「接近上限」", warn_row is not None and warn_row["remain"].startswith("仅剩") and "接近上限" in (warn_row["valuetext"] or ""), str(warn_row))
    run.check("可点按的行：名称是按钮，读屏名称「编辑预算：名称，状态文字」",
              over_row is not None and over_row["nameTag"] == "BUTTON" and over_row["aria"] == f"编辑预算：餐饮美食，{remain_text(2360, 2000)}", str(over_row))
    hit = covers_row(page, "餐饮美食")
    run.check("整行都是点击区域：进度条上、行的左右内边距、标题行取到的都是这一行的名称按钮", hit is not None and hit["onBar"] and hit["onRight"] and hit["onLeft"] and hit["onTitle"], str(hit))
    edit_box = tid(page, "budget-total-edit").bounding_box()
    run.check("总览卡的「编辑」点击区域 ≥ 44 × 44", edit_box["width"] >= 44 and edit_box["height"] >= 44, str(edit_box))
    pill = tid(page, "budget-ledger-pill").bounding_box()
    period = tid(page, "budget-period").bounding_box()
    run.check("筛选行：月份胶囊在左、账本胶囊在右，同一行，高度 ≥ 44，没有横向溢出",
              period["x"] < pill["x"] and abs(period["y"] - pill["y"]) < 2 and pill["height"] >= 44 and period["height"] >= 44
              and page.evaluate("() => document.documentElement.scrollWidth - document.documentElement.clientWidth") <= 0, f"{period} {pill}")
    glass = page.evaluate(GLASS_LAYERS_JS)
    run.check("整屏玻璃层 ≤ 3（预算页的卡片 / 行 / 提示条都不是玻璃）", glass <= 3, str(glass))
    shot(page, "budgets-all-ledgers")


# ---------------------------------------------------------------------------------------------- C. 账本筛选


def check_ledger_filter(run: TestRun, page, d: Data) -> None:
    pick_ledger_filter(page, d.l1, year=d.year, month=d.month)
    snap = snapshot(page)
    ov = snap["overview"]
    pct = shown_percent(d.used_l1, 5000)
    run.check("切到账本 1：胶囊显示账本名，总览用该账本自己的总预算——标题「账本 · 总预算」、额度 5,000、已用 2,860",
              snap["pill"] == d.l1_name and ov is not None and ov["title"] == f"{d.l1_name} · 总预算" and ov["amount"] == money(5000)
              and ov["used"] == money(d.used_l1) and ov["remain"] == money(5000 - d.used_l1) and ov["pct"] == f"{pct}%", f"{snap['pill']} {ov}")
    run.check("账本 1 的「日均可用」用它自己的总预算剩余额度", snap["daily"] is not None and snap["daily"]["text"] == daily_text(5000 - d.used_l1, d.now), str(snap["daily"]))
    other = snap["other"]
    run.check("总览之外的「全部账本」总预算单列在「其他总预算」分组：名称「全部账本 · 总预算」、「已用 / 额度」",
              len(other) == 1 and other[0]["name"] == "全部账本 · 总预算" and other[0]["tag"] == f"{compact(d.used_all)} / {compact(d.total)}", str(other))
    run.check("「其他总预算」分组有标题", tid(page, "budget-other-totals").locator(".ft-group__header").inner_text().strip() == "其他总预算")
    names = [c["name"] for c in snap["categories"]]
    run.check("账本 1 的分类行 = 该账本的分类预算 + 全部账本的分类预算；已用相同按分类名、账本（全部账本在前）排",
              names == ["餐饮美食", f"{d.l1_name} · 餐饮美食", "交通出行", "购物消费"], str(names))
    ledger_row = row_named(snap["categories"], f"{d.l1_name} · 餐饮美食")
    run.check("账本级分类预算只统计该账本的账单：餐饮 2360 / 800 → 超支",
              ledger_row is not None and ledger_row["tag"] == f"{compact(2360)} / {compact(800)}" and ledger_row["state"] == "over", str(ledger_row))
    shot(page, "budgets-ledger-1")

    # 账本 2 没有自己的预算：总览退回全部账本的总预算
    pick_ledger_filter(page, d.l2, year=d.year, month=d.month)
    snap = snapshot(page)
    ov = snap["overview"]
    run.check("账本 2 没有自己的总预算：总览退回全部账本的总预算（标题「总预算」），没有「其他总预算」",
              snap["pill"] == d.l2_name and ov is not None and ov["title"] == "总预算" and ov["amount"] == money(d.total) and snap["other"] == [], f"{snap['pill']} {ov} {snap['other']}")
    run.check("账本 2 的分类行只有全部账本的 3 条（账本 1 的预算不出现在这里）", [c["name"] for c in snap["categories"]] == ["餐饮美食", "交通出行", "购物消费"], str([c["name"] for c in snap["categories"]]))

    # 菜单：选中项带对勾，全部账本也在里面
    tid(page, "budget-ledger-pill").click()
    menu = tid(page, "budget-ledger-sheet")
    menu.wait_for(state="visible")
    wait_animations_settled(page)
    options = page.evaluate(
        """() => [...document.querySelectorAll('[data-testid="budget-ledger-sheet"] [data-testid="budget-ledger-all"], [data-testid="budget-ledger-sheet"] [data-testid="budget-ledger-option"]')]
          .map((o) => ({ text: o.textContent.trim(), current: o.getAttribute('aria-current'), hasCheck: !!o.querySelector('.mdi-check') }))"""
    )
    run.check("账本菜单：全部账本 + 两个账本，当前选中的（账本 2）带 aria-current 与对勾",
              [o["text"] for o in options] == ["全部账本", d.l1_name, d.l2_name] and [o["current"] for o in options] == [None, None, "true"] and [o["hasCheck"] for o in options] == [False, False, True], str(options))
    shot(page, "budgets-ledger-menu")
    tid(page, "budget-ledger-all").click()
    menu.wait_for(state="detached")
    wait_loaded(page, year=d.year, month=d.month)
    run.check("选回「全部账本」：胶囊文字恢复，总览回到全部账本的总预算", snapshot(page)["pill"] == "全部账本" and snapshot(page)["overview"]["title"] == "总预算")


# ---------------------------------------------------------------------------------------------- D. 月份切换


def check_month_switch(run: TestRun, page, d: Data) -> None:
    tid(page, "budget-period-prev").click()
    wait_loaded(page, year=d.prev_year, month=d.prev_month)
    snap = snapshot(page)
    ov = snap["overview"]
    run.check("上一月：月份胶囊更新，显示上月全部账本的总预算（额度 3,000、已用 999——只统计上月的账单）",
              snap["period"] == month_label(d.prev_year, d.prev_month) and ov is not None and ov["amount"] == money(3000) and ov["used"] == money(999), f"{snap['period']} {ov}")
    run.check("非当前月份没有「日均可用」", snap["daily"] is None)
    run.check("上月没有分类预算：分类分组里只有「新建分类预算」行", snap["categories"] == [] and snap["addRow"])

    # 表单沿用页面当前月份
    open_sheet(page, "budget-add")
    st = sheet_state(page)
    run.check("在上月视图点「＋」：表单的月份是上月；已有总预算所以默认「分类预算」", st["period"] == month_label(d.prev_year, d.prev_month) and st["scope"] == "category", str(st))
    close_sheet(page)

    tid(page, "budget-period-next").click()
    wait_loaded(page, year=d.year, month=d.month)
    tid(page, "budget-period-next").click()
    wait_loaded(page, year=d.next_year, month=d.next_month)
    snap = snapshot(page)
    run.check("下个月没有预算：空状态；日均可用不显示；胶囊是下个月", snap["state"] == "empty" and snap["period"] == month_label(d.next_year, d.next_month) and snap["daily"] is None, str(snap["state"]))
    run.check("下个月的空状态仍然可以新建（主按钮）", tid(page, "budget-empty-add").is_visible())
    shot(page, "budgets-next-month-empty")
    tid(page, "budget-period-prev").click()
    wait_loaded(page, year=d.year, month=d.month)
    run.check("切回本月：总预算与分类行恢复", snapshot(page)["overview"] is not None and len(snapshot(page)["categories"]) == 3)


# ---------------------------------------------------------------------------------------------- E. 新建分类预算 + 校验 + 重复


def check_create_category(run: TestRun, page, db: LocalDb, user_id: str, d: Data) -> None:
    before = len(db.budgets_of_user(user_id))
    open_sheet(page, "budget-add")
    st = sheet_state(page)
    expense_categories = db.visible_category_count(user_id, BILL_TYPE_EXPENSE)
    run.check(f"已有总预算时「＋」默认「分类预算」，出现账本一栏（全部账本 + 两个账本，默认全部账本）与分类一栏（{expense_categories} 个支出分类，都没选）",
              st["scope"] == "category" and st["segPressed"] == ["category"] and st["ledgerSection"] and st["anyLedger"] is True
              and [c["label"] for c in st["ledgers"]] == [d.l1_name, d.l2_name] and not any(c["selected"] for c in st["ledgers"])
              and st["categorySection"] and len(st["categories"]) == expense_categories >= 10 and not any(c["selected"] for c in st["categories"]), str(st))
    shot(page, "budget-sheet-category")

    # --- 校验：没敲金额点保存（置灰但可点）→ 缺金额；敲了金额没选分类 → 缺分类；选了分类提示消失
    tid(page, "budget-save").click(force=True)  # 置灰的保存键（aria-disabled）Playwright 视为不可点，force 跳过可操作性检查
    sheet(page).get_by_test_id("budget-form-error").wait_for(state="visible")
    st = sheet_state(page)
    run.check("没敲金额点保存：表单内提示「请输入预算金额」（role=alert），Sheet 保持打开，没落库",
              st["error"] == "请输入预算金额" and st["errorRole"] == "alert" and len(db.budgets_of_user(user_id)) == before, str(st))
    press_keys(page, "600")
    run.check("继续敲金额：提示消失", wait_amount(page, "600") and sheet_state(page)["error"] is None)
    tid(page, "budget-save").click(force=True)
    sheet(page).get_by_test_id("budget-form-error").wait_for(state="visible")
    run.check("敲了金额没选分类点保存：提示「请选择分类」", sheet_state(page)["error"] == "请选择分类", str(sheet_state(page)))
    choose_category(page, "教育培训")
    run.check("选了分类：提示消失，保存键点亮", sheet_state(page)["error"] is None and sheet_state(page)["saveDisabled"] is None)

    # --- 分段来回切换不丢金额
    choose_scope(page, "总预算")
    st = sheet_state(page)
    run.check("切到「总预算」：分类一栏收起，金额保留", st["scope"] == "total" and not st["categorySection"] and st["amount"] == "600", str(st))
    choose_scope(page, "分类预算")
    run.check("切回「分类预算」：金额保留，之前选的分类仍选中", sheet_state(page)["amount"] == "600"
              and [c["label"] for c in sheet_state(page)["categories"] if c["selected"]] == ["教育培训"], str(sheet_state(page)))

    # --- 保存（全部账本）
    submit_and_wait_closed(page)
    wait_toast(page, "预算创建成功")
    page.locator('[data-testid="budget-name"]').filter(has_text=re.compile(r"^教育培训$")).wait_for(state="visible")
    rows = [b for b in db.budgets_of_user(user_id) if b["CategoryId"] == db.category_id_by_name("教育培训")]
    created = rows[0] if len(rows) == 1 else None
    run.check("新建分类预算落库：本月、金额 600、全部账本、分类 = 教育培训",
              created is not None and (created["Year"], created["Month"]) == (d.year, d.month) and budget_amount(created) == Decimal("600") and created["LedgerId"] is None, str(rows))
    row = row_named(snapshot(page)["categories"], "教育培训")
    run.check("新建后列表里出现「教育培训」：未使用 → 剩余 ¥600.00，状态正常", row is not None and row["remain"] == "剩余 ¥600.00" and row["state"] == "normal" and row["tag"] == "¥0 / ¥600", str(row))
    wait_toasts_gone(page)

    # --- 重复：同月、同范围（全部账本）、同分类 → 服务层拦下，提示留在表单里
    count = len(db.budgets_of_user(user_id))
    open_sheet(page, "budget-add")
    press_keys(page, "900")
    choose_category(page, "教育培训")
    submit(page)
    sheet(page).get_by_test_id("budget-form-error").wait_for(state="visible")
    st = sheet_state(page)
    run.check("重复预算：表单内提示「该月份已存在相同的预算，请直接编辑」，Sheet 保持打开，没落库，金额与选择都还在",
              st["error"] == "该月份已存在相同的预算，请直接编辑" and sheet(page).is_visible() and len(db.budgets_of_user(user_id)) == count
              and st["amount"] == "900" and [c["label"] for c in st["categories"] if c["selected"]] == ["教育培训"], str(st))
    run.check("重复提示没有变成 Toast（Toast 只留给意外错误）", toast(page, "该月份已存在相同的预算").count() == 0)
    shot(page, "budget-sheet-duplicate")

    # --- 换账本再保存：成功，页面跳到该账本
    choose_ledger(page, d.l2_name)
    run.check("改选账本后提示清除", sheet_state(page)["error"] is None)
    submit_and_wait_closed(page)
    wait_toast(page, "预算创建成功")
    wait_loaded(page, year=d.year, month=d.month, ledger_id=d.l2)
    snap = snapshot(page)
    names = [c["name"] for c in snap["categories"]]
    run.check("账本级预算保存后页面跳到该账本视图（胶囊 = 账本 2），列表里有「账本 2 · 教育培训」",
              snap["pill"] == d.l2_name and f"{d.l2_name} · 教育培训" in names and "教育培训" in names, f"{snap['pill']} {names}")
    created2 = [b for b in db.budgets_of_user(user_id) if b["CategoryId"] == db.category_id_by_name("教育培训") and b["LedgerId"]]
    run.check("落库：账本 2、金额 900、分类 = 教育培训", len(created2) == 1 and created2[0]["LedgerId"].upper() == d.l2.upper() and budget_amount(created2[0]) == Decimal("900"), str(created2))
    pick_ledger_filter(page, None, year=d.year, month=d.month)
    wait_toasts_gone(page)


# ---------------------------------------------------------------------------------------------- F. 编辑


def check_edit(run: TestRun, page, db: LocalDb, user_id: str, d: Data) -> None:
    # --- 点按整行 → 编辑 Sheet（预填）
    traffic = next(b for b in db.budgets_of_user(user_id) if b["CategoryId"] == db.category_id_by_name("交通出行") and b["LedgerId"] is None and (b["Year"], b["Month"]) == (d.year, d.month))
    open_edit_by_name(page, "交通出行")
    st = sheet_state(page)
    run.check("编辑 Sheet：标题「编辑预算」、模式 edit、分类预算一段、金额预填 1500、月份与账本（全部账本）预填、分类「交通出行」选中、有「删除预算」",
              st["title"] == "编辑预算" and st["mode"] == "edit" and st["scope"] == "category" and st["amount"] == "1500" and st["shown"] == "1,500"
              and st["period"] == month_label(d.year, d.month) and st["anyLedger"] is True
              and [c["label"] for c in st["categories"] if c["selected"]] == ["交通出行"] and st["hasDelete"], str(st))
    shot(page, "budget-sheet-edit")

    # --- 改额度保存
    clear_amount(page)
    run.check("退格清空后保存键置灰", sheet_state(page)["empty"] == "true" and sheet_state(page)["saveDisabled"] == "true")
    press_keys(page, "1800")
    run.check("敲 1800", wait_amount(page, "1800"))
    submit_and_wait_closed(page)
    wait_toast(page, "预算更新成功")
    page.wait_for_function(
        "() => [...document.querySelectorAll('[data-testid=\"budget-row\"]')].some((r) => r.querySelector('[data-testid=\"budget-tag\"]')?.textContent.trim() === '¥1,260 / ¥1,800')"
    )
    after = db.budget_row(traffic["Id"])
    row = row_named(snapshot(page)["categories"], "交通出行")
    run.check("改额度：落库 1800，同一条预算（没有新增）；行上「¥1,260 / ¥1,800」「剩余 ¥540.00」状态变正常",
              budget_amount(after) == Decimal("1800") and after["IsDeleted"] == 0 and row is not None and row["remain"] == remain_text(1260, 1800) and row["state"] == "normal", f"{after} {row}")
    wait_toasts_gone(page)

    # --- 左滑「编辑」→ 同一个 Sheet；取消不落库；再次打开是干净的
    swipe_row_open(page, "购物消费")
    tap(page, page.locator('[data-testid="budget-swipe"][data-open="true"]').get_by_test_id("swipe-edit"))
    sheet(page).wait_for(state="visible")
    wait_animations_settled(page)
    st = sheet_state(page)
    run.check("左滑「编辑」进入同一个编辑 Sheet：金额预填 3000、分类「购物消费」选中", st["mode"] == "edit" and st["amount"] == "3000" and [c["label"] for c in st["categories"] if c["selected"]] == ["购物消费"], str(st))
    press_key(page, "backspace")
    run.check("在表单里改动但不保存", wait_amount(page, "300"))
    close_sheet(page)
    shopping = next(b for b in db.budgets_of_user(user_id) if b["CategoryId"] == db.category_id_by_name("购物消费") and b["LedgerId"] is None and (b["Year"], b["Month"]) == (d.year, d.month))
    run.check("点 ✕ 取消：不落库（购物消费仍是 3000）", budget_amount(shopping) == Decimal("3000"), str(shopping))
    open_edit_by_name(page, "购物消费")
    run.check("再次打开：金额回到库里的 3000（没有保留上次取消时的草稿）", sheet_state(page)["amount"] == "3000", str(sheet_state(page)))
    close_sheet(page)

    # --- 总览「编辑」→ 总预算的编辑 Sheet
    total = next(b for b in db.budgets_of_user(user_id) if b["CategoryId"] is None and b["LedgerId"] is None and (b["Year"], b["Month"]) == (d.year, d.month))
    open_sheet(page, "budget-total-edit")
    st = sheet_state(page)
    run.check("总览「编辑」：编辑 Sheet 是「总预算」一段，金额预填 12000，没有分类一栏，有「删除预算」",
              st["mode"] == "edit" and st["scope"] == "total" and st["amount"] == "12000" and not st["categorySection"] and st["hasDelete"], str(st))
    clear_amount(page)
    press_keys(page, "9000")
    run.check("敲 9000", wait_amount(page, "9000"))
    submit_and_wait_closed(page)
    wait_toast(page, "预算更新成功")
    page.wait_for_function("() => document.querySelector('[data-testid=\"budget-total-amount\"]')?.textContent.trim() === '¥9,000.00'")
    ov = snapshot(page)["overview"]
    pct = shown_percent(d.used_all, 9000)
    run.check(f"改总预算：落库 9000；总览额度 ¥9,000.00，使用率 {pct}%，剩余 {money(9000 - d.used_all)}",
              budget_amount(db.budget_row(total["Id"])) == Decimal("9000") and ov["amount"] == money(9000) and ov["pct"] == f"{pct}%" and ov["remain"] == money(9000 - d.used_all), str(ov))
    run.check("日均可用随总预算的剩余额度更新", snapshot(page)["daily"]["text"] == daily_text(9000 - d.used_all, d.now), str(snapshot(page)["daily"]))
    d.total = Decimal("9000")
    wait_toasts_gone(page)

    # --- 编辑成重复预算：全部账本的「教育培训」改选账本 2，撞上账本 2 已有的「教育培训」
    edu = next(b for b in db.budgets_of_user(user_id) if b["CategoryId"] == db.category_id_by_name("教育培训") and b["LedgerId"] is None)
    open_edit_by_name(page, "教育培训")
    choose_ledger(page, d.l2_name)
    submit(page)
    sheet(page).get_by_test_id("budget-form-error").wait_for(state="visible")
    run.check("编辑成重复预算：同样被拦下，提示留在表单里，库里不变",
              sheet_state(page)["error"] == "该月份已存在相同的预算，请直接编辑" and db.budget_row(edu["Id"])["LedgerId"] is None, str(sheet_state(page)))
    close_sheet(page)
    run.check("取消后教育培训仍是全部账本的 600", db.budget_row(edu["Id"])["LedgerId"] is None and budget_amount(db.budget_row(edu["Id"])) == Decimal("600"))


# ---------------------------------------------------------------------------------------------- G. 删除


def check_delete(run: TestRun, page, db: LocalDb, user_id: str, d: Data) -> None:
    shopping = next(b for b in db.budgets_of_user(user_id) if b["CategoryId"] == db.category_id_by_name("购物消费") and b["LedgerId"] is None and (b["Year"], b["Month"]) == (d.year, d.month))

    # --- 左滑「删除」→ Alert → 取消
    swipe_row_open(page, "购物消费")
    row = page.locator('[data-testid="budget-swipe"][data-open="true"]')
    tap(page, row.get_by_test_id("swipe-delete"))
    alert = tid(page, "budget-delete-alert")
    alert.wait_for(state="visible")
    run.check("删除确认 Alert：标题带名称「删除预算「购物消费」？」，说明已记账单不受影响，alertdialog 语义",
              tid(page, "alert-title").inner_text().strip() == "删除预算「购物消费」？" and "已记的账单不受影响" in tid(page, "alert-message").inner_text()
              and alert.get_attribute("role") == "alertdialog", tid(page, "alert-title").inner_text())
    tid(page, "alert-cancel").click()
    alert.wait_for(state="detached")
    run.check("取消删除：预算还在列表里，库里没删", db.budget_row(shopping["Id"])["IsDeleted"] == 0 and row_named(snapshot(page)["categories"], "购物消费") is not None)

    # --- 左滑「删除」→ 确认：软删除
    swipe_row_open(page, "购物消费")
    row = page.locator('[data-testid="budget-swipe"][data-open="true"]')
    tap(page, row.get_by_test_id("swipe-delete"))
    alert.wait_for(state="visible")
    tid(page, "alert-confirm").click()
    wait_toast(page, "预算删除成功")
    alert.wait_for(state="detached")
    page.wait_for_function("() => ![...document.querySelectorAll('[data-testid=\"budget-name\"]')].some((n) => n.textContent.trim() === '购物消费')")
    gone = db.budget_row(shopping["Id"])
    run.check("确认删除：软删除（IsDeleted = 1，记录还在），列表里消失", gone is not None and gone["IsDeleted"] == 1 and row_named(snapshot(page)["categories"], "购物消费") is None, str(gone))
    wait_toasts_gone(page)

    # --- Sheet 里的「删除预算」：Alert 盖在 Sheet 之上
    edu = next(b for b in db.budgets_of_user(user_id) if b["CategoryId"] == db.category_id_by_name("教育培训") and b["LedgerId"] is None)
    open_edit_by_name(page, "教育培训")
    sheet(page).get_by_test_id("budget-delete-cell").click()
    alert.wait_for(state="visible")
    wait_animations_settled(page)
    covered = page.evaluate(
        """() => { const a = document.querySelector('[data-testid="budget-delete-alert"]').getBoundingClientRect();
           const el = document.elementFromPoint(a.left + a.width / 2, a.top + a.height / 2);
           return !!el && !!el.closest('[data-testid="budget-delete-alert"]'); }"""
    )
    run.check("Sheet 里点「删除预算」：确认 Alert 盖在 Sheet 之上，标题带名称", covered and tid(page, "alert-title").inner_text().strip() == "删除预算「教育培训」？", tid(page, "alert-title").inner_text())
    shot(page, "budget-delete-from-sheet")
    tid(page, "alert-confirm").click()
    wait_toast(page, "预算删除成功")
    alert.wait_for(state="detached")
    sheet(page).wait_for(state="detached")
    run.check("确认后：Sheet 一并收起，预算软删除，列表里消失",
              db.budget_row(edu["Id"])["IsDeleted"] == 1 and row_named(snapshot(page)["categories"], "教育培训") is None)
    wait_toasts_gone(page)

    # --- 删掉总预算：总览与日均可用消失，出现「设置总预算」引导；「＋」默认总预算
    total = next(b for b in db.budgets_of_user(user_id) if b["CategoryId"] is None and b["LedgerId"] is None and (b["Year"], b["Month"]) == (d.year, d.month))
    open_sheet(page, "budget-total-edit")
    sheet(page).get_by_test_id("budget-delete-cell").click()
    alert.wait_for(state="visible")
    run.check("删总预算的确认标题用「全部账本 · 总预算」", tid(page, "alert-title").inner_text().strip() == "删除预算「全部账本 · 总预算」？", tid(page, "alert-title").inner_text())
    tid(page, "alert-confirm").click()
    wait_toast(page, "预算删除成功")
    alert.wait_for(state="detached")
    sheet(page).wait_for(state="detached")
    page.wait_for_function("() => !document.querySelector('[data-testid=\"budget-overview\"]')")
    snap = snapshot(page)
    run.check("没有总预算（还有分类预算）：不显示总览与日均可用，出现「设置总预算」引导，分类行仍在",
              db.budget_row(total["Id"])["IsDeleted"] == 1 and snap["overview"] is None and snap["daily"] is None and snap["guide"] and len(snap["categories"]) == 2, str(snap))
    run.check("引导行的文字：「设置总预算」+ 说明", "设置总预算" in tid(page, "budget-total-guide").inner_text() and "给这个月定一个总额度" in tid(page, "budget-total-guide").inner_text())
    shot(page, "budgets-no-total")
    wait_toasts_gone(page)

    # --- 引导 → 新建总预算 Sheet；「＋」在没有总预算时同样默认总预算
    open_sheet(page, "budget-total-guide")
    run.check("点引导：新建 Sheet 默认「总预算」一段", sheet_state(page)["mode"] == "create" and sheet_state(page)["scope"] == "total", str(sheet_state(page)))
    close_sheet(page)
    open_sheet(page, "budget-add")
    run.check("没有总预算时点「＋」：默认「总预算」", sheet_state(page)["scope"] == "total", str(sheet_state(page)))
    press_keys(page, "10000")
    wait_amount(page, "10000")
    submit_and_wait_closed(page)
    wait_toast(page, "预算创建成功")
    wait_state(page, "() => !!document.querySelector('[data-testid=\"budget-overview\"]')")
    run.check("重新设置总预算：总览回来，引导消失", snapshot(page)["overview"]["amount"] == money(10000) and not snapshot(page)["guide"])
    wait_toasts_gone(page)


# ---------------------------------------------------------------------------------------------- H. 保存后跳转


def check_save_jumps_to_month(run: TestRun, page, db: LocalDb, user_id: str, d: Data) -> None:
    open_sheet(page, "budget-add")
    tid(page, "budget-form-period-prev").click()
    wait_state(page, f"() => document.querySelector('[data-testid=\"budget-form-period-label\"]')?.textContent.trim() === '{month_label(d.prev_year, d.prev_month)}'")
    press_keys(page, "700")
    choose_category(page, "餐饮美食")
    submit_and_wait_closed(page)
    wait_toast(page, "预算创建成功")
    wait_loaded(page, year=d.prev_year, month=d.prev_month)
    snap = snapshot(page)
    saved = [b for b in db.budgets_of_user(user_id) if (b["Year"], b["Month"]) == (d.prev_year, d.prev_month) and b["CategoryId"] == db.category_id_by_name("餐饮美食")]
    run.check("表单里改了月份保存后，页面跳到保存的月份（上月），列表里能看到刚存的「餐饮美食」；落库的年月是上月",
              snap["period"] == month_label(d.prev_year, d.prev_month) and row_named(snap["categories"], "餐饮美食") is not None
              and len(saved) == 1 and budget_amount(saved[0]) == Decimal("700"), f"{snap['period']} {saved}")
    run.check("上月的餐饮预算 700，已用 999（上月账单）→ 超支 ¥299.00", row_named(snap["categories"], "餐饮美食")["remain"] == remain_text(999, 700), str(row_named(snap["categories"], "餐饮美食")))
    tid(page, "budget-period-next").click()
    wait_loaded(page, year=d.year, month=d.month)
    wait_toasts_gone(page)


# ---------------------------------------------------------------------------------------------- I. 总览的各种状态（单独会话）


def check_overview_states(run: TestRun, now: datetime) -> None:
    db = LocalDb()
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        warn_ledger_name, over_ledger_name, exact_ledger_name = unique_name("警示账本"), unique_name("超支账本"), unique_name("用完账本")
        warn, over, exact = (db.seed_ledger(user_id, n, sort_order=i) for i, n in enumerate([warn_ledger_name, over_ledger_name, exact_ledger_name], start=1))
        cat = db.category_id_by_name("餐饮美食")
        # 全部账本的总预算统计所有账本的支出：8713.50 + 6200 + 3000 = 17913.50，总预算 20000 → 89.6%，接近上限
        db.seed_budget(user_id, amount=20000)
        db.seed_bill(user_id, amount=8713.5, ledger_id=warn, category_id=cat, note=unique_name("警示"))
        db.seed_budget(user_id, amount=5000, ledger_id=over)  # 账本级总预算只统计该账本：6200 / 5000 = 124%，超支
        db.seed_bill(user_id, amount=6200, ledger_id=over, category_id=cat, note=unique_name("超支"))
        db.seed_budget(user_id, amount=3000, ledger_id=exact)  # 3000 / 3000 = 100%，刚好用完
        db.seed_bill(user_id, amount=3000, ledger_id=exact, category_id=cat, note=unique_name("用完"))
        year, month = now.year, now.month
        all_used = Decimal("17913.5")

        open_page(page, year=year, month=month)
        snap = snapshot(page)
        ov = snap["overview"]
        pct = shown_percent(all_used, 20000)
        run.check(f"接近上限（≥ 80%，{pct}%）：总览状态 warning、环是警示橙、环中心「{pct}%」+「已使用」、读屏「接近上限」",
                  ov["state"] == "warning" and ov["ringTone"] == "warning" and ov["pct"] == f"{pct}%" and ov["caption"] == "已使用" and "接近上限" in ov["ringText"], str(ov))
        run.check("接近上限仍有剩余额度：显示「日均可用」",
                  snap["daily"] is not None and snap["daily"]["text"] == daily_text(20000 - all_used, now), f"{snap['daily']} 期望 {daily_text(20000 - all_used, now)}")
        arc_color = page.evaluate("() => getComputedStyle(document.querySelector('[data-testid=\"budget-ring\"] .ft-ring__arc')).stroke")
        run.check("警示环的颜色是警示橙令牌", arc_color == token_color(page, "--ft-warning"), arc_color)

        pick_ledger_filter(page, over, year=year, month=month)
        snap = snapshot(page)
        ov = snap["overview"]
        run.check("超支（124%）：总览状态 over、环是危险红且撑满整圈、环中心「124%」+「已超支」、读屏「超支」",
                  ov["state"] == "over" and ov["ringTone"] == "danger" and ov["pct"] == "124%" and ov["caption"] == "已超支" and "超支" in ov["ringText"] and ov["ringPercent"] == "100", str(ov))
        run.check("超支：标签从「剩余」变「超支」，金额是超出的部分（红色），没有「日均可用」",
                  ov["remainLabel"] == "超支" and ov["remain"] == money(1200) and snap["daily"] is None, str(snap["daily"]))
        remain_color = page.evaluate("() => getComputedStyle(document.querySelector('[data-testid=\"budget-remain\"]')).color")
        run.check("超支的金额用深红文字色令牌（对比度由 DesignTokensTests 保证）", remain_color == token_color(page, "--ft-danger-text"), remain_color)
        shot(page, "budgets-over")

        pick_ledger_filter(page, exact, year=year, month=month)
        snap = snapshot(page)
        ov = snap["overview"]
        run.check("刚好用完（100%）：环中心「已用完」，剩余 ¥0.00，没有「日均可用」",
                  ov["caption"] == "已用完" and ov["pct"] == "100%" and ov["remain"] == money(0) and ov["remainLabel"] == "剩余" and snap["daily"] is None, str(ov))

        pick_ledger_filter(page, warn, year=year, month=month)
        pick_ledger_filter(page, None, year=year, month=month)
        run.check("切回全部账本仍是接近上限的总览", snapshot(page)["overview"]["state"] == "warning")
        shot(page, "budgets-warning")
        run.check_clean(s.errors, "I 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- J. 窄屏 320


def check_narrow(run: TestRun, now: datetime) -> None:
    db = LocalDb()
    with session(viewport={"width": 320, "height": 640}) as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger = db.seed_ledger(user_id, unique_name("窄屏账本"))
        db.seed_budget(user_id, amount=12345678.9)
        db.seed_budget(user_id, amount=2000, category_name="餐饮美食")
        db.seed_bill(user_id, amount=99999999.99, ledger_id=ledger, category_id=db.category_id_by_name("餐饮美食"), note=unique_name("巨额"))
        open_page(page, year=now.year, month=now.month)
        metrics = page.evaluate(
            """() => {
              const card = document.querySelector('[data-testid="budget-overview"]').getBoundingClientRect();
              const ring = document.querySelector('[data-testid="budget-ring"]').getBoundingClientRect();
              const info = document.querySelector('.ft-budgetcard__info').getBoundingClientRect();
              const overflow = [...document.querySelectorAll('.ft-budgetcard *')].filter((el) => el.getBoundingClientRect().right > card.right + 0.5 || el.getBoundingClientRect().left < card.left - 0.5);
              const pill = document.querySelector('[data-testid="budget-ledger-pill"]').getBoundingClientRect();
              const period = document.querySelector('[data-testid="budget-period"]').getBoundingClientRect();
              return {
                scrollW: document.documentElement.scrollWidth, clientW: document.documentElement.clientWidth,
                stacked: info.top >= ring.bottom - 1, ringCentered: Math.abs((ring.left + ring.right) / 2 - (card.left + card.right) / 2) < 2,
                overflowCount: overflow.length, overflowTags: overflow.map((e) => e.className).slice(0, 5),
                pillBelowPeriod: pill.top >= period.bottom - 1, pillTextVisible: document.querySelector('[data-testid="budget-ledger-pill"]').getBoundingClientRect().width > 80,
                amountFont: getComputedStyle(document.querySelector('[data-testid="budget-total-amount"]')).fontSize,
                pctFont: getComputedStyle(document.querySelector('[data-testid="budget-ring-percent"]')).fontSize,
                ringW: ring.width,
              };
            }"""
        )
        run.check("320 宽：没有横向溢出", metrics["scrollW"] <= metrics["clientW"], str(metrics))
        run.check("320 宽：总览卡改成上下排——环在上居中、额度在下", metrics["stacked"] and metrics["ringCentered"], str(metrics))
        run.check("320 宽：八位数的额度 / 已用 / 超支金额都在卡片之内（没有元素冲出卡片）", metrics["overflowCount"] == 0, str(metrics))
        run.check("320 宽：筛选行折行，账本胶囊放到月份胶囊下面，文字完整", metrics["pillBelowPeriod"] and metrics["pillTextVisible"], str(metrics))
        run.check("320 宽：环与环中心文字随屏宽收缩（环 ≤ 33vw，百分比字号 ≥ 20）", metrics["ringW"] <= 320 * 0.33 + 1 and float(metrics["pctFont"][:-2]) >= 20, str(metrics))
        run.check("长额度（≥ 12 个字符）用小一档的字号 18", metrics["amountFont"] == "18px", metrics["amountFont"])
        shot(page, "budgets-320")
        run.check_clean(s.errors, "J 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- K. 降级


def check_reduced_motion(run: TestRun, now: datetime) -> None:
    db = LocalDb()
    with session(reduced_motion=True) as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger = db.seed_ledger(user_id, unique_name("降级账本"))
        db.seed_budget(user_id, amount=1000)
        db.seed_bill(user_id, amount=400, ledger_id=ledger, note=unique_name("降级"))  # 有使用率，环上才有进度弧
        open_page(page, year=now.year, month=now.month)
        transition = page.evaluate("() => { const a = document.querySelector('[data-testid=\"budget-ring\"] .ft-ring__arc'); return a ? getComputedStyle(a).transitionDuration : null; }")
        run.check("减少动态效果：总览正常渲染，环上有进度弧", snapshot(page)["overview"] is not None and transition is not None, str(transition))
        run.check("减少动态效果：进度环弧长不做过渡（transition-duration 0s）", transition == "0s", str(transition))
        run.check_clean(s.errors, "K 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- L. 记一笔后进度更新（单独会话）


def check_bill_updates_progress(run: TestRun, now: datetime) -> None:
    """经「记一笔」Sheet 真实记几笔支出，再进预算页：已用 / 使用率 / 状态随之更新（预算由账单实时算出，页面不缓存、不用手动刷新）。"""
    db = LocalDb()
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger = unique_name("进度账本")
        db.seed_ledger(user_id, ledger)
        db.seed_budget(user_id, amount=1000)
        db.seed_budget(user_id, amount=500, category_name="餐饮美食")
        year, month = now.year, now.month

        open_page(page, year=year, month=month)
        snap = snapshot(page)
        food = row_named(snap["categories"], "餐饮美食")
        run.check("记账之前：总览 0%、已用 ¥0.00、剩余 ¥1,000.00；「餐饮美食」行剩余 ¥500.00、进度 0",
                  snap["overview"]["pct"] == "0%" and snap["overview"]["used"] == money(0) and snap["overview"]["remain"] == money(1000)
                  and food is not None and food["remain"] == remain_text(0, 500) and food["now"] == "0", f"{snap['overview']} {food}")

        # 经界面记一笔 300（餐饮美食）：回到首页（Tab 根页面）→ 记一笔 Sheet → 保存
        open_app(page, "/")
        save_bill_via_sheet(page, amount=300, ledger=ledger, category="餐饮美食", note=unique_name("进度一"))
        open_page(page, year=year, month=month)
        snap = snapshot(page)
        ov, food = snap["overview"], row_named(snap["categories"], "餐饮美食")
        run.check("记一笔 300 后：总览已用 ¥300.00、30%、正常，剩余 ¥700.00",
                  ov["used"] == money(300) and ov["pct"] == "30%" and ov["state"] == "normal" and ov["remain"] == money(700), str(ov))
        run.check("记一笔 300 后：「餐饮美食」行「¥300 / ¥500」、60%、正常、剩余 ¥200.00",
                  food is not None and food["tag"] == f"{compact(300)} / {compact(500)}" and food["percent"] == "60"
                  and food["state"] == "normal" and food["remain"] == remain_text(300, 500), str(food))
        run.check("「日均可用」跟着剩余额度变（700 ÷ 本月剩余天数）", snap["daily"] is not None and snap["daily"]["text"] == daily_text(700, now), f"{snap['daily']} 期望 {daily_text(700, now)}")

        # 再记 250（餐饮美食）：分类预算 550 / 500 超支，总预算 550 / 1000 仍然正常
        open_app(page, "/")
        save_bill_via_sheet(page, amount=250, ledger=ledger, category="餐饮美食", note=unique_name("进度二"))
        open_page(page, year=year, month=month)
        snap = snapshot(page)
        ov, food = snap["overview"], row_named(snap["categories"], "餐饮美食")
        run.check("再记 250 后：「餐饮美食」行 110%、超支（红）、「超支 ¥50.00」",
                  food is not None and food["percent"] == "110" and food["state"] == "over" and food["tone"] == "danger"
                  and food["remain"] == remain_text(550, 500) and food["tag"] == f"{compact(550)} / {compact(500)}", str(food))
        run.check("分类预算超支不影响总预算：总览已用 ¥550.00、55%、正常", ov["used"] == money(550) and ov["pct"] == "55%" and ov["state"] == "normal", str(ov))

        # 记一笔没有预算的分类（交通出行）：只进总预算的已用，不会冒出新的行，也不影响「餐饮美食」那行
        open_app(page, "/")
        save_bill_via_sheet(page, amount=120, ledger=ledger, category="交通出行", note=unique_name("进度三"))
        open_page(page, year=year, month=month)
        snap = snapshot(page)
        ov, food = snap["overview"], row_named(snap["categories"], "餐饮美食")
        run.check("记一笔没有预算的分类（交通出行 120）：总览已用 ¥670.00、67%；分类预算行仍只有「餐饮美食」一行，且数字不变",
                  ov["used"] == money(670) and ov["pct"] == "67%" and len(snap["categories"]) == 1
                  and food is not None and food["tag"] == f"{compact(550)} / {compact(500)}", f"{ov} {snap['categories']}")
        run.check_clean(s.errors, "L 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- main


def main() -> int:
    require_server()
    run = TestRun("budgets-page")
    db = LocalDb()
    db.verify_schema()
    now = datetime.now()

    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)

        check_empty_and_first_budget(run, page, db, user_id, now)
        d = Data(db, user_id, now)
        check_overview_and_rows(run, page, d)
        check_ledger_filter(run, page, d)
        check_month_switch(run, page, d)
        check_create_category(run, page, db, user_id, d)
        check_edit(run, page, db, user_id, d)
        check_delete(run, page, db, user_id, d)
        check_save_jumps_to_month(run, page, db, user_id, d)

        run.check_clean(s.errors)

    check_overview_states(run, now)
    check_narrow(run, now)
    check_reduced_motion(run, now)
    check_bill_updates_progress(run, now)
    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
