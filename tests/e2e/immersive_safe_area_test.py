# -*- coding: utf-8 -*-
"""
E2E 测试：沉浸式状态栏与安全区（iOS UI 重构 · 02 号 ticket）。

只覆盖网页层——原生部分（Android 边到边 / WindowInsets、iOS SafeAreaInsets、状态栏图标颜色真正切换）
无法在 Web.Server 里验证，见 ticket 02 的人工验收清单。这里验证 Web 层与原生之间的**契约**：

  A. 安全区变量 --ft-safe-top/right/bottom/left
     - 浏览器默认全为 0（Web.Server 不受影响）；
     - 原生注入（immersive.js 的 setSafeArea / clearSafeArea）优先于 CSS env()，非法值被钳到 0；
     - 底部边距里「只是 Home 指示条」的部分（setSafeArea 的第 5 个参数 → --ft-safe-indicator）与其余的「实心」部分（--ft-safe-bottom-solid），
       后者 = 整个底部边距 − 指示条，不会小于 0；Tab 栏只避开实心部分（iPhone 上距底仍是 21，压住 Home 指示条），键盘 / 三键导航栏整个避开；
  B. 状态栏图标风格 <html data-statusbar-icons="light|dark">
     - 采样：状态栏下方是深色不透明背景 → light（浅色图标），浅色 → dark；半透明 / 全透明元素不算背景；
     - 声明：data-statusbar-bg="dark|light" 优先于采样；移除声明后恢复；
     - 跟随：滚动、进入 / 返回二级页面（浅色导航栏）时自动更新；
  C. Tab 栏在刘海 / 手势条 / Home 指示条 / 键盘下的几何，以及从首页（深色 Hero，浅色图标）压栈进入二级页面（浅色导航栏，深色图标）再返回时状态栏图标跟随、
     新模板的导航栏让出状态栏。四个 Tab 根页面与全部二级页面都已迁移（22 号迁完最后一个旧二级页面「错误日志」），
     旧的 .container 根页面外壳与「迁移期兼容层」断言（依赖 PageStack 内部 DOM）都已删除；各页让出安全区的细节由各页自己的 E2E 覆盖。

可观察属性（已登记到 README）：html[data-statusbar-icons]、--ft-safe-*、[data-statusbar-bg]。
"""
import sys

from e2e_base import (
    PHONE_VIEWPORT,
    TestRun,
    active_root,
    login,
    open_app,
    open_tab,
    require_server,
    session,
    shot,
    wait_animations_settled,
    wait_attr,
    wait_secondary_pages_closed,
    wait_tab_bar,
    wait_url,
)

SCREEN_H = PHONE_VIEWPORT["height"]
TOP = 47  # 灵动岛 / 刘海机型状态栏高度
BOTTOM = 34  # 手势条 / Home 指示条高度
KEYBOARD = 336  # 系统键盘高度

READ_SAFE_JS = """() => {
  // 用探针元素读出 --ft-safe-* 的实际生效值（自定义属性本身可能带 env()/var() 原文）
  const probe = document.createElement('div');
  probe.style.cssText = 'position:fixed;top:0;left:0;visibility:hidden;pointer-events:none;'
    + 'padding:var(--ft-safe-top) var(--ft-safe-right) var(--ft-safe-bottom) var(--ft-safe-left)';
  document.body.appendChild(probe);
  const cs = getComputedStyle(probe);
  const values = [cs.paddingTop, cs.paddingRight, cs.paddingBottom, cs.paddingLeft].map(parseFloat);
  probe.remove();
  return values;
}"""

# 读出 --ft-safe-indicator（底部边距里只是 Home 指示条的部分）与 --ft-safe-bottom-solid（必须整个避开的部分）的实际生效值
READ_BOTTOM_PARTS_JS = """() => {
  const probe = document.createElement('div');
  probe.style.cssText = 'position:fixed;top:0;left:0;visibility:hidden;pointer-events:none;'
    + 'padding-top:var(--ft-safe-indicator);padding-bottom:var(--ft-safe-bottom-solid)';
  document.body.appendChild(probe);
  const cs = getComputedStyle(probe);
  const parts = { indicator: parseFloat(cs.paddingTop), solid: parseFloat(cs.paddingBottom) };
  probe.remove();
  return parts;
}"""

