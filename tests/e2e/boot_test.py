# -*- coding: utf-8 -*-
"""
E2E：冷启动加载页与交接（冷启动设计稿 docs/design/cold-start；样式 boot.css、行为 boot.js，标记在两个宿主页里，StartupHost 报「外壳就绪」，
Index / LoginPage 用 data-ft-ready 声明「首屏就绪」）。

前置条件：Web.Server 已启动（powershell tests/e2e/server.ps1 -Action start）。

要验证的事（按「系统启动画面 → 加载页 → 首页 / 登录页」的接力来排）：
  A 首帧与慢启动：Blazor 还没来时加载页就在，压在应用上面，自带 Logo / 名称 / 标语（不依赖网络与 Blazor）；
    快启动不出现转圈，超过门槛才出现 12 根刻度的转圈与「正在加载…」；Blazor 一到，加载页收走、露出登录页
  B 启动失败：看门狗超时 →「启动失败 / 重新加载」玻璃卡片（只有一个按钮、焦点在容器上）；外壳后来起来了（只是慢）自动撤掉；
    Blazor 脚本直接加载失败时点「重新加载」整页重载，重载后正常启动
  C 未登录 → 登录页：handoff-login，Logo 组位移到真实登录页 Logo 的位置（终点重合 < 0.5 px），真实品牌区先藏着、收走后可见，面板升起
  G 慢 CDN 不挡启动：图标字体 / ECharts 的 CDN 请求一直不返回，加载页到交接上限（capMs）就带着现状交出
  D 已登录 → 首页：handoff-home，幕布收拢的终点高度 = 真实 Hero 的高度
  E 深链接（启动路径不是 / 与 /login）：整体淡出，没有首屏可等
  F 减弱动态效果：交接是 100 ms 的淡出，不位移、不收拢
  H 图表等 ECharts：ECharts 改成页面 load 之后才注入，用户可能在它下载完之前进统计 Tab——EChartsView 先等 FtBoot.chartsReady()，
    脚本被扣住时统计页不崩（骨架占位、Tab 栏与电路都活着），放行后图表自己出现；脚本加载失败时给「图表组件未能加载」说明、应用照常可用
    （没有这层等待时，ECharts 没到就 echarts.init 会抛 ReferenceError，界面整个废掉）
  I 材质降级：加载页的 Logo 方块不跟着玻璃令牌变色。系统启动画面的方块是实色，降级路径（系统降低透明度 / 宿主判定写的根开关 data-ft-solid）
    若让方块换成令牌里的另一个实色，就是加载页上的一次色跳；而根开关是 Blazor 启动之后才写的，加载页还在时就会翻转。
    登录交接时降级下的真方块是另一个实色，加载页的方块要随滑行换成它（走真实链路：E2E 假实现的宿主判定 → UiRuntime → 根开关）
（G 排在 D 之前：D 要先登录，登录之后冷启动落的是首页而不是登录页）

做法：
  - 状态走向用 <html data-ft-boot> 的 MutationObserver 记录（不靠轮询），等待一律基于状态；
  - 交接动画整体放慢 6 倍（只覆盖 --ft-dur-sheet / --ft-dur-fade 两个令牌；boot.js 读样式算收走时机，所以随之放慢），
    才有足够的窗口在动画结束、节点移除之前量终点位置；
  - 用 page.route 扣住 blazor.server.js 来模拟「Blazor 迟迟不来」。
"""
import io
import json
import re
import sys

from PIL import Image

import e2e_base as base
from e2e_base import TIMEOUT_MS, TestRun, require_server, session

#: 动画放慢的倍数（时长令牌：sheet 400 ms、fade 180 ms）
SLOW = 6

BLAZOR_SCRIPT = "**/_framework/blazor.server.js"
CDN = re.compile(r"https://cdn\.jsdelivr\.net/")
#: 只匹配 ECharts 脚本本身（不连图标字体 CSS 一起扣住）
ECHARTS = re.compile(r"/echarts@[\d.]+/dist/echarts\.min\.js")


