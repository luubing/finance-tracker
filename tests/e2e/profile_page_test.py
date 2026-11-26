# -*- coding: utf-8 -*-
"""
E2E 测试：我的 Tab（iOS UI 重构 · 13 号 ticket）。

只覆盖网页层——Android / iOS 上的真机手感（下拉刷新的跟手与回弹、短信读取权限的真实授权状态、云端同步成功路径、
退出登录后回登录页的转场、玻璃模糊）见 ticket 13 的人工验收清单。云端同步成功路径依赖真实云端（假云端只实现登录与成员接口），
这里验证的是云端不可用时「立即同步」给出既有提示、不报错、不登出。

  A. 外壳与资料卡：大标题页（滚动容器、34px 大标题；导航栏右侧没有任何按钮，也没有刷新按钮）；资料卡（脱敏手机号与登录用的手机号对应、注册日期、头像、
     手机号与「注册于」之间没有被 <p> 外边距撑开）；分组顺序与边距；8 个入口的顺序 / 标题 / 分组；已迁出的四个入口不再出现；
     新用户的摘要值（0 个 / 未设置 / 预设分类与渠道个数 / 短信未开启 / 没有角标）；退出登录行；玻璃层预算；底部留白
  B. 入口：8 个入口各自进入对应页面、Tab 栏隐藏，返回后 Tab 栏恢复、仍停在「我的」，会话无页面异常
  C. 摘要值（有数据）：账本只数自己的（不含已删除、别人的、加入的共享账本）、预算只数本月「全部账本」口径、分类 / 支付渠道 = 预设 +
     自己的自定义（不含已删除与别人的）、待确认账单角标（3 / 99+）与读屏名称
  D. 同步行：已同步 / 有待同步（含上次失败的）两种状态的文案、图标、配色；云端不可用时点「立即同步」给出「同步失败: …」提示，
     不离开页面、不登出、按钮恢复、待同步数量不变；点按热区 ≥ 44
  E. 刷新：下拉刷新（真实触摸，唯一的刷新入口）只重读本页数据——更新摘要值、不向云端发同步请求；拉得不够 / 不在顶部不触发；
     二级页面关闭后自动重读摘要（账本个数、待确认账单角标）
  F. 大标题折叠：向上滚动后折叠为居中小标题，回到顶部展开
  G. 沉浸式：模拟刘海（状态栏 47）+ Home 指示条（34）后导航栏让出状态栏、内容不被 Tab 栏遮挡、Tab 栏压住指示条所在的安全区但不盖住指示条本体、浅色导航栏 → 深色状态栏图标
  H. 未登录：显示「请先登录」引导，「去登录」进入登录页
  I. 退出登录：Alert 取消 / Esc 都留在原地；确认后清登录状态回登录页（没有 Tab 栏）并提示；换号登录后不残留上一个用户的资料与摘要

可观察属性（已登记到 README）：profile-page（滚动容器 data-scroll-root、data-title-collapsed、aria-busy）、profile-ptr、
profile-card（profile-avatar / profile-phone / profile-registered）、sync-row（data-state、data-pending）/ sync-now、
profile-entry-{ledgers|budgets|categories|payment-channels|pending-bills|sms-recognition|import|log-viewer}、profile-pending-count、
profile-group-{manage|auto|other|logout}、profile-logout、profile-logout-alert、profile-login-empty / profile-login。
"""
import re
import sys
from datetime import datetime, timedelta

from e2e_base import (
    GLASS_LAYERS_JS,
    PHONE_VIEWPORT,
    ROLE_EDITOR,
    MEMBER_ACTIVE,
    SYNC_FAILED,
    SYNC_PENDING,
    SYNC_SYNCED,
    LocalDb,
    TestRun,
    active_tab,
    login,
    open_app,
    open_tab,
    pull_down,
    pull_refresh,
    require_server,
    scroll_root_state,
    scroll_root_to,
    session,
    set_capture,
    shot,
    tab_bar,
    unique_name,
    unique_phone,
    wait_animations_settled,
    wait_attr,
    wait_scroll_root_stable,
    wait_secondary_pages_closed,
    wait_state,
    wait_tab_bar,
    wait_url,
)
from fake_cloud import CloudPortBusy, FakeCloud

SCREEN_W = PHONE_VIEWPORT["width"]
SCREEN_H = PHONE_VIEWPORT["height"]
TOP = 47  # 灵动岛 / 刘海机型状态栏高度
BOTTOM = 34  # Home 指示条所在的底部安全区高度
HOME_INDICATOR_TOP = 13  # Home 指示条本体（高 5，离屏幕底 8）的顶边离屏幕底的距离

RGB_DANGER_TEXT = "rgb(211, 34, 26)"  # --ft-danger-text #D3221A
RGB_WARNING_TEXT = "rgb(154, 80, 0)"  # --ft-warning-text #9A5000（23 号起与预算提示条共用同一个令牌）
RGB_BADGE = "rgb(224, 42, 31)"  # --ft-badge-solid #E02A1F

#: 入口：测试 id 后缀、标题、所属分组、点进去后的地址
ENTRIES = [
    ("ledgers", "账本管理", "profile-group-manage", r"^/ledgers$"),
    ("budgets", "预算管理", "profile-group-manage", r"^/budgets$"),
    ("categories", "分类管理", "profile-group-manage", r"^/categories$"),
    ("payment-channels", "支付渠道", "profile-group-manage", r"^/payment-channels$"),
    ("pending-bills", "待确认账单", "profile-group-auto", r"^/pending-bills$"),
    ("sms-recognition", "短信识别", "profile-group-auto", r"^/sms-recognition$"),
    ("import", "数据导入", "profile-group-auto", r"^/import$"),
    ("log-viewer", "错误日志", "profile-group-other", r"^/log-viewer$"),
]

#: 已迁出「我的」的入口（账单列表 / 趋势分析 / 年度报告 = 账单与统计 Tab；自定义报表 = 统计页右上角）
REMOVED_ENTRIES = ["账单列表", "趋势分析", "年度报告", "自定义报表"]

GROUP_ORDER = [
    "profile-card-group",
    "profile-sync-group",
    "profile-group-manage",
    "profile-group-auto",
    "profile-group-other",
    "profile-group-logout",
]

