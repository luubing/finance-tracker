# -*- coding: utf-8 -*-
"""验收阶段的临时测量：账单筛选 Sheet 里各组标签的实际宽度（点，390×844）。不是回归测试。"""
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
  const out = {};
  const sheet = document.querySelector('[data-testid="filter-sheet"]') || document.querySelector('.ft-sheet');
  const r = (el) => { const b = el.getBoundingClientRect(); return [+b.left.toFixed(1), +b.top.toFixed(1), +b.width.toFixed(1), +b.height.toFixed(1)]; };
  out.sheet = r(sheet);
  for (const sec of sheet.querySelectorAll('.ft-filtersec')) {
    const name = sec.getAttribute('data-testid');
    const group = sec.querySelector('.ft-chips');
    out[name] = {group: r(group), chips: [...group.querySelectorAll('.ft-chip')].map(c => ({text: c.textContent.trim().slice(0, 14), rect: r(c)}))};
  }
  return out;
}"""


def main():
    require_server()
    db = LocalDb()
    with session() as s:
        page = s.page
        info = a11y.login(page)
        world = a11y.seed_world(db, info.phone)
        scenes = {sc.name: sc for sc in a11y.all_scenes()}
        scenes["sheet:filter"].open(page, world)
        page.evaluate(SET_SAFE_JS, [47, 34])
        wait_animations_settled(page)
        page.wait_for_timeout(300)
        data = page.evaluate(MEASURE_JS)
        for k, v in data.items():
            print(k)
            if isinstance(v, dict):
                print("   group", v["group"])
                for c in v["chips"]:
                    print("   ", c)
            else:
                print("   ", v)
    try:
        db.delete_pending_bills()
    except Exception:  # noqa: BLE001
        pass


if __name__ == "__main__":
    main()
