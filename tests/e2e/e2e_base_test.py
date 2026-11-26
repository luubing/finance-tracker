# -*- coding: utf-8 -*-
"""
E2E 基座自测：验证手机视口、手势、降级媒体特性、错误采集、状态等待、截图与造数据 helper 本身可靠。

不依赖 Web.Server：用 page.route 在虚拟源 http://e2e.test/ 上提供静态页面，
造数据用临时 SQLite（结构按 REQUIRED_SCHEMA 生成），不会碰真实本地库。

后续所有 ticket 的 E2E 都建立在这些 helper 之上，所以这里先把它们本身测牢。
有头模式人工观察：$env:E2E_HEADED='1'; python tests/e2e/e2e_base_test.py
"""
import base64
import json
import sqlite3
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.request
import uuid
from datetime import datetime
from pathlib import Path

import e2e_base
from e2e_base import (
    PHONE_DEVICE_SCALE,
    PHONE_VIEWPORT,
    ErrorCollector,
    LocalDb,
    LoginInfo,
    SchemaMismatch,
    TestRun,
    REQUIRED_SCHEMA,
    BILL_TYPE_EXPENSE,
    BILL_TYPE_INCOME,
    MEMBER_ACTIVE,
    MEMBER_PENDING,
    ROLE_EDITOR,
    ROLE_OWNER,
    ROLE_VIEWER,
    SOURCE_NOTIFICATION,
    create_bill,
    long_press,
    new_guid,
    pull_down,
    session,
    shot,
    swipe,
    swipe_by,
    tap,
    to_db_decimal,
    to_db_time,
    unique_name,
    unique_phone,
    wait_animations_settled,
    wait_attr,
    wait_state,
    wait_url,
)
from fake_cloud import (
    CLOUD_PORT,
    CloudPortBusy,
    CloudWorld,
    FakeCloud,
    parse_alipay_csv,
    parse_wechat_csv,
    split_csv_line,
)

ORIGIN = "http://e2e.test"

PAGE_HTML = """<!doctype html>
<html lang="zh-CN"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<style>
  html, body { margin: 0; }
  body { height: 2400px; font: 16px sans-serif; }
  #btn { position: absolute; left: 20px; top: 20px; width: 140px; height: 56px; }
  #pad { position: absolute; left: 20px; top: 160px; width: 350px; height: 300px;
         background: #ddd; touch-action: none; }
  #fade { position: absolute; left: 20px; top: 500px; width: 100px; height: 40px; background: #48f;
          animation: grow 200ms ease-out forwards; }
  @keyframes grow { from { width: 20px; } to { width: 100px; } }
</style></head>
<body>
<button id="btn">轻点</button>
<div id="pad"></div>
<div id="fade"></div>
<script>
  window.__log = { clicks: 0, starts: 0, ends: 0, moves: [], startAt: 0, endAt: 0, startXY: null, endXY: null };
  const btn = document.getElementById('btn');
  const pad = document.getElementById('pad');
  btn.addEventListener('click', () => window.__log.clicks++);
  pad.addEventListener('touchstart', e => {
    const t = e.touches[0];
    window.__log.starts++; window.__log.startAt = performance.now(); window.__log.startXY = [t.clientX, t.clientY];
  }, { passive: true });
  pad.addEventListener('touchmove', e => {
    const t = e.touches[0];
    window.__log.moves.push([t.clientX, t.clientY]);
  }, { passive: true });
  pad.addEventListener('touchend', () => {
    window.__log.ends++; window.__log.endAt = performance.now();
  }, { passive: true });
  setTimeout(() => document.body.setAttribute('data-ready', 'yes'), 250);
</script>
</body></html>"""


def serve_static(page) -> None:
    """让虚拟源 http://e2e.test 返回静态页（同源，localStorage / pushState 可用）。"""
    page.route(
        f"{ORIGIN}/**",
        lambda route: route.fulfill(status=200, content_type="text/html; charset=utf-8", body=PAGE_HTML),
    )


def reset_log(page) -> None:
    page.evaluate(
        "() => { window.__log = { clicks: 0, starts: 0, ends: 0, moves: [], startAt: 0, endAt: 0, startXY: null, endXY: null }; }"
    )


def test_viewport_and_gestures(run: TestRun) -> None:
    with session() as s:
        page = s.page
        serve_static(page)
        page.goto(f"{ORIGIN}/start")

        # ---- 手机视口 ----
        info = page.evaluate(
            """() => ({
                w: innerWidth, h: innerHeight, dpr: devicePixelRatio,
                touch: 'ontouchstart' in window,
                coarse: matchMedia('(pointer: coarse)').matches,
                noHover: matchMedia('(hover: none)').matches,
                reducedMotion: matchMedia('(prefers-reduced-motion: reduce)').matches,
            })"""
        )
        run.check("视口宽度 390", info["w"] == PHONE_VIEWPORT["width"], str(info))
        run.check("视口高度 844", info["h"] == PHONE_VIEWPORT["height"], str(info))
        run.check("像素比 >= 3", info["dpr"] >= PHONE_DEVICE_SCALE, str(info))
        run.check("支持触摸事件", info["touch"], str(info))
        run.check("pointer: coarse", info["coarse"], str(info))
        run.check("默认不是减少动态效果", not info["reducedMotion"], str(info))

        # ---- tap：真实触摸 → 点击 ----
        tap(page, "#btn")
        page.wait_for_function("() => window.__log.clicks === 1")
        run.check("tap 触发 click", page.evaluate("() => window.__log.clicks") == 1)

        # ---- long_press：触摸保持，期间无 touchmove ----
        reset_log(page)
        long_press(page, "#pad", hold_ms=700)
        page.wait_for_function("() => window.__log.ends === 1")
        log = page.evaluate("() => window.__log")
        held = log["endAt"] - log["startAt"]
        run.check("long_press 保持 >= 600ms", held >= 600, f"held={held:.0f}ms")
        run.check("long_press 期间无 touchmove", len(log["moves"]) == 0, f"moves={len(log['moves'])}")

        # ---- swipe_by：向左滑 ----
        reset_log(page)
        swipe_by(page, "#pad", -200, 0)
        page.wait_for_function("() => window.__log.ends === 1")
        log = page.evaluate("() => window.__log")
        dx = log["moves"][-1][0] - log["startXY"][0] if log["moves"] else 0
        run.check("swipe_by 产生连续 touchmove", len(log["moves"]) >= 8, f"moves={len(log['moves'])}")
        run.check("swipe_by 向左位移约 -200", -215 <= dx <= -185, f"dx={dx:.1f}")

        # ---- swipe：任意起止点 ----
        reset_log(page)
        swipe(page, (300, 200), (100, 420), steps=10, duration_ms=150)
        page.wait_for_function("() => window.__log.ends === 1")
        log = page.evaluate("() => window.__log")
        last = log["moves"][-1]
        run.check("swipe 终点与目标一致", abs(last[0] - 100) <= 1 and abs(last[1] - 420) <= 1, str(last))

        # ---- pull_down：从顶部区域向下拉 ----
        reset_log(page)
        pull_down(page, distance=160, from_y=200)
        page.wait_for_function("() => window.__log.ends === 1")
        log = page.evaluate("() => window.__log")
        dy = log["moves"][-1][1] - log["startXY"][1] if log["moves"] else 0
        run.check("pull_down 向下位移约 160", 150 <= dy <= 170, f"dy={dy:.1f}")

        # ---- 状态等待 ----
        wait_attr(page, "body", "data-ready", "yes")
        run.check("wait_attr 等到属性值", page.evaluate("() => document.body.dataset.ready") == "yes")
        wait_state(page, "() => document.getElementById('btn').textContent === '轻点'")
        wait_animations_settled(page)
        width = page.evaluate("() => document.getElementById('fade').getBoundingClientRect().width")
        run.check("wait_animations_settled 等动画结束", abs(width - 100) < 1, f"width={width}")
        page.evaluate("() => history.pushState({}, '', '/tab/stats?x=1')")
        wait_url(page, r"^/tab/stats\?x=1$")
        run.check("wait_url 匹配路径+查询串", True)

        # ---- 可选截图 ----
        with tempfile.TemporaryDirectory() as tmp:
            path = shot(page, "selftest 手机视口", force=True, directory=Path(tmp))
            run.check("shot(force) 生成 PNG", path is not None and path.exists() and path.stat().st_size > 1000)
            # 与是否设置了 E2E_SCREENSHOTS 无关：显式把总开关关掉再验证「默认不输出」
            original = e2e_base.SCREENSHOTS_ENABLED
            e2e_base.SCREENSHOTS_ENABLED = False
            try:
                run.check("shot 关闭时不产生文件", shot(page, "nope", directory=Path(tmp)) is None)
            finally:
                e2e_base.SCREENSHOTS_ENABLED = original
            width_px = int.from_bytes(path.read_bytes()[16:20], "big") if path else 0
            run.check(
                "截图像素宽 = 视口宽 × 像素比（1170）",
                width_px == PHONE_VIEWPORT["width"] * PHONE_DEVICE_SCALE,
                f"width={width_px}",
            )

        run.check_clean(s.errors)


