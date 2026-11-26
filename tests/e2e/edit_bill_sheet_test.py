# -*- coding: utf-8 -*-
"""
E2E 测试：编辑账单与删除（iOS UI 重构 · 16 号 ticket，手机视口）。

前置条件：
  1. pip install -r tests/e2e/requirements.txt && python -m playwright install chromium
  2. FinanceTracker.Web.Server 已用 http 启动配置运行：powershell tests/e2e/server.ps1 -Action start
     （共享账本一节要用假云端，端口 E2E_CLOUD_PORT，默认 5271；端口被占用时该节跳过，其余照常）

编辑账单与新建共用同一个「记一笔」Sheet（AddBillSheetService.OpenEdit）：标题「编辑账单」、各字段预填、右上角留空（没有麦克风），
内容最底部是红字「删除账单」行。旧的编辑页面（/add-bill/{BillId}）已删除，直接访问由服务端 302 到 /?add-bill={BillId}。验证点：

  A. 入口与预填：账单 Tab 点按行、首页最近账单点按、深链 /add-bill/{BillId}（302）都升起同一个编辑 Sheet；金额 / 类型 / 分类 / 账本 /
     支付渠道 / 时间 / 备注全部预填，地址不变（深链除外）、没有麦克风、没有垃圾桶、底部有红字「删除账单」
  B. 编辑保存：改了的字段落库，数据来源不变、同步状态变回待同步；列表与汇总随之更新；没有多出新账单
  C. 没改就保存：照常写库（修改时间更新、同步状态变回待同步），Sheet 收起、Toast「账单已更新」，不再有「没有修改」的提示
  D. 关闭确认：没改直接关；改了先确认（「放弃修改？」继续编辑 / 放弃）；改了又改回原样视为没改
  E. 切换类型：来回切「支出 / 收入」各自回到原来选的分类（新建模式同样）
  F. 删除：底部红字行 → Alert 点名这一笔 → 取消保留 / 确认软删除；Sheet 收起，列表与汇总刷新，Toast；已经被别处删了也有说明；
     编辑期间被别处删掉再点保存：Toast「账单不存在或已删除」，Sheet 收起，列表刷新，不留死表单
  G. 历史无账本账单、账本已被删除的账单：必须先补选账本才能保存（保存键置灰，点按时账本行抖动并弹出账本菜单）
  H. 超支提醒：编辑支出后沿用「预算超支提醒」
  I. 玻璃层预算：编辑 Sheet 打开时屏幕上只剩标题栏的一个玻璃圆钮（关闭，≤ 3 层）
  J. 共享账本：可编辑成员的账单可编辑（账本行带「共享」徽标）；只读成员的账单照常打开编辑 Sheet、没有删除行，保存时才被拒绝
     （「无权限」Toast，不登出、不改库、Sheet 留着；换到自己的账本再存也一样被拒）；服务层同样拒绝只读成员删除（列表里没有删除入口）；
     别人记的账单只读
  K. 不存在 / 已删除 / 非法的地址：提示而不是白屏，不登出

只覆盖网页层——Android / iOS 上的真机手感（Sheet 升起与删除按钮的位置、触觉反馈）见 ticket 16 的人工验收清单。
"""
import sys
import uuid
from datetime import datetime, timedelta

from e2e_base import (
    BASE_URL,
    BILL_TYPE_EXPENSE,
    BILL_TYPE_INCOME,
    ROLE_EDITOR,
    ROLE_VIEWER,
    SOURCE_IMPORT,
    SYNC_PENDING,
    SYNC_SYNCED,
    LocalDb,
    TestRun,
    active_root,
    active_tab,
    addbill_sheet,
    amount_text,
    close_add_bill_sheet,
    login,
    open_add_bill_sheet,
    open_app,
    open_tab,
    pick_category,
    pick_channel,
    pick_ledger,
    press_keys,
    pull_refresh,
    require_server,
    session,
    shot,
    tab_bar,
    to_db_time,
    unique_name,
    wait_animations_settled,
    wait_attr,
)
from fake_cloud import CloudPortBusy, CloudWorld, FakeCloud

# 记一笔 Sheet 的用例里已有的小工具与常量，这里直接复用
from addbill_sheet_test import (
    GLASS_LAYERS_JS,
    SHAKE_OBSERVER_JS,
    SHAKEN_JS,
    SHEET,
    VIEW_W,
    amount_equals,
    attr_is,
    clear_amount,
    gone,
    poll,
    row_value,
    selected_category,
    tid,
    toast,
    visible,
    wait_no_toasts,
)

TIME_FORMAT = "%Y-%m-%dT%H:%M"


# ---------------------------------------------------------------------------------------------- 小工具


def bills_row(page, bill_id: str):
    return page.locator(f'[data-testid="bills-page"] [data-testid="bill-row"][data-bill-id="{bill_id.lower()}"]')


def wait_bills_idle(page) -> None:
    poll(page, "() => document.querySelector('[data-testid=\"bills-page\"]')?.getAttribute('aria-busy') !== 'true'", timeout=15000)
    wait_animations_settled(page)


def show_all_time(page) -> None:
    """账单 Tab 默认只看「本月」（第 2 轮评审）。样本账单的日期是「昨天」「前天」，月初会落到上个月，
    汇总卡也要和库里全部账单的合计对得上——先把时间范围取消（不限时间），与今天几号无关。Tab 根页面常驻，只需做一次。"""
    chip = tid(page, "bills-chip-time")
    chip.wait_for(state="visible", timeout=15000)
    if chip.inner_text().strip() == "时间":
        return
    chip.click()
    tid(page, "filter-sheet").wait_for(state="visible")
    wait_animations_settled(page)
    tid(page, "filter-time-month").click()
    wait_attr(page, '[data-testid="filter-time-month"]', "data-selected", "false")
    tid(page, "filter-apply").click()
    tid(page, "filter-sheet").wait_for(state="detached")
    wait_animations_settled(page)
    poll(page, "() => document.querySelector('[data-testid=\"bills-chip-time\"]')?.textContent.trim() === '时间'", timeout=10000)