LAYOUT_JS = """() => {
  const root = document.querySelector('[data-testid="profile-page"]');
  const nav = root.querySelector('[data-testid="large-nav"]');
  const title = root.querySelector('[data-testid="large-title"]');
  const navTitle = root.querySelector('[data-testid="large-nav-title"]');
  const nr = nav.getBoundingClientRect();
  const tr = title.getBoundingClientRect();
  const ntr = navTitle.getBoundingClientRect();
  const ncs = getComputedStyle(nav);
  const tcs = getComputedStyle(title);
  const nts = getComputedStyle(navTitle);
  const actions = root.querySelector('.ft-lnav__actions').getBoundingClientRect();
  return {
    isScrollRoot: root.hasAttribute('data-scroll-root'),
    overflowY: getComputedStyle(root).overflowY,
    collapsed: root.getAttribute('data-title-collapsed'),
    h1Count: root.querySelectorAll('h1').length,
    title: { tag: title.tagName, text: title.textContent.trim(), size: tcs.fontSize, weight: tcs.fontWeight, top: tr.top, bottom: tr.bottom },
    nav: { top: nr.top, bottom: nr.bottom, height: nr.height, position: ncs.position, bg: ncs.backgroundColor },
    navTitle: { opacity: parseFloat(nts.opacity), ariaHidden: navTitle.getAttribute('aria-hidden'), text: navTitle.textContent.trim(), centerX: (ntr.left + ntr.right) / 2, right: ntr.right },
    actionsLeft: actions.left,
    navButtons: root.querySelectorAll('.ft-lnav__actions button').length,
    refreshEntries: root.querySelectorAll('[data-testid="profile-refresh"], button[aria-label="刷新"], .mdi-refresh').length,
    legacyContainer: !!root.querySelector('.container'),
    max: root.scrollHeight - root.clientHeight,
    scrollTop: root.scrollTop,
    overflowX: document.documentElement.scrollWidth - document.documentElement.clientWidth,
  };
}"""

CARD_JS = """() => {
  const root = document.querySelector('[data-testid="profile-page"]');
  const q = (id) => root.querySelector('[data-testid="' + id + '"]');
  const box = (el) => { const r = el.getBoundingClientRect(); return { left: r.left, right: r.right, top: r.top, bottom: r.bottom, width: r.width, height: r.height }; };
  const avatar = q('profile-avatar');
  const phone = q('profile-phone');
  const since = q('profile-registered');
  const pcs = getComputedStyle(phone);
  const scs = getComputedStyle(since);
  return {
    card: box(q('profile-card')), avatar: box(avatar), phone: box(phone), since: box(since),
    avatarAria: avatar.getAttribute('aria-hidden'),
    phoneText: phone.textContent.trim(), sinceText: since.textContent.trim(),
    phoneSize: pcs.fontSize, phoneWeight: pcs.fontWeight, sinceSize: scs.fontSize,
    phoneMarginBottom: parseFloat(pcs.marginBottom), sinceMarginBottom: parseFloat(scs.marginBottom),
  };
}"""

GROUPS_JS = """() => [...document.querySelectorAll('[data-testid="profile-page"] .ft-group')].map((g) => {
  const r = g.getBoundingClientRect();
  return { id: g.getAttribute('data-testid'), label: g.getAttribute('aria-label'), left: r.left, right: r.right, top: r.top, bottom: r.bottom };
})"""

ENTRIES_JS = """() => [...document.querySelectorAll('[data-testid="profile-page"] [data-testid^="profile-entry-"]')].map((el) => {
  const r = el.getBoundingClientRect();
  const text = (selector) => { const node = el.querySelector(selector); return node ? node.textContent.trim() : null; };
  const group = el.closest('[data-testid^="profile-group-"]');
  return {
    id: el.getAttribute('data-testid').replace('profile-entry-', ''),
    tag: el.tagName,
    title: text('.ft-cell__title'),
    value: text('.ft-cell__value'),
    aria: el.getAttribute('aria-label'),
    chevron: !!el.querySelector('.ft-cell__chevron'),
    group: group ? group.getAttribute('data-testid') : null,
    height: r.height, top: r.top,
  };
})"""

SYNC_JS = """() => {
  const el = document.querySelector('[data-testid="sync-row"]');
  if (!el) return null;
  const tile = el.querySelector('.ft-tile');
  const btn = el.querySelector('[data-testid="sync-now"]');
  const subtitle = el.querySelector('.ft-cell__subtitle');
  const br = btn.getBoundingClientRect();
  const pill = btn.querySelector('.ft-sync__pill').getBoundingClientRect();
  return {
    state: el.getAttribute('data-state'),
    pending: el.getAttribute('data-pending'),
    title: el.querySelector('.ft-cell__title').textContent.trim(),
    subtitle: subtitle ? subtitle.textContent.trim() : '',
    subtitleColor: subtitle ? getComputedStyle(subtitle).color : '',
    icon: tile.querySelector('i').className,
    tone: [...tile.classList].find((c) => c.startsWith('ft-tile--')),
    btnText: btn.textContent.trim(),
    btnDisabled: btn.disabled,
    busy: btn.getAttribute('aria-busy'),
    btnW: br.width, btnH: br.height, pillH: pill.height,
  };
}"""

LOGOUT_JS = """() => {
  const el = document.querySelector('[data-testid="profile-logout"]');
  const title = el.querySelector('.ft-cell__title');
  const r = el.getBoundingClientRect();
  const tr = title.getBoundingClientRect();
  return {
    tag: el.tagName, text: title.textContent.trim(), color: getComputedStyle(title).color,
    height: r.height, centerX: (tr.left + tr.right) / 2, rowCenterX: (r.left + r.right) / 2,
  };
}"""

# 底部留白：滚到底后最后一项内容在 Tab 栏之上（与 tab_shell_test 同一口径）
BOTTOM_CLEARANCE_JS = """() => {
  const root = document.querySelector('[data-testid="profile-page"]');
  const tab = document.querySelector('[data-testid="tab-bar"] nav').getBoundingClientRect();
  const bodyBottoms = [...root.querySelector('.ft-tabpage__body').children].map((c) => c.getBoundingClientRect()).filter((r) => r.height > 0).map((r) => r.bottom);
  return { tabTop: tab.top, tabBottom: tab.bottom, lowest: Math.max(...bodyBottoms), paddingBottom: parseFloat(getComputedStyle(root).paddingBottom) };
}"""

PTR_IDLE_JS = """() => {
  const zone = document.querySelector('[data-testid="profile-ptr"]');
  const scroller = document.querySelector('[data-testid="profile-page"]');
  if (!zone || !scroller) return false;
  return zone.getAttribute('data-state') === 'idle' && !scroller.style.getPropertyValue('--ft-ptr-offset');
}"""

# 「已退出登录」Toast 被加入页面的次数（它随布局切换很快消失，事后数不出来，所以在确认前用 MutationObserver 布防）
WATCH_LOGOUT_TOAST_JS = """() => {
  window.__ftLogoutToast = 0;
  new MutationObserver((records) => {
    for (const record of records) {
      for (const node of record.addedNodes) {
        if (node.nodeType === 1 && (node.textContent || '').includes('已退出登录')) window.__ftLogoutToast += 1;
      }
    }
  }).observe(document.body, { childList: true, subtree: true });
}"""

