# -*- coding: utf-8 -*-
"""
E2E 测试：Tab 滑块切换效果（iOS UI 重构 · 04 号 ticket）。

只覆盖网页层——弹簧的真实手感、触感反馈（iOS 轻触 / Android 时钟刻度）、中端 Android 上的流畅度，见 ticket 04 的人工验收清单。

  A. 静止：高光落在选中项正下方，没有残留的内联位置 / 拉伸
  B. 切换动画：弹簧过冲回弹、水平拉伸（最大 ≈1.7×）、垂直压缩 ≤ 7%，最终精确落在目标 Tab（逐帧采样高光的包围盒）
  C. 按下反馈：高光放大 ≈1.08 并加深投影、被按的图标缩到 0.9；松手 / 拖出后全部还原，拖出不切换
  D. 图标与过渡参数：线框 → 实心；时长取自令牌（图标 160ms、按下 120ms）
  E. 页面交叉淡入：目标页 0.985 → 1 并淡入，180ms；代码触发的跳转（首页「最近账单 → 全部」入口）高光同样跟过去
  F. 连续点按：以最后一次为准、无残留状态；Blazor 迟到的旧确认不会让高光折返；跳转没发生时高光回到真实 Tab
  G. 滚动收缩：向下滚动超过 24px 收缩成「当前页胶囊」，上滚 / 点按展开；「记一笔」始终可见可点，按下缩放 0.92
     （在造了 30 笔账单的「账单」Tab 上滚——它的内容远超一屏；13 号重做后「我的」只比一屏多一点，不再适合当长页面）
  H. 减弱动态效果：没有弹簧 / 拉伸 / 抬起，高光只做 100ms 淡入，页面只做 100ms 淡入；页面保活不变

可观察属性（已登记到 README）：[data-testid="tab-bar-thumb"]（+ data-state）、[data-testid="tab-bar-capsule"]、
data-collapsed / data-pressed / data-pressing、高光的包围盒（含拉伸）。
"""
import sys

from e2e_base import (
    TAB_ADD_SELECTOR,
    TAB_BAR_SELECTOR,
    TAB_CAPSULE_SELECTOR,
    TAB_KEYS,
    TAB_THUMB_SELECTOR,
    LocalDb,
    TestRun,
    active_root,
    active_tab,
    days_ago,
    give_home_a_ledger,
    login,
    open_tab,
    pull_refresh,
    require_server,
    scroll_root_state,
    scroll_root_to,
    session,
    shot,
    start_thumb_sampler,
    stop_thumb_sampler,
    tab_box,
    tab_button,
    tab_collapsed,
    thumb_box,
    unique_name,
    wait_animations_settled,
    wait_attr,
    wait_scroll_root_stable,
    wait_state,
    wait_tab_bar,
    wait_thumb_at,
    wait_thumb_idle,
    wait_url,
)

# 设计参数（与 tokens.css 一致；这里写死是为了让测试在令牌被改坏时失败）
STRETCH_MAX = 1.7  # 水平拉伸最大倍数
SQUASH_MAX = 0.07  # 垂直压缩最大比例
LIFT_SCALE = 1.08  # 按下时高光放大
LIFT_ICON_SCALE = 0.9  # 按下时被按图标缩小
FAB_PRESS_SCALE = 0.92  # 按下「记一笔」缩放
COLLAPSE_THRESHOLD = 24  # 向下滚动超过多少像素收缩
CAPSULE_WIDTH = 124  # 收缩后 Tab 栏（胶囊）宽度

NAV_SELECTOR = f"{TAB_BAR_SELECTOR} nav"
FAB_SELECTOR = TAB_ADD_SELECTOR


def approx(actual, expected, tolerance: float = 1.0) -> bool:
    return actual is not None and abs(actual - expected) <= tolerance


def glyph_selector(key: str) -> str:
    return f'{TAB_BAR_SELECTOR} [data-tab="{key}"] .ft-tab__glyph'


def computed(page, selector: str, *props: str) -> list:
    """读取元素的计算样式属性。"""
    return page.evaluate(
        "([sel, props]) => { const cs = getComputedStyle(document.querySelector(sel)); return props.map((p) => cs[p]); }",
        [selector, list(props)],
    )


def inline_state(page) -> dict:
    """高光上脚本写的内联位置 / 拉伸。脚本接管后 translate 始终保留（位置的唯一来源），scale（拉伸 / 抬起）静止后应当被清掉。"""
    return page.evaluate(
        "(sel) => { const s = document.querySelector(sel).style; return { translate: s.translate, scale: s.scale }; }",
        TAB_THUMB_SELECTOR,
    )


def rest_inline(page) -> bool:
    """静止时的内联状态是否干净：有 translate（脚本持有位置），没有 scale（无拉伸 / 抬起残留）。"""
    state = inline_state(page)
    return state["translate"] != "" and state["scale"] == ""


def wait_thumb_released(page) -> None:
    """等待脚本清掉高光的内联 scale（拉伸 / 抬起）。"""
    wait_state(
        page,
        "(sel) => document.querySelector(sel).style.scale === ''",
        arg=TAB_THUMB_SELECTOR,
    )


def wrap_flags(page) -> dict:
    return page.evaluate(
        """(sel) => {
            const wrap = document.querySelector(sel);
            return {
                collapsed: wrap.getAttribute('data-collapsed'),
                pressed: wrap.getAttribute('data-pressed'),
                pressing: wrap.querySelectorAll('[data-pressing]').length,
            };
        }""",
        TAB_BAR_SELECTOR,
    )


LOADED_BILLS_JS = """() => {
  const list = document.querySelector('[data-testid="bills-list"]');
  return !!list && parseInt(list.getAttribute('data-loaded-count') || '0', 10) >= 20;
}"""


