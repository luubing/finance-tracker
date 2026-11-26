# -*- coding: utf-8 -*-
"""
E2E 测试：账本成员页（iOS UI 重构 · 06 号 ticket）。

成员关系以云端为真相源，所有写操作（邀请 / 改角色 / 移除 / 转让所有权 / 退出）都先调云端接口、再把云端的成员关系拉回本地缓存。
本脚本在本机起一个假云端（fake_cloud.py，端口 E2E_CLOUD_PORT，默认 5271；server.ps1 启动 Web.Server 时已把
ApiSettings__BaseUrl 指向它），所以这些流程可以在没有真实 Api / PostgreSQL 的环境下端到端验证；
假云端的状态码 / 消息与真实控制器一致，用例同时核对「页面表现」「云端收到的请求与最终状态」「本地缓存」三处。
只覆盖网页层——Android / iOS 上的真机手感（左滑跟手、Sheet 弹簧、返回手势）见 ticket 06 的人工验收清单。

  A. 所有者视角：二级页面外壳（Tab 栏隐藏 / 导航栏标题 / 右上角「＋」）、按角色分组（所有者 / 可编辑 / 只读）、行的内容与徽标、
     「你」标记、所有者与自己不可管理、列表末尾的「邀请成员」
  B. 邀请 Sheet：手机号输入属性、角色单选、就地校验（与登录同一套规则，不发请求）、云端拒绝（未注册 / 已是成员 / 所有者）走 Toast
     且 Sheet 保留输入、成功后收起并提示、重复邀请的冲突提示、Esc 收起并重置、回车提交
  C. 读路径：云端上其他设备造成的变化，重新进入页面时联网刷新后可见
  D. 操作菜单：点按成员行（改角色 / 转让所有权 / 移除成员），改角色后成员换组；取消不改动
  E. 移除：左滑「移除」与菜单「移除成员」两个入口都经 Alert 确认（取消不改动）；云端与本地缓存都标记删除，重新进入仍不见
  F. 失败路径：云端 403（无权限，不登出）/ 5xx（不泄露内部信息）/ 404；失败后 Sheet / 列表保持原样，可重试
  G. 转让所有权与退出：转让后「＋」与管理入口立即消失、原所有者成为可编辑成员；退出账本经 Alert 确认并返回上一页
  H. 非所有者视角（可编辑 / 只读成员）：没有任何管理入口，只有「退出账本」
  I. 云端不可用：页面仍然读本地缓存并正常打开；写操作给出「无法连接云端」提示、不登出；云端恢复后可重试
  J. 异常入口：非法账本 Id 直接返回上一页（「账本不存在」的 Toast 随整页刷新丢失，留给人工验收）；没有任何成员信息的账本显示空状态

可观察属性（已登记到 README）：members-page、member-invite、member-group-owner|editor|viewer、member-row[data-member-id]、
member-cell[data-member-id][data-role][data-me]、badge-role / badge-me、swipe-remove、member-invite-cell / member-invite-group、
member-exit / member-exit-group、invite-sheet(-host)、invite-phone、invite-roles、invite-role-editor|viewer、invite-error、invite-submit、
member-actions、action-role / action-transfer / action-remove、member-remove-alert / member-transfer-alert / member-exit-alert、
members-empty / members-error / members-retry / members-skeleton。
"""
import os
import re
import sys
import time
import uuid
from dataclasses import dataclass

from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

from e2e_base import (
    GLASS_LAYERS_JS,
    BASE_URL,
    PHONE_VIEWPORT,
    ROLE_EDITOR,
    ROLE_OWNER,
    ROLE_VIEWER,
    LocalDb,
    TestRun,
    center_of,
    login,
    open_app,
    require_server,
    session,
    shot,
    swipe_by,
    unique_name,
    unique_phone,
    wait_animations_settled,
    wait_attr,
    wait_url,
)
from fake_cloud import CloudPortBusy, CloudWorld, FakeCloud, Person

SCREEN_W = PHONE_VIEWPORT["width"]
LOGOUT_PROBE_JS = "() => !!localStorage.getItem('userId')"

ROWS_JS = """() => [...document.querySelectorAll('[data-testid="member-cell"]')].map((el) => {
  const r = el.getBoundingClientRect();
  const text = (selector) => { const node = el.querySelector(selector); return node ? node.textContent.trim() : ''; };
  return {
    id: el.getAttribute('data-member-id'),
    role: el.getAttribute('data-role'),
    me: el.getAttribute('data-me') === 'true',
    tag: el.tagName,
    title: text('.ft-cell__title'),
    subtitle: text('.ft-cell__subtitle'),
    badges: [...el.querySelectorAll('[data-testid="badge-role"],[data-testid="badge-me"]')].map((b) => b.textContent.trim()),
    chevron: !!el.querySelector('.ft-cell__chevron'),
    swipe: !!el.closest('.ft-swipe'),
    height: r.height,
  };
})"""

GROUPS_JS = """() => [...document.querySelectorAll('[data-testid^="member-group-"]')].map((el) => ({
  id: el.getAttribute('data-testid'),
  header: ((el.querySelector('.ft-group__header') || {}).textContent || '').trim(),
  members: [...el.querySelectorAll('[data-testid="member-cell"]')].map((c) => c.getAttribute('data-member-id')),
  top: el.getBoundingClientRect().top,
}))"""

SHELL_JS = """() => {
  const rect = (el) => {
    if (!el) return null;
    const r = el.getBoundingClientRect();
    return { left: r.left, right: r.right, top: r.top, bottom: r.bottom, width: r.width, height: r.height };
  };
  const title = document.querySelector('[data-testid="nav-title"]');
  return {
    bar: rect(document.querySelector('.ft-subbar')),
    back: rect(document.querySelector('[data-testid="nav-back"]')),
    add: rect(document.querySelector('[data-testid="member-invite"]')),
    title: rect(title),
    titleTag: title && title.tagName,
    overflowX: document.documentElement.scrollWidth - document.documentElement.clientWidth,
  };
}"""

HIT_JS = """([x, y, selector]) => {
  const el = document.elementFromPoint(x, y);
  return !!el && !!el.closest(selector);
}"""

TOAST_TEXTS_JS = """() => [...document.querySelectorAll('.m-enqueued-snackbars .m-snack__wrapper')].map((e) => e.textContent.trim())"""

# 触发操作前给当前已有的 Toast 打上标记；之后只认「没有标记」的新 Toast，
# 这样先后两次同样的提示不会被上一条（或上一条消失的瞬间）冒充
MARK_TOASTS_JS = """() => document.querySelectorAll('.m-enqueued-snackbars .m-snack__wrapper')
  .forEach((e) => e.setAttribute('data-e2e-seen', '1'))"""

NEW_TOAST_JS = """(text) => [...document.querySelectorAll('.m-enqueued-snackbars .m-snack__wrapper:not([data-e2e-seen])')]
  .some((e) => e.textContent.includes(text))"""


@dataclass
class Ctx:
    run: TestRun
    page: object
    db: LocalDb
    cloud: FakeCloud
    world: CloudWorld
    me: Person


# ---------------------------------------------------------------------------------------------- 小工具


def mask(phone: str) -> str:
    return f"{phone[:3]}****{phone[-4:]}"


def toast_texts(page) -> "list[str]":
    return page.evaluate(TOAST_TEXTS_JS)


