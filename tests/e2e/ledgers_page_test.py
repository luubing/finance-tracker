# -*- coding: utf-8 -*-
"""
E2E 测试：二级页面模板 + 账本管理（iOS UI 重构 · 05 号 ticket）。

只覆盖网页层——Android / iOS 上的真机手感（左滑跟手与松手判定、Sheet 弹簧、玻璃模糊、键盘避让、返回手势）
见 ticket 05 的人工验收清单。共享账本邀请的接受 / 拒绝依赖云端：H 节分两个阶段——先在云端不可用时（假云端未启动）验证
「报错提示 + 邀请卡保留 + 不会登出」，再启动假云端（fake_cloud.py）验证云端业务错误、拒绝成功、接受成功（账本进入「加入的共享账本」）。

  A. 二级页面外壳：Tab 栏隐藏；玻璃导航栏（返回 + 居中内联标题 + 右上角「＋」）；页面背景与导航栏背景；返回按钮 / 边缘右滑返回；
     新用户的空状态（图标 + 说明 + 主操作）
  B. 新建（底部 Sheet）：名称校验（空 / 全空格 / 超长）、选图标、保存后出现在列表；Toast 样式与位置（玻璃胶囊，Tab 栏之上）
  C. 编辑：点按整行进入 Sheet，改名称与排序，列表按排序重排
  D. 设为默认：Sheet 里的开关 → 列表出现「默认」徽标；同一时刻只有一个默认账本；取消默认
  E. 左滑（真实触摸手势）：展开前操作按钮不可点；展开后「共享」「删除」可点；点内容只收起不进入编辑；纵向滑动不展开；
     左滑「共享」→ 操作菜单；左滑「删除」→ Alert 确认 / 取消
  F. 删除：Alert 确认后软删除；账本下有账单时被拦截并给出提示；非滑动入口（Sheet 里的「删除账本」）同样可用
  G. 共享：Sheet 里的「共享账本」操作菜单（邀请成员 → 成员页并可返回；复制账本信息）
  H. 加入的共享账本 / 共享徽标 / 邀请卡（本地与云端各造一份；云端不可用时拒绝给出提示且不登出；云端可用时接受 / 拒绝成功）
  I. Sheet 的收起方式：点遮罩、Esc、关闭按钮、拖拽把手；拖得不够会弹回；弹层盖住整屏（含导航栏）；Alert 在 Sheet 之上
  J. 降级：减少动态效果时 Sheet 入场改为淡入
  K. 沉浸式：模拟刘海（状态栏 47）+ 手势条（34）后，导航栏 / 内容 / Sheet / Toast 都让出安全区，浅色导航栏 → 深色状态栏图标

可观察属性（已登记到 README）：ledgers-page、ledger-add、ledger-row[data-ledger-id][data-open]、ledger-cell、joined-cell、
badge-default / badge-shared / badge-role、invitation-*、ledger-sheet(-host)、ledger-name / ledger-sort、ledger-default-switch、
ledger-share-cell、ledger-delete-cell、ledger-delete-alert、ledger-share-sheet、swipe-share / swipe-delete、nav-back / nav-title。
"""
import re
import sys
import uuid

from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

from e2e_base import (
    GLASS_LAYERS_JS,
    BASE_URL,
    MEMBER_ACTIVE,
    MEMBER_PENDING,
    PHONE_VIEWPORT,
    ROLE_EDITOR,
    ROLE_OWNER,
    ROLE_VIEWER,
    LocalDb,
    TestRun,
    active_root,
    active_tab,
    center_of,
    create_ledger,
    login,
    open_app,
    open_tab,
    require_server,
    session,
    shot,
    swipe,
    swipe_by,
    tap,
    unique_name,
    unique_phone,
    wait_animations_settled,
    wait_attr,
    wait_tab_bar,
    wait_url,
)
from fake_cloud import CloudPortBusy, CloudWorld, FakeCloud, Person

SCREEN_W = PHONE_VIEWPORT["width"]
SCREEN_H = PHONE_VIEWPORT["height"]
TOAST_BOTTOM_GAP = 21 + 62 + 14  # 与 tokens.css 的 --ft-toast-bottom 一致：Tab 栏距底 + 高度 + 14

SHELL_JS = """() => {
  const rect = (el) => {
    if (!el) return null;
    const r = el.getBoundingClientRect();
    return { left: r.left, right: r.right, top: r.top, bottom: r.bottom, width: r.width, height: r.height };
  };
  const bar = document.querySelector('.ft-subbar');
  const title = document.querySelector('[data-testid="nav-title"]');
  const probe = document.createElement('div');
  probe.style.background = 'var(--ft-bg)';
  document.body.appendChild(probe);
  const expectedBg = getComputedStyle(probe).backgroundColor;
  probe.remove();
  return {
    bar: rect(bar),
    barBg: bar && getComputedStyle(bar).backgroundColor,
    pageBg: getComputedStyle(document.querySelector('[data-testid="ledgers-page"]')).backgroundColor,
    expectedBg,
    page: rect(document.querySelector('[data-testid="ledgers-page"]')),
    back: rect(document.querySelector('[data-testid="nav-back"]')),
    add: rect(document.querySelector('[data-testid="ledger-add"]')),
    title: rect(title),
    titleTag: title && title.tagName,
    overflowX: document.documentElement.scrollWidth - document.documentElement.clientWidth,
  };
}"""

TOAST_JS = """() => {
  const el = document.querySelector('.m-enqueued-snackbars .m-snack__wrapper');
  if (!el) return null;
  const r = el.getBoundingClientRect();
  const cs = getComputedStyle(el);
  const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
  return {
    left: r.left, right: r.right, top: r.top, bottom: r.bottom, width: r.width, height: r.height,
    radius: parseFloat(cs.borderTopLeftRadius),
    onTop: !!hit && !!hit.closest('.m-enqueued-snackbars'),
    backdrop: cs.backdropFilter && cs.backdropFilter !== 'none' ? cs.backdropFilter : cs.webkitBackdropFilter,
  };
}"""

CELL_JS = """(testid) => [...document.querySelectorAll(`[data-testid="${testid}"]`)].map((el) => {
  const r = el.getBoundingClientRect();
  const text = (selector) => { const node = el.querySelector(selector); return node ? node.textContent : ''; };
  return { height: r.height, width: r.width, title: text('.ft-cell__title'), subtitle: text('.ft-cell__subtitle') };
})"""

HIT_JS = """([x, y, selector]) => {
  const el = document.elementFromPoint(x, y);
  return !!el && !!el.closest(selector);
}"""


# ---------------------------------------------------------------------------------------------- 小工具


def mask(phone: str) -> str:
    return f"{phone[:3]}****{phone[-4:]}"


def toast(page, text):
    """全局 Toast（MASA 排队消息容器）里包含指定文字的那一条。"""
    return page.locator(".m-enqueued-snackbars").get_by_text(text)


def wait_toast(page, text) -> None:
    toast(page, text).first.wait_for(state="visible")


def cell(page, name):
    return page.get_by_test_id("ledger-cell").filter(has_text=name)


def joined_cell(page, name):
    return page.get_by_test_id("joined-cell").filter(has_text=name)


def row_selector(ledger_id: str) -> str:
    # 页面渲染的 Guid 是小写；库里是大写
    return f'[data-testid="ledger-row"][data-ledger-id="{ledger_id.lower()}"]'