def go_bills(page) -> None:
    """切到账单 Tab（取消默认的「本月」时间范围）并下拉刷新（Tab 根页面常驻，界面之外造的数据要自己刷新才看得到）。"""
    open_tab(page, "账单")
    show_all_time(page)
    wait_bills_idle(page)
    pull_refresh(page, "bills-ptr")
    wait_bills_idle(page)


def wait_edit_sheet(page):
    """等编辑 Sheet 升起、数据读完、动画结束；返回 Sheet。"""
    sheet = addbill_sheet(page)
    sheet.wait_for(state="visible")
    wait_attr(page, SHEET, "data-ready", "true", timeout=15000)
    wait_animations_settled(page)
    return sheet


def open_edit_from_bills(page, bill_id: str):
    go_bills(page)
    row = bills_row(page, bill_id)
    row.wait_for(state="visible", timeout=10000)
    row.click()
    return wait_edit_sheet(page)


def expense_total(db: LocalDb, user_id: str) -> str:
    """库里该用户未删除的支出账单合计（两位小数文本）——账单 Tab 汇总卡的「支出」应当等于它。"""
    total = sum(float(b["Amount"]) for b in db.bills_of_user(user_id) if b["Type"] == BILL_TYPE_EXPENSE)
    return f"{total:.2f}"


def wait_summary_expense(page, expected: str) -> bool:
    return poll(page, "(t) => document.querySelector('[data-testid=\"bills-total-expense\"]')?.getAttribute('data-amount') === t", arg=expected, timeout=10000)


def field_values(page) -> dict:
    """编辑 Sheet 里各字段当前的值（给「预填」断言用）。"""
    return {
        "amount": amount_text(page),
        "type": addbill_sheet(page).get_attribute("data-type"),
        "category": selected_category(page),
        "ledger": row_value(page, "ledger-row"),
        "channel": row_value(page, "channel-row"),
        "time": tid(page, "time-row").get_attribute("data-value"),
        "note": tid(page, "addbill-note").input_value(),
    }


def set_note(page, text: str) -> None:
    field = tid(page, "addbill-note")
    field.fill(text)
    field.press("Enter")  # 回车收起系统键盘（change 事件在失焦时才提交到服务端）
    wait_attr(page, SHEET, "data-typing", None)


def save_is_lit(page) -> bool:
    return attr_is(page, '[data-testid="addbill-save"]', "aria-disabled", None, timeout=2000)


def logged_in(page) -> bool:
    return bool(page.evaluate("() => !!localStorage.getItem('userId')")) and page.locator("input[type='tel']").count() == 0 and tab_bar(page).is_visible()


def db_category_name(db: LocalDb, category_id: str) -> str:
    row = db.category_row(category_id)
    return row["Name"]


def close_without_confirm(page) -> bool:
    """点 ✕ 关闭：没有弹确认、Sheet 直接收起才返回 True。"""
    close_add_bill_sheet(page)
    return tid(page, "alert").count() == 0 and addbill_sheet(page).count() == 0


# ---------------------------------------------------------------------------------------------- 造数据


class Ctx:
    """主会话里共用的数据：两个账本，一笔来源为「导入」、已同步的样本账单 X 与一笔陪衬账单。"""

    def __init__(self, db: LocalDb, user_id: str) -> None:
        self.db = db
        self.user_id = user_id
        self.ledger_a = unique_name("编辑账本甲")
        self.ledger_b = unique_name("编辑账本乙")
        self.lid_a = db.seed_ledger(user_id, self.ledger_a)
        self.lid_b = db.seed_ledger(user_id, self.ledger_b, sort_order=1)
        self.cats = db.preset_category_names(BILL_TYPE_EXPENSE)
        self.channels = db.preset_channel_names()
        self.when = (datetime.now() - timedelta(days=1)).replace(hour=15, minute=30, second=0, microsecond=0)
        self.note = unique_name("编辑样本")
        # 来源「导入」+ 已同步：编辑后来源要原样保留、同步状态要变回待同步
        self.x = db.seed_bill(
            user_id,
            amount="88.5",
            ledger_id=self.lid_a,
            note=self.note,
            when=self.when,
            category_id=db.category_id_by_name(self.cats[1]),
            channel_id=db.channel_id_by_name(self.channels[1]),
            source=SOURCE_IMPORT,
            sync_status=SYNC_SYNCED,
        )
        self.filler = db.seed_bill(user_id, amount="11.5", ledger_id=self.lid_a, note=unique_name("陪衬"), when=datetime.now().replace(second=0, microsecond=0))


# ---------------------------------------------------------------------------------------------- A. 入口与预填