def test_degradation_emulation(run: TestRun) -> None:
    with session(reduced_motion=True, reduced_transparency=True) as s:
        page = s.page
        serve_static(page)
        page.goto(f"{ORIGIN}/start")
        info = page.evaluate(
            """() => ({
                motion: matchMedia('(prefers-reduced-motion: reduce)').matches,
                transparency: matchMedia('(prefers-reduced-transparency: reduce)').matches,
            })"""
        )
        run.check("reduced_motion=True → prefers-reduced-motion: reduce", info["motion"], str(info))
        run.check("reduced_transparency=True → prefers-reduced-transparency: reduce", info["transparency"], str(info))

        # 新开的页面同样带着降级模拟
        page2 = s.new_page()
        serve_static(page2)
        page2.goto(f"{ORIGIN}/second")
        info2 = page2.evaluate(
            """() => ({
                motion: matchMedia('(prefers-reduced-motion: reduce)').matches,
                transparency: matchMedia('(prefers-reduced-transparency: reduce)').matches,
            })"""
        )
        run.check("new_page 继承降级模拟", info2["motion"] and info2["transparency"], str(info2))


def test_error_collector(run: TestRun) -> None:
    with session(ignore=[r"allowed-noise"]) as s:
        page = s.page
        serve_static(page)
        page.goto(f"{ORIGIN}/start")
        run.check("干净页面无错误", s.errors.is_clean(), s.errors.summary())

        page.evaluate("() => console.error('allowed-noise should be ignored')")
        page.evaluate("() => console.error('cdn.jsdelivr.net blocked')")
        run.check("白名单内的控制台错误被忽略", s.errors.is_clean(), s.errors.summary())

        page.evaluate("() => console.error('real console error')")
        page.evaluate("() => setTimeout(() => { throw new Error('real page error'); }, 0)")
        # 后注册的 route 优先：/missing 返回 404，其余仍走静态页
        page.route(f"{ORIGIN}/missing", lambda r: r.fulfill(status=404, body="no"))
        page.evaluate("() => fetch('/missing').then(() => 0)")
        deadline_ok = False
        for _ in range(50):
            if s.errors.page_errors and s.errors.bad_responses:
                deadline_ok = True
                break
            page.wait_for_timeout(100)
        run.check("采集到控制台 error", any("real console error" in m for m in s.errors.console_errors))
        run.check("采集到页面异常", any("real page error" in m for m in s.errors.page_errors), str(s.errors.page_errors))
        run.check("采集到 HTTP >= 400", deadline_ok and any(r.startswith("404") for r in s.errors.bad_responses))
        run.check("is_clean 随之为 False", not s.errors.is_clean())
        s.errors.clear()
        run.check("clear 后恢复干净", s.errors.is_clean())

    collector = ErrorCollector()
    run.check("默认白名单含 CDN", any(p.search("https://cdn.jsdelivr.net/npm/x") for p in collector._ignore))


def make_temp_db(path: str) -> None:
    """按 REQUIRED_SCHEMA 建临时库，并放入一条预设分类 / 支付渠道。"""
    int_columns = {"IsDeleted", "IsPreset", "SortOrder", "Type", "Source", "SyncStatus", "Role", "Status"}
    con = sqlite3.connect(path)
    for table, columns in REQUIRED_SCHEMA.items():
        cols = ", ".join(f'"{c}" {"INTEGER" if c in int_columns else "TEXT"}' for c in columns)
        con.execute(f'CREATE TABLE "{table}" ({cols})')
    now = to_db_time(datetime.now())
    con.execute(
        'INSERT INTO "Categories" (Id, Name, Type, IsPreset, IsDeleted, SortOrder, UserId) VALUES (?,?,?,?,?,?,?)',
        ("10000000-0000-0000-0000-000000000001", "餐饮", 0, 1, 0, 1, None),
    )
    con.execute(
        'INSERT INTO "Categories" (Id, Name, Type, IsPreset, IsDeleted, SortOrder, UserId) VALUES (?,?,?,?,?,?,?)',
        ("10000000-0000-0000-0000-000000000101", "工资", 1, 1, 0, 1, None),
    )
    con.execute(
        'INSERT INTO "PaymentChannels" (Id, Name, IsPreset, IsDeleted, SortOrder, UserId) VALUES (?,?,?,?,?,?)',
        ("20000000-0000-0000-0000-000000000001", "微信支付", 1, 0, 1, None),
    )
    uid = new_guid()
    con.execute(
        'INSERT INTO "Users" (Id, PhoneNumber, IsDeleted, CreatedAt, UpdatedAt) VALUES (?,?,?,?,?)',
        (uid, "13800000000", 0, now, now),
    )
    con.commit()
    con.close()


