# -*- coding: utf-8 -*-
"""
分类管理 / 支付渠道管理两个页面共用的 E2E 检查（iOS UI 重构 · 14 号 ticket）。

两个页面结构同构：「自定义」「预设」两个分组、「默认」徽标、点按进入编辑 Sheet（含设为默认 / 删除）、左滑编辑 / 删除、
预设项点按弹出只含「设为 / 取消默认」的菜单、右上角「＋」与分组末尾的「新建…」行。行为一致的部分在这里按 Spec 参数化实现，
页面独有的部分（分类的支出 / 收入分段与账单类型、共用的新建分类面板）留在各自的脚本里。

本文件不是用例（文件名不以 _test 结尾，run_all 不会单独运行它）。
"""
import re
from dataclasses import dataclass
from typing import Callable, Optional

from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

from e2e_base import (
    GLASS_LAYERS_JS,
    PHONE_VIEWPORT,
    LocalDb,
    TestRun,
    active_root,
    active_tab,
    center_of,
    open_app,
    open_tab,
    shot,
    swipe,
    swipe_by,
    tap,
    unique_name,
    wait_animations_settled,
    wait_attr,
    wait_tab_bar,
    wait_url,
)

SCREEN_W = PHONE_VIEWPORT["width"]

SHELL_JS = """(pageId) => {
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
  const page = document.querySelector(`[data-testid="${pageId}"]`);
  return {
    bar: rect(bar),
    barBg: bar && getComputedStyle(bar).backgroundColor,
    pageBg: page && getComputedStyle(page).backgroundColor,
    expectedBg,
    page: rect(page),
    back: rect(document.querySelector('[data-testid="nav-back"]')),
    title: rect(title),
    titleTag: title && title.tagName,
    overflowX: document.documentElement.scrollWidth - document.documentElement.clientWidth,
  };
}"""

# 一组里的所有行：标题、右值、是否有默认徽标、图标类名、色块配色
ROWS_JS = """([group, cellId]) => {
  const g = document.querySelector(`[data-testid="${group}"]`);
  if (!g) return null;
  return [...g.querySelectorAll(`[data-testid="${cellId}"]`)].map((el) => {
    const tile = el.querySelector('.ft-tile');
    const icon = el.querySelector('.ft-tile i');
    const tone = tile ? [...tile.classList].find((c) => c.startsWith('ft-tile--') && c !== 'ft-tile--soft') : '';
    return {
      title: (el.querySelector('.ft-cell__title') || {}).textContent || '',
      value: (el.querySelector('.ft-cell__value') || {}).textContent || '',
      badge: !!el.querySelector('[data-testid="badge-default"]'),
      icon: icon ? [...icon.classList].find((c) => c.startsWith('mdi-')) : '',
      tone: tone ? tone.replace('ft-tile--', '') : '',
    };
  });
}"""

# 自定义分组里各子节点的顺序：行用标题，「新建…」行用 +add
ORDER_JS = """([group, cellId]) => {
  const g = document.querySelector(`[data-testid="${group}"]`);
  if (!g) return null;
  return [...g.querySelectorAll('.ft-group__body > *')].map((el) => {
    const cell = el.querySelector(`[data-testid="${cellId}"]`);
    return cell ? (cell.querySelector('.ft-cell__title') || {}).textContent : '+add';
  });
}"""

# 图标选择器里每个候选图标是否真的有字形（字体里没有的类名，::before 的 content 是 none / normal）
GLYPH_JS = """(selector) => [...document.querySelectorAll(selector)].map((i) => {
  const content = getComputedStyle(i, '::before').content;
  return { icon: [...i.classList].find((c) => c.startsWith('mdi-')), ok: !!content && content !== 'none' && content !== 'normal' };
})"""

HIT_JS = """([x, y, selector]) => {
  const el = document.elementFromPoint(x, y);
  return !!el && !!el.closest(selector);
}"""


@dataclass(frozen=True)
class Spec:
    """一个管理页的差异点：测试钩子名、文案、造数 / 查库的适配函数。"""

    key: str  # category | channel：多数 data-testid 是 {key}-xxx
    noun: str  # 分类 | 支付渠道
    entry_label: str  # 「我的」里入口行的文字
    entry_id: str  # profile-entry-{entry_id}
    path: str  # /categories | /payment-channels
    page_id: str  # categories-page | payment-channels-page
    title: str  # 导航栏标题
    group_custom: str  # custom-categories | custom-channels
    group_preset: str  # preset-categories | preset-channels
    default_icon: str  # mdi-tag | mdi-credit-card
    icon_count: int  # 图标网格里的候选数
    icon_a: str  # 测试里点选的两个候选图标（必须在该页的候选里，且不是默认图标）
    icon_b: str
    add_label: str  # 「新建分类」 | 「新建支付渠道」
    preset_footer: str
    preset_menu_message: str
    preset_menu_label: str  # 预设项菜单里「设为默认…」那一项（未默认时）
    placeholder: str  # 名称输入框占位
    seed: Callable  # (db, user_id, name, **kw) -> item_id
    row: Callable  # (db, item_id) -> dict | None
    default_id: Callable  # (db, user_id) -> item_id | None   （分类取的是支出默认）
    customs: Callable  # (db, user_id) -> list[dict]
    visible_total: Callable  # (db, user_id) -> int
    seed_bill: Callable  # (db, user_id, item_id, ledger_id, note) -> None   造一笔使用该条目的账单
    preset_id: Callable  # (db, name) -> item_id
    extra_row_attrs: tuple = ()  # 行 DOM 上额外要核对的 data 属性（暂无）

    def t(self, suffix: str) -> str:
        return f"{self.key}-{suffix}"

    def delete_title(self, name: str) -> str:
        return f"删除{self.noun}「{name}」？"

    @property
    def created_toast(self) -> str:
        return f"{self.noun}创建成功"

    @property
    def updated_toast(self) -> str:
        return f"{self.noun}更新成功"

    @property
    def deleted_toast(self) -> str:
        return f"{self.noun}删除成功"

    @property
    def blocked_toast(self) -> str:
        return f"该{self.noun}下有账单，无法删除"

    @property
    def default_on_toast(self) -> str:
        return f"已设置为默认{self.noun}"

    @property
    def default_off_toast(self) -> str:
        return f"已取消默认{self.noun}"


# ---------------------------------------------------------------------------------------------- 小工具