def row_of(page, ledger_id: str):
    return page.locator(row_selector(ledger_id))


def sheet(page):
    return page.get_by_test_id("ledger-sheet")


def wait_sheet(page) -> None:
    sheet(page).wait_for(state="visible")


def wait_sheet_gone(page) -> None:
    sheet(page).wait_for(state="detached")


def expect_sheet_closed(run: TestRun, page, label: str, timeout: int = 8000) -> bool:
    """断言 Sheet 已收起；没收起就记一次失败并强行关掉，避免后面的步骤被遮罩连锁拖垮。"""
    try:
        sheet(page).wait_for(state="detached", timeout=timeout)
        return run.check(label, True)
    except PlaywrightTimeoutError:
        run.check(label, False, "Sheet 没有收起")
        if sheet(page).count():
            page.get_by_test_id("sheet-close").click()
            wait_sheet_gone(page)
        return False


def wait_check(run: TestRun, label: str, page, predicate_js: str, *, arg=None, timeout: int = 8000) -> bool:
    """等到页面内谓词为真就记通过，超时记失败（不抛异常，便于一次看到所有失败项）。"""
    try:
        page.wait_for_function(predicate_js, arg=arg, timeout=timeout)
        return run.check(label, True)
    except PlaywrightTimeoutError:
        return run.check(label, False, "等待超时")


def open_edit(page, name: str) -> None:
    cell(page, name).first.click()
    wait_sheet(page)


def close_sheet(page) -> None:
    page.get_by_test_id("sheet-close").click()
    wait_sheet_gone(page)


def covers(page, point, selector) -> bool:
    return bool(page.evaluate(HIT_JS, [point[0], point[1], selector]))


def default_row(db: LocalDb, user_id: str, ledger_id: str) -> bool:
    current = db.default_ledger_id(user_id)
    return bool(current) and current.upper() == ledger_id.upper()


def swipe_open(page, ledger_id: str, *, steps: int = 10) -> None:
    """触摸左滑展开某一行（真实触摸事件，经 pointer 事件交给 ui.js）。

    松手前先停住一小会儿（速度归零）：Chromium 对「带速度松手」的触摸会起一次惯性滑动，
    CDP 合成的触摸不会让它自然结束，紧接着的第一次点按会被当成「止住惯性」而不产生 click——
    这是测试环境的现象，不是产品行为；真机上惯性滑动会自行结束。停住后松手没有这个副作用，
    落点由位置决定（-170 远超过托盘宽度的一半），不影响本用例要验证的行为。
    """
    target = row_of(page, ledger_id).get_by_test_id("ledger-cell")
    swipe_by(page, target, -170, 0, steps=steps, duration_ms=220, release_hold_ms=150)
    wait_attr(page, row_selector(ledger_id), "data-open", "true")
    wait_animations_settled(page)
    # 松手后 350ms 内行内的 click 会被吞掉（防止松手误触单元格，见 ui.js 的 CLICK_SUPPRESS_MS）；真人不会这么快，脚本要等过去
    page.wait_for_timeout(400)


def go_to_ledgers_from_profile(page) -> None:
    open_tab(page, "我的")
    active_root(page).get_by_text("账本管理", exact=True).first.click()
    wait_url(page, r"^/ledgers$")
    wait_tab_bar(page, visible=False)
    page.get_by_test_id("ledgers-page").wait_for(state="visible")
    wait_animations_settled(page)


# ---------------------------------------------------------------------------------------------- A. 外壳与空状态


def check_shell_and_empty(run: TestRun, page, db: LocalDb) -> None:
    go_to_ledgers_from_profile(page)

    run.check("进入二级页面：Tab 栏隐藏", page.get_by_test_id("tab-bar").is_hidden())
    run.check("导航栏标题是 h1「账本」", page.get_by_test_id("nav-title").inner_text() == "账本")
    info = page.evaluate(SHELL_JS)
    run.check("标题是语义化的 h1", info["titleTag"] == "H1", str(info["titleTag"]))
    run.check("导航栏高 44，贴着屏幕顶（无状态栏安全区时）", abs(info["bar"]["height"] - 44) <= 1 and abs(info["bar"]["top"]) <= 1, str(info["bar"]))
    run.check("标题水平居中（不受两侧按钮影响）", abs((info["title"]["left"] + info["title"]["right"]) / 2 - SCREEN_W / 2) <= 1, str(info["title"]))
    run.check("返回与「＋」按钮点击区域 ≥ 44 × 44", min(info["back"]["width"], info["back"]["height"], info["add"]["width"], info["add"]["height"]) >= 44, f"{info['back']} {info['add']}")
    run.check("返回在左、「＋」在右", info["back"]["left"] < SCREEN_W / 2 < info["add"]["left"])
    run.check("返回按钮有读屏名称「返回」", page.get_by_test_id("nav-back").get_attribute("aria-label") == "返回")
    run.check("「＋」按钮有读屏名称「新建账本」", page.get_by_test_id("ledger-add").get_attribute("aria-label") == "新建账本")
    run.check("导航栏背景 = 页面背景（不透明）", info["barBg"] == info["expectedBg"], f"{info['barBg']} vs {info['expectedBg']}")
    run.check("二级页面内容区背景 = 页面背景令牌", info["pageBg"] == info["expectedBg"], f"{info['pageBg']} vs {info['expectedBg']}")
    run.check("内容在导航栏之下，没有被盖住", info["page"]["top"] >= info["bar"]["bottom"] - 1, f"{info['page']} {info['bar']}")
    run.check("没有横向溢出", info["overflowX"] <= 0, str(info["overflowX"]))
    glass = page.evaluate(GLASS_LAYERS_JS)
    run.check("每屏玻璃层 ≤ 3（导航按钮 2 个）", glass <= 3, str(glass))

    # 新用户：空状态 + 主操作
    page.get_by_test_id("ledgers-empty").wait_for(state="visible")
    empty_text = page.get_by_test_id("ledgers-empty").inner_text()
    run.check("新用户看到空状态：标题与说明", "还没有账本" in empty_text and "账单必须记在某个账本里" in empty_text, empty_text)
    run.check("空状态有「新建账本」主操作", page.get_by_test_id("ledger-empty-add").is_visible())
    run.check("空状态下没有账本列表", page.get_by_test_id("ledger-row").count() == 0)
    shot(page, "ledgers-empty")

    # 返回：按钮 → 回到「我的」根页面，Tab 栏恢复
    page.get_by_test_id("nav-back").click()
    wait_url(page, r"^/profile$")
    wait_tab_bar(page)
    run.check("点返回回到「我的」，Tab 栏恢复", active_tab(page) == "profile")

    # 返回：左边缘右滑（页面栈自带手势，换成玻璃导航栏后仍然有效）
    go_to_ledgers_from_profile(page)
    swipe(page, (4, 420), (260, 420), steps=12, duration_ms=240)
    wait_url(page, r"^/profile$")
    wait_tab_bar(page)
    run.check("左边缘右滑返回仍然有效", active_tab(page) == "profile")

    go_to_ledgers_from_profile(page)


# ---------------------------------------------------------------------------------------------- B. 新建