def test_local_db(run: TestRun) -> None:
    with tempfile.TemporaryDirectory() as tmp:
        path = str(Path(tmp) / "e2e_temp.db")
        make_temp_db(path)
        db = LocalDb(path)
        db.verify_schema()
        run.check("verify_schema 通过", True)

        user_id = db.user_id("13800000000")
        run.check("user_id 按手机号取到用户", len(user_id) == 36 and user_id == user_id.upper())

        ledger_id = db.seed_ledger(user_id, "自测账本")
        run.check("seed_ledger 写入大写 GUID", ledger_id == ledger_id.upper() and len(ledger_id) == 36)
        run.check("ledgers_of_user 读回", [l["Name"] for l in db.ledgers_of_user(user_id)] == ["自测账本"])

        # 共享账本相关的造数据：其他用户、成员关系、默认账本、按 Id 读账本（含软删除）
        other_id = db.seed_user("13900000001")
        run.check("seed_user 写入大写 GUID，可按手机号读回", other_id == other_id.upper() and db.user_id("13900000001") == other_id)
        member_id = db.seed_ledger_member(ledger_id, other_id, role=ROLE_VIEWER, status=MEMBER_PENDING)
        members = db.ledger_members(ledger_id)
        run.check(
            "seed_ledger_member 写入成员关系（角色 / 状态 / 用户）",
            len(members) == 1 and members[0]["Id"] == member_id and members[0]["UserId"] == other_id
            and members[0]["Role"] == ROLE_VIEWER and members[0]["Status"] == MEMBER_PENDING and members[0]["IsDeleted"] == 0,
            str(members),
        )
        # 同一 (账本, 用户) 再造一次（应用也会写这张表，先后不定）：以造数为准，不抛唯一键冲突、不产生第二行、保留原来的 Id；
        # 显式指定 Id 时才改写主键（要与假云端的成员行对上）。临时库没有唯一索引，所以这里验证的是 UPDATE 优先的写法而不是 ON CONFLICT
        again = db.seed_ledger_member(ledger_id, other_id, role=ROLE_EDITOR, status=MEMBER_ACTIVE)
        members = db.ledger_members(ledger_id)
        run.check(
            "seed_ledger_member 同一 (账本, 用户) 再造：覆盖角色 / 状态、仍只有一行、保留原 Id",
            again == member_id and len(members) == 1 and members[0]["Id"] == member_id
            and members[0]["Role"] == ROLE_EDITOR and members[0]["Status"] == MEMBER_ACTIVE,
            str((again, members)),
        )
        forced_id = new_guid()
        forced = db.seed_ledger_member(ledger_id, other_id, role=ROLE_VIEWER, status=MEMBER_PENDING, member_id=forced_id)
        members = db.ledger_members(ledger_id)
        run.check(
            "seed_ledger_member 显式指定 Id：改写主键，仍只有一行",
            forced == forced_id and len(members) == 1 and members[0]["Id"] == forced_id and members[0]["Role"] == ROLE_VIEWER,
            str((forced, members)),
        )
        run.check("ledger_row 按 Id 读账本", db.ledger_row(ledger_id)["Name"] == "自测账本")
        run.check("default_ledger_id 没设置过时为 None", db.default_ledger_id(user_id) is None)
        with db._connect() as con:
            con.execute(
                'INSERT INTO "UserDefaults" (Id, UserId, DefaultLedgerId, CreatedAt, UpdatedAt) VALUES (?,?,?,?,?)',
                (new_guid(), user_id, ledger_id, to_db_time(datetime.now()), to_db_time(datetime.now())),
            )
        run.check("default_ledger_id 读回", db.default_ledger_id(user_id) == ledger_id)
        with db._connect() as con:
            con.execute('UPDATE "Ledgers" SET IsDeleted = 1 WHERE Id = ?', (ledger_id,))
        run.check("ledger_row 默认包含已软删除的账本，include_deleted=False 时读不到",
                  db.ledger_row(ledger_id)["IsDeleted"] == 1 and db.ledger_row(ledger_id, include_deleted=False) is None)
        with db._connect() as con:
            con.execute('UPDATE "Ledgers" SET IsDeleted = 0 WHERE Id = ?', (ledger_id,))

        when = datetime(2026, 3, 15, 8, 30, 0)  # naive 本地时间
        bill_id = db.seed_bill(user_id, amount="12.5", ledger_id=ledger_id, note="带账本账单", when=when)
        bill = db.bills_by_note("带账本账单")[0]
        run.check("账单金额以文本存储", bill["Amount"] == "12.5", str(bill["Amount"]))
        run.check("账单归属账本", bill["LedgerId"] == ledger_id)
        run.check("账单同步状态默认待同步(0)", bill["SyncStatus"] == 0)
        expected_utc = to_db_time(when)
        run.check("交易时间落库为 UTC 文本", bill["TransactionTime"] == expected_utc, bill["TransactionTime"])
        run.check("预设分类/渠道被自动选用", bill["CategoryId"].endswith("0001") and bill["PaymentChannelId"].startswith("2000"))
        run.check("seed_bill 返回 Id", bill["Id"] == bill_id)

        legacy_id = db.seed_legacy_bill(user_id, amount=99, note="历史账单", bill_type=BILL_TYPE_INCOME)
        legacy = db.bills_by_note("历史账单")[0]
        run.check("历史无账本账单 LedgerId 为 NULL", legacy["LedgerId"] is None and legacy["Id"] == legacy_id)
        run.check("收入类型取到收入预设分类", legacy["Type"] == BILL_TYPE_INCOME and legacy["CategoryId"].endswith("0101"))

        db.set_bill_sync_status(bill_id, 1)
        run.check("set_bill_sync_status 生效", db.bills_by_note("带账本账单")[0]["SyncStatus"] == 1)
        run.check("bill_row 按 Id（大小写都认）读账单", db.bill_row(bill_id)["Note"] == "带账本账单" and db.bill_row(bill_id.lower())["Id"] == bill_id)
        with db._connect() as con:
            con.execute('UPDATE "Bills" SET IsDeleted = 1 WHERE Id = ?', (bill_id,))
        run.check("bill_row 默认包含已软删除的账单，include_deleted=False 时读不到；不存在的账单是 None",
                  db.bill_row(bill_id)["IsDeleted"] == 1 and db.bill_row(bill_id, include_deleted=False) is None and db.bill_row(new_guid()) is None)
        with db._connect() as con:
            con.execute('UPDATE "Bills" SET IsDeleted = 0 WHERE Id = ?', (bill_id,))

        # 稳定签名的入口：localStorage 里的 userId 是小写，写库时必须归一成大写
        info = LoginInfo(phone="13800000000", user_id=user_id.lower())
        create_bill(info, amount=3, ledger="自测账本", note="门面账单", db=db)
        facade = db.bills_by_note("门面账单")[0]
        run.check("create_bill 按账本名称解析账本", facade["LedgerId"] == ledger_id)
        run.check("create_bill 以库内大写 userId 写入", facade["UserId"] == user_id)
        create_bill(info, ledger=None, note="门面历史账单", db=db)
        run.check("create_bill(ledger=None) 写入历史无账本账单", db.bills_by_note("门面历史账单")[0]["LedgerId"] is None)
        run.check("ledger_id_by_name 读回", db.ledger_id_by_name(user_id.lower(), "自测账本") == ledger_id)
        for note in ("门面账单", "门面历史账单"):
            db_bill = db.bills_by_note(note)[0]
            with db._connect() as con:
                con.execute('DELETE FROM "Bills" WHERE Id = ?', (db_bill["Id"],))

        pending_id = db.seed_pending_bill(amount=18, note="待确认自测", source=SOURCE_NOTIFICATION)
        pending = db.pending_bills()
        run.check("seed_pending_bill 写入", len(pending) == 1 and pending[0]["Id"] == pending_id and pending[0]["Source"] == 3)
        run.check("待确认账单金额/类型（金额按应用写入的文本写法存：18 → \"18.0\"，去重的 `=` 才对得上）", pending[0]["Amount"] == "18.0" and pending[0]["Type"] == BILL_TYPE_EXPENSE, str(pending[0]["Amount"]))
        decimals = {value: to_db_decimal(value) for value in (7, "7.00", 8.5, "8.50", 12345.5, 35, "0", 700, "0.125", "-7", "-7.50", "1E+2")}
        run.check(
            "to_db_decimal 与 Microsoft.Data.Sqlite 的 decimal 文本写法一致：至少一位小数、去掉多余的尾零（含负数、科学计数法输入）",
            decimals == {
                7: "7.0", "7.00": "7.0", 8.5: "8.5", "8.50": "8.5", 12345.5: "12345.5", 35: "35.0", "0": "0.0", 700: "700.0", "0.125": "0.125",
                "-7": "-7.0", "-7.50": "-7.5", "1E+2": "100.0",
            },
            str(decimals),
        )
        db.delete_pending_bills()
        run.check("delete_pending_bills 清空待确认账单", db.pending_bills() == [])
        db.seed_pending_bill(amount=5, when=datetime(2026, 11, 24, 9, 5), note="墙上时间自测")
        run.check(
            "seed_pending_bill 按墙上时间原样写入交易时间（不换算 UTC：应用存的就是设备本地时间）",
            db.pending_bills()[0]["TransactionTime"] == "2026-11-24 09:05:00",
            str(db.pending_bills()[0]["TransactionTime"]),
        )
        db.delete_pending_bills()

        # 预算（首页「本月预算」用例造数据）
        run.check(
            "category_id_by_name 按名称与类型取预设分类",
            db.category_id_by_name("餐饮").endswith("0001") and db.category_id_by_name("工资", BILL_TYPE_INCOME).endswith("0101"),
        )
        try:
            db.category_id_by_name("不存在的分类")
            run.check("category_id_by_name 找不到时抛 LookupError", False)
        except LookupError:
            run.check("category_id_by_name 找不到时抛 LookupError", True)
        total_budget_id = db.seed_budget(user_id, amount=5000)
        category_budget_id = db.seed_budget(user_id, amount=1500.5, category_name="餐饮", ledger_id=ledger_id, year=2026, month=3)
        with db._connect() as con:
            budgets = {r["Id"]: dict(r) for r in con.execute('SELECT * FROM "Budgets"')}
        total_row, category_row = budgets[total_budget_id], budgets[category_budget_id]
        today = datetime.now()
        run.check(
            "seed_budget 总预算：无分类、无账本、默认本月",
            total_row["CategoryId"] is None and total_row["LedgerId"] is None and total_row["UserId"] == user_id
            and int(total_row["Year"]) == today.year and int(total_row["Month"]) == today.month
            and total_row["Amount"] == "5000" and int(total_row["IsDeleted"]) == 0,
            str(total_row),
        )
        run.check(
            "seed_budget 分类预算：按分类名取预设分类、可指定账本与年月",
            category_row["CategoryId"].endswith("0001") and category_row["LedgerId"] == ledger_id
            and int(category_row["Year"]) == 2026 and int(category_row["Month"]) == 3 and category_row["Amount"] == "1500.5",
            str(category_row),
        )

        run.check("bills_of_user 默认排除软删除", len(db.bills_of_user(user_id)) == 2)

        # 预算页（18 号）：读预算
        run.check(
            "budgets_of_user 读出自己未删除的预算，budget_row 按 Id 读单条",
            {b["Id"] for b in db.budgets_of_user(user_id)} == {total_budget_id, category_budget_id}
            and db.budget_row(total_budget_id)["Amount"] == "5000" and db.budget_row("00000000-0000-0000-0000-000000000000") is None,
        )
        with db._connect() as con:
            con.execute('UPDATE "Budgets" SET IsDeleted = 1 WHERE Id = ?', (category_budget_id,))
        run.check(
            "budgets_of_user 默认排除已软删除的；include_deleted=True 或 budget_row 能读到（用来核对软删除）",
            [b["Id"] for b in db.budgets_of_user(user_id)] == [total_budget_id]
            and {b["Id"] for b in db.budgets_of_user(user_id, include_deleted=True)} == {total_budget_id, category_budget_id}
            and db.budget_row(category_budget_id)["IsDeleted"] == 1 and db.budget_row(category_budget_id, include_deleted=False) is None,
        )

        # 分类 / 支付渠道管理页的造数据与读库（14 号）
        custom_cat = db.seed_category(user_id, "自测分类", bill_type=BILL_TYPE_INCOME, icon="mdi-cash", sort_order=3)
        deleted_cat = db.seed_category(user_id, "已删自测分类", deleted=True)
        db.seed_category(other_id, "别人的自测分类")
        cat = db.category_row(custom_cat)
        run.check(
            "seed_category 写入大写 GUID、自定义（非预设）、类型 / 图标 / 排序 / 所有者",
            custom_cat == custom_cat.upper() and cat["IsPreset"] == 0 and cat["Type"] == BILL_TYPE_INCOME and cat["Icon"] == "mdi-cash"
            and cat["SortOrder"] == 3 and cat["UserId"] == user_id and cat["IsDeleted"] == 0,
            str(cat),
        )
        run.check("custom_categories 只含自己未删除的", [c["Name"] for c in db.custom_categories(user_id)] == ["自测分类"])
        run.check(
            "visible_category_count = 预设 2 + 自己未删除的自定义 1（别人的、已删的不算）；可按类型只数一种",
            db.visible_category_count(user_id) == 3 and db.visible_category_count(user_id, BILL_TYPE_INCOME) == 2
            and db.visible_category_count(user_id, BILL_TYPE_EXPENSE) == 1,
            f"{db.visible_category_count(user_id)}",
        )
        run.check(
            "preset_category_names 按类型取预设名称",
            db.preset_category_names() == ["餐饮"] and db.preset_category_names(BILL_TYPE_INCOME) == ["工资"],
        )
        run.check(
            "category_row 默认含已软删除的，include_deleted=False 时读不到",
            db.category_row(deleted_cat)["IsDeleted"] == 1 and db.category_row(deleted_cat, include_deleted=False) is None,
        )

        custom_channel = db.seed_channel(user_id, "自测渠道", icon="mdi-bank", sort_order=4)
        deleted_channel = db.seed_channel(user_id, "已删自测渠道", deleted=True)
        db.seed_channel(other_id, "别人的自测渠道")
        chan = db.channel_row(custom_channel)
        run.check(
            "seed_channel 写入大写 GUID、自定义（非预设）、图标 / 排序 / 所有者",
            custom_channel == custom_channel.upper() and chan["IsPreset"] == 0 and chan["Icon"] == "mdi-bank"
            and chan["SortOrder"] == 4 and chan["UserId"] == user_id and chan["IsDeleted"] == 0,
            str(chan),
        )
        run.check("custom_channels 只含自己未删除的", [c["Name"] for c in db.custom_channels(user_id)] == ["自测渠道"])
        run.check("visible_channel_count = 预设 1 + 自己未删除的自定义 1", db.visible_channel_count(user_id) == 2, str(db.visible_channel_count(user_id)))
        run.check("preset_channel_names 取预设渠道名称", db.preset_channel_names() == ["微信支付"])
        run.check(
            "channel_row 默认含已软删除的，include_deleted=False 时读不到",
            db.channel_row(deleted_channel)["IsDeleted"] == 1 and db.channel_row(deleted_channel, include_deleted=False) is None,
        )

        run.check(
            "默认分类 / 默认渠道没设置过时为 None",
            db.default_category_id(user_id) is None and db.default_category_id(user_id, BILL_TYPE_INCOME) is None and db.default_channel_id(user_id) is None,
        )
        with db._connect() as con:
            con.execute(
                'UPDATE "UserDefaults" SET DefaultExpenseCategoryId = ?, DefaultIncomeCategoryId = ?, DefaultPaymentChannelId = ? WHERE UserId = ?',
                (db.category_id_by_name("餐饮"), custom_cat, custom_channel, user_id),
            )
        run.check(
            "default_category_id 支出 / 收入各读各的，default_channel_id 读回",
            db.default_category_id(user_id) == db.category_id_by_name("餐饮") and db.default_category_id(user_id, BILL_TYPE_INCOME) == custom_cat
            and db.default_channel_id(user_id) == custom_channel,
        )
        db.set_user_defaults(user_id, expense_category_id=db.category_id_by_name("餐饮"), channel_id=custom_channel)
        with db._connect() as con:
            defaults_rows = con.execute('SELECT COUNT(*) FROM "UserDefaults" WHERE UserId = ?', (user_id,)).fetchone()[0]
        run.check(
            "set_user_defaults 覆盖已有的一行（没传的置空，不重复插入）",
            defaults_rows == 1 and db.default_category_id(user_id) == db.category_id_by_name("餐饮")
            and db.default_category_id(user_id, BILL_TYPE_INCOME) is None and db.default_channel_id(user_id) == custom_channel
            and db.default_ledger_id(user_id) is None,
            str(defaults_rows),
        )
        other_user = db.seed_user()
        db.set_user_defaults(other_user, income_category_id=custom_cat)
        run.check(
            "set_user_defaults 没有记录时插入",
            db.default_category_id(other_user, BILL_TYPE_INCOME) == custom_cat and db.default_category_id(other_user) is None,
        )

        # 结构不一致时给出清晰报错
        con = sqlite3.connect(path)
        con.execute('ALTER TABLE "Bills" RENAME COLUMN "LedgerId" TO "LedgerRef"')
        con.commit()
        con.close()
        try:
            db.verify_schema()
            run.check("列缺失时 verify_schema 抛 SchemaMismatch", False)
        except SchemaMismatch as exc:
            run.check("列缺失时 verify_schema 抛 SchemaMismatch 并点名缺失列", "LedgerId" in str(exc), str(exc))

    run.check("unique_phone 格式合法", all(len(p) == 11 and p.startswith("13") and p.isdigit() for p in (unique_phone() for _ in range(5))))
    run.check("unique_phone 互不重复", len({unique_phone() for _ in range(200)}) == 200)
    run.check("unique_name 带前缀且互不重复", len({unique_name("账本") for _ in range(50)}) >= 2 and unique_name("账本").startswith("账本"))

    real = LocalDb()
    if real.exists():
        try:
            real.verify_schema()
            run.check("真实本地库结构满足造数据假设", True)
        except SchemaMismatch as exc:
            run.check("真实本地库结构满足造数据假设", False, str(exc))
    else:
        run.skip("真实本地库结构核对", f"{real.path} 不存在（Web.Server 尚未启动过）")