def toast(page, text):
    """全局 Toast（MASA 排队消息容器）里包含指定文字的那一条。"""
    return page.locator(".m-enqueued-snackbars").get_by_text(text)


def wait_toast(page, text) -> None:
    toast(page, text).first.wait_for(state="visible")


def cell(page, spec: Spec, name: str):
    return page.get_by_test_id(spec.t("cell")).filter(has_text=name)


def preset_cell(page, name: str):
    return page.get_by_test_id("preset-cell").filter(has_text=name)


def row_selector(spec: Spec, item_id: str) -> str:
    # 页面渲染的 Guid 是小写；库里是大写
    return f'[data-testid="{spec.t("row")}"][data-{spec.key}-id="{item_id.lower()}"]'


def row_of(page, spec: Spec, item_id: str):
    return page.locator(row_selector(spec, item_id))


def sheet(page, spec: Spec):
    return page.get_by_test_id(spec.t("sheet"))


def wait_sheet(page, spec: Spec) -> None:
    sheet(page, spec).wait_for(state="visible")
    wait_animations_settled(page)


def wait_sheet_gone(page, spec: Spec) -> None:
    sheet(page, spec).wait_for(state="detached")


def close_sheet(page, spec: Spec) -> None:
    page.get_by_test_id("sheet-close").click()
    wait_sheet_gone(page, spec)


def rows(page, spec: Spec, which: str):
    """一组里的所有行（which = custom | preset）。"""
    if which == "custom":
        return page.evaluate(ROWS_JS, [spec.group_custom, spec.t("cell")])
    return page.evaluate(ROWS_JS, [spec.group_preset, "preset-cell"])


def custom_order(page, spec: Spec):
    return page.evaluate(ORDER_JS, [spec.group_custom, spec.t("cell")])


def row_info(page, spec: Spec, name: str, which: str = "custom"):
    for r in rows(page, spec, which) or []:
        if r["title"] == name:
            return r
    return None


def wait_check(run: TestRun, label: str, page, predicate_js: str, *, arg=None, timeout: int = 8000) -> bool:
    """等到页面内谓词为真就记通过，超时记失败（不抛异常，便于一次看到所有失败项）。"""
    try:
        page.wait_for_function(predicate_js, arg=arg, timeout=timeout)
        return run.check(label, True)
    except PlaywrightTimeoutError:
        return run.check(label, False, "等待超时")


def wait_row(page, spec: Spec, name: str, which: str = "custom") -> None:
    """等某名称的行出现在列表里（保存后列表要重新加载一次）。"""
    group = spec.group_custom if which == "custom" else spec.group_preset
    cell_id = spec.t("cell") if which == "custom" else "preset-cell"
    page.wait_for_function(
        """([group, cellId, name]) => [...document.querySelectorAll(`[data-testid="${group}"] [data-testid="${cellId}"] .ft-cell__title`)]
             .some((el) => el.textContent === name)""",
        arg=[group, cell_id, name],
        timeout=8000,
    )


def wait_row_gone(page, spec: Spec, name: str) -> None:
    page.wait_for_function(
        """([group, cellId, name]) => ![...document.querySelectorAll(`[data-testid="${group}"] [data-testid="${cellId}"] .ft-cell__title`)]
             .some((el) => el.textContent === name)""",
        arg=[spec.group_custom, spec.t("cell"), name],
        timeout=8000,
    )


def covers(page, point, selector) -> bool:
    return bool(page.evaluate(HIT_JS, [point[0], point[1], selector]))


def go_to_page(page, spec: Spec) -> None:
    """从「我的」点入口进入管理页（压栈二级页面），等 Tab 栏隐藏、页面出现。"""
    open_tab(page, "我的")
    active_root(page).get_by_test_id(f"profile-entry-{spec.entry_id}").click()
    wait_url(page, rf"^{spec.path}$")
    wait_tab_bar(page, visible=False)
    page.get_by_test_id(spec.page_id).wait_for(state="visible")
    page.get_by_test_id(spec.t("add-row")).wait_for(state="visible")
    wait_animations_settled(page)


def reload_page(page, spec: Spec) -> None:
    """整页重新进入管理页（造数 / 改库之后用：页面常驻不会自己感知库里的变化）。"""
    open_app(page, spec.path)
    page.get_by_test_id(spec.page_id).wait_for(state="visible")
    page.get_by_test_id(spec.t("add-row")).wait_for(state="visible")
    wait_animations_settled(page)


def go_back_to_profile(page) -> None:
    page.get_by_test_id("nav-back").click()
    wait_url(page, r"^/profile$")
    wait_tab_bar(page)


def swipe_open(page, spec: Spec, item_id: str, *, steps: int = 10) -> None:
    """触摸左滑展开某一行（真实触摸事件，经 pointer 事件交给 ui.js）；停住一小会儿再松手的缘由见账本页测试的同名函数。"""
    target = row_of(page, spec, item_id).get_by_test_id(spec.t("cell"))
    swipe_by(page, target, -170, 0, steps=steps, duration_ms=220, release_hold_ms=150)
    wait_attr(page, row_selector(spec, item_id), "data-open", "true")
    wait_animations_settled(page)
    # 松手后 350ms 内行内的 click 会被吞掉（防止松手误触单元格，见 ui.js 的 CLICK_SUPPRESS_MS）；真人不会这么快，脚本要等过去
    page.wait_for_timeout(400)


def open_create(page, spec: Spec, via: str = "nav") -> None:
    """打开新建 Sheet：via = nav（右上角「＋」）| row（分组末尾的「新建…」行）。"""
    page.get_by_test_id(spec.t("add") if via == "nav" else spec.t("add-row")).click()
    wait_sheet(page, spec)


def open_edit(page, spec: Spec, name: str) -> None:
    cell(page, spec, name).first.click()
    wait_sheet(page, spec)


def pressed_icons(page) -> "list[str]":
    return page.evaluate(
        """() => [...document.querySelectorAll('[data-testid="icon-picker"] .ft-iconpick__item[aria-pressed="true"]')]
             .map((b) => b.dataset.icon)"""
    )


def preview_icon(page) -> str:
    return page.evaluate(
        """() => { const i = document.querySelector('.ft-sheet-preview i'); return i ? [...i.classList].find((c) => c.startsWith('mdi-')) : ''; }"""
    )


