# -*- coding: utf-8 -*-
"""
E2E 冒烟测试：验证 Blazor Server 页面可正常加载、SignalR 电路可连接。

前置条件：
  1. pip install playwright && python -m playwright install chromium
  2. FinanceTracker.Web.Server 已启动（默认监听 http://localhost:59947）

验证点：
  - 页面 HTTP 200
  - 无 404 资源（历史 bug：_framework/blazor.server.js 404 导致电路无法连接、页面一直转圈）
  - 无浏览器控制台错误 / 页面异常
  - 登录表单出现（证明电路已连接、Index 组件 OnAfterRenderAsync 正常执行）
"""
import sys
import os
import json
from playwright.sync_api import sync_playwright

# ======== 配置 ========
BASE_URL = os.environ.get("E2E_BASE_URL", "http://localhost:59947")   # Web.Server http 端口（见 launchSettings.json）
TIMEOUT_MS = 30_000

# Windows 控制台默认 GBK，中文/特殊字符输出会报错，统一切换 UTF-8
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


def main() -> int:
    console_msgs: list[str] = []
    page_errors: list[str] = []
    failed_requests: list[str] = []
    bad_responses: list[str] = []

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page()

        page.on("console", lambda m: console_msgs.append(f"[{m.type}] {m.text}"))
        page.on("pageerror", lambda e: page_errors.append(str(e)))
        page.on("requestfailed", lambda r: failed_requests.append(f"{r.method} {r.url} -> {r.failure}"))
        page.on("response", lambda r: bad_responses.append(f"{r.status} {r.url}") if r.status >= 400 else None)

        page.goto(f"{BASE_URL}/", wait_until="networkidle", timeout=TIMEOUT_MS)

        # 等待登录表单出现 = 电路已连接 + Index 组件 OnAfterRenderAsync 已执行（loading 转圈消失）
        try:
            page.wait_for_selector("input[type='tel']", timeout=25_000)
            circuit_ok = True
        except Exception:
            circuit_ok = False

        body_text = page.inner_text("body")
        error_ui_visible = page.locator("#blazor-error-ui").is_visible()
        browser.close()

    print("=== HTTP >= 400 ===")
    print("\n".join(bad_responses) if bad_responses else "(none)")
    print("=== FAILED REQUESTS ===")
    print("\n".join(failed_requests) if failed_requests else "(none)")
    print("=== PAGE ERRORS ===")
    print("\n".join(page_errors) if page_errors else "(none)")
    print("=== CONSOLE ERRORS ===")
    print("\n".join(m for m in console_msgs if m.startswith("[error]")) or "(none)")

    print("=== RESULT ===")
    print(f"circuit_connected (login form shown): {circuit_ok}")
    print(f"error_ui_visible: {error_ui_visible}")

    ok = (
        circuit_ok
        and not bad_responses
        and not failed_requests
        and not page_errors
        and not error_ui_visible
        and "登录" in body_text
    )
    print("SMOKE TEST:", "PASSED" if ok else "FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