def _call(cloud, method: str, path: str, body=None, token=None):
    """向假云端发一个请求，返回 (状态码, 解析后的 JSON 或 None)。"""
    data = None if body is None else json.dumps(body).encode("utf-8")
    request = urllib.request.Request(f"http://127.0.0.1:{cloud.port}{path}", data=data, method=method)
    if data is not None:
        request.add_header("Content-Type", "application/json")
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    try:
        with urllib.request.urlopen(request, timeout=5) as response:
            raw = response.read()
            return response.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as err:
        raw = err.read()
        return err.code, (json.loads(raw) if raw else None)


def test_fake_cloud(run: TestRun) -> None:
    """假云端的协议自测：状态码 / 消息要与真实接口一致，否则用它验证出来的结论不可信。"""
    try:
        cloud = FakeCloud(port=CLOUD_PORT).start()
    except CloudPortBusy as exc:
        run.skip("假云端协议自测", str(exc))
        return
    try:
        run.check("假云端监听在约定端口", cloud.port == CLOUD_PORT and cloud.url.endswith(f":{CLOUD_PORT}"))

        # ---- 登录与令牌
        status, body = _call(cloud, "POST", "/api/auth/login", {"phoneNumber": ""})
        run.check("登录：空手机号 400「手机号不能为空」", status == 400 and body["message"] == "手机号不能为空", str(body))
        status, body = _call(cloud, "POST", "/api/auth/login", {"phoneNumber": "123"})
        run.check("登录：长度不对 400「手机号必须是11位」", status == 400 and body["message"] == "手机号必须是11位", str(body))
        status, body = _call(cloud, "POST", "/api/auth/login", {"phoneNumber": "23456789012"})
        run.check("登录：非 1 开头 400「手机号格式不正确」", status == 400 and body["message"] == "手机号格式不正确", str(body))

        owner_phone, editor_phone, viewer_phone, stranger_phone = "13800010001", "13800010002", "13800010003", "13800010004"
        status, owner_login = _call(cloud, "POST", "/api/auth/login", {"phoneNumber": owner_phone})
        run.check("登录成功返回 userId / phoneNumber / token", status == 200 and set(owner_login) >= {"userId", "phoneNumber", "token"})
        payload = json.loads(base64.urlsafe_b64decode(owner_login["token"].split(".")[1] + "=="))
        run.check(
            "令牌载荷带 nameid 与 phone_number（客户端 JwtClaimReader 读取）",
            payload["nameid"] == owner_login["userId"] and payload["phone_number"] == owner_phone,
            str(payload),
        )
        owner = owner_login["token"]
        run.check("同一手机号再次登录得到同一个云端用户 Id", _call(cloud, "POST", "/api/auth/login", {"phoneNumber": owner_phone})[1]["userId"] == owner_login["userId"])

        status, _ = _call(cloud, "POST", "/api/sync/ledgermembers/pull", {})
        run.check("不带令牌 401", status == 401)
        status, _ = _call(cloud, "POST", "/api/sync/ledgermembers/pull", {}, token="a.b.c")
        run.check("令牌无效 401", status == 401)

        editor = _call(cloud, "POST", "/api/auth/login", {"phoneNumber": editor_phone})[1]["token"]
        viewer = _call(cloud, "POST", "/api/auth/login", {"phoneNumber": viewer_phone})[1]["token"]
        stranger = _call(cloud, "POST", "/api/auth/login", {"phoneNumber": stranger_phone})[1]["token"]

        # ---- 账本 + 成员
        ledger_id = str(uuid.uuid4())
        owner_member = cloud.add_ledger(ledger_id, owner_phone, "自测账本")
        editor_member = cloud.add_member(ledger_id, editor_phone, role=ROLE_EDITOR)
        viewer_member = cloud.add_member(ledger_id, viewer_phone, role=ROLE_VIEWER, status=MEMBER_PENDING)

        status, body = _call(cloud, "POST", "/api/sync/ledgermembers/pull", {}, token=owner)
        ids = {m["id"] for m in body["members"]}
        run.check(
            "拉取：所有者拿到自己的行 + 其他生效成员，不含别人的待确认行",
            status == 200 and ids == {owner_member, editor_member}, str(ids),
        )
        sample = next(m for m in body["members"] if m["id"] == editor_member)
        run.check(
            "拉取的成员行带手机号 / 账本名 / 所有者 Id（客户端据此补建存根）",
            sample["userPhoneNumber"] == editor_phone and sample["ledgerName"] == "自测账本"
            and sample["ledgerOwnerId"] == owner_login["userId"] and sample["role"] == ROLE_EDITOR and sample["status"] == MEMBER_ACTIVE,
            str(sample),
        )
        body = _call(cloud, "POST", "/api/sync/ledgermembers/pull", {}, token=viewer)[1]
        run.check(
            "拉取：待确认的被邀请人拿到自己的行 + 该账本其他生效成员（真实服务端按「我的未删除成员行」圈定账本，含待确认）",
            {m["id"] for m in body["members"]} == {viewer_member, owner_member, editor_member},
            str([m["id"] for m in body["members"]]),
        )
        body = _call(cloud, "POST", "/api/sync/ledgermembers/pull", {}, token=stranger)[1]
        run.check("拉取：无关用户拿不到任何成员行", body["members"] == [])

        # ---- 邀请
        base = f"/api/ledgers/{ledger_id}/members"
        status, body = _call(cloud, "POST", base, {"PhoneNumber": "", "Role": ROLE_EDITOR}, token=owner)
        run.check("邀请：空手机号 400", status == 400 and body["message"] == "手机号不能为空")
        status, body = _call(cloud, "POST", base, {"PhoneNumber": "13899999999", "Role": ROLE_EDITOR}, token=owner)
        run.check("邀请：手机号未注册 400「该手机号尚未注册」", status == 400 and body["message"] == "该手机号尚未注册", str(body))
        status, body = _call(cloud, "POST", base, {"PhoneNumber": editor_phone, "Role": ROLE_EDITOR}, token=owner)
        run.check("邀请：已是成员 409", status == 409 and body["message"] == "该用户已是账本成员", str(body))
        status, body = _call(cloud, "POST", base, {"PhoneNumber": viewer_phone, "Role": ROLE_EDITOR}, token=owner)
        run.check("邀请：已被邀请待确认 409", status == 409 and body["message"] == "该用户已被邀请，等待确认", str(body))
        status, body = _call(cloud, "POST", base, {"PhoneNumber": owner_phone, "Role": ROLE_EDITOR}, token=owner)
        run.check("邀请：邀请所有者本人 409", status == 409 and body["message"] == "该用户是账本所有者", str(body))
        status, body = _call(cloud, "POST", base, {"PhoneNumber": stranger_phone, "Role": ROLE_OWNER}, token=owner)
        run.check("邀请：不能邀请 Owner 角色 409", status == 409 and "Owner" in body["message"], str(body))
        status, body = _call(cloud, "POST", base, {"PhoneNumber": stranger_phone, "Role": ROLE_EDITOR}, token=editor)
        run.check("邀请：非所有者 403「只有账本所有者可以邀请成员」", status == 403 and body["message"] == "只有账本所有者可以邀请成员", str(body))
        status, body = _call(cloud, "POST", base, {"PhoneNumber": stranger_phone, "Role": ROLE_VIEWER}, token=owner)
        run.check(
            "邀请成功：200 + 完整成员 DTO（待确认）",
            status == 200 and body["status"] == MEMBER_PENDING and body["role"] == ROLE_VIEWER
            and body["userPhoneNumber"] == stranger_phone and body["ledgerId"] == ledger_id,
            str(body),
        )
        invited_member = body["id"]

        # ---- 响应邀请
        status, body = _call(cloud, "POST", f"/api/invitations/{editor_member}/respond", {"Accept": True}, token=editor)
        run.check("响应邀请：已处理的邀请 404「邀请不存在或已处理」", status == 404 and body["message"] == "邀请不存在或已处理", str(body))
        status, _ = _call(cloud, "POST", f"/api/invitations/{invited_member}/respond", {"Accept": True}, token=viewer)
        run.check("响应邀请：不是自己的邀请 404", status == 404)
        status, _ = _call(cloud, "POST", f"/api/invitations/{invited_member}/respond", {"Accept": True}, token=stranger)
        run.check("接受邀请 204，成员变为生效", status == 204 and cloud.member(invited_member)["status"] == MEMBER_ACTIVE)
        status, _ = _call(cloud, "POST", f"/api/invitations/{viewer_member}/respond", {"Accept": False}, token=viewer)
        run.check("拒绝邀请 204，成员行标记删除", status == 204 and cloud.member(viewer_member)["isDeleted"] is True)
        body = _call(cloud, "POST", "/api/sync/ledgermembers/pull", {}, token=viewer)[1]
        run.check("拉取：被拒绝的行仍返回给本人（客户端据此清掉邀请卡）", [(m["id"], m["isDeleted"]) for m in body["members"]] == [(viewer_member, True)], str(body))

        # ---- 改角色 / 移除 / 转让 / 退出
        role_path = f"{base}/{editor_member}/role"
        status, body = _call(cloud, "PUT", role_path, {"Role": ROLE_VIEWER}, token=editor)
        run.check("改角色：非所有者 403「只有账本所有者可以执行此操作」", status == 403 and body["message"] == "只有账本所有者可以执行此操作", str(body))
        status, body = _call(cloud, "PUT", role_path, {"Role": ROLE_OWNER}, token=owner)
        run.check("改角色：不能改成 Owner 400", status == 400 and body["message"] == "请使用转让所有权变更 Owner", str(body))
        status, body = _call(cloud, "PUT", f"{base}/{owner_member}/role", {"Role": ROLE_VIEWER}, token=owner)
        run.check("改角色：不能改所有者 400", status == 400 and body["message"] == "不能修改账本所有者的角色", str(body))
        status, _ = _call(cloud, "PUT", role_path, {"Role": ROLE_VIEWER}, token=owner)
        run.check("改角色成功 204", status == 204 and cloud.member(editor_member)["role"] == ROLE_VIEWER)

        status, body = _call(cloud, "DELETE", f"{base}/{owner_member}", token=owner)
        run.check("移除：不能移除所有者 400", status == 400 and body["message"] == "不能移除账本所有者，请先转让所有权", str(body))
        status, body = _call(cloud, "DELETE", f"{base}/{editor_member}", token=editor)
        run.check("移除：非所有者 403", status == 403, str(body))
        status, body = _call(cloud, "DELETE", f"{base}/{str(uuid.uuid4())}", token=owner)
        run.check("移除：成员不存在 404「成员不存在」", status == 404 and body["message"] == "成员不存在", str(body))
        status, _ = _call(cloud, "DELETE", f"{base}/{invited_member}", token=owner)
        run.check("移除成功 204，成员行标记删除", status == 204 and cloud.member(invited_member)["isDeleted"] is True)
        body = _call(cloud, "POST", "/api/sync/ledgermembers/pull", {}, token=owner)[1]
        run.check(
            "拉取：所有者再也拉不到被移除成员的行（与真实服务端一致——客户端必须自己把本地行标记删除）",
            invited_member not in {m["id"] for m in body["members"]},
        )

        transfer = f"/api/ledgers/{ledger_id}/transfer-ownership"
        editor_user = cloud.user_id(editor_phone)
        status, body = _call(cloud, "POST", transfer, {"ToUserId": owner_login["userId"]}, token=owner)
        run.check("转让：不能转让给自己 400", status == 400 and body["message"] == "不能转让给自己", str(body))
        status, body = _call(cloud, "POST", transfer, {"ToUserId": str(uuid.uuid4())}, token=owner)
        run.check("转让：目标不是生效成员 404", status == 404 and body["message"] == "目标用户不是该账本的生效成员", str(body))
        status, body = _call(cloud, "POST", transfer, {"ToUserId": editor_user}, token=editor)
        run.check("转让：非所有者 403", status == 403, str(body))
        status, _ = _call(cloud, "POST", transfer, {"ToUserId": editor_user}, token=owner)
        run.check(
            "转让成功 204：新所有者是 Owner、原所有者降为 Editor、账本归属变更",
            status == 204 and cloud.member(editor_member)["role"] == ROLE_OWNER
            and cloud.member(owner_member)["role"] == ROLE_EDITOR and cloud.ledger(ledger_id)["ownerId"] == editor_user,
        )

        exit_path = f"/api/ledgers/{ledger_id}/exit"
        status, body = _call(cloud, "POST", exit_path, token=editor)
        run.check("退出：所有者 400「账本所有者不能退出账本，请先转让所有权」", status == 400 and body["message"] == "账本所有者不能退出账本，请先转让所有权", str(body))
        status, body = _call(cloud, "POST", exit_path, token=stranger)
        run.check("退出：已被移除的人 404「你不是该账本成员」", status == 404 and body["message"] == "你不是该账本成员", str(body))
        status, _ = _call(cloud, "POST", exit_path, token=owner)
        run.check("退出成功 204（转让后原所有者可以退出）", status == 204 and cloud.member(owner_member)["isDeleted"] is True)

        # ---- 故障注入 / 记录 / 未实现接口
        cloud.fail("POST", r"/exit$", status=403, message="故障注入 403")
        status, body = _call(cloud, "POST", exit_path, token=editor)
        run.check("fail()：按指定状态码失败一次", status == 403 and body["message"] == "故障注入 403", str(body))
        status, body = _call(cloud, "POST", exit_path, token=editor)
        run.check("fail() 只生效 times 次，随后恢复正常逻辑", status == 400, str(body))

        # delay()：请求先记录、睡在锁外，期间别的请求不受影响；到点后按正常逻辑处理，只生效 times 次
        exits_before = len(cloud.calls("POST", r"/exit$"))
        cloud.delay("POST", r"/exit$", 0.9)
        outcome: list = []
        worker = threading.Thread(target=lambda: outcome.append(_call(cloud, "POST", exit_path, token=editor)))
        began = time.time()
        worker.start()
        arrived = cloud.wait_request("POST", r"/exit$", timeout_s=2.0, count=exits_before + 1)
        status_while_waiting = arrived.status if arrived is not None else None
        meanwhile_started = time.time()
        _call(cloud, "GET", "/api/whatever", token=owner)
        meanwhile = time.time() - meanwhile_started
        worker.join(5)
        elapsed = time.time() - began
        run.check(
            "delay()：请求已到达并记录（尚未处理）、期间别的请求不被卡住、到点后按正常逻辑响应",
            arrived is not None and status_while_waiting == 0 and meanwhile < 0.5 and elapsed >= 0.85 and outcome and outcome[0][0] == 400,
            f"到达={arrived is not None} 等待中状态={status_while_waiting} 期间别的请求耗时={meanwhile:.2f}s 总耗时={elapsed:.2f}s 结果={outcome}",
        )
        began = time.time()
        status, _ = _call(cloud, "POST", exit_path, token=editor)
        run.check("delay() 只生效 times 次，随后不再变慢", status == 400 and time.time() - began < 0.5)

        status, body = _call(cloud, "GET", "/api/whatever", token=owner)
        run.check("未实现的接口 404 且被记录", status == 404 and cloud.calls("GET", r"/api/whatever$")[0].status == 404, str(body))
        recorded = cloud.calls("PUT", r"/role$")
        run.check(
            "记录请求：方法 / 路径 / 请求体 / 令牌里的手机号 / 状态码（含失败的请求）",
            len(recorded) == 4 and [r.status for r in recorded] == [403, 400, 400, 204]
            and recorded[-1].body == {"Role": ROLE_VIEWER} and recorded[-1].phone == owner_phone,
            repr(recorded),
        )
        run.check("wait_request 已发生的请求立即返回", cloud.wait_request("POST", r"/exit$", timeout_s=1) is not None)
        run.check("wait_request 超时返回 None", cloud.wait_request("DELETE", r"/never$", timeout_s=0.2) is None)

        # ---- CloudWorld：本地库与云端的数据一致
        with tempfile.TemporaryDirectory() as tmp:
            path = str(Path(tmp) / "e2e_world.db")
            make_temp_db(path)
            db = LocalDb(path)
            world = CloudWorld(cloud, db)
            me = world.me("13800000000")
            run.check("CloudWorld.me：本地用户 Id 与云端用户 Id 各自独立", me.local_id == db.user_id("13800000000") and me.local_id.lower() != me.cloud_id)
            friend = world.person()
            run.check("CloudWorld.person：「别人」两边用同一个 Id", friend.local_id.lower() == friend.cloud_id and db.user_id(friend.phone) == friend.local_id)
            own = db.seed_ledger(me.local_id, "世界账本")
            owner_row = world.own_ledger(me, own, "世界账本")
            member_row = world.add_member(own, friend, role=ROLE_VIEWER)
            local_rows = {m["Id"]: m for m in db.ledger_members(own)}
            run.check(
                "CloudWorld：本地与云端的成员行 Id、角色一致",
                set(local_rows) == {owner_row.upper(), member_row.upper()}
                and local_rows[member_row.upper()]["Role"] == ROLE_VIEWER and cloud.member(member_row)["role"] == ROLE_VIEWER,
                str(local_rows),
            )
            foreign = world.foreign_ledger(friend, "别人的账本", me=me, my_role=ROLE_EDITOR)
            row = db.ledger_row(foreign.ledger_id)
            run.check(
                "CloudWorld.foreign_ledger：账本在本地属于别人，云端所有者也是别人，我是其中的 Editor",
                row["UserId"] == friend.local_id and cloud.ledger(foreign.ledger_id)["ownerId"] == friend.cloud_id
                and cloud.member(foreign.my_member_id)["userId"] == me.cloud_id
                and {m["UserId"] for m in db.ledger_members(foreign.ledger_id)} == {friend.local_id, me.local_id},
            )
    finally:
        cloud.stop()

    # 停止后端口立即可重用（测试脚本会反复起停）
    try:
        FakeCloud(port=CLOUD_PORT).start().stop()
        run.check("假云端停止后端口释放", True)
    except CloudPortBusy as exc:
        run.check("假云端停止后端口释放", False, str(exc))


