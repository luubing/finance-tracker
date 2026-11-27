# -*- coding: utf-8 -*-
"""
E2E 测试：分类的「不计入统计」开关（ADR 0008）——存款这类记在支出里、但不想被统计的账单。

只覆盖网页层（本机库 + 假数据）；业务口径（哪些统计 / 预算受影响、软删除、同步、REST）由单元测试覆盖，这里验证用户真正走得到的那条链：
在分类上打开开关 → 统计 / 首页本月概览 / 预算不再计入 → 账单列表照常显示这些账单并标注「不计入统计」。

  A. 分类表单：新建 Sheet 里有「不计入统计」开关（默认关）与说明（随支出 / 收入换例子）；打开保存落库、分类列表出现标记；
     编辑 Sheet 带出已存的开关、改名不丢开关、关闭后保存落库；不保存就关掉不生效；开关状态不会从上一个 Sheet 漏到下一个
  B. 初始口径（造数：存款分类关、借入分类开；两个账本）：首页本月概览 / 统计月度 KPI 不含「借入」；底部提示「已忽略 1 笔…」；
     账单 Tab 的汇总卡照旧全含（支出 2,070、收入 2,600、9 笔），只有借入那一行带「不计入统计」标记
  C. 在分类页打开存款的开关再回来：统计 / 账单 / 首页都随二级页面关闭重新读取（不需要下拉刷新）——统计与首页不再含存款、
     提示多了存款一项（含笔数）、账单行多了标记、汇总卡不变；再关掉：全部回到初始
  D. 统计的账本筛选：选账本后提示与 KPI 只看这个账本（甲 / 乙 / 全部）
  E. 趋势 / 年度 / 自定义报表三个视图：同样不含被排除的账单，底部同样有提示
  F. 预算：总预算已用金额不含被排除的账单、给被排除分类设的分类预算已用 0；新建分类预算的分类选项里没有被排除的分类；
     编辑那笔预算时选项里仍有它并提示「账单不会累计到这笔预算里」
  G. 记一笔里内联新建分类并打开开关：保存账单后首页本月概览不变、统计不变、账单列表有该账单且带标记、统计提示多一项

可观察属性（已登记到 README）：category-exclude-group / category-exclude-row / category-exclude-switch（role=switch、aria-checked）、
category-cell[data-excluded]（行上的副标题「不计入统计」）、bill-row[data-excluded] 与 cell-tag（标签「不计入统计」）、
stats-excluded / trend-excluded / annual-excluded / report-excluded（底部提示，role=note）、budget-form-excluded（预算表单的说明）。
"""
import re
import sys
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from decimal import Decimal

from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

import managed_items_base as base
from categories_page_test import INCOME, SPEC, custom_row, select_segment
from e2e_base import (
    ACTIVE_ROOT_SELECTOR,
    BILL_TYPE_EXPENSE,
    BILL_TYPE_INCOME,
    LocalDb,
    TestRun,
    login,
    open_add_bill_sheet,
    open_app,
    open_tab,
    press_keys,
    require_server,
    session,
    shot,
    unique_name,
    wait_animations_settled,
    wait_secondary_pages_closed,
    wait_tab_bar,
)

LABEL = "不计入统计"
SCOPE = "不计入统计、首页本月概览和预算（账单列表仍会显示）"
EXPENSE_FOOTER = f"开启后，这个分类下的账单{SCOPE}。适合「存款」「还信用卡」这类不算花销的账单。"
INCOME_FOOTER = f"开启后，这个分类下的账单{SCOPE}。适合「借入」「退款」这类不算收入的账单。"
BUDGET_NOTE = f"这个分类设了「{LABEL}」，它的账单不会累计到这笔预算里"

CELL_JS = r"""(name) => {
  const cells = [...document.querySelectorAll('[data-testid="custom-categories"] [data-testid="category-cell"]')];
  const cell = cells.find((c) => ((c.querySelector('.ft-cell__title') || {}).textContent || '') === name);
  if (!cell) return null;
  const sub = cell.querySelector('.ft-cell__subtitle');
  return { excluded: cell.getAttribute('data-excluded'), subtitle: sub ? sub.textContent.trim() : null };
}"""

# 当前可见 Tab 根页面里的账单行（首页「最近账单」与账单 Tab 都用 bill-row）
ROWS_JS = r"""(rootSel) => {
  const root = document.querySelector(rootSel);
  if (!root) return null;
  return [...root.querySelectorAll('[data-testid="bill-row"]')].map((el) => {
    const tag = el.querySelector('[data-testid="cell-tag"]');
    return { id: el.getAttribute('data-bill-id'), excluded: el.getAttribute('data-excluded'), tag: tag ? tag.textContent.trim() : null, aria: el.getAttribute('aria-label') };
  });
}"""

ROWS_MATCH_JS = r"""([rootSel, flagged, total]) => {
  const root = document.querySelector(rootSel);
  if (!root) return false;
  const rows = [...root.querySelectorAll('[data-testid="bill-row"]')];
  if (total !== null && rows.length !== total) return false;
  const want = new Set(flagged);
  return rows.length > 0 && rows.every((el) => (el.getAttribute('data-excluded') === 'true') === want.has(el.getAttribute('data-bill-id')));
}"""