def check_entries_and_prefill(run: TestRun, page, db: LocalDb, ctx: Ctx) -> None:
    go_bills(page)
    row = bills_row(page, ctx.x)
    run.check("准备：样本账单在账单 Tab 的列表里", visible(row, 10000))
    row.click()
    sheet = wait_edit_sheet(page)

    run.check("点按账单行：升起同一个记一笔 Sheet，处于编辑模式（data-mode=edit，data-bill-id 是这一笔）", attr_is(page, SHEET, "data-mode", "edit") and attr_is(page, SHEET, "data-bill-id", ctx.x.lower()))
    run.check("地址不变、仍停在账单 Tab（Sheet 是覆盖层，没有压栈成二级页面）", page.evaluate("() => location.pathname") == "/bills" and active_tab(page) == "bills")
    run.check("标题「编辑账单」", tid(page, "sheet-title").inner_text().strip() == "编辑账单")

    values = field_values(page)
    expected_time = ctx.when.strftime(TIME_FORMAT)
    run.check("金额、账单类型预填（88.5 补满两位 → 88.50；支出）", values["amount"] == "88.50" and values["type"] == "expense", str(values))
    run.check("分类、支付渠道预填", values["category"] == ctx.cats[1] and values["channel"] == ctx.channels[1], str(values))
    run.check("账本预填（不是默认账本，而是这笔账单自己的账本）", values["ledger"] == ctx.ledger_a and tid(page, "ledger-row").get_attribute("data-missing") == "false", str(values))
    run.check("时间预填（应用时区的墙上时间，时间行显示「昨天 15:30」）", values["time"] == expected_time and row_value(page, "time-row") == "昨天 15:30", f"{values['time']} / {row_value(page, 'time-row')}")
    run.check("备注预填", values["note"] == ctx.note, str(values))
    run.check("保存键点亮（各项都齐了）", save_is_lit(page))

    run.check("右上角留空：没有麦克风，也没有垃圾桶（标题栏只有关闭）", tid(page, "addbill-voice").count() == 0 and sheet.locator(".ft-addbill__trash").count() == 0 and sheet.locator('.ft-sheet__header [data-testid="addbill-delete"]').count() == 0)
    run.check("没有「语音识别」状态行", tid(page, "addbill-voice-status").count() == 0)
    delete = tid(page, "addbill-delete")
    delete.scroll_into_view_if_needed()
    run.check("内容最底部有「删除账单」行（按钮，读屏读出文字）", visible(delete, 2000) and delete.evaluate("el => el.tagName") == "BUTTON" and delete.inner_text().strip() == "删除账单" and delete.is_enabled())
    delete_color = delete.locator(".ft-cell__title").evaluate("el => getComputedStyle(el).color")
    run.check("「删除账单」是加深的危险红（#D3221A）字，不是品牌色", delete_color == "rgb(211, 34, 26)", delete_color)
    below = page.evaluate(
        """() => {
          const del = document.querySelector('[data-testid="addbill-delete"]').getBoundingClientRect();
          const rows = document.querySelector('[data-testid="addbill-rows"]').getBoundingClientRect();
          return { delTop: del.top, rowsBottom: rows.bottom };
        }"""
    )
    run.check("它在四行设置的下面（自成一组，不夹在设置行中间）", below["delTop"] >= below["rowsBottom"] - 0.5, str(below))
    shot(page, "editbill-prefilled")
    close_add_bill_sheet(page)
    run.check("关闭后 Tab 栏恢复、仍在账单 Tab", tab_bar(page).is_visible() and active_tab(page) == "bills")

    # 首页最近账单
    open_tab(page, "首页")
    pull_refresh(page, "home-ptr")
    home_row = active_root(page).locator(f'[data-testid="bill-row"][data-bill-id="{ctx.x.lower()}"]')
    run.check("准备：首页「最近账单」里有这笔账单", visible(home_row, 10000))
    home_row.click()
    sheet = wait_edit_sheet(page)
    run.check("点按首页最近账单：同一个编辑 Sheet，字段同样预填，地址仍是首页", attr_is(page, SHEET, "data-mode", "edit") and attr_is(page, SHEET, "data-bill-id", ctx.x.lower()) and field_values(page)["amount"] == "88.50" and page.evaluate("() => location.pathname") == "/" and active_tab(page) == "home")
    close_add_bill_sheet(page)

    # 深链
    response = page.request.get(f"{BASE_URL}/add-bill/{ctx.x.lower()}", max_redirects=0)
    run.check("旧地址 /add-bill/{BillId}：服务端 302 到 /?add-bill={BillId}", response.status == 302 and response.headers.get("location") == f"/?add-bill={ctx.x.lower()}", f"{response.status} {response.headers.get('location')}")
    odd = page.request.get(f"{BASE_URL}/add-bill/not-a-guid", max_redirects=0)
    run.check("不是 GUID 的写法（/add-bill/abc）不属于旧地址：不做 302", odd.status != 302, str(odd.status))
    open_app(page, f"/add-bill/{ctx.x.lower()}")
    sheet = wait_edit_sheet(page)
    run.check("深链进入后同样升起编辑 Sheet，地址落在首页", attr_is(page, SHEET, "data-mode", "edit") and attr_is(page, SHEET, "data-bill-id", ctx.x.lower()) and page.evaluate("() => location.pathname") == "/")
    values = field_values(page)
    run.check("深链打开的 Sheet 同样预填（金额 / 分类 / 账本 / 备注）", values["amount"] == "88.50" and values["category"] == ctx.cats[1] and values["ledger"] == ctx.ledger_a and values["note"] == ctx.note, str(values))
    close_add_bill_sheet(page)
    run.check("没有任何账单页面以旧地址入栈（页面栈里没有二级页面）", page.evaluate("() => document.querySelectorAll('.m-page-stack-item').length") == 0)


# ---------------------------------------------------------------------------------------------- B. 编辑保存