# 模拟 iPhone：刘海（顶部）+ Home 指示条（底部整个边距都只是指示条，宿主上报的 BottomIndicator），Tab 栏压住它
SET_SAFE_JS = """([top, bottom]) => {
  const style = document.documentElement.style;
  style.setProperty('--ft-safe-top', top + 'px');
  style.setProperty('--ft-safe-bottom', bottom + 'px');
  style.setProperty('--ft-safe-indicator', bottom + 'px');
}"""

CLEAR_SAFE_JS = """() => {
  const style = document.documentElement.style;
  ['--ft-safe-top', '--ft-safe-bottom', '--ft-safe-indicator'].forEach((name) => style.removeProperty(name));
}"""


# ---------------------------------------------------------------------------------------------- 小工具


def approx(actual, expected, tolerance: float = 1.0) -> bool:
    return actual is not None and abs(actual - expected) <= tolerance


def tid(page, testid: str):
    return page.get_by_test_id(testid)


def path(page) -> str:
    return page.evaluate("() => location.pathname")


def mask(phone: str) -> str:
    return f"{phone[:3]}****{phone[7:]}"


def toast(page, text: str):
    """全局 Toast（MASA 排队消息容器）里包含指定文字的那一条。"""
    return page.locator(".m-enqueued-snackbars").get_by_text(text)


def wait_profile_ready(page) -> None:
    """资料卡出现（摘要已读完）且同步行不再是「正在读取 / 正在同步」。"""
    tid(page, "profile-card").wait_for(state="visible")
    page.wait_for_function(
        """() => {
            const el = document.querySelector('[data-testid="sync-row"]');
            return !!el && el.getAttribute('data-state') !== 'loading' && el.getAttribute('data-state') !== 'syncing';
        }"""
    )
    wait_animations_settled(page)


def go_profile(page) -> None:
    """回到「我的」根页面（已经在就不动），并等资料与摘要读完。"""
    if not (active_tab(page) == "profile" and path(page) == "/profile" and tab_bar(page).is_visible()):
        open_tab(page, "我的")
    wait_profile_ready(page)


def entries(page) -> dict:
    return {e["id"]: e for e in page.evaluate(ENTRIES_JS)}


def entry_value(page, key: str):
    return entries(page)[key]["value"]


def read_sync(page) -> dict:
    return page.evaluate(SYNC_JS)


def wait_sync_pending(page, expected: int) -> None:
    wait_state(
        page,
        "(n) => { const el = document.querySelector('[data-testid=\"sync-row\"]'); return !!el && el.getAttribute('data-state') !== 'loading' && el.getAttribute('data-pending') === String(n); }",
        arg=expected,
    )


def wait_ptr_idle(page) -> None:
    page.wait_for_function(PTR_IDLE_JS)


def wait_entry_value(page, key: str, expected) -> None:
    wait_state(
        page,
        """([key, expected]) => {
            const el = document.querySelector('[data-testid="profile-entry-' + key + '"]');
            if (!el) return false;
            const node = el.querySelector('.ft-cell__value');
            const value = node ? node.textContent.trim() : null;
            return value === expected;
        }""",
        arg=[key, expected],
    )


def sign_in(page, phone: str) -> None:
    """在登录页输入手机号登录（login() 的后半段：退出登录后已经停在登录页，不用再整页导航）。"""
    page.wait_for_selector("[data-testid='login-phone']", timeout=25_000)
    page.get_by_test_id("login-phone").fill(phone)
    page.get_by_test_id("login-submit").click()
    page.wait_for_function("() => !!localStorage.getItem('userId')")
    page.wait_for_selector("[data-testid='login-phone']", state="detached")
    wait_tab_bar(page)


# ---------------------------------------------------------------------------------------------- A. 外壳与资料卡


