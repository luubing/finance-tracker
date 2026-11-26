# -*- coding: utf-8 -*-
"""
E2E 测试：兜底页面「页面未找到」（iOS UI 重构 · 24 号 ticket 清理旧视觉遗留时重做）。

访问一个不存在的地址时，应用显示通用空状态 + 「返回首页」——设计令牌与复用组件（EmptyState / GlassButton），
不再是 MASA 的 MContainer / MRow / MButton 与写死的 `height: 100vh`、内联样式。

  A. 外壳与结构：整屏一层（位置贴齐四边、不写高度）、页面底色、没有 Tab 栏、不含 MASA 控件 / `.container` / 内联样式；
     空状态：图标 + 标题「页面未找到」（h2）+ 主按钮「返回首页」（品牌渐变、点击热区 ≥ 44）；声明浅色背景（状态栏深色图标）
  B. 沉浸式：模拟刘海（状态栏 47）+ 手势条（34）：内容留在安全区之内，仍铺满整屏
  C. 宽屏（1280×800）：与应用的其它页一样收在手机宽度的壳里并水平居中
  D. 返回首页：未登录回到登录页；已登录回到首页（Tab 栏出现）
  E. 页面没有异常 / 控制台错误 / 失败请求

「组件渲染发生异常」兜底（ErrorBoundary 的 ErrorContent）无法在 E2E 里自动触发（渲染失败没法注入）：样式与结构由单元测试
VisualSystemGuardTests 守住（无 MASA 控件 / 静态内联样式 / 写死颜色），页面本身走查代码 + 人工验收。

可观察属性（已登记到 README）：not-found（兜底页根，data-statusbar-bg）、not-found-empty、not-found-home。
"""
import sys

from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

from e2e_base import (
    PHONE_VIEWPORT,
    TestRun,
    login,
    open_app,
    require_server,
    session,
    shot,
    wait_tab_bar,
)

SCREEN_W = PHONE_VIEWPORT["width"]
SCREEN_H = PHONE_VIEWPORT["height"]
WIDE_VIEWPORT = {"width": 1280, "height": 800}
MISSING_PATH = "/no-such-page-24"

RGB_BG = "rgb(242, 242, 247)"  # --ft-bg #F2F2F7
SAFE_TOP = 47
SAFE_BOTTOM = 34

STATE_JS = r"""() => {
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  const box = (el) => { if (!el) return null; const r = el.getBoundingClientRect(); return { l: r.left, r: r.right, t: r.top, b: r.bottom, w: r.width, h: r.height }; };
  const cs = (el, prop) => (el ? getComputedStyle(el)[prop] : null);
  const text = (el) => (el ? el.textContent.replace(/\s+/g, ' ').trim() : null);
  const root = q('not-found');
  const empty = q('not-found-empty');
  const home = q('not-found-home');
  const title = empty ? empty.querySelector('h2') : null;
  return {
    path: location.pathname,
    exists: !!root,
    statusbarBg: root ? root.getAttribute('data-statusbar-bg') : null,
    icons: document.documentElement.getAttribute('data-statusbar-icons'),
    rootClass: root ? root.className : null,
    root: box(root), empty: box(empty), home: box(home), title: box(title),
    position: cs(root, 'position'),
    inlineStyle: root ? [root, ...root.querySelectorAll('*')].filter((el) => el.hasAttribute('style')).length : null,
    background: cs(root, 'backgroundColor'),
    titleText: text(title),
    titleTag: title ? title.tagName : null,
    homeText: text(home),
    homeTag: home ? home.tagName : null,
    homeType: home ? home.getAttribute('type') : null,
    homeBackground: cs(home, 'backgroundImage'),
    masa: root ? root.querySelectorAll('.m-btn, .m-icon, .row, .col, .container, [class*="m-sheet"]').length : null,
    tabBar: !!q('tab-bar'),
    shell: (() => { const s = document.querySelector('.m-application'); return s ? box(s) : null; })(),
    overflowX: document.documentElement.scrollWidth - window.innerWidth,
  };
}"""

SET_SAFE_JS = """([top, bottom]) => {
  const style = document.documentElement.style;
  style.setProperty('--ft-safe-top', top + 'px');
  style.setProperty('--ft-safe-bottom', bottom + 'px');
}"""


def approx(actual, expected, tolerance: float = 1.0) -> bool:
    return actual is not None and abs(actual - expected) <= tolerance


def state(page) -> dict:
    return page.evaluate(STATE_JS)


def wait_icons(page, value: str) -> None:
    """状态栏图标风格由 immersive.js 对顶部像素采样得出，渲染之后才更新；读它之前先等到期望值（等不到就按实际值让断言失败）。"""
    try:
        page.wait_for_function(f"() => document.documentElement.getAttribute('data-statusbar-icons') === '{value}'", timeout=5_000)
    except PlaywrightTimeoutError:
        pass


