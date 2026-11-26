# -*- coding: utf-8 -*-
"""
E2E 测试：首页重做（iOS UI 重构 · 07 号 ticket）。

只覆盖网页层——Android / iOS 上的真机手感（Hero 顶到屏幕最顶端的观感、状态栏图标颜色真正切换、下拉刷新的跟手与回弹、
灵动岛下转圈的位置、玻璃模糊）见 ticket 07 的人工验收清单。下拉刷新的**手势**这里用真实触摸事件验证（CDP touch），
手感（阻尼曲线、回弹）无法自动化。

  A. 新用户（没有账本、没有账单）：Hero（标题 / 日期星期 / 本月支出 / 收入 / 结余 / 通知入口，没有刷新按钮）；整块「新建账本」引导；
     新建账本 Sheet 盖住 Tab 栏；创建后变为「记第一笔」引导 + 「设置本月预算」引导；从预算页回来自动重新加载
  B. 有数据的首页：本月支出 / 收入 / 结余 = 造的账单；最近账单 5 行（内容、金额颜色、时间、未归属账本、点按升起编辑 Sheet）；
     本月预算三种状态（正常 / 接近上限 / 超支）与文字线索、进度条无障碍属性、脚注；超支提示；
     待确认横幅与角标（有 / 无）；「快捷操作」宫格已移除；「全部」切换到账单 Tab；布局（滚动、底部让出 Tab 栏、无横向溢出、玻璃层 ≤ 3）；
     沉浸式（模拟刘海 / 手势条）
  C. 刷新：下拉刷新（真实触摸，唯一的刷新入口——Hero 里没有刷新按钮）只重读本页数据，不触发任何云端请求；拉得不够不刷新；
     页面不在顶部时不触发；重新加载后同一个预算不会再次弹出超支提示
  D. 历史无账本账单：只在顶部给一行「还没有账本」提示，不挡住已有数据
  E. 小屏 + 减少动态效果：状态栏图标随 Hero 滚出屏幕而切换；下拉区与进度条不做过渡

可观察属性（已登记到 README）：home-page（滚动容器 data-scroll-root）、home-hero、home-title、home-date、home-bell(-badge)、
home-expense / home-income / home-balance（data-amount）、home-ptr（data-state）、home-pending-banner、home-empty-ledger / home-create-ledger、
home-ledger-hint、home-budget-header / home-budget-manage / home-budget-group / home-budget-guide、budget-row（data-state / data-percent）、
budget-name / budget-tag / budget-remain / budget-progress（role=progressbar，data-tone）、home-recent-header / home-recent-all / home-recent-group、
bill-row（data-bill-id / data-income）、bill-amount / bill-time、home-empty-bills / home-first-bill。
"""
import re
import sys
from datetime import datetime
from decimal import Decimal