def init_script(*, slow: bool = False, timings: "dict | None" = None) -> str:
    """每个新文档在页面脚本之前执行：记录 <html data-ft-boot> 的状态走向，可选放慢交接、覆盖 boot.js 的时间常量。"""
    timing_js = f"window.FT_BOOT_TIMINGS = {json.dumps(timings)};" if timings else ""
    slow_js = (
        f"style.textContent = ':root {{ --ft-dur-sheet: {400 * SLOW}ms !important; --ft-dur-fade: {180 * SLOW}ms !important; }}';"
        "document.documentElement.appendChild(style);"
        if slow
        else ""
    )
    return f"""
(() => {{
  {timing_js}
  window.__boot = {{ states: [], fadeDuration: null }};
  const install = () => {{
    const root = document.documentElement;
    if (!root) return false;
    const style = document.createElement('style');
    {slow_js}
    const log = () => {{
      const state = root.getAttribute('data-ft-boot');
      const states = window.__boot.states;
      if (!state || states[states.length - 1] === state) return;
      states.push(state);
      if (state === 'handoff-fade') {{
        const boot = document.getElementById('ft-boot');
        if (boot) window.__boot.fadeDuration = getComputedStyle(boot).transitionDuration;
      }}
    }};
    new MutationObserver(log).observe(root, {{ attributes: true, attributeFilter: ['data-ft-boot'] }});
    log();
    return true;
  }};
  if (!install()) {{
    const mo = new MutationObserver(() => {{ if (install()) mo.disconnect(); }});
    mo.observe(document, {{ childList: true }});
  }}
}})();
"""


def hold_blazor(page) -> list:
    """扣住 blazor.server.js：页面停在加载页，Blazor 迟迟不来。返回被扣住的请求，release_blazor 放行。"""
    held: list = []
    page.route(BLAZOR_SCRIPT, lambda route: held.append(route))
    return held


def release_blazor(page, held: list) -> None:
    # 先放行扣住的请求，再撤路由（先撤会把还没处理的请求一并丢掉）
    for route in held:
        route.continue_()
    held.clear()
    page.unroute(BLAZOR_SCRIPT)


def fail_first_blazor(page) -> None:
    """第一次请求 blazor.server.js 直接失败（模拟网络错误），之后的请求正常放行——点「重新加载」之后能成功。

    不扣住请求：被扣住又永远不处理的路由，会在上下文关闭时让 Playwright 打印一串 CancelledError。
    """
    state = {"failed": False}

    def handler(route) -> None:
        if state["failed"]:
            route.continue_()
            return
        state["failed"] = True
        route.abort("connectionfailed")

    page.route(BLAZOR_SCRIPT, handler)


def boot_state(page):
    return page.evaluate("() => (window.FtBoot ? window.FtBoot.state : null)")


def boot_states(page) -> "list[str]":
    return page.evaluate("() => window.__boot.states")


def wait_handoff(page) -> str:
    page.wait_for_function(
        "() => window.FtBoot && window.FtBoot.state.startsWith('handoff')", timeout=TIMEOUT_MS, polling=10
    )
    return boot_state(page)


def wait_done(page, timeout: int = TIMEOUT_MS) -> None:
    page.wait_for_function("() => window.FtBoot && window.FtBoot.state === 'done'", timeout=timeout)


#: 等加载页里所有过渡（CSSTransition，一定有限时长）结束，在同一个微任务里量终点：此时样式已是终值，节点要再过 60 ms 才被移除。
#: 不能「等所有动画」：加载页上有 30 s 的自毁动画与无限循环的转圈。放慢倍数不够、节点已经没了时返回 null。
MEASURE_AFTER_TRANSITIONS = """async (spec) => {
    const boot = document.getElementById('ft-boot');
    if (!boot) return null;
    const transitions = document.getAnimations()
        .filter((a) => a instanceof CSSTransition && a.effect && boot.contains(a.effect.target));
    await Promise.all(transitions.map((a) => a.finished.catch(() => {})));
    if (!document.getElementById('ft-boot')) return null;
    const center = (element) => {
        const rect = element.getBoundingClientRect();
        return [rect.left + rect.width / 2, rect.top + rect.height / 2];
    };
    const out = { transitions: transitions.length };
    if (spec.logo) {
        out.ours = center(boot.querySelector('.ft-boot__logo'));
        out.real = center(document.querySelector(spec.logo));
    }
    if (spec.hero) {
        out.curtain = boot.querySelector('.ft-boot__curtain').getBoundingClientRect().height;
        out.hero = document.querySelector(spec.hero).getBoundingClientRect().bottom;
    }
    if (spec.tile) {
        out.ourTile = getComputedStyle(boot.querySelector('.ft-boot__logo')).backgroundColor;
        out.realTile = getComputedStyle(document.querySelector(spec.tile)).backgroundColor;
    }
    return out;
}"""