OVERLAY_STATE_JS = """() => ({
  overlays: [...document.querySelectorAll('[data-testid$="-host"]')]
    .map((e) => `${e.getAttribute('data-testid')}:${e.getAttribute('data-state') || ''}`),
  reconnect: (document.querySelector('#components-reconnect-modal') || {}).className || null,
})"""


def server_log_warnings(max_lines: int = 8) -> "list[str]":
    """Web.Server 日志（server.ps1 写到 %TEMP%\\ft_webserver.log）里最近的 warn / fail / crit 条目（连同紧跟的消息行）。
    页面上给用户的失败提示不带内部细节，真正的异常记在这里；取不到日志就返回空列表（诊断本身不能再抛错）。"""
    path = os.path.join(os.environ.get("TEMP", ""), "ft_webserver.log")
    try:
        with open(path, encoding="utf-8", errors="replace") as handle:
            lines = handle.read().splitlines()[-600:]
    except OSError:
        return []

    picked: "list[str]" = []
    for index, line in enumerate(lines):
        if line.startswith(("warn:", "fail:", "crit:")):
            picked.append(line.strip())
            if index + 1 < len(lines):
                picked.append(lines[index + 1].strip())
    return picked[-max_lines:]


def diagnostics(ctx: Ctx) -> str:
    """等待超时时随失败信息一起打印：页面上还挂着哪些弹层、电路有没有断线重连、假云端最近收到了什么请求、
    服务端日志里最近的警告（偶发失败要靠这些复盘）。"""
    try:
        state = ctx.page.evaluate(OVERLAY_STATE_JS)
    except Exception as exc:  # noqa: BLE001 —— 诊断本身不能再抛错
        state = f"取不到（{exc}）"
    return (f"页面状态={state}；假云端最近请求={ctx.cloud.requests[-6:]}；url={ctx.page.url}；"
            f"服务端日志警告={server_log_warnings()}")


# 记录触发之后出现过的每一条 Toast（Toast 几秒后就消失，超时再看「当前 Toast」往往已经是空的，
# 而偶发失败恰恰需要知道那几秒里页面到底提示了什么）
TOAST_RECORDER_JS = """() => {
  window.__e2eToasts = [];
  const seen = new WeakSet();
  const collect = () => document.querySelectorAll('.m-enqueued-snackbars .m-snack__wrapper').forEach((el) => {
    if (!seen.has(el)) { seen.add(el); window.__e2eToasts.push(el.textContent.trim()); }
  });
  if (window.__e2eToastObserver) window.__e2eToastObserver.disconnect();
  window.__e2eToastObserver = new MutationObserver(collect);
  window.__e2eToastObserver.observe(document.body, { childList: true, subtree: true, characterData: true });
  collect();
}"""

TOAST_HISTORY_JS = "() => window.__e2eToasts || []"


def expect_toast(ctx: Ctx, label: str, text: str, trigger, *, timeout: int = 12000) -> bool:
    """执行 trigger() 之后，等到出现一条包含 text 的 *新* Toast（触发前已有的 Toast 一律不算）。"""
    # 先等已有的 Toast 自己走完：连着两次同样的提示（先改只读、再改回可编辑，都是「角色已更新」）时，
    # 上一条还挂在屏幕上，新一条不会另起一个元素，被当成「触发前已有的」而永远等不到
    # （云端请求不再被 localhost → ::1 的 2 秒延迟拖慢之后，两步之间的间隔短于 Toast 的停留时间，这个问题才暴露）
    try:
        ctx.page.wait_for_function("() => !document.querySelector('.m-enqueued-snackbars .m-snack__wrapper')", timeout=10000)
    except PlaywrightTimeoutError:
        pass  # 排空失败不是这里要判的事，继续按原逻辑等新 Toast，超时时诊断里会看到当前 Toast
    ctx.page.evaluate(MARK_TOASTS_JS)
    ctx.page.evaluate(TOAST_RECORDER_JS)
    trigger()
    try:
        ctx.page.wait_for_function(NEW_TOAST_JS, arg=text, timeout=timeout)
    except PlaywrightTimeoutError:
        history = ctx.page.evaluate(TOAST_HISTORY_JS)
        return ctx.run.check(
            label, False,
            f"没等到 Toast「{text}」；触发后出现过的 Toast：{history}；当前 Toast：{toast_texts(ctx.page)}；{diagnostics(ctx)}",
        )
    passed = ctx.run.check(label, True)
    wait_operation_settled(ctx)
    return passed


# 成员页的云端操作（邀请 / 改角色 / 移除 / 转让 / 退出）：成功提示在操作中途就出现了，随后还有一次成员刷新，
# 整个过程里成员行都是 disabled 的（页面的 _busy 标记）。提示出现后立刻手势 / 点击，会落在马上要重绘的列表上
IDLE_JS = "() => ![...document.querySelectorAll('[data-testid=\"member-cell\"]')].some((e) => e.disabled)"


def wait_operation_settled(ctx: Ctx) -> None:
    """等当前这次操作彻底做完（成员行恢复可点、动画停下）；等不到不在这里判，后面的断言会报出来。"""
    try:
        ctx.page.wait_for_function(IDLE_JS, timeout=10000)
        wait_animations_settled(ctx.page)
    except PlaywrightTimeoutError:
        pass


def wait_check(ctx: Ctx, label: str, predicate_js: str, *, arg=None, timeout: int = 8000) -> bool:
    try:
        ctx.page.wait_for_function(predicate_js, arg=arg, timeout=timeout)
        return ctx.run.check(label, True)
    except PlaywrightTimeoutError:
        return ctx.run.check(label, False, f"等待超时；{diagnostics(ctx)}")


def wait_gone(ctx: Ctx, label: str, test_id: str, *, timeout: int = 8000) -> bool:
    try:
        ctx.page.get_by_test_id(test_id).wait_for(state="detached", timeout=timeout)
        return ctx.run.check(label, True)
    except PlaywrightTimeoutError:
        return ctx.run.check(label, False, f"{test_id} 没有消失")


def members_path(ledger_id: str) -> str:
    return f"/ledgers/{ledger_id.lower()}/members"


def open_members(ctx: Ctx, ledger_id: str) -> None:
    """整页进入某账本的成员页，等加载骨架消失（本地缓存 + 联网刷新完成）。"""
    open_app(ctx.page, members_path(ledger_id))
    ctx.page.get_by_test_id("members-page").wait_for(state="visible")
    ctx.page.get_by_test_id("members-skeleton").wait_for(state="detached")
    wait_animations_settled(ctx.page)


def rows(page) -> "list[dict]":
    return page.evaluate(ROWS_JS)


def row_by_id(page, member_id: str) -> "dict | None":
    return next((r for r in rows(page) if r["id"] == member_id.lower()), None)


def groups(page) -> "list[dict]":
    return page.evaluate(GROUPS_JS)


def member_ids(page) -> "set[str]":
    return {r["id"] for r in rows(page)}


def wait_members(ctx: Ctx, label: str, expected_ids) -> bool:
    expected = sorted(i.lower() for i in expected_ids)
    return wait_check(
        ctx, label,
        """(ids) => JSON.stringify([...document.querySelectorAll('[data-testid="member-cell"]')]
            .map((e) => e.getAttribute('data-member-id')).sort()) === JSON.stringify(ids)""",
        arg=expected,
    )


def cell_of(page, member_id: str):
    return page.locator(f'[data-testid="member-cell"][data-member-id="{member_id.lower()}"]')


def swipe_row_selector(member_id: str) -> str:
    return f'[data-testid="member-row"][data-member-id="{member_id.lower()}"]'