def seed_scrollable_bills(page, db: LocalDb, user_id: str, count: int = 30) -> None:
    """给用户造足够多的账单并切到「账单」Tab，让它的内容远超一屏——滚动收缩 / 页面保活需要一个能滚很远的 Tab
    （13 号重做后「我的」只比一屏多一点，不再适合当「长页面」）。

    「账单」Tab 常驻 DOM：本场景里它可能早就建好并读过一次（那时还没有账单），所以造完数据后下拉刷新
    重新读一遍（等首次加载结束再拉——骨架还在时页面还在忙），再等第一页 20 笔渲染出来。"""
    ledger = db.seed_ledger(user_id, unique_name("滚动账本"))
    # 账单 Tab 默认只看「本月」：账单日期只铺在本月已过去的日子里（月初就都挤在今天附近），不会被默认视图挡在外面
    spread = max(1, min(20, days_ago(0).day))
    for i in range(count):
        db.seed_bill(user_id, amount=10 + i, ledger_id=ledger, when=days_ago(i % spread), note=unique_name("滚动账单"))
    open_tab(page, "账单")
    active_root(page).get_by_test_id("bills-ptr").wait_for(state="attached")
    wait_state(page, "() => !document.querySelector('[data-testid=\"bills-skeleton\"]')")
    pull_refresh(page, "bills-ptr")
    wait_state(page, LOADED_BILLS_JS)


def nav_width(page) -> float:
    return page.evaluate("(sel) => document.querySelector(sel).getBoundingClientRect().width", NAV_SELECTOR)


def glass_layers(page) -> int:
    return page.evaluate(
        """() => [...document.querySelectorAll('*')].filter((el) => {
            const cs = getComputedStyle(el);
            const filter = cs.backdropFilter && cs.backdropFilter !== 'none' ? cs.backdropFilter : cs.webkitBackdropFilter;
            if (!filter || filter === 'none' || cs.visibility === 'hidden') return false;
            const r = el.getBoundingClientRect();
            return r.width > 0 && r.height > 0;
        }).length"""
    )


def settle(page) -> None:
    """高光弹簧停稳、内联 scale 清掉、CSS 过渡 / 动画结束。"""
    wait_thumb_idle(page)
    wait_thumb_released(page)
    wait_animations_settled(page)


def tap_and_sample(page, title: str) -> list:
    """逐帧采样高光，同时点按某个 Tab，直到弹簧停稳在目标 Tab 下方；返回采样序列。"""
    target_cx = tab_box(page, title)["cx"]
    start_thumb_sampler(page)
    tab_button(page, title).click()
    wait_attr(page, TAB_BAR_SELECTOR, "data-active-tab", TAB_KEYS[title])
    wait_thumb_at(page, target_cx)
    return stop_thumb_sampler(page)


def move_stats(samples: list, start_cx: float, target_cx: float) -> dict:
    direction = 1 if target_cx > start_cx else -1
    started = next((s["t"] for s in samples if abs(s["cx"] - start_cx) > 0.5), None)
    settled = max((s["t"] for s in samples if abs(s["cx"] - target_cx) > 1.0), default=None)
    return {
        "frames": len(samples),
        "max_w": max(s["w"] for s in samples),
        "min_h": min(s["h"] for s in samples),
        "max_h": max(s["h"] for s in samples),
        "min_opacity": min(s["opacity"] for s in samples),
        "overshoot": max((s["cx"] - target_cx) * direction for s in samples),
        "duration": (settled - started) / 1000 if started is not None and settled is not None else None,
    }


# ----------------------------------------------------------------------
# A. 静止
# ----------------------------------------------------------------------


def check_rest(run: TestRun, page) -> dict:
    """A. 静止：高光落在选中项正下方，没有残留的内联位置 / 拉伸"""
    wait_tab_bar(page)
    settle(page)

    box, tab = thumb_box(page), tab_box(page, "首页")
    run.check(
        "静止：高光落在选中项（首页）正下方",
        approx(box["cx"], tab["cx"], 1),
        f"thumb.cx={box['cx']:.1f} tab.cx={tab['cx']:.1f}",
    )
    run.check(
        "静止：高光与选中项等宽等高，没有拉伸 / 抬起",
        approx(box["width"], tab["width"], 1.5) and 40 <= box["height"] <= 62,
        f"thumb={box['width']:.1f}×{box['height']:.1f} tab.width={tab['width']:.1f}",
    )
    run.check("静止：脚本持有位置（内联 translate），没有残留的内联 scale（拉伸 / 抬起已清掉）", rest_inline(page), str(inline_state(page)))
    state = page.get_attribute(TAB_THUMB_SELECTOR, "data-state")
    run.check("静止：高光 data-state = idle", state == "idle", str(state))
    flags = wrap_flags(page)
    run.check(
        "静止：Tab 栏展开、没有按住的按钮（data-collapsed=false，无 data-pressed / data-pressing）",
        flags == {"collapsed": "false", "pressed": None, "pressing": 0},
        str(flags),
    )
    return {"w": box["width"], "h": box["height"]}


# ----------------------------------------------------------------------
# B. 切换动画
# ----------------------------------------------------------------------


