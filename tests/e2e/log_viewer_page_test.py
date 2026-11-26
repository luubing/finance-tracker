# -*- coding: utf-8 -*-
"""
E2E 测试：错误日志页（iOS UI 重构 · 22 号 ticket）——二级页面模板 + 设置式分组列表；筛选 / 刷新 / 导出 / 清除收进右上角「⋯」菜单；点一行弹出半屏详情 Sheet。

数据来自 Serilog 写下的日志文件（app-yyyyMMdd.log，每行一条 CompactJson）。本脚本不碰开发机上真实的日志目录：必须用 tests/e2e/server.ps1 启动 Web.Server
（它设置 E2E__LogDirectory，日志页读取的就是那个专用目录），每个小节用 e2e_base.LogFiles 按应用真实的文件格式造数据。

  A. 空状态与「⋯」菜单：没有日志时的好消息文案 / 没有概览与搜索框；⋯ 圆钮的读屏属性；菜单四项与位置（在 ⋯ 下方右缘对齐）、没日志时导出 / 清除置灰、
     清除为红色；Esc / 点外面收起；刷新能读到新写入的日志（空状态 → 列表）；没有日志时也能打开筛选 Sheet（只有 Fatal / Error 两个级别）
  B. 概览、列表与分组：总数 / 今日 / 本周三栏、级别分布、时间范围；按天分组（今天 / 昨天 / 更早；去年的带年份）、组头条数；行的标题（模板代入属性后的消息）/ 副标题 /
     读屏名称；时间倒序；损坏行被跳过；没有来源的行；底部「共 N 条」；玻璃层 ≤ 3
  C. 加载更多：每页 20 条（20 → 40 → 45），进度文字，跨页的同一天合并为一组
  D. 搜索：消息 / 异常类型 / 来源 / 完整信息，不区分大小写，首尾空格忽略；结果条数给读屏播报；清除按钮；无结果的空状态与「清除搜索」；概览口径不受搜索影响
  E. 筛选 Sheet 与条件条：级别（只列出现过的 + Fatal / Error）、起止日期（含当天）、起止颠倒时提示并置灰「查看日志」、重置、放弃改动（✕ / 遮罩 / Esc）；
     条件标签（调整 / 单独移除 / 清除条件）；搜索 + 条件组合与空状态里的「清除搜索与筛选」
  F. 详情 Sheet 与复制：时间含毫秒、来源、异常类型、消息、完整异常信息、上下文；没有异常 / 没有来源的行；复制的剪贴板内容；关闭后内容清掉
  G. 导出：写到缓存目录并提示路径；JSON 里是全部日志（不止第一页）、消息不为空、没有界面标记字段；只按日期范围（日期条件生效）
  H. 清除：确认 Alert（红色破坏性、写明条数）、取消 / Esc 不删、确认后只删 app-*.log（目录里别的文件还在）、回到空状态并复位搜索与条件
  I. 入口、导航与沉浸式：从「我的」进入 / 返回键与浏览器返回；Tab 栏隐藏；模拟刘海与手势条时导航栏与内容的让位
  J. 窄屏（320）：长消息省略不撑破、行与导航不重叠、详情 Sheet 不溢出

可观察属性（已登记到 README）：logs-page、logs-more、logs-menu(-layer)、logs-menu-{filter|refresh|export|clear}、logs-skeleton、logs-empty、logs-empty-clear、logs-summary、
logs-total / logs-today / logs-week（data-count）、logs-levels、logs-range、logs-search(-input|-clear|-status)、logs-chips、logs-chip-{level|time|clear}、logs-list（data-loaded-count / data-total）、
logs-day（data-date）、logs-day-title、logs-day-count、log-row（data-id / data-level）、logs-footnote、logs-load-more、logs-clear-alert、log-filter-*、log-detail-*、log-copy。
"""
import json
import re
import sys
from datetime import date, datetime, timedelta
from pathlib import Path

from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

from e2e_base import (
    GLASS_LAYERS_JS,
    BASE_URL,
    PHONE_VIEWPORT,
    LogFiles,
    TestRun,
    log_entry,
    login,
    open_app,
    open_tab,
    require_server,
    session,
    shot,
    tab_bar,
    wait_animations_settled,
    wait_attr,
    wait_secondary_pages_closed,
    wait_tab_bar,
    wait_url,
)

SCREEN_W = PHONE_VIEWPORT["width"]
SCREEN_H = PHONE_VIEWPORT["height"]

RGB_DANGER_TEXT = "rgb(211, 34, 26)"  # --ft-danger-text #D3221A

WEEKDAYS = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"]  # date.weekday()：周一 = 0

MENU_LABELS = ["筛选", "刷新", "导出日志", "清除日志"]

# ---------------------------------------------------------------------------------------------- 页面状态

STATE_JS = r"""() => {
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  const qa = (id) => [...document.querySelectorAll(`[data-testid="${id}"]`)];
  const box = (el) => { if (!el) return null; const r = el.getBoundingClientRect(); return { l: r.left, r: r.right, t: r.top, b: r.bottom, w: r.width, h: r.height }; };
  const text = (el) => (el ? el.textContent.replace(/\s+/g, ' ').trim() : null);
  const attr = (el, name) => (el ? el.getAttribute(name) : null);
  const root = q('logs-page');
  const list = q('logs-list');
  const empty = q('logs-empty');
  const input = q('logs-search-input');
  const more = q('logs-more');
  return {
    path: location.pathname,
    exists: !!root,
    skeleton: !!q('logs-skeleton'),
    failed: !!q('logs-failed'),
    title: text(q('nav-title')),
    overflowX: document.documentElement.scrollWidth - window.innerWidth,
    statusbarIcons: document.documentElement.getAttribute('data-statusbar-icons'),
    moreButton: more ? { label: attr(more, 'aria-label'), haspopup: attr(more, 'aria-haspopup'), expanded: attr(more, 'aria-expanded'), box: box(more) } : null,
    summary: q('logs-summary') ? {
      labels: [...q('logs-summary').querySelectorAll('.ft-summary__label')].map((n) => text(n)),
      total: attr(q('logs-total'), 'data-count'), totalText: text(q('logs-total')),
      today: attr(q('logs-today'), 'data-count'), todayText: text(q('logs-today')),
      week: attr(q('logs-week'), 'data-count'), weekText: text(q('logs-week')),
      box: box(q('logs-summary')),
    } : null,
    levels: [...document.querySelectorAll('[data-testid="logs-levels"] li')].map((li) => ({ level: li.getAttribute('data-level'), count: Number(li.getAttribute('data-count')), text: text(li) })),
    range: text(q('logs-range')),
    search: input ? { exists: true, value: input.value, placeholder: input.placeholder, label: input.getAttribute('aria-label'), maxlength: input.maxLength, status: text(q('logs-search-status')), clearLabel: attr(q('logs-search-clear'), 'aria-label') } : { exists: false, value: null, status: null },
    chips: { group: !!q('logs-chips'), level: text(q('logs-chip-level')), time: text(q('logs-chip-time')), clear: text(q('logs-chip-clear')) },
    list: list ? { loaded: Number(attr(list, 'data-loaded-count')), total: Number(attr(list, 'data-total')) } : null,
    days: qa('logs-day').map((d) => ({
      key: attr(d, 'data-date'),
      title: text(d.querySelector('[data-testid="logs-day-title"]')),
      titleTag: d.querySelector('[data-testid="logs-day-title"]') ? d.querySelector('[data-testid="logs-day-title"]').tagName : null,
      date: text(d.querySelector('.ft-day__date')),
      count: text(d.querySelector('[data-testid="logs-day-count"]')),
      rows: d.querySelectorAll('[data-testid="log-row"]').length,
    })),
    rows: qa('log-row').map((el) => ({
      id: attr(el, 'data-id'), level: attr(el, 'data-level'), tag: el.tagName,
      title: text(el.querySelector('.ft-cell__title')), subtitle: text(el.querySelector('.ft-cell__subtitle')),
      label: attr(el, 'aria-label'), popup: attr(el, 'aria-haspopup'),
      box: box(el), main: box(el.querySelector('.ft-cell__main')), trail: box(el.querySelector('.ft-cell__trail')),
    })),
    foot: text(q('logs-footnote')),
    footRole: attr(q('logs-footnote'), 'role'),
    loadMore: q('logs-load-more') ? { text: text(q('logs-load-more')), box: box(q('logs-load-more')) } : null,
    empty: empty ? {
      title: text(empty.querySelector('.ft-empty__title')),
      text: text(empty.querySelector('.ft-empty__text')),
      action: text(q('logs-empty-clear')),
    } : null,
  };
}"""

MENU_JS = r"""() => {
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  const box = (el) => { if (!el) return null; const r = el.getBoundingClientRect(); return { l: r.left, r: r.right, t: r.top, b: r.bottom, w: r.width, h: r.height }; };
  const text = (el) => (el ? el.textContent.replace(/\s+/g, ' ').trim() : null);
  const menu = q('logs-menu');
  if (!menu) return null;
  return {
    box: box(menu), anchor: box(q('logs-more')),
    role: menu.getAttribute('role'), label: menu.getAttribute('aria-label'), placement: menu.getAttribute('data-placement'),
    items: [...menu.querySelectorAll('[role="menuitem"]')].map((b) => ({
      id: b.getAttribute('data-testid'), label: text(b), disabled: b.disabled,
      danger: b.classList.contains('ft-popover__item--danger'), color: getComputedStyle(b).color, box: box(b),
    })),
  };
}"""