def swipe_open(page, member_id: str) -> None:
    """触摸左滑展开某成员行（松手前停住，避开 CDP 合成触摸的惯性副作用，原因见 ledgers_page_test.swipe_open）。"""
    target = page.locator(swipe_row_selector(member_id)).get_by_test_id("member-cell")
    swipe_by(page, target, -170, 0, steps=10, duration_ms=220, release_hold_ms=150)
    wait_attr(page, swipe_row_selector(member_id), "data-open", "true")
    wait_animations_settled(page)
    page.wait_for_timeout(400)  # 松手后 350ms 内行内的 click 会被吞掉（ui.js 的 CLICK_SUPPRESS_MS）


def open_actions(page, member_id: str) -> None:
    cell_of(page, member_id).click()
    page.get_by_test_id("member-actions").wait_for(state="visible")
    wait_animations_settled(page)


def open_invite(page) -> None:
    page.get_by_test_id("member-invite").click()
    page.get_by_test_id("invite-sheet").wait_for(state="visible")
    wait_animations_settled(page)


def submit_invite(page, phone: str) -> None:
    page.get_by_test_id("invite-phone").fill(phone)
    page.get_by_test_id("invite-submit").click()


def close_invite(page) -> None:
    page.get_by_test_id("sheet-close").click()
    page.get_by_test_id("invite-sheet").wait_for(state="detached")


def covers(page, point, selector) -> bool:
    return bool(page.evaluate(HIT_JS, [point[0], point[1], selector]))


def still_logged_in(page) -> bool:
    return bool(page.evaluate(LOGOUT_PROBE_JS))


def seed_owned_ledger(ctx: Ctx, name: str, members: "list[tuple[Person, int]]") -> "tuple[str, str, dict[str, str]]":
    """造一个我是所有者的账本（本地 + 云端），按 (人, 角色) 加成员。返回 (账本 Id, 我的 Owner 成员行 Id, {手机号: 成员行 Id})。"""
    ledger_id = ctx.db.seed_ledger(ctx.me.local_id, name)
    me_member = ctx.world.own_ledger(ctx.me, ledger_id, name)
    member_ids_by_phone = {
        person.phone: ctx.world.add_member(ledger_id, person, role=role) for person, role in members
    }
    return ledger_id, me_member, member_ids_by_phone


# ---------------------------------------------------------------------------------------------- A. 所有者视角


def check_owner_view(ctx: Ctx, ledger_id: str, me_member: str, expected: "dict[str, tuple[Person, int]]") -> None:
    run, page = ctx.run, ctx.page
    open_members(ctx, ledger_id)
    wait_members(ctx, "所有者视角：成员行与造的数据一致（所有者 + 4 位成员）", {me_member, *expected})

    run.check("进入二级页面：Tab 栏隐藏", page.get_by_test_id("tab-bar").is_hidden())
    run.check("导航栏标题是 h1「成员管理」", page.get_by_test_id("nav-title").inner_text() == "成员管理")
    info = page.evaluate(SHELL_JS)
    run.check("标题是语义化的 h1", info["titleTag"] == "H1", str(info["titleTag"]))
    run.check("标题水平居中", abs((info["title"]["left"] + info["title"]["right"]) / 2 - SCREEN_W / 2) <= 1, str(info["title"]))
    run.check("所有者有右上角「＋」，读屏名称「邀请成员」，点击区域 ≥ 44 × 44",
              info["add"] is not None and min(info["add"]["width"], info["add"]["height"]) >= 44
              and page.get_by_test_id("member-invite").get_attribute("aria-label") == "邀请成员", str(info["add"]))
    run.check("返回在左、「＋」在右", info["back"]["left"] < SCREEN_W / 2 < info["add"]["left"])
    run.check("没有横向溢出", info["overflowX"] <= 0, str(info["overflowX"]))
    glass = page.evaluate(GLASS_LAYERS_JS)
    run.check("玻璃层数 ≤ 3（导航按钮 + 导航栏；性能预算）", glass <= 3, str(glass))

    grouped = groups(page)
    run.check("按角色分组，顺序：所有者 → 可编辑 → 只读，标题与角色徽标同名",
              [g["id"] for g in grouped] == ["member-group-owner", "member-group-editor", "member-group-viewer"]
              and [g["header"] for g in grouped] == ["所有者", "可编辑", "只读"], str(grouped))
    editors = {mid for mid, (_, role) in expected.items() if role == ROLE_EDITOR}
    viewers = {mid for mid, (_, role) in expected.items() if role == ROLE_VIEWER}
    run.check("各组成员正确（所有者组只有我自己）",
              grouped[0]["members"] == [me_member.lower()] and set(grouped[1]["members"]) == editors
              and set(grouped[2]["members"]) == viewers, str(grouped))
    run.check("分组自上而下排列", grouped[0]["top"] < grouped[1]["top"] < grouped[2]["top"])

    by_id = {r["id"]: r for r in rows(page)}
    mine = by_id[me_member.lower()]
    run.check("我的行：脱敏手机号 + 角色说明 + 徽标「所有者」「你」",
              mine["title"] == mask(ctx.me.phone) and mine["subtitle"] == "管理成员，拥有账本全部权限"
              and mine["badges"] == ["所有者", "你"] and mine["me"], str(mine))
    run.check("我的行不可管理：普通 div、没有箭头、没有左滑", mine["tag"] == "DIV" and not mine["chevron"] and not mine["swipe"], str(mine))
    others_ok = True
    for mid, (person, role) in expected.items():
        row = by_id[mid.lower()]
        label = "可编辑" if role == ROLE_EDITOR else "只读"
        description = "可记账，可编辑自己的账单" if role == ROLE_EDITOR else "只读，仅可查看账单"
        good = (row["title"] == mask(person.phone) and row["subtitle"] == description and row["badges"] == [label]
                and not row["me"] and row["tag"] == "BUTTON" and row["chevron"] and row["swipe"]
                and row["role"] == ("editor" if role == ROLE_EDITOR else "viewer") and abs(row["height"] - 66) <= 1)
        if not good:
            others_ok = False
            run.check(f"成员行（{label}）内容 / 可管理标记不对", False, str(row))
    run.check("别的成员：脱敏手机号 + 角色说明 + 角色徽标，可点（按钮）、有箭头、可左滑，行高 66", others_ok)

    run.check("列表末尾有「邀请成员」入口与说明脚注",
              page.get_by_test_id("member-invite-cell").is_visible()
              and "左滑成员可移除" in page.get_by_test_id("member-invite-group").inner_text())
    run.check("所有者没有「退出账本」", page.get_by_test_id("member-exit").count() == 0)
    shot(page, "members-owner")


# ---------------------------------------------------------------------------------------------- B. 邀请 Sheet


