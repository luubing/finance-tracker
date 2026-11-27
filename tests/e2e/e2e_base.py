# -*- coding: utf-8 -*-
"""
E2E 共享基座（手机视口）。

所有 E2E 脚本统一经本模块启动浏览器：手机视口（390×844）+ 触摸 + 3 倍像素比，
并共享以下能力，避免每个脚本各写一份：

  - 启动 / 视口 / 降级媒体特性模拟：session(reduced_motion=..., reduced_transparency=...)
  - Blazor 电路就绪等待（含启动加载页收走、CDN 资源尘埃落定）、唯一手机号登录：open_app() / login() / wait_boot_done() / wait_assets_settled()
  - 手势（真实触摸事件，经 CDP 注入）：tap / long_press / swipe / swipe_by / pull_down
  - 基于状态的等待（不用固定 sleep）：wait_state / wait_attr / wait_animations_settled
  - 悬浮玻璃 Tab 栏（03 号起）：tab_bar / tab_button / open_tab / wait_tab_bar / active_tab / active_root / scroll_root_state / scroll_root_to
  - 控制台 / 页面异常 / 失败请求采集（带白名单）：ErrorCollector
  - 直接写本地 SQLite 的造数据能力（含历史无账本账单、待确认账单）：LocalDb
  - 可选截图（E2E_SCREENSHOTS=1，默认关闭，目录已被 .gitignore 忽略）：shot()
  - 断言与退出码：TestRun（0 通过 / 1 失败 / 2 环境未就绪）

约定见 tests/e2e/README.md（含「文档化可观察属性」登记表）。
"""
from __future__ import annotations

import contextlib
import json
import os
import random
import re
import sqlite3
import sys
import time
import urllib.error
import urllib.request
import uuid
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from decimal import Decimal
from pathlib import Path
from typing import Iterable, Iterator, Optional, Union

from playwright.sync_api import Browser, BrowserContext, Locator, Page, Playwright, sync_playwright
from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

# Windows 控制台默认 GBK，中文输出会报错，统一切换 UTF-8
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8")

# ======================================================================
# 配置
# ======================================================================

#: Web.Server 的 http 启动配置地址（见 server/FinanceTracker.Web.Server/Properties/launchSettings.json）
BASE_URL = os.environ.get("E2E_BASE_URL", "http://localhost:5999").rstrip("/")

#: 单次等待的默认超时（毫秒）
TIMEOUT_MS = int(os.environ.get("E2E_TIMEOUT_MS", "30000"))

#: 有头模式（E2E_HEADED=1），方便人工观察
HEADED = os.environ.get("E2E_HEADED", "").lower() in ("1", "true", "yes")

#: 截图总开关（E2E_SCREENSHOTS=1）。默认关闭，不拖慢常规回归
SCREENSHOTS_ENABLED = os.environ.get("E2E_SCREENSHOTS", "").lower() in ("1", "true", "yes")

E2E_DIR = Path(__file__).resolve().parent

#: 截图输出目录（已被 .gitignore 忽略）
SCREENSHOT_DIR = Path(os.environ.get("E2E_SCREENSHOT_DIR", E2E_DIR / "screenshots"))

#: 页面快照等调试产物目录（已被 .gitignore 忽略）
ARTIFACT_DIR = E2E_DIR / "artifacts"

#: 本地 SQLite 路径：Web.Server 默认库，可用 E2E_DB_PATH 覆盖
DB_PATH = os.environ.get("E2E_DB_PATH") or os.path.expandvars(r"%LOCALAPPDATA%\finance_tracker.db")

#: 手机视口：iPhone 15 级别逻辑分辨率，像素比 3
PHONE_VIEWPORT = {"width": 390, "height": 844}
PHONE_DEVICE_SCALE = 3

#: 默认忽略的网络/控制台噪声（离线环境加载不到 CDN 不算应用缺陷；favicon 无关业务）
DEFAULT_IGNORED_PATTERNS = [
    r"cdn\.jsdelivr\.net",
    r"fonts\.(googleapis|gstatic)\.com",
    r"/favicon\.ico",
]

Target = Union[str, Locator, "tuple[float, float]"]

# ======================================================================
# 环境就绪检查
# ======================================================================


def require_server(timeout_s: float = 5.0) -> None:
    """确认 Web.Server 在运行；未运行时给出启动指引并以退出码 2 结束（区别于测试失败的 1）。"""
    try:
        urllib.request.urlopen(f"{BASE_URL}/", timeout=timeout_s)
    except urllib.error.HTTPError:
        return  # 服务有响应（哪怕是 4xx/5xx）就认为在运行，具体错误交给测试断言
    except Exception as exc:  # noqa: BLE001 - 任何连接异常都表示未就绪
        print(f"[ENV] 无法访问 {BASE_URL}（{exc}）")
        print("[ENV] 请先启动 Web.Server：powershell tests/e2e/server.ps1 -Action start")
        print("[ENV] 或：dotnet run --project server/FinanceTracker.Web.Server --launch-profile http")
        sys.exit(2)


# ======================================================================
# 唯一数据
# ======================================================================


def unique_suffix() -> str:
    """毫秒时间戳后 6 位 + 2 位随机，用于命名账本/备注，避免与历史 E2E 数据冲突（库未隔离）。"""
    return f"{int(time.time() * 1000) % 1_000_000:06d}{random.randint(0, 99):02d}"


def unique_name(prefix: str) -> str:
    return f"{prefix}{unique_suffix()}"


_USED_PHONES: set[str] = set()


def unique_phone() -> str:
    """生成符合 1[3-9]\\d{9} 的手机号（每个测试一个全新用户，互不污染；同一进程内不重复）。"""
    while True:
        phone = f"13{random.randint(0, 9)}{random.randint(0, 99_999_999):08d}"
        if phone not in _USED_PHONES:
            _USED_PHONES.add(phone)
            return phone


# ======================================================================
# 错误采集
# ======================================================================


class ErrorCollector:
    """采集页面异常、控制台 error、失败请求、HTTP >= 400，支持忽略白名单。"""

    def __init__(self, ignore: Optional[Iterable[str]] = None) -> None:
        patterns = list(DEFAULT_IGNORED_PATTERNS)
        if ignore:
            patterns.extend(ignore)
        self._ignore = [re.compile(p) for p in patterns]
        self.page_errors: list[str] = []
        self.console_errors: list[str] = []
        self.failed_requests: list[str] = []
        self.bad_responses: list[str] = []

    def _record(self, bucket: list[str], text: str) -> None:
        if any(p.search(text) for p in self._ignore):
            return
        bucket.append(text)

    def attach(self, page: Page) -> None:
        def on_console(msg) -> None:
            if msg.type == "error":
                url = (msg.location or {}).get("url", "")
                self._record(self.console_errors, f"{msg.text} @ {url}".strip())

        page.on("console", on_console)
        page.on("pageerror", lambda e: self._record(self.page_errors, str(e)))
        page.on(
            "requestfailed",
            lambda r: self._record(self.failed_requests, f"{r.method} {r.url} -> {r.failure}"),
        )
        page.on(
            "response",
            lambda r: self._record(self.bad_responses, f"{r.status} {r.url}") if r.status >= 400 else None,
        )

    def clear(self) -> None:
        for bucket in (self.page_errors, self.console_errors, self.failed_requests, self.bad_responses):
            bucket.clear()

    @property
    def count(self) -> int:
        return len(self.page_errors) + len(self.console_errors) + len(self.failed_requests) + len(self.bad_responses)

    def is_clean(self) -> bool:
        return self.count == 0

    def summary(self) -> str:
        sections = (
            ("PAGE ERRORS", self.page_errors),
            ("CONSOLE ERRORS", self.console_errors),
            ("FAILED REQUESTS", self.failed_requests),
            ("HTTP >= 400", self.bad_responses),
        )
        return "\n".join(f"=== {title} ===\n" + ("\n".join(items) if items else "(none)") for title, items in sections)


# ======================================================================
# 会话（浏览器 / 上下文 / 页面）
# ======================================================================


@dataclass
class E2ESession:
    playwright: Playwright
    browser: Browser
    context: BrowserContext
    page: Page
    errors: ErrorCollector
    options: dict = field(default_factory=dict)

    def new_page(self) -> Page:
        """同一上下文再开一页（共享登录状态），并挂上同一个错误采集器与媒体特性模拟。"""
        page = self.context.new_page()
        self.errors.attach(page)
        emulate_media(page, reduced_transparency=self.options.get("reduced_transparency", False))
        return page


def emulate_media(page: Page, *, reduced_transparency: bool = False) -> None:
    """通过 CDP 模拟 prefers-reduced-transparency（Playwright 原生不支持该特性）。"""
    if not reduced_transparency:
        return
    cdp = _cdp(page)
    cdp.send(
        "Emulation.setEmulatedMedia",
        {"features": [{"name": "prefers-reduced-transparency", "value": "reduce"}]},
    )


@contextlib.contextmanager
def session(
    *,
    reduced_motion: bool = False,
    reduced_transparency: bool = False,
    headless: Optional[bool] = None,
    viewport: Optional[dict] = None,
    ignore: Optional[Iterable[str]] = None,
    storage_state: Optional[Union[str, Path, dict]] = None,
) -> Iterator[E2ESession]:
    """启动手机视口浏览器会话。

    reduced_motion / reduced_transparency 用于验证「降级」分支（对应系统「减少动态效果 / 降低透明度」）。
    """
    options = {"reduced_motion": reduced_motion, "reduced_transparency": reduced_transparency}
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=(not HEADED) if headless is None else headless)
        context = browser.new_context(
            viewport=viewport or PHONE_VIEWPORT,
            screen=viewport or PHONE_VIEWPORT,
            device_scale_factor=PHONE_DEVICE_SCALE,
            is_mobile=True,
            has_touch=True,
            locale="zh-CN",
            reduced_motion="reduce" if reduced_motion else "no-preference",
            storage_state=storage_state,
        )
        context.set_default_timeout(TIMEOUT_MS)
        page = context.new_page()
        errors = ErrorCollector(ignore)
        errors.attach(page)
        emulate_media(page, reduced_transparency=reduced_transparency)
        try:
            yield E2ESession(p, browser, context, page, errors, options)
        finally:
            context.close()
            browser.close()


# ======================================================================
# 页面进入 / 登录
# ======================================================================

_CIRCUIT_READY_JS = """() => {
    // Blazor Server 预渲染的组件以 <!--Blazor:{...}--> 注释标记，电路连接并接管后标记被移除
    if (!window.Blazor) return false;
    const walker = document.createTreeWalker(document, NodeFilter.SHOW_COMMENT);
    while (walker.nextNode()) {
        if (walker.currentNode.nodeValue.startsWith('Blazor:')) return false;
    }
    return true;
}"""


_BOOT_DONE_JS = """() => {
    // 启动加载页（冷启动设计稿，boot.js）：<html data-ft-boot> 走到 done 才算收走（节点同时被移除）。
    // 没有这个属性 = 页面不带加载页（例如非应用页），视为已就绪。
    const state = document.documentElement.getAttribute('data-ft-boot');
    return state === null || state === 'done';
}"""

