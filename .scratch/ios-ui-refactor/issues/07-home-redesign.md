# 07 — 首页重做

**What to build:** 首页按设计稿重做：品牌渐变 Hero 铺满到屏幕顶端，把「本月支出」作为最大的数字，收入和结余是两张玻璃小卡；右上角带角标的通知入口通向待确认账单。Hero 之下依次是待确认横幅（有待确认账单时才出现）、本月预算（三色进度）、最近账单（可点「全部」进入账单 Tab）。首页不再有快捷操作宫格；新用户看到清晰的空状态与引导；支持只刷新本页数据的下拉刷新。

**Blocked by:** 05 — 二级页面模板 + 账本管理

**Status:** completed（自动化验收全部通过——`dotnet build` 0 警告 0 错误、`dotnet test` 758/758、`run_all.py` 11/11（新增 `home_page_test.py` 93 项）；设备 / 模拟器上的人工验收待办，见下方「待人工验收」）

- [x] 开工前先列出现有首页的全部功能点并写入本 ticket 的实现说明，迁移后逐项对照（概览、预算、快捷操作、最近账单、刷新、新增入口、待确认入口、首次使用引导等）——见「实现说明 · 现有功能点对照」
- [x] Hero：品牌渐变延伸到状态栏之下；标题「记账本」；按应用时区显示日期与星期；带待确认角标的通知入口通向待确认账单；「本月支出」为最大数字（整数大字 + 小数小字）；收入 / 结余两张深色玻璃小卡。「本月」边界按应用时区，区间左闭右开，口径与统计一致——小卡是**扁平半透明**而非真模糊（玻璃层预算，见「与 ticket 文字的差异」第 1 条）
- [x] 状态栏：Hero 位于顶部时声明深色背景（浅色图标）；Hero 滚出屏幕后平滑切换为浅色背景（深色图标），使用 02 定下的页面声明方式
- [x] 待确认横幅：有待确认账单时出现（笔数与来源提示），点按进入待确认账单；没有时不占位
- [x] 本月预算：总预算 + 重点分类预算的进度条，三种状态色（正常 / ≥80% 接近上限 / 超支）并带文字说明；没有预算时给出创建预算的引导
- [x] 最近账单：账单行（分类图标、备注、「支付渠道 · 账本」、金额、时间；支出用常规文字色带负号，收入用绿色带正号；历史无账本的账单标注「未归属账本」）+「全部」切换到账单 Tab；点按账单行进入现有编辑页
- [x] 去掉快捷操作宫格；空状态与引导：没有账本 → 引导先创建账本（复用 05 的新建账本 Sheet）；有账本没账单 → 引导记第一笔
- [x] 下拉刷新（新增复用组件）只刷新本页数据，不触发同步；~~同时保留不依赖手势的手动刷新入口（桌面浏览器也可用）~~——**验收后修订：Hero 里的刷新按钮已撤掉（设计稿没画，用户拍板），下拉是唯一的刷新入口，见「验收后修订」**
- [x] 新增复用组件：金额展示（整数大字 + 小数小字）、三色进度条、账单行、空状态、下拉刷新——空状态 05 已有，本 ticket 新增其余四个，另有节标题 `SectionHeader`、预算行 `BudgetRow`
- [x] 自动化验收（E2E，手机视口）：新用户首页展示空状态与创建账本引导；创建账本与账单后，「本月支出」与所建账单一致，最近账单出现该账单；预算进度与文字说明；滚动后状态栏样式属性切换；首页没有任何按钮形式的刷新入口，刷新只靠真实触摸下拉（验收后修订：原为「非手势刷新入口可用」）；依赖旧文案（如「本月概览」）的断言同步更新——`home_page_test.py` 93 项
- [ ] 人工验收（**待人工**，需要设备 / 模拟器，见下方清单）：对照设计稿 01 与 screens 的 01-home；Hero 顶到屏幕最顶端、状态栏图标颜色随滚动切换、下拉刷新手感；Android 与 iOS（CI 产物）
- [x] 回归：既有 E2E 与 `dotnet test` 全部通过，`dotnet build` 零错误

