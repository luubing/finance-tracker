# -*- coding: utf-8 -*-
"""
E2E 测试：导入账单页（iOS UI 重构 · 20 号 ticket）——按步骤的导入流程：来源 → 文件 → 解析预览（显式按钮）→ 账本（必填）→ 导入 → 结果与同步。
底部固定一个主按钮，按步骤换动作：「解析预览」（没选文件置灰）→「导入 N 条账单」→「继续导入」；选好文件 / 换来源后都停在「已选文件」，不自动解析。

导入写入的是云端数据库，解析预览也走云端接口。本脚本在本机起一个假云端（fake_cloud.py，端口 E2E_CLOUD_PORT，默认 5271；
server.ps1 启动 Web.Server 时已把 ApiSettings__BaseUrl 指向它）：解析规则、校验顺序、状态码与消息对照 ImportController / CsvParserService，
所以「选文件 → 预览 → 选账本 → 导入 → 自动同步 → 账单列表看得到」可以端到端验证；用例同时核对页面表现、云端收到的请求与本地库里的账单。
只覆盖网页层——真机上的系统文件选择器、真实的微信 / 支付宝导出文件见 ticket 20 的人工验收清单。

  A. 外壳与初始状态：从「我的」进入（二级页面、Tab 栏隐藏）、来源分段、折叠的导出说明、文件行（透明文件输入盖满整行、点整行唤起文件选择器）、
     置灰的「解析预览」主按钮固定在底部
  B. 换来源：说明 / 文案跟着换；解析 / 导入进行中不响应换来源
  C. 选文件 → 已选文件（不自动解析：云端没收到预览请求、「解析预览」点亮）→ 点「解析预览」→ 解析中的状态（主按钮忙「解析中」）、
     预览请求只带 CSV 原文、汇总 / 笔数与时间范围 / 跳过说明 / 预览行（时间原样，不换算时区）、文件行显示文件名与大小
  D. 账本必填：没有账本时账本行提示「选择账本」、主按钮置灰且点不动（云端没收到导入请求）、菜单里「新建账本…」建好后选中、菜单对勾
  E. 导入（成功）：导入中的状态（按钮忙、控件禁用）、导入前先同步账本（刚在本机建的账本登记到云端）、请求带 CSV / 本机时区偏移 / 账本、
     结果页（成功 / 总计 / 账本）、Toast、「同步到本机」一行、本地库里的账单（金额 / 类型 / 数据来源 / 账本 / UTC 时间）、账单 Tab 立刻看得到、
     「继续导入」回到第 1 步
  F. 有两个账本：选另一个账本、支付宝来源导入（预览没有「跳过」说明）、请求发往支付宝接口
  G. 换来源（已读入的文件保留、预览作废、回到「已选文件」、不自动重新解析；再点「解析预览」按新来源解析：选错来源 → 失败 → 换回来不用重选文件）、
     解析失败（不是账单 / 全是中性交易 / 文件太大 / 云端不可用 / 云端 5xx）与「重试」（底部「解析预览」与它等价）、GB18030 / UTF-16 编码的文件
  H. 导入失败：云端拒绝（403）/ 5xx（提示「云端可能已收下」）/ 云端不可用——预览与账本选择都保留、可再试；部分失败（失败详情）+ 同步失败；
     全部失败；导入前同步账本失败不拦截；账本在云端不存在（400）的提示
  I. 预览行数上限：130 条只显示前 100 条，汇总与导入针对全部
  J. 窄屏（320）：已选文件 / 预览 / 失败 / 结果四个状态都不溢出、不重叠；每屏玻璃层 ≤ 3

可观察属性（已登记到 README）：import-page、import-content（data-stage = idle｜reading｜ready｜parsing｜failed｜preview｜importing｜done / data-source）、import-source（data-value）/ segment、
import-source-step、import-guide-group、import-guide-toggle（aria-expanded）/ import-guide / import-guide-path、
import-file-group、import-file-row（data-has-file / data-file-name）/ import-file、import-parsing（data-phase = reading｜parsing）、import-error / import-error-text / import-retry、
import-preview（data-count / data-skipped）/ import-summary（import-expense / import-income / import-balance）/ import-caption / import-skipped / import-rows /
import-row（data-income）/ import-row-amount / import-row-time / import-more、import-ledger-group、import-ledger-row（data-value / data-missing）/ import-ledger-menu、
import-failure / import-failure-text、import-foot、import-submit（data-action = parse｜import｜again）、
import-result（data-total / data-success / data-failed）/ import-result-head / import-result-group / import-result-ledger / import-result-total / import-result-success / import-result-failed、
import-errors / import-error-item、import-sync-group / import-sync（data-state）。
"""
import sys
import time
from datetime import datetime, timedelta
from decimal import Decimal

from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

from e2e_base import (
    GLASS_LAYERS_JS,
    BILL_TYPE_EXPENSE,
    BILL_TYPE_INCOME,
    PHONE_VIEWPORT,
    SOURCE_IMPORT,
    LocalDb,
    TestRun,
    login,
    open_app,
    open_tab,
    require_server,
    session,
    shot,
    unique_name,
    wait_animations_settled,
    wait_attr,
    wait_secondary_pages_closed,
    wait_tab_bar,
    wait_url,
)
from fake_cloud import CloudPortBusy, FakeCloud

SCREEN_W = PHONE_VIEWPORT["width"]
SCREEN_H = PHONE_VIEWPORT["height"]
CONTENT = '[data-testid="import-content"]'
PREVIEW_PATH = r"/api/import/(wechat|alipay)/preview$"
CLOUD: "FakeCloud | None" = None  # main() 起好假云端后赋值，供 slow_next_preview 使用

# ---------------------------------------------------------------------------------------------- 造数据：账单文件

WECHAT_HEADER = (
    "微信支付账单明细\r\n微信昵称：[E2E]\r\n起始时间：[2024-05-01 00:00:00] 终止时间：[2024-05-31 23:59:59]\r\n"
    "----------------------微信支付账单明细列表--------------------\r\n"
    "交易时间,交易类型,交易对方,商品,收/支,金额(元),支付方式,当前状态,交易单号,商户单号,备注\r\n"
)
#: 3 条有效（支出 38.50 / 收入 1,200.00 / 支出 12.00）
WECHAT_VALID_ROWS = (
    '2024-05-01 12:30:00,商户消费,美团外卖,午餐外卖,支出,¥38.50,零钱,支付成功,4200001,M1,"/"\r\n'
    '2024-05-02 09:05:00,转账,小王,转账备注,收入,"¥1,200.00",中国银行储蓄卡(1234),已收钱,4200002,/,/\r\n'
    '2024-05-03 18:20:00,扫二维码付款,便利店,/,支出,¥12.00,招商银行信用卡(5678),支付成功,4200003,M3,/\r\n'
)
#: 2 条被云端跳过（中性交易 / 已全额退款）
WECHAT_SKIPPED_ROWS = (
    "2024-05-04 10:00:00,零钱提现,/,/,/,¥100.00,零钱,提现成功,4200004,/,/\r\n"
    "2024-05-05 11:00:00,商户消费,某商户,退款商品,支出,¥20.00,零钱,已全额退款,4200005,M5,/\r\n"
)
WECHAT_TEXT = WECHAT_HEADER + WECHAT_VALID_ROWS + WECHAT_SKIPPED_ROWS
WECHAT_BYTES = b"\xef\xbb\xbf" + WECHAT_TEXT.encode("utf-8")  # 微信导出的是带 BOM 的 UTF-8
WECHAT_NEUTRAL_ONLY_TEXT = WECHAT_HEADER + WECHAT_SKIPPED_ROWS

#: 支付宝官方导出文件的真实列（交易时间,交易分类,交易对方,对方账号,商品说明,收/支,金额,收/付款方式,交易状态,交易订单号,商家订单号,备注）：
#: 解析器按表头名称定位各列；订单号字段末尾带制表符、行尾有多余的逗号，与真实文件一致。
#: 解析发生在云端（假云端镜像了 CsvParserService），这里验证「页面如何对待云端的响应」；真实导出文件仍要人工验收
ALIPAY_TEXT = (
    "支付宝交易记录明细查询\r\n账号:[e2e@example.com]\r\n"
    "交易时间,交易分类,交易对方,对方账号,商品说明,收/支,金额,收/付款方式,交易状态,交易订单号,商家订单号,备注,\r\n"
    "2024-05-06 08:15:00,餐饮美食,某咖啡店,cof***@e2e.com,拿铁,支出,28.00,余额宝,交易成功,A001\t,M001\t,,\r\n"
    "2024-05-07 20:00:00,转账红包,朋友,fri***@e2e.com,红包,收入,88.00,余额,交易成功,A002\t,,,\r\n"
)
ALIPAY_BYTES = ALIPAY_TEXT.encode("gb18030")

NOT_A_BILL_TEXT = "姓名,年龄\r\n张三,18\r\n"


def big_wechat_text(rows: int) -> str:
    """rows 条有效支出（商品 1…rows，金额 1…rows 元），日期从 2024-06-01 起每 5 条换一天。"""
    lines = [WECHAT_HEADER]
    for i in range(1, rows + 1):
        day = (i - 1) // 5 + 1
        lines.append(f"2024-06-{day:02d} {8 + (i - 1) % 5:02d}:00:00,商户消费,商户{i},商品{i},支出,¥{i}.00,零钱,支付成功,ID{i},M{i},/\r\n")
    return "".join(lines)


# ---------------------------------------------------------------------------------------------- 页面状态