# ---------------------------------------------------------------- A 首帧与慢启动

def scenario_slow_start(run: TestRun, s) -> None:
    print("\n== A 首帧与慢启动 ==")
    page = s.new_page()
    page.add_init_script(init_script(timings={"spinnerMs": 1500, "watchdogMs": 60000}))
    held = hold_blazor(page)
    page.goto(f"{base.BASE_URL}/", wait_until="commit")
    page.wait_for_selector("#ft-boot", state="attached")

    run.check("A1 首帧是加载页：状态 booting", boot_state(page) == "booting", str(boot_state(page)))
    run.check("A1 快启动不出现转圈（还没到门槛）", page.evaluate("() => !document.getElementById('ft-boot').hasAttribute('data-slow')"))
    run.check("A1 自带 Logo（内联 SVG）、名称与标语，不依赖网络 / Blazor", page.evaluate(
        """() => {
            const boot = document.getElementById('ft-boot');
            return !!boot.querySelector('.ft-boot__logo svg path')
                && boot.querySelector('.ft-boot__name')?.textContent.trim() === '记账本'
                && boot.querySelector('.ft-boot__slogan')?.textContent.trim().length > 0;
        }"""))
    run.check("A1 压在应用上面：屏幕中心的命中元素在加载页里", page.evaluate(
        "() => !!document.elementFromPoint(innerWidth / 2, innerHeight / 2)?.closest('#ft-boot')"))
    run.check("A1 声明状态栏底色 data-statusbar-bg=dark（图标从第一帧起就是白色）", page.evaluate(
        "() => document.getElementById('ft-boot').getAttribute('data-statusbar-bg') === 'dark'"))
    run.check("A1 读屏：role=status「记账本，正在加载」", page.evaluate(
        "() => document.querySelector('#ft-boot [role=status]')?.textContent.includes('正在加载')"))

    base.wait_attr(page, "#ft-boot", "data-slow", "true", timeout=20_000)
    run.check("A2 超过门槛后出现转圈：12 根刻度", page.evaluate(
        "() => document.querySelectorAll('#ft-boot .ft-boot__spinner i').length") == 12)
    page.wait_for_function(
        "() => getComputedStyle(document.querySelector('#ft-boot .ft-boot__foot')).opacity === '1'", timeout=5000)
    run.check("A2 转圈旁的说明「正在加载…」可见", page.evaluate(
        "() => document.querySelector('#ft-boot .ft-boot__foot')?.textContent.includes('正在加载')"))

    release_blazor(page, held)
    wait_done(page)
    run.check("A3 Blazor 一到，加载页收走（节点已移除）", page.evaluate("() => !document.getElementById('ft-boot')"))
    run.check("A3 露出的是登录页", page.locator("[data-testid='login-page']").count() == 1)
    page.close()


# ---------------------------------------------------------------- B 启动失败