# 金额 / 提示的 data-testid（home-expense、stats-kpi-*、trend-*、annual-*、*-excluded 等）在整个应用里各只有一个，
# 所以直接按 document 查；只有 bill-row 这种首页与账单 Tab 都有的 id 才需要限定到当前可见的根页面（见 ROWS_JS）
AMOUNTS_JS = r"""(ids) => Object.fromEntries(ids.map((id) => {
  const el = document.querySelector(`[data-testid="${id}"]`);
  return [id, el ? el.getAttribute('data-amount') : null];
}))"""

AMOUNTS_MATCH_JS = r"""(want) => Object.entries(want).every(([id, v]) => {
  const el = document.querySelector(`[data-testid="${id}"]`);
  return !!el && el.getAttribute('data-amount') === v;
})"""

NOTE_JS = r"""([id, want]) => {
  const el = document.querySelector(`[data-testid="${id}"]`);
  const got = el ? el.textContent.replace(/\s+/g, ' ').trim() : null;
  return got === want;
}"""

NOTE_NOW_JS = r"""(id) => {
  const el = document.querySelector(`[data-testid="${id}"]`);
  return el ? { text: el.textContent.replace(/\s+/g, ' ').trim(), role: el.getAttribute('role') } : null;
}"""


# ---------------------------------------------------------------------------------------------- 小工具


def tid(page, testid: str):
    return page.get_by_test_id(testid)


def dec(value) -> Decimal:
    return Decimal(str(value))


def plain(value) -> str:
    """金额 → data-amount 的写法：两位小数、无千分位。"""
    return f"{dec(value):.2f}"


def money(value) -> str:
    return f"¥{dec(value):,.2f}"


def signed(value, income: bool) -> str:
    """提示里的金额：支出 -、收入 +（与账单行一致）。"""
    return f"{'+' if income else '-'}{money(value)}"


def note_text(parts: "list[tuple[str, int, bool, int]]") -> str:
    """统计页底部提示的期望文案。parts = [(分类名, 金额, 是否收入, 笔数)]，已按金额从大到小排好；这里独立再写一遍用来对照（不 import 被测代码）。"""
    bills = sum(count for _, _, _, count in parts)
    many = len(parts) > 1
    named = "、".join(
        f"{name} {signed(amount, income)}（{count} 笔）" if many else f"{name} {signed(amount, income)}"
        for name, amount, income, count in parts[:3]
    )
    more = f"等 {len(parts)} 个分类" if len(parts) > 3 else ""
    return f"已忽略 {bills} 笔「{LABEL}」的账单：{named}{more}"


def poll(page, js: str, arg=None, timeout: int = 12000) -> bool:
    """等页面内的 JS 谓词成立；超时返回 False（让断言记失败，而不是抛异常打断后面的用例）。"""
    try:
        page.wait_for_function(js, arg=arg, timeout=timeout)
        return True
    except PlaywrightTimeoutError:
        return False


def check_amounts(run: TestRun, page, label: str, want: "dict[str, str]") -> bool:
    ok = poll(page, AMOUNTS_MATCH_JS, arg=want)
    return run.check(label, ok, f"{page.evaluate(AMOUNTS_JS, list(want))} 期望 {want}")


def check_note(run: TestRun, page, label: str, testid: str, expected: "str | None") -> bool:
    """底部提示的文字等于 expected；expected 为 None 表示整行不存在。"""
    ok = poll(page, NOTE_JS, arg=[testid, expected])
    return run.check(label, ok, f"{page.evaluate(NOTE_NOW_JS, testid)} 期望 {expected!r}")


def check_note_semantics(run: TestRun, page, label: str, testid: str) -> None:
    now = page.evaluate(NOTE_NOW_JS, testid)
    run.check(label, now is not None and now["role"] == "note", str(now))


def check_rows(run: TestRun, page, label: str, flagged: "set[str]", total: "int | None" = None, at_least: int = 0) -> bool:
    """当前可见 Tab 根页面里每一行账单：带标记 ⇔ 它的账单 Id 在 flagged 里；标签文字与读屏名称一致；至少有 at_least 行是带标记的。"""
    want = sorted(i.lower() for i in flagged)
    ok = poll(page, ROWS_MATCH_JS, arg=[ACTIVE_ROOT_SELECTOR, want, total])
    rows = page.evaluate(ROWS_JS, ACTIVE_ROOT_SELECTOR) or []
    marked = [r for r in rows if r["excluded"] == "true"]
    tags_ok = all((r["tag"] == LABEL) == (r["excluded"] == "true") for r in rows)
    aria_ok = all(((r["aria"] or "").endswith(f"，{LABEL}")) == (r["excluded"] == "true") for r in rows)
    return run.check(
        label,
        ok and tags_ok and aria_ok and len(marked) >= at_least,
        f"行 {len(rows)}，带标记 {len(marked)}（要求 ≥ {at_least}），标签一致 {tags_ok}，读屏名称一致 {aria_ok}",
    )


