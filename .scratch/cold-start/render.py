"""把冷启动设计稿 HTML 渲染成 PNG（1980px 宽 @2x，与 docs/design/ios-ui-refactor 总览稿同规格）。

用法：python .scratch/cold-start/render.py [cold-start.html ...]   不带参数则全部渲染。
依赖：pip install playwright && playwright install chromium
"""
import pathlib
import sys

from playwright.sync_api import sync_playwright

ROOT = pathlib.Path(__file__).resolve().parent
OUT = ROOT.parents[1] / "docs" / "design" / "cold-start"

SHEETS = {
    "cold-start.html": "01-cold-start.png",
    "cold-start-states.html": "02-cold-start-states.png",
    "cold-start-spec.html": "03-cold-start-spec.png",
}


def main(names: list[str]) -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    with sync_playwright() as p:
        browser = p.chromium.launch()
        # 视口给足高度：滚出视口的 iframe 会被浏览器节流，截图前要保证它们都已渲染
        page = browser.new_page(viewport={"width": 1980, "height": 4800}, device_scale_factor=2)
        page.on("console", lambda m: print("  [console]", m.type, m.text) if m.type in ("error", "warning") else None)
        page.on("pageerror", lambda e: print("  [pageerror]", e))
        page.on("requestfailed", lambda r: print("  [requestfailed]", r.url[:120]))
        for src, dst in SHEETS.items():
            if names and src not in names and dst not in names:
                continue
            html = ROOT / src
            if not html.exists():
                print("skip (missing):", src)
                continue
            page.goto(html.as_uri(), wait_until="networkidle")
            page.evaluate("document.fonts.ready")
            # 嵌入的真实页面还原（iframe）：等各自的字体加载完；登录页还原要等 data-ready（脚本摆好交接中间态后设置）
            for fr in page.frames:
                if fr == page.main_frame:
                    continue
                fr.evaluate("document.fonts.ready.then(() => true)")
                if "login-screen" in fr.url:
                    fr.wait_for_function("document.documentElement.getAttribute('data-ready') === 'true'")
            page.wait_for_timeout(400)
            target = OUT / dst
            page.locator("#sheet").screenshot(path=str(target))
            box = page.locator("#sheet").bounding_box()
            print(f"wrote {target}  ({int(box['width'])}x{int(box['height'])} css px)")
        browser.close()


if __name__ == "__main__":
    main(sys.argv[1:])