_ASSETS_SETTLED_JS = """() => !window.FtBoot || window.FtBoot.assetsSettled"""


def wait_boot_done(page: Page, timeout: int = TIMEOUT_MS) -> None:
    """等启动加载页收走。加载页是全屏的、压在应用上面，CDP 触摸会点在它身上，所以交互前必须先等它走。

    快启动约 0.4–0.6 s（交接动画）；停在 failed（看门狗超时）会等到超时失败，这正是该报的。
    """
    page.wait_for_function(_BOOT_DONE_JS, timeout=timeout)


def wait_assets_settled(page: Page, timeout: int = 15_000) -> None:
    """等 CDN 资源（图标字体 CSS、ECharts）都「加载完或失败」。

    这些资源改成页面 load 之后才注入（boot.js），不再挡首屏；需要判断 ECharts 在不在的脚本（has_echarts）要先等它尘埃落定，
    否则会把「还在下载」误判成「不可达」而悄悄跳过图表断言。超时不抛：CDN 一直不返回时与改造前一样，交给各脚本自己的跳过逻辑。
    """
    try:
        page.wait_for_function(_ASSETS_SETTLED_JS, timeout=timeout)
    except PlaywrightTimeoutError:
        pass


def wait_circuit_ready(page: Page, timeout: int = TIMEOUT_MS) -> None:
    """等待 Blazor 电路连接（预渲染标记消失），再等启动加载页收走、CDN 资源尘埃落定，之后页面才是可交互的。"""
    page.wait_for_function(_CIRCUIT_READY_JS, timeout=timeout)
    wait_boot_done(page, timeout)
    wait_assets_settled(page, min(timeout, 15_000))


def open_app(page: Page, path: str = "/", *, wait_circuit: bool = True) -> None:
    """进入应用内某路径（整页导航），并等待电路就绪。"""
    page.goto(f"{BASE_URL}{path}", wait_until="networkidle", timeout=TIMEOUT_MS)
    if wait_circuit:
        wait_circuit_ready(page)


@dataclass
class LoginInfo:
    phone: str
    user_id: str


def login(page: Page, phone: Optional[str] = None) -> LoginInfo:
    """用唯一手机号登录（无验证码，首次登录即注册），返回手机号与 localStorage 里的 userId。

    登录页结构变化时只需改这里，各脚本不用动。
    """
    phone = phone or unique_phone()
    open_app(page, "/")
    # 登录页（22 号）：手机号输入框 login-phone、主按钮 login-submit（按钮文案是「登录 / 注册」，不按文案找，改文案不用动各脚本）
    page.wait_for_selector("[data-testid='login-phone']", timeout=25_000)
    page.get_by_test_id("login-phone").fill(phone)
    page.get_by_test_id("login-submit").click()
    page.wait_for_function("() => !!localStorage.getItem('userId')", timeout=TIMEOUT_MS)
    page.wait_for_selector("[data-testid='login-phone']", state="detached", timeout=TIMEOUT_MS)
    # 登录完成 = 回到首页且应用壳（Tab 栏）就绪，之后的脚本可以直接交互
    wait_tab_bar(page)
    user_id = page.evaluate("() => localStorage.getItem('userId')")
    return LoginInfo(phone=phone, user_id=user_id)


# ======================================================================
# 手势（真实触摸事件）
# ======================================================================


def _cdp(page: Page):
    cdp = getattr(page, "_ft_cdp", None)
    if cdp is None:
        cdp = page.context.new_cdp_session(page)
        page._ft_cdp = cdp  # type: ignore[attr-defined]
    return cdp


def _touch(page: Page, event: str, points: "list[tuple[float, float]]") -> None:
    _cdp(page).send(
        "Input.dispatchTouchEvent",
        {
            "type": event,
            "touchPoints": [{"x": x, "y": y, "id": i} for i, (x, y) in enumerate(points)],
        },
    )


def center_of(page: Page, target: Target) -> "tuple[float, float]":
    """目标中心的视口坐标。target 可以是 CSS 选择器、Locator 或 (x, y)。"""
    if isinstance(target, tuple):
        return float(target[0]), float(target[1])
    locator = target if isinstance(target, Locator) else page.locator(target)
    locator.first.scroll_into_view_if_needed()
    box = locator.first.bounding_box()
    if box is None:
        raise RuntimeError(f"目标不可见，无法定位: {target!r}")
    return box["x"] + box["width"] / 2, box["y"] + box["height"] / 2


def tap(page: Page, target: Target) -> None:
    """触摸轻点（touchStart + touchEnd），会触发 pointer/click 事件。"""
    x, y = center_of(page, target)
    _touch(page, "touchStart", [(x, y)])
    time.sleep(0.04)
    _touch(page, "touchEnd", [])


def long_press(page: Page, target: Target, hold_ms: int = 650) -> None:
    """长按：按下后原地保持 hold_ms 再抬起（期间不产生 touchmove）。"""
    x, y = center_of(page, target)
    _touch(page, "touchStart", [(x, y)])
    time.sleep(hold_ms / 1000)
    _touch(page, "touchEnd", [])


def swipe(
    page: Page,
    start: "tuple[float, float]",
    end: "tuple[float, float]",
    *,
    steps: int = 12,
    duration_ms: int = 240,
    release_hold_ms: int = 0,
) -> None:
    """从 start 滑动到 end（线性插值的一串 touchMove）。

    release_hold_ms > 0 时在终点停留后再抬起（用于模拟「拖到位置后停住」，速度≈0）。
    """
    (x1, y1), (x2, y2) = start, end
    _touch(page, "touchStart", [(x1, y1)])
    pause = duration_ms / 1000 / max(steps, 1)
    for i in range(1, steps + 1):
        t = i / steps
        _touch(page, "touchMove", [(x1 + (x2 - x1) * t, y1 + (y2 - y1) * t)])
        time.sleep(pause)
    if release_hold_ms:
        time.sleep(release_hold_ms / 1000)
    _touch(page, "touchEnd", [])


def swipe_by(page: Page, target: Target, dx: float, dy: float, **kwargs) -> None:
    """以目标中心为起点，按位移 (dx, dy) 滑动。"""
    x, y = center_of(page, target)
    swipe(page, (x, y), (x + dx, y + dy), **kwargs)


def pull_down(page: Page, distance: float = 180, *, from_y: float = 120, x: Optional[float] = None, **kwargs) -> None:
    """从页面顶部区域向下拉（下拉刷新手势）。页面需已滚动到顶部。"""
    px = x if x is not None else PHONE_VIEWPORT["width"] / 2
    swipe(page, (px, from_y), (px, from_y + distance), **kwargs)


#: 布防：记录下拉刷新区是否真的进入过 refreshing（MutationObserver 盯着 data-state；refreshing 至少停留 500ms，不会漏）
_PTR_ARM_JS = """(id) => {
  const zone = document.querySelector(`[data-testid="${id}"]`);
  if (!zone) throw new Error('找不到下拉刷新区 ' + id);
  window.__ptrSeen = window.__ptrSeen || {};
  window.__ptrObs = window.__ptrObs || {};
  if (window.__ptrObs[id]) window.__ptrObs[id].disconnect();
  window.__ptrSeen[id] = zone.dataset.state === 'refreshing';
  window.__ptrObs[id] = new MutationObserver(() => { if (zone.dataset.state === 'refreshing') window.__ptrSeen[id] = true; });
  window.__ptrObs[id].observe(zone, { attributes: true, attributeFilter: ['data-state'] });
}"""

#: 脚本已接管且当前空闲：pullRefresh.js 的 attach() 最后才把 data-state 写成 idle——写之前的下拉没有任何反应（页面刚渲染完就拉会撞上）；
#: 上一次刷新还没收起（refreshing / settling）时也要等
_PTR_READY_JS = """(id) => {
  const zone = document.querySelector(`[data-testid="${id}"]`);
  return !!zone && zone.dataset.state === 'idle';
}"""

#: 这一次下拉真的刷新过、并且已经收起（回到 idle）
_PTR_DONE_JS = """(id) => {
  const zone = document.querySelector(`[data-testid="${id}"]`);
  return !!zone && !!window.__ptrSeen && window.__ptrSeen[id] === true && zone.dataset.state === 'idle';
}"""


def pull_refresh(page: Page, ptr_testid: str, *, distance: float = 240, from_y: float = 300) -> None:
    """下拉刷新一次，并等到这次刷新真的发生、转圈收起——四个 Tab 唯一的刷新入口（ADR 0007：没有刷新按钮）。

    ptr_testid 是下拉区的 data-testid：首页 home-ptr / 账单 bills-ptr / 统计 stats-ptr / 我的 profile-ptr；
    调用时对应的 Tab 要是当前可见的那个。会先把页面滚回顶部（只有停在顶部才会开始下拉）。
    先布防再拉：下拉区没进入过 refreshing（距离不够、手势被吃掉）就一直等不到，超时抛错，
    而不是悄悄返回、让后面读到旧数据的断言莫名其妙地失败。
    数据变更若不经界面（直接写库），要靠它让常驻的 Tab 根页面重读。
    """
    scroll_root_to(page, 0)
    page.wait_for_function(_PTR_READY_JS, arg=ptr_testid, timeout=TIMEOUT_MS)
    page.evaluate(_PTR_ARM_JS, ptr_testid)
    pull_down(page, distance=distance, from_y=from_y)
    page.wait_for_function(_PTR_DONE_JS, arg=ptr_testid, timeout=TIMEOUT_MS)


# ======================================================================
# 基于状态的等待（不使用固定 sleep）
# ======================================================================


def wait_state(page: Page, predicate_js: str, *, timeout: int = TIMEOUT_MS, arg=None) -> None:
    """等待页面内的 JS 谓词为真，例如 wait_state(page, "() => document.title === 'x'")。"""
    page.wait_for_function(predicate_js, arg=arg, timeout=timeout)


def wait_text(page: Page, text: str, *, timeout: int = TIMEOUT_MS) -> None:
    page.get_by_text(text).first.wait_for(state="visible", timeout=timeout)


def wait_attr(page: Page, selector: str, attr: str, value: Optional[str], *, timeout: int = TIMEOUT_MS) -> None:
    """等待元素属性等于 value；value 为 None 表示等待属性被移除。"""
    page.wait_for_function(
        """([sel, attr, value]) => {
            const el = document.querySelector(sel);
            if (!el) return false;
            return value === null ? !el.hasAttribute(attr) : el.getAttribute(attr) === value;
        }""",
        arg=[selector, attr, value],
        timeout=timeout,
    )


def wait_url(page: Page, pattern: str, *, timeout: int = TIMEOUT_MS) -> None:
    """等待地址路径（含查询串）匹配正则；Blazor 内部导航不触发整页加载，所以不用 wait_for_url 的 load 语义。"""
    page.wait_for_function(
        "(p) => new RegExp(p).test(location.pathname + location.search)", arg=pattern, timeout=timeout
    )