#: 微信账单样例（CRLF，带说明段落）：3 条有效 + 3 条被跳过（中性 / 无效状态 / 零金额）+ 1 条日期坏掉静默丢弃
WECHAT_SAMPLE = (
    "微信支付账单明细\r\n微信昵称：[测试]\r\n----------------------微信支付账单明细列表--------------------\r\n"
    "交易时间,交易类型,交易对方,商品,收/支,金额(元),支付方式,当前状态,交易单号,商户单号,备注\r\n"
    '2024/5/1 12:30,商户消费,美团外卖,午餐外卖,支出,¥38.50,零钱,支付成功,4200001,M1,"/"\r\n'
    '2024/5/2 9:05,转账,小王,转账备注,收入,"¥1,200.00",中国银行储蓄卡(1234),已收钱,4200002,/,/\r\n'
    "2024/5/3 18:00,微信红包,/,/,收入,¥8.80,/,已存入零钱,4200003,/,/\r\n"
    "2024/5/4 10:00,零钱充值,/,/,/,¥100.00,招商银行信用卡(5678),充值完成,4200004,/,/\r\n"
    "2024/5/5 11:00,商户消费,某商家,退款商品,支出,¥20.00,零钱,已全额退款,4200005,/,/\r\n"
    "2024/5/6 11:00,商户消费,某商家,零元商品,支出,¥0.00,零钱,支付成功,4200006,/,/\r\n"
    "坏行,商户消费,x,x,支出,¥1.00,零钱,支付成功,4200007,/,/\r\n"
)
#: 支付宝账单样例（官方导出文件的真实列：交易时间,交易分类,交易对方,对方账号,商品说明,收/支,金额,收/付款方式,交易状态,交易订单号,商家订单号,备注，
#: 解析器按表头名称定位）：2 条有效 + 不计收支 / 交易关闭 / 坏时间各 1 条被丢弃；订单号字段末尾带制表符、行尾有多余的逗号，与真实文件一致
ALIPAY_SAMPLE = (
    "支付宝交易记录明细查询\n账号:[x]\n"
    "交易时间,交易分类,交易对方,对方账号,商品说明,收/支,金额,收/付款方式,交易状态,交易订单号,商家订单号,备注,\n"
    "2024-05-01 08:40:44,餐饮,某店,abc***@qq.com,早餐,支出,12.50,余额,交易成功,A001\t,M001\t,,\n"
    "2024-05-02 09:00:00,转账,某人,hr***@corp.com,工资,收入,5000.00,余额,交易成功,A002\t,,,\n"
    "2024-05-03 12:00:00,信用卡还款,某银行,,信用卡还款,不计收支,2000.00,余额,还款成功,A003\t,,,\n"
    "2024-05-04 13:00:00,数码,某商城,shop***@mall.com,耳机,支出,299.00,花呗,交易关闭,A004\t,M004\t,,\n"
    "bad-time,,,,x,支出,1.00,余额,交易成功,A005\t,,,\n"
)