def check_invite_sheet(ctx: Ctx, ledger_id: str, existing_phone: str, invitees: "dict[str, Person]") -> dict:
    """返回云端上新增的待确认成员（手机号 → 成员行 Id）。"""
    run, page, cloud = ctx.run, ctx.page, ctx.cloud
    members_post = rf"/api/ledgers/{ledger_id.lower()}/members$"
    posts_before = len(cloud.calls("POST", members_post))

    open_invite(page)
    sheet = page.get_by_test_id("invite-sheet")
    phone_box = page.get_by_test_id("invite-phone")
    run.check("点「＋」弹出邀请 Sheet，标题「邀请成员」", sheet.is_visible() and page.get_by_test_id("sheet-title").inner_text() == "邀请成员")
    run.check("手机号输入框：tel / 数字键盘 / 最多 11 位，有读屏名称「手机号」",
              phone_box.get_attribute("type") == "tel" and phone_box.get_attribute("inputmode") == "numeric"
              and phone_box.get_attribute("maxlength") == "11" and page.get_by_label("手机号").count() == 1)
    run.check("角色是单选组：默认「可编辑」被选中，「只读」未选",
              page.get_by_test_id("invite-roles").get_attribute("role") == "radiogroup"
              and page.get_by_test_id("invite-role-editor").get_attribute("aria-checked") == "true"
              and page.get_by_test_id("invite-role-viewer").get_attribute("aria-checked") == "false")
    run.check("每个角色带一句话说明",
              "可记账，可编辑自己的账单" in page.get_by_test_id("invite-role-editor").inner_text()
              and "只读，仅可查看账单" in page.get_by_test_id("invite-role-viewer").inner_text())

    page.get_by_test_id("invite-role-viewer").click()
    wait_check(ctx, "点「只读」：选中态切换（aria-checked 与对勾同步）", """() => {
        const pick = (id) => document.querySelector(`[data-testid="${id}"]`);
        const viewer = pick('invite-role-viewer'), editor = pick('invite-role-editor');
        return viewer.getAttribute('aria-checked') === 'true' && editor.getAttribute('aria-checked') === 'false'
            && viewer.querySelectorAll('.ft-cell__check').length === 1 && editor.querySelectorAll('.ft-cell__check').length === 0;
    }""")

    # 就地校验：与登录同一套规则，不发任何请求
    def error_text() -> str:
        node = page.get_by_test_id("invite-error")
        return node.inner_text().strip() if node.count() else ""

    page.get_by_test_id("invite-submit").click()
    wait_check(ctx, "空手机号：就地提示「手机号不能为空」", "() => !!document.querySelector('[data-testid=\"invite-error\"]')")
    run.check("空手机号提示文案", error_text() == "手机号不能为空", error_text())
    run.check("错误提示是 role=alert（读屏会播报）", page.get_by_test_id("invite-error").get_attribute("role") == "alert")
    wait_check(ctx, "校验失败后焦点回到手机号输入框（直接改号重试）",
               "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'invite-phone'")

    phone_box.fill("123")
    page.get_by_test_id("invite-submit").click()
    wait_check(ctx, "不足 11 位：提示「手机号必须是11位」", "() => document.querySelector('[data-testid=\"invite-error\"]')?.textContent.trim() === '手机号必须是11位'")
    phone_box.fill("23800138000")
    page.get_by_test_id("invite-submit").click()
    wait_check(ctx, "不是 1 开头：提示「手机号格式不正确」", "() => document.querySelector('[data-testid=\"invite-error\"]')?.textContent.trim() === '手机号格式不正确'")
    phone_box.fill("1380013800")
    wait_check(ctx, "继续输入后错误提示消失", "() => !document.querySelector('[data-testid=\"invite-error\"]')")
    run.check("格式校验不会发出任何云端请求", len(cloud.calls("POST", members_post)) == posts_before)
    run.check("校验失败时 Sheet 保持打开", sheet.is_visible())

    # 键盘输入：每个按键都会往返一次服务端（oninput），用接近真人的速度敲，超出 11 位的部分被浏览器截断
    phone_box.fill("")
    page.wait_for_timeout(300)
    phone_box.focus()
    phone_box.press_sequentially("138001380001234", delay=40)
    page.wait_for_timeout(300)
    run.check("键盘输入超过 11 位会被截断", phone_box.input_value() == "13800138000", phone_box.input_value())

    # 云端拒绝：走 Toast，Sheet 与输入保留
    unregistered = unique_phone()
    expect_toast(ctx, "手机号未注册：Toast「邀请失败：该手机号尚未注册」", "邀请失败：该手机号尚未注册",
                 lambda: submit_invite(page, unregistered))
    run.check("云端拒绝后 Sheet 保持打开，输入的手机号与所选角色保留",
              sheet.is_visible() and phone_box.input_value() == unregistered
              and page.get_by_test_id("invite-role-viewer").get_attribute("aria-checked") == "true")
    wait_check(ctx, "云端拒绝后焦点回到手机号输入框（提交按钮忙碌时被禁用，焦点不能丢）",
               "() => document.activeElement && document.activeElement.getAttribute('data-testid') === 'invite-phone'")
    run.check("拒绝时没有登出", still_logged_in(page))
    expect_toast(ctx, "邀请已是成员的人：Toast「邀请失败：该用户已是账本成员」", "邀请失败：该用户已是账本成员",
                 lambda: submit_invite(page, existing_phone))
    expect_toast(ctx, "邀请所有者本人：Toast「邀请失败：该用户是账本所有者」", "邀请失败：该用户是账本所有者",
                 lambda: submit_invite(page, ctx.me.phone))

    # 成功：邀请「只读」成员
    invited = invitees["viewer"]
    expect_toast(ctx, "邀请成功：Toast「邀请已发送，等待对方确认」", "邀请已发送，等待对方确认",
                 lambda: submit_invite(page, invited.phone))
    try:
        sheet.wait_for(state="detached", timeout=8000)
        run.check("邀请成功后 Sheet 收起", True)
    except PlaywrightTimeoutError:
        run.check("邀请成功后 Sheet 收起", False, "Sheet 没有收起")
        close_invite(page)

    request = cloud.wait_request("POST", members_post, count=posts_before + 4)
    keys = {k.lower(): v for k, v in (request.body or {}).items()} if request else {}
    run.check("云端收到的邀请请求：手机号 + 角色（只读 = 2），带着本人令牌",
              request is not None and keys.get("phonenumber") == invited.phone and keys.get("role") == ROLE_VIEWER
              and request.phone == ctx.me.phone, str(request))
    pending = cloud.member_of_phone(ledger_id, invited.phone)
    run.check("云端：新增一条待确认（Pending）的只读成员行", pending is not None and pending["status"] == 0 and pending["role"] == ROLE_VIEWER, str(pending))
    run.check("待确认的邀请不会出现在成员列表里（对方接受后才生效）", not any(r["title"] == mask(invited.phone) for r in rows(page)))
    result = {invited.phone: pending["id"]}

    # 再邀请同一个人：冲突提示，Sheet 保持
    open_invite(page)
    expect_toast(ctx, "重复邀请：Toast「邀请失败：该用户已被邀请，等待确认」", "邀请失败：该用户已被邀请，等待确认",
                 lambda: submit_invite(page, invited.phone))
    run.check("冲突后 Sheet 仍在，可以改手机号重试", sheet.is_visible())

    page.keyboard.press("Escape")
    try:
        sheet.wait_for(state="detached", timeout=8000)
        run.check("Esc 收起邀请 Sheet", True)
    except PlaywrightTimeoutError:
        run.check("Esc 收起邀请 Sheet", False, "Sheet 没有收起")
        close_invite(page)

    open_invite(page)
    run.check("重新打开：手机号清空、角色回到默认「可编辑」、没有残留错误",
              page.get_by_test_id("invite-phone").input_value() == ""
              and page.get_by_test_id("invite-role-editor").get_attribute("aria-checked") == "true"
              and page.get_by_test_id("invite-error").count() == 0)

    # 回车提交 + 邀请「可编辑」成员
    editor_invitee = invitees["editor"]
    page.get_by_test_id("invite-phone").fill(editor_invitee.phone)
    expect_toast(ctx, "键盘回车直接提交：Toast「邀请已发送，等待对方确认」", "邀请已发送，等待对方确认",
                 lambda: page.get_by_test_id("invite-phone").press("Enter"))
    page.get_by_test_id("invite-sheet").wait_for(state="detached")
    pending_editor = cloud.member_of_phone(ledger_id, editor_invitee.phone)
    run.check("回车提交：云端新增待确认的可编辑成员行", pending_editor is not None and pending_editor["status"] == 0 and pending_editor["role"] == ROLE_EDITOR, str(pending_editor))
    result[editor_invitee.phone] = pending_editor["id"]
    return result


