# -*- coding: utf-8 -*-
"""
E2E 测试：无障碍与降级加固（iOS UI 重构 · 23 号 ticket，手机视口）。

前置条件：
  1. pip install -r tests/e2e/requirements.txt && python -m playwright install chromium
  2. FinanceTracker.Web.Server 已用 http 启动配置运行：powershell tests/e2e/server.ps1 -Action start
     （要用 server.ps1 启动：「宿主判定」小节依赖它打开的 E2E 假实现 /__e2e/rendering）

页面探针在 a11y_probes.py（取数的 JS 片段 + 逐屏扫描驱动）。本脚本先造一份「什么都有」的数据（两个自建账本 + 一个加入的共享账本 + 待处理邀请、
四十多天的账单含超长备注 / 巨额金额 / 历史无账本 / 同步失败、总预算与分类预算含超支、待确认账单、自定义分类与支付渠道、错误日志），
再逐屏扫描：四个 Tab（含统计的三个分段）、全部二级页面、各类 Sheet / 弹出菜单 / Alert / Toast / 语音覆盖层、登录页。

小节（不带参数跑全部；带小节名只跑指定小节，例如 `python a11y_degradation_test.py S Z`）：

  M  材质降级：系统「降低透明度」（媒体特性）/ 应用根开关 <html data-ft-solid> / 宿主判定链路（E2E 假实现）三条触发路径，
     结果一致——屏幕上没有任何元素还带 backdrop-filter，所有玻璃表面的底色不透明；关掉后恢复玻璃；降级后 Tab 切换 / 记一笔保存 / 账单列表照常工作
  S  逐屏扫描（100% 字号）：每屏玻璃层 ≤ 3（Toast 是文档化的瞬时例外）；每个可点击元素的实际点击区域 ≥ 44 × 44；
     文字对比度 ≥ 4.5:1（大号文字 ≥ 3:1）；登录页同样扫描
  Z  字号放大 130%：四个 Tab、记一笔 Sheet、各二级页面、登录页没有横向溢出 / 元素越出视口 / 文字被固定高度裁掉，可点击元素仍 ≥ 44 × 44
  R  减弱动态效果：Tab 切换 / 记一笔升起与保存 / 筛选 Sheet / 页面入栈出栈 / Alert / Toast / 语音覆盖层里没有超过 150 ms 的动画或过渡，
     也没有无限循环的装饰动画（加载转圈与光标闪烁是状态指示，放行）
  T  状态不只靠颜色：预算三档、Tab 选中、分段选中、多选勾选、表单错误、账本角色、同步状态、日志级别都有文字 / 图标 / 属性线索

放行清单（HIT_WHITELIST / CONTRAST_WHITELIST / GLASS_NOTES）每一项都写明理由；新增放行项必须同时补理由。
真机才能验收的部分（Android 大字号、低端设备、iOS 系统「降低透明度」/「减弱动态效果」、对比度抽查）见 ticket 23 的人工验收清单。
"""
import os
import sys
from dataclasses import dataclass, field
from datetime import datetime
from typing import Callable, Optional

from playwright.sync_api import Page
from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

import a11y_probes as probes
from e2e_base import (
    ADDBILL_SHEET_SELECTOR,
    BILL_TYPE_EXPENSE,
    BILL_TYPE_INCOME,
    MEMBER_PENDING,
    ROLE_EDITOR,
    ROLE_OWNER,
    ROLE_VIEWER,
    SOURCE_IMPORT,
    SOURCE_MANUAL,
    SOURCE_VOICE,
    SYNC_FAILED,
    SYNC_PENDING,
    SYNC_SYNCED,
    LocalDb,
    LogFiles,
    TestRun,
    days_ago,
    log_entry,
    login,
    open_add_bill_sheet,
    open_app,
    open_tab,
    pull_down,
    rendering_state,
    require_server,
    save_bill_via_sheet,
    session,
    set_rendering,
    unique_name,
    wait_animations_settled,
    wait_attr,
    wait_secondary_pages_closed,
    wait_tab_bar,
)
from voice_overlay_test import FAKE_SPEECH_JS, SPEECH_EXPENSE, say, start_listening, transcript_is

VERBOSE = os.environ.get("A11Y_VERBOSE", "").lower() in ("1", "true", "yes")

#: 玻璃层预算：每屏最多几个带 backdrop-filter 的元素
GLASS_BUDGET = 3
#: 点击热区下限：44 减 1 像素采样误差
HIT_MINIMUM = 43.0
#: 减弱动态效果下放行的最长动画 / 过渡（毫秒）：--ft-dur-reduced 是 100，留 50 的余量
MOTION_THRESHOLD_MS = 150

# ---------------------------------------------------------------------------------------------- 放行清单（每项写明理由）

#: 点击热区放行：标识子串 → 理由。命中的元素不计入「热区 < 44」。
HIT_WHITELIST: "dict[str, str]" = {}

#: 对比度放行：标识 / 文字子串 → 理由。
CONTRAST_WHITELIST: "dict[str, str]" = {}

#: 玻璃层预算的说明：Toast 是瞬时的、盖在最上层的反馈，不计入每屏预算（ADR 0007）。
GLASS_NOTES = "Toast（.m-snack__wrapper）是瞬时的反馈层，不计入每屏玻璃层预算"


# ---------------------------------------------------------------------------------------------- 造数据


@dataclass
class World:
    """一份「什么都有」的数据：扫描每一屏时页面上都有足够多样的内容。"""

    user_id: str
    phone: str
    ledger_main: str
    ledger_trip: str
    ledger_shared: str
    ledger_main_id: str
    ledger_shared_id: str
    bill_ids: "list[str]" = field(default_factory=list)
    legacy_bill_id: str = ""
    failed_bill_id: str = ""
    synced_bill_id: str = ""

    @property
    def edit_bill_id(self) -> str:
        """编辑 Sheet / Alert 场景用的账单：自己的、有账本的一笔。"""
        return self.bill_ids[0]


LONG_NOTE = "这是一条特别特别长的备注，用来检验长文字在列表、详情与编辑里是否正确折行或省略，而不是把布局撑坏、把按钮挤出屏幕之外" * 2
LONG_LEDGER = "旅行基金：一个名字特别特别长长长长长长长长长长长长的账本"