def wait_toasts_gone(page) -> None:
    """等排队的提示都消失（同一句提示连着出现两次时，免得把上一条当成这一条）。"""
    page.mouse.move(1, 1)
    page.wait_for_function("() => document.querySelectorAll('.m-enqueued-snackbars .m-snack__wrapper').length === 0", timeout=20000)


def cell_info(page, name: str):
    return page.evaluate(CELL_JS, name)


def switch(page):
    return tid(page, "category-exclude-switch")


def switch_checked(page) -> "str | None":
    return switch(page).get_attribute("aria-checked")


def set_switch(page, on: bool) -> None:
    """把分类 Sheet 里的开关拨到 on 并等它的状态变过去。"""
    if (switch_checked(page) == "true") != on:
        switch(page).click()
    page.wait_for_function(
        "(v) => document.querySelector('[data-testid=\"category-exclude-switch\"]').getAttribute('aria-checked') === v",
        arg="true" if on else "false",
    )


def save_category(page, toast_text: str) -> None:
    wait_toasts_gone(page)
    base.save_form(page, SPEC)
    base.wait_toast(page, toast_text)
    base.wait_sheet_gone(page, SPEC)


def toggle_via_ui(page, name: str, on: bool) -> None:
    """从「我的」进分类管理页，把名为 name 的自定义分类的开关拨到 on、保存，返回「我的」。
    统计 / 账单 / 首页都是常驻的 Tab 根页面，二级页面关闭后它们要自己重新读取——这条路径正是用户设置开关后真实会走的。"""
    base.go_to_page(page, SPEC)
    base.open_edit(page, SPEC, name)
    set_switch(page, on)
    save_category(page, SPEC.updated_toast)
    base.go_back_to_profile(page)
    wait_secondary_pages_closed(page)


def go_stats_monthly(page) -> None:
    """点统计 Tab（常驻的根页面，不整页重载）并等月度视图读完。"""
    open_tab(page, "统计")
    page.wait_for_function("() => { const el = document.querySelector('[data-testid=\"stats-monthly\"]'); return !!el && el.getAttribute('data-state') === 'ready'; }")


def pick_stats_ledger(page, ledger_id: "str | None") -> None:
    """统计页的账本菜单选一项（None = 全部账本），选中后菜单自动收起。"""
    tid(page, "stats-ledger-pill").click()
    tid(page, "stats-ledger-sheet").wait_for(state="visible")
    wait_animations_settled(page)
    if ledger_id is None:
        tid(page, "stats-ledger-all").click()
    else:
        page.locator(f'[data-testid="stats-ledger-option"][data-ledger-id="{ledger_id.lower()}"]').click()
    tid(page, "stats-ledger-sheet").wait_for(state="detached")


# ---------------------------------------------------------------------------------------------- A. 分类表单


