# -*- coding: utf-8 -*-
"""
E2E 测试：统计 Tab · 分段壳与月度（iOS UI 重构 · 11 号 ticket）。

只覆盖网页层——Android / iOS 上的真机手感（大标题折叠的跟手度、分段滑块的弹性、下拉刷新的回弹、灵动岛下的位置、
月份切换时图表的重绘流畅度、ECharts 在真机 WebView 里的渲染）见 ticket 11 的人工验收清单。
趋势 / 年度两个分段的内容（时间范围 / 维度 / 年份、同比、图表、明细表、旧地址 /trend /annual-report 的跳转）由 statistics_trend_annual_test.py 覆盖（12 号 ticket），
这里只验证分段切换（滑块跟随）与账本筛选在三个分段间共用。

  A. 新用户（没有账本、没有账单）：大标题页骨架、分段控件、月份胶囊、没有账本胶囊、KPI 全为 0、圆环空环 + 说明、
     四个图表节（没有预算时不显示预算节）的空状态、布局与玻璃层预算
  B. 有数据的月度：
     B1 KPI（口径：应用时区自然月、左闭右开、只算自己记的账单）、构成圆环与图例（排序、占比、配色一一对应）、支出 ↔ 收入切换、
        按分类 ↔ 按账本（含「未归属账本」）、预算执行节、无障碍属性、布局（金额不挤出栏、圆环居中、底部让出 Tab 栏）；
     B2 月份切换：上一月 / 下一月、跨年、未来月份切不过去（当月时「下一月」置灰）、快速连点只显示最后一次、当月预算只在当月出现；
     B3 账本筛选：账本菜单（选项、选中对勾、选中即关闭）、筛选后各节重算、「按账本」随之消失、Tab 切换保留月份与账本
  C. 分段：月度 / 趋势 / 年度来回切换（滑块跟随），账本筛选在三个分段间共用
  D. 刷新与二级页面：下拉刷新（真实触摸，唯一的刷新入口——导航栏没有刷新按钮）只重读本页数据、不触发云端请求；拉得不够 / 页面不在顶部不刷新；
     点自定义报表进入二级页，在那里记的账单回来后自动体现（页面关闭后重新加载）
  E. 只有历史无账本账单（没有任何账本）：没有账本胶囊与「按账本」，账单照常计入
  F. 账本很多 / 名称很长：账本菜单在面板内滚动不顶出屏幕，长名称的胶囊省略不撑破一行
  G. 大额：KPI 与圆环中心的金额缩小字号，不挤出栏 / 不挤出圆环中心的孔

可观察属性（已登记到 README）：stats-page（滚动容器 data-scroll-root）、large-nav / large-title、stats-report、
stats-segment（data-value）→ segment（data-value、aria-pressed）、stats-ptr（data-state）、stats-filters、
stats-period（-prev / -next / -label（data-label））、stats-ledger-pill（data-value）、
stats-ledger-sheet、stats-ledger-all / stats-ledger-option（data-ledger-id、aria-current）、
stats-monthly（data-year / data-month / data-state = loading | ready | reloading | failed）、
stats-kpi（stats-kpi-expense / -income / -balance，data-amount）、
stats-comp-header、stats-comp-title、stats-comp-switch（data-target）、stats-comp-dimension（data-mode）、
stats-composition（data-state）、stats-donut（data-slices）→ donut-arc（data-name / data-fraction）、stats-donut-label、stats-donut-total（data-amount）、
stats-legend → legend-row（data-name / data-amount / data-percent / data-count）→ legend-amount、stats-composition-empty、
stats-budget-header / stats-budget-manage / stats-budget-group → budget-row、
stats-rank / stats-calendar / stats-cumulative（data-state）与 {id}-empty、stats-trend / stats-annual（data-state）、
stats-year（-prev / -next / -label）、trend-total-expense / -income / -net、annual-total-expense / -income / -net。
"""
import re
import sys
from datetime import datetime, timedelta
from decimal import ROUND_HALF_EVEN, ROUND_HALF_UP, Decimal

