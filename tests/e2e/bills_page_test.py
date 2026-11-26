# -*- coding: utf-8 -*-
"""
E2E 测试：账单 Tab · 列表、筛选 Sheet、批量归属账本与搜索（iOS UI 重构 · 08 / 09 / 10 号 ticket）。

只覆盖网页层——Android / iOS 上的真机手感（大标题折叠的跟手度、分段滑块的弹性、左滑手势的阻尼、下拉刷新的回弹、
灵动岛下的实际位置、玻璃模糊、Sheet 的弹簧与下拉收起手势、条件条的横向滑动手感、软键盘与输入法的真实表现）见 ticket 08 / 09 / 10 的人工验收清单。
左滑、下拉刷新的**手势**这里用真实触摸事件验证（CDP touch），手感无法自动化。

  默认视图是「本月」（第 2 轮评审拍板，不算筛选条件）：条件条上时间标签是中性的「本月」、Sheet 里默认选中「本月」、「重置」回到它；
  把「本月」点掉 / 点时间标签的 × = 「不限时间」（时间标签是「时间」）。测列表本身的小节（B / C / D / F / G / I）统一先用 show_all_time 看全部时间，
  不受造数据的日期跨不跨月影响；默认视图本身与「不限时间」的区别在 A / B / E1 / E2 里单独验证（造了一笔 40 天前的旧账单区分两者）。
  A. 新用户（没有账单）：大标题页骨架、汇总卡为 0、空状态「本月还没有账单」+「记一笔」+「查看全部账单」（点了变成真正的「还没有账单」）、
     分段控件与导航栏两个按钮（筛选、多选；没有刷新按钮）、条件条四颗标签、Sheet 里「查看 0 笔账单」照常可点
  B. 有数据的列表：汇总卡（全量口径）、按日分组（今天 / 昨天 / 日期 + 星期 + 当日合计）、行内容与金额颜色、
     分段筛选（全部 / 支出 / 收入，滑块跟随）、大标题折叠、布局（底部让出 Tab 栏、玻璃层 ≤ 3、无横向溢出）、
     Tab 切换后状态保留、无障碍属性、沉浸式（模拟刘海 / 手势条）、点按整行升起编辑 Sheet（16 号起编辑是记一笔 Sheet 的编辑模式）
  C. 左滑「编辑 / 删除」：编辑升起编辑 Sheet；删除先 Alert 确认（取消不删），确认后软删除、Toast、汇总与分组即时更新、删光后回到空状态
  D. 刷新：下拉刷新（真实触摸，唯一的刷新入口——导航栏没有刷新按钮）只重读本页数据，不触发任何云端请求；拉得不够 / 页面不在顶部不刷新
  E. 条件条与筛选 Sheet（09 号）：
     E1 默认视图（本月）与不限时间的区别、结构（条件条四颗标签、Sheet 四组标签、底部「查看 N 笔账单」）、时间预设（「本月」默认选中，再点 = 不限时间）、
        生效 / 放弃（✕ / Esc / 遮罩）、从标签进入时定位到该组、条件条上的 × 单独移除（时间的 × = 不限时间）、重置回「本月」；
     E2 账本（含「未归属账本」）/ 分类（多选，「餐饮美食等 2 项」）/ 支付渠道 / 自定义日期的叠加、条件条排序、角标、
        无结果（Sheet 里「查看 0 笔账单」照常可点、空状态与「清除筛选」）、重置、清除筛选不顺手取消默认的「本月」；
     E3 条件随页面常驻（切 Tab 不丢）、改条件后不在列表里的已选账单自动移出、被别处删除的账本条件自动移除；
     E4 共享账本：筛选里可选，只读账单（别人记的）没有左滑、点按给「只有记录它的人可以修改」的提示且不登出、批量选择里置灰、不能作为归属目标；
        我是只读成员（Viewer）的共享账本里自己记的账单只露「编辑」不给「删除」、多选里置灰（不能批量归属）
  F. 批量选择与「归属账本」：进入 / 退出多选、底部操作栏（悬浮在 Tab 栏之上、非玻璃）、全选 / 清空、逐行勾选；
     归属账本 Sheet（账本必选、只列自己的账本、单账本预选、没有账本时的引导），归属后落库（回到待同步）并清空选择，「未归属账本」筛选下归属掉的账单离开列表
  G. 分页：每页 20 笔，加载更多追加（不重复、不跳回顶部），汇总卡始终是全量，当日合计基于已加载账单
  H. 减弱动态效果：分段滑块与条件标签直接就位（过渡 ≈ 0）
  I. 搜索（10 号）：
     I1 搜索框外观与语义（位置 / 44 高 / 16px 字号 / 灰色填充 / 不占玻璃层 / role=search）、备注 · 分类名称 · 金额匹配（不区分大小写、金额相等、
        没有备注的账单靠分类名搜到、首尾空白忽略）、清除按钮（保持焦点）、提交时机（停顿合并 / 回车立即提交并失焦 / 输入法组词期间不提交 / 组词结束后提交）、
        与分段 · 筛选 Sheet · 条件条叠加（Sheet 底部笔数带关键字）、空状态（只有关键字 / 关键字 + 条件）、切 Tab 保留、删除后保持关键字、只有空白不算关键字、长度上限；
     I2 搜索结果分页（加载更多沿用关键字、汇总是全量）、% 与 _ 按字面匹配

可观察属性（已登记到 README）：bills-page（滚动容器 data-scroll-root、data-title-collapsed、aria-busy）、large-nav / large-nav-title / large-title、
bills-filter-open（aria-haspopup=dialog）/ bills-filter-badge / bills-select-toggle（aria-pressed）、bills-segment（data-value）→ segment（data-value、aria-pressed）、
bills-ptr（data-state）、bills-search（role=search）→ bills-search-input / bills-search-clear / bills-search-status（role=status）、
bills-chips → bills-chip-{time|ledger|category|channel}（data-active、data-dimension；已设置时内含 chip-open / chip-remove）、
filter-sheet(-host)（data-state）/ filter-reset / filter-apply（data-count）/ filter-section-{time|ledger|category|channel} / filter-time-{today|week|month|year|custom} /
filter-start / filter-end / filter-custom-range / filter-ledger-any / filter-ledger-chip / filter-ledger-unassigned / filter-category-chip / filter-channel-chip（后几类带 data-id、data-selected）、
bills-batchbar / bills-select-all / bills-select-clear / bills-select-count（data-count）/ bills-assign、
assign-sheet(-host) / assign-hint / assign-ledger-list / assign-ledger-option（data-id、data-selected）/ assign-confirm / assign-no-ledger、sheet-close / sheet-scrim / sheet-footer / sheet-title、
bills-summary / bills-total-expense / bills-total-income（data-amount）/ bills-count（data-count）、bills-skeleton、bills-empty / bills-first-bill / bills-show-all / bills-clear-filter、
bills-list（data-loaded-count）→ bills-day（data-date）→ bills-day-title / bills-day-date / bills-day-totals（data-expense / data-income）→ bill-swipe（data-bill-id / data-open / data-disabled）→ bill-row
（data-bill-id / data-income / data-selected）→ bill-amount / bill-time、swipe-edit / swipe-delete、bills-load-more、bill-delete-alert（alert-title / alert-message / alert-confirm / alert-cancel）。
"""
import re
import sys
from collections import namedtuple
from datetime import datetime, timedelta
from decimal import Decimal

from playwright.sync_api import expect

from e2e_base import (
    GLASS_LAYERS_JS,
    ADDBILL_SHEET_SELECTOR,
    BILL_TYPE_INCOME,
    MEMBER_ACTIVE,
    PHONE_VIEWPORT,
    ROLE_EDITOR,
    ROLE_VIEWER,
    SYNC_PENDING,
    SYNC_SYNCED,
    LocalDb,
    TIMEOUT_MS,
    TestRun,
    active_tab,
    addbill_sheet,
    close_add_bill_sheet,
    days_ago,
    login,
    open_app,
    open_tab,
    pull_down,
    pull_refresh,
    require_server,
    scroll_root_state,
    scroll_root_to,
    session,
    shot,
    swipe_by,
    tab_bar,
    unique_name,
    wait_animations_settled,
    wait_attr,
    wait_state,
)
from fake_cloud import CloudPortBusy, FakeCloud

SCREEN_W = PHONE_VIEWPORT["width"]
SCREEN_H = PHONE_VIEWPORT["height"]
TOP = 47  # 灵动岛 / 刘海机型状态栏高度
BOTTOM = 34  # 手势条高度
WEEKDAYS = "一二三四五六日"
PAGE_SIZE = 20

RGB_INCOME_TEXT = "rgb(24, 133, 64)"  # --ft-income-text #188540
RGB_LABEL = "rgb(0, 0, 0)"  # --ft-label
RGB_BG = "rgb(242, 242, 247)"  # --ft-bg #F2F2F7

READ_ONLY_HINT = "这笔账单由账本里的其他成员记录，只有记录它的人可以修改或删除"

LAYOUT_JS = """() => {
  const root = document.querySelector('[data-testid="bills-page"]');
  const nav = root.querySelector('[data-testid="large-nav"]');
  const title = root.querySelector('[data-testid="large-title"]');
  const navTitle = root.querySelector('[data-testid="large-nav-title"]');
  const nr = nav.getBoundingClientRect();
  const tr = title.getBoundingClientRect();
  const pr = root.getBoundingClientRect();
  const ncs = getComputedStyle(nav);
  const tcs = getComputedStyle(title);
  const pcs = getComputedStyle(root);
  const nts = getComputedStyle(navTitle);
  const ntr = navTitle.getBoundingClientRect();
  const actions = root.querySelector('.ft-lnav__actions').getBoundingClientRect();
  const box = (id) => {
    const el = root.querySelector(`[data-testid="${id}"]`);
    if (!el) return null;
    const r = el.getBoundingClientRect();
    return { left: r.left, right: r.right, top: r.top, bottom: r.bottom, width: r.width, height: r.height };
  };
  return {
    isScrollRoot: root.hasAttribute('data-scroll-root'),
    collapsed: root.getAttribute('data-title-collapsed'),
    page: { top: pr.top, height: pr.height, overflowY: pcs.overflowY, paddingBottom: parseFloat(pcs.paddingBottom) },
    nav: { top: nr.top, bottom: nr.bottom, height: nr.height, position: ncs.position, bg: ncs.backgroundColor, backdrop: ncs.backdropFilter || ncs.webkitBackdropFilter || 'none' },
    title: { top: tr.top, bottom: tr.bottom, tag: title.tagName, size: tcs.fontSize, weight: tcs.fontWeight, text: title.textContent.trim() },
    navTitle: { opacity: parseFloat(nts.opacity), ariaHidden: navTitle.getAttribute('aria-hidden'), text: navTitle.textContent.trim(), centerX: (ntr.left + ntr.right) / 2, left: ntr.left, right: ntr.right },
    actionsLeft: actions.left,
    refreshEntries: root.querySelectorAll('[data-testid="bills-refresh"], button[aria-label="刷新"], .mdi-refresh').length,
    filter: box('bills-filter-open'), select: box('bills-select-toggle'),
    scrollTop: root.scrollTop,
    max: root.scrollHeight - root.clientHeight,
    overflowX: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    h1Count: root.querySelectorAll('h1').length,
  };
}"""

DAY_GROUPS_JS = """() => [...document.querySelectorAll('[data-testid="bills-page"] [data-testid="bills-day"]')].map((section) => {
  const text = (id) => { const node = section.querySelector(`[data-testid="${id}"]`); return node ? node.textContent.trim() : null; };
  const totals = section.querySelector('[data-testid="bills-day-totals"]');
  return {
    date: section.getAttribute('data-date'),
    title: text('bills-day-title'),
    dateText: text('bills-day-date'),
    totals: text('bills-day-totals'),
    expense: totals && totals.getAttribute('data-expense'),
    income: totals && totals.getAttribute('data-income'),
    titleTag: section.querySelector('[data-testid="bills-day-title"]').tagName,
    rows: [...section.querySelectorAll('[data-testid="bill-row"]')].map((row) => row.getAttribute('data-bill-id')),
  };
})"""

ROWS_JS = """() => [...document.querySelectorAll('[data-testid="bills-page"] [data-testid="bill-row"]')].map((el) => {
  const text = (selector) => { const node = el.querySelector(selector); return node ? node.textContent.trim() : ''; };
  const amount = el.querySelector('[data-testid="bill-amount"]');
  const tile = el.querySelector('.ft-tile');
  const swipe = el.closest('[data-testid="bill-swipe"]');
  const r = el.getBoundingClientRect();
  return {
    id: el.getAttribute('data-bill-id'),
    income: el.getAttribute('data-income'),
    tag: el.tagName,
    role: el.getAttribute('role'),
    checked: el.getAttribute('aria-checked'),
    selected: el.getAttribute('data-selected'),
    disabled: el.disabled === true,
    title: text('.ft-cell__title'),
    subtitle: text('.ft-cell__subtitle'),
    amount: amount ? amount.textContent.trim() : '',
    amountColor: amount ? getComputedStyle(amount).color : '',
    time: text('[data-testid="bill-time"]'),
    aria: el.getAttribute('aria-label'),
    tile: tile ? tile.className : '',
    icon: tile && tile.querySelector('i') ? tile.querySelector('i').className : '',
    height: r.height,
    bottom: r.bottom,
    swipeDisabled: swipe ? swipe.getAttribute('data-disabled') : null,
    swipeActions: swipe ? swipe.querySelectorAll('[data-testid="swipe-edit"], [data-testid="swipe-delete"]').length : 0,
  };
})"""

SEGMENTS_JS = """() => {
  const seg = document.querySelector('[data-testid="bills-segment"]');
  const thumb = seg.querySelector('.ft-seg__thumb');
  const sr = seg.getBoundingClientRect();
  const tr = thumb.getBoundingClientRect();
  return {
    role: seg.getAttribute('role'),
    label: seg.getAttribute('aria-label'),
    value: seg.getAttribute('data-value'),
    height: sr.height,
    thumbCenter: (tr.left + tr.right) / 2,
    thumbWidth: tr.width,
    thumbDuration: getComputedStyle(thumb).transitionDuration,
    items: [...seg.querySelectorAll('[data-testid="segment"]')].map((b) => {
      const r = b.getBoundingClientRect();
      return { text: b.textContent.trim(), tag: b.tagName, value: b.getAttribute('data-value'), pressed: b.getAttribute('aria-pressed'), center: (r.left + r.right) / 2, width: r.width, height: r.height, weight: getComputedStyle(b).fontWeight };
    }),
  };
}"""

COLLAPSED_JS = "() => document.querySelector('[data-testid=\"bills-page\"]').getAttribute('data-title-collapsed') === 'true'"
EXPANDED_JS = "() => document.querySelector('[data-testid=\"bills-page\"]').getAttribute('data-title-collapsed') === 'false'"

PTR_IDLE_JS = """() => {
  const zone = document.querySelector('[data-testid="bills-ptr"]');
  const scroller = document.querySelector('[data-testid="bills-page"]');
  if (!zone || !scroller) return false;
  return zone.getAttribute('data-state') === 'idle' && !scroller.style.getPropertyValue('--ft-ptr-offset');
}"""

SET_SAFE_JS = """([top, bottom]) => {
  const style = document.documentElement.style;
  style.setProperty('--ft-safe-top', top + 'px');
  style.setProperty('--ft-safe-bottom', bottom + 'px');
}"""

CLEAR_SAFE_JS = """() => {
  const style = document.documentElement.style;
  ['--ft-safe-top', '--ft-safe-bottom'].forEach((name) => style.removeProperty(name));
}"""


# ---------------------------------------------------------------------------------------------- 小工具


def approx(actual, expected, tolerance: float = 1.0) -> bool:
    return actual is not None and abs(actual - expected) <= tolerance


def thumb_under(seg: dict, index: int) -> bool:
    """分段滑块与第 index 段居中对齐（滑块比每段略窄，两端留白不同，所以比中心而不是左缘）。"""
    return seg["thumbWidth"] > 0 and approx(seg["thumbCenter"], seg["items"][index]["center"], 1.5)


def plain(value) -> str:
    """金额 → data-amount 的写法：两位小数、无千分位。"""
    return f"{Decimal(str(value)):.2f}"


def money(value) -> str:
    """金额 → 界面文字（¥ 加千分位加两位小数）。"""
    return f"¥{Decimal(str(value)):,.2f}"


def at(days: int, hour: int, minute: int = 0) -> datetime:
    """naive 本地时间：days 天前的 hour:minute。"""
    return days_ago(days, hour).replace(minute=minute)


def without_weekday(day: datetime, now: datetime) -> str:
    return f"{day.month}月{day.day}日" if day.year == now.year else f"{day.year}年{day.month}月{day.day}日"


def day_title(day: datetime, now: datetime) -> str:
    if day.date() == now.date():
        return "今天"
    if day.date() == (now - timedelta(days=1)).date():
        return "昨天"
    return without_weekday(day, now)


def day_date_text(day: datetime, now: datetime) -> str:
    weekday = f"周{WEEKDAYS[day.weekday()]}"
    if day.date() in (now.date(), (now - timedelta(days=1)).date()):
        return f"{without_weekday(day, now)} {weekday}"
    return weekday


def totals_text(expense, income) -> str:
    parts = []
    if expense > 0 or income <= 0:
        parts.append(f"支出 {money(expense)}")
    if income > 0:
        parts.append(f"收入 {money(income)}")
    return " · ".join(parts)


def tid(page, testid: str):
    return page.get_by_test_id(testid)


def bills_page(page):
    return tid(page, "bills-page")


def bill_rows(page):
    return bills_page(page).get_by_test_id("bill-row")


def amount_of(page, testid: str):
    return tid(page, testid).get_attribute("data-amount")


def count_of(page) -> int:
    return int(tid(page, "bills-count").get_attribute("data-count"))


def toast(page, text: str):
    return page.locator(".m-enqueued-snackbars .m-snack__wrapper").filter(has_text=text).first


def wait_bills_loaded(page) -> None:
    """汇总卡里的「支出」金额出现 = 骨架屏已被真实数据替换；再等页面进入 / 切换的过渡结束，否则量到的是动画中间帧。"""
    tid(page, "bills-total-expense").wait_for(state="visible")
    wait_animations_settled(page)


def wait_summary(page, expense, income, count: int) -> None:
    page.wait_for_function(
        """([e, i, c]) => {
          const get = (id, attr) => { const el = document.querySelector(`[data-testid="${id}"]`); return el ? el.getAttribute(attr) : null; };
          return get('bills-total-expense', 'data-amount') === e && get('bills-total-income', 'data-amount') === i && get('bills-count', 'data-count') === String(c);
        }""",
        arg=[plain(expense), plain(income), count],
    )


def wait_rows(page, expected: int) -> None:
    page.wait_for_function(
        "(n) => document.querySelectorAll('[data-testid=\"bills-page\"] [data-testid=\"bill-row\"]').length === n",
        arg=expected,
    )


def wait_ptr_idle(page) -> None:
    page.wait_for_function(PTR_IDLE_JS)


def swipe_of(page, bill_id: str):
    return page.locator(f'[data-testid="bills-page"] [data-testid="bill-swipe"][data-bill-id="{bill_id.lower()}"]')


def swipe_open(page, bill_id: str):
    """左滑一行（真实触摸）并等它展开；已经展开就不再滑。"""
    swipe = swipe_of(page, bill_id)
    if swipe.get_attribute("data-open") != "true":
        swipe_by(page, swipe, -200, 0)
    page.wait_for_function(
        "(id) => { const el = document.querySelector(`[data-testid=\"bill-swipe\"][data-bill-id=\"${id}\"]`); return !!el && el.dataset.open === 'true'; }",
        arg=bill_id.lower(),
    )
    return swipe


def delete_via_swipe(page, bill_id: str) -> None:
    """左滑 → 删除 → 在 Alert 里确认；等 Alert 退出动画结束（180 ms）、遮罩卸载，之后的触摸才不会被它拦住。"""
    swipe = swipe_open(page, bill_id)
    swipe.get_by_test_id("swipe-delete").click()
    tid(page, "bill-delete-alert").wait_for(state="visible")
    tid(page, "alert-confirm").click()
    tid(page, "alert-host").wait_for(state="detached")


def go_bills(page) -> None:
    """点 Tab 栏进入账单 Tab 并等数据加载完。账单 Tab 默认看「本月」（不算筛选条件），要看全部时间用 show_all_time。"""
    open_tab(page, "账单")
    wait_bills_loaded(page)


def show_all_time(page, summary=None) -> None:
    """把账单 Tab 从默认的「本月」改成「不限时间」：筛选 Sheet 里把默认选中的「本月」点掉（= 取消时间范围），点「查看」。
    测列表本身（分组 / 左滑 / 分页 / 搜索……）的小节用它：造的账单日期是「几天前」，月初跑时会跨到上个月，不能让测试结果取决于今天是几号。
    summary：(支出, 收入, 笔数)，给了就等列表与汇总落到这三个数。"""
    open_filter_sheet(page, via="time")
    toggle_chip(page, "filter-time-month", selected=False)
    apply_filter(page)
    page.wait_for_function("() => document.querySelector('[data-testid=\"bills-chip-time\"]').textContent.trim() === '时间'")
    if summary is not None:
        wait_summary(page, *summary)
        wait_rows(page, min(summary[2], PAGE_SIZE))