def check_shell(run: TestRun, page, db: LocalDb, phone: str, user_id: str) -> None:
    db.delete_pending_bills()
    go_profile(page)
    wait_scroll_root_stable(page)
    layout = page.evaluate(LAYOUT_JS)

    run.check(
        "【外壳】地址 /profile、当前 Tab = profile、Tab 栏显示",
        path(page) == "/profile" and active_tab(page) == "profile" and tab_bar(page).is_visible(),
    )
    run.check(
        "【大标题页】根节点是滚动容器（data-scroll-root、纵向可滚动），不再有旧布局的 .container",
        layout["isScrollRoot"] and layout["overflowY"] in ("auto", "scroll") and not layout["legacyContainer"],
        str(layout),
    )
    run.check(
        "【大标题页】大标题「我的」是页面里唯一的一级标题，34px / 800",
        layout["h1Count"] == 1 and layout["title"]["tag"] == "H1" and layout["title"]["text"] == "我的"
        and layout["title"]["size"] == "34px" and layout["title"]["weight"] == "800",
        str(layout["title"]),
    )
    run.check(
        "【大标题页】导航栏吸顶（sticky）、高 44、贴着屏幕顶端；初始未折叠，小标题不可见且只是装饰（aria-hidden）",
        layout["nav"]["position"] == "sticky" and approx(layout["nav"]["height"], 44) and approx(layout["nav"]["top"], 0)
        and layout["collapsed"] != "true" and layout["navTitle"]["opacity"] == 0
        and layout["navTitle"]["ariaHidden"] == "true" and layout["navTitle"]["text"] == "我的",
        f"{layout['nav']} {layout['navTitle']} {layout['collapsed']}",
    )
    run.check(
        "【导航栏】右侧没有任何按钮，也没有按钮形式的刷新入口（刷新只靠下拉）：页面里没有 profile-refresh / 「刷新」按钮 / 刷新图标",
        layout["navButtons"] == 0 and layout["refreshEntries"] == 0,
        f"navButtons={layout['navButtons']} refreshEntries={layout['refreshEntries']}",
    )
    run.check("没有横向溢出", layout["overflowX"] <= 0, str(layout["overflowX"]))

    # ---- 资料卡 ----
    card = page.evaluate(CARD_JS)
    run.check(
        "【资料卡】手机号脱敏（前 3 后 4），与登录用的手机号对应",
        card["phoneText"] == mask(phone) and re.fullmatch(r"1[3-9]\d\*{4}\d{4}", card["phoneText"]) is not None,
        f"{card['phoneText']} vs {mask(phone)}",
    )
    match = re.fullmatch(r"注册于 (\d{4}-\d{2}-\d{2})", card["sinceText"])
    registered = datetime.strptime(match.group(1), "%Y-%m-%d").date() if match else None
    today = datetime.now().date()
    run.check(
        "【资料卡】「注册于 yyyy-MM-dd」，日期是今天（刚注册；容许跨零点差一天）",
        registered is not None and abs((registered - today).days) <= 1,
        card["sinceText"],
    )
    run.check(
        "【资料卡】头像 52×52 圆形图形（aria-hidden）；手机号 20px / 800",
        approx(card["avatar"]["width"], 52) and approx(card["avatar"]["height"], 52) and card["avatarAria"] == "true"
        and card["phoneSize"] == "20px" and card["phoneWeight"] == "800",
        f"{card['avatar']} {card['phoneSize']} {card['phoneWeight']}",
    )
    gap = card["since"]["top"] - card["phone"]["bottom"]
    run.check(
        "【资料卡】手机号与「注册于」之间只隔 2px 行距，没有被 MASA 的 p 外边距（16px）撑开",
        0 <= gap <= 6 and card["phoneMarginBottom"] == 0 and card["sinceMarginBottom"] == 0,
        f"gap={gap} margins={card['phoneMarginBottom']}/{card['sinceMarginBottom']}",
    )
    card_center = (card["card"]["top"] + card["card"]["bottom"]) / 2
    text_center = (card["phone"]["top"] + card["since"]["bottom"]) / 2
    run.check(
        "【资料卡】头像与文字块都在卡片中线上（垂直居中），文字在头像右侧",
        approx((card["avatar"]["top"] + card["avatar"]["bottom"]) / 2, card_center, 1.5)
        and approx(text_center, card_center, 2.5)
        and card["phone"]["left"] >= card["avatar"]["right"] + 10
        and approx(card["phone"]["left"], card["since"]["left"], 1),
        f"card={card_center} text={text_center} avatar={card['avatar']}",
    )
    run.check("【资料卡】卡片高度 ≥ 76（头像 52 + 上下内边距）", card["card"]["height"] >= 76, str(card["card"]["height"]))

    # ---- 分组 ----
    groups = page.evaluate(GROUPS_JS)
    ids = [g["id"] for g in groups]
    run.check("【分组】从上到下：资料卡 / 云端同步 / 记账管理 / 自动记账 / 其他 / 退出登录", ids == GROUP_ORDER, str(ids))
    tops = [g["top"] for g in groups]
    run.check("【分组】纵向依次排列，互不重叠", all(tops[i] < tops[i + 1] and groups[i]["bottom"] <= tops[i + 1] + 0.5 for i in range(len(groups) - 1)), str(groups))
    run.check(
        "【分组】左右各留 16 边距（宽 358）",
        all(approx(g["left"], 16) and approx(g["right"], SCREEN_W - 16) for g in groups),
        str([(g["id"], g["left"], g["right"]) for g in groups]),
    )
    labels = {g["id"]: g["label"] for g in groups}
    run.check(
        "【分组】每组有读屏名称（账号信息 / 云端同步 / 记账管理 / 自动记账 / 其他）",
        labels["profile-card-group"] == "账号信息" and labels["profile-sync-group"] == "云端同步"
        and labels["profile-group-manage"] == "记账管理" and labels["profile-group-auto"] == "自动记账" and labels["profile-group-other"] == "其他",
        str(labels),
    )
    sync_gap = groups[1]["top"] - groups[0]["bottom"]
    run.check("【分组】云端同步贴着资料卡（间距 12），其余分组之间的间距 ≥ 16", approx(sync_gap, 12) and all(groups[i + 1]["top"] - groups[i]["bottom"] >= 16 - 0.5 for i in range(1, len(groups) - 1)), f"{sync_gap} {[round(groups[i + 1]['top'] - groups[i]['bottom'], 1) for i in range(len(groups) - 1)]}")

    # ---- 入口 ----
    rows = page.evaluate(ENTRIES_JS)
    run.check("【入口】恰好 8 个入口，顺序：" + " / ".join(e[1] for e in ENTRIES), [r["id"] for r in rows] == [e[0] for e in ENTRIES], str([r["id"] for r in rows]))
    run.check("【入口】标题与所属分组正确（记账管理 4 / 自动记账 3 / 其他 1）", [(r["title"], r["group"]) for r in rows] == [(e[1], e[2]) for e in ENTRIES], str([(r["title"], r["group"]) for r in rows]))
    run.check("【入口】都是按钮、带右侧箭头、行高 ≥ 56", all(r["tag"] == "BUTTON" and r["chevron"] and r["height"] >= 56 - 0.5 for r in rows), str([(r["id"], r["tag"], r["height"]) for r in rows]))
    text = page.evaluate("() => document.querySelector('[data-testid=\"profile-page\"]').innerText")
    for name in REMOVED_ENTRIES:
        run.check(f"【迁出】「我的」里不再有「{name}」入口", name not in text)

    # ---- 新用户的摘要值 ----
    by_id = {r["id"]: r for r in rows}
    categories = db.visible_category_count(user_id)
    channels = db.visible_channel_count(user_id)
    run.check("【摘要】账本管理「0 个」（新用户没有账本）", by_id["ledgers"]["value"] == "0 个", str(by_id["ledgers"]))
    run.check("【摘要】预算管理「未设置」（本月没有预算）", by_id["budgets"]["value"] == "未设置", str(by_id["budgets"]))
    run.check(f"【摘要】分类管理「{categories} 个」、支付渠道「{channels} 个」（预设，与本地库一致）", by_id["categories"]["value"] == f"{categories} 个" and by_id["payment-channels"]["value"] == f"{channels} 个", f"{by_id['categories']['value']} {by_id['payment-channels']['value']}")
    run.check("【摘要】短信识别「未开启」（Web 没有短信读取权限）", by_id["sms-recognition"]["value"] == "未开启", str(by_id["sms-recognition"]))
    run.check("【摘要】数据导入 / 错误日志没有摘要值", by_id["import"]["value"] is None and by_id["log-viewer"]["value"] is None)
    run.check(
        "【摘要】没有待确认账单：没有红色角标，读屏名称「待确认账单」",
        tid(page, "profile-pending-count").count() == 0 and by_id["pending-bills"]["aria"] == "待确认账单" and by_id["pending-bills"]["value"] is None,
        str(by_id["pending-bills"]),
    )

    # ---- 云端同步行（新用户）----
    sync = read_sync(page)
    run.check(
        "【同步行】标题「云端同步」；没有待同步账单：「已同步」+ 绿色对勾云图标；按钮「立即同步」始终显示",
        sync["title"] == "云端同步" and sync["state"] == "synced" and sync["pending"] == "0" and sync["subtitle"] == "已同步"
        and sync["tone"] == "ft-tile--green" and "mdi-cloud-check" in sync["icon"] and sync["btnText"] == "立即同步" and not sync["btnDisabled"],
        str(sync),
    )

    # ---- 退出登录行 ----
    logout = page.evaluate(LOGOUT_JS)
    run.check(
        "【退出登录】独立一张卡的整行按钮：红色文字（加深红）居中、行高 ≥ 56",
        logout["tag"] == "BUTTON" and logout["text"] == "退出登录" and logout["color"] == RGB_DANGER_TEXT
        and approx(logout["centerX"], logout["rowCenterX"], 2) and logout["height"] >= 56 - 0.5,
        str(logout),
    )

    # ---- 玻璃层 / 底部留白 ----
    layers = page.evaluate(GLASS_LAYERS_JS)
    run.check(f"【玻璃层】每屏玻璃层 ≤ 3（实际 {layers}；页面本身不放玻璃，只有 Tab 栏）", layers <= 3, str(layers))
    scroll_root_to(page, "bottom")
    wait_animations_settled(page)
    clearance = page.evaluate(BOTTOM_CLEARANCE_JS)
    run.check("【底部留白】滚到底，退出登录行完整露在 Tab 栏之上", clearance["lowest"] <= clearance["tabTop"] + 1, str(clearance))
    run.check("【底部留白】留白 ≥ Tab 栏高度 + 距底（62 + 21）", clearance["paddingBottom"] >= 62 + 21, str(clearance))
    scroll_root_to(page, 0)
    wait_animations_settled(page)
    shot(page, "profile-new-user")


