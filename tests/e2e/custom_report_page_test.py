# -*- coding: utf-8 -*-
"""
E2E 测试：自定义报表页（iOS UI 重构 · 21 号 ticket）——统计 Tab 右上角进入的二级页面：
快捷区间 / 起止日期（日期选择）/ 账本 → 生成报表 → 结果（KPI、分类环比、排行、构成）→ 导出 CSV / 分享摘要。

只覆盖网页层——真机上的系统日期选择器（iOS 滚轮 / Android 日历）、系统分享面板、文件下载落到哪里（MAUI WebView 里 <a download> 是否真的存盘）
见 ticket 21 的人工验收清单。报表读的是本机库，不经过云端，所以不需要假云端。

  A. 外壳与初始状态（新用户：没有账本、没有账单）：从统计 Tab 右上角进入（二级页面、Tab 栏隐藏）、导航栏、快捷区间芯片（顺序 / 默认「本月」亮起 / 热区）、
     报表范围分组（起止日期行 = 本月 1 日 → 今天、透明 date 输入盖满整行、min / max）、没有账本时不显示账本行、固定在底部的主按钮、提示文字；
     生成空报表（生成中：骨架 + 按钮忙 → 结果：KPI 全 0、说明行、环比 / 排行 / 构成的空状态、各节顺序）；点返回回到统计 Tab
  B. 不合法输入：结束早于开始 / 清空开始 / 清空结束 / 超出可选范围——红字提示（role=alert）、「生成报表」置灰且点不动、已有结果不被清掉；改对后恢复
  C. 快捷区间与自定义日期（口径）：四个快捷区间点一下只填日期、不立刻生成（旧结果保持）；生成结果 = 区间内（含两端日、应用时区自然日）所建账单的合计；
     手动输入与快捷区间相同的日期时芯片亮起；单日 / 月初边界（00:30 算当天、前一天 23:30 不算）
  D. 结果内容：KPI（支出 / 收入 / 结余）、说明行、分类支出环比（新增 / 涨 / 跌，红 = 变多、绿 = 变少，上期有本期归零的分类取上期的名字）、
     支出 / 收入构成（圆环 + 图例）、导出 CSV（BOM、不带千分位、逗号 / 引号转义）、分享摘要（系统分享 / 取消 / 退到剪贴板 / 都不支持）、生成后改参数不改已有结果
  E. 账本筛选：账本行（全部账本 / 单个账本、对勾）、各账本的结果、「全部账本」包含没有账本的历史账单、说明行 / 导出 / 分享带账本名
  F. 沉浸式：模拟刘海（状态栏 47）+ 手势条（34）：导航栏、内容、底部主按钮都让出安全区
  G. 窄屏（320）：空闲 / 不合法 / 结果三个状态都不溢出、不重叠；每屏玻璃层 ≤ 3

可观察属性（已登记到 README）：report-page、report-content（data-state = idle｜loading｜ready｜failed、data-quick、data-generation）、
report-quick / report-quick-option（data-value、data-selected、aria-pressed）、report-range-group、report-start-row / report-start、report-end-row / report-end（data-value）、
report-ledger-row（data-value）、report-ledger-sheet / report-ledger-all / report-ledger-option（data-ledger-id、aria-current）、report-error、
report-foot / report-generate、report-idle / report-failed / report-skeleton、report-results（data-start / data-end / data-ledger / data-count）、
report-kpi（report-kpi-expense / -income / -balance，data-amount）、report-caption、
report-comparison-header / report-comparison（report-comparison-row：data-name / data-current / data-previous / data-kind / data-tone，report-delta）、
report-rank-header / report-rank（data-state）、report-{expense|income}-{header|composition|donut|total|legend|empty}、report-actions / report-export / report-share。
"""
import sys
from datetime import date, datetime, timedelta
from decimal import Decimal
from pathlib import Path

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
    open_tab,
    require_server,
    session,
    shot,
    tab_bar,
    unique_name,
    wait_animations_settled,
    wait_attr,
    wait_secondary_pages_closed,
    wait_tab_bar,
    wait_url,
)

SCREEN_W = PHONE_VIEWPORT["width"]
SCREEN_H = PHONE_VIEWPORT["height"]

RGB_DANGER_TEXT = "rgb(211, 34, 26)"  # --ft-danger-text #D3221A（支出变多）
RGB_INCOME_TEXT = "rgb(24, 133, 64)"  # --ft-income-text #188540（支出变少）
RGB_LABEL = "rgb(0, 0, 0)"  # --ft-label（持平）

QUICK_RANGES = [("this-month", "本月"), ("last-month", "上月"), ("last-30", "近30天"), ("last-90", "近90天")]

IDLE_TEXT = "选择日期范围后点击「生成报表」"
RANGE_ORDER_TEXT = "开始日期不能晚于结束日期"
START_REQUIRED_TEXT = "请选择开始日期"
END_REQUIRED_TEXT = "请选择结束日期"
OUT_OF_RANGE_TEXT = "日期需在 2000 年到 2100 年之间"

# 含逗号与引号的自建分类名：导出 CSV 必须按 RFC 4180 转义
CUSTOM_CATEGORY = '自建,"分类"'

# ---------------------------------------------------------------------------------------------- 页面状态

STATE_JS = r"""() => {
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  const all = (id) => [...document.querySelectorAll(`[data-testid="${id}"]`)];
  const text = (node) => (node ? node.textContent.replace(/\s+/g, ' ').trim() : null);
  const rect = (node) => {
    if (!node) return null;
    const r = node.getBoundingClientRect();
    return { l: r.left, r: r.right, t: r.top, b: r.bottom, w: r.width, h: r.height };
  };
  const content = q('report-content');
  if (!content) return null;
  const row = (id) => {
    const n = q(id);
    if (!n) return null;
    return {
      title: text(n.querySelector('.ft-cell__title')),
      value: text(n.querySelector('.ft-cell__value')),
      dataValue: n.getAttribute('data-value'),
      missing: n.classList.contains('ft-addbill__field--missing'),
      aria: n.getAttribute('aria-label'),
      haspopup: n.getAttribute('aria-haspopup'),
      expanded: n.getAttribute('aria-expanded'),
      box: rect(n),
    };
  };
  const input = (id) => {
    const n = q(id);
    return n ? { type: n.type, value: n.value, min: n.min, max: n.max, aria: n.getAttribute('aria-label') } : null;
  };
  const gen = q('report-generate');
  const foot = q('report-foot');
  const error = q('report-error');
  const group = q('report-range-group');
  const quick = q('report-quick');
  return {
    state: content.getAttribute('data-state'),
    quick: content.getAttribute('data-quick'),
    generation: Number(content.getAttribute('data-generation')),
    busy: content.getAttribute('aria-busy'),
    overflowX: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    quickLabel: quick ? text(quick.querySelector('.ft-report__label')) : null,
    chips: all('report-quick-option').map((b) => ({
      value: b.getAttribute('data-value'), label: text(b), pressed: b.getAttribute('aria-pressed'), selected: b.getAttribute('data-selected'), box: rect(b),
    })),
    rangeHeader: group ? text(group.querySelector('.ft-group__header')) : null,
    start: row('report-start-row'), end: row('report-end-row'), ledger: row('report-ledger-row'),
    startInput: input('report-start'), endInput: input('report-end'),
    error: error ? { text: text(error), role: error.getAttribute('role'), color: getComputedStyle(error).color, box: rect(error) } : null,
    gen: gen ? { disabled: gen.disabled, text: text(gen), busy: gen.getAttribute('aria-busy'), box: rect(gen) } : null,
    foot: foot ? { box: rect(foot), position: getComputedStyle(foot).position } : null,
    idle: text(q('report-idle')),
    failed: !!q('report-failed'),
    hasResults: !!q('report-results'),
    skeleton: !!q('report-skeleton'),
  };
}"""