def scenario_failure(run: TestRun, s) -> None:
    print("\n== B 启动失败 ==")
    page = s.new_page()
    page.add_init_script(init_script(timings={"watchdogMs": 1500}))
    held = hold_blazor(page)
    page.goto(f"{base.BASE_URL}/", wait_until="commit")
    page.get_by_test_id("boot-failed").wait_for(state="visible", timeout=20_000)

    run.check("B1 看门狗超时后进入 failed", boot_state(page) == "failed", str(boot_state(page)))
    run.check("B1 是 alertdialog，标题「启动失败」", page.evaluate(
        """() => {
            const card = document.querySelector('[data-testid=boot-failed] [role=alertdialog]');
            return !!card && card.querySelector('.ft-alert__title')?.textContent.trim() === '启动失败';
        }"""))
    run.check("B1 只有一个按钮，文案「重新加载」", page.locator("[data-testid='boot-failed'] button").count() == 1
              and page.get_by_test_id("boot-reload").inner_text().strip() == "重新加载")
    run.check("B1 转圈已撤掉（失败态不再是等待）", page.evaluate(
        "() => !document.getElementById('ft-boot').hasAttribute('data-slow')"))
    run.check("B1 焦点在容器上而不是按钮上（读屏读出 Alert，按钮不出焦点环）", page.evaluate(
        "() => document.activeElement?.getAttribute('data-testid') === 'boot-failed'"))

    release_blazor(page, held)
    wait_done(page)
    run.check("B2 外壳后来起来了：卡片自动撤掉、加载页收走", page.evaluate(
        "() => !document.querySelector('[data-testid=boot-failed]') && !document.getElementById('ft-boot')"))
    run.check("B2 露出的是登录页", page.locator("[data-testid='login-page']").count() == 1)
    page.close()

    # Blazor 脚本直接加载失败（网络错误）→ 失败卡片 → 点「重新加载」：整页重载（window 上的标记被清掉），重载之后的请求正常放行，应用正常启动
    page = s.new_page()
    page.add_init_script(init_script(timings={"watchdogMs": 1500}))
    fail_first_blazor(page)
    page.goto(f"{base.BASE_URL}/", wait_until="commit")
    page.get_by_test_id("boot-reload").wait_for(state="visible", timeout=20_000)
    page.evaluate("() => { window.__marker = 'before-reload'; }")
    with page.expect_navigation(wait_until="commit", timeout=TIMEOUT_MS):
        page.get_by_test_id("boot-reload").click()
    wait_done(page)
    run.check("B3 点「重新加载」整页重载，之后正常启动", page.evaluate("() => window.__marker === undefined"))
    page.close()


# ---------------------------------------------------------------- C 未登录 → 登录页

def scenario_login_handoff(run: TestRun, s) -> None:
    print("\n== C 未登录 → 登录页 ==")
    s.errors.clear()  # 前面的场景故意扣住了 Blazor 脚本，页面关闭时的中止请求不算缺陷
    page = s.new_page()
    page.add_init_script(init_script(slow=True))
    page.goto(f"{base.BASE_URL}/", wait_until="commit")
    state = wait_handoff(page)
    run.check("C1 未登录走 handoff-login", state == "handoff-login", state)

    info = page.evaluate(
        """() => {
            const boot = document.getElementById('ft-boot');
            return {
                dx: boot.style.getPropertyValue('--ft-boot-dx'),
                dy: boot.style.getPropertyValue('--ft-boot-dy'),
                realBrand: getComputedStyle(document.querySelector('.ft-login__brand')).visibility,
                panelRising: document.getAnimations().some((a) => a.animationName === 'ft-boot-panel'),
            };
        }""")
    run.check("C1 量出了 Logo 组的位移（--ft-boot-dx / dy）", info["dx"].endswith("px") and info["dy"].endswith("px"), str(info))
    run.check("C1 滑行期间真实品牌区先藏着（否则出现两个 Logo）", info["realBrand"] == "hidden", str(info))
    run.check("C1 登录面板在升起", info["panelRising"], str(info))

    end = page.evaluate(MEASURE_AFTER_TRANSITIONS, {"logo": ".ft-login__logo"})
    if end is None:
        run.check("C2 终点处加载页 Logo 与真实登录页 Logo 重合（< 0.5 px）", False, "量终点时加载页已被移除，放慢倍数不够？")
    else:
        dx, dy = abs(end["ours"][0] - end["real"][0]), abs(end["ours"][1] - end["real"][1])
        run.check("C2 终点处加载页 Logo 与真实登录页 Logo 重合（< 0.5 px）", end["transitions"] > 0 and dx < 0.5 and dy < 0.5,
                  f"dx={dx:.2f} dy={dy:.2f}（等了 {end['transitions']} 个过渡）")

    wait_done(page)
    run.check("C3 交接完成：加载页已移除、真实品牌区可见", page.evaluate(
        """() => !document.getElementById('ft-boot')
            && getComputedStyle(document.querySelector('.ft-login__brand')).visibility === 'visible'"""))
    run.check("C3 状态走向 booting → handoff-login → done", boot_states(page) == ["booting", "handoff-login", "done"],
              str(boot_states(page)))
    run.check_clean(s.errors, "C4 无页面异常 / 控制台错误 / 失败请求")
    page.close()


# ---------------------------------------------------------------- G 慢 CDN