# ---------------------------------------------------------------------------------------------- C. 读路径联网刷新


def check_refresh_on_load(ctx: Ctx, ledger_id: str, known_ids: "set[str]") -> str:
    """云端上其他设备造成的变化（有人接受了邀请），重新进入页面时联网刷新后可见。返回新成员的成员行 Id。"""
    run, page, cloud = ctx.run, ctx.page, ctx.cloud
    newcomer_phone = unique_phone()
    new_member = cloud.add_member(ledger_id, newcomer_phone, role=ROLE_VIEWER)
    pulls_before = len(cloud.calls("POST", "/api/sync/ledgermembers/pull"))

    open_members(ctx, ledger_id)
    wait_members(ctx, "云端新增的成员：重新进入页面后联网刷新，成员行出现", known_ids | {new_member})
    run.check("页面加载时确实联网拉取了成员关系", len(cloud.calls("POST", "/api/sync/ledgermembers/pull")) > pulls_before)
    row = row_by_id(page, new_member)
    run.check("新成员显示云端带回的手机号（脱敏），归在「只读」组",
              row is not None and row["title"] == mask(newcomer_phone) and row["role"] == "viewer", str(row))
    return new_member


# ---------------------------------------------------------------------------------------------- D. 操作菜单


def check_actions_menu(ctx: Ctx, ledger_id: str, editor_member: str, editor: Person, viewer_member: str, viewer: Person) -> None:
    run, page, cloud = ctx.run, ctx.page, ctx.cloud
    role_path = rf"/api/ledgers/{ledger_id.lower()}/members/{{id}}/role"

    open_actions(page, editor_member)
    menu = page.get_by_test_id("member-actions")
    text = menu.inner_text()
    run.check("点按「可编辑」成员：菜单标题是脱敏手机号，说明是角色与权限",
              mask(editor.phone) in text and "可编辑" in text and "可记账，可编辑自己的账单" in text, text)
    run.check("菜单项：改为只读成员 / 转让所有权 / 移除成员 / 取消",
              page.get_by_test_id("action-role").inner_text() == "改为只读成员"
              and page.get_by_test_id("action-transfer").inner_text() == "转让所有权"
              and page.get_by_test_id("action-remove").inner_text() == "移除成员"
              and page.get_by_test_id("actionsheet-cancel").inner_text() == "取消", text)
    shot(page, "members-actions")

    # 取消：不改动，没有云端写请求
    writes_before = len(cloud.calls("PUT")) + len(cloud.calls("DELETE")) + len(cloud.calls("POST", r"/transfer-ownership$"))
    page.get_by_test_id("actionsheet-cancel").click()
    wait_gone(ctx, "点「取消」收起菜单", "member-actions")
    run.check("取消不改动：没有任何云端写请求，角色不变",
              len(cloud.calls("PUT")) + len(cloud.calls("DELETE")) + len(cloud.calls("POST", r"/transfer-ownership$")) == writes_before
              and cloud.member(editor_member)["role"] == ROLE_EDITOR)

    # 可编辑 → 只读
    open_actions(page, editor_member)
    expect_toast(ctx, "改角色：Toast「角色已更新」", "角色已更新", lambda: page.get_by_test_id("action-role").click())
    wait_gone(ctx, "选中菜单项后菜单收起", "member-actions")
    request = cloud.wait_request("PUT", role_path.format(id=editor_member.lower()))
    keys = {k.lower(): v for k, v in (request.body or {}).items()} if request else {}
    run.check("云端收到改角色请求（只读 = 2）且成员状态已变化",
              request is not None and keys.get("role") == ROLE_VIEWER and cloud.member(editor_member)["role"] == ROLE_VIEWER, str(request))
    wait_check(ctx, "改完角色后该成员换到「只读」组（列表随云端刷新）",
               """(mid) => !!document.querySelector(`[data-testid="member-group-viewer"] [data-member-id="${mid}"]`)""",
               arg=editor_member.lower())
    row = row_by_id(page, editor_member)
    run.check("换组后行内徽标、角色标记同步更新", row is not None and row["badges"] == ["只读"] and row["role"] == "viewer", str(row))

    # 只读 → 可编辑
    open_actions(page, viewer_member)
    run.check("只读成员的菜单项是「改为可编辑成员」", page.get_by_test_id("action-role").inner_text() == "改为可编辑成员")
    expect_toast(ctx, "改回可编辑：Toast「角色已更新」", "角色已更新", lambda: page.get_by_test_id("action-role").click())
    wait_check(ctx, "只读成员换到「可编辑」组",
               """(mid) => !!document.querySelector(`[data-testid="member-group-editor"] [data-member-id="${mid}"]`)""",
               arg=viewer_member.lower())
    run.check("云端成员角色同步变化", cloud.member(viewer_member)["role"] == ROLE_EDITOR)


# ---------------------------------------------------------------------------------------------- E. 移除