def pick_icon(page, icon: str) -> None:
    page.locator(f'[data-testid="icon-picker"] .ft-iconpick__item[data-icon="{icon}"]').click()
    page.wait_for_function(
        """(icon) => { const b = document.querySelector(`[data-testid="icon-picker"] .ft-iconpick__item[data-icon="${icon}"]`);
             return !!b && b.getAttribute('aria-pressed') === 'true'; }""",
        arg=icon,
    )


def fill_form(page, spec: Spec, name: Optional[str] = None, sort: Optional[int] = None) -> None:
    if name is not None:
        page.get_by_test_id(spec.t("name")).fill(name)
    if sort is not None:
        page.get_by_test_id(spec.t("sort")).fill(str(sort))


def fill_overlong(page, spec: Spec, length: int = 51) -> None:
    """用脚本给名称框塞超长文字（fill 走输入法路径，会被 maxlength 截断；粘贴 / 程序赋值不受限，服务端校验仍要兜住）。"""
    page.get_by_test_id(spec.t("name")).evaluate(
        "(el, n) => { el.value = 'x'.repeat(n); el.dispatchEvent(new Event('input', { bubbles: true })); }", length
    )


def save_form(page, spec: Spec) -> None:
    page.get_by_test_id(spec.t("save")).click()


# ---------------------------------------------------------------------------------------------- A. 外壳与预设分组


def check_shell(run: TestRun, page, db: LocalDb, spec: Spec, preset_names: "list[str]") -> None:
    """新用户（没有自定义项）：二级页面外壳、预设分组（只读、标注「预设」）、自定义分组只有「新建…」行；返回。"""
    go_to_page(page, spec)
    n = spec.noun

    run.check(f"进入{n}页：Tab 栏隐藏", page.get_by_test_id("tab-bar").is_hidden())
    run.check(f"导航栏标题是 h1「{spec.title}」", page.get_by_test_id("nav-title").inner_text() == spec.title)
    info = page.evaluate(SHELL_JS, spec.page_id)
    run.check("标题是语义化的 h1", info["titleTag"] == "H1", str(info["titleTag"]))
    run.check("导航栏高 44，贴着屏幕顶（无状态栏安全区时）", abs(info["bar"]["height"] - 44) <= 1 and abs(info["bar"]["top"]) <= 1, str(info["bar"]))
    run.check("标题水平居中", abs((info["title"]["left"] + info["title"]["right"]) / 2 - SCREEN_W / 2) <= 1, str(info["title"]))
    add = page.get_by_test_id(spec.t("add"))
    box = add.bounding_box()
    run.check("返回与「＋」按钮点击区域 ≥ 44 × 44", min(info["back"]["width"], info["back"]["height"], box["width"], box["height"]) >= 44, f"{info['back']} {box}")
    run.check("返回在左、「＋」在右", info["back"]["left"] < SCREEN_W / 2 < box["x"])
    run.check(f"「＋」按钮有读屏名称「新建{n}」", add.get_attribute("aria-label") == f"新建{n}")
    run.check("导航栏背景与内容区背景 = 页面背景令牌（不透明）", info["barBg"] == info["expectedBg"] and info["pageBg"] == info["expectedBg"], f"{info['barBg']} {info['pageBg']} vs {info['expectedBg']}")
    run.check("没有横向溢出", info["overflowX"] <= 0, str(info["overflowX"]))
    glass = page.evaluate(GLASS_LAYERS_JS)
    run.check("每屏玻璃层 ≤ 3（导航按钮 2 个）", glass <= 3, str(glass))

    # 预设分组
    presets = rows(page, spec, "preset")
    run.check(f"「预设」组恰好是 {len(preset_names)} 个预设{n}，顺序与预设排序一致", [r["title"] for r in presets] == preset_names, str([r["title"] for r in presets]))
    run.check("每个预设项右侧标注「预设」", all(r["value"] == "预设" for r in presets))
    run.check("新用户没有任何默认徽标", not any(r["badge"] for r in presets))
    run.check("预设项都带图标色块，且图标类名合法", all(r["icon"].startswith("mdi-") and r["tone"] for r in presets), str(presets[:2]))
    run.check("预设项是只读行：没有左滑托盘、没有箭头", page.evaluate(
        """(group) => { const g = document.querySelector(`[data-testid="${group}"]`);
             return g.querySelectorAll('.ft-swipe').length === 0 && g.querySelectorAll('.ft-cell__chevron').length === 0; }""",
        spec.group_preset))
    footer = page.evaluate("(group) => (document.querySelector(`[data-testid=\"${group}\"] .ft-group__footer`) || {}).textContent || ''", spec.group_preset)
    run.check("预设组脚注说明「只读、可设为默认」", footer == spec.preset_footer, footer)

    # 自定义分组：只有「新建…」行
    run.check("「自定义」组：新用户只有一行「新建…」，没有自定义项", custom_order(page, spec) == ["+add"], str(custom_order(page, spec)))
    add_row = page.get_by_test_id(spec.t("add-row"))
    run.check(f"「{spec.add_label}」行文字与读屏名称", add_row.inner_text().strip() == spec.add_label, add_row.inner_text())
    run.check("「新建…」行的标题是品牌色", page.evaluate(
        """(id) => { const t = document.querySelector(`[data-testid="${id}"] .ft-cell__title`);
             const probe = document.createElement('i'); probe.style.color = 'var(--ft-brand)'; document.body.appendChild(probe);
             const want = getComputedStyle(probe).color; probe.remove(); return getComputedStyle(t).color === want; }""",
        spec.t("add-row")))
    has_footer = page.evaluate("(group) => !!document.querySelector(`[data-testid=\"${group}\"] .ft-group__footer`)", spec.group_custom)
    run.check("没有自定义项时「自定义」组不显示左滑提示脚注", not has_footer)
    shot(page, f"{spec.key}-shell")

    # 返回：按钮 → 回到「我的」；边缘右滑
    go_back_to_profile(page)
    run.check("点返回回到「我的」，Tab 栏恢复", active_tab(page) == "profile")
    go_to_page(page, spec)
    swipe(page, (4, 420), (260, 420), steps=12, duration_ms=240)
    wait_url(page, r"^/profile$")
    wait_tab_bar(page)
    run.check("左边缘右滑返回仍然有效", active_tab(page) == "profile")
    go_to_page(page, spec)


# ---------------------------------------------------------------------------------------------- B. 新建


