# -*- coding: utf-8 -*-
"""
分类管理页 E2E（iOS UI 重构 · 14 号 ticket）：二级页面模板 + 「支出 / 收入」分段 + 「自定义 / 预设」分组 + 底部 Sheet 编辑 + 左滑操作 + 默认分类。

只覆盖网页层——Android / iOS 上的真机手感（左滑跟手、Sheet 弹簧、键盘避让、返回手势）见 ticket 14 的人工验收清单。
与支付渠道页同构，两个页面共用 managed_items_base.py 里按 Spec 参数化的检查（A–J，见该文件），本脚本只在支出分段上跑这些共用检查，
再补分类独有的 K：

  K. 支出 / 收入分段：每个分类只属于一种账单类型——预设组与自定义组随分段切换；在收入分段新建的分类类型是收入、占位提示不同；
     编辑不改类型；默认分类支出 / 收入各有一个、互不影响（菜单与开关的文案随类型变化）；收入分类同样可以删除

可观察属性（已登记到 README）：categories-page、categories-segment（data-value）/ segment、category-add / category-add-row、
category-row[data-category-id][data-open]、category-cell、preset-cell、badge-default、custom-categories / preset-categories、
category-sheet(-host)、category-name / category-sort / category-form-error / category-save、category-default-switch、category-delete-cell、
category-delete-alert、category-preset-sheet / preset-default、swipe-edit / swipe-delete、nav-back / nav-title、icon-picker。
"""
import sys

import managed_items_base as base
from e2e_base import (
    BILL_TYPE_EXPENSE,
    BILL_TYPE_INCOME,
    LocalDb,
    TestRun,
    login,
    require_server,
    session,
    unique_name,
    wait_animations_settled,
    wait_attr,
)
from managed_items_base import Spec

SEGMENT = '[data-testid="categories-segment"]'
EXPENSE = "Expense"
INCOME = "Income"

SPEC = Spec(
    key="category",
    noun="分类",
    entry_label="分类管理",
    entry_id="categories",
    path="/categories",
    page_id="categories-page",
    title="分类管理",
    group_custom="custom-categories",
    group_preset="preset-categories",
    default_icon="mdi-tag",
    icon_count=42,
    icon_a="mdi-paw",
    icon_b="mdi-heart",
    add_label="新建分类",
    preset_footer="预设分类不能修改或删除，点按可设为默认。",
    preset_menu_message="预设分类不能修改或删除",
    preset_menu_label="设为默认支出分类",
    placeholder="例如：宠物",
    seed=lambda db, uid, name, **kw: db.seed_category(uid, name, **kw),
    row=lambda db, item_id: db.category_row(item_id),
    default_id=lambda db, uid: db.default_category_id(uid, BILL_TYPE_EXPENSE),
    customs=lambda db, uid: db.custom_categories(uid),
    visible_total=lambda db, uid: db.visible_category_count(uid),
    seed_bill=lambda db, uid, item_id, ledger_id, note: db.seed_bill(uid, ledger_id=ledger_id, category_id=item_id, note=note),
    preset_id=lambda db, name: db.category_id_by_name(name),
)


def select_segment(page, value: str) -> None:
    page.locator(f'{SEGMENT} [data-testid="segment"][data-value="{value}"]').click()
    wait_attr(page, SEGMENT, "data-value", value)
    wait_animations_settled(page)


def segment_state(page) -> dict:
    return page.evaluate(
        """(sel) => {
          const root = document.querySelector(sel);
          return {
            value: root.getAttribute('data-value'),
            labels: [...root.querySelectorAll('[data-testid="segment"]')].map((b) => b.textContent.trim()),
            pressed: [...root.querySelectorAll('[data-testid="segment"][aria-pressed="true"]')].map((b) => b.dataset.value),
            minHeight: Math.min(...[...root.querySelectorAll('[data-testid="segment"]')].map((b) => b.getBoundingClientRect().height)),
          };
        }""",
        SEGMENT,
    )


def titles(page, which: str) -> "list[str]":
    return [r["title"] for r in (base.rows(page, SPEC, which) or [])]