def scenario_slow_cdn(run: TestRun, s) -> None:
    print("\n== G 慢 CDN 不挡启动 ==")
    page = s.new_page()
    # 把交接上限缩到 800 ms，免得测试等太久；CDN（图标字体 CSS / ECharts）请求一律扣住不返回
    page.add_init_script(init_script(timings={"capMs": 800}))
    held: list = []
    page.route(CDN, lambda route: held.append(route))
    page.goto(f"{base.BASE_URL}/", wait_until="commit")
    wait_done(page, 20_000)
    state = page.evaluate(
        "() => ({ settled: window.FtBoot.assetsSettled, login: !!document.querySelector('[data-testid=login-page]') })")
    run.check("G1 CDN 一直没回来，加载页到上限就交出去了（登录页已就位）", state["login"], str(state))
    run.check("G1 此时 CDN 资源确实还没尘埃落定（证明不是等到了才交）", state["settled"] is False, str(state))
    for route in held:
        route.abort()
    page.unroute(CDN)
    page.close()


# ---------------------------------------------------------------- D 已登录 → 首页

def scenario_home_handoff(run: TestRun, s) -> None:
    print("\n== D 已登录 → 首页 ==")
    # 调用方已在同一上下文里登录（共享 localStorage），这里是冷启动
    s.errors.clear()
    page = s.new_page()
    page.add_init_script(init_script(slow=True))
    page.goto(f"{base.BASE_URL}/", wait_until="commit")
    state = wait_handoff(page)
    run.check("D1 已登录走 handoff-home", state == "handoff-home", state)

    # 页面栈的 Tab 根页面刚挂上时有 0.985 → 1 的入场缩放，先让它走完再量真实 Hero 的底边（boot.js 量的是缩放结束后的布局高度）
    page.evaluate(
        "() => document.querySelectorAll('.p-page-container__item').forEach((el) => el.getAnimations().forEach((a) => a.finish()))")
    hero = page.evaluate(
        """() => ({
            target: parseFloat(document.getElementById('ft-boot').style.getPropertyValue('--ft-boot-hero-h')),
            real: document.querySelector('.ft-home .ft-hero').getBoundingClientRect().bottom,
        })""")
    run.check("D1 幕布收拢的目标高度 = 真实 Hero 的高度（< 0.6 px）", abs(hero["target"] - hero["real"]) < 0.6, str(hero))

    end = page.evaluate(MEASURE_AFTER_TRANSITIONS, {"hero": ".ft-home .ft-hero"})
    if end is None:
        run.check("D2 收拢到位后幕布高度 = Hero 高度（< 0.6 px）", False, "量终点时加载页已被移除，放慢倍数不够？")
    else:
        run.check("D2 收拢到位后幕布高度 = Hero 高度（< 0.6 px）",
                  end["transitions"] > 0 and abs(end["curtain"] - end["hero"]) < 0.6,
                  f"幕布 {end['curtain']:.2f} / Hero {end['hero']:.2f}（等了 {end['transitions']} 个过渡）")

    wait_done(page)
    run.check("D3 交接完成：加载页已移除、首页就位", page.evaluate("() => !document.getElementById('ft-boot')")
              and page.get_by_test_id("home-page").count() == 1)
    run.check("D3 首页根节点声明了 data-ft-ready=home，加载中的 aria-busy 已去掉", page.evaluate(
        """() => {
            const home = document.querySelector('[data-testid=home-page]');
            return home.getAttribute('data-ft-ready') === 'home' && !home.hasAttribute('aria-busy');
        }"""))
    run.check("D3 状态走向 booting → handoff-home → done", boot_states(page) == ["booting", "handoff-home", "done"],
              str(boot_states(page)))
    run.check_clean(s.errors, "D4 无页面异常 / 控制台错误 / 失败请求")
    page.close()


# ---------------------------------------------------------------- E 深链接

def scenario_deep_link(run: TestRun, s) -> None:
    print("\n== E 深链接 ==")
    # 会话已登录（在上下文里）；深链接进账单 Tab：启动路径不是 / 与 /login，没有首屏可等
    s.errors.clear()
    page = s.new_page()
    page.add_init_script(init_script())
    page.goto(f"{base.BASE_URL}/bills", wait_until="commit")
    wait_done(page)
    states = boot_states(page)
    run.check("E1 深链接整体淡出交接，不收拢也不位移", "handoff-fade" in states and "handoff-home" not in states
              and "handoff-login" not in states and states[-1] == "done", str(states))
    run.check("E1 落在账单 Tab", page.get_by_test_id("tab-bar").get_attribute("data-active-tab") == "bills")
    run.check_clean(s.errors, "E2 无页面异常 / 控制台错误 / 失败请求")
    page.close()


