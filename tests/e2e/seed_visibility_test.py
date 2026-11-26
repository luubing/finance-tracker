# -*- coding: utf-8 -*-
"""
E2E 测试：造数据 helper 写入的本地库数据，应用能原样读出并展示（手机视口）。

这是基座的「端到端自证」：LocalDb 直接写 SQLite（大写 GUID、UTC 时间文本、枚举整数），
应用走 EF Core 读出来必须一致，否则后续所有依赖造数据的 ticket 都会得到错误前提。

验证点：
  - create_ledger（经界面）创建的账本落到当前登录用户名下
  - 带账本账单与「历史无账本账单」（LedgerId = NULL）都能在账单列表看到
  - 造数据时用的本地时间（10:30）在列表里仍显示为 10:30（UTC 存储 → 应用时区展示，口径一致）
  - 账本名称在账单项上展示；历史账单不带账本标签

说明：待确认账单（PendingBills）从 07 号起在 Web.Server 里注册的是真实的 SQLite 实现，首页的横幅与角标能读到库里的数据
（见 home_page_test.py）；这里只验证账本与账单的可见性。
"""
import sys
from datetime import datetime

from e2e_base import (
    LocalDb,
    TestRun,
    create_bill,
    create_ledger,
    login,
    open_app,
    require_server,
    session,
    shot,
    unique_name,
    wait_text,
)


def main() -> int:
    require_server()
    run = TestRun("seed-visibility")
    db = LocalDb()
    db.verify_schema()

    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)

        # ---- 账本：经界面创建 ----
        ledger_name = create_ledger(page)
        ledgers = db.ledgers_of_user(user_id)
        run.check("经界面创建的账本落在当前用户名下", [l["Name"] for l in ledgers] == [ledger_name], str(ledgers))

        # ---- 账单：直接写库 ----
        in_ledger_note = unique_name("账本内账单")
        legacy_note = unique_name("历史账单")
        today_1030 = datetime.now().replace(hour=10, minute=30, second=0, microsecond=0)
        create_bill(info, amount="12.50", ledger=ledger_name, note=in_ledger_note, when=today_1030)
        create_bill(info, amount="8.00", ledger=None, note=legacy_note, when=today_1030.replace(hour=9, minute=15))
        run.check(
            "历史无账本账单 LedgerId 为 NULL",
            any(b["Note"] == legacy_note and b["LedgerId"] is None for b in db.bills_of_user(user_id)),
        )

        # ---- 账单列表 ----
        open_app(page, "/bills")
        wait_text(page, in_ledger_note)
        body = page.inner_text("body")
        run.check("带账本账单可见", in_ledger_note in body)
        run.check("历史无账本账单可见", legacy_note in body)
        run.check("金额按 N2 展示", "12.50" in body and "8.00" in body)
        run.check("账单项展示账本名", ledger_name in body)
        run.check("UTC 存储的 10:30 仍显示 10:30", "10:30" in body, "时间口径不一致？")
        run.check("UTC 存储的 09:15 仍显示 09:15", "09:15" in body, "时间口径不一致？")
        shot(page, "seed-visibility-bills")

        run.check_clean(s.errors)

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