def check_category_form(run: TestRun, page, db: LocalDb, user_id: str) -> None:
    base.go_to_page(page, SPEC)

    # A1 新建 Sheet：开关行（默认关）+ 说明
    base.open_create(page, SPEC, "nav")
    row_title = tid(page, "category-exclude-row").locator(".ft-cell__title").inner_text().strip()
    footer = tid(page, "category-exclude-group").locator(".ft-group__footer").inner_text().strip()
    run.check(
        "【新建】有「不计入统计」开关行：role=switch、读屏名称「不计入统计」、默认关闭",
        switch(page).get_attribute("role") == "switch" and switch(page).get_attribute("aria-label") == LABEL and switch_checked(page) == "false" and row_title == LABEL,
        f"{switch(page).get_attribute('role')} / {switch(page).get_attribute('aria-label')} / {switch_checked(page)} / {row_title}",
    )
    run.check("【新建】支出分类的说明点明范围（统计 / 首页本月概览 / 预算，账单列表仍显示）并举「存款」「还信用卡」", footer == EXPENSE_FOOTER, footer)
    shot(page, "category-sheet-exclude-off")

    # A2 打开开关保存：落库、列表出现标记
    saving = unique_name("存钱")
    base.fill_form(page, SPEC, saving)
    set_switch(page, True)
    shot(page, "category-sheet-exclude-on")
    save_category(page, SPEC.created_toast)
    base.wait_row(page, SPEC, saving)
    row = custom_row(db, user_id, saving)
    run.check("【新建】打开开关保存：库里 ExcludeFromStatistics=1，是自己的自定义分类", row is not None and row["ExcludeFromStatistics"] == 1 and row["IsPreset"] == 0 and (row["UserId"] or "").upper() == user_id.upper(), str(row))
    run.check("【列表】开着开关的分类：行带 data-excluded=true，名称下一行是「不计入统计」", cell_info(page, saving) == {"excluded": "true", "subtitle": LABEL}, str(cell_info(page, saving)))
    shot(page, "category-list-excluded")

    # 不动开关再建一个：默认关
    plain_name = unique_name("日常开销")
    base.open_create(page, SPEC, "nav")
    base.fill_form(page, SPEC, plain_name)
    save_category(page, SPEC.created_toast)
    base.wait_row(page, SPEC, plain_name)
    row = custom_row(db, user_id, plain_name)
    run.check("【新建】不动开关保存：库里 ExcludeFromStatistics=0", row is not None and row["ExcludeFromStatistics"] == 0, str(row))
    run.check("【列表】没开开关的分类：没有 data-excluded，也没有副标题", cell_info(page, plain_name) == {"excluded": None, "subtitle": None}, str(cell_info(page, plain_name)))

    # A3 编辑 Sheet 带出已存的开关；开关状态不从上一个 Sheet 漏到下一个
    base.open_edit(page, SPEC, saving)
    run.check("【编辑】开着开关的分类：打开编辑 Sheet 时开关已是开", switch_checked(page) == "true", str(switch_checked(page)))
    base.close_sheet(page, SPEC)
    base.open_edit(page, SPEC, plain_name)
    run.check("【编辑】紧接着打开没开开关的分类：开关是关（不沿用上一个 Sheet 的状态）", switch_checked(page) == "false", str(switch_checked(page)))
    base.close_sheet(page, SPEC)
    base.open_create(page, SPEC, "nav")
    run.check("【新建】编辑过一个开着的分类之后再新建：开关仍默认关", switch_checked(page) == "false", str(switch_checked(page)))
    base.close_sheet(page, SPEC)

    # A4 只改名不碰开关：开关保留
    renamed = unique_name("改名存钱")
    base.open_edit(page, SPEC, saving)
    base.fill_form(page, SPEC, renamed)
    save_category(page, SPEC.updated_toast)
    base.wait_row(page, SPEC, renamed)
    row = custom_row(db, user_id, renamed)
    run.check("【编辑】只改名不碰开关：名称更新、开关仍是开（没有被改名顺手关掉）", row is not None and row["ExcludeFromStatistics"] == 1, str(row))
    run.check("【列表】改名后标记仍在", cell_info(page, renamed) == {"excluded": "true", "subtitle": LABEL}, str(cell_info(page, renamed)))

    # A5 不保存就关：不生效
    base.open_edit(page, SPEC, plain_name)
    set_switch(page, True)
    base.close_sheet(page, SPEC)
    row = custom_row(db, user_id, plain_name)
    run.check("【编辑】拨开开关但不点保存直接关掉 Sheet：库里仍是关，列表没有标记", row is not None and row["ExcludeFromStatistics"] == 0 and cell_info(page, plain_name)["excluded"] is None, str(row))
    base.open_edit(page, SPEC, plain_name)
    run.check("【编辑】再打开：开关是关（上次没保存的改动没有残留）", switch_checked(page) == "false", str(switch_checked(page)))
    base.close_sheet(page, SPEC)

    # A6 关闭开关保存
    base.open_edit(page, SPEC, renamed)
    set_switch(page, False)
    save_category(page, SPEC.updated_toast)
    row = custom_row(db, user_id, renamed)
    run.check("【编辑】关掉开关保存：库里 ExcludeFromStatistics=0，列表标记消失", row is not None and row["ExcludeFromStatistics"] == 0 and cell_info(page, renamed) == {"excluded": None, "subtitle": None}, f"{row} {cell_info(page, renamed)}")

    # A7 收入分段：说明换成收入的例子
    select_segment(page, INCOME)
    base.open_create(page, SPEC, "nav")
    income_footer = tid(page, "category-exclude-group").locator(".ft-group__footer").inner_text().strip()
    run.check("【新建】收入分类的说明换成「借入」「退款」这类不算收入的账单，开关同样默认关", income_footer == INCOME_FOOTER and switch_checked(page) == "false", f"{income_footer} / {switch_checked(page)}")
    base.close_sheet(page, SPEC)

    base.go_back_to_profile(page)
    wait_secondary_pages_closed(page)


# ---------------------------------------------------------------------------------------------- 造数据


@dataclass
class World:
    ledger1: str = ""
    ledger2: str = ""
    saving_name: str = ""
    saving_id: str = ""
    borrow_name: str = ""
    borrow_id: str = ""
    daily_name: str = ""
    daily_id: str = ""
    bills: "dict[str, str]" = field(default_factory=dict)

    @property
    def saving_bills(self) -> "set[str]":
        return {self.bills[k] for k in ("save1", "save2", "save3", "save4")}

    @property
    def borrow_bills(self) -> "set[str]":
        return {self.bills["borrow"]}

    @property
    def all_bills(self) -> "set[str]":
        return set(self.bills.values())


# 金额（汇总口径用）：存款 500 + 300 + 400（账本甲）+ 700（账本乙）= 1,900；日常 100 + 50（甲）+ 20（乙）= 170；工资 600；借入 2,000
SAVING_TOTAL = 1900
DAILY_TOTAL = 170
SALARY = 600
BORROW = 2000


def seed_base_time() -> datetime:
    """造数的起点：5 分钟前（不早于本月 1 号零点）。后面在界面里记的那一笔账单时间是「现在」（精确到分钟），要比造的账单都新，
    这样「首页最近账单的第一行就是新账单」不取决于脚本跑在哪一秒。"""
    now = datetime.now()
    month_start = now.replace(day=1, hour=0, minute=0, second=0, microsecond=0)
    return max(now - timedelta(minutes=5), month_start)


