# -*- coding: utf-8 -*-
"""
E2E 测试：语音录入功能集成验证（记账页语音栏）。

前置条件：
  1. pip install playwright && python -m playwright install chromium
  2. FinanceTracker.Web.Server 已启动（Development 环境，含开发态静态资源）

验证点：
  - /add-bill 页面正常加载（分类/支付渠道渲染，既有回归点）
  - 语音录入栏正常渲染（麦克风按钮 + 引导文案/降级提示）
  - billVoiceInput.js 模块可加载，无头浏览器（无 Web Speech API）下静默降级不报错
  - 无页面 JS 错误（OnInitializedAsync 中语音能力检测不崩溃）

注意：真实语音识别（麦克风）无法在无头浏览器中自动化，解析逻辑由单元测试覆盖
（tests/unit/FinanceTracker.Tests/Services/BillVoiceParserTests.cs）。
"""
import sys
import os
import json
from playwright.sync_api import sync_playwright

# ======== 配置 ========
BASE_URL = os.environ.get("E2E_BASE_URL", "http://localhost:5999")
TEST_PHONE = "13800138000"
TIMEOUT_MS = 30_000

# Windows 控制台默认 GBK，统一切换 UTF-8
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")


def main() -> int:
    console_msgs: list[str] = []
    page_errors: list[str] = []
    result: dict = {}

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

        # ---- 直接进入记一笔页面 ----
        page.goto(f"{BASE_URL}/add-bill", wait_until="networkidle", timeout=TIMEOUT_MS)
        page.wait_for_timeout(3_000)

        body = page.inner_text("body")
        result["add_bill_loaded"] = "选择分类" in body
        result["categories_rendered"] = "餐饮" in body
        result["payment_channels_rendered"] = "支付渠道" in body

        # ---- 语音录入栏 ----
        result["voice_bar_rendered"] = page.locator(".voice-bar").count() >= 1
        result["mic_button_present"] = page.locator(".voice-bar button").count() >= 1
        # 两种合法状态：环境支持时显示引导文案；不支持时显示降级提示（均不应报错）
        result["voice_ready_or_degraded"] = "说出账单" in body or "当前环境不支持语音识别" in body

        with open("e2e_voice_addbill_page.html", "w", encoding="utf-8") as f:
            f.write(page.content())

        browser.close()

    print(json.dumps(result, ensure_ascii=False, indent=2))
    print("=== PAGE ERRORS ===")
    print("\n".join(page_errors) or "(none)")
    print("=== CONSOLE ERRORS ===")
    print("\n".join(m for m in console_msgs if m.startswith("[error]")) or "(none)")

    ok = (
        result["add_bill_loaded"]
        and result["categories_rendered"]
        and result["payment_channels_rendered"]
        and result["voice_bar_rendered"]
        and result["mic_button_present"]
        and result["voice_ready_or_degraded"]
        and not page_errors
    )
    print("E2E VOICE ADDBILL TEST:", "PASSED" if ok else "FAILED")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