def custom_row(db: LocalDb, user_id: str, name: str):
    found = [c for c in db.custom_categories(user_id) if c["Name"] == name]
    return found[0] if found else None


# ---------------------------------------------------------------------------------------------- K. 支出 / 收入分段


def check_segments(run: TestRun, page, db: LocalDb, user_id: str) -> None:
    expense_presets = db.preset_category_names(BILL_TYPE_EXPENSE)
    income_presets = db.preset_category_names(BILL_TYPE_INCOME)
    income_seeded = unique_name("兼职收入")
    expense_seeded = unique_name("日常支出")
    db.seed_category(user_id, income_seeded, bill_type=BILL_TYPE_INCOME, sort_order=1, icon="mdi-briefcase")
    db.seed_category(user_id, expense_seeded, bill_type=BILL_TYPE_EXPENSE, sort_order=1)
    base.reload_page(page, SPEC)

    state = segment_state(page)
    run.check("分段控件：「支出 / 收入」两段，默认选中支出", state["labels"] == ["支出", "收入"] and state["value"] == EXPENSE and state["pressed"] == [EXPENSE], str(state))
    run.check("分段按钮点击区域高度 ≥ 44", state["minHeight"] >= 44, str(state["minHeight"]))
    run.check("支出分段：预设组 = 预设支出分类，自定义组有支出的自定义分类、没有收入的",
              titles(page, "preset") == expense_presets and expense_seeded in titles(page, "custom") and income_seeded not in titles(page, "custom"),
              f"{titles(page, 'preset')} {titles(page, 'custom')}")
    base.shot(page, "category-segment-expense")

    select_segment(page, INCOME)
    state = segment_state(page)
    run.check("切到收入：分段选中收入", state["value"] == INCOME and state["pressed"] == [INCOME])
    run.check(f"收入分段：预设组 = 预设收入分类（{len(income_presets)} 个），自定义组只有收入的自定义分类",
              titles(page, "preset") == income_presets and income_seeded in titles(page, "custom") and expense_seeded not in titles(page, "custom"),
              f"{titles(page, 'preset')} {titles(page, 'custom')}")
    run.check("收入分段的预设分类与支出分段互不重叠", not set(income_presets) & set(expense_presets), str(set(income_presets) & set(expense_presets)))
    shown = len(titles(page, "preset")) + len(titles(page, "custom"))
    run.check("收入分段列表条目数 = 库里该用户可见的收入分类数", shown == db.visible_category_count(user_id, BILL_TYPE_INCOME), f"{shown} vs {db.visible_category_count(user_id, BILL_TYPE_INCOME)}")
    base.shot(page, "category-segment-income")

    # --- 在收入分段新建：类型是收入、占位提示不同
    base.open_create(page, SPEC, "nav")
    run.check("收入分段打开新建 Sheet：占位提示是「例如：稿费」", page.get_by_test_id("category-name").get_attribute("placeholder") == "例如：稿费")
    income_name = unique_name("稿费")
    base.fill_form(page, SPEC, income_name, 2)
    base.pick_icon(page, "mdi-cash")
    base.save_form(page, SPEC)
    base.wait_toast(page, SPEC.created_toast)
    base.wait_sheet_gone(page, SPEC)
    base.wait_row(page, SPEC, income_name)
    created = custom_row(db, user_id, income_name)
    run.check("收入分段新建的分类：类型是收入，所有者与图标正确", created is not None and created["Type"] == BILL_TYPE_INCOME
              and created["Icon"] == "mdi-cash" and created["UserId"].upper() == user_id.upper() and created["IsPreset"] == 0, str(created))
    income_id = created["Id"] if created else ""

    select_segment(page, EXPENSE)
    run.check("切回支出：刚建的收入分类不在支出分段", income_name not in titles(page, "custom"))
    select_segment(page, INCOME)
    run.check("再切到收入：它仍在收入分段的自定义组", income_name in titles(page, "custom"))

    # --- 编辑不改类型
    base.open_edit(page, SPEC, income_name)
    renamed = unique_name("改名稿费")
    base.fill_form(page, SPEC, renamed)
    base.save_form(page, SPEC)
    base.wait_toast(page, SPEC.updated_toast)
    base.wait_sheet_gone(page, SPEC)
    base.wait_row(page, SPEC, renamed)
    after = db.category_row(income_id)
    run.check("编辑收入分类：名称更新，类型保持收入", after["Name"] == renamed and after["Type"] == BILL_TYPE_INCOME, str(after))
    run.check("编辑后仍在收入分段，没有跑到支出分段", renamed in titles(page, "custom"))

    # --- 默认分类：支出 / 收入各有一个，互不影响
    run.check("起点：支出、收入都没有默认分类", db.default_category_id(user_id, BILL_TYPE_EXPENSE) is None and db.default_category_id(user_id, BILL_TYPE_INCOME) is None)
    base.open_edit(page, SPEC, renamed)
    subtitle = page.evaluate("""() => { const s = document.querySelector('[data-testid="category-sheet"] .ft-cell__subtitle'); return s ? s.textContent : ''; }""")
    run.check("收入分类的编辑 Sheet：默认开关副标题是「记收入时默认选中这个分类」", subtitle == "记收入时默认选中这个分类", subtitle)
    page.get_by_test_id("category-default-switch").click()
    base.wait_toast(page, SPEC.default_on_toast)
    base.wait_row_badge(page, SPEC, renamed, True)
    run.check("设为默认收入分类：落在「默认收入分类」，不影响默认支出分类", (db.default_category_id(user_id, BILL_TYPE_INCOME) or "").upper() == income_id.upper()
              and db.default_category_id(user_id, BILL_TYPE_EXPENSE) is None)
    base.close_sheet(page, SPEC)

    select_segment(page, EXPENSE)
    badges = [r["title"] for r in base.rows(page, SPEC, "custom") + base.rows(page, SPEC, "preset") if r["badge"]]
    run.check("支出分段看不到收入的默认徽标", badges == [], str(badges))

    # 支出预设设为默认（菜单文案「设为默认支出分类」），不影响收入默认
    preset = titles(page, "preset")[0]
    base.preset_cell(page, preset).first.click()
    menu = page.get_by_test_id("category-preset-sheet")
    menu.wait_for(state="visible")
    wait_animations_settled(page)
    run.check("支出预设项菜单文案：设为默认支出分类", menu.get_by_test_id("preset-default").inner_text().strip() == "设为默认支出分类", menu.inner_text())
    menu.get_by_test_id("preset-default").click()
    base.wait_toast(page, SPEC.default_on_toast)
    base.wait_row_badge(page, SPEC, preset, True, which="preset")
    run.check("设为默认支出分类：落库，收入默认保持不变", (db.default_category_id(user_id, BILL_TYPE_EXPENSE) or "").upper() == db.category_id_by_name(preset).upper()
              and (db.default_category_id(user_id, BILL_TYPE_INCOME) or "").upper() == income_id.upper())

    select_segment(page, INCOME)
    base.wait_row_badge(page, SPEC, renamed, True)
    income_preset = titles(page, "preset")[0]
    base.preset_cell(page, income_preset).first.click()
    menu.wait_for(state="visible")
    wait_animations_settled(page)
    run.check("收入预设项菜单文案：设为默认收入分类", menu.get_by_test_id("preset-default").inner_text().strip() == "设为默认收入分类", menu.inner_text())
    menu.get_by_test_id("preset-default").click()
    base.wait_toast(page, SPEC.default_on_toast)
    base.wait_row_badge(page, SPEC, income_preset, True, which="preset")
    base.wait_row_badge(page, SPEC, renamed, False)
    run.check("收入预设设为默认：取代之前的收入默认，支出默认不受影响",
              (db.default_category_id(user_id, BILL_TYPE_INCOME) or "").upper() == db.category_id_by_name(income_preset, BILL_TYPE_INCOME).upper()
              and (db.default_category_id(user_id, BILL_TYPE_EXPENSE) or "").upper() == db.category_id_by_name(preset).upper())
    base.shot(page, "category-income-default")

    # 清理：取消两个默认
    base.preset_cell(page, income_preset).first.click()
    menu.wait_for(state="visible")
    wait_animations_settled(page)
    menu.get_by_test_id("preset-default").click()
    base.wait_row_badge(page, SPEC, income_preset, False, which="preset")
    select_segment(page, EXPENSE)
    base.preset_cell(page, preset).first.click()
    menu.wait_for(state="visible")
    wait_animations_settled(page)
    menu.get_by_test_id("preset-default").click()
    base.wait_row_badge(page, SPEC, preset, False, which="preset")
    run.check("取消默认后支出、收入都回到「没有默认分类」", db.default_category_id(user_id, BILL_TYPE_EXPENSE) is None and db.default_category_id(user_id, BILL_TYPE_INCOME) is None)

    # --- 收入分类的删除：左滑 → Alert → 软删除；收入分类下有账单时同样拦截
    select_segment(page, INCOME)
    ledger = db.seed_ledger(user_id, unique_name("收入删除用账本"))
    blocked_name = unique_name("有账单收入")
    blocked_id = db.seed_category(user_id, blocked_name, bill_type=BILL_TYPE_INCOME, sort_order=20)
    db.seed_bill(user_id, ledger_id=ledger, bill_type=BILL_TYPE_INCOME, category_id=blocked_id, note=unique_name("收入账单"))
    base.reload_page(page, SPEC)
    select_segment(page, INCOME)
    run.check("收入分类的账单数：有一笔收入账单 →「1 笔」", (base.row_info(page, SPEC, blocked_name) or {}).get("value") == "1 笔", str(base.row_info(page, SPEC, blocked_name)))

    base.swipe_open(page, SPEC, income_id)
    page_tap_delete(page, income_id)
    alert = page.get_by_test_id("category-delete-alert")
    alert.wait_for(state="visible")
    run.check("收入分类的删除确认 Alert 标题带名称", page.get_by_test_id("alert-title").inner_text() == SPEC.delete_title(renamed), page.get_by_test_id("alert-title").inner_text())
    page.get_by_test_id("alert-confirm").click()
    base.wait_toast(page, SPEC.deleted_toast)
    base.wait_row_gone(page, SPEC, renamed)
    alert.wait_for(state="detached")
    run.check("收入分类删除：从收入分段消失，库里是软删除", db.category_row(income_id)["IsDeleted"] == 1 and renamed not in titles(page, "custom"))

    base.swipe_open(page, SPEC, blocked_id)
    page_tap_delete(page, blocked_id)
    alert.wait_for(state="visible")
    page.get_by_test_id("alert-confirm").click()
    base.wait_toast(page, SPEC.blocked_toast)
    alert.wait_for(state="detached")
    run.check("收入分类下有账单：被拦截并保留", db.category_row(blocked_id)["IsDeleted"] == 0 and blocked_name in titles(page, "custom"))