STATE_JS = r"""() => {
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  const all = (id) => [...document.querySelectorAll(`[data-testid="${id}"]`)];
  const text = (node) => (node ? node.textContent.replace(/\s+/g, ' ').trim() : null);
  const rect = (node) => {
    if (!node) return null;
    const r = node.getBoundingClientRect();
    return { l: r.left, r: r.right, t: r.top, b: r.bottom, w: r.width, h: r.height };
  };
  const cell = (node) => (node ? {
    title: text(node.querySelector('.ft-cell__title')),
    subtitle: text(node.querySelector('.ft-cell__subtitle')),
    value: text(node.querySelector('.ft-cell__value')),
  } : null);
  const group = (id) => {
    const g = q(id);
    return g ? { header: text(g.querySelector('.ft-group__header')), footer: text(g.querySelector('.ft-group__footer')) } : null;
  };
  const amountOf = (id) => { const n = q(id); return n ? n.getAttribute('data-amount') : null; };
  const content = q('import-content');
  if (!content) return null;

  const source = q('import-source');
  const guide = q('import-guide-toggle');
  const fileRow = q('import-file-row');
  const input = q('import-file');
  const ledgerRow = q('import-ledger-row');
  const submit = q('import-submit');
  const foot = q('import-foot');
  const preview = q('import-preview');
  const result = q('import-result');
  const sync = q('import-sync');
  const resultHead = q('import-result-head');

  return {
    stage: content.getAttribute('data-stage'),
    source: content.getAttribute('data-source'),
    busy: content.getAttribute('aria-busy'),
    overflowX: document.documentElement.scrollWidth - document.documentElement.clientWidth,

    sourceValue: source && source.getAttribute('data-value'),
    segments: source ? [...source.querySelectorAll('[data-testid="segment"]')].map((b) => ({
      value: b.getAttribute('data-value'), label: text(b), pressed: b.getAttribute('aria-pressed'), h: b.getBoundingClientRect().height,
    })) : [],
    sourceStep: group('import-source-step'),

    guideTitle: guide && text(guide.querySelector('.ft-cell__title')),
    guideOpen: guide && guide.getAttribute('aria-expanded'),
    guideControls: guide && guide.getAttribute('aria-controls'),
    guidePath: text(q('import-guide-path')),
    guideText: text(q('import-guide')),

    fileGroup: group('import-file-group'),
    file: cell(fileRow),
    hasFile: fileRow && fileRow.getAttribute('data-has-file'),
    fileName: fileRow && fileRow.getAttribute('data-file-name'),
    fileRowBox: rect(fileRow),
    inputDisabled: input ? input.disabled : null,
    inputAccept: input && input.getAttribute('accept'),
    inputAria: input && input.getAttribute('aria-label'),
    inputBox: rect(input),
    parsing: text(q('import-parsing')),
    parsingRole: q('import-parsing') && q('import-parsing').getAttribute('role'),
    parsingPhase: q('import-parsing') && q('import-parsing').getAttribute('data-phase'),
    error: text(q('import-error-text')),
    errorRole: q('import-error') && q('import-error').getAttribute('role'),
    errorBox: rect(q('import-error')),
    retry: !!q('import-retry'),
    retryBox: rect(q('import-retry')),

    hasPreview: !!preview,
    previewCount: preview && preview.getAttribute('data-count'),
    previewSkipped: preview && preview.getAttribute('data-skipped'),
    previewHeader: preview ? text(preview.querySelector('.ft-group__header')) : null,
    expense: amountOf('import-expense'),
    income: amountOf('import-income'),
    balance: amountOf('import-balance'),
    caption: text(q('import-caption')),
    skipped: text(q('import-skipped')),
    more: text(q('import-more')),
    rows: all('import-row').map((row) => ({
      title: text(row.querySelector('.ft-cell__title')),
      subtitle: text(row.querySelector('.ft-cell__subtitle')),
      amount: text(row.querySelector('[data-testid="import-row-amount"]')),
      time: text(row.querySelector('[data-testid="import-row-time"]')),
      income: row.getAttribute('data-income'),
      box: rect(row),
    })),

    ledgerGroup: group('import-ledger-group'),
    ledger: cell(ledgerRow),
    ledgerMissing: ledgerRow && ledgerRow.getAttribute('data-missing'),
    ledgerId: ledgerRow && ledgerRow.getAttribute('data-value'),
    ledgerDisabled: ledgerRow ? ledgerRow.disabled : null,
    ledgerExpanded: ledgerRow && ledgerRow.getAttribute('aria-expanded'),
    ledgerPopup: ledgerRow && ledgerRow.getAttribute('aria-haspopup'),
    failure: text(q('import-failure-text')),
    failureRole: q('import-failure') && q('import-failure').getAttribute('role'),

    submit: submit ? { text: text(submit), disabled: submit.disabled, busy: submit.getAttribute('aria-busy'), action: submit.getAttribute('data-action'), box: rect(submit) } : null,
    foot: foot ? { position: getComputedStyle(foot).position, box: rect(foot) } : null,

    result: result ? {
      total: result.getAttribute('data-total'),
      success: result.getAttribute('data-success'),
      failed: result.getAttribute('data-failed'),
      title: text(resultHead && resultHead.querySelector('.ft-empty__title')),
      summary: text(resultHead && resultHead.querySelector('.ft-empty__text')),
      ledger: cell(q('import-result-ledger')),
      totalRow: cell(q('import-result-total')),
      successRow: cell(q('import-result-success')),
      failedRow: cell(q('import-result-failed')),
      errorsGroup: group('import-errors'),
      errors: all('import-error-item').map((n) => text(n.querySelector('.ft-cell__title'))),
    } : null,
    sync: sync ? { state: sync.getAttribute('data-state'), ...cell(sync) } : null,
  };
}"""

# 导航栏的几何：标题取文字本身的范围，与返回按钮比较是否重叠
NAV_JS = """() => {
  const rect = (el) => { if (!el) return null; const r = el.getBoundingClientRect(); return { l: r.left, r: r.right, t: r.top, b: r.bottom, w: r.width, h: r.height }; };
  const textRect = (el) => { if (!el) return null; const range = document.createRange(); range.selectNodeContents(el); return rect(range); };
  const q = (id) => document.querySelector(`[data-testid="${id}"]`);
  return { back: rect(q('nav-back')), title: textRect(q('nav-title')), titleTag: q('nav-title') ? q('nav-title').tagName : null };
}"""

# 文件输入盖在文件行上：行中心那一点命中的就是文件输入
HIT_FILE_INPUT_JS = """() => {
  const row = document.querySelector('[data-testid="import-file-row"]');
  const r = row.getBoundingClientRect();
  const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
  return !!hit && hit.getAttribute('data-testid') === 'import-file';
}"""


# ---------------------------------------------------------------------------------------------- 小工具


def tid(page, testid: str):
    return page.get_by_test_id(testid)


def visible(locator, timeout: int = 4000) -> bool:
    try:
        locator.wait_for(state="visible", timeout=timeout)
        return True
    except PlaywrightTimeoutError:
        return False


def gone(locator, timeout: int = 4000) -> bool:
    try:
        locator.wait_for(state="hidden", timeout=timeout)
        return True
    except PlaywrightTimeoutError:
        return False


def poll(page, js: str, arg=None, timeout: int = 8000) -> bool:
    """等页面内的 JS 谓词成立；超时返回 False（让断言记失败，而不是抛异常打断后面的用例）。"""
    try:
        page.wait_for_function(js, arg=arg, timeout=timeout)
        return True
    except PlaywrightTimeoutError:
        return False


def toast(page, text: str):
    return page.locator(".m-enqueued-snackbars").get_by_text(text)


def wait_toast(page, text: str) -> bool:
    return visible(toast(page, text).first, 8000)


def wait_toasts_gone(page) -> None:
    # MASA 提示条在鼠标悬停时会暂停消失倒计时：先把鼠标挪开（真机是触摸，没有悬停）
    page.mouse.move(1, 1)
    page.wait_for_function("() => document.querySelectorAll('.m-enqueued-snackbars .m-snack__wrapper').length === 0", timeout=20000)


def state(page) -> dict:
    return page.evaluate(STATE_JS)


def wait_stage(page, value: str, timeout: int = 15000) -> bool:
    return poll(
        page,
        "(v) => { const c = document.querySelector('[data-testid=\"import-content\"]'); return !!c && c.getAttribute('data-stage') === v; }",
        arg=value,
        timeout=timeout,
    )


def pick_file(page, name: str, data: bytes) -> None:
    """直接给文件输入设置文件（等价于在系统文件选择器里选了它）。"""
    tid(page, "import-file").set_input_files({"name": name, "mimeType": "text/csv", "buffer": data})


def pick_file_via_row(page, name: str, data: bytes) -> bool:
    """点按文件行（整行都是文件输入）→ 唤起文件选择器 → 选文件。返回是否真的唤起了文件选择器。"""
    try:
        with page.expect_file_chooser(timeout=5000) as chooser:
            tid(page, "import-file-row").click()
    except PlaywrightTimeoutError:
        return False
    chooser.value.set_files({"name": name, "mimeType": "text/csv", "buffer": data})
    return True


def open_import(page) -> None:
    """从「我的」点「导入账单」进入（二级页面压栈），等页面出来。"""
    open_tab(page, "我的")
    tid(page, "profile-entry-import").click()
    wait_url(page, r"^/import$")
    wait_tab_bar(page, visible=False)
    tid(page, "import-page").wait_for(state="visible")
    wait_animations_settled(page)


def leave_import(page) -> None:
    page.go_back()
    wait_url(page, r"^/profile$")
    wait_tab_bar(page)
    wait_secondary_pages_closed(page)