def check_motion(run: TestRun, page, rest: dict) -> None:
    """B. 切换动画：弹簧过冲回弹、水平拉伸、垂直压缩，最终精确落在目标 Tab"""
    for source, target in [("首页", "统计"), ("统计", "首页"), ("首页", "账单")]:
        settle(page)
        start_cx = thumb_box(page)["cx"]
        target_cx = tab_box(page, target)["cx"]
        distance = abs(target_cx - start_cx)

        samples = tap_and_sample(page, target)
        stats = move_stats(samples, start_cx, target_cx)
        label = f"{source}→{target}"
        detail = f"{stats}"

        ratio = stats["max_w"] / rest["w"]
        run.check(
            f"{label}：弹簧运动中高光被水平拉长（最宽 {ratio:.2f}× 静止宽度，期望 1.25×~1.9×）",
            1.25 <= ratio <= STRETCH_MAX * LIFT_SCALE + 0.1,
            detail,
        )
        run.check(
            f"{label}：垂直方向压缩不超过 7%（最矮 {stats['min_h'] / rest['h']:.3f}×）",
            stats["min_h"] >= rest["h"] * (1 - SQUASH_MAX) - 0.6,
            detail,
        )
        run.check(
            f"{label}：越过目标后轻微回弹（过冲 {stats['overshoot']:.1f}px，约占距离 {stats['overshoot'] / distance * 100:.0f}%）",
            0.03 * distance <= stats["overshoot"] <= 0.2 * distance,
            f"distance={distance:.1f} {detail}",
        )
        run.check(
            f"{label}：运动时长合理（{stats['duration'] if stats['duration'] is None else round(stats['duration'], 2)}s，期望 0.25~1.2s）",
            stats["duration"] is not None and 0.25 <= stats["duration"] <= 1.2,
            detail,
        )
        run.check(f"{label}：运动期间高光始终完全不透明（没有被误当成淡入）", stats["min_opacity"] >= 0.99, detail)

        settle(page)
        final = thumb_box(page)
        run.check(
            f"{label}：最终精确落在目标 Tab 正下方，恢复静止宽度",
            approx(final["cx"], target_cx, 1) and approx(final["width"], rest["w"], 0.5) and approx(final["height"], rest["h"], 0.5),
            f"final={final} target.cx={target_cx:.1f}",
        )
        run.check(
            f"{label}：静止后 data-state=idle，没有残留的拉伸 / 抬起",
            page.get_attribute(TAB_THUMB_SELECTOR, "data-state") == "idle" and rest_inline(page),
            str(inline_state(page)),
        )

    run.check("切换到「账单」后 data-active-tab 同步更新", active_tab(page) == "bills", str(active_tab(page)))
    shot(page, "tab-slider-after-move")


# ----------------------------------------------------------------------
# C. 按下反馈
# ----------------------------------------------------------------------


def check_press(run: TestRun, page, rest: dict) -> None:
    """C. 按下反馈：高光抬起、图标缩小，松手 / 拖出后还原"""
    open_tab(page, "首页")
    settle(page)
    shadow_idle = computed(page, TAB_THUMB_SELECTOR, "boxShadow")[0]
    target = tab_box(page, "统计")
    glyph = glyph_selector("statistics")

    page.mouse.move(target["cx"], (target["top"] + target["bottom"]) / 2)
    page.mouse.down()
    try:
        wait_attr(page, TAB_BAR_SELECTOR, "data-pressed", "true")
        wait_attr(page, f'{TAB_BAR_SELECTOR} [data-tab="statistics"]', "data-pressing", "true")
        # 抬起量按指数平滑逼近（约 120ms），等到位后再量
        page.wait_for_function(
            "([sel, w]) => document.querySelector(sel).getBoundingClientRect().width >= w",
            arg=[TAB_THUMB_SELECTOR, rest["w"] * (LIFT_SCALE - 0.0005)],
        )
        pressed = thumb_box(page)
        run.check(
            f"按下：高光放大约 {LIFT_SCALE}（宽 {pressed['width'] / rest['w']:.3f}× / 高 {pressed['height'] / rest['h']:.3f}×）",
            approx(pressed["width"] / rest["w"], LIFT_SCALE, 0.01) and approx(pressed["height"] / rest["h"], LIFT_SCALE, 0.01),
            str(pressed),
        )
        run.check("按下：高光仍在原来的位置（按住不触发切换）", approx(pressed["cx"], tab_box(page, "首页")["cx"], 1) and active_tab(page) == "home")

        wait_state(
            page,
            "([sel, idle]) => getComputedStyle(document.querySelector(sel)).boxShadow !== idle",
            arg=[TAB_THUMB_SELECTOR, shadow_idle],
        )
        shadow_pressed = computed(page, TAB_THUMB_SELECTOR, "boxShadow")[0]
        run.check("按下：高光投影加深（box-shadow 与静止时不同）", shadow_pressed != shadow_idle, f"{shadow_idle} → {shadow_pressed}")

        wait_state(page, "(sel) => getComputedStyle(document.querySelector(sel)).scale === '0.9'", arg=glyph)
        run.check(f"按下：被按住的图标缩到 {LIFT_ICON_SCALE}", computed(page, glyph, "scale")[0] == "0.9", str(computed(page, glyph, "scale")))
    finally:
        page.mouse.up()

    # 松手：点按生效，高光滑到统计，所有按下状态还原
    wait_attr(page, TAB_BAR_SELECTOR, "data-active-tab", "statistics")
    wait_attr(page, TAB_BAR_SELECTOR, "data-pressed", None)
    settle(page)
    flags = wrap_flags(page)
    after = thumb_box(page)
    run.check("松手：切换到「统计」，按下状态全部清除", flags["pressed"] is None and flags["pressing"] == 0 and active_tab(page) == "statistics", str(flags))
    run.check(
        "松手：高光还原到静止尺寸并落在「统计」下方，投影还原",
        approx(after["width"], rest["w"], 0.5) and approx(after["cx"], tab_box(page, "统计")["cx"], 1) and computed(page, TAB_THUMB_SELECTOR, "boxShadow")[0] == shadow_idle,
        str(after),
    )
    run.check("松手：图标还原（scale 不再是 0.9）", computed(page, glyph, "scale")[0] in ("none", "1"), str(computed(page, glyph, "scale")))

    # 拖出取消：在「账单」上按下，拖到 Tab 栏外松手——不切换，按下状态同样清除
    bills = tab_box(page, "账单")
    page.mouse.move(bills["cx"], (bills["top"] + bills["bottom"]) / 2)
    page.mouse.down()
    wait_attr(page, TAB_BAR_SELECTOR, "data-pressed", "true")
    page.mouse.move(195, 300)
    page.mouse.up()
    wait_attr(page, TAB_BAR_SELECTOR, "data-pressed", None)
    settle(page)
    flags = wrap_flags(page)
    run.check(
        "拖出 Tab 栏后松手：不切换（仍在「统计」），按下状态清除，高光还原",
        active_tab(page) == "statistics" and flags["pressed"] is None and flags["pressing"] == 0 and approx(thumb_box(page)["width"], rest["w"], 0.5),
        f"{flags} {thumb_box(page)}",
    )