def wait_animations_settled(page: Page, *, timeout: int = TIMEOUT_MS) -> None:
    """等待所有有限时长的 CSS/Web 动画结束（无限循环动画不计）。"""
    page.wait_for_function(
        """() => document.getAnimations().every(a => {
            const t = a.effect && a.effect.getComputedTiming ? a.effect.getComputedTiming() : null;
            return !t || t.iterations === Infinity || a.playState !== 'running';
        })""",
        timeout=timeout,
    )


# ======================================================================
# 悬浮玻璃 Tab 栏（03 号起）
# ======================================================================

#: Tab 栏容器（文档化可观察属性 data-testid="tab-bar"，见 README）
TAB_BAR_SELECTOR = '[data-testid="tab-bar"]'

#: Tab 显示名 → data-active-tab 取值
TAB_KEYS = {"首页": "home", "账单": "bills", "统计": "statistics", "我的": "profile"}

#: 当前可见 Tab 根页面的滚动容器（表达式；根页面把滚动容器标记为 data-scroll-root，找不到标记退回到根页面容器本身，与 tabBar.js 同口径）
_ACTIVE_SCROLL_ROOT_EXPR = """(() => {
  const root = document.querySelector('.p-page-container__item:not(.d-none)');
  if (!root) return null;
  return root.querySelector('[data-scroll-root]') || root;
})()"""


#: 当前屏幕上带 backdrop-filter 的可见元素个数（玻璃层预算：每屏 ≤ 3，见 ADR 0007 / tokens.css；表达式，用 page.evaluate 取值）。
#: 各页面脚本的「玻璃层 ≤ 3」检查共用这一份；23 号的逐屏扫描（含最坏情形口径）在 a11y_probes.py 里，不依赖它。
GLASS_LAYERS_JS = """() => [...document.querySelectorAll('*')].filter((el) => {
  const cs = getComputedStyle(el);
  const filter = cs.backdropFilter && cs.backdropFilter !== 'none' ? cs.backdropFilter : cs.webkitBackdropFilter;
  if (!filter || filter === 'none' || cs.visibility === 'hidden') return false;
  const r = el.getBoundingClientRect();
  return r.width > 0 && r.height > 0;
}).length"""


#: 当前可见 Tab 根页面的容器。Tab 根页面是常驻的（切走只加 d-none 不销毁），
#: 所以 page.get_by_text(...) 这类全局定位会命中其它 Tab 里被隐藏的同名元素——点按页面内容时用 active_root(page) 限定范围。
ACTIVE_ROOT_SELECTOR = ".p-page-container__item:not(.d-none)"


def active_root(page: Page) -> Locator:
    """当前可见的 Tab 根页面（不含压栈的二级页面）。"""
    return page.locator(ACTIVE_ROOT_SELECTOR)


def tab_bar(page: Page) -> Locator:
    """Tab 栏容器。二级页面与登录页时它仍在 DOM 里（或根本不存在），用 wait_tab_bar 判断显示 / 隐藏。"""
    return page.locator(TAB_BAR_SELECTOR)


def tab_button(page: Page, title: str) -> Locator:
    """Tab 栏上的某一项：首页 / 账单 / 统计 / 我的（无障碍角色 tab，按显示名定位）。"""
    return tab_bar(page).get_by_role("tab", name=title, exact=True)


def active_tab(page: Page) -> Optional[str]:
    """当前激活的 Tab 键（home / bills / statistics / profile），读自 data-active-tab。"""
    return page.get_attribute(TAB_BAR_SELECTOR, "data-active-tab")


def wait_tab_bar(page: Page, *, visible: bool = True, timeout: int = TIMEOUT_MS) -> None:
    """等待 Tab 栏显示 / 隐藏（含滑入滑出过渡）。未登录时 Tab 栏不存在，等 hidden 同样成立。"""
    tab_bar(page).wait_for(state="visible" if visible else "hidden", timeout=timeout)


def wait_secondary_pages_closed(page: Page, *, timeout: int = TIMEOUT_MS) -> None:
    """等待页面栈里的二级页面全部移除（含退出动画）。

    浏览器回退后地址立刻变回根页面，Tab 栏也马上显示，但二级页面还要播放约 300ms 的退出动画才从 DOM 移除；
    这期间再点按会被页面栈忽略（点击事件到了，导航被吞掉）。返回根页面后要继续点按，先等它。
    """
    page.wait_for_function("() => !document.querySelector('.m-page-stack-item')", timeout=timeout)


def open_tab(page: Page, title: str) -> None:
    """点按 Tab 栏上的某一项，等到激活 Tab 切换完成。向下滚动后 Tab 栏会收缩成胶囊（04 号起），这里先展开再点。"""
    wait_tab_bar(page)
    expand_tab_bar(page)
    tab_button(page, title).click()
    wait_attr(page, TAB_BAR_SELECTOR, "data-active-tab", TAB_KEYS[title])


def scroll_root_state(page: Page) -> "Optional[dict]":
    """当前 Tab 根页面滚动容器的 {top, max}：滚动位置与可滚动的最大距离；没有可见的根页面时返回 None。"""
    return page.evaluate(
        "() => { const el = %s; return el ? { top: el.scrollTop, max: el.scrollHeight - el.clientHeight } : null; }"
        % _ACTIVE_SCROLL_ROOT_EXPR
    )


def scroll_root_to(page: Page, top: Union[int, str]) -> None:
    """把当前 Tab 根页面的滚动容器滚到指定位置；top='bottom' 表示滚到底。"""
    page.evaluate(
        "(top) => { const el = %s; el.scrollTop = top === 'bottom' ? el.scrollHeight : top; }"
        % _ACTIVE_SCROLL_ROOT_EXPR,
        top,
    )


def wait_scroll_root_stable(page: Page, *, settle_ms: int = 400, timeout: int = TIMEOUT_MS) -> None:
    """等待当前 Tab 根页面的内容高度连续 settle_ms 不再变化（首次进入时的数据加载 / 骨架屏替换已完成）。

    在设置滚动位置、测量布局之前调用，避免内容高度还在变化时量到不稳定的值。
    """
    page.evaluate("() => { delete window.__ftRootHeight; }")
    page.wait_for_function(
        """(settle) => {
            const el = %s;
            if (!el) return false;
            const now = performance.now();
            const height = el.scrollHeight;
            const last = window.__ftRootHeight;
            if (!last || last.height !== height) { window.__ftRootHeight = { height, since: now }; return false; }
            return now - last.since >= settle;
        }"""
        % _ACTIVE_SCROLL_ROOT_EXPR,
        arg=settle_ms,
        polling=100,
        timeout=timeout,
    )


def wait_scroll_root_top(page: Page, *, at_most: float = 1, timeout: int = TIMEOUT_MS) -> None:
    """等待当前 Tab 根页面滚回顶部（含平滑滚动的过程）。"""
    page.wait_for_function(
        "(limit) => { const el = %s; return !!el && el.scrollTop <= limit; }" % _ACTIVE_SCROLL_ROOT_EXPR,
        arg=at_most,
        timeout=timeout,
    )


# ----------------------------------------------------------------------
# Tab 滑块（04 号起）
# ----------------------------------------------------------------------

#: 选中项的玻璃高光。data-state = idle | moving（弹簧是否还在运动）；
#: getBoundingClientRect 含水平拉伸与按下抬起（scale 绕自身中心，所以中心 x 就是弹簧位置）。
TAB_THUMB_SELECTOR = '[data-testid="tab-bar-thumb"]'

#: 向下滚动后 Tab 栏收缩成「当前页胶囊」时显示当前页的按钮
TAB_CAPSULE_SELECTOR = '[data-testid="tab-bar-capsule"]'

#: 「记一笔」圆钮
TAB_ADD_SELECTOR = '[data-testid="tab-bar-add"]'

_THUMB_BOX_JS = """() => {
  const el = document.querySelector('%s');
  if (!el) return null;
  const r = el.getBoundingClientRect();
  return { left: r.left, right: r.right, top: r.top, bottom: r.bottom, width: r.width, height: r.height, cx: r.left + r.width / 2 };
}""" % TAB_THUMB_SELECTOR

# 逐帧采样高光：每个 requestAnimationFrame 记一次位置 / 尺寸 / 不透明度（页面里同一时刻只允许一个采样器）
_THUMB_SAMPLER_START_JS = """() => {
  const thumb = document.querySelector('%s');
  if (window.__ftThumbSampler) window.__ftThumbSampler.running = false;
  const store = (window.__ftThumbSampler = { running: true, samples: [] });
  const tick = () => {
    if (!store.running) return;
    const r = thumb.getBoundingClientRect();
    store.samples.push({
      t: performance.now(),
      cx: r.left + r.width / 2,
      w: r.width,
      h: r.height,
      opacity: parseFloat(getComputedStyle(thumb).opacity),
      state: thumb.dataset.state || null,
    });
    requestAnimationFrame(tick);
  };
  tick();
}""" % TAB_THUMB_SELECTOR

_THUMB_SAMPLER_STOP_JS = """() => {
  const store = window.__ftThumbSampler;
  if (!store) return [];
  store.running = false;
  return store.samples;
}"""


def tab_thumb(page: Page) -> Locator:
    """选中项的玻璃高光。"""
    return page.locator(TAB_THUMB_SELECTOR)


def thumb_box(page: Page) -> dict:
    """高光当前的包围盒 {left, right, top, bottom, width, height, cx}（含拉伸 / 抬起）。"""
    return page.evaluate(_THUMB_BOX_JS)


def tab_box(page: Page, title: str) -> dict:
    """Tab 栏上某一项的包围盒 {left, right, top, bottom, width, height, cx}。"""
    box = tab_button(page, title).bounding_box()
    if box is None:
        raise RuntimeError(f"Tab 不可见: {title}")
    return {
        "left": box["x"],
        "right": box["x"] + box["width"],
        "top": box["y"],
        "bottom": box["y"] + box["height"],
        "width": box["width"],
        "height": box["height"],
        "cx": box["x"] + box["width"] / 2,
    }


def wait_thumb_idle(page: Page, *, timeout: int = TIMEOUT_MS) -> None:
    """等待高光弹簧运动结束（data-state = idle）。点按 Tab 时脚本同步把它置为 moving，所以点按后立即调用是安全的。"""
    wait_attr(page, TAB_THUMB_SELECTOR, "data-state", "idle", timeout=timeout)


def wait_thumb_at(page: Page, cx: float, *, tolerance: float = 1.0, timeout: int = TIMEOUT_MS) -> None:
    """等待高光弹簧停稳（idle）且中心 x 落在 cx ± tolerance。

    比单独 wait_thumb_idle 稳：Blazor 触发的跳转（非点按）要等 sync 往返之后高光才开始运动，
    此前它还是 idle，只看 idle 会误判为「已经到位」。
    """
    page.wait_for_function(
        """([sel, cx, tolerance]) => {
            const thumb = document.querySelector(sel);
            if (!thumb || thumb.dataset.state !== 'idle') return false;
            const r = thumb.getBoundingClientRect();
            return Math.abs(r.left + r.width / 2 - cx) <= tolerance;
        }""",
        arg=[TAB_THUMB_SELECTOR, cx, tolerance],
        timeout=timeout,
    )