def seed_world(db: LocalDb, login_phone: str) -> World:
    user_id = db.user_id(login_phone)
    ledger_main = "日常开销"
    main_id = db.seed_ledger(user_id, ledger_main, sort_order=0)
    trip_id = db.seed_ledger(user_id, LONG_LEDGER, sort_order=1, icon="mdi-airplane")

    # 一个加入的共享账本（自己是编辑者）+ 一个待处理的邀请
    owner = db.seed_user()
    ledger_shared = "合租账本"
    shared_id = db.seed_ledger(owner, ledger_shared, sort_order=0)
    db.seed_ledger_member(shared_id, owner, role=ROLE_OWNER)
    db.seed_ledger_member(shared_id, user_id, role=ROLE_EDITOR)
    db.seed_ledger_member(shared_id, db.seed_user(), role=ROLE_VIEWER)
    invite_id = db.seed_ledger(owner, "邀请你加入的账本", sort_order=1)
    db.seed_ledger_member(invite_id, owner, role=ROLE_OWNER)
    db.seed_ledger_member(invite_id, user_id, role=ROLE_VIEWER, status=MEMBER_PENDING)

    expense_cats = db.preset_category_names(BILL_TYPE_EXPENSE)
    income_cats = db.preset_category_names(BILL_TYPE_INCOME)
    channels = db.preset_channel_names()
    # 超长名称的自定义分类同时开着「不计入统计」：账单行的小标签、分类页副标题、统计页底部的「已忽略」提示
    # 都以最长的内容出现在每一屏里，让 Z（字号放大 130%）与窄屏扫描一并覆盖它们
    custom_category = db.seed_category(user_id, "我的自定义分类名称特别特别长", exclude_from_statistics=True)
    custom_channel = db.seed_channel(user_id, "我的自定义支付渠道")
    db.set_user_defaults(user_id, ledger_id=main_id)

    ledger_ids = [main_id, trip_id, shared_id]
    amounts = [12.5, 36, 128.8, 6, 2599.99, 45, 18, 300, 79.9, 5.5, 1288, 66.6]
    world = World(user_id, login_phone, ledger_main, LONG_LEDGER, ledger_shared, main_id, shared_id)
    for day in range(46):
        income = day % 9 == 4
        bill_type = BILL_TYPE_INCOME if income else BILL_TYPE_EXPENSE
        names = income_cats if income else expense_cats
        note: Optional[str] = None if day % 4 == 3 else f"备注{day:02d}"
        if day == 1:
            note = LONG_NOTE
        bill_id = db.seed_bill(
            user_id,
            amount=amounts[day % len(amounts)] * (2 if income else 1),
            ledger_id=ledger_ids[day % 3] if day not in (2, 5) else main_id,
            bill_type=bill_type,
            note=note,
            when=days_ago(day, hour=7 + day % 14),
            category_id=db.category_id_by_name(names[day % len(names)], bill_type),
            channel_id=db.channel_id_by_name(channels[day % len(channels)]),
            source=[SOURCE_MANUAL, SOURCE_IMPORT, SOURCE_VOICE][day % 3],
            sync_status=[SYNC_PENDING, SYNC_SYNCED, SYNC_FAILED][day % 3],
        )
        world.bill_ids.append(bill_id)
    # 极端值：巨额、自定义分类 / 渠道、历史无账本、别人在共享账本里记的账
    db.seed_bill(user_id, amount="9999999.99", ledger_id=main_id, note="巨额账单", when=days_ago(0, hour=6))
    db.seed_bill(user_id, amount=88, ledger_id=main_id, note="自定义分类与渠道", when=days_ago(0, hour=5), category_id=custom_category, channel_id=custom_channel)
    world.legacy_bill_id = db.seed_legacy_bill(user_id, amount=23.5, note="历史无账本账单", when=days_ago(1, hour=9))
    world.failed_bill_id = db.seed_bill(user_id, amount=15, ledger_id=main_id, note="同步失败的账单", when=days_ago(0, hour=4), sync_status=SYNC_FAILED)
    world.synced_bill_id = db.seed_bill(user_id, amount=16, ledger_id=main_id, note="已同步的账单", when=days_ago(0, hour=3), sync_status=SYNC_SYNCED)
    db.seed_bill(owner, amount=200, ledger_id=shared_id, note="房东转来的水电费", when=days_ago(2, hour=10))

    # 预算：总预算（超支）+ 分类预算（一个超支、一个宽松）+ 账本预算
    db.seed_budget(user_id, amount=1500)
    db.seed_budget(user_id, amount=300, category_name=expense_cats[0])
    db.seed_budget(user_id, amount=100000, category_name=expense_cats[1])
    db.seed_budget(user_id, amount=900, ledger_id=main_id)

    # 待确认账单（PendingBills 不区分用户、全进程共享：开始前与结束后各清一次）
    db.delete_pending_bills()
    db.seed_pending_bill(amount=18, channel="微信支付", note="星巴克咖啡")
    db.seed_pending_bill(amount=3500, bill_type=BILL_TYPE_INCOME, channel="支付宝", note="工资到账")
    db.seed_pending_bill(amount=99.9, channel="微信支付", note="超市购物", source=2)

    # 错误日志：三种级别各一条（含异常堆栈）
    LogFiles().write([
        log_entry(datetime.now(), "同步失败：{Reason}", level="Error", exception="System.Net.Http.HttpRequestException: 无法连接到远程服务器\n   at SyncService.PushAsync()", properties={"Reason": "超时"}),
        log_entry(datetime.now(), "云端令牌即将过期", level="Warning"),
        log_entry(datetime.now(), "已同步 {Count} 笔账单", level="Information", properties={"Count": 12}),
    ])
    return world


# ---------------------------------------------------------------------------------------------- 场景


@dataclass
class Scene:
    name: str
    group: str  # tab | page | overlay
    open: "Callable[[Page, World], None]"
    scroll: bool = True  # 是否沿最上层的滚动容器（Sheet 自己的滚动区 > 二级页 > Tab 页）多个位置各扫一遍；弹出菜单 / Alert / Toast / 语音覆盖层盖在别的层上，不滚动


def _ready(page: Page) -> None:
    probes.wait_page_ready(page)


def goto(path: str) -> "Callable[[Page, World], None]":
    def opener(page: Page, world: World) -> None:
        open_app(page, path)
        _ready(page)

    return opener


def _sheet_ready(page: Page) -> None:
    page.get_by_test_id("addbill-sheet").wait_for(state="visible")
    wait_attr(page, ADDBILL_SHEET_SELECTOR, "data-ready", "true")
    wait_animations_settled(page)


def open_addbill(page: Page, world: World) -> None:
    open_app(page, "/")
    wait_tab_bar(page)
    _ready(page)
    open_add_bill_sheet(page)


def open_edit_bill(page: Page, world: World) -> None:
    open_app(page, f"/?add-bill={world.edit_bill_id.lower()}")
    _sheet_ready(page)


def open_ledger_menu(page: Page, world: World) -> None:
    open_addbill(page, world)
    page.get_by_test_id("ledger-row").click()
    page.get_by_test_id("ledger-menu").wait_for(state="visible")
    wait_animations_settled(page)


def open_delete_alert(page: Page, world: World) -> None:
    open_edit_bill(page, world)
    page.get_by_test_id("addbill-delete").click()
    page.get_by_test_id("addbill-delete-alert").wait_for(state="visible")
    wait_animations_settled(page)


def open_filter_sheet(page: Page, world: World) -> None:
    open_app(page, "/bills")
    _ready(page)
    page.get_by_test_id("bills-filter-open").click()
    page.get_by_test_id("filter-sheet").wait_for(state="visible")
    wait_animations_settled(page)


def open_select_mode(page: Page, world: World) -> None:
    open_app(page, "/bills")
    _ready(page)
    page.get_by_test_id("bills-select-toggle").click()
    page.get_by_test_id("bills-batchbar").wait_for(state="visible")
    wait_animations_settled(page)