def check_edit_save(run: TestRun, page, db: LocalDb, ctx: Ctx) -> dict:
    """返回编辑后账单 X 应有的字段，供后面的小节核对。"""
    wait_no_toasts(page)
    before = db.bill_row(ctx.x)
    count_before = len(db.bills_of_user(ctx.user_id))
    sheet = open_edit_from_bills(page, ctx.x)

    clear_amount(page)
    run.check("长按退格清空金额", amount_text(page) == "")
    run.check("金额清空后保存键置灰（编辑同样要金额）", not save_is_lit(page))
    press_keys(page, "120.25")
    new_category, new_channel = ctx.cats[2], ctx.channels[2]
    pick_category(page, new_category)
    pick_channel(page, new_channel)
    pick_ledger(page, ctx.ledger_b)
    new_when = (datetime.now() - timedelta(days=2)).replace(hour=9, minute=10, second=0, microsecond=0)
    sheet.get_by_test_id("addbill-time").fill(new_when.strftime(TIME_FORMAT))
    wait_attr(page, '[data-testid="time-row"]', "data-value", new_when.strftime(TIME_FORMAT))
    new_note = unique_name("已编辑")
    set_note(page, new_note)
    run.check("改动后保存键点亮", save_is_lit(page))
    shot(page, "editbill-edited")

    tid(page, "addbill-save").click()
    run.check("保存后 Sheet 收起", gone(sheet, 8000))
    run.check("Toast「账单已更新」", toast(page, "账单已更新"))
    run.check("背景页面复位（不再缩小）", poll(page, "() => getComputedStyle(document.querySelector('.m-page-stack')).transform === 'none'"))

    after = db.bill_row(ctx.x)
    run.check("金额改成 120.25，类型仍是支出", amount_equals(after["Amount"], 120.25) and after["Type"] == BILL_TYPE_EXPENSE, str(after["Amount"]))
    run.check("分类 / 支付渠道 / 账本落库为新选的", after["CategoryId"] == db.category_id_by_name(new_category) and after["PaymentChannelId"] == db.channel_id_by_name(new_channel) and after["LedgerId"] == ctx.lid_b)
    run.check("备注落库为新备注", after["Note"] == new_note, str(after["Note"]))
    run.check("时间按应用时区输入、库内落 UTC", after["TransactionTime"] == to_db_time(new_when), f"{after['TransactionTime']} vs {to_db_time(new_when)}")
    run.check("编辑不改变数据来源（仍是「导入」）、仍归当前用户、没有被软删除", after["Source"] == SOURCE_IMPORT and after["UserId"] == ctx.user_id and after["IsDeleted"] == 0, f"{after['Source']} {after['UserId']}")
    run.check("同步状态从「已同步」变回「待同步」", before["SyncStatus"] == SYNC_SYNCED and after["SyncStatus"] == SYNC_PENDING, f"{before['SyncStatus']} -> {after['SyncStatus']}")
    run.check("修改时间更新、创建时间不变、Id 不变", after["UpdatedAt"] != before["UpdatedAt"] and after["CreatedAt"] == before["CreatedAt"] and after["Id"] == before["Id"])
    run.check("没有多出新账单（是更新不是新建）", len(db.bills_of_user(ctx.user_id)) == count_before)

    row = bills_row(page, ctx.x)
    visible(row, 10000)
    text = row.first.inner_text() if row.count() else ""
    run.check("账单 Tab 里这一行随之更新：金额、分类、账本、备注都是新的", "120.25" in text and new_category in text and ctx.ledger_b in text and new_note in text, text)
    run.check("账单 Tab 汇总卡的支出 = 库里的支出合计", wait_summary_expense(page, expense_total(db, ctx.user_id)), expense_total(db, ctx.user_id))
    run.check("编辑后仍停在账单 Tab、地址不变", active_tab(page) == "bills" and page.evaluate("() => location.pathname") == "/bills")

    # 再打开一次：预填的是新内容
    row.click()
    wait_edit_sheet(page)
    values = field_values(page)
    run.check("再次打开：预填的是保存后的内容", values["amount"] == "120.25" and values["category"] == new_category and values["channel"] == new_channel and values["ledger"] == ctx.ledger_b and values["note"] == new_note and values["time"] == new_when.strftime(TIME_FORMAT), str(values))
    close_add_bill_sheet(page)
    return {"category": new_category, "channel": new_channel, "note": new_note, "when": new_when}


# ---------------------------------------------------------------------------------------------- C. 没改就保存


def check_no_change_save(run: TestRun, page, db: LocalDb, ctx: Ctx) -> None:
    """没改动就点保存：照常写库（用户拍板：不再特判成「没有修改」）——账单按原样保存、修改时间更新、重新标记待同步。"""
    wait_no_toasts(page)
    # 用一笔全新造的「已同步」账单：应用的 DbContext 常驻、会缓存读过的账单——从库外改已读过的那笔的同步状态，应用看不到，
    # 没读过的新账单才能还原「已同步的账单没改就保存」这个真实场景（保存后它应当变回待同步、修改时间更新）
    fresh = db.seed_bill(
        ctx.user_id,
        amount="33.5",
        ledger_id=ctx.lid_a,
        note=unique_name("没改就保存"),
        when=datetime.now().replace(second=0, microsecond=0),
        category_id=db.category_id_by_name(ctx.cats[0]),
        channel_id=db.channel_id_by_name(ctx.channels[0]),
        source=SOURCE_IMPORT,
        sync_status=SYNC_SYNCED,
    )
    before = db.bill_row(fresh)
    sheet = open_edit_from_bills(page, fresh)
    tid(page, "addbill-save").click()
    run.check("没改动就点保存：Sheet 收起", gone(sheet, 8000))
    run.check("Toast「账单已更新」（和改了字段的保存一样）", toast(page, "账单已更新"))
    run.check("没有「没有修改」的提示", not toast(page, "没有修改", timeout=500))
    after = db.bill_row(fresh)
    run.check("照常写库：修改时间更新、同步状态变回「待同步」", after["UpdatedAt"] != before["UpdatedAt"] and before["SyncStatus"] == SYNC_SYNCED and after["SyncStatus"] == SYNC_PENDING, f"{before['UpdatedAt']} -> {after['UpdatedAt']}；{before['SyncStatus']} -> {after['SyncStatus']}")
    unchanged = ("Id", "UserId", "Amount", "Type", "CategoryId", "PaymentChannelId", "LedgerId", "TransactionTime", "Note", "Source", "CreatedAt", "IsDeleted")
    run.check("账单内容原样不变（金额 / 分类 / 渠道 / 账本 / 时间 / 备注 / 数据来源 / 创建时间）", all(after[k] == before[k] for k in unchanged), str({k: (before[k], after[k]) for k in unchanged if after[k] != before[k]}))
    run.check("仍是同一笔账单、没有多出新账单", len([b for b in db.bills_of_user(ctx.user_id) if b["Id"] == before["Id"]]) == 1)


# ---------------------------------------------------------------------------------------------- D. 关闭确认