FILTER_JS = r"""() => {
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  const box = (el) => { if (!el) return null; const r = el.getBoundingClientRect(); return { l: r.left, r: r.right, t: r.top, b: r.bottom, w: r.width, h: r.height }; };
  const text = (el) => (el ? el.textContent.replace(/\s+/g, ' ').trim() : null);
  const sheet = q('log-filter-sheet');
  if (!sheet) return null;
  return {
    box: box(sheet),
    title: text(sheet.querySelector('[data-testid="sheet-title"]')),
    levels: [...sheet.querySelectorAll('[data-testid="log-filter-level"]')].map((c) => ({ level: c.getAttribute('data-level'), selected: c.getAttribute('data-selected'), pressed: c.getAttribute('aria-pressed') })),
    start: q('log-filter-start').value, end: q('log-filter-end').value,
    error: text(q('log-filter-error')), errorRole: q('log-filter-error') ? q('log-filter-error').getAttribute('role') : null,
    apply: { disabled: q('log-filter-apply').disabled, text: text(q('log-filter-apply')) },
    reset: { disabled: q('log-filter-reset').disabled },
  };
}"""

DETAIL_JS = r"""() => {
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  const box = (el) => { if (!el) return null; const r = el.getBoundingClientRect(); return { l: r.left, r: r.right, t: r.top, b: r.bottom, w: r.width, h: r.height }; };
  const text = (el) => (el ? el.textContent.replace(/\s+/g, ' ').trim() : null);
  const sheet = q('log-detail-sheet');
  if (!sheet) return null;
  return {
    box: box(sheet),
    title: text(sheet.querySelector('[data-testid="sheet-title"]')),
    level: text(q('log-detail-level')), time: text(q('log-detail-time')), message: text(q('log-detail-message')),
    source: text(q('log-detail-source')), type: text(q('log-detail-type')),
    exception: q('log-detail-exception') ? q('log-detail-exception').textContent : null,
    exceptionBox: box(q('log-detail-exception')),
    context: !!q('log-detail-context'),
    props: [...document.querySelectorAll('[data-testid="log-detail-prop"]')].map((p) => ({ key: p.getAttribute('data-key'), value: text(p.querySelector('.ft-logdetail__value')) })),
    copy: q('log-copy') ? { disabled: q('log-copy').disabled, text: text(q('log-copy')), box: box(q('log-copy')) } : null,
    footer: box(sheet.querySelector('[data-testid="sheet-footer"]')),
    overflowX: document.documentElement.scrollWidth - window.innerWidth,
    contentRight: Math.max(...[...sheet.querySelectorAll('*')].map((el) => el.getBoundingClientRect().right)),
  };
}"""

NAV_JS = r"""() => {
  const rect = (el) => { if (!el) return null; const r = el.getBoundingClientRect(); return { l: r.left, r: r.right, t: r.top, b: r.bottom, w: r.width, h: r.height }; };
  const textRect = (el) => { if (!el) return null; const range = document.createRange(); range.selectNodeContents(el); return rect(range); };
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  return { back: rect(q('nav-back')), title: textRect(q('nav-title')), more: rect(q('logs-more')) };
}"""

GEOMETRY_JS = r"""() => {
  const rect = (el) => { if (!el) return null; const r = el.getBoundingClientRect(); return { top: r.top, bottom: r.bottom, height: r.height }; };
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  return { bar: rect(document.querySelector('.ft-subbar')), nav: rect(q('nav-bar')), page: rect(q('logs-page')), summary: rect(q('logs-summary')) };
}"""