def tab_collapsed(page: Page) -> bool:
    """Tab 栏是否处于收缩（胶囊）状态，读自脚本维护的 data-collapsed。"""
    return page.get_attribute(TAB_BAR_SELECTOR, "data-collapsed") == "true"


def expand_tab_bar(page: Page, *, timeout: int = TIMEOUT_MS) -> None:
    """向下滚动后 Tab 栏会收缩成「当前页胶囊」，四个 Tab 此时不可点。已收缩则点按胶囊展开，已展开什么也不做。

    测试里用脚本把页面滚下去再去点 Tab（或点「再次点按当前 Tab 回顶」）之前调用；open_tab 已经内置。
    """
    # 脚本刚设置过 scrollTop 时，scroll 事件要到下一帧才派发、Tab 栏才会收缩——让出两帧再判断，免得判断完才收缩
    page.evaluate("() => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))")
    if tab_collapsed(page):
        page.locator(TAB_CAPSULE_SELECTOR).click(timeout=timeout)
        wait_attr(page, TAB_BAR_SELECTOR, "data-collapsed", "false", timeout=timeout)


def start_thumb_sampler(page: Page) -> None:
    """开始逐帧采样高光的位置与尺寸；配合 stop_thumb_sampler 取回采样序列。"""
    page.evaluate(_THUMB_SAMPLER_START_JS)


def stop_thumb_sampler(page: Page) -> "list[dict]":
    """停止采样并返回 [{t, cx, w, h, opacity, state}, …]（t 为页面内 performance.now() 毫秒）。

    停止前先让出两帧，保证采样序列里至少带上「停稳之后」的帧。
    """
    page.evaluate("() => new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)))")
    return page.evaluate(_THUMB_SAMPLER_STOP_JS)


# ======================================================================
# 截图 / 调试产物
# ======================================================================


def _safe_name(name: str) -> str:
    return re.sub(r"[^\w\-.\u4e00-\u9fff]+", "_", name).strip("_") or "shot"


def shot(page: Page, name: str, *, force: bool = False, directory: Optional[Path] = None) -> Optional[Path]:
    """可选截图：E2E_SCREENSHOTS=1 或 force=True 时输出 PNG，返回路径；否则不做任何事。"""
    if not (SCREENSHOTS_ENABLED or force):
        return None
    target_dir = directory or SCREENSHOT_DIR
    target_dir.mkdir(parents=True, exist_ok=True)
    path = target_dir / f"{_safe_name(name)}.png"
    page.screenshot(path=str(path))
    return path


def dump_html(page: Page, name: str) -> Path:
    """保存页面 HTML 快照到 artifacts 目录（排错用，已被 .gitignore 忽略）。"""
    ARTIFACT_DIR.mkdir(parents=True, exist_ok=True)
    path = ARTIFACT_DIR / f"{_safe_name(name)}.html"
    path.write_text(page.content(), encoding="utf-8")
    return path


# ======================================================================
# 本地 SQLite 造数据
# ======================================================================

#: 造数据依赖的表与列。应用的实体/迁移变更导致列缺失时，verify_schema 会给出清晰报错而不是莫名失败
REQUIRED_SCHEMA = {
    "Users": ["Id", "PhoneNumber", "IsDeleted", "CreatedAt", "UpdatedAt"],
    "Ledgers": ["Id", "UserId", "Name", "Icon", "SortOrder", "IsDeleted", "CreatedAt", "UpdatedAt"],
    "Bills": [
        "Id", "UserId", "Amount", "Type", "CategoryId", "PaymentChannelId", "LedgerId",
        "TransactionTime", "Note", "Source", "SyncStatus", "IsDeleted", "CreatedAt", "UpdatedAt",
    ],
    "PendingBills": ["Id", "Amount", "Type", "Channel", "Note", "TransactionTime", "Source", "CapturedAt"],
    "Budgets": ["Id", "UserId", "LedgerId", "Year", "Month", "CategoryId", "Amount", "IsDeleted", "CreatedAt", "UpdatedAt"],
    "Categories": ["Id", "Name", "Icon", "Type", "IsPreset", "IsDeleted", "SortOrder", "UserId", "ExcludeFromStatistics", "CreatedAt", "UpdatedAt"],
    "PaymentChannels": ["Id", "Name", "Icon", "IsPreset", "IsDeleted", "SortOrder", "UserId", "CreatedAt", "UpdatedAt"],
    "LedgerMembers": ["Id", "LedgerId", "UserId", "Role", "Status", "IsDeleted", "CreatedAt", "UpdatedAt"],
    "UserDefaults": [
        "Id", "UserId", "DefaultLedgerId", "DefaultExpenseCategoryId", "DefaultIncomeCategoryId", "DefaultPaymentChannelId",
        "CreatedAt", "UpdatedAt",
    ],
}

# 枚举取值与 FinanceTracker.Core.Enums 保持一致
BILL_TYPE_EXPENSE, BILL_TYPE_INCOME = 0, 1
SOURCE_MANUAL, SOURCE_IMPORT, SOURCE_SMS, SOURCE_NOTIFICATION, SOURCE_VOICE = 0, 1, 2, 3, 4
SYNC_PENDING, SYNC_SYNCED, SYNC_FAILED = 0, 1, 2
ROLE_OWNER, ROLE_EDITOR, ROLE_VIEWER = 0, 1, 2
MEMBER_PENDING, MEMBER_ACTIVE = 0, 1


class SchemaMismatch(RuntimeError):
    pass


def new_guid() -> str:
    """EF Core 的 SQLite 提供程序把 Guid 存成大写 TEXT；造数据必须同样大写，否则 `=` 比较会失配。"""
    return str(uuid.uuid4()).upper()


def _gid(value: Optional[str]) -> Optional[str]:
    """外部传入的 Id（如 localStorage 里小写的 userId）统一成库内的大写形式。"""
    return value.upper() if value else value


def to_db_time(moment: datetime) -> str:
    """DateTime → EF 风格文本。naive 时间视为运行环境本地时区（即应用时区默认值），先换算成 UTC。

    数据库里的 Bill.TransactionTime 一律是 UTC（见 CLAUDE.md 时间口径）。
    """
    aware = moment.astimezone() if moment.tzinfo is None else moment
    utc = aware.astimezone(timezone.utc)
    text = utc.strftime("%Y-%m-%d %H:%M:%S")
    if utc.microsecond:
        text += f".{utc.microsecond:06d}0"
    return text


def to_wall_time(moment: datetime) -> str:
    """DateTime → EF 风格文本，**按墙上时间原样写入、不做时区换算**（naive 本地时间直接格式化）。

    用于「存的就是设备本地时间」的列：PendingBills.TransactionTime / CapturedAt（捕获时取的是 LocalDateTime / DateTime.Now）。
    Bills.TransactionTime 等 UTC 列请用 to_db_time。
    """
    text = moment.strftime("%Y-%m-%d %H:%M:%S")
    if moment.microsecond:
        text += f".{moment.microsecond:06d}0"
    return text


def to_db_decimal(value) -> str:
    """decimal → 应用写进库里的那段文本。

    Microsoft.Data.Sqlite 把 decimal 存 / 绑定成 TEXT，格式是 "0.0###…"：至少一位小数、去掉多余的尾零（7 → "7.0"，8.50 → "8.5"）。
    SQLite 里金额是文本，应用按金额做 `=` 比较（待确认账单去重）比的就是这段文本——"7" / "7.00" 与应用写入的 "7.0" 不相等，
    所以需要和应用的写入 / 查询对得上的列（PendingBills.Amount）要按这个写法造（单元测试 `PendingBillDeduplicationTests` 锁住了这个假设）。
    """
    text = format(Decimal(str(value)).normalize(), "f")
    return text if "." in text else f"{text}.0"