def check_guarded_close(run: TestRun, page, db: LocalDb, ctx: Ctx) -> None:
    wait_no_toasts(page)
    before = db.bill_row(ctx.x)

    # 没改：直接关
    open_edit_from_bills(page, ctx.x)
    close_add_bill_sheet(page)
    run.check("没有改动时点 ✕ 直接收起，不弹确认", tid(page, "alert").count() == 0 and addbill_sheet(page).count() == 0)

    # 改了：先确认
    open_edit_from_bills(page, ctx.x)
    pick_category(page, ctx.cats[3])
    addbill_sheet(page).get_by_test_id("sheet-close").click()
    alert = tid(page, "alert")
    run.check("改了内容再点 ✕：先弹出确认，Sheet 不关", visible(alert) and addbill_sheet(page).is_visible())
    title = tid(page, "alert-title").inner_text().strip()
    message = tid(page, "alert-message").inner_text().strip()
    run.check("确认文案是编辑专用的：「放弃修改？」/ 本次的修改不会保存。 / 继续编辑 / 放弃", title == "放弃修改？" and message == "本次的修改不会保存。" and tid(page, "alert-cancel").inner_text().strip() == "继续编辑" and tid(page, "alert-confirm").inner_text().strip() == "放弃", f"{title} / {message}")
    shot(page, "editbill-discard-alert")
    tid(page, "alert-cancel").click()
    run.check("「继续编辑」：Alert 收起，Sheet 与改动保留", gone(alert) and addbill_sheet(page).is_visible() and selected_category(page) == ctx.cats[3])

    page.mouse.click(VIEW_W / 2, 5)  # Sheet 上沿之上的那条缝：遮罩
    run.check("点遮罩同样先确认（改了才拦）", visible(alert) and addbill_sheet(page).is_visible())
    tid(page, "alert-cancel").click()
    gone(alert)

    addbill_sheet(page).get_by_test_id("sheet-close").click()
    visible(alert)
    tid(page, "alert-confirm").click()
    run.check("「放弃」：Sheet 收起，改动没有保存", gone(addbill_sheet(page)) and db.bill_row(ctx.x) == before)

    # 改了又改回原样：视为没改
    open_edit_from_bills(page, ctx.x)
    set_note(page, unique_name("临时改动"))
    pick_category(page, ctx.cats[3])
    pick_category(page, db_category_name(db, before["CategoryId"]))
    set_note(page, before["Note"])
    close_add_bill_sheet(page)
    run.check("改了又改回原样：点 ✕ 直接收起，不弹确认", tid(page, "alert").count() == 0 and addbill_sheet(page).count() == 0)


# ---------------------------------------------------------------------------------------------- E. 切换类型


def check_type_switch(run: TestRun, page, db: LocalDb, ctx: Ctx) -> None:
    before = db.bill_row(ctx.x)
    own_category = db_category_name(db, before["CategoryId"])
    sheet = open_edit_from_bills(page, ctx.x)
    income = tid(page, "addbill-type").get_by_test_id("segment").filter(has_text="收入")
    income.click()
    run.check("编辑里切到收入：金额保留，分类换成收入类型下的默认项", attr_is(page, SHEET, "data-type", "income") and amount_text(page) == "120.25" and selected_category(page) == db.preset_category_names(BILL_TYPE_INCOME)[0], selected_category(page))
    tid(page, "addbill-type").get_by_test_id("segment").filter(has_text="支出").click()
    run.check("切回支出：回到原来选的分类（来回切不丢）", attr_is(page, SHEET, "data-type", "expense") and selected_category(page) == own_category, f"{selected_category(page)} vs {own_category}")
    close_add_bill_sheet(page)
    run.check("切回原类型 + 原分类后与原账单一致：点 ✕ 不弹确认", tid(page, "alert").count() == 0 and addbill_sheet(page).count() == 0)

    # 新建模式同样
    open_tab(page, "首页")
    open_add_bill_sheet(page)
    picked = ctx.cats[4]
    pick_category(page, picked)
    tid(page, "addbill-type").get_by_test_id("segment").filter(has_text="收入").click()
    attr_is(page, SHEET, "data-type", "income")
    tid(page, "addbill-type").get_by_test_id("segment").filter(has_text="支出").click()
    run.check("新建模式：选过的支出分类，切到收入再切回来仍是它（不被重置成默认项）", attr_is(page, SHEET, "data-type", "expense") and selected_category(page) == picked, selected_category(page))
    close_add_bill_sheet(page)


# ---------------------------------------------------------------------------------------------- F. 删除