## 实现说明

- 覆盖 spec 用户故事：28–35。
- 设计稿：01 · 四个 Tab 根页面；screens 的 01-home。
- **与设计稿的差异，以 spec 为准**：设计稿 07 写「下拉刷新触发同步」，实际只刷新本页数据，云端同步仍只在「我的」里手动触发。
- 账单行、空状态、下拉刷新等组件在本 ticket 创建，08 起复用。
- 数据口径沿用现有统计与预算概览的服务，不新增查询。

### 现有功能点对照（迁移前列出、迁移后逐项核对）

迁移前的首页（`PStackPageBar` + `MContainer` + 四张 `MCard`）具备的全部功能：

| 迁移前首页的功能 | 迁移后的位置 / 行为 | E2E 段（`home_page_test.py`） |
|---|---|---|
| 顶栏：标题「记账本」、刷新按钮（刷新中转圈）、`＋` 记一笔 | Hero 里的标题（`h1`）与通知入口（**刷新按钮已撤掉**——验收后修订，下拉是唯一的刷新入口）；`＋` 不再出现——Tab 栏右侧的「记一笔」圆钮（03 号起）覆盖它 | A、C |
| 本月概览卡：收入 / 支出 / 结余三列（`ToString("N2")`） | Hero：**支出**是最大数字（整数大字 + 小数小字），收入 / 结余两张小卡；千分位与小数点固定（不随设备区域设置变化）；「本月」改按应用时区（迁移前用 `DateTime.Now`） | A、B |
| 本月预算卡（仅有预算时）：前 3 项（图标 + 分类名 + 括号里的账本名、「剩余 ¥」、超支时红字）、三色进度条、「共 N 项预算，点击"管理"查看全部」、「管理」进预算页 | 预算分组：总预算在前 + 使用率最高的分类预算（一共 3 项，**展示规则有调整**，见差异第 2 条）；名称后灰色小字标注范围（全部账本 / 账本名）；文字线索「剩余 / 仅剩 / 超支 / 已用完」；进度条带 `role="progressbar"` 与带状态的读屏文案；脚注与「管理」保留；**没有预算时新增「设置本月预算」引导**（迁移前整张卡消失） | A、B |
| 快捷操作卡：记一笔 / 账单 / 待确认（带笔数角标） | **按 spec 移除**：Tab 栏的四个 Tab + 「记一笔」圆钮覆盖「记一笔」「账单」；「待确认」由 Hero 的通知入口（带角标）与待确认横幅承接 | A（断言已移除） |
| 最近账单卡：5 笔（图标按收支着色、「分类 - 备注」、「渠道 + MM-dd HH:mm」、金额带正负号并着色）、「查看全部」、加载转圈、空状态「暂无账单 + 记一笔」 | 最近账单分组：5 行 `BillRow`（柔和图标块、「分类 · 备注」、「支付渠道 · 账本」、金额与时间两行；支出常规文字色带负号、收入深绿带正号；历史无账本标「未归属账本」）；「全部」**切换到账单 Tab**（迁移前是把 `/bills` 压栈成二级页面，违反 ADR 0007 第 4 点）；点按行进入编辑页（迁移前行不可点）；加载中是骨架屏；空状态改为「还没有账单 · 记第一笔」 | B |
| 待确认账单数量（角标，`PendingBillsChanged` 事件更新） | 通知入口角标（> 99 显示 99+，读屏名称带笔数）+ 待确认横幅（笔数与最近几笔的来源渠道）；仍订阅 `PendingBillsChanged`，没有待确认账单时横幅与角标都不占位 | B |
| 预算超支：应用内 Snackbar「预算超支提醒：xx 本月预算已超支」+ `BudgetAlertService` 本地通知（每日最多一次） | 保留；Snackbar **每个预算在一个首页实例内只提示一次**（迁移前每次重新加载都弹，下拉刷新 / 账单变更时会反复弹，见差异第 3 条）；本地通知沿用 | B、C |
| 登录态：未登录重定向 `/login`；认证失败（`UnauthorizedAccessException`）重新登录；其它加载失败只提示不登出；`AuthStateChanged` 重新加载 | 沿用，并补上：换账号（退出后用别的手机号登录）时先清掉上一个用户的数据并回到骨架，不让旧数据闪一下；未登录时首页不渲染 | A（登录后首次进入） |
| 账单变更（`BillChanged`）自动刷新 | 沿用；**新增**二级页面关闭后重新加载（预算、账本、待确认账单、导入……都可能改了首页数据；记一笔 / 编辑账单走 `BillChanged`，不重复加载） | A（从预算页返回） |
| 三块数据顺序读取（`SemaphoreSlim` 闸门，EF Core 不允许并行查询） | 沿用，并扩展到新增的账本数、待确认账单读取与新的刷新入口 | C（下拉刷新反复触发） |
| 刷新入口：只有顶栏刷新按钮 | **下拉刷新**（手势）取代刷新按钮（验收后修订：实现时曾先保留按钮、与下拉共用同一套加载，用户拍板撤掉按钮）；**只重读本页数据、不触发云端同步** | C |
| 首次使用引导：迁移前没有（新用户看到空的概览与「暂无账单」） | **新增**：没有账本也没有账单 → 整块「还没有账本」+「新建账本」（复用 05 的新建账本 Sheet，保存后原地变为「记第一笔」）；只有历史无账本账单、没有账本 → 顶部一行提示、不挡住已有数据；有账本没账单 → 「记第一笔」 | A、D |