# 模拟原生宿主注入：与 ImmersiveCoordinator → immersive.js setSafeArea 同一条路径的 JS 端（参数顺序 上、右、下、左、底部指示条）
SET_NATIVE_JS = """async (args) => {
  const module = await import('/_content/FinanceTracker.Web/js/immersive.js');
  module.setSafeArea(...args);
}"""

CLEAR_NATIVE_JS = """async () => {
  const module = await import('/_content/FinanceTracker.Web/js/immersive.js');
  module.clearSafeArea();
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

TAB_BAR_JS = """() => {
  const tabBar = document.querySelector('[data-testid="tab-bar"]');
  if (!tabBar) return null;
  const r = tabBar.getBoundingClientRect();
  return { top: r.top, bottom: r.bottom, height: r.height };
}"""

NAV_JS = """() => {
  const box = (el) => { if (!el) return null; const r = el.getBoundingClientRect(); return { top: r.top, bottom: r.bottom, height: r.height }; };
  return {
    bar: box(document.querySelector('.ft-subbar')),
    nav: box(document.querySelector('[data-testid="nav-bar"]')),
    page: box(document.querySelector('[data-testid="secondary-page"], .ft-subpage')),
  };
}"""

# 在页面里放一个固定在屏幕顶部的元素，用来制造"状态栏下方的背景"
ADD_TOP_ELEMENT_JS = """([id, css, declared]) => {
  document.getElementById(id)?.remove();
  const el = document.createElement('div');
  el.id = id;
  el.style.cssText = 'position:fixed;top:0;left:0;right:0;height:120px;z-index:2147483000;' + css;
  if (declared) el.setAttribute('data-statusbar-bg', declared);
  document.body.appendChild(el);
}"""

REMOVE_ELEMENT_JS = "(id) => document.getElementById(id)?.remove()"

# 独立的滚动沙盒：顶部 300px 声明为深色的"Hero"，下面是白色长内容（不依赖应用现有页面内容）
ADD_SCROLL_SANDBOX_JS = """() => {
  document.getElementById('ft-probe-scroller')?.remove();
  const scroller = document.createElement('div');
  scroller.id = 'ft-probe-scroller';
  scroller.style.cssText = 'position:fixed;inset:0;overflow-y:auto;z-index:2147483000;background:#fff';
  const hero = document.createElement('div');
  hero.setAttribute('data-statusbar-bg', 'dark');
  hero.style.cssText = 'height:300px;background:linear-gradient(#4318FF,#7A45FF)';
  const body = document.createElement('div');
  body.style.cssText = 'height:2000px';
  scroller.append(hero, body);
  document.body.appendChild(scroller);
}"""


def icons(page) -> str:
    return page.evaluate("() => document.documentElement.getAttribute('data-statusbar-icons')")


def read_safe(page) -> "list[float]":
    return page.evaluate(READ_SAFE_JS)


def read_bottom_parts(page) -> "dict[str, float]":
    return page.evaluate(READ_BOTTOM_PARTS_JS)


def approx(actual, expected, tolerance: float = 1.0) -> bool:
    return actual is not None and abs(actual - expected) <= tolerance


def check_safe_area_contract(run: TestRun, page) -> None:
    """A. 安全区变量：默认值与原生注入契约"""
    run.check("浏览器默认安全区四边都是 0", read_safe(page) == [0, 0, 0, 0], str(read_safe(page)))

    # 模拟原生宿主注入（与 ImmersiveCoordinator → immersive.js setSafeArea 同一条路径的 JS 端）
    page.evaluate(
        """async () => {
            const module = await import('/_content/FinanceTracker.Web/js/immersive.js');
            module.setSafeArea(50, 8, 30, 6);
        }"""
    )
    run.check("原生注入后 --ft-safe-* 取原生值", read_safe(page) == [50, 8, 30, 6], str(read_safe(page)))

    page.evaluate(
        """async () => {
            const module = await import('/_content/FinanceTracker.Web/js/immersive.js');
            module.setSafeArea(-5, 'abc', null, undefined);
        }"""
    )
    run.check("非法 / 负数的原生值被钳到 0", read_safe(page) == [0, 0, 0, 0], str(read_safe(page)))

    page.evaluate(
        """async () => {
            const module = await import('/_content/FinanceTracker.Web/js/immersive.js');
            module.setSafeArea(50, 8, 30, 6);
            module.clearSafeArea();
        }"""
    )
    run.check("clearSafeArea 后回落到默认 0", read_safe(page) == [0, 0, 0, 0], str(read_safe(page)))

    check_bottom_indicator_contract(run, page)

    page.evaluate(SET_SAFE_JS, [TOP, BOTTOM])
    page.evaluate(
        """async () => {
            const module = await import('/_content/FinanceTracker.Web/js/immersive.js');
            module.setSafeArea(1, 1, 1, 1);
        }"""
    )
    run.check(
        "页面（html 内联）覆盖优先于原生注入，便于 E2E 模拟刘海",
        read_safe(page)[0] == TOP and read_safe(page)[2] == BOTTOM,
        str(read_safe(page)),
    )
    page.evaluate(CLEAR_SAFE_JS)
    page.evaluate(
        """async () => {
            const module = await import('/_content/FinanceTracker.Web/js/immersive.js');
            module.clearSafeArea();
        }"""
    )


def check_bottom_indicator_contract(run: TestRun, page) -> None:
    """A（续）. 底部边距里「只是 Home 指示条」的部分：--ft-safe-indicator / --ft-safe-bottom-solid"""
    run.check(
        "浏览器默认：没有指示条、没有实心占用（env(safe-area-inset-bottom) = 0）",
        read_bottom_parts(page) == {"indicator": 0, "solid": 0},
        str(read_bottom_parts(page)),
    )

    page.evaluate(SET_NATIVE_JS, [50, 8, 34, 6, 34])
    parts = read_bottom_parts(page)
    run.check(
        "iPhone：整个底部边距 34 都标明是 Home 指示条 → 实心部分 0（Tab 栏可以压住它）",
        read_safe(page) == [50, 8, 34, 6] and parts == {"indicator": 34, "solid": 0},
        f"{read_safe(page)} {parts}",
    )

    page.evaluate(SET_NATIVE_JS, [50, 8, 34, 6])
    parts = read_bottom_parts(page)
    run.check(
        "宿主没传第 5 个参数（Android）→ 指示条 0，整个底部边距都是实心占用",
        read_safe(page) == [50, 8, 34, 6] and parts == {"indicator": 0, "solid": 34},
        f"{read_safe(page)} {parts}",
    )

    page.evaluate(SET_NATIVE_JS, [50, 8, 336, 6, 0])
    parts = read_bottom_parts(page)
    run.check(
        "键盘升起（底部边距并入键盘高度 336，指示条归零）→ 实心部分 336，Tab 栏整个避开",
        parts == {"indicator": 0, "solid": 336},
        str(parts),
    )

    page.evaluate(SET_NATIVE_JS, [50, 8, 34, 6, 50])
    parts = read_bottom_parts(page)
    run.check(
        "指示条比整个底部边距还大 → 实心部分钳到 0，不会出现负的距底",
        parts == {"indicator": 50, "solid": 0},
        str(parts),
    )

    page.evaluate(SET_NATIVE_JS, [50, 8, 34, 6, -5])
    negative = read_bottom_parts(page)
    page.evaluate(SET_NATIVE_JS, [50, 8, 34, 6, "abc"])
    garbage = read_bottom_parts(page)
    page.evaluate(SET_NATIVE_JS, [50, 8, 34, 6, None])
    missing = read_bottom_parts(page)
    expected = {"indicator": 0, "solid": 34}
    run.check(
        "非法 / 负数 / 空的指示条值被钳到 0（按「整个底部边距都是实心」处理，最保守）",
        negative == expected and garbage == expected and missing == expected,
        f"{negative} {garbage} {missing}",
    )

    page.evaluate(CLEAR_NATIVE_JS)
    parts = read_bottom_parts(page)
    run.check(
        "clearSafeArea 后指示条一并清除，回落默认（env = 0）",
        read_safe(page) == [0, 0, 0, 0] and parts == {"indicator": 0, "solid": 0},
        f"{read_safe(page)} {parts}",
    )

    # 页面（html 内联）覆盖优先于原生注入：与 --ft-safe-* 一样，E2E 可以直接在 html 上模拟指示条
    page.evaluate(SET_NATIVE_JS, [0, 0, 20, 0, 0])
    page.evaluate("() => document.documentElement.style.setProperty('--ft-safe-indicator', '20px')")
    overridden = read_bottom_parts(page)
    page.evaluate("() => document.documentElement.style.removeProperty('--ft-safe-indicator')")
    page.evaluate(CLEAR_NATIVE_JS)
    run.check("页面（html 内联）对 --ft-safe-indicator 的覆盖优先于原生注入", overridden == {"indicator": 20, "solid": 0}, str(overridden))


def check_status_bar_protocol(run: TestRun, page) -> None:
    """B. 状态栏图标风格：采样、声明、滚动跟随"""
    wait_attr(page, "html", "data-statusbar-icons", "dark")
    run.check("白色根页面 → 深色图标", icons(page) == "dark", str(icons(page)))

    # --- 采样（无声明）---
    page.evaluate(ADD_TOP_ELEMENT_JS, ["ft-probe-top", "background:#4318FF", None])
    wait_attr(page, "html", "data-statusbar-icons", "light")
    run.check("顶部是品牌紫色不透明背景 → 浅色图标", icons(page) == "light")

    page.evaluate(ADD_TOP_ELEMENT_JS, ["ft-probe-top", "background:#F2F2F7", None])
    wait_attr(page, "html", "data-statusbar-icons", "dark")
    run.check("顶部是浅灰背景 → 深色图标", icons(page) == "dark")

    page.evaluate(ADD_TOP_ELEMENT_JS, ["ft-probe-top", "background:#FFD60A", None])
    wait_attr(page, "html", "data-statusbar-icons", "dark")
    run.check("顶部是明亮的黄色背景 → 深色图标", icons(page) == "dark")

    page.evaluate(ADD_TOP_ELEMENT_JS, ["ft-probe-top", "background:rgba(0,0,0,0.3)", None])
    page.wait_for_timeout(150)  # 负向断言：给一帧以上的时间确认"不会翻转"
    run.check("半透明遮罩不算背景（沿用下层页面）→ 仍是深色图标", icons(page) == "dark")

    page.evaluate(ADD_TOP_ELEMENT_JS, ["ft-probe-top", "background:#4318FF;opacity:0", None])
    page.wait_for_timeout(150)
    run.check("opacity:0 的元素不参与判定", icons(page) == "dark")

    page.evaluate(ADD_TOP_ELEMENT_JS, ["ft-probe-top", "background:linear-gradient(#4318FF,#7A45FF)", None])
    page.wait_for_timeout(150)
    run.check("渐变背景无法采样，需要声明（未声明时沿用下层）", icons(page) == "dark")

    # --- 声明 ---
    page.evaluate(ADD_TOP_ELEMENT_JS, ["ft-probe-top", "background:linear-gradient(#4318FF,#7A45FF)", "dark"])
    wait_attr(page, "html", "data-statusbar-icons", "light")
    run.check("声明 data-statusbar-bg=dark（渐变背景）→ 浅色图标", icons(page) == "light")

    page.evaluate(ADD_TOP_ELEMENT_JS, ["ft-probe-top", "background:#4318FF", "light"])
    wait_attr(page, "html", "data-statusbar-icons", "dark")
    run.check("声明优先于背景采样：紫色背景声明为 light → 深色图标", icons(page) == "dark")

    page.evaluate(ADD_TOP_ELEMENT_JS, ["ft-probe-top", "background:#fff", "dark"])
    wait_attr(page, "html", "data-statusbar-icons", "light")
    page.evaluate("() => document.getElementById('ft-probe-top').setAttribute('data-statusbar-bg', 'light')")
    wait_attr(page, "html", "data-statusbar-icons", "dark")
    run.check("运行中修改声明值会即时重新判定", icons(page) == "dark")

    page.evaluate(ADD_TOP_ELEMENT_JS, ["ft-probe-top", "background:#4318FF", "dark"])
    wait_attr(page, "html", "data-statusbar-icons", "light")
    page.evaluate(REMOVE_ELEMENT_JS, "ft-probe-top")
    wait_attr(page, "html", "data-statusbar-icons", "dark")
    run.check("移除声明元素后恢复为页面本身的判定", icons(page) == "dark")

    # --- 滚动跟随 ---
    page.evaluate(ADD_SCROLL_SANDBOX_JS)
    wait_attr(page, "html", "data-statusbar-icons", "light")
    run.check("滚动前：顶部是声明为深色的 Hero → 浅色图标", icons(page) == "light")
    page.evaluate("() => { document.getElementById('ft-probe-scroller').scrollTop = 600; }")
    wait_attr(page, "html", "data-statusbar-icons", "dark")
    run.check("滚动离开 Hero 后 → 深色图标（随滚动更新）", icons(page) == "dark")
    page.evaluate("() => { document.getElementById('ft-probe-scroller').scrollTop = 0; }")
    wait_attr(page, "html", "data-statusbar-icons", "light")
    run.check("滚回顶部 → 再次切换为浅色图标", icons(page) == "light")
    page.evaluate(REMOVE_ELEMENT_JS, "ft-probe-scroller")
    wait_attr(page, "html", "data-statusbar-icons", "dark")


def check_tab_bar_and_push(run: TestRun, page) -> None:
    """C. 安全区下的 Tab 栏几何 + 压栈进入二级页面时状态栏图标跟随

    四个 Tab 根页面与全部二级页面都是新结构（22 号迁完最后一个旧二级页面），根页面 / 二级页面让出状态栏、留出底部占位的细节
    由各页自己的 E2E 覆盖（例如 profile_page_test.py、ledgers_page_test.py 的「沉浸式」一节）。
    这里只剩两块跨页面的契约：Tab 栏在刘海 / 手势条下的几何；首页（深色 Hero）→ 二级页面（浅色导航栏）→ 返回时状态栏图标来回切换。
    """
    open_app(page, "/profile")
    wait_tab_bar(page)
    wait_attr(page, "html", "data-statusbar-icons", "dark")

    base = page.evaluate(TAB_BAR_JS)
    run.check("默认：Tab 栏高 62，距底 21", base and approx(base["height"], 62) and approx(base["bottom"], SCREEN_H - 21), str(base))

    page.evaluate(SET_SAFE_JS, [TOP, BOTTOM])
    run.check("模拟刘海 + 手势条后变量生效", read_safe(page)[0] == TOP and read_safe(page)[2] == BOTTOM, str(read_safe(page)))
    wait_animations_settled(page)  # Tab 栏的位置有过渡动画

    notch = page.evaluate(TAB_BAR_JS)
    run.check(
        "底部 34 是实心占用（行内变量模拟、没标明指示条）时 Tab 栏停在它之上（高 62，底边 = 屏高 - 21 - 34）",
        notch and approx(notch["height"], 62) and approx(notch["bottom"], SCREEN_H - 21 - BOTTOM),
        str(notch),
    )
    shot(page, "immersive-root-notch")

    # 首页：顶部是声明为深色的 Hero → 浅色图标；点铃铛压栈进入「待确认账单」（浅色导航栏的二级页面）→ 深色图标；返回 → 再换回浅色图标
    # 注意：PageStack 只响应它自己的 Push，直接 Blazor.navigateTo 只会改地址栏，所以这里点真实的入口
    open_tab(page, "首页")
    wait_attr(page, "html", "data-statusbar-icons", "light")
    run.check("首页（顶部是深色 Hero）→ 浅色图标", icons(page) == "light")

    active_root(page).get_by_test_id("home-bell").click()
    wait_url(page, r"^/pending-bills$")
    wait_attr(page, "html", "data-statusbar-icons", "dark")
    wait_animations_settled(page)
    run.check("进入二级页面（浅色导航栏）→ 深色图标", icons(page) == "dark")

    pushed = page.evaluate(NAV_JS)
    run.check("二级页面导航栏背景延伸到屏幕顶部，高 = 44 + 状态栏 47", pushed["bar"] and approx(pushed["bar"]["top"], 0) and approx(pushed["bar"]["height"], 44 + TOP), str(pushed["bar"]))
    run.check("二级页面导航行（返回 / 标题）在状态栏之下，高 44", pushed["nav"] and approx(pushed["nav"]["top"], TOP) and approx(pushed["nav"]["height"], 44), str(pushed["nav"]))
    shot(page, "immersive-pushed-notch")

    # 返回根页面 → 回到深色 Hero → 浅色图标
    page.go_back()
    wait_url(page, r"^/$")
    wait_secondary_pages_closed(page)
    wait_tab_bar(page)
    wait_attr(page, "html", "data-statusbar-icons", "light")
    run.check("返回首页 → 浅色图标", icons(page) == "light")

    page.evaluate(CLEAR_SAFE_JS)
    wait_animations_settled(page)
    restored = page.evaluate(TAB_BAR_JS)
    run.check(
        "安全区还原为 0 后 Tab 栏回到无刘海 / 手势条的取值",
        restored and approx(restored["height"], 62) and approx(restored["bottom"], SCREEN_H - 21),
        str(restored),
    )

    # 走真实的注入路径（与 ImmersiveCoordinator → immersive.js setSafeArea 同一条，不靠行内变量覆盖）：
    # iPhone 底部 34 全是 Home 指示条 → Tab 栏压住它，下沿离屏幕底 21（设计稿 01，指示条在它下面）；
    # 键盘升起 → 宿主把键盘高度并入底部边距、指示条归零 → Tab 栏整个停在键盘之上；收起键盘 → 回到压住指示条
    page.evaluate(SET_NATIVE_JS, [TOP, 0, BOTTOM, 0, BOTTOM])
    wait_animations_settled(page)
    ios = page.evaluate(TAB_BAR_JS)
    run.check(
        "iPhone（宿主上报底部 34 全是 Home 指示条）：Tab 栏压住它，高 62，底边 = 屏高 - 21",
        ios and approx(ios["height"], 62) and approx(ios["bottom"], SCREEN_H - 21),
        str(ios),
    )
    shot(page, "immersive-root-ios-indicator")

    page.evaluate(SET_NATIVE_JS, [TOP, 0, KEYBOARD, 0, 0])
    wait_animations_settled(page)
    keyboard = page.evaluate(TAB_BAR_JS)
    run.check(
        f"键盘升起（底部边距 {KEYBOARD}，指示条 0）：Tab 栏整个停在键盘之上，底边 = 屏高 - 21 - {KEYBOARD}",
        keyboard and approx(keyboard["bottom"], SCREEN_H - 21 - KEYBOARD) and keyboard["bottom"] <= SCREEN_H - KEYBOARD,
        str(keyboard),
    )

    page.evaluate(SET_NATIVE_JS, [TOP, 0, BOTTOM, 0, BOTTOM])
    wait_animations_settled(page)
    dismissed = page.evaluate(TAB_BAR_JS)
    run.check(
        "收起键盘：Tab 栏回到压住 Home 指示条的位置",
        dismissed and approx(dismissed["bottom"], SCREEN_H - 21),
        str(dismissed),
    )

    page.evaluate(CLEAR_NATIVE_JS)
    wait_animations_settled(page)
    cleared = page.evaluate(TAB_BAR_JS)
    run.check(
        "宿主注入清除后 Tab 栏回到默认位置",
        cleared and approx(cleared["bottom"], SCREEN_H - 21),
        str(cleared),
    )


def main() -> int:
    require_server()
    run = TestRun("immersive-safe-area")

    with session() as s:
        page = s.page
        login(page)
        # 协议检查需要一个顶部是浅色的页面做基线（首页顶部是深色 Hero，状态栏图标是浅色），所以停在「我的」
        open_app(page, "/profile")
        wait_tab_bar(page)

        check_safe_area_contract(run, page)
        check_status_bar_protocol(run, page)
        check_tab_bar_and_push(run, page)

        run.check_clean(s.errors)

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
