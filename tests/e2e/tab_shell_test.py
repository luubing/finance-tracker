# -*- coding: utf-8 -*-
"""
E2E 测试：设计令牌 + 悬浮玻璃 Tab 栏 + 四 Tab 壳（iOS UI 重构 · 03 号 ticket）。

只覆盖网页层——Android / iOS 上玻璃模糊真实渲染、手势条不遮挡 Tab 栏等原生表现，见 ticket 03 的人工验收清单。

  A. 设计令牌：两个宿主共用的 tokens.css 已加载；MASA 主题 primary 与令牌品牌色一致
  B. Tab 栏外观与位置：四个 Tab + 独立「记一笔」圆钮；高 62、左右边距 16、距底 21（在安全区之上）；
     选中项图标换实心并染品牌色，底部有静态高光；每屏玻璃层 ≤ 3
  C. 切换 Tab：文档化的「当前激活 Tab」属性（data-active-tab）随之变化，并显示对应页面；
     Home 里指向账单列表的入口（「最近账单」的「全部」）切到「账单」Tab，而不是把它压栈（「我的」里的账单列表入口已由 13 号迁出）
  D. 切走再切回：滚动位置与筛选状态保持
  E. 二级页面：Tab 栏隐藏、返回后恢复；再次点按当前 Tab 回顶；停在二级页面时再点当前 Tab 先回到根页面；
     一级页面底部留白，最后一项内容能完整滚出 Tab 栏之上
  F. 「记一笔」圆钮进入现有记账页
  G. 未登录（登录页）没有 Tab 栏
  H. 宽屏（1280×800）：应用壳与 Tab 栏限制最大宽度并居中，所有固定定位元素都收在壳内

可观察属性（已登记到 README）：[data-testid="tab-bar"]、data-active-tab、--ft-* 令牌。
"""
import contextlib
import sys

from e2e_base import (
    GLASS_LAYERS_JS,
    PHONE_VIEWPORT,
    TAB_BAR_SELECTOR,
    TestRun,
    active_root,
    active_tab,
    LocalDb,
    expand_tab_bar,
    give_home_a_ledger,
    login,
    open_app,
    open_tab,
    require_server,
    scroll_root_state,
    scroll_root_to,
    session,
    shot,
    tab_bar,
    tab_button,
    wait_animations_settled,
    wait_attr,
    wait_scroll_root_stable,
    wait_scroll_root_top,
    wait_state,
    wait_tab_bar,
    wait_url,
)

SCREEN_W = PHONE_VIEWPORT["width"]
SCREEN_H = PHONE_VIEWPORT["height"]
BOTTOM = 34  # iPhone 手势条高度
WIDE_VIEWPORT = {"width": 1280, "height": 800}
TAB_TITLES = ["首页", "账单", "统计", "我的"]
TAB_PATHS = {"首页": r"^/$", "账单": r"^/bills$", "统计": r"^/statistics$", "我的": r"^/profile$"}


SHORT_VIEWPORT = {"width": SCREEN_W, "height": 560}


def approx(actual, expected, tolerance: float = 1.0) -> bool:
    return actual is not None and abs(actual - expected) <= tolerance


@contextlib.contextmanager
def short_viewport(page):
    """临时把视口压矮。滚动保活 / 底部留白 / 回顶这类检查与页面内容无关，只需要一个能滚动的页面；
    13 号重做后「我的」只比一屏多一点，压矮视口才能保证有足够的滚动距离，不必依赖它的内容高度。"""
    page.set_viewport_size(SHORT_VIEWPORT)
    try:
        yield
    finally:
        page.set_viewport_size(PHONE_VIEWPORT)


READ_TOKENS_JS = """() => {
  const cs = getComputedStyle(document.documentElement);
  const get = (name) => cs.getPropertyValue(name).trim();
  const primary = get('--m-theme-primary').split(',').map((n) => parseInt(n, 10));
  const masaHex = '#' + primary.map((n) => n.toString(16).padStart(2, '0')).join('').toUpperCase();
  const app = getComputedStyle(document.querySelector('.m-application'));
  return {
    brand: get('--ft-brand').toUpperCase(),
    masaPrimaryHex: masaHex,
    glassBg: get('--ft-glass-bg'),
    tabbarHeight: get('--ft-tabbar-height'),
    appFont: app.fontFamily,
    appBackground: app.backgroundColor,
  };
}"""