def check_remove(ctx: Ctx, ledger_id: str, swipe_member: str, swipe_person: Person, menu_member: str, menu_person: Person) -> None:
    run, page, cloud, db = ctx.run, ctx.page, ctx.cloud, ctx.db
    delete_path = rf"/api/ledgers/{ledger_id.lower()}/members/"

    # 左滑「移除」→ Alert → 取消
    swipe_open(page, swipe_member)
    run.check("左滑展开后出现「移除」操作", page.locator(swipe_row_selector(swipe_member)).get_by_test_id("swipe-remove").is_visible())
    page.locator(swipe_row_selector(swipe_member)).get_by_test_id("swipe-remove").click()
    alert = page.get_by_test_id("member-remove-alert")
    alert.wait_for(state="visible")
    wait_animations_settled(page)
    run.check("移除确认 Alert：标题带脱敏手机号，说明移除后果，按钮「取消」「移除」",
              alert.get_by_test_id("alert-title").inner_text() == f"移除成员「{mask(swipe_person.phone)}」？"
              and "不能再查看或记录" in alert.get_by_test_id("alert-message").inner_text()
              and page.get_by_test_id("alert-cancel").inner_text() == "取消"
              and page.get_by_test_id("alert-confirm").inner_text() == "移除")
    run.check("Alert 在最上层（盖住列表与操作按钮）", covers(page, center_of(page, page.get_by_test_id("alert-confirm")), '[data-testid="alert-host"]'))
    shot(page, "members-remove-alert")
    page.get_by_test_id("alert-cancel").click()
    wait_gone(ctx, "点「取消」：Alert 收起", "alert-host")
    run.check("取消移除：没有 DELETE 请求，成员仍在云端与列表里",
              len(cloud.calls("DELETE", delete_path)) == 0 and not cloud.member(swipe_member)["isDeleted"]
              and row_by_id(page, swipe_member) is not None)

    # 再次左滑 → 确认
    if page.locator(swipe_row_selector(swipe_member)).get_attribute("data-open") != "true":
        swipe_open(page, swipe_member)
    page.locator(swipe_row_selector(swipe_member)).get_by_test_id("swipe-remove").click()
    page.get_by_test_id("member-remove-alert").wait_for(state="visible")
    wait_animations_settled(page)
    expect_toast(ctx, "确认移除：Toast「成员已移除」", "成员已移除", lambda: page.get_by_test_id("alert-confirm").click())
    wait_check(ctx, "被移除的成员从列表消失", "(mid) => !document.querySelector(`[data-member-id=\"${mid}\"]`)", arg=swipe_member.lower())
    run.check("云端：成员行已软删除", cloud.member(swipe_member)["isDeleted"])
    local = [m for m in db.ledger_members(ledger_id) if m["Id"].upper() == swipe_member.upper()]
    run.check("本地缓存：成员行同步标记为删除（云端不会再把被移除成员的行拉回来，必须由客户端自己对齐）",
              len(local) == 1 and local[0]["IsDeleted"] == 1, str(local))

    # 菜单「移除成员」（左滑的非滑动等价入口）
    open_actions(page, menu_member)
    page.get_by_test_id("action-remove").click()
    page.get_by_test_id("member-remove-alert").wait_for(state="visible")
    wait_animations_settled(page)
    run.check("经菜单移除：Alert 标题带该成员的脱敏手机号",
              page.get_by_test_id("alert-title").inner_text() == f"移除成员「{mask(menu_person.phone)}」？")
    run.check("菜单已收起，只剩 Alert", page.get_by_test_id("member-actions").count() == 0)
    expect_toast(ctx, "经菜单确认移除：Toast「成员已移除」", "成员已移除", lambda: page.get_by_test_id("alert-confirm").click())
    wait_check(ctx, "经菜单移除的成员从列表消失", "(mid) => !document.querySelector(`[data-member-id=\"${mid}\"]`)", arg=menu_member.lower())
    run.check("云端 / 本地缓存都已标记删除",
              cloud.member(menu_member)["isDeleted"]
              and [m["IsDeleted"] for m in db.ledger_members(ledger_id) if m["Id"].upper() == menu_member.upper()] == [1])

    # 重新进入：被移除的人不会再出现
    remaining = member_ids(page)
    open_members(ctx, ledger_id)
    run.check("重新进入页面（含联网刷新）：被移除的成员仍然不在", member_ids(page) == remaining, f"{member_ids(page)} vs {remaining}")


# ---------------------------------------------------------------------------------------------- F. 失败路径


def check_failures(ctx: Ctx, ledger_id: str, invitee: Person, target_member: str, target_person: Person) -> None:
    run, page, cloud = ctx.run, ctx.page, ctx.cloud
    members_post = r"/members$"

    open_invite(page)
    page.get_by_test_id("invite-phone").fill(invitee.phone)
    cloud.fail("POST", members_post, status=403, message="只有账本所有者可以邀请成员")
    expect_toast(ctx, "云端 403：Toast「无权限：…」（与登录过期区分）", "无权限：只有账本所有者可以邀请成员",
                 lambda: page.get_by_test_id("invite-submit").click())
    run.check("无权限不会登出：会话仍在、还在成员页、Sheet 还在", still_logged_in(page)
              and re.search(r"/members$", page.url) is not None and page.get_by_test_id("invite-sheet").is_visible())

    cloud.fail("POST", members_post, status=500, message="boom: NullReferenceException at Secret.Internal")
    expect_toast(ctx, "云端 5xx：Toast「邀请失败：云端服务暂时不可用，请稍后重试」", "邀请失败：云端服务暂时不可用，请稍后重试",
                 lambda: page.get_by_test_id("invite-submit").click())
    run.check("5xx 的内部细节不会露给用户", not any("boom" in t or "Secret" in t or "NullReference" in t for t in toast_texts(page)), str(toast_texts(page)))

    expect_toast(ctx, "故障清除后同一个 Sheet 里重试成功：Toast「邀请已发送，等待对方确认」", "邀请已发送，等待对方确认",
                 lambda: page.get_by_test_id("invite-submit").click())
    page.get_by_test_id("invite-sheet").wait_for(state="detached")
    run.check("重试成功后云端有该待确认邀请", cloud.member_of_phone(ledger_id, invitee.phone) is not None)

    # 移除被云端拒绝（403）：成员留在列表里
    open_actions(page, target_member)
    page.get_by_test_id("action-remove").click()
    page.get_by_test_id("member-remove-alert").wait_for(state="visible")
    wait_animations_settled(page)
    cloud.fail("DELETE", r"/members/", status=403, message="只有账本所有者可以执行此操作")
    expect_toast(ctx, "移除被云端拒绝（403）：Toast「无权限：…」", "无权限：只有账本所有者可以执行此操作",
                 lambda: page.get_by_test_id("alert-confirm").click())
    run.check("移除失败：成员仍在列表与云端，没有登出",
              row_by_id(page, target_member) is not None and not cloud.member(target_member)["isDeleted"] and still_logged_in(page))

    # 改角色时成员已不存在（404）：给出云端的说明
    open_actions(page, target_member)
    cloud.fail("PUT", r"/role$", status=404, message="成员不存在")
    expect_toast(ctx, "改角色遇到 404：Toast「修改失败：成员不存在」", "修改失败：成员不存在",
                 lambda: page.get_by_test_id("action-role").click())
    # 菜单在选中菜单项时就开始收起（260ms 动画），云端失败的 Toast 比动画结束得早，所以要等菜单消失，不能立刻数
    wait_gone(ctx, "失败后菜单已收起", "member-actions")
    run.check("失败后行仍在", row_by_id(page, target_member) is not None)


# ---------------------------------------------------------------------------------------------- G. 转让与退出


def go_to_members_via_ledgers(ctx: Ctx, ledger_name: str, ledger_id: str) -> None:
    """账本页 → 编辑 Sheet → 共享账本 → 邀请成员共享 → 成员页（这样「返回 / 退出后 Pop」有上一页可回）。"""
    page = ctx.page
    open_app(page, "/ledgers")
    page.get_by_test_id("ledger-cell").filter(has_text=ledger_name).first.click()
    page.get_by_test_id("ledger-sheet").wait_for(state="visible")
    page.get_by_test_id("ledger-share-cell").click()
    page.get_by_test_id("ledger-share-sheet").wait_for(state="visible")
    wait_animations_settled(page)
    page.get_by_test_id("share-invite").click()
    wait_url(page, rf"^{members_path(ledger_id)}$")
    page.get_by_test_id("members-page").wait_for(state="visible")
    page.get_by_test_id("members-skeleton").wait_for(state="detached")
    wait_animations_settled(page)