class LocalDb:
    """直连 Web.Server 使用的本地 SQLite 做造数据 / 核对（库与应用同时打开，用 busy_timeout 兜底）。"""

    def __init__(self, path: Optional[Union[str, Path]] = None) -> None:
        self.path = str(path or DB_PATH)

    # ---- 基础 ----
    def exists(self) -> bool:
        return os.path.exists(self.path)

    @contextlib.contextmanager
    def _connect(self) -> Iterator[sqlite3.Connection]:
        con = sqlite3.connect(self.path, timeout=15)
        con.row_factory = sqlite3.Row
        con.execute("PRAGMA busy_timeout = 15000")
        try:
            yield con
            con.commit()
        finally:
            con.close()

    def verify_schema(self) -> None:
        """核对造数据依赖的表/列都存在；不匹配直接报错并指出缺失项。"""
        problems: list[str] = []
        with self._connect() as con:
            for table, columns in REQUIRED_SCHEMA.items():
                found = [r["name"] for r in con.execute(f'PRAGMA table_info("{table}")')]
                if not found:
                    problems.append(f"缺少表 {table}")
                    continue
                missing = [c for c in columns if c not in found]
                if missing:
                    problems.append(f"表 {table} 缺少列 {', '.join(missing)}")
        if problems:
            raise SchemaMismatch("本地库结构与 E2E 造数据假设不一致：" + "；".join(problems))

    # ---- 查询 ----
    def user_id(self, phone: str) -> str:
        with self._connect() as con:
            row = con.execute('SELECT Id FROM "Users" WHERE PhoneNumber = ?', (phone,)).fetchone()
        if row is None:
            raise LookupError(f"本地库没有手机号 {phone} 的用户")
        return row["Id"]

    def ledger_id_by_name(self, user_id: str, name: str) -> str:
        with self._connect() as con:
            row = con.execute(
                'SELECT Id FROM "Ledgers" WHERE UserId = ? AND Name = ? AND IsDeleted = 0', (_gid(user_id), name)
            ).fetchone()
        if row is None:
            raise LookupError(f"用户 {user_id} 没有名为 {name} 的账本")
        return row["Id"]

    def preset_category_id(self, bill_type: int = BILL_TYPE_EXPENSE) -> str:
        with self._connect() as con:
            row = con.execute(
                'SELECT Id FROM "Categories" WHERE IsPreset = 1 AND IsDeleted = 0 AND Type = ? '
                "ORDER BY SortOrder LIMIT 1",
                (bill_type,),
            ).fetchone()
        if row is None:
            raise LookupError("本地库没有预设分类（应用至少启动过一次才会写入）")
        return row["Id"]

    def category_id_by_name(self, name: str, bill_type: int = BILL_TYPE_EXPENSE) -> str:
        """按名称取预设分类 Id（例如「餐饮美食」「工资薪酬」）。"""
        with self._connect() as con:
            row = con.execute(
                'SELECT Id FROM "Categories" WHERE IsPreset = 1 AND IsDeleted = 0 AND Type = ? AND Name = ? LIMIT 1',
                (bill_type, name),
            ).fetchone()
        if row is None:
            raise LookupError(f"本地库没有名为 {name} 的预设分类")
        return row["Id"]

    def preset_channel_id(self) -> str:
        with self._connect() as con:
            row = con.execute(
                'SELECT Id FROM "PaymentChannels" WHERE IsPreset = 1 AND IsDeleted = 0 ORDER BY SortOrder LIMIT 1'
            ).fetchone()
        if row is None:
            raise LookupError("本地库没有预设支付渠道（应用至少启动过一次才会写入）")
        return row["Id"]

    def channel_id_by_name(self, name: str) -> str:
        """按名称取预设支付渠道 Id（例如「微信支付」「支付宝」）。"""
        with self._connect() as con:
            row = con.execute(
                'SELECT Id FROM "PaymentChannels" WHERE IsPreset = 1 AND IsDeleted = 0 AND Name = ? LIMIT 1',
                (name,),
            ).fetchone()
        if row is None:
            raise LookupError(f"本地库没有名为 {name} 的预设支付渠道")
        return row["Id"]

    def visible_category_count(self, user_id: str, bill_type: Optional[int] = None) -> int:
        """用户在分类管理里能看到的分类个数（缺省收入 + 支出，bill_type 给定则只数该类型）：
        预设 + 自己的自定义，不含已软删除的，也不含别人的自定义。"""
        sql = 'SELECT COUNT(*) AS n FROM "Categories" WHERE IsDeleted = 0 AND (IsPreset = 1 OR UserId = ?)'
        params: list = [_gid(user_id)]
        if bill_type is not None:
            sql += " AND Type = ?"
            params.append(bill_type)
        with self._connect() as con:
            row = con.execute(sql, params).fetchone()
        return int(row["n"])

    def preset_category_names(self, bill_type: int = BILL_TYPE_EXPENSE) -> "list[str]":
        """预设分类的名称，按分类管理页里的顺序（排序值升序）。"""
        with self._connect() as con:
            rows = con.execute(
                'SELECT Name FROM "Categories" WHERE IsPreset = 1 AND IsDeleted = 0 AND Type = ? ORDER BY SortOrder', (bill_type,)
            ).fetchall()
        return [r["Name"] for r in rows]

    def preset_channel_names(self) -> "list[str]":
        """预设支付渠道的名称，按支付渠道页里的顺序（排序值升序）。"""
        with self._connect() as con:
            rows = con.execute('SELECT Name FROM "PaymentChannels" WHERE IsPreset = 1 AND IsDeleted = 0 ORDER BY SortOrder').fetchall()
        return [r["Name"] for r in rows]

    def visible_channel_count(self, user_id: str) -> int:
        """用户在支付渠道管理里能看到的支付渠道个数：预设 + 自己的自定义，不含已软删除的，也不含别人的自定义。"""
        with self._connect() as con:
            row = con.execute(
                'SELECT COUNT(*) AS n FROM "PaymentChannels" WHERE IsDeleted = 0 AND (IsPreset = 1 OR UserId = ?)',
                (_gid(user_id),),
            ).fetchone()
        return int(row["n"])

    def bills_by_note(self, note: str) -> "list[dict]":
        with self._connect() as con:
            rows = con.execute('SELECT * FROM "Bills" WHERE Note = ? ORDER BY CreatedAt', (note,)).fetchall()
        return [dict(r) for r in rows]

    def bills_of_user(self, user_id: str, *, include_deleted: bool = False) -> "list[dict]":
        sql = 'SELECT * FROM "Bills" WHERE UserId = ?'
        if not include_deleted:
            sql += " AND IsDeleted = 0"
        with self._connect() as con:
            rows = con.execute(sql + " ORDER BY TransactionTime DESC", (_gid(user_id),)).fetchall()
        return [dict(r) for r in rows]

    def ledgers_of_user(self, user_id: str) -> "list[dict]":
        with self._connect() as con:
            rows = con.execute(
                'SELECT * FROM "Ledgers" WHERE UserId = ? AND IsDeleted = 0 ORDER BY SortOrder, CreatedAt',
                (_gid(user_id),),
            ).fetchall()
        return [dict(r) for r in rows]

    def pending_bills(self) -> "list[dict]":
        with self._connect() as con:
            rows = con.execute('SELECT * FROM "PendingBills" ORDER BY CapturedAt').fetchall()
        return [dict(r) for r in rows]

    def bill_row(self, bill_id: str, *, include_deleted: bool = True) -> "Optional[dict]":
        """按 Id 读一笔账单（默认含已软删除的，用来核对「删除是软删除」「编辑后哪些字段变了」）。没有这笔账单返回 None。"""
        sql = 'SELECT * FROM "Bills" WHERE Id = ?'
        if not include_deleted:
            sql += " AND IsDeleted = 0"
        with self._connect() as con:
            row = con.execute(sql, (_gid(bill_id),)).fetchone()
        return dict(row) if row else None

    def ledger_row(self, ledger_id: str, *, include_deleted: bool = True) -> "Optional[dict]":
        """按 Id 读一个账本（默认含已软删除的，用来核对「删除是软删除」）。"""
        sql = 'SELECT * FROM "Ledgers" WHERE Id = ?'
        if not include_deleted:
            sql += " AND IsDeleted = 0"
        with self._connect() as con:
            row = con.execute(sql, (_gid(ledger_id),)).fetchone()
        return dict(row) if row else None

    def budgets_of_user(self, user_id: str, *, include_deleted: bool = False) -> "list[dict]":
        """用户的预算（默认不含已软删除的），按年月、创建时间排序（18 号：预算页新建 / 编辑 / 删除后核对落库）。"""
        sql = 'SELECT * FROM "Budgets" WHERE UserId = ?'
        if not include_deleted:
            sql += " AND IsDeleted = 0"
        with self._connect() as con:
            rows = con.execute(sql + " ORDER BY Year, Month, CreatedAt", (_gid(user_id),)).fetchall()
        return [dict(r) for r in rows]

    def budget_row(self, budget_id: str, *, include_deleted: bool = True) -> "Optional[dict]":
        """按 Id 读一条预算（默认含已软删除的，用来核对「删除是软删除」）。没有返回 None。"""
        sql = 'SELECT * FROM "Budgets" WHERE Id = ?'
        if not include_deleted:
            sql += " AND IsDeleted = 0"
        with self._connect() as con:
            row = con.execute(sql, (_gid(budget_id),)).fetchone()
        return dict(row) if row else None

    def default_ledger_id(self, user_id: str) -> "Optional[str]":
        """用户设置的默认账本 Id（没有设置过 / 已取消返回 None）。"""
        with self._connect() as con:
            row = con.execute('SELECT DefaultLedgerId FROM "UserDefaults" WHERE UserId = ?', (_gid(user_id),)).fetchone()
        return row["DefaultLedgerId"] if row and row["DefaultLedgerId"] else None

    def category_row(self, category_id: str, *, include_deleted: bool = True) -> "Optional[dict]":
        """按 Id 读一个分类（默认含已软删除的，用来核对「删除是软删除」）。"""
        sql = 'SELECT * FROM "Categories" WHERE Id = ?'
        if not include_deleted:
            sql += " AND IsDeleted = 0"
        with self._connect() as con:
            row = con.execute(sql, (_gid(category_id),)).fetchone()
        return dict(row) if row else None

    def channel_row(self, channel_id: str, *, include_deleted: bool = True) -> "Optional[dict]":
        """按 Id 读一个支付渠道（默认含已软删除的，用来核对「删除是软删除」）。"""
        sql = 'SELECT * FROM "PaymentChannels" WHERE Id = ?'
        if not include_deleted:
            sql += " AND IsDeleted = 0"
        with self._connect() as con:
            row = con.execute(sql, (_gid(channel_id),)).fetchone()
        return dict(row) if row else None

    def custom_categories(self, user_id: str) -> "list[dict]":
        """用户自己的自定义分类（未删除；收入 + 支出），按创建时间排序。"""
        with self._connect() as con:
            rows = con.execute(
                'SELECT * FROM "Categories" WHERE UserId = ? AND IsPreset = 0 AND IsDeleted = 0 ORDER BY CreatedAt',
                (_gid(user_id),),
            ).fetchall()
        return [dict(r) for r in rows]

    def custom_channels(self, user_id: str) -> "list[dict]":
        """用户自己的自定义支付渠道（未删除），按创建时间排序。"""
        with self._connect() as con:
            rows = con.execute(
                'SELECT * FROM "PaymentChannels" WHERE UserId = ? AND IsPreset = 0 AND IsDeleted = 0 ORDER BY CreatedAt',
                (_gid(user_id),),
            ).fetchall()
        return [dict(r) for r in rows]

    def default_category_id(self, user_id: str, bill_type: int = BILL_TYPE_EXPENSE) -> "Optional[str]":
        """用户设置的默认分类 Id（支出 / 收入各有一个；没有设置过 / 已取消返回 None）。"""
        column = "DefaultIncomeCategoryId" if bill_type == BILL_TYPE_INCOME else "DefaultExpenseCategoryId"
        with self._connect() as con:
            row = con.execute(f'SELECT {column} AS v FROM "UserDefaults" WHERE UserId = ?', (_gid(user_id),)).fetchone()
        return row["v"] if row and row["v"] else None

    def default_channel_id(self, user_id: str) -> "Optional[str]":
        """用户设置的默认支付渠道 Id（没有设置过 / 已取消返回 None）。"""
        with self._connect() as con:
            row = con.execute('SELECT DefaultPaymentChannelId AS v FROM "UserDefaults" WHERE UserId = ?', (_gid(user_id),)).fetchone()
        return row["v"] if row and row["v"] else None

    def ledger_members(self, ledger_id: str) -> "list[dict]":
        with self._connect() as con:
            rows = con.execute(
                'SELECT * FROM "LedgerMembers" WHERE LedgerId = ? ORDER BY CreatedAt', (_gid(ledger_id),)
            ).fetchall()
        return [dict(r) for r in rows]

    # ---- 造数据 ----
    def seed_user(self, phone: Optional[str] = None) -> str:
        """造一个本地用户（例如共享账本的所有者 / 邀请人），返回用户 Id。不登录，只在库里存在。"""
        user_id = new_guid()
        now = to_db_time(datetime.now(timezone.utc))
        with self._connect() as con:
            con.execute(
                'INSERT INTO "Users" (Id, PhoneNumber, IsDeleted, CreatedAt, UpdatedAt) VALUES (?, ?, 0, ?, ?)',
                (user_id, phone or unique_phone(), now, now),
            )
        return user_id

    def seed_ledger_member(
        self,
        ledger_id: str,
        user_id: str,
        *,
        role: int = ROLE_EDITOR,
        status: int = MEMBER_ACTIVE,
        member_id: Optional[str] = None,
    ) -> str:
        """造一条账本成员关系（本地缓存）。Owner + Active 对应「自己的账本」，Editor/Viewer + Active 是「加入的共享账本」，
        任意角色 + Pending 是待处理的邀请。注意：成员关系以云端为准，云端不可用时页面只读这份本地缓存。
        member_id 缺省随机生成；要和假云端的成员行对上（同一个 Id）时由 fake_cloud.CloudWorld 传入。"""
        explicit_id = member_id is not None
        member_id = _gid(member_id) or new_guid()
        now = to_db_time(datetime.now(timezone.utc))
        ledger_key, user_key = _gid(ledger_id), _gid(user_id)
        # 先 UPDATE 再 INSERT，而不是直接 INSERT：应用自己也会写这张表（账本创建 / 打开成员页 / 页面加载顺带刷新成员缓存时合并云端成员），
        # 造数与应用写入的先后取决于当时的负载，(LedgerId, UserId) 唯一键会冲突（24 号全量回归里出现过一次偶发的 IntegrityError）；
        # 冲突时以造数为准。UPDATE 先拿到写锁、持有到提交，所以后面的 INSERT 与应用的写入不会再交错；
        # 不用 ON CONFLICT：基座自测的临时库只有列、没有唯一索引，那样写会报「不匹配任何唯一约束」。
        # 只有调用方显式指定了 Id（要与假云端的成员行对上）才改写主键，否则保留已有那一行的 Id
        with self._connect() as con:
            update_id = ", Id = ?" if explicit_id else ""
            params = [role, status, now] + ([member_id] if explicit_id else []) + [ledger_key, user_key]
            updated = con.execute(
                f'UPDATE "LedgerMembers" SET Role = ?, Status = ?, IsDeleted = 0, UpdatedAt = ?{update_id} WHERE LedgerId = ? AND UserId = ?',
                params,
            ).rowcount
            if updated == 0:
                con.execute(
                    'INSERT INTO "LedgerMembers" (Id, LedgerId, UserId, Role, Status, IsDeleted, CreatedAt, UpdatedAt) '
                    "VALUES (?, ?, ?, ?, ?, 0, ?, ?)",
                    (member_id, ledger_key, user_key, role, status, now, now),
                )
                return member_id
            row = con.execute('SELECT Id FROM "LedgerMembers" WHERE LedgerId = ? AND UserId = ?', (ledger_key, user_key)).fetchone()
        return row[0] if row else member_id

    def seed_ledger(self, user_id: str, name: Optional[str] = None, *, sort_order: int = 0, icon: str = "mdi-book") -> str:
        ledger_id = new_guid()
        now = to_db_time(datetime.now(timezone.utc))
        with self._connect() as con:
            con.execute(
                'INSERT INTO "Ledgers" (Id, UserId, Name, Icon, SortOrder, IsDeleted, CreatedAt, UpdatedAt) '
                "VALUES (?, ?, ?, ?, ?, 0, ?, ?)",
                (ledger_id, _gid(user_id), name or unique_name("E2E账本"), icon, sort_order, now, now),
            )
        return ledger_id

    def seed_bill(
        self,
        user_id: str,
        *,
        amount: Union[int, float, str, Decimal] = 10,
        ledger_id: Optional[str] = None,
        bill_type: int = BILL_TYPE_EXPENSE,
        note: Optional[str] = None,
        when: Optional[datetime] = None,
        category_id: Optional[str] = None,
        channel_id: Optional[str] = None,
        source: int = SOURCE_MANUAL,
        sync_status: int = SYNC_PENDING,
    ) -> str:
        """造一条账单。when 为 naive 本地时间（缺省为现在），落库时换算成 UTC。

        ledger_id=None 就是「历史无账本账单」——应用统计里归入「未归属账本」。
        """
        bill_id = new_guid()
        now = to_db_time(datetime.now(timezone.utc))
        when = when or datetime.now()
        with self._connect() as con:
            con.execute(
                'INSERT INTO "Bills" (Id, UserId, Amount, Type, CategoryId, PaymentChannelId, LedgerId, '
                "TransactionTime, Note, Source, SyncStatus, IsDeleted, CreatedAt, UpdatedAt) "
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?, ?)",
                (
                    bill_id,
                    _gid(user_id),
                    str(Decimal(str(amount))),
                    bill_type,
                    _gid(category_id) or self.preset_category_id(bill_type),
                    _gid(channel_id) or self.preset_channel_id(),
                    _gid(ledger_id),
                    to_db_time(when),
                    note,
                    source,
                    sync_status,
                    now,
                    now,
                ),
            )
        return bill_id

    def seed_legacy_bill(self, user_id: str, **kwargs) -> str:
        """历史无账本账单（LedgerId = NULL）。"""
        kwargs.pop("ledger_id", None)
        return self.seed_bill(user_id, ledger_id=None, **kwargs)

    def seed_pending_bill(
        self,
        *,
        amount: Union[int, float, str, Decimal] = 18,
        bill_type: int = BILL_TYPE_EXPENSE,
        channel: str = "微信支付",
        note: Optional[str] = None,
        when: Optional[datetime] = None,
        source: int = SOURCE_NOTIFICATION,
    ) -> str:
        """造一条待确认账单（PendingBills 不区分用户，与应用现状一致）。

        when 是 naive 本地时间（缺省为现在），**按墙上时间原样写入、不换算成 UTC**：通知 / 短信捕获时存的就是设备本地时间
        （`DateTimeOffset.FromUnixTimeMilliseconds(...).LocalDateTime`），页面也按墙上时间显示，入账时才经 AppTimeZone 换成账单的 UTC。
        这一点与 Bills.TransactionTime（一律 UTC）不同。
        """
        pending_id = new_guid()
        with self._connect() as con:
            con.execute(
                'INSERT INTO "PendingBills" (Id, Amount, Type, Channel, Note, TransactionTime, Source, CapturedAt) '
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                (
                    pending_id,
                    to_db_decimal(amount),
                    bill_type,
                    channel,
                    note or unique_name("待确认"),
                    to_wall_time(when or datetime.now()),
                    source,
                    to_wall_time(datetime.now()),
                ),
            )
        return pending_id

    def delete_pending_bills(self) -> None:
        """清空待确认账单。PendingBills 不区分用户、所有用例共享一张表，而首页的横幅 / 角标读它：
        用到它的用例开始前与结束后各清一次，免得互相影响。"""
        with self._connect() as con:
            con.execute('DELETE FROM "PendingBills"')

    def seed_budget(
        self,
        user_id: str,
        *,
        amount: Union[int, float, str, Decimal],
        category_name: Optional[str] = None,
        ledger_id: Optional[str] = None,
        year: Optional[int] = None,
        month: Optional[int] = None,
        category_id: Optional[str] = None,
    ) -> str:
        """造一条预算。category_name 为 None 是总预算，否则是该预设支出分类的预算；ledger_id 为 None 是「全部账本」。
        自定义分类的预算传 category_id（优先于 category_name）。年月缺省为本月（运行环境本地时区，即应用时区默认值）。"""
        budget_id = new_guid()
        now = to_db_time(datetime.now(timezone.utc))
        today = datetime.now()
        category_id = _gid(category_id) if category_id else (self.category_id_by_name(category_name) if category_name else None)
        with self._connect() as con:
            con.execute(
                'INSERT INTO "Budgets" (Id, UserId, LedgerId, Year, Month, CategoryId, Amount, IsDeleted, CreatedAt, UpdatedAt) '
                "VALUES (?, ?, ?, ?, ?, ?, ?, 0, ?, ?)",
                (
                    budget_id,
                    _gid(user_id),
                    _gid(ledger_id),
                    year or today.year,
                    month or today.month,
                    category_id,
                    str(Decimal(str(amount))),
                    now,
                    now,
                ),
            )
        return budget_id

    def seed_category(
        self,
        user_id: str,
        name: Optional[str] = None,
        *,
        bill_type: int = BILL_TYPE_EXPENSE,
        icon: str = "mdi-tag",
        sort_order: int = 100,
        deleted: bool = False,
        exclude_from_statistics: bool = False,
    ) -> str:
        """造一个用户自定义分类（UserId = 该用户、IsPreset = 0）。deleted=True 造一个已软删除的；
        exclude_from_statistics=True 造一个开着「不计入统计」的（它的账单不进统计 / 首页本月概览 / 预算）。"""
        category_id = new_guid()
        now = to_db_time(datetime.now(timezone.utc))
        with self._connect() as con:
            con.execute(
                'INSERT INTO "Categories" (Id, Name, Icon, Type, IsPreset, IsDeleted, SortOrder, UserId, ExcludeFromStatistics, CreatedAt, UpdatedAt) '
                "VALUES (?, ?, ?, ?, 0, ?, ?, ?, ?, ?, ?)",
                (
                    category_id,
                    name or unique_name("E2E分类"),
                    icon,
                    bill_type,
                    1 if deleted else 0,
                    sort_order,
                    _gid(user_id),
                    1 if exclude_from_statistics else 0,
                    now,
                    now,
                ),
            )
        return category_id

    def set_category_excluded(self, category_id: str, excluded: bool) -> None:
        """直接改库里某个分类的「不计入统计」开关（造「页面常驻、库被改了」的场景用；不动 UpdatedAt，模拟的就是库里已有的状态）。"""
        with self._connect() as con:
            con.execute('UPDATE "Categories" SET ExcludeFromStatistics = ? WHERE Id = ?', (1 if excluded else 0, _gid(category_id)))

    def seed_channel(
        self,
        user_id: str,
        name: Optional[str] = None,
        *,
        icon: str = "mdi-credit-card",
        sort_order: int = 100,
        deleted: bool = False,
    ) -> str:
        """造一个用户自定义支付渠道（UserId = 该用户、IsPreset = 0）。deleted=True 造一个已软删除的。"""
        channel_id = new_guid()
        now = to_db_time(datetime.now(timezone.utc))
        with self._connect() as con:
            con.execute(
                'INSERT INTO "PaymentChannels" (Id, Name, Icon, IsPreset, IsDeleted, SortOrder, UserId, CreatedAt, UpdatedAt) '
                "VALUES (?, ?, ?, 0, ?, ?, ?, ?, ?)",
                (channel_id, name or unique_name("E2E渠道"), icon, 1 if deleted else 0, sort_order, _gid(user_id), now, now),
            )
        return channel_id

    def set_user_defaults(
        self,
        user_id: str,
        *,
        ledger_id: Optional[str] = None,
        expense_category_id: Optional[str] = None,
        income_category_id: Optional[str] = None,
        channel_id: Optional[str] = None,
    ) -> None:
        """写入（覆盖）用户默认值：默认账本 / 默认支出分类 / 默认收入分类 / 默认支付渠道，没传的置空。
        与应用一致：一个用户一行（UserDefaults.UserId）。记一笔打开时按这些值预选。"""
        now = to_db_time(datetime.now(timezone.utc))
        values = (_gid(ledger_id), _gid(expense_category_id), _gid(income_category_id), _gid(channel_id))
        with self._connect() as con:
            exists = con.execute('SELECT 1 FROM "UserDefaults" WHERE UserId = ?', (_gid(user_id),)).fetchone()
            if exists:
                con.execute(
                    'UPDATE "UserDefaults" SET DefaultLedgerId = ?, DefaultExpenseCategoryId = ?, DefaultIncomeCategoryId = ?, '
                    "DefaultPaymentChannelId = ?, UpdatedAt = ? WHERE UserId = ?",
                    (*values, now, _gid(user_id)),
                )
            else:
                con.execute(
                    'INSERT INTO "UserDefaults" (Id, UserId, DefaultLedgerId, DefaultExpenseCategoryId, DefaultIncomeCategoryId, '
                    "DefaultPaymentChannelId, CreatedAt, UpdatedAt) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                    (new_guid(), _gid(user_id), *values, now, now),
                )

    def set_bill_sync_status(self, bill_id: str, sync_status: int) -> None:
        with self._connect() as con:
            con.execute('UPDATE "Bills" SET SyncStatus = ? WHERE Id = ?', (sync_status, bill_id))

    def soft_delete_ledger(self, ledger_id: str) -> None:
        """在界面之外把账本软删除（模拟「别处删了 / 退出了账本」），用于验证页面如何处理悬空的筛选条件与归属目标。"""
        now = to_db_time(datetime.now(timezone.utc))
        with self._connect() as con:
            con.execute('UPDATE "Ledgers" SET IsDeleted = 1, UpdatedAt = ? WHERE Id = ?', (now, _gid(ledger_id)))


