# -*- coding: utf-8 -*-
"""
E2E 冒烟测试：验证 Blazor Server 页面可正常加载、SignalR 电路可连接（手机视口）。

前置条件：
  1. pip install -r tests/e2e/requirements.txt && python -m playwright install chromium
  2. FinanceTracker.Web.Server 已用 http 启动配置运行（监听 http://localhost:5999）：
     powershell tests/e2e/server.ps1 -Action start

验证点：
  - 页面 HTTP 200
  - 无 404 资源（历史 bug：_framework/blazor.server.js 404 导致电路无法连接、页面一直转圈）
  - 无浏览器控制台错误 / 页面异常
  - 登录表单出现（证明电路已连接、Index 组件 OnAfterRenderAsync 正常执行）
  - 手机视口（390 宽）下页面无横向溢出
"""
import sys

from e2e_base import BASE_URL, PHONE_VIEWPORT, TIMEOUT_MS, TestRun, require_server, session, shot, wait_circuit_ready


def main() -> int:
    require_server()
    run = TestRun("smoke")

    with session() as s:
        page = s.page
        response = page.goto(f"{BASE_URL}/", wait_until="networkidle", timeout=TIMEOUT_MS)
        run.check("页面 HTTP 200", response is not None and response.status == 200)

        # 登录表单出现 = 电路已连接 + Index 组件 OnAfterRenderAsync 已执行（loading 转圈消失）
        try:
            page.wait_for_selector("input[type='tel']", timeout=25_000)
            circuit_ok = True
        except Exception:  # noqa: BLE001
            circuit_ok = False
        run.check("登录表单出现（电路已连接）", circuit_ok)

        wait_circuit_ready(page)
        run.check("错误提示条 #blazor-error-ui 未显示", not page.locator("#blazor-error-ui").is_visible())
        run.check("页面包含登录入口", "登录" in page.inner_text("body"))

        viewport = page.viewport_size
        run.check("使用手机视口", viewport == PHONE_VIEWPORT, str(viewport))
        overflow = page.evaluate("() => document.documentElement.scrollWidth - document.documentElement.clientWidth")
        run.check("手机视口下无横向溢出", overflow <= 0, f"overflow={overflow}px")

        shot(page, "smoke-login")
        run.check_clean(s.errors)

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