def local_offset_minutes() -> int:
    return int(datetime.now().astimezone().utcoffset().total_seconds() // 60)


def same_id(a, b) -> bool:
    return a is not None and b is not None and str(a).lower() == str(b).lower()


def money_equals(value, expected) -> bool:
    try:
        return Decimal(str(value)) == Decimal(str(expected))
    except Exception:
        return False


def utc_text(wall: datetime, offset_minutes: int) -> str:
    """CSV 里的墙上时间按上报的偏移换算成库里的 UTC 文本（EF 的 SQLite 格式）。"""
    return (wall - timedelta(minutes=offset_minutes)).strftime("%Y-%m-%d %H:%M:%S")


def requests_of(cloud, method: str, path_regex: str):
    return cloud.calls(method, path_regex)


def ledger_known(cloud, ledger_id: str) -> bool:
    try:
        cloud.ledger(ledger_id)
        return True
    except KeyError:
        return False


def last_import_request(cloud, source: str = "wechat"):
    found = requests_of(cloud, "POST", rf"/api/import/{source}$")
    return found[-1] if found else None


def open_ledger_menu(page):
    """点账本行 → 返回账本菜单（没弹出来返回 None）。"""
    tid(page, "import-ledger-row").click()
    menu = tid(page, "import-ledger-menu")
    return menu if visible(menu) else None


def choose_ledger(page, name: str) -> bool:
    menu = open_ledger_menu(page)
    if menu is None:
        return False
    menu.get_by_test_id("ledger-option").filter(has_text=name).click()
    return gone(menu, 4000) and poll(
        page,
        "(n) => { const v = document.querySelector('[data-testid=\"import-ledger-row\"] .ft-cell__value'); return !!v && v.textContent.trim() === n; }",
        arg=name,
        timeout=4000,
    )


def create_ledger_from_menu(page, name: str) -> bool:
    """账本菜单 →「新建账本…」→ 填名称保存；返回 Sheet 是否收起。"""
    menu = open_ledger_menu(page)
    if menu is None:
        return False
    menu.get_by_test_id("ledger-option-new").click()
    if not visible(tid(page, "ledger-sheet")):
        return False
    tid(page, "ledger-name").fill(name)
    tid(page, "ledger-save").click()
    return gone(tid(page, "ledger-sheet"), 6000)


def slow_next_preview(seconds: float = 0.4) -> None:
    """让云端把下一次「解析预览」拖慢一点（页面里「解析中」停留得够久，测试一定能观察到它）。"""
    assert CLOUD is not None
    CLOUD.delay("POST", PREVIEW_PATH, seconds)


def wait_reparsed(page, expect: str = "preview") -> bool:
    """触发了一次（重新）解析之后：先确认进入了「解析中」、再等它落到 expect。
    页面本来就停在 preview / failed 时，直接等「落到 expect」会立刻读到上一次的残留状态——必须先看到「解析中」。"""
    return wait_stage(page, "parsing", 8000) and wait_stage(page, expect)


def read_file(page, name: str, data: bytes) -> bool:
    """选文件并等它读入：页面停在「已选文件」（ready），不会自动向云端解析。"""
    pick_file(page, name, data)
    return wait_stage(page, "ready", 8000)


def click_parse(page, *, expect: str = "preview") -> bool:
    """点底部主按钮「解析预览」，等解析结束（先看到「解析中」、再等 expect）。调用方先 slow_next_preview() 才保证看得到「解析中」。"""
    tid(page, "import-submit").click()
    return wait_reparsed(page, expect)


def preview_file(page, name: str, data: bytes, *, expect: str = "preview") -> bool:
    """选文件 → 点「解析预览」→ 等到解析结束（preview / failed）。"""
    slow_next_preview()
    return read_file(page, name, data) and click_parse(page, expect=expect)


def wait_error(page, text: str, timeout: int = 15000) -> bool:
    """等失败提示条出现这段文字（每个用例的提示文字各不相同，不会被上一个用例残留的提示冒充）。"""
    return poll(
        page,
        "(t) => document.querySelector('[data-testid=\"import-error-text\"]')?.textContent.trim() === t",
        arg=text,
        timeout=timeout,
    )


def continue_import(page) -> bool:
    """结果页点「继续导入」→ 回到第 1 步。"""
    tid(page, "import-submit").click()
    return wait_stage(page, "idle", 6000)


# ---------------------------------------------------------------------------------------------- 主流程


class Ctx:
    def __init__(self, run: TestRun, page, db: LocalDb, cloud: FakeCloud, phone: str, user_id: str) -> None:
        self.run = run
        self.page = page
        self.db = db
        self.cloud = cloud
        self.phone = phone
        self.user_id = user_id
        self.ledger_a = ""
        self.ledger_b = ""
        self.ledger_a_name = unique_name("导入账本A")
        self.ledger_b_name = unique_name("导入账本B")
        self.offset = local_offset_minutes()


def check_shell(c: Ctx) -> None:
    """A. 外壳与初始状态（用户还没有账本，也还没有账单）"""
    run, page = c.run, c.page

    # 先看一眼账单 Tab：现在没有账单（导入完成后要回来对照）。账单 Tab 默认只看「本月」（第 2 轮评审），而导入的是 2024 年的账单，
    # 所以先点空状态里的「查看全部账单」取消时间范围（不限时间）——Tab 根页面常驻，导入完成后回来才对照得到
    open_tab(page, "账单")
    run.check("导入前：账单 Tab 是空状态（默认看「本月」：「本月还没有账单」，给「查看全部账单」）", visible(tid(page, "bills-empty")) and visible(tid(page, "bills-show-all")))
    tid(page, "bills-show-all").click()
    page.wait_for_function("() => document.querySelector('[data-testid=\"bills-chip-time\"]').textContent.trim() === '时间'")
    tid(page, "bills-first-bill").wait_for(state="visible")
    run.check("点「查看全部账单」后（不限时间）仍是空状态：「还没有账单」，不再有「查看全部账单」", visible(tid(page, "bills-empty")) and tid(page, "bills-show-all").count() == 0)

    open_import(page)
    st = state(page)
    nav = page.evaluate(NAV_JS)
    run.check("二级页面：Tab 栏隐藏、标题「导入账单」、返回按钮可见", tid(page, "tab-bar").is_hidden() and tid(page, "nav-title").inner_text().strip() == "导入账单" and visible(tid(page, "nav-back")), str(nav))
    run.check("初始：内容状态 idle、来源微信、没有忙", st["stage"] == "idle" and st["source"] == "wechat" and st["busy"] is None, f"{st['stage']} {st['source']} {st['busy']}")
    run.check("第 1 步小标题「1 · 选择来源」", st["sourceStep"] and st["sourceStep"]["header"] == "1 · 选择来源", str(st["sourceStep"]))
    run.check(
        "来源分段：微信 | 支付宝，选中微信，每段点击区域 ≥ 44",
        [s["label"] for s in st["segments"]] == ["微信", "支付宝"] and st["sourceValue"] == "wechat" and [s["pressed"] for s in st["segments"]] == ["true", "false"] and all(s["h"] >= 44 for s in st["segments"]),
        str(st["segments"]),
    )

    # 折叠的导出说明
    run.check("导出说明默认收起：标题「如何导出微信账单」、aria-expanded=false、内容不在页面上", st["guideTitle"] == "如何导出微信账单" and st["guideOpen"] == "false" and st["guideText"] is None, f"{st['guideTitle']} {st['guideOpen']}")
    tid(page, "import-guide-toggle").click()
    visible(tid(page, "import-guide"))
    st = state(page)
    run.check(
        "点开：aria-expanded=true、导出路径与格式说明（微信）",
        st["guideOpen"] == "true" and st["guidePath"] == "微信 → 我 → 服务 → 钱包 → 账单 → 常见问题 → 下载账单" and "支持微信导出的 CSV 格式账单文件。" in (st["guideText"] or "") and st["guideControls"] == "import-guide",
        f"{st['guideOpen']} {st['guidePath']} {st['guideText']}",
    )
    tid(page, "import-guide-toggle").click()
    run.check("再点：收起", gone(tid(page, "import-guide")) and state(page)["guideOpen"] == "false")

    # 文件行
    st = state(page)
    run.check("第 2 步小标题「2 · 选择文件」", st["fileGroup"]["header"] == "2 · 选择文件", str(st["fileGroup"]))
    run.check(
        "文件行（还没选）：标题「选择 CSV 文件」、副标题「选择微信导出的账单文件」、右值「选择」、data-has-file=false",
        st["file"] == {"title": "选择 CSV 文件", "subtitle": "选择微信导出的账单文件", "value": "选择"} and st["hasFile"] == "false" and st["fileName"] is None,
        str(st["file"]),
    )
    run.check(
        "文件步骤的脚注说明格式与大小限制，并点明流程「选好文件后点「解析预览」…确认无误再导入」",
        "支持微信导出的 CSV 账单，不超过 10 MB" in (st["fileGroup"]["footer"] or "") and "选好文件后点「解析预览」" in (st["fileGroup"]["footer"] or "") and "确认无误再导入" in (st["fileGroup"]["footer"] or ""),
        str(st["fileGroup"]["footer"]),
    )
    box, row = st["inputBox"], st["fileRowBox"]
    run.check(
        "文件输入：只接受 .csv、有读屏名称、盖满整行（行中心命中的就是它）、整行高度 ≥ 44",
        st["inputAccept"] == ".csv" and st["inputAria"] == "选择 CSV 文件" and abs(box["l"] - row["l"]) <= 1 and abs(box["r"] - row["r"]) <= 1 and abs(box["t"] - row["t"]) <= 1 and abs(box["b"] - row["b"]) <= 1 and page.evaluate(HIT_FILE_INPUT_JS) and row["h"] >= 44,
        f"{box} / {row}",
    )

    # 底部主按钮
    sub, foot = st["submit"], st["foot"]
    run.check("主按钮：置灰（还没选文件）、文字「解析预览」、动作是解析（data-action=parse）", sub["disabled"] and sub["text"] == "解析预览" and sub["action"] == "parse", str(sub))
    run.check(
        "主按钮固定在页面底部：fixed、贴底、满宽，按钮高度 ≥ 44 且在屏幕内",
        foot["position"] == "fixed" and abs(foot["box"]["b"] - SCREEN_H) <= 1 and abs(foot["box"]["w"] - SCREEN_W) <= 1 and sub["box"]["h"] >= 44 and sub["box"]["b"] <= SCREEN_H,
        str(foot),
    )
    run.check("没选文件时没有预览、账本、结果区块", not st["hasPreview"] and st["ledger"] is None and st["result"] is None and st["parsing"] is None and st["error"] is None)
    run.check("没有横向溢出；每屏玻璃层 ≤ 3", st["overflowX"] <= 0 and page.evaluate(GLASS_LAYERS_JS) <= 3, f"{st['overflowX']} / {page.evaluate(GLASS_LAYERS_JS)}")
    shot(page, "import-idle")


def check_source_switch(c: Ctx) -> None:
    """B. 换来源（还没选文件）"""
    run, page = c.run, c.page
    tid(page, "import-source").get_by_test_id("segment").filter(has_text="支付宝").click()
    wait_attr(page, CONTENT, "data-source", "alipay")
    tid(page, "import-guide-toggle").click()
    visible(tid(page, "import-guide"))
    st = state(page)
    run.check(
        "换到支付宝：分段选中支付宝、说明标题与导出路径、文件行副标题、文件步骤脚注都换成支付宝",
        st["sourceValue"] == "alipay" and [s["pressed"] for s in st["segments"]] == ["false", "true"] and st["guideTitle"] == "如何导出支付宝账单"
        and st["guidePath"] == "支付宝 → 我的 → 账单 → 右上角 ··· → 开具交易流水证明" and "支持支付宝导出的 CSV 格式账单文件。" in (st["guideText"] or "")
        and st["file"]["subtitle"] == "选择支付宝导出的账单文件" and "支持支付宝导出的 CSV 账单" in (st["fileGroup"]["footer"] or ""),
        str({k: st[k] for k in ("sourceValue", "guideTitle", "guidePath", "file")}),
    )
    tid(page, "import-source").get_by_test_id("segment").filter(has_text="微信").click()
    wait_attr(page, CONTENT, "data-source", "wechat")
    st = state(page)
    run.check("换回微信：说明仍展开、路径换回微信", st["guideOpen"] == "true" and st["guidePath"].startswith("微信 →") and st["guideTitle"] == "如何导出微信账单", f"{st['guideOpen']} {st['guidePath']}")
    tid(page, "import-guide-toggle").click()
    gone(tid(page, "import-guide"))


def check_preview(c: Ctx) -> None:
    """C. 选文件（点按文件行唤起选择器）→ 解析预览（微信）"""
    run, page, cloud = c.run, c.page, c.cloud
    before = len(requests_of(cloud, "POST", r"/api/import/wechat/preview$"))
    cloud.delay("POST", r"/api/import/wechat/preview$", 2.0)

    chosen = pick_file_via_row(page, "wechat.csv", WECHAT_BYTES)
    run.check("点按文件行 → 唤起系统文件选择器（整行都是文件输入）", chosen)

    # 选好文件只读入、不自动向云端解析：停在「已选文件」，由底部显式的「解析预览」触发（第 2 轮评审后用户拍板恢复这个按钮）
    run.check("选好文件 → 停在「已选文件」（ready）", wait_stage(page, "ready", 8000))
    time.sleep(1.0)  # 给「自动解析」留出现形的时间——它不该出现
    st = state(page)
    run.check(
        "已选文件：不自动解析——云端没有收到预览请求、仍是 ready、没有「正在解析」、没有预览与失败提示",
        len(requests_of(cloud, "POST", r"/api/import/wechat/preview$")) == before and st["stage"] == "ready" and st["parsing"] is None and not st["hasPreview"] and st["error"] is None and st["busy"] is None,
        f"{st['stage']} {st['parsing']} {st['hasPreview']} {st['error']} 预览请求={len(requests_of(cloud, 'POST', r'/api/import/wechat/preview$')) - before}",
    )
    run.check(
        "已选文件：文件行显示文件名与大小、右值「重新选择」、文件输入可用",
        st["hasFile"] == "true" and st["fileName"] == "wechat.csv" and st["file"]["title"] == "wechat.csv" and st["file"]["subtitle"].endswith((" B", " KB")) and st["file"]["value"] == "重新选择" and st["inputDisabled"] is False,
        str(st["file"]),
    )
    run.check(
        "已选文件：底部主按钮「解析预览」点亮（动作 parse、不是忙态）",
        st["submit"]["text"] == "解析预览" and not st["submit"]["disabled"] and st["submit"]["action"] == "parse" and st["submit"]["busy"] is None,
        str(st["submit"]),
    )

    tid(page, "import-submit").click()
    run.check("点「解析预览」→ 进入解析中", wait_stage(page, "parsing", 6000))
    st = state(page)
    run.check(
        "解析中：「正在解析…」（role=status、data-phase=parsing）、文件名仍显示、内容标记 aria-busy",
        st["parsing"] == "正在解析…" and st["parsingRole"] == "status" and st["parsingPhase"] == "parsing" and st["hasFile"] == "true" and st["fileName"] == "wechat.csv" and st["busy"] == "true",
        f"{st['parsing']} {st['parsingRole']} {st['parsingPhase']} {st['fileName']} {st['busy']}",
    )
    run.check(
        "解析中：文件输入禁用；主按钮忙（禁用 + aria-busy）、文字「解析中」",
        st["inputDisabled"] is True and st["submit"]["disabled"] and st["submit"]["busy"] == "true" and st["submit"]["text"] == "解析中",
        f"{st['inputDisabled']} {st['submit']}",
    )
    tid(page, "import-source").get_by_test_id("segment").filter(has_text="支付宝").click()
    run.check("解析中点另一个来源：不响应（来源仍是微信）", state(page)["source"] == "wechat" and state(page)["stage"] == "parsing")

    run.check("解析完成 → 预览", wait_stage(page, "preview"))
    wait_animations_settled(page)
    previews = requests_of(cloud, "POST", r"/api/import/wechat/preview$")
    request = previews[-1] if previews else None
    run.check(
        "预览请求：发往微信预览接口，只带 CSV 原文（BOM 已去掉、内容一字不差），不带账本与时区偏移",
        len(previews) == before + 1 and request is not None and request.body == {"CsvContent": WECHAT_TEXT} and request.status == 200,
        str(request),
    )
    run.check("解析预览没有写入任何账单（云端没有收到正式导入）", cloud.imports() == [])

    st = state(page)
    run.check("预览：data-count=3、data-skipped=2、第 3 步小标题", st["previewCount"] == "3" and st["previewSkipped"] == "2" and st["previewHeader"] == "3 · 解析预览", f"{st['previewCount']} {st['previewSkipped']} {st['previewHeader']}")
    run.check("汇总：支出 50.50、收入 1200.00、结余 1149.50（金额按全部账单汇总）", (st["expense"], st["income"], st["balance"]) == ("50.50", "1200.00", "1149.50"), f"{st['expense']} {st['income']} {st['balance']}")
    run.check("汇总说明：笔数与时间范围（CSV 里的日期，不换算时区）", st["caption"] == "共 3 笔账单 · 2024-05-01 至 2024-05-03", str(st["caption"]))
    run.check("跳过说明：另有 2 条中性交易 / 无效交易已跳过", st["skipped"] == "另有 2 条中性交易 / 无效交易已跳过", str(st["skipped"]))
    run.check("预览行是 CSV 顺序的 3 条", [r["title"] for r in st["rows"]] == ["午餐外卖", "转账备注", "扫二维码付款"], str([r["title"] for r in st["rows"]]))
    run.check(
        "预览行内容：商户 · 渠道、带符号的金额、CSV 里的交易时间原样、收入标记",
        [(r["subtitle"], r["amount"], r["time"], r["income"]) for r in st["rows"]]
        == [("美团外卖 · 微信支付", "-¥38.50", "2024-05-01 12:30", "false"), ("小王 · 银行卡", "+¥1,200.00", "2024-05-02 09:05", "true"), ("便利店 · 信用卡", "-¥12.00", "2024-05-03 18:20", "false")],
        str([(r["subtitle"], r["amount"], r["time"], r["income"]) for r in st["rows"]]),
    )
    run.check("没超过上限：不显示「仅显示前…」", st["more"] is None)
    run.check(
        "文件行：显示文件名与大小、右值「重新选择」、data-has-file=true",
        st["file"]["title"] == "wechat.csv" and st["file"]["subtitle"].endswith((" B", " KB")) and st["file"]["value"] == "重新选择" and st["hasFile"] == "true",
        str(st["file"]),
    )
    run.check("文件输入恢复可用（可以换文件）", st["inputDisabled"] is False)
    run.check("预览行都在屏幕内、没有横向溢出", all(r["box"]["l"] >= 0 and r["box"]["r"] <= SCREEN_W + 0.5 for r in st["rows"]) and st["overflowX"] <= 0, str(st["overflowX"]))
    run.check("第 4 步：「4 · 导入到账本」、账本脚注说明必选与导入前同步", st["ledgerGroup"] and st["ledgerGroup"]["header"] == "4 · 导入到账本" and "必须归属一个账本" in st["ledgerGroup"]["footer"] and "导入前会先同步一次账本" in st["ledgerGroup"]["footer"], str(st["ledgerGroup"]))
    run.check("每屏玻璃层 ≤ 3", page.evaluate(GLASS_LAYERS_JS) <= 3, str(page.evaluate(GLASS_LAYERS_JS)))
    shot(page, "import-preview")


def check_ledger_required(c: Ctx) -> None:
    """D. 账本必填（用户没有任何账本）"""
    run, page, cloud, db = c.run, c.page, c.cloud, c.db
    st = state(page)
    run.check(
        "没有账本：账本行右值「选择账本」（红色提示）、data-missing=true、没有 data-value",
        st["ledger"]["title"] == "导入到账本" and st["ledger"]["value"] == "选择账本" and st["ledgerMissing"] == "true" and not st["ledgerId"],
        str(st["ledger"]),
    )
    run.check("账本行：弹出菜单属性（aria-haspopup=menu、aria-expanded=false）、高度 ≥ 44", st["ledgerPopup"] == "menu" and st["ledgerExpanded"] == "false" and page.evaluate("() => document.querySelector('[data-testid=\"import-ledger-row\"]').getBoundingClientRect().height") >= 44)
    run.check(
        "预览出来后主按钮从「解析预览」换成「导入 3 条账单」（动作 import）、置灰（账本必填）",
        st["submit"]["text"] == "导入 3 条账单" and st["submit"]["action"] == "import" and st["submit"]["disabled"],
        str(st["submit"]),
    )

    try:
        tid(page, "import-submit").click(force=True, timeout=2000)
    except PlaywrightTimeoutError:
        pass
    time.sleep(0.5)
    run.check(
        "强行点置灰的按钮：没有任何导入请求（界面先拦住，云端没收到）",
        requests_of(cloud, "POST", r"/api/import/wechat$") == [] and cloud.imports() == [] and state(page)["stage"] == "preview",
        str(requests_of(cloud, "POST", r"/api/import/wechat$")),
    )

    menu = open_ledger_menu(page)
    run.check(
        "点账本行 → 账本菜单：没有账本，只有「新建账本…」",
        menu is not None and menu.get_by_test_id("ledger-option").count() == 0 and "新建账本" in menu.get_by_test_id("ledger-option-new").inner_text() and state(page)["ledgerExpanded"] == "true",
    )
    menu.get_by_test_id("ledger-option-new").click()
    run.check("点「新建账本…」→ 新建账本 Sheet，菜单收起", visible(tid(page, "ledger-sheet")) and gone(tid(page, "import-ledger-menu")))
    tid(page, "ledger-name").fill(c.ledger_a_name)
    tid(page, "ledger-save").click()
    run.check("保存 → Sheet 收起", gone(tid(page, "ledger-sheet"), 6000))
    wait_animations_settled(page)
    st = state(page)
    c.ledger_a = c.db.ledger_id_by_name(c.user_id, c.ledger_a_name)
    run.check("新账本进了本地库（用户的第一个账本）", len(db.ledgers_of_user(c.user_id)) == 1)
    run.check(
        "新建后自动选中：账本行显示它、data-missing=false、data-value 是它的 Id",
        st["ledger"]["value"] == c.ledger_a_name and st["ledgerMissing"] == "false" and same_id(st["ledgerId"], c.ledger_a),
        f"{st['ledger']} {st['ledgerMissing']} {st['ledgerId']}",
    )
    run.check("主按钮点亮：「导入 3 条账单」", st["submit"]["text"] == "导入 3 条账单" and not st["submit"]["disabled"], str(st["submit"]))
    run.check("新建账本之后仍停在预览（预览与文件都保留）", st["stage"] == "preview" and st["previewCount"] == "3" and st["fileName"] == "wechat.csv")

    menu = open_ledger_menu(page)
    run.check(
        "再开菜单：1 个账本、对勾在它身上",
        menu is not None and menu.get_by_test_id("ledger-option").count() == 1 and menu.locator('[data-testid="ledger-option"][data-selected="true"]').count() == 1,
    )
    page.keyboard.press("Escape")
    run.check("Esc 收起菜单", gone(tid(page, "import-ledger-menu")) and state(page)["ledger"]["value"] == c.ledger_a_name)


def check_import_success(c: Ctx) -> None:
    """E. 导入（成功）——慢响应下观察导入中的状态，随后核对云端 / 本地库 / 账单 Tab"""
    run, page, cloud, db = c.run, c.page, c.cloud, c.db
    run.check("导入前：云端还不认识这个刚在本机新建的账本", not ledger_known(cloud, c.ledger_a))
    cloud.delay("POST", r"/api/import/wechat$", 2.0)
    pull_before = len(requests_of(cloud, "POST", r"/api/sync/pull$"))

    tid(page, "import-submit").click()
    run.check("点「导入 3 条账单」→ 导入中", wait_stage(page, "importing", 6000))
    st = state(page)
    run.check(
        "导入中：主按钮忙（禁用 + aria-busy、文字「导入中」）、账本行 / 文件输入禁用、内容标记 aria-busy",
        st["submit"]["busy"] == "true" and st["submit"]["disabled"] and st["submit"]["text"] == "导入中" and st["ledgerDisabled"] is True and st["inputDisabled"] is True and st["busy"] == "true",
        f"{st['submit']} {st['ledgerDisabled']} {st['inputDisabled']} {st['busy']}",
    )
    run.check("导入中：预览仍在（失败时不丢）", st["hasPreview"] and st["previewCount"] == "3")

    run.check("导入完成 → 结果", wait_stage(page, "done", 15000))
    wait_animations_settled(page)

    imports = cloud.imports("wechat")
    request = last_import_request(cloud)
    run.check("云端收下了一次微信导入（且只有一次）", len(imports) == 1 and request is not None and request.status == 200, str(len(imports)))
    body = request.body if request else {}
    run.check(
        "导入请求：CSV 原文、本机时区偏移（UtcOffsetMinutes）、所选账本",
        body.get("CsvContent") == WECHAT_TEXT and body.get("UtcOffsetMinutes") == c.offset and same_id(body.get("LedgerId"), c.ledger_a) and set(body) == {"CsvContent", "UtcOffsetMinutes", "LedgerId"},
        f"{ {k: v for k, v in body.items() if k != 'CsvContent'} } 本机偏移={c.offset}",
    )
    everything = list(cloud.requests)
    import_index = next((i for i, r in enumerate(everything) if r.method == "POST" and r.path == "/api/import/wechat"), -1)
    pushes_before = [r for r in everything[:import_index] if r.method == "POST" and r.path == "/api/sync/ledgers/push"] if import_index >= 0 else []
    pushed_ids = {str(item.get("Id", "")).lower() for r in pushes_before for item in (r.body or {}).get("Ledgers", [])}
    run.check(
        "导入前先同步账本：导入请求之前有一次账本推送、带着这个新账本；云端因此认得它（所有者是我）",
        pushes_before and c.ledger_a.lower() in pushed_ids and ledger_known(cloud, c.ledger_a) and cloud.ledger(c.ledger_a)["ownerId"] == cloud.user_id(c.phone),
        f"导入请求序号={import_index} 之前的账本推送={len(pushes_before)} 推送过的账本={pushed_ids}",
    )
    run.check("云端入库的 3 条账单带着账本与我的云端用户 Id", len(imports[0]["bills"]) == 3 and all(same_id(b["ledgerId"], c.ledger_a) and b["userId"] == cloud.user_id(c.phone) for b in imports[0]["bills"]))

    st = state(page)
    result = st["result"]
    run.check("结果页：data-total=3 / success=3 / failed=0", result and (result["total"], result["success"], result["failed"]) == ("3", "3", "0"), str(result))
    run.check("结果标题「导入完成」、说明「成功导入 3 条账单到「账本」」", result["title"] == "导入完成" and result["summary"] == f"成功导入 3 条账单到「{c.ledger_a_name}」", f"{result['title']} / {result['summary']}")
    run.check(
        "结果分组：账本 / 总计 / 成功（没有失败行，没有失败详情）",
        result["ledger"]["value"] == c.ledger_a_name and result["totalRow"]["value"] == "3 条" and result["successRow"]["value"] == "3 条" and result["failedRow"] is None and result["errorsGroup"] is None and result["errors"] == [],
        str(result),
    )
    run.check("结果页：来源 / 文件 / 预览 / 账本这些步骤都收起了", not st["hasPreview"] and st["ledger"] is None and st["sourceValue"] is None and st["file"] is None)
    run.check("Toast：「微信账单导入完成：成功 3 条」", wait_toast(page, "微信账单导入完成：成功 3 条"))

    run.check("自动同步一次：「同步到本机」一行最终是已完成", poll(page, "() => document.querySelector('[data-testid=\"import-sync\"]')?.getAttribute('data-state') === 'synced'", timeout=15000))
    st = state(page)
    run.check(
        "同步一行：标题「同步到本机」、右值「已完成」、说明「已从云端拉取到本地，账单列表即可查看」",
        st["sync"]["title"] == "同步到本机" and st["sync"]["value"] == "已完成" and st["sync"]["subtitle"] == "已从云端拉取到本地，账单列表即可查看",
        str(st["sync"]),
    )
    run.check("同步时向云端拉取了账单", len(requests_of(cloud, "POST", r"/api/sync/pull$")) > pull_before)
    run.check("主按钮变成可点的「继续导入」", st["submit"]["text"] == "继续导入" and not st["submit"]["disabled"] and st["stage"] == "done", str(st["submit"]))
    run.check("结果页没有横向溢出、玻璃层 ≤ 3", st["overflowX"] <= 0 and page.evaluate(GLASS_LAYERS_JS) <= 3)
    shot(page, "import-result")

    # 本地库：同步把导入的账单拉了下来
    bills = db.bills_of_user(c.user_id)
    by_note = {b["Note"]: b for b in bills}
    run.check("本地库里有 3 条导入的账单（描述作备注）", sorted(by_note) == sorted(["午餐外卖", "转账备注", "扫二维码付款"]), str(sorted(by_note)))
    expected = {
        "午餐外卖": ("38.50", BILL_TYPE_EXPENSE, datetime(2024, 5, 1, 12, 30)),
        "转账备注": ("1200.00", BILL_TYPE_INCOME, datetime(2024, 5, 2, 9, 5)),
        "扫二维码付款": ("12.00", BILL_TYPE_EXPENSE, datetime(2024, 5, 3, 18, 20)),
    }
    ok = True
    detail = []
    for note, (amount, bill_type, wall) in expected.items():
        bill = by_note.get(note)
        good = (
            bill is not None and money_equals(bill["Amount"], amount) and bill["Type"] == bill_type and bill["Source"] == SOURCE_IMPORT
            and same_id(bill["LedgerId"], c.ledger_a) and same_id(bill["UserId"], c.user_id) and not bill["IsDeleted"]
            and str(bill["TransactionTime"]).startswith(utc_text(wall, c.offset))
        )
        ok = ok and good
        detail.append(f"{note}: {None if bill is None else (bill['Amount'], bill['Type'], bill['Source'], bill['LedgerId'], bill['TransactionTime'])}")
    run.check("账单：金额 / 类型 / 数据来源「导入」/ 账本 / 用户都对，交易时间在库里是 UTC（= CSV 墙上时间 − 上报的偏移）", ok, "；".join(detail))
    run.check("上报的偏移就是本机时区的当前偏移", body.get("UtcOffsetMinutes") == local_offset_minutes(), f"{body.get('UtcOffsetMinutes')} / {local_offset_minutes()}")

    # 回到「我的」→ 账单 Tab：导入后立刻看得到（不需要手动刷新）
    wait_toasts_gone(page)
    leave_import(page)
    open_tab(page, "账单")
    run.check(
        "账单 Tab：自动刷新，看得到导入的 3 笔账单",
        poll(page, "() => document.querySelector('[data-testid=\"bills-count\"]')?.getAttribute('data-count') === '3'", timeout=10000),
        str(page.evaluate("() => document.querySelector('[data-testid=\"bills-count\"]')?.getAttribute('data-count')")),
    )
    run.check(
        "账单 Tab 汇总：支出 50.50、收入 1200.00",
        page.evaluate("() => [document.querySelector('[data-testid=\"bills-total-expense\"]')?.getAttribute('data-amount'), document.querySelector('[data-testid=\"bills-total-income\"]')?.getAttribute('data-amount')]") == ["50.50", "1200.00"],
        str(page.evaluate("() => [document.querySelector('[data-testid=\"bills-total-expense\"]')?.getAttribute('data-amount'), document.querySelector('[data-testid=\"bills-total-income\"]')?.getAttribute('data-amount')]")),
    )

    # 再进导入页：「继续导入」的回到第 1 步另在 F 节验证（这里是全新的页面实例：初始状态）
    open_import(page)
    st = state(page)
    run.check("再次进入导入页：全新的页面（idle、没有文件）", st["stage"] == "idle" and st["hasFile"] == "false" and st["result"] is None)


def check_second_ledger_and_alipay(c: Ctx) -> None:
    """F. 有两个账本：选另一个账本、支付宝来源导入、「继续导入」"""
    run, page, cloud, db = c.run, c.page, c.cloud, c.db
    # 账本 B 在页面进入之后才出现在库里 → 重新进入页面让它读到
    c.ledger_b = db.seed_ledger(c.user_id, c.ledger_b_name, sort_order=5)
    leave_import(page)
    open_import(page)

    tid(page, "import-source").get_by_test_id("segment").filter(has_text="支付宝").click()
    wait_attr(page, CONTENT, "data-source", "alipay")
    run.check("支付宝文件（GB18030、无 BOM）→ 选好 → 点「解析预览」→ 预览", preview_file(page, "alipay.csv", ALIPAY_BYTES))
    st = state(page)
    requests = requests_of(cloud, "POST", r"/api/import/alipay/preview$")
    run.check("预览请求发往支付宝预览接口，CSV 按 GB18030 解码、内容一字不差", bool(requests) and requests[-1].body == {"CsvContent": ALIPAY_TEXT}, str(requests[-1].body if requests else None))
    run.check("支付宝预览：2 笔、没有「跳过」说明（支付宝接口不返回跳过数）、data-skipped=0", st["previewCount"] == "2" and st["previewSkipped"] == "0" and st["skipped"] is None, f"{st['previewCount']} {st['previewSkipped']} {st['skipped']}")
    run.check("汇总：支出 28.00、收入 88.00、结余 60.00；说明「共 2 笔账单 · 2024-05-06 至 2024-05-07」", (st["expense"], st["income"], st["balance"]) == ("28.00", "88.00", "60.00") and st["caption"] == "共 2 笔账单 · 2024-05-06 至 2024-05-07", f"{st['expense']} {st['income']} {st['balance']} {st['caption']}")
    run.check(
        "预览行：商户 · 渠道（支付宝）、金额带符号、时间原样",
        [(r["title"], r["subtitle"], r["amount"], r["time"]) for r in st["rows"]] == [("拿铁", "某咖啡店 · 支付宝", "-¥28.00", "2024-05-06 08:15"), ("红包", "朋友 · 支付宝", "+¥88.00", "2024-05-07 20:00")],
        str([(r["title"], r["subtitle"], r["amount"], r["time"]) for r in st["rows"]]),
    )

    # 有账本时默认已选一个（自己的账本里的第一个 / 默认账本）
    run.check("有账本时账本行默认已选（没有红色提示）、主按钮可点「导入 2 条账单」", st["ledgerMissing"] == "false" and bool(st["ledgerId"]) and not st["submit"]["disabled"] and st["submit"]["text"] == "导入 2 条账单", f"{st['ledger']} {st['ledgerMissing']} {st['submit']}")
    menu = open_ledger_menu(page)
    run.check("账本菜单：自己的两个账本 + 新建账本…，当前账本带对勾", menu is not None and menu.get_by_test_id("ledger-option").count() == 2 and menu.locator('[data-testid="ledger-option"][data-selected="true"]').count() == 1)
    page.keyboard.press("Escape")
    gone(tid(page, "import-ledger-menu"))
    run.check("改选账本 B：账本行显示 B、data-value 是 B", choose_ledger(page, c.ledger_b_name) and same_id(state(page)["ledgerId"], c.ledger_b), str(state(page)["ledger"]))

    tid(page, "import-submit").click()
    run.check("导入 → 结果", wait_stage(page, "done", 15000))
    request = last_import_request(cloud, "alipay")
    run.check(
        "导入请求发往支付宝导入接口、账本是 B、带本机时区偏移",
        request is not None and same_id(request.body.get("LedgerId"), c.ledger_b) and request.body.get("UtcOffsetMinutes") == c.offset and request.body.get("CsvContent") == ALIPAY_TEXT and len(requests_of(cloud, "POST", r"/api/import/wechat$")) == 1,
        str(request),
    )
    run.check("账本 B 也是导入前同步到云端的", ledger_known(cloud, c.ledger_b) and cloud.ledger(c.ledger_b)["name"] == c.ledger_b_name)
    st = state(page)
    run.check(
        "结果：「支付宝账单导入完成：成功 2 条」、说明里是账本 B、同步完成",
        wait_toast(page, "支付宝账单导入完成：成功 2 条") and st["result"]["summary"] == f"成功导入 2 条账单到「{c.ledger_b_name}」" and poll(page, "() => document.querySelector('[data-testid=\"import-sync\"]')?.getAttribute('data-state') === 'synced'", timeout=15000),
        str(st["result"]),
    )
    wait_toasts_gone(page)

    # 「继续导入」：回到第 1 步，来源沿用、文件清空、结果消失
    run.check("「继续导入」→ 回到第 1 步", continue_import(page))
    st = state(page)
    run.check(
        "继续导入后：来源沿用支付宝、文件行清空、没有预览与结果、主按钮回到置灰的「解析预览」",
        st["source"] == "alipay" and st["hasFile"] == "false" and st["fileName"] is None and not st["hasPreview"] and st["result"] is None and st["submit"]["disabled"] and st["submit"]["text"] == "解析预览" and st["submit"]["action"] == "parse",
        f"{st['source']} {st['hasFile']} {st['submit']}",
    )
    tid(page, "import-source").get_by_test_id("segment").filter(has_text="微信").click()
    wait_attr(page, CONTENT, "data-source", "wechat")


def check_parse_failures(c: Ctx) -> None:
    """G. 换来源重新解析、解析失败与重试、各种编码"""
    run, page, cloud = c.run, c.page, c.cloud

    # 微信文件 → 预览；换成支付宝：已读入的文件保留、预览作废、回到「已选文件」，不自动重新解析；点「解析预览」才按支付宝解析（解不出 → 失败）；
    # 换回微信同样回到「已选文件」，再点「解析预览」——全程不用重选文件
    run.check("微信文件 → 预览", preview_file(page, "wechat.csv", WECHAT_BYTES))
    before = len(requests_of(cloud, "POST", r"/api/import/alipay/preview$"))
    tid(page, "import-source").get_by_test_id("segment").filter(has_text="支付宝").click()
    run.check("预览出来后换来源 → 回到「已选文件」（ready）", wait_stage(page, "ready", 6000))
    time.sleep(1.0)  # 给「自动重新解析」现形的时间——它不该出现
    st = state(page)
    run.check(
        "换来源：预览作废（没有预览与账本区块）、文件保留（文件名仍显示）、不自动重新解析（云端没收到支付宝预览请求、没有「正在解析」）",
        st["stage"] == "ready" and st["source"] == "alipay" and not st["hasPreview"] and st["ledger"] is None and st["hasFile"] == "true" and st["fileName"] == "wechat.csv"
        and st["parsing"] is None and st["error"] is None and len(requests_of(cloud, "POST", r"/api/import/alipay/preview$")) == before,
        f"{st['stage']} {st['source']} {st['hasPreview']} {st['fileName']} {st['parsing']} {st['error']} 支付宝预览请求={len(requests_of(cloud, 'POST', r'/api/import/alipay/preview$')) - before}",
    )
    run.check("换来源后底部主按钮回到可点的「解析预览」（动作 parse）", st["submit"]["text"] == "解析预览" and not st["submit"]["disabled"] and st["submit"]["action"] == "parse", str(st["submit"]))
    slow_next_preview()
    run.check("点「解析预览」→ 按支付宝解析（选错来源：把微信文件当支付宝）→ 解析失败", click_parse(page, expect="failed"))
    st = state(page)
    requests = requests_of(cloud, "POST", r"/api/import/alipay/preview$")
    run.check("同一个文件被提交给支付宝预览接口（没有重新选文件）", len(requests) == before + 1 and requests[-1].body == {"CsvContent": WECHAT_TEXT}, str(len(requests) - before))
    run.check(
        "失败提示：未解析出任何账单，请确认文件为支付宝导出的 CSV 账单（role=alert）、有「重试」、文件名仍显示",
        st["error"] == "未解析出任何账单，请确认文件为支付宝导出的 CSV 账单" and st["errorRole"] == "alert" and st["retry"] and st["fileName"] == "wechat.csv",
        f"{st['error']} {st['errorRole']} {st['retry']} {st['fileName']}",
    )
    run.check(
        "失败时没有预览与账本区块，页面仍可操作（输入可用）；文件还在，底部主按钮仍是可点的「解析预览」（与「重试」等价）",
        not st["hasPreview"] and st["ledger"] is None and st["inputDisabled"] is False and not st["submit"]["disabled"] and st["submit"]["text"] == "解析预览" and st["submit"]["action"] == "parse",
        f"{st['hasPreview']} {st['ledger']} {st['inputDisabled']} {st['submit']}",
    )
    run.check("失败提示条在屏幕内、重试按钮点击区域 ≥ 44", st["errorBox"]["l"] >= 0 and st["errorBox"]["r"] <= SCREEN_W + 0.5 and st["retryBox"]["h"] >= 44 and st["overflowX"] <= 0, f"{st['errorBox']} {st['retryBox']}")
    shot(page, "import-parse-failed")
    tid(page, "import-source").get_by_test_id("segment").filter(has_text="微信").click()
    run.check("失败后换回微信 → 回到「已选文件」：失败提示与「重试」消失、不自动解析", wait_stage(page, "ready", 6000) and state(page)["error"] is None and not state(page)["retry"] and state(page)["parsing"] is None)
    slow_next_preview()
    run.check("点「解析预览」→ 预览（不用重选文件）", click_parse(page) and state(page)["previewCount"] == "3")

    # 不是账单的文件
    run.check("换一个不是账单的文件 → 解析失败", preview_file(page, "notes.csv", NOT_A_BILL_TEXT.encode("utf-8"), expect="failed"))
    st = state(page)
    run.check("提示「未解析出任何账单，请确认文件为微信导出的 CSV 账单」，文件名换成新文件", st["error"] == "未解析出任何账单，请确认文件为微信导出的 CSV 账单" and st["fileName"] == "notes.csv", f"{st['error']} {st['fileName']}")
    before = len(requests_of(cloud, "POST", r"/api/import/wechat/preview$"))
    slow_next_preview()
    tid(page, "import-retry").click()
    run.check("点「重试」→ 再向云端解析一次（结果同样失败，页面不卡）", wait_reparsed(page, "failed") and len(requests_of(cloud, "POST", r"/api/import/wechat/preview$")) == before + 1)

    # 全是中性交易
    run.check("只有中性交易 / 无效交易的微信文件 → 解析失败", preview_file(page, "neutral.csv", (b"\xef\xbb\xbf" + WECHAT_NEUTRAL_ONLY_TEXT.encode("utf-8")), expect="failed"))
    st = state(page)
    run.check("提示里说明跳过了多少条：（已跳过 2 条中性交易 / 无效交易）", st["error"] == "未解析出任何账单，请确认文件为微信导出的 CSV 账单（已跳过 2 条中性交易 / 无效交易）", str(st["error"]))

    # 文件太大：不读内容、不向云端提交；没有可重试的内容所以没有「重试」
    before = len(requests_of(cloud, "POST", r"/api/import/wechat/preview$"))
    pick_file(page, "huge.csv", b"x" * (10 * 1024 * 1024 + 1024))
    run.check("超过 10 MB 的文件 → 失败", poll(page, "() => (document.querySelector('[data-testid=\"import-error-text\"]')?.textContent || '').startsWith('文件太大')"))
    st = state(page)
    run.check(
        "提示「文件太大（10 MB），最多支持 10 MB」、没有向云端提交、没有「重试」（没有内容可重试）、文件名显示",
        st["error"] is not None and st["error"].startswith("文件太大（10") and st["error"].endswith("最多支持 10 MB") and len(requests_of(cloud, "POST", r"/api/import/wechat/preview$")) == before and not st["retry"] and st["fileName"] == "huge.csv",
        f"{st['error']} {st['retry']}",
    )
    run.check("没有可解析的内容：底部「解析预览」置灰（只能换文件），点不动也不会向云端提交", st["submit"]["disabled"] and st["submit"]["text"] == "解析预览", str(st["submit"]))
    try:
        tid(page, "import-submit").click(force=True, timeout=2000)
    except PlaywrightTimeoutError:
        pass
    time.sleep(0.5)
    run.check("强行点置灰的「解析预览」：云端没收到预览请求、仍是失败状态", len(requests_of(cloud, "POST", r"/api/import/wechat/preview$")) == before and state(page)["stage"] == "failed")

    # 编码：GB18030（无 BOM）与 UTF-16 LE（带 BOM），到云端的都是同一份文本
    run.check("GB18030（无 BOM）的微信文件 → 预览", preview_file(page, "wechat-gbk.csv", WECHAT_TEXT.encode("gb18030")) and state(page)["previewCount"] == "3")
    requests = requests_of(cloud, "POST", r"/api/import/wechat/preview$")
    run.check("GB18030 的文件被正确解码（云端收到的文本与原文一字不差）", requests[-1].body == {"CsvContent": WECHAT_TEXT})
    run.check("UTF-16 LE（带 BOM）的微信文件 → 预览", preview_file(page, "wechat-utf16.csv", b"\xff\xfe" + WECHAT_TEXT.encode("utf-16-le")) and state(page)["previewCount"] == "3")
    requests = requests_of(cloud, "POST", r"/api/import/wechat/preview$")
    run.check("UTF-16 的文件被正确解码", requests[-1].body == {"CsvContent": WECHAT_TEXT})

    # 云端 5xx / 业务错误：翻译成给用户看的提示，可重试
    cloud.fail("POST", r"/api/import/wechat/preview$", status=500, message="Npgsql: connection refused")
    run.check("云端 500 → 解析失败", preview_file(page, "wechat.csv", WECHAT_BYTES, expect="failed"))
    st = state(page)
    run.check("提示「解析失败：云端服务暂时不可用，请稍后重试」，不泄露内部信息，有「重试」", st["error"] == "解析失败：云端服务暂时不可用，请稍后重试" and st["retry"] and "Npgsql" not in (st["error"] or ""), str(st["error"]))
    tid(page, "import-retry").click()
    run.check("点「重试」（云端恢复）→ 预览", wait_stage(page, "preview") and state(page)["previewCount"] == "3")
    cloud.fail("POST", r"/api/import/wechat/preview$", status=400, message="CSV 内容不能为空")
    run.check("云端 400 业务错误 → 解析失败", preview_file(page, "wechat.csv", WECHAT_BYTES, expect="failed"))
    run.check("提示「解析失败：CSV 内容不能为空」（取响应里的消息）", state(page)["error"] == "解析失败：CSV 内容不能为空", str(state(page)["error"]))

    # 云端不可用：无法连接
    cloud.stop()
    try:
        # 连接被拒绝是瞬间的，「解析中」可能一闪而过——以提示文字（与上一条不同）为准
        read_file(page, "offline.csv", WECHAT_BYTES)
        tid(page, "import-submit").click()
        run.check("云端不可用 → 点「解析预览」→ 解析失败", wait_error(page, "解析失败：无法连接云端，请检查网络后重试"))
        st = state(page)
        run.check("提示「解析失败：无法连接云端，请检查网络后重试」，页面可操作（有重试、输入可用）", st["stage"] == "failed" and st["retry"] and st["inputDisabled"] is False, f"{st['stage']} {st['error']} {st['retry']} {st['inputDisabled']}")
    finally:
        try:
            cloud.start()
        except CloudPortBusy as exc:
            run.check("假云端恢复（后面的检查依赖它）", False, str(exc))
    tid(page, "import-submit").click()
    run.check("云端恢复后点底部「解析预览」（与「重试」等价）→ 预览", wait_stage(page, "preview") and state(page)["previewCount"] == "3")


def check_import_failures(c: Ctx) -> None:
    """H. 导入失败：预览与账本选择保留、可再试；部分失败 / 全部失败；导入前同步账本失败不拦截"""
    run, page, cloud, db = c.run, c.page, c.cloud, c.db
    # 现在在预览阶段（G 节最后一步）：先固定账本 A
    run.check("选账本 A", choose_ledger(page, c.ledger_a_name) and same_id(state(page)["ledgerId"], c.ledger_a))
    imports_before = len(cloud.imports())

    def submit_expecting_failure(label: str, expected_text: str, toast_text: str) -> None:
        """点「导入」，等页面出现这条失败提示（每次的提示文字都不同，不会被上一次残留的提示冒充），再核对「停在预览、什么都没丢」。"""
        tid(page, "import-submit").click()
        shown = poll(page, "(t) => document.querySelector('[data-testid=\"import-failure-text\"]')?.textContent.trim() === t", arg=expected_text, timeout=20000)
        st = state(page)
        run.check(
            label,
            shown and st["stage"] == "preview" and st["failureRole"] == "alert" and st["previewCount"] == "3" and same_id(st["ledgerId"], c.ledger_a) and not st["submit"]["disabled"] and st["submit"]["text"] == "导入 3 条账单" and len(cloud.imports()) == imports_before,
            f"{st['stage']} 提示={st['failure']} 账本={st['ledgerId']} 按钮={st['submit']}",
        )
        run.check(f"  └ Toast：{toast_text}", wait_toast(page, toast_text))
        wait_toasts_gone(page)

    # 403：云端明确拒绝——一定没写入，所以提示里没有「云端可能已收下」
    cloud.fail("POST", r"/api/import/wechat$", status=403, message="无权向该账本记账")
    submit_expecting_failure("403 被拒绝：停在预览，提示「无权限：无权向该账本记账」，预览与账本保留、按钮可再点", "无权限：无权向该账本记账", "无权限：无权向该账本记账")

    # 5xx：没有收到明确的响应 → 提示云端可能已收下部分账单
    cloud.fail("POST", r"/api/import/wechat$", status=500, message="Npgsql: timeout")
    uncertain = "云端可能已经收下了部分账单，请先到账单列表确认，再决定要不要重新导入"
    submit_expecting_failure("500：停在预览，提示带「云端可能已经收下了部分账单…」", f"导入失败：云端服务暂时不可用，请稍后重试。{uncertain}", "导入失败：云端服务暂时不可用，请稍后重试")
    run.check("提示里没有内部错误细节", "Npgsql" not in (state(page)["failure"] or ""))

    # 云端不可用
    cloud.stop()
    try:
        submit_expecting_failure("云端不可用：停在预览，提示「无法连接云端…」+ 云端可能已收下的说明", f"导入失败：无法连接云端，请检查网络后重试。{uncertain}", "导入失败：无法连接云端，请检查网络后重试")
    finally:
        try:
            cloud.start()
        except CloudPortBusy as exc:
            run.check("假云端恢复（后面的检查依赖它）", False, str(exc))

    # 部分失败 + 同步失败：同一次导入里第 2 条入库失败，拉取账单也失败
    cloud.fail_import_rows({2: "分类不存在"})
    cloud.fail("POST", r"/api/sync/pull$", status=500, message="boom")
    tid(page, "import-submit").click()
    run.check("再试一次（云端恢复）→ 结果", wait_stage(page, "done", 15000))
    st = state(page)
    result = st["result"]
    run.check("导入成功后失败提示消失、结果是 2 成功 1 失败", st["failure"] is None and (result["total"], result["success"], result["failed"]) == ("3", "2", "1"), str(result))
    run.check("结果标题「部分账单导入失败」、说明「成功导入 2 条账单到「账本」，失败 1 条」", result["title"] == "部分账单导入失败" and result["summary"] == f"成功导入 2 条账单到「{c.ledger_a_name}」，失败 1 条", f"{result['title']} / {result['summary']}")
    run.check("结果分组里有「失败 1 条」", result["failedRow"] is not None and result["failedRow"]["value"] == "1 条" and result["successRow"]["value"] == "2 条", str(result))
    run.check("失败详情：小标题「失败详情」、一条「第 2 条: 分类不存在」、没有「仅显示前…」", result["errorsGroup"] and result["errorsGroup"]["header"] == "失败详情" and result["errors"] == ["第 2 条: 分类不存在"] and not result["errorsGroup"]["footer"], str(result["errorsGroup"]))
    run.check("Toast（警告）：「微信账单导入完成：成功 2 条，失败 1 条」", wait_toast(page, "微信账单导入完成：成功 2 条，失败 1 条"))
    run.check("导入成功了、只是没拉到本地：「同步到本机」一行是未完成（failed）", poll(page, "() => document.querySelector('[data-testid=\"import-sync\"]')?.getAttribute('data-state') === 'failed'", timeout=15000))
    st = state(page)
    run.check("同步失败的说明：「导入成功，但拉取到本地失败：…；可稍后在“我的”页点击“立即同步”」，右值「未完成」", st["sync"]["value"] == "未完成" and st["sync"]["subtitle"].startswith("导入成功，但拉取到本地失败：") and st["sync"]["subtitle"].endswith("可稍后在“我的”页点击“立即同步”"), str(st["sync"]))
    run.check("同步失败时 Toast 也提示一次", wait_toast(page, "导入成功，但拉取到本地失败："))
    wait_toasts_gone(page)

    # 全部失败：没有成功就不触发同步
    run.check("继续导入 → 选文件 → 预览", continue_import(page) and preview_file(page, "wechat.csv", WECHAT_BYTES))
    pulls = len(requests_of(cloud, "POST", r"/api/sync/pull$"))
    cloud.fail_import_rows({1: "分类不存在", 2: "分类不存在", 3: "金额无效"})
    tid(page, "import-submit").click()
    run.check("全部入库失败 → 结果", wait_stage(page, "done", 15000))
    st = state(page)
    result = st["result"]
    run.check(
        "标题「没有账单导入成功」、说明「成功导入 0 条账单…，失败 3 条」、失败详情 3 条",
        result["title"] == "没有账单导入成功" and result["summary"] == f"成功导入 0 条账单到「{c.ledger_a_name}」，失败 3 条" and result["errors"] == ["第 1 条: 分类不存在", "第 2 条: 分类不存在", "第 3 条: 金额无效"],
        str(result),
    )
    run.check("Toast（警告）：「微信账单没有导入成功（失败 3 条）」", wait_toast(page, "微信账单没有导入成功（失败 3 条）"))
    time.sleep(0.8)
    run.check("一条都没导入成功：不显示「同步到本机」、也没有去拉取", state(page)["sync"] is None and len(requests_of(cloud, "POST", r"/api/sync/pull$")) == pulls, str(state(page)["sync"]))
    wait_toasts_gone(page)

    # 导入前同步账本失败不拦截（账本已经在云端）
    run.check("继续导入 → 选文件 → 预览", continue_import(page) and preview_file(page, "wechat.csv", WECHAT_BYTES))
    cloud.fail("POST", r"/api/sync/ledgers/push$", status=500, message="boom")
    tid(page, "import-submit").click()
    run.check("导入前同步账本失败（云端 500）：不拦截，导入照常成功", wait_stage(page, "done", 15000) and state(page)["result"]["success"] == "3")
    wait_toasts_gone(page)

    # 账本在云端不存在（刚在本机新建、同步又没成功）：云端 400「账本不存在」，提示说清楚
    ledger_c_name = unique_name("导入账本C")
    c.db.seed_ledger(c.user_id, ledger_c_name, sort_order=9)
    run.check("继续导入（重新进入页面读到新账本）", continue_import(page))
    leave_import(page)
    open_import(page)
    run.check("选文件 → 预览", preview_file(page, "wechat.csv", WECHAT_BYTES))
    run.check("选账本 C（只在本机、云端不认识）", choose_ledger(page, ledger_c_name))
    cloud.fail("POST", r"/api/sync/ledgers/push$", status=500, message="boom")
    tid(page, "import-submit").click()
    shown = poll(page, "() => document.querySelector('[data-testid=\"import-failure-text\"]')?.textContent.trim() === '导入失败：账本不存在'", timeout=20000)
    st = state(page)
    run.check(
        "云端 400「账本不存在」：提示「导入失败：账本不存在」（明确被拒绝：不带「可能已收下」）、停在预览、预览与账本选择保留",
        shown and st["stage"] == "preview" and st["previewCount"] == "3" and st["ledger"]["value"] == ledger_c_name,
        f"{st['failure']} {st['ledger']}",
    )
    wait_toasts_gone(page)
    tid(page, "import-submit").click()
    run.check("再试一次（这次同步账本成功，云端认得它了）→ 导入成功", wait_stage(page, "done", 15000) and state(page)["result"]["success"] == "3")
    run.check("账本 C 现在登记在云端了", ledger_known(cloud, db.ledger_id_by_name(c.user_id, ledger_c_name)))
    wait_toasts_gone(page)


def check_row_limit(c: Ctx) -> None:
    """I. 预览行数上限：130 条只显示前 100 条，汇总与导入针对全部"""
    run, page, cloud, db = c.run, c.page, c.cloud, c.db
    text = big_wechat_text(130)
    run.check("继续导入 → 选 130 条账单的文件 → 预览", continue_import(page) and preview_file(page, "big.csv", b"\xef\xbb\xbf" + text.encode("utf-8")))
    st = state(page)
    run.check("预览只渲染前 100 行，第 100 行是「商品100」", len(st["rows"]) == 100 and st["rows"][0]["title"] == "商品1" and st["rows"][-1]["title"] == "商品100", f"{len(st['rows'])} {st['rows'][-1]['title'] if st['rows'] else None}")
    run.check("汇总针对全部 130 条：data-count=130、支出 8515.00（1+…+130）", st["previewCount"] == "130" and st["expense"] == "8515.00" and st["caption"].startswith("共 130 笔账单"), f"{st['previewCount']} {st['expense']} {st['caption']}")
    run.check("列表下说明「仅显示前 100 条，其余 30 条会在导入时一并导入」", st["more"] == "仅显示前 100 条，其余 30 条会在导入时一并导入", str(st["more"]))
    run.check("主按钮「导入 130 条账单」", st["submit"]["text"] == "导入 130 条账单" and not st["submit"]["disabled"], str(st["submit"]))
    tid(page, "import-submit").click()
    run.check("导入 → 结果", wait_stage(page, "done", 30000))
    st = state(page)
    request = last_import_request(cloud)
    run.check(
        "云端收到全部 130 条（不是只有显示的 100 条），结果「成功 130 条」",
        request is not None and request.body.get("CsvContent") == text and len(cloud.imports("wechat")[-1]["bills"]) == 130 and (st["result"]["total"], st["result"]["success"]) == ("130", "130") and st["result"]["successRow"]["value"] == "130 条",
        str(st["result"]),
    )
    run.check("同步完成后本地库里也有这 130 条", poll(page, "() => document.querySelector('[data-testid=\"import-sync\"]')?.getAttribute('data-state') === 'synced'", timeout=30000) and len([b for b in db.bills_of_user(c.user_id) if str(b["Note"]).startswith("商品")]) == 130)
    wait_toasts_gone(page)


def run_main(run: TestRun, cloud: FakeCloud) -> None:
    db = LocalDb()
    db.verify_schema()
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        # 假云端用本地库里的预设分类 / 渠道 Id 还账单（客户端合并账单时外键才对得上）
        cloud.use_import_catalog(
            expense_category_id=db.preset_category_id(BILL_TYPE_EXPENSE),
            income_category_id=db.preset_category_id(BILL_TYPE_INCOME),
            channel_id=db.preset_channel_id(),
        )
        c = Ctx(run, page, db, cloud, info.phone, user_id)
        check_shell(c)
        check_source_switch(c)
        check_preview(c)
        check_ledger_required(c)
        check_import_success(c)
        check_second_ledger_and_alipay(c)
        check_parse_failures(c)
        check_import_failures(c)
        check_row_limit(c)
        run.check_clean(s.errors, "主会话无页面异常 / 控制台错误 / 失败请求")


def run_narrow(run: TestRun, cloud: FakeCloud) -> None:
    """J. 窄屏（320）：预览 / 失败 / 结果三个状态"""
    db = LocalDb()
    with session(viewport={"width": 320, "height": 640}) as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_name = unique_name("窄屏导入账本")
        db.seed_ledger(user_id, ledger_name)
        open_app(page, "/import")
        tid(page, "import-page").wait_for(state="visible")
        wait_animations_settled(page)

        st = state(page)
        nav = page.evaluate(NAV_JS)
        sub = st["submit"]
        run.check("320 宽：空闲态没有横向溢出、导航标题与返回按钮不重叠、主按钮在屏幕内且高度 ≥ 44", st["overflowX"] <= 0 and nav["back"]["r"] <= nav["title"]["l"] + 0.5 and sub["box"]["l"] >= 0 and sub["box"]["r"] <= 320 + 0.5 and sub["box"]["h"] >= 44, f"{st['overflowX']} {nav} {sub['box']}")
        run.check("320 宽：分段控件每段 ≥ 44 高、文件行盖满整行", all(sg["h"] >= 44 for sg in st["segments"]) and page.evaluate(HIT_FILE_INPUT_JS))

        run.check("320 宽：选文件 → 已选文件（等点「解析预览」）", read_file(page, "wechat.csv", WECHAT_BYTES))
        wait_animations_settled(page)
        st = state(page)
        run.check(
            "320 宽（已选文件）：没有横向溢出、文件行与底部主按钮都在屏幕内、主按钮「解析预览」点亮且高度 ≥ 44",
            st["overflowX"] <= 0 and st["fileRowBox"]["l"] >= 0 and st["fileRowBox"]["r"] <= 320 + 0.5 and st["submit"]["box"]["l"] >= 0 and st["submit"]["box"]["r"] <= 320 + 0.5
            and st["submit"]["box"]["h"] >= 44 and not st["submit"]["disabled"] and st["submit"]["text"] == "解析预览",
            f"{st['overflowX']} {st['fileRowBox']} {st['submit']}",
        )
        shot(page, "import-ready-320")
        slow_next_preview()
        run.check("320 宽：点「解析预览」→ 预览", click_parse(page))
        wait_animations_settled(page)
        st = state(page)
        run.check(
            "320 宽（预览）：没有横向溢出、预览行与账本行都在屏幕内、汇总三栏都在屏幕内",
            st["overflowX"] <= 0 and all(r["box"]["l"] >= 0 and r["box"]["r"] <= 320 + 0.5 for r in st["rows"]) and page.evaluate("() => { const r = document.querySelector('[data-testid=\"import-ledger-row\"]').getBoundingClientRect(); return r.left >= 0 && r.right <= 320.5; }")
            and page.evaluate("() => [...document.querySelectorAll('[data-testid=\"import-summary\"] .ft-kpi__cell')].every((n) => { const r = n.getBoundingClientRect(); return r.left >= 0 && r.right <= 320.5; })"),
            f"{st['overflowX']} {[r['box'] for r in st['rows']]}",
        )
        run.check("320 宽（预览）：每屏玻璃层 ≤ 3", page.evaluate(GLASS_LAYERS_JS) <= 3, str(page.evaluate(GLASS_LAYERS_JS)))
        shot(page, "import-preview-320")

        run.check("320 宽：换一个不是账单的文件 → 失败", preview_file(page, "notes.csv", NOT_A_BILL_TEXT.encode("utf-8"), expect="failed"))
        st = state(page)
        run.check(
            "320 宽（失败）：提示条与「重试」在屏幕内、没有横向溢出、重试点击区域 ≥ 44",
            st["errorBox"]["l"] >= 0 and st["errorBox"]["r"] <= 320 + 0.5 and st["retryBox"]["l"] >= 0 and st["retryBox"]["r"] <= 320 + 0.5 and st["retryBox"]["h"] >= 44 and st["overflowX"] <= 0,
            f"{st['errorBox']} {st['retryBox']}",
        )
        shot(page, "import-failed-320")

        run.check("320 宽：换回账单文件 → 预览 → 导入", preview_file(page, "wechat.csv", WECHAT_BYTES))
        tid(page, "import-submit").click()
        run.check("320 宽：导入 → 结果", wait_stage(page, "done", 15000))
        wait_animations_settled(page)
        st = state(page)
        run.check(
            "320 宽（结果）：没有横向溢出、结果行与同步行都在屏幕内、主按钮在屏幕内",
            st["overflowX"] <= 0 and page.evaluate("() => [...document.querySelectorAll('[data-testid=\"import-result-group\"] .ft-cell, [data-testid=\"import-sync\"]')].every((n) => { const r = n.getBoundingClientRect(); return r.left >= 0 && r.right <= 320.5; })")
            and st["submit"]["box"]["r"] <= 320 + 0.5,
            str(st["overflowX"]),
        )
        shot(page, "import-result-320")
        run.check_clean(s.errors, "窄屏会话无页面异常 / 控制台错误 / 失败请求")


def main() -> int:
    global CLOUD
    run = TestRun("import-page")
    require_server()
    try:
        cloud = FakeCloud().start()
    except CloudPortBusy as exc:
        print(f"[FAIL] {exc}\n请关掉占用该端口的进程，或用 E2E_CLOUD_PORT 与 server.ps1 -CloudPort 换一个端口。")
        return 1

    CLOUD = cloud
    try:
        run_main(run, cloud)
        run_narrow(run, cloud)
    finally:
        cloud.stop()

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