def check_create(run: TestRun, page, db: LocalDb, user_id: str, spec: Spec, *, extra_db_checks: Optional[Callable] = None) -> "tuple[str, str]":
    """新建一个自定义项，返回 (名称, 库里的 Id)。"""
    n = spec.noun
    before = len(spec.customs(db, user_id))

    open_create(page, spec, "nav")
    run.check(f"新建 Sheet 标题是「新建{n}」", page.get_by_test_id("sheet-title").inner_text() == f"新建{n}")
    run.check("新建时没有「设为默认 / 删除」", page.get_by_test_id(spec.t("default-switch")).count() == 0
              and page.get_by_test_id(spec.t("delete-cell")).count() == 0)
    run.check(f"名称输入框占位「{spec.placeholder}」，最长 50 个字", page.get_by_test_id(spec.t("name")).get_attribute("placeholder") == spec.placeholder
              and page.get_by_test_id(spec.t("name")).get_attribute("maxlength") == "50")
    run.check("没有表单错误提示", page.get_by_test_id(spec.t("form-error")).count() == 0)
    glass = page.evaluate(GLASS_LAYERS_JS)
    run.check("Sheet 打开时玻璃层仍 ≤ 3", glass <= 3, str(glass))

    count = page.locator('[data-testid="icon-picker"] .ft-iconpick__item').count()
    run.check(f"图标网格有 {spec.icon_count} 个候选，每个有读屏名称", count == spec.icon_count and page.evaluate(
        """() => [...document.querySelectorAll('[data-testid="icon-picker"] .ft-iconpick__item')].every((b) => (b.getAttribute('aria-label') || '').length > 0)"""),
        str(count))
    run.check(f"默认选中默认图标 {spec.default_icon}，预览同步", pressed_icons(page) == [spec.default_icon] and preview_icon(page) == spec.default_icon,
              f"{pressed_icons(page)} {preview_icon(page)}")
    rects = page.evaluate(
        """() => [...document.querySelectorAll('[data-testid="icon-picker"] .ft-iconpick__item')].map((b) => { const r = b.getBoundingClientRect(); return [r.width, r.height]; })""")
    run.check("每个图标按钮点击区域 ≥ 44 × 44", all(w >= 44 and h >= 44 for w, h in rects), str(min(rects)))

    # 名称校验
    save_form(page, spec)
    page.get_by_test_id(spec.t("form-error")).wait_for(state="visible")
    run.check(f"名称为空：提示「请输入{n}名称」，不保存", page.get_by_test_id(spec.t("form-error")).inner_text() == f"请输入{n}名称"
              and len(spec.customs(db, user_id)) == before)
    run.check("表单错误是 alert 语义", page.get_by_test_id(spec.t("form-error")).get_attribute("role") == "alert")
    fill_form(page, spec, "    ")
    page.get_by_test_id(spec.t("form-error")).wait_for(state="detached")
    save_form(page, spec)
    page.get_by_test_id(spec.t("form-error")).wait_for(state="visible")
    run.check("名称全是空格：同样拦截", page.get_by_test_id(spec.t("form-error")).inner_text() == f"请输入{n}名称" and len(spec.customs(db, user_id)) == before)
    fill_overlong(page, spec, 51)
    save_form(page, spec)
    wait_check(run, "名称超过 50 个字：提示最多 50 个字", page,
               """([id, text]) => (document.querySelector(`[data-testid="${id}"]`) || {}).innerText === text""",
               arg=[spec.t("form-error"), f"{n}名称最多 50 个字"])
    run.check("超长名称不会落库", len(spec.customs(db, user_id)) == before)
    fill_form(page, spec, "a")
    page.get_by_test_id(spec.t("form-error")).wait_for(state="detached")
    run.check("重新输入后错误提示消失", page.get_by_test_id(spec.t("form-error")).count() == 0)

    # 选图标：点选后高亮，同一时刻只有一个，预览同步
    pick_icon(page, spec.icon_a)
    run.check("点选图标：只有它被按下，预览同步", pressed_icons(page) == [spec.icon_a] and preview_icon(page) == spec.icon_a, f"{pressed_icons(page)} {preview_icon(page)}")
    shot(page, f"{spec.key}-sheet-create")

    name = unique_name(f"E2E{n}")
    fill_form(page, spec, name, 5)
    save_form(page, spec)
    wait_toast(page, spec.created_toast)
    wait_sheet_gone(page, spec)
    wait_row(page, spec, name)
    created = [c for c in spec.customs(db, user_id) if c["Name"] == name]
    run.check(f"保存：库里新增一条自定义{n}（名称、图标、排序、所有者、非预设）", len(created) == 1 and created[0]["Icon"] == spec.icon_a
              and created[0]["SortOrder"] == 5 and created[0]["IsPreset"] == 0 and created[0]["IsDeleted"] == 0
              and created[0]["UserId"].upper() == user_id.upper(), str(created))
    item_id = created[0]["Id"] if created else ""
    if extra_db_checks is not None and created:
        extra_db_checks(created[0])
    info = row_info(page, spec, name)
    run.check("新条目出现在「自定义」组：图标是所选，右侧是 0 笔，没有默认徽标", info is not None and info["icon"] == spec.icon_a
              and info["value"] == "0 笔" and not info["badge"], str(info))
    order = custom_order(page, spec)
    run.check("「新建…」行始终在自定义组末尾", order[-1] == "+add" and name in order, str(order))
    run.check(f"自定义{n}的色块配色与预览之外的列表一致（有配色类）", bool(info and info["tone"]), str(info))

    # 表单每次打开都是干净的
    open_create(page, spec, "row")
    run.check("再次打开新建 Sheet（经「新建…」行）：名称空、排序 0、图标回到默认", page.get_by_test_id(spec.t("name")).input_value() == ""
              and page.get_by_test_id(spec.t("sort")).input_value() == "0" and pressed_icons(page) == [spec.default_icon],
              f"{page.get_by_test_id(spec.t('name')).input_value()!r} {page.get_by_test_id(spec.t('sort')).input_value()!r} {pressed_icons(page)}")
    run.check("两个入口（右上角「＋」与「新建…」行）打开的是同一个 Sheet 标题", page.get_by_test_id("sheet-title").inner_text() == f"新建{n}")
    close_sheet(page, spec)
    return name, item_id


# ---------------------------------------------------------------------------------------------- C. 编辑与排序