def seed_world(db: LocalDb, user_id: str) -> World:
    """存款（支出，开关关）、借入（收入，开关开）、日常（支出，开关关）、工资（预设收入）；两个账本。借入那笔最后造，是造的账单里最新的一笔。"""
    w = World()
    base_time = seed_base_time()
    w.ledger1 = db.seed_ledger(user_id, unique_name("账本甲"))
    w.ledger2 = db.seed_ledger(user_id, unique_name("账本乙"))
    w.saving_name = unique_name("存款")
    w.saving_id = db.seed_category(user_id, w.saving_name, sort_order=1)
    w.borrow_name = unique_name("借入")
    w.borrow_id = db.seed_category(user_id, w.borrow_name, bill_type=BILL_TYPE_INCOME, sort_order=2, exclude_from_statistics=True)
    w.daily_name = unique_name("日常")
    w.daily_id = db.seed_category(user_id, w.daily_name, sort_order=3)
    salary_id = db.category_id_by_name("工资薪酬", BILL_TYPE_INCOME)

    def bill(key: str, **kwargs) -> None:
        # 一笔比一笔晚一秒：账单列表按时间倒序，顺序就是造数的反序
        w.bills[key] = db.seed_bill(user_id, when=base_time + timedelta(seconds=len(w.bills)), **kwargs)

    bill("save1", amount=500, ledger_id=w.ledger1, category_id=w.saving_id)
    bill("save2", amount=300, ledger_id=w.ledger1, category_id=w.saving_id)
    bill("save3", amount=400, ledger_id=w.ledger1, category_id=w.saving_id)
    bill("save4", amount=700, ledger_id=w.ledger2, category_id=w.saving_id)
    bill("daily1", amount=100, ledger_id=w.ledger1, category_id=w.daily_id)
    bill("daily2", amount=50, ledger_id=w.ledger1, category_id=w.daily_id)
    bill("daily3", amount=20, ledger_id=w.ledger2, category_id=w.daily_id)
    bill("salary", amount=SALARY, ledger_id=w.ledger1, bill_type=BILL_TYPE_INCOME, category_id=salary_id)
    bill("borrow", amount=BORROW, ledger_id=w.ledger1, bill_type=BILL_TYPE_INCOME, category_id=w.borrow_id)
    return w


# ---------------------------------------------------------------------------------------------- B. 初始口径


def home_amounts(expense, income) -> "dict[str, str]":
    return {"home-expense": plain(expense), "home-income": plain(income), "home-balance": plain(dec(income) - dec(expense))}


def stats_amounts(expense, income) -> "dict[str, str]":
    return {"stats-kpi-expense": plain(expense), "stats-kpi-income": plain(income)}


def bills_amounts() -> "dict[str, str]":
    """账单 Tab 的汇总卡：不受开关影响，全部账单都算（支出 = 存款 + 日常，收入 = 工资 + 借入）。"""
    return {"bills-total-expense": plain(SAVING_TOTAL + DAILY_TOTAL), "bills-total-income": plain(SALARY + BORROW)}


def check_initial_views(run: TestRun, page, w: World) -> None:
    open_app(page, "/")
    wait_tab_bar(page)
    check_amounts(
        run, page, "【首页】本月概览（存款关、借入开）：支出 2,070 含存款、收入 600 不含借入",
        home_amounts(SAVING_TOTAL + DAILY_TOTAL, SALARY),
    )
    check_rows(run, page, "【首页】最近账单：只有「借入」那一行带「不计入统计」标记（其余没有）", w.borrow_bills, at_least=1)

    open_tab(page, "账单")
    check_amounts(run, page, "【账单】汇总卡照旧全含：支出 2,070、收入 2,600（含借入）", bills_amounts())
    ok = poll(page, "() => document.querySelector('[data-testid=\"bills-count\"]')?.getAttribute('data-count') === '9'")
    run.check("【账单】汇总卡笔数 9：被排除的账单也在列表里", ok, str(page.get_by_test_id("bills-count").get_attribute("data-count")))
    check_rows(run, page, "【账单】9 行都显示；只有「借入」那一行带标记，标签文字「不计入统计」，读屏名称以「，不计入统计」结尾", w.borrow_bills, total=9, at_least=1)
    shot(page, "bills-excluded-marker")

    go_stats_monthly(page)
    check_amounts(run, page, "【统计】月度 KPI（存款关、借入开）：支出 2,070、收入 600 不含借入", stats_amounts(SAVING_TOTAL + DAILY_TOTAL, SALARY))
    check_note(
        run, page, "【统计】底部提示：已忽略 1 笔「不计入统计」的账单：借入 +¥2,000.00（只有一个分类时不重复笔数）",
        "stats-excluded", note_text([(w.borrow_name, BORROW, True, 1)]),
    )
    check_note_semantics(run, page, "【统计】提示是 role=note 的一行说明", "stats-excluded")
    shot(page, "stats-excluded-note-initial")


# ---------------------------------------------------------------------------------------------- C. 在分类页拨开关再回来