TAB_LAYOUT_JS = """() => {
  const rect = (el) => { const r = el.getBoundingClientRect(); return { left: r.left, right: r.right, top: r.top, bottom: r.bottom, width: r.width, height: r.height }; };
  const wrap = document.querySelector('[data-testid="tab-bar"]');
  const nav = wrap.querySelector('nav');
  const fab = wrap.querySelector('[data-testid="tab-bar-add"]');
  const thumb = wrap.querySelector('.ft-tabbar__thumb');
  const selected = wrap.querySelector('[role="tab"][aria-selected="true"]');
  const glyph = (tab, cls) => parseFloat(getComputedStyle(tab.querySelector(cls)).opacity);
  return {
    wrap: rect(wrap),
    nav: rect(nav),
    fab: rect(fab),
    thumb: thumb && rect(thumb),
    selected: selected && rect(selected),
    selectedColor: selected && getComputedStyle(selected).color,
    selectedOutline: selected && glyph(selected, '.ft-tab__o'),
    selectedFilled: selected && glyph(selected, '.ft-tab__f'),
    navBackdrop: getComputedStyle(nav).backdropFilter || getComputedStyle(nav).webkitBackdropFilter,
    fabRadius: getComputedStyle(fab).borderRadius,
    selectedCount: wrap.querySelectorAll('[role="tab"][aria-selected="true"]').length,
  };
}"""

# 末尾内容与 Tab 栏的相对位置：滚到底后，滚动容器里最靠下的子元素底边必须在 Tab 栏顶边之上
BOTTOM_CLEARANCE_JS = """() => {
  const root = document.querySelector('.p-page-container__item:not(.d-none)');
  const scroller = root.querySelector('[data-scroll-root]') || root;
  const tabTop = document.querySelector('[data-testid="tab-bar"] nav').getBoundingClientRect().top;
  const bottoms = [...scroller.children].map((c) => c.getBoundingClientRect()).filter((r) => r.height > 0).map((r) => r.bottom);
  return { tabTop, lowest: Math.max(...bottoms), paddingBottom: parseFloat(getComputedStyle(scroller).paddingBottom) };
}"""

# 宽屏：收集所有可见的固定定位元素与应用壳的水平范围
FIXED_ELEMENTS_JS = """() => {
  const shell = document.querySelector('.m-application').getBoundingClientRect();
  const fixed = [...document.querySelectorAll('*')]
    .filter((el) => getComputedStyle(el).position === 'fixed' && getComputedStyle(el).visibility !== 'hidden')
    .map((el) => ({ el, r: el.getBoundingClientRect() }))
    .filter(({ r }) => r.width > 0 && r.height > 0)
    .map(({ el, r }) => ({ name: el.className.toString().slice(0, 40) || el.tagName, left: r.left, right: r.right }));
  return { shell: { left: shell.left, right: shell.right, width: shell.width }, fixed };
}"""


# 宽屏：壳左右两侧各留 12px 的点位上命中的元素是否属于应用（true = 内容从壳里漏了出来）
OUTSIDE_SHELL_PROBE_JS = """() => {
  const shell = document.querySelector('.m-application').getBoundingClientRect();
  const leaks = (x) => { const el = document.elementFromPoint(x, 300); return !!el && !!el.closest('.m-application'); };
  return { left: leaks(shell.left - 12), right: leaks(shell.right + 12) };
}"""

VISIBLE_ROOT_TEXT_JS = "() => (document.querySelector('.p-page-container__item:not(.d-none)') || document.body).innerText"


def visible_root_text(page) -> str:
    return page.evaluate(VISIBLE_ROOT_TEXT_JS)


def wait_root_changed(page, previous_text: str) -> str:
    """点按 Tab 后，地址 / data-active-tab 先变，页面栈随后才换上新的根页面——等到可见内容与之前不同再读取。"""
    wait_state(
        page,
        "(prev) => { const el = document.querySelector('.p-page-container__item:not(.d-none)'); return !!el && el.innerText !== prev; }",
        arg=previous_text,
    )
    return visible_root_text(page)


def path(page) -> str:
    return page.evaluate("() => location.pathname")