# ---------------------------------------------------------------------------------------------- B. 入口


def check_entries(run: TestRun, page) -> None:
    for key, title, _group, pattern in ENTRIES:
        go_profile(page)
        scroll_root_to(page, 0)
        tid(page, f"profile-entry-{key}").click()
        wait_url(page, pattern)
        wait_tab_bar(page, visible=False)
        run.check(
            f"【入口】{title}：进入 {path(page)}，Tab 栏隐藏，页面栈里有二级页面",
            re.match(pattern, path(page)) is not None and not tab_bar(page).is_visible()
            and page.evaluate("() => !!document.querySelector('.m-page-stack-item')"),
        )
        page.go_back()
        wait_url(page, r"^/profile$")
        wait_tab_bar(page)
        wait_secondary_pages_closed(page)
        wait_animations_settled(page)
        run.check(
            f"【入口】{title}：返回后回到 /profile，Tab 栏恢复，当前 Tab 仍是「我的」",
            path(page) == "/profile" and tab_bar(page).is_visible() and active_tab(page) == "profile",
        )
    # 回到「我的」后摘要仍在（没有被二级页面的开合清掉）
    wait_profile_ready(page)
    run.check("【入口】逐个进出之后，各行摘要仍然显示", entry_value(page, "ledgers") == "0 个" and entry_value(page, "sms-recognition") == "未开启")


# ---------------------------------------------------------------------------------------------- C. 摘要值（有数据）


def check_summaries(run: TestRun, page, db: LocalDb, user_id: str) -> "dict":
    db.delete_pending_bills()
    other = db.seed_user()

    ledger_a = db.seed_ledger(user_id, unique_name("摘要账本A"))
    db.seed_ledger(user_id, unique_name("摘要账本B"))
    db.soft_delete_ledger(db.seed_ledger(user_id, unique_name("已删除账本")))
    db.seed_ledger(other, unique_name("别人的账本"))
    joined = db.seed_ledger(other, unique_name("加入的共享账本"))
    db.seed_ledger_member(joined, user_id, role=ROLE_EDITOR, status=MEMBER_ACTIVE)

    categories_before = db.visible_category_count(user_id)
    channels_before = db.visible_channel_count(user_id)
    db.seed_category(user_id, unique_name("自定义分类"))
    db.seed_category(user_id, unique_name("已删分类"), deleted=True)
    db.seed_category(other, unique_name("别人的分类"))
    db.seed_channel(user_id, unique_name("自定义渠道"))
    db.seed_channel(user_id, unique_name("已删渠道"), deleted=True)
    db.seed_channel(other, unique_name("别人的渠道"))

    first_of_month = datetime.now().replace(day=1)
    previous = first_of_month - timedelta(days=1)
    db.seed_budget(user_id, amount=3000)  # 本月 · 全部账本 · 总预算
    db.seed_budget(user_id, amount=500, category_name="餐饮美食")  # 本月 · 全部账本 · 分类预算
    db.seed_budget(user_id, amount=800, ledger_id=ledger_a)  # 本月 · 某个账本的预算：不计入「全部账本」口径
    db.seed_budget(user_id, amount=2000, year=previous.year, month=previous.month)  # 上个月：不计入

    for _ in range(3):
        db.seed_pending_bill()

    open_app(page, "/profile")  # 整页重新进入：走首次加载读摘要的路径
    wait_profile_ready(page)
    rows = entries(page)

    run.check("【摘要】账本管理「2 个」：只数自己的有效账本（不含已删除的、别人的、加入的共享账本）", rows["ledgers"]["value"] == "2 个", str(rows["ledgers"]))
    run.check("【摘要】预算管理「本月 2 项」：只数本月「全部账本」口径的总预算 + 分类预算（不含账本预算与上个月）", rows["budgets"]["value"] == "本月 2 项", str(rows["budgets"]))
    run.check(
        f"【摘要】分类管理「{categories_before + 1} 个」、支付渠道「{channels_before + 1} 个」= 预设 + 自己的自定义（不含已删除的与别人的）",
        rows["categories"]["value"] == f"{categories_before + 1} 个" and rows["payment-channels"]["value"] == f"{channels_before + 1} 个",
        f"{rows['categories']['value']} {rows['payment-channels']['value']}",
    )

    badge = page.evaluate(
        """() => {
            const el = document.querySelector('[data-testid="profile-pending-count"]');
            if (!el) return null;
            const r = el.getBoundingClientRect();
            const cs = getComputedStyle(el);
            return { text: el.textContent.trim(), ariaHidden: el.getAttribute('aria-hidden'), bg: cs.backgroundColor, color: cs.color, height: r.height, width: r.width, radius: parseFloat(cs.borderTopLeftRadius) };
        }"""
    )
    run.check(
        "【角标】待确认账单行右侧红色计数角标「3」：白字 / 加深的红底、22 高的胶囊、只是图形（aria-hidden）",
        badge is not None and badge["text"] == "3" and badge["ariaHidden"] == "true" and badge["bg"] == RGB_BADGE
        and badge["color"] == "rgb(255, 255, 255)" and approx(badge["height"], 22) and badge["width"] >= 22 and badge["radius"] >= 11,
        str(badge),
    )
    run.check("【角标】整行的读屏名称带笔数：「待确认账单，3 笔」", rows["pending-bills"]["aria"] == "待确认账单，3 笔", str(rows["pending-bills"]))

    for _ in range(97):
        db.seed_pending_bill()
    pull_refresh(page, "profile-ptr")
    wait_state(page, "() => { const e = document.querySelector('[data-testid=\"profile-pending-count\"]'); return !!e && e.textContent.trim() === '99+'; }")
    rows = entries(page)
    run.check("【角标】待确认账单超过 99 笔显示「99+」，读屏名称仍是真实笔数「待确认账单，100 笔」", rows["pending-bills"]["aria"] == "待确认账单，100 笔", str(rows["pending-bills"]))
    shot(page, "profile-with-data")

    db.delete_pending_bills()
    pull_refresh(page, "profile-ptr")
    wait_state(page, "() => !document.querySelector('[data-testid=\"profile-pending-count\"]')")
    run.check("【角标】待确认账单清空后角标消失", tid(page, "profile-pending-count").count() == 0 and entries(page)["pending-bills"]["aria"] == "待确认账单")
    return {"ledger": ledger_a}