def check_create(run: TestRun, page, db: LocalDb, user_id: str) -> str:
    """新建账本，返回账本名称。"""
    page.get_by_test_id("ledger-empty-add").click()
    wait_sheet(page)
    wait_animations_settled(page)

    run.check("新建 Sheet 标题是「新建账本」", page.get_by_test_id("sheet-title").inner_text() == "新建账本")
    run.check("新建时没有「设为默认 / 共享 / 删除」", page.get_by_test_id("ledger-default-switch").count() == 0
              and page.get_by_test_id("ledger-share-cell").count() == 0
              and page.get_by_test_id("ledger-delete-cell").count() == 0)
    anim = page.evaluate("() => getComputedStyle(document.querySelector('[data-testid=\"ledger-sheet\"]')).animationName")
    run.check("普通模式：Sheet 入场动画是自底部弹起（ft-sheet-in）", anim == "ft-sheet-in", anim)
    run.check("Sheet 是对话框语义（role=dialog + aria-modal）",
              sheet(page).get_attribute("role") == "dialog" and sheet(page).get_attribute("aria-modal") == "true")
    run.check("打开后焦点交给弹层容器（Esc 可用，不弹出键盘）", page.evaluate("() => document.activeElement && document.activeElement.dataset.testid") == "ledger-sheet-host")
    maxlength = page.get_by_test_id("ledger-name").get_attribute("maxlength")
    run.check("名称输入框限制最多 50 个字（maxlength）", maxlength == "50", str(maxlength))
    glass = page.evaluate(GLASS_LAYERS_JS)
    run.check("Sheet 打开时玻璃层仍 ≤ 3", glass <= 3, str(glass))
    shot(page, "ledgers-sheet-create")

    # 校验：空、全空格、超长（用脚本绕过 maxlength）
    page.get_by_test_id("ledger-save").click()
    page.get_by_test_id("ledger-form-error").wait_for(state="visible")
    run.check("名称为空：提示「请输入账本名称」，Sheet 不关闭", page.get_by_test_id("ledger-form-error").inner_text() == "请输入账本名称" and sheet(page).is_visible())
    run.check("校验失败不会创建账本", len(db.ledgers_of_user(user_id)) == 0)

    page.get_by_test_id("ledger-name").fill("   ")
    page.get_by_test_id("ledger-save").click()
    page.get_by_test_id("ledger-form-error").wait_for(state="visible")
    run.check("名称全是空格：同样提示", page.get_by_test_id("ledger-form-error").inner_text() == "请输入账本名称")

    page.get_by_test_id("ledger-name").evaluate(
        "(el) => { el.value = 'x'.repeat(51); el.dispatchEvent(new Event('input', { bubbles: true })); }"
    )
    page.get_by_test_id("ledger-save").click()
    wait_check(run, "名称超过 50 个字：提示「账本名称最多 50 个字」", page,
               "() => (document.querySelector('[data-testid=\"ledger-form-error\"]') || {}).innerText === '账本名称最多 50 个字'")
    run.check("校验失败仍不创建账本", len(db.ledgers_of_user(user_id)) == 0)

    page.get_by_test_id("ledger-name").fill("a")
    page.get_by_test_id("ledger-form-error").wait_for(state="detached")
    run.check("继续输入后错误提示消失", page.get_by_test_id("ledger-form-error").count() == 0)

    # 选图标
    name = unique_name("日常开销")
    page.get_by_test_id("ledger-name").fill(name)
    pressed_before = page.locator('[data-icon="mdi-book"]').get_attribute("aria-pressed")
    page.locator('[data-icon="mdi-airplane"]').click()
    wait_attr(page, '[data-icon="mdi-airplane"]', "aria-pressed", "true")
    run.check("默认选中第一个图标，点选「旅行」后改选它", pressed_before == "true"
              and page.locator('[data-icon="mdi-book"]').get_attribute("aria-pressed") == "false")
    run.check("顶部预览图标同步为所选图标", page.locator(".ft-sheet-preview .mdi-airplane").count() == 1)

    page.get_by_test_id("ledger-save").click()
    wait_toast(page, "账本创建成功")
    wait_sheet_gone(page)
    created = db.ledgers_of_user(user_id)
    run.check("保存后落库：名称 / 图标 / 排序", len(created) == 1 and created[0]["Name"] == name
              and created[0]["Icon"] == "mdi-airplane" and created[0]["SortOrder"] == 0, str(created))
    cell(page, name).first.wait_for(state="visible")
    run.check("空状态消失，账本出现在「我的账本」", page.get_by_test_id("ledgers-empty").count() == 0
              and page.get_by_test_id("own-ledgers").is_visible())
    cell_text = cell(page, name).first.inner_text()
    run.check("无账单的账本副标题是「暂无账单」", "暂无账单" in cell_text, cell_text)
    run.check("列表里用所选图标", cell(page, name).first.locator(".mdi-airplane").count() == 1)

    # Toast 外观与位置（出现 → 稳定后测量）
    wait_animations_settled(page)
    info = page.evaluate(TOAST_JS)
    if info is None:
        run.skip("Toast 外观 / 位置", "测量时 Toast 已消失")
    else:
        run.check("Toast 是玻璃胶囊（大圆角 + 背景模糊）", info["radius"] >= 20 and bool(info["backdrop"]) and info["backdrop"] != "none", str(info))
        run.check("Toast 水平居中且不超出页面边距", abs((info["left"] + info["right"]) / 2 - SCREEN_W / 2) <= 1 and info["width"] <= SCREEN_W - 32 + 1, str(info))
        run.check("Toast 底边在 Tab 栏之上（距底 97）", abs(info["bottom"] - (SCREEN_H - TOAST_BOTTOM_GAP)) <= 3, str(info))
        run.check("Toast 在最上层（不被页面遮住）", info["onTop"], str(info))
    shot(page, "ledgers-after-create")
    return name


# ---------------------------------------------------------------------------------------------- C. 编辑与排序


def check_edit_and_order(run: TestRun, page, db: LocalDb, user_id: str, first_name: str) -> "tuple[str, str]":
    """编辑名称 / 排序；返回 (改名后的第一个账本, 第二个账本) 名称。"""
    first_id = db.ledger_id_by_name(user_id, first_name)

    open_edit(page, first_name)
    wait_animations_settled(page)
    run.check("点按整行打开编辑 Sheet（标题「编辑账本」）", page.get_by_test_id("sheet-title").inner_text() == "编辑账本")
    run.check("编辑时名称已填入", page.get_by_test_id("ledger-name").input_value() == first_name)
    run.check("编辑时图标保持原选择", page.locator('[data-icon="mdi-airplane"]').get_attribute("aria-pressed") == "true")
    run.check("编辑时出现「设为默认 / 共享账本 / 删除账本」三个入口",
              page.get_by_test_id("ledger-default-switch").count() == 1 and page.get_by_test_id("ledger-share-cell").count() == 1
              and page.get_by_test_id("ledger-delete-cell").count() == 1)
    shot(page, "ledgers-sheet-edit")

    renamed = unique_name("改名后")
    page.get_by_test_id("ledger-name").fill(renamed)
    page.get_by_test_id("ledger-sort").fill("5")
    page.get_by_test_id("ledger-save").click()
    wait_toast(page, "账本更新成功")
    wait_sheet_gone(page)
    cell(page, renamed).first.wait_for(state="visible")
    row = db.ledger_row(first_id)
    run.check("编辑后名称与排序落库，旧名称不再显示", row["Name"] == renamed and row["SortOrder"] == 5 and cell(page, first_name).count() == 0, str(row))

    # 第二个账本经基座 helper（走新界面）创建；排序 0，应排在改名后那个（排序 5）之前
    second = create_ledger(page, unique_name("第二账本"))
    texts = page.evaluate(CELL_JS, "ledger-cell")
    names = [t["title"] for t in texts]
    run.check("基座 create_ledger 经新界面创建成功", len(db.ledgers_of_user(user_id)) == 2)
    run.check("列表按排序升序：排序 0 的在前，排序 5 的在后", names[:2] == [second, renamed], str(names))
    heights = [round(t["height"]) for t in texts]
    run.check("带副标题的单元格高 66", all(abs(h - 66) <= 1 for h in heights), str(heights))

    open_edit(page, renamed)
    page.get_by_test_id("ledger-sort").fill("-1")
    page.get_by_test_id("ledger-save").click()
    wait_sheet_gone(page)
    wait_check(run, "把排序改成 -1 后该账本排到列表最前", page, """([a, b]) => {
        const names = [...document.querySelectorAll('[data-testid="ledger-cell"] .ft-cell__title')].map((el) => el.textContent);
        return names.indexOf(a) === 0 && names.indexOf(b) === 1;
    }""", arg=[renamed, second])
    run.check("排序 -1 已落库", db.ledger_row(first_id)["SortOrder"] == -1)
    return renamed, second