# ----------------------------------------------------------------------
# D. 图标与过渡参数
# ----------------------------------------------------------------------

ICON_OPACITY_JS = """() => {
  const wrap = document.querySelector('[data-testid="tab-bar"]');
  const read = (key, cls) => parseFloat(getComputedStyle(wrap.querySelector('[data-tab="' + key + '"] ' + cls)).opacity);
  return Object.fromEntries(['home', 'bills', 'statistics', 'profile'].map((key) => [key, { outline: read(key, '.ft-tab__o'), filled: read(key, '.ft-tab__f') }]));
}"""


def check_icons(run: TestRun, page) -> None:
    """D. 图标线框 → 实心；过渡时长取自令牌"""
    open_tab(page, "统计")
    settle(page)
    icons = page.evaluate(ICON_OPACITY_JS)
    run.check(
        "选中的「统计」是实心图标，其余三个是线框",
        approx(icons["statistics"]["filled"], 1, 0.02)
        and approx(icons["statistics"]["outline"], 0, 0.02)
        and all(approx(icons[k]["outline"], 1, 0.02) and approx(icons[k]["filled"], 0, 0.02) for k in ("home", "bills", "profile")),
        str(icons),
    )

    icon_duration = computed(page, f'{TAB_BAR_SELECTOR} [data-tab="statistics"] .ft-tab__f', "transitionDuration")[0]
    run.check("图标线框 ↔ 实心的交叉淡入约 160ms", icon_duration == "0.16s", icon_duration)
    press_duration = computed(page, glyph_selector("statistics"), "transitionDuration")[0]
    run.check("按下时图标缩放约 120ms", press_duration == "0.12s", press_duration)
    thumb_duration = computed(page, TAB_THUMB_SELECTOR, "transitionDuration")[0]
    run.check("按下时高光投影变化约 120ms", thumb_duration.split(",")[0].strip() == "0.12s", thumb_duration)
    fab_duration = computed(page, FAB_SELECTOR, "transitionDuration")[0]
    run.check("「记一笔」按下缩放约 120ms", fab_duration == "0.12s", fab_duration)


# ----------------------------------------------------------------------
# E. 页面交叉淡入 / 代码触发的跳转
# ----------------------------------------------------------------------

# 在 animationstart 的瞬间把动画暂停在 0 读取起始帧样式，再恢复播放——不依赖轮询时机
PAGE_ENTER_CAPTURE_JS = """() => {
  window.__ftPageEnter = [];
  if (window.__ftPageEnterHandler) document.removeEventListener('animationstart', window.__ftPageEnterHandler, true);
  window.__ftPageEnterHandler = (e) => {
    if (e.animationName !== 'ft-page-enter' && e.animationName !== 'ft-page-fade') return;
    const el = e.target;
    const rec = {
      name: e.animationName,
      duration: getComputedStyle(el).animationDuration,
      root: el.classList.contains('p-page-container__item'),
    };
    const anim = el.getAnimations().find((a) => a.animationName === e.animationName);
    if (anim) {
      anim.pause();
      anim.currentTime = 0;
      const start = getComputedStyle(el);
      rec.startOpacity = start.opacity;
      rec.startTransform = start.transform;
      anim.play();
    }
    window.__ftPageEnter.push(rec);
  };
  document.addEventListener('animationstart', window.__ftPageEnterHandler, true);
}"""


def capture_page_enter(page) -> None:
    page.evaluate(PAGE_ENTER_CAPTURE_JS)


def read_page_enter(page) -> list:
    return page.evaluate("() => window.__ftPageEnter.filter((r) => r.root)")