def check_edit_and_order(run: TestRun, page, db: LocalDb, user_id: str, spec: Spec, first_name: str, first_id: str) -> "tuple[str, str, str]":
    """编辑第一个自定义项、再造第二个验证排序；返回 (第一个名称, 第二个名称, 第二个 Id)。"""
    n = spec.noun
    second_name = unique_name(f"排序{n}")
    second_id = spec.seed(db, user_id, second_name, sort_order=1, icon="mdi-star")
    reload_page(page, spec)

    order = custom_order(page, spec)
    run.check("自定义组按排序升序：排序 1 的在排序 5 的前面", order.index(second_name) < order.index(first_name), str(order))

    open_edit(page, spec, first_name)
    run.check(f"编辑 Sheet 标题是「编辑{n}」", page.get_by_test_id("sheet-title").inner_text() == f"编辑{n}")
    run.check("编辑时带出原名称、排序与图标", page.get_by_test_id(spec.t("name")).input_value() == first_name
              and page.get_by_test_id(spec.t("sort")).input_value() == "5" and pressed_icons(page) == [spec.icon_a],
              f"{page.get_by_test_id(spec.t('name')).input_value()!r} {page.get_by_test_id(spec.t('sort')).input_value()!r} {pressed_icons(page)}")
    run.check("编辑时出现「设为默认 / 删除」两个入口", page.get_by_test_id(spec.t("default-switch")).count() == 1
              and page.get_by_test_id(spec.t("delete-cell")).count() == 1)
    shot(page, f"{spec.key}-sheet-edit")

    new_name = unique_name(f"改名{n}")
    fill_form(page, spec, new_name, 0)
    pick_icon(page, spec.icon_b)
    save_form(page, spec)
    wait_toast(page, spec.updated_toast)
    wait_sheet_gone(page, spec)
    wait_row(page, spec, new_name)
    after = spec.row(db, first_id)
    run.check("保存：同一条记录被更新（名称、图标、排序），没有新增", after["Name"] == new_name and after["Icon"] == spec.icon_b and after["SortOrder"] == 0
              and after["IsDeleted"] == 0 and len([c for c in spec.customs(db, user_id) if c["Id"].upper() == first_id.upper()]) == 1, str(after))
    run.check("列表里旧名称消失、新名称出现，自定义项个数不变", row_info(page, spec, first_name) is None and row_info(page, spec, new_name) is not None
              and len([t for t in custom_order(page, spec) if t != "+add"]) == len(spec.customs(db, user_id)))
    order = custom_order(page, spec)
    run.check("排序改为 0 后，改名的条目排到排序 1 的前面", order.index(new_name) < order.index(second_name), str(order))
    info = row_info(page, spec, new_name)
    run.check("列表行的图标同步更新", info is not None and info["icon"] == spec.icon_b, str(info))

    # 历史数据：图标类名非法 → 列表回退默认图标；编辑保存时规范化为默认图标
    legacy_name = unique_name(f"历史{n}")
    legacy_id = spec.seed(db, user_id, legacy_name, sort_order=9, icon="legacy")
    reload_page(page, spec)
    info = row_info(page, spec, legacy_name)
    run.check("历史数据图标类名非法：列表回退默认图标，不渲染成空白", info is not None and info["icon"] == spec.default_icon, str(info))
    open_edit(page, spec, legacy_name)
    run.check("编辑时非法图标回退为默认图标（高亮默认图标，预览同步），不会留空", pressed_icons(page) == [spec.default_icon]
              and preview_icon(page) == spec.default_icon, f"{pressed_icons(page)} {preview_icon(page)}")
    save_form(page, spec)
    wait_toast(page, spec.updated_toast)
    wait_sheet_gone(page, spec)
    run.check("不改图标直接保存：库里规范化为默认图标", spec.row(db, legacy_id)["Icon"] == spec.default_icon, str(spec.row(db, legacy_id)))
    return new_name, second_name, second_id


# ---------------------------------------------------------------------------------------------- D. 设为默认