def check_toggle_propagates(run: TestRun, page, w: World) -> None:
    saving_note = note_text([(w.borrow_name, BORROW, True, 1), (w.saving_name, SAVING_TOTAL, False, 4)])
    only_borrow = note_text([(w.borrow_name, BORROW, True, 1)])
    flagged_all = w.saving_bills | w.borrow_bills

    # ---- 打开存款的开关
    toggle_via_ui(page, w.saving_name, True)
    go_stats_monthly(page)
    check_amounts(run, page, "【打开开关】统计 Tab 自动重读：支出只剩日常 170（存款 1,900 不再计入），收入仍是 600", stats_amounts(DAILY_TOTAL, SALARY))
    check_note(run, page, "【打开开关】统计底部提示多了存款一项（带笔数）：借入 1 笔、存款 4 笔，共 5 笔", "stats-excluded", saving_note)
    tid(page, "stats-excluded").scroll_into_view_if_needed()
    wait_animations_settled(page)
    shot(page, "stats-excluded-note-two")

    open_tab(page, "账单")
    check_amounts(run, page, "【打开开关】账单 Tab 汇总卡不变（仍含存款与借入）", bills_amounts())
    check_rows(run, page, "【打开开关】账单行：存款的 4 笔与借入 1 笔带标记，日常与工资没有", flagged_all, total=9, at_least=5)

    open_tab(page, "首页")
    check_amounts(run, page, "【打开开关】首页本月概览自动重读：支出 170、收入 600、结余 430", home_amounts(DAILY_TOTAL, SALARY))

    # ---- 再关掉：全部回到初始
    toggle_via_ui(page, w.saving_name, False)
    go_stats_monthly(page)
    check_amounts(run, page, "【关闭开关】统计回到：支出 2,070（存款重新计入）、收入 600", stats_amounts(SAVING_TOTAL + DAILY_TOTAL, SALARY))
    check_note(run, page, "【关闭开关】统计底部提示回到只有借入一项", "stats-excluded", only_borrow)
    open_tab(page, "账单")
    check_rows(run, page, "【关闭开关】账单行：只剩借入那一行带标记", w.borrow_bills, total=9, at_least=1)
    open_tab(page, "首页")
    check_amounts(run, page, "【关闭开关】首页本月概览回到：支出 2,070、结余 -1,470", home_amounts(SAVING_TOTAL + DAILY_TOTAL, SALARY))


# ---------------------------------------------------------------------------------------------- D. 统计的账本筛选


def check_ledger_filter(run: TestRun, page, db: LocalDb, w: World) -> None:
    db.set_category_excluded(w.saving_id, True)  # 后面的检查都在「存款已打开开关」的状态下做
    open_app(page, "/statistics")
    wait_tab_bar(page)
    page.wait_for_function("() => { const el = document.querySelector('[data-testid=\"stats-monthly\"]'); return !!el && el.getAttribute('data-state') === 'ready'; }")

    both = note_text([(w.borrow_name, BORROW, True, 1), (w.saving_name, SAVING_TOTAL, False, 4)])
    check_amounts(run, page, "【全部账本】支出 170、收入 600", stats_amounts(DAILY_TOTAL, SALARY))
    check_note(run, page, "【全部账本】提示：借入 1 笔、存款 4 笔（1,900）", "stats-excluded", both)

    pick_stats_ledger(page, w.ledger1)
    check_amounts(run, page, "【账本甲】支出 150（日常 100 + 50）、收入 600", stats_amounts(150, SALARY))
    check_note(
        run, page, "【账本甲】提示只算甲里被排除的：借入 1 笔、存款 3 笔（1,200）",
        "stats-excluded", note_text([(w.borrow_name, BORROW, True, 1), (w.saving_name, 1200, False, 3)]),
    )

    pick_stats_ledger(page, w.ledger2)
    check_amounts(run, page, "【账本乙】支出 20、收入 0", stats_amounts(20, 0))
    check_note(run, page, "【账本乙】提示只有存款 1 笔（700），没有借入", "stats-excluded", note_text([(w.saving_name, 700, False, 1)]))

    pick_stats_ledger(page, None)
    check_amounts(run, page, "【切回全部账本】KPI 与提示都回到全部账本的口径", stats_amounts(DAILY_TOTAL, SALARY))
    check_note(run, page, "【切回全部账本】提示回到借入 + 存款", "stats-excluded", both)


# ---------------------------------------------------------------------------------------------- E. 趋势 / 年度 / 自定义报表