def open_voice(page: Page, world: World) -> None:
    open_app(page, "/")
    wait_tab_bar(page)
    _ready(page)
    start_listening(page)
    say(page, SPEECH_EXPENSE, False)
    transcript_is(page, SPEECH_EXPENSE)
    wait_animations_settled(page)


TOAST_JS = "() => !!document.querySelector('.m-enqueued-snackbars .m-snack__wrapper')"


def open_toast(page: Page, world: World) -> None:
    """账本页新建一个账本：保存成功后弹「账本创建成功」Toast（成功类型）。"""
    open_app(page, "/ledgers")
    _ready(page)
    page.get_by_test_id("ledger-add").click()
    page.get_by_test_id("ledger-sheet").wait_for(state="visible")
    page.get_by_test_id("ledger-name").fill(unique_name("扫描账本"))
    page.get_by_test_id("ledger-save").click()
    page.get_by_test_id("ledger-sheet").wait_for(state="detached")
    page.wait_for_function(TOAST_JS)


def open_confirm_sheet(page: Page, world: World) -> None:
    open_app(page, "/pending-bills")
    _ready(page)
    page.get_by_test_id("pending-card").first.click()
    page.get_by_test_id("confirm-body").wait_for(state="visible")
    wait_animations_settled(page)


TAB_SCENES = [
    Scene("home", "tab", goto("/")),
    Scene("bills", "tab", goto("/bills")),
    Scene("statistics-month", "tab", goto("/statistics")),
    Scene("statistics-trend", "tab", goto("/statistics?segment=trend")),
    Scene("statistics-annual", "tab", goto("/statistics?segment=annual")),
    Scene("profile", "tab", goto("/profile")),
]

PAGE_SCENES = [
    Scene("ledgers", "page", goto("/ledgers")),
    Scene("categories", "page", goto("/categories")),
    Scene("payment-channels", "page", goto("/payment-channels")),
    Scene("budgets", "page", goto("/budgets")),
    Scene("pending-bills", "page", goto("/pending-bills")),
    Scene("sms-recognition", "page", goto("/sms-recognition")),
    Scene("import", "page", goto("/import")),
    Scene("custom-report", "page", goto("/custom-report")),
    Scene("log-viewer", "page", goto("/log-viewer")),
]

OVERLAY_SCENES = [
    Scene("sheet:add-bill", "overlay", open_addbill),
    Scene("sheet:edit-bill", "overlay", open_edit_bill),
    Scene("sheet:filter", "overlay", open_filter_sheet),
    Scene("sheet:confirm-pending", "overlay", open_confirm_sheet),
    Scene("popover:ledger-menu", "overlay", open_ledger_menu, scroll=False),
    Scene("alert:delete-bill", "overlay", open_delete_alert, scroll=False),
    Scene("toast", "overlay", open_toast, scroll=False),
    Scene("voice", "overlay", open_voice, scroll=False),
    Scene("bills:select-mode", "overlay", open_select_mode),
]


def open_members(page: Page, world: World) -> None:
    open_app(page, f"/ledgers/{world.ledger_shared_id}/members")
    _ready(page)


MEMBERS_SCENE = Scene("ledger-members", "page", open_members)


def all_scenes() -> "list[Scene]":
    return TAB_SCENES + PAGE_SCENES + [MEMBERS_SCENE] + OVERLAY_SCENES


# ---------------------------------------------------------------------------------------------- 小工具


def info(*lines: str) -> None:
    """只在 A11Y_VERBOSE=1 时打印的盘点输出。"""
    if VERBOSE:
        for line in lines:
            print("    " + line, flush=True)


def scan_scene(page: Page, scene: Scene, world: World, names: "list[str]") -> "dict[str, list]":
    scene.open(page, world)
    return probes.scan(page, names, scroll=scene.scroll)


def guarded(run: TestRun, label: str, fn: "Callable[[], object]") -> object:
    """场景自身出错（元素找不到、超时）记一条失败并继续扫下一个，不让一个场景打断整轮扫描。"""
    try:
        return fn()
    except Exception as exc:  # noqa: BLE001 - 任何异常都记成失败
        run.check(label, False, f"{type(exc).__name__}: {str(exc)[:300]}")
        return None


def new_world(run: TestRun, db: LocalDb, page: Page) -> World:
    info_ = login(page)
    return seed_world(db, info_.phone)


def reset_rendering() -> None:
    try:
        set_rendering(False)
    except Exception:  # noqa: BLE001 - 服务不是 server.ps1 启动的（没有假实现）时忽略
        pass


# ---------------------------------------------------------------------------------------------- S. 逐屏扫描

CENSUS = ["glass", "hits", "contrast"]


