# -*- coding: utf-8 -*-
"""
本地 SQLite 数据库检查工具（只读，操作副本，不影响运行中的应用）。

用途：
  - 保存账单失败（FOREIGN KEY constraint failed）时，快速核对 Users/Categories/
    PaymentChannels/Bills 的实际数据，定位是哪个外键值无效。
  - 验证 E2E 测试保存的账单是否落库。

用法：
  python db_check.py                          # 检查 Web.Server 默认库 (%LOCALAPPDATA%\\finance_tracker.db)
  python db_check.py <db文件路径>              # 检查指定库（如 MAUI 设备导出的 db）
"""
import sys
import os
import sqlite3
import shutil

# Windows 控制台默认 GBK，统一切换 UTF-8
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


def main() -> int:
    default_db = os.path.expandvars(r"%LOCALAPPDATA%\finance_tracker.db")
    src = sys.argv[1] if len(sys.argv) > 1 else default_db

    if not os.path.exists(src):
        print(f"数据库不存在: {src}")
        return 1

    # 复制 db + WAL + SHM 到临时目录，避免锁定运行中的应用，也保证读到 WAL 中的最新数据
    tmp = os.path.join(os.environ["TEMP"], "ft_dbcheck.db")
    for ext in ("", "-wal", "-shm"):
        if os.path.exists(src + ext):
            shutil.copy(src + ext, tmp + ext)

    con = sqlite3.connect(tmp)
    cur = con.cursor()

    print("=== Users ===")
    for r in cur.execute('SELECT Id, PhoneNumber, CreatedAt FROM "Users"'):
        print(r)

    print("=== Categories ===")
    cnt = cur.execute('SELECT COUNT(*) FROM "Categories"').fetchone()[0]
    print(f"count = {cnt}")

    print("=== PaymentChannels ===")
    cnt = cur.execute('SELECT COUNT(*) FROM "PaymentChannels"').fetchone()[0]
    print(f"count = {cnt}")

    print("=== Bills ===")
    n = cur.execute('SELECT COUNT(*) FROM "Bills"').fetchone()[0]
    print(f"count = {n}")
    for r in cur.execute(
        'SELECT Id, Amount, Type, Note, substr(UserId,1,8), substr(CategoryId,1,8), '
        'substr(PaymentChannelId,1,8), TransactionTime FROM "Bills"'
    ):
        print("BILL:", r)

    # 外键核对：Bills 引用的父记录是否都存在（诊断 FOREIGN KEY constraint failed）
    print("=== FK 有效性检查（值为空串表示该父记录存在）===")
    for r in cur.execute(
        'SELECT substr(b.Id,1,8),'
        ' CASE WHEN u.Id IS NULL THEN "MISSING-USER" ELSE "" END,'
        ' CASE WHEN c.Id IS NULL THEN "MISSING-CATEGORY" ELSE "" END,'
        ' CASE WHEN p.Id IS NULL THEN "MISSING-CHANNEL" ELSE "" END'
        ' FROM "Bills" b'
        ' LEFT JOIN "Users" u ON b.UserId = u.Id'
        ' LEFT JOIN "Categories" c ON b.CategoryId = c.Id'
        ' LEFT JOIN "PaymentChannels" p ON b.PaymentChannelId = p.Id'
    ):
        print("BILL FK:", r)

    con.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