def check_other_views(run: TestRun, page, w: World) -> None:
    both = note_text([(w.borrow_name, BORROW, True, 1), (w.saving_name, SAVING_TOTAL, False, 4)])

    open_app(page, "/statistics?segment=trend")
    wait_tab_bar(page)
    page.wait_for_function("() => document.querySelector('[data-testid=\"stats-trend\"]')?.getAttribute('data-state') === 'ready'")
    check_amounts(
        run, page, "【趋势】最近 7 天：支出 170、收入 600（不含被排除的账单）",
        {"trend-total-expense": plain(DAILY_TOTAL), "trend-total-income": plain(SALARY)},
    )
    check_note(run, page, "【趋势】底部提示与月度同口径（窗口终点含「现在」，今天刚记的账单也算）", "trend-excluded", both)
    check_note_semantics(run, page, "【趋势】提示是 role=note", "trend-excluded")

    open_app(page, "/statistics?segment=annual")
    wait_tab_bar(page)
    page.wait_for_function("() => document.querySelector('[data-testid=\"stats-annual\"]')?.getAttribute('data-state') === 'ready'")
    check_amounts(
        run, page, "【年度】本年：支出 170、收入 600（不含被排除的账单）",
        {"annual-total-expense": plain(DAILY_TOTAL), "annual-total-income": plain(SALARY)},
    )
    check_note(run, page, "【年度】底部提示与月度同口径", "annual-excluded", both)
    check_note_semantics(run, page, "【年度】提示是 role=note", "annual-excluded")

    open_app(page, "/custom-report")
    ready = poll(page, "() => { const b = document.querySelector('[data-testid=\"report-generate\"]'); return !!b && !b.disabled; }", timeout=20000)
    run.check("【自定义报表】页面读完用户与账本，「生成报表」可用", ready)
    tid(page, "report-generate").click()
    got = poll(page, "() => !!document.querySelector('[data-testid=\"report-results\"]')", timeout=20000)
    count = tid(page, "report-results").get_attribute("data-count") if got else None
    run.check("【自定义报表】默认本月：账单数 4（日常 3 + 工资 1，被排除的 5 笔不算）", count == "4", str(count))
    check_note(run, page, "【自定义报表】底部提示与月度同口径（导出 / 分享的数字与屏幕一致，同样不含它们）", "report-excluded", both)
    check_note_semantics(run, page, "【自定义报表】提示是 role=note", "report-excluded")


# ---------------------------------------------------------------------------------------------- F. 预算


def check_budgets(run: TestRun, page, db: LocalDb, user_id: str, w: World) -> None:
    db.seed_budget(user_id, amount=1000)
    db.seed_budget(user_id, amount=500, category_id=w.saving_id)
    db.seed_budget(user_id, amount=300, category_id=w.daily_id)

    open_app(page, "/budgets")
    ok = poll(page, "() => document.querySelector('[data-testid=\"budgets-content\"]')?.getAttribute('data-state') === 'ready' && !!document.querySelector('[data-testid=\"budget-used\"]')", timeout=20000)
    run.check("【预算】页面读完（有总预算与分类预算）", ok)
    wait_animations_settled(page)

    used = tid(page, "budget-used").inner_text().strip() if ok else None
    run.check("【预算】总预算已用 ¥170.00：只算日常，存款的 1,900 不占预算", used == money(DAILY_TOTAL), str(used))

    rows = page.evaluate(
        r"""() => [...document.querySelectorAll('[data-testid="budget-category-group"] [data-testid="budget-row"]')].map((el) => ({
          name: ((el.querySelector('[data-testid="budget-name"]') || {}).textContent || '').trim(),
          remain: ((el.querySelector('[data-testid="budget-remain"]') || {}).textContent || '').trim(),
        }))"""
    )
    by_name = {r["name"]: r["remain"] for r in rows}
    run.check(
        "【预算】给被排除的「存款」设的分类预算：已用 0，剩余 ¥500.00；正常的「日常」预算照常累计（剩余 ¥130.00）",
        by_name.get(w.saving_name) == f"剩余 {money(500)}" and by_name.get(w.daily_name) == f"剩余 {money(130)}",
        str(by_name),
    )

    # 新建分类预算：选项里没有被排除的分类
    tid(page, "budget-category-add").click()
    tid(page, "budget-sheet").wait_for(state="visible")
    wait_animations_settled(page)
    chips = page.evaluate("() => [...document.querySelectorAll('[data-testid=\"budget-form-category-chip\"]')].map((c) => c.textContent.trim())")
    run.check(
        "【新建分类预算】分类选项里有正常的「日常」，没有被排除的「存款」（设了也累计不到）",
        w.daily_name in chips and w.saving_name not in chips,
        str(chips),
    )
    run.check("【新建分类预算】没有选中任何分类时不显示「不会累计」的说明", tid(page, "budget-form-excluded").count() == 0)
    tid(page, "budget-sheet").get_by_test_id("sheet-close").click()
    tid(page, "budget-sheet").wait_for(state="detached")

    # 编辑存款那笔预算：选项里仍有它（选中），并说明
    page.locator('[data-testid="budget-name"]').filter(has_text=re.compile(rf"^{re.escape(w.saving_name)}$")).click()
    tid(page, "budget-sheet").wait_for(state="visible")
    wait_animations_settled(page)
    selected = page.evaluate("() => [...document.querySelectorAll('[data-testid=\"budget-form-category-chip\"][data-selected=\"true\"]')].map((c) => c.textContent.trim())")
    note = tid(page, "budget-form-excluded")
    run.check(
        "【编辑预算】存款的预算：选项里仍有「存款」且选中，下面提示「这个分类设了「不计入统计」，它的账单不会累计到这笔预算里」",
        selected == [w.saving_name] and note.count() == 1 and note.inner_text().strip() == BUDGET_NOTE,
        f"{selected} / {note.count()} / {note.inner_text().strip() if note.count() else None}",
    )
    note.scroll_into_view_if_needed()
    wait_animations_settled(page)
    shot(page, "budget-form-excluded-note")
    tid(page, "budget-sheet").get_by_test_id("sheet-close").click()
    tid(page, "budget-sheet").wait_for(state="detached")