def check_delete(run: TestRun, page, db: LocalDb, ctx: Ctx) -> None:
    wait_no_toasts(page)
    note = unique_name("待删除")
    gone_id = db.seed_bill(ctx.user_id, amount="15", ledger_id=ctx.lid_a, note=note, when=datetime.now().replace(second=0, microsecond=0), sync_status=SYNC_SYNCED)
    sheet = open_edit_from_bills(page, gone_id)

    trash = tid(page, "addbill-delete")
    run.check("编辑 Sheet 底部有红字「删除账单」，可点", visible(trash, 2000) and trash.is_enabled() and trash.inner_text().strip() == "删除账单")
    trash.click()
    alert = tid(page, "addbill-delete-alert")
    run.check("点「删除」：先弹确认 Alert，Sheet 还在", visible(alert) and sheet.is_visible())
    title = tid(page, "alert-title").inner_text().strip()
    message = tid(page, "alert-message").inner_text().strip()
    run.check("标题「删除这笔账单？」，确认键是红色的「删除」", title == "删除这笔账单？" and tid(page, "alert-confirm").inner_text().strip() == "删除" and "ft-alert__btn--danger" in (tid(page, "alert-confirm").get_attribute("class") or ""), title)
    run.check("说明点名是哪一笔（分类 · 备注、金额），并告诉用户会随同步传播", note in message and "-¥15.00" in message and "随同步传播" in message, message)
    shot(page, "editbill-delete-alert")

    tid(page, "alert-cancel").click()
    run.check("取消：Alert 收起，Sheet 与账单都在", gone(alert) and sheet.is_visible() and db.bill_row(gone_id)["IsDeleted"] == 0)

    trash.click()
    visible(alert)
    total_before = expense_total(db, ctx.user_id)
    tid(page, "alert-confirm").click()
    run.check("确认删除：Sheet 收起", gone(sheet, 8000))
    run.check("Toast「账单已删除」", toast(page, "账单已删除"))
    row = db.bill_row(gone_id)
    run.check("软删除落库（IsDeleted=1），同步状态变回待同步（删除会随同步传播）", row is not None and row["IsDeleted"] == 1 and row["SyncStatus"] == SYNC_PENDING, str(row))
    run.check("账单行从列表里消失", poll(page, "(id) => !document.querySelector(`[data-testid=\"bills-page\"] [data-testid=\"bill-row\"][data-bill-id=\"${id}\"]`)", arg=gone_id.lower(), timeout=10000))
    total_after = expense_total(db, ctx.user_id)
    run.check("汇总卡的支出同步减少（= 库里未删除账单的合计，比删除前少 15）", wait_summary_expense(page, total_after) and abs(float(total_before) - float(total_after) - 15.0) < 1e-6, f"{total_before} -> {total_after}")
    run.check("背景页面复位、停在账单 Tab", poll(page, "() => getComputedStyle(document.querySelector('.m-page-stack')).transform === 'none'") and active_tab(page) == "bills")

    # 已经被别处删了：说明情况，列表也要刷新
    wait_no_toasts(page)
    ghost_note = unique_name("先被删")
    ghost = db.seed_bill(ctx.user_id, amount="7", ledger_id=ctx.lid_a, note=ghost_note, when=datetime.now().replace(second=0, microsecond=0))
    sheet = open_edit_from_bills(page, ghost)
    with db._connect() as con:
        con.execute('UPDATE "Bills" SET IsDeleted = 1 WHERE Id = ?', (ghost.upper(),))
    tid(page, "addbill-delete").click()
    visible(tid(page, "addbill-delete-alert"))
    tid(page, "alert-confirm").click()
    run.check("账单已被别处删掉再点删除：Sheet 收起并提示「账单不存在或已删除」", gone(sheet, 8000) and toast(page, "账单不存在或已删除"))
    run.check("列表随之刷新（那一行不在了）", poll(page, "(id) => !document.querySelector(`[data-testid=\"bills-page\"] [data-testid=\"bill-row\"][data-bill-id=\"${id}\"]`)", arg=ghost.lower(), timeout=10000))

    # 编辑期间账单被别处删掉、再点保存：同样说明情况并收起，不是「保存失败」，也不让用户对着一笔已不存在的账单继续改
    wait_no_toasts(page)
    stale = db.seed_bill(ctx.user_id, amount="8", ledger_id=ctx.lid_a, note=unique_name("编辑时被删"), when=datetime.now().replace(second=0, microsecond=0))
    sheet = open_edit_from_bills(page, stale)
    clear_amount(page)
    press_keys(page, "9")
    with db._connect() as con:
        con.execute('UPDATE "Bills" SET IsDeleted = 1 WHERE Id = ?', (stale.upper(),))
    tid(page, "addbill-save").click()
    run.check("编辑期间账单被别处删掉后点保存：Sheet 收起并提示「账单不存在或已删除」", gone(sheet, 8000) and toast(page, "账单不存在或已删除"))
    run.check("不是「保存失败」那一类的错误提示", not toast(page, "保存失败", timeout=500))
    stale_row = db.bill_row(stale)
    run.check("库里这笔账单仍是已删除、金额没被改成新值（8）", stale_row["IsDeleted"] == 1 and amount_equals(stale_row["Amount"], 8), str(stale_row))
    run.check("列表随之刷新（那一行不在了）", poll(page, "(id) => !document.querySelector(`[data-testid=\"bills-page\"] [data-testid=\"bill-row\"][data-bill-id=\"${id}\"]`)", arg=stale.lower(), timeout=10000))
    run.check("没有被登出、仍在账单 Tab", logged_in(page) and active_tab(page) == "bills")


# ---------------------------------------------------------------------------------------------- G. 必须补选账本


def check_missing_ledger(run: TestRun, page, db: LocalDb, ctx: Ctx) -> None:
    wait_no_toasts(page)
    note = unique_name("历史无账本")
    legacy = db.seed_legacy_bill(ctx.user_id, amount="42", note=note, when=datetime.now().replace(second=0, microsecond=0))
    sheet = open_edit_from_bills(page, legacy)

    run.check("历史无账本账单：账本行标记缺失、显示「请选择账本」", tid(page, "ledger-row").get_attribute("data-missing") == "true" and row_value(page, "ledger-row") == "请选择账本")
    run.check("其余字段照常预填（金额 42）", amount_text(page) == "42" and field_values(page)["note"] == note)
    run.check("保存键置灰（但没有被禁用，仍可点按引导）", not save_is_lit(page) and tid(page, "addbill-save").get_attribute("disabled") is None)
    shot(page, "editbill-missing-ledger")

    page.evaluate(SHAKE_OBSERVER_JS)
    tid(page, "addbill-save").click(force=True)
    menu = tid(page, "ledger-menu")
    run.check("点按置灰的保存键：弹出账本菜单", visible(menu))
    run.check("账本行抖动（data-shake 出现过）", poll(page, SHAKEN_JS, arg="ft-addbill__anchor", timeout=3000))
    options = tid(page, "ledger-option")
    names = [options.nth(i).inner_text().strip().replace("\n", " ") for i in range(options.count())]
    run.check("菜单里是自己的账本（甲、乙），不含已删除的", len(names) >= 2 and any(ctx.ledger_a in n for n in names) and any(ctx.ledger_b in n for n in names), str(names))
    menu.get_by_test_id("ledger-option").filter(has_text=ctx.ledger_a).click()
    run.check("选了账本：缺失标记消失，保存键点亮", gone(menu) and tid(page, "ledger-row").get_attribute("data-missing") == "false" and row_value(page, "ledger-row") == ctx.ledger_a and save_is_lit(page))

    tid(page, "addbill-save").click()
    run.check("补选账本后保存成功：Sheet 收起，Toast「账单已更新」", gone(sheet, 8000) and toast(page, "账单已更新"))
    row = db.bill_row(legacy)
    run.check("账单有了账本，金额与备注没变", row["LedgerId"] == ctx.lid_a and amount_equals(row["Amount"], 42) and row["Note"] == note, str(row))

    # 账本已经被（别处）删除：当作没有账本，同样要重新选
    wait_no_toasts(page)
    doomed = db.seed_ledger(ctx.user_id, unique_name("将被删账本"))
    orphan_note = unique_name("账本已删")
    orphan = db.seed_bill(ctx.user_id, amount="9", ledger_id=doomed, note=orphan_note, when=datetime.now().replace(second=0, microsecond=0))
    db.soft_delete_ledger(doomed)
    sheet = open_edit_from_bills(page, orphan)
    run.check("账本已被删除的账单：同样标记缺失，必须重新选账本", tid(page, "ledger-row").get_attribute("data-missing") == "true" and row_value(page, "ledger-row") == "请选择账本" and not save_is_lit(page))
    run.check("没改直接关闭：不弹确认", close_without_confirm(page))

    sheet = open_edit_from_bills(page, orphan)
    pick_ledger(page, ctx.ledger_b)
    tid(page, "addbill-save").click()
    run.check("重新选账本后保存成功", gone(sheet, 8000) and db.bill_row(orphan)["LedgerId"] == ctx.lid_b)