# ---------------------------------------------------------------------------------------------- D. 同步行


def check_sync_row(run: TestRun, page, db: LocalDb, user_id: str, ledger_id: str) -> None:
    go_profile(page)
    sync = read_sync(page)
    run.check("【同步行】开始时没有账单：已同步", sync["state"] == "synced" and sync["subtitle"] == "已同步", str(sync))

    pending = [db.seed_bill(user_id, amount=10 + i, ledger_id=ledger_id, note=unique_name("待同步")) for i in range(3)]
    failed = db.seed_bill(user_id, amount=99, ledger_id=ledger_id, note=unique_name("上次失败"), sync_status=SYNC_FAILED)
    synced = db.seed_bill(user_id, amount=5, ledger_id=ledger_id, note=unique_name("已同步"), sync_status=SYNC_SYNCED)
    pull_refresh(page, "profile-ptr")
    wait_sync_pending(page, 4)
    sync = read_sync(page)
    run.check(
        "【同步行】3 笔待同步 + 1 笔上次失败（会重试）= 「4 条待同步」；已同步的不算；橙色图标块 + 同步云",
        sync["state"] == "pending" and sync["pending"] == "4" and sync["subtitle"] == "4 条待同步"
        and sync["tone"] == "ft-tile--orange" and "mdi-cloud-sync" in sync["icon"],
        str(sync),
    )
    run.check("【同步行】有待同步时副标题用加深的橙色（对比度）", sync["subtitleColor"] == RGB_WARNING_TEXT, sync["subtitleColor"])
    run.check(
        "【同步行】「立即同步」按钮：命中区域 ≥ 44×44，胶囊视觉高 34，未禁用、没有忙碌标记",
        sync["btnW"] >= 44 and sync["btnH"] >= 44 and approx(sync["pillH"], 34) and not sync["btnDisabled"] and sync["busy"] is None,
        str(sync),
    )
    shot(page, "profile-sync-pending")

    for bill_id in pending + [failed]:
        db.set_bill_sync_status(bill_id, SYNC_SYNCED)
    pull_refresh(page, "profile-ptr")
    wait_sync_pending(page, 0)
    sync = read_sync(page)
    run.check(
        "【同步行】全部同步后回到「已同步」：绿色图标块 + 对勾云，副标题不再是橙色",
        sync["state"] == "synced" and sync["subtitle"] == "已同步" and sync["tone"] == "ft-tile--green" and "mdi-cloud-check" in sync["icon"]
        and sync["subtitleColor"] != RGB_WARNING_TEXT,
        str(sync),
    )

    # ---- 云端不可用：立即同步给出既有提示，不报错、不登出 ----
    db.set_bill_sync_status(synced, SYNC_PENDING)
    pull_refresh(page, "profile-ptr")
    wait_sync_pending(page, 1)
    user_before = page.evaluate("() => localStorage.getItem('userId')")
    tid(page, "sync-now").click()
    toast(page, "同步失败").first.wait_for(state="visible")
    text = toast(page, "同步失败").first.inner_text()
    page.wait_for_function("() => !document.querySelector('[data-testid=\"sync-now\"]').disabled")
    wait_animations_settled(page)
    sync = read_sync(page)
    run.check("【云端不可用】点「立即同步」：Toast 以「同步失败:」开头并带原因", text.strip().startswith("同步失败:") and len(text.strip()) > len("同步失败:") + 2, text)
    run.check(
        "【云端不可用】按钮恢复可用，待同步数量不变（仍是 1 条），没有离开页面，也没有被登出",
        not sync["btnDisabled"] and sync["busy"] is None and sync["state"] == "pending" and sync["pending"] == "1"
        and path(page) == "/profile" and active_tab(page) == "profile" and page.evaluate("() => localStorage.getItem('userId')") == user_before
        and tid(page, "profile-card").is_visible(),
        str(sync),
    )
    db.set_bill_sync_status(synced, SYNC_SYNCED)


# ---------------------------------------------------------------------------------------------- E. 刷新


def check_refresh(run: TestRun, page, db: LocalDb, user_id: str) -> None:
    go_profile(page)
    scroll_root_to(page, 0)
    wait_ptr_idle(page)
    count = int(entry_value(page, "ledgers").split()[0])

    cloud = FakeCloud()
    try:
        cloud.start()
    except CloudPortBusy as exc:
        run.check("假云端可以启动（验证「刷新不触发同步」依赖它）", False, str(exc))
        cloud = None

    try:
        baseline = len(cloud.requests) if cloud else 0

        # ---- 拉得不够：不刷新 ----
        db.seed_ledger(user_id, unique_name("刷新账本"))
        pull_down(page, distance=40, from_y=300)
        wait_ptr_idle(page)
        run.check("【下拉刷新】距离不够（没到触发阈值）：松手回弹，不刷新", entry_value(page, "ledgers") == f"{count} 个", entry_value(page, "ledgers"))

        # ---- 页面不在顶部：不触发（滚下去 100，只拖 80：整个手势里页面都没回到顶部）----
        state = scroll_root_state(page)
        if state and state["max"] >= 100:
            scroll_root_to(page, 100)
            page.wait_for_function("() => document.querySelector('[data-testid=\"profile-page\"]').scrollTop >= 99")
            pull_down(page, distance=80, from_y=300)
            wait_ptr_idle(page)
            run.check("【下拉刷新】页面不在顶部时向下拖只是滚动，不触发刷新", entry_value(page, "ledgers") == f"{count} 个", entry_value(page, "ledgers"))
            scroll_root_to(page, 0)
            page.wait_for_function("() => document.querySelector('[data-testid=\"profile-page\"]').scrollTop <= 1")
            wait_animations_settled(page)
        else:
            run.check("「我的」页内容够长，能验证「不在顶部不触发刷新」", False, str(state))

        # ---- 完整下拉刷新（真实触摸）----
        count += 1
        pull_down(page, distance=240, from_y=300)
        wait_entry_value(page, "ledgers", f"{count} 个")
        wait_ptr_idle(page)
        run.check("【下拉刷新】真实触摸下拉到位：摘要更新为最新数据（造的那个账本读到了）", entry_value(page, "ledgers") == f"{count} 个", entry_value(page, "ledgers"))
        run.check("【下拉刷新】刷新结束后页面不再标记忙碌（aria-busy 已移除）", tid(page, "profile-page").get_attribute("aria-busy") is None)
        run.check("【下拉刷新】刷新结束后下拉区收起，没有残留的位移变量；读屏状态播报区是 aria-live=polite", page.evaluate(PTR_IDLE_JS) and tid(page, "profile-ptr").locator("[data-ptr-status]").get_attribute("aria-live") == "polite")

        if cloud is not None:
            # 「我的」不读共享账本的云端成员缓存，所以除登录换令牌 / 成员拉取外不应有任何请求；任何推送 / 拉取账单、分类、账本才是「触发了同步」
            allowed = {"/api/auth/login", "/api/sync/ledgermembers/pull"}
            requests = [r for r in cloud.requests[baseline:] if r.path not in allowed]
            run.check(
                "【只读本页】下拉刷新不触发同步（没有向云端发任何同步请求）",
                len(requests) == 0,
                "；".join(f"{r.method} {r.path}" for r in requests),
            )
    finally:
        if cloud is not None:
            cloud.stop()

    # ---- 二级页面关闭后自动重读摘要 ----
    go_profile(page)
    tid(page, "profile-entry-ledgers").click()
    wait_url(page, r"^/ledgers$")
    wait_tab_bar(page, visible=False)
    count += 1
    db.seed_ledger(user_id, unique_name("二级页期间新增"))
    page.go_back()
    wait_url(page, r"^/profile$")
    wait_tab_bar(page)
    wait_secondary_pages_closed(page)
    wait_entry_value(page, "ledgers", f"{count} 个")
    run.check("【页面关闭后重读】在账本管理页期间新增的账本，回到「我的」后摘要自动更新", entry_value(page, "ledgers") == f"{count} 个", entry_value(page, "ledgers"))

    for _ in range(2):
        db.seed_pending_bill()
    pull_refresh(page, "profile-ptr")
    wait_state(page, "() => { const e = document.querySelector('[data-testid=\"profile-pending-count\"]'); return !!e && e.textContent.trim() === '2'; }")
    tid(page, "profile-entry-pending-bills").click()
    wait_url(page, r"^/pending-bills$")
    wait_tab_bar(page, visible=False)
    db.delete_pending_bills()
    page.go_back()
    wait_url(page, r"^/profile$")
    wait_tab_bar(page)
    wait_secondary_pages_closed(page)
    wait_state(page, "() => !document.querySelector('[data-testid=\"profile-pending-count\"]')")
    run.check("【页面关闭后重读】在待确认账单页期间清空了待确认账单，回到「我的」后角标自动消失", tid(page, "profile-pending-count").count() == 0)