# ---------------------------------------------------------------------------------------------- G. 记一笔里内联新建分类并打开开关


def check_add_bill_inline(run: TestRun, page, db: LocalDb, user_id: str, w: World) -> None:
    # 先把首页与统计都读一遍（常驻的根页面），后面看它们在记账后是否自己更新
    open_app(page, "/")
    wait_tab_bar(page)
    check_amounts(run, page, "【记一笔前】首页本月概览：支出 170、收入 600", home_amounts(DAILY_TOTAL, SALARY))
    go_stats_monthly(page)
    check_amounts(run, page, "【记一笔前】统计：支出 170、收入 600", stats_amounts(DAILY_TOTAL, SALARY))
    open_tab(page, "首页")

    amount = 66
    new_name = unique_name("应急存款")
    sheet = open_add_bill_sheet(page)
    press_keys(page, str(amount))
    tid(page, "category-new").click()
    tid(page, "category-sheet").wait_for(state="visible")
    wait_animations_settled(page)
    run.check("【记一笔·新建分类】共用的分类 Sheet 里同样有「不计入统计」开关，默认关", switch_checked(page) == "false" and tid(page, "category-exclude-row").count() == 1, str(switch_checked(page)))
    tid(page, "category-name").fill(new_name)
    set_switch(page, True)
    wait_toasts_gone(page)
    tid(page, "category-save").click()
    tid(page, "category-sheet").wait_for(state="detached")
    chosen = poll(page, "(n) => document.querySelector('[data-testid=\"category-tile\"][data-selected=\"true\"]')?.getAttribute('data-name') === n", arg=new_name, timeout=8000)
    run.check("【记一笔·新建分类】保存后新分类自动选中，记一笔 Sheet 与金额保留", chosen and sheet.is_visible(), "")
    created = next((c for c in db.custom_categories(user_id) if c["Name"] == new_name), None)
    run.check("【记一笔·新建分类】新分类落库：开关开、支出类型", created is not None and created["ExcludeFromStatistics"] == 1 and created["Type"] == BILL_TYPE_EXPENSE, str(created))

    tid(page, "addbill-save").click()
    sheet.wait_for(state="detached", timeout=10000)
    bill = next((b for b in db.bills_of_user(user_id) if created and b["CategoryId"] == created["Id"]), None)
    run.check("【记一笔·新建分类】账单保存到新分类，金额 66", bill is not None and dec(bill["Amount"]) == amount, str(bill))
    bill_id = bill["Id"] if bill else ""

    # 首页：最新一笔就是它，带标记；本月概览没变
    newest = poll(page, "(id) => document.querySelector('.p-page-container__item:not(.d-none) [data-testid=\"bill-row\"]')?.getAttribute('data-bill-id') === id", arg=bill_id.lower(), timeout=10000)
    run.check("【记一笔后】首页最近账单的第一行就是这笔新账单", newest)
    check_rows(run, page, "【记一笔后】首页最近账单：新账单与借入带「不计入统计」标记，其余没有", {bill_id} | w.borrow_bills, at_least=2)
    check_amounts(run, page, "【记一笔后】首页本月概览不变：支出仍是 170、收入 600（这笔 66 没计入）", home_amounts(DAILY_TOTAL, SALARY))

    # 统计：KPI 不变，提示多一项（3 个分类，没有「等 N 个」）
    go_stats_monthly(page)
    check_amounts(run, page, "【记一笔后】统计 KPI 不变：支出 170、收入 600", stats_amounts(DAILY_TOTAL, SALARY))
    check_note(
        run, page, "【记一笔后】统计提示多了新分类：借入 1 笔、存款 4 笔、新分类 1 笔，共 6 笔",
        "stats-excluded",
        note_text([(w.borrow_name, BORROW, True, 1), (w.saving_name, SAVING_TOTAL, False, 4), (new_name, amount, False, 1)]),
    )

    # 账单：汇总卡含这笔（支出 2,070 + 66），行带标记
    open_tab(page, "账单")
    check_amounts(run, page, "【记一笔后】账单汇总卡含这笔：支出 2,136、收入 2,600", {"bills-total-expense": plain(SAVING_TOTAL + DAILY_TOTAL + amount), "bills-total-income": plain(SALARY + BORROW)})
    check_rows(run, page, "【记一笔后】账单行：新账单、存款 4 笔、借入 1 笔带标记，共 10 行", w.saving_bills | w.borrow_bills | {bill_id}, total=10, at_least=6)


# ---------------------------------------------------------------------------------------------- main


def main() -> int:
    require_server()
    run = TestRun("exclude-from-statistics")
    db = LocalDb()
    db.verify_schema()

    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)

        check_category_form(run, page, db, user_id)
        world = seed_world(db, user_id)
        check_initial_views(run, page, world)
        check_toggle_propagates(run, page, world)
        check_ledger_filter(run, page, db, world)
        check_other_views(run, page, world)
        check_budgets(run, page, db, user_id, world)
        check_add_bill_inline(run, page, db, user_id, world)

        run.check_clean(s.errors)

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