def check_tokens(run: TestRun, page) -> None:
    """A. 设计令牌"""
    tokens = page.evaluate(READ_TOKENS_JS)
    run.check("两个宿主共用的 tokens.css 已加载（--ft-brand = #4318FF）", tokens["brand"] == "#4318FF", str(tokens))
    run.check(
        "MASA 主题 primary 与令牌品牌色一致",
        tokens["masaPrimaryHex"] == tokens["brand"],
        f"MASA={tokens['masaPrimaryHex']} token={tokens['brand']}",
    )
    run.check("玻璃材质令牌可用（--ft-glass-bg）", "255" in tokens["glassBg"] and ".58" in tokens["glassBg"], tokens["glassBg"])
    run.check("尺寸令牌可用（--ft-tabbar-height = 62px）", tokens["tabbarHeight"] == "62px", tokens["tabbarHeight"])
    run.check("应用字体取自令牌（PingFang SC 在字体栈里）", "PingFang SC" in tokens["appFont"], tokens["appFont"])
    run.check("应用背景 = 页面底色 #F2F2F7", tokens["appBackground"] == "rgb(242, 242, 247)", tokens["appBackground"])


def check_tab_bar_look(run: TestRun, page) -> None:
    """B. Tab 栏外观与位置"""
    wait_tab_bar(page)
    wait_animations_settled(page)

    names = [tab_bar(page).get_by_role("tab").nth(i).inner_text().strip() for i in range(tab_bar(page).get_by_role("tab").count())]
    run.check("Tab 栏有四个 Tab：首页 / 账单 / 统计 / 我的（顺序固定）", names == TAB_TITLES, str(names))
    run.check("独立的「记一笔」圆钮", tab_bar(page).get_by_role("button", name="记一笔").count() == 1)
    run.check("默认激活首页（data-active-tab = home）", active_tab(page) == "home", str(active_tab(page)))

    layout = page.evaluate(TAB_LAYOUT_JS)
    wrap = layout["wrap"]
    run.check("Tab 栏高 62", approx(wrap["height"], 62), str(wrap))
    run.check("Tab 栏左边距 16", approx(wrap["left"], 16), str(wrap))
    run.check("Tab 栏右边距 16", approx(wrap["right"], SCREEN_W - 16), str(wrap))
    run.check("Tab 栏距底 21（安全区为 0 时）", approx(wrap["bottom"], SCREEN_H - 21), str(wrap))
    run.check(
        "「记一笔」圆钮在右侧：62×62 的圆",
        approx(layout["fab"]["width"], 62) and approx(layout["fab"]["height"], 62) and approx(layout["fab"]["right"], SCREEN_W - 16) and layout["fabRadius"].startswith("50%"),
        f"{layout['fab']} radius={layout['fabRadius']}",
    )
    run.check("Tab 栏与圆钮之间有间距，互不重叠", layout["fab"]["left"] - layout["nav"]["right"] >= 8, f"{layout['nav']} {layout['fab']}")
    run.check("Tab 栏是玻璃材质（背景模糊 22px）", "blur(22px)" in (layout["navBackdrop"] or ""), str(layout["navBackdrop"]))

    run.check("只有一个选中项", layout["selectedCount"] == 1, str(layout["selectedCount"]))
    run.check("选中项（首页）染品牌色 #4318FF", layout["selectedColor"] == "rgb(67, 24, 255)", str(layout["selectedColor"]))
    run.check(
        "选中项图标由线框换成实心（线框透明、实心不透明）",
        approx(layout["selectedOutline"], 0, 0.05) and approx(layout["selectedFilled"], 1, 0.05),
        f"outline={layout['selectedOutline']} filled={layout['selectedFilled']}",
    )
    thumb, selected = layout["thumb"], layout["selected"]
    run.check(
        "选中项下方有静态高光（与选中项水平对齐）",
        thumb and selected and approx((thumb["left"] + thumb["right"]) / 2, (selected["left"] + selected["right"]) / 2, 1.5),
        f"thumb={thumb} selected={selected}",
    )

    glass = page.evaluate(GLASS_LAYERS_JS)
    run.check("每屏同时叠加的玻璃层不超过 3 层", glass <= 3, f"{glass} 层")

    # 位于底部安全区之上：模拟手势条后，Tab 栏整体上移 34
    page.evaluate("() => document.documentElement.style.setProperty('--ft-safe-bottom', '34px')")
    wait_animations_settled(page)
    lifted = page.evaluate(TAB_LAYOUT_JS)["wrap"]
    run.check(
        "手势条 34px 时 Tab 栏距底 = 21 + 34，整体停在安全区之上",
        approx(lifted["bottom"], SCREEN_H - 21 - BOTTOM) and approx(lifted["height"], 62),
        str(lifted),
    )
    page.evaluate("() => document.documentElement.style.removeProperty('--ft-safe-bottom')")
    wait_animations_settled(page)
    shot(page, "tab-shell-home")