### 完成记录（agent 实现）

**交付物**

| 层 | 文件 | 作用 |
|---|---|---|
| 页面 | `Pages/Index.razor` | 重写：Hero（`PullToRefresh` 放在里面）→ 待确认横幅 → 引导 / 本月预算 → 最近账单；加载、事件订阅、闸门、超支提示去重、二级页面关闭后重新加载 |
| 页面 | `Pages/Ledgers.razor` | 新建 / 编辑账本的 Sheet 抽成 `LedgerFormSheet` 后改为使用它（首页与账本页共用一份）；既有 `ledgers_page_test.py` 147 项原样通过 |
| 组件 | `Components/Ui/AmountDisplay.razor` · `ProgressBar.razor` · `SectionHeader.razor` · `BillRow.razor` · `BudgetRow.razor` · `PullToRefresh.razor`、`Components/Ledgers/LedgerFormSheet.razor` | 08 起账单 Tab 复用 `BillRow` / `AmountDisplay` / `PullToRefresh`，18 号预算页复用 `BudgetRow` / `ProgressBar` |
| 脚本 | `wwwroot/js/pullRefresh.js` | 下拉刷新手势：状态与位移写在 Blazor 不渲染的属性上；只在滚动容器停在顶部、单指纵向下拉时接管；阻尼、阈值 72 / 上限 132 / 保持 48（取自令牌） |
| 展示规则（纯函数） | `Models/MoneyPresentation.cs` · `MdiIcons.cs` · `BillPresentation.cs` · `BudgetPresentation.cs` · `HomePresentation.cs` · `LedgerFormTarget.cs`（`LedgerPresentation.cs` 改用 `MdiIcons.Normalize`、`UiEnums.cs` 增加 `AmountSize` / `ProgressTone`） | 金额拆分与格式、图标名校验、账单行文字 / 配色 / 时间、预算状态与文字线索、首页的日期行 / 横幅文案 / 重点预算挑选 / 引导矩阵——页面里不散落字符串拼接 |
| 样式 | `wwwroot/css/tokens.css`（`--ft-income-text` / `--ft-danger-text` / `--ft-badge-solid`、`--ft-warning-text` 加深、下拉刷新参数）、`ui.css`（通用件与首页一大段） | 文字用色另有加深档（对比度 ≥ 4.5:1），见 ADR 0007 第 9 点 |
| 宿主 | `server/FinanceTracker.Web.Server/Program.cs` | 注册 `AddDbContextFactory` 与**真实的** `PendingBillService`（原为空实现），首页的横幅与角标才能被 E2E 验收 |
| 测试（单元） | `MoneyPresentationTests` · `BillPresentationTests`（含 `MdiIconsTests`）· `BudgetPresentationTests` · `HomePresentationTests`、`DesignTokensTests` 新增对比度计算与令牌守护 | 共新增 148 项，累计 758 |
| 测试（E2E） | `tests/e2e/home_page_test.py`（93 项）；`e2e_base.py`（`Budgets` 表结构、`category_id_by_name` / `seed_budget` / `delete_pending_bills` / `give_home_a_ledger` / `wait_secondary_pages_closed`）及其自测；`tab_shell` / `tab_slider` / `immersive_safe_area` / `login_addbill` 因首页改版同步 | 见下 |
| 文档 | `docs/adr/0007-ios-visual-system.md`（第 9 点与已知限制）、`tests/e2e/README.md`（脚本、helper、可观察属性登记） | 架构决策与可观察属性登记 |