# ======================================================================
# 「自动识别」假实现（19 号起）
#
# 浏览器里没有「通知使用权」，也读不到短信，Web.Server 平时用空实现（都没开启、没有短信）。server.ps1 启动时打开 E2E:FakeCapture，
# 换成 server/FinanceTracker.Web.Server/E2ECapture.cs 里的假实现，由 E2E 经 POST /__e2e/capture 设置权限状态与「读到的短信」，
# 经 GET 观察「点了去开启 / 去授权」真的走到了平台服务。状态是整个进程共享的：用到它的脚本开始前与结束后各 set_capture(reset=True) 一次。
# ======================================================================

CAPTURE_URL = f"{BASE_URL}/__e2e/capture"


def _capture_call(method: str, body: Optional[dict]) -> dict:
    data = None if body is None else json.dumps(body, ensure_ascii=False).encode("utf-8")
    request = urllib.request.Request(CAPTURE_URL, data=data, method=method, headers={"Content-Type": "application/json"} if data else {})
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            return json.loads(response.read().decode("utf-8"))
    except (urllib.error.HTTPError, json.JSONDecodeError) as exc:
        # 没打开开关时这条路由不存在：GET 会落到应用的兜底页（返回的是 HTML，解析 JSON 失败），POST 是 404 / 405
        print(f"[ENV] 「自动识别」假实现不可用（{exc}）：Web.Server 需要以 E2E__FakeCapture=true 启动")
        print("[ENV] 请用 powershell tests/e2e/server.ps1 -Action restart 重启（它会设置这个开关）")
        sys.exit(2)