# ---------------------------------------------------------------------------------------------- D. 设为默认


def check_default(run: TestRun, page, db: LocalDb, user_id: str, first_name: str, second_name: str) -> None:
    first_id = db.ledger_id_by_name(user_id, first_name)
    second_id = db.ledger_id_by_name(user_id, second_name)

    run.check("（前置）没有设置过默认账本", db.default_ledger_id(user_id) is None)
    open_edit(page, first_name)
    switch = page.get_by_test_id("ledger-default-switch")
    run.check("开关是 switch 语义，初始为关", switch.get_attribute("role") == "switch" and switch.get_attribute("aria-checked") == "false")
    switch.click()
    wait_attr(page, '[data-testid="ledger-default-switch"]', "aria-checked", "true")
    wait_toast(page, "已设置为默认账本")
    row_of(page, first_id).get_by_test_id("badge-default").wait_for(state="visible")
    run.check("打开开关：落库为默认账本", default_row(db, user_id, first_id))
    run.check("Sheet 还开着时，列表那一行已出现「默认」徽标", sheet(page).is_visible()
              and row_of(page, first_id).get_by_test_id("badge-default").inner_text() == "默认")

    # Toast 在 Sheet 之上
    wait_animations_settled(page)
    info = page.evaluate(TOAST_JS)
    run.check("Sheet 打开时 Toast 仍在最上层", bool(info and info["onTop"]), str(info))

    switch.click()
    wait_attr(page, '[data-testid="ledger-default-switch"]', "aria-checked", "false")
    row_of(page, first_id).get_by_test_id("badge-default").wait_for(state="detached")
    run.check("关闭开关：取消默认并移除徽标", db.default_ledger_id(user_id) is None
              and row_of(page, first_id).get_by_test_id("badge-default").count() == 0)
    close_sheet(page)

    # 同一时刻只有一个默认账本
    open_edit(page, first_name)
    page.get_by_test_id("ledger-default-switch").click()
    row_of(page, first_id).get_by_test_id("badge-default").wait_for(state="visible")
    close_sheet(page)
    open_edit(page, second_name)
    page.get_by_test_id("ledger-default-switch").click()
    row_of(page, second_id).get_by_test_id("badge-default").wait_for(state="visible")
    row_of(page, first_id).get_by_test_id("badge-default").wait_for(state="detached")
    run.check("把另一个账本设为默认后，原来的默认徽标消失（只有一个默认）",
              page.get_by_test_id("badge-default").count() == 1 and default_row(db, user_id, second_id))
    close_sheet(page)
    shot(page, "ledgers-default-badge")


# ---------------------------------------------------------------------------------------------- E / F. 左滑、删除