def check_switch_tabs(run: TestRun, page) -> None:
    """C. 切换 Tab"""
    texts = {}
    previous = visible_root_text(page)
    for title in ["账单", "统计", "我的", "首页"]:
        open_tab(page, title)
        wait_url(page, TAB_PATHS[title])
        texts[title] = previous = wait_root_changed(page, previous)
        run.check(
            f"点按「{title}」：data-active-tab = {active_tab(page)}，地址 {path(page)}，该 Tab 高亮",
            tab_button(page, title).get_attribute("aria-selected") == "true"
            and sum(tab_button(page, t).get_attribute("aria-selected") == "true" for t in TAB_TITLES) == 1,
        )
        wait_animations_settled(page)
        shot(page, f"tab-shell-{active_tab(page)}")

    run.check("四个 Tab 显示的是各自不同的页面", len(set(texts.values())) == len(texts), str({k: v[:20] for k, v in texts.items()}))
    run.check("切换后 Tab 栏仍然显示", tab_bar(page).is_visible())

    # 入口：首页「最近账单」的「全部」→ 切换到「账单」Tab，而不是压栈
    # （Tab 根页面常驻 DOM，只在 active_root 范围里找，免得点到别的 Tab 里被隐藏的同名元素；
    # 新用户的首页只有「新建账本」引导，先给用户造一个账本，首页才有「最近账单」区域）
    open_tab(page, "首页")
    give_home_a_ledger(page)
    active_root(page).get_by_test_id("home-recent-all").click()
    wait_attr(page, TAB_BAR_SELECTOR, "data-active-tab", "bills")
    wait_url(page, TAB_PATHS["账单"])
    run.check("首页「最近账单」的「全部」入口切换到「账单」Tab（Tab 栏仍显示，没有压栈成二级页面）", tab_bar(page).is_visible() and active_tab(page) == "bills")
    # 返回键：不应该有一份被压栈的账单页可返回（回到入口所在的首页）
    # （「我的」里原来的「账单列表」入口已由 13 号迁出——账单列表就是「账单」Tab）
    page.go_back()
    wait_url(page, TAB_PATHS["首页"])
    run.check("返回回到「首页」，没有多出一层账单页", active_tab(page) == "home" and tab_bar(page).is_visible())


def check_state_preserved(run: TestRun, page) -> None:
    """D. 切走再切回：滚动位置与筛选状态保持"""
    open_tab(page, "我的")
    wait_scroll_root_stable(page)  # 首次进入时数据还在加载，内容高度稳定后再设置滚动位置
    with short_viewport(page):
        scroll_root_to(page, 300)
        before = scroll_root_state(page)
        run.check("「我的」页内容超出一屏，能滚动", before and before["max"] > 200 and before["top"] > 200, str(before))

        open_tab(page, "首页")
        open_tab(page, "我的")
        wait_scroll_root_stable(page)
        after = scroll_root_state(page)
        run.check("切走再切回「我的」：滚动位置保持", after and approx(after["top"], before["top"], 2), f"{before} → {after}")

    # 账单页的筛选条件（09 号起：导航栏筛选按钮 → 筛选 Sheet → 选「本年」→「查看 N 笔账单」，生效后以条件条上的标签展示）：
    # 切走再切回仍保留。账单 Tab 默认看「本月」且不算条件（第 2 轮评审），所以这里选一个偏离默认的预设「本年」；先给这个用户造一笔账单
    db = LocalDb()
    user_id = page.evaluate("() => localStorage.getItem('userId')")
    db.seed_bill(user_id, amount=10, ledger_id=db.seed_ledger(user_id), note="Tab 壳筛选")
    open_tab(page, "账单")
    active_root(page).get_by_test_id("bills-filter-open").click()
    page.get_by_test_id("filter-sheet").wait_for(state="visible")
    page.get_by_test_id("filter-time-year").click()
    wait_attr(page, "[data-testid=\"filter-time-year\"]", "data-selected", "true")
    wait_state(page, "() => document.querySelector('[data-testid=\"filter-apply\"]').dataset.count === '1'")
    page.get_by_test_id("filter-apply").click()
    page.get_by_test_id("filter-sheet").wait_for(state="detached")
    chip = active_root(page).get_by_test_id("bills-chip-time")
    wait_attr(page, ".p-page-container__item:not(.d-none) [data-testid=\"bills-chip-time\"]", "data-active", "true")
    open_tab(page, "统计")
    open_tab(page, "账单")
    kept = active_root(page).get_by_test_id("bills-chip-time")
    run.check(
        "切走再切回「账单」：筛选状态保持（时间条件仍是「本年」，条件条上的标签仍是已设置状态）",
        kept.get_attribute("data-active") == "true" and "本年" in kept.inner_text() and chip.count() == 1,
        kept.inner_text(),
    )


