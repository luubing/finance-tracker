# -*- coding: utf-8 -*-
"""验收阶段的临时测量：记一笔 Sheet 各区块的实际位置（点，390×844，刘海按 47/34 模拟）。不是回归测试。"""
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[2]
sys.path.insert(0, str(ROOT / "tests" / "e2e"))

import a11y_degradation_test as a11y  # noqa: E402
from e2e_base import LocalDb, require_server, session, wait_animations_settled  # noqa: E402

SET_SAFE_JS = """([top, bottom]) => {
  const style = document.documentElement.style;
  style.setProperty('--ft-safe-top', top + 'px');
  style.setProperty('--ft-safe-bottom', bottom + 'px');
}"""

MEASURE_JS = """() => {
  const q = (sel) => document.querySelector(sel);
  const r = (el) => { if (!el) return null; const b = el.getBoundingClientRect(); return {top: +b.top.toFixed(1), bottom: +b.bottom.toFixed(1), h: +b.height.toFixed(1), left: +b.left.toFixed(1), w: +b.width.toFixed(1)}; };
  const sheet = q('[data-testid="addbill-sheet"]');
  const out = {};
  out.viewport = [innerWidth, innerHeight];
  out.sheet = r(sheet);
  out.handle = r(sheet.querySelector('.ft-sheet__handle'));
  out.header = r(sheet.querySelector('.ft-sheet__header'));
  out.body = r(sheet.querySelector('.ft-sheet__body'));
  out.head = r(sheet.querySelector('.ft-addbill__head'));
  out.seg = r(sheet.querySelector('[data-testid="addbill-type"]'));
  out.amount = r(sheet.querySelector('.ft-addbill__amount'));
  out.cats = r(sheet.querySelector('.ft-addbill__cats'));
  out.catgrid = r(sheet.querySelector('.ft-catgrid'));
  out.cat1 = r(sheet.querySelector('.ft-cat'));
  out.cattile = r(sheet.querySelector('.ft-cat__tile'));
  out.dots = r(sheet.querySelector('.ft-catgrid__dots'));
  out.rows = r(sheet.querySelector('.ft-addbill__rows'));
  const cells = [...sheet.querySelectorAll('.ft-addbill__rows .ft-cell')].map(r);
  out.cells = cells;
  out.footer = r(sheet.querySelector('.ft-sheet__footer'));
  out.keypad = r(sheet.querySelector('.ft-keypad'));
  out.key1 = r(sheet.querySelector('.ft-key'));
  out.danger = r(sheet.querySelector('.ft-addbill__danger'));
  return out;
}"""


def main():
    require_server()
    db = LocalDb()
    with session() as s:
        page = s.page
        info = a11y.login(page)
        world = a11y.seed_world(db, info.phone)
        names = sys.argv[1:] or ["sheet:add-bill"]
        scenes = {sc.name: sc for sc in a11y.all_scenes()}
        for name in names:
            scenes[name].open(page, world)
            page.evaluate(SET_SAFE_JS, [47, 34])
            wait_animations_settled(page)
            page.wait_for_timeout(300)
            data = page.evaluate(MEASURE_JS)
            print("==", name)
            for k, v in data.items():
                print(f"  {k}: {v}")
    try:
        db.delete_pending_bills()
    except Exception:  # noqa: BLE001
        pass


if __name__ == "__main__":
    main()
