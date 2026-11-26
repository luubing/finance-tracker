# -*- coding: utf-8 -*-
"""
E2E 测试：统计 Tab · 趋势与年度重做 + 旧地址兼容（iOS UI 重构 · 12 号 ticket）。

只覆盖网页层——Android / iOS 上的真机手感（图表在手机上的可读性、在图表上纵向滑动是否卡住页面滚动、时间范围菜单与系统手势条的配合）
见 ticket 12 的人工验收清单。月度分段与统计壳（大标题 / 分段 / 账本胶囊 / 刷新）由 statistics_page_test.py 负责。

  R. 旧地址：直接访问 /trend、/annual-report（含查询串）由宿主 302 到 /statistics 对应分段，不残留二级页 / 占位页；
     （应用内压栈到旧地址的路径——原来由「我的」里的两个入口触发——随 13 号迁出入口后没有调用方，E2E 无法再触发，不再覆盖）
     落地后换分段、进自定义报表再返回仍停在换到的分段（地址里残留的 ?segment= 不能把用户的选择冲掉）；Tab 切走再切回保持用户停留的分段
  T. 趋势：唯一账本下造一批跨度 400 天的账单，逐项核对——
     入口与筛选行（时间范围胶囊在左、账本胶囊在右）、KPI / 明细表 / 三张折线图（ECharts 的实际数据与颜色）与造数一致
     （折线图的横轴补零：从窗口起点所在的日 / 周 / 月走到今天所在的日 / 周 / 月，每个桶一个点，没有账单的桶是 0；明细表仍只列有账单的桶）；
     时间范围菜单（四项、选中对勾、取消不变、选中即关闭）× 四个范围；时间维度 按日 / 按周 / 按月（KPI 不变、行按周一 / 月初聚合）；
     明细表排序三态（升序 → 降序 → 取消）与 aria-sort、「显示更多」逐批展开；空账本的空状态；
     分段 / Tab 来回切换后范围与维度保持；布局（命中区域、无横向溢出、玻璃层 ≤ 3、底部让出 Tab 栏）
  Y. 年度：KPI、同比（当年「年初至今 vs 去年同期」、已结束年份比整年、去年同期为 0 显示「—」+「无对比基数」、涨跌语义色）、
     月度趋势折线（ECharts 数据）、「支出构成」「收入构成」两个圆环与图例（各自占本类合计；第 2 轮评审后由原来收支混合的「分类统计」拆开）；
     年份胶囊（上一年 / 下一年 / 2000–2100 边界）；
     账本筛选对同比与各节生效；空年份；年份跨分段保持；变化率很长时缩小字号不挤出
  F. 刷新：趋势 / 年度分段的下拉刷新（唯一的刷新入口）只重读本页数据、不向云端发请求

可观察属性（已登记到 README）：stats-range-pill（data-value）、stats-range-sheet、stats-range-option（data-value、aria-current）、
stats-year（-prev / -next / -label）、stats-redirect、
stats-trend（data-range / data-dimension / data-state）→ trend-dimension（data-value）→ segment、trend-kpi、trend-total-expense / -income / -net、
trend-expense / trend-income / trend-net / trend-table-panel（data-state = loading | ready | empty）、trend-table → trend-sort（data-column，所在 th 的 aria-sort）、
trend-row（data-date / data-expense / data-income / data-net）、trend-more；
stats-annual（data-year / data-state）→ annual-kpi、annual-total-expense / -income / -net、annual-yoy → annual-yoy-scope / annual-yoy-period、
annual-yoy-expense / annual-yoy-income（data-tone）→ -rate / -base / -nobase、annual-monthly、
annual-{expense|income}-header / annual-{expense|income}-composition → annual-{key}-donut / annual-{key}-total / annual-{key}-legend / annual-{key}-empty。
"""
import calendar
import sys
from datetime import date, datetime, timedelta
from decimal import ROUND_HALF_EVEN, ROUND_HALF_UP, Decimal

from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

from e2e_base import (
    GLASS_LAYERS_JS,
    BASE_URL,
    BILL_TYPE_INCOME,
    PHONE_VIEWPORT,
    LocalDb,
    TestRun,
    active_tab,
    days_ago,
    login,
    open_app,
    open_tab,
    pull_down,
    pull_refresh,
    require_server,
    scroll_root_to,
    session,
    shot,
    tab_bar,
    unique_name,
    wait_animations_settled,
    wait_secondary_pages_closed,
    wait_tab_bar,
    wait_url,
)
from fake_cloud import CloudPortBusy, FakeCloud

SCREEN_W = PHONE_VIEWPORT["width"]

RGB_WHITE = "rgb(255, 255, 255)"
RGB_LABEL = "rgb(0, 0, 0)"  # --ft-label
RGB_GOOD = "rgb(24, 133, 64)"  # --ft-income-text #188540
RGB_BAD = "rgb(211, 34, 26)"  # --ft-danger-text #D3221A
HEX_EXPENSE, HEX_INCOME, HEX_NET = "#E8362C", "#1FA544", "#4318FF"  # DesignTokens.ExpenseColor / IncomeColor / BrandColor

OVERFLOW_JS = "() => document.documentElement.scrollWidth - document.documentElement.clientWidth"

FILTERS_JS = """() => {
  const row = document.querySelector('[data-testid="stats-filters"]');
  const rr = row.getBoundingClientRect();
  const box = (id) => {
    const el = row.querySelector(`[data-testid="${id}"]`);
    if (!el) return null;
    const r = el.getBoundingClientRect();
    return { left: r.left, right: r.right, width: r.width, height: r.height };
  };
  return { row: { left: rr.left, right: rr.right, width: rr.width }, range: box('stats-range-pill'), ledger: box('stats-ledger-pill'), year: box('stats-year'), period: box('stats-period') };
}"""

RANGE_PILL_JS = """() => {
  const pill = document.querySelector('[data-testid="stats-range-pill"]');
  if (!pill) return null;
  const r = pill.getBoundingClientRect();
  return {
    tag: pill.tagName, value: pill.getAttribute('data-value'), aria: pill.getAttribute('aria-label'), haspopup: pill.getAttribute('aria-haspopup'),
    text: pill.textContent.trim(), height: r.height, bg: getComputedStyle(pill).backgroundColor,
  };
}"""

RANGE_SHEET_JS = """() => {
  const sheet = document.querySelector('[data-testid="stats-range-sheet"]');
  if (!sheet) return null;
  const title = sheet.querySelector('.ft-actionsheet__title');
  const group = sheet.querySelector('.ft-actionsheet__group');
  const gr = group.getBoundingClientRect();
  return {
    title: title ? title.textContent.trim() : null,
    options: [...sheet.querySelectorAll('[data-testid="stats-range-option"]')].map((el) => ({
      value: el.getAttribute('data-value'), text: el.textContent.trim(), current: el.getAttribute('aria-current'), check: !!el.querySelector('.mdi-check'),
      height: el.getBoundingClientRect().height,
    })),
    role: sheet.getAttribute('role'), modal: sheet.getAttribute('aria-modal'),
    group: { top: gr.top, bottom: gr.bottom },
    viewport: window.innerHeight,
  };
}"""

TREND_STATE_JS = """() => {
  const root = document.querySelector('[data-testid="stats-trend"]');
  const amount = (id) => { const el = document.querySelector(`[data-testid="${id}"]`); return el ? el.getAttribute('data-amount') : null; };
  const table = document.querySelector('[data-testid="trend-table"]');
  const rows = table ? [...table.querySelectorAll('[data-testid="trend-row"]')].map((tr) => ({
    date: tr.getAttribute('data-date'), expense: tr.getAttribute('data-expense'), income: tr.getAttribute('data-income'), net: tr.getAttribute('data-net'),
    label: tr.querySelector('th').textContent.trim(), cells: [...tr.querySelectorAll('td')].map((td) => td.textContent.trim()),
  })) : [];
  const heads = table ? [...table.querySelectorAll('th[scope="col"]')].map((th) => {
    const b = th.querySelector('button');
    const r = b.getBoundingClientRect();
    return { text: th.textContent.trim(), sort: th.getAttribute('aria-sort'), column: b.getAttribute('data-column'), on: b.classList.contains('ft-table__sort--on'),
             icon: (th.querySelector('i') || {}).className || '', width: r.width, height: r.height };
  }) : [];
  const more = document.querySelector('[data-testid="trend-more"]');
  const pill = document.querySelector('[data-testid="stats-range-pill"]');
  const dim = document.querySelector('[data-testid="trend-dimension"]');
  const wrap = table ? table.parentElement : null;
  return {
    state: root && root.getAttribute('data-state'), range: root && root.getAttribute('data-range'), dimension: root && root.getAttribute('data-dimension'),
    kpi: { expense: amount('trend-total-expense'), income: amount('trend-total-income'), net: amount('trend-total-net') },
    rows, heads, more: more ? more.textContent.trim() : null, moreHeight: more ? more.getBoundingClientRect().height : null,
    pill: pill ? pill.textContent.trim() : null, pillValue: pill ? pill.getAttribute('data-value') : null,
    dimValue: dim ? dim.getAttribute('data-value') : null,
    dimSegments: dim ? [...dim.querySelectorAll('[data-testid="segment"]')].map((b) => ({ text: b.textContent.trim(), value: b.getAttribute('data-value'), pressed: b.getAttribute('aria-pressed'), height: b.getBoundingClientRect().height })) : [],
    dimLabel: dim ? dim.getAttribute('aria-label') : null,
    panels: ['trend-expense', 'trend-income', 'trend-net', 'trend-table-panel'].map((id) => { const p = document.querySelector(`[data-testid="${id}"]`); return p ? p.getAttribute('data-state') : null; }),
    tableEmpty: (document.querySelector('[data-testid="trend-table-panel-empty"]') || {}).textContent || null,
    chartEmpty: ['trend-expense', 'trend-income', 'trend-net'].map((id) => { const e = document.querySelector(`[data-testid="${id}-empty"]`); return e ? e.textContent.trim() : null; }),
    wrapOverflow: wrap ? wrap.scrollWidth - wrap.clientWidth : null,
    kpiLabels: [...document.querySelectorAll('[data-testid="trend-kpi"] .ft-kpi__label')].map((el) => el.textContent.trim()),
  };
}"""