# ---------------------------------------------------------------------------------------------- H. 超支提醒


def check_over_budget(run: TestRun, page, db: LocalDb, ctx: Ctx) -> None:
    wait_no_toasts(page)
    db.seed_budget(ctx.user_id, amount=100)
    note = unique_name("编辑超支")
    bill = db.seed_bill(ctx.user_id, amount="10", ledger_id=ctx.lid_a, note=note, when=datetime.now().replace(second=0, microsecond=0))
    open_edit_from_bills(page, bill)
    clear_amount(page)
    press_keys(page, "500")
    tid(page, "addbill-save").click()
    run.check("编辑支出后超出预算：沿用「预算超支提醒」Toast", toast(page, "预算超支提醒", timeout=10000))
    run.check("超支的编辑照常保存", amount_equals(db.bill_row(bill)["Amount"], 500))
    wait_no_toasts(page)


# ---------------------------------------------------------------------------------------------- I. 玻璃层预算


def check_glass_budget(run: TestRun, page, db: LocalDb, ctx: Ctx) -> None:
    wait_no_toasts(page)
    open_edit_from_bills(page, ctx.x)
    wait_animations_settled(page)
    during = page.evaluate(GLASS_LAYERS_JS)
    run.check("账单 Tab 上打开编辑 Sheet：屏幕上只有标题栏的一个玻璃圆钮（关闭；右上角留空，≤ 3 层）", sorted(during) == ["sheet-close"], str(during))
    close_add_bill_sheet(page)

    # 首页「最近账单」只列最新的 5 笔：样本账单 X 是昨天的，前面各节又造了好些「现在」的账单，早就挤出去了——另造一笔最新的来点
    newest = db.seed_bill(ctx.user_id, amount="19.5", ledger_id=ctx.lid_a, note=unique_name("首页编辑"), when=datetime.now().replace(second=0, microsecond=0) + timedelta(minutes=2))
    open_tab(page, "首页")
    pull_refresh(page, "home-ptr")
    row = active_root(page).locator(f'[data-testid="bill-row"][data-bill-id="{newest.lower()}"]')
    visible(row, 10000)
    row.click()
    wait_edit_sheet(page)
    wait_animations_settled(page)
    during = page.evaluate(GLASS_LAYERS_JS)
    run.check("首页上打开编辑 Sheet：同样只剩关闭这一个玻璃圆钮", sorted(during) == ["sheet-close"], str(during))
    close_add_bill_sheet(page)


# ---------------------------------------------------------------------------------------------- J / K. 共享账本、非法地址