def check_swipe_and_delete(run: TestRun, page, db: LocalDb, user_id: str) -> None:
    to_delete = db.seed_ledger(user_id, unique_name("待删除"), sort_order=10)
    to_delete_name = db.ledger_row(to_delete)["Name"]
    with_bills = db.seed_ledger(user_id, unique_name("有账单"), sort_order=11)
    with_bills_name = db.ledger_row(with_bills)["Name"]
    db.seed_bill(user_id, ledger_id=with_bills, amount=8, note=unique_name("拦截删除用账单"))
    via_sheet = db.seed_ledger(user_id, unique_name("经Sheet删除"), sort_order=12)
    via_sheet_name = db.ledger_row(via_sheet)["Name"]
    open_app(page, "/ledgers")
    cell(page, via_sheet_name).first.wait_for(state="visible")

    # --- 展开前：操作按钮被内容盖住，点不到
    target_row = row_of(page, to_delete)
    run.check("展开前 data-open=false", target_row.get_attribute("data-open") == "false")
    tray_delete = target_row.get_by_test_id("swipe-delete")
    cx, cy = center_of(page, tray_delete)
    run.check("展开前操作按钮点不到（被内容盖住）", not covers(page, (cx, cy), ".ft-swipe__tray"))
    run.check("展开前托盘 inert 且对读屏隐藏", page.evaluate(
        "(sel) => { const t = document.querySelector(sel + ' .ft-swipe__tray'); return t.inert === true && t.getAttribute('aria-hidden') === 'true'; }",
        row_selector(to_delete)))
    run.check("行副标题：无账单", "暂无账单" in cell(page, to_delete_name).first.inner_text())
    run.check("行副标题：有账单显示笔数", "1 笔账单" in cell(page, with_bills_name).first.inner_text(), cell(page, with_bills_name).first.inner_text())

    # --- 纵向滑动不展开
    swipe_by(page, row_of(page, to_delete).get_by_test_id("ledger-cell"), 0, 90, steps=8, duration_ms=160)
    page.wait_for_timeout(250)
    run.check("纵向滑动不会展开行", row_of(page, to_delete).get_attribute("data-open") == "false")

    # --- 松手判定：同样的位移（约 -56，不到托盘宽度 152 的一半），慢拖停住再松手 → 弹回；快速划过松手 → 按速度续滑，展开
    target_cell = row_of(page, to_delete).get_by_test_id("ledger-cell")
    swipe_by(page, target_cell, -70, 0, steps=7, duration_ms=500, release_hold_ms=250)
    page.wait_for_timeout(450)
    run.check("慢拖到一半以内、停住再松手：弹回，不展开", row_of(page, to_delete).get_attribute("data-open") == "false"
              and page.evaluate("(sel) => document.querySelector(sel + ' .ft-swipe__content').style.transform", row_selector(to_delete)) == "")
    swipe_by(page, target_cell, -70, 0, steps=5, duration_ms=50)
    wait_check(run, "同样的位移快速划过松手：按松手速度续滑，展开", page,
               "(sel) => document.querySelector(sel).dataset.open === 'true'", arg=row_selector(to_delete))
    page.wait_for_timeout(400)
    page.mouse.click(150, center_of(page, target_cell)[1])
    wait_check(run, "点按展开行的内容（鼠标）：收起，不触发编辑", page,
               "(sel) => document.querySelector(sel).dataset.open === 'false'", arg=row_selector(to_delete))
    page.wait_for_timeout(300)
    run.check("（鼠标收起）没有打开编辑 Sheet", sheet(page).count() == 0)

    # --- 左滑展开
    swipe_open(page, to_delete)
    cx, cy = center_of(page, row_of(page, to_delete).get_by_test_id("swipe-delete"))
    run.check("左滑后「删除」可点（不再被盖住）", covers(page, (cx, cy), ".ft-swipe__tray"))
    cx2, cy2 = center_of(page, row_of(page, to_delete).get_by_test_id("swipe-share"))
    run.check("左滑后「共享」可点，且在「删除」左侧", covers(page, (cx2, cy2), ".ft-swipe__tray") and cx2 < cx)
    labels = row_of(page, to_delete).locator(".ft-swipe__tray").inner_text().replace("\n", " ")
    run.check("操作按钮文字：共享 / 删除", "共享" in labels and "删除" in labels, labels)
    run.check("展开后托盘可聚焦、对读屏可见", page.evaluate(
        "(sel) => { const t = document.querySelector(sel + ' .ft-swipe__tray'); return t.inert === false && t.getAttribute('aria-hidden') === 'false'; }",
        row_selector(to_delete)))
    shot(page, "ledgers-swipe-open")

    # --- 展开时点内容：只收起，不进入编辑
    tap(page, row_of(page, to_delete).get_by_test_id("ledger-cell"))
    wait_attr(page, row_selector(to_delete), "data-open", "false")
    page.wait_for_timeout(450)
    run.check("展开时点按内容只收起，不会打开编辑 Sheet", sheet(page).count() == 0)

    # --- 左滑「共享」→ 操作菜单，行自动收起
    swipe_open(page, to_delete)
    tap(page, row_of(page, to_delete).get_by_test_id("swipe-share"))
    page.get_by_test_id("ledger-share-sheet").wait_for(state="visible")
    run.check("左滑点「共享」打开操作菜单，标题带账本名", to_delete_name in page.get_by_test_id("ledger-share-sheet").inner_text())
    wait_attr(page, row_selector(to_delete), "data-open", "false")
    run.check("点了操作按钮后这一行自动收起", True)
    page.get_by_test_id("actionsheet-cancel").click()
    page.get_by_test_id("ledger-share-sheet").wait_for(state="detached")

    # --- 左滑「删除」→ Alert → 取消
    swipe_open(page, to_delete)
    tap(page, row_of(page, to_delete).get_by_test_id("swipe-delete"))
    page.get_by_test_id("ledger-delete-alert").wait_for(state="visible")
    title = page.get_by_test_id("alert-title").inner_text()
    run.check("删除确认 Alert 标题带账本名", title == f"删除账本「{to_delete_name}」？", title)
    run.check("Alert 是 alertdialog 语义", page.get_by_test_id("ledger-delete-alert").get_attribute("role") == "alertdialog")
    shot(page, "ledgers-delete-alert")
    page.get_by_test_id("alert-cancel").click()
    page.get_by_test_id("ledger-delete-alert").wait_for(state="detached")
    run.check("取消删除：账本仍在", db.ledger_row(to_delete)["IsDeleted"] == 0 and cell(page, to_delete_name).count() == 1)

    # --- 左滑「删除」→ 确认 → 软删除
    swipe_open(page, to_delete)
    tap(page, row_of(page, to_delete).get_by_test_id("swipe-delete"))
    page.get_by_test_id("ledger-delete-alert").wait_for(state="visible")
    page.get_by_test_id("alert-confirm").click()
    wait_toast(page, "账本删除成功")
    cell(page, to_delete_name).first.wait_for(state="detached")
    page.get_by_test_id("ledger-delete-alert").wait_for(state="detached")  # 退出动画期间遮罩仍会接住触摸
    run.check("确认删除：账本消失，库里是软删除（IsDeleted=1）", db.ledger_row(to_delete)["IsDeleted"] == 1
              and to_delete.upper() not in [l["Id"] for l in db.ledgers_of_user(user_id)])

    # --- 账本下有账单：被拦截，账本保留
    swipe_open(page, with_bills)
    tap(page, row_of(page, with_bills).get_by_test_id("swipe-delete"))
    page.get_by_test_id("ledger-delete-alert").wait_for(state="visible")
    page.get_by_test_id("alert-confirm").click()
    wait_toast(page, "该账本下有账单，无法删除")
    page.get_by_test_id("ledger-delete-alert").wait_for(state="detached")
    run.check("账本下有账单：提示无法删除，账本保留", db.ledger_row(with_bills)["IsDeleted"] == 0 and cell(page, with_bills_name).count() == 1)
    run.check("被拦截后仍停留在账本页（没有被登出）", page.evaluate("() => !!localStorage.getItem('userId')") and re.search(r"/ledgers$", page.url) is not None)

    # --- 非滑动入口：编辑 Sheet 里的「删除账本」
    open_edit(page, via_sheet_name)
    page.get_by_test_id("ledger-delete-cell").click()
    page.get_by_test_id("ledger-delete-alert").wait_for(state="visible")
    wait_animations_settled(page)
    confirm_center = center_of(page, page.get_by_test_id("alert-confirm"))
    run.check("Alert 盖在 Sheet 之上（Alert 按钮位置命中 Alert）", covers(page, confirm_center, '[data-testid="alert-host"]'))
    save_center = center_of(page, page.get_by_test_id("ledger-save"))
    run.check("Alert 弹出时 Sheet 的保存按钮被遮罩挡住", not covers(page, save_center, '[data-testid="ledger-sheet"]'))
    page.get_by_test_id("alert-confirm").click()
    wait_sheet_gone(page)
    cell(page, via_sheet_name).first.wait_for(state="detached")
    run.check("经 Sheet 删除：Sheet 随之收起，账本软删除", db.ledger_row(via_sheet)["IsDeleted"] == 1)


# ---------------------------------------------------------------------------------------------- G. 共享入口


def check_share_entries(run: TestRun, page, db: LocalDb, user_id: str, name: str) -> None:
    ledger_id = db.ledger_id_by_name(user_id, name)
    open_app(page, "/ledgers")
    open_edit(page, name)
    page.get_by_test_id("ledger-share-cell").click()
    page.get_by_test_id("ledger-share-sheet").wait_for(state="visible")
    wait_animations_settled(page)
    text = page.get_by_test_id("ledger-share-sheet").inner_text()
    run.check("共享操作菜单：标题带账本名，含「邀请成员共享」「复制账本信息」「取消」",
              name in text and "邀请成员共享" in text and "复制账本信息" in text and "取消" in text, text)
    cancel_center = center_of(page, page.get_by_test_id("actionsheet-cancel"))
    run.check("操作菜单盖在 Sheet 之上", covers(page, cancel_center, '[data-testid="actionsheet-host"]'))
    shot(page, "ledgers-share-actionsheet")

    # 复制账本信息
    page.get_by_test_id("share-copy").click()
    wait_toast(page, "账本信息已复制，快去粘贴给好友吧")
    page.get_by_test_id("ledger-share-sheet").wait_for(state="detached")
    clip = page.evaluate("() => navigator.clipboard.readText()")
    run.check("复制账本信息：剪贴板里是带账本名的邀请语", name in clip, clip)

    # 邀请成员共享 → 成员页，返回后账本页是干净的
    page.get_by_test_id("ledger-share-cell").click()
    page.get_by_test_id("ledger-share-sheet").wait_for(state="visible")
    page.get_by_test_id("share-invite").click()
    wait_url(page, rf"^/ledgers/{ledger_id.lower()}/members$")
    wait_tab_bar(page, visible=False)
    expect_sheet_closed(run, page, "邀请成员共享：进入该账本的成员页前，编辑 Sheet 已收起")
    wait_animations_settled(page)
    page.go_back()
    wait_url(page, r"^/ledgers$")
    page.get_by_test_id("ledgers-page").wait_for(state="visible")
    run.check("从成员页返回账本页：没有残留的 Sheet / 菜单", sheet(page).count() == 0 and page.get_by_test_id("ledger-share-sheet").count() == 0)
    run.check("返回后账本仍在列表里", cell(page, name).count() == 1)


