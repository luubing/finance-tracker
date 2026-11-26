# -*- coding: utf-8 -*-
"""
逐屏截图并与设计稿并排（24 号收尾核对 / 验收阶段用，不是回归测试，不做像素对比）。

用法（Web.Server 已由 tests/e2e/server.ps1 启动）：
  python design_capture.py                 # 13 屏全部
  python design_capture.py 01 05 13        # 只截编号以这些开头的屏

输出（目录已被忽略，不进版本库）：
  shots/NN-name.png          实现截图：390×844、像素比 3；用样式变量模拟刘海（状态栏 47 / 手势条 34），与设计稿的手机边框更接近
  shots/NN-name-compare.png  左：设计稿（docs/design/ios-ui-refactor/screens），右：实现截图，同高并排

数据：沿用 a11y_degradation_test 的 seed_world（两个自建账本 + 共享账本、四十多天账单、预算、待确认账单、日志…），
所以列表里的具体数字与设计稿示意图不同——对照的是布局、层级、材质与组件形态，不是数据。
"""
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[2]
sys.path.insert(0, str(ROOT / "tests" / "e2e"))

from PIL import Image, ImageDraw, ImageFont  # noqa: E402

import a11y_probes as probes  # noqa: E402
import a11y_degradation_test as a11y  # noqa: E402
from e2e_base import (  # noqa: E402
    LocalDb,
    open_app,
    require_server,
    session,
    shot,
    wait_animations_settled,
)
from voice_overlay_test import FAKE_SPEECH_JS  # noqa: E402

DESIGN_DIR = ROOT / "docs" / "design" / "ios-ui-refactor" / "screens"
OUT_DIR = HERE / "shots"

SAFE_TOP = 47
SAFE_BOTTOM = 34

SET_SAFE_JS = """([top, bottom]) => {
  const style = document.documentElement.style;
  style.setProperty('--ft-safe-top', top + 'px');
  style.setProperty('--ft-safe-bottom', bottom + 'px');
}"""


def open_income_sheet(page, world):
    a11y.open_addbill(page, world)
    page.get_by_test_id("addbill-sheet").get_by_test_id("addbill-type").get_by_test_id("segment").filter(has_text="收入").click()
    page.wait_for_function("() => document.querySelector('[data-testid=\"addbill-sheet\"]')?.getAttribute('data-type') === 'income'")
    wait_animations_settled(page)


def scene(name):
    return {s.name: s for s in a11y.all_scenes()}[name].open


# (编号-名称, 设计稿文件, 打开场景的函数)；登录页是未登录会话，单独处理
SCREENS = [
    ("01-home", "01-home.png", scene("home")),
    ("02-bills", "02-bills.png", scene("bills")),
    ("03-stats", "03-stats.png", scene("statistics-month")),
    ("04-me", "04-me.png", scene("profile")),
    ("05-add-expense", "05-add-expense.png", scene("sheet:add-bill")),
    ("06-add-voice", "06-add-voice.png", scene("voice")),
    ("07-add-income", "07-add-income.png", open_income_sheet),
    ("08-ledgers", "08-ledgers.png", scene("ledgers")),
    ("09-budgets", "09-budgets.png", scene("budgets")),
    ("10-pending", "10-pending.png", scene("pending-bills")),
    ("11-categories", "11-categories.png", scene("categories")),
    ("13-filter", "13-filter.png", scene("sheet:filter")),
]
LOGIN = ("12-login", "12-login.png")

# 设计稿没有单独出图的页面（清单里「同模板」处理）：只截图，不并排；编号以 x 开头，用来肉眼核对是否与同类页面一致
EXTRA_SCREENS = [
    ("x01-trend", scene("statistics-trend")),
    ("x02-annual", scene("statistics-annual")),
    ("x03-channels", scene("payment-channels")),
    ("x04-sms", scene("sms-recognition")),
    ("x05-import", scene("import")),
    ("x06-report", scene("custom-report")),
    ("x07-logs", scene("log-viewer")),
    ("x08-members", a11y.MEMBERS_SCENE.open),
    ("x09-edit", scene("sheet:edit-bill")),
    ("x10-confirm", scene("sheet:confirm-pending")),
    ("x11-select", scene("bills:select-mode")),
    ("x12-alert", scene("alert:delete-bill")),
    ("x13-ledger-menu", scene("popover:ledger-menu")),
]