def check_transfer_and_exit(
    ctx: Ctx, name: str, ledger_id: str, me_member: str, target_member: str, target: Person, other_member: str
) -> None:
    run, page, cloud, db = ctx.run, ctx.page, ctx.cloud, ctx.db
    go_to_members_via_ledgers(ctx, name, ledger_id)
    wait_members(ctx, "转让前：所有者 + 2 位成员", {me_member, target_member, other_member})

    open_actions(page, target_member)
    page.get_by_test_id("action-transfer").click()
    alert = page.get_by_test_id("member-transfer-alert")
    alert.wait_for(state="visible")
    wait_animations_settled(page)
    run.check("转让确认 Alert：标题带脱敏手机号，说明转让后果",
              alert.get_by_test_id("alert-title").inner_text() == f"转让所有权给「{mask(target.phone)}」？"
              and "你将成为可编辑成员" in alert.get_by_test_id("alert-message").inner_text()
              and page.get_by_test_id("alert-confirm").inner_text() == "转让")
    page.get_by_test_id("alert-cancel").click()
    wait_gone(ctx, "取消转让：Alert 收起", "alert-host")
    run.check("取消转让：云端所有者不变", cloud.ledger(ledger_id)["ownerId"] == ctx.me.cloud_id
              and len(cloud.calls("POST", r"/transfer-ownership$")) == 0)

    open_actions(page, target_member)
    page.get_by_test_id("action-transfer").click()
    page.get_by_test_id("member-transfer-alert").wait_for(state="visible")
    wait_animations_settled(page)
    expect_toast(ctx, "确认转让：Toast「所有权已转让，你已成为可编辑成员」", "所有权已转让，你已成为可编辑成员",
                 lambda: page.get_by_test_id("alert-confirm").click())

    request = cloud.wait_request("POST", rf"/api/ledgers/{ledger_id.lower()}/transfer-ownership$")
    keys = {k.lower(): v for k, v in (request.body or {}).items()} if request else {}
    run.check("云端收到转让请求（目标用户 Id 是对方的云端 Id）", request is not None and str(keys.get("touserid", "")).lower() == target.cloud_id, str(request))
    run.check("云端：账本所有者变成对方，我降为可编辑成员",
              cloud.ledger(ledger_id)["ownerId"] == target.cloud_id and cloud.member(me_member)["role"] == ROLE_EDITOR
              and cloud.member(target_member)["role"] == ROLE_OWNER)

    wait_check(ctx, "转让后：对方进入「所有者」组，我降到「可编辑」组",
               """([target, me]) => !!document.querySelector(`[data-testid="member-group-owner"] [data-member-id="${target}"]`)
                   && !!document.querySelector(`[data-testid="member-group-editor"] [data-member-id="${me}"]`)""",
               arg=[target_member.lower(), me_member.lower()])
    wait_check(ctx, "转让后：右上角「＋」立即消失（所有者专属入口随身份重新判定）",
               "() => !document.querySelector('[data-testid=\"member-invite\"]')")
    run.check("转让后：列表末尾的「邀请成员」消失，换成「退出账本」",
              page.get_by_test_id("member-invite-cell").count() == 0 and page.get_by_test_id("member-exit").is_visible())
    mine = row_by_id(page, me_member)
    run.check("转让后：我的行是可编辑成员，仍有「你」标记，不可管理（普通 div，无左滑）",
              mine is not None and mine["badges"] == ["可编辑", "你"] and mine["tag"] == "DIV" and not mine["swipe"], str(mine))
    run.check("转让后：所有成员行都不可点、不可左滑（没有任何管理入口）",
              all(r["tag"] == "DIV" and not r["swipe"] and not r["chevron"] for r in rows(page)), str(rows(page)))
    shot(page, "members-after-transfer")

    # 退出账本（转让后的原所有者可以退出）
    page.get_by_test_id("member-exit").click()
    alert = page.get_by_test_id("member-exit-alert")
    alert.wait_for(state="visible")
    wait_animations_settled(page)
    run.check("退出确认 Alert：「退出这个账本？」+ 后果说明 + 红色「退出」",
              alert.get_by_test_id("alert-title").inner_text() == "退出这个账本？"
              and "本机已有的副本会保留" in alert.get_by_test_id("alert-message").inner_text()
              and "ft-alert__btn--danger" in (page.get_by_test_id("alert-confirm").get_attribute("class") or ""))
    page.get_by_test_id("alert-cancel").click()
    wait_gone(ctx, "取消退出：Alert 收起", "alert-host")
    run.check("取消退出：云端我的成员行仍然生效、仍在成员页",
              not cloud.member(me_member)["isDeleted"] and re.search(r"/members$", page.url) is not None)

    page.get_by_test_id("member-exit").click()
    page.get_by_test_id("member-exit-alert").wait_for(state="visible")
    wait_animations_settled(page)
    expect_toast(ctx, "确认退出：Toast「已退出共享账本」", "已退出共享账本", lambda: page.get_by_test_id("alert-confirm").click())
    wait_url(page, r"^/ledgers$")
    run.check("退出后返回上一页（账本页）", page.get_by_test_id("ledgers-page").is_visible())
    run.check("云端：我的成员行已软删除，所有者仍是对方", cloud.member(me_member)["isDeleted"] and cloud.ledger(ledger_id)["ownerId"] == target.cloud_id)
    local = [m for m in db.ledger_members(ledger_id) if m["Id"].upper() == me_member.upper()]
    run.check("本地缓存：我的成员行同步标记为删除", len(local) == 1 and local[0]["IsDeleted"] == 1, str(local))
    run.check("退出没有登出", still_logged_in(page))


# ---------------------------------------------------------------------------------------------- H. 非所有者视角


def check_non_owner_view(ctx: Ctx, label: str, ledger_id: str, my_member: str, my_role: int, owner: Person, owner_member: str) -> None:
    run, page = ctx.run, ctx.page
    open_members(ctx, ledger_id)
    role_label = "可编辑" if my_role == ROLE_EDITOR else "只读"
    run.check(f"{label}：没有右上角「＋」", page.get_by_test_id("member-invite").count() == 0)
    run.check(f"{label}：没有列表末尾的「邀请成员」", page.get_by_test_id("member-invite-cell").count() == 0)
    run.check(f"{label}：没有可左滑的行", page.locator(".ft-swipe").count() == 0)
    all_rows = rows(page)
    run.check(f"{label}：所有成员行都是不可点的 div，没有箭头", all(r["tag"] == "DIV" and not r["chevron"] for r in all_rows), str(all_rows))
    by_id = {r["id"]: r for r in all_rows}
    mine = by_id.get(my_member.lower())
    run.check(f"{label}：我的行带「{role_label}」徽标与「你」标记", mine is not None and mine["badges"] == [role_label, "你"] and mine["me"], str(mine))
    owner_row = by_id.get(owner_member.lower())
    run.check(f"{label}：所有者行显示脱敏手机号与「所有者」徽标，没有「你」",
              owner_row is not None and owner_row["title"] == mask(owner.phone) and owner_row["badges"] == ["所有者"] and not owner_row["me"], str(owner_row))
    run.check(f"{label}：末尾是「退出账本」，说明只有所有者能管理成员",
              page.get_by_test_id("member-exit").is_visible() and "只有账本所有者可以邀请、移除成员或修改角色" in page.get_by_test_id("member-exit-group").inner_text())
    shot(page, f"members-non-owner-{my_role}")


# ---------------------------------------------------------------------------------------------- I. 云端不可用