def check_secondary_pages(run: TestRun, page) -> None:
    """E. 二级页面、回到顶部、底部留白"""
    open_tab(page, "我的")
    wait_scroll_root_stable(page)

    # 底部留白 / 回顶只需要一个能滚动的页面：压矮视口，保证「我的」有足够的滚动距离（与页面内容多少无关）
    with short_viewport(page):
        # 底部留白：滚到底后最后一项内容在 Tab 栏之上
        scroll_root_to(page, "bottom")
        clearance = page.evaluate(BOTTOM_CLEARANCE_JS)
        run.check(
            "一级页面底部留白：滚到底，最后一项内容完整露在 Tab 栏之上",
            clearance["lowest"] <= clearance["tabTop"] + 1,
            str(clearance),
        )
        run.check("留白 ≥ Tab 栏高度 + 距底（62 + 21）", clearance["paddingBottom"] >= 62 + 21, str(clearance))

        # 再次点按当前 Tab：滚到顶部
        state = scroll_root_state(page)
        run.check("（前置）已滚到下方", state and state["top"] > 100, str(state))
        expand_tab_bar(page)  # 向下滚动后 Tab 栏收缩成胶囊（04 号），先展开才能点到 Tab
        tab_button(page, "我的").click()
        wait_scroll_root_top(page)
        run.check("再次点按当前 Tab：滚动回到顶部", scroll_root_state(page)["top"] <= 1)
        run.check("回顶不切换页面，仍在「我的」", active_tab(page) == "profile" and path(page) == "/profile")

    # 进入二级页面（我的 → 账本管理）：Tab 栏隐藏
    active_root(page).get_by_text("账本管理", exact=True).first.click()
    wait_url(page, r"^/ledgers")
    wait_tab_bar(page, visible=False)
    run.check("进入二级页面：Tab 栏隐藏", not tab_bar(page).is_visible())
    run.check("停在二级页面时仍记得所属 Tab（data-active-tab = profile）", active_tab(page) == "profile")
    shot(page, "tab-shell-secondary")

    # 返回：Tab 栏恢复
    page.go_back()
    wait_url(page, TAB_PATHS["我的"])
    wait_tab_bar(page)
    run.check("返回根页面：Tab 栏恢复显示", tab_bar(page).is_visible() and active_tab(page) == "profile")

    # 过渡期间 / 二级页面里点按当前 Tab：先回到根页面。Tab 栏已隐藏，用 DOM 点击模拟「隐藏过渡中的那一下」
    active_root(page).get_by_text("账本管理", exact=True).first.click()
    wait_url(page, r"^/ledgers")
    wait_tab_bar(page, visible=False)
    page.evaluate("() => document.querySelector('[data-testid=\"tab-bar\"] [data-tab=\"profile\"]').click()")
    wait_url(page, TAB_PATHS["我的"])
    wait_tab_bar(page)
    run.check("停在二级页面时再点当前 Tab：先回到它的根页面", path(page) == "/profile" and tab_bar(page).is_visible())


def check_add_button(run: TestRun, page) -> None:
    """F. 「记一笔」圆钮"""
    for title in TAB_TITLES:
        open_tab(page, title)
        run.check(f"「记一笔」圆钮在「{title}」根页面可见", tab_bar(page).get_by_role("button", name="记一笔").is_visible())

    # 15 号起「记一笔」不再是路由页，而是从底部升起的 Sheet：地址不变、Tab 栏留在原处（被遮罩盖住），关闭后原样回来
    open_tab(page, "首页")
    tab_bar(page).get_by_role("button", name="记一笔").click()
    sheet = page.get_by_test_id("addbill-sheet")
    sheet.wait_for(state="visible")
    run.check("点按「记一笔」升起记一笔 Sheet：地址不变，仍停在首页", path(page) == "/" and active_tab(page) == "home")
    covered = page.evaluate(
        """() => {
            const b = document.querySelector('[data-testid="tab-bar-add"]').getBoundingClientRect();
            const hit = document.elementFromPoint(b.x + b.width / 2, b.y + b.height / 2);
            return !!hit && !hit.closest('[data-testid="tab-bar"]');
        }"""
    )
    run.check("Sheet 升起时 Tab 栏被遮罩盖住（点不到）", covered)
    sheet.get_by_test_id("sheet-close").click()
    sheet.wait_for(state="detached")
    run.check("关闭 Sheet 后 Tab 栏恢复可点，仍停在首页", tab_bar(page).is_visible() and active_tab(page) == "home" and path(page) == "/")