def check_scan(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        s.context.add_init_script(FAKE_SPEECH_JS)
        page = s.page
        world = new_world(run, db, page)
        results: "dict[str, dict]" = {}
        for scene in all_scenes():
            data = guarded(run, f"【扫描】打开场景 {scene.name}", lambda scene=scene: scan_scene(page, scene, world, CENSUS))
            if data is not None:
                results[scene.name] = data
        run.check_clean(s.errors, "【扫描】会话无页面异常 / 控制台错误 / 失败请求")

    with session() as s:  # 登录页：没有登录的会话
        page = s.page
        open_app(page, "/")
        page.wait_for_selector("[data-testid='login-phone']")
        _ready(page)
        results["login"] = probes.scan(page, CENSUS, scroll=True)

    report_glass(run, results)
    report_hits(run, results)
    report_contrast(run, results)


def report_glass(run: TestRun, results: "dict[str, dict]") -> None:
    for name, data in results.items():
        peak = probes.glass_peak(data["glass"])
        toast = [g for g in peak if "m-snack" in g]
        rest = [g for g in peak if g not in toast]
        info(f"玻璃层 {name}: {len(rest)}（Toast {len(toast)}） {rest}")
        run.check(f"【玻璃层】{name}：每屏 ≤ {GLASS_BUDGET} 层（{len(rest)}）", len(rest) <= GLASS_BUDGET, f"{rest}；{GLASS_NOTES}")


def report_hits(run: TestRun, results: "dict[str, dict]") -> None:
    total = 0
    for name, data in results.items():
        bad = probes.hit_violations(data["hits"], minimum=HIT_MINIMUM, whitelist=HIT_WHITELIST)
        total += len(data["hits"])
        info(f"点击热区 {name}: 共 {len(data['hits'])} 个可点击元素，不足 44 的 {len(bad)} 个" + probes.summarize(bad, ["label", "w", "h"], limit=40))
        run.check(
            f"【点击热区】{name}：{len(data['hits'])} 个可点击元素都 ≥ 44 × 44",
            not bad,
            probes.summarize(bad, ["label", "w", "h"]),
        )
    run.check("【点击热区】扫描确实覆盖到了可点击元素（防止探针失灵而「全部通过」）", total >= 150, f"共 {total}")


def report_contrast(run: TestRun, results: "dict[str, dict]") -> None:
    total = 0
    for name, data in results.items():
        bad = probes.contrast_violations(data["contrast"], whitelist=CONTRAST_WHITELIST)
        checked = probes.pixel_checked(data["contrast"])
        total += len(data["contrast"])
        info(f"对比度 {name}: 共 {len(data['contrast'])} 段文字，不达标 {len(bad)} 段（其中 {len(checked)} 段经像素复核）" + probes.summarize(bad, ["label", "text", "ratio", "required", "fg", "bg"], limit=40))
        run.check(
            f"【对比度】{name}：{len(data['contrast'])} 段文字都 ≥ 4.5:1（大号 3:1）",
            not bad,
            probes.summarize(bad, ["label", "text", "ratio", "required", "fg", "bg"]),
        )
    run.check("【对比度】扫描确实覆盖到了文字（防止探针失灵而「全部通过」）", total >= 400, f"共 {total}")


# ---------------------------------------------------------------------------------------------- M. 材质降级

#: 材质降级抽查的场景：覆盖 Tab 栏 / 导航钮 / Hero 小卡（深色玻璃）/ Sheet 的钮 / Alert / Toast / 语音覆盖层这几类玻璃表面
M_SCENES = ["home", "bills", "statistics-month", "profile", "ledgers", "sheet:add-bill", "alert:delete-bill", "toast", "voice"]

#: 三条触发路径必须一致的令牌（取自 MATERIALS_JS 的 tokens）
SOLID_TOKENS = ["--ft-glass-bg", "--ft-glass-filter", "--ft-glass-dark-bg", "--ft-glass-dark-filter", "--ft-glass-alert-bg", "--ft-voice-bg", "--ft-voice-filter"]

SET_SOLID_JS = "(on) => import('/_content/FinanceTracker.Web/js/ui.js').then((m) => { m.setSolid(on); return document.documentElement.getAttribute('data-ft-solid'); })"


def scene_named(name: str) -> Scene:
    for scene in all_scenes():
        if scene.name == name:
            return scene
    raise KeyError(name)


def solid_problems(snapshots: "list[dict]") -> "list[str]":
    """降级后材质体检的违规项（一屏多个滚动位置取并集）：还带 backdrop-filter 的元素；底色不透明度 < 0.99 的玻璃表面。"""
    problems: "list[str]" = []
    for snapshot in snapshots:
        problems += [f"仍有 backdrop-filter：{label}" for label in snapshot["filtered"]]
        problems += [f"玻璃表面底色不透明度 {s['alpha']:.2f}：{s['label']}（{s['color']}）" for s in snapshot["surfaces"] if s["alpha"] < 0.99]
    return sorted(set(problems))


def has_glass(snapshots: "list[dict]") -> bool:
    return any(snapshot["filtered"] for snapshot in snapshots)


def check_solid_scene(run: TestRun, route: str, scene_name: str, data: dict) -> None:
    problems = solid_problems(data["materials"])
    info(f"材质降级[{route}] {scene_name}: {problems or '全部不透明、无 backdrop-filter'}")
    run.check(f"【材质降级·{route}】{scene_name}：没有 backdrop-filter，玻璃表面全部不透明", not problems, "；".join(problems[:6]))
    bad = probes.contrast_violations(data["contrast"], whitelist=CONTRAST_WHITELIST)
    run.check(
        f"【材质降级·{route}】{scene_name}：{len(data['contrast'])} 段文字在不透明表面上仍 ≥ 4.5:1",
        not bad,
        probes.summarize(bad, ["label", "text", "ratio", "required", "fg", "bg"]),
    )


def check_flows_work(run: TestRun, page: Page, world: World, route: str) -> None:
    """降级后的功能不受影响：四个 Tab 都能切换；记一笔保存；保存的账单出现在账单列表里。"""
    open_app(page, "/")
    wait_tab_bar(page)
    _ready(page)
    switched = []
    for title in ("账单", "统计", "我的", "首页"):
        open_tab(page, title)
        switched.append(title)
    run.check(f"【材质降级·{route}】Tab 栏四个 Tab 逐个切换都生效", switched == ["账单", "统计", "我的", "首页"], str(switched))

    note = unique_name("降级保存")
    save_bill_via_sheet(page, amount="23.5", ledger=world.ledger_main, note=note)
    run.check(f"【材质降级·{route}】记一笔照常保存（Sheet 收起）", page.locator(ADDBILL_SHEET_SELECTOR).count() == 0)
    open_app(page, "/bills")
    _ready(page)
    shown = page.get_by_text(note).first
    try:
        shown.wait_for(state="visible", timeout=10_000)
        found = True
    except PlaywrightTimeoutError:
        found = False
    run.check(f"【材质降级·{route}】保存的账单出现在账单列表", found)


def check_materials(run: TestRun, db: LocalDb) -> None:
    # ---- 路径 A：系统「降低透明度」（媒体特性 prefers-reduced-transparency: reduce）----
    media_tokens: "dict[str, str]" = {}
    with session(reduced_transparency=True) as s:
        s.context.add_init_script(FAKE_SPEECH_JS)
        page = s.page
        world = new_world(run, db, page)
        for name in M_SCENES:
            scene = scene_named(name)
            data = guarded(run, f"【材质降级·系统降低透明度】打开场景 {name}", lambda scene=scene: scan_scene(page, scene, world, ["materials", "contrast"]))
            if data is not None:
                check_solid_scene(run, "系统降低透明度", name, data)
                if name == "home":
                    media_tokens = data["materials"][0]["tokens"]
                    run.check("【材质降级·系统降低透明度】根节点没有 data-ft-solid（降级来自媒体特性，不是应用开关）", data["materials"][0]["solid"] is None, str(data["materials"][0]["solid"]))
        guarded(run, "【材质降级·系统降低透明度】功能照常", lambda: check_flows_work(run, page, world, "系统降低透明度"))
        run.check_clean(s.errors, "【材质降级·系统降低透明度】会话无页面异常 / 控制台错误 / 失败请求")

    with session(reduced_transparency=True) as s:  # 登录页（深色玻璃 Logo）
        page = s.page
        open_app(page, "/")
        page.wait_for_selector("[data-testid='login-phone']")
        _ready(page)
        data = probes.scan(page, ["materials", "contrast"], scroll=True)
        check_solid_scene(run, "系统降低透明度", "login", data)

    # ---- 路径 B：应用根开关 <html data-ft-solid="true">（宿主 / 低端设备降级用的同一个开关）----
    with session() as s:
        page = s.page
        new_world(run, db, page)
        open_app(page, "/")
        wait_tab_bar(page)
        _ready(page)
        before = probes.scan(page, ["materials"], scroll=False)["materials"]
        run.check("【材质降级·应用根开关】开关关闭时首页有玻璃（对照组：探针没失灵）", has_glass(before), str(before[0]["filtered"]))
        page.evaluate(SET_SOLID_JS, True)
        probes.settle(page, frames=3)
        after = probes.scan(page, ["materials"], scroll=False)["materials"]
        problems = solid_problems(after)
        run.check("【材质降级·应用根开关】setSolid(true) 后根节点 data-ft-solid=true", after[0]["solid"] == "true", str(after[0]["solid"]))
        run.check("【材质降级·应用根开关】setSolid(true) 后没有 backdrop-filter、玻璃表面全部不透明", not problems, "；".join(problems[:6]))
        same = {name: (media_tokens.get(name), after[0]["tokens"].get(name)) for name in SOLID_TOKENS}
        diff = {name: pair for name, pair in same.items() if pair[0] != pair[1]}
        run.check("【材质降级·应用根开关】与系统媒体特性降级后的令牌取值完全一致（同一个降级结果）", bool(media_tokens) and not diff, str(diff))
        page.evaluate(SET_SOLID_JS, False)
        probes.settle(page, frames=3)
        restored = probes.scan(page, ["materials"], scroll=False)["materials"]
        run.check("【材质降级·应用根开关】setSolid(false) 后属性移除、玻璃恢复", restored[0]["solid"] is None and has_glass(restored), f"{restored[0]['solid']} {restored[0]['filtered']}")
        run.check_clean(s.errors, "【材质降级·应用根开关】会话无页面异常 / 控制台错误 / 失败请求")

    # ---- 路径 C：宿主判定链路（IRenderingProfileService → UiRuntime → 根开关；E2E 假实现 /__e2e/rendering）----
    with session() as s:
        page = s.page
        new_world(run, db, page)
        open_app(page, "/")
        wait_tab_bar(page)
        _ready(page)
        run.check("【材质降级·宿主判定】假实现初始为非降级", rendering_state() == {"solid": False} and page.evaluate("() => document.documentElement.getAttribute('data-ft-solid')") is None)
        set_rendering(True)
        wait_attr(page, "html", "data-ft-solid", "true")
        solid = probes.scan(page, ["materials"], scroll=False)["materials"]
        run.check("【材质降级·宿主判定】宿主报告「偏好实色」后不刷新页面就降级（变化事件即时跟随）", not solid_problems(solid), "；".join(solid_problems(solid)[:6]))
        open_app(page, "/")
        wait_tab_bar(page)
        run.check("【材质降级·宿主判定】重新加载页面后仍是降级状态（首屏就按宿主判定）", page.evaluate("() => document.documentElement.getAttribute('data-ft-solid')") == "true")
        set_rendering(False)
        wait_attr(page, "html", "data-ft-solid", None)
        back = probes.scan(page, ["materials"], scroll=False)["materials"]
        run.check("【材质降级·宿主判定】宿主恢复后不刷新页面就回到玻璃", has_glass(back), str(back[0]["filtered"]))
        run.check_clean(s.errors, "【材质降级·宿主判定】会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- Z. 字号放大 130%

TEXT_ZOOM = 1.3

#: 320 宽（iPhone SE 一类的窄屏）只抽查这些场景：四个 Tab 的代表页、记一笔 / 筛选 Sheet、两个二级页、登录页
Z_NARROW_SCENES = ["home", "bills", "statistics-month", "profile", "budgets", "ledgers", "sheet:add-bill", "sheet:filter"]

REACHABLE_JS = r"""(selectors) => selectors.map((sel) => {
  const els = [...document.querySelectorAll(sel)];
  if (!els.length) return { sel, ok: false, why: 'missing' };
  const bad = [];
  els.forEach((el, i) => {
    const r = el.getBoundingClientRect();
    const inside = r.width >= 1 && r.height >= 1 && r.left >= -0.5 && r.top >= -0.5 && r.right <= innerWidth + 0.5 && r.bottom <= innerHeight + 0.5;
    const t = document.elementFromPoint(Math.min(innerWidth - 1, Math.max(0, r.left + r.width / 2)), Math.min(innerHeight - 1, Math.max(0, r.top + r.height / 2)));
    const hit = !!t && (t === el || el.contains(t));
    if (!inside || !hit) bad.push({ i, inside, hit, rect: [Math.round(r.left), Math.round(r.top), Math.round(r.width), Math.round(r.height)] });
  });
  return { sel, ok: !bad.length, bad };
})"""

#: 放大后必须仍然完整可见、点得到的关键控件（CSS 选择器；Tab 页看 Tab 栏，二级页看返回钮，Sheet 看保存 / 关闭）
KEY_CONTROLS: "dict[str, list[str]]" = {
    "home": ["nav.ft-tabbar [role='tab']", "[data-testid='tab-bar-add']"],
    "bills": ["nav.ft-tabbar [role='tab']"],
    "statistics-month": ["nav.ft-tabbar [role='tab']"],
    "profile": ["nav.ft-tabbar [role='tab']"],
    "sheet:add-bill": ["[data-testid='addbill-save']", "[data-testid='sheet-close']"],
    "sheet:filter": ["[data-testid='sheet-close']"],
    "budgets": ["[data-testid='nav-back']"],
    "ledgers": ["[data-testid='nav-back']"],
    "login": ["[data-testid='login-phone']", "[data-testid='login-submit']"],
}


def zoom_scene(page: Page, scene: Scene, world: World) -> "dict[str, list]":
    scene.open(page, world)
    probes.apply_text_zoom(page, TEXT_ZOOM)
    probes.settle_animations(page)
    data = probes.scan(page, ["overflow", "hits"], scroll=scene.scroll, refine=False)
    keys = KEY_CONTROLS.get(scene.name)
    data["reachable"] = page.evaluate(REACHABLE_JS, keys) if keys else []
    return data


def report_zoom(run: TestRun, width: int, name: str, data: dict) -> None:
    overflow_x = max((snap["overflowX"] for snap in data["overflow"]), default=0)
    outside = probes.dedupe([o for snap in data["overflow"] for o in snap["outside"]], lambda o: o["label"])
    clipped = probes.dedupe([c for snap in data["overflow"] for c in snap["clipped"]], lambda c: c["label"])
    bad_hits = probes.hit_violations(data["hits"], minimum=HIT_MINIMUM, whitelist=HIT_WHITELIST)
    unreachable = [r for r in data["reachable"] if not r["ok"]]
    tag = f"【字号130%·{width}宽】{name}"
    info(f"{tag}: overflowX={overflow_x} 越界={len(outside)} 裁字={len(clipped)} 热区不足={len(bad_hits)} 关键控件不可达={len(unreachable)}")
    run.check(f"{tag}：页面没有横向溢出", overflow_x <= 1, f"scrollWidth - clientWidth = {overflow_x}")
    run.check(f"{tag}：没有元素越出视口左右边", not outside, probes.summarize(outside, ["label", "left", "right"]))
    run.check(f"{tag}：没有文字被固定高度裁掉", not clipped, probes.summarize(clipped, ["label", "scrollH", "clientH", "text"]))
    run.check(f"{tag}：{len(data['hits'])} 个可点击元素仍 ≥ 44 × 44", not bad_hits, probes.summarize(bad_hits, ["label", "w", "h"]))
    run.check(f"{tag}：关键控件完整可见且点得到", not unreachable, str(unreachable))


def check_text_zoom(run: TestRun, db: LocalDb) -> None:
    for width, height, names in ((390, 844, None), (320, 568, Z_NARROW_SCENES)):
        viewport = {"width": width, "height": height}
        with session(viewport=viewport) as s:
            s.context.add_init_script(FAKE_SPEECH_JS)
            page = s.page
            world = new_world(run, db, page)
            for scene in all_scenes():
                if names is not None and scene.name not in names:
                    continue
                data = guarded(run, f"【字号130%·{width}宽】打开场景 {scene.name}", lambda scene=scene: zoom_scene(page, scene, world))
                if data is not None:
                    report_zoom(run, width, scene.name, data)
            run.check_clean(s.errors, f"【字号130%·{width}宽】会话无页面异常 / 控制台错误 / 失败请求")

        with session(viewport=viewport) as s:  # 登录页
            page = s.page
            open_app(page, "/")
            page.wait_for_selector("[data-testid='login-phone']")
            _ready(page)
            probes.apply_text_zoom(page, TEXT_ZOOM)
            probes.settle_animations(page)
            data = probes.scan(page, ["overflow", "hits"], scroll=True, refine=False)
            data["reachable"] = page.evaluate(REACHABLE_JS, KEY_CONTROLS["login"])
            report_zoom(run, width, "login", data)


# ---------------------------------------------------------------------------------------------- R. 减弱动态效果

TOAST_GONE_JS = "() => !document.querySelector('.m-enqueued-snackbars .m-snack__wrapper')"
#: 首页下拉区的状态：离开 idle = 手势被识别、开始刷新；回到 idle 且位移变量清掉 = 收起完毕
PTR_BUSY_JS = "() => { const z = document.querySelector('[data-testid=\"home-ptr\"]'); return !!z && (z.dataset.state || 'idle') !== 'idle'; }"
PTR_IDLE_JS = "() => { const z = document.querySelector('[data-testid=\"home-ptr\"]'); return !!z && (z.dataset.state || 'idle') === 'idle' && !z.style.getPropertyValue('--ft-ptr-offset'); }"


class Recording:
    """with recording(page) as rec: …  退出时 rec.log 是期间发生的全部 CSS 动画 / 过渡。"""

    def __init__(self, page: Page) -> None:
        self.page = page
        self.log: "list[dict]" = []

    def __enter__(self) -> "Recording":
        probes.start_motion_recorder(self.page)
        return self

    def __exit__(self, *exc) -> None:
        self.log = probes.stop_motion_recorder(self.page)


def recording(page: Page) -> Recording:
    return Recording(page)


def report_motion(run: TestRun, label: str, log: "list[dict]") -> None:
    long_ones = probes.long_motions(log, threshold_ms=MOTION_THRESHOLD_MS)
    names = sorted({f"{m['kind']}:{m['name']}@{m['target']}={m['duration']}ms" for m in long_ones})
    info(f"动效 {label}: 记录 {len(log)} 个，超过 {MOTION_THRESHOLD_MS}ms 或无限循环 {len(long_ones)} 个 {names[:12]}")
    run.check(f"【减弱动态效果·{label}】期间没有超过 {MOTION_THRESHOLD_MS} ms 的动画 / 过渡（共记录 {len(log)} 个）", not long_ones, "；".join(names[:10]))


def flow_tabs(page: Page, world: World) -> "list[dict]":
    open_app(page, "/")
    wait_tab_bar(page)
    _ready(page)
    with recording(page) as rec:
        for title in ("账单", "统计", "我的", "首页"):
            open_tab(page, title)
        wait_animations_settled(page)
    return rec.log


def flow_add_bill(page: Page, world: World) -> "list[dict]":
    open_app(page, "/")
    wait_tab_bar(page)
    _ready(page)
    with recording(page) as rec:
        save_bill_via_sheet(page, amount="12.5", ledger=world.ledger_main, note=unique_name("动效"))
        wait_animations_settled(page)
    return rec.log


def flow_filter_sheet(page: Page, world: World) -> "list[dict]":
    open_app(page, "/bills")
    _ready(page)
    with recording(page) as rec:
        page.get_by_test_id("bills-filter-open").click()
        page.get_by_test_id("filter-sheet").wait_for(state="visible")
        wait_animations_settled(page)
        page.get_by_test_id("sheet-close").click()
        page.get_by_test_id("filter-sheet").wait_for(state="detached")
        wait_animations_settled(page)
    return rec.log


def flow_page_stack(page: Page, world: World) -> "list[dict]":
    open_app(page, "/profile")
    wait_tab_bar(page)
    _ready(page)
    with recording(page) as rec:
        # PageStack 只响应它自己的 Push，直接 Blazor.navigateTo 只会改地址栏——要点真实的入口
        page.get_by_test_id("profile-entry-ledgers").click()
        page.get_by_test_id("nav-back").wait_for(state="visible")
        wait_animations_settled(page)
        page.get_by_test_id("nav-back").click()
        wait_secondary_pages_closed(page)
        wait_animations_settled(page)
    return rec.log


def flow_alert(page: Page, world: World) -> "list[dict]":
    open_app(page, f"/?add-bill={world.edit_bill_id.lower()}")
    _sheet_ready(page)
    with recording(page) as rec:
        page.get_by_test_id("addbill-delete").click()
        page.get_by_test_id("addbill-delete-alert").wait_for(state="visible")
        wait_animations_settled(page)
        page.get_by_test_id("alert-cancel").click()
        page.get_by_test_id("addbill-delete-alert").wait_for(state="detached")
        wait_animations_settled(page)
    return rec.log


def flow_toast(page: Page, world: World) -> "list[dict]":
    open_app(page, "/ledgers")
    _ready(page)
    page.get_by_test_id("ledger-add").click()
    page.get_by_test_id("ledger-sheet").wait_for(state="visible")
    page.get_by_test_id("ledger-name").fill(unique_name("动效账本"))
    with recording(page) as rec:
        page.get_by_test_id("ledger-save").click()
        page.get_by_test_id("ledger-sheet").wait_for(state="detached")
        page.wait_for_function(TOAST_JS)
        page.wait_for_function(TOAST_GONE_JS, timeout=15_000)  # Toast 自己消失（含离场动画）
        wait_animations_settled(page)
    return rec.log


def flow_voice(page: Page, world: World) -> "list[dict]":
    open_app(page, "/")
    wait_tab_bar(page)
    _ready(page)
    with recording(page) as rec:
        start_listening(page)
        say(page, SPEECH_EXPENSE, False)
        transcript_is(page, SPEECH_EXPENSE)
        page.get_by_test_id("voice-cancel").click()
        page.get_by_test_id("voice-overlay").wait_for(state="detached")
        wait_animations_settled(page)
    return rec.log


def flow_pull_refresh(page: Page, world: World) -> "list[dict]":
    open_app(page, "/")
    wait_tab_bar(page)
    _ready(page)
    with recording(page) as rec:
        pull_down(page, distance=240, from_y=300)
        page.wait_for_function(PTR_BUSY_JS, timeout=3000)
        page.wait_for_function(PTR_IDLE_JS)
        wait_animations_settled(page)
    return rec.log


MOTION_FLOWS: "list[tuple[str, Callable[[Page, World], list]]]" = [
    ("Tab 切换", flow_tabs),
    ("记一笔升起与保存", flow_add_bill),
    ("筛选 Sheet", flow_filter_sheet),
    ("页面入栈出栈", flow_page_stack),
    ("删除 Alert", flow_alert),
    ("Toast", flow_toast),
    ("语音覆盖层", flow_voice),
    ("下拉刷新", flow_pull_refresh),
]


def check_reduced_motion(run: TestRun, db: LocalDb) -> None:
    # 对照组：同样的 Tab 切换在没有开「减弱动态效果」时有 > 150 ms 的动画——证明录制器是灵敏的，下面的「没有」不是探针失灵
    with session() as s:
        page = s.page
        world = new_world(run, db, page)
        log = flow_tabs(page, world)
        control = probes.long_motions(log, threshold_ms=MOTION_THRESHOLD_MS)
        run.check("【减弱动态效果】对照组：未开启减弱时 Tab 切换录得 > 150 ms 的动画（录制器灵敏）", len(control) > 0, f"共记录 {len(log)} 个")

    with session(reduced_motion=True) as s:
        s.context.add_init_script(FAKE_SPEECH_JS)
        page = s.page
        world = new_world(run, db, page)
        # 静态扫描：每个场景里所有元素（含伪元素）计算样式上的动画 / 过渡
        for scene in all_scenes():
            def sweep(scene=scene) -> None:
                scene.open(page, world)
                probes.settle_animations(page)
                found = page.evaluate(probes.MOTION_JS, [MOTION_THRESHOLD_MS, probes.MOTION_ALLOW])
                found = probes.dedupe(found, lambda m: f"{m['kind']}|{m['where']}|{m['name']}")
                info(f"动效静态扫描 {scene.name}: {len(found)} 项")
                run.check(
                    f"【减弱动态效果·静态扫描】{scene.name}：没有超过 {MOTION_THRESHOLD_MS} ms 的动画 / 过渡 / 无限循环",
                    not found,
                    probes.summarize(found, ["kind", "where", "name", "duration"], limit=8),
                )

            guarded(run, f"【减弱动态效果·静态扫描】打开场景 {scene.name}", sweep)
        # 录制：真实操作过程中出现的动画（含脚本创建的 Web Animations）
        for label, flow in MOTION_FLOWS:
            log = guarded(run, f"【减弱动态效果·{label}】操作流程", lambda flow=flow: flow(page, world))
            if log is not None:
                report_motion(run, label, log)
        run.check_clean(s.errors, "【减弱动态效果】会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- T. 状态不只靠颜色


def check_not_color_only(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        world = new_world(run, db, page)
        user_id = world.user_id

        # ---- Tab 栏选中 ----
        open_app(page, "/")
        wait_tab_bar(page)
        _ready(page)
        tabs = page.evaluate(
            """() => [...document.querySelectorAll("nav.ft-tabbar [role='tab']")].map((t) => ({
                text: t.innerText.trim(), selected: t.getAttribute('aria-selected'),
                rect: (() => { const r = t.getBoundingClientRect(); return [r.left, r.top, r.width, r.height]; })() }))"""
        )
        thumb = page.evaluate(
            """() => { const t = document.querySelector("[data-testid='tab-bar-thumb']"); if (!t) return null; const r = t.getBoundingClientRect(); return [r.left, r.top, r.width, r.height]; }"""
        )
        selected = [t for t in tabs if t["selected"] == "true"]
        run.check("【状态非颜色·Tab 栏】四个 Tab 都有文字标签，且恰好一个 aria-selected=true", len(tabs) == 4 and all(t["text"] for t in tabs) and len(selected) == 1, str(tabs))
        centered = bool(thumb and selected and selected[0]["rect"][0] - 1 <= thumb[0] + thumb[2] / 2 <= selected[0]["rect"][0] + selected[0]["rect"][2] + 1)
        run.check("【状态非颜色·Tab 栏】选中项下面有高光块（形状线索），位置与选中项对得上", centered, f"{thumb} / {selected}")

        # ---- 分段控件 ----
        open_app(page, "/statistics")
        _ready(page)
        segments = page.evaluate(
            """() => [...document.querySelectorAll("[data-testid='segment']")].map((b) => ({ text: b.innerText.trim(), pressed: b.getAttribute('aria-pressed') }))"""
        )
        thumb_present = page.evaluate("() => !!document.querySelector('.ft-seg__thumb')")
        run.check("【状态非颜色·分段控件】统计页的分段每段有文字，恰好一段 aria-pressed=true", len(segments) == 3 and all(x["text"] for x in segments) and sum(x["pressed"] == "true" for x in segments) == 1, str(segments))
        run.check("【状态非颜色·分段控件】选中段下有滑块（形状线索）", thumb_present)

        # ---- 多选 ----
        open_app(page, "/bills")
        _ready(page)
        page.get_by_test_id("bills-select-toggle").click()
        page.get_by_test_id("bills-batchbar").wait_for(state="visible")
        row = page.get_by_test_id("bill-row").first
        off = page.evaluate(
            """() => { const row = document.querySelector("[data-testid='bill-row']"); const c = row.querySelector('.ft-check'); const i = c.querySelector('i');
                return { role: row.getAttribute('role'), checked: row.getAttribute('aria-checked'), glyph: getComputedStyle(i).color, fill: getComputedStyle(c).backgroundColor }; }"""
        )
        row.click()
        wait_attr(page, "[data-testid='bill-row']", "aria-checked", "true")
        wait_animations_settled(page)  # 圈的填充色有淡入过渡：先等它落定，再取最终的颜色
        on = page.evaluate(
            """() => { const row = document.querySelector("[data-testid='bill-row']"); const c = row.querySelector('.ft-check'); const i = c.querySelector('i');
                return { checked: row.getAttribute('aria-checked'), glyph: getComputedStyle(i).color, fill: getComputedStyle(c).backgroundColor }; }"""
        )
        run.check("【状态非颜色·多选】账单行是 role=checkbox，勾选后 aria-checked 变 true", off["role"] == "checkbox" and off["checked"] == "false" and on["checked"] == "true", f"{off} {on}")
        run.check(
            "【状态非颜色·多选】勾选后圈内出现白色对勾（形状）并填满，未勾选是空心圈",
            off["glyph"] == "rgba(0, 0, 0, 0)" and off["fill"] == "rgba(0, 0, 0, 0)" and on["glyph"] == "rgb(255, 255, 255)" and on["fill"] != "rgba(0, 0, 0, 0)",
            f"{off} {on}",
        )

        # ---- 金额：支出 / 收入靠正负号区分，不靠颜色 ----
        amounts = page.evaluate(
            """() => [...document.querySelectorAll("[data-testid='bill-row']")].slice(0, 30).map((r) => ({ income: r.getAttribute('data-income'), text: r.querySelector("[data-testid='bill-amount']").innerText.trim() }))"""
        )
        wrong = [a for a in amounts if not a["text"].startswith("+" if a["income"] == "true" else "-")]
        run.check("【状态非颜色·金额】账单列表里收入带 +、支出带 -（已看 %d 行）" % len(amounts), bool(amounts) and not wrong, str(wrong[:3]))

        # ---- 同步状态 ----
        open_app(page, "/profile")
        _ready(page)
        sync = page.evaluate(
            """() => { const row = document.querySelector("[data-testid='sync-row']"); if (!row) return null;
                return { state: row.getAttribute('data-state'), pending: row.getAttribute('data-pending'), text: row.innerText.replace(/\\s+/g, ' ').trim(), icon: (row.querySelector('i.mdi') || {}).className }; }"""
        )
        run.check("【状态非颜色·同步状态】云端同步行有状态文字（「N 条待同步」）与图标", bool(sync) and sync["state"] == "pending" and "待同步" in sync["text"] and "mdi-cloud" in (sync["icon"] or ""), str(sync))

        # ---- 账本角色 ----
        open_app(page, f"/ledgers/{world.ledger_shared_id}/members")
        _ready(page)
        roles = page.evaluate("() => [...document.querySelectorAll(\"[data-testid='badge-role']\")].map((b) => b.innerText.trim())")
        run.check("【状态非颜色·账本角色】成员的角色用文字徽标（所有者 / 可编辑 / 只读）标出", len(roles) >= 3 and all(r for r in roles) and len(set(roles)) >= 3, str(roles))

        # ---- 日志级别 ----
        open_app(page, "/log-viewer")
        _ready(page)
        levels = page.evaluate(
            """() => [...document.querySelectorAll("[data-level]")].slice(0, 16).map((el) => ({ level: el.getAttribute('data-level'), text: el.innerText.replace(/\\s+/g, ' ').trim() }))"""
        )
        missing = [x for x in levels if x["level"] and x["level"] not in x["text"]]
        run.check("【状态非颜色·日志级别】每条日志 / 级别项的文字里都写着级别名", bool(levels) and not missing, str(missing[:3] or levels[:3]))
        run.check_clean(s.errors, "【状态非颜色】会话无页面异常 / 控制台错误 / 失败请求")

    # ---- 预算三档：图标之外还有「剩余 / 仅剩 / 超支」文字与读屏文案 ----
    with session() as s:
        page = s.page
        info_ = login(page)
        user_id = db.user_id(info_.phone)
        ledger_id = db.seed_ledger(user_id, "预算对照账本")
        db.set_user_defaults(user_id, ledger_id=ledger_id)
        categories = db.preset_category_names(BILL_TYPE_EXPENSE)[:3]
        channel = db.channel_id_by_name(db.preset_channel_names()[0])
        for name, spent, limit in ((categories[0], 10, 100), (categories[1], 90, 100), (categories[2], 150, 100)):
            db.seed_bill(user_id, amount=spent, ledger_id=ledger_id, bill_type=BILL_TYPE_EXPENSE, when=days_ago(0, hour=8),
                         category_id=db.category_id_by_name(name, BILL_TYPE_EXPENSE), channel_id=channel)
            db.seed_budget(user_id, amount=limit, category_name=name)
        db.seed_budget(user_id, amount=200)  # 总预算：已花 250 → 超支
        open_app(page, "/budgets")
        _ready(page)
        rows = page.evaluate(
            """() => [...document.querySelectorAll("[data-testid='budget-row']")].map((r) => ({
                state: r.getAttribute('data-state'), remain: r.querySelector("[data-testid='budget-remain']").innerText.trim(),
                value: (r.querySelector("[role='progressbar']") || { getAttribute: () => null }).getAttribute('aria-valuetext') }))"""
        )
        by_state = {r["state"]: r for r in rows}
        run.check("【状态非颜色·预算】预算行覆盖正常 / 接近上限 / 超支三档", {"normal", "warning", "over"} <= set(by_state), str(rows))
        if {"normal", "warning", "over"} <= set(by_state):
            run.check("【状态非颜色·预算】正常 →「剩余」；接近上限 →「仅剩」；超支 →「超支」", by_state["normal"]["remain"].startswith("剩余") and by_state["warning"]["remain"].startswith("仅剩") and by_state["over"]["remain"].startswith("超支"), str(rows))
            run.check("【状态非颜色·预算】读屏文案带状态（接近上限 / 已超支）", "接近上限" in (by_state["warning"]["value"] or "") and "已超支" in (by_state["over"]["value"] or ""), str(rows))
        overview = page.evaluate(
            """() => { const o = document.querySelector("[data-testid='budget-overview']"); if (!o) return null;
                return { label: o.querySelector("[data-testid='budget-remain-label']").innerText.trim(), caption: o.querySelector("[data-testid='budget-ring-caption']").innerText.trim(),
                         value: (o.querySelector("[role='progressbar']") || { getAttribute: () => null }).getAttribute('aria-valuetext') }; }"""
        )
        run.check("【状态非颜色·预算】总预算圆环：超支时标签是「超支」，读屏文案带「已超支」", bool(overview) and overview["label"] == "超支" and "已超支" in (overview["value"] or ""), str(overview))
        run.check_clean(s.errors, "【状态非颜色·预算】会话无页面异常 / 控制台错误 / 失败请求")

    # ---- 表单错误：文字 + role=alert + aria-invalid（登录页）----
    with session() as s:
        page = s.page
        open_app(page, "/")
        page.wait_for_selector("[data-testid='login-phone']")
        page.get_by_test_id("login-phone").fill("123")
        page.get_by_test_id("login-submit").click()
        error = page.get_by_test_id("login-error")
        # 提示行是常驻的（预留固定高度、没有错误时是空的），Playwright 眼里它一直「可见」——要等的是文字出现（点提交 → 服务端校验 → 回传重绘）
        page.wait_for_function("() => (document.querySelector('[data-testid=\"login-error\"]')?.textContent || '').trim().length > 0")
        run.check(
            "【状态非颜色·表单错误】登录页校验失败：有文字提示（role=alert），输入框 aria-invalid=true",
            bool(error.inner_text().strip()) and error.get_attribute("role") == "alert" and page.get_by_test_id("login-phone").get_attribute("aria-invalid") == "true",
            error.inner_text(),
        )


# ---------------------------------------------------------------------------------------------- 主流程

SECTIONS: "dict[str, Callable[[TestRun, LocalDb], None]]" = {
    "M": check_materials,
    "S": check_scan,
    "Z": check_text_zoom,
    "R": check_reduced_motion,
    "T": check_not_color_only,
}


def main() -> int:
    require_server()
    run = TestRun("a11y-degradation")
    db = LocalDb()
    db.verify_schema()

    wanted = [a.upper() for a in sys.argv[1:]]
    unknown = [a for a in wanted if a not in SECTIONS]
    if unknown:
        print(f"[ENV] 不认识的小节：{unknown}；可选：{list(SECTIONS)}")
        return 2

    reset_rendering()
    try:
        for name, section in SECTIONS.items():
            if not wanted or name in wanted:
                section(run, db)
    finally:
        reset_rendering()
        db.delete_pending_bills()
        try:
            LogFiles().clear()
        except SystemExit:
            pass

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
