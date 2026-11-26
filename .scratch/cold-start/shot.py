"""单页截图（393×852 @2x），用来看真实页面还原的某个状态。
用法：python .scratch/cold-start/shot.py <页面.html> <输出名> [查询串]
例：  python .scratch/cold-start/shot.py login-screen.html login-mid "?hand=.5&panel=.5"
输出：.scratch/cold-start/assets/<输出名>.png
（查询串放最后：PowerShell 调原生命令时会吞掉空字符串参数）
"""
import pathlib
import sys

from playwright.sync_api import sync_playwright

ROOT = pathlib.Path(__file__).resolve().parent


def main() -> None:
    page_name, out_name = sys.argv[1], sys.argv[2]
    query = sys.argv[3] if len(sys.argv) > 3 else ""
    (ROOT / "assets").mkdir(exist_ok=True)
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = browser.new_page(viewport={"width": 393, "height": 852}, device_scale_factor=2)
        page.on("pageerror", lambda e: print("  [pageerror]", e))
        page.goto((ROOT / page_name).as_uri() + query, wait_until="networkidle")
        page.evaluate("document.fonts.ready")
        if "login-screen" in page_name:
            page.wait_for_function("document.documentElement.getAttribute('data-ready') === 'true'")
        page.wait_for_timeout(300)
        out = ROOT / "assets" / f"{out_name}.png"
        page.screenshot(path=str(out))
        print("wrote", out)
        browser.close()


if __name__ == "__main__":
    main()