# 把页面最近的可滚动祖先滚到底，返回最后一块内容（加载更多 / 末尾进度）的底边
SCROLL_TO_END_JS = r"""() => {
  let el = document.querySelector('[data-testid="logs-page"]');
  while (el && el !== document.documentElement) {
    const cs = getComputedStyle(el);
    if ((cs.overflowY === 'auto' || cs.overflowY === 'scroll') && el.scrollHeight > el.clientHeight) break;
    el = el.parentElement;
  }
  const scroller = el && el !== document.documentElement ? el : document.scrollingElement;
  scroller.scrollTop = scroller.scrollHeight;
  return new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(() => {
    const last = document.querySelector('[data-testid="logs-load-more"]') || document.querySelector('[data-testid="logs-footnote"]');
    resolve({ lastBottom: last.getBoundingClientRect().bottom, scrolled: scroller.scrollTop, scrollable: scroller.scrollHeight > scroller.clientHeight });
  })));
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


def gone(locator, timeout: int = 8000) -> bool:
    try:
        locator.wait_for(state="detached", timeout=timeout)
        return True
    except PlaywrightTimeoutError:
        return False


def toast(page, text: str):
    return page.locator(".m-enqueued-snackbars").get_by_text(text)


def wait_toast(page, text: str) -> bool:
    page.mouse.move(1, 1)  # MASA 的 Snackbar 悬停时暂停计时，鼠标别停在上面
    return visible(toast(page, text).first, 8000)


def approx(actual, expected, tolerance: float = 1.0) -> bool:
    return actual is not None and abs(actual - expected) <= tolerance


def state(page) -> dict:
    return page.evaluate(STATE_JS)


def path(page) -> str:
    return page.evaluate("() => location.pathname")


# ---- 期望值（独立于被测代码再算一遍） ----


def at(days_ago: int, hour: int, minute: int = 0, second: int = 0, millisecond: int = 0) -> datetime:
    """本机时区「days_ago 天前的 hour 点」（naive 本地时间；LogFiles 写文件时按本机时区换算成 UTC）。"""
    base = datetime.now().replace(hour=hour, minute=minute, second=second, microsecond=millisecond * 1000)
    return base - timedelta(days=days_ago)


def day_label(day: date, today: date) -> str:
    """「11月24日」；不在今年的带年份（与账单页的日期写法一致）。"""
    return f"{day.month}月{day.day}日" if day.year == today.year else f"{day.year}年{day.month}月{day.day}日"


def day_title(day: date, today: date) -> str:
    if day == today:
        return "今天"
    if day == today - timedelta(days=1):
        return "昨天"
    return day_label(day, today)


def day_date_text(day: date, today: date) -> str:
    weekday = WEEKDAYS[day.weekday()]
    if day in (today, today - timedelta(days=1)):
        return f"{day_label(day, today)} {weekday}"
    return weekday


def message_of(entry: dict) -> str:
    """模板代入属性后的消息。"""
    return re.sub(r"\{(\w+)\}", lambda m: str(entry["properties"].get(m.group(1), m.group(0))), entry["template"])


def short_source(entry: dict) -> str:
    return entry["source"].rsplit(".", 1)[-1] if entry["source"] else ""


def clock(entry: dict) -> str:
    return entry["when"].strftime("%H:%M:%S")


def full_time(entry: dict) -> str:
    return entry["when"].strftime("%Y-%m-%d %H:%M:%S.") + f"{entry['when'].microsecond // 1000:03d}"


def subtitle_of(entry: dict) -> str:
    parts = [clock(entry), entry["level"]]
    if short_source(entry):
        parts.append(short_source(entry))
    return " · ".join(parts)


def label_of(entry: dict) -> str:
    return "，".join([entry["level"], full_time(entry), message_of(entry)])


def newest_first(entries: "list[dict]") -> "list[dict]":
    return sorted(entries, key=lambda e: e["when"], reverse=True)


def group_by_day(entries: "list[dict]") -> "list[tuple[date, list[dict]]]":
    groups: "list[tuple[date, list[dict]]]" = []
    for entry in newest_first(entries):
        day = entry["when"].date()
        if groups and groups[-1][0] == day:
            groups[-1][1].append(entry)
        else:
            groups.append((day, [entry]))
    return groups


def week_start(today: date) -> date:
    """本周周日（与服务一致：周日起算；date.weekday() 周一 = 0）。"""
    return today - timedelta(days=(today.weekday() + 1) % 7)


def expected_levels(entries: "list[dict]") -> "list[tuple[str, int]]":
    """级别分布：条数多的在前，同数按严重程度（Fatal → Error → Warning …）。"""
    severity = {"Fatal": 0, "Error": 1, "Warning": 2, "Information": 3, "Debug": 4}
    counts: "dict[str, int]" = {}
    for entry in entries:
        counts[entry["level"]] = counts.get(entry["level"], 0) + 1
    return sorted(counts.items(), key=lambda kv: (-kv[1], severity.get(kv[0], 6), kv[0]))


def expected_range(entries: "list[dict]", today: date) -> str:
    earliest = min(e["when"] for e in entries)
    latest = max(e["when"] for e in entries)
    pattern = "%Y-%m-%d %H:%M" if earliest.year != today.year or latest.year != today.year else "%m-%d %H:%M"
    return f"{earliest.strftime(pattern)} ~ {latest.strftime(pattern)}"


def copy_text(entry: dict) -> str:
    """复制到剪贴板的文本（与 LogViewerPresentation.CopyText 的格式一致，时间不含毫秒）。"""
    lines = [f"时间: {entry['when'].strftime('%Y-%m-%d %H:%M:%S')}", f"级别: {entry['level']}"]
    if entry["exception"]:
        lines.append(f"异常类型: {exception_type(entry)}")
    lines += [f"消息: {message_of(entry)}", f"来源: {entry['source'] or ''}", "完整信息:"]
    return "\n".join(lines) + "\n" + (entry["exception"] or message_of(entry))


def exception_type(entry: dict) -> str:
    return entry["exception"].split(":", 1)[0].strip()


# ---- 数据集 ----


def rich_entries() -> "list[dict]":
    """7 条日志：跨今天 / 昨天 / 3 天前 / 10 天前 / 去年；Error 5、Fatal 1、Warning 1；有的有异常与上下文，有的没有来源。"""
    today = date.today()
    return [
        log_entry(
            at(0, 9, 30, 15, 123), "同步失败: {Reason}", level="Error", source="FinanceTracker.Core.Services.SyncService",
            exception="System.InvalidOperationException: 云端不可用\n   at FinanceTracker.Core.Services.SyncService.PushAsync()\n   at FinanceTracker.Core.Services.SyncService.SyncAsync()",
            properties={"Reason": "云端不可用", "App": "FinanceTracker", "Platform": "Android"},
        ),
        log_entry(
            at(0, 8, 0, 1), "应用启动异常", level="Fatal", source="FinanceTracker.App.MauiProgram",
            exception="System.NullReferenceException: 未将对象引用设置到对象实例。\n   at FinanceTracker.App.MauiProgram.CreateMauiApp()",
        ),
        log_entry(at(1, 21, 15), "保存账单失败: {BillId}", level="Error", source="FinanceTracker.Core.Services.BillService", properties={"BillId": "b-001"}),
        log_entry(at(1, 10, 0), "磁盘空间不足", level="Warning", source="FinanceTracker.Web.Services.CacheService"),
        log_entry(
            at(3, 12, 0), "导入失败", level="Error", source="FinanceTracker.Core.Services.ImportService",
            exception="System.FormatException: 金额格式不正确\n   at FinanceTracker.Core.Services.ImportService.Parse()",
        ),
        log_entry(at(10, 12, 0), "旧日志 A", level="Error", source=None),
        log_entry(datetime(today.year - 1, 12, 31, 23, 59, 58), "跨年日志", level="Error", source="FinanceTracker.Core.Services.SyncService"),
    ]


def corrupt_lines() -> "dict[str, list[str]]":
    """今天那份文件里再追加几行坏数据：不是 JSON、空白行、缺字段的 JSON 之外的垃圾——页面都应当跳过。"""
    return {date.today().strftime("%Y%m%d"): ["{这不是合法的 JSON", "   ", "也不是 JSON"]}


def paging_entries() -> "list[dict]":
    """45 条 Error：前 12 条在今天、其余 33 条在昨天（一页 20 条：第一页跨两天，之后昨天那组在翻页时合并变长）。"""
    entries = []
    for index in range(45):
        if index < 12:
            when = at(0, 23, 30) - timedelta(minutes=index * 7)
        else:
            when = at(1, 23, 30) - timedelta(minutes=(index - 12) * 7)
        entries.append(log_entry(when, f"分页日志 {index + 1:02d}", source="FinanceTracker.Core.Services.SyncService"))
    return entries


# ---- 进入页面与交互 ----


def wait_logs_ready(page) -> bool:
    """首次加载结束：骨架屏消失，出现空状态 / 列表 / 失败之一。"""
    return poll(
        page,
        """() => !document.querySelector('[data-testid="logs-skeleton"]')
            && !!document.querySelector('[data-testid="logs-empty"], [data-testid="logs-list"], [data-testid="logs-failed"]')""",
        timeout=20000,
    )


def go_logs(page) -> bool:
    """整页直接进入错误日志页（造数据要在这之前做）。"""
    open_app(page, "/log-viewer")
    tid(page, "logs-page").wait_for(state="visible")
    ok = wait_logs_ready(page)
    wait_animations_settled(page)
    return ok


def wait_list(page, total: int, shown: "int | None" = None, timeout: int = 10000) -> bool:
    """等列表落到指定结果：total = 0 表示空状态；否则 data-total（和可选的已加载条数）对上。"""
    return poll(
        page,
        """([total, shown]) => {
          const list = document.querySelector('[data-testid="logs-list"]');
          if (total === 0) return !list && !!document.querySelector('[data-testid="logs-empty"]');
          if (!list) return false;
          return Number(list.getAttribute('data-total')) === total && (shown === null || Number(list.getAttribute('data-loaded-count')) === shown);
        }""",
        arg=[total, shown],
        timeout=timeout,
    )


def wait_titles(page, titles: "list[str]", timeout: int = 10000) -> bool:
    """等列表的行标题恰好是 titles（同样条数的两次搜索之间 wait_list 分不出新旧，必须按内容等）。"""
    return poll(
        page,
        r"""(titles) => {
          const rows = [...document.querySelectorAll('[data-testid="log-row"] .ft-cell__title')].map((n) => n.textContent.replace(/\s+/g, ' ').trim());
          return rows.length === titles.length && rows.every((t, i) => t === titles[i]);
        }""",
        arg=titles,
        timeout=timeout,
    )


def row_titles(page) -> "list[str]":
    return [r["title"] for r in state(page)["rows"]]


def selected_levels(sheet: dict) -> "list[str]":
    """筛选 Sheet 里选中的级别（未选中的标签是 data-selected="false"）。"""
    return [level["level"] for level in sheet["levels"] if level["selected"] == "true"]


def open_menu(page) -> dict:
    """点 ⋯ 打开菜单（等它定位完显示出来），返回菜单状态。"""
    tid(page, "logs-more").click()
    tid(page, "logs-menu").wait_for(state="visible")
    return page.evaluate(MENU_JS)


def close_menu_with_escape(page) -> bool:
    page.keyboard.press("Escape")
    return gone(tid(page, "logs-menu"))


def choose_menu(page, key: str) -> None:
    """打开菜单并点其中一项（filter / refresh / export / clear）。"""
    open_menu(page)
    tid(page, f"logs-menu-{key}").click()
    gone(tid(page, "logs-menu"))


def open_filter(page) -> dict:
    choose_menu(page, "filter")
    tid(page, "log-filter-sheet").wait_for(state="visible")
    wait_animations_settled(page)
    return page.evaluate(FILTER_JS)


def level_chip(page, level: str):
    return page.locator(f'[data-testid="log-filter-level"][data-level="{level}"]')


def toggle_level(page, level: str, selected: bool) -> bool:
    level_chip(page, level).click()
    return poll(
        page,
        "([level, on]) => { const c = document.querySelector(`[data-testid=\"log-filter-level\"][data-level=\"${level}\"]`); return !!c && (c.getAttribute('data-selected') === 'true') === on; }",
        arg=[level, selected],
    )


def set_dates(page, start: "date | None", end: "date | None") -> None:
    """在筛选 Sheet 的两个日期框里填起止日期（None = 清空）。"""
    tid(page, "log-filter-start").fill(start.isoformat() if start else "")
    tid(page, "log-filter-end").fill(end.isoformat() if end else "")
    poll(
        page,
        "([s, e]) => document.querySelector('[data-testid=\"log-filter-start\"]').value === s && document.querySelector('[data-testid=\"log-filter-end\"]').value === e",
        arg=[start.isoformat() if start else "", end.isoformat() if end else ""],
    )


def apply_filter(page) -> bool:
    """点「查看日志」：Sheet 收起（等它卸载）。"""
    tid(page, "log-filter-apply").click()
    ok = gone(tid(page, "log-filter-sheet-host"))
    wait_animations_settled(page)
    return ok


def close_sheet(page, testid: str) -> bool:
    """点 Sheet 右上角 ✕ 关闭（等退出动画结束、卸载）。"""
    tid(page, testid).get_by_test_id("sheet-close").click()
    ok = gone(tid(page, f"{testid}-host"))
    wait_animations_settled(page)
    return ok


def search(page, text: str) -> None:
    """在搜索框里输入并回车（立即提交，不等 350 ms 的停顿）。"""
    box = tid(page, "logs-search-input")
    box.fill(text)
    box.press("Enter")


def open_row(page, index: int) -> dict:
    """点列表第 index 行，等详情 Sheet 入场，返回详情状态。"""
    page.locator('[data-testid="log-row"]').nth(index).click()
    tid(page, "log-detail-sheet").wait_for(state="visible")
    wait_animations_settled(page)
    return page.evaluate(DETAIL_JS)


def exported_path(page) -> "Path | None":
    """从「日志已导出到: …」的提示里取出文件路径。"""
    if not wait_toast(page, "日志已导出到:"):
        return None
    match = re.search(r"日志已导出到:\s*(.+?\.json)", toast(page, "日志已导出到:").first.text_content() or "")
    return Path(match.group(1)) if match else None


def wait_toasts_gone(page, timeout: int = 15000) -> bool:
    page.mouse.move(1, 1)
    return poll(page, "() => document.querySelectorAll('.m-enqueued-snackbars .m-snack__content').length === 0", timeout=timeout)


# ---------------------------------------------------------------------------------------------- A. 空状态与「⋯」菜单


def check_empty_and_menu(run: TestRun, files: LogFiles) -> None:
    files.clear()
    with session() as s:
        page = s.page
        login(page)
        run.check("进入错误日志页", go_logs(page))
        st = state(page)
        icons_dark = poll(page, "() => document.documentElement.getAttribute('data-statusbar-icons') === 'dark'")
        run.check(
            "【外壳】二级页面：导航栏标题「错误日志」（h1）、没有 Tab 栏、浅色页面 → 状态栏深色图标",
            st["exists"] and st["title"] == "错误日志" and not tab_bar(page).is_visible() and icons_dark,
            f"{st['title']} {icons_dark}",
        )
        run.check(
            "【空状态】一条日志都没有：好消息文案，没有概览 / 搜索框 / 列表，也没有失败状态",
            st["empty"] is not None and st["empty"]["title"] == "暂无错误日志" and st["empty"]["text"] == "太棒了！没有发现任何错误" and st["empty"]["action"] is None
            and st["summary"] is None and not st["search"]["exists"] and st["list"] is None and not st["failed"],
            str(st["empty"]),
        )
        run.check("【空状态】没有横向溢出、不是骨架屏", st["overflowX"] <= 0 and not st["skeleton"])

        more = st["moreButton"]
        run.check(
            "【⋯】右上角圆钮：读屏名称「更多」、aria-haspopup=menu、aria-expanded=false，点按区域 ≥ 44",
            more is not None and more["label"] == "更多" and more["haspopup"] == "menu" and more["expanded"] == "false" and more["box"]["w"] >= 44 and more["box"]["h"] >= 44,
            str(more),
        )
        run.check("【⋯】在屏幕右上角（右缘离屏幕边 ≤ 24、在导航行内）", more["box"]["r"] >= SCREEN_W - 24 and more["box"]["t"] <= 60, str(more["box"]))

        menu = open_menu(page)
        run.check("【菜单】role=menu、读屏名称「更多」", menu["role"] == "menu" and menu["label"] == "更多", f"{menu['role']} {menu['label']}")
        run.check("【菜单】四项按序：筛选 / 刷新 / 导出日志 / 清除日志", [i["label"] for i in menu["items"]] == MENU_LABELS, str([i["label"] for i in menu["items"]]))
        run.check(
            "【菜单】在 ⋯ 下方（data-placement=below）、右缘对齐 ⋯（内缩 12）、完全在屏幕内",
            menu["placement"] == "below" and menu["box"]["t"] >= menu["anchor"]["b"] - 2 and approx(menu["box"]["r"], menu["anchor"]["r"] - 12, 2)
            and menu["box"]["l"] >= 0 and menu["box"]["r"] <= SCREEN_W + 0.5 and menu["box"]["b"] <= SCREEN_H,
            f"{menu['placement']} {menu['box']} {menu['anchor']}",
        )
        by_id = {i["id"]: i for i in menu["items"]}
        run.check(
            "【菜单】没有日志时「导出日志」「清除日志」置灰，「筛选」「刷新」可用",
            by_id["logs-menu-export"]["disabled"] and by_id["logs-menu-clear"]["disabled"] and not by_id["logs-menu-filter"]["disabled"] and not by_id["logs-menu-refresh"]["disabled"],
            str({k: v["disabled"] for k, v in by_id.items()}),
        )
        run.check("【菜单】仅「清除日志」是红色（破坏性）", [i["id"] for i in menu["items"] if i["danger"]] == ["logs-menu-clear"] and by_id["logs-menu-clear"]["danger"], str([(i["id"], i["danger"]) for i in menu["items"]]))
        run.check("【菜单】菜单打开时 ⋯ 的 aria-expanded=true", state(page)["moreButton"]["expanded"] == "true")
        run.check("【菜单】菜单项高度 ≥ 44", all(i["box"]["h"] >= 44 for i in menu["items"]), str([i["box"]["h"] for i in menu["items"]]))
        run.check("【菜单】每屏玻璃层 ≤ 3", page.evaluate(GLASS_LAYERS_JS) <= 3, str(page.evaluate(GLASS_LAYERS_JS)))
        shot(page, "logs-empty-menu")

        run.check("【菜单】Esc 收起，⋯ 的 aria-expanded 回到 false", close_menu_with_escape(page) and state(page)["moreButton"]["expanded"] == "false")

        open_menu(page)
        tid(page, "logs-menu-layer").click(position={"x": 20, "y": 500})
        run.check("【菜单】点菜单外面收起", gone(tid(page, "logs-menu")))
        run.check("【菜单】点外面只是收起：没有误触下面的内容、没有弹出别的东西", tid(page, "log-detail-sheet").count() == 0 and tid(page, "log-filter-sheet").count() == 0 and state(page)["empty"] is not None)

        # ---- 没有日志时也能打开筛选 Sheet：只有 Fatal / Error 两个级别 ----
        sheet = open_filter(page)
        run.check(
            "【筛选】没有日志时 Sheet 仍可打开：标题「筛选日志」、只有 Fatal / Error 两个级别、都没选中",
            sheet["title"] == "筛选日志" and [l["level"] for l in sheet["levels"]] == ["Fatal", "Error"] and selected_levels(sheet) == [],
            str(sheet),
        )
        run.check("【筛选】Sheet 底部主按钮「查看日志」、「重置」置灰（没有条件可重置）", sheet["apply"]["text"] == "查看日志" and not sheet["apply"]["disabled"] and sheet["reset"]["disabled"])
        run.check("【筛选】✕ 关闭", close_sheet(page, "log-filter-sheet"))

        # ---- 刷新读到新写入的日志：空状态 → 列表 ----
        entries = [log_entry(at(0, 9, 0), "刷新后出现的日志")]
        files.write(entries)
        choose_menu(page, "refresh")
        run.check("【刷新】菜单里点「刷新」：读到新写入的日志，空状态变成列表", wait_list(page, 1))
        st = state(page)
        run.check(
            "【刷新】概览与搜索框出现，行标题是新日志的消息",
            st["summary"] is not None and st["summary"]["total"] == "1" and st["search"]["exists"] and [r["title"] for r in st["rows"]] == ["刷新后出现的日志"],
            str(st["rows"]),
        )
        menu = open_menu(page)
        by_id = {i["id"]: i for i in menu["items"]}
        run.check("【菜单】有日志后「导出日志」「清除日志」可用", not by_id["logs-menu-export"]["disabled"] and not by_id["logs-menu-clear"]["disabled"])
        close_menu_with_escape(page)
        run.check_clean(s.errors, "A 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- B. 概览、列表与分组


def check_overview_and_list(run: TestRun, files: LogFiles) -> None:
    entries = rich_entries()
    files.write(entries, extra_lines=corrupt_lines())
    today = date.today()
    ordered = newest_first(entries)
    groups = group_by_day(entries)
    with session() as s:
        page = s.page
        login(page)
        run.check("进入错误日志页", go_logs(page))
        st = state(page)

        summary = st["summary"]
        in_week = sum(1 for e in entries if e["when"] >= datetime.combine(week_start(today), datetime.min.time()))
        in_today = sum(1 for e in entries if e["when"].date() == today)
        run.check(
            "【概览】三栏：总日志数 / 今日日志 / 本周日志（损坏行不计入总数）",
            summary is not None and summary["labels"] == ["总日志数", "今日日志", "本周日志"]
            and summary["total"] == str(len(entries)) and summary["today"] == str(in_today) and summary["week"] == str(in_week),
            str(summary),
        )
        run.check("【概览】数字用千分位文本显示（与 data-count 一致）", summary["totalText"] == str(len(entries)) and summary["todayText"] == str(in_today))
        run.check(
            "【概览】级别分布：条数多的在前，同数按严重程度（Error 5 · Fatal 1 · Warning 1）",
            [(l["level"], l["count"]) for l in st["levels"]] == expected_levels(entries),
            str(st["levels"]),
        )
        run.check("【概览】时间范围：最早 ~ 最晚（跨年时两端都带年份）", st["range"] == f"时间范围：{expected_range(entries, today)}", f"{st['range']} / {expected_range(entries, today)}")

        search_state = st["search"]
        run.check(
            "【搜索框】占位文字「搜索消息、异常类型或来源」、读屏名称「搜索日志」、最多 50 字、清除按钮名称「清除搜索」，一开始是空的",
            search_state["placeholder"] == "搜索消息、异常类型或来源" and search_state["label"] == "搜索日志" and search_state["maxlength"] == 50
            and search_state["clearLabel"] == "清除搜索" and search_state["value"] == "",
            str(search_state),
        )

        run.check(
            "【分组】按天分组、组头标题按今天 / 昨天 / 更早（去年的带年份）排列，时间倒序",
            [d["key"] for d in st["days"]] == [day.isoformat() for day, _ in groups] and [d["title"] for d in st["days"]] == [day_title(day, today) for day, _ in groups],
            str([(d["key"], d["title"]) for d in st["days"]]),
        )
        run.check(
            "【分组】组头日期：今天 / 昨天补具体日期与星期，更早的只有星期；条数「N 条」",
            [d["date"] for d in st["days"]] == [day_date_text(day, today) for day, _ in groups] and [d["count"] for d in st["days"]] == [f"{len(items)} 条" for _, items in groups],
            str([(d["date"], d["count"]) for d in st["days"]]),
        )
        run.check("【分组】组头标题是 h2（标题层级：导航 h1 → 日期 h2）", all(d["titleTag"] == "H2" for d in st["days"]))

        rows = st["rows"]
        run.check("【列表】共 7 行（3 行损坏数据被跳过），按时间倒序", len(rows) == len(entries) and [r["title"] for r in rows] == [message_of(e) for e in ordered], str([r["title"] for r in rows]))
        run.check("【列表】标题是模板代入属性后的消息（不是空的）", rows[0]["title"] == "同步失败: 云端不可用" and rows[2]["title"] == "保存账单失败: b-001", f"{rows[0]['title']} / {rows[2]['title']}")
        run.check("【列表】副标题「时:分:秒 · 级别 · 来源短名」，没有来源时省略最后一段", [r["subtitle"] for r in rows] == [subtitle_of(e) for e in ordered], str([r["subtitle"] for r in rows]))
        run.check("【列表】读屏名称「级别，完整时间，标题」", [r["label"] for r in rows] == [label_of(e) for e in ordered], str(rows[0]["label"]))
        run.check("【列表】每行是按钮、aria-haspopup=dialog、data-level 对得上", all(r["tag"] == "BUTTON" and r["popup"] == "dialog" for r in rows) and [r["level"] for r in rows] == [e["level"] for e in ordered])
        run.check("【列表】行高 ≥ 44、没有超出屏幕", all(r["box"]["h"] >= 44 and r["box"]["l"] >= 0 and r["box"]["r"] <= SCREEN_W + 0.5 for r in rows))
        run.check("【列表】末尾进度「共 7 条」（role=status），没有「加载更多」", st["foot"] == f"共 {len(entries)} 条" and st["footRole"] == "status" and st["loadMore"] is None, f"{st['foot']} {st['loadMore']}")
        run.check("【列表】概览在列表之上、没有横向溢出", st["overflowX"] <= 0 and st["summary"]["box"]["b"] <= rows[0]["box"]["t"])
        run.check("【列表】每屏玻璃层 ≤ 3（导航的两个圆钮）", page.evaluate(GLASS_LAYERS_JS) <= 3, str(page.evaluate(GLASS_LAYERS_JS)))
        shot(page, "logs-list")
        run.check_clean(s.errors, "B 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- C. 加载更多


def check_paging(run: TestRun, files: LogFiles) -> None:
    entries = paging_entries()
    files.write(entries)
    ordered = newest_first(entries)
    titles = [message_of(e) for e in ordered]
    with session() as s:
        page = s.page
        login(page)
        run.check("进入错误日志页", go_logs(page))
        st = state(page)
        run.check(
            "【分页】第一页 20 条：进度「已显示 20 / 共 45 条」、有「加载更多」",
            st["list"] == {"loaded": 20, "total": 45} and len(st["rows"]) == 20 and st["foot"] == "已显示 20 / 共 45 条" and st["loadMore"] is not None and st["loadMore"]["text"] == "加载更多",
            f"{st['list']} {st['foot']}",
        )
        run.check("【分页】概览的总数是全部 45 条（不是已加载的 20 条）", st["summary"]["total"] == "45")
        run.check("【分页】第一页跨两天：今天 12 条 + 昨天 8 条", [d["count"] for d in st["days"]] == ["12 条", "8 条"] and [d["title"] for d in st["days"]] == ["今天", "昨天"], str(st["days"]))
        run.check("【分页】第一页是最新的 20 条（分页日志 01 ~ 20）", [r["title"] for r in st["rows"]] == titles[:20])

        tid(page, "logs-load-more").click()
        run.check("【分页】点「加载更多」：再加载 20 条，共 40 行", wait_list(page, 45, 40))
        st = state(page)
        run.check("【分页】进度「已显示 40 / 共 45 条」，行不重复、顺序连续", st["foot"] == "已显示 40 / 共 45 条" and [r["title"] for r in st["rows"]] == titles[:40], st["foot"])
        run.check("【分页】跨页的同一天合并：昨天那组变成 28 条（仍然只有两组）", [d["count"] for d in st["days"]] == ["12 条", "28 条"], str([d["count"] for d in st["days"]]))

        tid(page, "logs-load-more").click()
        run.check("【分页】再点一次：加载完剩下的 5 条，共 45 行", wait_list(page, 45, 45))
        st = state(page)
        run.check("【分页】全部加载完：进度「共 45 条」、「加载更多」消失、昨天那组 33 条", st["foot"] == "共 45 条" and st["loadMore"] is None and [d["count"] for d in st["days"]] == ["12 条", "33 条"], f"{st['foot']} {st['loadMore']}")
        run.check("【分页】45 行按时间倒序、没有重复", [r["title"] for r in st["rows"]] == titles and len({r["id"] for r in st["rows"]}) == 45)

        # 条件变了回到第一页
        search(page, "分页日志 0")
        run.check("【分页】搜索后回到第一页（「分页日志 0」匹配 01 ~ 09 共 9 条）", wait_list(page, 9, 9))
        run.check_clean(s.errors, "C 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- D. 搜索


def check_search(run: TestRun, files: LogFiles) -> None:
    entries = rich_entries()
    files.write(entries)
    ordered = newest_first(entries)
    with session() as s:
        page = s.page
        login(page)
        run.check("进入错误日志页", go_logs(page))

        search(page, "云端")
        run.check("【搜索】消息里的字：「云端」只命中 1 条", wait_list(page, 1))
        st = state(page)
        run.check("【搜索】行是那一条、进度「共 1 条」", row_titles(page) == ["同步失败: 云端不可用"] and st["foot"] == "共 1 条", str(row_titles(page)))
        run.check("【搜索】结果条数播报给读屏（role=status）：「找到 1 条日志」", st["search"]["status"] == "找到 1 条日志", str(st["search"]["status"]))
        run.check("【搜索】概览口径是全部日志，不受搜索影响（总数仍是 7）", st["summary"]["total"] == "7" and len(st["levels"]) == 3, str(st["summary"]))

        tid(page, "logs-search-clear").click()
        run.check("【搜索】清除按钮：清空搜索框、列表回到全部", wait_list(page, 7) and state(page)["search"]["value"] == "")
        run.check("【搜索】清除后播报文字清空", state(page)["search"]["status"] == "")

        search(page, "nullreference")
        run.check("【搜索】异常类型、不区分大小写：「nullreference」命中异常类型为 NullReferenceException 的那条", wait_titles(page, ["应用启动异常"]), str(row_titles(page)))

        search(page, "BillService")
        run.check("【搜索】来源：「BillService」命中 1 条", wait_titles(page, ["保存账单失败: b-001"]), str(row_titles(page)))

        search(page, "PushAsync")
        run.check("【搜索】完整异常信息（堆栈里的方法名）也能搜到", wait_titles(page, ["同步失败: 云端不可用"]), str(row_titles(page)))

        search(page, "磁盘")
        wait_titles(page, ["磁盘空间不足"])
        search(page, "  云端  ")
        run.check("【搜索】首尾空格被忽略", wait_titles(page, ["同步失败: 云端不可用"]), str(row_titles(page)))

        search(page, "SyncService")
        run.check("【搜索】跨天的结果仍按天分组：「SyncService」命中 2 条（今天 + 去年）", wait_list(page, 2) and len(state(page)["days"]) == 2, str(state(page)["days"]))

        # ---- 无结果 ----
        search(page, "不存在的关键字xyz")
        run.check("【搜索·无结果】空状态", wait_list(page, 0))
        st = state(page)
        run.check(
            "【搜索·无结果】文案区分「没有日志」：标题「没有符合条件的日志」、说明「换个关键字试试，或清除搜索」、按钮「清除搜索」",
            st["empty"] is not None and st["empty"]["title"] == "没有符合条件的日志" and st["empty"]["text"] == "换个关键字试试，或清除搜索" and st["empty"]["action"] == "清除搜索",
            str(st["empty"]),
        )
        run.check("【搜索·无结果】读屏播报「没有找到相关日志」；概览与搜索框仍在", st["search"]["status"] == "没有找到相关日志" and st["summary"] is not None and st["search"]["exists"], str(st["search"]))
        tid(page, "logs-empty-clear").click()
        run.check("【搜索·无结果】点「清除搜索」：列表回到全部", wait_list(page, 7))
        st = state(page)
        run.check("【搜索·无结果】搜索框被清空（重建）、行与顺序复原", st["search"]["value"] == "" and [r["title"] for r in st["rows"]] == [message_of(e) for e in ordered], str(st["search"]))
        run.check_clean(s.errors, "D 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- E. 筛选 Sheet 与条件条


def check_filter(run: TestRun, files: LogFiles) -> None:
    entries = rich_entries()
    files.write(entries)
    today = date.today()
    yesterday = today - timedelta(days=1)
    with session() as s:
        page = s.page
        login(page)
        run.check("进入错误日志页", go_logs(page))

        # ---- 打开：级别列出现过的 + Fatal / Error ----
        sheet = open_filter(page)
        run.check(
            "【筛选 Sheet】标题「筛选日志」；级别：Fatal、Error、Warning（日志里出现过 Warning），都没选；日期都空；「重置」置灰",
            sheet["title"] == "筛选日志" and [l["level"] for l in sheet["levels"]] == ["Fatal", "Error", "Warning"] and selected_levels(sheet) == []
            and sheet["start"] == "" and sheet["end"] == "" and sheet["reset"]["disabled"],
            str(sheet),
        )
        run.check("【筛选 Sheet】半屏：底边贴屏幕底、高度不超过屏幕的 90%、主按钮「查看日志」", approx(sheet["box"]["b"], SCREEN_H) and sheet["box"]["h"] <= SCREEN_H * 0.9 and sheet["apply"]["text"] == "查看日志", str(sheet["box"]))
        run.check("【筛选 Sheet】每屏玻璃层 ≤ 3", page.evaluate(GLASS_LAYERS_JS) <= 3, str(page.evaluate(GLASS_LAYERS_JS)))
        shot(page, "logs-filter-sheet")

        # ---- 级别 ----
        run.check("【级别】点 Error：选中，「重置」可用", toggle_level(page, "Error", True) and not page.evaluate(FILTER_JS)["reset"]["disabled"])
        run.check("【级别】「查看日志」：Sheet 收起、只剩 Error 级别的 5 条", apply_filter(page) and wait_list(page, 5))
        st = state(page)
        run.check("【级别】行的级别全是 Error；概览口径不变（总数 7）", all(r["level"] == "Error" for r in st["rows"]) and st["summary"]["total"] == "7", str([r["level"] for r in st["rows"]]))
        run.check(
            "【条件条】出现「Error」标签与「清除条件」；没有时间标签",
            st["chips"]["group"] and st["chips"]["level"] == "Error" and st["chips"]["time"] is None and st["chips"]["clear"] == "清除条件",
            str(st["chips"]),
        )
        shot(page, "logs-chips")

        # 点标签文字 = 调整：Sheet 里带着当前的条件
        tid(page, "logs-chip-level").get_by_test_id("chip-open").click()
        tid(page, "log-filter-sheet").wait_for(state="visible")
        wait_animations_settled(page)
        sheet = page.evaluate(FILTER_JS)
        run.check("【条件条】点标签文字：打开筛选 Sheet，里面是当前的条件（Error 已选中）", selected_levels(sheet) == ["Error"], str(sheet["levels"]))
        toggle_level(page, "Fatal", True)
        apply_filter(page)
        run.check("【级别】再加上 Fatal：标签变成「Fatal、Error」（按严重程度排好），共 6 条", wait_list(page, 6) and state(page)["chips"]["level"] == "Fatal、Error", str(state(page)["chips"]))

        # 单独移除级别条件
        tid(page, "logs-chip-level").get_by_test_id("chip-remove").click()
        run.check("【条件条】点标签的 ×：只移除这个条件，列表回到全部、条件条消失", wait_list(page, 7) and not state(page)["chips"]["group"])

        # ---- 日期范围 ----
        open_filter(page)
        set_dates(page, yesterday, yesterday)
        run.check("【日期】起止同一天（含当天）：「查看日志」可用", not page.evaluate(FILTER_JS)["apply"]["disabled"])
        apply_filter(page)
        expected_yesterday = [e for e in entries if e["when"].date() == yesterday]
        run.check("【日期】只看昨天：" + str(len(expected_yesterday)) + " 条", wait_list(page, len(expected_yesterday)))
        st = state(page)
        run.check(
            "【日期】时间标签只写这一天（「11月24日」）；只有「昨天」一组",
            st["chips"]["time"] == day_label(yesterday, today) and st["chips"]["level"] is None and [d["title"] for d in st["days"]] == ["昨天"],
            f"{st['chips']} {[d['title'] for d in st['days']]}",
        )

        # 只填开始日期
        tid(page, "logs-chip-time").get_by_test_id("chip-open").click()
        tid(page, "log-filter-sheet").wait_for(state="visible")
        wait_animations_settled(page)
        sheet = page.evaluate(FILTER_JS)
        run.check("【日期】点时间标签：Sheet 里带着当前的起止日期", sheet["start"] == yesterday.isoformat() and sheet["end"] == yesterday.isoformat(), f"{sheet['start']} {sheet['end']}")
        start = today - timedelta(days=5)
        set_dates(page, start, None)
        apply_filter(page)
        expected_since = [e for e in entries if e["when"].date() >= start]
        run.check("【日期】只填开始日期（含当天及之后）：" + str(len(expected_since)) + " 条", wait_list(page, len(expected_since)))
        run.check("【日期】时间标签「{起}起」", state(page)["chips"]["time"] == f"{day_label(start, today)}起", str(state(page)["chips"]["time"]))

        # 起止颠倒
        open_filter(page)
        set_dates(page, today, yesterday)
        sheet = page.evaluate(FILTER_JS)
        run.check(
            "【日期】结束早于开始：红字提示「结束日期不能早于开始日期」（role=alert）、「查看日志」置灰",
            sheet["error"] == "结束日期不能早于开始日期" and sheet["errorRole"] == "alert" and sheet["apply"]["disabled"],
            f"{sheet['error']} {sheet['apply']}",
        )
        set_dates(page, today, today)
        sheet = page.evaluate(FILTER_JS)
        run.check("【日期】改回合法范围：提示消失、「查看日志」可用", sheet["error"] is None and not sheet["apply"]["disabled"], str(sheet["error"]))

        # 重置
        tid(page, "log-filter-reset").click()
        poll(page, "() => document.querySelector('[data-testid=\"log-filter-start\"]').value === '' && document.querySelector('[data-testid=\"log-filter-end\"]').value === ''")
        sheet = page.evaluate(FILTER_JS)
        run.check("【重置】清空日期与级别、「重置」置灰", sheet["start"] == "" and sheet["end"] == "" and sheet["reset"]["disabled"], str(sheet))
        apply_filter(page)
        run.check("【重置】再「查看日志」：回到全部 7 条、条件条消失", wait_list(page, 7) and not state(page)["chips"]["group"])

        # ---- 放弃改动：✕ / 遮罩 / Esc ----
        open_filter(page)
        toggle_level(page, "Warning", True)
        close_sheet(page, "log-filter-sheet")
        run.check("【放弃】点 ✕：不生效（列表仍是 7 条、没有条件条）", wait_list(page, 7) and not state(page)["chips"]["group"])
        sheet = open_filter(page)
        run.check("【放弃】再打开：上次没提交的 Warning 选择不留（草稿被丢弃）", selected_levels(sheet) == [], str(sheet["levels"]))
        toggle_level(page, "Warning", True)
        tid(page, "sheet-scrim").click(position={"x": SCREEN_W / 2, "y": 40})
        run.check("【放弃】点遮罩：Sheet 收起、不生效", gone(tid(page, "log-filter-sheet-host")) and state(page)["list"]["total"] == 7)
        wait_animations_settled(page)
        open_filter(page)
        toggle_level(page, "Warning", True)
        page.keyboard.press("Escape")
        run.check("【放弃】Esc：Sheet 收起、不生效", gone(tid(page, "log-filter-sheet-host")) and state(page)["list"]["total"] == 7)
        wait_animations_settled(page)

        # ---- 「清除条件」标签：清掉级别与时间，保留搜索关键字 ----
        search(page, "Sync")
        wait_list(page, 2)  # 「Sync」命中同步失败（今天）与跨年日志（去年）两条
        open_filter(page)
        toggle_level(page, "Error", True)
        set_dates(page, yesterday, today)
        apply_filter(page)
        st = state(page)
        run.check(
            "【组合】搜索「Sync」+ 级别 Error + 日期：条件条里有级别、时间两个标签与「清除条件」",
            st["chips"]["level"] == "Error" and st["chips"]["time"] == f"{day_label(yesterday, today)}至{day_label(today, today)}" and st["chips"]["clear"] == "清除条件",
            str(st["chips"]),
        )
        combined = [
            e for e in newest_first(entries)
            if e["level"] == "Error" and "sync" in ((e["source"] or "") + (e["exception"] or "")).lower() and yesterday <= e["when"].date() <= today
        ]
        run.check(
            "【组合】三个条件同时生效（关键字 + 级别 + 日期）：" + str(len(combined)) + " 条",
            wait_titles(page, [message_of(e) for e in combined]),
            f"{row_titles(page)} / {[message_of(e) for e in combined]}",
        )
        tid(page, "logs-chip-clear").click()
        run.check("【清除条件】点「清除条件」：级别与时间都清掉、搜索关键字保留（仍是「Sync」的 2 条）", wait_list(page, 2) and not state(page)["chips"]["group"] and state(page)["search"]["value"] == "Sync", str(state(page)["search"]))
        tid(page, "logs-search-clear").click()
        wait_list(page, 7)

        # ---- 组合出空 → 空状态里的「清除搜索与筛选」 ----
        search(page, "同步")
        wait_list(page, 1)
        open_filter(page)
        toggle_level(page, "Fatal", True)
        apply_filter(page)
        run.check("【组合·无结果】搜索「同步」+ 级别 Fatal：没有符合条件的日志", wait_list(page, 0))
        st = state(page)
        run.check(
            "【组合·无结果】说明「换个关键字试试，或清除搜索与筛选」、按钮「清除搜索与筛选」；条件条仍在（能看到并单独移除条件）",
            st["empty"] is not None and st["empty"]["text"] == "换个关键字试试，或清除搜索与筛选" and st["empty"]["action"] == "清除搜索与筛选" and st["chips"]["level"] == "Fatal",
            f"{st['empty']} {st['chips']}",
        )
        tid(page, "logs-empty-clear").click()
        run.check("【组合·无结果】点「清除搜索与筛选」：全部清掉，回到 7 条", wait_list(page, 7))
        st = state(page)
        run.check("【组合·无结果】搜索框清空、条件条消失", st["search"]["value"] == "" and not st["chips"]["group"], f"{st['search']} {st['chips']}")
        run.check_clean(s.errors, "E 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- F. 详情 Sheet 与复制


def check_detail(run: TestRun, files: LogFiles) -> None:
    entries = rich_entries()
    files.write(entries)
    ordered = newest_first(entries)
    first, fatal, warning = ordered[0], ordered[1], ordered[3]
    no_source_index = next(i for i, e in enumerate(ordered) if e["source"] is None)  # 10 天前与去年 12 月底的先后随运行日期变，按「没有来源」找
    no_source = ordered[no_source_index]
    with session() as s:
        s.context.grant_permissions(["clipboard-read", "clipboard-write"], origin=BASE_URL)
        page = s.page
        login(page)
        run.check("进入错误日志页", go_logs(page))

        # ---- 有异常、有上下文的一条 ----
        detail = open_row(page, 0)
        run.check("【详情】点第一行：弹出半屏 Sheet，标题「日志详情」", detail is not None and detail["title"] == "日志详情")
        run.check(
            "【详情】Sheet 底边贴屏幕底；内容多时最高到状态栏下方（留出 12 的缝，不盖住状态栏）、没有横向溢出",
            approx(detail["box"]["b"], SCREEN_H) and detail["box"]["t"] >= 12 - 1 and detail["overflowX"] <= 0,
            f"{detail['box']} {detail['overflowX']}",
        )
        run.check("【详情】头部：级别 + 完整时间（含毫秒）", detail["level"] == "Error" and detail["time"] == full_time(first), f"{detail['level']} {detail['time']} / {full_time(first)}")
        run.check("【详情】消息是模板代入属性后的全文", detail["message"] == message_of(first), str(detail["message"]))
        run.check("【详情】来源显示完整名称、异常类型显示完整类型名", detail["source"] == first["source"] and detail["type"] == exception_type(first), f"{detail['source']} {detail['type']}")
        run.check("【详情】完整异常信息（等宽、保留换行）与日志里的 @x 一致", detail["exception"] == first["exception"], str(detail["exception"]))
        run.check(
            "【详情】上下文信息列出 Serilog 的附加属性（来源 SourceContext 不在其中）",
            [p["key"] for p in detail["props"]] == ["Reason", "App", "Platform"] and [p["value"] for p in detail["props"]] == ["云端不可用", "FinanceTracker", "Android"],
            str(detail["props"]),
        )
        run.check("【详情】底部固定主按钮「复制日志信息」可用，在 Sheet 底部", detail["copy"]["text"] == "复制日志信息" and not detail["copy"]["disabled"] and detail["copy"]["box"]["b"] <= SCREEN_H + 0.5, str(detail["copy"]))
        run.check("【详情】每屏玻璃层 ≤ 3", page.evaluate(GLASS_LAYERS_JS) <= 3, str(page.evaluate(GLASS_LAYERS_JS)))
        shot(page, "logs-detail")

        # ---- 复制 ----
        tid(page, "log-copy").click()
        run.check("【复制】提示「已复制到剪贴板」", wait_toast(page, "已复制到剪贴板"))
        clipboard = page.evaluate("() => navigator.clipboard.readText()").replace("\r\n", "\n")  # Windows 的系统剪贴板把换行存成 CRLF
        run.check("【复制】剪贴板里是「时间 / 级别 / 异常类型 / 消息 / 来源 / 完整信息」", clipboard == copy_text(first), f"{clipboard!r} != {copy_text(first)!r}")
        run.check("【复制】复制后 Sheet 保持打开", tid(page, "log-detail-sheet").is_visible())

        # ---- 关闭：✕ ----
        run.check("【详情】点 ✕ 关闭", close_sheet(page, "log-detail-sheet"))
        run.check("【详情】关闭后详情内容被清掉（再没有详情节点）", tid(page, "log-detail-level").count() == 0 and tid(page, "log-detail-sheet").count() == 0)
        wait_toasts_gone(page)

        # ---- 异常类型是别的类型的一条（Fatal） ----
        detail = open_row(page, 1)
        run.check(
            "【详情】第二行（Fatal）：级别、异常类型 NullReferenceException、完整信息",
            detail["level"] == "Fatal" and detail["type"] == exception_type(fatal) and detail["exception"] == fatal["exception"] and detail["message"] == "应用启动异常",
            f"{detail['level']} {detail['type']}",
        )
        run.check("【详情】没有附加属性的日志没有「上下文信息」一节", not detail["context"] and detail["props"] == [])
        tid(page, "sheet-scrim").click(position={"x": SCREEN_W / 2, "y": 40})
        run.check("【详情】点遮罩关闭", gone(tid(page, "log-detail-sheet-host")))
        wait_animations_settled(page)

        # ---- 没有异常的一条（Warning）：不显示异常类型与完整异常信息 ----
        detail = open_row(page, 3)
        run.check(
            "【详情】没有异常的日志（Warning）：没有「异常类型」与「完整异常信息」，来源与消息照常",
            detail["level"] == "Warning" and detail["type"] is None and detail["exception"] is None and detail["message"] == message_of(warning) and detail["source"] == warning["source"],
            str(detail),
        )
        run.check("【详情】内容少时是半屏：高度 ≤ 屏幕的 60%（高度由内容决定）", detail["box"]["h"] <= SCREEN_H * 0.6, str(detail["box"]))
        page.keyboard.press("Escape")
        run.check("【详情】Esc 关闭", gone(tid(page, "log-detail-sheet-host")))
        wait_animations_settled(page)

        # ---- 没有来源的一条：来源显示「未知」 ----
        detail = open_row(page, no_source_index)
        run.check("【详情】没有来源的日志：来源显示「未知」", detail["message"] == message_of(no_source) and detail["source"] == "未知", f"{detail['message']} {detail['source']}")
        close_sheet(page, "log-detail-sheet")
        run.check_clean(s.errors, "F 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- G. 导出


def check_export(run: TestRun, files: LogFiles) -> None:
    exported: "list[Path]" = []
    today = date.today()
    yesterday = today - timedelta(days=1)
    try:
        # ---- 超过一页的日志：导出的是全部，不是第一页 ----
        entries = paging_entries()
        files.write(entries)
        with session() as s:
            page = s.page
            login(page)
            run.check("进入错误日志页（45 条日志，一页只显示 20 条）", go_logs(page) and wait_list(page, 45, 20))
            choose_menu(page, "export")
            target = exported_path(page)
            if target is not None:
                exported.append(target)
            run.check("【导出】提示「日志已导出到: …json」（缓存目录下的文件）", target is not None and target.suffix == ".json" and target.parent.name == "Cache", str(target))
            run.check("【导出】文件确实写出来了", target is not None and target.exists(), str(target))
            data = json.loads(target.read_text(encoding="utf-8")) if target is not None and target.exists() else []
            run.check("【导出】文件里是全部 45 条日志（不止页面上已显示的 20 条）", len(data) == 45, str(len(data)))
            run.check(
                "【导出】消息不为空（模板已代入属性）、按时间倒序、不含界面展开标记（ShowDetail）",
                bool(data) and all(item.get("Message") for item in data) and [d["Message"] for d in data][:3] == ["分页日志 01", "分页日志 02", "分页日志 03"]
                and all("ShowDetail" not in item for item in data),
                str(data[:1]),
            )
            run.check_clean(s.errors, "G 节（导出全部）会话无页面异常 / 控制台错误 / 失败请求")

        # ---- 日期条件生效：只导出范围内的 ----
        entries = rich_entries()
        files.write(entries)
        with session() as s:
            page = s.page
            login(page)
            run.check("进入错误日志页（7 条日志）", go_logs(page) and wait_list(page, 7))
            open_filter(page)
            set_dates(page, yesterday, today)
            apply_filter(page)
            expected = [e for e in entries if e["when"].date() >= yesterday]
            run.check("按日期筛到昨天与今天", wait_list(page, len(expected)))
            wait_toasts_gone(page)
            choose_menu(page, "export")
            target = exported_path(page)
            if target is not None:
                exported.append(target)
            data = json.loads(target.read_text(encoding="utf-8")) if target is not None and target.exists() else []
            run.check(
                "【导出】日期条件生效：只导出「昨天与今天」的 " + str(len(expected)) + " 条",
                [d["Message"] for d in data] == [message_of(e) for e in newest_first(expected)],
                str([d.get("Message") for d in data]),
            )
            run.check("【导出】上下文属性与来源随导出一起写出", any(d.get("Properties", {}).get("Reason") == "云端不可用" for d in data) and any(d.get("Source") == "FinanceTracker.Core.Services.SyncService" for d in data))
            run.check_clean(s.errors, "G 节（按日期导出）会话无页面异常 / 控制台错误 / 失败请求")
    finally:
        for file in exported:
            file.unlink(missing_ok=True)


# ---------------------------------------------------------------------------------------------- H. 清除


def check_clear(run: TestRun, files: LogFiles) -> None:
    entries = rich_entries()
    files.write(entries)
    keep = files.directory / "keep.txt"
    keep.write_text("不是日志文件，清除日志时必须留着", encoding="utf-8")
    try:
        with session() as s:
            page = s.page
            login(page)
            run.check("进入错误日志页", go_logs(page) and wait_list(page, 7))

            # 先设上搜索与筛选，确认清除后一并复位
            search(page, "失败")
            wait_list(page, 3)
            open_filter(page)
            toggle_level(page, "Error", True)
            apply_filter(page)
            run.check("（前置）搜索「失败」+ 级别 Error：3 条（同步失败、保存账单失败、导入失败）、有条件条", wait_list(page, 3) and state(page)["chips"]["level"] == "Error")

            # ---- 取消 ----
            choose_menu(page, "clear")
            alert = tid(page, "logs-clear-alert")
            run.check("【清除】点「清除日志」弹出 Alert（不是直接删）", visible(alert))
            title = alert.get_by_test_id("alert-title").inner_text()
            message = alert.get_by_test_id("alert-message").inner_text()
            buttons = [b.inner_text().strip() for b in alert.locator("button").all()]
            confirm_class = alert.get_by_test_id("alert-confirm").get_attribute("class") or ""
            run.check(
                "【清除】Alert 是 alertdialog：标题「清除所有错误日志？」、说明写明全部 7 条（不是筛选后的 3 条）与不可恢复、按钮「取消」「清除」（确认是红色）",
                alert.get_attribute("role") == "alertdialog" and title == "清除所有错误日志？" and message == "共 7 条日志将被删除，此操作不可恢复。"
                and buttons == ["取消", "清除"] and "ft-alert__btn--danger" in confirm_class,
                f"{alert.get_attribute('role')} | {title} | {message} | {buttons} | {confirm_class}",
            )
            run.check("【清除】Alert 弹出时日志文件还在", len(files.files()) > 0)
            shot(page, "logs-clear-alert")
            alert.get_by_test_id("alert-cancel").click()
            run.check("【清除·取消】Alert 关闭、日志文件还在、条件与列表不变", gone(tid(page, "alert-host")) and len(files.files()) > 0 and state(page)["list"]["total"] == 3 and toast(page, "日志已清除").count() == 0)

            # ---- Esc 等同取消 ----
            choose_menu(page, "clear")
            visible(alert)
            page.keyboard.press("Escape")
            run.check("【清除·Esc】Esc 等同「取消」：Alert 关闭、日志文件还在", gone(tid(page, "alert-host")) and len(files.files()) > 0)

            # ---- 确认 ----
            choose_menu(page, "clear")
            visible(alert)
            alert.get_by_test_id("alert-confirm").click()
            run.check("【清除·确认】提示「日志已清除」", wait_toast(page, "日志已清除"))
            run.check("【清除·确认】落到「暂无错误日志」空状态", wait_list(page, 0))
            st = state(page)
            run.check(
                "【清除·确认】空状态：好消息文案；概览 / 搜索框 / 条件条都不在了（搜索与筛选复位）",
                st["empty"] is not None and st["empty"]["title"] == "暂无错误日志" and st["summary"] is None and not st["search"]["exists"] and not st["chips"]["group"],
                str(st["empty"]),
            )
            run.check("【清除·确认】日志文件（app-*.log）全部删除", files.files() == [], str(files.files()))
            run.check("【清除·确认】目录里别的文件原样保留（只删 app-*.log）", keep.exists() and keep.read_text(encoding="utf-8") == "不是日志文件，清除日志时必须留着")
            menu = open_menu(page)
            by_id = {i["id"]: i for i in menu["items"]}
            run.check("【清除·确认】菜单里「导出日志」「清除日志」重新置灰", by_id["logs-menu-export"]["disabled"] and by_id["logs-menu-clear"]["disabled"])
            close_menu_with_escape(page)

            # 重新进入仍是空的
            run.check("重新进入页面：仍是空状态", go_logs(page) and state(page)["empty"] is not None)
            run.check_clean(s.errors, "H 节会话无页面异常 / 控制台错误 / 失败请求")
    finally:
        keep.unlink(missing_ok=True)


# ---------------------------------------------------------------------------------------------- I. 入口、导航与沉浸式


def check_navigation_and_immersive(run: TestRun, files: LogFiles) -> None:
    files.write(paging_entries())
    with session() as s:
        page = s.page
        login(page)

        # ---- 从「我的」进入 ----
        open_tab(page, "我的")
        tid(page, "profile-card").wait_for(state="visible")
        wait_animations_settled(page)
        tid(page, "profile-entry-log-viewer").click()
        wait_url(page, r"^/log-viewer$")
        wait_tab_bar(page, visible=False)
        run.check("【入口】「我的」→ 错误日志：进入 /log-viewer，Tab 栏隐藏", path(page) == "/log-viewer" and not tab_bar(page).is_visible())
        tid(page, "logs-page").wait_for(state="visible")
        run.check("【入口】页面读到日志", wait_logs_ready(page) and wait_list(page, 45, 20))
        wait_animations_settled(page)
        st = state(page)
        run.check("【入口】导航栏标题「错误日志」、有返回按钮与「⋯」", st["title"] == "错误日志" and tid(page, "nav-back").is_visible() and st["moreButton"] is not None)

        # ---- 返回键 ----
        tid(page, "nav-back").click()
        wait_url(page, r"^/profile$")
        wait_tab_bar(page)
        wait_secondary_pages_closed(page)
        wait_animations_settled(page)
        run.check("【返回】点返回键回到「我的」，Tab 栏恢复", path(page) == "/profile" and tab_bar(page).is_visible())

        # ---- 浏览器 / 系统返回 ----
        tid(page, "profile-entry-log-viewer").click()
        wait_url(page, r"^/log-viewer$")
        wait_tab_bar(page, visible=False)
        tid(page, "logs-page").wait_for(state="visible")
        wait_animations_settled(page)
        page.go_back()
        wait_url(page, r"^/profile$")
        wait_tab_bar(page)
        wait_secondary_pages_closed(page)
        run.check("【返回】浏览器 / 系统返回同样回到「我的」", path(page) == "/profile" and tab_bar(page).is_visible())

        # ---- 沉浸式：刘海 47 + 手势条 34 ----
        notch, gesture = 47, 34
        run.check("进入错误日志页", go_logs(page) and wait_list(page, 45, 20))
        page.evaluate(SET_SAFE_JS, [notch, gesture])
        wait_animations_settled(page)
        geo = page.evaluate(GEOMETRY_JS)
        run.check("【沉浸式】导航栏背景延伸到屏幕顶部，高 = 44 + 状态栏 47", approx(geo["bar"]["top"], 0) and approx(geo["bar"]["height"], 44 + notch), str(geo["bar"]))
        run.check("【沉浸式】导航行（返回 / 标题 / ⋯）在状态栏之下，高 44", approx(geo["nav"]["top"], notch) and approx(geo["nav"]["height"], 44), str(geo["nav"]))
        run.check("【沉浸式】内容紧贴导航栏下沿，没有被盖住", geo["summary"]["top"] >= geo["bar"]["bottom"] - 1, f"{geo['summary']} {geo['bar']}")
        wait_attr(page, "html", "data-statusbar-icons", "dark")
        run.check("【沉浸式】浅色页面 → 状态栏用深色图标", page.locator("html").get_attribute("data-statusbar-icons") == "dark")
        end = page.evaluate(SCROLL_TO_END_JS)
        run.check("【沉浸式】滚到底：最后的「加载更多」完整露在手势条之上（内容底部让出了手势条）", end["scrollable"] and end["lastBottom"] <= SCREEN_H - gesture + 1, str(end))
        shot(page, "logs-immersive-notch")

        # 菜单在刘海下仍在 ⋯ 下方、完全在屏幕内
        menu = open_menu(page)
        run.check("【沉浸式】菜单仍在 ⋯ 下方、完全在屏幕内", menu["box"]["t"] >= menu["anchor"]["b"] - 2 and menu["box"]["b"] <= SCREEN_H - gesture + 1, f"{menu['box']} {menu['anchor']}")
        close_menu_with_escape(page)

        page.evaluate(CLEAR_SAFE_JS)
        wait_animations_settled(page)
        restored = page.evaluate(GEOMETRY_JS)
        run.check("【沉浸式】安全区还原为 0 后，导航栏回到 44 高", approx(restored["bar"]["height"], 44), str(restored["bar"]))
        run.check_clean(s.errors, "I 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- J. 窄屏


def check_narrow(run: TestRun, files: LogFiles) -> None:
    width, height = 320, 640
    long_message = "这是一条特别特别长的错误日志消息用来测试在窄屏上列表行是否会省略而不是撑破屏幕" * 2
    long_source = "FinanceTracker.Infrastructure.Repositories.VeryLongRepositoryNameForNarrowScreenTesting"
    entries = [
        log_entry(at(0, 11, 0, 5), long_message, source=long_source, exception="System.InvalidOperationException: " + long_message + "\n   at FinanceTracker.Infrastructure.Repositories.VeryLongRepositoryNameForNarrowScreenTesting.SaveChangesAsyncWithAVeryLongMethodName()"),
        log_entry(at(0, 10, 0), "短消息"),
        log_entry(at(1, 9, 0), long_message, level="Fatal", source=long_source),
    ]
    files.write(entries)
    with session(viewport={"width": width, "height": height}) as s:
        page = s.page
        login(page)
        run.check("进入错误日志页", go_logs(page) and wait_list(page, 3))
        st = state(page)
        nav = page.evaluate(NAV_JS)
        run.check(
            "320 宽（列表）：没有横向溢出；导航标题夹在返回钮与 ⋯ 之间、互不重叠",
            st["overflowX"] <= 0 and nav["back"]["r"] <= nav["title"]["l"] + 0.5 and nav["title"]["r"] <= nav["more"]["l"] + 0.5,
            f"{st['overflowX']} {nav}",
        )
        run.check(
            "320 宽（列表）：长消息在行内省略，标题 / 副标题不压到右侧箭头，整行在屏幕内",
            all(r["box"]["l"] >= 0 and r["box"]["r"] <= width + 0.5 and r["main"]["r"] <= r["trail"]["l"] + 0.5 for r in st["rows"]),
            str([(r["box"], r["main"], r["trail"]) for r in st["rows"]]),
        )
        summary = st["summary"]
        run.check("320 宽（概览）：三栏都在屏幕内", summary is not None and summary["box"]["l"] >= 0 and summary["box"]["r"] <= width + 0.5, str(summary and summary["box"]))
        run.check("320 宽：每屏玻璃层 ≤ 3", page.evaluate(GLASS_LAYERS_JS) <= 3, str(page.evaluate(GLASS_LAYERS_JS)))
        shot(page, "logs-320")

        detail = open_row(page, 0)
        run.check(
            "320 宽（详情 Sheet）：长消息 / 长来源 / 长堆栈都换行或在框内滚动，不撑出屏幕；主按钮在屏幕内",
            detail["overflowX"] <= 0 and detail["contentRight"] <= width + 0.5 and detail["copy"]["box"]["r"] <= width + 0.5 and detail["copy"]["box"]["h"] >= 44,
            f"{detail['overflowX']} {detail['contentRight']} {detail['copy']['box']}",
        )
        run.check("320 宽（详情 Sheet）：每屏玻璃层 ≤ 3", page.evaluate(GLASS_LAYERS_JS) <= 3, str(page.evaluate(GLASS_LAYERS_JS)))
        shot(page, "logs-detail-320")
        close_sheet(page, "log-detail-sheet")

        sheet = open_filter(page)
        run.check("320 宽（筛选 Sheet）：级别与日期行在屏幕内、没有横向溢出", sheet["box"]["l"] >= 0 and sheet["box"]["r"] <= width + 0.5 and state(page)["overflowX"] <= 0, str(sheet["box"]))
        set_dates(page, date.today() - timedelta(days=1), date.today() - timedelta(days=3))
        sheet = page.evaluate(FILTER_JS)
        run.check("320 宽（筛选 Sheet）：起止颠倒的提示在屏幕内、「查看日志」置灰", sheet["error"] == "结束日期不能早于开始日期" and sheet["apply"]["disabled"] and state(page)["overflowX"] <= 0)
        shot(page, "logs-filter-320")
        close_sheet(page, "log-filter-sheet")
        run.check_clean(s.errors, "J 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- main

SECTIONS = {
    "A": check_empty_and_menu,
    "B": check_overview_and_list,
    "C": check_paging,
    "D": check_search,
    "E": check_filter,
    "F": check_detail,
    "G": check_export,
    "H": check_clear,
    "I": check_navigation_and_immersive,
    "J": check_narrow,
}


def main() -> int:
    """不带参数跑全部小节；带小节名（如 `python log_viewer_page_test.py B E`）只跑指定小节，便于定位问题。"""
    require_server()
    run = TestRun("log-viewer-page")
    files = LogFiles()  # 取不到 E2E 日志目录（服务不是用 server.ps1 启动的）会以退出码 2 结束

    wanted = [a.upper() for a in sys.argv[1:]]
    unknown = [a for a in wanted if a not in SECTIONS]
    if unknown:
        print(f"[ENV] 不认识的小节：{unknown}；可选：{list(SECTIONS)}")
        return 2

    try:
        for name, section in SECTIONS.items():
            if not wanted or name in wanted:
                section(run, files)
    finally:
        files.clear()  # 不把造的日志留在 E2E 目录里（其他脚本的「我的 → 错误日志」入口检查读到的应当是空目录）

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