CHIPS_JS = """() => [...document.querySelectorAll('[data-testid="bills-page"] [data-testid="bills-chips"] > [data-dimension]')].map((el) => {
  const open = el.querySelector('[data-testid="chip-open"]');
  const remove = el.querySelector('[data-testid="chip-remove"]');
  const main = open || el;
  const mr = main.getBoundingClientRect();
  const rr = remove ? remove.getBoundingClientRect() : null;
  const er = el.getBoundingClientRect();
  return {
    dimension: el.dataset.dimension,
    active: el.dataset.active,
    text: el.querySelector('.ft-chip__label').textContent.trim(),
    openLabel: main.getAttribute('aria-label'),
    removeLabel: remove ? remove.getAttribute('aria-label') : null,
    openHeight: mr.height,
    removeHeight: rr ? rr.height : null,
    removeWidth: rr ? rr.width : null,
    left: er.left,
    right: er.right,
  };
})"""

ORDER_JS = """() => {
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  const rect = (id) => q(id).getBoundingClientRect();
  const row = q('bills-chips');
  const rs = getComputedStyle(row);
  return {
    segBottom: rect('bills-segment').bottom,
    chipsTop: rect('bills-chips').top,
    chipsBottom: rect('bills-chips').bottom,
    chipsHeight: rect('bills-chips').height,
    sumTop: rect('bills-summary').top,
    rowRole: row.getAttribute('role'),
    rowLabel: row.getAttribute('aria-label'),
    rowOverflowX: rs.overflowX,
    rowWrap: rs.flexWrap,
    overflowX: document.documentElement.scrollWidth - document.documentElement.clientWidth,
  };
}"""

SHEET_STATE_JS = """() => {
  const sheet = document.querySelector('[data-testid="filter-sheet"]');
  const labelOf = (el) => el.querySelector('.ft-chip__label').textContent.trim();
  const section = (name) => sheet.querySelector(`[data-testid="filter-section-${name}"]`);
  const chips = (name) => [...section(name).querySelectorAll('.ft-chip')];
  const selected = (name) => chips(name).filter((el) => el.dataset.selected === 'true').map(labelOf);
  const apply = sheet.querySelector('[data-testid="filter-apply"]');
  const reset = sheet.querySelector('[data-testid="filter-reset"]');
  const start = sheet.querySelector('[data-testid="filter-start"]');
  const end = sheet.querySelector('[data-testid="filter-end"]');
  return {
    time: selected('time'),
    ledger: selected('ledger'),
    category: selected('category'),
    channel: selected('channel'),
    timeLabels: chips('time').map(labelOf),
    ledgerLabels: chips('ledger').map(labelOf),
    categoryLabels: chips('category').map(labelOf),
    channelLabels: chips('channel').map(labelOf),
    titles: [...sheet.querySelectorAll('.ft-filtersec__title')].map((el) => el.textContent.trim()),
    applyText: apply.textContent.trim(),
    applyCount: apply.dataset.count ?? null,
    applyDisabled: apply.disabled,
    resetDisabled: reset.disabled,
    customVisible: !!sheet.querySelector('[data-testid="filter-custom-range"]'),
    start: start ? start.value : null,
    end: end ? end.value : null,
  };
}"""

SHEET_GEOM_JS = """() => {
  const sheet = document.querySelector('[data-testid="filter-sheet"]');
  const rect = (el) => { const b = el.getBoundingClientRect(); return { top: b.top, bottom: b.bottom, left: b.left, right: b.right, height: b.height }; };
  const body = sheet.querySelector('.ft-sheet__body');
  const footer = sheet.querySelector('[data-testid="sheet-footer"]');
  const apply = sheet.querySelector('[data-testid="filter-apply"]');
  const title = sheet.querySelector('[data-testid="sheet-title"]');
  const section = (name) => rect(sheet.querySelector(`[data-testid="filter-section-${name}"]`));
  return {
    sheet: rect(sheet),
    body: rect(body),
    footer: footer ? rect(footer) : null,
    apply: rect(apply),
    sections: { time: section('time'), ledger: section('ledger'), category: section('category'), channel: section('channel') },
    role: sheet.getAttribute('role'),
    modal: sheet.getAttribute('aria-modal'),
    labelledby: sheet.getAttribute('aria-labelledby'),
    title: title.textContent.trim(),
    titleId: title.id,
    live: apply.parentElement.getAttribute('aria-live'),
    bodyScrollTop: body.scrollTop,
    bodyMax: body.scrollHeight - body.clientHeight,
  };
}"""

COVER_JS = """([x, y, testid]) => {
  const el = document.elementFromPoint(x, y);
  return !!el && !!el.closest(`[data-testid="${testid}"]`);
}"""

#: 筛选 Sheet 里分类 / 支付渠道标签前的图标：字体里没有这个字形时 ::before 的 content 是 none，图标就是一块空白
SHEET_ICONS_JS = """() => {
  const reference = document.querySelector('[data-testid="bills-chips"] i.mdi-chevron-down');
  const glyph = (el) => getComputedStyle(el, '::before').content;
  return {
    fontLoaded: !!reference && glyph(reference) !== 'none' && glyph(reference) !== 'normal',
    blank: [...document.querySelectorAll('[data-testid="filter-sheet"] .ft-chips i.mdi')].filter((el) => ['none', 'normal'].includes(glyph(el))).map((el) => el.className),
  };
}"""

BATCHBAR_JS = """() => {
  const bar = document.querySelector('[data-testid="bills-batchbar"]');
  const page = document.querySelector('[data-testid="bills-page"]');
  const cs = getComputedStyle(bar);
  const b = bar.getBoundingClientRect();
  const size = (id) => { const r = bar.querySelector(`[data-testid="${id}"]`).getBoundingClientRect(); return { width: r.width, height: r.height }; };
  return {
    role: bar.getAttribute('role'),
    label: bar.getAttribute('aria-label'),
    top: b.top, bottom: b.bottom, left: b.left, right: b.right, height: b.height,
    position: cs.position,
    backdrop: cs.backdropFilter && cs.backdropFilter !== 'none' ? cs.backdropFilter : (cs.webkitBackdropFilter || 'none'),
    buttons: ['bills-select-all', 'bills-select-clear', 'bills-assign'].map(size),
    selecting: page.classList.contains('ft-bills--selecting'),
    pagePadding: parseFloat(getComputedStyle(page).paddingBottom),
    countLive: bar.querySelector('[data-testid="bills-select-count"]').getAttribute('aria-live'),
  };
}"""

PRESET_LABELS = [("today", "今天"), ("week", "本周"), ("month", "本月"), ("year", "本年")]


def filter_sheet(page):
    return tid(page, "filter-sheet")


def open_filter_sheet(page, via: str = "nav"):
    """打开筛选 Sheet，并等入场动画结束。via='nav' 点导航栏的筛选按钮；
    其余取值（time / ledger / category / channel）点条件条上对应维度的标签——已设置条件的标签点它的文字部分。"""
    if via == "nav":
        tid(page, "bills-filter-open").click()
    else:
        chip = tid(page, f"bills-chip-{via}")
        (chip.get_by_test_id("chip-open") if chip.get_attribute("data-active") == "true" else chip).click()
    filter_sheet(page).wait_for(state="visible")
    wait_animations_settled(page)
    return filter_sheet(page)


def wait_sheet_gone(page, testid: str = "filter-sheet") -> None:
    """等 Sheet 退出动画结束、从 DOM 卸载（260 ms）。动画期间遮罩还在，点按会被它拦住。"""
    tid(page, testid).wait_for(state="detached")
    wait_animations_settled(page)


def close_filter_sheet(page) -> None:
    """点右上角 ✕ 关闭筛选 Sheet（放弃草稿）。"""
    filter_sheet(page).get_by_test_id("sheet-close").click()
    wait_sheet_gone(page)


def sheet_chip(page, testid: str, text=None):
    """筛选 Sheet 里的标签；同一个 testid 有多颗时（账本 / 分类 / 渠道）用文字挑一颗。"""
    chips = filter_sheet(page).get_by_test_id(testid)
    return chips.filter(has_text=text).first if text else chips.first


def toggle_chip(page, testid: str, text=None, *, selected: bool = True):
    """点筛选 Sheet 里的标签，并等它的选中状态落成 selected。
    点按之后要等 Blazor 事件往返、DOM 更新——立刻读状态读到的是点之前的（笔数没变时 wait_apply_count 也帮不上忙）。"""
    chip = sheet_chip(page, testid, text)
    chip.click()
    expect(chip).to_have_attribute("data-selected", "true" if selected else "false", timeout=TIMEOUT_MS)
    return chip


def wait_apply_count(page, count: int) -> None:
    """等底部主按钮的笔数（data-count）变成 count：每次改草稿都会重新估算，估算在途时旧数字还在。"""
    page.wait_for_function(
        "(n) => { const b = document.querySelector('[data-testid=\"filter-apply\"]'); return !!b && b.dataset.count === String(n); }",
        arg=count,
    )


def apply_filter(page) -> None:
    """点底部「查看 N 笔账单」：Sheet 先收起，页面再重新查询。"""
    tid(page, "filter-apply").click()
    wait_sheet_gone(page)


def apply_and_wait(page, expense, income, count: int) -> None:
    """点「查看」并等列表与汇总落到期望值（汇总卡与行数一起核对）。"""
    apply_filter(page)
    wait_summary(page, expense, income, count)
    wait_rows(page, min(count, PAGE_SIZE))


def remove_chip(page, dimension: str) -> None:
    tid(page, f"bills-chip-{dimension}").get_by_test_id("chip-remove").click()


def chips_state(page) -> list:
    return page.evaluate(CHIPS_JS)


def chip_order(page) -> list:
    return [c["dimension"] for c in chips_state(page)]


def sheet_state(page) -> dict:
    return page.evaluate(SHEET_STATE_JS)


def badge_text(page):
    badge = tid(page, "bills-filter-badge")
    return badge.inner_text() if badge.count() else None


def list_is(page, expense, income, count: int) -> bool:
    """汇总卡（支出 / 收入 / 笔数）与已加载的行数都等于期望值。"""
    return (
        amount_of(page, "bills-total-expense") == plain(expense)
        and amount_of(page, "bills-total-income") == plain(income)
        and count_of(page) == count
        and len(page.evaluate(ROWS_JS)) == min(count, PAGE_SIZE)
    )


def row_ids(page) -> list:
    return [r["id"] for r in page.evaluate(ROWS_JS)]


def row_of(page, bill_id: str):
    return page.locator(f'[data-testid="bills-page"] [data-testid="bill-row"][data-bill-id="{bill_id.lower()}"]')


def wait_selected(page, count: int) -> None:
    """等多选操作栏里的已选笔数（data-count）变成 count。"""
    page.wait_for_function(
        "(n) => { const el = document.querySelector('[data-testid=\"bills-select-count\"]'); return !!el && el.dataset.count === String(n); }",
        arg=count,
    )


def segment_button(page, text: str):
    return tid(page, "bills-segment").get_by_test_id("segment").filter(has_text=text)


# ---------------------------------------------------------------------------------------------- 造数据

#: key, 几天前, 时, 分, 金额, 是否收入, 备注, 分类名, 是否在账本里
BILL_SPECS = [
    ("lunch", 0, 12, 8, "35", False, "午餐", "餐饮美食", True),
    ("coffee", 0, 9, 32, "28", False, "咖啡", "餐饮美食", True),
    ("metro", 0, 8, 41, "6", False, "地铁通勤", "交通出行", True),
    ("jd", 1, 20, 15, "299", False, "京东", "购物消费", True),
    ("salary", 1, 10, 0, "12000", True, "工资", "工资薪酬", True),
    ("dinner", 5, 19, 30, "18.50", False, "周末聚餐", "餐饮美食", True),
    ("legacy", 6, 14, 0, "18", False, "老账单", "其他支出", False),
]


Spec = namedtuple("Spec", "key days hour minute amount income note category in_ledger")

#: 筛选测试在 BILL_SPECS 之外多造两笔「支付宝」账单（都在账本里、都是支出），再造一笔 40 天前的旧账单：
#: 40 天前一定不在当月（一个月最多 31 天），默认的「本月」看不到它、「不限时间」才看得到——用来区分这两种视图
FILTER_EXTRA = [
    ("ali_lunch", 0, 10, 15, "50", False, "支付宝午餐", "餐饮美食", True),
    ("ali_shop", 3, 10, 15, "70", False, "支付宝购物", "购物消费", True),
    ("old_lunch", 40, 12, 20, "30", False, "上月午餐", "餐饮美食", True),
]
FILTER_SPECS = BILL_SPECS + FILTER_EXTRA
ALIPAY_KEYS = {"ali_lunch", "ali_shop"}


def seed_bills(db: LocalDb, user_id: str, ledger_id: str, specs=BILL_SPECS, channels=None) -> dict:
    """按清单造账单；channels：key → 支付渠道 Id，没给的用默认渠道。"""
    channels = channels or {}
    bills = {}
    for key, days, hour, minute, amount, income, note, category, in_ledger in specs:
        kind = BILL_TYPE_INCOME if income else 0
        category_id = db.category_id_by_name(category, kind)
        bills[key] = db.seed_bill(
            user_id,
            amount=amount,
            ledger_id=ledger_id if in_ledger else None,
            bill_type=kind,
            note=note,
            when=at(days, hour, minute),
            category_id=category_id,
            channel_id=channels.get(key),
        )
    return bills


def alipay_channels(db: LocalDb) -> dict:
    """FILTER_EXTRA 里两笔账单用的支付渠道（预设「支付宝」）。"""
    channel = db.channel_id_by_name("支付宝")
    return {key: channel for key in ALIPAY_KEYS}


def in_preset(when: datetime, preset: str, now: datetime) -> bool:
    """账单日期是否落在时间预设里（应用时区的日历日；周一为一周之始）。"""
    day, today = when.date(), now.date()
    if preset == "today":
        return day == today
    if preset == "week":
        monday = today - timedelta(days=today.weekday())
        return monday <= day <= monday + timedelta(days=6)
    if preset == "month":
        return (day.year, day.month) == (today.year, today.month)
    if preset == "year":
        return day.year == today.year
    raise ValueError(preset)


def pick(now: datetime, *, preset=None, ledger=None, category=None, alipay=False, income=None, start=None, end=None, all_time=False) -> list:
    """从 FILTER_SPECS 里挑出符合条件的账单——语义与筛选 Sheet 一致（日期含当天）。
    时间：什么都不给 = 账单 Tab 的默认视图「本月」；preset 给预设（today / week / month / year）；start / end 给自定义范围；
    all_time=True = 不限时间（在 Sheet 里取消了时间范围）。
    ledger：True = 在账本里，False = 历史无账本（未归属账本），None = 不限。
    category：分类名，或一组分类名（多选，满足任一个即可）。"""
    if isinstance(category, str):
        category = {category}
    if preset is None and start is None and end is None and not all_time:
        preset = "month"
    chosen = []
    for raw in FILTER_SPECS:
        spec = Spec(*raw)
        when = at(spec.days, spec.hour, spec.minute)
        if preset and not in_preset(when, preset, now):
            continue
        if ledger is not None and spec.in_ledger != ledger:
            continue
        if category and spec.category not in category:
            continue
        if alipay and spec.key not in ALIPAY_KEYS:
            continue
        if income is not None and spec.income != income:
            continue
        if start and when.date() < start:
            continue
        if end and when.date() > end:
            continue
        chosen.append(spec)
    return chosen


def sums(specs: list) -> tuple:
    """(支出, 收入, 笔数)：与汇总卡的三个数一一对应。"""
    expense = sum((Decimal(s.amount) for s in specs if not s.income), Decimal(0))
    income = sum((Decimal(s.amount) for s in specs if s.income), Decimal(0))
    return expense, income, len(specs)


def expected_for(now: datetime, specs=BILL_SPECS) -> dict:
    """由造数据的清单推出期望：汇总、按日分组（日期晚的在前、同日时间晚的在前）。"""
    items = []
    for key, days, hour, minute, amount, income, note, category, in_ledger in specs:
        items.append({"key": key, "when": at(days, hour, minute), "amount": Decimal(amount), "income": income, "note": note, "category": category, "in_ledger": in_ledger})

    groups = []
    for day in sorted({i["when"].date() for i in items}, reverse=True):
        members = sorted((i for i in items if i["when"].date() == day), key=lambda i: i["when"], reverse=True)
        moment = datetime.combine(day, datetime.min.time())
        expense = sum((m["amount"] for m in members if not m["income"]), Decimal(0))
        income = sum((m["amount"] for m in members if m["income"]), Decimal(0))
        groups.append(
            {
                "date": day.strftime("%Y-%m-%d"),
                "title": day_title(moment, now),
                "dateText": day_date_text(moment, now),
                "totals": totals_text(expense, income),
                "expense": expense,
                "income": income,
                "keys": [m["key"] for m in members],
            }
        )

    return {
        "items": {i["key"]: i for i in items},
        "groups": groups,
        "expense": sum((i["amount"] for i in items if not i["income"]), Decimal(0)),
        "income": sum((i["amount"] for i in items if i["income"]), Decimal(0)),
        "count": len(items),
    }


# ---------------------------------------------------------------------------------------------- A. 新用户