RESULT_JS = r"""() => {
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  const all = (id) => [...document.querySelectorAll(`[data-testid="${id}"]`)];
  const text = (node) => (node ? node.textContent.replace(/\s+/g, ' ').trim() : null);
  const rect = (node) => {
    if (!node) return null;
    const r = node.getBoundingClientRect();
    return { l: r.left, r: r.right, t: r.top, b: r.bottom, w: r.width, h: r.height };
  };
  const results = q('report-results');
  if (!results) return null;
  const amount = (id) => { const n = q(id); return n ? n.getAttribute('data-amount') : null; };
  const comparison = q('report-comparison');
  const rows = all('report-comparison-row').map((n) => {
    const delta = n.querySelector('[data-testid="report-delta"]');
    const icon = delta ? delta.querySelector('i') : null;
    return {
      name: n.getAttribute('data-name'), current: n.getAttribute('data-current'), previous: n.getAttribute('data-previous'),
      kind: n.getAttribute('data-kind'), tone: n.getAttribute('data-tone'),
      title: text(n.querySelector('.ft-cell__title')), subtitle: text(n.querySelector('.ft-cell__subtitle')),
      delta: text(delta), deltaColor: delta ? getComputedStyle(delta).color : null, icon: icon ? icon.className : null,
      box: rect(n), main: rect(n.querySelector('.ft-cell__main')), trail: rect(n.querySelector('.ft-cell__trail')),
    };
  });
  const legend = (key) => (q(`report-${key}-legend`) ? [...q(`report-${key}-legend`).querySelectorAll('[data-testid="legend-row"]')].map((n) => ({
    name: n.getAttribute('data-name'), amount: n.getAttribute('data-amount'), percent: n.getAttribute('data-percent'), count: n.getAttribute('data-count'),
    pct: text(n.querySelector('.ft-legend__pct')), box: rect(n),
  })) : []);
  const donut = (key) => {
    const n = q(`report-${key}-donut`);
    return n ? { slices: n.getAttribute('data-slices'), arcs: [...n.querySelectorAll('[data-testid="donut-arc"]')].map((a) => a.getAttribute('data-name')) } : null;
  };
  const panel = (id) => { const n = q(id); return n ? { state: n.getAttribute('data-state'), box: rect(n), empty: text(q(id + '-empty')) } : null; };
  const kpiCells = [...document.querySelectorAll('[data-testid="report-kpi"] .ft-kpi__cell')].map((cell) => {
    const a = cell.querySelector('[data-amount]');
    return { cell: rect(cell), amount: rect(a), label: text(cell.querySelector('.ft-kpi__label')) };
  });
  return {
    start: results.getAttribute('data-start'), end: results.getAttribute('data-end'), ledger: results.getAttribute('data-ledger'), count: results.getAttribute('data-count'),
    expense: amount('report-kpi-expense'), income: amount('report-kpi-income'), balance: amount('report-kpi-balance'),
    kpiCells, caption: text(q('report-caption')),
    comparisonHeader: text(q('report-comparison-header')),
    comparisonState: comparison ? comparison.getAttribute('data-state') : null,
    comparisonEmpty: text(q('report-comparison-empty')),
    comparisonFooter: comparison ? text(comparison.querySelector('.ft-group__footer')) : null,
    rows,
    rankHeader: text(q('report-rank-header')), rank: panel('report-rank'),
    expenseHeader: text(q('report-expense-header')), incomeHeader: text(q('report-income-header')),
    expenseTotal: amount('report-expense-total'), incomeTotal: amount('report-income-total'),
    expenseDonut: donut('expense'), incomeDonut: donut('income'),
    expenseLegend: legend('expense'), incomeLegend: legend('income'),
    expenseEmpty: text(q('report-expense-empty')), incomeEmpty: text(q('report-income-empty')),
    actionsHeader: q('report-actions') ? text(q('report-actions').querySelector('.ft-group__header')) : null,
    exportRow: q('report-export') ? { title: text(q('report-export').querySelector('.ft-cell__title')), subtitle: text(q('report-export').querySelector('.ft-cell__subtitle')), disabled: q('report-export').disabled } : null,
    shareRow: q('report-share') ? { title: text(q('report-share').querySelector('.ft-cell__title')), subtitle: text(q('report-share').querySelector('.ft-cell__subtitle')), disabled: q('report-share').disabled } : null,
    overflowX: document.documentElement.scrollWidth - document.documentElement.clientWidth,
  };
}"""

# 各节从上到下的顺序：它们的 top 必须严格递增
SECTION_TOPS_JS = r"""() => ['report-kpi', 'report-caption', 'report-comparison-header', 'report-comparison', 'report-rank-header', 'report-rank',
  'report-expense-header', 'report-expense-composition', 'report-income-header', 'report-income-composition', 'report-actions']
  .map((id) => { const n = document.querySelector(`[data-testid="${id}"]`); return n ? n.getBoundingClientRect().top : null; })"""

# 导航栏的几何：标题取文字本身的范围，与返回按钮比较是否重叠
NAV_JS = """() => {
  const rect = (el) => { if (!el) return null; const r = el.getBoundingClientRect(); return { l: r.left, r: r.right, t: r.top, b: r.bottom, w: r.width, h: r.height }; };
  const textRect = (el) => { if (!el) return null; const range = document.createRange(); range.selectNodeContents(el); return rect(range); };
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  return { back: rect(q('nav-back')), title: textRect(q('nav-title')), titleText: q('nav-title') ? q('nav-title').textContent.trim() : null };
}"""

# 透明的 date 输入盖在行上：行中心那一点命中的就是该输入
HIT_ROW_JS = """(rowId) => {
  const row = document.querySelector(`[data-testid="${rowId}"]`);
  const r = row.getBoundingClientRect();
  const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
  return hit ? hit.getAttribute('data-testid') : null;
}"""

# 把页面里最近的可滚动祖先滚到底，返回「最后一节（导出与分享）」与「底部固定按钮区」的位置
SCROLL_TO_END_JS = """() => {
  let el = document.querySelector('[data-testid="report-page"]');
  while (el && el !== document.documentElement) {
    const cs = getComputedStyle(el);
    if ((cs.overflowY === 'auto' || cs.overflowY === 'scroll') && el.scrollHeight > el.clientHeight) break;
    el = el.parentElement;
  }
  const scroller = el && el !== document.documentElement ? el : document.scrollingElement;
  scroller.scrollTop = scroller.scrollHeight;
  return new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => {
    const last = document.querySelector('[data-testid="report-actions"]').getBoundingClientRect();
    const foot = document.querySelector('[data-testid="report-foot"]').getBoundingClientRect();
    resolve({ lastBottom: last.bottom, footTop: foot.top, scrolled: scroller.scrollTop });
  })));
}"""

# 记下「生成中」那一刻的页面：请求发出后很快就结束，轮询可能错过，用 MutationObserver 抓
WATCH_LOADING_JS = """() => {
  window.__reportLoading = null;
  const content = document.querySelector('[data-testid="report-content"]');
  if (window.__reportObserver) window.__reportObserver.disconnect();
  window.__reportObserver = new MutationObserver(() => {
    if (window.__reportLoading || content.getAttribute('data-state') !== 'loading') return;
    const q = (id) => document.querySelector(`[data-testid="${id}"]`);
    const gen = q('report-generate');
    window.__reportLoading = {
      busy: content.getAttribute('aria-busy'), skeleton: !!q('report-skeleton'), results: !!q('report-results'), idle: !!q('report-idle'),
      genDisabled: gen.disabled, genBusy: gen.getAttribute('aria-busy'), genText: gen.textContent.replace(/\\s+/g, ' ').trim(),
    };
  });
  window.__reportObserver.observe(content, { attributes: true, childList: true, subtree: true });
}"""