def check_page_enter(run: TestRun, page, rest: dict) -> None:
    """E. 页面交叉淡入；代码触发的跳转高光同样跟过去"""
    open_tab(page, "统计")
    settle(page)
    capture_page_enter(page)
    tab_button(page, "账单").click()
    wait_attr(page, TAB_BAR_SELECTOR, "data-active-tab", "bills")
    wait_state(page, "() => window.__ftPageEnter.some((r) => r.root)")
    records = read_page_enter(page)
    first = records[0]
    run.check("切换 Tab：目标页播放页面进入动画（ft-page-enter）", first["name"] == "ft-page-enter", str(first))
    run.check("页面进入动画时长约 180ms", first["duration"] == "0.18s", str(first))
    run.check(
        "页面进入动画起点：透明且轻微缩小（opacity 0，scale 0.985）",
        first.get("startOpacity") == "0" and first.get("startTransform") == "matrix(0.985, 0, 0, 0.985, 0, 0)",
        str(first),
    )
    settle(page)
    end_opacity, end_transform = computed(page, ".p-page-container__item:not(.d-none)", "opacity", "transform")
    run.check("动画结束后目标页完全不透明、没有残留缩放", end_opacity == "1" and end_transform == "none", f"{end_opacity} {end_transform}")
    run.check("页面栈里始终只有一个可见的根页面", page.locator(".p-page-container__item:not(.d-none)").count() == 1)

    # 代码触发的跳转：首页「最近账单」的「全部」入口（Navigation.NavigateTo，不是点按 Tab 栏），高光同样滑过去
    # （新用户的首页只有「新建账本」引导，先给用户造一个账本，首页才有「最近账单」区域）
    open_tab(page, "首页")
    give_home_a_ledger(page)
    settle(page)
    start_cx = thumb_box(page)["cx"]
    target_cx = tab_box(page, "账单")["cx"]
    start_thumb_sampler(page)
    active_root(page).get_by_test_id("home-recent-all").click()
    wait_attr(page, TAB_BAR_SELECTOR, "data-active-tab", "bills")
    wait_thumb_at(page, target_cx)
    samples = stop_thumb_sampler(page)
    stats = move_stats(samples, start_cx, target_cx)
    settle(page)
    run.check(
        "代码触发的跳转（首页「最近账单 → 全部」入口）：高光同样带弹簧拉伸滑到「账单」",
        stats["max_w"] / rest["w"] >= 1.25 and approx(thumb_box(page)["cx"], target_cx, 1),
        str(stats),
    )


# ----------------------------------------------------------------------
# F. 连续点按
# ----------------------------------------------------------------------

# 在页面里造一个 Tab 栏副本，接上同一份脚本，由测试直接扮演「迟到的 Blazor 确认」——Blazor Server 往返很快，
# 真实连点很难稳定复现「更早那次点按的确认在下一次点按之后才到」，副本里可以精确控制次序与间隔。
HARNESS_JS = """async (script) => {
  const mod = await import(script);
  const real = document.querySelector('[data-testid="tab-bar"]');
  const clone = real.cloneNode(true);
  clone.style.visibility = 'hidden';
  document.body.appendChild(clone); // 排在真实 Tab 栏之后，document.querySelector 仍先命中真实的
  const thumb = clone.querySelector('[data-testid="tab-bar-thumb"]');
  const tab = (key) => clone.querySelector('[data-tab="' + key + '"]');
  const centreOf = (el) => { const r = el.getBoundingClientRect(); return r.left + r.width / 2; };
  const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
  const frames = (ms) => new Promise((resolve) => {
    const out = [];
    const t0 = performance.now();
    const tick = () => {
      out.push(centreOf(thumb));
      if (performance.now() - t0 >= ms) resolve(out); else requestAnimationFrame(tick);
    };
    tick();
  });
  const nav = clone.querySelector('[role="tablist"]');
  const clean = () => thumb.style.scale === ''; // 静止后没有拉伸 / 抬起的残留
  // 扮演 Blazor：每次渲染都会把激活序号写到 nav 的 --ft-tab-x（脚本接管后它只是回退），渲染之后再调用 sync
  const confirm = (index) => {
    nav.style.setProperty('--ft-tab-x', String(index));
    ctl.sync(index);
  };
  const ctl = mod.attachTabBar(clone, 1); // 初始在「账单」
  const result = {};
  try {
    await frames(50);
    result.startCx = centreOf(thumb);

    // 1. 迟到的旧确认：先点「首页」（向左），约 3 帧后改点「我的」（向右），之后 Blazor 才送来「首页」的确认
    tab('home').click();
    await frames(50);
    tab('profile').click();
    await frames(80);
    const beforeStale = centreOf(thumb);
    confirm(0); // 迟到的、更早那次点按的确认
    const after = await frames(160);
    result.stale = { beforeStale, afterStale: after[after.length - 1], profileCx: centreOf(tab('profile')) };
    confirm(3); // 最后一次点按的确认
    await wait(1100);
    result.stale.finalCx = centreOf(thumb);
    result.stale.clean = clean();

    // 2. 跳转没有发生：点了「统计」却一直没有确认，超时后高光回到 Blazor 报告的真实 Tab（我的）
    tab('statistics').click();
    await wait(300);
    result.timeout = { midCx: centreOf(thumb), statisticsCx: centreOf(tab('statistics')) };
    await wait(3400);
    result.timeout.finalCx = centreOf(thumb);
    result.timeout.profileCx = centreOf(tab('profile'));
    result.timeout.clean = clean();

    // 3. 代码触发的跳转（没有点按）：Blazor 先渲染（--ft-tab-x 变了）、sync 随后才到——高光不能先瞬移到新位置，
    //    要从旧位置自己滑过去。渲染之后立刻采样一帧：位置必须还在旧位置（我的）
    nav.style.setProperty('--ft-tab-x', '0');
    await frames(32);
    result.programmatic = { beforeSyncCx: centreOf(thumb), profileCx: centreOf(tab('profile')) };
    ctl.sync(0);
    await wait(1100);
    result.programmatic.finalCx = centreOf(thumb);
    result.programmatic.homeCx = centreOf(tab('home'));
    result.programmatic.clean = clean();
  } finally {
    ctl.dispose();
    clone.remove();
  }
  return result;
}"""