# ---------------------------------------------------------------------------------------------- H. 共享账本、邀请卡


def check_shared_and_invitations(run: TestRun, page, db: LocalDb, user_id: str, phone: str, owned_name: str) -> None:
    # 共享数据本地与云端各写一份（CloudWorld）；假云端先不启动——第一阶段验证「云端不可用」，第二阶段再启动它验证成功路径
    cloud = FakeCloud()
    world = CloudWorld(cloud, db)
    me = world.me(phone)
    owner_phone = unique_phone()
    owner = world.person(owner_phone)
    stub_owner_id = db.seed_user("stub_" + uuid.uuid4().hex[:15])
    partner_id = db.seed_user()

    editor_name = unique_name("家庭共用")
    viewer_name = unique_name("只读共享")
    stub_name = unique_name("未知所有者")
    invite_name = unique_name("邀请入账本")
    editor_ledger = world.foreign_ledger(owner, editor_name, me=me, my_role=ROLE_EDITOR).ledger_id
    viewer_ledger = world.foreign_ledger(owner, viewer_name, me=me, my_role=ROLE_VIEWER).ledger_id
    # 所有者是同步占位用户的账本只存在于本地缓存（云端不认识这个人）
    stub_ledger = db.seed_ledger(stub_owner_id, stub_name)
    db.seed_ledger_member(stub_ledger, user_id, role=ROLE_EDITOR, status=MEMBER_ACTIVE)
    invite = world.foreign_ledger(owner, invite_name, me=me, my_role=ROLE_VIEWER, my_status=MEMBER_PENDING)
    invite_ledger, invite_member = invite.ledger_id, invite.my_member_id

    # 我自己的账本被共享出去：所有者 + 一位成员 → 「共享」徽标与成员数副标题
    owned_id = db.ledger_id_by_name(user_id, owned_name)
    db.seed_ledger_member(owned_id, user_id, role=ROLE_OWNER, status=MEMBER_ACTIVE)
    db.seed_ledger_member(owned_id, partner_id, role=ROLE_EDITOR, status=MEMBER_ACTIVE)

    open_app(page, "/ledgers")
    # 共享数据在本地账本列表之后加载（云端不可用时最多等 3 秒短期限）
    joined_cell(page, editor_name).first.wait_for(state="visible")
    page.get_by_test_id("invitation-card").first.wait_for(state="visible")

    run.check("「加入的共享账本」分组出现", page.get_by_test_id("joined-ledgers").is_visible())
    joined_names = [t["title"] for t in page.evaluate(CELL_JS, "joined-cell")]
    run.check("加入的共享账本只含我作为成员加入的 3 个", len(joined_names) == 3, str(joined_names))
    editor_text = joined_cell(page, editor_name).first.inner_text()
    viewer_text = joined_cell(page, viewer_name).first.inner_text()
    stub_text = joined_cell(page, stub_name).first.inner_text()
    run.check("可编辑角色：徽标「可编辑」，副标题是脱敏的所有者手机号",
              "可编辑" in editor_text and f"所有者 {mask(owner_phone)}" in editor_text, editor_text)
    run.check("只读角色：徽标「只读」", "只读" in viewer_text and f"所有者 {mask(owner_phone)}" in viewer_text, viewer_text)
    run.check("所有者是同步占位用户时显示「未知用户」，不露出占位手机号", "所有者 未知用户" in stub_text and "stub_" not in stub_text, stub_text)
    run.check("加入的共享账本不会出现在「我的账本」", cell(page, editor_name).count() == 0)
    run.check("加入的共享账本不会因为是别人的账本就带左滑操作（我不是所有者）",
              page.locator(f'[data-testid="ledger-row"]:has-text("{editor_name}")').count() == 0)

    owned_text = cell(page, owned_name).first.inner_text()
    run.check("被共享出去的我的账本：副标题「2 位成员 · 你是所有者」", "2 位成员 · 你是所有者" in owned_text, owned_text)
    run.check("被共享出去的我的账本：出现「共享」徽标", row_of(page, owned_id).get_by_test_id("badge-shared").count() == 1)
    run.check("没有被共享的账本没有「共享」徽标", page.get_by_test_id("badge-shared").count() == 1)

    cards = page.get_by_test_id("invitation-card")
    run.check("每个待处理邀请一张卡", cards.count() == 1)
    title = cards.first.get_by_test_id("invitation-title").inner_text()
    subtitle = cards.first.get_by_test_id("invitation-subtitle").inner_text()
    run.check("邀请卡标题「…邀请你加入」", title == f"「{invite_name}」邀请你加入", title)
    run.check("邀请卡副标题：邀请人（脱敏）与角色", subtitle == f"来自 {mask(owner_phone)} · 角色：只读", subtitle)
    run.check("邀请卡有「拒绝」「接受邀请」", cards.first.get_by_test_id("invitation-reject").inner_text() == "拒绝"
              and cards.first.get_by_test_id("invitation-accept").inner_text() == "接受邀请")
    order = page.evaluate("""() => {
        const y = (id) => { const el = document.querySelector(`[data-testid="${id}"]`); return el ? el.getBoundingClientRect().top : null; };
        return { card: y('invitation-card'), own: y('own-ledgers'), joined: y('joined-ledgers') };
    }""")
    run.check("三段顺序：邀请卡 → 我的账本 → 加入的共享账本", order["card"] < order["own"] < order["joined"], str(order))
    shot(page, "ledgers-shared-and-invitation")

    # 点按共享账本进入成员页
    joined_cell(page, editor_name).first.click()
    wait_url(page, rf"^/ledgers/{editor_ledger.lower()}/members$")
    wait_animations_settled(page)
    page.go_back()
    wait_url(page, r"^/ledgers$")
    page.get_by_test_id("ledgers-page").wait_for(state="visible")
    run.check("点按「加入的共享账本」进入成员页，返回后回到账本页", joined_cell(page, editor_name).count() == 1)

    # 第一阶段 · 云端不可用（假云端还没启动）：拒绝邀请 → 报错提示、邀请卡保留、不登出
    page.get_by_test_id("invitation-card").first.wait_for(state="visible")
    page.get_by_test_id("invitation-reject").click()
    wait_toast(page, "操作失败：无法连接云端，请检查网络后重试")
    run.check("云端不可用：拒绝邀请给出「操作失败：无法连接云端…」提示", True)
    page.get_by_test_id("invitation-reject").wait_for(state="visible")
    run.check("操作失败后邀请卡保留，按钮恢复可点", page.get_by_test_id("invitation-card").count() == 1
              and page.get_by_test_id("invitation-reject").is_enabled() and page.get_by_test_id("invitation-accept").is_enabled())
    pending = [m for m in db.ledger_members(invite_ledger) if m["Id"].upper() == invite_member.upper()]
    run.check("本地的邀请记录没有被改动", len(pending) == 1 and pending[0]["Status"] == MEMBER_PENDING and pending[0]["IsDeleted"] == 0, str(pending))
    run.check("云端失败不会被当成登录过期：仍在账本页，会话仍在",
              bool(page.evaluate("() => !!localStorage.getItem('userId')")) and re.search(r"/ledgers$", page.url) is not None)

    # 第二阶段 · 假云端可用：接受 / 拒绝的成功路径与云端业务错误
    try:
        cloud.start()
    except CloudPortBusy as exc:
        run.check("假云端可以启动（接受 / 拒绝邀请的成功路径依赖它）", False, str(exc))
        return
    try:
        check_invitation_responses(run, page, db, world, me, owner, invite, invite_name)
    finally:
        cloud.stop()