**契约（后续 ticket 直接使用）**

- **账单行**：`BillPresentation.ToRow(bill, nowLocal, includeDate)` → `BillRowModel` → `<BillRow Model OnClick>`。首页的混合列表 `includeDate: true`（今天只显示时分，昨天「昨天 HH:mm」，同年 `MM-dd HH:mm`，更早带年份）；08 的账单 Tab 按天分组，传 `false` 只显示时分。账单 Id 在行上是 `data-bill-id`（小写 Guid）。
- **金额**：一律 `<AmountDisplay Amount Size TestId>`（`Xl` Hero 主指标、`Lg` 卡片、`Md` 列表），断言用 `data-amount`（两位小数、无千分位）。不要再写 `ToString("N2")`——区域设置不同千分位不同。
- **预算行**：`BudgetPresentation.ToRow(BudgetStatus)` → `<BudgetRow Model>`；状态阈值只在 `BudgetPresentation`（`WarningPercent = 80`、`OverPercent = 100`）；使用百分比向下取整，保证显示值与状态对得上。
- **下拉刷新**：`<PullToRefresh OnRefresh TestId>` 放在可滚动内容最上方，滚动容器标 `data-scroll-root`。组件只通知「松手过了阈值」，页面决定刷新什么——**不得**在里面触发云端同步（spec）。~~必须同时提供不依赖手势的刷新入口。~~——**验收后修订**：用户拍板撤掉按钮，下拉是四个 Tab 唯一的刷新入口（ADR 0007）；新增依赖本组件的页面不要再补按钮入口。
- **一级页面的结构**：`<div class="ft-tabpage" data-scroll-root>`（占满一屏、自己滚动、底部让出 Tab 栏）→ 可选的 Hero → `<div class="ft-tabpage__body">`；分组卡片用 `GroupedList` + `ListCell` / `BillRow` / `BudgetRow`，节标题用 `SectionHeader`。
- **E2E 造数**：`seed_budget(user_id, amount=, category_name=, ledger_id=)`、`category_id_by_name`、`delete_pending_bills`（`PendingBills` 全库共享一张表，用到它的用例开始与结束各清一次）、`give_home_a_ledger(page)`（新用户首页只有「新建账本」引导，要走到「最近账单 → 全部」先造账本）。
- **回退后点按先等页面栈**：浏览器回退后二级页面还要约 300ms 才从 DOM 移除，期间页面栈会吞掉新的导航——用 `wait_secondary_pages_closed(page)`（再 `wait_animations_settled`）。