def check_rapid_taps(run: TestRun, page, rest: dict) -> None:
    """F. 连续点按：以最后一次为准、无残留状态"""
    open_tab(page, "首页")
    settle(page)

    # 真实连点：账单 → 统计 → 我的 → 首页 → 统计 → 账单（最后一次是「账单」）
    sequence = ["账单", "统计", "我的", "首页", "统计", "账单"]
    bills_cx = tab_box(page, "账单")["cx"]
    start_thumb_sampler(page)
    for title in sequence:
        tab_button(page, title).click()
    # 中间几次点按的 Blazor 导航也会让 data-active-tab 短暂等于 bills，所以以高光停稳在「账单」下方为准，再确认地址
    wait_thumb_at(page, bills_cx)
    wait_attr(page, TAB_BAR_SELECTOR, "data-active-tab", "bills")
    wait_url(page, r"^/bills$")
    samples = stop_thumb_sampler(page)
    settle(page)

    final = thumb_box(page)
    flags = wrap_flags(page)
    selected = [t for t in ("首页", "账单", "统计", "我的") if tab_button(page, t).get_attribute("aria-selected") == "true"]
    run.check(f"快速连点 {' → '.join(sequence)}：最终激活的是最后点按的「账单」", active_tab(page) == "bills" and selected == ["账单"], f"active={active_tab(page)} selected={selected}")
    run.check("快速连点后高光停在「账单」正下方，恢复静止宽度", approx(final["cx"], tab_box(page, "账单")["cx"], 1) and approx(final["width"], rest["w"], 0.5), str(final))
    run.check(
        "快速连点后无残留状态（无 data-pressed / data-pressing / 内联 scale，data-state=idle）",
        flags["pressed"] is None and flags["pressing"] == 0 and rest_inline(page) and page.get_attribute(TAB_THUMB_SELECTOR, "data-state") == "idle",
        f"{flags} {inline_state(page)}",
    )
    wait_url(page, r"^/bills$")
    run.check("快速连点后页面停在「账单」，页面栈里只有一个可见根页面", page.evaluate("() => location.pathname") == "/bills" and page.locator(".p-page-container__item:not(.d-none)").count() == 1)
    run.check("连点过程中高光被多次打断重定向，仍然平滑（采样帧里最大拉伸不超过 1.9×）", max(s["w"] for s in samples) <= rest["w"] * 1.9, f"{max(s['w'] for s in samples):.1f}")

    # 迟到确认 / 超时回退 / 代码跳转：Tab 栏副本 + 同一份脚本，精确控制 sync 的次序与间隔
    result = page.evaluate(HARNESS_JS, "/_content/FinanceTracker.Web/js/tabBar.js")
    stale = result["stale"]
    tab_w = rest["w"]
    run.check(
        "迟到的旧确认被忽略：更早那次点按（首页）的确认到达后，高光继续朝最后一次点按（我的）前进，不折返",
        stale["afterStale"] > stale["beforeStale"] + tab_w * 0.3,
        f"{stale['beforeStale']:.1f} → {stale['afterStale']:.1f}",
    )
    run.check(
        "最后一次点按的确认到达后，高光精确落在「我的」，没有残留拉伸",
        approx(stale["finalCx"], stale["profileCx"], 1) and stale["clean"],
        str(stale),
    )
    timeout = result["timeout"]
    run.check(
        "点按「统计」后一直没有确认：先乐观地滑向「统计」",
        abs(timeout["midCx"] - timeout["statisticsCx"]) < abs(timeout["profileCx"] - timeout["statisticsCx"]),
        str(timeout),
    )
    run.check(
        "超过 2.5s 仍没有确认（跳转没发生）：高光回到 Blazor 报告的真实 Tab（我的）",
        approx(timeout["finalCx"], timeout["profileCx"], 1) and timeout["clean"],
        str(timeout),
    )
    programmatic = result["programmatic"]
    run.check(
        "代码触发的跳转：Blazor 先渲染（--ft-tab-x 已变）、sync 后到时，高光不会先瞬移到新位置",
        approx(programmatic["beforeSyncCx"], programmatic["profileCx"], 1),
        str(programmatic),
    )
    run.check(
        "没有点按、直接收到新的激活序号：高光自己滑到「首页」，没有残留拉伸",
        approx(programmatic["finalCx"], programmatic["homeCx"], 1) and programmatic["clean"],
        str(programmatic),
    )
    run.check("Tab 栏副本已移除，真实 Tab 栏不受影响", page.locator(TAB_BAR_SELECTOR).count() == 1 and active_tab(page) == "bills")


# ----------------------------------------------------------------------
# G. 滚动收缩
# ----------------------------------------------------------------------


def capsule_state(page) -> dict:
    return page.evaluate(
        """([capsuleSel, tabSel]) => {
            const capsule = document.querySelector(capsuleSel);
            const cs = getComputedStyle(capsule);
            return {
                opacity: parseFloat(cs.opacity),
                inert: capsule.inert,
                text: capsule.innerText.trim(),
                label: capsule.getAttribute('aria-label'),
                tabsInert: [...document.querySelectorAll(tabSel)].map((t) => t.inert),
            };
        }""",
        [TAB_CAPSULE_SELECTOR, f"{TAB_BAR_SELECTOR} [role='tab']"],
    )


def wait_expanded(page) -> None:
    wait_attr(page, TAB_BAR_SELECTOR, "data-collapsed", "false")
    wait_state(page, "(sel) => document.querySelector(sel).getBoundingClientRect().width > 250", arg=NAV_SELECTOR)
    wait_animations_settled(page)


def wait_collapsed(page) -> None:
    wait_attr(page, TAB_BAR_SELECTOR, "data-collapsed", "true")
    wait_state(page, "(sel) => document.querySelector(sel).getBoundingClientRect().width < 140", arg=NAV_SELECTOR)
    wait_animations_settled(page)


