"""把设计稿里第 N 部手机（.phone）单独截出来，方便 QA 细节。
用法：python .scratch/cold-start/phone_shot.py <设计稿.html> <第几部，从 0 开始> <输出名>
"""
import pathlib
import sys

from playwright.sync_api import sync_playwright

ROOT = pathlib.Path(__file__).resolve().parent


def main() -> None:
    page_name, idx, out_name = sys.argv[1], int(sys.argv[2]), sys.argv[3]
    (ROOT / "assets").mkdir(exist_ok=True)
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = browser.new_page(viewport={"width": 1980, "height": 4800}, device_scale_factor=2)
        page.goto((ROOT / page_name).as_uri(), wait_until="networkidle")
        page.evaluate("document.fonts.ready")
        for fr in page.frames:
            if fr != page.main_frame:
                fr.evaluate("document.fonts.ready.then(() => true)")
                if "login-screen" in fr.url:
                    fr.wait_for_function("document.documentElement.getAttribute('data-ready') === 'true'")
        page.wait_for_timeout(400)
        phones = page.locator(".phone")
        print("phones:", phones.count())
        out = ROOT / "assets" / f"{out_name}.png"
        phones.nth(idx).screenshot(path=str(out))
        box = phones.nth(idx).bounding_box()
        print("wrote", out, {k: round(v, 1) for k, v in box.items()})
        browser.close()


if __name__ == "__main__":
    main()