def set_capture(
    *,
    reset: bool = False,
    notification: Optional[bool] = None,
    sms: Optional[bool] = None,
    sms_grantable: Optional[bool] = None,
    messages: "Optional[list[dict]]" = None,
) -> dict:
    """设置「自动识别」假实现并返回最新状态。只应用给出的参数；reset=True 先恢复成「都没开启、没有短信、可授予短信权限」。
    messages 是要被「读到」的短信：每条 {"id": int, "address": str, "body": str, "date": datetime（设备本地时间）}。
    返回 {notification, sms, smsGrantable, messageCount, notificationSettingsOpened, smsPermissionRequests, smsReads, lastSmsReadFrom}。"""
    body: dict = {}
    if reset:
        body["reset"] = True
    if notification is not None:
        body["notification"] = notification
    if sms is not None:
        body["sms"] = sms
    if sms_grantable is not None:
        body["smsGrantable"] = sms_grantable
    if messages is not None:
        body["messages"] = [{**m, "date": m["date"].isoformat(timespec="seconds")} for m in messages]
    return _capture_call("POST", body)


def capture_state() -> dict:
    """「自动识别」假实现的当前状态（含被调用的次数）。"""
    return _capture_call("GET", None)


# ======================================================================
# 记一笔 Sheet（15 号起）
#
# Tab 栏的「记一笔」圆钮升起的全高 Sheet：金额由自定义数字键盘一个个按出来（不唤起系统键盘），分类 / 账本 / 支付渠道都是普通按钮与
# 弹出菜单，所以不再有「MASA 下拉在无头浏览器里选不了」的限制——E2E 第一次能稳定地经界面完整保存一笔账单。
# 观察属性：addbill-sheet（data-ready / data-type）、amount-display（data-value）、category-tile（data-name / data-selected）、
# ledger-row / channel-row / time-row / note-row、addbill-time / addbill-note、ledger-menu / channel-menu（选项 ledger-option / channel-option）、
# keypad 与 key-0..key-9 / key-dot / key-00 / key-backspace、addbill-save。
# ======================================================================

ADDBILL_SHEET_SELECTOR = '[data-testid="addbill-sheet"]'


def addbill_sheet(page: Page) -> Locator:
    return page.get_by_test_id("addbill-sheet")


def open_add_bill_sheet(page: Page) -> Locator:
    """点 Tab 栏的「记一笔」圆钮升起记一笔 Sheet，等到分类 / 支付渠道 / 账本读完（data-ready=true）、升起动画结束。返回 Sheet。
    要求当前在 Tab 根页面（Tab 栏可见）。"""
    wait_tab_bar(page)
    page.locator(TAB_ADD_SELECTOR).click()
    sheet = addbill_sheet(page)
    sheet.wait_for(state="visible")
    wait_attr(page, ADDBILL_SHEET_SELECTOR, "data-ready", "true")
    wait_animations_settled(page)
    return sheet


def close_add_bill_sheet(page: Page) -> None:
    """点 ✕ 关闭记一笔 Sheet（没有输入时直接收起；有输入会弹出放弃确认，要自己处理）。"""
    addbill_sheet(page).get_by_test_id("sheet-close").click()
    addbill_sheet(page).wait_for(state="detached")


def _keypad_testid(key: str) -> str:
    return {".": "key-dot"}.get(key, f"key-{key}")


def press_keys(page: Page, keys: str) -> None:
    """在记一笔的数字键盘上依次按键：每个字符一个键（数字、小数点）；"00" 键与退格请用 press_key。"""
    for key in keys:
        page.get_by_test_id(_keypad_testid(key)).click()


def press_key(page: Page, key: str) -> None:
    """按一个键："0".."9"、"."、"00"、"backspace"。"""
    page.get_by_test_id(_keypad_testid(key)).click()


def amount_text(page: Page) -> Optional[str]:
    """金额显示区当前的金额文本（未格式化：没有千分位；还没敲是空串）。"""
    return page.get_by_test_id("amount-display").get_attribute("data-value")


def wait_amount_text(page: Page, expected: str, *, timeout: int = 4000) -> bool:
    """等金额文本变成 expected；超时返回 False（让断言记失败，而不是抛异常打断后面的用例）。"""
    try:
        page.wait_for_function(
            "(v) => { const el = document.querySelector('[data-testid=\"amount-display\"]'); return !!el && el.getAttribute('data-value') === v; }",
            arg=expected,
            timeout=timeout,
        )
        return True
    except PlaywrightTimeoutError:
        return False


def pick_category(page: Page, name: str) -> None:
    """点分类网格里名为 name 的分类（在第二页时先自动翻过去）。"""
    selector = f'[data-testid="category-tile"][data-name="{name}"]'
    page.locator(selector).click()
    page.wait_for_function("(sel) => document.querySelector(sel)?.getAttribute('data-selected') === 'true'", arg=selector)


def pick_ledger(page: Page, name: str) -> None:
    """账本行 → 账本菜单 → 选名为 name 的账本。"""
    page.get_by_test_id("ledger-row").click()
    menu = page.get_by_test_id("ledger-menu")
    menu.wait_for(state="visible")
    menu.get_by_test_id("ledger-option").filter(has_text=name).click()
    menu.wait_for(state="detached")


def pick_channel(page: Page, name: str) -> None:
    """支付渠道行 → 支付渠道菜单 → 选名为 name 的渠道。"""
    page.get_by_test_id("channel-row").click()
    menu = page.get_by_test_id("channel-menu")
    menu.wait_for(state="visible")
    menu.get_by_test_id("channel-option").filter(has_text=name).click()
    menu.wait_for(state="detached")


def save_bill_via_sheet(
    page: Page,
    *,
    amount: Union[int, float, str, Decimal],
    ledger: str,
    bill_type: int = BILL_TYPE_EXPENSE,
    category: Optional[str] = None,
    channel: Optional[str] = None,
    note: Optional[str] = None,
    when: Optional[datetime] = None,
) -> None:
    """经「记一笔」Sheet 完整保存一笔账单（数据来源为手动录入）：升起 Sheet → 切类型 → 键盘输入金额 → 选分类 / 账本 / 支付渠道 →
    时间 → 备注 → 保存，等 Sheet 收起。category / channel 缺省用默认项；when 是 naive 本地时间（应用时区墙上时间），缺省为当前时间。"""
    sheet = open_add_bill_sheet(page)
    if bill_type == BILL_TYPE_INCOME:
        sheet.get_by_test_id("addbill-type").get_by_test_id("segment").filter(has_text="收入").click()
        wait_attr(page, ADDBILL_SHEET_SELECTOR, "data-type", "income")

    press_keys(page, str(amount))
    if category:
        pick_category(page, category)
    pick_ledger(page, ledger)
    if channel:
        pick_channel(page, channel)
    if when is not None:
        sheet.get_by_test_id("addbill-time").fill(when.strftime("%Y-%m-%dT%H:%M"))
    if note:
        field = sheet.get_by_test_id("addbill-note")
        field.fill(note)
        field.press("Enter")  # 回车收起系统键盘（change 事件在失焦时才提交到服务端）
        wait_attr(page, ADDBILL_SHEET_SELECTOR, "data-typing", None)

    sheet.get_by_test_id("addbill-save").click()
    sheet.wait_for(state="detached")


# ======================================================================
# 稳定签名的造数入口
#
# 调用方只依赖这里的函数签名；哪个 ticket 改了对应界面，就由哪个 ticket 改这里的内部实现：
#   create_ledger：经「账本」页右上角「＋」→ 底部 Sheet（05 号重做后）
#   create_bill  ：传 page 时经「记一笔」Sheet 保存（15 号起；账本必填、数据来源为手动录入）；
#                  不传 page 仍直接写本地库——保留给历史无账本账单、非手动来源（导入 / 语音 / 短信）与需要精确造时间点的数据
# ======================================================================