def check_new_user(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        login(page)
        go_bills(page)

        layout = page.evaluate(LAYOUT_JS)
        run.check("【新用户】账单 Tab 地址是 /bills，Tab 栏高亮「账单」", page.evaluate("() => location.pathname") == "/bills" and active_tab(page) == "bills")
        run.check("【大标题页】根节点是滚动容器（data-scroll-root），占满整屏高度、纵向可滚", layout["isScrollRoot"] and layout["page"]["overflowY"] == "auto" and approx(layout["page"]["height"], SCREEN_H, 1), str(layout["page"]))
        run.check("【大标题页】大标题「账单」是页面里唯一的一级标题，34px / 800", layout["h1Count"] == 1 and layout["title"]["tag"] == "H1" and layout["title"]["text"] == "账单" and layout["title"]["size"] == "34px" and layout["title"]["weight"] == "800", str(layout["title"]))
        run.check("【大标题页】导航栏吸顶（sticky）、高 44、贴着屏幕顶端", layout["nav"]["position"] == "sticky" and approx(layout["nav"]["height"], 44) and approx(layout["nav"]["top"], 0), str(layout["nav"]))
        run.check("【大标题页】大标题在导航栏之下；初始未折叠，导航栏透明、小标题不可见", layout["collapsed"] != "true" and layout["title"]["top"] >= layout["nav"]["bottom"] - 1 and layout["nav"]["bg"] == "rgba(242, 242, 247, 0)" and layout["navTitle"]["opacity"] == 0, f"{layout['collapsed']} {layout['nav']} {layout['navTitle']}")
        run.check("【大标题页】折叠小标题只是装饰（aria-hidden），读屏读大标题", layout["navTitle"]["ariaHidden"] == "true" and layout["navTitle"]["text"] == "账单")
        run.check("【导航栏】筛选 / 多选两个按钮都在右侧，点击区域 ≥ 44 × 44", all(layout[k] and layout[k]["width"] >= 44 and layout[k]["height"] >= 44 and layout[k]["right"] <= SCREEN_W for k in ("filter", "select")), str([layout[k] for k in ("filter", "select")]))
        run.check("【导航栏】没有按钮形式的刷新入口（刷新只靠下拉）：页面里没有 bills-refresh / 「刷新」按钮 / 刷新图标", layout["refreshEntries"] == 0, str(layout["refreshEntries"]))
        run.check("没有横向溢出", layout["overflowX"] <= 0, str(layout["overflowX"]))

        run.check("页面加载完成后不再标记忙碌（aria-busy 已移除），没有骨架屏", bills_page(page).get_attribute("aria-busy") is None and tid(page, "bills-skeleton").count() == 0)
        run.check("【汇总卡】新用户：支出 / 收入 0.00，笔数 0", [amount_of(page, "bills-total-expense"), amount_of(page, "bills-total-income"), page.get_by_test_id("bills-count").get_attribute("data-count")] == ["0.00", "0.00", "0"])
        run.check("【汇总卡】金额整体读屏（role=img + 完整金额）", tid(page, "bills-total-expense").get_attribute("role") == "img" and tid(page, "bills-total-expense").get_attribute("aria-label") == "¥0.00")
        # 默认视图是「本月」：新用户一笔账单都没有，页面并不知道更早的月份里有没有，所以说「本月还没有账单」并给「查看全部账单」
        run.check(
            "【空状态】新用户（默认看「本月」）：「本月还没有账单」+ 品牌色「记一笔」+ 「查看全部账单」，没有列表 / 加载更多 / 「清除筛选」",
            tid(page, "bills-empty").is_visible() and "本月还没有账单" in tid(page, "bills-empty").inner_text()
            and tid(page, "bills-first-bill").is_visible() and tid(page, "bills-show-all").is_visible() and "查看全部账单" in tid(page, "bills-show-all").inner_text()
            and tid(page, "bills-list").count() == 0 and tid(page, "bills-load-more").count() == 0 and tid(page, "bills-clear-filter").count() == 0,
            tid(page, "bills-empty").inner_text(),
        )
        chips = chips_state(page)
        run.check(
            "条件条四颗标签都没设置条件：时间标签是中性的「本月」（默认视图不算条件，读屏名称「时间：本月，点按调整」），其余是维度名；"
            "筛选 Sheet 与批量操作栏默认不在页面里，导航栏筛选按钮无角标",
            chip_order(page) == ["time", "ledger", "category", "channel"] and all(c["active"] == "false" for c in chips)
            and [c["text"] for c in chips] == ["本月", "账本", "分类", "支付渠道"]
            and [c["openLabel"] for c in chips] == ["时间：本月，点按调整", "账本，点按设置", "分类，点按设置", "支付渠道，点按设置"]
            and filter_sheet(page).count() == 0 and tid(page, "bills-batchbar").count() == 0 and badge_text(page) is None
            and tid(page, "bills-filter-open").get_attribute("aria-haspopup") == "dialog",
            str(chips),
        )
        run.check("导航栏按钮读屏名称：筛选 / 多选", [tid(page, t).get_attribute("aria-label") for t in ("bills-filter-open", "bills-select-toggle")] == ["筛选", "多选"])
        # 没有账本也没有账单的新用户：Sheet 里账本只有「全部」与「未归属账本」；默认选中「本月」；笔数 0，「查看」照常可点（点了得到空状态与清除入口）
        open_filter_sheet(page)
        wait_apply_count(page, 0)
        st = sheet_state(page)
        run.check(
            "新用户打开筛选 Sheet：账本只有「全部」「未归属账本」（还没有自己的账本）；时间默认选中「本月」，「重置」不可点（已经是默认视图）",
            st["ledgerLabels"] == ["全部", "未归属账本"] and st["time"] == ["本月"] and st["resetDisabled"],
            str(st),
        )
        run.check(
            "新用户 Sheet 的「查看」按钮笔数 0 也照常写「查看 0 笔账单」且可点（不再置灰）",
            not st["applyDisabled"] and st["applyText"] == "查看 0 笔账单" and st["applyCount"] == "0",
            str(st),
        )
        close_filter_sheet(page)
        shot(page, "bills-new-user")

        # 「查看全部账单」= 取消时间范围：时间标签变回中性的「时间」，仍然一笔都没有——这回才是真的「还没有账单」，不再给「查看全部账单」
        tid(page, "bills-show-all").click()
        page.wait_for_function("() => document.querySelector('[data-testid=\"bills-chip-time\"]').textContent.trim() === '时间'")
        tid(page, "bills-empty").wait_for(state="visible")
        run.check(
            "点「查看全部账单」：时间范围取消（时间标签变成「时间」、不是条件、无角标），仍是空的 → 「还没有账单」，只剩「记一笔」",
            "还没有账单" in tid(page, "bills-empty").inner_text() and "本月" not in tid(page, "bills-empty").inner_text()
            and tid(page, "bills-first-bill").is_visible() and tid(page, "bills-show-all").count() == 0 and tid(page, "bills-clear-filter").count() == 0
            and badge_text(page) is None and chips_state(page)[0]["text"] == "时间" and chips_state(page)[0]["openLabel"] == "时间，点按设置",
            f"{tid(page, 'bills-empty').inner_text()} / {chips_state(page)[0]}",
        )
        open_filter_sheet(page)
        wait_apply_count(page, 0)
        st = sheet_state(page)
        run.check(
            "取消时间范围后重新打开 Sheet：时间一项都没选，「重置」可点（不限时间不是默认视图），笔数 0 的「查看」仍可点",
            st["time"] == [] and not st["resetDisabled"] and not st["applyDisabled"] and st["applyText"] == "查看 0 笔账单",
            str(st),
        )
        tid(page, "filter-reset").click()
        page.wait_for_function("() => document.querySelector('[data-testid=\"filter-sheet\"] [data-testid=\"filter-reset\"]').disabled")
        st = sheet_state(page)
        run.check("点「重置」：回到默认的「本月」（不是不限时间），「重置」又不可点", st["time"] == ["本月"] and st["resetDisabled"], str(st))
        apply_filter(page)
        page.wait_for_function("() => document.querySelector('[data-testid=\"bills-chip-time\"]').textContent.trim() === '本月'")
        tid(page, "bills-empty").wait_for(state="visible")
        run.check("重置并「查看」后回到默认视图：「本月还没有账单」", "本月还没有账单" in tid(page, "bills-empty").inner_text() and tid(page, "bills-show-all").is_visible())

        tid(page, "bills-first-bill").click()
        tid(page, "addbill-sheet").wait_for(state="visible")
        run.check("空状态「记一笔」升起记一笔 Sheet（15 号起不再是路由页，地址不变）", page.evaluate("() => location.pathname") == "/bills")

        run.check_clean(s.errors, "A 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- B. 有数据的列表


def check_summary_and_groups(run: TestRun, page, expected: dict, bills: dict) -> None:
    run.check(
        "【汇总卡】支出 / 收入 / 笔数 = 全部账单之和（含历史无账本的账单）",
        [amount_of(page, "bills-total-expense"), amount_of(page, "bills-total-income"), page.get_by_test_id("bills-count").get_attribute("data-count")]
        == [plain(expected["expense"]), plain(expected["income"]), str(expected["count"])],
        f"{amount_of(page, 'bills-total-expense')} / {amount_of(page, 'bills-total-income')} / {count_of(page)}",
    )
    run.check("【汇总卡】金额整体读屏文案带千分位", tid(page, "bills-total-income").get_attribute("aria-label") == money(expected["income"]), tid(page, "bills-total-income").get_attribute("aria-label"))
    run.check("【汇总卡】支出用加深的红、收入用加深的绿（文字对比度 ≥ 4.5）", page.evaluate("() => getComputedStyle(document.querySelector('[data-testid=\"bills-total-expense\"]')).color") == "rgb(211, 34, 26)" and page.evaluate("() => getComputedStyle(document.querySelector('[data-testid=\"bills-total-income\"]')).color") == RGB_INCOME_TEXT)

    groups = page.evaluate(DAY_GROUPS_JS)
    want = expected["groups"]
    run.check("【分组】按日分组：日期晚的在前（今天 / 昨天 / 5 天前 / 6 天前）", [g["date"] for g in groups] == [w["date"] for w in want], str([g["date"] for g in groups]))
    run.check("【分组】组头标题：今天 / 昨天 / 「M月D日」（二级标题 h2）", [g["title"] for g in groups] == [w["title"] for w in want] and all(g["titleTag"] == "H2" for g in groups), str([g["title"] for g in groups]))
    run.check("【分组】组头日期：今天 / 昨天带日期与星期，更早的只补星期", [g["dateText"] for g in groups] == [w["dateText"] for w in want], str([g["dateText"] for g in groups]))
    run.check("【分组】当日合计：只列有金额的一边，两边都有用「 · 」连接", [g["totals"] for g in groups] == [w["totals"] for w in want], str([g["totals"] for g in groups]))
    run.check("【分组】当日合计可观察（data-expense / data-income）", [(g["expense"], g["income"]) for g in groups] == [(plain(w["expense"]), plain(w["income"])) for w in want], str([(g["expense"], g["income"]) for g in groups]))
    run.check("【分组】组内按交易时间倒序，每个账单只出现一次", [g["rows"] for g in groups] == [[bills[k].lower() for k in w["keys"]] for w in want], str([g["rows"] for g in groups]))


def check_rows(run: TestRun, page, expected: dict, bills: dict, ledger_name: str) -> None:
    rows = {r["id"]: r for r in page.evaluate(ROWS_JS)}
    items = expected["items"]

    def row(key: str) -> dict:
        return rows[bills[key].lower()]

    lunch, salary, legacy, dinner = row("lunch"), row("salary"), row("legacy"), row("dinner")
    titles = {k: row(k)["title"] for k in items}
    run.check("行标题是「分类 · 备注」", all(titles[k] == f"{items[k]['category']} · {items[k]['note']}" for k in items), str(titles))
    run.check("行副标题是「支付渠道 · 账本」", lunch["subtitle"].endswith(f"· {ledger_name}") and " · " in lunch["subtitle"], lunch["subtitle"])
    run.check("历史无账本的账单副标题标注「未归属账本」", legacy["subtitle"].endswith("· 未归属账本"), legacy["subtitle"])
    run.check("支出金额带负号、千分位、两位小数", [lunch["amount"], dinner["amount"], legacy["amount"]] == ["-¥35.00", "-¥18.50", "-¥18.00"], str([lunch["amount"], dinner["amount"], legacy["amount"]]))
    run.check("收入金额带正号、千分位", salary["amount"] == "+¥12,000.00" and salary["income"] == "true" and lunch["income"] == "false", salary["amount"])
    run.check("金额颜色：支出是常规文字色（不用红色），收入是加深的绿色", lunch["amountColor"] == RGB_LABEL and salary["amountColor"] == RGB_INCOME_TEXT, f"{lunch['amountColor']} / {salary['amountColor']}")
    run.check("每行只显示时分（日期在组头里）", all(row(k)["time"] == f"{items[k]['when'].hour:02d}:{items[k]['when'].minute:02d}" for k in items), str({k: row(k)["time"] for k in items}))
    run.check("行读屏名称把标题 / 副标题 / 收支 / 金额 / 时间连成一句话", salary["aria"] == f"{salary['title']}，{salary['subtitle']}，收入 ¥12,000.00，{salary['time']}", str(salary["aria"]))
    run.check("整行是按钮，点击区域高 ≥ 56", all(r["tag"] == "BUTTON" and r["height"] >= 56 for r in rows.values()), str([(r["tag"], r["height"]) for r in rows.values()]))
    run.check("每行都有柔和样式的分类图标块", all("ft-tile--soft" in r["tile"] and re.search(r"\bmdi-[a-z0-9-]+", r["icon"]) for r in rows.values()), str([r["tile"] for r in rows.values()]))
    run.check("自己的账单都能左滑（编辑 / 删除两个操作），非多选模式下没有禁用", all(r["swipeDisabled"] is None and r["swipeActions"] == 2 for r in rows.values()), str([(r["swipeDisabled"], r["swipeActions"]) for r in rows.values()]))
    run.check("非多选模式下行不是复选框（没有 role / data-selected）", all(r["role"] is None and r["selected"] is None for r in rows.values()))


def check_segments(run: TestRun, page, expected: dict, bills: dict) -> None:
    seg = page.evaluate(SEGMENTS_JS)
    run.check("【分段】三段「全部 / 支出 / 收入」，默认选中「全部」", [i["text"] for i in seg["items"]] == ["全部", "支出", "收入"] and seg["value"] == "All" and [i["pressed"] for i in seg["items"]] == ["true", "false", "false"], str(seg["items"]))
    run.check("【分段】语义：role=group + 读屏名称「账单类型」，每段是 button[aria-pressed]", seg["role"] == "group" and seg["label"] == "账单类型" and all(i["tag"] == "BUTTON" for i in seg["items"]))
    run.check("【分段】轨道高 44，每段点击区域也是 44 高、宽 ≥ 44", approx(seg["height"], 44) and all(i["height"] >= 44 and i["width"] >= 44 for i in seg["items"]), str([(i["width"], i["height"]) for i in seg["items"]]))
    run.check("【分段】白色滑块停在选中的那一段下面（滑块与该段居中对齐）", thumb_under(seg, 0), f"{seg['thumbCenter']} vs {seg['items'][0]['center']}")
    run.check("【分段】选中项文字加粗", int(seg["items"][0]["weight"]) > int(seg["items"][1]["weight"]), str([i["weight"] for i in seg["items"]]))

    expenses = [k for k, v in expected["items"].items() if not v["income"]]
    incomes = [k for k, v in expected["items"].items() if v["income"]]

    tid(page, "bills-segment").get_by_test_id("segment").filter(has_text="支出").click()
    wait_summary(page, expected["expense"], 0, len(expenses))
    page.wait_for_function("(n) => document.querySelectorAll('[data-testid=\"bills-page\"] [data-testid=\"bill-row\"]').length === n", arg=len(expenses))
    page.wait_for_function("() => !document.querySelector('[data-testid=\"bills-page\"] [data-testid=\"bill-row\"][data-income=\"true\"]')")
    wait_animations_settled(page)
    seg = page.evaluate(SEGMENTS_JS)
    groups = page.evaluate(DAY_GROUPS_JS)
    run.check("【分段】选「支出」：只剩支出账单，汇总卡支出不变、收入归零、笔数 = 支出笔数", amount_of(page, "bills-total-income") == "0.00" and count_of(page) == len(expenses), f"{amount_of(page, 'bills-total-income')} {count_of(page)}")
    run.check("【分段】选「支出」：组头合计只剩「支出」，昨天那一组不再有收入", all("收入" not in g["totals"] for g in groups) and "支出 ¥299.00" in [g["totals"] for g in groups], str([g["totals"] for g in groups]))
    run.check("【分段】滑块滑到第二段（data-value / aria-pressed / 位置同步）", seg["value"] == "Expense" and [i["pressed"] for i in seg["items"]] == ["false", "true", "false"] and thumb_under(seg, 1), f"{seg['value']} {seg['thumbCenter']} vs {seg['items'][1]['center']}")
    shot(page, "bills-segment-expense")

    tid(page, "bills-segment").get_by_test_id("segment").filter(has_text="收入").click()
    wait_summary(page, 0, expected["income"], len(incomes))
    wait_rows(page, len(incomes))
    wait_animations_settled(page)
    seg = page.evaluate(SEGMENTS_JS)
    groups = page.evaluate(DAY_GROUPS_JS)
    run.check("【分段】选「收入」：只剩收入账单，支出归零；组头合计只有「收入」", amount_of(page, "bills-total-income") == plain(expected["income"]) and [g["totals"] for g in groups] == [f"收入 {money(expected['income'])}"], str([g["totals"] for g in groups]))
    run.check("【分段】滑块滑到第三段", seg["value"] == "Income" and thumb_under(seg, 2), f"{seg['thumbCenter']} vs {seg['items'][2]['center']}")

    tid(page, "bills-segment").get_by_test_id("segment").filter(has_text="全部").click()
    wait_summary(page, expected["expense"], expected["income"], expected["count"])
    wait_rows(page, expected["count"])
    run.check("【分段】回到「全部」：账单与汇总恢复", count_of(page) == expected["count"] and len(page.evaluate(ROWS_JS)) == expected["count"])


def check_title_collapse(run: TestRun, page) -> None:
    state = scroll_root_state(page)
    run.check("内容超出一屏，账单页能在自己的容器里滚动", state and state["max"] > 100, str(state))
    if not state or state["max"] <= 100:
        return

    scroll_root_to(page, 12)
    wait_animations_settled(page)
    near = page.evaluate(LAYOUT_JS)
    run.check("【折叠】轻微下滚、大标题还露着：不折叠，小标题仍不可见", near["collapsed"] != "true" and near["navTitle"]["opacity"] == 0 and near["title"]["bottom"] > near["nav"]["bottom"] + 2, f"{near['collapsed']} {near['navTitle']} {near['title']}")

    scroll_root_to(page, min(160, state["max"]))
    page.wait_for_function(COLLAPSED_JS)
    wait_animations_settled(page)
    far = page.evaluate(LAYOUT_JS)
    run.check("【折叠】大标题滚到导航栏之下后折叠：data-title-collapsed=true", far["collapsed"] == "true" and far["title"]["bottom"] <= far["nav"]["bottom"] + 2, f"{far['collapsed']} {far['title']} {far['nav']}")
    run.check("【折叠】折叠后导航栏填上页面背景色（不透明，不是玻璃）、仍吸在屏幕顶端", far["nav"]["bg"] == RGB_BG and far["nav"]["backdrop"] == "none" and approx(far["nav"]["top"], 0), str(far["nav"]))
    run.check("【折叠】居中浮现小标题「账单」，右侧按钮不动", far["navTitle"]["opacity"] == 1 and approx(far["navTitle"]["centerX"], SCREEN_W / 2, 2) and far["navTitle"]["right"] <= far["actionsLeft"] + 0.5 and far["filter"]["left"] == near["filter"]["left"], f"{far['navTitle']} {far['actionsLeft']}")
    shot(page, "bills-collapsed")

    scroll_root_to(page, 0)
    page.wait_for_function(EXPANDED_JS)
    wait_animations_settled(page)
    top = page.evaluate(LAYOUT_JS)
    run.check("【折叠】回到顶部又展开：导航栏透明、小标题消失", top["collapsed"] == "false" and top["nav"]["bg"] == "rgba(242, 242, 247, 0)" and top["navTitle"]["opacity"] == 0, f"{top['collapsed']} {top['nav']} {top['navTitle']}")


def check_layout(run: TestRun, page) -> None:
    scroll_root_to(page, "bottom")
    page.wait_for_function("() => document.querySelector('[data-testid=\"bills-page\"]').scrollTop > 100")
    wait_animations_settled(page)
    rows = page.evaluate(ROWS_JS)
    tab_top = SCREEN_H - 21 - 62
    run.check("滚到底后最后一行账单露出在 Tab 栏之上（底部让出 Tab 栏占位）", rows and rows[-1]["bottom"] <= tab_top + 1, f"{rows[-1]['bottom'] if rows else None} <= {tab_top}")
    scroll_root_to(page, 0)
    page.wait_for_function(EXPANDED_JS)

    glass = page.evaluate(GLASS_LAYERS_JS)
    run.check("账单页同屏玻璃（backdrop-filter）层数 ≤ 3（Tab 栏 + 筛选 + 多选，性能预算，见 ADR 0007）", glass <= 3, str(glass))
    run.check("没有横向溢出", page.evaluate("() => document.documentElement.scrollWidth - document.documentElement.clientWidth") <= 0)
    run.check("组头与汇总标签都不是 <p>（避免 MASA 的 p 底部外边距把紧凑排版撑开）", page.evaluate("() => document.querySelectorAll('[data-testid=\"bills-page\"] .ft-summary p, [data-testid=\"bills-page\"] .ft-day__head p').length") == 0)
    shot(page, "bills-full")


def check_tab_state_kept(run: TestRun, page, expected: dict) -> None:
    """Tab 根页面常驻：切到别的 Tab 再回来，分段与滚动位置都还在，折叠状态与滚动位置一致。"""
    tid(page, "bills-segment").get_by_test_id("segment").filter(has_text="支出").click()
    wait_summary(page, expected["expense"], 0, sum(1 for v in expected["items"].values() if not v["income"]))
    state = scroll_root_state(page)
    scroll_root_to(page, min(120, state["max"]))
    page.wait_for_function(COLLAPSED_JS)
    wait_animations_settled(page)
    before = scroll_root_state(page)["top"]

    open_tab(page, "首页")
    page.wait_for_function("() => document.querySelector('[data-testid=\"bills-page\"]').closest('.p-page-container__item').classList.contains('d-none')")
    open_tab(page, "账单")
    wait_animations_settled(page)
    after = page.evaluate(LAYOUT_JS)
    seg = page.evaluate(SEGMENTS_JS)
    consistent = (after["collapsed"] == "true") == (after["title"]["bottom"] <= after["nav"]["bottom"] + 2)
    run.check("【Tab 切换】回到账单：分段选择保持（支出）", seg["value"] == "Expense", seg["value"])
    run.check("【Tab 切换】回到账单：滚动位置保持", approx(after["scrollTop"], before, 2), f"{after['scrollTop']} vs {before}")
    run.check("【Tab 切换】回到账单：大标题折叠状态与滚动位置一致（折叠 ⇔ 大标题已滚到导航栏之下）", consistent, f"{after['collapsed']} scrollTop={after['scrollTop']} title={after['title']} nav={after['nav']}")

    scroll_root_to(page, 0)
    page.wait_for_function(EXPANDED_JS)
    tid(page, "bills-segment").get_by_test_id("segment").filter(has_text="全部").click()
    wait_summary(page, expected["expense"], expected["income"], expected["count"])


def check_immersive_notch(run: TestRun, page) -> None:
    base = page.evaluate(LAYOUT_JS)
    page.evaluate(SET_SAFE_JS, [TOP, BOTTOM])
    notch = page.evaluate(LAYOUT_JS)
    run.check("【沉浸式】模拟刘海后导航栏变高（44 + 47），仍贴着屏幕顶端；按钮与标题都在状态栏之下", approx(notch["nav"]["height"], 44 + TOP) and approx(notch["nav"]["top"], 0) and notch["select"]["top"] >= TOP and notch["filter"]["top"] >= TOP, f"{notch['nav']} {notch['select']}")
    run.check("【沉浸式】大标题仍排在导航栏之下，没有被状态栏盖住", notch["title"]["top"] >= notch["nav"]["bottom"] - 1 and approx(notch["title"]["top"] - base["title"]["top"], TOP), f"{base['title']} -> {notch['title']}")
    run.check("【沉浸式】底部留白 = Tab 栏占位（21 + 62 + 34 + 24），最后一项内容滚得出 Tab 栏", approx(notch["page"]["paddingBottom"], 21 + 62 + BOTTOM + 24), str(notch["page"]["paddingBottom"]))
    state = scroll_root_state(page)
    scroll_root_to(page, min(200, state["max"]))
    page.wait_for_function(COLLAPSED_JS)
    wait_animations_settled(page)
    collapsed = page.evaluate(LAYOUT_JS)
    run.check("【沉浸式】折叠后导航栏（含状态栏高度）是不透明的页面背景色，内容不会从状态栏图标后面透出来", collapsed["nav"]["bg"] == RGB_BG and approx(collapsed["nav"]["top"], 0) and approx(collapsed["nav"]["height"], 44 + TOP), str(collapsed["nav"]))
    shot(page, "bills-notch")
    scroll_root_to(page, 0)
    page.wait_for_function(EXPANDED_JS)
    page.evaluate(CLEAR_SAFE_JS)


def check_navigation(run: TestRun, page, bills: dict) -> None:
    lunch_id = bills["lunch"].lower()
    page.locator(f'[data-testid="bills-page"] [data-testid="bill-row"][data-bill-id="{lunch_id}"]').click()
    sheet = addbill_sheet(page)
    sheet.wait_for(state="visible")
    wait_attr(page, ADDBILL_SHEET_SELECTOR, "data-ready", "true")
    run.check(
        "点按账单行升起该账单的编辑 Sheet（记一笔 Sheet 的编辑模式，是覆盖层不是路由页：地址不变）",
        sheet.get_attribute("data-mode") == "edit" and sheet.get_attribute("data-bill-id") == lunch_id and page.evaluate("() => location.pathname") == "/bills",
    )
    wait_animations_settled(page)
    close_add_bill_sheet(page)
    run.check("关闭编辑 Sheet 后 Tab 栏仍在、停在账单 Tab，列表仍在", tab_bar(page).is_visible() and active_tab(page) == "bills" and len(page.evaluate(ROWS_JS)) == 7)


def check_data_list(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_name = unique_name("账单账本")
        ledger_id = db.seed_ledger(user_id, ledger_name)
        now = datetime.now()
        bills = seed_bills(db, user_id, ledger_id)
        expected = expected_for(now)

        go_bills(page)
        # 默认视图只看「本月」：造的账单是「几天前」，月初跑时有几笔会落到上个月，所以按「本月」里的那部分算期望
        month_view = sums([Spec(*raw) for raw in BILL_SPECS if in_preset(at(raw[1], raw[2], raw[3]), "month", now)])
        wait_summary(page, *month_view)
        wait_rows(page, min(month_view[2], PAGE_SIZE))
        run.check(
            "【默认视图】账单 Tab 打开就是「本月」：汇总与列表只含本月的账单；时间标签是中性的「本月」（不是条件、无角标）",
            list_is(page, *month_view) and chips_state(page)[0]["text"] == "本月" and chips_state(page)[0]["active"] == "false" and badge_text(page) is None,
            f"{amount_of(page, 'bills-total-expense')} / {count_of(page)} vs {month_view}",
        )
        # 其余检查看的是列表本身（分组 / 行 / 分段 / 折叠…），统一看全部时间，不受今天是几号影响
        show_all_time(page, (expected["expense"], expected["income"], expected["count"]))
        check_summary_and_groups(run, page, expected, bills)
        check_rows(run, page, expected, bills, ledger_name)
        check_segments(run, page, expected, bills)
        check_title_collapse(run, page)
        check_layout(run, page)
        check_tab_state_kept(run, page, expected)
        check_immersive_notch(run, page)
        check_navigation(run, page, bills)

        run.check_clean(s.errors, "B 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- C. 左滑与删除


def check_swipe_and_delete(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_id = db.seed_ledger(user_id, unique_name("删除账本"))
        keep_note, gone_note, lone_note = unique_name("保留"), unique_name("待删"), unique_name("昨日唯一")
        keep = db.seed_bill(user_id, amount=40, ledger_id=ledger_id, note=keep_note, when=at(0, 11, 0))
        gone = db.seed_bill(user_id, amount=15, ledger_id=ledger_id, note=gone_note, when=at(0, 9, 0))
        lone = db.seed_bill(user_id, amount=66, ledger_id=ledger_id, note=lone_note, when=at(1, 9, 0))
        # 待删的这笔先标成「已同步」：删除之后要变回「待同步」，下一次同步才会把删除传给云端与其他设备
        db.set_bill_sync_status(gone, SYNC_SYNCED)

        go_bills(page)
        show_all_time(page, (121, 0, 3))

        # ---- 左滑露出「编辑 / 删除」，点「编辑」进入编辑页 ----
        swipe = swipe_open(page, gone)
        labels = [t.strip() for t in swipe.locator(".ft-swipe__tray button").all_inner_texts()]
        run.check("左滑一行露出「编辑」「删除」两个操作（真实触摸）", labels == ["编辑", "删除"], str(labels))
        swipe.get_by_test_id("swipe-edit").click()
        sheet = addbill_sheet(page)
        sheet.wait_for(state="visible")
        wait_attr(page, ADDBILL_SHEET_SELECTOR, "data-ready", "true")
        run.check(
            "左滑「编辑」升起该账单的编辑 Sheet（地址不变，仍在账单 Tab）",
            sheet.get_attribute("data-mode") == "edit" and sheet.get_attribute("data-bill-id") == gone.lower() and page.evaluate("() => location.pathname") == "/bills",
        )
        wait_animations_settled(page)
        close_add_bill_sheet(page)
        run.check("关闭编辑 Sheet 后账单仍在、行已合上", swipe_of(page, gone).count() == 1 and swipe_of(page, gone).get_attribute("data-open") == "false")

        # ---- 删除：先取消 ----
        swipe = swipe_open(page, gone)
        swipe.get_by_test_id("swipe-delete").click()
        tid(page, "bill-delete-alert").wait_for(state="visible")
        title = tid(page, "alert-title").inner_text()
        message = tid(page, "alert-message").inner_text()
        run.check("删除先弹确认 Alert：标题「删除这笔账单？」", title == "删除这笔账单？", title)
        run.check("Alert 点名是哪一笔（分类 · 备注、金额），并说明会随同步传播", f"其他支出 · {gone_note}" in message or f"· {gone_note}" in message, message)
        run.check("Alert 说明里有金额与「随同步传播」", "-¥15.00" in message and "随同步传播" in message, message)
        tid(page, "alert-cancel").click()
        tid(page, "alert-host").wait_for(state="detached")
        run.check("取消删除：账单仍在列表与数据库里，汇总不变", swipe_of(page, gone).count() == 1 and amount_of(page, "bills-total-expense") == "121.00" and any(b["Id"] == gone.upper() for b in db.bills_of_user(user_id)))

        # ---- 删除：确认 ----
        delete_via_swipe(page, gone)
        toast(page, "账单已删除").wait_for(state="visible")
        wait_summary(page, 106, 0, 2)
        wait_rows(page, 2)
        row = next((b for b in db.bills_of_user(user_id, include_deleted=True) if b["Id"] == gone.upper()), None)
        run.check("确认删除：Toast「账单已删除」，行消失，汇总 / 笔数即时更新（121 → 106）", swipe_of(page, gone).count() == 0 and amount_of(page, "bills-total-expense") == "106.00" and count_of(page) == 2)
        run.check("删除是软删除：记录仍在库里且 IsDeleted=1，没有物理删除", row is not None and row["IsDeleted"] == 1, str(row))
        run.check("删除随同步传播：这笔账单（原已同步）变回「待同步」", row is not None and row["SyncStatus"] == SYNC_PENDING, str(row))
        run.check("删除后列表里别的账单不受影响", swipe_of(page, keep).count() == 1 and swipe_of(page, lone).count() == 1)

        # ---- 删光一个分组：分组消失；删光所有账单：回到空状态 ----
        delete_via_swipe(page, lone)
        wait_summary(page, 40, 0, 1)
        page.wait_for_function("() => document.querySelectorAll('[data-testid=\"bills-page\"] [data-testid=\"bills-day\"]').length === 1")
        run.check("删光「昨天」的账单后，这一组整个消失", [g["title"] for g in page.evaluate(DAY_GROUPS_JS)] == ["今天"], str([g["title"] for g in page.evaluate(DAY_GROUPS_JS)]))

        delete_via_swipe(page, keep)
        tid(page, "bills-empty").wait_for(state="visible")
        run.check(
            "删光所有账单后回到空状态「还没有账单」（此时看的是不限时间，不再有「本月」的限定），汇总归零",
            "还没有账单" in tid(page, "bills-empty").inner_text() and "本月" not in tid(page, "bills-empty").inner_text()
            and amount_of(page, "bills-total-expense") == "0.00" and count_of(page) == 0,
            tid(page, "bills-empty").inner_text(),
        )
        run.check("没有残留的遮罩层 / Alert", tid(page, "alert-host").count() == 0)

        run.check_clean(s.errors, "C 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- D. 刷新


def check_refresh(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_id = db.seed_ledger(user_id, unique_name("刷新账本"))
        other = db.category_id_by_name("其他支出")
        total = Decimal(0)
        # 内容要够长（> 一屏），才能验证「页面不在顶部不触发刷新」
        for i in range(14):
            db.seed_bill(user_id, amount=10, ledger_id=ledger_id, note=unique_name(f"底{i}"), when=at(i, 12, 0), category_id=other)
            total += 10
        go_bills(page)
        show_all_time(page, (total, 0, 14))

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
            db.seed_bill(user_id, amount=200, ledger_id=ledger_id, note=unique_name("短拉"), when=at(0, 14, 0), category_id=other)
            pull_down(page, distance=40, from_y=300)
            wait_ptr_idle(page)
            run.check("下拉距离不够（没到触发阈值）：松手回弹，不刷新", count_of(page) == 14, str(count_of(page)))

            # ---- 页面不在顶部：不触发 ----
            state = scroll_root_state(page)
            if state and state["max"] > 160:
                scroll_root_to(page, 150)
                page.wait_for_function("() => document.querySelector('[data-testid=\"bills-page\"]').scrollTop >= 149")
                pull_down(page, distance=120, from_y=300)
                wait_ptr_idle(page)
                run.check("页面不在顶部时向下拖只是滚动，不触发刷新", count_of(page) == 14, str(count_of(page)))
                scroll_root_to(page, 0)
                page.wait_for_function("() => document.querySelector('[data-testid=\"bills-page\"]').scrollTop <= 1")
                wait_animations_settled(page)
            else:
                run.check("账单页内容足够长，能验证「不在顶部不触发刷新」", False, str(state))

            # ---- 完整下拉刷新（真实触摸）----
            pull_down(page, distance=240, from_y=300)
            wait_summary(page, total, 0, 15)
            run.check("下拉刷新（真实触摸）：汇总卡与列表更新为最新数据（造了一笔 200）", amount_of(page, "bills-total-expense") == plain(total) and count_of(page) == 15, f"{amount_of(page, 'bills-total-expense')} {count_of(page)}")
            wait_ptr_idle(page)
            run.check("刷新结束后页面不再标记忙碌（aria-busy 已移除）", bills_page(page).get_attribute("aria-busy") is None)
            run.check("刷新结束后下拉区收起，没有残留的位移变量", page.evaluate(PTR_IDLE_JS))
            zone_height = page.evaluate("() => document.querySelector('[data-testid=\"bills-ptr\"]').getBoundingClientRect().height")
            run.check("下拉区收起后高度为 0（大标题回到原位）", zone_height <= 0.5, str(zone_height))
            run.check("下拉区的读屏状态播报区是 aria-live=polite（正在刷新 / 刷新完成）", page.locator('[data-testid="bills-ptr"] [data-ptr-status]').get_attribute("aria-live") == "polite")
            layout = page.evaluate(LAYOUT_JS)
            run.check("下拉时导航栏不动（刷新结束后仍吸顶），大标题回到导航栏之下", approx(layout["nav"]["top"], 0) and layout["title"]["top"] >= layout["nav"]["bottom"] - 1, f"{layout['nav']} {layout['title']}")

            if cloud is not None:
                # 页面读路径会顺带联网刷新共享账本的成员缓存（登录换令牌 + 拉成员关系，ADR 0006）：那是读取、不是同步，
                # 而且失败后 30 秒内会直接跳过——窗口还在不在取决于本节跑了多久，所以不计入；其余任何请求（推送 / 拉取账单、分类、账本……）才是「触发了同步」
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

        run.check_clean(s.errors, "D 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- E. 条件条、筛选 Sheet 与共享账本


def check_filter_sheet(run: TestRun, db: LocalDb) -> None:
    """E1. 条件条与筛选 Sheet：结构、时间预设、生效 / 放弃、定位到分组、单独移除条件"""
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_name = unique_name("筛选账本")
        ledger_id = db.seed_ledger(user_id, ledger_name)
        now = datetime.now()
        seed_bills(db, user_id, ledger_id, FILTER_SPECS, alipay_channels(db))
        everything = sums(pick(now))  # 默认视图 = 「本月」
        everything_all = sums(pick(now, all_time=True))  # 不限时间：多一笔 40 天前的旧账单
        total_text = f"查看 {everything[2]} 笔账单"
        all_text = f"查看 {everything_all[2]} 笔账单"

        go_bills(page)
        wait_summary(page, *everything)
        wait_rows(page, min(everything[2], PAGE_SIZE))
        run.check(
            "【默认视图】账单 Tab 默认只看「本月」：汇总与列表不含 40 天前的旧账单（不限时间时才多出这 1 笔 ¥30）",
            list_is(page, *everything) and everything_all[2] == everything[2] + 1 and everything_all[0] - everything[0] == Decimal(30),
            f"{everything} vs {everything_all}",
        )

        # ---- 条件条：默认四颗标签，都没设置条件（时间标签是中性的「本月」）----
        chips = chips_state(page)
        bar = page.evaluate(ORDER_JS)
        open_button = tid(page, "bills-filter-open")
        run.check(
            "【条件条】四颗标签依次是「时间 / 账本 / 分类 / 支付渠道」，都没设置条件：时间标签写着默认的「本月」（中性的，不是条件），其余写维度名",
            [c["dimension"] for c in chips] == ["time", "ledger", "category", "channel"]
            and [c["text"] for c in chips] == ["本月", "账本", "分类", "支付渠道"]
            and all(c["active"] == "false" for c in chips),
            str(chips),
        )
        run.check(
            "【条件条】没设置条件的标签，读屏名称是「账本，点按设置」这一类；默认的「本月」读「时间：本月，点按调整」",
            [c["openLabel"] for c in chips] == ["时间：本月，点按调整", "账本，点按设置", "分类，点按设置", "支付渠道，点按设置"],
            str([c["openLabel"] for c in chips]),
        )
        run.check("【条件条】每颗标签的点击区域高 ≥ 44", all(c["openHeight"] >= 44 for c in chips), str([c["openHeight"] for c in chips]))
        run.check(
            "【条件条】夹在分段控件与汇总卡之间，一行排开不换行、可横向滑动（role=group，读屏名「筛选条件」）",
            bar["segBottom"] <= bar["chipsTop"] + 1 and bar["chipsBottom"] <= bar["sumTop"] + 1 and bar["chipsHeight"] <= 50
            and bar["rowWrap"] == "nowrap" and bar["rowOverflowX"] == "auto" and bar["rowRole"] == "group" and bar["rowLabel"] == "筛选条件",
            str(bar),
        )
        run.check(
            "导航栏筛选按钮：没有角标，读屏名称「筛选」并声明会弹出对话框；筛选 Sheet 默认不在页面里",
            badge_text(page) is None and open_button.get_attribute("aria-label") == "筛选"
            and open_button.get_attribute("aria-haspopup") == "dialog" and filter_sheet(page).count() == 0,
        )

        # ---- 打开 Sheet：结构 ----
        open_filter_sheet(page)
        geom = page.evaluate(SHEET_GEOM_JS)
        st = sheet_state(page)
        run.check(
            "点导航栏筛选按钮：弹出筛选 Sheet（role=dialog、aria-modal=true），标题「筛选」，aria-labelledby 指向标题",
            geom["role"] == "dialog" and geom["modal"] == "true" and geom["title"] == "筛选" and geom["labelledby"] == geom["titleId"],
            str(geom),
        )
        run.check(
            "Sheet 贴着屏幕底边升起，「查看」按钮在固定的底部栏里（不随内容滚动）",
            geom["sheet"]["bottom"] >= SCREEN_H - 1 and geom["footer"] is not None and geom["footer"]["bottom"] >= SCREEN_H - 1
            and geom["apply"]["bottom"] <= geom["footer"]["bottom"] + 1,
            str(geom),
        )
        run.check(
            "Sheet 盖住整屏（含 Tab 栏）：Tab 栏所在位置点到的是 Sheet，不是 Tab 按钮",
            page.evaluate(COVER_JS, [SCREEN_W / 2, SCREEN_H - 30, "filter-sheet-host"]) and page.evaluate(COVER_JS, [40, SCREEN_H - 30, "filter-sheet-host"]),
        )
        run.check("四个分组标题依次是「时间 / 账本 / 分类 / 支付渠道」", st["titles"] == ["时间", "账本", "分类", "支付渠道"], str(st["titles"]))
        run.check(
            "【时间】今天 / 本周 / 本月 / 本年 / 自定义，默认选中「本月」（与账单页的默认视图一致），日期框收起",
            st["timeLabels"] == ["今天", "本周", "本月", "本年", "自定义"] and st["time"] == ["本月"] and not st["customVisible"],
            str(st),
        )
        run.check("【账本】全部（默认选中）/ 自己的账本 / 未归属账本", st["ledgerLabels"] == ["全部", ledger_name, "未归属账本"] and st["ledger"] == ["全部"], str(st["ledgerLabels"]))
        run.check(
            "【分类】含全部预设分类，支出分类在前、收入分类在后，默认都没选中",
            {"餐饮美食", "交通出行", "购物消费", "其他支出", "工资薪酬"} <= set(st["categoryLabels"])
            and st["categoryLabels"].index("餐饮美食") < st["categoryLabels"].index("工资薪酬") and st["category"] == [],
            str(st["categoryLabels"]),
        )
        run.check("【支付渠道】含「微信支付」「支付宝」，默认都没选中", {"微信支付", "支付宝"} <= set(st["channelLabels"]) and st["channel"] == [], str(st["channelLabels"]))
        icons = page.evaluate(SHEET_ICONS_JS)
        if icons["fontLoaded"]:
            run.check("分类 / 支付渠道标签前的图标都有字形（「支付宝」曾因 mdi-alipay 不在图标字体里而是一块空白）", not icons["blank"], str(icons["blank"]))
        else:
            run.skip("标签图标字形检查", "图标字体（CDN）没有加载，无法判断字形是否存在")
        run.check(
            "底部主按钮「查看 N 笔账单」，N = 汇总卡的笔数（打开瞬间就有数字）；已经是默认视图时「重置」不可点",
            st["applyText"] == total_text and st["applyCount"] == str(everything[2]) and not st["applyDisabled"] and st["resetDisabled"],
            str(st),
        )
        run.check("读屏：底部按钮外层是 aria-live=polite，笔数变化会被播报", geom["live"] == "polite", str(geom["live"]))
        # 23 号起：Sheet 打开时 <html data-ft-sheet-open> 让被盖住的页面与 Tab 栏的玻璃让位（MutationObserver 异步写，等它落定再量），
        # 同屏只剩 Sheet 自己的玻璃，满足每屏 ≤ 3（a11y_degradation_test 的 sheet:filter 场景同一口径）
        wait_state(page, f"() => ({GLASS_LAYERS_JS})() <= 3")
        glass = page.evaluate(GLASS_LAYERS_JS)
        run.check("Sheet 打开时同屏玻璃层数 ≤ 3（被盖住的页面与 Tab 栏的玻璃已让位，见 ADR 0007 §11）", glass <= 3, str(glass))
        shot(page, "bills-filter-sheet")

        # ---- 时间预设：互斥、再点取消（= 不限时间）、笔数联动；「本月」是默认，选它时「重置」不可点 ----
        for key, label in PRESET_LABELS:
            count = sums(pick(now, preset=key))[2]
            is_default = key == "month"
            toggle_chip(page, f"filter-time-{key}")
            wait_apply_count(page, count)
            st = sheet_state(page)
            run.check(
                f"【时间预设】选「{label}」：只有它选中（预设之间互斥），底部变成「查看 {count} 笔账单」，「重置」{'不可点（本月就是默认视图）' if is_default else '可点'}",
                st["time"] == [label] and st["applyText"] == f"查看 {count} 笔账单" and st["resetDisabled"] == is_default and not st["customVisible"],
                str(st),
            )
        toggle_chip(page, "filter-time-year", selected=False)
        wait_apply_count(page, everything_all[2])
        st = sheet_state(page)
        run.check(
            "再点一次已选中的「本年」= 取消选择（不限时间）：笔数 = 全部账单（含 40 天前的旧账单），「重置」可点（不限时间不是默认视图）",
            st["time"] == [] and st["applyText"] == all_text and not st["resetDisabled"],
            str(st),
        )
        toggle_chip(page, "filter-time-month")
        wait_apply_count(page, everything[2])
        st = sheet_state(page)
        run.check("再点「本月」：回到默认视图，笔数回到本月，「重置」又不可点", st["time"] == ["本月"] and st["applyText"] == total_text and st["resetDisabled"], str(st))

        today = sums(pick(now, preset="today"))
        toggle_chip(page, "filter-time-today")
        wait_apply_count(page, today[2])
        tid(page, "filter-reset").click()
        expect(tid(page, "filter-reset")).to_be_disabled(timeout=TIMEOUT_MS)
        wait_apply_count(page, everything[2])
        st = sheet_state(page)
        run.check(
            "点「重置」：时间回到默认的「本月」（不是不限时间），笔数回到本月，「重置」又不可点",
            st["time"] == ["本月"] and st["applyText"] == total_text and st["resetDisabled"],
            str(st),
        )

        # ---- 生效：今天 ----
        toggle_chip(page, "filter-time-today")
        wait_apply_count(page, today[2])
        apply_and_wait(page, *today)
        chips = chips_state(page)
        run.check(
            "【生效】点「查看」：Sheet 收起，列表与汇总按「今天」重算（只剩今天的组）",
            list_is(page, *today) and [g["title"] for g in page.evaluate(DAY_GROUPS_JS)] == ["今天"] and filter_sheet(page).count() == 0,
            f"{amount_of(page, 'bills-total-expense')} {count_of(page)}",
        )
        run.check(
            "【条件条】时间标签变成已设置：文字是当前条件「今天」，读屏名称「时间：今天，点按调整」，右侧 × 名称「移除时间条件：今天」",
            chips[0]["dimension"] == "time" and chips[0]["active"] == "true" and chips[0]["text"] == "今天"
            and chips[0]["openLabel"] == "时间：今天，点按调整" and chips[0]["removeLabel"] == "移除时间条件：今天",
            str(chips[0]),
        )
        run.check(
            "【条件条】已设置的标签：文字区与 × 的点击区域都 ≥ 44 高，× 至少 44 宽",
            chips[0]["openHeight"] >= 44 and chips[0]["removeHeight"] >= 44 and chips[0]["removeWidth"] >= 44,
            str(chips[0]),
        )
        run.check("其余三颗仍是未设置的标签", [c["active"] for c in chips[1:]] == ["false", "false", "false"], str([c["active"] for c in chips]))
        run.check("导航栏筛选按钮出现角标「1」，读屏名称「筛选，已设置 1 项」", badge_text(page) == "1" and open_button.get_attribute("aria-label") == "筛选，已设置 1 项", str(badge_text(page)))
        shot(page, "bills-chip-active")

        # ---- 放弃：✕ / Esc / 遮罩 / 没改直接「查看」 ----
        year = sums(pick(now, preset="year"))
        open_filter_sheet(page, via="time")
        st = sheet_state(page)
        run.check(
            "点已设置标签的文字：重新打开 Sheet，草稿 = 当前生效的条件（「今天」选中、「重置」可点），底部笔数是当前笔数",
            st["time"] == ["今天"] and not st["resetDisabled"] and st["applyCount"] == str(today[2]),
            str(st),
        )
        toggle_chip(page, "filter-time-year")
        wait_apply_count(page, year[2])
        close_filter_sheet(page)
        run.check("点 ✕ 关闭 = 放弃改动：生效条件仍是「今天」，列表没变", chips_state(page)[0]["text"] == "今天" and list_is(page, *today))

        open_filter_sheet(page)
        st = sheet_state(page)
        run.check("放弃后再打开：草稿重新从当前生效的条件复制（不是上次没提交的「本年」）", st["time"] == ["今天"], str(st))
        toggle_chip(page, "filter-time-month")
        page.keyboard.press("Escape")
        wait_sheet_gone(page)
        run.check("按 Esc 同样放弃改动", chips_state(page)[0]["text"] == "今天" and list_is(page, *today))

        open_filter_sheet(page)
        toggle_chip(page, "filter-time-month")
        top = page.evaluate(SHEET_GEOM_JS)["sheet"]["top"]
        if top >= 6:
            page.mouse.click(SCREEN_W / 2, top / 2)
            wait_sheet_gone(page)
            run.check("点 Sheet 上方的遮罩同样放弃改动", chips_state(page)[0]["text"] == "今天" and list_is(page, *today))
        else:
            close_filter_sheet(page)
            run.skip("点遮罩放弃改动", f"Sheet 顶部离屏幕上沿只有 {top:.0f}px，没有遮罩可点")

        open_filter_sheet(page)
        apply_filter(page)
        run.check("没改任何条件就点「查看」：只是收起 Sheet，条件与列表不变", chips_state(page)[0]["text"] == "今天" and list_is(page, *today) and filter_sheet(page).count() == 0)

        # ---- 从条件条的标签进入：定位到那一组（矮屏下内容区才需要滚动）----
        page.set_viewport_size({"width": SCREEN_W, "height": 480})
        open_filter_sheet(page, via="category")
        geom = page.evaluate(SHEET_GEOM_JS)
        run.check(
            "【定位】从条件条的「分类」标签进入：Sheet 滚到「分类」那一组，组标题贴近内容区顶部",
            geom["bodyScrollTop"] > 0 and abs(geom["sections"]["category"]["top"] - geom["body"]["top"]) <= 14,
            str(geom),
        )
        close_filter_sheet(page)
        open_filter_sheet(page, via="channel")
        geom = page.evaluate(SHEET_GEOM_JS)
        run.check(
            "【定位】从「支付渠道」标签进入：滚到底，该组完整落在内容区里",
            geom["bodyScrollTop"] >= geom["bodyMax"] - 2 and geom["sections"]["channel"]["top"] >= geom["body"]["top"] - 1
            and geom["sections"]["channel"]["bottom"] <= geom["body"]["bottom"] + 1,
            str(geom),
        )
        close_filter_sheet(page)
        page.set_viewport_size(PHONE_VIEWPORT)

        # ---- 条件条上的 ×：单独移除这个条件（时间的 × = 取消时间范围，不是回到「本月」）----
        remove_chip(page, "time")
        wait_summary(page, *everything_all)
        wait_rows(page, min(everything_all[2], PAGE_SIZE))
        chips = chips_state(page)
        run.check(
            "点「时间」标签上的 ×：只移除这个条件（不打开 Sheet）= 取消时间范围（不限时间）：列表与汇总是全部账单（含 40 天前的旧账单），"
            "标签变成中性的「时间」（读屏名称「时间，点按设置」），不算条件、角标消失",
            list_is(page, *everything_all) and filter_sheet(page).count() == 0 and chips[0]["text"] == "时间" and chips[0]["active"] == "false"
            and chips[0]["openLabel"] == "时间，点按设置" and badge_text(page) is None,
            str(chips[0]),
        )

        # ---- 不限时间 ≠ 默认视图：Sheet 里一项时间都没选、「重置」可点；重置 = 回到「本月」----
        open_filter_sheet(page, via="time")
        wait_apply_count(page, everything_all[2])
        st = sheet_state(page)
        run.check(
            "不限时间时再打开 Sheet：时间一项都没选，底部笔数是全部账单，「重置」可点（它偏离了默认的「本月」）",
            st["time"] == [] and st["applyText"] == all_text and not st["resetDisabled"] and not st["applyDisabled"],
            str(st),
        )
        tid(page, "filter-reset").click()
        expect(tid(page, "filter-reset")).to_be_disabled(timeout=TIMEOUT_MS)
        wait_apply_count(page, everything[2])
        apply_and_wait(page, *everything)
        chips = chips_state(page)
        run.check(
            "重置并「查看」：回到默认的「本月」，列表不再含旧账单，标签是中性的「本月」，仍没有角标",
            list_is(page, *everything) and chips[0]["text"] == "本月" and chips[0]["active"] == "false" and badge_text(page) is None,
            str(chips[0]),
        )

        run.check_clean(s.errors, "E1 节会话无页面异常 / 控制台错误 / 失败请求")


def check_filter_dimensions(run: TestRun, db: LocalDb) -> None:
    """E2. 账本（含未归属账本）/ 分类 / 支付渠道 / 自定义日期 / 重置 / 无结果：条件叠加、条件条的排序与移除"""
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_name = unique_name("维度账本")
        ledger_id = db.seed_ledger(user_id, ledger_name)
        now = datetime.now()
        seed_bills(db, user_id, ledger_id, FILTER_SPECS, alipay_channels(db))
        everything = sums(pick(now))  # 默认视图 = 「本月」
        everything_all = sums(pick(now, all_time=True))  # 不限时间（多一笔 40 天前的旧账单）
        total_text = f"查看 {everything[2]} 笔账单"
        all_text = f"查看 {everything_all[2]} 笔账单"
        in_ledger = sums(pick(now, ledger=True))
        unassigned = sums(pick(now, ledger=False))

        go_bills(page)
        wait_summary(page, *everything)

        # ---- 账本：具体账本与「未归属账本」互斥，「全部」= 不限 ----
        open_filter_sheet(page, via="ledger")
        toggle_chip(page, "filter-ledger-chip", ledger_name)
        wait_apply_count(page, in_ledger[2])
        st = sheet_state(page)
        run.check(
            "【账本】选自己的账本：「全部」取消选中、该账本选中，笔数 = 账本里的账单（历史无账本那笔不算）",
            st["ledger"] == [ledger_name] and st["applyText"] == f"查看 {in_ledger[2]} 笔账单",
            str(st),
        )
        toggle_chip(page, "filter-ledger-unassigned")
        wait_apply_count(page, unassigned[2])
        st = sheet_state(page)
        run.check(
            f"【账本】再选「未归属账本」：与具体账本互斥（只剩它选中），笔数 = 历史无账本的账单（{unassigned[2]} 笔）",
            st["ledger"] == ["未归属账本"] and st["applyText"] == f"查看 {unassigned[2]} 笔账单",
            str(st),
        )
        toggle_chip(page, "filter-ledger-any")
        wait_apply_count(page, everything[2])
        st = sheet_state(page)
        run.check("【账本】点「全部」= 不限账本：只有「全部」选中，笔数回到全部，「重置」不可点", st["ledger"] == ["全部"] and st["resetDisabled"] and st["applyCount"] == str(everything[2]), str(st))
        toggle_chip(page, "filter-ledger-unassigned")
        wait_apply_count(page, unassigned[2])
        apply_and_wait(page, *unassigned)
        chips = chips_state(page)
        run.check(
            "【未归属账本】生效：只剩历史无账本的账单；账本标签显示「未归属账本」，读屏名称「账本：未归属账本，点按调整」",
            list_is(page, *unassigned) and chips[0]["dimension"] == "ledger" and chips[0]["text"] == "未归属账本" and chips[0]["openLabel"] == "账本：未归属账本，点按调整",
            str(chips[0]),
        )
        run.check(
            "【条件条】已设置的标签排在最前（账本），其余保持原顺序：账本 → 时间 → 分类 → 支付渠道",
            [c["dimension"] for c in chips] == ["ledger", "time", "category", "channel"],
            str([c["dimension"] for c in chips]),
        )

        open_filter_sheet(page, via="ledger")
        st = sheet_state(page)
        run.check("从「账本」标签重新打开：草稿里「未归属账本」选中", st["ledger"] == ["未归属账本"], str(st))
        toggle_chip(page, "filter-ledger-chip", ledger_name)
        st = sheet_state(page)
        run.check("改选具体账本：「未归属账本」自动取消（同一个维度的不同取值）", st["ledger"] == [ledger_name], str(st))
        wait_apply_count(page, in_ledger[2])
        apply_and_wait(page, *in_ledger)
        chips = chips_state(page)
        run.check("按自己的账本筛选生效：历史无账本的账单被排除，账本标签显示账本名", list_is(page, *in_ledger) and chips[0]["text"] == ledger_name, str(chips[0]))

        # ---- 分类：可多选（满足任一个就算），与账本叠加 ----
        food = sums(pick(now, ledger=True, category="餐饮美食"))
        food_traffic = sums(pick(now, ledger=True, category={"餐饮美食", "交通出行"}))
        open_filter_sheet(page, via="category")
        toggle_chip(page, "filter-category-chip", "餐饮美食")
        wait_apply_count(page, food[2])
        st = sheet_state(page)
        run.check("【分类】选「餐饮美食」：笔数 = 账本 ∩ 餐饮美食（条件叠加）", st["category"] == ["餐饮美食"] and st["applyText"] == f"查看 {food[2]} 笔账单", str(st))
        toggle_chip(page, "filter-category-chip", "交通出行")
        wait_apply_count(page, food_traffic[2])
        st = sheet_state(page)
        run.check(
            "【分类】再选「交通出行」：两个都选中（分类可多选），笔数 = 账本 ∩（餐饮美食 或 交通出行）",
            set(st["category"]) == {"餐饮美食", "交通出行"} and st["applyText"] == f"查看 {food_traffic[2]} 笔账单" and food_traffic[2] > food[2],
            str(st),
        )
        toggle_chip(page, "filter-category-chip", "交通出行", selected=False)
        wait_apply_count(page, food[2])
        st = sheet_state(page)
        run.check("【分类】再点一次已选中的「交通出行」：只取消这一个，「餐饮美食」仍选中，笔数回到只含餐饮美食", st["category"] == ["餐饮美食"] and st["applyText"] == f"查看 {food[2]} 笔账单", str(st))
        toggle_chip(page, "filter-category-chip", "交通出行")
        wait_apply_count(page, food_traffic[2])
        apply_and_wait(page, *food_traffic)
        chips = chips_state(page)
        run.check(
            "【叠加】账本 ∩ 分类（两个）生效：分类标签写「餐饮美食等 2 项」（条件条放不下全部名字），读屏名称「分类：餐饮美食等 2 项，点按调整」，"
            "两个分类只算角标里的 1 项（账本 + 分类 = 2）",
            list_is(page, *food_traffic) and chips[1]["dimension"] == "category" and chips[1]["text"] == "餐饮美食等 2 项"
            and chips[1]["openLabel"] == "分类：餐饮美食等 2 项，点按调整" and chips[1]["removeLabel"] == "移除分类条件：餐饮美食等 2 项"
            and chip_order(page) == ["ledger", "category", "time", "channel"] and badge_text(page) == "2",
            f"{chips[1]} {badge_text(page)}",
        )
        open_filter_sheet(page, via="category")
        st = sheet_state(page)
        run.check("从「分类」标签重新打开：两个分类都选中（回显当前条件）", set(st["category"]) == {"餐饮美食", "交通出行"}, str(st))
        close_filter_sheet(page)
        remove_chip(page, "category")
        wait_summary(page, *in_ledger)
        wait_rows(page, min(in_ledger[2], PAGE_SIZE))
        run.check(
            "点「分类」标签上的 ×：两个分类一起移除（一个维度一个 ×），账本条件保留，角标回到「1」",
            list_is(page, *in_ledger) and chip_order(page) == ["ledger", "time", "category", "channel"] and badge_text(page) == "1",
            f"{chip_order(page)} {badge_text(page)}",
        )
        open_filter_sheet(page, via="category")
        toggle_chip(page, "filter-category-chip", "餐饮美食")
        wait_apply_count(page, food[2])
        apply_and_wait(page, *food)
        chips = chips_state(page)
        run.check(
            "【叠加】账本 ∩ 分类生效；分类只选一个时标签直接显示「餐饮美食」，排在账本之后、时间之前",
            list_is(page, *food) and chip_order(page) == ["ledger", "category", "time", "channel"] and chips[1]["text"] == "餐饮美食",
            str(chip_order(page)),
        )

        # ---- 支付渠道 ----
        ali = sums(pick(now, ledger=True, category="餐饮美食", alipay=True))
        open_filter_sheet(page, via="channel")
        toggle_chip(page, "filter-channel-chip", "支付宝")
        wait_apply_count(page, ali[2])
        apply_and_wait(page, *ali)
        run.check(
            f"【叠加】再加支付渠道「支付宝」：账本 ∩ 分类 ∩ 渠道，只剩 {ali[2]} 笔（支付宝记的午餐）；条件条顺序 账本 → 分类 → 支付渠道 → 时间",
            list_is(page, *ali) and ali[2] == 1 and chip_order(page) == ["ledger", "category", "channel", "time"],
            str(chip_order(page)),
        )

        # ---- 四个维度全设置：角标、横向溢出、玻璃层 ----
        open_filter_sheet(page, via="time")
        toggle_chip(page, "filter-time-year")
        year_all = sums(pick(now, ledger=True, category="餐饮美食", alipay=True, preset="year"))
        wait_apply_count(page, year_all[2])
        apply_and_wait(page, *year_all)
        run.check(
            "【叠加】四个维度全部设置：条件条顺序「时间 → 账本 → 分类 → 支付渠道」，角标「4」，读屏名称带条件数",
            chip_order(page) == ["time", "ledger", "category", "channel"] and badge_text(page) == "4"
            and tid(page, "bills-filter-open").get_attribute("aria-label") == "筛选，已设置 4 项",
            f"{chip_order(page)} {badge_text(page)}",
        )
        run.check("四颗标签都带条件文字时没有页面级横向溢出（超出的部分在条件条里横向滑动）", page.evaluate(ORDER_JS)["overflowX"] <= 0, str(page.evaluate(ORDER_JS)["overflowX"]))
        glass = page.evaluate(GLASS_LAYERS_JS)
        run.check("Sheet 收起后账单 Tab 同屏玻璃层数仍 ≤ 3", glass <= 3, str(glass))
        shot(page, "bills-four-chips")

        remove_chip(page, "category")
        three = sums(pick(now, ledger=True, alipay=True, preset="year"))
        wait_summary(page, *three)
        wait_rows(page, min(three[2], PAGE_SIZE))
        run.check(
            "点「分类」标签上的 ×：只移除分类，其余条件保留（时间 / 账本 / 渠道），列表联动，角标「3」",
            list_is(page, *three) and chip_order(page) == ["time", "ledger", "channel", "category"] and badge_text(page) == "3",
            f"{chip_order(page)} {badge_text(page)}",
        )

        # ---- 无结果：分段「收入」叠加已有条件 → 必空 ----
        segment_button(page, "收入").click()
        tid(page, "bills-empty").wait_for(state="visible")
        run.check(
            "【无结果】分段「收入」叠加已有条件后没有符合的账单：空状态「没有符合条件的账单」，汇总归零，给「清除筛选」而不是「记一笔」",
            "没有符合条件的账单" in tid(page, "bills-empty").inner_text() and tid(page, "bills-clear-filter").is_visible()
            and tid(page, "bills-first-bill").count() == 0 and count_of(page) == 0,
            str(count_of(page)),
        )
        open_filter_sheet(page)
        wait_apply_count(page, 0)
        st = sheet_state(page)
        run.check(
            "【无结果】Sheet 里笔数 0：底部按钮照常写「查看 0 笔账单」且可点（不置灰，点了得到空状态与清除入口）；「重置」可点",
            not st["applyDisabled"] and st["applyText"] == "查看 0 笔账单" and st["applyCount"] == "0" and not st["resetDisabled"],
            str(st),
        )
        run.check("账单类型限定为「收入」时，分类标签只列收入分类（支出分类不出现）", "工资薪酬" in st["categoryLabels"] and "餐饮美食" not in st["categoryLabels"], str(st["categoryLabels"]))

        income_all = sums(pick(now, income=True))
        tid(page, "filter-reset").click()
        expect(tid(page, "filter-reset")).to_be_disabled(timeout=TIMEOUT_MS)
        wait_apply_count(page, income_all[2])
        st = sheet_state(page)
        run.check(
            "点「重置」：时间回到默认的「本月」，账本 / 分类 / 渠道的草稿全清（账单类型不在 Sheet 里，不受影响），笔数 = 本月的收入账单",
            st["time"] == ["本月"] and st["ledger"] == ["全部"] and st["category"] == [] and st["channel"] == [] and st["resetDisabled"]
            and st["applyText"] == f"查看 {income_all[2]} 笔账单",
            str(st),
        )
        run.check(
            "重置只改草稿：Sheet 还开着时，页面上的条件标签没变（仍是 3 个已设置）",
            [c["active"] for c in chips_state(page)].count("true") == 3,
            str([c["active"] for c in chips_state(page)]),
        )
        close_filter_sheet(page)
        run.check("重置后点 ✕ 放弃：页面条件仍然生效（仍是空状态、3 个已设置）", tid(page, "bills-empty").is_visible() and [c["active"] for c in chips_state(page)].count("true") == 3)
        open_filter_sheet(page)
        tid(page, "filter-reset").click()
        wait_apply_count(page, income_all[2])
        apply_and_wait(page, *income_all)
        run.check(
            "重置后点「查看」：全部条件清除（时间回到默认的「本月」）、角标消失；分段仍是「收入」，只剩那笔工资",
            list_is(page, *income_all) and all(c["active"] == "false" for c in chips_state(page)) and badge_text(page) is None
            and tid(page, "bills-segment").get_attribute("data-value") == "Income",
            str(chips_state(page)),
        )
        segment_button(page, "全部").click()
        wait_summary(page, *everything)

        # ---- 空状态里的「清除筛选」：条件与账单类型一起清掉 ----
        ali_only = sums(pick(now, alipay=True))
        open_filter_sheet(page, via="channel")
        toggle_chip(page, "filter-channel-chip", "支付宝")
        wait_apply_count(page, ali_only[2])
        apply_and_wait(page, *ali_only)
        segment_button(page, "收入").click()
        tid(page, "bills-empty").wait_for(state="visible")
        tid(page, "bills-clear-filter").click()
        wait_summary(page, *everything)
        wait_rows(page, min(everything[2], PAGE_SIZE))
        run.check(
            "「清除筛选」：条件与账单类型一起清掉（默认的「本月」不是条件，保留），本月的账单回来，分段回到「全部」，角标消失",
            list_is(page, *everything) and all(c["active"] == "false" for c in chips_state(page)) and badge_text(page) is None
            and chips_state(page)[0]["text"] == "本月" and tid(page, "bills-segment").get_attribute("data-value") == "All",
        )

        # ---- 清除筛选不顺手取消时间：时间条件（本年）也在条件里 → 清掉后是「不限时间」；之后点「查看全部账单」不会出现（不再只受「本月」限制）----
        open_filter_sheet(page, via="time")
        toggle_chip(page, "filter-time-year")
        year_only = sums(pick(now, preset="year"))
        wait_apply_count(page, year_only[2])
        apply_and_wait(page, *year_only)
        segment_button(page, "收入").click()
        wait_summary(page, *sums(pick(now, preset="year", income=True)))
        open_filter_sheet(page, via="channel")
        toggle_chip(page, "filter-channel-chip", "支付宝")
        wait_apply_count(page, 0)
        apply_filter(page)
        tid(page, "bills-empty").wait_for(state="visible")
        tid(page, "bills-clear-filter").click()
        wait_summary(page, *everything_all)
        wait_rows(page, min(everything_all[2], PAGE_SIZE))
        run.check(
            "空状态里「清除筛选」：偏离默认的时间条件（本年）也一并去掉，结果是「不限时间」（全部账单，含旧账单）；时间标签是中性的「时间」，分段回到「全部」",
            list_is(page, *everything_all) and chips_state(page)[0]["text"] == "时间" and badge_text(page) is None
            and tid(page, "bills-segment").get_attribute("data-value") == "All",
            f"{chips_state(page)[0]}",
        )
        # 回到默认视图，继续后面的自定义日期检查
        open_filter_sheet(page, via="time")
        tid(page, "filter-reset").click()
        expect(tid(page, "filter-reset")).to_be_disabled(timeout=TIMEOUT_MS)
        apply_and_wait(page, *everything)

        # ---- 自定义日期：起止都含当天；预设与自定义互斥 ----
        yesterday = now - timedelta(days=1)
        yesterday_text = yesterday.strftime("%Y-%m-%d")
        yday = sums(pick(now, start=yesterday.date(), end=yesterday.date()))
        first_day = now.replace(day=1)
        last_day = (first_day.replace(day=28) + timedelta(days=4)).replace(day=1) - timedelta(days=1)
        month_start, month_end = first_day.strftime("%Y-%m-%d"), last_day.strftime("%Y-%m-%d")
        open_filter_sheet(page)
        toggle_chip(page, "filter-time-month", selected=False)
        toggle_chip(page, "filter-time-custom")
        wait_apply_count(page, everything_all[2])
        st = sheet_state(page)
        run.check(
            "【自定义日期】先取消「本月」（不限时间）再点「自定义」：展开起止日期框（都是空的），「自定义」选中；没填日期不算条件，笔数仍是全部账单",
            st["customVisible"] and st["time"] == ["自定义"] and st["start"] == "" and st["end"] == "" and st["applyText"] == all_text,
            str(st),
        )
        filter_sheet(page).get_by_test_id("filter-start").fill(yesterday_text)
        filter_sheet(page).get_by_test_id("filter-end").fill(yesterday_text)
        wait_apply_count(page, yday[2])
        run.check(
            f"开始 = 结束 = 昨天：笔数 = 昨天的 {yday[2]} 笔（结束日含当天，按整天计）",
            sheet_state(page)["applyText"] == f"查看 {yday[2]} 笔账单" and not sheet_state(page)["resetDisabled"],
            str(sheet_state(page)),
        )
        apply_and_wait(page, *yday)
        chips = chips_state(page)
        run.check(
            "自定义生效：只剩昨天的账单；时间标签显示单日「M月D日」（起止是同一天只写一次）",
            list_is(page, *yday) and chips[0]["text"] == without_weekday(yesterday, now) and [g["title"] for g in page.evaluate(DAY_GROUPS_JS)] == ["昨天"],
            str(chips[0]),
        )

        open_filter_sheet(page, via="time")
        st = sheet_state(page)
        run.check(
            "从「时间」标签重新打开：自定义日期框已展开，起止都是昨天（回显当前条件）",
            st["customVisible"] and st["time"] == ["自定义"] and st["start"] == yesterday_text and st["end"] == yesterday_text,
            str(st),
        )
        toggle_chip(page, "filter-time-month")
        st = sheet_state(page)
        run.check("改选预设「本月」：自定义日期框收起、「自定义」取消选中（预设与自定义互斥）", st["time"] == ["本月"] and not st["customVisible"], str(st))
        toggle_chip(page, "filter-time-custom")
        st = sheet_state(page)
        run.check(
            "已选预设（这里是默认的「本月」）时点「自定义」：把预设的起止预填进日期框（本月 = 1 日至月末），方便微调",
            st["customVisible"] and st["time"] == ["自定义"] and st["start"] == month_start and st["end"] == month_end,
            str(st),
        )

        two_days_ago = now - timedelta(days=2)
        start_only = sums(pick(now, start=two_days_ago.date()))
        filter_sheet(page).get_by_test_id("filter-start").fill(two_days_ago.strftime("%Y-%m-%d"))
        filter_sheet(page).get_by_test_id("filter-end").fill("")
        wait_apply_count(page, start_only[2])
        apply_and_wait(page, *start_only)
        run.check(
            "只填开始日期：条件 = 该日起（含当天），标签「M月D日起」",
            list_is(page, *start_only) and chips_state(page)[0]["text"] == f"{without_weekday(two_days_ago, now)}起",
            str(chips_state(page)[0]),
        )
        end_only = sums(pick(now, end=two_days_ago.date()))
        open_filter_sheet(page, via="time")
        filter_sheet(page).get_by_test_id("filter-start").fill("")
        filter_sheet(page).get_by_test_id("filter-end").fill(two_days_ago.strftime("%Y-%m-%d"))
        wait_apply_count(page, end_only[2])
        apply_and_wait(page, *end_only)
        run.check(
            "只填结束日期：条件 = 截止到该日（含当天），标签「至M月D日」",
            list_is(page, *end_only) and chips_state(page)[0]["text"] == f"至{without_weekday(two_days_ago, now)}",
            str(chips_state(page)[0]),
        )

        open_filter_sheet(page, via="time")
        filter_sheet(page).get_by_test_id("filter-start").fill("2099-01-01")
        filter_sheet(page).get_by_test_id("filter-end").fill("")
        wait_apply_count(page, 0)
        st = sheet_state(page)
        run.check(
            "【无结果】开始日期设成 2099-01-01：笔数 0，底部按钮照常写「查看 0 笔账单」且可点（不置灰）",
            not st["applyDisabled"] and st["applyText"] == "查看 0 笔账单" and st["applyCount"] == "0",
            str(st),
        )
        apply_and_wait(page, 0, 0, 0)
        tid(page, "bills-empty").wait_for(state="visible")
        run.check(
            "点「查看 0 笔账单」：Sheet 收起，列表给出「没有符合条件的账单」与「清除筛选」（零结果的条件也照常生效），时间标签「2099年1月1日起」",
            "没有符合条件的账单" in tid(page, "bills-empty").inner_text() and tid(page, "bills-clear-filter").is_visible()
            and chips_state(page)[0]["text"] == "2099年1月1日起" and chips_state(page)[0]["active"] == "true",
            f"{tid(page, 'bills-empty').inner_text()} / {chips_state(page)[0]}",
        )

        open_filter_sheet(page, via="time")
        tid(page, "filter-reset").click()
        expect(tid(page, "filter-reset")).to_be_disabled(timeout=TIMEOUT_MS)
        wait_apply_count(page, everything[2])
        st = sheet_state(page)
        run.check(
            "点「重置」：日期框收起并清空，时间回到默认的「本月」，笔数回到本月，「查看」按钮可点",
            st["time"] == ["本月"] and not st["customVisible"] and not st["applyDisabled"] and st["applyText"] == total_text,
            str(st),
        )
        close_filter_sheet(page)
        run.check("放弃后页面条件仍是「2099年1月1日起」（空状态）", tid(page, "bills-empty").is_visible() and chips_state(page)[0]["text"] == "2099年1月1日起")

        open_filter_sheet(page)
        tid(page, "filter-reset").click()
        expect(tid(page, "filter-reset")).to_be_disabled(timeout=TIMEOUT_MS)
        wait_apply_count(page, everything[2])
        apply_and_wait(page, *everything)
        run.check(
            "重置后点「查看」：时间条件回到默认的「本月」，列表回到本月的账单，角标消失",
            list_is(page, *everything) and badge_text(page) is None and all(c["active"] == "false" for c in chips_state(page)) and chips_state(page)[0]["text"] == "本月",
        )

        # 默认的「本月」选中时点「自定义」：同样预填本月的起止（不用先改选别的预设）
        open_filter_sheet(page)
        toggle_chip(page, "filter-time-custom")
        wait_apply_count(page, everything[2])
        st = sheet_state(page)
        run.check(
            "默认视图下直接点「自定义」：起止日期框预填本月的起止，条件没有变（笔数仍是本月的笔数）",
            st["customVisible"] and st["time"] == ["自定义"] and st["start"] == month_start and st["end"] == month_end and st["applyText"] == total_text,
            str(st),
        )
        close_filter_sheet(page)

        run.check_clean(s.errors, "E2 节会话无页面异常 / 控制台错误 / 失败请求")


def check_filter_persistence(run: TestRun, db: LocalDb) -> None:
    """E3. 条件跟页面常驻（切 Tab 不丢）、改条件后不在列表里的已选账单自动移出、悬空的账本条件自动移除"""
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        name1, name2 = unique_name("常驻账本一"), unique_name("常驻账本二")
        ledger1, ledger2 = db.seed_ledger(user_id, name1), db.seed_ledger(user_id, name2)
        food = db.category_id_by_name("餐饮美食")
        traffic = db.category_id_by_name("交通出行")
        b1 = db.seed_bill(user_id, amount=35, ledger_id=ledger1, note=unique_name("常驻一"), when=at(0, 12, 0), category_id=food)
        db.seed_bill(user_id, amount=28, ledger_id=ledger1, note=unique_name("常驻二"), when=at(1, 12, 0), category_id=food)
        db.seed_bill(user_id, amount=6, ledger_id=ledger2, note=unique_name("常驻三"), when=at(0, 11, 0), category_id=food)
        db.seed_bill(user_id, amount=9, ledger_id=ledger2, note=unique_name("常驻四"), when=at(0, 10, 0), category_id=traffic)

        go_bills(page)
        show_all_time(page, (78, 0, 4))  # 昨天的账单月初会落到上个月：这一节看全部时间

        open_filter_sheet(page, via="ledger")
        toggle_chip(page, "filter-ledger-chip", name1)
        toggle_chip(page, "filter-category-chip", "餐饮美食")
        wait_apply_count(page, 2)
        apply_and_wait(page, 63, 0, 2)
        run.check("账本一 ∩ 餐饮美食：两笔（35 + 28），角标「2」（不限时间不算条件）", list_is(page, 63, 0, 2) and badge_text(page) == "2")

        # ---- 切 Tab 往返：条件随页面常驻 ----
        open_tab(page, "首页")
        open_tab(page, "账单")
        wait_bills_loaded(page)
        run.check(
            "【常驻】切到首页再切回：条件标签、列表与汇总原样保留（Tab 根页面常驻，不重置条件）；取消的时间范围（时间标签是「时间」）也保留，不会变回默认的「本月」",
            list_is(page, 63, 0, 2) and chip_order(page) == ["ledger", "category", "time", "channel"] and badge_text(page) == "2"
            and chips_state(page)[2]["text"] == "时间",
            f"{chip_order(page)} {amount_of(page, 'bills-total-expense')} {chips_state(page)[2]}",
        )

        # ---- 多选里选了账单，再改条件：不在列表里的从已选移除 ----
        tid(page, "bills-select-toggle").click()
        tid(page, "bills-batchbar").wait_for(state="visible")
        row_of(page, b1).click()
        wait_selected(page, 1)
        open_filter_sheet(page, via="ledger")
        toggle_chip(page, "filter-ledger-chip", name2)
        wait_apply_count(page, 1)
        apply_and_wait(page, 6, 0, 1)
        wait_selected(page, 0)
        run.check(
            "多选里选了账本一的账单，再把条件改成账本二：不在列表里的账单自动从已选里移除（「未选择」），仍在多选模式",
            tid(page, "bills-select-count").inner_text() == "未选择" and tid(page, "bills-batchbar").is_visible(),
            tid(page, "bills-select-count").inner_text(),
        )
        tid(page, "bills-select-toggle").click()
        tid(page, "bills-batchbar").wait_for(state="detached")

        # ---- 悬空的账本条件：账本在别处被删除后，刷新时自动移除 ----
        db.soft_delete_ledger(ledger2)
        pull_refresh(page, "bills-ptr")
        page.wait_for_function("() => document.querySelector('[data-testid=\"bills-chip-ledger\"]').dataset.active === 'false'")
        wait_summary(page, 69, 0, 3)
        run.check(
            "账本二被别处删除后下拉刷新：指向它的条件自动移除（标签恢复成「账本」），其余条件（分类）保留，列表按剩下的条件重算",
            chips_state(page)[0]["dimension"] == "category" and chips_state(page)[0]["text"] == "餐饮美食"
            and next(c for c in chips_state(page) if c["dimension"] == "ledger")["text"] == "账本" and list_is(page, 69, 0, 3) and badge_text(page) == "1",
            str(chips_state(page)),
        )
        open_filter_sheet(page)
        st = sheet_state(page)
        run.check("筛选 Sheet 的账本标签也不再有已删除的账本二", st["ledgerLabels"] == ["全部", name1, "未归属账本"], str(st["ledgerLabels"]))
        close_filter_sheet(page)

        run.check_clean(s.errors, "E3 节会话无页面异常 / 控制台错误 / 失败请求")


def check_shared_ledger(run: TestRun, db: LocalDb) -> None:
    """E4. 共享账本：筛选里能选共享账本，别人记的账单只读"""
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        owner = db.seed_user()
        own_name, shared_name = unique_name("我的账本"), unique_name("共享账本")
        own_id = db.seed_ledger(user_id, own_name)
        shared_id = db.seed_ledger(owner, shared_name)
        db.seed_ledger_member(shared_id, user_id, role=ROLE_EDITOR, status=MEMBER_ACTIVE)
        food = db.category_id_by_name("餐饮美食")
        mine = db.seed_bill(user_id, amount=35, ledger_id=own_id, note=unique_name("我的"), when=at(0, 12, 0), category_id=food)
        mine_in_shared = db.seed_bill(user_id, amount=20, ledger_id=shared_id, note=unique_name("我在共享里"), when=at(0, 11, 0), category_id=food)
        theirs = db.seed_bill(owner, amount=77, ledger_id=shared_id, note=unique_name("别人记的"), when=at(0, 10, 0), category_id=food)

        go_bills(page)
        wait_summary(page, 55, 0, 2)
        run.check("不筛选时只显示自己记的账单（共享账本里别人记的不混进总列表）", row_ids(page) == [mine.lower(), mine_in_shared.lower()], str(row_ids(page)))

        open_filter_sheet(page, via="ledger")
        st = sheet_state(page)
        run.check(
            "筛选 Sheet 的账本标签：全部 / 自己的账本 / 共享给我的账本（名称带「（共享）」）/ 未归属账本",
            st["ledgerLabels"] == ["全部", own_name, f"{shared_name}（共享）", "未归属账本"],
            str(st["ledgerLabels"]),
        )
        toggle_chip(page, "filter-ledger-chip", shared_name)
        wait_apply_count(page, 2)
        apply_and_wait(page, 97, 0, 2)
        rows = {r["id"]: r for r in page.evaluate(ROWS_JS)}
        run.check("按共享账本筛选：自己的和其他成员记的都能看到（20 + 77）", set(rows) == {mine_in_shared.lower(), theirs.lower()} and amount_of(page, "bills-total-expense") == "97.00", str(list(rows)))
        run.check("条件条上账本标签显示「共享账本名（共享）」", chips_state(page)[0]["text"] == f"{shared_name}（共享）", str(chips_state(page)[0]))
        run.check("自己记的账单可以左滑（编辑 / 删除）", rows[mine_in_shared.lower()]["swipeDisabled"] is None and rows[mine_in_shared.lower()]["swipeActions"] == 2)
        run.check("别人记的账单是只读：不能左滑、没有编辑 / 删除操作", rows[theirs.lower()]["swipeDisabled"] == "true" and rows[theirs.lower()]["swipeActions"] == 0, str(rows[theirs.lower()]))
        shot(page, "bills-shared-ledger")

        # ---- 点按只读账单：只是提示，不跳转、不登出 ----
        row_of(page, theirs).click()
        toast(page, "只有记录它的人可以修改").wait_for(state="visible")
        run.check("点按别人记的账单：提示「只有记录它的人可以修改或删除」", READ_ONLY_HINT in toast(page, "只有记录它的人可以修改").inner_text(), toast(page, "只有记录它的人可以修改").inner_text())
        run.check("这是提示而不是错误：停在账单页、没有跳转，也没有被登出（Tab 栏仍在）", page.evaluate("() => location.pathname") == "/bills" and tab_bar(page).is_visible() and bills_page(page).is_visible())

        # ---- 批量选择里置灰、全选不含它 ----
        tid(page, "bills-select-toggle").click()
        tid(page, "bills-batchbar").wait_for(state="visible")
        rows = {r["id"]: r for r in page.evaluate(ROWS_JS)}
        run.check("多选模式：别人记的账单整行置灰不可选，自己的可选", rows[theirs.lower()]["disabled"] and not rows[mine_in_shared.lower()]["disabled"], str(rows))
        tid(page, "bills-select-all").click()
        wait_selected(page, 1)
        rows = {r["id"]: r for r in page.evaluate(ROWS_JS)}
        run.check("全选只选自己的（已选 1 笔），不含别人记的", tid(page, "bills-select-count").inner_text() == "已选 1 笔" and rows[mine_in_shared.lower()]["checked"] == "true" and rows[theirs.lower()]["checked"] == "false", tid(page, "bills-select-count").inner_text())

        # ---- 共享账本不能作为归属目标：归属 Sheet 只列自己的账本 ----
        tid(page, "bills-assign").click()
        tid(page, "assign-sheet").wait_for(state="visible")
        wait_animations_settled(page)
        names = [o.inner_text().split("\n")[0].strip() for o in tid(page, "assign-ledger-option").all()]
        run.check("归属账本 Sheet 只列自己的账本（共享账本不能作为归属目标）", names == [own_name], str(names))
        tid(page, "assign-sheet").get_by_test_id("sheet-close").click()
        wait_sheet_gone(page, "assign-sheet")

        # ---- 只读成员（Viewer）：自己在里面记过的账单能看、能打开编辑（保存时才被拒绝），但不给删除，也不能批量归属 ----
        # （服务层同样以 403 拒绝，ADR 0004：角色在别处被降成只读后，那些历史账单不能再被我改走 / 删掉）
        tid(page, "bills-select-toggle").click()
        tid(page, "bills-batchbar").wait_for(state="detached")
        viewer_name = unique_name("只读账本")
        viewer_id = db.seed_ledger(owner, viewer_name)
        db.seed_ledger_member(viewer_id, user_id, role=ROLE_VIEWER, status=MEMBER_ACTIVE)
        mine_in_viewer = db.seed_bill(user_id, amount=11, ledger_id=viewer_id, note=unique_name("我在只读里"), when=at(0, 9, 0), category_id=food)
        theirs_in_viewer = db.seed_bill(owner, amount=5, ledger_id=viewer_id, note=unique_name("别人在只读里"), when=at(0, 8, 0), category_id=food)
        pull_refresh(page, "bills-ptr")
        open_filter_sheet(page, via="ledger")
        toggle_chip(page, "filter-ledger-chip", viewer_name)
        wait_apply_count(page, 2)
        apply_and_wait(page, 16, 0, 2)
        rows = {r["id"]: r for r in page.evaluate(ROWS_JS)}
        mine_row, theirs_row = rows[mine_in_viewer.lower()], rows[theirs_in_viewer.lower()]
        run.check(
            "【只读成员】我是只读成员的共享账本里：自己记的账单能左滑，但只露「编辑」、没有「删除」；别人记的仍是只读（不能左滑）",
            mine_row["swipeDisabled"] is None and mine_row["swipeActions"] == 1
            and swipe_of(page, mine_in_viewer).get_by_test_id("swipe-edit").count() == 1
            and swipe_of(page, mine_in_viewer).get_by_test_id("swipe-delete").count() == 0
            and theirs_row["swipeDisabled"] == "true" and theirs_row["swipeActions"] == 0,
            f"{mine_row['swipeDisabled']}/{mine_row['swipeActions']} {theirs_row['swipeDisabled']}/{theirs_row['swipeActions']}",
        )
        tid(page, "bills-select-toggle").click()
        tid(page, "bills-batchbar").wait_for(state="visible")
        rows = {r["id"]: r for r in page.evaluate(ROWS_JS)}
        run.check(
            "【只读成员】多选模式：我在只读账本里记的账单也置灰不可选（不能批量归属到别的账本），「全选」不可点",
            rows[mine_in_viewer.lower()]["disabled"] and rows[theirs_in_viewer.lower()]["disabled"] and tid(page, "bills-select-all").is_disabled(),
            str({k[:4]: v["disabled"] for k, v in rows.items()}),
        )
        tid(page, "bills-select-toggle").click()
        tid(page, "bills-batchbar").wait_for(state="detached")

        run.check_clean(s.errors, "E4 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- F. 批量选择与归属账本


def check_batch_assign(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_name, other_name = unique_name("归属账本"), unique_name("另一个账本")
        ledger_id = db.seed_ledger(user_id, ledger_name)
        other_id = db.seed_ledger(user_id, other_name)
        other = db.category_id_by_name("其他支出")
        own1 = db.seed_bill(user_id, amount=10, ledger_id=ledger_id, note=unique_name("已归属一"), when=at(0, 12, 0), category_id=other)
        db.seed_bill(user_id, amount=20, ledger_id=ledger_id, note=unique_name("已归属二"), when=at(0, 11, 0), category_id=other)
        old1 = db.seed_legacy_bill(user_id, amount=30, note=unique_name("历史一"), when=at(1, 12, 0), category_id=other)
        old2 = db.seed_legacy_bill(user_id, amount=40, note=unique_name("历史二"), when=at(1, 11, 0), category_id=other)
        # 两笔历史账单先标成「已同步」：归属账本是一次编辑，落库后应回到「待同步」
        db.set_bill_sync_status(old1, SYNC_SYNCED)
        db.set_bill_sync_status(old2, SYNC_SYNCED)

        go_bills(page)
        show_all_time(page, (100, 0, 4))  # 「昨天」的两笔历史账单月初会落到上个月：这一节看全部时间

        # ---- 进入多选 ----
        tid(page, "bills-select-toggle").click()
        tid(page, "bills-batchbar").wait_for(state="visible")
        wait_animations_settled(page)
        rows = page.evaluate(ROWS_JS)
        run.check("进入多选：每行变成复选框（role=checkbox，aria-checked=false），行首出现圆形勾选框", len(rows) == 4 and all(r["role"] == "checkbox" and r["checked"] == "false" and r["selected"] == "false" for r in rows), str([(r["role"], r["checked"]) for r in rows]))
        run.check(
            "批量操作栏：全选可点，清空 / 归属账本不可点，计数「未选择」；多选按钮变成「退出多选」且 aria-pressed=true",
            tid(page, "bills-select-all").is_enabled() and tid(page, "bills-select-clear").is_disabled() and tid(page, "bills-assign").is_disabled()
            and tid(page, "bills-select-count").inner_text() == "未选择"
            and tid(page, "bills-select-toggle").get_attribute("aria-label") == "退出多选" and tid(page, "bills-select-toggle").get_attribute("aria-pressed") == "true",
        )
        run.check("多选期间不能左滑（每个左滑行都被禁用）", all(r["swipeDisabled"] == "true" for r in rows), str([r["swipeDisabled"] for r in rows]))
        bar = page.evaluate(BATCHBAR_JS)
        tab_top = SCREEN_H - 21 - 62
        run.check("【操作栏】role=toolbar、读屏名「批量操作」；已选笔数是 aria-live=polite（选一笔播报一次）", bar["role"] == "toolbar" and bar["label"] == "批量操作" and bar["countLive"] == "polite", str(bar))
        run.check(
            "【操作栏】悬浮在 Tab 栏之上：fixed、高 56，底边离 Tab 栏顶端 ≥ 8，左右各留出边距",
            bar["position"] == "fixed" and approx(bar["height"], 56) and bar["bottom"] <= tab_top - 8 and bar["left"] >= 8 and bar["right"] <= SCREEN_W - 8,
            str(bar),
        )
        run.check("【操作栏】不是玻璃（玻璃层预算已用完），三个按钮的点击区域高 ≥ 44", bar["backdrop"] == "none" and all(b["height"] >= 44 for b in bar["buttons"]), str(bar))
        run.check(
            "【操作栏】页面根带 ft-bills--selecting，底部多留出操作栏的位置（最后一笔账单能滚到操作栏之上）",
            bar["selecting"] and bar["pagePadding"] >= SCREEN_H - bar["top"] - 1,
            str(bar),
        )
        glass = page.evaluate(GLASS_LAYERS_JS)
        run.check("多选模式同屏玻璃层数仍 ≤ 3（批量操作栏不是玻璃）", glass <= 3, str(glass))
        shot(page, "bills-select-mode")

        # ---- 逐行勾选、全选、清空 ----
        row_of(page, old1).click()
        wait_selected(page, 1)
        row_state = {r["id"]: r for r in page.evaluate(ROWS_JS)}
        run.check("点一行：勾选该行（aria-checked=true），计数「已选 1 笔」，清空 / 归属账本变可点", row_state[old1.lower()]["checked"] == "true" and tid(page, "bills-select-count").inner_text() == "已选 1 笔" and tid(page, "bills-assign").is_enabled() and tid(page, "bills-select-clear").is_enabled())
        row_of(page, old1).click()
        wait_selected(page, 0)
        run.check("再点一次取消勾选，回到「未选择」", tid(page, "bills-select-count").inner_text() == "未选择")

        tid(page, "bills-select-all").click()
        wait_selected(page, 4)
        run.check("全选：4 行都勾上，计数「已选 4 笔」", all(r["checked"] == "true" for r in page.evaluate(ROWS_JS)) and tid(page, "bills-select-count").inner_text() == "已选 4 笔")
        tid(page, "bills-select-clear").click()
        wait_selected(page, 0)
        run.check("清空：全部取消勾选", all(r["checked"] == "false" for r in page.evaluate(ROWS_JS)))

        # ---- 用「未归属账本」筛出历史账单，全选后归属（有两个自有账本：必须明确选一个）----
        open_filter_sheet(page, via="ledger")
        toggle_chip(page, "filter-ledger-unassigned")
        wait_apply_count(page, 2)
        apply_and_wait(page, 70, 0, 2)
        run.check("【未归属账本】筛选后只剩两笔历史账单，仍在多选模式（操作栏还在）", set(row_ids(page)) == {old1.lower(), old2.lower()} and tid(page, "bills-batchbar").is_visible(), str(row_ids(page)))
        tid(page, "bills-select-all").click()
        wait_selected(page, 2)
        tid(page, "bills-assign").click()
        sheet = tid(page, "assign-sheet")
        sheet.wait_for(state="visible")
        wait_animations_settled(page)
        options = tid(page, "assign-ledger-option")
        option_names = [o.inner_text().split("\n")[0].strip() for o in options.all()]
        run.check("「归属账本」打开 Sheet，说明已选笔数「将已选的 2 笔账单归属到：」", tid(page, "assign-hint").inner_text() == "将已选的 2 笔账单归属到：", tid(page, "assign-hint").inner_text())
        run.check(
            "Sheet 只列自己的账本（两个）；有多个账本时不预选，主按钮「归属 2 笔账单」置灰（账本必选）",
            sorted(option_names) == sorted([ledger_name, other_name]) and all(o.get_attribute("data-selected") == "false" for o in options.all())
            and tid(page, "assign-confirm").is_disabled() and tid(page, "assign-confirm").inner_text() == "归属 2 笔账单",
            str(option_names),
        )
        run.check("账本列表是单选组（role=radiogroup，每项 role=radio + aria-checked）", tid(page, "assign-ledger-list").get_attribute("role") == "radiogroup" and all(o.get_attribute("role") == "radio" and o.get_attribute("aria-checked") == "false" for o in options.all()))
        shot(page, "bills-assign-sheet")

        sheet.get_by_test_id("sheet-close").click()
        wait_sheet_gone(page, "assign-sheet")
        run.check("点 ✕ 收起 Sheet = 取消归属：已选的 2 笔仍保留，没有写库", tid(page, "bills-select-count").inner_text() == "已选 2 笔" and all(b["LedgerId"] is None for b in db.bills_of_user(user_id) if b["Id"] in (old1.upper(), old2.upper())))

        tid(page, "bills-assign").click()
        sheet.wait_for(state="visible")
        wait_animations_settled(page)
        run.check("再打开 Sheet：上次没提交的选择不保留（仍然没有预选）", all(o.get_attribute("data-selected") == "false" for o in tid(page, "assign-ledger-option").all()) and tid(page, "assign-confirm").is_disabled())
        other_option = sheet.get_by_test_id("assign-ledger-option").filter(has_text=other_name)
        target_option = sheet.get_by_test_id("assign-ledger-option").filter(has_text=ledger_name)
        other_option.click()
        expect(other_option).to_have_attribute("data-selected", "true", timeout=TIMEOUT_MS)
        target_option.click()
        expect(target_option).to_have_attribute("data-selected", "true", timeout=TIMEOUT_MS)
        expect(other_option).to_have_attribute("data-selected", "false", timeout=TIMEOUT_MS)
        states = {o.inner_text().split("\n")[0].strip(): o.get_attribute("data-selected") for o in tid(page, "assign-ledger-option").all()}
        run.check("先点「另一个账本」再点目标账本：单选，只有最后点的选中，主按钮变成可点", states == {ledger_name: "true", other_name: "false"} and tid(page, "assign-confirm").is_enabled(), str(states))

        tid(page, "assign-confirm").click()
        toast(page, f"已将 2 笔账单归属到「{ledger_name}」").wait_for(state="visible")
        wait_sheet_gone(page, "assign-sheet")
        by_id = {b["Id"]: b for b in db.bills_of_user(user_id)}
        run.check(
            "确认后两笔历史账单落库归属到目标账本并回到「待同步」，其余不变",
            by_id[old1.upper()]["LedgerId"] == ledger_id and by_id[old2.upper()]["LedgerId"] == ledger_id and by_id[own1.upper()]["LedgerId"] == ledger_id
            and by_id[old1.upper()]["SyncStatus"] == SYNC_PENDING and by_id[old2.upper()]["SyncStatus"] == SYNC_PENDING and len(by_id) == 4,
            str({k[:4]: (v["LedgerId"], v["SyncStatus"]) for k, v in by_id.items()}),
        )
        tid(page, "bills-empty").wait_for(state="visible")
        run.check(
            "筛选「未归属账本」的列表即时刷新：归属掉的两笔离开了列表（空状态），汇总归零，选择已清空，仍在多选模式",
            count_of(page) == 0 and tid(page, "bills-select-count").inner_text() == "未选择" and tid(page, "bills-batchbar").is_visible(),
            str(count_of(page)),
        )

        remove_chip(page, "ledger")
        wait_summary(page, 100, 0, 4)
        wait_rows(page, 4)
        rows = {r["id"]: r for r in page.evaluate(ROWS_JS)}
        run.check("移除账本条件后：这两行不再标注「未归属账本」，副标题变成目标账本", all(rows[b.lower()]["subtitle"].endswith(f"· {ledger_name}") for b in (old1, old2)), str([rows[b.lower()]["subtitle"] for b in (old1, old2)]))
        run.check("汇总不变（归属只改账本，不改金额）", amount_of(page, "bills-total-expense") == "100.00" and count_of(page) == 4)

        # ---- 只剩一个自有账本：打开即预选；没有自有账本：给出说明 ----
        # 在库里再放一笔标记账单：刷新时先重读账本列表再读账单，汇总里出现这笔 = 账本列表也已经是最新的
        db.soft_delete_ledger(other_id)
        db.seed_legacy_bill(user_id, amount=1, note=unique_name("标记一"), when=at(0, 8, 0), category_id=other)
        pull_refresh(page, "bills-ptr")
        wait_summary(page, 101, 0, 5)
        row_of(page, own1).click()
        wait_selected(page, 1)
        tid(page, "bills-assign").click()
        sheet.wait_for(state="visible")
        wait_animations_settled(page)
        options = tid(page, "assign-ledger-option")
        run.check(
            "只有一个自己的账本时打开即预选，主按钮「归属 1 笔账单」直接可点",
            options.count() == 1 and options.first.get_attribute("data-selected") == "true" and tid(page, "assign-confirm").is_enabled() and tid(page, "assign-confirm").inner_text() == "归属 1 笔账单",
            str(options.count()),
        )
        page.keyboard.press("Escape")
        wait_sheet_gone(page, "assign-sheet")
        run.check("按 Esc 收起 Sheet，选择保留", tid(page, "bills-select-count").inner_text() == "已选 1 笔")

        db.soft_delete_ledger(ledger_id)
        db.seed_legacy_bill(user_id, amount=2, note=unique_name("标记二"), when=at(0, 7, 0), category_id=other)
        pull_refresh(page, "bills-ptr")
        wait_summary(page, 103, 0, 6)
        tid(page, "bills-assign").click()
        sheet.wait_for(state="visible")
        wait_animations_settled(page)
        run.check(
            "一个自有账本都没有时：Sheet 里说明「还没有自己的账本」，没有选项，主按钮置灰",
            tid(page, "assign-no-ledger").is_visible() and "还没有自己的账本" in tid(page, "assign-no-ledger").inner_text()
            and tid(page, "assign-ledger-option").count() == 0 and tid(page, "assign-confirm").is_disabled(),
        )
        sheet.get_by_test_id("sheet-close").click()
        wait_sheet_gone(page, "assign-sheet")

        # ---- 退出多选（带着一个勾选退出，再进来应当是干净的）----
        tid(page, "bills-select-toggle").click()
        tid(page, "bills-batchbar").wait_for(state="detached")
        rows = page.evaluate(ROWS_JS)
        run.check("退出多选：批量操作栏消失，行不再是复选框，左滑恢复", all(r["role"] is None and r["swipeDisabled"] is None for r in rows) and tid(page, "bills-select-toggle").get_attribute("aria-label") == "多选", str([(r["role"], r["swipeDisabled"]) for r in rows]))
        tid(page, "bills-select-toggle").click()
        tid(page, "bills-batchbar").wait_for(state="visible")
        run.check("退出多选时选择一并清空：再进多选仍是「未选择」", tid(page, "bills-select-count").inner_text() == "未选择" and all(r["checked"] == "false" for r in page.evaluate(ROWS_JS)))

        run.check_clean(s.errors, "F 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- G. 分页


def check_pagination(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_id = db.seed_ledger(user_id, unique_name("分页账本"))
        other = db.category_id_by_name("其他支出")
        now = datetime.now()
        ids = []
        for i in range(45):
            ids.append(db.seed_bill(user_id, amount=1, ledger_id=ledger_id, note=unique_name(f"页{i:02d}"), when=now - timedelta(minutes=30 * i), category_id=other))
        newest_first = [i.lower() for i in ids]

        go_bills(page)
        show_all_time(page, (45, 0, 45))  # 45 笔跨越 22 个小时，月初会有一部分落到上个月：这一节看全部时间
        rows = [r["id"] for r in page.evaluate(ROWS_JS)]
        groups = page.evaluate(DAY_GROUPS_JS)
        run.check(f"第一页只加载 {PAGE_SIZE} 笔，按交易时间倒序", rows == newest_first[:PAGE_SIZE], str(len(rows)))
        run.check("汇总卡始终是全量（45 笔 / 45.00），不受分页影响", amount_of(page, "bills-total-expense") == "45.00" and count_of(page) == 45)
        run.check("当日合计基于已加载的账单（只统计已加载的 20 笔），与汇总卡的全量口径不同——这是刻意保留的差异", sum(Decimal(g["expense"]) for g in groups) == Decimal(PAGE_SIZE), str([g["expense"] for g in groups]))
        run.check("还有更多时显示「加载更多」", tid(page, "bills-load-more").is_visible() and tid(page, "bills-list").get_attribute("data-loaded-count") == str(PAGE_SIZE))

        tid(page, "bills-load-more").scroll_into_view_if_needed()
        before = scroll_root_state(page)["top"]
        tid(page, "bills-load-more").click()
        wait_rows(page, 2 * PAGE_SIZE)
        rows = [r["id"] for r in page.evaluate(ROWS_JS)]
        after = scroll_root_state(page)["top"]
        run.check("点「加载更多」追加下一页（40 笔）：顺序连续、没有重复", rows == newest_first[: 2 * PAGE_SIZE] and len(set(rows)) == len(rows), str(len(rows)))
        run.check("追加后滚动位置不跳回顶部", after >= before - 2, f"{before} -> {after}")
        run.check("汇总卡仍是全量；当日合计随已加载账单增长（40 笔）", amount_of(page, "bills-total-expense") == "45.00" and sum(Decimal(g["expense"]) for g in page.evaluate(DAY_GROUPS_JS)) == Decimal(2 * PAGE_SIZE))

        tid(page, "bills-load-more").scroll_into_view_if_needed()
        tid(page, "bills-load-more").click()
        wait_rows(page, 45)
        tid(page, "bills-load-more").wait_for(state="detached")
        rows = [r["id"] for r in page.evaluate(ROWS_JS)]
        run.check("最后一页加载完（45 笔）后「加载更多」消失，顺序与数据库一致", rows == newest_first and tid(page, "bills-load-more").count() == 0, str(len(rows)))
        run.check("全部加载后当日合计之和 = 汇总卡（45.00），两种口径重合", sum(Decimal(g["expense"]) for g in page.evaluate(DAY_GROUPS_JS)) == Decimal(45))
        shot(page, "bills-all-loaded")

        # ---- 删除后保持已加载页数 ----
        delete_via_swipe(page, ids[0])
        wait_summary(page, 44, 0, 44)
        wait_rows(page, 44)
        run.check("删除一笔后保持已加载的页数（仍是 44 笔全部在列表里），不退回第一页", len(page.evaluate(ROWS_JS)) == 44 and tid(page, "bills-load-more").count() == 0)

        run.check_clean(s.errors, "G 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- H. 减弱动态效果


def check_reduced_motion(run: TestRun, db: LocalDb) -> None:
    with session(reduced_motion=True) as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_id = db.seed_ledger(user_id, unique_name("减弱账本"))
        other = db.category_id_by_name("其他支出")
        db.seed_bill(user_id, amount=12, ledger_id=ledger_id, note=unique_name("减弱"), when=at(0, 10, 0), category_id=other)
        db.seed_bill(user_id, amount=34, ledger_id=ledger_id, bill_type=BILL_TYPE_INCOME, note=unique_name("减弱收入"), when=at(0, 9, 0), category_id=db.category_id_by_name("工资薪酬", BILL_TYPE_INCOME))

        go_bills(page)
        wait_summary(page, 12, 34, 2)
        seg = page.evaluate(SEGMENTS_JS)
        durations = [float(part.strip().rstrip("s")) for part in seg["thumbDuration"].split(",")]
        run.check("减弱动态效果：分段滑块不做滑动过渡（时长 ≈ 0）", all(d <= 0.001 for d in durations), seg["thumbDuration"])

        tid(page, "bills-segment").get_by_test_id("segment").filter(has_text="收入").click()
        wait_summary(page, 0, 34, 1)
        wait_animations_settled(page)  # 过渡 ≈ 0.01 ms：下一帧就位
        seg = page.evaluate(SEGMENTS_JS)
        run.check("减弱动态效果：切换分段后滑块立刻就位在第三段下面", seg["value"] == "Income" and thumb_under(seg, 2), f"{seg['thumbCenter']} vs {seg['items'][2]['center']}")

        title_transform = page.evaluate("() => getComputedStyle(document.querySelector('[data-testid=\"bills-page\"] [data-testid=\"large-nav-title\"]')).transform")
        run.check("减弱动态效果：折叠小标题不做位移（只淡入淡出）", title_transform in ("none", "matrix(1, 0, 0, 1, 0, 0)") or "matrix" in title_transform, title_transform)

        chip_transition = page.evaluate("() => getComputedStyle(document.querySelector('[data-testid=\"bills-chip-time\"]'), '::before').transitionDuration")
        chip_durations = [float(part.strip().rstrip("s")) for part in chip_transition.split(",")]
        run.check("减弱动态效果：条件标签的按下 / 选中高亮不做过渡（时长 ≈ 0）", all(d <= 0.001 for d in chip_durations), chip_transition)

        run.check_clean(s.errors, "H 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- I. 搜索（10 号）

#: 搜索测试的账单清单（格式同 BILL_SPECS；备注为 None = 没有备注）。刻意安排的碰撞：
#: 金额 35 既是「麦当劳午餐」的金额、又出现在「京东35周年购物」的备注里；35.50 与 35 不相等；18.50 的账单没有备注，只能靠分类名搜到；
#: 「Starbucks咖啡」的备注含大写字母，用来验证英文不区分大小写
SEARCH_SPECS = [
    ("mcd", 0, 12, 8, "35", False, "麦当劳午餐", "餐饮美食", True),
    ("star", 0, 9, 32, "28", False, "Starbucks咖啡", "餐饮美食", True),
    ("metro", 0, 8, 41, "6", False, "地铁通勤", "交通出行", True),
    ("jd", 1, 20, 15, "299", False, "京东35周年购物", "购物消费", True),
    ("salary", 1, 10, 0, "12000", True, "十一月工资", "工资薪酬", True),
    ("nonote", 5, 19, 30, "18.50", False, None, "餐饮美食", True),
    ("legacy", 6, 14, 0, "35.50", False, "老账单", "其他支出", False),
]
SEARCH_KEYS = [spec[0] for spec in SEARCH_SPECS]

SEARCH_STATE_JS = """() => {
  const root = document.querySelector('[data-testid="bills-page"]');
  const field = root.querySelector('[data-testid="bills-search"]');
  const input = root.querySelector('[data-testid="bills-search-input"]');
  const clear = root.querySelector('[data-testid="bills-search-clear"]');
  const status = root.querySelector('[data-testid="bills-search-status"]');
  const seg = root.querySelector('[data-testid="bills-segment"]');
  const title = root.querySelector('[data-testid="large-title"]');
  const rect = (el) => { const r = el.getBoundingClientRect(); return { left: r.left, right: r.right, top: r.top, bottom: r.bottom, width: r.width, height: r.height }; };
  const fcs = getComputedStyle(field);
  const ics = getComputedStyle(input);
  return {
    role: field.getAttribute('role'),
    field: rect(field), input: rect(input), clear: rect(clear), seg: rect(seg), title: rect(title),
    fieldBackdrop: fcs.backdropFilter || fcs.webkitBackdropFilter || 'none',
    fieldBackground: fcs.backgroundColor,
    fieldRadius: parseFloat(fcs.borderTopLeftRadius),
    inputType: input.type,
    inputLabel: input.getAttribute('aria-label'),
    placeholder: input.placeholder,
    inputmode: input.getAttribute('inputmode'),
    enterkeyhint: input.getAttribute('enterkeyhint'),
    inputFont: parseFloat(ics.fontSize),
    value: input.value,
    focused: document.activeElement === input,
    clearDisplay: getComputedStyle(clear).display,
    clearLabel: clear.getAttribute('aria-label'),
    clearTag: clear.tagName,
    clearType: clear.getAttribute('type'),
    statusText: status.textContent.trim(),
    statusRole: status.getAttribute('role'),
    statusLive: status.getAttribute('aria-live'),
    iconHidden: field.querySelector('.ft-search__icon').getAttribute('aria-hidden'),
  };
}"""

#: 在搜索框里计数 change 事件（脚本派发的与浏览器自己派发的都算）
SEARCH_COUNT_CHANGES_JS = """() => {
  window.__searchChanges = 0;
  document.querySelector('[data-testid="bills-search-input"]').addEventListener('change', () => { window.__searchChanges += 1; });
}"""

#: 不经过键盘、直接在页面里模拟「输入后立刻按回车」：脚本在同一个同步调用里读到计数，证明回车是立即提交、没有等停顿计时
SEARCH_ENTER_JS = """(text) => {
  const input = document.querySelector('[data-testid="bills-search-input"]');
  input.focus();
  window.__searchChanges = 0;
  input.value = text;
  input.dispatchEvent(new InputEvent('input', { bubbles: true, data: text, inputType: 'insertText' }));
  input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
  return { changes: window.__searchChanges, focused: document.activeElement === input, value: input.value };
}"""

#: 模拟输入法组词：compositionstart → 带 isComposing 的 input（拼音还没选字）
SEARCH_COMPOSE_JS = """(pinyin) => {
  const input = document.querySelector('[data-testid="bills-search-input"]');
  input.focus();
  window.__searchChanges = 0;
  input.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true }));
  input.value = pinyin;
  input.dispatchEvent(new InputEvent('input', { bubbles: true, data: pinyin, inputType: 'insertCompositionText', isComposing: true }));
}"""

#: 组词期间按回车（确认候选字，不是提交搜索）
SEARCH_COMPOSING_ENTER_JS = """() => {
  const input = document.querySelector('[data-testid="bills-search-input"]');
  input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', keyCode: 229, bubbles: true, cancelable: true, isComposing: true }));
  return { changes: window.__searchChanges, focused: document.activeElement === input };
}"""

#: 选定候选字、组词结束
SEARCH_COMPOSE_END_JS = """(text) => {
  const input = document.querySelector('[data-testid="bills-search-input"]');
  input.value = text;
  input.dispatchEvent(new InputEvent('input', { bubbles: true, data: text, inputType: 'insertCompositionText', isComposing: true }));
  input.dispatchEvent(new CompositionEvent('compositionend', { bubbles: true, data: text }));
}"""


def sums_of(keys) -> tuple:
    """SEARCH_SPECS 里这些账单的（支出, 收入, 笔数）：与汇总卡的三个数一一对应。"""
    return sums([Spec(*raw) for raw in SEARCH_SPECS if raw[0] in keys])


def search_input(page):
    return tid(page, "bills-search-input")


def search_state(page) -> dict:
    return page.evaluate(SEARCH_STATE_JS)


def wait_results(page, keys) -> None:
    """等汇总卡与已加载的行数都落到这些账单的合计。"""
    wait_summary(page, *sums_of(keys))
    wait_rows(page, min(len(keys), PAGE_SIZE))


def check_search(run: TestRun, db: LocalDb) -> None:
    """I1. 搜索框的外观与语义、备注 / 分类 / 金额匹配、与分段 / 筛选 Sheet / 条件条叠加、提交时机（停顿 / 回车 / 输入法组词 / 清除）、
    空状态、切 Tab 保留、删除后保持关键字、长度上限"""
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_id = db.seed_ledger(user_id, unique_name("搜索账本"))
        bills = seed_bills(db, user_id, ledger_id, SEARCH_SPECS)
        by_id = {v.lower(): k for k, v in bills.items()}

        def shown() -> list:
            return sorted(by_id.get(i, i) for i in row_ids(page))

        def is_exactly(keys) -> bool:
            return shown() == sorted(keys) and list_is(page, *sums_of(keys))

        go_bills(page)
        show_all_time(page, sums_of(SEARCH_KEYS))  # 搜索的账单跨 6 天，月初会落到上个月：这一节看全部时间（搜索本身不依赖时间范围）

        # ---------------------------------------------------------------- 外观与语义
        st = search_state(page)
        run.check(
            "搜索框在大标题之下、分段控件之上，左右与分段控件对齐（同在页面边距之内）",
            st["title"]["bottom"] <= st["field"]["top"] + 1 and st["field"]["bottom"] <= st["seg"]["top"]
            and approx(st["field"]["left"], st["seg"]["left"]) and approx(st["field"]["width"], st["seg"]["width"]),
            f"title={st['title']} field={st['field']} seg={st['seg']}",
        )
        run.check("搜索框整条 ≥ 44 高（点击热区）；输入框字号 16px（iOS Safari 小于 16px 聚焦时会放大页面）", st["field"]["height"] >= 44 and st["inputFont"] == 16, f"{st['field']['height']} {st['inputFont']}")
        run.check(
            "搜索框是灰色填充（--ft-fill）的 18px 圆角输入框，自己不用 backdrop-filter（不占玻璃层预算）",
            st["fieldBackground"] == "rgba(120, 120, 128, 0.12)" and st["fieldRadius"] == 18 and st["fieldBackdrop"] == "none",
            f"{st['fieldBackground']} {st['fieldRadius']} {st['fieldBackdrop']}",
        )
        run.check(
            "语义：容器 role=search；输入框 type=search、aria-label「搜索账单」、占位「搜索备注、分类或金额」、enterkeyhint / inputmode = search；放大镜是装饰（aria-hidden）",
            st["role"] == "search" and st["inputType"] == "search" and st["inputLabel"] == "搜索账单" and st["placeholder"] == "搜索备注、分类或金额"
            and st["enterkeyhint"] == "search" and st["inputmode"] == "search" and st["iconHidden"] == "true",
            str({k: st[k] for k in ("role", "inputType", "inputLabel", "placeholder", "enterkeyhint", "inputmode", "iconHidden")}),
        )
        run.check(
            "没有内容时不显示清除按钮；读屏状态区是 role=status（polite）且为空",
            st["clearDisplay"] == "none" and not tid(page, "bills-search-clear").is_visible() and st["statusRole"] == "status" and st["statusLive"] == "polite" and st["statusText"] == "",
            f"{st['clearDisplay']} {st['statusRole']} {st['statusLive']} {st['statusText']!r}",
        )
        run.check("没有关键字时账单页是完整列表（7 笔），搜索框是空的", is_exactly(SEARCH_KEYS) and st["value"] == "")

        # ---------------------------------------------------------------- 匹配规则（相邻两项的期望不同，等汇总变化才能确认这一次的结果已经落地）
        cases = [
            ("备注片段", "咖啡", ["star"]),
            ("分类名称（含没有备注的账单）", "餐饮", ["mcd", "star", "nonote"]),
            ("英文不区分大小写：小写搜到备注里的大写 S", "starbucks", ["star"]),
            ("整体是数字：金额相等的账单 + 备注里含这个数字的账单；35.50 不算", "35", ["mcd", "jd"]),
            ("英文不区分大小写：大写", "STARBUCKS", ["star"]),
            ("带小数的金额", "35.5", ["legacy"]),
            ("金额 18.5 匹配存成 18.50 的账单（没有备注）", "18.5", ["nonote"]),
            ("收入账单的备注", "工资", ["salary"]),
            ("首尾空白被忽略", "  咖啡  ", ["star"]),
        ]
        for label, text, keys in cases:
            search_input(page).fill(text)
            wait_results(page, keys)
            run.check(f"搜索「{text.strip()}」（{label}）：列表恰好是 {keys}，汇总卡同一口径", is_exactly(keys), f"{shown()}")

        st = search_state(page)
        run.check(
            "有内容时出现清除按钮（≥ 44 × 44）；读屏状态区播报结果笔数",
            st["clearDisplay"] != "none" and tid(page, "bills-search-clear").is_visible() and st["clear"]["width"] >= 44 and st["clear"]["height"] >= 44
            and st["clearLabel"] == "清除搜索" and st["clearTag"] == "BUTTON" and st["clearType"] == "button" and st["statusText"] == "找到 1 笔账单",
            f"{st['clear']} {st['statusText']!r}",
        )
        shot(page, "bills-search-active")

        # ---------------------------------------------------------------- 清除按钮：清空、保持焦点、立即还原
        tid(page, "bills-search-clear").click()
        wait_results(page, SEARCH_KEYS)
        st = search_state(page)
        run.check("点清除按钮：输入框清空、焦点留在输入框（软键盘不收起，可以接着输入）、列表还原为全部 7 笔", st["value"] == "" and st["focused"] and is_exactly(SEARCH_KEYS), f"{st['value']!r} focused={st['focused']}")
        run.check("清空之后清除按钮又消失，状态区的播报清空", st["clearDisplay"] == "none" and st["statusText"] == "", f"{st['clearDisplay']} {st['statusText']!r}")

        # ---------------------------------------------------------------- 提交时机一：停顿（连续敲字合并成一次提交）
        page.evaluate(SEARCH_COUNT_CHANGES_JS)
        search_input(page).press_sequentially("starbucks", delay=60)
        wait_results(page, ["star"])
        page.wait_for_timeout(600)
        changes = page.evaluate("() => window.__searchChanges")
        run.check("逐字敲「starbucks」（每个字间隔 60 ms，共 9 个键）：停顿后只提交一次，不逐字查库", changes == 1 and is_exactly(["star"]), f"changes={changes}")

        # ---------------------------------------------------------------- 提交时机二：回车立即提交并收起软键盘
        entered = page.evaluate(SEARCH_ENTER_JS, "工资")
        wait_results(page, ["salary"])
        run.check("回车（软键盘的「搜索」键）：同步派发提交、不等停顿计时，并让输入框失焦（收起软键盘）", entered["changes"] >= 1 and not entered["focused"] and entered["value"] == "工资" and is_exactly(["salary"]), str(entered))

        # ---------------------------------------------------------------- 提交时机三：输入法组词期间不提交
        page.evaluate(SEARCH_COMPOSE_JS, "kafei")
        page.wait_for_timeout(700)  # 超过停顿时长：若组词期间也提交，这里早已提交
        composing = page.evaluate("() => ({ changes: window.__searchChanges, value: document.querySelector('[data-testid=\"bills-search-input\"]').value })")
        run.check("输入法组词中（拼音「kafei」还没选字）：停顿再久也不提交，列表保持原样", composing["changes"] == 0 and is_exactly(["salary"]) and composing["value"] == "kafei", str(composing))
        confirm = page.evaluate(SEARCH_COMPOSING_ENTER_JS)
        run.check("组词期间按回车（确认候选字）不是提交搜索：不派发提交、输入框保持焦点", confirm["changes"] == 0 and confirm["focused"], str(confirm))
        page.evaluate(SEARCH_COMPOSE_END_JS, "咖啡")
        wait_results(page, ["star"])
        run.check("选定候选字、组词结束后：停顿再提交，列表变成「咖啡」的结果", is_exactly(["star"]) and search_state(page)["value"] == "咖啡")
        tid(page, "bills-search-clear").click()
        wait_results(page, SEARCH_KEYS)

        # ---------------------------------------------------------------- 与分段叠加；空状态（关键字 + 筛选条件）
        search_input(page).fill("餐饮")
        wait_results(page, ["mcd", "star", "nonote"])
        st = search_state(page)
        run.check(
            "关键字不是筛选维度：不计入角标数、条件条上没有它的标签（四颗标签都没有条件）",
            badge_text(page) is None and all(c["active"] == "false" for c in chips_state(page)) and st["statusText"] == "找到 3 笔账单",
            f"badge={badge_text(page)} {chips_state(page)}",
        )
        segment_button(page, "收入").click()
        wait_results(page, [])
        tid(page, "bills-empty").wait_for(state="visible")
        empty_title = tid(page, "bills-empty").locator(".ft-empty__title").inner_text()
        empty_text = tid(page, "bills-empty").locator(".ft-empty__text").inner_text()
        clear_text = tid(page, "bills-clear-filter").inner_text().strip()
        run.check(
            "叠加分段（收入）后没有符合的账单：空状态是「没有找到相关账单」，提示清除搜索与筛选，按钮文案与之对应；汇总卡归零",
            empty_title == "没有找到相关账单" and "清除搜索与筛选" in empty_text and clear_text == "清除搜索与筛选" and list_is(page, 0, 0, 0)
            and search_state(page)["statusText"] == "没有找到相关账单",
            f"{empty_title!r} {empty_text!r} {clear_text!r}",
        )
        tid(page, "bills-clear-filter").click()
        wait_results(page, SEARCH_KEYS)
        st = search_state(page)
        run.check(
            "点空状态里的「清除搜索与筛选」：关键字、账单类型一起清掉，搜索框清空、分段回到「全部」，列表还原",
            st["value"] == "" and tid(page, "bills-segment").get_attribute("data-value") == "All" and is_exactly(SEARCH_KEYS) and tid(page, "bills-empty").count() == 0,
            f"{st['value']!r} {tid(page, 'bills-segment').get_attribute('data-value')}",
        )

        # ---------------------------------------------------------------- 与筛选 Sheet / 条件条叠加
        search_input(page).fill("餐饮")
        wait_results(page, ["mcd", "star", "nonote"])
        open_filter_sheet(page)
        toggle_chip(page, "filter-time-today")
        wait_apply_count(page, 2)
        apply_and_wait(page, 63, 0, 2)
        run.check("关键字「餐饮」+ 时间「今天」：两个条件取交集（今天的麦当劳午餐与咖啡），角标只数时间一项", is_exactly(["mcd", "star"]) and badge_text(page) == "1", f"{shown()} {badge_text(page)}")

        open_filter_sheet(page)
        initial_count = tid(page, "filter-apply").get_attribute("data-count")
        toggle_chip(page, "filter-category-chip", "交通出行")
        wait_apply_count(page, 0)
        apply_text = tid(page, "filter-apply").inner_text().strip()
        run.check(
            "筛选 Sheet 底部的「查看 N 笔账单」带着关键字算：打开时是当前结果 2 笔；把分类改成交通出行（今天有一笔地铁，但不含「餐饮」）后是 0 笔，按钮照常可点",
            initial_count == "2" and apply_text == "查看 0 笔账单" and tid(page, "filter-apply").is_enabled(),
            f"initial={initial_count} text={apply_text!r}",
        )
        close_filter_sheet(page)
        run.check("放弃 Sheet 草稿后列表与搜索框都保持原样", is_exactly(["mcd", "star"]) and search_state(page)["value"] == "餐饮")

        remove_chip(page, "time")
        wait_results(page, ["mcd", "star", "nonote"])
        run.check("移除时间条件标签：只去掉时间，关键字还在（搜索框里仍是「餐饮」），结果回到 3 笔", is_exactly(["mcd", "star", "nonote"]) and search_state(page)["value"] == "餐饮" and badge_text(page) is None)

        # ---------------------------------------------------------------- 切 Tab 保留
        open_tab(page, "首页")
        open_tab(page, "账单")
        wait_bills_loaded(page)
        run.check("【常驻】切到首页再切回：搜索框里的关键字与对应的结果都保留", search_state(page)["value"] == "餐饮" and is_exactly(["mcd", "star", "nonote"]), f"{search_state(page)['value']!r} {shown()}")

        # ---------------------------------------------------------------- 长度上限（放在删除之前：删除会弹 Toast，Toast 本身就是一层玻璃，会让下面的玻璃层计数多 1）
        tid(page, "bills-search-clear").click()
        wait_results(page, SEARCH_KEYS)
        search_input(page).press_sequentially("a" * 60)
        tid(page, "bills-empty").wait_for(state="visible")
        st = search_state(page)
        run.check("长度上限 50：键入 60 个字符只保留 50 个", len(st["value"]) == 50, str(len(st["value"])))
        glass = page.evaluate(GLASS_LAYERS_JS)
        run.check(
            "长关键字不撑破布局（没有横向溢出），页面同屏玻璃层仍 ≤ 3（搜索框本身不占玻璃层）",
            page.evaluate("() => document.documentElement.scrollWidth - document.documentElement.clientWidth") <= 0 and glass <= 3,
            f"glass={glass}",
        )
        tid(page, "bills-search-clear").click()
        wait_results(page, SEARCH_KEYS)
        search_input(page).fill("餐饮")
        wait_results(page, ["mcd", "star", "nonote"])

        # ---------------------------------------------------------------- 删除一笔后保持关键字
        delete_via_swipe(page, bills["star"])
        remaining = ["mcd", "nonote"]
        wait_summary(page, Decimal("53.50"), 0, 2)
        wait_rows(page, 2)
        run.check("搜索结果里左滑删除一笔：列表与汇总立即更新，关键字保持（搜索框里仍是「餐饮」）", shown() == sorted(remaining) and amount_of(page, "bills-total-expense") == "53.50" and search_state(page)["value"] == "餐饮", f"{shown()} {amount_of(page, 'bills-total-expense')}")

        # ---------------------------------------------------------------- 空状态（只有关键字）
        search_input(page).fill("没有这个账单")
        wait_summary(page, 0, 0, 0)
        tid(page, "bills-empty").wait_for(state="visible")
        empty_title = tid(page, "bills-empty").locator(".ft-empty__title").inner_text()
        empty_text = tid(page, "bills-empty").locator(".ft-empty__text").inner_text()
        clear_text = tid(page, "bills-clear-filter").inner_text().strip()
        run.check(
            "只有关键字、没有搜到：空状态是「没有找到相关账单」，只提示换关键字 / 清除搜索（不提筛选），按钮是「清除搜索」",
            empty_title == "没有找到相关账单" and empty_text == "换个关键字试试，或清除搜索" and clear_text == "清除搜索",
            f"{empty_title!r} {empty_text!r} {clear_text!r}",
        )
        tid(page, "bills-clear-filter").click()
        # 删掉了「Starbucks咖啡」，剩下 6 笔
        left = [k for k in SEARCH_KEYS if k != "star"]
        wait_results(page, left)
        st = search_state(page)
        run.check("点「清除搜索」：搜索框清空、列表还原为剩下的全部 6 笔，空状态消失", st["value"] == "" and is_exactly(left) and tid(page, "bills-empty").count() == 0, f"{st['value']!r} {shown()}")

        # ---------------------------------------------------------------- 只有空白不算关键字
        search_input(page).fill("   ")
        page.wait_for_timeout(700)
        st = search_state(page)
        run.check("只输入空格不算关键字：不查询、列表不变、不出现空状态、不播报", is_exactly(left) and tid(page, "bills-empty").count() == 0 and st["statusText"] == "", f"{shown()} {st['statusText']!r}")
        tid(page, "bills-search-clear").click()
        page.wait_for_timeout(100)
        st = search_state(page)
        run.check("清除按钮清掉空格：输入框为空、列表仍是剩下的 6 笔", st["value"] == "" and is_exactly(left), f"{st['value']!r}")

        run.check_clean(s.errors, "I1 节会话无页面异常 / 控制台错误 / 失败请求")


def check_search_paging(run: TestRun, db: LocalDb) -> None:
    """I2. 搜索结果分页（加载更多沿用关键字、汇总是全量）；% 与 _ 按字面匹配，不当通配符"""
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_id = db.seed_ledger(user_id, unique_name("搜索分页账本"))
        food = db.category_id_by_name("餐饮美食")
        now = datetime.now()
        for i in range(25):
            db.seed_bill(user_id, amount=2, ledger_id=ledger_id, note=f"批量{i:02d}", when=now - timedelta(minutes=10 * i), category_id=food)
        percent = db.seed_bill(user_id, amount=12, ledger_id=ledger_id, note="100%纯果汁", when=now - timedelta(minutes=400), category_id=food)
        underscore = db.seed_bill(user_id, amount=7, ledger_id=ledger_id, note="a_b折扣", when=now - timedelta(minutes=410), category_id=food)
        db.seed_bill(user_id, amount=3, ledger_id=ledger_id, note="axb对照", when=now - timedelta(minutes=420), category_id=food)

        go_bills(page)
        show_all_time(page, (25 * 2 + 12 + 7 + 3, 0, 28))  # 这些账单跨 7 个小时，凌晨在月初跑时会落到上个月：看全部时间

        search_input(page).fill("批量")
        wait_summary(page, 50, 0, 25)
        wait_rows(page, PAGE_SIZE)
        run.check("搜索「批量」（25 笔）：第一页 20 笔，汇总卡是全量（25 笔 / 50.00），还有「加载更多」", len(row_ids(page)) == PAGE_SIZE and count_of(page) == 25 and tid(page, "bills-load-more").is_visible())

        tid(page, "bills-load-more").scroll_into_view_if_needed()
        tid(page, "bills-load-more").click()
        wait_rows(page, 25)
        tid(page, "bills-load-more").wait_for(state="detached")
        rows = row_ids(page)
        run.check("点「加载更多」：追加的仍是「批量」的结果（共 25 笔，不重复、不混进其他账单），「加载更多」消失", len(rows) == 25 and len(set(rows)) == 25 and percent.lower() not in rows and underscore.lower() not in rows, str(len(rows)))
        run.check("搜索框里的关键字在翻页后保持", search_state(page)["value"] == "批量")

        search_input(page).fill("%")
        wait_summary(page, 12, 0, 1)
        wait_rows(page, 1)
        run.check("搜索「%」只匹配备注里真有 % 的账单（100%纯果汁），不当通配符匹配全部", row_ids(page) == [percent.lower()], str(row_ids(page)))

        search_input(page).fill("_")
        wait_summary(page, 7, 0, 1)
        wait_rows(page, 1)
        run.check("搜索「_」只匹配备注里真有 _ 的账单（a_b折扣），不把 _ 当成「任意一个字符」而匹配到 axb对照", row_ids(page) == [underscore.lower()], str(row_ids(page)))

        run.check_clean(s.errors, "I2 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- 入口


def main() -> int:
    require_server()
    run = TestRun("bills-page")
    db = LocalDb()
    db.verify_schema()
    db.delete_pending_bills()

    try:
        check_new_user(run, db)
        check_data_list(run, db)
        check_swipe_and_delete(run, db)
        check_refresh(run, db)
        check_filter_sheet(run, db)
        check_filter_dimensions(run, db)
        check_filter_persistence(run, db)
        check_shared_ledger(run, db)
        check_batch_assign(run, db)
        check_search(run, db)
        check_search_paging(run, db)
        check_pagination(run, db)
        check_reduced_motion(run, db)
    finally:
        db.delete_pending_bills()

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