def check_cloud_down(ctx: Ctx, ledger_id: str, expected_ids: "set[str]", editable_member: str, invitee: Person) -> None:
    run, page, cloud = ctx.run, ctx.page, ctx.cloud
    cloud.stop()
    try:
        started = time.time()
        open_members(ctx, ledger_id)
        elapsed = time.time() - started
        run.check("云端不可用：页面仍能打开（读本地缓存），没有被拖住", elapsed < 10, f"{elapsed:.1f}s")
        wait_members(ctx, "云端不可用：成员列表来自本地缓存（与最后一次同步一致）", expected_ids)
        run.check("云端不可用：所有者身份来自本地缓存，仍有「＋」与管理入口",
                  page.get_by_test_id("member-invite").is_visible() and page.get_by_test_id("member-invite-cell").is_visible())
        run.check("云端不可用：不会弹出「加载失败」整页错误", page.get_by_test_id("members-error").count() == 0)

        open_invite(page)
        page.get_by_test_id("invite-phone").fill(invitee.phone)
        expect_toast(ctx, "云端不可用：邀请给出「无法连接云端」提示", "邀请失败：无法连接云端，请检查网络后重试",
                     lambda: page.get_by_test_id("invite-submit").click())
        run.check("邀请失败后 Sheet 保留输入，没有登出",
                  page.get_by_test_id("invite-sheet").is_visible() and page.get_by_test_id("invite-phone").input_value() == invitee.phone
                  and still_logged_in(page))

        # 云端恢复：同一个 Sheet 里直接重试成功
        cloud.start()
        expect_toast(ctx, "云端恢复后：同一个 Sheet 里重试成功", "邀请已发送，等待对方确认",
                     lambda: page.get_by_test_id("invite-submit").click())
        page.get_by_test_id("invite-sheet").wait_for(state="detached")
        run.check("云端恢复后：云端有该待确认邀请", cloud.member_of_phone(ledger_id, invitee.phone) is not None)

        # 改角色在云端不可用时失败，列表保持原样
        cloud.stop()
        before = {r["id"]: r["role"] for r in rows(page)}
        open_actions(page, editable_member)
        expect_toast(ctx, "云端不可用：改角色给出「修改失败：无法连接云端…」", "修改失败：无法连接云端，请检查网络后重试",
                     lambda: page.get_by_test_id("action-role").click())
        run.check("改角色失败：列表与角色保持原样", {r["id"]: r["role"] for r in rows(page)} == before)
    finally:
        try:
            cloud.start()
        except CloudPortBusy as exc:
            run.check("假云端恢复（后面的检查依赖它）", False, str(exc))


# ---------------------------------------------------------------------------------------------- J. 异常入口


def check_edge_entries(ctx: Ctx) -> None:
    run, page = ctx.run, ctx.page
    # 非法账本 Id 只可能来自手输 / 书签的地址：提示「账本不存在」并 Pop 回上一页。
    # 网页里整页进入时 Pop 是浏览器回退（整页重载），Toast 随旧文档一起消失，这里只能断言「不会停在打不开的成员页」；
    # 站内（MAUI 里的真实路径）Toast 会留着，列入 ticket 06 的人工验收
    open_app(page, "/ledgers")
    page.get_by_test_id("ledgers-page").wait_for(state="visible")
    page.goto(f"{BASE_URL}/ledgers/not-a-guid/members", wait_until="commit")
    try:
        wait_url(page, r"^/ledgers$")
        page.get_by_test_id("ledgers-page").wait_for(state="visible")
        run.check("非法账本 Id：不会停在打不开的成员页，直接回到上一页", True)
    except PlaywrightTimeoutError:
        run.check("非法账本 Id：不会停在打不开的成员页，直接回到上一页", False, page.url)

    open_members(ctx, str(uuid.uuid4()))
    run.check("没有任何成员信息的账本：显示「暂无成员」空状态，没有管理入口",
              page.get_by_test_id("members-empty").is_visible() and page.get_by_test_id("member-invite").count() == 0
              and page.get_by_test_id("member-exit").count() == 0)


# ---------------------------------------------------------------------------------------------- 主流程


def main() -> int:
    run = TestRun("ledger-members-page")
    require_server()
    db = LocalDb()
    db.verify_schema()

    try:
        cloud = FakeCloud().start()
    except CloudPortBusy as exc:
        print(f"[FAIL] {exc}\n请关掉占用该端口的进程，或用 E2E_CLOUD_PORT 与 server.ps1 -CloudPort 换一个端口。")
        return 1

    try:
        with session() as s:
            page = s.page
            info = login(page)
            world = CloudWorld(cloud, db)
            me = world.me(info.phone)
            ctx = Ctx(run, page, db, cloud, world, me)

            # ---- 造数据：我的账本 L1（我 + 2 位可编辑 + 2 位只读）、L2（转让 / 退出专用）、别人的账本各一
            a, b, d, e = (world.person() for _ in range(4))
            name1 = unique_name("成员页账本")
            ledger1, me_member1, ids1 = seed_owned_ledger(ctx, name1, [(a, ROLE_EDITOR), (b, ROLE_VIEWER), (d, ROLE_EDITOR), (e, ROLE_VIEWER)])
            expected1 = {ids1[a.phone]: (a, ROLE_EDITOR), ids1[b.phone]: (b, ROLE_VIEWER), ids1[d.phone]: (d, ROLE_EDITOR), ids1[e.phone]: (e, ROLE_VIEWER)}

            # A. 所有者视角
            check_owner_view(ctx, ledger1, me_member1, expected1)

            # B. 邀请 Sheet
            invitees = {"viewer": world.person(), "editor": world.person()}
            pending = check_invite_sheet(ctx, ledger1, a.phone, invitees)

            # C. 读路径联网刷新：云端上有人接受了邀请
            known = {me_member1.lower(), *(i.lower() for i in expected1)}
            newcomer_member = check_refresh_on_load(ctx, ledger1, known)
            known.add(newcomer_member.lower())

            # D. 操作菜单（A 可编辑→只读，B 只读→可编辑）
            check_actions_menu(ctx, ledger1, ids1[a.phone], a, ids1[b.phone], b)

            # E. 移除（D 左滑，E 经菜单）
            check_remove(ctx, ledger1, ids1[d.phone], d, ids1[e.phone], e)
            known -= {ids1[d.phone].lower(), ids1[e.phone].lower()}

            # F. 失败路径（目标：B，现在是可编辑成员）
            check_failures(ctx, ledger1, world.person(), ids1[b.phone], b)

            # G. 转让与退出（L2：我 + T 可编辑 + V 只读；转让给 T，然后退出）
            t, v = world.person(), world.person()
            name2 = unique_name("转让退出账本")
            ledger2, me_member2, ids2 = seed_owned_ledger(ctx, name2, [(t, ROLE_EDITOR), (v, ROLE_VIEWER)])
            check_transfer_and_exit(ctx, name2, ledger2, me_member2, ids2[t.phone], t, ids2[v.phone])

            # H. 非所有者视角
            owner_x = world.person()
            foreign_editor = world.foreign_ledger(owner_x, unique_name("别人的可编辑账本"), me=me, my_role=ROLE_EDITOR)
            world.add_member(foreign_editor.ledger_id, world.person(), role=ROLE_VIEWER)
            check_non_owner_view(ctx, "可编辑成员", foreign_editor.ledger_id, foreign_editor.my_member_id, ROLE_EDITOR, owner_x, foreign_editor.owner_member_id)
            owner_y = world.person()
            foreign_viewer = world.foreign_ledger(owner_y, unique_name("别人的只读账本"), me=me, my_role=ROLE_VIEWER)
            check_non_owner_view(ctx, "只读成员", foreign_viewer.ledger_id, foreign_viewer.my_member_id, ROLE_VIEWER, owner_y, foreign_viewer.owner_member_id)

            # I. 云端不可用（放在最后：之后 Web.Server 会短暂记住「云端不可用」）
            open_members(ctx, ledger1)
            current_ids = member_ids(page)
            check_cloud_down(ctx, ledger1, current_ids, ids1[b.phone], world.person())

            # J. 异常入口
            check_edge_entries(ctx)

            run.check_clean(s.errors)
    finally:
        cloud.stop()

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
