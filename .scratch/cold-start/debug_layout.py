import pathlib
import sys

from playwright.sync_api import sync_playwright

ROOT = pathlib.Path(__file__).resolve().parent
page_name = sys.argv[1] if len(sys.argv) > 1 else "home-screen.html"
selectors = sys.argv[2:] or ["html", "body", ".m-application", ".m-application--wrap", ".ft-tabpage", ".ft-hero", ".ft-tabpage__body"]

JS = """(sels) => sels.map((s) => {
  const e = document.querySelector(s);
  if (!e) return [s, null];
  const r = e.getBoundingClientRect();
  const cs = getComputedStyle(e);
  return [s, { x: Math.round(r.left), w: Math.round(r.width), y: Math.round(r.top), h: Math.round(r.height),
               display: cs.display, position: cs.position, width: cs.width, maxWidth: cs.maxWidth, margin: cs.margin, padding: cs.padding, flex: cs.flex, overflow: cs.overflow }];
})"""

with sync_playwright() as p:
    b = p.chromium.launch()
    pg = b.new_page(viewport={"width": 393, "height": 852}, device_scale_factor=2)
    pg.goto((ROOT / page_name).as_uri(), wait_until="networkidle")
    pg.wait_for_timeout(300)
    for sel, info in pg.evaluate(JS, selectors):
        print(sel, info)
    b.close()