def check_default(run: TestRun, page, db: LocalDb, user_id: str, spec: Spec, first_name: str, first_id: str, second_name: str, second_id: str) -> None:
    n = spec.noun
    run.check("起点：没有默认项", spec.default_id(db, user_id) is None)

    # --- 自定义项：编辑 Sheet 里的开关
    open_edit(page, spec, first_name)
    switch = page.get_by_test_id(spec.t("default-switch"))
    run.check("开关是 switch 语义，初始为关", switch.get_attribute("role") == "switch" and switch.get_attribute("aria-checked") == "false")
    # 编辑中先改了名称与排序、还没保存，再切换默认（会让页面重读库并重绘）：正在输入的草稿不能被冲掉
    fill_form(page, spec, "草稿名称", 77)
    switch.click()
    wait_attr(page, f'[data-testid="{spec.t("default-switch")}"]', "aria-checked", "true")
    wait_toast(page, spec.default_on_toast)
    wait_row_badge(page, spec, first_name, True)
    run.check("打开开关：落库为默认，列表该行出现「默认」徽标（Sheet 不关闭）", (spec.default_id(db, user_id) or "").upper() == first_id.upper()
              and sheet(page, spec).count() == 1)
    run.check("切换默认会重读列表，但编辑中还没保存的名称与排序草稿不被冲掉",
              page.get_by_test_id(spec.t("name")).input_value() == "草稿名称" and page.get_by_test_id(spec.t("sort")).input_value() == "77",
              f"{page.get_by_test_id(spec.t('name')).input_value()!r} {page.get_by_test_id(spec.t('sort')).input_value()!r}")
    shot(page, f"{spec.key}-default-badge")
    switch.click()
    wait_attr(page, f'[data-testid="{spec.t("default-switch")}"]', "aria-checked", "false")
    wait_row_badge(page, spec, first_name, False)
    run.check("关闭开关：取消默认并移除徽标", spec.default_id(db, user_id) is None)
    close_sheet(page, spec)
    run.check("草稿没有保存：关闭 Sheet 后列表与库里仍是原名称", cell(page, spec, first_name).count() == 1
              and spec.row(db, first_id)["Name"] == first_name and spec.row(db, first_id)["SortOrder"] == 0, str(spec.row(db, first_id)))

    # --- 同一时刻只有一个默认
    open_edit(page, spec, first_name)
    page.get_by_test_id(spec.t("default-switch")).click()
    wait_row_badge(page, spec, first_name, True)
    close_sheet(page, spec)
    open_edit(page, spec, second_name)
    page.get_by_test_id(spec.t("default-switch")).click()
    wait_row_badge(page, spec, second_name, True)
    wait_row_badge(page, spec, first_name, False)
    badges = [r["title"] for r in rows(page, spec, "custom") + rows(page, spec, "preset") if r["badge"]]
    run.check("把另一个设为默认后，原来的徽标消失（只有一个默认）", badges == [second_name] and (spec.default_id(db, user_id) or "").upper() == second_id.upper(), str(badges))
    close_sheet(page, spec)
    open_edit(page, spec, first_name)
    run.check("重新打开原来那一个：开关为关（状态来自库，不是上次的残留）", page.get_by_test_id(spec.t("default-switch")).get_attribute("aria-checked") == "false")
    close_sheet(page, spec)

    # --- 预设项：点按弹出菜单，只能设为 / 取消默认
    preset = rows(page, spec, "preset")[0]["title"]
    preset_id = spec.preset_id(db, preset)
    preset_cell(page, preset).first.click()
    menu = page.get_by_test_id(spec.t("preset-sheet"))
    menu.wait_for(state="visible")
    wait_animations_settled(page)
    run.check("预设项点按弹出菜单（不打开编辑 Sheet）", sheet(page, spec).count() == 0)
    text = menu.inner_text()
    run.check("菜单标题是该预设项名称，说明预设项不能修改或删除", preset in text and spec.preset_menu_message in text, text)
    run.check(f"菜单里只有一项「{spec.preset_menu_label}」（外加取消）", menu.get_by_test_id("preset-default").inner_text().strip() == spec.preset_menu_label
              and menu.locator(".ft-actionsheet__item").count() == 1, menu.inner_text())
    shot(page, f"{spec.key}-preset-menu")
    page.get_by_test_id("actionsheet-cancel").click()
    menu.wait_for(state="detached")
    run.check("取消菜单：默认项不变（仍是第二个）", (spec.default_id(db, user_id) or "").upper() == second_id.upper())

    preset_cell(page, preset).first.click()
    menu.wait_for(state="visible")
    wait_animations_settled(page)
    menu.get_by_test_id("preset-default").click()
    wait_toast(page, spec.default_on_toast)
    wait_row_badge(page, spec, preset, True, which="preset")
    wait_row_badge(page, spec, second_name, False)
    run.check("预设项设为默认：落库、徽标出现在预设行，之前的自定义默认被取代", (spec.default_id(db, user_id) or "").upper() == preset_id.upper()
              and not row_info(page, spec, second_name)["badge"])
    preset_cell(page, preset).first.click()
    menu.wait_for(state="visible")
    wait_animations_settled(page)
    run.check("已是默认的预设项：菜单项文案变成「取消默认」", menu.get_by_test_id("preset-default").inner_text().strip() == "取消默认", menu.inner_text())
    menu.get_by_test_id("preset-default").click()
    wait_toast(page, spec.default_off_toast)
    wait_row_badge(page, spec, preset, False, which="preset")
    run.check("取消预设项的默认：落库清空、徽标消失", spec.default_id(db, user_id) is None)
    run.check("预设项始终只读：没有被改名、没有被删除", spec.row(db, preset_id)["Name"] == preset and spec.row(db, preset_id)["IsDeleted"] == 0)


def wait_row_badge(page, spec: Spec, name: str, expected: bool, which: str = "custom") -> None:
    group = spec.group_custom if which == "custom" else spec.group_preset
    cell_id = spec.t("cell") if which == "custom" else "preset-cell"
    page.wait_for_function(
        """([group, cellId, name, expected]) => {
          const el = [...document.querySelectorAll(`[data-testid="${group}"] [data-testid="${cellId}"]`)]
            .find((c) => (c.querySelector('.ft-cell__title') || {}).textContent === name);
          return !!el && (!!el.querySelector('[data-testid="badge-default"]') === expected);
        }""",
        arg=[group, cell_id, name, expected],
        timeout=8000,
    )


# ---------------------------------------------------------------------------------------------- E / F. 左滑与删除