from e2e_base import (
    GLASS_LAYERS_JS,
    BILL_TYPE_INCOME,
    PHONE_VIEWPORT,
    LocalDb,
    TestRun,
    days_ago,
    login,
    open_app,
    open_tab,
    pull_down,
    require_server,
    scroll_root_state,
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
SCREEN_H = PHONE_VIEWPORT["height"]

RGB_WHITE = "rgb(255, 255, 255)"
RGB_LABEL = "rgb(0, 0, 0)"  # --ft-label
RGB_BRAND = "rgb(67, 24, 255)"  # --ft-brand #4318FF
RGB_RED = "rgb(232, 54, 44)"  # --ft-expense #E8362C（与折线 / 热力图同一套；iOS 系统红 #FF3B30 不再用于支出）
RGB_GREEN = "rgb(31, 165, 68)"  # --ft-income #1FA544（iOS 系统绿 #34C759 在白底上只有约 2.2:1）

LAYOUT_JS = """() => {
  const root = document.querySelector('[data-testid="stats-page"]');
  const box = (id) => {
    const el = root.querySelector(`[data-testid="${id}"]`);
    if (!el) return null;
    const r = el.getBoundingClientRect();
    const cs = getComputedStyle(el);
    return { left: r.left, right: r.right, top: r.top, bottom: r.bottom, width: r.width, height: r.height, bg: cs.backgroundColor, radius: cs.borderTopLeftRadius };
  };
  const pr = root.getBoundingClientRect();
  const pcs = getComputedStyle(root);
  const title = root.querySelector('[data-testid="large-title"]');
  return {
    page: { top: pr.top, height: pr.height, overflowY: pcs.overflowY, paddingBottom: parseFloat(pcs.paddingBottom) },
    isScrollRoot: root.hasAttribute('data-scroll-root'),
    title: title ? { tag: title.tagName, text: title.textContent.trim() } : null,
    segment: box('stats-segment'), filters: box('stats-filters'), period: box('stats-period'),
    prev: box('stats-period-prev'), next: box('stats-period-next'), label: box('stats-period-label'), pill: box('stats-ledger-pill'),
    kpi: box('stats-kpi'), composition: box('stats-composition'), donut: box('stats-donut'),
    report: box('stats-report'),
    refreshEntries: root.querySelectorAll('[data-testid="stats-refresh"], button[aria-label="刷新"], .mdi-refresh').length,
    overflowX: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    max: root.scrollHeight - root.clientHeight,
  };
}"""

KPI_JS = """() => {
  const card = document.querySelector('[data-testid="stats-kpi"]');
  const cells = [...card.querySelectorAll('.ft-kpi__cell')].map((cell) => {
    const label = cell.querySelector('.ft-kpi__label');
    const dot = cell.querySelector('.ft-dot');
    const amount = cell.querySelector('[data-amount]');
    const cr = cell.getBoundingClientRect();
    const ar = amount ? amount.getBoundingClientRect() : null;
    const acs = amount ? getComputedStyle(amount) : null;
    return {
      label: label.textContent.trim(),
      dot: getComputedStyle(dot).backgroundColor,
      dotWidth: dot.getBoundingClientRect().width,
      testid: amount && amount.getAttribute('data-testid'),
      amount: amount && amount.getAttribute('data-amount'),
      aria: amount && amount.getAttribute('aria-label'),
      role: amount && amount.getAttribute('role'),
      color: acs && acs.color, weight: acs && acs.fontWeight, size: acs && acs.fontSize,
      long: !!amount && amount.classList.contains('ft-amount--long'),
      left: cr.left, right: cr.right, aLeft: ar && ar.left, aRight: ar && ar.right,
    };
  });
  const cs = getComputedStyle(card);
  const r = card.getBoundingClientRect();
  return { cells, bg: cs.backgroundColor, radius: cs.borderTopLeftRadius, left: r.left, right: r.right, busy: card.getAttribute('aria-busy') };
}"""

COMPOSITION_JS = """() => {
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  const text = (id) => { const el = q(id); return el ? el.textContent.trim() : null; };
  const attr = (id, name) => { const el = q(id); return el ? el.getAttribute(name) : null; };
  const donut = q('stats-donut');
  const arcs = [...document.querySelectorAll('[data-testid="stats-donut"] [data-testid="donut-arc"]')].map((el) => ({
    name: el.getAttribute('data-name'), fraction: parseFloat(el.getAttribute('data-fraction')), stroke: getComputedStyle(el).stroke,
  }));
  const rows = [...document.querySelectorAll('[data-testid="stats-legend"] [data-testid="legend-row"]')].map((el) => {
    const dot = el.querySelector('.ft-dot');
    const r = el.getBoundingClientRect();
    return {
      name: el.getAttribute('data-name'), amount: el.getAttribute('data-amount'), percent: el.getAttribute('data-percent'),
      count: el.getAttribute('data-count'), pct: el.querySelector('.ft-legend__pct').textContent.trim(),
      amountText: el.querySelector('[data-testid="legend-amount"]').getAttribute('aria-label'),
      dot: getComputedStyle(dot).backgroundColor, height: r.height, left: r.left, right: r.right,
    };
  });
  const total = q('stats-donut-total');
  const dr = donut ? donut.getBoundingClientRect() : null;
  const tr = total ? total.getBoundingClientRect() : null;
  const panel = q('stats-composition');
  const pr = panel.getBoundingClientRect();
  const svg = donut ? donut.querySelector('svg') : null;
  const legend = q('stats-legend');
  return {
    title: text('stats-comp-title'), caption: text('stats-donut-label'),
    total: attr('stats-donut-total', 'data-amount'), totalLong: !!total && total.classList.contains('ft-amount--long'),
    slices: attr('stats-donut', 'data-slices'), arcs, rows, empty: text('stats-composition-empty'),
    switchTarget: attr('stats-comp-switch', 'data-target'), switchText: text('stats-comp-switch'), switchLabel: attr('stats-comp-switch', 'aria-label'),
    switchHeight: q('stats-comp-switch') ? q('stats-comp-switch').getBoundingClientRect().height : null,
    dimension: q('stats-comp-dimension') ? { mode: attr('stats-comp-dimension', 'data-mode'), text: text('stats-comp-dimension'), label: attr('stats-comp-dimension', 'aria-label') } : null,
    panelState: panel.getAttribute('data-state'), panelBg: getComputedStyle(panel).backgroundColor, panelRadius: getComputedStyle(panel).borderTopLeftRadius,
    donutBox: dr && { left: dr.left, right: dr.right, width: dr.width, height: dr.height, cx: (dr.left + dr.right) / 2 },
    totalBox: tr && { left: tr.left, right: tr.right, width: tr.width, top: tr.top, bottom: tr.bottom },
    panelCx: (pr.left + pr.right) / 2,
    svgHidden: svg ? svg.getAttribute('aria-hidden') : null,
    legendLabel: legend ? legend.getAttribute('aria-label') : null, legendTag: legend ? legend.tagName : null,
  };
}"""

SECTION_TITLES_JS = """() => [...document.querySelectorAll('[data-testid="stats-monthly"] h2.ft-section__title')].map((el) => el.textContent.trim())"""

PERIOD_JS = """() => {
  const group = document.querySelector('[data-testid="stats-period"]');
  const label = document.querySelector('[data-testid="stats-period-label"]');
  const prev = document.querySelector('[data-testid="stats-period-prev"]');
  const next = document.querySelector('[data-testid="stats-period-next"]');
  return {
    role: group.getAttribute('role'), groupLabel: group.getAttribute('aria-label'),
    label: label.textContent.trim(), dataLabel: label.getAttribute('data-label'), live: label.getAttribute('aria-live'),
    prevLabel: prev.getAttribute('aria-label'), nextLabel: next.getAttribute('aria-label'),
    prevTag: prev.tagName, nextTag: next.tagName, prevDisabled: prev.disabled, nextDisabled: next.disabled,
  };
}"""

PILL_JS = """() => {
  const pill = document.querySelector('[data-testid="stats-ledger-pill"]');
  if (!pill) return null;
  const text = pill.querySelector('.ft-pill__text');
  const row = pill.parentElement.getBoundingClientRect();
  const r = pill.getBoundingClientRect();
  return {
    tag: pill.tagName, value: pill.getAttribute('data-value'), aria: pill.getAttribute('aria-label'), haspopup: pill.getAttribute('aria-haspopup'),
    text: text.textContent.trim(), ellipsis: getComputedStyle(text).textOverflow, truncated: text.scrollWidth > text.clientWidth + 1,
    width: r.width, height: r.height, left: r.left, right: r.right, rowWidth: row.width,
    bg: getComputedStyle(pill).backgroundColor, radius: getComputedStyle(pill).borderTopLeftRadius,
  };
}"""

SHEET_JS = """() => {
  const sheet = document.querySelector('[data-testid="stats-ledger-sheet"]');
  const group = sheet.querySelector('.ft-actionsheet__group');
  const cancel = sheet.querySelector('[data-testid="actionsheet-cancel"]');
  const gr = group.getBoundingClientRect();
  const cr = cancel.getBoundingClientRect();
  const options = [...sheet.querySelectorAll('[data-testid="stats-ledger-option"]')];
  return {
    all: { current: sheet.querySelector('[data-testid="stats-ledger-all"]').getAttribute('aria-current'), text: sheet.querySelector('[data-testid="stats-ledger-all"]').textContent.trim() },
    options: options.map((el) => ({ id: el.getAttribute('data-ledger-id'), text: el.textContent.trim(), current: el.getAttribute('aria-current') })),
    group: { top: gr.top, bottom: gr.bottom, height: gr.height, scrollHeight: group.scrollHeight, clientHeight: group.clientHeight, overflowY: getComputedStyle(group).overflowY },
    cancel: { top: cr.top, bottom: cr.bottom },
    title: sheet.querySelector('.ft-actionsheet__title') ? sheet.querySelector('.ft-actionsheet__title').textContent.trim() : null,
    dialog: sheet.getAttribute('role'), modal: sheet.getAttribute('aria-modal'),
  };
}"""

PTR_IDLE_JS = """() => {
  const zone = document.querySelector('[data-testid="stats-ptr"]');
  const scroller = document.querySelector('[data-testid="stats-page"]');
  if (!zone || !scroller) return false;
  return zone.getAttribute('data-state') === 'idle' && !scroller.style.getPropertyValue('--ft-ptr-offset');
}"""


# ---------------------------------------------------------------------------------------------- 小工具


def approx(actual, expected, tolerance: float = 1.0) -> bool:
    return actual is not None and abs(actual - expected) <= tolerance


def tid(page, testid: str):
    return page.get_by_test_id(testid)


def plain(value) -> str:
    """金额 → data-amount 的写法：两位小数、无千分位。"""
    return f"{Decimal(str(value)):.2f}"


def money(value) -> str:
    """金额 → 界面文字（¥ 加千分位加两位小数）。"""
    return f"¥{Decimal(str(value)):,.2f}"


def amount_of(page, testid: str):
    return tid(page, testid).get_attribute("data-amount")


def shift_month(year: int, month: int, delta: int) -> "tuple[int, int]":
    index = year * 12 + (month - 1) + delta
    return index // 12, index % 12 + 1


def month_label(year: int, month: int) -> str:
    return f"{year}年{month}月"


def percent_text(amount, total) -> "tuple[Decimal, str]":
    """统计服务的占比（两位小数，银行家舍入）与图例上的占比文字（一位小数，四舍五入；不足 0.1% 显示 <0.1%）。"""
    two = (Decimal(str(amount)) / Decimal(str(total)) * 100).quantize(Decimal("0.01"), rounding=ROUND_HALF_EVEN)
    one = two.quantize(Decimal("0.1"), rounding=ROUND_HALF_UP)
    return two, ("<0.1%" if 0 < two < Decimal("0.05") else f"{one}%")


def wait_stats_loaded(page) -> None:
    """月度视图 ready（KPI 金额出现 = 骨架已被真实数据替换）；再等页面进入 / 切换的过渡结束，否则量到的是动画中间帧。"""
    page.wait_for_function(
        "() => { const el = document.querySelector('[data-testid=\"stats-monthly\"]'); return !!el && el.getAttribute('data-state') === 'ready'; }"
    )
    tid(page, "stats-kpi-expense").wait_for(state="visible")
    wait_animations_settled(page)


def go_stats(page) -> None:
    """整页进入统计 Tab 并等数据加载完（造数要在这之前做：Tab 根页面首次进入时读库）。"""
    open_app(page, "/statistics")
    wait_tab_bar(page)
    wait_stats_loaded(page)


def wait_kpi(page, expense, income) -> None:
    page.wait_for_function(
        """([e, i]) => {
          const get = (id) => { const el = document.querySelector(`[data-testid="${id}"]`); return el ? el.getAttribute('data-amount') : null; };
          return get('stats-kpi-expense') === e && get('stats-kpi-income') === i;
        }""",
        arg=[plain(expense), plain(income)],
    )


def wait_month(page, year: int, month: int) -> None:
    """月度视图切到了该月并加载完（年月属性与 ready 状态在同一次渲染里变化）。"""
    page.wait_for_function(
        """([y, m]) => {
          const el = document.querySelector('[data-testid="stats-monthly"]');
          return !!el && el.getAttribute('data-year') === String(y) && el.getAttribute('data-month') === String(m) && el.getAttribute('data-state') === 'ready';
        }""",
        arg=[year, month],
    )


def wait_text_of(page, testid: str, text: str) -> None:
    page.wait_for_function(
        "([id, t]) => { const el = document.querySelector(`[data-testid=\"${id}\"]`); return !!el && el.textContent.trim() === t; }",
        arg=[testid, text],
    )


def wait_gone(page, testid: str) -> None:
    page.wait_for_function("(id) => !document.querySelector(`[data-testid=\"${id}\"]`)", arg=testid)


def wait_ptr_idle(page) -> None:
    page.wait_for_function(PTR_IDLE_JS)


def segment_button(page, value: str):
    return tid(page, "stats-segment").locator(f'[data-testid="segment"][data-value="{value}"]')


def switch_segment(page, value: str) -> None:
    segment_button(page, value).click()
    page.wait_for_function(
        "(v) => document.querySelector('[data-testid=\"stats-segment\"]').getAttribute('data-value') === v", arg=value
    )


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


def expect_rows(rows, expected, with_counts: bool = True):
    """图例行 = 期望的 (名称, 金额, 笔数) 列表（顺序即排名）。"""
    names = [r["name"] for r in rows]
    amounts = [r["amount"] for r in rows]
    counts = [r["count"] for r in rows]
    ok = names == [e[0] for e in expected] and amounts == [plain(e[1]) for e in expected]
    if with_counts:
        ok = ok and counts == [str(e[2]) for e in expected]
    return ok


# ---------------------------------------------------------------------------------------------- 造数


class Dataset:
    """一份有代表性的账单：跨月、跨账本、含历史无账本账单、含别人在共享账本里记的账单、含月初 / 月末的边界账单。"""

    def __init__(self, db: LocalDb, user_id: str, *, budget: bool = False) -> None:
        now = datetime.now()
        self.now = now
        self.year, self.month = now.year, now.month
        self.prev_year, self.prev_month = shift_month(now.year, now.month, -1)
        first = datetime(now.year, now.month, 1)

        def day(d: int, hour: int = 12) -> datetime:
            return first.replace(day=d, hour=hour)

        self.ledger_a_name = unique_name("统计账本甲")
        self.ledger_b_name = unique_name("统计账本乙")
        self.ledger_a = db.seed_ledger(user_id, self.ledger_a_name, sort_order=1)
        self.ledger_b = db.seed_ledger(user_id, self.ledger_b_name, sort_order=2)
        cat = db.category_id_by_name
        income = lambda name: db.category_id_by_name(name, BILL_TYPE_INCOME)  # noqa: E731

        # 本月支出
        db.seed_bill(user_id, amount="400.00", ledger_id=self.ledger_a, category_id=cat("餐饮美食"), when=day(2))
        db.seed_bill(user_id, amount="180.00", ledger_id=self.ledger_a, category_id=cat("餐饮美食"), when=day(3))
        db.seed_bill(user_id, amount="110.00", ledger_id=self.ledger_a, category_id=cat("交通出行"), when=day(4))
        db.seed_bill(user_id, amount="10.00", ledger_id=self.ledger_a, category_id=cat("交通出行"), when=first)  # 月初 00:00:00：左闭，算本月
        db.seed_bill(user_id, amount="300.00", ledger_id=self.ledger_b, category_id=cat("购物消费"), when=day(5))
        db.seed_bill(user_id, amount="1200.50", ledger_id=self.ledger_b, category_id=cat("居住生活"), when=day(6))
        db.seed_legacy_bill(user_id, amount="18.00", category_id=cat("其他支出"), when=day(7))  # 历史无账本账单
        # 本月收入
        db.seed_bill(user_id, amount="12000.00", ledger_id=self.ledger_a, bill_type=BILL_TYPE_INCOME, category_id=income("工资薪酬"), when=day(8))
        db.seed_bill(user_id, amount="50.00", ledger_id=self.ledger_b, bill_type=BILL_TYPE_INCOME, category_id=income("其他收入"), when=day(9))
        # 上个月：最后一分钟的支出（右开：不算本月）+ 一笔收入
        db.seed_bill(user_id, amount="45.00", ledger_id=self.ledger_a, category_id=cat("餐饮美食"), when=first - timedelta(minutes=1))
        db.seed_bill(
            user_id, amount="1000.00", ledger_id=self.ledger_a, bill_type=BILL_TYPE_INCOME, category_id=income("工资薪酬"),
            when=datetime(self.prev_year, self.prev_month, 15, 12),
        )
        # 别人在共享账本（甲）里记的账单：统计只算自己记的，不计入
        other = db.seed_user()
        db.seed_ledger_member(self.ledger_a, other)
        db.seed_bill(other, amount="999.00", ledger_id=self.ledger_a, category_id=cat("购物消费"), when=day(5))

        if budget:
            db.seed_budget(user_id, amount=5000)

        # 期望值（本月）
        self.expense = Decimal("2218.50")
        self.income = Decimal("12050.00")
        self.balance = self.income - self.expense
        self.expense_categories = [
            ("居住生活", "1200.50", 1),
            ("餐饮美食", "580.00", 2),
            ("购物消费", "300.00", 1),
            ("交通出行", "120.00", 2),
            ("其他支出", "18.00", 1),
        ]
        self.expense_ledgers = [
            (self.ledger_b_name, "1500.50", 2),
            (self.ledger_a_name, "700.00", 4),
            ("未归属账本", "18.00", 1),
        ]
        self.income_categories = [("工资薪酬", "12000.00", 1), ("其他收入", "50.00", 1)]
        self.income_ledgers = [(self.ledger_a_name, "12000.00", 1), (self.ledger_b_name, "50.00", 1)]
        # 上个月
        self.prev_expense = Decimal("45.00")
        self.prev_income = Decimal("1000.00")
        # 只看甲账本（本月）
        self.a_expense = Decimal("700.00")
        self.a_income = Decimal("12000.00")
        self.a_expense_categories = [("餐饮美食", "580.00", 2), ("交通出行", "120.00", 2)]


# ---------------------------------------------------------------------------------------------- A. 新用户


def check_new_user(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        login(page)
        go_stats(page)
        now = datetime.now()

        layout = page.evaluate(LAYOUT_JS)
        run.check("【大标题】「统计」是一级标题", layout["title"] == {"tag": "H1", "text": "统计"}, str(layout["title"]))
        run.check(
            "统计页是滚动容器（data-scroll-root）且占满整屏高度，底部给 Tab 栏让出位置",
            layout["isScrollRoot"] and layout["page"]["overflowY"] == "auto" and approx(layout["page"]["height"], SCREEN_H, 1) and layout["page"]["paddingBottom"] >= 80,
            str(layout["page"]),
        )
        run.check("导航栏右侧有「自定义报表」，点击区域 ≥ 44", layout["report"] and layout["report"]["width"] >= 44 and layout["report"]["height"] >= 44, str(layout["report"]))
        run.check("自定义报表的读屏名称", tid(page, "stats-report").get_attribute("aria-label") == "自定义报表")
        run.check("【导航栏】没有按钮形式的刷新入口（刷新只靠下拉）：页面里没有 stats-refresh / 「刷新」按钮 / 刷新图标", layout["refreshEntries"] == 0, str(layout["refreshEntries"]))

        # ---- 分段 ----
        seg = tid(page, "stats-segment")
        items = seg.locator('[data-testid="segment"]')
        run.check("【分段】月度 / 趋势 / 年度，默认月度", items.all_inner_texts() == ["月度", "趋势", "年度"] and seg.get_attribute("data-value") == "monthly", str(items.all_inner_texts()))
        run.check("【分段】读屏名称「统计维度」；每段高 ≥ 44", seg.get_attribute("aria-label") == "统计维度" and all((b.bounding_box() or {"height": 0})["height"] >= 44 for b in items.all()))

        # ---- 月份胶囊 ----
        period = page.evaluate(PERIOD_JS)
        prev_y, prev_m = shift_month(now.year, now.month, -1)
        next_y, next_m = shift_month(now.year, now.month, 1)
        run.check("【月份胶囊】显示当月「YYYY年M月」", period["label"] == month_label(now.year, now.month) and period["dataLabel"] == period["label"], str(period))
        run.check(
            "【月份胶囊】左右箭头是按钮，读屏名称带上目标月份；整组读屏名称「月份」；文字是 aria-live=polite；当月时「下一月」置灰（未来没有账单），「上一月」可用",
            period["role"] == "group" and period["groupLabel"] == "月份" and period["live"] == "polite"
            and period["prevLabel"] == f"上一月，{month_label(prev_y, prev_m)}" and period["nextLabel"] == f"下一月，{month_label(next_y, next_m)}"
            and period["prevTag"] == "BUTTON" and period["nextTag"] == "BUTTON" and not period["prevDisabled"] and period["nextDisabled"],
            str(period),
        )
        run.check(
            "【月份胶囊】白色胶囊（高 44，全圆角），两个箭头点击区域 ≥ 44 × 44",
            layout["period"]["bg"] == RGB_WHITE and approx(layout["period"]["height"], 44, 0.5) and layout["period"]["radius"] in ("999px", "9999px", "50%")
            and layout["prev"]["width"] >= 44 and layout["prev"]["height"] >= 44 and layout["next"]["width"] >= 44 and layout["next"]["height"] >= 44,
            f"{layout['period']} {layout['prev']} {layout['next']}",
        )
        run.check("没有账本：不显示账本胶囊", tid(page, "stats-ledger-pill").count() == 0)
        run.check(
            "【版面顺序】分段 → 月份胶囊 → KPI 卡，月份胶囊靠左贴页面边距",
            layout["segment"]["bottom"] <= layout["filters"]["top"] + 1 and layout["filters"]["bottom"] <= layout["kpi"]["top"] + 1 and approx(layout["period"]["left"], 16, 1),
            f"{layout['segment']} {layout['filters']} {layout['kpi']}",
        )

        # ---- KPI ----
        kpi = page.evaluate(KPI_JS)
        run.check("【KPI】三栏：支出 / 收入 / 结余，金额都是 0.00", [c["label"] for c in kpi["cells"]] == ["支出", "收入", "结余"] and [c["amount"] for c in kpi["cells"]] == ["0.00"] * 3, str(kpi["cells"]))
        run.check("【KPI】没有加载中标记", kpi["busy"] is None)

        # ---- 构成 ----
        comp = page.evaluate(COMPOSITION_JS)
        run.check("【构成】标题「支出构成」、圆环中心「总支出」与 0.00，空环（0 段）", comp["title"] == "支出构成" and comp["caption"] == "总支出" and comp["total"] == "0.00" and comp["slices"] == "0", f"{comp['title']} {comp['caption']} {comp['total']} {comp['slices']}")
        run.check("【构成】没有图例，给出「当月暂无支出账单」", len(comp["rows"]) == 0 and comp["empty"] == "当月暂无支出账单" and comp["legendTag"] is None, str(comp["empty"]))
        run.check("【构成】没有账本：不提供「按账本」，但可以切换看收入", comp["dimension"] is None and comp["switchTarget"] == "income" and comp["switchText"] == "收入", str(comp))
        run.check("【构成】没有预算：不显示「预算执行」节", tid(page, "stats-budget-header").count() == 0 and tid(page, "stats-budget-group").count() == 0)
        titles = page.evaluate(SECTION_TITLES_JS)
        run.check("【节标题】支出构成 → 分类排行 → 支出日历 → 累计收支（都是 h2）", titles == ["支出构成", "分类排行", "支出日历", "累计收支"], str(titles))

        for panel, text in (("stats-rank", "当月暂无支出账单"), ("stats-calendar", "当月暂无支出账单"), ("stats-cumulative", "当月暂无收支账单")):
            run.check(
                f"【{panel}】没有数据：空状态与说明文字",
                tid(page, panel).get_attribute("data-state") == "empty" and tid(page, f"{panel}-empty").inner_text().strip() == text,
                str(tid(page, panel).get_attribute("data-state")),
            )

        run.check("没有横向溢出；玻璃层 ≤ 3", layout["overflowX"] <= 0 and page.evaluate(GLASS_LAYERS_JS) <= 3, f"{layout['overflowX']} / {page.evaluate(GLASS_LAYERS_JS)}")
        shot(page, "stats-new-user")

        run.check_clean(s.errors, "A 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- B. 有数据的月度


def check_monthly_data(run: TestRun, db: LocalDb) -> None:
    """B1. KPI、构成、切换、预算、无障碍与布局"""
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        d = Dataset(db, user_id, budget=True)
        go_stats(page)
        wait_kpi(page, d.expense, d.income)

        # ---- KPI ----
        kpi = page.evaluate(KPI_JS)
        cells = kpi["cells"]
        run.check(
            "【KPI】支出 / 收入 / 结余 = 本月自己记的账单（月初 00:00 的算本月、上月最后一分钟的不算、别人在共享账本里记的不算）",
            [c["amount"] for c in cells] == [plain(d.expense), plain(d.income), plain(d.balance)] and [c["testid"] for c in cells] == ["stats-kpi-expense", "stats-kpi-income", "stats-kpi-balance"],
            str([c["amount"] for c in cells]),
        )
        run.check("【KPI】栏标题是「支出 / 收入 / 结余」", [c["label"] for c in cells] == ["支出", "收入", "结余"], str([c["label"] for c in cells]))
        run.check(
            "【KPI】金额统一中性文字色加粗（颜色只出现在小圆点上）：黑色、700、18px",
            all(c["color"] == RGB_LABEL and c["weight"] == "700" and c["size"] == "18px" for c in cells),
            str([(c["color"], c["weight"], c["size"]) for c in cells]),
        )
        run.check(
            "【KPI】小圆点：支出红、收入绿、结余品牌色（8px）",
            [c["dot"] for c in cells] == [RGB_RED, RGB_GREEN, RGB_BRAND] and all(approx(c["dotWidth"], 8, 0.5) for c in cells),
            str([(c["dot"], c["dotWidth"]) for c in cells]),
        )
        run.check(
            "【KPI】金额整体读屏（role=img + 完整金额，带千分位）",
            [c["aria"] for c in cells] == [money(d.expense), money(d.income), money(d.balance)] and all(c["role"] == "img" for c in cells),
            str([c["aria"] for c in cells]),
        )
        run.check("【KPI】白色圆角卡（26px），三栏金额都在各自栏内", kpi["bg"] == RGB_WHITE and kpi["radius"] == "26px" and all(c["aLeft"] >= c["left"] - 0.5 and c["aRight"] <= c["right"] + 0.5 for c in cells), f"{kpi['bg']} {kpi['radius']}")

        # ---- 构成：按分类、支出 ----
        comp = page.evaluate(COMPOSITION_JS)
        rows, arcs = comp["rows"], comp["arcs"]
        run.check("【构成】标题「支出构成」、圆环中心「总支出」+ 本月支出合计", comp["title"] == "支出构成" and comp["caption"] == "总支出" and comp["total"] == plain(d.expense), f"{comp['title']} {comp['caption']} {comp['total']}")
        run.check("【构成】图例按金额降序（居住 > 餐饮 > 购物 > 交通 > 其他），金额与笔数正确", expect_rows(rows, d.expense_categories), str([(r["name"], r["amount"], r["count"]) for r in rows]))
        expected_pct = [percent_text(amount, d.expense) for _, amount, _ in d.expense_categories]
        run.check(
            "【构成】占比：数据属性是服务算出的两位小数，图例文字是一位小数的百分比",
            [Decimal(r["percent"]) for r in rows] == [p[0] for p in expected_pct] and [r["pct"] for r in rows] == [p[1] for p in expected_pct],
            f"{[(r['percent'], r['pct']) for r in rows]} / {expected_pct}",
        )
        run.check(
            "【构成】圆环 5 段，顺序与图例一致，每段占比 = 金额 / 合计，所有段之和为 1",
            comp["slices"] == "5" and [a["name"] for a in arcs] == [r["name"] for r in rows]
            and all(abs(a["fraction"] - float(Decimal(r["amount"]) / d.expense)) < 0.001 for a, r in zip(arcs, rows)) and abs(sum(a["fraction"] for a in arcs) - 1) < 0.001,
            str([(a["name"], a["fraction"]) for a in arcs]),
        )
        dots = [r["dot"] for r in rows]
        run.check(
            "【构成】图例圆点与圆环段颜色一一对应，5 种颜色互不相同，第一名是品牌色",
            dots == [a["stroke"] for a in arcs] and len(set(dots)) == 5 and dots[0] == RGB_BRAND,
            f"{dots} / {[a['stroke'] for a in arcs]}",
        )
        run.check("【构成】每行图例高 ≥ 44；金额读屏文案带千分位", all(r["height"] >= 44 for r in rows) and [r["amountText"] for r in rows][0] == "¥1,200.50", str([r["height"] for r in rows]))
        run.check(
            "【构成】白色圆角卡；圆环水平居中、不超过 200px（--ft-donut-size，验收阶段由 224 收到 200，设计稿外径约 184pt）；环中心文字放得进孔（孔径约为外径 68%）",
            comp["panelBg"] == RGB_WHITE and comp["panelRadius"] == "26px" and approx(comp["donutBox"]["cx"], comp["panelCx"], 1.5)
            and comp["donutBox"]["width"] <= 200.5 and comp["totalBox"]["width"] <= comp["donutBox"]["width"] * 0.68,
            f"{comp['donutBox']} {comp['totalBox']}",
        )
        run.check(
            "【构成】读屏：圆环是装饰（aria-hidden），图例是列表并带名称「支出构成明细（按分类）」",
            comp["svgHidden"] == "true" and comp["legendTag"] == "UL" and comp["legendLabel"] == "支出构成明细（按分类）",
            f"{comp['svgHidden']} {comp['legendTag']} {comp['legendLabel']}",
        )
        run.check(
            "【构成】标题旁的切换按钮：「收入」，读屏名称「切换为收入构成」，点击区域 ≥ 44；右侧「按账本」",
            comp["switchText"] == "收入" and comp["switchLabel"] == "切换为收入构成" and comp["switchTarget"] == "income" and comp["switchHeight"] >= 44
            and comp["dimension"] == {"mode": "category", "text": "按账本", "label": "按账本查看构成"},
            str(comp),
        )

        # ---- 切换：收入 ----
        tid(page, "stats-comp-switch").click()
        wait_text_of(page, "stats-comp-title", "收入构成")
        comp = page.evaluate(COMPOSITION_JS)
        income_pct = [percent_text(amount, d.income) for _, amount, _ in d.income_categories]
        run.check(
            "【切换收入】标题「收入构成」、中心「总收入」+ 收入合计，图例是收入分类（工资 > 其他），占比正确",
            comp["caption"] == "总收入" and comp["total"] == plain(d.income) and expect_rows(comp["rows"], d.income_categories)
            and [r["pct"] for r in comp["rows"]] == [p[1] for p in income_pct] and comp["slices"] == "2",
            f"{comp['caption']} {comp['total']} {[(r['name'], r['amount'], r['pct']) for r in comp['rows']]}",
        )
        run.check(
            "【切换收入】按钮变成「支出」（读屏「切换为支出构成」），图例读屏名称随之变化；KPI 不受影响",
            comp["switchText"] == "支出" and comp["switchLabel"] == "切换为支出构成" and comp["legendLabel"] == "收入构成明细（按分类）"
            and amount_of(page, "stats-kpi-expense") == plain(d.expense),
            f"{comp['switchText']} {comp['switchLabel']} {comp['legendLabel']}",
        )

        # ---- 切换：按账本（收入）----
        tid(page, "stats-comp-dimension").click()
        wait_text_of(page, "stats-comp-dimension", "按分类")
        comp = page.evaluate(COMPOSITION_JS)
        run.check(
            "【收入 · 按账本】图例是各账本的收入（甲 12000 > 乙 50），读屏名称「收入构成明细（按账本）」，口径按钮变成「按分类」",
            expect_rows(comp["rows"], d.income_ledgers) and comp["legendLabel"] == "收入构成明细（按账本）" and comp["dimension"]["mode"] == "ledger" and comp["dimension"]["label"] == "按分类查看构成",
            str([(r["name"], r["amount"]) for r in comp["rows"]]),
        )

        # ---- 切换：按账本（支出，含未归属账本）----
        tid(page, "stats-comp-switch").click()
        wait_text_of(page, "stats-comp-title", "支出构成")
        comp = page.evaluate(COMPOSITION_JS)
        ledger_pct = [percent_text(amount, d.expense) for _, amount, _ in d.expense_ledgers]
        run.check(
            "【支出 · 按账本】乙 1500.50 > 甲 700.00 > 未归属账本 18.00（历史无账本账单归「未归属账本」；别人记的 999 不计入），合计不变",
            expect_rows(comp["rows"], d.expense_ledgers) and comp["total"] == plain(d.expense) and comp["slices"] == "3"
            and [r["pct"] for r in comp["rows"]] == [p[1] for p in ledger_pct],
            f"{[(r['name'], r['amount'], r['count']) for r in comp['rows']]} {comp['total']}",
        )
        run.check("【支出 · 按账本】圆点与圆环段颜色仍一一对应", [r["dot"] for r in comp["rows"]] == [a["stroke"] for a in comp["arcs"]] and len({r["dot"] for r in comp["rows"]}) == 3)
        tid(page, "stats-comp-dimension").click()
        wait_text_of(page, "stats-comp-dimension", "按账本")
        comp = page.evaluate(COMPOSITION_JS)
        run.check("【按分类】切回后恢复 5 行分类图例", expect_rows(comp["rows"], d.expense_categories) and comp["legendLabel"] == "支出构成明细（按分类）")

        # ---- 预算执行 ----
        titles = page.evaluate(SECTION_TITLES_JS)
        run.check("【节标题】有预算时：支出构成 → 预算执行 → 分类排行 → 支出日历 → 累计收支", titles == ["支出构成", "预算执行", "分类排行", "支出日历", "累计收支"], str(titles))
        budget_rows = tid(page, "stats-budget-group").get_by_test_id("budget-row")
        run.check("【预算】只有「总预算」一行（全部账本、正常状态）", budget_rows.count() == 1 and budget_rows.first.get_attribute("data-state") == "normal" and tid(page, "stats-budget-group").get_by_test_id("budget-name").inner_text().strip() == "总预算")
        run.check(
            "【预算】剩余 = 5000 − 本月支出合计",
            tid(page, "stats-budget-group").get_by_test_id("budget-remain").inner_text().strip() == f"剩余 {money(Decimal(5000) - d.expense)}",
            tid(page, "stats-budget-group").get_by_test_id("budget-remain").inner_text(),
        )
        run.check("【预算】节标题带「管理」入口（读屏「管理预算」）", tid(page, "stats-budget-manage").get_attribute("aria-label") == "管理预算")

        # ---- 图表节：有数据 ----
        run.check(
            "【图表】分类排行 / 支出日历 / 累计收支都有数据（不是空状态）",
            all(tid(page, p).get_attribute("data-state") == "ready" for p in ("stats-rank", "stats-calendar", "stats-cumulative")),
            str([tid(page, p).get_attribute("data-state") for p in ("stats-rank", "stats-calendar", "stats-cumulative")]),
        )

        # ---- 布局 ----
        layout = page.evaluate(LAYOUT_JS)
        run.check("没有横向溢出；玻璃层 ≤ 3", layout["overflowX"] <= 0 and page.evaluate(GLASS_LAYERS_JS) <= 3, f"{layout['overflowX']} / {page.evaluate(GLASS_LAYERS_JS)}")
        state = scroll_root_state(page)
        run.check("内容比一屏长，页面可以滚动", state is not None and state["max"] > 400, str(state))
        scroll_root_to(page, "bottom")
        page.wait_for_function("() => { const r = document.querySelector('[data-testid=\"stats-page\"]'); return r.scrollTop >= r.scrollHeight - r.clientHeight - 1; }")
        wait_animations_settled(page)
        bottoms = page.evaluate(
            """() => ({
              panel: document.querySelector('[data-testid="stats-cumulative"]').getBoundingClientRect().bottom,
              tab: document.querySelector('[data-testid="tab-bar"]').getBoundingClientRect().top,
            })"""
        )
        run.check("滚到底：最后一节的卡片完整露出，在 Tab 栏之上", bottoms["panel"] <= bottoms["tab"] + 1, str(bottoms))
        scroll_root_to(page, 0)
        shot(page, "stats-month")

        run.check_clean(s.errors, "B1 节会话无页面异常 / 控制台错误 / 失败请求")


def check_month_navigation(run: TestRun, db: LocalDb) -> None:
    """B2. 月份切换"""
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        d = Dataset(db, user_id, budget=True)
        go_stats(page)
        wait_kpi(page, d.expense, d.income)
        now = datetime.now()

        # ---- 上一月 ----
        tid(page, "stats-period-prev").click()
        wait_month(page, d.prev_year, d.prev_month)
        wait_kpi(page, d.prev_expense, d.prev_income)
        period = page.evaluate(PERIOD_JS)
        run.check("【上一月】胶囊文字、数据属性与读屏名称都切到上一月", period["label"] == month_label(d.prev_year, d.prev_month), str(period))
        run.check(
            "【上一月】KPI = 上月数据（上月最后一分钟的 45 算上月；月初 00:00 的 10 不算上月）",
            amount_of(page, "stats-kpi-expense") == plain(d.prev_expense) and amount_of(page, "stats-kpi-income") == plain(d.prev_income)
            and amount_of(page, "stats-kpi-balance") == plain(d.prev_income - d.prev_expense),
            f"{amount_of(page, 'stats-kpi-expense')} {amount_of(page, 'stats-kpi-income')}",
        )
        comp = page.evaluate(COMPOSITION_JS)
        run.check(
            "【上一月】构成只有餐饮一类：圆环只有一段（占满一圈，占比 100.0%）",
            comp["slices"] == "1" and expect_rows(comp["rows"], [("餐饮美食", "45.00", 1)]) and comp["rows"][0]["pct"] == "100.0%"
            and len(comp["arcs"]) == 1 and abs(comp["arcs"][0]["fraction"] - 1) < 1e-6,
            f"{comp['slices']} {comp['rows']}",
        )
        run.check(
            "【上一月】当月预算只在当月：上月没有预算节",
            tid(page, "stats-budget-header").count() == 0 and page.evaluate(SECTION_TITLES_JS) == ["支出构成", "分类排行", "支出日历", "累计收支"],
            str(page.evaluate(SECTION_TITLES_JS)),
        )

        # ---- 下一月（回到本月）----
        tid(page, "stats-period-next").click()
        wait_month(page, now.year, now.month)
        wait_kpi(page, d.expense, d.income)
        run.check("【下一月】回到本月：KPI 恢复，预算节重新出现", amount_of(page, "stats-kpi-balance") == plain(d.balance) and tid(page, "stats-budget-header").count() == 1)

        # ---- 未来月份：切不过去（「下一月」在当月置灰）----
        nxt = tid(page, "stats-period-next")
        run.check("【未来月份】当月时「下一月」置灰（disabled），读屏名称仍带目标月份", nxt.is_disabled() and nxt.get_attribute("aria-label") == f"下一月，{month_label(*shift_month(now.year, now.month, 1))}", str(nxt.get_attribute("aria-label")))
        nxt.click(force=True)  # 强制点击也不会生效：禁用的按钮不派发 click
        page.wait_for_timeout(300)
        run.check("【未来月份】强制点击「下一月」没有反应：仍是本月，KPI 不变", page.evaluate(PERIOD_JS)["label"] == month_label(now.year, now.month) and amount_of(page, "stats-kpi-expense") == plain(d.expense), page.evaluate(PERIOD_JS)["label"])
        tid(page, "stats-period-prev").click()
        wait_month(page, d.prev_year, d.prev_month)
        run.check("【未来月份】退回上月后「下一月」又可用（只有当月是上限）", not tid(page, "stats-period-next").is_disabled())
        tid(page, "stats-period-next").click()
        wait_month(page, now.year, now.month)
        wait_kpi(page, d.expense, d.income)

        # ---- 快速连点：只显示最后一次的结果，不错乱 ----
        for _ in range(3):
            tid(page, "stats-period-prev").click()
        ty, tm = shift_month(now.year, now.month, -3)
        wait_month(page, ty, tm)
        wait_kpi(page, 0, 0)
        run.check(
            "【快速连点】连点 3 次「上一月」：最终停在三个月前，胶囊、数据属性、KPI 一致，没有残留上一次的数据",
            page.evaluate(PERIOD_JS)["label"] == month_label(ty, tm) and amount_of(page, "stats-kpi-expense") == "0.00",
            str(page.evaluate(PERIOD_JS)),
        )

        # ---- 跨年：一路退到去年 12 月 ----
        # 已经退了 3 个月；再退到「去年 12 月」需要的步数 = 本月数 - 3（可能为负：本月在 1–3 月时已经跨过了）
        cur_y, cur_m = ty, tm
        target = (now.year - 1, 12)
        steps = 0
        while (cur_y, cur_m) > target and steps < 24:
            tid(page, "stats-period-prev").click()
            cur_y, cur_m = shift_month(cur_y, cur_m, -1)
            wait_text_of(page, "stats-period-label", month_label(cur_y, cur_m))
            steps += 1
        wait_month(page, cur_y, cur_m)
        run.check("【跨年】一路「上一月」，1 月之后是上一年 12 月", page.evaluate(PERIOD_JS)["label"] == month_label(now.year - 1, 12) or (cur_y, cur_m) < target, f"{page.evaluate(PERIOD_JS)['label']}")
        if (cur_y, cur_m) == target:
            tid(page, "stats-period-next").click()
            wait_month(page, now.year, 1)
            run.check("【跨年】12 月之后「下一月」是下一年 1 月", page.evaluate(PERIOD_JS)["label"] == month_label(now.year, 1), page.evaluate(PERIOD_JS)["label"])

        run.check_clean(s.errors, "B2 节会话无页面异常 / 控制台错误 / 失败请求")


def check_ledger_filter(run: TestRun, db: LocalDb) -> None:
    """B3. 账本筛选：菜单、重算、「按账本」消失、Tab 切换保留状态"""
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        d = Dataset(db, user_id, budget=True)
        # 别人的账本（我是编辑成员）：不出现在账本菜单里
        other = db.seed_user()
        foreign = db.seed_ledger(other, unique_name("别人的账本"))
        db.seed_ledger_member(foreign, user_id)
        go_stats(page)
        wait_kpi(page, d.expense, d.income)

        pill = page.evaluate(PILL_JS)
        layout = page.evaluate(LAYOUT_JS)
        run.check(
            "【账本胶囊】默认「全部账本」：按钮，读屏「账本：全部账本，点按切换」并声明会弹出对话框；白色胶囊（高 44）靠右贴页面边距",
            pill["tag"] == "BUTTON" and pill["text"] == "全部账本" and pill["value"] == "全部账本" and pill["aria"] == "账本：全部账本，点按切换" and pill["haspopup"] == "dialog"
            and pill["bg"] == RGB_WHITE and approx(pill["height"], 44, 0.5) and approx(pill["right"], SCREEN_W - 16, 1),
            str(pill),
        )
        run.check(
            "【账本胶囊】与月份胶囊在同一行：月份在左、账本在右，互不重叠",
            approx(layout["period"]["top"], layout["pill"]["top"], 1) and layout["period"]["right"] <= layout["pill"]["left"] + 0.5,
            f"{layout['period']} {layout['pill']}",
        )

        # ---- 账本菜单 ----
        open_ledger_sheet(page)
        sheet = page.evaluate(SHEET_JS)
        names = {o["text"] for o in sheet["options"]}
        run.check(
            "【账本菜单】标题「选择账本」；第一项「全部账本」带对勾（aria-current）；选项只有自己的两个账本（不含别人的账本）",
            sheet["title"] == "选择账本" and sheet["all"] == {"current": "true", "text": "全部账本"} and names == {d.ledger_a_name, d.ledger_b_name}
            and all(o["current"] is None for o in sheet["options"]) and sheet["dialog"] == "dialog" and sheet["modal"] == "true",
            str(sheet),
        )
        shot(page, "stats-ledger-sheet")
        run.check("【账本菜单】Tab 栏被菜单盖住（z 层级）", page.evaluate("() => { const r = document.querySelector('[data-testid=\"tab-bar\"]').getBoundingClientRect(); const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2); return !!hit && !!hit.closest('[data-testid=\"actionsheet-host\"]'); }"))
        tid(page, "actionsheet-cancel").click()
        tid(page, "stats-ledger-sheet").wait_for(state="detached")
        run.check("【账本菜单】点「取消」收起，不改变筛选", page.evaluate(PILL_JS)["text"] == "全部账本" and amount_of(page, "stats-kpi-expense") == plain(d.expense))

        # ---- 选甲账本 ----
        pick_ledger(page, d.ledger_a)
        wait_kpi(page, d.a_expense, d.a_income)
        pill = page.evaluate(PILL_JS)
        run.check("【选甲账本】胶囊显示账本名称，读屏名称带上账本", pill["text"] == d.ledger_a_name and pill["aria"] == f"账本：{d.ledger_a_name}，点按切换", str(pill))
        run.check(
            "【选甲账本】KPI 只算甲账本：支出 700、收入 12000、结余 11300（仍是自己记的账单）",
            [amount_of(page, t) for t in ("stats-kpi-expense", "stats-kpi-income", "stats-kpi-balance")] == [plain(d.a_expense), plain(d.a_income), plain(d.a_income - d.a_expense)],
            str([amount_of(page, t) for t in ("stats-kpi-expense", "stats-kpi-income", "stats-kpi-balance")]),
        )
        comp = page.evaluate(COMPOSITION_JS)
        run.check(
            "【选甲账本】构成只剩甲账本的分类（餐饮 580 > 交通 120），圆环 2 段；选了具体账本后不再提供「按账本」",
            expect_rows(comp["rows"], d.a_expense_categories) and comp["slices"] == "2" and comp["dimension"] is None,
            f"{[(r['name'], r['amount']) for r in comp['rows']]} {comp['dimension']}",
        )
        run.check(
            "【选甲账本】预算：全部账本的总预算仍在（预算口径随账本筛选：该账本的预算 + 全部账本的预算）",
            tid(page, "stats-budget-group").get_by_test_id("budget-row").count() == 1,
        )
        open_ledger_sheet(page)
        sheet = page.evaluate(SHEET_JS)
        current = [o["text"] for o in sheet["options"] if o["current"] == "true"]
        run.check("【再次打开菜单】对勾在甲账本上，「全部账本」没有对勾", current == [d.ledger_a_name] and sheet["all"]["current"] is None, str(sheet))
        tid(page, "actionsheet-cancel").click()
        tid(page, "stats-ledger-sheet").wait_for(state="detached")

        # ---- Tab 切换保留月份与账本 ----
        tid(page, "stats-period-prev").click()
        wait_month(page, d.prev_year, d.prev_month)
        wait_kpi(page, d.prev_expense, d.prev_income)
        open_tab(page, "账单")
        tid(page, "bills-segment").wait_for(state="visible")
        open_tab(page, "统计")
        wait_animations_settled(page)
        run.check(
            "【Tab 切换】切到账单再回来：月份与账本筛选都保留，数据没有被重置",
            page.evaluate(PERIOD_JS)["label"] == month_label(d.prev_year, d.prev_month) and page.evaluate(PILL_JS)["text"] == d.ledger_a_name
            and amount_of(page, "stats-kpi-expense") == plain(d.prev_expense),
            f"{page.evaluate(PERIOD_JS)['label']} {page.evaluate(PILL_JS)['text']}",
        )

        # ---- 回到全部账本 ----
        pick_ledger(page, None)
        wait_kpi(page, d.prev_expense, d.prev_income)
        run.check("【回到全部账本】胶囊恢复「全部账本」；该月（上月）有账本分组时「按账本」回来", page.evaluate(PILL_JS)["text"] == "全部账本" and page.evaluate(COMPOSITION_JS)["dimension"] is not None)
        tid(page, "stats-period-next").click()
        wait_month(page, d.year, d.month)
        wait_kpi(page, d.expense, d.income)
        run.check("【回到全部账本】本月 KPI 恢复为全部账本合计", amount_of(page, "stats-kpi-expense") == plain(d.expense))

        # ---- 按账本模式下选具体账本 → 自动回到按分类 ----
        tid(page, "stats-comp-dimension").click()
        wait_text_of(page, "stats-comp-dimension", "按分类")
        pick_ledger(page, d.ledger_b)
        wait_kpi(page, Decimal("1500.50"), Decimal("50.00"))
        comp = page.evaluate(COMPOSITION_JS)
        run.check(
            "【按账本时选账本】选了具体账本后回到按分类（乙账本：居住 1200.50 > 购物 300），之后回到全部账本也保持按分类",
            expect_rows(comp["rows"], [("居住生活", "1200.50", 1), ("购物消费", "300.00", 1)]) and comp["dimension"] is None,
            f"{[(r['name'], r['amount']) for r in comp['rows']]}",
        )
        pick_ledger(page, None)
        wait_kpi(page, d.expense, d.income)
        comp = page.evaluate(COMPOSITION_JS)
        run.check("【回到全部账本】图例仍是按分类（口径按钮显示「按账本」）", comp["dimension"] and comp["dimension"]["mode"] == "category" and expect_rows(comp["rows"], d.expense_categories), str(comp["dimension"]))

        run.check_clean(s.errors, "B3 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- C. 分段


def check_segments(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        busy_name, empty_name = unique_name("分段账本"), unique_name("空账本")
        busy = db.seed_ledger(user_id, busy_name, sort_order=1)
        empty = db.seed_ledger(user_id, empty_name, sort_order=2)
        now = datetime.now()
        yesterday, three_days = days_ago(1), days_ago(3)
        db.seed_bill(user_id, amount="77.00", ledger_id=busy, category_id=db.category_id_by_name("餐饮美食"), when=yesterday)
        db.seed_bill(user_id, amount="23.00", ledger_id=busy, bill_type=BILL_TYPE_INCOME, category_id=db.category_id_by_name("工资薪酬", BILL_TYPE_INCOME), when=three_days)
        this_year_expense = Decimal("77.00") if yesterday.year == now.year else Decimal(0)
        this_year_income = Decimal("23.00") if three_days.year == now.year else Decimal(0)
        go_stats(page)

        # ---- 趋势 ----
        switch_segment(page, "trend")
        tid(page, "stats-trend").wait_for(state="visible")
        page.wait_for_function("() => document.querySelector('[data-testid=\"stats-trend\"]').getAttribute('data-state') === 'ready' && !!document.querySelector('[data-testid=\"trend-total-expense\"]')")
        run.check(
            "【趋势】分段切到趋势：月度视图卸载、月份胶囊隐藏、账本胶囊保留",
            tid(page, "stats-monthly").count() == 0 and tid(page, "stats-period").count() == 0 and tid(page, "stats-ledger-pill").count() == 1
            and tid(page, "stats-segment").get_attribute("data-value") == "trend",
        )
        run.check(
            "【趋势】默认最近 7 天：总支出 77.00、总收入 23.00、净收支 −54.00（账本筛选对它生效）",
            [amount_of(page, t) for t in ("trend-total-expense", "trend-total-income", "trend-total-net")] == ["77.00", "23.00", "-54.00"],
            str([amount_of(page, t) for t in ("trend-total-expense", "trend-total-income", "trend-total-net")]),
        )
        wait_animations_settled(page)  # 滑块有位移过渡，量位置前等它就位
        seg = page.evaluate(
            """() => {
              const seg = document.querySelector('[data-testid="stats-segment"]');
              const thumb = seg.querySelector('.ft-seg__thumb').getBoundingClientRect();
              const on = [...seg.querySelectorAll('[data-testid="segment"]')].find((b) => b.getAttribute('aria-pressed') === 'true');
              const r = on.getBoundingClientRect();
              return { thumbCenter: (thumb.left + thumb.right) / 2, itemCenter: (r.left + r.right) / 2, text: on.textContent.trim() };
            }"""
        )
        run.check("【趋势】分段滑块跟随到「趋势」下面", seg["text"] == "趋势" and approx(seg["thumbCenter"], seg["itemCenter"], 1.5), str(seg))

        # ---- 账本筛选在分段间共用：选空账本 ----
        pick_ledger(page, empty)
        page.wait_for_function("() => document.querySelector('[data-testid=\"trend-total-expense\"]')?.getAttribute('data-amount') === '0.00'")
        run.check("【趋势】选「空账本」：总支出 / 总收入都是 0.00", amount_of(page, "trend-total-expense") == "0.00" and amount_of(page, "trend-total-income") == "0.00")

        # ---- 年度 ----
        switch_segment(page, "annual")
        tid(page, "stats-annual").wait_for(state="visible")
        page.wait_for_function("() => document.querySelector('[data-testid=\"stats-annual\"]').getAttribute('data-state') === 'ready' && !!document.querySelector('[data-testid=\"annual-total-expense\"]')")
        run.check(
            "【年度】沿用账本筛选（空账本）：当年数据为 0；账本胶囊仍显示空账本，月份胶囊不显示",
            tid(page, "stats-trend").count() == 0 and amount_of(page, "annual-total-expense") == "0.00" and page.evaluate(PILL_JS)["text"] == empty_name and tid(page, "stats-period").count() == 0,
            f"{amount_of(page, 'annual-total-expense')} {page.evaluate(PILL_JS)['text']}",
        )
        pick_ledger(page, None)
        page.wait_for_function("(e) => document.querySelector('[data-testid=\"annual-total-expense\"]')?.getAttribute('data-amount') === e", arg=plain(this_year_expense))
        run.check(
            "【年度】全部账本：当年总支出 / 总收入 = 自己当年的账单",
            amount_of(page, "annual-total-expense") == plain(this_year_expense) and amount_of(page, "annual-total-income") == plain(this_year_income)
            and tid(page, "stats-year-label").inner_text().strip() == f"{now.year}年",
            f"{amount_of(page, 'annual-total-expense')} {amount_of(page, 'annual-total-income')}",
        )
        tid(page, "stats-year-prev").click()
        page.wait_for_function("(y) => document.querySelector('[data-testid=\"stats-annual\"]').getAttribute('data-year') === String(y)", arg=now.year - 1)
        tid(page, "stats-year-next").click()
        page.wait_for_function("(y) => document.querySelector('[data-testid=\"stats-annual\"]').getAttribute('data-year') === String(y)", arg=now.year)

        # ---- 回到月度：月份保持 ----
        switch_segment(page, "monthly")
        wait_stats_loaded(page)
        run.check(
            "【回到月度】月份胶囊回来，仍是当月；月度视图重新出现并加载完",
            page.evaluate(PERIOD_JS)["label"] == month_label(now.year, now.month) and tid(page, "stats-annual").count() == 0 and tid(page, "stats-kpi").count() == 1,
        )

        run.check("没有横向溢出；玻璃层 ≤ 3", page.evaluate(LAYOUT_JS)["overflowX"] <= 0 and page.evaluate(GLASS_LAYERS_JS) <= 3)
        run.check_clean(s.errors, "C 节统计 Tab 会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- D. 刷新与二级页面


def check_refresh_and_secondary(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger = db.seed_ledger(user_id, unique_name("刷新账本"))
        now = datetime.now()
        first = datetime(now.year, now.month, 1)
        other_cat = db.category_id_by_name("其他支出")
        total = Decimal(0)
        # 内容要够长（> 一屏）才能验证「页面不在顶部不触发刷新」：多造几个分类让图例变长
        for i, name in enumerate(("餐饮美食", "交通出行", "购物消费", "娱乐休闲", "居住生活", "医疗健康", "其他支出")):
            db.seed_bill(user_id, amount=10, ledger_id=ledger, category_id=db.category_id_by_name(name), when=first.replace(day=2 + i, hour=12))
            total += 10
        go_stats(page)
        wait_kpi(page, total, 0)

        cloud = FakeCloud()
        try:
            cloud.start()
        except CloudPortBusy as exc:
            run.check("假云端可以启动（验证「刷新不触发同步」依赖它）", False, str(exc))
            cloud = None

        try:
            baseline = len(cloud.requests) if cloud else 0

            # ---- 拉得不够：不刷新 ----
            scroll_root_to(page, 0)
            wait_ptr_idle(page)
            total += 200
            db.seed_bill(user_id, amount=200, ledger_id=ledger, category_id=other_cat, when=first.replace(day=13, hour=12))
            pull_down(page, distance=40, from_y=300)
            wait_ptr_idle(page)
            run.check("下拉距离不够（没到触发阈值）：松手回弹，不刷新", amount_of(page, "stats-kpi-expense") == plain(total - 200), amount_of(page, "stats-kpi-expense"))

            # ---- 页面不在顶部：不触发 ----
            state = scroll_root_state(page)
            if state and state["max"] > 160:
                scroll_root_to(page, 150)
                page.wait_for_function("() => document.querySelector('[data-testid=\"stats-page\"]').scrollTop >= 149")
                pull_down(page, distance=120, from_y=300)
                wait_ptr_idle(page)
                run.check("页面不在顶部时向下拖只是滚动，不触发刷新", amount_of(page, "stats-kpi-expense") == plain(total - 200), amount_of(page, "stats-kpi-expense"))
                scroll_root_to(page, 0)
                page.wait_for_function("() => document.querySelector('[data-testid=\"stats-page\"]').scrollTop <= 1")
                wait_animations_settled(page)
            else:
                run.check("统计页内容足够长，能验证「不在顶部不触发刷新」", False, str(state))

            # ---- 完整下拉刷新（真实触摸）----
            pull_down(page, distance=240, from_y=300)
            wait_kpi(page, total, 0)
            run.check("下拉刷新（真实触摸）：KPI 更新为最新数据（造了一笔 200）", amount_of(page, "stats-kpi-expense") == plain(total), amount_of(page, "stats-kpi-expense"))
            wait_ptr_idle(page)
            run.check("刷新结束后页面不再标记忙碌（aria-busy 已移除）", tid(page, "stats-page").get_attribute("aria-busy") is None)
            run.check("刷新结束后下拉区收起，没有残留的位移变量", page.evaluate(PTR_IDLE_JS))
            run.check("下拉区的读屏状态播报区是 aria-live=polite", page.locator('[data-testid="stats-ptr"] [data-ptr-status]').get_attribute("aria-live") == "polite")

            if cloud is not None:
                # 页面读路径会顺带联网刷新共享账本的成员缓存（登录换令牌 + 拉成员关系，ADR 0006）：那是读取、不是同步，不计入；
                # 其余任何请求（推送 / 拉取账单、分类、账本……）才是「触发了同步」
                member_cache_paths = {"/api/auth/login", "/api/sync/ledgermembers/pull"}
                requests = [r for r in cloud.requests[baseline:] if r.path not in member_cache_paths]
                run.check(
                    "下拉刷新只重读本页数据，不触发同步（除读路径顺带刷新的成员缓存外，没有向云端发任何请求）",
                    len(requests) == 0,
                    "；".join(f"{r.method} {r.path}" for r in requests),
                )
        finally:
            if cloud is not None:
                cloud.stop()

        # ---- 自定义报表：二级页 + 页面关闭后重新加载 ----
        tid(page, "stats-report").click()
        wait_url(page, r"^/custom-report")
        wait_tab_bar(page, visible=False)
        run.check("【自定义报表】点右上角进入自定义报表页，Tab 栏隐藏", page.evaluate("() => location.pathname") == "/custom-report" and not tab_bar(page).is_visible())
        total += 1000
        db.seed_bill(user_id, amount=1000, ledger_id=ledger, category_id=other_cat, when=first.replace(day=14, hour=12))
        page.go_back()
        wait_url(page, r"^/statistics$")
        wait_tab_bar(page)
        wait_secondary_pages_closed(page)
        wait_animations_settled(page)
        wait_kpi(page, total, 0)
        run.check("【页面关闭后重新加载】在自定义报表页期间新增的账单（1000），回到统计 Tab 后自动体现", amount_of(page, "stats-kpi-expense") == plain(total), amount_of(page, "stats-kpi-expense"))

        run.check_clean(s.errors, "D 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- E. 只有历史无账本账单


def check_legacy_only(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        now = datetime.now()
        db.seed_legacy_bill(user_id, amount="88.00", category_id=db.category_id_by_name("餐饮美食"), when=datetime(now.year, now.month, 1, 12))
        go_stats(page)
        wait_kpi(page, "88.00", 0)

        comp = page.evaluate(COMPOSITION_JS)
        run.check("【历史无账本账单】没有任何账本：不显示账本胶囊，也没有「按账本」", tid(page, "stats-ledger-pill").count() == 0 and comp["dimension"] is None)
        run.check("【历史无账本账单】账单照常计入 KPI 与构成（餐饮 88.00，100.0%）", expect_rows(comp["rows"], [("餐饮美食", "88.00", 1)]) and comp["rows"][0]["pct"] == "100.0%", str(comp["rows"]))
        run.check_clean(s.errors, "E 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- F. 账本很多 / 名称很长


def check_many_ledgers(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        suffix = unique_name("")
        ids = [db.seed_ledger(user_id, f"账本{i:02d}-{suffix}", sort_order=i) for i in range(14)]
        long_name = "这是一个名字特别特别长的账本用来测试胶囊省略" + suffix
        long_id = db.seed_ledger(user_id, long_name, sort_order=20)
        go_stats(page)

        open_ledger_sheet(page)
        sheet = page.evaluate(SHEET_JS)
        run.check("【账本很多】菜单里有 15 个账本选项", len(sheet["options"]) == 15, str(len(sheet["options"])))
        run.check(
            "【账本很多】选项在面板内滚动（可滚动、内容比面板高），面板不顶出屏幕，「取消」仍完整可见",
            sheet["group"]["overflowY"] == "auto" and sheet["group"]["scrollHeight"] > sheet["group"]["clientHeight"] + 1
            and sheet["group"]["top"] >= 0 and sheet["group"]["bottom"] <= sheet["cancel"]["top"] and sheet["cancel"]["bottom"] <= SCREEN_H,
            str(sheet),
        )
        shot(page, "stats-ledger-sheet-many")
        # 滚到最后一项并选中（长名称账本）
        target = page.locator(f'[data-testid="stats-ledger-option"][data-ledger-id="{long_id.lower()}"]')
        target.scroll_into_view_if_needed()
        run.check("【账本很多】滚动后最后一项露出并可点按", target.is_visible())
        target.click()
        tid(page, "stats-ledger-sheet").wait_for(state="detached")
        page.wait_for_function("(v) => document.querySelector('[data-testid=\"stats-ledger-pill\"]').getAttribute('data-value') === v", arg=long_name)
        pill = page.evaluate(PILL_JS)
        layout = page.evaluate(LAYOUT_JS)
        run.check(
            "【长名称】胶囊文字省略（text-overflow: ellipsis）、宽度不超过一行的 56%、仍贴右边距，一行放得下月份胶囊与账本胶囊",
            pill["ellipsis"] == "ellipsis" and pill["truncated"] and pill["width"] <= pill["rowWidth"] * 0.56 + 1 and approx(pill["right"], SCREEN_W - 16, 1)
            and layout["period"]["right"] <= layout["pill"]["left"] + 0.5 and approx(layout["period"]["top"], layout["pill"]["top"], 1),
            f"{pill} {layout['period']}",
        )
        run.check("【长名称】读屏名称带完整账本名；页面没有横向溢出", pill["aria"] == f"账本：{long_name}，点按切换" and layout["overflowX"] <= 0, f"{pill['aria']} {layout['overflowX']}")
        shot(page, "stats-ledger-long-name")
        open_ledger_sheet(page)
        sheet = page.evaluate(SHEET_JS)
        current = [o["id"] for o in sheet["options"] if o["current"] == "true"]
        run.check(
            "【长名称】再次打开菜单：对勾在长名称账本上（滚动位置不影响），其余账本没有对勾",
            current == [long_id.lower()] and sheet["all"]["current"] is None and len(sheet["options"]) == len(ids) + 1,
            str(current),
        )
        tid(page, "actionsheet-cancel").click()
        tid(page, "stats-ledger-sheet").wait_for(state="detached")
        run.check_clean(s.errors, "F 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- G. 大额


def check_large_amounts(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger = db.seed_ledger(user_id, unique_name("大额账本"))
        now = datetime.now()
        when = datetime(now.year, now.month, 1, 12)
        db.seed_bill(user_id, amount="1234567.89", ledger_id=ledger, category_id=db.category_id_by_name("居住生活"), when=when)
        db.seed_bill(user_id, amount="98765432.10", ledger_id=ledger, bill_type=BILL_TYPE_INCOME, category_id=db.category_id_by_name("工资薪酬", BILL_TYPE_INCOME), when=when)
        go_stats(page)
        wait_kpi(page, "1234567.89", "98765432.10")

        kpi = page.evaluate(KPI_JS)
        cells = kpi["cells"]
        run.check(
            "【大额】金额位数多的栏自动缩小字号（≥ 12 个字符），都在各自栏内、不挤出卡片",
            all(c["long"] for c in cells) and all(c["aLeft"] >= c["left"] - 0.5 and c["aRight"] <= c["right"] + 0.5 for c in cells) and all(c["right"] <= kpi["right"] + 0.5 for c in cells),
            str([(c["label"], c["long"], c["size"], c["aLeft"], c["aRight"], c["left"], c["right"]) for c in cells]),
        )
        run.check("【大额】KPI 金额读屏仍是完整金额", [c["aria"] for c in cells][:2] == ["¥1,234,567.89", "¥98,765,432.10"], str([c["aria"] for c in cells]))
        comp = page.evaluate(COMPOSITION_JS)
        run.check(
            "【大额】圆环中心的总额缩小字号，放得进孔（宽度 ≤ 圆环外径的 68%）且水平居中",
            comp["totalLong"] and comp["totalBox"]["width"] <= comp["donutBox"]["width"] * 0.68
            and approx((comp["totalBox"]["left"] + comp["totalBox"]["right"]) / 2, comp["donutBox"]["cx"], 1.5),
            f"{comp['totalLong']} {comp['totalBox']} {comp['donutBox']}",
        )
        run.check("【大额】没有横向溢出", page.evaluate(LAYOUT_JS)["overflowX"] <= 0)
        shot(page, "stats-large-amounts")
        run.check_clean(s.errors, "G 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- 入口


SECTIONS = {
    "A": check_new_user,
    "B1": check_monthly_data,
    "B2": check_month_navigation,
    "B3": check_ledger_filter,
    "C": check_segments,
    "D": check_refresh_and_secondary,
    "E": check_legacy_only,
    "F": check_many_ledgers,
    "G": check_large_amounts,
}


def main() -> int:
    """不带参数跑全部小节；带小节名（如 `python statistics_page_test.py D F`）只跑指定小节，便于定位问题。"""
    require_server()
    run = TestRun("statistics-page")
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