def test_fake_cloud_import(run: TestRun) -> None:
    """假云端的「导入 + 同步桩」自测：解析规则、校验顺序、状态码与消息要与 ImportController / CsvParserService 一致。"""
    # ---- CSV 解析（纯函数）
    run.check(
        "split_csv_line：引号内的逗号不分隔、引号丢弃、字段去首尾空白",
        split_csv_line(' a , "b,c" ,"¥1,234.50", ') == ["a", "b,c", "¥1,234.50", ""],
        str(split_csv_line(' a , "b,c" ,"¥1,234.50", ')),
    )
    bills, skipped = parse_wechat_csv(WECHAT_SAMPLE)
    run.check("微信解析：3 条有效、3 条跳过（中性 / 已全额退款 / 零金额），日期坏掉的行静默丢弃（不计入跳过）", len(bills) == 3 and skipped == 3, f"{len(bills)} / {skipped}")
    first, second, third = bills
    run.check(
        "微信解析：支出行——商品作描述、交易对方作商家、¥ 去掉、零钱 → 微信支付、时间不带时区后缀",
        first == {
            "transactionTime": "2024-05-01T12:30:00", "description": "午餐外卖", "merchantName": "美团外卖", "amount": 38.5,
            "isIncome": False, "paymentChannel": "微信支付", "transactionId": "4200001",
        },
        str(first),
    )
    run.check(
        "微信解析：收入行——千分位逗号、储蓄卡 → 银行卡、「9:05」这种不补零的时间",
        second["amount"] == 1200.0 and second["isIncome"] is True and second["paymentChannel"] == "银行卡"
        and second["transactionTime"] == "2024-05-02T09:05:00" and second["description"] == "转账备注",
        str(second),
    )
    run.check(
        "微信解析：商品 / 备注都是占位符「/」时描述回退到交易类型，商家回退到交易类型，支付方式占位 → 微信支付",
        third["description"] == "微信红包" and third["merchantName"] == "微信红包" and third["paymentChannel"] == "微信支付",
        str(third),
    )
    run.check("微信解析：找不到表头（交易时间 + 交易单号）→ 空", parse_wechat_csv("交易时间,金额\r\n2024/5/1 1:00,1") == ([], 0))
    alipay = parse_alipay_csv(ALIPAY_SAMPLE)
    run.check(
        "支付宝解析（按表头名称定位）：2 条有效；收/支列决定收支（不看金额符号 / 对方账号），渠道固定「支付宝」，不计收支 / 交易关闭 / 坏时间的行丢弃",
        len(alipay) == 2 and alipay[0]["isIncome"] is False and alipay[0]["amount"] == 12.5 and alipay[1]["isIncome"] is True
        and alipay[1]["amount"] == 5000.0 and alipay[0]["paymentChannel"] == "支付宝" and alipay[0]["transactionId"] == "A001"
        and alipay[0]["description"] == "早餐" and alipay[0]["merchantName"] == "某店"
        and alipay[1]["description"] == "工资" and alipay[1]["merchantName"] == "某人" and alipay[1]["transactionId"] == "A002",
        str(alipay),
    )
    shuffled = parse_alipay_csv(
        "备注,交易订单号,交易状态,金额,收/支,商品说明,交易对方,交易时间\n"
        ",ORDER-1\t,交易成功,45.60,收入,红包,朋友,2024-06-01 20:00:00\n"
    )
    run.check(
        "支付宝解析：列顺序不同也按表头名称取列（商家取「交易对方」、单号取「交易订单号」）",
        len(shuffled) == 1 and shuffled[0]["isIncome"] is True and shuffled[0]["amount"] == 45.6 and shuffled[0]["merchantName"] == "朋友"
        and shuffled[0]["description"] == "红包" and shuffled[0]["transactionId"] == "ORDER-1" and shuffled[0]["transactionTime"] == "2024-06-01T20:00:00",
        str(shuffled),
    )
    run.check(
        "支付宝解析：缺必需的列（没有「收/支」/ 没有「金额」/ 找不到表头）→ 空",
        parse_alipay_csv("交易时间,交易对方,金额\n2024-05-01 08:40:44,某店,12.50\n") == []
        and parse_alipay_csv("交易时间,交易对方,收/支\n2024-05-01 08:40:44,某店,支出\n") == []
        and parse_alipay_csv("没有表头\n") == [],
    )

    # ---- 路由
    try:
        cloud = FakeCloud(port=CLOUD_PORT).start()
    except CloudPortBusy as exc:
        run.skip("假云端导入 / 同步桩自测", str(exc))
        return
    try:
        def login_as(phone: str) -> str:
            return _call(cloud, "POST", "/api/auth/login", {"phoneNumber": phone})[1]["token"]

        owner_phone, editor_phone, viewer_phone, stranger_phone = "13800020001", "13800020002", "13800020003", "13800020004"
        owner, editor, viewer, stranger = (login_as(p) for p in (owner_phone, editor_phone, viewer_phone, stranger_phone))
        ledger_id = str(uuid.uuid4())
        cloud.add_ledger(ledger_id, owner_phone, "导入自测账本")
        cloud.add_member(ledger_id, editor_phone, role=ROLE_EDITOR)
        cloud.add_member(ledger_id, viewer_phone, role=ROLE_VIEWER)

        def post_import(token, kind="wechat", **body):
            return _call(cloud, "POST", f"/api/import/{kind}", {"CsvContent": WECHAT_SAMPLE, **body}, token=token)

        status, _ = _call(cloud, "POST", "/api/import/wechat", {"CsvContent": WECHAT_SAMPLE})
        run.check("导入接口需要令牌：不带令牌 401", status == 401)
        status, _ = _call(cloud, "POST", "/api/import/wechat/preview", {"CsvContent": WECHAT_SAMPLE})
        run.check("预览接口同样需要令牌：不带令牌 401", status == 401)

        # 校验顺序：CSV 非空 → 时区偏移 → 账本必填 / 写权限
        status, body = post_import(owner, CsvContent="  \r\n ", UtcOffsetMinutes=9999)
        run.check("校验顺序：CSV 空白最先拦截 400「CSV 内容不能为空」（先于时区与账本）", status == 400 and body["message"] == "CSV 内容不能为空", str(body))
        status, body = post_import(owner, UtcOffsetMinutes=841, LedgerId=ledger_id)
        run.check("时区偏移越界 400「时区偏移必须在 -840 到 840 分钟之间」", status == 400 and body["message"] == "时区偏移必须在 -840 到 840 分钟之间", str(body))
        status, _ = post_import(owner, UtcOffsetMinutes=-840, LedgerId=ledger_id)
        run.check("偏移 -840（边界）合法", status == 200)
        cloud_imports_before = len(cloud.imports())
        status, body = post_import(owner, UtcOffsetMinutes=480)
        run.check("没带账本 400「请选择要导入到的账本」", status == 400 and body["message"] == "请选择要导入到的账本", str(body))
        status, body = post_import(owner, UtcOffsetMinutes=480, LedgerId="00000000-0000-0000-0000-000000000000")
        run.check("空 Guid 账本同样 400「请选择要导入到的账本」", status == 400 and body["message"] == "请选择要导入到的账本", str(body))
        status, body = post_import(owner, UtcOffsetMinutes=480, LedgerId=str(uuid.uuid4()))
        run.check("账本在云端不存在 400「账本不存在」（刚在本机建、还没同步上来的账本就是这个情形）", status == 400 and body["message"] == "账本不存在", str(body))
        status, body = post_import(stranger, UtcOffsetMinutes=480, LedgerId=ledger_id)
        run.check("不是成员 403「无权向该账本记账」", status == 403 and body["message"] == "无权向该账本记账", str(body))
        status, body = post_import(viewer, UtcOffsetMinutes=480, LedgerId=ledger_id)
        run.check("只读成员 403「只读成员不能在共享账本中记账」", status == 403 and body["message"] == "只读成员不能在共享账本中记账", str(body))
        run.check("校验没通过的请求都没有入库", len(cloud.imports()) == cloud_imports_before, f"{len(cloud.imports())} / {cloud_imports_before}")

        # 成功入库
        status, body = post_import(owner, UtcOffsetMinutes=480, LedgerId=ledger_id.upper())
        run.check(
            "所有者导入成功 200：totalCount / successCount / failCount / errors（账本 Id 大小写不敏感）",
            status == 200 and body == {"totalCount": 3, "successCount": 3, "failCount": 0, "errors": []},
            str(body),
        )
        last = cloud.imports("wechat")[-1]
        run.check(
            "记录里有收到的 CSV 原文、上报的偏移、账本与云端用户 Id",
            last["csv"] == WECHAT_SAMPLE and last["utcOffsetMinutes"] == 480 and last["ledgerId"] == ledger_id and last["userId"] == cloud.user_id(owner_phone),
        )
        run.check(
            "账单时间按上报的偏移从本地时间换算成 UTC（东八区 12:30 → 04:30Z）",
            last["bills"][0]["transactionTimeUtc"] == "2024-05-01T04:30:00Z" and last["bills"][1]["transactionTimeUtc"] == "2024-05-02T01:05:00Z",
            str([b["transactionTimeUtc"] for b in last["bills"]]),
        )
        status, _ = post_import(owner, LedgerId=ledger_id)
        run.check(
            "没上报偏移（旧版客户端）：本地时间原样当作 UTC，utcOffsetMinutes 记为 None",
            status == 200 and cloud.imports()[-1]["utcOffsetMinutes"] is None and cloud.imports()[-1]["bills"][0]["transactionTimeUtc"] == "2024-05-01T12:30:00Z",
        )
        status, body = post_import(editor, UtcOffsetMinutes=480, LedgerId=ledger_id)
        run.check("Editor 可以往共享账本里导入", status == 200 and body["successCount"] == 3, str(body))
        run.check("imported_bills 按账本过滤，每条带账本与用户", all(b["ledgerId"] == ledger_id for b in cloud.imported_bills(ledger_id)) and len(cloud.imported_bills(ledger_id.upper())) == len(cloud.imported_bills()))
        status, body = post_import(owner, kind="alipay", CsvContent=ALIPAY_SAMPLE, UtcOffsetMinutes=480, LedgerId=ledger_id)
        run.check("支付宝导入：解析出 2 条、成功 2 条", status == 200 and body["totalCount"] == 2 and body["successCount"] == 2, str(body))

        # 部分失败：fail_import_rows 一次性
        cloud.fail_import_rows({2: "分类不存在"})
        status, body = post_import(owner, UtcOffsetMinutes=480, LedgerId=ledger_id)
        run.check(
            "fail_import_rows：第 2 条入库失败 → successCount 2 / failCount 1 / errors「第 2 条: 分类不存在」",
            status == 200 and body == {"totalCount": 3, "successCount": 2, "failCount": 1, "errors": ["第 2 条: 分类不存在"]},
            str(body),
        )
        run.check("失败的账单不入库，原因记在导入记录里", len(cloud.imports()[-1]["bills"]) == 2 and cloud.imports()[-1]["errors"] == ["第 2 条: 分类不存在"])
        status, body = post_import(owner, UtcOffsetMinutes=480, LedgerId=ledger_id)
        run.check("fail_import_rows 只生效一次，下一次导入恢复正常", status == 200 and body["failCount"] == 0, str(body))
        many = "交易时间,交易类型,交易对方,商品,收/支,金额(元),支付方式,当前状态,交易单号\r\n" + "".join(
            f"2024/5/1 10:{i:02d},商户消费,店{i},商品{i},支出,¥{i + 1}.00,零钱,支付成功,T{i}\r\n" for i in range(12)
        )
        cloud.fail_import_rows({i: "boom" for i in range(1, 13)})
        status, body = post_import(owner, CsvContent=many, UtcOffsetMinutes=480, LedgerId=ledger_id)
        run.check(
            "errors 只返回前 10 条（failCount 仍是真实数量 12）",
            status == 200 and body["failCount"] == 12 and body["successCount"] == 0 and len(body["errors"]) == 10 and body["errors"][0] == "第 1 条: boom",
            str(body),
        )

        # 预览
        before = len(cloud.imports())
        status, body = _call(cloud, "POST", "/api/import/wechat/preview", {"CsvContent": WECHAT_SAMPLE}, token=stranger)
        run.check(
            "微信预览：不要求账本 / 权限，返回 totalCount / skippedCount / bills",
            status == 200 and body["totalCount"] == 3 and body["skippedCount"] == 3 and len(body["bills"]) == 3 and body["bills"][0]["description"] == "午餐外卖",
            str(body)[:200],
        )
        status, body = _call(cloud, "POST", "/api/import/alipay/preview", {"CsvContent": ALIPAY_SAMPLE}, token=owner)
        run.check("支付宝预览：只有 totalCount / bills，没有 skippedCount（与真实接口一致）", status == 200 and body["totalCount"] == 2 and "skippedCount" not in body, str(body)[:200])
        status, body = _call(cloud, "POST", "/api/import/alipay/preview", {"CsvContent": ""}, token=owner)
        run.check("预览：CSV 为空 400「CSV 内容不能为空」", status == 400 and body["message"] == "CSV 内容不能为空", str(body))
        run.check("预览不入库、不记入导入记录", len(cloud.imports()) == before)
        status, body = _call(cloud, "POST", "/api/import/wechat/preview", {"CsvContent": "随便一段不是账单的文字"}, token=owner)
        run.check("不是账单的文字：预览 200，没有任何账单（页面据此提示「未解析出任何账单」）", status == 200 and body["totalCount"] == 0 and body["bills"] == [], str(body))

        # 同步桩：账本推送登记账本（所有者以令牌为准），其余推送 / 拉取是「没有变化」
        pushed_id = str(uuid.uuid4())
        forged_owner = cloud.register_user(stranger_phone)
        status, body = _call(
            cloud, "POST", "/api/sync/ledgers/push",
            {"Ledgers": [{"Id": pushed_id, "Name": "本机新建的账本", "Icon": "mdi-book", "OwnerId": forged_owner}]}, token=owner,
        )
        run.check(
            "账本推送：登记云端不认识的账本，所有者以令牌为准（不信任 DTO 的 OwnerId），回 pushed",
            status == 200 and body["results"] == [{"ledgerId": pushed_id, "action": "pushed"}]
            and cloud.ledger(pushed_id)["ownerId"] == cloud.user_id(owner_phone) and cloud.ledger(pushed_id)["name"] == "本机新建的账本",
            str(body),
        )
        status, body = post_import(owner, UtcOffsetMinutes=480, LedgerId=pushed_id)
        run.check("推送之后往这个账本导入成功（「导入前先同步账本」要解决的就是这件事）", status == 200 and body["successCount"] == 3, str(body))
        status, body = _call(cloud, "POST", "/api/sync/ledgers/push", {"Ledgers": [{"Id": pushed_id, "Name": "改了名字", "Icon": "mdi-book"}]}, token=owner)
        run.check("再推送自己的账本：更新名称，仍是 pushed", status == 200 and body["results"][0]["action"] == "pushed" and cloud.ledger(pushed_id)["name"] == "改了名字", str(body))
        status, body = _call(cloud, "POST", "/api/sync/ledgers/push", {"Ledgers": [{"Id": pushed_id, "Name": "抢占", "Icon": "mdi-book"}]}, token=stranger)
        run.check("别人推送这个账本：skipped，名称不变", status == 200 and body["results"][0]["action"] == "skipped" and cloud.ledger(pushed_id)["name"] == "改了名字", str(body))
        empties = [
            ("/api/sync/push", "results"), ("/api/sync/categories/push", "results"), ("/api/sync/paymentchannels/push", "results"),
            ("/api/sync/pull", "bills"), ("/api/sync/categories/pull", "categories"), ("/api/sync/paymentchannels/pull", "paymentChannels"),
            ("/api/sync/ledgers/pull", "ledgers"),
        ]
        ok = True
        detail = []
        for path, key in empties:
            status, body = _call(cloud, "POST", path, {}, token=owner)
            ok = ok and status == 200 and body == {key: []}
            detail.append(f"{path}:{status}:{body}")
        run.check("其余推送 / 拉取接口：200 + 空结果（没有变化）", ok, "；".join(detail))
        run.check("同步与导入的请求都记录在 cloud.requests 里", len(cloud.calls("POST", r"/api/sync/ledgers/push$")) == 3 and len(cloud.calls("POST", r"/api/import/")) > 10)
        failed = _call(cloud, "POST", "/api/sync/pull", {}, token=None)[0]
        run.check("同步桩也需要令牌", failed == 401)

        # 拉取账单：设了 use_import_catalog 之后，把「我导入的账单」按 BillSyncDto 的形态还给客户端
        expense_category, income_category, channel = (str(uuid.uuid4()) for _ in range(3))
        cloud.use_import_catalog(expense_category_id=expense_category.upper(), income_category_id=income_category, channel_id=channel)
        imported = {b["id"]: b for b in cloud.imported_bills()}
        owner_cloud_id = cloud.user_id(owner_phone)
        owner_bill_ids = {bill_id for bill_id, b in imported.items() if b["userId"] == owner_cloud_id}
        status, body = _call(cloud, "POST", "/api/sync/pull", {}, token=owner)
        pulled = body["bills"]
        run.check(
            "拉取账单：只还我导入的账单（Editor 导入的不在内），Id 与云端入库的一致",
            status == 200 and len(pulled) > 0 and {p["id"] for p in pulled} == owner_bill_ids and len(owner_bill_ids) < len(imported),
            f"{status} 拉到 {len(pulled)} 条，我导入的 {len(owner_bill_ids)} 条，云端共 {len(imported)} 条",
        )
        shaped = all(
            p["type"] == (1 if imported[p["id"]]["isIncome"] else 0)
            and p["categoryId"] == (income_category if imported[p["id"]]["isIncome"] else expense_category)
            and p["paymentChannelId"] == channel
            and p["transactionTime"] == imported[p["id"]]["transactionTimeUtc"] and p["transactionTime"].endswith("Z")
            and p["ledgerId"] == imported[p["id"]]["ledgerId"] and p["note"] == imported[p["id"]]["description"]
            and p["amount"] == imported[p["id"]]["amount"] and p["userId"] == owner_cloud_id
            and p["source"] == 1 and p["syncStatus"] == 1 and p["isDeleted"] is False
            for p in pulled
        )
        run.check("拉取账单：BillSyncDto 形态（枚举是数字、时间是带 Z 的 UTC、分类 / 渠道取目录里的小写 Id、来源=导入、已同步）", shaped, str(pulled[:1]))
        run.check(
            "拉取账单：收入与支出各用自己的分类",
            {p["categoryId"] for p in pulled if p["type"] == 1} == {income_category} and {p["categoryId"] for p in pulled if p["type"] == 0} == {expense_category},
        )
        status, body = _call(cloud, "POST", "/api/sync/pull", {}, token=stranger)
        run.check("没导入过账单的人拉到空列表", status == 200 and body == {"bills": []}, str(body))
    finally:
        cloud.stop()