**与 ticket 文字的差异（均已评估）**

1. **Hero 的小卡与按钮是扁平半透明，不是真模糊**：ticket 写「收入 / 结余两张深色玻璃小卡」。首页同屏已有 Tab 栏的玻璃层，Hero 上再叠 3 块 `backdrop-filter`（通知入口、两张小卡）会越过 ADR 0007「每屏 ≤ 3 层」；深紫渐变上半透明白底（`--ft-glass-dark-bg` + 描边投影）与真模糊的观感差别很小。仍使用玻璃令牌的背景与投影，只是不用 `--ft-glass-dark-filter`。真机观感列入人工验收。
2. **预算区的展示规则**：迁移前取服务返回的前 3 项（顺序不保证"重点"）；现在总预算在前，再按使用率从高到低取分类预算，一共 3 项（`HomePresentation.PickBudgetHighlights`，单测覆盖）——「重点分类预算」的字面实现。
3. **预算超支 Snackbar 去重**：迁移前每次重新加载（下拉刷新、账单变更、手动刷新）只要还超支就再弹一次；现在同一个预算在一个首页实例内只提示一次（`_alertedBudgetIds`；换账号时清空）。本地通知的「每日最多一次」不变。这是体验修正，不改变预算 / 通知的服务层规则。
4. **「全部」切 Tab 而不是压栈**：迁移前 `NavController.Push("/bills")` 把 Tab 根页面压成二级页面，违反 ADR 0007 第 4 点；现在 `NavigationManager.NavigateTo("/bills")`。
5. **「本月」按应用时区**：迁移前用 `DateTime.Now`；现在统一 `AppTimeZone.ToLocal(DateTime.UtcNow)`（ADR 0005），账单行的「昨天」「时分」同口径。
6. **新增 `AddDbContextFactory` + 真实 `PendingBillService` 注册（Web.Server）**：之前 Web.Server 用空实现，横幅 / 角标永远读不到数据，无法自动化验收。真实实现每次调用自己建上下文，读它不会撞上首页的 `DbContext`。Web 端仍没有通知 / 短信捕获能力，捕获本身由 E2E 写库模拟。

**验收后修订：撤掉四个 Tab 的刷新按钮（用户拍板，ADR 0007）**

验收时用户发现四个 Tab（首页 / 账单 / 统计 / 我的）的右上角各有一枚刷新图标、设计稿一枚也没画，问「有作用吗」——有（与下拉刷新同一条路径、只重读本页数据），但选择**全部去掉、只留下拉刷新**。这是一次**有意的取舍**：spec 用户故事 34 原文要求「保留手动刷新能力」（已修订），代价是桌面浏览器 / 辅助技术没有应用内的按钮入口（可用浏览器自带的刷新；真机是触摸，不受影响）。

- 源码：首页 Hero 的刷新按钮、账单 / 统计 / 我的导航栏的刷新按钮及其忙碌状态（`_isRefreshing`）全部删除；四页只剩 `PullRefreshAsync`；「我的」读取失败的「重试」按钮（`profile-retry`）与刷新无关、保留。CSS 里只给刷新按钮用的样式（`.ft-hero__btn--ghost`、导航栏文字圆钮、刷新图标转圈）一并删除。
- 统计三个视图的加载失败占位文案「点按右上角的刷新，或下拉再试一次」改为「下拉刷新，再试一次」——原文指向的按钮已经不存在。
- E2E：`home-refresh` / `bills-refresh` / `stats-refresh` / `profile-refresh` 的点击与按钮属性断言删除，换成「页面里没有按钮形式的刷新入口」（`refreshEntries == 0`：testid、`aria-label="刷新"` 的按钮、`.mdi-refresh` 图标）；靠点按钮来让常驻 Tab 重读直写库数据的用例，改用新 helper `pull_refresh(page, "<tab>-ptr")`（真实触摸下拉，先布防再拉，没真的刷新就超时抛错，不会悄悄返回）。
- 约定：新增依赖 `PullToRefresh` 的页面**不要再补按钮入口**，除非先改 ADR 0007 里的这条决定。