def check_shared_and_deep_links(run: TestRun, db: LocalDb) -> None:
    try:
        cloud = FakeCloud().start()
    except CloudPortBusy as exc:
        run.skip("共享账本里的编辑权限", str(exc))
        return

    try:
        with session() as s:
            page = s.page
            info = login(page)
            world = CloudWorld(cloud, db)
            me = world.me(info.phone)
            friend = world.person()
            own_name = unique_name("自有账本")
            own_id = db.seed_ledger(me.local_id, own_name)
            world.own_ledger(me, own_id, own_name)
            editor_name = unique_name("可编辑共享账本")
            viewer_name = unique_name("只读共享账本")
            editor = world.foreign_ledger(friend, editor_name, me=me, my_role=ROLE_EDITOR)
            viewer = world.foreign_ledger(friend, viewer_name, me=me, my_role=ROLE_VIEWER)

            now = datetime.now().replace(second=0, microsecond=0)
            mine_editor_note, mine_viewer_note, theirs_note = unique_name("我在可编辑账本"), unique_name("我在只读账本"), unique_name("别人记的")
            mine_editor = db.seed_bill(me.local_id, amount="21", ledger_id=editor.ledger_id, note=mine_editor_note, when=now)
            mine_viewer = db.seed_bill(me.local_id, amount="22", ledger_id=viewer.ledger_id, note=mine_viewer_note, when=now - timedelta(minutes=1))
            theirs = db.seed_bill(friend.local_id, amount="23", ledger_id=editor.ledger_id, note=theirs_note, when=now - timedelta(minutes=2))

            # ---- J1. 可编辑成员：自己记的账单可以编辑
            sheet = open_edit_from_bills(page, mine_editor)
            run.check("共享账本里自己记的账单（我是可编辑成员）：能打开编辑 Sheet，账本行显示共享账本并带「共享」徽标", attr_is(page, SHEET, "data-mode", "edit") and row_value(page, "ledger-row") == editor_name and visible(tid(page, "ledger-shared-badge"), 3000), row_value(page, "ledger-row"))
            new_note = unique_name("共享编辑")
            set_note(page, new_note)
            tid(page, "addbill-save").click()
            run.check("保存成功：Sheet 收起、Toast「账单已更新」", gone(sheet, 8000) and toast(page, "账单已更新"))
            row = db.bill_row(mine_editor)
            run.check("账单仍在这个共享账本、作者仍是我，备注已更新", (row["LedgerId"] or "").upper() == editor.ledger_id.upper() and row["UserId"] == me.local_id and row["Note"] == new_note, str(row))

            # ---- J2. 只读成员：照常打开编辑 Sheet（打开时不拦），保存时才被拒绝（403 语义，不登出）
            wait_no_toasts(page)
            before = db.bill_row(mine_viewer)
            go_bills(page)
            vrow = bills_row(page, mine_viewer)
            run.check("准备：我在只读共享账本里的这笔账单（我是作者）出现在列表里", visible(vrow, 10000))
            vrow.click()
            sheet = wait_edit_sheet(page)
            run.check(
                "只读成员点按自己的账单：照常升起编辑 Sheet、字段预填（打开时不拦）",
                attr_is(page, SHEET, "data-mode", "edit") and attr_is(page, SHEET, "data-bill-id", mine_viewer.lower()) and amount_text(page) == "22" and row_value(page, "ledger-row") == viewer_name,
                str(field_values(page)),
            )
            run.check("打开时没有任何「无权限」提示", not toast(page, "无权限", timeout=500))
            run.check("只读账本里的账单没有「删除账单」行（服务层同样会拒绝删除）", tid(page, "addbill-delete").count() == 0 and tid(page, "addbill-danger").count() == 0)
            set_note(page, unique_name("只读想改"))
            tid(page, "addbill-save").click()
            run.check("保存时才被拒绝：Toast「无权限编辑这笔账单」", toast(page, "无权限编辑这笔账单", timeout=10000))
            run.check("提示点明原因（只读成员不能在共享账本中记账）", toast(page, "只读成员不能在共享账本中记账"))
            run.check("Sheet 留着（让用户改选账本或放弃）；没有被登出：登录态仍在、仍在账单 Tab", sheet.is_visible() and logged_in(page) and active_tab(page) == "bills")
            run.check("库里的账单原样没变", db.bill_row(mine_viewer) == before)
            shot(page, "editbill-no-permission")

            # 把它挪去自己的账本再存：目标账本的写权限过得了，但服务层认的是原账本——只读成员不能改只读账本里的账单，同样被拒
            wait_no_toasts(page)
            pick_ledger(page, own_name)
            tid(page, "addbill-save").click()
            run.check("换到自己的账本再保存：同样「无权限」，原因是只读成员不能修改共享账本中的账单", toast(page, "无权限编辑这笔账单", timeout=10000) and toast(page, "只读成员不能修改共享账本中的账单"))
            run.check("仍然没有改库（账单还在只读账本里），Sheet 还在，没有被登出", db.bill_row(mine_viewer) == before and sheet.is_visible() and logged_in(page))

            # 放弃修改收起（改过了，会先确认）
            wait_no_toasts(page)
            sheet.get_by_test_id("sheet-close").click()
            visible(tid(page, "alert"))
            tid(page, "alert-confirm").click()
            run.check("放弃修改后 Sheet 收起，库里的账单仍原样没变", gone(addbill_sheet(page), 8000) and db.bill_row(mine_viewer) == before)

            # ---- K1. 别人记的账单：只读（深链）
            wait_no_toasts(page)
            open_app(page, f"/add-bill/{theirs.lower()}")
            run.check("别人记的账单（深链）：提示由账本里的其他成员记录、只有记录它的人可以修改", toast(page, "由账本里的其他成员记录", timeout=15000))
            run.check("别人记的账单：不升起编辑 Sheet，也不登出", gone(addbill_sheet(page), 8000) and logged_in(page))

            # ---- K2. 不存在 / 已删除
            wait_no_toasts(page)
            open_app(page, f"/add-bill/{uuid.uuid4()}")
            run.check("不存在的账单 Id（深链）：提示「账单不存在或已删除」", toast(page, "账单不存在或已删除", timeout=15000))
            run.check("不存在的账单：不升起编辑 Sheet，也不登出", gone(addbill_sheet(page), 8000) and logged_in(page))

            wait_no_toasts(page)
            deleted = db.seed_bill(me.local_id, amount="5", ledger_id=own_id, note=unique_name("已软删除"), when=now)
            with db._connect() as con:
                con.execute('UPDATE "Bills" SET IsDeleted = 1 WHERE Id = ?', (deleted.upper(),))
            open_app(page, f"/add-bill/{deleted.lower()}")
            run.check("已软删除的账单（深链）：同样提示「账单不存在或已删除」", toast(page, "账单不存在或已删除", timeout=15000) and gone(addbill_sheet(page), 8000) and logged_in(page))

            run.check_clean(s.errors, "J / K 节会话无页面异常 / 控制台错误 / 失败请求")
    finally:
        cloud.stop()


# ---------------------------------------------------------------------------------------------- 主流程


def main() -> int:
    require_server()
    run = TestRun("edit-bill-sheet")
    db = LocalDb()
    db.verify_schema()

    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ctx = Ctx(db, user_id)

        check_entries_and_prefill(run, page, db, ctx)
        check_edit_save(run, page, db, ctx)
        check_no_change_save(run, page, db, ctx)
        check_guarded_close(run, page, db, ctx)
        check_type_switch(run, page, db, ctx)
        check_delete(run, page, db, ctx)
        check_missing_ledger(run, page, db, ctx)
        check_over_budget(run, page, db, ctx)
        check_glass_budget(run, page, db, ctx)

        run.check_clean(s.errors, "A–I 节会话无页面异常 / 控制台错误 / 失败请求")

    check_shared_and_deep_links(run, db)

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