def check_invitation_responses(run: TestRun, page, db: LocalDb, world: CloudWorld, me: Person, owner: Person, invite, invite_name: str) -> None:
    cloud = world.cloud
    accept_name = unique_name("接受入账本")
    accept = world.foreign_ledger(owner, accept_name, me=me, my_role=ROLE_EDITOR, my_status=MEMBER_PENDING)

    def card(name):
        return page.get_by_test_id("invitation-card").filter(has_text=name)

    open_app(page, "/ledgers")
    card(invite_name).wait_for(state="visible")
    card(accept_name).wait_for(state="visible")
    run.check("两张待处理邀请各一张卡", page.get_by_test_id("invitation-card").count() == 2)

    # 云端业务错误（邀请已在别处被处理）：取云端给的说明，邀请卡保留，不登出
    respond_path = rf"/api/invitations/{invite.my_member_id.lower()}/respond$"
    cloud.fail("POST", r"/respond$", status=404, message="邀请不存在或已处理")
    card(invite_name).get_by_test_id("invitation-reject").click()
    wait_toast(page, "操作失败：邀请不存在或已处理")
    run.check("云端业务错误：提示取自云端说明（不露出「请求失败: NotFound - {…}」）",
              not any("请求失败" in t or "NotFound" in t for t in page.evaluate(
                  "() => [...document.querySelectorAll('.m-enqueued-snackbars .m-snack__wrapper')].map((e) => e.textContent)")))
    run.check("云端业务错误后：邀请卡保留、按钮恢复可点、不登出",
              card(invite_name).count() == 1 and card(invite_name).get_by_test_id("invitation-reject").is_enabled()
              and bool(page.evaluate("() => !!localStorage.getItem('userId')")))

    # 拒绝成功：邀请卡消失，云端与本地缓存都标记删除，账本不会出现在任何列表
    card(invite_name).get_by_test_id("invitation-reject").click()
    wait_toast(page, "已拒绝邀请")
    card(invite_name).wait_for(state="detached")
    calls = cloud.calls("POST", respond_path)
    keys = {k.lower(): v for k, v in (calls[-1].body or {}).items()} if calls else {}
    run.check("拒绝：云端先后收到两次请求（失败 404 → 成功 204），最后一次 accept=false",
              [c.status for c in calls] == [404, 204] and keys.get("accept") is False, str(calls))
    run.check("拒绝：云端的邀请行已删除", cloud.member(invite.my_member_id)["isDeleted"])
    local = [m for m in db.ledger_members(invite.ledger_id) if m["Id"].upper() == invite.my_member_id.upper()]
    run.check("拒绝：本地缓存的邀请行同步标记为删除", len(local) == 1 and local[0]["IsDeleted"] == 1, str(local))
    run.check("拒绝后账本不会出现在「我的账本」或「加入的共享账本」", cell(page, invite_name).count() == 0 and joined_cell(page, invite_name).count() == 0)
    run.check("另一张邀请卡不受影响", card(accept_name).count() == 1)

    # 接受成功：邀请卡消失，账本进入「加入的共享账本」
    card(accept_name).get_by_test_id("invitation-accept").click()
    wait_toast(page, "已接受邀请")
    card(accept_name).wait_for(state="detached")
    joined_cell(page, accept_name).first.wait_for(state="visible")
    text = joined_cell(page, accept_name).first.inner_text()
    run.check("接受：账本出现在「加入的共享账本」，带「可编辑」徽标与脱敏的所有者手机号",
              "可编辑" in text and f"所有者 {mask(owner.phone)}" in text, text)
    accept_call = cloud.calls("POST", rf"/api/invitations/{accept.my_member_id.lower()}/respond$")
    keys = {k.lower(): v for k, v in (accept_call[-1].body or {}).items()} if accept_call else {}
    run.check("接受：云端收到 accept=true，我的成员行生效（Active）",
              keys.get("accept") is True and cloud.member(accept.my_member_id)["status"] == MEMBER_ACTIVE, str(accept_call))
    local = [m for m in db.ledger_members(accept.ledger_id) if m["Id"].upper() == accept.my_member_id.upper()]
    run.check("接受：本地缓存的成员行同步为生效", len(local) == 1 and local[0]["Status"] == MEMBER_ACTIVE and local[0]["IsDeleted"] == 0, str(local))
    run.check("接受后没有邀请卡了", page.get_by_test_id("invitation-card").count() == 0)

    # 接受后进成员页：所有者 + 我，只有「退出账本」
    joined_cell(page, accept_name).first.click()
    wait_url(page, rf"^/ledgers/{accept.ledger_id.lower()}/members$")
    page.get_by_test_id("member-exit").wait_for(state="visible")
    run.check("接受后进入该账本的成员页：所有者与我两行，只有「退出账本」",
              page.get_by_test_id("member-cell").count() == 2 and page.get_by_test_id("member-invite").count() == 0)
    page.go_back()
    wait_url(page, r"^/ledgers$")


# ---------------------------------------------------------------------------------------------- I. Sheet 收起方式