def check_swipe_and_delete(run: TestRun, page, db: LocalDb, user_id: str, spec: Spec) -> None:
    n = spec.noun
    to_delete_name = unique_name(f"待删除{n}")
    to_delete = spec.seed(db, user_id, to_delete_name, sort_order=10)
    with_bills_name = unique_name(f"有账单{n}")
    with_bills = spec.seed(db, user_id, with_bills_name, sort_order=11)
    ledger = db.seed_ledger(user_id, unique_name("删除用账本"))
    spec.seed_bill(db, user_id, with_bills, ledger, unique_name("拦截删除用账单"))
    spec.seed_bill(db, user_id, with_bills, ledger, unique_name("拦截删除用账单"))
    via_sheet_name = unique_name(f"经Sheet删除{n}")
    via_sheet = spec.seed(db, user_id, via_sheet_name, sort_order=12)
    reload_page(page, spec)

    run.check("行右侧显示该条目下的账单数：没有账单「0 笔」，有账单「2 笔」",
              row_info(page, spec, to_delete_name)["value"] == "0 笔" and row_info(page, spec, with_bills_name)["value"] == "2 笔",
              f"{row_info(page, spec, to_delete_name)} {row_info(page, spec, with_bills_name)}")
    footer = page.evaluate("(group) => (document.querySelector(`[data-testid=\"${group}\"] .ft-group__footer`) || {}).textContent || ''", spec.group_custom)
    run.check("有自定义项时「自定义」组脚注提示可左滑、可点按", "左滑" in footer and "点按" in footer, footer)

    # --- 展开前：操作按钮被内容盖住，点不到
    target_row = row_of(page, spec, to_delete)
    run.check("展开前 data-open=false", target_row.get_attribute("data-open") == "false")
    cx, cy = center_of(page, target_row.get_by_test_id("swipe-delete"))
    run.check("展开前操作按钮点不到（被内容盖住）", not covers(page, (cx, cy), ".ft-swipe__tray"))
    run.check("展开前托盘 inert 且对读屏隐藏", page.evaluate(
        "(sel) => { const t = document.querySelector(sel + ' .ft-swipe__tray'); return t.inert === true && t.getAttribute('aria-hidden') === 'true'; }",
        row_selector(spec, to_delete)))

    # --- 纵向滑动不展开
    swipe_by(page, row_of(page, spec, to_delete).get_by_test_id(spec.t("cell")), 0, 90, steps=8, duration_ms=160)
    page.wait_for_timeout(250)
    run.check("纵向滑动不会展开行", row_of(page, spec, to_delete).get_attribute("data-open") == "false")

    # --- 左滑展开：「编辑」「删除」可点
    swipe_open(page, spec, to_delete)
    cx, cy = center_of(page, row_of(page, spec, to_delete).get_by_test_id("swipe-delete"))
    ex, ey = center_of(page, row_of(page, spec, to_delete).get_by_test_id("swipe-edit"))
    run.check("左滑后「编辑」「删除」都可点，「编辑」在「删除」左侧", covers(page, (cx, cy), ".ft-swipe__tray")
              and covers(page, (ex, ey), ".ft-swipe__tray") and ex < cx)
    labels = row_of(page, spec, to_delete).locator(".ft-swipe__tray").inner_text().replace("\n", " ")
    run.check("操作按钮文字：编辑 / 删除", "编辑" in labels and "删除" in labels, labels)
    shot(page, f"{spec.key}-swipe-open")

    # --- 展开时点内容：只收起，不进入编辑
    tap(page, row_of(page, spec, to_delete).get_by_test_id(spec.t("cell")))
    wait_attr(page, row_selector(spec, to_delete), "data-open", "false")
    page.wait_for_timeout(450)
    run.check("展开时点按内容只收起，不会打开编辑 Sheet", sheet(page, spec).count() == 0)

    # --- 左滑「编辑」→ 进入同一个编辑 Sheet
    swipe_open(page, spec, to_delete)
    tap(page, row_of(page, spec, to_delete).get_by_test_id("swipe-edit"))
    wait_sheet(page, spec)
    run.check("左滑点「编辑」打开编辑 Sheet，带出该条目", page.get_by_test_id("sheet-title").inner_text() == f"编辑{n}"
              and page.get_by_test_id(spec.t("name")).input_value() == to_delete_name)
    wait_attr(page, row_selector(spec, to_delete), "data-open", "false")
    run.check("点了操作按钮后这一行自动收起", row_of(page, spec, to_delete).get_attribute("data-open") == "false")
    close_sheet(page, spec)

    # --- 左滑「删除」→ Alert → 取消
    swipe_open(page, spec, to_delete)
    tap(page, row_of(page, spec, to_delete).get_by_test_id("swipe-delete"))
    alert = page.get_by_test_id(spec.t("delete-alert"))
    alert.wait_for(state="visible")
    title = page.get_by_test_id("alert-title").inner_text()
    run.check(f"删除确认 Alert 标题带名称：删除{n}「…」？", title == spec.delete_title(to_delete_name), title)
    run.check("Alert 是 alertdialog 语义，确认按钮是红色破坏性样式", alert.get_attribute("role") == "alertdialog")
    shot(page, f"{spec.key}-delete-alert")
    page.get_by_test_id("alert-cancel").click()
    alert.wait_for(state="detached")
    run.check("取消删除：仍在列表里，库里没删", spec.row(db, to_delete)["IsDeleted"] == 0 and cell(page, spec, to_delete_name).count() == 1)

    # --- 左滑「删除」→ 确认 → 软删除
    swipe_open(page, spec, to_delete)
    tap(page, row_of(page, spec, to_delete).get_by_test_id("swipe-delete"))
    alert.wait_for(state="visible")
    page.get_by_test_id("alert-confirm").click()
    wait_toast(page, spec.deleted_toast)
    wait_row_gone(page, spec, to_delete_name)
    alert.wait_for(state="detached")  # 退出动画期间遮罩仍会接住触摸
    run.check("确认删除：从列表消失，库里是软删除（IsDeleted=1，记录仍在）", spec.row(db, to_delete)["IsDeleted"] == 1
              and to_delete.upper() not in [c["Id"].upper() for c in spec.customs(db, user_id)])

    # --- 下面有账单：被拦截，保留
    swipe_open(page, spec, with_bills)
    tap(page, row_of(page, spec, with_bills).get_by_test_id("swipe-delete"))
    alert.wait_for(state="visible")
    page.get_by_test_id("alert-confirm").click()
    wait_toast(page, spec.blocked_toast)
    alert.wait_for(state="detached")
    run.check(f"{n}下有账单：提示无法删除，条目保留", spec.row(db, with_bills)["IsDeleted"] == 0 and cell(page, spec, with_bills_name).count() == 1)
    run.check("被拦截后仍停留在本页（没有被登出）", page.evaluate("() => !!localStorage.getItem('userId')") and re.search(rf"{spec.path}$", page.url) is not None)

    # --- 非滑动入口：编辑 Sheet 里的「删除…」
    open_edit(page, spec, via_sheet_name)
    page.get_by_test_id(spec.t("delete-cell")).click()
    alert.wait_for(state="visible")
    wait_animations_settled(page)
    confirm_center = center_of(page, page.get_by_test_id("alert-confirm"))
    run.check("Alert 盖在 Sheet 之上（Alert 按钮位置命中 Alert）", covers(page, confirm_center, '[data-testid="alert-host"]'))
    save_center = center_of(page, page.get_by_test_id(spec.t("save")))
    run.check("Alert 弹出时 Sheet 的保存按钮被遮罩挡住", not covers(page, save_center, f'[data-testid="{spec.t("sheet")}"]'))
    page.get_by_test_id("alert-confirm").click()
    wait_sheet_gone(page, spec)
    wait_row_gone(page, spec, via_sheet_name)
    run.check("经 Sheet 删除：Sheet 随之收起，条目软删除", spec.row(db, via_sheet)["IsDeleted"] == 1)

    # --- 编辑 Sheet 里删除一个有账单的：被拦截，Sheet 仍打开
    open_edit(page, spec, with_bills_name)
    page.get_by_test_id(spec.t("delete-cell")).click()
    alert.wait_for(state="visible")
    page.get_by_test_id("alert-confirm").click()
    wait_toast(page, spec.blocked_toast)
    alert.wait_for(state="detached")
    run.check("经 Sheet 删除有账单的条目：同样被拦截，Sheet 保持打开让用户另做处理", sheet(page, spec).count() == 1 and spec.row(db, with_bills)["IsDeleted"] == 0)
    close_sheet(page, spec)