def font(size):
    for candidate in (r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\simhei.ttf", r"C:\Windows\Fonts\arial.ttf"):
        if pathlib.Path(candidate).exists():
            return ImageFont.truetype(candidate, size)
    return ImageFont.load_default()


def compose(key, design_file, impl_path):
    height = 1024
    design = Image.open(DESIGN_DIR / design_file).convert("RGB")
    impl = Image.open(impl_path).convert("RGB")
    design = design.resize((round(design.width * height / design.height), height), Image.LANCZOS)
    impl = impl.resize((round(impl.width * height / impl.height), height), Image.LANCZOS)
    gap = 24
    header = 44
    canvas = Image.new("RGB", (design.width + impl.width + gap * 3, height + header + gap), (255, 255, 255))
    draw = ImageDraw.Draw(canvas)
    draw.text((gap, 10), f"{key}  设计稿", fill=(0, 0, 0), font=font(22))
    draw.text((gap * 2 + design.width, 10), f"{key}  实现（{impl.width}×{impl.height} 缩放，刘海按 47/34 模拟）", fill=(0, 0, 0), font=font(22))
    canvas.paste(design, (gap, header))
    canvas.paste(impl, (gap * 2 + design.width, header))
    out = OUT_DIR / f"{key}-compare.png"
    canvas.save(out)
    return out


def main(argv):
    wanted = argv[1:]
    require_server()
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    failures = []
    db = LocalDb()

    def want(key):
        return not wanted or any(key.startswith(w) for w in wanted)

    todo = [item for item in SCREENS if want(item[0])]
    extras = [item for item in EXTRA_SCREENS if want(item[0])]
    if todo or extras:
        with session() as s:
            s.context.add_init_script(FAKE_SPEECH_JS)
            page = s.page
            info = a11y.login(page)
            world = a11y.seed_world(db, info.phone)
            for key, design_file, opener in todo:
                try:
                    opener(page, world)
                    page.evaluate(SET_SAFE_JS, [SAFE_TOP, SAFE_BOTTOM])
                    wait_animations_settled(page)
                    page.wait_for_timeout(400)
                    path = shot(page, key, force=True, directory=OUT_DIR)
                    print("截图", path.name, "→", compose(key, design_file, path).name, flush=True)
                except Exception as exc:  # noqa: BLE001
                    failures.append(key)
                    print("失败", key, type(exc).__name__, str(exc)[:200], flush=True)

            for key, opener in extras:
                try:
                    opener(page, world)
                    page.evaluate(SET_SAFE_JS, [SAFE_TOP, SAFE_BOTTOM])
                    wait_animations_settled(page)
                    page.wait_for_timeout(400)
                    path = shot(page, key, force=True, directory=OUT_DIR)
                    print("截图", path.name, flush=True)
                except Exception as exc:  # noqa: BLE001
                    failures.append(key)
                    print("失败", key, type(exc).__name__, str(exc)[:200], flush=True)

    if want(LOGIN[0]):
        with session() as s:
            page = s.page
            try:
                open_app(page, "/")
                page.wait_for_selector("[data-testid='login-phone']")
                probes.wait_page_ready(page)
                page.evaluate(SET_SAFE_JS, [SAFE_TOP, SAFE_BOTTOM])
                page.wait_for_timeout(400)
                path = shot(page, LOGIN[0], force=True, directory=OUT_DIR)
                print("截图", path.name, "→", compose(LOGIN[0], LOGIN[1], path).name, flush=True)
            except Exception as exc:  # noqa: BLE001
                failures.append(LOGIN[0])
                print("失败", LOGIN[0], type(exc).__name__, str(exc)[:200], flush=True)

    try:
        db.delete_pending_bills()
    except Exception:  # noqa: BLE001
        pass
    print("失败的屏：", failures or "无")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