CHART_JS = """(panelId) => {
  const panel = document.querySelector(`[data-testid="${panelId}"]`);
  if (!panel || typeof window.echarts === 'undefined') return null;
  const dom = [panel, ...panel.querySelectorAll('div')].find((d) => window.echarts.getInstanceByDom(d));
  if (!dom) return null;
  const opt = window.echarts.getInstanceByDom(dom).getOption();
  const r = dom.getBoundingClientRect();
  const pr = panel.getBoundingClientRect();
  const label = (d) => (typeof d === 'object' && d !== null ? d.value : d);
  return {
    width: r.width, height: r.height, panelWidth: pr.width, hasCanvas: !!dom.querySelector('canvas'),
    x: opt.xAxis[0].data.map(label),
    series: opt.series.map((s) => ({ name: s.name, type: s.type, data: s.data.map(Number), color: s.lineStyle && s.lineStyle.color })),
    legend: opt.legend && opt.legend[0] ? opt.legend[0].data.map((d) => (typeof d === 'object' ? d.name : d)) : null,
  };
}"""

CHART_MATCH_JS = """([id, labels, series]) => {
  const panel = document.querySelector(`[data-testid="${id}"]`);
  if (!panel || typeof window.echarts === 'undefined') return false;
  const dom = [panel, ...panel.querySelectorAll('div')].find((d) => window.echarts.getInstanceByDom(d));
  if (!dom) return false;
  const opt = window.echarts.getInstanceByDom(dom).getOption();
  const label = (d) => (typeof d === 'object' && d !== null ? d.value : d);
  return JSON.stringify(opt.xAxis[0].data.map(label)) === JSON.stringify(labels)
    && JSON.stringify(opt.series.map((s) => s.data.map(Number))) === JSON.stringify(series);
}"""

ANNUAL_STATE_JS = """() => {
  const root = document.querySelector('[data-testid="stats-annual"]');
  const amount = (id) => { const el = document.querySelector(`[data-testid="${id}"]`); return el ? el.getAttribute('data-amount') : null; };
  const text = (id) => { const el = document.querySelector(`[data-testid="${id}"]`); return el ? el.textContent.trim() : null; };
  const cell = (id) => {
    const el = document.querySelector(`[data-testid="${id}"]`);
    if (!el) return null;
    const rate = document.querySelector(`[data-testid="${id}-rate"]`);
    const base = document.querySelector(`[data-testid="${id}-base"]`);
    const nobase = document.querySelector(`[data-testid="${id}-nobase"]`);
    const cr = el.getBoundingClientRect();
    return {
      tone: el.getAttribute('data-tone'), rate: rate.textContent.trim(), rateColor: getComputedStyle(rate).color, rateClass: rate.className,
      rateOverflow: rate.scrollWidth - rate.clientWidth, base: base ? base.getAttribute('data-amount') : null, nobase: nobase ? nobase.textContent.trim() : null,
      left: cr.left, right: cr.right, width: cr.width,
    };
  };
  const year = document.querySelector('[data-testid="stats-year"]');
  const yr = year ? year.getBoundingClientRect() : null;
  const card = document.querySelector('[data-testid="annual-yoy"]');
  const kr = card ? card.getBoundingClientRect() : null;
  // 构成：支出 / 收入各一节（annual-{key}-header / -composition / -donut / -total / -legend / -empty）
  const composition = (key) => {
    const arcs = [...document.querySelectorAll(`[data-testid="annual-${key}-donut"] [data-testid="donut-arc"]`)].map((el) => ({
      name: el.getAttribute('data-name'), fraction: parseFloat(el.getAttribute('data-fraction')), stroke: getComputedStyle(el).stroke,
    }));
    const rows = [...document.querySelectorAll(`[data-testid="annual-${key}-legend"] [data-testid="legend-row"]`)].map((el) => ({
      name: el.getAttribute('data-name'), amount: el.getAttribute('data-amount'), percent: el.getAttribute('data-percent'), count: el.getAttribute('data-count'),
      pct: el.querySelector('.ft-legend__pct').textContent.trim(), dot: getComputedStyle(el.querySelector('.ft-dot')).backgroundColor,
    }));
    const donut = document.querySelector(`[data-testid="annual-${key}-donut"]`);
    const total = document.querySelector(`[data-testid="annual-${key}-total"]`);
    const panel = document.querySelector(`[data-testid="annual-${key}-composition"]`);
    const legend = document.querySelector(`[data-testid="annual-${key}-legend"]`);
    const dr = donut ? donut.getBoundingClientRect() : null;
    const tr = total ? total.getBoundingClientRect() : null;
    return {
      title: text(`annual-${key}-header`), panelState: panel ? panel.getAttribute('data-state') : null,
      arcs, rows, total: amount(`annual-${key}-total`), legendLabel: legend ? legend.getAttribute('aria-label') : null,
      caption: (document.querySelector(`[data-testid="annual-${key}-donut"] .ft-donut__caption`) || {}).textContent || null,
      note: (panel ? panel.querySelector('.ft-panel__note') : null) ? panel.querySelector('.ft-panel__note').textContent : null,
      empty: text(`annual-${key}-empty`),
      donutBox: dr && { cx: (dr.left + dr.right) / 2, width: dr.width },
      totalBox: tr && { left: tr.left, right: tr.right, width: tr.width },
    };
  };
  const ykpi = document.querySelector('[data-testid="annual-kpi"]');
  return {
    state: root && root.getAttribute('data-state'), year: root && root.getAttribute('data-year'),
    kpi: { expense: amount('annual-total-expense'), income: amount('annual-total-income'), net: amount('annual-total-net') },
    kpiLabels: ykpi ? [...ykpi.querySelectorAll('.ft-kpi__label')].map((el) => el.textContent.trim()) : [],
    scope: text('annual-yoy-scope'), period: text('annual-yoy-period'),
    expenseCell: cell('annual-yoy-expense'), incomeCell: cell('annual-yoy-income'),
    yoyCard: kr && { left: kr.left, right: kr.right },
    panels: ['annual-yoy', 'annual-monthly', 'annual-expense-composition', 'annual-income-composition'].map((id) => { const p = document.querySelector(`[data-testid="${id}"]`); return p ? p.getAttribute('data-state') : null; }),
    monthlyEmpty: text('annual-monthly-empty'),
    expense: composition('expense'), income: composition('income'),
    oldComposition: ['annual-composition', 'annual-composition-header', 'annual-donut', 'annual-legend'].filter((id) => document.querySelector(`[data-testid="${id}"]`)),
    pill: year ? { label: text('stats-year-label'), prevAria: (document.querySelector('[data-testid="stats-year-prev"]') || {}).getAttribute('aria-label'), nextAria: (document.querySelector('[data-testid="stats-year-next"]') || {}).getAttribute('aria-label'),
                   prevDisabled: document.querySelector('[data-testid="stats-year-prev"]').disabled, nextDisabled: document.querySelector('[data-testid="stats-year-next"]').disabled,
                   role: year.getAttribute('role'), aria: year.getAttribute('aria-label'), left: yr.left, height: yr.height } : null,
  };
}"""

PAGE_STATE_JS = """() => ({
  url: location.pathname + location.search,
  stackItems: document.querySelectorAll('.m-page-stack-item').length,
  redirects: document.querySelectorAll('[data-testid="stats-redirect"]').length,
  segment: (document.querySelector('[data-testid="stats-segment"]') || {getAttribute: () => null}).getAttribute('data-value'),
  trend: !!document.querySelector('[data-testid="stats-trend"]'),
  annual: !!document.querySelector('[data-testid="stats-annual"]'),
  monthly: !!document.querySelector('[data-testid="stats-monthly"]'),
})"""


# ---------------------------------------------------------------------------------------------- 小工具


def approx(actual, expected, tolerance: float = 1.0) -> bool:
    return actual is not None and abs(actual - expected) <= tolerance


def tid(page, testid: str):
    return page.get_by_test_id(testid)


def plain(value) -> str:
    """金额 → data-amount 的写法：两位小数、无千分位。"""
    return f"{Decimal(str(value)):.2f}"


def money(value) -> str:
    """金额 → 界面文字（¥ 加千分位加两位小数，负数是 -¥）。"""
    d = Decimal(str(value))
    return f"-¥{abs(d):,.2f}" if d < 0 else f"¥{d:,.2f}"


def percent_text(amount, total) -> "tuple[Decimal, str]":
    """统计服务的占比（两位小数，银行家舍入）与图例上的占比文字（一位小数，四舍五入；不足 0.1% 显示 <0.1%）。"""
    two = (Decimal(str(amount)) / Decimal(str(total)) * 100).quantize(Decimal("0.01"), rounding=ROUND_HALF_EVEN)
    one = two.quantize(Decimal("0.1"), rounding=ROUND_HALF_UP)
    return two, ("<0.1%" if 0 < two < Decimal("0.05") else f"{one}%")


def rate_text(current, previous) -> str:
    """同比变化率文字：去年同期为 0 是「—」；一位小数、四舍五入远离零、正数带 +（与 StatisticsPresentation.FormatChangeRate 一致）。"""
    previous = Decimal(str(previous))
    if previous == 0:
        return "—"
    rate = ((Decimal(str(current)) - previous) / previous * 100).quantize(Decimal("0.1"), rounding=ROUND_HALF_UP)
    if rate == 0:
        return "0.0%"
    return f"+{rate}%" if rate > 0 else f"{rate}%"


def wait_ready(page, view: str, attrs: "dict | None" = None) -> None:
    """视图（stats-trend / stats-annual）加载完：data-state = ready 且指定属性都已是新值（属性与状态在同一次渲染里变化）。"""
    page.wait_for_function(
        """([id, attrs]) => {
          const el = document.querySelector(`[data-testid="${id}"]`);
          if (!el || el.getAttribute('data-state') !== 'ready') return false;
          return Object.entries(attrs).every(([k, v]) => el.getAttribute(k) === v);
        }""",
        arg=[view, attrs or {}],
    )


def go_segment(page, segment: str, attrs: "dict | None" = None) -> None:
    """整页进入统计 Tab 的某个分段（造数要在这之前做：Tab 根页面首次进入时读库）并等视图加载完。"""
    open_app(page, f"/statistics?segment={segment}")
    wait_tab_bar(page)
    wait_ready(page, f"stats-{segment}", attrs)
    wait_animations_settled(page)


def segment_button(page, value: str):
    return tid(page, "stats-segment").locator(f'[data-testid="segment"][data-value="{value}"]')


def switch_segment(page, value: str, attrs: "dict | None" = None) -> None:
    segment_button(page, value).click()
    page.wait_for_function(
        "(v) => document.querySelector('[data-testid=\"stats-segment\"]').getAttribute('data-value') === v", arg=value
    )
    if value != "monthly":
        wait_ready(page, f"stats-{value}", attrs)
    wait_animations_settled(page)