def check_collapse(run: TestRun, page, db: LocalDb, user_id: str) -> None:
    """G. 滚动收缩成「当前页胶囊」；「记一笔」始终可见可点（在造了 30 笔账单的「账单」Tab 上滚——它的内容远超一屏）"""
    seed_scrollable_bills(page, db, user_id)
    wait_scroll_root_stable(page)
    settle(page)
    state = scroll_root_state(page)
    run.check("（前置）「账单」页内容远超一屏，能滚动", state and state["max"] > 400, str(state))

    expanded_width = nav_width(page)
    fab_before = page.locator(FAB_SELECTOR).bounding_box()

    # 阈值：滚动没超过 24 不收缩（负向断言，给脚本一个事件周期 + 收缩过渡的时间）
    scroll_root_to(page, COLLAPSE_THRESHOLD - 4)
    page.wait_for_timeout(350)
    run.check(f"向下滚动未超过 {COLLAPSE_THRESHOLD}（滚了 {COLLAPSE_THRESHOLD - 4}）：Tab 栏不收缩", not tab_collapsed(page))

    # 超过阈值：收缩成胶囊
    scroll_root_to(page, 200)
    wait_collapsed(page)
    capsule = capsule_state(page)
    run.check(
        f"向下滚动超过 {COLLAPSE_THRESHOLD}：Tab 栏收缩成胶囊（宽 {nav_width(page):.0f}，期望 ≈{CAPSULE_WIDTH}）",
        approx(nav_width(page), CAPSULE_WIDTH, 1.5) and nav_width(page) < expanded_width / 2,
        f"expanded={expanded_width:.0f}",
    )
    run.check(
        "胶囊显示当前页（实心图标 + 「账单」），可聚焦；四个 Tab 不可聚焦 / 不可点",
        capsule["opacity"] >= 0.99 and not capsule["inert"] and capsule["text"] == "账单" and all(capsule["tabsInert"]) and "账单" in (capsule["label"] or ""),
        str(capsule),
    )
    run.check("收缩后高光隐藏", computed(page, TAB_THUMB_SELECTOR, "opacity")[0] == "0", str(computed(page, TAB_THUMB_SELECTOR, "opacity")))
    run.check("收缩态下每屏玻璃层仍不超过 3 层", glass_layers(page) <= 3, f"{glass_layers(page)} 层")
    fab = page.locator(FAB_SELECTOR)
    fab_now = fab.bounding_box()
    run.check(
        "收缩时「记一笔」圆钮仍在原位、可见",
        fab.is_visible() and approx(fab_now["x"], fab_before["x"], 1) and approx(fab_now["y"], fab_before["y"], 1) and approx(fab_now["width"], fab_before["width"], 1),
        f"{fab_before} → {fab_now}",
    )
    shot(page, "tab-slider-collapsed")

    # 向上滚动：展开
    scroll_root_to(page, 120)
    wait_expanded(page)
    capsule = capsule_state(page)
    run.check(
        "向上滚动：Tab 栏展开，高光与四个 Tab 恢复，胶囊不可点",
        approx(nav_width(page), expanded_width, 1.5) and computed(page, TAB_THUMB_SELECTOR, "opacity")[0] == "1" and not any(capsule["tabsInert"]) and capsule["inert"],
        f"{nav_width(page):.0f} {capsule}",
    )

    # 再收缩，点按胶囊展开（不切换页面）
    scroll_root_to(page, 260)
    wait_collapsed(page)
    page.locator(TAB_CAPSULE_SELECTOR).click()
    wait_expanded(page)
    run.check("点按胶囊：展开，仍停在「账单」（不切换页面）", not tab_collapsed(page) and active_tab(page) == "bills" and page.evaluate("() => location.pathname") == "/bills")

    # 收缩态下「记一笔」可点：按下缩放 0.92，松手升起记一笔 Sheet（地址不变）
    scroll_root_to(page, 400)
    wait_collapsed(page)
    box = page.locator(FAB_SELECTOR).bounding_box()
    page.mouse.move(box["x"] + box["width"] / 2, box["y"] + box["height"] / 2)
    page.mouse.down()
    try:
        wait_attr(page, FAB_SELECTOR, "data-pressing", "true")
        wait_state(page, "(sel) => getComputedStyle(document.querySelector(sel)).transform === 'matrix(0.92, 0, 0, 0.92, 0, 0)'", arg=FAB_SELECTOR)
        run.check(f"按下「记一笔」：缩放 {FAB_PRESS_SCALE}", computed(page, FAB_SELECTOR, "transform")[0] == "matrix(0.92, 0, 0, 0.92, 0, 0)", computed(page, FAB_SELECTOR, "transform")[0])
    finally:
        page.mouse.up()
    sheet = page.get_by_test_id("addbill-sheet")
    sheet.wait_for(state="visible")
    run.check("松手：升起记一笔 Sheet（Tab 栏收缩态下「记一笔」照常可点），地址不变", page.evaluate("() => location.pathname") == "/bills" and active_tab(page) == "bills")
    sheet.get_by_test_id("sheet-close").click()
    sheet.wait_for(state="detached")
    wait_tab_bar(page)

    # 回到顶部：展开
    scroll_root_to(page, 0)
    wait_expanded(page)
    run.check("滚回顶部：Tab 栏展开", not tab_collapsed(page) and approx(nav_width(page), expanded_width, 1.5), f"{nav_width(page):.0f}")


# ----------------------------------------------------------------------
# H. 减弱动态效果
# ----------------------------------------------------------------------

ANIMATE_RECORDER_JS = """() => {
  window.__ftAnimateCalls = [];
  if (!window.__ftNativeAnimate) window.__ftNativeAnimate = Element.prototype.animate;
  Element.prototype.animate = function (keyframes, options) {
    window.__ftAnimateCalls.push({
      testid: this.getAttribute('data-testid'),
      keyframes: JSON.stringify(keyframes),
      duration: typeof options === 'number' ? options : options && options.duration,
    });
    return window.__ftNativeAnimate.apply(this, arguments);
  };
}"""