# ---------------------------------------------------------------------------------------------- F. 大标题折叠


def check_collapse(run: TestRun, page) -> None:
    go_profile(page)
    wait_scroll_root_stable(page)
    state = scroll_root_state(page)
    run.check("「我的」页内容超出一屏，能滚动（足以折叠大标题）", state is not None and state["max"] > 60, str(state))
    scroll_root_to(page, "bottom")
    page.wait_for_function("() => document.querySelector('[data-testid=\"profile-page\"]').getAttribute('data-title-collapsed') === 'true'")
    wait_animations_settled(page)
    far = page.evaluate(LAYOUT_JS)
    run.check(
        "【折叠】大标题滚到导航栏之下后折叠，居中浮现小标题「我的」",
        far["collapsed"] == "true" and far["title"]["bottom"] <= far["nav"]["bottom"] + 2
        and far["navTitle"]["opacity"] == 1 and approx(far["navTitle"]["centerX"], SCREEN_W / 2, 2)
        and far["navTitle"]["right"] <= far["actionsLeft"] + 0.5,
        f"{far['collapsed']} {far['title']} {far['nav']} {far['navTitle']}",
    )
    shot(page, "profile-collapsed")
    scroll_root_to(page, 0)
    page.wait_for_function("() => document.querySelector('[data-testid=\"profile-page\"]').getAttribute('data-title-collapsed') === 'false'")
    wait_animations_settled(page)
    top = page.evaluate(LAYOUT_JS)
    run.check("【折叠】回到顶部又展开：小标题消失", top["collapsed"] == "false" and top["navTitle"]["opacity"] == 0, f"{top['collapsed']} {top['navTitle']}")


# ---------------------------------------------------------------------------------------------- G. 沉浸式


def check_immersive(run: TestRun, page) -> None:
    go_profile(page)
    scroll_root_to(page, 0)
    page.evaluate(SET_SAFE_JS, [TOP, BOTTOM])
    wait_animations_settled(page)
    layout = page.evaluate(LAYOUT_JS)
    run.check(
        "【沉浸式】导航栏背景延伸到屏幕顶部，高 = 44 + 状态栏 47；大标题在导航栏之下",
        approx(layout["nav"]["top"], 0) and approx(layout["nav"]["height"], 44 + TOP) and layout["title"]["top"] >= layout["nav"]["bottom"] - 1,
        f"{layout['nav']} {layout['title']}",
    )
    wait_attr(page, "html", "data-statusbar-icons", "dark")
    run.check(
        "【沉浸式】浅色导航栏 → 状态栏用深色图标",
        page.evaluate("() => document.documentElement.getAttribute('data-statusbar-icons')") == "dark",
    )

    scroll_root_to(page, "bottom")
    page.wait_for_function("() => document.querySelector('[data-testid=\"profile-page\"]').getAttribute('data-title-collapsed') === 'true'")
    wait_animations_settled(page)
    clearance = page.evaluate(BOTTOM_CLEARANCE_JS)
    run.check(
        "【沉浸式】有 Home 指示条时，滚到底最后一行仍完整露在 Tab 栏之上",
        clearance["lowest"] <= clearance["tabTop"] + 1,
        str(clearance),
    )
    run.check(
        "【沉浸式】Tab 栏下沿离屏幕底 21（压住指示条所在的安全区，设计稿 01），且没有盖住指示条本体（顶边离屏幕底约 13）",
        approx(clearance["tabBottom"], SCREEN_H - 21) and clearance["tabBottom"] <= SCREEN_H - HOME_INDICATOR_TOP,
        str(clearance),
    )
    collapsed = page.evaluate(LAYOUT_JS)
    run.check(
        "【沉浸式】折叠后导航栏（含状态栏高度）是不透明的页面背景色，内容不会从状态栏图标后面透出来",
        collapsed["collapsed"] == "true" and collapsed["nav"]["bg"] == "rgb(242, 242, 247)" and approx(collapsed["nav"]["height"], 44 + TOP),
        str(collapsed["nav"]),
    )
    shot(page, "profile-immersive-notch")
    page.evaluate(CLEAR_SAFE_JS)
    scroll_root_to(page, 0)
    wait_animations_settled(page)
    restored = page.evaluate(LAYOUT_JS)
    run.check("【沉浸式】安全区还原为 0 后，导航栏回到 44 高", approx(restored["nav"]["height"], 44), str(restored["nav"]))


# ---------------------------------------------------------------------------------------------- H. 未登录