# ---------------------------------------------------------------- H 图表等 ECharts

#: 图表实例数：统计页里 ECharts 已初始化的容器个数（ECharts 脚本还没到时为 -1）
CHART_INSTANCES_JS = """() => {
    if (typeof window.echarts === 'undefined') return -1;
    const page = document.querySelector('[data-testid=stats-page]');
    if (!page) return 0;
    return Array.from(page.querySelectorAll('div')).filter((d) => window.echarts.getInstanceByDom(d)).length;
}"""

#: 应用还活着：没有 Blazor 错误条、Tab 栏在、能切回首页再回来（电路废了这些都做不到）
APP_ALIVE_JS = """() => {
    const error = document.getElementById('blazor-error-ui');
    const tabBar = document.querySelector('[data-testid=tab-bar]');
    return (!error || getComputedStyle(error).display === 'none') && !!tabBar && tabBar.getBoundingClientRect().height > 0;
}"""


def app_alive(page) -> bool:
    if not page.evaluate(APP_ALIVE_JS):
        return False
    base.open_tab(page, "首页")
    page.get_by_test_id("home-page").wait_for(state="visible", timeout=10_000)
    base.open_tab(page, "统计")
    page.get_by_test_id("stats-page").wait_for(state="visible", timeout=10_000)
    return True


def scenario_charts_wait_for_echarts(run: TestRun, s, info) -> None:
    print("\n== H 图表等 ECharts ==")
    # 有账单，统计页的图表面板才会渲染图表（没有数据是空状态，不渲染 ECharts）
    db = base.LocalDb()
    user_id = db.user_id(info.phone)
    ledger = base.unique_name("图表账本")
    db.seed_ledger(user_id, ledger)
    base.create_bill(info, ledger=ledger, amount=88, db=db)

    # H1 ECharts 迟迟不来：进统计 Tab，图表位置是骨架，应用照常；脚本一到图表自己出现
    s.errors.clear()
    page = s.new_page()
    held: list = []
    page.route(ECHARTS, lambda route: held.append(route))
    page.goto(f"{base.BASE_URL}/", wait_until="commit")
    wait_done(page)
    run.check("H1 加载页收走时 ECharts 还没到（CDN 资源不挡启动）", page.evaluate("() => typeof window.echarts === 'undefined'"))
    base.open_tab(page, "统计")
    page.locator("[data-testid=chart-pending]").first.wait_for(state="visible", timeout=15_000)
    pending = page.locator("[data-testid=chart-pending]").count()
    run.check("H1 统计页的三张图（分类排行 / 支出日历 / 累计收支）都在等待态，是与图表同高的骨架", pending == 3, str(pending))
    run.check("H1 等待期间应用还活着（没有错误条、Tab 栏在、能切页）", app_alive(page))
    for route in held:
        route.continue_()
    held.clear()
    page.unroute(ECHARTS)
    page.wait_for_function(f"() => ({CHART_INSTANCES_JS})() >= 3", timeout=20_000)
    run.check("H1 脚本到了之后图表自己出现（无需重新进页面），等待态骨架消失", page.evaluate(
        "() => document.querySelectorAll('[data-testid=chart-pending]').length === 0"))
    page.close()

    # H2 ECharts 加载失败（离线 / CDN 不可达）：给说明，应用照常可用
    s.errors.clear()
    page = s.new_page()
    page.route(ECHARTS, lambda route: route.abort("connectionfailed"))
    page.goto(f"{base.BASE_URL}/", wait_until="commit")
    wait_done(page)
    base.open_tab(page, "统计")
    page.locator("[data-testid=chart-unavailable]").first.wait_for(state="visible", timeout=15_000)
    unavailable = page.locator("[data-testid=chart-unavailable]")
    run.check("H2 三张图都显示「图表组件未能加载」", unavailable.count() == 3
              and all("图表组件未能加载" in text for text in unavailable.all_inner_texts()), str(unavailable.all_inner_texts()))
    run.check("H2 图表之外的页面照常可用（没有错误条、Tab 栏在、能切页）", app_alive(page))
    run.check_clean(s.errors, "H3 无页面异常 / 控制台错误（CDN 不可达的请求失败已忽略）")
    page.close()