SET_SAFE_JS = """([top, bottom]) => {
  const style = document.documentElement.style;
  style.setProperty('--ft-safe-top', top + 'px');
  style.setProperty('--ft-safe-bottom', bottom + 'px');
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


def visible(locator, timeout: int = 4000) -> bool:
    try:
        locator.wait_for(state="visible", timeout=timeout)
        return True
    except PlaywrightTimeoutError:
        return False


def toast(page, text: str):
    return page.locator(".m-enqueued-snackbars").get_by_text(text)


def wait_toast(page, text: str) -> bool:
    page.mouse.move(1, 1)  # MASA 的 Snackbar 悬停时暂停计时，鼠标别停在上面
    return visible(toast(page, text).first, 8000)


def plain(value) -> str:
    """金额 → data-amount 的写法：两位小数、无千分位。"""
    return f"{Decimal(str(value)):.2f}"


def d_text(day: date) -> str:
    return day.isoformat()


def d_label(day: date) -> str:
    return f"{day.year}年{day.month}月{day.day}日"


def at(day: date, hour: int, minute: int = 0) -> datetime:
    """应用时区（= 运行环境本地时区）某天的某个时刻（naive 本地时间，造数据时 LocalDb 换算成 UTC）。"""
    return datetime(day.year, day.month, day.day, hour, minute)


def quick_range(key: str, today: date) -> "tuple[date, date]":
    """快捷区间对应的起止日期（与 CustomReportPresentation.QuickRangeBounds 同一口径，这里独立再算一遍用来对照）。"""
    if key == "last-month":
        last = today.replace(day=1) - timedelta(days=1)
        return last.replace(day=1), last
    if key == "last-30":
        return today - timedelta(days=30), today
    if key == "last-90":
        return today - timedelta(days=90), today
    return today.replace(day=1), today


def path(page) -> str:
    return page.evaluate("() => location.pathname")


def state(page) -> dict:
    return page.evaluate(STATE_JS)


def result(page) -> "dict | None":
    return page.evaluate(RESULT_JS)


def wait_report_ready(page) -> bool:
    """页面读完用户与账本后「生成报表」才可用（此前点它会拿空用户生成一份空报表）。"""
    return poll(page, "() => { const b = document.querySelector('[data-testid=\"report-generate\"]'); return !!b && !b.disabled; }", timeout=20000)


def wait_row(page, row_id: str, value: str) -> bool:
    return poll(page, "([id, v]) => { const n = document.querySelector(`[data-testid=\"${id}\"]`); return !!n && n.getAttribute('data-value') === v; }", arg=[row_id, value])


def wait_error(page, text: "str | None") -> bool:
    """等不合法提示出现（text 为文字）或消失（text 为 None）。"""
    return poll(
        page,
        "(t) => { const n = document.querySelector('[data-testid=\"report-error\"]'); return t === null ? !n : !!n && n.textContent.trim() === t; }",
        arg=text,
    )


def go_report_from_stats(page) -> None:
    """从统计 Tab 右上角进入自定义报表（AC 要求的入口）。"""
    open_tab(page, "统计")
    page.wait_for_function("() => { const el = document.querySelector('[data-testid=\"stats-monthly\"]'); return !!el && el.getAttribute('data-state') === 'ready'; }")
    tid(page, "stats-report").click()
    wait_url(page, r"^/custom-report$")
    wait_tab_bar(page, visible=False)
    wait_report_ready(page)
    wait_animations_settled(page)


def go_report(page) -> bool:
    """整页直接进入自定义报表（造数要在这之前做；之后的页面状态是新的）。"""
    open_app(page, "/custom-report")
    ok = wait_report_ready(page)
    wait_animations_settled(page)
    return ok


def generate(page, timeout: int = 15000) -> bool:
    """点「生成报表」，等这一次生成结束（成功或失败都会让 data-generation 加一）。"""
    before = state(page)["generation"]
    tid(page, "report-generate").click()
    return poll(
        page,
        "(n) => { const c = document.querySelector('[data-testid=\"report-content\"]'); return !!c && Number(c.getAttribute('data-generation')) > n && c.getAttribute('data-state') !== 'loading'; }",
        arg=before,
        timeout=timeout,
    )


def set_range(page, start: date, end: date) -> bool:
    """在两个日期输入里填起止日期（与手机上选好日期等价），等行上的值更新。"""
    tid(page, "report-start").fill(d_text(start))
    ok = wait_row(page, "report-start-row", d_text(start))
    tid(page, "report-end").fill(d_text(end))
    return wait_row(page, "report-end-row", d_text(end)) and ok


def click_quick(page, key: str) -> None:
    page.locator(f'[data-testid="report-quick-option"][data-value="{key}"]').click()


def wait_toasts_gone(page, timeout: int = 15000) -> bool:
    """等屏幕上的 Snackbar 都消失（它们浮在页面底部，会压住最后一行；鼠标别停在上面，悬停时它们不计时）。"""
    page.mouse.move(1, 1)
    return poll(page, "() => document.querySelectorAll('.m-enqueued-snackbars .m-snack__content').length === 0", timeout=timeout)


def click_centered(page, testid: str) -> None:
    """点页面里靠下的一行（导出 / 分享）：先等 Snackbar 消失（它们浮在底部压住最后一行），再滚到屏幕中间点（底部还有固定的主按钮区）。"""
    wait_toasts_gone(page)
    node = tid(page, testid)
    node.evaluate("(el) => el.scrollIntoView({ block: 'center' })")
    node.click()


def range_text(start: date, end: date) -> str:
    return f"{d_text(start)} ~ {d_text(end)}"


def lit_chips(st: dict) -> "list[str]":
    return [c["value"] for c in st["chips"] if c["pressed"] == "true" and c["selected"] == "true"]


def seed(db: LocalDb, user_id: str, ledger_id, amount, category: str, when: datetime, *, income: bool = False, category_id=None) -> None:
    """造一条账单：category 是预设分类的名称；自建分类传 category_id（此时 category 不用）。ledger_id 为 None 是历史无账本账单。"""
    bill_type = BILL_TYPE_INCOME if income else BILL_TYPE_EXPENSE
    db.seed_bill(
        user_id,
        amount=amount,
        ledger_id=ledger_id,
        bill_type=bill_type,
        category_id=category_id or db.category_id_by_name(category, bill_type),
        when=when,
    )


def expected_csv(start: date, end: date, *, ledger_name=None, expense, income, balance, count, rows) -> str:
    lines = [f"自定义报表 {range_text(start, end)}"]
    if ledger_name:
        lines.append(f"账本,{ledger_name}")
    lines += [f"总支出,{expense}", f"总收入,{income}", f"净收支,{balance}", f"账单数量,{count}", "", "分类,金额,笔数,占比(%)"]
    for name, amount, n, pct in rows:
        field = '"' + name.replace('"', '""') + '"' if any(ch in name for ch in ',"\r\n') else name
        lines.append(f"{field},{amount},{n},{pct}")
    return "\r\n".join(lines) + "\r\n"


# ---------------------------------------------------------------------------------------------- A. 外壳与初始状态


def check_shell_and_empty(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        login(page)
        today = date.today()
        first = today.replace(day=1)

        go_report_from_stats(page)
        st = state(page)
        nav = page.evaluate(NAV_JS)

        run.check("【进入】从统计 Tab 右上角进入：地址 /custom-report，Tab 栏隐藏（二级页面）", path(page) == "/custom-report" and not tab_bar(page).is_visible(), path(page))
        run.check(
            "【导航栏】标题「自定义报表」，返回按钮在标题左侧、不重叠",
            nav["titleText"] == "自定义报表" and nav["back"]["r"] <= nav["title"]["l"] + 0.5,
            str(nav),
        )
        run.check("【初始】结果区是提示「选择日期范围后点击「生成报表」」，没有结果 / 骨架 / 失败占位", st["state"] == "idle" and st["idle"] == IDLE_TEXT and not st["hasResults"] and not st["skeleton"] and not st["failed"], str(st))

        run.check(
            "【快捷区间】小标题「快捷区间」，四个芯片依次是 本月 / 上月 / 近30天 / 近90天，每个热区 ≥ 44 高",
            st["quickLabel"] == "快捷区间" and [(c["value"], c["label"]) for c in st["chips"]] == QUICK_RANGES and all(c["box"]["h"] >= 44 for c in st["chips"]),
            str(st["chips"]),
        )
        run.check("【快捷区间】默认区间 = 本月：只有「本月」亮起（aria-pressed / data-selected），data-quick = this-month", lit_chips(st) == ["this-month"] and st["quick"] == "this-month", f"{lit_chips(st)} {st['quick']}")
        run.check(
            "【报表范围】分组标题「报表范围」；开始日期 = 本月 1 日、结束日期 = 今天（「2026年11月1日」的写法），行上的值与 data-value 一致",
            st["rangeHeader"] == "报表范围"
            and st["start"]["title"] == "开始日期" and st["start"]["value"] == d_label(first) and st["start"]["dataValue"] == d_text(first)
            and st["end"]["title"] == "结束日期" and st["end"]["value"] == d_label(today) and st["end"]["dataValue"] == d_text(today),
            f"{st['start']} {st['end']}",
        )
        run.check(
            "【日期选择】起止日期是系统日期输入（type=date，带读屏名称，可选范围 2000-01-01 ~ 2100-12-31），值与行一致",
            st["startInput"] == {"type": "date", "value": d_text(first), "min": "2000-01-01", "max": "2100-12-31", "aria": "开始日期"}
            and st["endInput"] == {"type": "date", "value": d_text(today), "min": "2000-01-01", "max": "2100-12-31", "aria": "结束日期"},
            f"{st['startInput']} {st['endInput']}",
        )
        run.check(
            "【日期选择】透明的 date 输入盖满整行：行中心命中的就是输入本身（点哪里都唤起日期选择器）",
            page.evaluate(HIT_ROW_JS, "report-start-row") == "report-start" and page.evaluate(HIT_ROW_JS, "report-end-row") == "report-end",
        )
        run.check("【账本】没有任何账本：不显示账本一行", st["ledger"] is None)
        run.check(
            "【主按钮】「生成报表」固定在页面底部（position: fixed，贴屏幕底）、可用、高 52、横向撑满",
            st["gen"]["text"] == "生成报表" and not st["gen"]["disabled"] and st["foot"]["position"] == "fixed" and abs(st["foot"]["box"]["b"] - SCREEN_H) <= 1
            and abs(st["gen"]["box"]["h"] - 52) <= 1 and st["gen"]["box"]["w"] >= SCREEN_W - 60,
            f"{st['gen']} {st['foot']}",
        )
        run.check("【布局】没有横向溢出；每屏玻璃层 ≤ 3", st["overflowX"] <= 0 and page.evaluate(GLASS_LAYERS_JS) <= 3, f"{st['overflowX']} {page.evaluate(GLASS_LAYERS_JS)}")
        shot(page, "report-idle")

        # ---- 第一次生成（没有任何账单）----
        page.evaluate(WATCH_LOADING_JS)
        run.check("【生成】点「生成报表」，生成结束", generate(page))
        loading = page.evaluate("() => window.__reportLoading")
        run.check(
            "【生成中】结果区先是骨架（没有提示 / 旧结果），内容标记忙碌，主按钮置灰并显示「生成中」",
            bool(loading) and loading["busy"] == "true" and loading["skeleton"] and not loading["idle"] and not loading["results"]
            and loading["genDisabled"] and loading["genBusy"] == "true" and loading["genText"] == "生成中",
            str(loading),
        )
        wait_animations_settled(page)
        st = state(page)
        res = result(page)
        run.check("【生成后】内容标记 data-state = ready、不再忙碌，提示与骨架消失，主按钮恢复「生成报表」", st["state"] == "ready" and st["busy"] is None and st["idle"] is None and not st["skeleton"] and st["gen"]["text"] == "生成报表" and not st["gen"]["disabled"], str(st))
        run.check(
            "【空报表】KPI 支出 / 收入 / 结余都是 0.00，区间属性 = 本月 1 日 ~ 今天、没有账本筛选、0 笔账单",
            res["expense"] == "0.00" and res["income"] == "0.00" and res["balance"] == "0.00"
            and res["start"] == d_text(first) and res["end"] == d_text(today) and res["ledger"] is None and res["count"] == "0",
            str(res),
        )
        run.check(
            "【说明行】「区间 · 全部账本 · 共 0 笔账单」",
            res["caption"] == f"{range_text(first, today)} · 全部账本 · 共 0 笔账单",
            res["caption"],
        )
        run.check(
            "【空报表】分类支出环比 / 分类支出排行 / 支出构成 / 收入构成都是空状态说明（环比没有「对比哪一段」的脚注）",
            res["comparisonHeader"] == "分类支出环比" and res["comparisonState"] == "empty" and res["comparisonEmpty"] == "所选范围内暂无支出数据" and res["comparisonFooter"] is None and not res["rows"]
            and res["rankHeader"] == "分类支出排行" and res["rank"]["state"] == "empty" and res["rank"]["empty"] == "所选范围内暂无支出账单"
            and res["expenseHeader"] == "支出构成" and res["expenseEmpty"] == "所选范围内暂无支出账单" and res["expenseDonut"]["slices"] == "0" and res["expenseTotal"] == "0.00"
            and res["incomeHeader"] == "收入构成" and res["incomeEmpty"] == "所选范围内暂无收入账单" and res["incomeDonut"]["slices"] == "0" and res["incomeTotal"] == "0.00",
            str(res),
        )
        run.check(
            "【空报表】「导出与分享」两行照常可点（导出 CSV / 分享摘要，各带副标题）",
            res["actionsHeader"] == "导出与分享"
            and res["exportRow"] == {"title": "导出 CSV", "subtitle": "支出分类汇总，可用 Excel 打开", "disabled": False}
            and res["shareRow"] == {"title": "分享摘要", "subtitle": "文字摘要，发给家人或朋友", "disabled": False},
            f"{res['exportRow']} {res['shareRow']}",
        )
        tops = page.evaluate(SECTION_TOPS_JS)
        run.check("【顺序】各节自上而下：KPI → 说明 → 环比 → 排行 → 支出构成 → 收入构成 → 导出与分享", None not in tops and all(a < b for a, b in zip(tops, tops[1:])), str(tops))
        run.check("【布局】结果态没有横向溢出；每屏玻璃层 ≤ 3", res["overflowX"] <= 0 and page.evaluate(GLASS_LAYERS_JS) <= 3, f"{res['overflowX']} {page.evaluate(GLASS_LAYERS_JS)}")
        shot(page, "report-empty-result")

        # ---- 返回统计 Tab ----
        tid(page, "nav-back").click()
        wait_url(page, r"^/statistics$")
        wait_tab_bar(page)
        wait_secondary_pages_closed(page)
        run.check("【返回】点返回回到统计 Tab：Tab 栏恢复、统计页可见", path(page) == "/statistics" and tab_bar(page).is_visible() and visible(tid(page, "stats-monthly")))
        run.check_clean(s.errors, "A 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- B. 不合法输入


def check_validation(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_id = db.seed_ledger(user_id, unique_name("报表账本"))
        today = date.today()
        first = today.replace(day=1)
        seed(db, user_id, ledger_id, 25, "餐饮美食", at(today, 12))

        run.check("进入自定义报表页", go_report(page))
        run.check("先生成一份报表（本月：25.00）", generate(page) and result(page)["expense"] == "25.00")
        before = result(page)

        # ---- 结束早于开始 ----
        end_before = first - timedelta(days=1)
        tid(page, "report-end").fill(d_text(end_before))
        run.check("结束日期改到开始日期之前", wait_row(page, "report-end-row", d_text(end_before)))
        run.check("【结束早于开始】分组下面出现红字「开始日期不能晚于结束日期」", wait_error(page, RANGE_ORDER_TEXT))
        st = state(page)
        run.check("【结束早于开始】提示是 role=alert（读屏立即播报）", st["error"] is not None and st["error"]["role"] == "alert", str(st["error"]))
        run.check("【结束早于开始】提示用加深的危险文字色（页面底色上 ≥ 4.5:1；不是只有 4.2:1 的图形红）", st["error"]["color"] == RGB_DANGER_TEXT, str(st["error"]))
        run.check("【结束早于开始】「生成报表」置灰", st["gen"]["disabled"], str(st["gen"]))
        run.check("【结束早于开始】已有的结果原样保留（支出 25.00，区间还是本月）", result(page)["expense"] == "25.00" and result(page)["start"] == before["start"] and result(page)["end"] == before["end"], str(result(page)))
        gen_before = st["generation"]
        tid(page, "report-generate").click(force=True)  # 点不动：浏览器不会给置灰的按钮派发点击
        # 改对日期（这是一次完整的服务端往返）：之前那次强行点击若触发过生成，次数早就加一了
        tid(page, "report-end").fill(d_text(today))
        run.check("【结束早于开始】改回合法日期后提示消失、「生成报表」恢复可用", wait_error(page, None) and poll(page, "() => !document.querySelector('[data-testid=\"report-generate\"]').disabled"))
        run.check("【结束早于开始】期间强行点置灰的「生成报表」没有触发生成", state(page)["generation"] == gen_before, f"{state(page)['generation']} vs {gen_before}")

        # ---- 清空开始日期 ----
        tid(page, "report-start").fill("")
        run.check("【清空开始】提示「请选择开始日期」", wait_error(page, START_REQUIRED_TEXT))
        st = state(page)
        run.check(
            "【清空开始】开始日期一行的值是「请选择」（加深的红色提示），data-value 为空，主按钮置灰，没有快捷区间亮起",
            st["start"]["value"] == "请选择" and st["start"]["missing"] and st["start"]["dataValue"] == "" and st["gen"]["disabled"] and lit_chips(st) == [] and st["quick"] == "",
            str(st),
        )
        tid(page, "report-start").fill(d_text(first))
        run.check("【清空开始】重新选好后恢复", wait_error(page, None) and wait_row(page, "report-start-row", d_text(first)))

        # ---- 清空结束日期 ----
        tid(page, "report-end").fill("")
        run.check("【清空结束】提示「请选择结束日期」，结束日期一行是「请选择」，主按钮置灰", wait_error(page, END_REQUIRED_TEXT) and state(page)["end"]["value"] == "请选择" and state(page)["end"]["missing"] and state(page)["gen"]["disabled"])
        tid(page, "report-end").fill(d_text(today))
        run.check("【清空结束】重新选好后恢复", wait_error(page, None) and wait_row(page, "report-end-row", d_text(today)))

        # ---- 超出可选范围 ----
        tid(page, "report-start").fill("1999-12-31")
        run.check("【超出范围】开始日期早于 2000 年：提示「日期需在 2000 年到 2100 年之间」，行上照实显示、主按钮置灰", wait_error(page, OUT_OF_RANGE_TEXT) and state(page)["start"]["value"] == "1999年12月31日" and state(page)["gen"]["disabled"])
        tid(page, "report-start").fill(d_text(first))
        tid(page, "report-end").fill("2101-01-01")
        run.check("【超出范围】结束日期晚于 2100 年同样拦下", wait_error(page, OUT_OF_RANGE_TEXT) and state(page)["gen"]["disabled"])
        tid(page, "report-end").fill(d_text(today))
        run.check("【超出范围】改回合法日期后恢复", wait_error(page, None) and wait_row(page, "report-end-row", d_text(today)))

        run.check("【恢复后】可以再次生成，结果仍是 25.00", generate(page) and result(page)["expense"] == "25.00")
        run.check_clean(s.errors, "B 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- C. 快捷区间与自定义日期


def seed_range_plan(db: LocalDb, user_id: str, ledger_id: str, today: date) -> "list[tuple[int, datetime]]":
    """按「离今天 / 月初多远」造一组账单。金额取 2 的幂：任何区间的合计都能唯一对应「哪几笔被算进去」，对不上时一眼看出差了哪笔。
    边界账单故意放在 00:30 与 23:30：区间按应用时区的自然日、含两端日，凌晨的算当天、前一天深夜的不算。"""
    first = today.replace(day=1)
    prev_last = first - timedelta(days=1)
    plan = [
        (1, at(today, 12)),
        (2, at(first, 0, 30)),  # 本月 1 日凌晨：本月的第一天
        (4, at(prev_last, 23, 30)),  # 上月最后一天深夜：不属于本月
        (8, at(prev_last.replace(day=15), 12)),
        (16, at(today - timedelta(days=29), 12)),
        (32, at(today - timedelta(days=30), 0, 30)),  # 近 30 天的第一天（今天往前 30 天，含）
        (64, at(today - timedelta(days=31), 23, 30)),  # 近 30 天的前一天深夜：不算
        (128, at(today - timedelta(days=89), 12)),
        (256, at(today - timedelta(days=90), 0, 30)),  # 近 90 天的第一天
        (512, at(today - timedelta(days=91), 23, 30)),  # 近 90 天的前一天深夜：不算
    ]
    category = db.category_id_by_name("餐饮美食")
    for amount, when in plan:
        db.seed_bill(user_id, amount=amount, ledger_id=ledger_id, category_id=category, when=when)
    return plan


def expected_sum(plan: "list[tuple[int, datetime]]", start: date, end: date) -> "tuple[int, int]":
    hits = [amount for amount, when in plan if start <= when.date() <= end]
    return sum(hits), len(hits)


def check_quick_and_dates(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_id = db.seed_ledger(user_id, unique_name("报表账本"))
        today = date.today()
        first = today.replace(day=1)
        plan = seed_range_plan(db, user_id, ledger_id, today)

        run.check("进入自定义报表页（有账本：多出账本一行）", go_report(page) and state(page)["ledger"]["value"] == "全部账本")

        def check_generated(label: str, start: date, end: date, quick: str) -> None:
            total, count = expected_sum(plan, start, end)
            ok = generate(page)
            res = result(page) or {}
            run.check(
                f"【{label}】生成：支出合计 = 区间内（含两端日）所建账单 {plain(total)}（{count} 笔），区间属性与说明行都对",
                ok and res.get("expense") == plain(total) and res.get("count") == str(count)
                and res.get("start") == d_text(start) and res.get("end") == d_text(end)
                and res.get("caption") == f"{range_text(start, end)} · 全部账本 · 共 {count} 笔账单"
                and res.get("income") == "0.00" and res.get("balance") == plain(-total),
                str(res),
            )
            st = state(page)
            run.check(f"【{label}】生成后：内容的快捷区间标记 = {quick or '（无）'}", st["quick"] == quick and lit_chips(st) == ([quick] if quick else []), f"{st['quick']} {lit_chips(st)}")

        check_generated("本月（默认）", first, today, "this-month")

        # ---- 四个快捷区间：点一下只填日期，不生成 ----
        for key, label in QUICK_RANGES:
            start, end = quick_range(key, today)
            old = result(page)
            click_quick(page, key)
            filled = wait_row(page, "report-start-row", d_text(start)) and wait_row(page, "report-end-row", d_text(end))
            st = state(page)
            run.check(
                f"【{label}】点芯片只把起止日期填好（{d_label(start)} → {d_label(end)}），{label}亮起、其余不亮",
                filled and st["start"]["value"] == d_label(start) and st["end"]["value"] == d_label(end) and lit_chips(st) == [key] and st["quick"] == key,
                f"{st['start']} {st['end']} {lit_chips(st)}",
            )
            cur = result(page)
            run.check(f"【{label}】点芯片不会立刻生成：结果与说明还是上一次的", cur["start"] == old["start"] and cur["end"] == old["end"] and cur["caption"] == old["caption"] and st["generation"] == state(page)["generation"], f"{cur['start']} vs {old['start']}")
            check_generated(label, start, end, key)

        # ---- 手动输入日期 ----
        start, end = today - timedelta(days=30), today
        run.check("【手动 = 快捷】手动填的起止日期恰好等于「近30天」→ 芯片亮起", set_range(page, start, end) and lit_chips(state(page)) == ["last-30"] and state(page)["quick"] == "last-30")

        start, end = today - timedelta(days=45), today - timedelta(days=12)
        run.check("【手动】另填一段（45 天前 ~ 12 天前）→ 没有快捷区间亮起", set_range(page, start, end) and lit_chips(state(page)) == [] and state(page)["quick"] == "")
        check_generated("手动区间", start, end, "")

        run.check("【单日】开始 = 结束 = 今天", set_range(page, today, today))
        check_generated("单日（今天）", today, today, "")

        run.check("【月初】开始 = 结束 = 本月 1 日（凌晨 00:30 的账单算这一天，前一天深夜 23:30 的不算）", set_range(page, first, first))
        check_generated("单日（本月 1 日）", first, first, "")

        last_month_end = first - timedelta(days=1)
        run.check("【月末】开始 = 结束 = 上月最后一天", set_range(page, last_month_end, last_month_end))
        check_generated("单日（上月最后一天）", last_month_end, last_month_end, "")

        run.check_clean(s.errors, "C 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- D. 结果内容、导出、分享


def check_results_export_share(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_name = unique_name("报表账本")
        ledger_id = db.seed_ledger(user_id, ledger_name)
        today = date.today()
        custom = db.seed_category(user_id, CUSTOM_CATEGORY)

        # 本期 [今天-13, 今天-7]，上一等长周期 [今天-20, 今天-14]；边界账单放在 00:30 / 23:30
        cur_start, cur_end = today - timedelta(days=13), today - timedelta(days=7)
        prev_start, prev_end = today - timedelta(days=20), today - timedelta(days=14)
        seed(db, user_id, ledger_id, 200, "购物消费", at(cur_start, 0, 30))  # 本期第一天凌晨
        seed(db, user_id, ledger_id, 100, "餐饮美食", at(today - timedelta(days=12), 12))
        seed(db, user_id, ledger_id, 50, "餐饮美食", at(cur_end, 23, 30))  # 本期最后一天深夜
        seed(db, user_id, ledger_id, 30, "交通出行", at(today - timedelta(days=10), 12))
        seed(db, user_id, ledger_id, 20, "", at(today - timedelta(days=11), 12), category_id=custom)
        seed(db, user_id, ledger_id, "1234.50", "工资薪酬", at(today - timedelta(days=9), 12), income=True)
        seed(db, user_id, ledger_id, 100, "餐饮美食", at(today - timedelta(days=18), 12))  # 上期
        seed(db, user_id, ledger_id, 60, "交通出行", at(prev_start, 0, 30))  # 上期第一天凌晨
        seed(db, user_id, ledger_id, 40, "居住生活", at(prev_end, 23, 30))  # 上期最后一天深夜：本期归零的分类
        seed(db, user_id, ledger_id, 999, "其他支出", at(prev_start - timedelta(days=1), 23, 30))  # 上期之前一天深夜：哪个区间都不算
        seed(db, user_id, ledger_id, 888, "其他支出", at(cur_end + timedelta(days=1), 0, 30))  # 本期之后一天凌晨：哪个区间都不算

        run.check("进入自定义报表页", go_report(page))
        run.check("填本期区间（13 天前 ~ 7 天前）并生成", set_range(page, cur_start, cur_end) and generate(page))
        wait_animations_settled(page)
        res = result(page)

        run.check(
            "【KPI】支出 400.00 / 收入 1234.50 / 结余 834.50（只算区间内：边界账单按应用时区自然日取舍，区间外的 999 / 888 不算）",
            res["expense"] == "400.00" and res["income"] == "1234.50" and res["balance"] == "834.50" and res["count"] == "6",
            f"{res['expense']} {res['income']} {res['balance']} {res['count']}",
        )
        run.check(
            "【KPI】三栏标题依次是 支出 / 收入 / 结余",
            [c["label"] for c in res["kpiCells"]] == ["支出", "收入", "结余"],
            str([c["label"] for c in res["kpiCells"]]),
        )
        run.check(
            "【说明行】区间 · 全部账本 · 共 6 笔账单；区间属性 data-start / data-end 与填的一致",
            res["caption"] == f"{range_text(cur_start, cur_end)} · 全部账本 · 共 6 笔账单" and res["start"] == d_text(cur_start) and res["end"] == d_text(cur_end),
            f"{res['caption']} {res['start']} {res['end']}",
        )

        expected_rows = [
            # 分类, 本期, 上期, 方向, 语义色, 变化文字, 箭头, 文字颜色, 副标题
            ("购物消费", "200.00", "0.00", "new", "bad", "新增", "mdi-arrow-up", RGB_DANGER_TEXT, "¥200.00（上期 ¥0.00）"),
            ("餐饮美食", "150.00", "100.00", "up", "bad", "+50.0%", "mdi-arrow-up", RGB_DANGER_TEXT, "¥150.00（上期 ¥100.00）"),
            ("居住生活", "0.00", "40.00", "down", "good", "-100.0%", "mdi-arrow-down", RGB_INCOME_TEXT, "¥0.00（上期 ¥40.00）"),
            ("交通出行", "30.00", "60.00", "down", "good", "-50.0%", "mdi-arrow-down", RGB_INCOME_TEXT, "¥30.00（上期 ¥60.00）"),
            (CUSTOM_CATEGORY, "20.00", "0.00", "new", "bad", "新增", "mdi-arrow-up", RGB_DANGER_TEXT, "¥20.00（上期 ¥0.00）"),
        ]
        rows = res["rows"]
        run.check(
            "【环比】五个分类按「变化金额绝对值」从大到小排列（200 / 50 / 40 / 30 / 20），名称与本期 / 上期金额都对——本期归零的「居住生活」取上期的名字（不是「未知分类」）",
            [(r["name"], r["current"], r["previous"]) for r in rows] == [(e[0], e[1], e[2]) for e in expected_rows],
            str([(r["name"], r["current"], r["previous"]) for r in rows]),
        )
        run.check(
            "【环比】方向 / 语义色 / 变化文字：新增（红、向上）、+50.0%（红、向上）、-100.0%（绿、向下）、-50.0%（绿、向下）",
            [(r["kind"], r["tone"], r["delta"]) for r in rows] == [(e[3], e[4], e[5]) for e in expected_rows],
            str([(r["kind"], r["tone"], r["delta"]) for r in rows]),
        )
        run.check(
            "【环比】变化文字的颜色：变多 = 深红、变少 = 深绿；箭头图标与方向一致",
            all(r["deltaColor"] == e[7] and e[6] in (r["icon"] or "") for r, e in zip(rows, expected_rows)),
            str([(r["deltaColor"], r["icon"]) for r in rows]),
        )
        run.check("【环比】副标题「¥本期（上期 ¥上期）」", [r["subtitle"] for r in rows] == [e[8] for e in expected_rows], str([r["subtitle"] for r in rows]))
        run.check(
            "【环比】节标题「分类支出环比」，脚注写明对比的是哪一段（上一等长周期：紧邻本期之前、天数相同）",
            res["comparisonHeader"] == "分类支出环比" and res["comparisonFooter"] == f"对比上一等长周期（{range_text(prev_start, prev_end)}）",
            f"{res['comparisonHeader']} / {res['comparisonFooter']}",
        )

        expected_expense = [("购物消费", "200.00", "1", "50"), ("餐饮美食", "150.00", "2", "37.5"), ("交通出行", "30.00", "1", "7.5"), (CUSTOM_CATEGORY, "20.00", "1", "5")]
        run.check(
            "【支出构成】圆环 4 段、中心总额 400.00；图例按金额从大到小：名称 / 金额 / 笔数 / 占比；圆环段与图例一一对应",
            res["expenseHeader"] == "支出构成" and res["expenseDonut"]["slices"] == "4" and res["expenseTotal"] == "400.00"
            and [(r["name"], r["amount"], r["count"], r["percent"]) for r in res["expenseLegend"]] == expected_expense
            and res["expenseDonut"]["arcs"] == [e[0] for e in expected_expense],
            f"{res['expenseDonut']} {res['expenseLegend']}",
        )
        run.check(
            "【收入构成】圆环 1 段、中心总额 1234.50；图例只有「工资薪酬」100%",
            res["incomeHeader"] == "收入构成" and res["incomeDonut"]["slices"] == "1" and res["incomeTotal"] == "1234.50"
            and [(r["name"], r["amount"], r["count"], r["percent"]) for r in res["incomeLegend"]] == [("工资薪酬", "1234.50", "1", "100")],
            f"{res['incomeDonut']} {res['incomeLegend']}",
        )
        run.check("【排行】节标题「分类支出排行」，有数据时面板是 ready（不是空状态）", res["rankHeader"] == "分类支出排行" and res["rank"]["state"] == "ready", str(res["rank"]))
        tops = page.evaluate(SECTION_TOPS_JS)
        run.check("【顺序】各节自上而下：KPI → 说明 → 环比 → 排行 → 支出构成 → 收入构成 → 导出与分享", None not in tops and all(a < b for a, b in zip(tops, tops[1:])), str(tops))
        shot(page, "report-results")

        # ---- 生成后改参数：已有结果不动，说明行写明它的区间 ----
        click_quick(page, "last-90")
        run.check("【快照】生成后改了区间：已有结果与说明行不变（要看新参数得再点「生成报表」）", wait_row(page, "report-start-row", d_text(today - timedelta(days=90))) and result(page)["start"] == d_text(cur_start) and result(page)["caption"] == res["caption"])
        run.check("【快照】改了区间再点「生成报表」：结果换成新区间（90 天内：含上面所有账单）", generate(page) and result(page)["start"] == d_text(today - timedelta(days=90)) and result(page)["count"] != "6")
        run.check("改回本期区间重新生成", set_range(page, cur_start, cur_end) and generate(page) and result(page)["expense"] == "400.00")

        # ---- 导出 CSV ----
        with page.expect_download(timeout=15000) as download_info:
            click_centered(page, "report-export")
        download = download_info.value
        data = Path(download.path()).read_bytes()
        run.check("【导出】下载的文件名是 custom_report.csv", download.suggested_filename == "custom_report.csv", download.suggested_filename)
        run.check("【导出】文件以 UTF-8 BOM 开头（Excel 打开中文不乱码）", data[:3] == b"\xef\xbb\xbf", repr(data[:6]))
        expected = expected_csv(
            cur_start, cur_end, expense="400.00", income="1234.50", balance="834.50", count=6,
            rows=[("购物消费", "200.00", 1, "50.00"), ("餐饮美食", "150.00", 2, "37.50"), ("交通出行", "30.00", 1, "7.50"), (CUSTOM_CATEGORY, "20.00", 1, "5.00")],
        )
        text = data[3:].decode("utf-8")
        run.check(
            "【导出】内容：标题行（区间）、总支出 / 总收入 / 净收支 / 账单数量、空行、分类明细；金额是不带千分位的两位小数（1234.50）；含逗号与引号的分类名按 CSV 规则加引号转义；换行 CRLF",
            text == expected,
            "\n" + repr(text) + "\n" + repr(expected),
        )
        run.check("【导出】Snackbar「报表已导出」", wait_toast(page, "报表已导出"))
        run.check("【导出】导出完成后「导出 CSV」恢复可点", poll(page, "() => !document.querySelector('[data-testid=\"report-export\"]').disabled"))

        # ---- 分享摘要 ----
        expected_share = (
            f"【记账报表】{range_text(cur_start, cur_end)}\n"
            "总支出 ¥400.00，总收入 ¥1,234.50，净收支 ¥834.50（共 6 笔账单）\n"
            f"支出 Top3：购物消费 ¥200.00、餐饮美食 ¥150.00、交通出行 ¥30.00\n"
            "环比增长最多：购物消费（+¥200.00）\n"
            "环比减少最多：居住生活（-¥40.00，-100.0%）"
        )

        def share_enabled() -> bool:
            return poll(page, "() => !document.querySelector('[data-testid=\"report-share\"]').disabled")

        def toast_count(text: str) -> int:
            return toast(page, text).count()

        # 1) 系统分享面板可用：把标题与摘要交给它
        page.evaluate("() => { delete navigator.share; window.__shared = null; navigator.share = async (data) => { window.__shared = data; }; }")
        click_centered(page, "report-share")
        run.check("【分享】系统分享面板可用：交给它标题「记账报表」与完整摘要", poll(page, "() => !!window.__shared") and page.evaluate("() => window.__shared") == {"title": "记账报表", "text": expected_share}, str(page.evaluate("() => window.__shared")))
        run.check("【分享】分享完成后不弹 Snackbar（面板自己有反馈），按钮恢复可点", share_enabled() and toast_count("报表摘要已复制到剪贴板") == 0 and toast_count("当前环境不支持分享，可截图保存") == 0)

        # 2) 用户关掉分享面板（AbortError）：什么都不做，更不能偷偷改他的剪贴板
        page.evaluate(
            """() => {
                window.__clip = null;
                window.__shareCalls = 0;
                navigator.share = async () => { window.__shareCalls += 1; const e = new Error('cancel'); e.name = 'AbortError'; throw e; };
                Object.defineProperty(navigator.clipboard, 'writeText', { configurable: true, value: async (t) => { window.__clip = t; } });
            }"""
        )
        click_centered(page, "report-share")
        run.check("【分享】用户关掉分享面板：不退到剪贴板、不弹 Snackbar", poll(page, "() => window.__shareCalls >= 1") and share_enabled() and page.evaluate("() => window.__clip") is None and toast_count("报表摘要已复制到剪贴板") == 0 and toast_count("当前环境不支持分享，可截图保存") == 0)

        # 3) 没有系统分享（桌面浏览器 / 旧 WebView）：复制到剪贴板并提示
        page.evaluate("() => { Object.defineProperty(navigator, 'share', { configurable: true, value: undefined }); }")
        click_centered(page, "report-share")
        run.check("【分享】没有系统分享面板：摘要复制到剪贴板", poll(page, "() => !!window.__clip") and page.evaluate("() => window.__clip") == expected_share, str(page.evaluate("() => window.__clip")))
        run.check("【分享】Snackbar「报表摘要已复制到剪贴板」", wait_toast(page, "报表摘要已复制到剪贴板"))

        # 4) 系统分享调用失败（非取消）：同样退到剪贴板
        page.evaluate("() => { window.__clip = null; navigator.share = async () => { const e = new Error('denied'); e.name = 'NotAllowedError'; throw e; }; }")
        click_centered(page, "report-share")
        run.check("【分享】系统分享调用失败（非取消）：退到剪贴板", poll(page, "() => !!window.__clip") and page.evaluate("() => window.__clip") == expected_share)

        # 5) 两条路都走不通：提示「当前环境不支持分享」
        page.evaluate("() => { Object.defineProperty(navigator, 'share', { configurable: true, value: undefined }); Object.defineProperty(navigator, 'clipboard', { configurable: true, value: undefined }); }")
        click_centered(page, "report-share")
        run.check("【分享】分享与剪贴板都用不了：Snackbar「当前环境不支持分享，可截图保存」", wait_toast(page, "当前环境不支持分享，可截图保存"))
        page.evaluate("() => { delete navigator.share; delete navigator.clipboard; }")

        # ---- 底部固定按钮不遮住最后一节 ----
        end = page.evaluate(SCROLL_TO_END_JS)
        run.check("【布局】滚到底：最后一节（导出与分享）完整露在固定的主按钮上方", end["lastBottom"] <= end["footTop"] + 0.5, str(end))
        run.check_clean(s.errors, "D 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- E. 账本筛选


def check_ledger_filter(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        today = date.today()
        name1, name2 = unique_name("账本甲"), unique_name("账本乙")
        l1 = db.seed_ledger(user_id, name1, sort_order=0)
        l2 = db.seed_ledger(user_id, name2, sort_order=1)

        cur_start, cur_end = today - timedelta(days=6), today
        prev_start, prev_end = today - timedelta(days=13), today - timedelta(days=7)
        seed(db, user_id, l1, 100, "餐饮美食", at(today - timedelta(days=3), 12))
        seed(db, user_id, l1, 500, "工资薪酬", at(today - timedelta(days=2), 12), income=True)
        seed(db, user_id, l1, 50, "餐饮美食", at(today - timedelta(days=10), 12))
        seed(db, user_id, l2, 40, "交通出行", at(today - timedelta(days=2), 12))
        seed(db, user_id, l2, 20, "交通出行", at(today - timedelta(days=9), 12))
        seed(db, user_id, None, 7, "其他支出", at(today - timedelta(days=1), 12))  # 历史无账本账单
        seed(db, user_id, None, 5, "其他支出", at(today - timedelta(days=8), 12))

        run.check("进入自定义报表页并填本期区间（近 7 天）", go_report(page) and set_range(page, cur_start, cur_end))
        st = state(page)
        run.check(
            "【账本行】有账本时出现「账本」一行：默认「全部账本」、可点（弹出菜单）、读屏名称带当前选择",
            st["ledger"] is not None and st["ledger"]["title"] == "账本" and st["ledger"]["value"] == "全部账本" and st["ledger"]["dataValue"] is None
            and st["ledger"]["haspopup"] == "dialog" and st["ledger"]["expanded"] == "false" and st["ledger"]["aria"] == "账本，全部账本",
            str(st["ledger"]),
        )

        def open_sheet() -> None:
            tid(page, "report-ledger-row").click()
            tid(page, "report-ledger-sheet").wait_for(state="visible")
            wait_animations_settled(page)

        def pick(ledger_id) -> None:
            open_sheet()
            if ledger_id is None:
                tid(page, "report-ledger-all").click()
            else:
                page.locator(f'[data-testid="report-ledger-option"][data-ledger-id="{ledger_id.lower()}"]').click()
            tid(page, "report-ledger-sheet").wait_for(state="detached")

        open_sheet()
        options = page.evaluate(
            """() => ({
                all: { text: document.querySelector('[data-testid="report-ledger-all"]').textContent.trim(), current: document.querySelector('[data-testid="report-ledger-all"]').getAttribute('aria-current') },
                items: [...document.querySelectorAll('[data-testid="report-ledger-option"]')].map((n) => ({ id: n.getAttribute('data-ledger-id'), text: n.textContent.trim(), current: n.getAttribute('aria-current') })),
                expanded: document.querySelector('[data-testid="report-ledger-row"]').getAttribute('aria-expanded'),
            })"""
        )
        run.check(
            "【账本菜单】选项 = 全部账本 + 两个账本（按排序）；当前是「全部账本」（对勾 / aria-current），行标记已展开",
            options["all"] == {"text": "全部账本", "current": "true"} and [(i["id"], i["text"], i["current"]) for i in options["items"]] == [(l1.lower(), name1, None), (l2.lower(), name2, None)] and options["expanded"] == "true",
            str(options),
        )
        page.keyboard.press("Escape")
        tid(page, "report-ledger-sheet").wait_for(state="detached")
        run.check("【账本菜单】Esc 关闭、选择不变", state(page)["ledger"]["value"] == "全部账本")

        # ---- 账本乙 ----
        pick(l2)
        st = state(page)
        run.check("【选账本乙】行上显示账本名称，data-value = 账本 Id，读屏名称跟着变", st["ledger"]["value"] == name2 and st["ledger"]["dataValue"] == l2.lower() and st["ledger"]["aria"] == f"账本，{name2}", str(st["ledger"]))
        run.check("【选账本乙】生成", generate(page))
        wait_animations_settled(page)
        res = result(page)
        run.check(
            "【账本乙】只算这个账本：支出 40.00 / 收入 0.00 / 结余 -40.00 / 1 笔；说明行写明账本；data-ledger = 账本 Id",
            res["expense"] == "40.00" and res["income"] == "0.00" and res["balance"] == "-40.00" and res["count"] == "1" and res["ledger"] == l2.lower()
            and res["caption"] == f"{range_text(cur_start, cur_end)} · {name2} · 共 1 笔账单",
            str(res),
        )
        run.check(
            "【账本乙】环比只比较这个账本：交通出行 40.00（上期 20.00）+100.0%；脚注是上一等长周期",
            [(r["name"], r["current"], r["previous"], r["delta"]) for r in res["rows"]] == [("交通出行", "40.00", "20.00", "+100.0%")] and res["comparisonFooter"] == f"对比上一等长周期（{range_text(prev_start, prev_end)}）",
            str(res["rows"]),
        )

        # ---- 导出 / 分享带账本名 ----
        with page.expect_download(timeout=15000) as download_info:
            click_centered(page, "report-export")
        text = Path(download_info.value.path()).read_bytes()[3:].decode("utf-8")
        expected = expected_csv(cur_start, cur_end, ledger_name=name2, expense="40.00", income="0.00", balance="-40.00", count=1, rows=[("交通出行", "40.00", 1, "100.00")])
        run.check("【账本乙·导出】CSV 在标题行下面多一行「账本,账本名」，净收支 -40.00", text == expected, "\n" + repr(text) + "\n" + repr(expected))
        page.evaluate("() => { delete navigator.share; window.__shared = null; navigator.share = async (data) => { window.__shared = data; }; }")
        click_centered(page, "report-share")
        expected_share = (
            f"【记账报表】{range_text(cur_start, cur_end)} · 账本：{name2}\n"
            "总支出 ¥40.00，总收入 ¥0.00，净收支 -¥40.00（共 1 笔账单）\n"
            "支出 Top3：交通出行 ¥40.00\n"
            "环比增长最多：交通出行（+¥20.00，+100.0%）"
        )
        run.check("【账本乙·分享】摘要标题行带「· 账本：账本名」，负的净收支写成 -¥40.00", poll(page, "() => !!window.__shared") and page.evaluate("() => window.__shared")["text"] == expected_share, str(page.evaluate("() => window.__shared")))
        page.evaluate("() => { delete navigator.share; }")

        # ---- 换成账本甲但不生成：结果还是账本乙的 ----
        pick(l1)
        res = result(page)
        run.check("【快照】换了账本还没生成：结果与说明行还是账本乙的", res["ledger"] == l2.lower() and name2 in res["caption"] and res["expense"] == "40.00", str(res))
        run.check("【账本甲】生成", generate(page))
        res = result(page)
        run.check(
            "【账本甲】支出 100.00 / 收入 500.00 / 结余 400.00 / 2 笔；环比 餐饮美食 100.00（上期 50.00）+100.0%",
            res["expense"] == "100.00" and res["income"] == "500.00" and res["balance"] == "400.00" and res["count"] == "2" and res["ledger"] == l1.lower()
            and [(r["name"], r["current"], r["previous"], r["delta"]) for r in res["rows"]] == [("餐饮美食", "100.00", "50.00", "+100.0%")],
            str(res),
        )

        # ---- 全部账本 ----
        pick(None)
        st = state(page)
        run.check("【全部账本】行上回到「全部账本」，data-value 清空", st["ledger"]["value"] == "全部账本" and st["ledger"]["dataValue"] is None, str(st["ledger"]))
        run.check("【全部账本】生成", generate(page))
        res = result(page)
        run.check(
            "【全部账本】包含没有账本的历史账单：支出 147.00（100 + 40 + 7）/ 收入 500.00 / 4 笔；说明行「全部账本」；没有账本筛选属性",
            res["expense"] == "147.00" and res["income"] == "500.00" and res["balance"] == "353.00" and res["count"] == "4" and res["ledger"] is None
            and res["caption"] == f"{range_text(cur_start, cur_end)} · 全部账本 · 共 4 笔账单",
            str(res),
        )
        run.check(
            "【全部账本】环比包含历史无账本账单：餐饮美食 +100.0%、交通出行 +100.0%、其他支出 +40.0%（按变化金额 50 / 20 / 2 排序）",
            [(r["name"], r["current"], r["previous"], r["delta"]) for r in res["rows"]] == [("餐饮美食", "100.00", "50.00", "+100.0%"), ("交通出行", "40.00", "20.00", "+100.0%"), ("其他支出", "7.00", "5.00", "+40.0%")],
            str([(r["name"], r["current"], r["previous"], r["delta"]) for r in res["rows"]]),
        )
        run.check_clean(s.errors, "E 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- F. 沉浸式


def check_immersive(run: TestRun, db: LocalDb) -> None:
    """模拟刘海（状态栏 47）+ 手势条（34）：导航栏、内容、底部主按钮都要让出安全区。"""
    notch, gesture = 47, 34
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_id = db.seed_ledger(user_id, unique_name("报表账本"))
        today = date.today()
        seed(db, user_id, ledger_id, 66, "餐饮美食", at(today, 12))

        run.check("进入自定义报表页并生成", go_report(page) and generate(page))
        page.evaluate(SET_SAFE_JS, [notch, gesture])
        wait_animations_settled(page)
        info_js = """() => {
            const r = (el) => { const b = el.getBoundingClientRect(); return { top: b.top, bottom: b.bottom, height: b.height }; };
            return {
                bar: r(document.querySelector('.ft-subbar')),
                nav: r(document.querySelector('[data-testid="nav-bar"]')),
                page: r(document.querySelector('[data-testid="report-page"]')),
                button: r(document.querySelector('[data-testid="report-generate"]')),
                foot: r(document.querySelector('[data-testid="report-foot"]')),
            };
        }"""
        geo = page.evaluate(info_js)
        run.check("【沉浸式】导航栏背景延伸到屏幕顶部，高 = 44 + 状态栏 47", abs(geo["bar"]["top"]) <= 1 and abs(geo["bar"]["height"] - (44 + notch)) <= 1, str(geo["bar"]))
        run.check("【沉浸式】导航行（返回 / 标题）在状态栏之下，高 44", abs(geo["nav"]["top"] - notch) <= 1 and abs(geo["nav"]["height"] - 44) <= 1, str(geo["nav"]))
        run.check("【沉浸式】内容紧贴导航栏下沿，没有被盖住", geo["page"]["top"] >= geo["bar"]["bottom"] - 1, f"{geo['page']} {geo['bar']}")
        wait_attr(page, "html", "data-statusbar-icons", "dark")
        run.check("【沉浸式】浅色页面 → 状态栏用深色图标", page.locator("html").get_attribute("data-statusbar-icons") == "dark")
        run.check(
            "【沉浸式】底部主按钮避开手势条（按钮底边在手势条之上），固定区底边仍贴屏幕底",
            geo["button"]["bottom"] <= SCREEN_H - gesture + 1 and abs(geo["foot"]["bottom"] - SCREEN_H) <= 1,
            f"{geo['button']} {geo['foot']}",
        )
        end = page.evaluate(SCROLL_TO_END_JS)
        run.check("【沉浸式】滚到底：最后一节完整露在固定的主按钮区上方（内容底部让出了手势条）", end["lastBottom"] <= end["footTop"] + 0.5, str(end))
        shot(page, "report-immersive-notch")

        page.evaluate(CLEAR_SAFE_JS)
        wait_animations_settled(page)
        restored = page.evaluate(info_js)
        run.check("【沉浸式】安全区还原为 0 后，导航栏回到 44 高、主按钮贴回底部", abs(restored["bar"]["height"] - 44) <= 1 and restored["button"]["bottom"] > geo["button"]["bottom"], f"{restored['bar']} {restored['button']}")
        run.check_clean(s.errors, "F 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- G. 窄屏


def check_narrow(run: TestRun, db: LocalDb) -> None:
    width = 320
    long_name = "这是一个名字特别特别长的自建分类用来测试换行" + unique_name("")
    with session(viewport={"width": width, "height": 640}) as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_name = "这是一个名字特别特别长的账本名称" + unique_name("")
        ledger_id = db.seed_ledger(user_id, ledger_name)
        today = date.today()
        custom = db.seed_category(user_id, long_name)

        seed(db, user_id, ledger_id, "1234567.89", "", at(today - timedelta(days=2), 12), category_id=custom)
        seed(db, user_id, ledger_id, "98765.43", "购物消费", at(today - timedelta(days=3), 12))
        seed(db, user_id, ledger_id, "98765432.10", "工资薪酬", at(today - timedelta(days=4), 12), income=True)
        seed(db, user_id, ledger_id, "987654.32", "", at(today - timedelta(days=8), 12), category_id=custom)

        run.check("进入自定义报表页", go_report(page))
        st = state(page)
        nav = page.evaluate(NAV_JS)
        run.check(
            "320 宽（空闲）：没有横向溢出、导航标题与返回按钮不重叠、芯片 / 日期行 / 账本行 / 主按钮都在屏幕内、主按钮高度 ≥ 44",
            st["overflowX"] <= 0 and nav["back"]["r"] <= nav["title"]["l"] + 0.5
            and all(c["box"]["l"] >= 0 and c["box"]["r"] <= width + 0.5 for c in st["chips"])
            and all(st[k]["box"]["l"] >= 0 and st[k]["box"]["r"] <= width + 0.5 for k in ("start", "end", "ledger"))
            and st["gen"]["box"]["l"] >= 0 and st["gen"]["box"]["r"] <= width + 0.5 and st["gen"]["box"]["h"] >= 44,
            f"{st['overflowX']} {nav} {[c['box'] for c in st['chips']]}",
        )
        run.check("320 宽（空闲）：账本名称很长时账本行不撑破一行", st["ledger"]["value"] == "全部账本" and st["ledger"]["box"]["r"] <= width + 0.5)

        tid(page, "report-ledger-row").click()
        tid(page, "report-ledger-sheet").wait_for(state="visible")
        wait_animations_settled(page)
        page.locator(f'[data-testid="report-ledger-option"][data-ledger-id="{ledger_id.lower()}"]').click()
        tid(page, "report-ledger-sheet").wait_for(state="detached")
        st = state(page)
        run.check("320 宽：选上长账本名后账本行的值省略 / 换行，没有撑出屏幕", st["ledger"]["box"]["r"] <= width + 0.5 and st["overflowX"] <= 0, f"{st['ledger']['box']} {st['overflowX']}")

        # ---- 不合法 ----
        tid(page, "report-start").fill("")
        wait_error(page, START_REQUIRED_TEXT)
        st = state(page)
        run.check(
            "320 宽（不合法）：红字提示在屏幕内、没有横向溢出，主按钮置灰",
            st["error"]["box"]["l"] >= 0 and st["error"]["box"]["r"] <= width + 0.5 and st["overflowX"] <= 0 and st["gen"]["disabled"],
            str(st["error"]),
        )
        shot(page, "report-invalid-320")
        set_range(page, today - timedelta(days=5), today)

        # ---- 结果 ----
        run.check("320 宽：生成", generate(page))
        wait_animations_settled(page)
        res = result(page)
        rows = res["rows"]
        run.check("320 宽（结果）：没有横向溢出", res["overflowX"] <= 0, str(res["overflowX"]))
        run.check(
            "320 宽（结果）：KPI 三栏里的金额都不挤出栏（大额自动缩小字号）",
            all(c["amount"]["l"] >= c["cell"]["l"] - 0.5 and c["amount"]["r"] <= c["cell"]["r"] + 0.5 for c in res["kpiCells"]),
            str(res["kpiCells"]),
        )
        run.check(
            "320 宽（结果）：环比的长分类名换行显示，标题 / 副标题不压到右侧的变化文字，整行在屏幕内",
            len(rows) == 2 and all(r["box"]["l"] >= 0 and r["box"]["r"] <= width + 0.5 and r["main"]["r"] <= r["trail"]["l"] + 0.5 for r in rows),
            str([(r["name"][:6], r["box"], r["main"], r["trail"]) for r in rows]),
        )
        run.check(
            "320 宽（结果）：图例行都在屏幕内",
            all(r["box"]["l"] >= 0 and r["box"]["r"] <= width + 0.5 for r in res["expenseLegend"] + res["incomeLegend"]),
            str([r["box"] for r in res["expenseLegend"]]),
        )
        run.check("320 宽（结果）：每屏玻璃层 ≤ 3", page.evaluate(GLASS_LAYERS_JS) <= 3, str(page.evaluate(GLASS_LAYERS_JS)))
        shot(page, "report-result-320")
        run.check_clean(s.errors, "G 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- main

SECTIONS = {
    "A": check_shell_and_empty,
    "B": check_validation,
    "C": check_quick_and_dates,
    "D": check_results_export_share,
    "E": check_ledger_filter,
    "F": check_immersive,
    "G": check_narrow,
}


def main() -> int:
    """不带参数跑全部小节；带小节名（如 `python custom_report_page_test.py D E`）只跑指定小节，便于定位问题。"""
    require_server()
    run = TestRun("custom-report-page")
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