def test_run_bookkeeping(run: TestRun) -> None:
    """TestRun 自身的记账：跳过的检查不计入通过 / 失败，但收尾时要露出来。"""
    import contextlib
    import io

    inner = TestRun("inner")
    with contextlib.redirect_stdout(io.StringIO()):
        inner.check("通过的检查", True)
        inner.check("失败的检查", False, "原因")
        inner.skip("环境不具备条件的检查", "没有 CDN")
    out = io.StringIO()
    with contextlib.redirect_stdout(out):
        code = inner.finish()
    text = out.getvalue()
    run.check("TestRun：跳过的检查不进结果表（总数、通过数都不含它）", len(inner.results) == 2 and "=== inner: 1/2 通过 ===" in text, text)
    run.check("TestRun：跳过的检查在收尾时列出（免得全绿被当成全都验过）", "另有 1 项被跳过" in text and "环境不具备条件的检查" in text and inner.skipped == [("环境不具备条件的检查", "没有 CDN")], text)
    run.check("TestRun：有失败项时退出码 1，跳过不影响退出码", code == 1)

    clean = TestRun("clean")
    with contextlib.redirect_stdout(io.StringIO()):
        clean.check("通过", True)
        clean.skip("跳过", "原因")
    with contextlib.redirect_stdout(io.StringIO()):
        clean_code = clean.finish()
    run.check("TestRun：只有跳过、没有失败时退出码仍是 0", clean_code == 0)

    quiet = TestRun("quiet")
    out = io.StringIO()
    with contextlib.redirect_stdout(out):
        quiet.check("通过", True)
        quiet.finish()
    run.check("TestRun：没有跳过时不多打印「跳过」那一行", "跳过" not in out.getvalue(), out.getvalue())


def main() -> int:
    run = TestRun("base-selftest")
    test_run_bookkeeping(run)
    test_viewport_and_gestures(run)
    test_degradation_emulation(run)
    test_error_collector(run)
    test_local_db(run)
    test_fake_cloud(run)
    test_fake_cloud_import(run)
    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