def create_ledger(page: Page, name: Optional[str] = None) -> str:
    """经界面创建账本（账本页右上角「＋」→ 新建 Sheet → 保存），返回账本名称（带时间戳的唯一名称）。"""
    name = name or unique_name("E2E账本")
    open_app(page, "/ledgers")
    page.get_by_test_id("ledger-add").click()
    page.get_by_test_id("ledger-sheet").wait_for(state="visible")
    page.get_by_test_id("ledger-name").fill(name)
    page.get_by_test_id("ledger-save").click()
    # Sheet 收起（保存成功）后账本出现在列表里
    page.get_by_test_id("ledger-sheet").wait_for(state="detached")
    page.get_by_test_id("ledger-cell").filter(has_text=name).first.wait_for(state="visible")
    return name


def give_home_a_ledger(page: Page, db: Optional[LocalDb] = None) -> str:
    """新用户的首页只有「新建账本」引导；给当前登录用户直接造一个账本，再下拉刷新首页让它重新加载，
    首页就会出现「本月预算 / 最近账单」区域（含「最近账单 → 全部」入口 home-recent-all）。
    要求当前停在首页的 Tab 上。返回账本 Id。"""
    db = db or LocalDb()
    user_id = page.evaluate("() => localStorage.getItem('userId')")
    ledger_id = db.seed_ledger(user_id, unique_name("首页账本"))
    pull_refresh(page, "home-ptr")
    active_root(page).get_by_test_id("home-recent-all").wait_for(state="visible")
    return ledger_id


def create_bill(
    login_info: LoginInfo,
    *,
    amount: Union[int, float, str, Decimal] = 10,
    ledger: Optional[str] = None,
    bill_type: int = BILL_TYPE_EXPENSE,
    note: Optional[str] = None,
    when: Optional[datetime] = None,
    source: int = SOURCE_MANUAL,
    db: Optional[LocalDb] = None,
    page: Optional[Page] = None,
) -> str:
    """为已登录用户造一笔账单，返回账单 Id。ledger 为账本名称（None 表示历史无账本账单）。

    note 缺省时自动生成带时间戳的唯一备注，方便只断言自己创建的数据。

    传 page：经「记一笔」Sheet 完整走一遍界面（键盘输入金额 → 选账本 → 保存），要求当前在 Tab 根页面；
    只适用于「有账本的手动录入账单」（Sheet 要求账本必填，数据来源恒为手动录入），否则抛 ValueError。
    不传 page：直接写本地库——历史无账本账单、非手动来源与需要精确造时间点的数据都走这条路。
    """
    db = db or LocalDb()
    if page is not None:
        if ledger is None:
            raise ValueError("记一笔 Sheet 要求账本必填：历史无账本账单请不传 page（直接写库）")
        if source != SOURCE_MANUAL:
            raise ValueError("记一笔 Sheet 保存的账单数据来源恒为手动录入：其它来源请不传 page（直接写库）")
        note = note or unique_name("E2E账单")
        save_bill_via_sheet(page, amount=amount, ledger=ledger, bill_type=bill_type, note=note, when=when)
        saved = db.bills_by_note(note)
        if len(saved) != 1:
            raise RuntimeError(f"经记一笔 Sheet 保存后，本机库里按备注 {note!r} 应当恰有 1 笔账单，实际 {len(saved)} 笔")
        return saved[0]["Id"]

    user_id = db.user_id(login_info.phone)
    ledger_id = db.ledger_id_by_name(user_id, ledger) if ledger else None
    return db.seed_bill(
        user_id,
        amount=amount,
        ledger_id=ledger_id,
        bill_type=bill_type,
        note=note or unique_name("E2E账单"),
        when=when,
        source=source,
    )


# ======================================================================
# 「渲染档位」假实现（23 号）
#
# 真机上「要不要把毛玻璃降级成不透明」由原生宿主判定（Android 低端设备 / iOS 系统「降低透明度」）；浏览器里没有这个信号。
# server.ps1 打开 E2E:FakeCapture 后，Web.Server 把 IRenderingProfileService 换成 server/FinanceTracker.Web.Server/E2ERendering.cs 里的假实现，
# 由 E2E 经 POST /__e2e/rendering 设置判定结果、GET 观察——整条「宿主判定 → UiRuntime → <html data-ft-solid> → 设计令牌」链路因此可验收。
# 状态是整个进程共享的：用到它的脚本开始前与结束后各 set_rendering(False) 一次。
# ======================================================================

RENDERING_URL = f"{BASE_URL}/__e2e/rendering"


def set_rendering(solid: bool) -> dict:
    """设置假实现的判定结果（True = 宿主判定应当降级为不透明），返回服务端当前状态 {"solid": bool}。
    页面里已经打开的 UiRuntime 会即时跟随（触发了变化事件）。"""
    request = urllib.request.Request(
        RENDERING_URL,
        data=json.dumps({"solid": solid}).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=10) as response:
        return json.loads(response.read().decode("utf-8"))


def rendering_state() -> dict:
    with urllib.request.urlopen(RENDERING_URL, timeout=10) as response:
        return json.loads(response.read().decode("utf-8"))


# ======================================================================
# 错误日志文件（22 号）：造数据 / 清理
# ======================================================================

#: Web.Server 为 E2E 暴露日志目录的路径（server.ps1 用 E2E__LogDirectory 把日志页读取的目录指到一个专用的临时目录）
LOG_DIRECTORY_ROUTE = "/__e2e/log-directory"


def log_directory() -> Path:
    """Web.Server 的「错误日志」页读取的目录（专用临时目录，不是开发机上真实的日志目录）。

    必须用 tests/e2e/server.ps1 启动 Web.Server（它设置 E2E__LogDirectory）；手动 dotnet run 的服务没有这个入口，这里给出指引并以退出码 2 结束。
    """
    try:
        with urllib.request.urlopen(f"{BASE_URL}{LOG_DIRECTORY_ROUTE}", timeout=5) as response:
            return Path(json.loads(response.read().decode("utf-8"))["directory"])
    except Exception as exc:  # noqa: BLE001 - 任何异常都表示服务没带 E2E 日志目录启动
        print(f"[ENV] 取不到 E2E 日志目录（{BASE_URL}{LOG_DIRECTORY_ROUTE}：{exc}）")
        print("[ENV] 错误日志页的 E2E 要求 Web.Server 由 tests/e2e/server.ps1 启动（它设置 E2E__LogDirectory，日志页才读专用目录）")
        sys.exit(2)


def log_entry(
    when: datetime,
    template: str = "同步失败",
    *,
    level: str = "Error",
    source: Optional[str] = "FinanceTracker.Core.Services.SyncService",
    exception: Optional[str] = None,
    properties: Optional[dict] = None,
) -> dict:
    """一条日志（Serilog CompactJsonFormatter 的字段）。when 是 naive 本地时间；template 里的 {名字} 由 properties 代入。"""
    return {
        "when": when,
        "template": template,
        "level": level,
        "source": source,
        "exception": exception,
        "properties": properties or {},
    }


class LogFiles:
    """错误日志页 E2E 的造数据：按本地日期写 app-yyyyMMdd.log（每行一条 CompactJson），与应用真实的日志文件同一格式。

    只动 E2E 专用目录里的 app-*.log；目录里别的文件（例如验证「清除只删日志文件」时放的）不会被清掉。
    """

    def __init__(self) -> None:
        self.directory = log_directory()
        self.directory.mkdir(parents=True, exist_ok=True)

    def files(self) -> "list[Path]":
        return sorted(self.directory.glob("app-*.log"))

    def clear(self) -> None:
        for file in self.files():
            file.unlink()

    @staticmethod
    def _line(entry: dict) -> str:
        utc = entry["when"].astimezone(timezone.utc)  # naive datetime 按本机时区解释
        record: dict = {"@t": utc.strftime("%Y-%m-%dT%H:%M:%S.") + f"{utc.microsecond:06d}0Z", "@mt": entry["template"]}
        if entry["level"] != "Information":  # CompactJsonFormatter 只在不是 Information 时写 @l
            record["@l"] = entry["level"]
        if entry["exception"]:
            record["@x"] = entry["exception"]
        record.update(entry["properties"])
        if entry["source"]:
            record["SourceContext"] = entry["source"]
        return json.dumps(record, ensure_ascii=False)

    def write(self, entries: "Iterable[dict]", *, extra_lines: "Optional[dict[str, list[str]]]" = None) -> None:
        """把日志写进对应日期的文件（先清空 app-*.log）。extra_lines：{文件日期 yyyyMMdd: [原样追加的行]}，用来造损坏行。"""
        self.clear()
        by_day: "dict[str, list[str]]" = {}
        for entry in entries:
            by_day.setdefault(entry["when"].strftime("%Y%m%d"), []).append(self._line(entry))
        for day, lines in (extra_lines or {}).items():
            by_day.setdefault(day, []).extend(lines)
        for day, lines in by_day.items():
            (self.directory / f"app-{day}.log").write_text("\n".join(lines) + "\n", encoding="utf-8")


# ======================================================================
# 断言与退出码
# ======================================================================


class TestRun:
    """逐项记录断言，最后统一给出结果与退出码（0 通过 / 1 失败）。"""

    __test__ = False  # 避免被 pytest 当作测试类收集

    def __init__(self, name: str) -> None:
        self.name = name
        self.results: list[tuple[str, bool, str]] = []
        #: 被跳过的检查（环境不具备条件）：不算通过也不算失败，但收尾时要让它露出来，免得「全绿」被误当成「全都验过」
        self.skipped: list[tuple[str, str]] = []

    def check(self, label: str, ok: object, detail: str = "") -> bool:
        passed = bool(ok)
        self.results.append((label, passed, detail))
        suffix = f"  ({detail})" if detail and not passed else ""
        print(f"[{'PASS' if passed else 'FAIL'}] {label}{suffix}", flush=True)
        return passed

    def skip(self, label: str, reason: str) -> None:
        self.skipped.append((label, reason))
        print(f"[SKIP] {label}  ({reason})", flush=True)

    def check_clean(self, errors: ErrorCollector, label: str = "无页面异常 / 控制台错误 / 失败请求") -> bool:
        ok = errors.is_clean()
        self.check(label, ok, "\n" + errors.summary() if not ok else "")
        return ok

    @property
    def failed(self) -> "list[str]":
        return [label for label, passed, _ in self.results if not passed]

    def finish(self) -> int:
        failed = self.failed
        print(f"=== {self.name}: {len(self.results) - len(failed)}/{len(self.results)} 通过 ===")
        if self.skipped:
            print(f"    （另有 {len(self.skipped)} 项被跳过，没有验证：{'；'.join(label for label, _ in self.skipped)}）")
        print(f"E2E {self.name.upper()}:", "PASSED" if not failed else "FAILED")
        for label in failed:
            print(f"  - 失败项: {label}")
        return 0 if not failed else 1


def local_now_text() -> str:
    """调试输出用的本地时间文本。"""
    return datetime.now().strftime("%Y-%m-%d %H:%M:%S")


def days_ago(days: float, hour: int = 12) -> datetime:
    """naive 本地时间：days 天前的 hour 点（造跨天/跨月账单用）。"""
    base = datetime.now().replace(hour=hour, minute=0, second=0, microsecond=0)
    return base - timedelta(days=days)
