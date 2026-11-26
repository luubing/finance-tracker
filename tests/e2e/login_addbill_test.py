# -*- coding: utf-8 -*-
"""
E2E 测试：完整登录 + 升起记一笔 Sheet（手机视口，每次用全新手机号注册）。

前置条件：
  1. pip install -r tests/e2e/requirements.txt && python -m playwright install chromium
  2. FinanceTracker.Web.Server 已用 http 启动配置运行（监听 http://localhost:5999）：
     powershell tests/e2e/server.ps1 -Action start

验证点（对应历史 bug：AuthenticationService.IsPrerendering 判断错误导致
localStorage 未写入 userId → 记账时 UserId=Guid.Empty → SQLite FOREIGN KEY constraint failed）：
  - 登录成功后 localStorage 正确写入 userId / phoneNumber（核心回归点）
  - 登录后首页显示"本月支出"（Hero 里的主指标；认证状态事件通知生效，无需整页刷新）
  - 点 Tab 栏的「记一笔」升起记一笔 Sheet：分类与支付渠道正常加载
  - 完整的"保存账单"（键盘输入金额、选分类 / 账本 / 支付渠道、保存、落库核对）见 addbill_sheet_test.py；
    保存逻辑本身由 tests/unit 里的 BillService 测试覆盖（见 tests/e2e/README.md）
"""
import re
import sys

from e2e_base import (
    TestRun,
    dump_html,
    login,
    require_server,
    session,
    shot,
    wait_attr,
    wait_text,
)

GUID_RE = re.compile(r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")


def main() -> int:
    require_server()
    run = TestRun("login-addbill")

    with session() as s:
        page = s.page

        # ---- 登录 ----
        info = login(page)
        user_id = page.evaluate("() => localStorage.getItem('userId')")
        phone = page.evaluate("() => localStorage.getItem('phoneNumber')")

        # 核心回归点：localStorage 必须写入 userId（修复前为 null）
        run.check("localStorage 写入合法 userId", bool(user_id and GUID_RE.match(user_id)), str(user_id))
        run.check("localStorage 写入手机号", phone == info.phone, f"{phone} != {info.phone}")

        wait_text(page, "本月支出")
        run.check("登录后首页显示'本月支出'", "本月支出" in page.inner_text("body"))
        shot(page, "login-addbill-home")

        # ---- 升起记一笔 Sheet ----
        # 记一笔的入口是 Tab 栏上的圆钮（aria-label「记一笔」）；.first 兜底，避免页面里再出现同名按钮时触发严格模式报错
        page.get_by_role("button", name="记一笔").first.click()
        sheet = page.get_by_test_id("addbill-sheet")
        sheet.wait_for(state="visible")
        wait_attr(page, '[data-testid="addbill-sheet"]', "data-ready", "true")

        body = sheet.inner_text()
        run.check("记账 Sheet 升起（标题「记一笔」），地址不变", "记一笔" in body and page.evaluate("() => location.pathname") == "/")
        run.check("分类渲染（支出 / 收入分段 + 餐饮美食）", "支出" in body and "收入" in body and "餐饮美食" in body)
        run.check("账本 / 支付渠道行渲染", "账本" in body and "支付渠道" in body)
        shot(page, "login-addbill-add-bill")
        dump_html(page, "login_addbill_add-bill")

        run.check_clean(s.errors)

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