# ---------------------------------------------------------------- F 减弱动态效果

def scenario_reduced_motion(run: TestRun) -> None:
    print("\n== F 减弱动态效果 ==")
    with session(reduced_motion=True) as s:
        page = s.page
        page.add_init_script(init_script())
        page.goto(f"{base.BASE_URL}/", wait_until="commit")
        wait_done(page)
        states = boot_states(page)
        run.check("F1 未登录也只是淡出（不位移、不升起面板）", "handoff-fade" in states and "handoff-login" not in states, str(states))
        duration = page.evaluate("() => window.__boot.fadeDuration")
        run.check("F2 淡出时长 = --ft-dur-reduced（100 ms）", duration == "0.1s", str(duration))
        run.check_clean(s.errors, "F3 无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------- I 材质降级：加载页的方块不变色

#: 等加载页上「渐变淡入 / 名称标语升起」两组入场动画跑完再读：渐变还在淡入时，方块背后的颜色在变，读到的像素没有可比性
SETTLE_JS = """async () => {
    const animations = document.getAnimations()
        .filter((a) => a instanceof CSSAnimation && /^ft-boot-(fade-in|rise)/.test(a.animationName));
    await Promise.all(animations.map((a) => a.finished.catch(() => {})));
    await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
}"""

#: 方块的底色、--ft-splash-tile 令牌的取值（经计算样式归一成 rgb），以及方块里不被钱包图形压住的一点（图形占中间 50 px，方块 96 px）
TILE_JS = """() => {
    const probe = document.createElement('div');
    probe.style.background = 'var(--ft-splash-tile)';
    document.body.appendChild(probe);
    const splashTile = getComputedStyle(probe).backgroundColor;
    probe.remove();
    const logo = document.querySelector('#ft-boot .ft-boot__logo');
    const rect = logo.getBoundingClientRect();
    return { background: getComputedStyle(logo).backgroundColor, splashTile,
             x: rect.left + rect.width / 2 - 38, y: rect.top + rect.height / 2 };
}"""

#: 两个像素颜色每个通道最多差多少仍算「没有色跳」。常态的玻璃与启动画面的实色实测只差 2；旧实现换成令牌里的 #6A42F2 会差 22
TILE_PIXEL_TOLERANCE = 8


def tile_snapshot(page) -> dict:
    """加载页方块此刻的样子：底色、启动画面实色令牌的取值、方块里一点的实际像素（截图取 4×4 的平均，CSS 像素尺度）。"""
    page.evaluate(SETTLE_JS)
    info = page.evaluate(TILE_JS)
    png = page.screenshot(clip={"x": info["x"] - 2, "y": info["y"] - 2, "width": 4, "height": 4}, scale="css")
    image = Image.open(io.BytesIO(png)).convert("RGB")
    pixels = [image.getpixel((i, j)) for i in range(image.width) for j in range(image.height)]
    info["pixel"] = tuple(round(sum(p[k] for p in pixels) / len(pixels)) for k in range(3))
    return info


def pixel_gap(a, b) -> int:
    return max(abs(x - y) for x, y in zip(a, b))


def open_booting(s):
    """开一页并停在「Blazor 迟迟不来」的加载页上（不出转圈、不触发看门狗）。返回 (page, 被扣住的请求)。"""
    page = s.new_page()
    page.add_init_script(init_script(timings={"spinnerMs": 60_000, "watchdogMs": 60_000}))
    held = hold_blazor(page)
    page.goto(f"{base.BASE_URL}/", wait_until="commit")
    page.wait_for_selector("#ft-boot", state="attached")
    return page, held


def scenario_degraded_materials(run: TestRun) -> None:
    print("\n== I 材质降级：加载页的方块不变色 ==")
    # I1 常态 + 加载页还在时根开关翻转（宿主判定为低端设备：UiRuntime 在 Blazor 启动之后才写 data-ft-solid）
    with session() as s:
        page, held = open_booting(s)
        glass = tile_snapshot(page)
        run.check("I1 常态：方块是玻璃（半透明底），与启动画面的实色不是同一个计算值——下面比的是实际像素",
                  glass["background"] != glass["splashTile"], f"{glass['background']} / {glass['splashTile']}")
        page.evaluate("() => document.documentElement.setAttribute('data-ft-solid', 'true')")
        solid = tile_snapshot(page)
        run.check("I1 根开关 data-ft-solid 翻转后：方块底色 = 启动画面的实色（--ft-splash-tile）",
                  solid["background"] == solid["splashTile"], f"{solid['background']} / {solid['splashTile']}")
        gap = pixel_gap(glass["pixel"], solid["pixel"])
        run.check(f"I1 翻转前后方块的实际像素没有色跳（每个通道差 ≤ {TILE_PIXEL_TOLERANCE}）",
                  gap <= TILE_PIXEL_TOLERANCE, f"{glass['pixel']} → {solid['pixel']}（差 {gap}）")
        release_blazor(page, held)
        wait_done(page)
        run.check_clean(s.errors, "I1 无页面异常 / 控制台错误 / 失败请求")

    # I2 系统「降低透明度」（媒体特性）：从第一帧起方块就是启动画面的实色
    with session(reduced_transparency=True) as s:
        page, held = open_booting(s)
        reduced = tile_snapshot(page)
        run.check("I2 系统降低透明度：方块底色 = 启动画面的实色（--ft-splash-tile）",
                  reduced["background"] == reduced["splashTile"], f"{reduced['background']} / {reduced['splashTile']}")
        gap = pixel_gap(glass["pixel"], reduced["pixel"])
        run.check(f"I2 降级的方块与常态的方块是同一个颜色（每个通道差 ≤ {TILE_PIXEL_TOLERANCE}）",
                  gap <= TILE_PIXEL_TOLERANCE, f"{glass['pixel']} → {reduced['pixel']}（差 {gap}）")
        release_blazor(page, held)
        wait_done(page)
        run.check_clean(s.errors, "I2 无页面异常 / 控制台错误 / 失败请求")

    # I3 登录交接：降级下登录页里的真方块是令牌里的不透明深色玻璃（#6A42F2），加载页的方块要随滑行一路换成它——
    #    否则滑行结束、加载页收走真方块露出来的那一刻是一次瞬时的色跳。走真实链路：宿主判定（E2E 假实现）→ UiRuntime → 根开关
    #    （假实现的状态是整个服务进程共享的，用完一定还原）
    base.set_rendering(True)
    try:
        with session() as s:
            page = s.new_page()
            page.add_init_script(init_script(slow=True))
            page.goto(f"{base.BASE_URL}/", wait_until="commit")
            state = wait_handoff(page)
            solid_attr = page.evaluate("() => document.documentElement.getAttribute('data-ft-solid')")
            run.check("I3 宿主判定降级时，未登录走 handoff-login，根开关已经翻转",
                      state == "handoff-login" and solid_attr == "true", f"{state} / data-ft-solid={solid_attr}")
            end = page.evaluate(MEASURE_AFTER_TRANSITIONS, {"tile": ".ft-login__logo"})
            if end is None:
                run.check("I3 滑行结束时加载页的方块已经是真方块的颜色", False, "量终点时加载页已被移除，放慢倍数不够？")
            else:
                run.check("I3 滑行结束时加载页的方块已经是真方块的颜色（加载页收走时没有色跳）",
                          end["transitions"] > 0 and end["ourTile"] == end["realTile"],
                          f"加载页 {end['ourTile']} / 真方块 {end['realTile']}（等了 {end['transitions']} 个过渡）")
            wait_done(page)
            run.check_clean(s.errors, "I3 无页面异常 / 控制台错误 / 失败请求")
    finally:
        base.set_rendering(False)


def main() -> int:
    require_server()
    run = TestRun("boot")
    with session() as s:
        scenario_slow_start(run, s)
        scenario_failure(run, s)
        scenario_login_handoff(run, s)
        scenario_slow_cdn(run, s)
        # 以下场景都要已登录：在这个上下文里登录一次（共享 localStorage），之后每个场景各开新页冷启动
        info = base.login(s.page)
        scenario_home_handoff(run, s)
        scenario_deep_link(run, s)
        scenario_charts_wait_for_echarts(run, s, info)
    scenario_reduced_motion(run)
    scenario_degraded_materials(run)
    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