def check_sheet_dismiss(run: TestRun, page, db: LocalDb, user_id: str) -> None:
    open_app(page, "/ledgers")
    page.get_by_test_id("ledger-add").click()
    wait_sheet(page)
    wait_animations_settled(page)

    box = page.evaluate("""() => {
        const r = document.querySelector('[data-testid="ledger-sheet-host"]').getBoundingClientRect();
        return { left: r.left, top: r.top, width: r.width, height: r.height };
    }""")
    run.check("Sheet 容器盖住整个视口", box["left"] == 0 and box["top"] == 0 and abs(box["width"] - SCREEN_W) <= 1 and abs(box["height"] - SCREEN_H) <= 1, str(box))
    back_center = center_of(page, page.get_by_test_id("nav-back"))
    run.check("Sheet 打开时底下的导航栏点不到（被遮罩挡住）", covers(page, back_center, '[data-testid="ledger-sheet-host"]'))
    run.check("Sheet 靠底部显示（顶部留出遮罩区）", page.evaluate(
        "() => { const r = document.querySelector('[data-testid=\"ledger-sheet\"]').getBoundingClientRect(); return r.bottom >= window.innerHeight - 1 && r.top > 44; }"))
    ledgers_before = len(db.ledgers_of_user(user_id))

    # 点遮罩
    page.mouse.click(SCREEN_W / 2, 60)
    expect_sheet_closed(run, page, "点遮罩收起 Sheet")

    # Esc
    page.get_by_test_id("ledger-add").click()
    wait_sheet(page)
    page.keyboard.press("Escape")
    expect_sheet_closed(run, page, "按 Esc 收起 Sheet")

    # 关闭按钮
    page.get_by_test_id("ledger-add").click()
    wait_sheet(page)
    run.check("关闭按钮有读屏名称「关闭」", page.get_by_test_id("sheet-close").get_attribute("aria-label") == "关闭")
    page.get_by_test_id("sheet-close").click()
    expect_sheet_closed(run, page, "点左上角关闭收起 Sheet")

    # 拖拽把手向下：拖得够 → 跟手后收起
    page.get_by_test_id("ledger-add").click()
    wait_sheet(page)
    wait_animations_settled(page)
    swipe_by(page, page.locator(".ft-sheet__handle"), 0, 360, steps=12, duration_ms=260)
    expect_sheet_closed(run, page, "按住把手向下拖拽足够距离：Sheet 跟手后收起")

    # 拖得不够 → 弹回原位
    page.get_by_test_id("ledger-add").click()
    wait_sheet(page)
    wait_animations_settled(page)
    top_before = page.evaluate("() => document.querySelector('[data-testid=\"ledger-sheet\"]').getBoundingClientRect().top")
    swipe_by(page, page.locator(".ft-sheet__handle"), 0, 40, steps=8, duration_ms=300, release_hold_ms=350)
    wait_animations_settled(page)
    page.wait_for_timeout(350)  # 弹回的过渡（260ms）+ 清理内联过渡（280ms 定时器）
    top_after = page.evaluate("() => { const el = document.querySelector('[data-testid=\"ledger-sheet\"]'); return el ? el.getBoundingClientRect().top : null; }")
    run.check("拖得不够：Sheet 弹回原位，不收起", top_after is not None and abs(top_after - top_before) <= 1, f"{top_before} -> {top_after}")
    if sheet(page).count():
        page.get_by_test_id("sheet-close").click()
        wait_sheet_gone(page)

    # 拖拽标题栏同样可以收起
    page.get_by_test_id("ledger-add").click()
    wait_sheet(page)
    wait_animations_settled(page)
    title_pos = center_of(page, page.get_by_test_id("sheet-title"))
    swipe(page, title_pos, (title_pos[0], title_pos[1] + 380), steps=12, duration_ms=260)
    expect_sheet_closed(run, page, "按住标题栏向下拖拽也能收起")
    run.check("以上各种收起方式都没有创建账本", len(db.ledgers_of_user(user_id)) == ledgers_before)


# ---------------------------------------------------------------------------------------------- K. 沉浸式


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


def check_immersive(run: TestRun, page, db: LocalDb, user_id: str) -> None:
    """模拟刘海（状态栏 47）+ 手势条（34）：新模板的导航栏、内容、Sheet、Toast 都要让出安全区。"""
    notch, gesture = 47, 34
    open_app(page, "/ledgers")
    page.get_by_test_id("ledgers-page").wait_for(state="visible")
    page.evaluate(SET_SAFE_JS, [notch, gesture])
    wait_animations_settled(page)

    info = page.evaluate(SHELL_JS)
    nav = page.evaluate("() => { const r = document.querySelector('[data-testid=\"nav-bar\"]').getBoundingClientRect(); return { top: r.top, height: r.height }; }")
    run.check("【沉浸式】导航栏背景延伸到屏幕顶部，高 = 44 + 状态栏 47", abs(info["bar"]["top"]) <= 1 and abs(info["bar"]["height"] - (44 + notch)) <= 1, str(info["bar"]))
    run.check("【沉浸式】导航行（返回 / 标题 / ＋）在状态栏之下，高 44", abs(nav["top"] - notch) <= 1 and abs(nav["height"] - 44) <= 1, str(nav))
    run.check("【沉浸式】内容紧贴导航栏下沿，没有被盖住", info["page"]["top"] >= info["bar"]["bottom"] - 1, f"{info['page']} {info['bar']}")
    wait_attr(page, "html", "data-statusbar-icons", "dark")
    run.check("【沉浸式】浅色导航栏 → 状态栏用深色图标", page.locator("html").get_attribute("data-statusbar-icons") == "dark")

    page.get_by_test_id("ledger-add").click()
    wait_sheet(page)
    wait_animations_settled(page)
    geometry = page.evaluate("""() => {
        const r = (sel) => document.querySelector(sel).getBoundingClientRect();
        return { sheet: r('[data-testid="ledger-sheet"]'), save: r('[data-testid="ledger-save"]') };
    }""")
    run.check("【沉浸式】Sheet 不顶进状态栏", geometry["sheet"]["top"] >= notch, str(geometry["sheet"]))
    run.check("【沉浸式】Sheet 内最后一个按钮避开手势条", geometry["save"]["bottom"] <= SCREEN_H - gesture + 1, str(geometry["save"]))
    run.check("【沉浸式】Sheet 底边仍贴屏幕底（背景延伸到手势条下）", geometry["sheet"]["bottom"] >= SCREEN_H - 1, str(geometry["sheet"]))

    name = unique_name("沉浸式")
    page.get_by_test_id("ledger-name").fill(name)
    page.get_by_test_id("ledger-save").click()
    wait_toast(page, "账本创建成功")
    wait_sheet_gone(page)
    wait_animations_settled(page)
    toast_info = page.evaluate(TOAST_JS)
    if toast_info is None:
        run.skip("【沉浸式】Toast 避开手势条", "测量时 Toast 已消失")
    else:
        expected = SCREEN_H - gesture - TOAST_BOTTOM_GAP
        run.check("【沉浸式】Toast 在手势条与 Tab 栏占位之上", abs(toast_info["bottom"] - expected) <= 3, f"{toast_info['bottom']} vs {expected}")
    shot(page, "ledgers-immersive-notch")

    page.evaluate(CLEAR_SAFE_JS)
    wait_animations_settled(page)
    restored = page.evaluate(SHELL_JS)
    run.check("【沉浸式】安全区还原为 0 后，导航栏回到 44 高", abs(restored["bar"]["height"] - 44) <= 1, str(restored["bar"]))


# ---------------------------------------------------------------------------------------------- J. 降级


def check_reduced_motion(run: TestRun) -> None:
    with session(reduced_motion=True) as s:
        page = s.page
        login(page)
        open_app(page, "/ledgers")
        page.get_by_test_id("ledger-add").click()
        wait_sheet(page)
        anim = page.evaluate("() => getComputedStyle(document.querySelector('[data-testid=\"ledger-sheet\"]')).animationName")
        run.check("减少动态效果：Sheet 入场改为淡入（不再自底部滑入）", anim == "ft-page-fade", anim)
        close_sheet(page)
        run.check_clean(s.errors, "减少动态效果会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- main


def main() -> int:
    require_server()
    run = TestRun("ledgers-page")
    db = LocalDb()
    db.verify_schema()

    with session() as s:
        page = s.page
        s.context.grant_permissions(["clipboard-read", "clipboard-write"], origin=BASE_URL)
        info = login(page)
        user_id = db.user_id(info.phone)

        check_shell_and_empty(run, page, db)
        first = check_create(run, page, db, user_id)
        first, second = check_edit_and_order(run, page, db, user_id, first)
        check_default(run, page, db, user_id, first, second)
        check_swipe_and_delete(run, page, db, user_id)
        check_share_entries(run, page, db, user_id, second)
        check_shared_and_invitations(run, page, db, user_id, info.phone, second)
        check_sheet_dismiss(run, page, db, user_id)
        check_immersive(run, page, db, user_id)

        run.check_clean(s.errors)

    check_reduced_motion(run)
    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
