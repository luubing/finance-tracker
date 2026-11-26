# -*- coding: utf-8 -*-
"""
E2E 测试：语音录入功能集成验证（记一笔 Sheet 标题栏右侧的麦克风，手机视口）。

前置条件：
  1. pip install -r tests/e2e/requirements.txt && python -m playwright install chromium
  2. FinanceTracker.Web.Server 已用 http 启动配置运行（Development 环境，监听 http://localhost:5999）：
     powershell tests/e2e/server.ps1 -Action start

验证点：
  - 旧地址 /add-bill 深链进入后升起记一笔 Sheet（分类 / 支付渠道渲染，既有回归点）
  - 麦克风按钮渲染在标题栏右侧，读屏名称写明「语音记账」
  - billVoiceInput.js 模块可加载，无头浏览器（无 Web Speech API）下静默降级：置灰 + 点按给出一句说明，手动填写不受影响
  - 无页面 JS 错误（语音能力检测在 Sheet 升起时执行，不崩溃）

注意：真实语音识别（麦克风）无法在无头浏览器中自动化，解析逻辑由单元测试覆盖
（tests/unit/FinanceTracker.Tests/Services/BillVoiceParserTests.cs）；本脚本只验证「入口」与「不支持环境的降级」。
语音覆盖层（实时转写、高亮与芯片、说完了 / 取消 / 失败、预填与保存、降级）由 voice_overlay_test.py 注入假的 SpeechRecognition 覆盖；
按住说话 / 真机手感见 ticket 17 的人工验收清单。
"""
import sys

from e2e_base import TestRun, dump_html, login, open_app, require_server, session, shot, wait_attr


def toast_shown(page, text: str) -> bool:
    try:
        page.wait_for_function(
            "(t) => [...document.querySelectorAll('.m-enqueued-snackbars .m-snack__wrapper')].some((e) => e.textContent.includes(t))",
            arg=text,
            timeout=8000,
        )
        return True
    except Exception:  # noqa: BLE001 - 超时就是没出现
        return False


def main() -> int:
    require_server()
    run = TestRun("voice-addbill")

    with session() as s:
        page = s.page
        login(page)

        # ---- 深链进入记一笔 Sheet ----
        open_app(page, "/add-bill")
        sheet = page.get_by_test_id("addbill-sheet")
        sheet.wait_for(state="visible")
        wait_attr(page, '[data-testid="addbill-sheet"]', "data-ready", "true")

        body = sheet.inner_text()
        run.check("记账 Sheet 加载（标题「记一笔」）", "记一笔" in body)
        run.check("分类渲染（餐饮美食）", "餐饮美食" in body)
        run.check("支付渠道渲染", "支付渠道" in body)

        # ---- 语音入口 ----
        mic = page.get_by_test_id("addbill-voice")
        run.check("麦克风按钮渲染", mic.count() == 1 and mic.is_visible())
        label = mic.get_attribute("aria-label") or ""
        disabled = mic.get_attribute("aria-disabled") == "true"
        # 两种合法状态：环境支持时可用；不支持时置灰、读屏名称注明（均不应报错）
        run.check("语音就绪或降级（置灰 + 说明）", "语音记账" in label and ("不支持" in label) == disabled, label)
        if disabled:
            mic.click()
            run.check("不支持语音识别的环境：点按只给一句说明，键盘仍可用", toast_shown(page, "当前环境不支持语音识别") and page.get_by_test_id("keypad").is_visible())

        shot(page, "voice-addbill")
        dump_html(page, "voice_addbill_add-bill")
        run.check_clean(s.errors)

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
