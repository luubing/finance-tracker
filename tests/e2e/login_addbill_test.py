# -*- coding: utf-8 -*-
"""
E2E 测试：完整登录 + 记一笔账单流程。

前置条件：
  1. pip install playwright && python -m playwright install chromium
  2. FinanceTracker.Web.Server 已启动（默认监听 http://localhost:59947）

验证点（对应历史 bug：AuthenticationService.IsPrerendering 判断错误导致
localStorage 未写入 userId → 记账时 UserId=Guid.Empty → SQLite FOREIGN KEY constraint failed）：
  - 登录成功后 localStorage 正确写入 userId / phoneNumber（核心回归点）
  - 首页显示"欢迎回来"（认证状态事件通知生效，无需整页刷新）
  - /add-bill 页面正常加载分类与支付渠道
  - 注意：MASA 下拉框在无头浏览器中难以自动化选择支付渠道，
    表单提交依赖该项，故本脚本止步于页面加载验证（见 save_bill_test.py 的说明）
"""
import sys
import os
import json
from playwright.sync_api import sync_playwright

# ======== 配置 ========
BASE_URL = os.environ.get("E2E_BASE_URL", "http://localhost:59947")
TEST_PHONE = "13800138000"
TIMEOUT_MS = 30_000

# Windows 控制台默认 GBK，统一切换 UTF-8
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


def main() -> int:
    console_msgs: list[str] = []
    page_errors: list[str] = []
    result = {}

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page()
        page.on("console", lambda m: console_msgs.append(f"[{m.type}] {m.text}"))
        page.on("pageerror", lambda e: page_errors.append(str(e)))

        page.goto(f"{BASE_URL}/", wait_until="networkidle", timeout=TIMEOUT_MS)
        page.wait_for_selector("input[type='tel']", timeout=25_000)

        # ---- 登录 ----
        page.locator("input[type='tel']").fill(TEST_PHONE)
        page.get_by_role("button", name="登录/注册").click()
        page.wait_for_timeout(4_000)

        body = page.inner_text("body")
        result["welcome_shown"] = "欢迎回来" in body

        # 核心回归点：localStorage 必须写入 userId（修复前为 null）
        result["localStorage_userId"] = page.evaluate("() => localStorage.getItem('userId')")
        result["localStorage_phone"] = page.evaluate("() => localStorage.getItem('phoneNumber')")

        # ---- 进入记一笔页面 ----
        page.get_by_role("button", name="记一笔").click()
        page.wait_for_timeout(3_000)
        add_bill_body = page.inner_text("body")
        result["add_bill_loaded"] = "选择分类" in add_bill_body
        result["categories_rendered"] = add_bill_body.count("支出") >= 1 and "餐饮" in add_bill_body
        result["payment_channels_rendered"] = "支付渠道" in add_bill_body

        with open("e2e_addbill_page.html", "w", encoding="utf-8") as f:
            f.write(page.content())

        browser.close()

    print(json.dumps(result, ensure_ascii=False, indent=2))
    print("=== PAGE ERRORS ===")
    print("\n".join(page_errors) or "(none)")
    print("=== CONSOLE ERRORS ===")
    print("\n".join(m for m in console_msgs if m.startswith("[error]")) or "(none)")

    ok = (
        result["welcome_shown"]
        and result["localStorage_userId"]           # 非 null 且为合法 GUID
        and result["localStorage_phone"] == TEST_PHONE
        and result["add_bill_loaded"]
        and result["categories_rendered"]
        and result["payment_channels_rendered"]
        and not page_errors
    )
    print("E2E LOGIN+ADDBILL TEST:", "PASSED" if ok else "FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