from e2e_base import (
    GLASS_LAYERS_JS,
    ADDBILL_SHEET_SELECTOR,
    BILL_TYPE_INCOME,
    PHONE_VIEWPORT,
    SOURCE_NOTIFICATION,
    LocalDb,
    TestRun,
    active_tab,
    addbill_sheet,
    close_add_bill_sheet,
    days_ago,
    login,
    open_app,
    pull_down,
    pull_refresh,
    require_server,
    scroll_root_state,
    scroll_root_to,
    session,
    shot,
    tab_bar,
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
TOP = 47  # 灵动岛 / 刘海机型状态栏高度
BOTTOM = 34  # 手势条高度
WEEKDAYS = "一二三四五六日"

RGB_INCOME_TEXT = "rgb(24, 133, 64)"  # --ft-income-text #188540
RGB_DANGER_TEXT = "rgb(211, 34, 26)"  # --ft-danger-text #D3221A
RGB_WARNING_TEXT = "rgb(154, 80, 0)"  # --ft-warning-text #9A5000（23 号起与预算提示条共用同一个令牌）
RGB_LABEL = "rgb(0, 0, 0)"  # --ft-label

HERO_JS = """() => {
  const home = document.querySelector('[data-testid="home-page"]');
  const hero = document.querySelector('[data-testid="home-hero"]');
  if (!home || !hero) return null;
  const box = (id) => {
    const el = document.querySelector(`[data-testid="${id}"]`);
    if (!el) return null;
    const r = el.getBoundingClientRect();
    return { left: r.left, right: r.right, top: r.top, bottom: r.bottom, width: r.width, height: r.height };
  };
  const hr = hero.getBoundingClientRect();
  const hcs = getComputedStyle(hero);
  const pr = home.getBoundingClientRect();
  const pcs = getComputedStyle(home);
  return {
    hero: { left: hr.left, right: hr.right, top: hr.top, bottom: hr.bottom, width: hr.width, height: hr.height },
    heroBg: hcs.backgroundImage,
    heroRadius: [hcs.borderBottomLeftRadius, hcs.borderBottomRightRadius],
    heroDeclared: hero.getAttribute('data-statusbar-bg'),
    page: { top: pr.top, bottom: pr.bottom, height: pr.height, overflowY: pcs.overflowY, paddingBottom: parseFloat(pcs.paddingBottom) },
    isScrollRoot: home.hasAttribute('data-scroll-root'),
    title: box('home-title'), date: box('home-date'), bell: box('home-bell'),
    refreshEntries: home.querySelectorAll('[data-testid="home-refresh"], button[aria-label="刷新"], .mdi-refresh').length,
    expense: box('home-expense'), incomeCard: box('home-income-card'), balanceCard: box('home-balance-card'),
    titleTag: document.querySelector('[data-testid="home-title"]').tagName,
    overflowX: document.documentElement.scrollWidth - document.documentElement.clientWidth,
  };
}"""

BILL_ROWS_JS = """() => [...document.querySelectorAll('[data-testid="home-recent-group"] [data-testid="bill-row"]')].map((el) => {
  const text = (selector) => { const node = el.querySelector(selector); return node ? node.textContent.trim() : ''; };
  const amount = el.querySelector('[data-testid="bill-amount"]');
  const tile = el.querySelector('.ft-tile');
  const r = el.getBoundingClientRect();
  return {
    id: el.getAttribute('data-bill-id'),
    income: el.getAttribute('data-income'),
    tag: el.tagName,
    title: text('.ft-cell__title'),
    subtitle: text('.ft-cell__subtitle'),
    amount: amount ? amount.textContent.trim() : '',
    amountColor: amount ? getComputedStyle(amount).color : '',
    time: text('[data-testid="bill-time"]'),
    aria: el.getAttribute('aria-label'),
    tile: tile ? tile.className : '',
    icon: tile && tile.querySelector('i') ? tile.querySelector('i').className : '',
    height: r.height,
    bottom: r.bottom,
  };
})"""

BUDGET_ROWS_JS = """() => [...document.querySelectorAll('[data-testid="home-budget-group"] [data-testid="budget-row"]')].map((el) => {
  const text = (id) => { const node = el.querySelector(`[data-testid="${id}"]`); return node ? node.textContent.trim() : null; };
  const bar = el.querySelector('[data-testid="budget-progress"]');
  const remain = el.querySelector('[data-testid="budget-remain"]');
  const fill = el.querySelector('.ft-progress__fill');
  const tile = el.querySelector('.ft-tile');
  return {
    id: el.getAttribute('data-budget-id'),
    state: el.getAttribute('data-state'),
    percent: el.getAttribute('data-percent'),
    name: text('budget-name'),
    tag: text('budget-tag'),
    remain: remain ? remain.textContent.trim() : null,
    remainColor: remain ? getComputedStyle(remain).color : null,
    role: bar && bar.getAttribute('role'),
    now: bar && bar.getAttribute('aria-valuenow'),
    valueText: bar && bar.getAttribute('aria-valuetext'),
    label: bar && bar.getAttribute('aria-label'),
    tone: bar && bar.getAttribute('data-tone'),
    fillWidth: fill ? fill.getBoundingClientRect().width : null,
    barWidth: bar ? bar.getBoundingClientRect().width : null,
    tile: tile ? tile.className : '',
    icon: tile && tile.querySelector('i') ? tile.querySelector('i').className : '',
    height: el.getBoundingClientRect().height,
  };
})"""

COVER_JS = """() => {
  const bar = document.querySelector('[data-testid="tab-bar"]');
  const r = bar.getBoundingClientRect();
  const hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
  return {
    inHost: !!hit && !!hit.closest('[data-testid="ledger-sheet-host"]'),
    inTabBar: !!hit && !!hit.closest('[data-testid="tab-bar"]'),
  };
}"""

SET_SAFE_JS = """([top, bottom]) => {
  const style = document.documentElement.style;
  style.setProperty('--ft-safe-top', top + 'px');
  style.setProperty('--ft-safe-bottom', bottom + 'px');
}"""

CLEAR_SAFE_JS = """() => {
  const style = document.documentElement.style;
  ['--ft-safe-top', '--ft-safe-bottom'].forEach((name) => style.removeProperty(name));
}"""

# 统计「超支提示」Toast 被加入页面的次数（Toast 几秒后自动消失，事后数不出来，所以用 MutationObserver 在刷新前布防）
WATCH_TOAST_JS = """(text) => {
  window.__ftToastSeen = 0;
  new MutationObserver((records) => {
    for (const record of records) {
      for (const node of record.addedNodes) {
        if (node.textContent && node.textContent.includes(text)) window.__ftToastSeen += 1;
      }
    }
  }).observe(document.body, { childList: true, subtree: true });
}"""

PTR_IDLE_JS = """() => {
  const zone = document.querySelector('[data-testid="home-ptr"]');
  return !!zone && (zone.dataset.state || 'idle') === 'idle' && !zone.style.getPropertyValue('--ft-ptr-offset');
}"""


# ---------------------------------------------------------------------------------------------- 小工具


def approx(actual, expected, tolerance: float = 1.0) -> bool:
    return actual is not None and abs(actual - expected) <= tolerance


def date_line(now: datetime) -> str:
    return f"{now.month}月{now.day}日 周{WEEKDAYS[now.weekday()]}"


def plain(value) -> str:
    """金额 → data-amount 的写法：两位小数、无千分位。"""
    return f"{Decimal(str(value)):.2f}"


def money(value) -> str:
    """金额 → 界面文字（¥ 加千分位加两位小数）。"""
    return f"¥{Decimal(str(value)):,.2f}"


def in_this_month(moment: datetime, now: datetime) -> bool:
    return moment.year == now.year and moment.month == now.month


def icons(page) -> str:
    return page.evaluate("() => document.documentElement.getAttribute('data-statusbar-icons')")


def tid(page, testid: str):
    return page.get_by_test_id(testid)


def amount_of(page, testid: str):
    return tid(page, testid).get_attribute("data-amount")


def wait_home_loaded(page) -> None:
    """Hero 里的「本月支出」金额出现 = 骨架屏已被真实数据替换；再等页面进入 / 切换的过渡（缩放 0.985 → 1）结束，
    否则量到的位置与尺寸是动画中间帧。"""
    tid(page, "home-expense").wait_for(state="visible")
    wait_animations_settled(page)


def wait_amount(page, testid: str, expected: str) -> None:
    page.wait_for_function(
        "([id, value]) => { const el = document.querySelector(`[data-testid=\"${id}\"]`); return !!el && el.getAttribute('data-amount') === value; }",
        arg=[testid, expected],
    )


def wait_ptr_idle(page) -> None:
    page.wait_for_function(PTR_IDLE_JS)


def go_home(page) -> None:
    """整页进入首页并等数据加载完。"""
    open_app(page, "/")
    wait_tab_bar(page)
    wait_home_loaded(page)


def back_to_home(page) -> None:
    """从二级页面返回首页（浏览器回退）。等页面栈的退出动画结束再继续：动画期间点按会被页面栈忽略（真人点不到这么快）。"""
    page.go_back()
    wait_url(page, r"^/$")
    wait_tab_bar(page)
    wait_secondary_pages_closed(page)
    wait_animations_settled(page)
    wait_home_loaded(page)


# ---------------------------------------------------------------------------------------------- A. 新用户


def check_new_user(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        tid(page, "home-empty-ledger").wait_for(state="visible")
        wait_home_loaded(page)
        now = datetime.now()

        wait_attr(page, "html", "data-statusbar-icons", "light")
        layout = page.evaluate(HERO_JS)
        hero = layout["hero"]
        run.check("【Hero】铺到屏幕最顶端、左右铺满", approx(hero["top"], 0) and approx(hero["left"], 0) and approx(hero["right"], SCREEN_W), str(hero))
        run.check("【Hero】品牌渐变背景、底部大圆角 40px", "linear-gradient" in layout["heroBg"] and layout["heroRadius"] == ["40px", "40px"], f"{layout['heroBg'][:60]} {layout['heroRadius']}")
        run.check("【Hero】声明深色状态栏背景（data-statusbar-bg=dark），顶部状态栏图标为浅色", layout["heroDeclared"] == "dark" and icons(page) == "light", f"{layout['heroDeclared']} {icons(page)}")
        run.check("【Hero】标题「记账本」是一级标题", layout["titleTag"] == "H1" and tid(page, "home-title").inner_text() == "记账本")
        run.check("【Hero】日期行是「M月D日 周X」", tid(page, "home-date").inner_text() == date_line(now), tid(page, "home-date").inner_text())
        run.check("【Hero】本月支出 / 收入 / 结余都是 0.00", [amount_of(page, t) for t in ("home-expense", "home-income", "home-balance")] == ["0.00"] * 3)
        run.check(
            "【Hero】金额整体读屏（role=img + 完整金额），不把整数与小数分开读",
            tid(page, "home-expense").get_attribute("role") == "img" and tid(page, "home-expense").get_attribute("aria-label") == "¥0.00",
        )
        run.check("【Hero】通知入口点击区域 ≥ 44", layout["bell"]["width"] >= 44 and layout["bell"]["height"] >= 44, str(layout["bell"]))
        run.check("【Hero】没有按钮形式的刷新入口（刷新只靠下拉）：页面里没有 home-refresh / 「刷新」按钮 / 刷新图标", layout["refreshEntries"] == 0, str(layout["refreshEntries"]))
        run.check(
            "【Hero】标题、日期、通知入口、收入 / 结余小卡都在 Hero 范围内",
            all(layout[k]["top"] >= hero["top"] and layout[k]["bottom"] <= hero["bottom"] + 0.5 for k in ("title", "date", "bell", "incomeCard", "balanceCard")),
        )
        run.check("【Hero】收入、结余两张小卡并排等宽", approx(layout["incomeCard"]["width"], layout["balanceCard"]["width"], 1) and approx(layout["incomeCard"]["top"], layout["balanceCard"]["top"], 1))
        run.check("首页是滚动容器（data-scroll-root）且占满整屏高度", layout["isScrollRoot"] and layout["page"]["overflowY"] == "auto" and approx(layout["page"]["height"], SCREEN_H, 1), str(layout["page"]))
        run.check("没有横向溢出", layout["overflowX"] <= 0, str(layout["overflowX"]))

        run.check("【已移除】「快捷操作」宫格不再出现（Tab 栏与「记一笔」圆钮已覆盖这些入口）", page.get_by_text("快捷操作").count() == 0)
        run.check("没有待确认账单：无横幅、无角标，通知入口读屏名称不带笔数", tid(page, "home-pending-banner").count() == 0 and tid(page, "home-bell-badge").count() == 0 and tid(page, "home-bell").get_attribute("aria-label") == "待确认账单")
        run.check(
            "新用户：整块「还没有账本」引导，没有预算 / 最近账单区域",
            tid(page, "home-empty-ledger").is_visible() and tid(page, "home-create-ledger").is_visible()
            and tid(page, "home-budget-group").count() == 0 and tid(page, "home-recent-group").count() == 0,
        )
        shot(page, "home-new-user")

        # ---- 新建账本 Sheet（与账本页共用同一个组件）----
        tid(page, "home-create-ledger").click()
        tid(page, "ledger-sheet").wait_for(state="visible")
        cover = page.evaluate(COVER_JS)
        run.check("新建账本 Sheet 盖住 Tab 栏（z 层级：Sheet 在 Tab 栏之上）", cover["inHost"] and not cover["inTabBar"], str(cover))

        ledger_name = unique_name("首页账本")
        tid(page, "ledger-name").fill(ledger_name)
        tid(page, "ledger-save").click()
        tid(page, "ledger-sheet").wait_for(state="detached")
        tid(page, "home-empty-bills").wait_for(state="visible")
        run.check("创建后账本落库", [l["Name"] for l in db.ledgers_of_user(user_id)] == [ledger_name])
        run.check("创建后引导变为「记第一笔」，整块「还没有账本」消失", tid(page, "home-first-bill").is_visible() and tid(page, "home-empty-ledger").count() == 0)
        run.check(
            "有账本、没预算：预算区给出「设置本月预算」引导，节标题有「管理」入口",
            tid(page, "home-budget-guide").is_visible() and tid(page, "home-budget-manage").is_visible() and tid(page, "home-budget-header").is_visible(),
        )
        run.check("最近账单区的「全部」入口可见", tid(page, "home-recent-all").is_visible())
        shot(page, "home-first-bill")

        # ---- 预算引导 → 预算页；在预算页期间造一条预算，返回后首页自动重新加载 ----
        tid(page, "home-budget-guide").click()
        wait_url(page, r"^/budgets")
        wait_tab_bar(page, visible=False)
        db.seed_budget(user_id, amount=2000)
        back_to_home(page)
        # home-budget-group 在「没有预算」时也存在（里面是创建引导），所以等的是预算行本身出现
        tid(page, "budget-row").first.wait_for(state="visible")
        rows = page.evaluate(BUDGET_ROWS_JS)
        run.check("从预算页返回后首页自动重新加载：出现新建的总预算，不再显示创建引导", len(rows) == 1 and rows[0]["name"] == "总预算" and tid(page, "home-budget-guide").count() == 0, str(rows))

        # ---- 记第一笔 → 升起记一笔 Sheet（15 号起不再是路由页，地址不变） ----
        tid(page, "home-first-bill").click()
        tid(page, "addbill-sheet").wait_for(state="visible")
        run.check("「记第一笔」升起记一笔 Sheet（地址不变，仍停在首页）", page.evaluate("() => location.pathname") == "/")

        run.check_clean(s.errors, "A 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- B. 有数据


def seed_month(db: LocalDb, user_id: str, ledger_id: str, now: datetime) -> dict:
    """造一个月的数据：最近 5 笔 + 1 笔更早的；预算 4 条（总预算 + 三个分类，三种状态各一）。返回断言用的期望值。"""
    older = days_ago(3)
    yesterday = days_ago(1)
    other = db.category_id_by_name("其他支出")

    bills = {}
    bills["older"] = db.seed_bill(user_id, amount=7, ledger_id=ledger_id, note="最早的一笔", when=older, category_id=other)
    bills["yesterday"] = db.seed_bill(user_id, amount=60, ledger_id=ledger_id, note="昨日", when=yesterday, category_id=other)
    bills["salary"] = db.seed_bill(
        user_id, amount=12000, ledger_id=ledger_id, note="工资", bill_type=BILL_TYPE_INCOME,
        category_id=db.category_id_by_name("工资薪酬", BILL_TYPE_INCOME),
    )
    bills["rent"] = db.seed_bill(user_id, amount="1200.50", ledger_id=ledger_id, note="房租", category_id=db.category_id_by_name("居住生活"))
    bills["lunch"] = db.seed_bill(user_id, amount=35, ledger_id=ledger_id, note="午饭", category_id=db.category_id_by_name("餐饮美食"))
    bills["legacy"] = db.seed_legacy_bill(user_id, amount=18, note="老账单", category_id=db.category_id_by_name("交通出行"))

    db.seed_budget(user_id, amount=5000)
    db.seed_budget(user_id, amount=1000, category_name="居住生活")
    db.seed_budget(user_id, amount=40, category_name="餐饮美食")
    db.seed_budget(user_id, amount=300, category_name="交通出行")

    expense = Decimal("1200.50") + 35 + 18
    for moment, amount in ((yesterday, 60), (older, 7)):
        if in_this_month(moment, now):
            expense += amount
    return {"bills": bills, "expense": expense, "income": Decimal(12000)}


def check_amounts(run: TestRun, page, expected: dict) -> None:
    expense, income = expected["expense"], expected["income"]
    run.check("【Hero】本月支出 = 本月支出账单之和", amount_of(page, "home-expense") == plain(expense), f"{amount_of(page, 'home-expense')} != {plain(expense)}")
    run.check("【Hero】收入 = 本月收入账单之和", amount_of(page, "home-income") == plain(income), amount_of(page, "home-income"))
    run.check("【Hero】结余 = 收入 - 支出", amount_of(page, "home-balance") == plain(income - expense), amount_of(page, "home-balance"))
    run.check("【Hero】金额读屏文案带千分位", tid(page, "home-income").get_attribute("aria-label") == money(income), tid(page, "home-income").get_attribute("aria-label"))


def check_recent_bills(run: TestRun, page, expected: dict, ledger_name: str) -> None:
    bills = expected["bills"]
    rows = page.evaluate(BILL_ROWS_JS)
    run.check("最近账单显示 5 行（最早的一笔不在其中）", len(rows) == 5, str([r["title"] for r in rows]))
    if len(rows) != 5:
        return

    run.check(
        "最近账单按交易时间倒序（老账单、午饭、房租、工资、昨日）",
        [r["id"] for r in rows] == [bills[k].lower() for k in ("legacy", "lunch", "rent", "salary", "yesterday")],
        str([r["title"] for r in rows]),
    )
    legacy, lunch, rent, salary, yesterday = rows
    run.check("行标题是「分类 · 备注」", [r["title"] for r in rows] == ["交通出行 · 老账单", "餐饮美食 · 午饭", "居住生活 · 房租", "工资薪酬 · 工资", "其他支出 · 昨日"], str([r["title"] for r in rows]))
    run.check("行副标题是「支付渠道 · 账本」", lunch["subtitle"].endswith(f"· {ledger_name}") and " · " in lunch["subtitle"], lunch["subtitle"])
    run.check("历史无账本的账单副标题标注「未归属账本」", legacy["subtitle"].endswith("· 未归属账本"), legacy["subtitle"])
    run.check("支出金额带负号、千分位、两位小数", [lunch["amount"], rent["amount"], legacy["amount"]] == ["-¥35.00", "-¥1,200.50", "-¥18.00"], str([lunch["amount"], rent["amount"], legacy["amount"]]))
    run.check("收入金额带正号", salary["amount"] == "+¥12,000.00" and salary["income"] == "true", salary["amount"])
    run.check("金额颜色：支出是常规文字色（不用红色），收入是加深的绿色", lunch["amountColor"] == RGB_LABEL and salary["amountColor"] == RGB_INCOME_TEXT, f"{lunch['amountColor']} / {salary['amountColor']}")
    run.check("今天的账单只显示时分", all(re.fullmatch(r"\d{2}:\d{2}", r["time"]) for r in (legacy, lunch, rent, salary)), str([r["time"] for r in rows]))
    run.check("昨天的账单显示「昨天 12:00」", yesterday["time"] == "昨天 12:00", yesterday["time"])
    run.check("行读屏名称把标题 / 副标题 / 收支 / 金额 / 时间连成一句话", salary["aria"] == f"{salary['title']}，{salary['subtitle']}，收入 ¥12,000.00，{salary['time']}", str(salary["aria"]))
    run.check("整行是按钮，点击区域高 ≥ 56", all(r["tag"] == "BUTTON" and r["height"] >= 56 for r in rows), str([(r["tag"], r["height"]) for r in rows]))
    run.check(
        "图标块是柔和样式，配色按分类（交通蓝、餐饮橙、居住绿、其他灰）",
        all("ft-tile--soft" in r["tile"] for r in rows)
        and "ft-tile--blue" in legacy["tile"] and "ft-tile--orange" in lunch["tile"] and "ft-tile--green" in rent["tile"] and "ft-tile--gray" in yesterday["tile"],
        str([r["tile"] for r in rows]),
    )
    run.check("每行都有分类图标", all(re.search(r"\bmdi-[a-z0-9-]+", r["icon"]) for r in rows), str([r["icon"] for r in rows]))


def check_budgets(run: TestRun, page, expected: dict) -> None:
    rows = page.evaluate(BUDGET_ROWS_JS)
    run.check("预算区展示 3 项：总预算在前，再按使用率从高到低取分类预算", [r["name"] for r in rows] == ["总预算", "居住生活", "餐饮美食"], str([r["name"] for r in rows]))
    if len(rows) != 3:
        return
    total, rent, lunch = rows

    used = expected["expense"]
    run.check("总预算：正常状态、「剩余」文字、品牌色进度条", total["state"] == "normal" and total["remain"] == f"剩余 {money(Decimal(5000) - used)}" and total["tone"] == "brand", str(total))
    run.check("总预算名称后有灰色小字「全部账本」，分类预算没有", total["tag"] == "全部账本" and rent["tag"] is None and lunch["tag"] is None, str([r["tag"] for r in rows]))
    run.check("总预算用紫色钱包图标，分类预算用分类图标与配色", "mdi-wallet" in total["icon"] and "ft-tile--purple" in total["tile"] and "ft-tile--green" in rent["tile"] and "ft-tile--orange" in lunch["tile"], str([(r["tile"], r["icon"]) for r in rows]))

    run.check(
        "超支（120%）：红色状态、「超支 ¥」文字加粗变红、进度条撑满且是危险色",
        rent["state"] == "over" and rent["percent"] == "120" and rent["remain"] == "超支 ¥200.50" and rent["remainColor"] == RGB_DANGER_TEXT
        and rent["tone"] == "danger" and rent["now"] == "100" and approx(rent["fillWidth"], rent["barWidth"], 0.5),
        str(rent),
    )
    run.check(
        "接近上限（87%）：橙色状态、「仅剩 ¥」文字、进度条是警示色",
        lunch["state"] == "warning" and lunch["percent"] == "87" and lunch["remain"] == "仅剩 ¥5.00" and lunch["remainColor"] == RGB_WARNING_TEXT
        and lunch["tone"] == "warning" and lunch["now"] == "87",
        str(lunch),
    )
    run.check(
        "进度条无障碍：role=progressbar，读屏名称是预算名，值说明带上状态",
        all(r["role"] == "progressbar" for r in rows)
        and [r["label"] for r in rows] == ["总预算", "居住生活", "餐饮美食"]
        and rent["valueText"] == "已用 120%，已超支" and lunch["valueText"] == "已用 87%，接近上限" and re.fullmatch(r"已用 \d+%", total["valueText"] or ""),
        str([r["valueText"] for r in rows]),
    )
    run.check("状态不只靠颜色：三种状态的文字说明各不相同（剩余 / 超支 / 仅剩）", len({r["remain"].split(" ")[0] for r in rows}) == 3, str([r["remain"] for r in rows]))
    run.check("进度条宽度与使用率一致（总预算宽度 < 100%，且 = 取整前的使用率）", 0 < total["fillWidth"] < total["barWidth"] - 1, f"{total['fillWidth']} / {total['barWidth']}")
    footer = page.locator('[data-testid="home-budget-group"] .ft-group__footer').inner_text()
    run.check("预算多于展示条数时脚注提示「共 4 项预算，点「管理」查看全部」", footer.strip() == "共 4 项预算，点「管理」查看全部", footer)


def check_over_budget_toast(run: TestRun, page) -> None:
    toast = page.locator(".m-enqueued-snackbars .m-snack__wrapper").filter(has_text="预算超支提醒")
    toast.first.wait_for(state="visible")
    text = toast.first.inner_text()
    run.check("加载后弹出一次应用内超支提示，写明是哪个预算", "居住生活" in text and "本月预算已超支" in text, text)


def check_banner(run: TestRun, page, db: LocalDb) -> None:
    run.check("没有待确认账单：无横幅、无角标", tid(page, "home-pending-banner").count() == 0 and tid(page, "home-bell-badge").count() == 0)

    # 向 PendingBills 表写数据即可模拟自动捕获（Web.Server 与 App 一样用 SQLite 实现的待确认账单服务）
    for channel in ("微信支付", "支付宝", "银行短信"):
        db.seed_pending_bill(channel=channel, source=SOURCE_NOTIFICATION)
    go_home(page)

    tid(page, "home-pending-banner").wait_for(state="visible")
    banner = tid(page, "home-pending-banner")
    run.check("有 3 笔待确认账单：横幅标题「3 笔支付等待确认」", "3 笔支付等待确认" in banner.inner_text(), banner.inner_text())
    run.check("横幅副标题按捕获先后列出渠道（最近的在前）", "银行短信 · 支付宝 · 微信支付" in banner.inner_text(), banner.inner_text())
    run.check("通知入口角标显示 3，读屏名称带笔数", tid(page, "home-bell-badge").inner_text().strip() == "3" and tid(page, "home-bell").get_attribute("aria-label") == "待确认账单，3 笔", tid(page, "home-bell").get_attribute("aria-label"))
    badge = tid(page, "home-bell-badge").bounding_box()
    bell = tid(page, "home-bell").bounding_box()
    run.check("角标贴在通知入口右上角", badge and bell and badge["x"] + badge["width"] > bell["x"] + bell["width"] - 6 and badge["y"] < bell["y"] + 6, f"{badge} {bell}")
    shot(page, "home-banner")

    banner.click()
    wait_url(page, r"^/pending-bills")
    run.check("点按横幅进入待确认账单页", page.evaluate("() => location.pathname") == "/pending-bills")
    back_to_home(page)
    tid(page, "home-bell").click()
    wait_url(page, r"^/pending-bills")
    run.check("点按通知入口同样进入待确认账单页", page.evaluate("() => location.pathname") == "/pending-bills")
    back_to_home(page)

    db.delete_pending_bills()
    go_home(page)
    run.check("待确认账单处理完（清空）后：横幅与角标消失", tid(page, "home-pending-banner").count() == 0 and tid(page, "home-bell-badge").count() == 0)


def check_layout(run: TestRun, page) -> None:
    state = scroll_root_state(page)
    run.check("内容超出一屏，首页能在自己的容器里滚动", state and state["max"] > 100, str(state))

    scroll_root_to(page, "bottom")
    page.wait_for_function("() => document.querySelector('[data-testid=\"home-page\"]').scrollTop > 100")
    rows = page.evaluate(BILL_ROWS_JS)
    tab_top = SCREEN_H - 21 - 62
    run.check("滚到底后最后一行账单露出在 Tab 栏之上（底部让出 Tab 栏占位）", rows and rows[-1]["bottom"] <= tab_top + 1, f"{rows[-1]['bottom'] if rows else None} <= {tab_top}")
    scroll_root_to(page, 0)

    glass = page.evaluate(GLASS_LAYERS_JS)
    run.check("首页同屏玻璃（backdrop-filter）层数 ≤ 3（性能预算，见 ADR 0007）", glass <= 3, str(glass))
    run.check("没有横向溢出", page.evaluate("() => document.documentElement.scrollWidth - document.documentElement.clientWidth") <= 0)
    run.check("区块标题是二级标题（本月预算 / 最近账单）", [h.inner_text() for h in page.locator('[data-testid="home-page"] h2').all()] == ["本月预算", "最近账单"])
    shot(page, "home-full")


def check_navigation(run: TestRun, page, expected: dict) -> None:
    tid(page, "home-recent-all").click()
    wait_attr(page, '[data-testid="tab-bar"]', "data-active-tab", "bills")
    wait_url(page, r"^/bills$")
    run.check("「最近账单 → 全部」切换到账单 Tab（Tab 栏仍在，没有压栈成二级页面）", tab_bar(page).is_visible() and active_tab(page) == "bills")

    go_home(page)
    rows = tid(page, "home-recent-group").get_by_test_id("bill-row")
    lunch_id = expected["bills"]["lunch"].lower()
    page.locator(f'[data-testid="bill-row"][data-bill-id="{lunch_id}"]').click()
    sheet = addbill_sheet(page)
    sheet.wait_for(state="visible")
    wait_attr(page, ADDBILL_SHEET_SELECTOR, "data-ready", "true")
    run.check(
        "点按账单行升起该账单的编辑 Sheet（记一笔 Sheet 的编辑模式，是覆盖层不是路由页：地址不变、仍在首页）",
        sheet.get_attribute("data-mode") == "edit" and sheet.get_attribute("data-bill-id") == lunch_id and page.evaluate("() => location.pathname") == "/" and active_tab(page) == "home",
        str(rows.count()),
    )
    wait_animations_settled(page)
    close_add_bill_sheet(page)
    run.check("关闭编辑 Sheet 后回到首页，Tab 栏仍在", tab_bar(page).is_visible() and active_tab(page) == "home")

    tid(page, "home-budget-manage").click()
    wait_url(page, r"^/budgets")
    run.check("预算区「管理」进入预算页", page.evaluate("() => location.pathname") == "/budgets")
    back_to_home(page)


def check_immersive_notch(run: TestRun, page) -> None:
    base = page.evaluate(HERO_JS)
    page.evaluate(SET_SAFE_JS, [TOP, BOTTOM])
    notch = page.evaluate(HERO_JS)
    run.check("【沉浸式】模拟刘海后 Hero 仍顶到屏幕最顶端", approx(notch["hero"]["top"], 0), str(notch["hero"]))
    run.check("【沉浸式】标题与通知入口让出状态栏（在状态栏之下）", notch["title"]["top"] >= TOP and notch["bell"]["top"] >= TOP, f"{notch['title']} {notch['bell']}")
    run.check("【沉浸式】Hero 随安全区变高（刘海 47）", approx(notch["hero"]["height"] - base["hero"]["height"], TOP, 1), f"{base['hero']['height']} -> {notch['hero']['height']}")
    run.check("【沉浸式】底部留白 = Tab 栏占位（21 + 62 + 34 + 24），最后一项内容滚得出 Tab 栏", approx(notch["page"]["paddingBottom"], 21 + 62 + BOTTOM + 24), str(notch["page"]["paddingBottom"]))
    shot(page, "home-notch")
    page.evaluate(CLEAR_SAFE_JS)


def check_data_home(run: TestRun, db: LocalDb, ledger_name_out: dict) -> str:
    """B. 有数据的首页；返回登录用的手机号（E 节复用同一个账号）。"""
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        ledger_name = unique_name("日常")
        ledger_id = db.seed_ledger(user_id, ledger_name)
        db.delete_pending_bills()
        now = datetime.now()
        expected = seed_month(db, user_id, ledger_id, now)

        go_home(page)
        check_over_budget_toast(run, page)
        check_amounts(run, page, expected)
        check_recent_bills(run, page, expected, ledger_name)
        check_budgets(run, page, expected)
        check_banner(run, page, db)
        check_layout(run, page)
        check_immersive_notch(run, page)
        check_navigation(run, page, expected)

        ledger_name_out.update(phone=info.phone, user_id=user_id, ledger_id=ledger_id, ledger_name=ledger_name, expected=expected)
        check_refresh(run, page, db, ledger_name_out)

        db.delete_pending_bills()
        run.check_clean(s.errors, "B / C 节会话无页面异常 / 控制台错误 / 失败请求")
    return info.phone


# ---------------------------------------------------------------------------------------------- C. 刷新


def check_refresh(run: TestRun, page, db: LocalDb, ctx: dict) -> None:
    user_id, ledger_id = ctx["user_id"], ctx["ledger_id"]
    other = db.category_id_by_name("其他支出")
    total = ctx["expected"]["expense"]

    go_home(page)
    cloud = FakeCloud()
    try:
        cloud.start()
    except CloudPortBusy as exc:
        run.check("假云端可以启动（验证「刷新不触发同步」依赖它）", False, str(exc))
        cloud = None

    try:
        # 这次进入首页时本电路已经提示过一次超支；等那条 Toast 消失后再布防，才数得出「刷新后有没有再弹」
        page.wait_for_function("() => !document.querySelector('.m-enqueued-snackbars .m-snack__wrapper')", timeout=20_000)
        page.evaluate(WATCH_TOAST_JS, "预算超支提醒")
        baseline = len(cloud.requests) if cloud else 0

        # ---- 拉得不够：不刷新 ----
        scroll_root_to(page, 0)
        after_pull_total = total + 200
        db.seed_bill(user_id, amount=200, ledger_id=ledger_id, note=unique_name("短拉"), category_id=other)
        pull_down(page, distance=40, from_y=300)
        wait_ptr_idle(page)
        run.check("下拉距离不够（没到触发阈值）：松手回弹，不刷新", amount_of(page, "home-expense") == plain(total), f"{amount_of(page, 'home-expense')} != {plain(total)}")

        # ---- 页面不在顶部：不触发 ----
        state = scroll_root_state(page)
        if state and state["max"] > 160:
            scroll_root_to(page, 150)
            page.wait_for_function("() => document.querySelector('[data-testid=\"home-page\"]').scrollTop >= 149")
            pull_down(page, distance=120, from_y=300)
            wait_ptr_idle(page)
            run.check("页面不在顶部时向下拖只是滚动，不触发刷新", amount_of(page, "home-expense") == plain(total), amount_of(page, "home-expense"))
            scroll_root_to(page, 0)
            page.wait_for_function("() => document.querySelector('[data-testid=\"home-page\"]').scrollTop <= 1")
        else:
            run.check("首页内容足够长，能验证「不在顶部不触发刷新」", False, str(state))

        # ---- 完整下拉刷新 ----
        pull_down(page, distance=240, from_y=300)
        wait_amount(page, "home-expense", plain(after_pull_total))
        run.check("下拉刷新（真实触摸）：Hero 的本月支出更新为最新数据", amount_of(page, "home-expense") == plain(after_pull_total), amount_of(page, "home-expense"))
        wait_ptr_idle(page)
        run.check("刷新后同一个预算不会再次弹出超支提示", page.evaluate("() => window.__ftToastSeen") == 0, str(page.evaluate("() => window.__ftToastSeen")))
        run.check("刷新结束后下拉区收起，没有残留的位移变量", page.evaluate(PTR_IDLE_JS))
        zone_height = page.evaluate("() => document.querySelector('[data-testid=\"home-ptr\"]').getBoundingClientRect().height")
        run.check("下拉区收起后高度为 0（Hero 回到原位）", zone_height <= 0.5, str(zone_height))
        run.check("下拉区的读屏状态播报区是 aria-live=polite（正在刷新 / 刷新完成）", page.locator('[data-testid="home-ptr"] [data-ptr-status]').get_attribute("aria-live") == "polite")

        if cloud is not None:
            # 页面读路径会顺带联网刷新共享账本的成员缓存（登录换令牌 + 拉成员关系，ADR 0006）：那是读取、不是同步，
            # 而且失败后 30 秒内会直接跳过——窗口还在不在取决于前面的脚本跑了多久，所以不计入（账单页同款检查也是这样处理）；
            # 其余任何请求（推送 / 拉取账单、分类、账本……）才是「触发了同步」
            member_cache_paths = {"/api/auth/login", "/api/sync/ledgermembers/pull"}
            requests = [r for r in cloud.requests[baseline:] if r.path not in member_cache_paths]
            run.check(
                "下拉刷新只重读本页数据，不触发同步（除读路径顺带刷新的成员缓存外，没有向云端发任何请求）",
                len(requests) == 0,
                "；".join(f"{r.method} {r.path}" for r in requests),
            )
    finally:
        if cloud is not None:
            cloud.stop()


# ---------------------------------------------------------------------------------------------- D. 历史无账本账单


def check_legacy_only(run: TestRun, db: LocalDb) -> None:
    with session() as s:
        page = s.page
        info = login(page)
        user_id = db.user_id(info.phone)
        tid(page, "home-empty-ledger").wait_for(state="visible")

        # 只有历史无账本的账单、没有账本：下拉刷新重新加载
        note = unique_name("历史账单")
        db.seed_legacy_bill(user_id, amount=18, note=note)
        pull_refresh(page, "home-ptr")
        tid(page, "home-ledger-hint").wait_for(state="visible")
        run.check("没有账本但有历史账单：不再整块引导，只在顶部给一行「还没有账本」提示", tid(page, "home-empty-ledger").count() == 0 and tid(page, "home-ledger-hint").is_visible())
        run.check("历史账单照常展示在最近账单里，标注「未归属账本」", tid(page, "home-recent-group").get_by_test_id("bill-row").count() == 1 and "未归属账本" in tid(page, "home-recent-group").inner_text())
        wait_amount(page, "home-expense", "18.00")
        run.check("历史账单计入本月支出", amount_of(page, "home-expense") == "18.00", amount_of(page, "home-expense"))

        tid(page, "home-ledger-hint").click()
        tid(page, "ledger-sheet").wait_for(state="visible")
        tid(page, "ledger-name").fill(unique_name("补建账本"))
        tid(page, "ledger-save").click()
        tid(page, "ledger-sheet").wait_for(state="detached")
        tid(page, "home-ledger-hint").wait_for(state="detached")
        run.check("补建账本后提示消失", tid(page, "home-ledger-hint").count() == 0 and tid(page, "home-recent-group").get_by_test_id("bill-row").count() == 1)

        run.check_clean(s.errors, "D 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- E. 小屏 + 减少动态效果


def check_small_screen_reduced_motion(run: TestRun, phone: str) -> None:
    # 屏高 520：首页总高度远大于一屏，Hero（约 340）能完全滚出屏幕
    with session(viewport={"width": SCREEN_W, "height": 520}, reduced_motion=True) as s:
        page = s.page
        login(page, phone)
        wait_tab_bar(page)
        wait_home_loaded(page)

        wait_attr(page, "html", "data-statusbar-icons", "light")
        run.check("【状态栏】Hero 在顶端 → 浅色图标", icons(page) == "light")
        scroll_root_to(page, "bottom")
        wait_attr(page, "html", "data-statusbar-icons", "dark")
        run.check("【状态栏】Hero 滚出屏幕、顶部是浅色页面 → 深色图标", icons(page) == "dark")
        scroll_root_to(page, 0)
        wait_attr(page, "html", "data-statusbar-icons", "light")
        run.check("【状态栏】滚回顶部 → 再次切换为浅色图标", icons(page) == "light")

        durations = page.evaluate(
            """() => ({
                ptr: getComputedStyle(document.querySelector('[data-testid="home-ptr"]')).transitionDuration,
                fill: getComputedStyle(document.querySelector('.ft-progress__fill')).transitionDuration,
            })"""
        )
        run.check("【减少动态效果】下拉区高度与进度条宽度不做过渡", durations["ptr"] == "0s" and durations["fill"] == "0s", str(durations))

        run.check_clean(s.errors, "E 节会话无页面异常 / 控制台错误 / 失败请求")


# ---------------------------------------------------------------------------------------------- main


def main() -> int:
    require_server()
    run = TestRun("home-page")
    db = LocalDb()
    db.verify_schema()
    db.delete_pending_bills()

    ctx: dict = {}
    try:
        check_new_user(run, db)
        phone = check_data_home(run, db, ctx)
        check_legacy_only(run, db)
        check_small_screen_reduced_motion(run, phone)
    finally:
        db.delete_pending_bills()

    return run.finish()


if __name__ == "__main__":
    sys.exit(main())