# ---------------------------------------------------------------------------------------------- G. 图标都有字形


def check_icons_render(run: TestRun, page, spec: Spec) -> None:
    """图标必须存在于应用引用的 MDI 7.2.96：候选图标与列表里的图标都应在字体里有字形（页面加载的就是线上那份 CSS）。"""
    open_create(page, spec, "nav")
    glyphs = page.evaluate(GLYPH_JS, '[data-testid="icon-picker"] .ft-iconpick__item i')
    missing = [g["icon"] for g in glyphs if not g["ok"]]
    run.check(f"图标网格的 {len(glyphs)} 个候选图标在 MDI 7.2.96 字体里都有字形", len(glyphs) == spec.icon_count and not missing, f"缺字形：{missing}")
    close_sheet(page, spec)

    listed = page.evaluate(GLYPH_JS, f'[data-testid="{spec.page_id}"] .ft-tile i')
    missing = [g["icon"] for g in listed if not g["ok"]]
    run.check(f"列表里的 {len(listed)} 个图标（预设 + 自定义，含历史非法值的回退）都有字形", len(listed) > 0 and not missing, f"缺字形：{missing}")


# ---------------------------------------------------------------------------------------------- H. 可见性


def check_visibility(run: TestRun, page, db: LocalDb, user_id: str, spec: Spec, expected_shown: Callable) -> None:
    """别人的自定义项与已软删除的不显示；当前视图里的条目总数 = 库里该用户可见的（预设 + 自己未删除的自定义）。

    expected_shown() 返回库里「当前视图应显示」的条目数（分类页只显示当前分段的类型，所以由各页自己给）。
    """
    n = spec.noun
    other = db.seed_user()
    others_name = unique_name(f"别人的{n}")
    spec.seed(db, other, others_name)
    deleted_name = unique_name(f"已删{n}")
    spec.seed(db, user_id, deleted_name, deleted=True)
    mine_name = unique_name(f"自己的{n}")
    spec.seed(db, user_id, mine_name)
    reload_page(page, spec)

    run.check("别人的自定义项不显示", row_info(page, spec, others_name) is None and cell(page, spec, others_name).count() == 0)
    run.check("已软删除的自定义项不显示", row_info(page, spec, deleted_name) is None)
    run.check("自己的自定义项显示", row_info(page, spec, mine_name) is not None)
    shown = len(rows(page, spec, "custom") or []) + len(rows(page, spec, "preset") or [])
    expected = expected_shown()
    run.check(f"列表里的条目总数 = 库里该用户可见的{n}数（预设 + 自己未删除的自定义）", shown == expected, f"{shown} vs {expected}")


# ---------------------------------------------------------------------------------------------- I. 我的页摘要


def summary_text(page, spec: Spec) -> str:
    return page.evaluate(
        "(sel) => ((document.querySelector(sel) || {}).textContent || '').trim()",
        f'[data-testid="profile-entry-{spec.entry_id}"] .ft-cell__value',
    )


def wait_summary(run: TestRun, page, spec: Spec, label: str, expected: str) -> None:
    try:
        page.wait_for_function(
            """([sel, want]) => { const el = document.querySelector(sel); return !!el && el.textContent.trim() === want; }""",
            arg=[f'[data-testid="profile-entry-{spec.entry_id}"] .ft-cell__value', expected],
            timeout=8000,
        )
        run.check(label, True)
    except PlaywrightTimeoutError:
        run.check(label, False, f"期望「{expected}」，实际「{summary_text(page, spec)}」")


def check_profile_summary(run: TestRun, page, db: LocalDb, user_id: str, spec: Spec) -> None:
    """「我的」页入口行的摘要（N 个）：与库里一致；在管理页新建一个、返回后随之刷新（二级页面关闭后重读，13 号）。"""
    n = spec.noun
    open_app(page, "/")
    open_tab(page, "我的")
    before = spec.visible_total(db, user_id)
    wait_summary(run, page, spec, f"「我的」入口行摘要 = 库里可见的{n}数（{before} 个）", f"{before} 个")

    go_to_page(page, spec)
    name = unique_name(f"摘要{n}")
    open_create(page, spec, "nav")
    fill_form(page, spec, name)
    save_form(page, spec)
    wait_toast(page, spec.created_toast)
    wait_sheet_gone(page, spec)
    wait_row(page, spec, name)
    go_back_to_profile(page)
    wait_summary(run, page, spec, f"新建一个后返回「我的」，摘要自动刷新为「{before + 1} 个」", f"{before + 1} 个")


# ---------------------------------------------------------------------------------------------- J. Sheet 收起方式


def check_sheet_dismiss(run: TestRun, page, db: LocalDb, user_id: str, spec: Spec) -> None:
    n = spec.noun
    before = len(spec.customs(db, user_id))

    open_create(page, spec, "nav")
    fill_form(page, spec, "不会保存的名称", 3)
    # 图标网格让 Sheet 很高（最高到视口顶部下 12px），遮罩区只剩顶上一条：点那条的中间
    top = page.evaluate("(id) => document.querySelector(`[data-testid=\"${id}\"]`).getBoundingClientRect().top", spec.t("sheet"))
    run.check("Sheet 再高也在顶部留出一条遮罩区（≥ 8px）", top >= 8, str(top))
    page.mouse.click(SCREEN_W / 2, max(1.0, top / 2))
    wait_sheet_gone(page, spec)
    run.check("点遮罩收起，不保存", len(spec.customs(db, user_id)) == before)

    open_create(page, spec, "nav")
    run.check("收起后再打开：上次输入没有残留", page.get_by_test_id(spec.t("name")).input_value() == "" and page.get_by_test_id(spec.t("sort")).input_value() == "0")
    page.keyboard.press("Escape")
    wait_sheet_gone(page, spec)
    run.check("Esc 收起，不保存", sheet(page, spec).count() == 0 and len(spec.customs(db, user_id)) == before)

    open_create(page, spec, "nav")
    fill_form(page, spec, unique_name(f"回车保存{n}"))
    page.get_by_test_id(spec.t("name")).press("Enter")
    wait_toast(page, spec.created_toast)
    wait_sheet_gone(page, spec)
    run.check("在名称框里按回车等同于点「保存」", len(spec.customs(db, user_id)) == before + 1)
    shot(page, f"{spec.key}-after-enter-save")