def check_shell(run: TestRun) -> None:
    with session() as s:
        page = s.page
        open_app(page, MISSING_PATH)
        page.wait_for_selector("[data-testid='not-found']", timeout=15_000)
        wait_icons(page, "dark")
        st = state(page)

        run.check("A. 地址保持不变（不被改写）", st["path"] == MISSING_PATH, st["path"])
        run.check("A. 整屏一层：position fixed，四边贴齐视口",
                  st["position"] == "fixed" and approx(st["root"]["l"], 0) and approx(st["root"]["t"], 0)
                  and approx(st["root"]["w"], SCREEN_W) and approx(st["root"]["h"], SCREEN_H), str(st["root"]))
        run.check("A. 页面底色是 --ft-bg", st["background"] == RGB_BG, str(st["background"]))
        run.check("A. 没有 Tab 栏", not st["tabBar"])
        run.check("A. 不含 MASA 控件 / .container，也没有内联样式", st["masa"] == 0 and st["inlineStyle"] == 0, f"masa={st['masa']} style={st['inlineStyle']}")
        run.check("A. 声明浅色背景（状态栏深色图标）", st["statusbarBg"] == "light" and st["icons"] == "dark", f"{st['statusbarBg']} / {st['icons']}")
        run.check("A. 标题「页面未找到」是 h2", st["titleText"] == "页面未找到" and st["titleTag"] == "H2", f"{st['titleText']} {st['titleTag']}")
        run.check("A. 主按钮「返回首页」是 button、品牌渐变、点击热区 ≥ 44",
                  st["homeText"] == "返回首页" and st["homeTag"] == "BUTTON" and st["homeType"] == "button"
                  and "gradient" in (st["homeBackground"] or "") and st["home"]["h"] >= 44 and st["home"]["w"] >= 44,
                  str((st["homeText"], st["homeTag"], st["home"])))
        run.check("A. 空状态在屏幕中央（水平居中、垂直居中）",
                  approx((st["empty"]["l"] + st["empty"]["r"]) / 2, SCREEN_W / 2, 2)
                  and approx((st["empty"]["t"] + st["empty"]["b"]) / 2, SCREEN_H / 2, 3), str(st["empty"]))
        run.check("A. 没有横向溢出", st["overflowX"] <= 0, str(st["overflowX"]))
        shot(page, "not-found")
        run.check_clean(s.errors, "A 节会话无页面异常 / 控制台错误 / 失败请求")


def check_immersive(run: TestRun) -> None:
    with session() as s:
        page = s.page
        open_app(page, MISSING_PATH)
        page.wait_for_selector("[data-testid='not-found']", timeout=15_000)
        page.evaluate(SET_SAFE_JS, [SAFE_TOP, SAFE_BOTTOM])
        page.wait_for_function("() => getComputedStyle(document.documentElement).getPropertyValue('--ft-safe-top').trim() === '47px'")
        st = state(page)

        run.check("B. 仍铺满整屏（含状态栏之下与手势条之下）",
                  approx(st["root"]["t"], 0) and approx(st["root"]["b"], SCREEN_H), str(st["root"]))
        run.check("B. 内容留在安全区之内（不钻进刘海 / 手势条）",
                  st["empty"]["t"] >= SAFE_TOP - 0.5 and st["empty"]["b"] <= SCREEN_H - SAFE_BOTTOM + 0.5, str(st["empty"]))
        run.check_clean(s.errors, "B 节会话无页面异常 / 控制台错误 / 失败请求")


def check_wide(run: TestRun) -> None:
    with session(viewport=WIDE_VIEWPORT) as s:
        page = s.page
        open_app(page, MISSING_PATH)
        page.wait_for_selector("[data-testid='not-found']", timeout=15_000)
        st = state(page)
        center_x = WIDE_VIEWPORT["width"] / 2

        run.check("C. 应用壳宽度受限（≤ 480）", st["shell"] is not None and 300 <= st["shell"]["w"] <= 480.5, str(st["shell"]))
        run.check("C. 兜底页收在壳里并水平居中（不拉伸到整个窗口）",
                  st["root"]["w"] <= 480.5 and approx((st["root"]["l"] + st["root"]["r"]) / 2, center_x, 2), str(st["root"]))
        shot(page, "not-found-wide")
        run.check_clean(s.errors, "C 节会话无页面异常 / 控制台错误 / 失败请求")


def check_go_home(run: TestRun) -> None:
    # 未登录：回到首页被带到登录页
    with session() as s:
        page = s.page
        open_app(page, MISSING_PATH)
        page.get_by_test_id("not-found-home").click()
        page.wait_for_selector("[data-testid='login-phone']", timeout=25_000)
        st = state(page)
        run.check("D. 未登录点「返回首页」→ 登录页（/ 被带到 /login），兜底页已消失", st["path"] == "/login" and not st["exists"], f"{st['path']} exists={st['exists']}")
        run.check_clean(s.errors, "D 节（未登录）会话无页面异常 / 控制台错误 / 失败请求")

    # 已登录：回到首页，Tab 栏出现
    with session() as s:
        page = s.page
        login(page)
        open_app(page, MISSING_PATH)
        page.wait_for_selector("[data-testid='not-found']", timeout=15_000)
        run.check("D. 已登录访问不存在的地址也是兜底页（没有 Tab 栏）", not state(page)["tabBar"])
        page.get_by_test_id("not-found-home").click()
        wait_tab_bar(page)
        page.wait_for_selector("[data-testid='home-page']", timeout=15_000)
        st = state(page)
        run.check("D. 已登录点「返回首页」→ 首页（Tab 栏出现、兜底页消失）", st["path"] == "/" and st["tabBar"] and not st["exists"],
                  f"{st['path']} tabBar={st['tabBar']} exists={st['exists']}")
        run.check_clean(s.errors, "D 节（已登录）会话无页面异常 / 控制台错误 / 失败请求")


SECTIONS = {
    "A": check_shell,
    "B": check_immersive,
    "C": check_wide,
    "D": check_go_home,
}


def main() -> int:
    """不带参数跑全部小节；带小节名（如 `python not_found_test.py B`）只跑指定小节，便于定位问题。"""
    require_server()
    run = TestRun("not-found")

    wanted = [a.upper() for a in sys.argv[1:]]
    unknown = [a for a in wanted if a not in SECTIONS]
    if unknown:
        print(f"[ENV] 不认识的小节：{unknown}；可选：{list(SECTIONS)}")
        return 2

    for name, section in SECTIONS.items():
        if not wanted or name in wanted:
            section(run)

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