def check_signed_out(run: TestRun) -> None:
    with session() as s:
        page = s.page
        open_app(page, "/profile")
        tid(page, "profile-login-empty").wait_for(state="visible")
        text = tid(page, "profile-login-empty").inner_text()
        run.check(
            "【未登录】显示「请先登录」引导（不显示资料卡 / 同步行 / 入口 / 退出登录），有「去登录」按钮",
            "请先登录" in text and tid(page, "profile-login").is_visible() and tid(page, "profile-card").count() == 0
            and tid(page, "sync-row").count() == 0 and tid(page, "profile-logout").count() == 0
            and page.locator("[data-testid^=\"profile-entry-\"]").count() == 0,
            text,
        )
        tid(page, "profile-login").click()
        wait_url(page, r"^/login")
        page.wait_for_selector("input[type='tel']", timeout=25_000)
        wait_tab_bar(page, visible=False)
        run.check("【未登录】点「去登录」进入登录页：有手机号输入框，没有 Tab 栏", path(page).startswith("/login") and not tab_bar(page).is_visible())
        run.check_clean(s.errors, "H 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- I. 退出登录


def check_logout(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        db.seed_ledger(user_id, unique_name("退出前账本"))
        db.seed_bill(user_id, amount=12, ledger_id=db.seed_ledger(user_id, unique_name("退出前账本2")))
        open_tab(page, "我的")
        wait_profile_ready(page)
        pull_refresh(page, "profile-ptr")
        wait_entry_value(page, "ledgers", "2 个")
        run.check("（前置）退出前：账本管理「2 个」、同步行「1 条待同步」", entry_value(page, "ledgers") == "2 个" and read_sync(page)["pending"] == "1")

        # ---- 取消 ----
        tid(page, "profile-logout").click()
        alert = tid(page, "profile-logout-alert")
        alert.wait_for(state="visible")
        title = alert.locator("[data-testid=\"alert-title\"]").inner_text()
        message = alert.locator("[data-testid=\"alert-message\"]").inner_text()
        buttons = [b.inner_text().strip() for b in alert.locator("button").all()]
        confirm_class = alert.locator("[data-testid=\"alert-confirm\"]").get_attribute("class") or ""
        run.check(
            "【退出登录】点按弹出 Alert：标题「退出登录？」、说明「退出后需要重新输入手机号登录，本机已有的账单数据会保留。」、按钮「取消」「退出登录」（确认是红色破坏性按钮）",
            title == "退出登录？" and message == "退出后需要重新输入手机号登录，本机已有的账单数据会保留。"
            and buttons == ["取消", "退出登录"] and "ft-alert__btn--danger" in confirm_class,
            f"{title} | {message} | {buttons} | {confirm_class}",
        )
        run.check("【退出登录】Alert 弹出时还没有登出（登录状态仍在）", page.evaluate("() => !!localStorage.getItem('userId')"))
        shot(page, "profile-logout-alert")

        alert.locator("[data-testid=\"alert-cancel\"]").click()
        tid(page, "alert-host").wait_for(state="detached")
        run.check(
            "【退出登录·取消】Alert 关闭，仍在「我的」、仍是登录状态、Tab 栏仍在，没有「已退出登录」提示",
            path(page) == "/profile" and page.evaluate("() => !!localStorage.getItem('userId')") and tab_bar(page).is_visible()
            and toast(page, "已退出登录").count() == 0 and tid(page, "profile-card").is_visible(),
        )

        # ---- Esc 等同取消 ----
        tid(page, "profile-logout").click()
        alert.wait_for(state="visible")
        page.keyboard.press("Escape")
        tid(page, "alert-host").wait_for(state="detached")
        run.check(
            "【退出登录·Esc】Esc 等同「取消」：Alert 关闭，仍是登录状态",
            path(page) == "/profile" and page.evaluate("() => !!localStorage.getItem('userId')"),
        )

        # ---- 确认 ----
        # 「已退出登录」Toast 发出后马上随布局切换（主布局 → 登录页布局）被丢弃，事后看不到：在确认前布防，记录它是否被加入过页面
        page.evaluate(WATCH_LOGOUT_TOAST_JS)
        tid(page, "profile-logout").click()
        alert.wait_for(state="visible")
        alert.locator("[data-testid=\"alert-confirm\"]").click()
        wait_url(page, r"^/login")
        page.wait_for_selector("input[type='tel']", timeout=25_000)
        wait_tab_bar(page, visible=False)
        run.check(
            "【退出登录·确认】回到登录页：有手机号输入框、没有 Tab 栏、登录状态已清除",
            path(page).startswith("/login") and not tab_bar(page).is_visible() and page.evaluate("() => !localStorage.getItem('userId')"),
        )
        run.check("【退出登录·确认】发出了「已退出登录」提示（随后因切到登录页布局而消失，见 ADR 0007 已知限制）", page.evaluate("() => window.__ftLogoutToast > 0"))
        shot(page, "profile-after-logout")

        # ---- 换号登录：不残留上一个用户的资料与摘要 ----
        phone2 = unique_phone()
        sign_in(page, phone2)
        open_tab(page, "我的")
        wait_profile_ready(page)
        card = page.evaluate(CARD_JS)
        rows = entries(page)
        sync = read_sync(page)
        run.check(
            "【换号登录】「我的」显示新手机号（脱敏），不残留上一个用户的手机号",
            card["phoneText"] == mask(phone2) and mask(info.phone) not in page.evaluate("() => document.querySelector('[data-testid=\"profile-page\"]').innerText"),
            f"{card['phoneText']} vs {mask(phone2)}",
        )
        run.check(
            "【换号登录】摘要与同步行都是新用户的（账本 0 个、没有待同步），不残留上一个用户的数据",
            rows["ledgers"]["value"] == "0 个" and sync["state"] == "synced" and sync["pending"] == "0",
            f"{rows['ledgers']['value']} {sync}",
        )
        run.check_clean(s.errors, "I 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- main


def main() -> int:
    require_server()
    run = TestRun("profile-page")
    db = LocalDb()
    db.verify_schema()
    db.delete_pending_bills()
    # 「短信识别」行的摘要取决于「自动识别」假实现里的权限状态（整个进程共享）：先复位成默认的「没开启」，不受前一个脚本影响
    set_capture(reset=True)

    try:
        with session() as s:
            page = s.page
            info = login(page)
            user_id = db.user_id(info.phone)

            check_shell(run, page, db, info.phone, user_id)
            check_entries(run, page)
            seeded = check_summaries(run, page, db, user_id)
            check_sync_row(run, page, db, user_id, seeded["ledger"])
            check_refresh(run, page, db, user_id)
            check_collapse(run, page)
            check_immersive(run, page)

            run.check_clean(s.errors, "A–G 节会话无页面异常 / 控制台错误 / 失败请求")

        check_signed_out(run)
        check_logout(run, db)
    finally:
        db.delete_pending_bills()
        set_capture(reset=True)
    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