def open_ledger_sheet(page) -> None:
    tid(page, "stats-ledger-pill").click()
    tid(page, "stats-ledger-sheet").wait_for(state="visible")
    wait_animations_settled(page)


def pick_ledger(page, ledger_id=None) -> None:
    """打开账本菜单选一项（ledger_id 为 None 是「全部账本」），选中后菜单自动收起。"""
    open_ledger_sheet(page)
    if ledger_id is None:
        tid(page, "stats-ledger-all").click()
    else:
        page.locator(f'[data-testid="stats-ledger-option"][data-ledger-id="{ledger_id.lower()}"]').click()
    tid(page, "stats-ledger-sheet").wait_for(state="detached")


def open_range_sheet(page) -> None:
    tid(page, "stats-range-pill").click()
    tid(page, "stats-range-sheet").wait_for(state="visible")
    wait_animations_settled(page)


def pick_range(page, value: str) -> None:
    """打开时间范围菜单选一项：选中即收起，视图按新范围重新加载完。"""
    open_range_sheet(page)
    page.locator(f'[data-testid="stats-range-option"][data-value="{value}"]').click()
    tid(page, "stats-range-sheet").wait_for(state="detached")
    wait_ready(page, "stats-trend", {"data-range": value})
    wait_animations_settled(page)


def pick_dimension(page, value: str) -> None:
    tid(page, "trend-dimension").locator(f'[data-testid="segment"][data-value="{value}"]').click()
    wait_ready(page, "stats-trend", {"data-dimension": value})
    wait_animations_settled(page)


def has_echarts(page) -> bool:
    return bool(page.evaluate("() => typeof window.echarts !== 'undefined'"))


def wait_chart(page, panel_id: str, labels: list, series: list) -> bool:
    """等图表实例的横轴标签与每条线的数据都等于期望（ECharts 在组件渲染后经 JS 互操作才初始化，所以要等）。"""
    try:
        page.wait_for_function(CHART_MATCH_JS, arg=[panel_id, labels, series], timeout=10_000)
        return True
    except PlaywrightTimeoutError:
        return False


def wait_kpi(page, prefix: str, expense, income) -> None:
    page.wait_for_function(
        """([e, i, p]) => {
          const get = (id) => { const el = document.querySelector(`[data-testid="${id}"]`); return el ? el.getAttribute('data-amount') : null; };
          return get(`${p}-total-expense`) === e && get(`${p}-total-income`) === i;
        }""",
        arg=[plain(expense), plain(income), prefix],
    )


def amount_of(page, testid: str):
    return tid(page, testid).get_attribute("data-amount")


# ---------------------------------------------------------------------------------------------- 趋势：造数与期望

EXPENSE, INCOME = "expense", "income"

#: 趋势造数：(几天前, 类型, 金额)。天数避开各范围的边界（7 / 30 / 90 / 365 天）：
#: 窗口 = [现在 − N 天, 现在]，账单在「几天前的 12:00」，所以天数 ≤ N−1 一定在窗口内、≥ N+1 一定在窗口外
TREND_SEEDS = [(d, EXPENSE, Decimal(d * 10) + Decimal("0.50")) for d in (1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 20, 40, 60, 100, 200, 400)] + [
    (d, INCOME, Decimal(d * 3 + 1)) for d in (2, 4, 10, 40, 200, 400)
]

#: 每个范围里最远的账单是「几天前」（含）
WINDOW_MAX_DAYS = {"7days": 6, "30days": 29, "90days": 89, "12months": 364}

RANGES = [("7days", "最近7天"), ("30days", "最近30天"), ("90days", "最近90天"), ("12months", "最近12个月")]


def seed_trend(db: LocalDb, user_id: str, ledger_id: str) -> None:
    expense_cat = db.category_id_by_name("餐饮美食")
    income_cat = db.category_id_by_name("工资薪酬", BILL_TYPE_INCOME)
    for days, kind, amount in TREND_SEEDS:
        if kind == EXPENSE:
            db.seed_bill(user_id, amount=plain(amount), ledger_id=ledger_id, category_id=expense_cat, when=days_ago(days))
        else:
            db.seed_bill(user_id, amount=plain(amount), ledger_id=ledger_id, bill_type=BILL_TYPE_INCOME, category_id=income_cat, when=days_ago(days))


def week_start(day: date) -> date:
    return day - timedelta(days=day.weekday())


def expected_trend(range_value: str, dimension: str, today: date, seeds=None) -> "list[tuple[date, Decimal, Decimal]]":
    """期望的明细行（日期, 支出, 收入），按日期升序；日期是按日 / 周一 / 月初。"""
    buckets: dict = {}
    for days, kind, amount in seeds or TREND_SEEDS:
        if days > WINDOW_MAX_DAYS[range_value]:
            continue
        day = today - timedelta(days=days)
        key = {"day": day, "week": week_start(day), "month": day.replace(day=1)}[dimension]
        expense, income = buckets.get(key, (Decimal(0), Decimal(0)))
        buckets[key] = (expense + amount, income) if kind == EXPENSE else (expense, income + amount)
    return [(key, *buckets[key]) for key in sorted(buckets)]


#: 各范围的窗口起点 = 现在往前推这么多天（12 个月是往前推 12 个月，见 window_start）
WINDOW_BACK_DAYS = {"7days": 7, "30days": 30, "90days": 90}


def window_start(range_value: str, today: date) -> date:
    """窗口起点所在的那一天（与 StatisticsPresentation.TrendRangeBounds 一致：7 / 30 / 90 天往前推；12 个月 = AddMonths(-12)，
    今天的日号超过目标月的天数时夹到目标月的最后一天）"""
    if range_value == "12months":
        year, month = today.year - 1, today.month
        return date(year, month, min(today.day, calendar.monthrange(year, month)[1]))
    return today - timedelta(days=WINDOW_BACK_DAYS[range_value])


def bucket_start(day: date, dimension: str) -> date:
    return {"day": day, "week": week_start(day), "month": day.replace(day=1)}[dimension]


def next_bucket(bucket: date, dimension: str) -> date:
    if dimension == "day":
        return bucket + timedelta(days=1)
    if dimension == "week":
        return bucket + timedelta(days=7)
    return date(bucket.year + (1 if bucket.month == 12 else 0), bucket.month % 12 + 1, 1)


def expected_axis(range_value: str, dimension: str, today: date, seeds=None) -> "list[tuple[date, Decimal, Decimal]]":
    """折线图期望的数据点（日期, 支出, 收入）：横轴补零——从窗口起点所在的桶走到今天所在的桶，每桶一个点，没有账单的桶是 0。
    （明细表用 expected_trend，只列有账单的桶。）"""
    rows = {key: (expense, income) for key, expense, income in expected_trend(range_value, dimension, today, seeds)}
    points: "list[tuple[date, Decimal, Decimal]]" = []
    bucket, last = bucket_start(window_start(range_value, today), dimension), bucket_start(today, dimension)
    while bucket <= last:
        points.append((bucket, *rows.get(bucket, (Decimal(0), Decimal(0)))))
        bucket = next_bucket(bucket, dimension)
    return points


def date_label(day: date, dimension: str) -> str:
    return day.strftime("%Y-%m" if dimension == "month" else "%Y-%m-%d")


def axis_label(day: date, dimension: str) -> str:
    return day.strftime("%y-%m" if dimension == "month" else "%m-%d")


def row_view(row, dimension: str) -> dict:
    day, expense, income = row
    net = income - expense
    return {
        "date": day.isoformat(), "expense": plain(expense), "income": plain(income), "net": plain(net),
        "label": date_label(day, dimension), "cells": [money(expense), money(income), money(net)],
    }


def totals(rows) -> "tuple[Decimal, Decimal]":
    return sum((e for _, e, _ in rows), Decimal(0)), sum((i for _, _, i in rows), Decimal(0))


def verify_trend(run: TestRun, page, label: str, range_value: str, dimension: str, today: date, *, charts: bool = True) -> None:
    """页面上的趋势视图 = 造数按（范围, 维度）算出来的期望：KPI、明细表前 10 行与「显示更多」、胶囊文字、三张图的数据。"""
    expected = expected_trend(range_value, dimension, today)
    total_expense, total_income = totals(expected)
    state = page.evaluate(TREND_STATE_JS)
    shown = expected[:10]
    remaining = len(expected) - len(shown)
    run.check(
        f"{label}：KPI 支出 {plain(total_expense)} / 收入 {plain(total_income)} / 结余 {plain(total_income - total_expense)}",
        state["kpi"] == {"expense": plain(total_expense), "income": plain(total_income), "net": plain(total_income - total_expense)},
        str(state["kpi"]),
    )
    run.check(
        f"{label}：明细表 {len(expected)} 行，先显示 {len(shown)} 行（日期 / 支出 / 收入 / 净收支，按日期升序）",
        state["rows"] == [row_view(r, dimension) for r in shown],
        str([(r["date"], r["expense"], r["income"]) for r in state["rows"]]) + " 期望 " + str([(r[0].isoformat(), plain(r[1]), plain(r[2])) for r in shown]),
    )
    run.check(
        f"{label}：「显示更多」" + (f"（还有 {remaining} 条）" if remaining else "不出现（行数不超过 10）"),
        state["more"] == (f"显示更多（还有 {remaining} 条）" if remaining else None),
        str(state["more"]),
    )
    if not charts or not has_echarts(page):
        return
    # 折线图的横轴补零：每个日 / 周 / 月一个点（没有账单的补 0），与明细表只列有账单的桶不同
    axis = expected_axis(range_value, dimension, today)
    labels = [axis_label(r[0], dimension) for r in axis]
    zeros = sum(1 for r in axis if r[1] == 0 and r[2] == 0)
    specs = (
        ("trend-expense", "支出趋势", HEX_EXPENSE, [float(r[1]) for r in axis]),
        ("trend-income", "收入趋势", HEX_INCOME, [float(r[2]) for r in axis]),
        ("trend-net", "净收支趋势", HEX_NET, [float(r[2] - r[1]) for r in axis]),
    )
    for panel, name, color, values in specs:
        ok = wait_chart(page, panel, labels, [values])
        info = page.evaluate(CHART_JS, panel)
        run.check(
            f"{label}：「{name}」图的横轴补零（{len(axis)} 个点，其中 {zeros} 个没有账单补 0），数据与明细一致，线色 {color}",
            ok and info is not None and info["series"][0]["name"] == name and info["series"][0]["color"] == color and info["series"][0]["type"] == "line",
            str(info),
        )