**实现要点 / 发现**

- **文字对比度**：`--ft-expense`（#E8362C）在白底上约 4.2:1、`--ft-income`（#1FA544）约 3.2:1，都达不到 AA 的 4.5:1，不能直接用于小号文字。新增文字专用的加深档 `--ft-danger-text`（#D3221A，约 5.2:1）、`--ft-income-text`（#188540，约 4.7:1），`--ft-warning-text` 由 #B25E00 加深到 #A15500（在预警徽标的浅橙底上也 ≥ 4.5:1）。`DesignTokensTests` 计算对比度守住这几个值。`--ft-label-2`（次要文字，约 3.6:1）的全局审计留给 23 号 ticket（**23 号已处理**：`--ft-label-2` 由 .62 加深到 .76，各底色上 ≥ 4.5:1，见 23 号判断记录 1）。
- **页面切换动画期间量不准**：根页面进入有 0.985 → 1 的缩放（04 号），首次几何断言必须先 `wait_animations_settled`（`home_page_test.py` 的 `wait_home_loaded` 已内置）。
- **页面栈退出动画期间吞导航**：见契约最后一条；只影响 E2E 的快速连点，真人手速碰不到，但如果以后有「返回后自动弹出 / 立即跳转」的需求要留意。
- **`home-budget-group` 在没有预算时也存在**（里面是创建引导）：等预算行出现要等 `budget-row`，不能等分组。
- **已知局限**：「有没有账本」只数用户**自己**的账本；只加入了共享账本、没有自己账本的用户首页仍会看到「还没有账本」引导（点「新建账本」会正常创建）。这个边界未在 ticket 里定义，如需改为「也算上加入的账本」请告知。
- 回归：`dotnet build FinanceTracker.sln` 0 警告 0 错误；`dotnet test` 758/758（本 ticket 新增 148 项）；`run_all.py` 11/11（新增 `home_page_test.py` 93 项；`ledgers_page_test.py` 147 项、`ledger_members_page_test.py` 137 项、`tab_shell_test.py` 64 项、`immersive_safe_area_test.py` 35 项等原样通过）。

### 待人工验收（设备 / 模拟器）

- [ ] 对照设计稿 01 与 screens 的 01-home，在 Android 与 iOS（CI 产物）上确认：Hero 渐变与大圆角、标题 / 日期 / 通知入口间距、收入 / 结余小卡的**半透明观感**（见差异第 1 条，是否需要在真机上换回真模糊）、预算行与账单行的间距与配色、待确认角标
- [ ] **Hero 真正顶到屏幕最顶端**（状态栏之下没有露出页面底色）；刘海 / 灵动岛机型上标题与通知入口避开状态栏；状态栏图标颜色随滚动在「浅色（Hero 在顶）」与「深色（Hero 滚出）」之间切换
- [ ] 下拉刷新手感：阻尼、过阈值后的松手触发、回弹；转圈位置（灵动岛机型上不被遮挡）；与 iOS WKWebView 的橡皮筋 / Android WebView 的原生过度滚动与「下拉刷新」手势互不冲突（`overscroll-behavior-y: contain` 在较老的 WebView 上可能不生效）
- [ ] 底部：最后一行账单滚得出悬浮 Tab 栏与手势条之外；Tab 栏收缩 / 展开与首页滚动配合顺畅
- [ ] 真机上的待确认账单：开启通知使用权 / 短信权限后实际捕获一笔，首页横幅与角标随之出现（Web 端只能用写库模拟捕获）
- [ ] 中低端 Android 上首页（渐变 Hero + 列表）滚动流畅度