def check_reduced_motion(run: TestRun) -> None:
    """H. 减弱动态效果：没有弹簧 / 拉伸 / 抬起，只剩约 100ms 淡入；页面保活不变"""
    with session(reduced_motion=True) as s:
        page = s.page
        info = login(page)
        db = LocalDb()
        user_id = db.user_id(info.phone)
        wait_tab_bar(page)
        settle(page)
        rest = thumb_box(page)
        rest = {"w": rest["width"], "h": rest["height"], "cx": rest["cx"]}
        page.evaluate(ANIMATE_RECORDER_JS)
        capture_page_enter(page)

        target_cx = tab_box(page, "统计")["cx"]
        samples = tap_and_sample(page, "统计")
        stats = move_stats(samples, rest["cx"], target_cx)
        middle = [x for x in samples if abs(x["cx"] - rest["cx"]) > 1 and abs(x["cx"] - target_cx) > 1]
        run.check("减弱动态效果：全程没有水平拉伸", stats["max_w"] <= rest["w"] + 0.5, str(stats))
        run.check("减弱动态效果：没有垂直压缩 / 抬起", abs(stats["min_h"] - rest["h"]) <= 0.5 and stats["max_h"] <= rest["h"] + 0.5, str(stats))
        run.check("减弱动态效果：没有弹簧，高光直接就位（没有中间帧、没有过冲）", not middle and stats["overshoot"] <= 1, f"{len(middle)} 个中间帧 {stats}")
        calls = page.evaluate("() => window.__ftAnimateCalls")
        fade = [c for c in calls if c["testid"] == "tab-bar-thumb"]
        run.check(
            "减弱动态效果：高光只做约 100ms 淡入（0 → 1）",
            len(fade) >= 1 and fade[0]["duration"] == 100 and '"opacity"' in fade[0]["keyframes"],
            str(calls),
        )
        settle(page)
        run.check("减弱动态效果：高光最终落在「统计」下方，没有残留 scale", approx(thumb_box(page)["cx"], target_cx, 1) and rest_inline(page), f"{thumb_box(page)} {inline_state(page)}")

        wait_state(page, "() => window.__ftPageEnter.some((r) => r.root)")
        entered = read_page_enter(page)[0]
        run.check(
            "减弱动态效果：页面只做约 100ms 淡入（ft-page-fade，没有缩放）",
            entered["name"] == "ft-page-fade" and entered["duration"] == "0.1s" and entered.get("startOpacity") == "0" and entered.get("startTransform") == "none",
            str(entered),
        )

        # 按住：不抬起、不缩小图标
        bills = tab_box(page, "账单")
        page.mouse.move(bills["cx"], (bills["top"] + bills["bottom"]) / 2)
        page.mouse.down()
        wait_attr(page, TAB_BAR_SELECTOR, "data-pressed", "true")
        page.wait_for_timeout(250)  # 负向断言：正常模式下 120ms 就会抬起，多等一倍
        pressed = thumb_box(page)
        glyph_scale = computed(page, glyph_selector("bills"), "scale")[0]
        page.mouse.move(195, 300)
        page.mouse.up()
        wait_attr(page, TAB_BAR_SELECTOR, "data-pressed", None)
        run.check("减弱动态效果：按住时高光不放大", approx(pressed["width"], rest["w"], 0.5) and approx(pressed["height"], rest["h"], 0.5), str(pressed))
        run.check("减弱动态效果：按住时图标不缩小", glyph_scale in ("none", "1"), glyph_scale)

        # 收缩 / 展开功能保留，只是没有宽度动画（在造了 30 笔账单的「账单」Tab 上滚）
        seed_scrollable_bills(page, db, user_id)
        wait_scroll_root_stable(page)
        scroll_root_to(page, 200)
        wait_attr(page, TAB_BAR_SELECTOR, "data-collapsed", "true")
        wait_state(page, "(sel) => document.querySelector(sel).getBoundingClientRect().width < 140", arg=NAV_SELECTOR)
        run.check("减弱动态效果：滚动收缩仍然可用（宽度直接变成胶囊）", approx(nav_width(page), CAPSULE_WIDTH, 1.5), f"{nav_width(page):.0f}")
        scroll_root_to(page, 100)
        wait_attr(page, TAB_BAR_SELECTOR, "data-collapsed", "false")
        wait_state(page, "(sel) => document.querySelector(sel).getBoundingClientRect().width > 250", arg=NAV_SELECTOR)

        # 页面保活行为不变：切走再切回，滚动位置保持
        scroll_root_to(page, 300)
        before = scroll_root_state(page)
        open_tab(page, "首页")
        open_tab(page, "账单")
        wait_scroll_root_stable(page)
        after = scroll_root_state(page)
        run.check("减弱动态效果：页面保活不变（切走再切回「账单」，滚动位置保持）", before and after and approx(after["top"], before["top"], 2), f"{before} → {after}")

        run.check_clean(s.errors, "减弱动态效果会话无页面异常 / 控制台错误 / 失败请求")


def main() -> int:
    require_server()
    run = TestRun("tab-slider")

    db = LocalDb()
    with session() as s:
        page = s.page
        info = login(page)

        rest = check_rest(run, page)
        check_motion(run, page, rest)
        check_press(run, page, rest)
        check_icons(run, page)
        check_page_enter(run, page, rest)
        check_rapid_taps(run, page, rest)
        check_collapse(run, page, db, db.user_id(info.phone))

        run.check_clean(s.errors)

    check_reduced_motion(run)

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