def check_logged_out(run: TestRun) -> None:
    """G. 未登录没有 Tab 栏"""
    with session() as anonymous:
        page = anonymous.page
        open_app(page, "/")
        page.wait_for_selector("input[type='tel']", timeout=25_000)
        run.check("未登录访问首页被带到登录页", path(page) in ("/", "/login"), path(page))
        run.check("登录页没有 Tab 栏（元素根本不存在）", tab_bar(page).count() == 0)
        run.check("登录页没有「记一笔」圆钮", page.get_by_role("button", name="记一笔").count() == 0)
        shot(page, "tab-shell-login")

        open_app(page, "/login")
        page.wait_for_selector("input[type='tel']", timeout=25_000)
        run.check("直接访问 /login 同样没有 Tab 栏", tab_bar(page).count() == 0)
        run.check_clean(anonymous.errors, "未登录会话无页面异常 / 控制台错误 / 失败请求")


def check_wide_viewport(run: TestRun) -> None:
    """H. 宽屏：应用壳与 Tab 栏限制最大宽度并居中"""
    with session(viewport=WIDE_VIEWPORT) as wide:
        page = wide.page
        login(page)
        wait_tab_bar(page)
        wait_animations_settled(page)
        center_x = WIDE_VIEWPORT["width"] / 2

        shell = page.evaluate("() => { const r = document.querySelector('.m-application').getBoundingClientRect(); return { left: r.left, right: r.right, width: r.width }; }")
        run.check("应用壳宽度受限（≤ 480）", 300 <= shell["width"] <= 480.5, str(shell))
        run.check("应用壳水平居中", approx((shell["left"] + shell["right"]) / 2, center_x, 2), str(shell))

        wrap = page.evaluate(TAB_LAYOUT_JS)["wrap"]
        run.check("Tab 栏宽度受限，保持手机比例（≤ 480 - 32）", 300 <= wrap["width"] <= 448.5, str(wrap))
        run.check("Tab 栏水平居中", approx((wrap["left"] + wrap["right"]) / 2, center_x, 2), str(wrap))
        run.check("Tab 栏距底 21", approx(wrap["bottom"], WIDE_VIEWPORT["height"] - 21), str(wrap))
        overflow = page.evaluate("() => document.documentElement.scrollWidth - document.documentElement.clientWidth")
        run.check("宽屏下没有横向溢出", overflow <= 0, str(overflow))
        shot(page, "tab-shell-wide-home")

        # 二级页面（页面栈用 position: fixed）也必须收在壳里，不能拉伸到整个窗口
        open_tab(page, "我的")
        active_root(page).get_by_text("账本管理", exact=True).first.click()
        wait_url(page, r"^/ledgers")
        wait_tab_bar(page, visible=False)
        wait_animations_settled(page)
        info = page.evaluate(FIXED_ELEMENTS_JS)
        escaped = [e for e in info["fixed"] if e["left"] < info["shell"]["left"] - 1 or e["right"] > info["shell"]["right"] + 1]
        run.check("宽屏下二级页面里所有固定定位元素都收在应用壳内", not escaped, f"shell={info['shell']} escaped={escaped}")
        # 压栈时下面那一页会向左错开一截，不能露在壳外：壳左右两侧的空白区域不应命中任何应用内容
        outside = page.evaluate(OUTSIDE_SHELL_PROBE_JS)
        run.check("宽屏下壳左右两侧的空白区域没有应用内容露出", not outside["left"] and not outside["right"], str(outside))
        shot(page, "tab-shell-wide-secondary")

        page.go_back()
        wait_tab_bar(page)
        run.check_clean(wide.errors, "宽屏会话无页面异常 / 控制台错误 / 失败请求")


def main() -> int:
    require_server()
    run = TestRun("tab-shell")

    with session() as s:
        page = s.page
        login(page)

        check_tokens(run, page)
        check_tab_bar_look(run, page)
        check_switch_tabs(run, page)
        check_state_preserved(run, page)
        check_secondary_pages(run, page)
        check_add_button(run, page)

        run.check_clean(s.errors)

    check_logged_out(run)
    check_wide_viewport(run)

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