def page_tap_delete(page, item_id: str) -> None:
    base.tap(page, base.row_of(page, SPEC, item_id).get_by_test_id("swipe-delete"))


# ---------------------------------------------------------------------------------------------- main


def main() -> int:
    require_server()
    run = TestRun("categories-page")
    db = LocalDb()
    db.verify_schema()

    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)

        base.check_shell(run, page, db, SPEC, db.preset_category_names(BILL_TYPE_EXPENSE))
        name, item_id = base.check_create(
            run, page, db, user_id, SPEC,
            extra_db_checks=lambda row: run.check("支出分段新建的分类：类型是支出", row["Type"] == BILL_TYPE_EXPENSE, str(row)),
        )
        first, second, second_id = base.check_edit_and_order(run, page, db, user_id, SPEC, name, item_id)
        base.check_default(run, page, db, user_id, SPEC, first, item_id, second, second_id)
        base.check_swipe_and_delete(run, page, db, user_id, SPEC)
        check_segments(run, page, db, user_id)
        base.check_icons_render(run, page, SPEC)
        base.check_visibility(run, page, db, user_id, SPEC, lambda: db.visible_category_count(user_id, BILL_TYPE_EXPENSE))
        base.check_sheet_dismiss(run, page, db, user_id, SPEC)
        base.check_profile_summary(run, page, db, user_id, SPEC)

        run.check_clean(s.errors)

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