# ---------------------------------------------------------------------------------------------- R. 旧地址


def check_back_keeps_segment(run: TestRun, page, label: str, other: str) -> None:
    """落地以后用户换到另一个分段、进自定义报表再返回：仍停在换到的分段。
    直接访问的地址会一直带着 ?segment=（宿主 302 的形态），返回时地址又回到那个形态——它不能把用户后来的选择冲掉。"""
    switch_segment(page, other)
    tid(page, "stats-report").click()
    wait_url(page, r"^/custom-report")
    wait_tab_bar(page, visible=False)
    page.go_back()
    wait_url(page, r"^/statistics")
    wait_tab_bar(page)
    wait_secondary_pages_closed(page)
    wait_animations_settled(page)
    state = page.evaluate(PAGE_STATE_JS)
    name = {"trend": "趋势", "annual": "年度"}[other]
    run.check(
        f"{label}落地后换到「{name}」，进自定义报表再返回：仍停在「{name}」（地址里残留的 ?segment= 不会把用户的选择冲掉）",
        state["segment"] == other and tid(page, f"stats-{other}").count() == 1 and state["stackItems"] == 0 and state["redirects"] == 0,
        str(state),
    )


def check_legacy_routes(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        db.seed_ledger(user_id, unique_name("旧地址账本"), sort_order=1)

        # ---- 宿主层：旧地址回 302，目标就是统计页的对应分段 ----
        for path, target in (("/trend", "/statistics?segment=trend"), ("/annual-report", "/statistics?segment=annual"), ("/trend?x=1", "/statistics?segment=trend")):
            response = page.request.get(f"{BASE_URL}{path}", max_redirects=0)
            location = response.headers.get("location", "")
            run.check(
                f"【宿主】GET {path} → 302，Location 指向 {target}",
                response.status == 302 and location.endswith(target) and "/trend" not in location.split("?")[0],
                f"{response.status} {location}",
            )

        # ---- 直接访问：落到统计页对应分段，没有空白中间页 ----
        for path, segment, view in (("/trend", "trend", "stats-trend"), ("/annual-report", "annual", "stats-annual"), ("/trend?x=1", "trend", "stats-trend")):
            open_app(page, path)
            wait_tab_bar(page)
            wait_ready(page, view)
            wait_animations_settled(page)
            state = page.evaluate(PAGE_STATE_JS)
            run.check(
                f"【直接访问 {path}】落到 /statistics?segment={segment}：Tab = 统计、分段 = {segment}、对应视图已加载，没有二级页 / 占位页",
                state["url"] == f"/statistics?segment={segment}" and active_tab(page) == "statistics" and state["segment"] == segment
                and state["stackItems"] == 0 and state["redirects"] == 0 and tab_bar(page).is_visible(),
                str(state),
            )
            pills = {name: tid(page, name).count() for name in ("stats-period", "stats-range-pill", "stats-year")}
            expected_pills = {"stats-period": 0, "stats-range-pill": int(segment == "trend"), "stats-year": int(segment == "annual")}
            run.check(f"【直接访问 {path}】筛选行与分段一致（趋势：范围胶囊；年度：年份胶囊；都没有月份胶囊）", pills == expected_pills, str(pills))
            if "?" not in path:
                check_back_keeps_segment(run, page, f"【直接访问 {path}】", "annual" if segment == "trend" else "trend")

        # 应用内压栈到旧地址（原来由「我的」里的「趋势分析」「年度报告」入口触发，由 StatisticsRedirect 跳转页接住）：
        # 13 号把这两个入口迁出后，应用内已经没有任何调用方；页面栈只认自己发起的导航，E2E 里用 Blazor 客户端导航触发不了它
        # （地址变了但页面栈不压栈、停在原页），所以这条路径不再有自动化覆盖——跳转页保留为安全网，见 ADR 0007 已知限制与 ticket 13。

        # ---- Tab 切走再切回：保持用户停留的分段，地址不带参数 ----
        open_app(page, "/statistics?segment=annual")
        wait_tab_bar(page)
        wait_ready(page, "stats-annual")
        open_tab(page, "首页")
        open_tab(page, "统计")
        wait_animations_settled(page)
        state = page.evaluate(PAGE_STATE_JS)
        run.check(
            "【Tab 切换】统计 → 首页 → 统计：仍停在「年度」分段（点 Tab 栏的地址不带分段参数，不会把用户重置回月度）",
            state["segment"] == "annual" and state["annual"] and state["url"] == "/statistics",
            str(state),
        )
        run.check_clean(s.errors, "R 节旧地址会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- T. 趋势


def check_trend(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        busy_name = unique_name("趋势账本")
        busy = db.seed_ledger(user_id, busy_name, sort_order=1)  # 唯一账本
        seed_trend(db, user_id, busy)
        today = datetime.now().date()

        go_segment(page, "trend", {"data-range": "7days", "data-dimension": "day"})
        shot(page, "t12-trend-default")

        # ================= 入口与筛选行 =================
        run.check(
            "【入口】直接进入 /statistics?segment=trend：Tab = 统计、分段 = 趋势；没有月份 / 年份胶囊；时间范围胶囊与账本胶囊都在；"
            "月度 / 年度视图不在 DOM 里；旧的 stats-embed 外层已移除",
            active_tab(page) == "statistics" and tid(page, "stats-segment").get_attribute("data-value") == "trend"
            and tid(page, "stats-period").count() == 0 and tid(page, "stats-year").count() == 0
            and tid(page, "stats-range-pill").count() == 1 and tid(page, "stats-ledger-pill").count() == 1
            and tid(page, "stats-monthly").count() == 0 and tid(page, "stats-annual").count() == 0 and tid(page, "stats-embed").count() == 0,
        )
        pill = page.evaluate(RANGE_PILL_JS)
        run.check(
            "【时间范围胶囊】按钮、文字「最近7天」、读屏名称「时间范围：最近7天，点按切换」、弹出对话框、高度 44、白底",
            pill["tag"] == "BUTTON" and pill["text"] == "最近7天" and pill["value"] == "最近7天" and pill["aria"] == "时间范围：最近7天，点按切换"
            and pill["haspopup"] == "dialog" and approx(pill["height"], 44) and pill["bg"] == RGB_WHITE,
            str(pill),
        )
        filters = page.evaluate(FILTERS_JS)
        rng, led, row = filters["range"], filters["ledger"], filters["row"]
        run.check(
            "【筛选行】时间范围胶囊靠左、账本胶囊靠右、两颗不重叠，各不超过一半宽",
            approx(rng["left"], row["left"], 1.5) and approx(led["right"], row["right"], 1.5) and rng["right"] < led["left"]
            and rng["width"] <= row["width"] / 2 and led["width"] <= row["width"] / 2,
            str(filters),
        )
        state = page.evaluate(TREND_STATE_JS)
        run.check(
            "【KPI 卡】三项标签 支出 / 收入 / 结余",
            state["kpiLabels"] == ["支出", "收入", "结余"],
            str(state["kpiLabels"]),
        )
        run.check(
            "【时间维度】分段控件「按日 / 按周 / 按月」，默认按日，读屏名称「时间维度」，每段高度 44",
            [(d["text"], d["value"], d["pressed"]) for d in state["dimSegments"]] == [("按日", "day", "true"), ("按周", "week", "false"), ("按月", "month", "false")]
            and state["dimLabel"] == "时间维度" and state["dimValue"] == "day" and all(approx(d["height"], 44) for d in state["dimSegments"]),
            str(state["dimSegments"]),
        )
        run.check(
            "【明细表】四列表头 日期 / 支出 / 收入 / 净收支，初始都没有排序（aria-sort = none）",
            [(h["text"], h["column"], h["sort"]) for h in state["heads"]]
            == [("日期", "date", "none"), ("支出", "expense", "none"), ("收入", "income", "none"), ("净收支", "net", "none")],
            str(state["heads"]),
        )

        # ================= 数据口径：最近 7 天 =================
        verify_trend(run, page, "【最近7天·按日】", "7days", "day", today)
        if has_echarts(page):
            # 不依赖 expected_axis 的实现，直接写死「补零」的样子：连续 8 天，没有账单的两头（起点那天、今天）也有点且值为 0
            info = page.evaluate(CHART_JS, "trend-expense")
            days = [today - timedelta(days=n) for n in range(7, -1, -1)]
            seeded = {n: float(Decimal(n * 10) + Decimal("0.50")) for n in (1, 2, 3, 4, 5, 6)}
            run.check(
                "【折线图横轴补零】最近7天·按日的支出图：连续 8 个点（起点那一天到今天），没有账单的起点日与今天也有点、值为 0；明细表只有有账单的 6 行",
                info is not None and info["x"] == [d.strftime("%m-%d") for d in days]
                and info["series"][0]["data"] == [seeded.get((today - d).days, 0.0) for d in days]
                and len(page.evaluate(TREND_STATE_JS)["rows"]) == 6,
                str(info),
            )

        # ================= 时间范围菜单 =================
        open_range_sheet(page)
        sheet = page.evaluate(RANGE_SHEET_JS)
        run.check(
            "【时间范围菜单】标题「选择时间范围」；四项 最近7天 / 30天 / 90天 / 12个月，当前项（7天）带对勾与 aria-current；对话框，在屏幕内",
            sheet["title"] == "选择时间范围"
            and [(o["value"], o["text"]) for o in sheet["options"]] == RANGES
            and [(o["value"], o["current"], o["check"]) for o in sheet["options"]]
            == [("7days", "true", True), ("30days", None, False), ("90days", None, False), ("12months", None, False)]
            and sheet["role"] == "dialog" and sheet["modal"] == "true" and 0 <= sheet["group"]["top"] and sheet["group"]["bottom"] <= sheet["viewport"]
            and all(o["height"] >= 44 for o in sheet["options"]),
            str(sheet),
        )
        shot(page, "t12-trend-range-sheet")
        tid(page, "actionsheet-cancel").click()
        tid(page, "stats-range-sheet").wait_for(state="detached")
        run.check(
            "【时间范围菜单】点「取消」收起，范围保持最近7天",
            page.evaluate(TREND_STATE_JS)["range"] == "7days" and page.evaluate(RANGE_PILL_JS)["text"] == "最近7天",
        )

        for value, text in RANGES[1:]:
            pick_range(page, value)
            state = page.evaluate(TREND_STATE_JS)
            run.check(
                f"【选范围 {text}】菜单收起；胶囊文字「{text}」；视图 data-range = {value}",
                state["range"] == value and state["pill"] == text and state["pillValue"] == text and tid(page, "stats-range-sheet").count() == 0,
                str((state["range"], state["pill"])),
            )
            verify_trend(run, page, f"【{text}·按日】", value, "day", today)
            if value == "30days":
                shot(page, "t12-trend-30days")
                # 当前项换到 30 天：再打开菜单对勾跟着走
                open_range_sheet(page)
                sheet = page.evaluate(RANGE_SHEET_JS)
                run.check(
                    "【时间范围菜单】选了30天后再打开，对勾与 aria-current 在「最近30天」上",
                    [(o["value"], o["current"], o["check"]) for o in sheet["options"]]
                    == [("7days", None, False), ("30days", "true", True), ("90days", None, False), ("12months", None, False)],
                    str(sheet["options"]),
                )
                tid(page, "actionsheet-cancel").click()
                tid(page, "stats-range-sheet").wait_for(state="detached")

        # ================= 明细表：排序三态 + 显示更多（12 个月 · 17 行）=================
        expected = expected_trend("12months", "day", today)
        state = page.evaluate(TREND_STATE_JS)
        run.check(f"【明细表】12 个月 {len(expected)} 行：先显示 10 行，「显示更多」按钮高度 ≥ 44", len(state["rows"]) == 10 and state["moreHeight"] is not None and state["moreHeight"] >= 44, str(len(state["rows"])))

        def head(column: str):
            return tid(page, "trend-table").locator(f'[data-testid="trend-sort"][data-column="{column}"]')

        def sort_state():
            st = page.evaluate(TREND_STATE_JS)
            return st, {h["column"]: h["sort"] for h in st["heads"]}, [r["date"] for r in st["rows"]]

        def wait_sort(column: str, value: str) -> None:
            page.wait_for_function(
                "([c, v]) => document.querySelector(`[data-testid=\"trend-sort\"][data-column=\"${c}\"]`).closest('th').getAttribute('aria-sort') === v",
                arg=[column, value],
            )

        by_expense = sorted(expected, key=lambda r: r[1])
        head("expense").click()
        wait_sort("expense", "ascending")
        st, sorts, dates = sort_state()
        run.check(
            "【排序】点「支出」：升序（aria-sort = ascending，只有这一列），箭头向上、表头变品牌色；行 = 全部行按支出升序取前 10",
            sorts == {"date": "none", "expense": "ascending", "income": "none", "net": "none"}
            and [h["icon"] for h in st["heads"] if h["column"] == "expense"][0].endswith("mdi-arrow-up") and [h["on"] for h in st["heads"]] == [False, True, False, False]
            and dates == [r[0].isoformat() for r in by_expense[:10]],
            f"{sorts} {dates}",
        )
        head("expense").click()
        wait_sort("expense", "descending")
        st, sorts, dates = sort_state()
        run.check(
            "【排序】再点「支出」：降序（aria-sort = descending），箭头向下；行 = 按支出降序取前 10",
            sorts["expense"] == "descending" and [h["icon"] for h in st["heads"] if h["column"] == "expense"][0].endswith("mdi-arrow-down")
            and dates == [r[0].isoformat() for r in sorted(expected, key=lambda r: r[1], reverse=True)[:10]],
            f"{sorts} {dates}",
        )
        head("expense").click()
        wait_sort("expense", "none")
        st, sorts, dates = sort_state()
        run.check(
            "【排序】第三次点「支出」：取消排序，回到数据原顺序（日期升序），所有列 aria-sort = none，没有箭头",
            set(sorts.values()) == {"none"} and dates == [r[0].isoformat() for r in expected[:10]] and all(h["icon"].strip() in ("mdi", "") or "arrow" not in h["icon"] for h in st["heads"]),
            f"{sorts} {dates}",
        )
        # 换一列：从头升序（收入列有大量 0：同值保持原顺序）
        head("income").click()
        wait_sort("income", "ascending")
        st, sorts, dates = sort_state()
        run.check(
            "【排序】点「收入」：升序，同值（大量 0）保持原来的日期顺序",
            sorts["income"] == "ascending" and dates == [r[0].isoformat() for r in sorted(expected, key=lambda r: r[2])[:10]],
            f"{sorts} {dates}",
        )
        head("net").click()
        wait_sort("net", "ascending")
        st, sorts, dates = sort_state()
        run.check(
            "【排序】换到「净收支」：重新从升序开始（收入列的排序被清掉）",
            sorts == {"date": "none", "expense": "none", "income": "none", "net": "ascending"}
            and dates == [r[0].isoformat() for r in sorted(expected, key=lambda r: r[2] - r[1])[:10]],
            f"{sorts} {dates}",
        )
        head("date").click()
        wait_sort("date", "ascending")
        head("date").click()
        wait_sort("date", "descending")
        st, sorts, dates = sort_state()
        run.check(
            "【排序】「日期」两次点按：降序（最新的在最前）",
            dates == [r[0].isoformat() for r in reversed(expected)][:10],
            str(dates),
        )
        head("date").click()
        wait_sort("date", "none")

        sizes = [(h["column"], h["width"], h["height"]) for h in page.evaluate(TREND_STATE_JS)["heads"]]
        run.check("【排序】表头按钮命中区域 ≥ 44 × 44", all(w >= 44 and h >= 44 for _, w, h in sizes), str(sizes))

        tid(page, "trend-more").click()
        page.wait_for_function("(n) => document.querySelectorAll('[data-testid=\"trend-row\"]').length === n", arg=len(expected))
        state = page.evaluate(TREND_STATE_JS)
        run.check(
            f"【显示更多】点一下展开剩余 {len(expected) - 10} 行（共 {len(expected)} 行），按钮消失",
            len(state["rows"]) == len(expected) and state["more"] is None and [r["date"] for r in state["rows"]] == [r[0].isoformat() for r in expected],
            str(len(state["rows"])),
        )

        # ================= 时间维度（90 天）=================
        pick_range(page, "90days")
        for dimension, text in (("week", "按周"), ("month", "按月"), ("day", "按日")):
            pick_dimension(page, dimension)
            state = page.evaluate(TREND_STATE_JS)
            run.check(
                f"【时间维度 {text}】分段高亮「{text}」；视图 data-dimension = {dimension}；范围仍是最近90天",
                state["dimension"] == dimension and state["dimValue"] == dimension and state["range"] == "90days"
                and [d["pressed"] for d in state["dimSegments"]] == ["true" if d["value"] == dimension else "false" for d in state["dimSegments"]],
                str((state["dimension"], state["range"])),
            )
            verify_trend(run, page, f"【最近90天·{text}】", "90days", dimension, today)
        by_week = expected_trend("90days", "week", today)
        by_month = expected_trend("90days", "month", today)
        run.check(
            "【时间维度】按周的行 = 周一、按月的行 = 月初（期望的聚合行数与按日不同）",
            all(r[0].weekday() == 0 for r in by_week) and all(r[0].day == 1 for r in by_month)
            and len(by_week) < len(expected_trend("90days", "day", today)) and len(by_month) <= len(by_week),
            f"周 {len(by_week)} 月 {len(by_month)}",
        )

        # ================= 图表容器 =================
        pick_range(page, "30days")
        if has_echarts(page):
            infos = {p: page.evaluate(CHART_JS, p) for p in ("trend-expense", "trend-income", "trend-net")}
            run.check(
                "【图表容器】三张折线图都已渲染（有 canvas、高度 220、不超出卡片宽度）",
                all(i is not None and i["hasCanvas"] and approx(i["height"], 220) and i["width"] <= i["panelWidth"] + 1 for i in infos.values()),
                str(infos),
            )
        else:
            run.skip("【图表容器】三张折线图渲染", "ECharts 脚本未加载（CDN 不可达）")

        # ================= 空账本：空状态 =================
        empty_name = unique_name("趋势空账本")
        empty = db.seed_ledger(user_id, empty_name, sort_order=2)
        pull_refresh(page, "stats-ptr")
        pick_ledger(page, empty)
        page.wait_for_function("() => document.querySelector('[data-testid=\"trend-total-expense\"]')?.getAttribute('data-amount') === '0.00'")
        wait_ready(page, "stats-trend")
        state = page.evaluate(TREND_STATE_JS)
        run.check(
            "【空账本】选「空账本」（沿用外壳的账本筛选）：KPI 全 0；明细表 / 三张图都是空状态，文案「暂无支出数据 / 暂无收入数据 / 暂无数据」",
            state["kpi"] == {"expense": "0.00", "income": "0.00", "net": "0.00"} and state["panels"] == ["empty", "empty", "empty", "empty"]
            and state["chartEmpty"] == ["暂无支出数据", "暂无收入数据", "暂无数据"] and (state["tableEmpty"] or "").strip() == "暂无数据" and state["rows"] == [],
            str(state),
        )
        pick_ledger(page, busy)
        wait_kpi(page, "trend", totals(expected_trend("30days", "day", today))[0], totals(expected_trend("30days", "day", today))[1])
        run.check(
            "【账本筛选】选回「趋势账本」：数据回来（与最近30天一致）；账本胶囊显示该账本名",
            page.evaluate(TREND_STATE_JS)["kpi"]["expense"] == plain(totals(expected_trend("30days", "day", today))[0])
            and tid(page, "stats-ledger-pill").get_attribute("data-value") == busy_name,
        )
        pick_ledger(page, None)
        wait_ready(page, "stats-trend")

        # ================= 状态保持：范围 + 维度 跨分段 / Tab =================
        pick_range(page, "90days")
        pick_dimension(page, "week")
        switch_segment(page, "annual")
        run.check("【分段切换】切到年度：时间范围胶囊消失、年份胶囊出现", tid(page, "stats-range-pill").count() == 0 and tid(page, "stats-year").count() == 1 and tid(page, "stats-trend").count() == 0)
        switch_segment(page, "monthly")
        run.check("【分段切换】切到月度：年份胶囊消失、月份胶囊出现", tid(page, "stats-year").count() == 0 and tid(page, "stats-period").count() == 1)
        switch_segment(page, "trend", {"data-range": "90days", "data-dimension": "week"})
        state = page.evaluate(TREND_STATE_JS)
        run.check(
            "【分段切换】再切回趋势：时间范围（最近90天）与时间维度（按周）保持，不重置",
            state["range"] == "90days" and state["dimension"] == "week" and state["pill"] == "最近90天" and state["dimValue"] == "week",
            str((state["range"], state["dimension"], state["pill"])),
        )
        open_tab(page, "账单")
        open_tab(page, "统计")
        wait_animations_settled(page)
        state = page.evaluate(TREND_STATE_JS)
        run.check(
            "【Tab 切换】切到账单再回统计：仍是趋势分段，范围与维度保持",
            page.evaluate(PAGE_STATE_JS)["segment"] == "trend" and state["range"] == "90days" and state["dimension"] == "week",
            str((state["range"], state["dimension"])),
        )

        # ================= 布局 =================
        pick_dimension(page, "day")
        pick_range(page, "30days")
        scroll_root_to(page, "bottom")
        page.wait_for_function("() => { const el = document.querySelector('[data-testid=\"stats-page\"]'); return el.scrollTop + el.clientHeight >= el.scrollHeight - 2; }")
        wait_animations_settled(page)
        layout = page.evaluate(
            """() => {
              const panel = document.querySelector('[data-testid="trend-table-panel"]');
              const bar = document.querySelector('[data-testid="tab-bar"]');
              const pr = panel.getBoundingClientRect();
              const br = bar.getBoundingClientRect();
              const wrap = document.querySelector('.ft-table-wrap');
              return { panelBottom: pr.bottom, barTop: br.top, barVisible: br.height > 0 && getComputedStyle(bar).visibility !== 'hidden', wrapOverflow: wrap.scrollWidth - wrap.clientWidth,
                       panelLeft: pr.left, panelRight: pr.right };
            }"""
        )
        run.check(
            "【布局】滚到底：明细表底部在 Tab 栏之上（底部让出了 Tab 栏）；表格不需要横向滚动；卡片不超出屏幕",
            layout["panelBottom"] <= layout["barTop"] + 1 and layout["wrapOverflow"] <= 1 and layout["panelLeft"] >= 0 and layout["panelRight"] <= SCREEN_W,
            str(layout),
        )
        run.check("没有横向溢出；玻璃层 ≤ 3", page.evaluate(OVERFLOW_JS) <= 0 and page.evaluate(GLASS_LAYERS_JS) <= 3, f"{page.evaluate(OVERFLOW_JS)} {page.evaluate(GLASS_LAYERS_JS)}")
        scroll_root_to(page, 0)
        run.check_clean(s.errors, "T 节趋势会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- Y. 年度


class AnnualDataset:
    """年度造数：当年 / 去年 / 前年 / 大前年，两个账本。当年与去年同期的账单都落在 1 月 1 日（任何一天跑都在「年初至今」窗口内）。"""

    def __init__(self, db: LocalDb, user_id: str) -> None:
        now = datetime.now()
        self.today = now.date()
        self.year = now.year
        self.a_name, self.b_name = unique_name("年度账本甲"), unique_name("年度账本乙")
        self.a = db.seed_ledger(user_id, self.a_name, sort_order=1)
        self.b = db.seed_ledger(user_id, self.b_name, sort_order=2)
        cat = db.category_id_by_name
        income = lambda name: db.category_id_by_name(name, BILL_TYPE_INCOME)  # noqa: E731

        def jan1(year: int, hour: int = 12) -> datetime:
            return datetime(year, 1, 1, hour)

        y = self.year
        # (年, 账本, 类型, 金额, 分类, 时间)
        self.bills = [
            (y, "a", EXPENSE, "150.00", "餐饮美食", jan1(y, 12)),
            (y, "a", EXPENSE, "50.00", "交通出行", jan1(y, 9)),
            (y, "a", INCOME, "600.00", "工资薪酬", jan1(y, 10)),
            (y, "b", EXPENSE, "40.00", "购物消费", jan1(y, 11)),
            (y, "b", INCOME, "200.00", "奖金补贴", jan1(y, 8)),  # 收入有两个分类，「收入构成」才看得出占比各自占收入合计
            (y - 1, "a", EXPENSE, "100.00", "餐饮美食", jan1(y - 1, 12)),
            (y - 1, "a", INCOME, "1000.00", "工资薪酬", jan1(y - 1, 12)),
            (y - 1, "a", EXPENSE, "25.00", "交通出行", datetime(y - 1, 12, 31, 12)),  # 去年同期窗口之外（除非今天就是 12 月 31 日）
            (y - 1, "b", EXPENSE, "10.00", "购物消费", jan1(y - 1, 12)),
            (y - 2, "a", EXPENSE, "400.00", "餐饮美食", jan1(y - 2, 12)),
            (y - 2, "a", INCOME, "100.00", "工资薪酬", jan1(y - 2, 12)),
        ]
        for _, ledger, kind, amount, category, when in self.bills:
            ledger_id = self.a if ledger == "a" else self.b
            if kind == EXPENSE:
                db.seed_bill(user_id, amount=amount, ledger_id=ledger_id, category_id=cat(category), when=when)
            else:
                db.seed_bill(user_id, amount=amount, ledger_id=ledger_id, bill_type=BILL_TYPE_INCOME, category_id=income(category), when=when)

    def in_ledger(self, ledger: "str | None"):
        return [b for b in self.bills if ledger is None or b[1] == ledger]

    def year_bills(self, year: int, ledger: "str | None" = None):
        return [b for b in self.in_ledger(ledger) if b[5].year == year]

    def sum_of(self, bills, kind: str) -> Decimal:
        return sum((Decimal(b[3]) for b in bills if b[2] == kind), Decimal(0))

    def yoy_window(self, year: int):
        """（当前期间, 去年同期）的 [起, 止) 区间：当年比到今天（含）为止，其余年份比整年；闰日回退到 2/28。"""
        if year == self.today.year:
            end = self.today + timedelta(days=1)
            try:
                prev_end = self.today.replace(year=year - 1) + timedelta(days=1)
            except ValueError:
                prev_end = date(year - 1, 2, 28) + timedelta(days=1)
            return (date(year, 1, 1), end), (date(year - 1, 1, 1), prev_end)
        return (date(year, 1, 1), date(year + 1, 1, 1)), (date(year - 1, 1, 1), date(year, 1, 1))

    def in_period(self, ledger, period):
        return [b for b in self.in_ledger(ledger) if period[0] <= b[5].date() < period[1]]

    def expected(self, year: int, ledger: "str | None" = None) -> dict:
        bills = self.year_bills(year, ledger)
        current, previous = self.yoy_window(year)
        cur, prev = self.in_period(ledger, current), self.in_period(ledger, previous)
        expense, income = self.sum_of(bills, EXPENSE), self.sum_of(bills, INCOME)
        cur_e, cur_i = self.sum_of(cur, EXPENSE), self.sum_of(cur, INCOME)
        prev_e, prev_i = self.sum_of(prev, EXPENSE), self.sum_of(prev, INCOME)
        partial = (current[1] - timedelta(days=1)) < date(year, 12, 31)
        def legend_of(kind: str) -> list:
            """某一类（支出 / 收入）按分类汇总的图例：(分类名, 金额, 笔数)，金额降序（同额按名称）"""
            by_category: dict = {}
            for b in bills:
                if b[2] == kind:
                    amount, count = by_category.get(b[4], (Decimal(0), 0))
                    by_category[b[4]] = (amount + Decimal(b[3]), count + 1)
            return sorted(((name, amt, cnt) for name, (amt, cnt) in by_category.items()), key=lambda r: (-r[1], r[0]))

        months_expense = [float(sum((Decimal(b[3]) for b in bills if b[2] == EXPENSE and b[5].month == m), Decimal(0))) for m in range(1, 13)]
        months_income = [float(sum((Decimal(b[3]) for b in bills if b[2] == INCOME and b[5].month == m), Decimal(0))) for m in range(1, 13)]
        return {
            "expense": expense, "income": income, "net": income - expense, "count": len(bills),
            "scope": "年初至今 vs 去年同期" if partial else f"{year}年 vs {year - 1}年",
            "period": f"{current[0].isoformat()} ~ {(current[1] - timedelta(days=1)).isoformat()} 对比 {previous[0].isoformat()} ~ {(previous[1] - timedelta(days=1)).isoformat()}",
            "expense_rate": rate_text(cur_e, prev_e), "income_rate": rate_text(cur_i, prev_i), "prev_expense": prev_e, "prev_income": prev_i,
            "expense_legend": legend_of(EXPENSE), "income_legend": legend_of(INCOME),
            "months": [months_expense, months_income, [i - e for e, i in zip(months_expense, months_income)]],
        }


def tone_of(rate: str, higher_is_better: bool) -> str:
    if rate in ("—", "0.0%"):
        return "flat"
    return "good" if rate.startswith("+") == higher_is_better else "bad"


TONE_COLOR = {"good": RGB_GOOD, "bad": RGB_BAD, "flat": RGB_LABEL}


def verify_annual(run: TestRun, page, label: str, data: AnnualDataset, year: int, ledger: "str | None" = None, *, charts: bool = True) -> None:
    exp = data.expected(year, ledger)
    state = page.evaluate(ANNUAL_STATE_JS)
    run.check(
        f"{label}：KPI 支出 {plain(exp['expense'])} / 收入 {plain(exp['income'])} / 结余 {plain(exp['net'])}",
        state["kpi"] == {"expense": plain(exp["expense"]), "income": plain(exp["income"]), "net": plain(exp["net"])},
        str(state["kpi"]),
    )
    ec, ic = state["expenseCell"], state["incomeCell"]
    run.check(
        f"{label}：同比口径「{exp['scope']}」，期间「{exp['period']}」",
        state["scope"] == exp["scope"] and state["period"] == exp["period"],
        f"{state['scope']} | {state['period']}",
    )
    exp_tone, inc_tone = tone_of(exp["expense_rate"], False), tone_of(exp["income_rate"], True)
    run.check(
        f"{label}：支出同比 {exp['expense_rate']}（{exp_tone}）、收入同比 {exp['income_rate']}（{inc_tone}）；去年同期金额 {plain(exp['prev_expense'])} / {plain(exp['prev_income'])}",
        ec["rate"] == exp["expense_rate"] and ec["tone"] == exp_tone and ec["rateColor"] == TONE_COLOR[exp_tone] and ec["base"] == plain(exp["prev_expense"])
        and ic["rate"] == exp["income_rate"] and ic["tone"] == inc_tone and ic["rateColor"] == TONE_COLOR[inc_tone] and ic["base"] == plain(exp["prev_income"]),
        f"{ec} | {ic}",
    )
    run.check(
        f"{label}：去年同期为 0 的一栏显示「—」与「（无对比基数）」，其余没有这句说明",
        (ec["nobase"] == "（无对比基数）") == (exp["expense_rate"] == "—") and (ic["nobase"] == "（无对比基数）") == (exp["income_rate"] == "—"),
        f"{ec['nobase']} | {ic['nobase']}",
    )
    # 构成：支出构成、收入构成各一个圆环 + 图例，占比各自占本类合计（不再是收支混在一起的「分类统计」）
    run.check(
        f"{label}：构成分成两节——「支出构成」在前、「收入构成」在后；旧的混合「分类统计」（annual-composition / -donut / -legend）不在了",
        state["expense"]["title"] == "支出构成" and state["income"]["title"] == "收入构成" and state["oldComposition"] == []
        and all(p is not None for p in state["panels"][2:]),
        str((state["expense"]["title"], state["income"]["title"], state["oldComposition"], state["panels"])),
    )
    if exp["count"] == 0:
        run.check(
            f"{label}：没有账单——月度趋势是空状态「当年暂无账单」，两节构成都是空环，说明「当年暂无支出账单」「当年暂无收入账单」",
            state["panels"][1] == "empty" and state["monthlyEmpty"] == "当年暂无账单"
            and state["expense"]["empty"] == "当年暂无支出账单" and state["income"]["empty"] == "当年暂无收入账单"
            and state["expense"]["rows"] == [] and state["income"]["rows"] == [],
            str((state["panels"], state["monthlyEmpty"], state["expense"]["empty"], state["income"]["empty"])),
        )
    for key, name in (("expense", "支出"), ("income", "收入")):
        comp, legend, total = state[key], exp[f"{key}_legend"], exp[key]
        if not legend:
            if exp["count"] > 0:
                run.check(
                    f"{label}：这一年没有{name}账单——「{name}构成」是空环（中心 0.00），图例没有，说明「当年暂无{name}账单」",
                    comp["rows"] == [] and comp["total"] == "0.00" and comp["empty"] == f"当年暂无{name}账单" and comp["caption"] == f"全年{name}",
                    str(comp),
                )
            continue
        rows = [(r["name"], r["amount"], r["count"], Decimal(r["percent"]), r["pct"]) for r in comp["rows"]]
        expected_rows = []
        for cat_name, amount, count in legend:
            two, text = percent_text(amount, total)
            expected_rows.append((cat_name, plain(amount), str(count), two, text))
        run.check(
            f"{label}：「{name}构成」图例 = 全年{name}账单按分类汇总，金额降序，占比占全年{name}合计 {plain(total)}；圆环中心「全年{name}」；没有「当年暂无」说明",
            rows == expected_rows and comp["total"] == plain(total) and comp["caption"] == f"全年{name}" and comp["legendLabel"] == f"全年{name}分类明细"
            and comp["empty"] is None and comp["note"] is None,
            f"{rows} | {comp['total']} | {comp['note']}",
        )
        run.check(
            f"{label}：「{name}构成」圆环段数 = 分类数（{len(legend)}），每段占比 = 图例占比，段色 = 图例圆点色",
            [a["name"] for a in comp["arcs"]] == [r["name"] for r in comp["rows"]]
            and all(abs(a["fraction"] - float(Decimal(r["amount"]) / total)) < 0.001 for a, r in zip(comp["arcs"], comp["rows"]))
            and [a["stroke"] for a in comp["arcs"]] == [r["dot"] for r in comp["rows"]],
            str(comp["arcs"]),
        )
    if exp["count"] > 0:
        if charts and has_echarts(page):
            labels = [f"{m}月" for m in range(1, 13)]
            ok = wait_chart(page, "annual-monthly", labels, exp["months"])
            info = page.evaluate(CHART_JS, "annual-monthly")
            run.check(
                f"{label}：月度趋势折线——十二个月、支出 / 收入 / 净收支三条线的数据与颜色、图例",
                ok and info is not None and [(s["name"], s["color"]) for s in info["series"]] == [("支出", HEX_EXPENSE), ("收入", HEX_INCOME), ("净收支", HEX_NET)]
                and info["legend"] == ["支出", "收入", "净收支"] and info["hasCanvas"] and approx(info["height"], 260),
                str(info),
            )


def check_annual(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        data = AnnualDataset(db, user_id)
        y = data.year

        go_segment(page, "annual", {"data-year": str(y)})
        shot(page, "t12-annual-default")

        # ================= 入口与年份胶囊 =================
        state = page.evaluate(ANNUAL_STATE_JS)
        run.check(
            "【入口】直接进入 /statistics?segment=annual：Tab = 统计、分段 = 年度；年份胶囊 + 账本胶囊；没有月份胶囊 / 时间范围胶囊；趋势 / 月度视图不在 DOM 里",
            active_tab(page) == "statistics" and tid(page, "stats-segment").get_attribute("data-value") == "annual"
            and tid(page, "stats-year").count() == 1 and tid(page, "stats-ledger-pill").count() == 1 and tid(page, "stats-period").count() == 0
            and tid(page, "stats-range-pill").count() == 0 and tid(page, "stats-trend").count() == 0 and tid(page, "stats-monthly").count() == 0
            and tid(page, "stats-embed").count() == 0,
        )
        pill = state["pill"]
        run.check(
            f"【年份胶囊】文字「{y}年」、读屏名称「年份」、上一年 / 下一年按钮带目标年份「上一年，{y - 1}年」「下一年，{y + 1}年」、都可用、高度 44",
            pill["label"] == f"{y}年" and pill["aria"] == "年份" and pill["role"] == "group" and pill["prevAria"] == f"上一年，{y - 1}年" and pill["nextAria"] == f"下一年，{y + 1}年"
            and not pill["prevDisabled"] and not pill["nextDisabled"] and approx(pill["height"], 44),
            str(pill),
        )
        filters = page.evaluate(FILTERS_JS)
        row, yr, led = filters["row"], filters["year"], filters["ledger"]
        run.check(
            "【筛选行】年份胶囊靠左、账本胶囊靠右、不重叠",
            approx(yr["left"], row["left"], 1.5) and approx(led["right"], row["right"], 1.5) and yr["right"] < led["left"],
            str(filters),
        )
        run.check("【KPI 卡】三项标签 支出 / 收入 / 结余", state["kpiLabels"] == ["支出", "收入", "结余"], str(state["kpiLabels"]))

        # ================= 当年（全部账本）=================
        verify_annual(run, page, f"【{y}年·全部账本】", data, y)
        run.check(
            "【同比·布局】两栏并排、栏宽相等且都在卡片内；变化率文字不撑出栏",
            state["expenseCell"]["right"] <= state["incomeCell"]["left"] + 1 and approx(state["expenseCell"]["width"], state["incomeCell"]["width"], 1.5)
            and state["expenseCell"]["left"] >= state["yoyCard"]["left"] and state["incomeCell"]["right"] <= state["yoyCard"]["right"]
            and state["expenseCell"]["rateOverflow"] <= 0 and state["incomeCell"]["rateOverflow"] <= 0,
            str((state["expenseCell"], state["incomeCell"])),
        )
        run.check(
            "【构成·布局】支出构成 / 收入构成的圆环都水平居中于卡片、中心总额不超出圆环内孔",
            all(
                state[k]["donutBox"] is not None and approx(state[k]["donutBox"]["cx"], SCREEN_W / 2, 1.5)
                and state[k]["totalBox"]["width"] < state[k]["donutBox"]["width"] * 0.62
                for k in ("expense", "income")
            ),
            str([(state[k]["donutBox"], state[k]["totalBox"]) for k in ("expense", "income")]),
        )

        # ================= 年份切换 =================
        tid(page, "stats-year-prev").click()
        wait_ready(page, "stats-annual", {"data-year": str(y - 1)})
        state = page.evaluate(ANNUAL_STATE_JS)
        run.check(
            f"【上一年】胶囊文字「{y - 1}年」，按钮读屏名称跟着变；视图 data-year = {y - 1}",
            state["pill"]["label"] == f"{y - 1}年" and state["year"] == str(y - 1) and state["pill"]["prevAria"] == f"上一年，{y - 2}年" and state["pill"]["nextAria"] == f"下一年，{y}年",
            str(state["pill"]),
        )
        verify_annual(run, page, f"【{y - 1}年（已结束）·全部账本】", data, y - 1)
        shot(page, "t12-annual-prev-year")

        tid(page, "stats-year-prev").click()
        wait_ready(page, "stats-annual", {"data-year": str(y - 2)})
        verify_annual(run, page, f"【{y - 2}年·去年同期没有账单】", data, y - 2)

        tid(page, "stats-year-prev").click()
        wait_ready(page, "stats-annual", {"data-year": str(y - 3)})
        verify_annual(run, page, f"【{y - 3}年·没有任何账单】", data, y - 3)
        shot(page, "t12-annual-empty-year")

        tid(page, "stats-year-next").click()
        tid(page, "stats-year-next").click()
        tid(page, "stats-year-next").click()
        wait_ready(page, "stats-annual", {"data-year": str(y)})
        run.check(f"【下一年】连点三次回到 {y} 年，数据回来", page.evaluate(ANNUAL_STATE_JS)["kpi"]["expense"] == plain(data.expected(y)["expense"]))

        # 边界：2000 – 2100（连点期间只有最后一次真正生效）
        page.evaluate("() => { for (let i = 0; i < 80; i++) { const b = document.querySelector('[data-testid=\"stats-year-prev\"]'); if (b) b.click(); } }")
        wait_ready(page, "stats-annual", {"data-year": "2000"})
        edge = page.evaluate(ANNUAL_STATE_JS)["pill"]
        run.check(
            "【年份下限】连点上一年停在 2000 年：「上一年」置灰，「下一年」可用，名称「下一年，2001年」",
            edge["label"] == "2000年" and edge["prevDisabled"] and not edge["nextDisabled"] and edge["nextAria"] == "下一年，2001年",
            str(edge),
        )
        page.evaluate("() => { for (let i = 0; i < 120; i++) { const b = document.querySelector('[data-testid=\"stats-year-next\"]'); if (b) b.click(); } }")
        wait_ready(page, "stats-annual", {"data-year": "2100"})
        edge = page.evaluate(ANNUAL_STATE_JS)["pill"]
        run.check(
            "【年份上限】连点下一年停在 2100 年：「下一年」置灰，「上一年」可用，名称「上一年，2099年」",
            edge["label"] == "2100年" and edge["nextDisabled"] and not edge["prevDisabled"] and edge["prevAria"] == "上一年，2099年",
            str(edge),
        )
        page.evaluate("() => { for (let i = 0; i < 120; i++) { const b = document.querySelector('[data-testid=\"stats-year-prev\"]'); if (b) b.click(); } }")
        wait_ready(page, "stats-annual", {"data-year": "2000"})
        page.evaluate(f"() => {{ for (let i = 0; i < {y - 2000}; i++) {{ const b = document.querySelector('[data-testid=\"stats-year-next\"]'); if (b) b.click(); }} }}")
        wait_ready(page, "stats-annual", {"data-year": str(y)})

        # ================= 账本筛选 =================
        pick_ledger(page, data.a)
        wait_ready(page, "stats-annual")
        page.wait_for_function("(e) => document.querySelector('[data-testid=\"annual-total-expense\"]')?.getAttribute('data-amount') === e", arg=plain(data.expected(y, "a")["expense"]))
        verify_annual(run, page, f"【{y}年·只看账本甲】", data, y, "a")
        run.check(
            "【账本筛选】账本胶囊显示该账本名；年份保持不变",
            tid(page, "stats-ledger-pill").get_attribute("data-value") == data.a_name and page.evaluate(ANNUAL_STATE_JS)["year"] == str(y),
        )
        tid(page, "stats-year-prev").click()
        wait_ready(page, "stats-annual", {"data-year": str(y - 1)})
        verify_annual(run, page, f"【{y - 1}年·只看账本甲】", data, y - 1, "a")
        pick_ledger(page, None)
        wait_ready(page, "stats-annual")

        # ================= 状态保持：年份跨分段 / Tab =================
        run.check("【年份保持·前置】当前在去年", page.evaluate(ANNUAL_STATE_JS)["year"] == str(y - 1))
        switch_segment(page, "trend")
        switch_segment(page, "annual", {"data-year": str(y - 1)})
        run.check("【分段切换】趋势 ↔ 年度来回：年份仍是去年，不重置", page.evaluate(ANNUAL_STATE_JS)["pill"]["label"] == f"{y - 1}年")
        open_tab(page, "首页")
        open_tab(page, "统计")
        wait_animations_settled(page)
        run.check("【Tab 切换】切走再回统计：仍是年度分段、年份保持", page.evaluate(PAGE_STATE_JS)["segment"] == "annual" and page.evaluate(ANNUAL_STATE_JS)["year"] == str(y - 1))
        tid(page, "stats-year-next").click()
        wait_ready(page, "stats-annual", {"data-year": str(y)})

        # ================= 布局 =================
        scroll_root_to(page, "bottom")
        page.wait_for_function("() => { const el = document.querySelector('[data-testid=\"stats-page\"]'); return el.scrollTop + el.clientHeight >= el.scrollHeight - 2; }")
        wait_animations_settled(page)
        layout = page.evaluate(
            """() => {
              const panel = document.querySelector('[data-testid="annual-income-composition"]');
              const bar = document.querySelector('[data-testid="tab-bar"]');
              return { panelBottom: panel.getBoundingClientRect().bottom, barTop: bar.getBoundingClientRect().top };
            }"""
        )
        run.check("【布局】滚到底：最后一张卡（收入构成）底部在 Tab 栏之上", layout["panelBottom"] <= layout["barTop"] + 1, str(layout))
        run.check("没有横向溢出；玻璃层 ≤ 3", page.evaluate(OVERFLOW_JS) <= 0 and page.evaluate(GLASS_LAYERS_JS) <= 3, f"{page.evaluate(OVERFLOW_JS)} {page.evaluate(GLASS_LAYERS_JS)}")
        scroll_root_to(page, 0)
        run.check_clean(s.errors, "Y 节年度会话无页面异常 / 控制台错误 / 失败请求")


def check_huge_rate(run: TestRun, db: LocalDb) -> None:
    """去年同期基数很小、本年很大：变化率文字很长，缩小字号、不挤出栏；KPI 的大额金额也不挤出。"""
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger = db.seed_ledger(user_id, unique_name("大额账本"), sort_order=1)
        y = datetime.now().year
        cat = db.category_id_by_name
        db.seed_bill(user_id, amount="9999999.99", ledger_id=ledger, category_id=cat("居住生活"), when=datetime(y - 1, 1, 1, 12))
        db.seed_bill(user_id, amount="0.01", ledger_id=ledger, category_id=cat("居住生活"), when=datetime(y - 2, 1, 1, 12))
        db.seed_bill(user_id, amount="1.00", ledger_id=ledger, category_id=cat("居住生活"), when=datetime(y - 3, 1, 1, 12))
        go_segment(page, "annual", {"data-year": str(y)})
        tid(page, "stats-year-prev").click()
        wait_ready(page, "stats-annual", {"data-year": str(y - 1)})
        state = page.evaluate(ANNUAL_STATE_JS)
        ec = state["expenseCell"]
        expected = rate_text("9999999.99", "0.01")
        run.check(
            f"【超长变化率】{y - 1} 年支出 9,999,999.99 对前年 0.01：变化率「{expected}」，按字数缩小字号（xlong / long 档），不挤出栏",
            ec["rate"] == expected and ("ft-yoy__rate--xlong" in ec["rateClass"] or "ft-yoy__rate--long" in ec["rateClass"]) and ec["rateOverflow"] <= 0
            and ec["left"] >= state["yoyCard"]["left"] and ec["right"] <= state["yoyCard"]["right"],
            str(ec),
        )
        run.check(
            "【大额】KPI 支出 9,999,999.99 不挤出屏幕；没有横向溢出",
            state["kpi"]["expense"] == "9999999.99" and page.evaluate(OVERFLOW_JS) <= 0,
            str(state["kpi"]),
        )
        shot(page, "t12-annual-huge-rate")
        run.check_clean(s.errors, "大额会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- F. 刷新


def check_refresh(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger = db.seed_ledger(user_id, unique_name("刷新账本"), sort_order=1)
        cat = db.category_id_by_name("餐饮美食")
        now = datetime.now()
        db.seed_bill(user_id, amount="10.00", ledger_id=ledger, category_id=cat, when=days_ago(1))
        jan1 = datetime(now.year, 1, 1, 12)
        db.seed_bill(user_id, amount="100.00", ledger_id=ledger, category_id=cat, when=jan1)
        trend_total = Decimal("10.00")
        annual_total = Decimal("110.00") if days_ago(1).year == now.year else Decimal("100.00")
        go_segment(page, "trend", {"data-range": "7days"})
        wait_kpi(page, "trend", trend_total, 0)

        cloud = FakeCloud()
        try:
            cloud.start()
        except CloudPortBusy as exc:
            run.check("假云端可以启动（验证「刷新不触发同步」依赖它）", False, str(exc))
            cloud = None
        try:
            baseline = len(cloud.requests) if cloud else 0

            # ---- 趋势：下拉刷新（真实触摸）----
            scroll_root_to(page, 0)
            page.wait_for_function("() => document.querySelector('[data-testid=\"stats-ptr\"]').getAttribute('data-state') === 'idle'")
            trend_total += Decimal("7.00")
            annual_total += Decimal("7.00") if days_ago(3).year == now.year else Decimal(0)
            db.seed_bill(user_id, amount="7.00", ledger_id=ledger, category_id=cat, when=days_ago(3))
            pull_down(page, distance=240, from_y=300)
            wait_kpi(page, "trend", trend_total, 0)
            run.check("【趋势·下拉刷新】KPI 更新为最新数据（造了一笔 7.00）", amount_of(page, "trend-total-expense") == plain(trend_total), amount_of(page, "trend-total-expense"))
            wait_ready(page, "stats-trend")
            rows = page.evaluate(TREND_STATE_JS)["rows"]
            new_day = days_ago(3).date().isoformat()
            run.check("【趋势·下拉刷新】明细表同步更新（新账单所在的日期行有这笔支出）", any(r["date"] == new_day and Decimal(r["expense"]) >= Decimal("7.00") for r in rows), str(rows))
            page.wait_for_function("() => document.querySelector('[data-testid=\"stats-ptr\"]').getAttribute('data-state') === 'idle'")

            # ---- 年度：下拉刷新 ----
            switch_segment(page, "annual", {"data-year": str(now.year)})
            wait_kpi(page, "annual", annual_total, 0)
            annual_total += Decimal("1000.00")
            db.seed_bill(user_id, amount="1000.00", ledger_id=ledger, category_id=cat, when=jan1.replace(hour=13))
            pull_refresh(page, "stats-ptr")
            wait_kpi(page, "annual", annual_total, 0)
            run.check("【年度·下拉刷新】KPI 更新为最新数据（造了一笔 1000.00）", amount_of(page, "annual-total-expense") == plain(annual_total), amount_of(page, "annual-total-expense"))

            # ---- 年度：账单变更事件（BillChanged）：导入 / 记账会触发；这里用自定义报表二级页回来的路径验证「页面关闭后重新加载」----
            tid(page, "stats-report").click()
            wait_url(page, r"^/custom-report")
            wait_tab_bar(page, visible=False)
            annual_total += Decimal("2000.00")
            db.seed_bill(user_id, amount="2000.00", ledger_id=ledger, category_id=cat, when=jan1.replace(hour=14))
            page.go_back()
            wait_url(page, r"^/statistics")
            wait_tab_bar(page)
            wait_secondary_pages_closed(page)
            wait_animations_settled(page)
            wait_kpi(page, "annual", annual_total, 0)
            run.check(
                "【年度·页面关闭后重新加载】在自定义报表页期间新增的账单，回到统计 Tab 后自动体现；仍停在年度分段",
                amount_of(page, "annual-total-expense") == plain(annual_total) and page.evaluate(PAGE_STATE_JS)["segment"] == "annual",
                amount_of(page, "annual-total-expense"),
            )

            if cloud is not None:
                member_cache_paths = {"/api/auth/login", "/api/sync/ledgermembers/pull"}
                requests = [r for r in cloud.requests[baseline:] if r.path not in member_cache_paths]
                run.check(
                    "趋势 / 年度的下拉刷新只重读本页数据，不触发同步（除读路径顺带刷新的成员缓存外，没有向云端发任何请求）",
                    len(requests) == 0,
                    "；".join(f"{r.method} {r.path}" for r in requests),
                )
        finally:
            if cloud is not None:
                cloud.stop()
        run.check_clean(s.errors, "F 节刷新会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- 入口


SECTIONS = {
    "R": check_legacy_routes,
    "T": check_trend,
    "Y": check_annual,
    "H": check_huge_rate,
    "F": check_refresh,
}


def main() -> int:
    """不带参数跑全部小节；带小节名（如 `python statistics_trend_annual_test.py T`）只跑指定小节，便于定位问题。"""
    require_server()
    run = TestRun("statistics-trend-annual")
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
