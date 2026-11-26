# 15 — 记一笔 Sheet（新建）

**What to build:** 点 Tab 栏的「记一笔」圆钮，从底部升起一个全高 Sheet，背景页面轻微缩小：金额大字、自定义数字键盘与高大的「保存」键、5 列分类网格、分组的账本 / 支付渠道 / 时间 / 备注行，一屏完成记账。账本是必选项：没选账本时「保存」置灰，点按置灰的保存键会让账本行抖动并弹出账本菜单。保存成功后 Sheet 收起，出现「已保存到〈账本名〉」的 Toast，各页数据随之刷新。新的自定义选择器让 E2E 第一次能稳定地经界面完整保存一笔账单（此前的 E2E 造账单可能依赖直接写库）。

**Blocked by:** 08 — 账单 Tab：列表；14 — 分类管理 + 支付渠道

**Status:** completed（自动化验收全部通过——`dotnet build FinanceTracker.sln` 0 警告 0 错误、`dotnet test` 1418/1418、`run_all.py` 18/18 个脚本通过（共 1777 项检查；新增 `addbill_sheet_test.py` 174 项，基座自测 134 项，`login_addbill_test.py` 改写为 7 项、`voice_addbill_test.py` 改写为 6 项）；真机 / 设备上的人工验收待办，见下方「待人工验收」；若干判断项待用户确认，见判断记录）

- [x] 开工前先列出现有记账页的全部功能点并写入本 ticket 的实现说明，迁移后逐项对照（金额、账单类型、分类选择、账本（必填）、支付渠道、时间、备注、默认值、语音入口、保存校验、超支提醒、保存后的事件通知、编辑模式等）
- [x] 呈现：全高 Sheet 从底部升起，背景页面轻微缩小；`/add-bill` 与 `/add-bill/{BillId}` 地址继续可用；关闭或保存后回到来源页，来源页状态（滚动位置、筛选）不丢失。呈现方式（样式化的路由页或覆盖层）由本 ticket 决定，需与 PageStack 兼容
- [x] 结构：顶部抓手 + ✕ + 标题「记一笔」+ 语音入口；「支出 / 收入」分段（切换后分类网格与金额颜色同步）；金额大字 + 光标；分类网格；分组行（账本、支付渠道、时间、备注）；自定义数字键盘
- [x] 数字键盘：0–9、小数点、00、退格；最多两位小数、上限 99,999,999.99、不允许多个小数点；长按退格清空；金额输入不唤起系统键盘；零值 / 空值不可保存（沿用现有校验）；高大的「保存」键位于键盘右侧
- [x] 分类网格：5 列；支出 10 项 / 收入 5 项，外加「新建分类」；超过两行可左右翻页并有分页点；「新建分类」打开 14 的共用新建分类面板，保存后自动选中新分类
- [x] 账本必选：未选账本时「保存」置灰；点按置灰的保存键 → 账本行抖动并弹出账本菜单。账本菜单 = 自己的账本 + 有写权限的共享账本（标注「共享」），末项「新建账本…」（复用 05 的新建账本 Sheet，创建后自动选中）。保存前的账本必填与写权限校验仍由现有服务层统一负责，界面只做前置拦截与提示
- [x] 支付渠道 / 时间 / 备注分组行：支付渠道点按弹出菜单；时间按应用时区显示与输入、默认当前时间，界面层不直接调用 ToUniversalTime / ToLocalTime，一律经 AppTimeZone 换算；备注使用系统键盘，聚焦备注时自定义键盘让位且布局不被遮挡
- [x] 默认值沿用现有「用户默认值」（设为默认的分类 / 支付渠道 / 账本，没有则取第一项），不引入「最近使用」
- [x] 保存流程沿用现有逻辑（含超支提醒检查、账单变更事件通知）；成功后收起 Sheet，Toast「已保存到〈账本名〉」，首页、账单、统计随之刷新；数据来源为手动录入
- [x] 已输入内容（金额或备注非空）时关闭 Sheet 需二次确认
- [x] 本 ticket 完成后，语音入口仍可用（沿用现有交互，覆盖层样式由 17 重做）；编辑账单暂时仍走现有编辑页（由 16 迁移）
- [x] 新建复用组件：自定义数字键盘；账本菜单（重绘共用的账本选择器，供待确认账单与导入复用）
- [x] 自动化验收（E2E，手机视口）：「记一笔」圆钮打开 Sheet → 键盘输入金额 → 选分类 → 未选账本时「保存」置灰且点按后弹出账本菜单 → 选账本 → 保存 → Toast → 账单 Tab 出现该账单（金额、分类、账本一致），本机库里数据来源为手动录入；键盘规则（第三位小数被忽略、多个小数点被忽略、超上限、长按退格清空）；有输入时关闭出现确认；内联新建账本与新建分类后自动选中；新的自定义选择器（菜单、行、Sheet）能被 E2E 稳定操作；基座的「创建账单」helper 改为经新界面（直接写库仍保留给造历史无账本账单与待确认账单）；依赖旧文案（「记一笔」「选择分类」等）的断言同步更新
- [ ] 人工验收：对照设计稿 04 与 screens 的 05-add-expense、07-add-income；真机上备注的系统键盘与自定义键盘之间的切换；Sheet 升起与背景缩放手感；Android 与 iOS（CI 产物）
- [x] 回归：既有 E2E 与 `dotnet test` 全部通过，`dotnet build` 零错误

## 实现说明

- 覆盖 spec 用户故事：62–75、78。
- 设计稿：04 · 记一笔；screens 的 05-add-expense、07-add-income。
- **与设计稿的差异，以 spec 为准**：设计稿 04 写「默认选中最近使用」，实际沿用现有的用户默认值。
- 现有说明文档记载 MASA 的下拉选择在无头浏览器里难以自动化；新的菜单 / 行 / Sheet 选择器要设计成能被 E2E 稳定操作，这是本 ticket 的验收点之一。
- 账本必填由 BillService 统一校验，界面各入口只是前置拦截；账本必填的现有规则（含历史无账本账单的处理）见 CLAUDE.md 的核心数据模型。
- 术语：「账单」「分类」「支付渠道」「账本」「数据来源」。

### 开工前清单：现有记账页的功能点（迁移后逐项对照）

读自改造前的 `Pages/AddBill.razor`（含「账本必填」改造的工作区版本，旧新建 / 编辑共用一页）。编辑模式（A16）不在本 ticket 范围，由 16 号迁移。

| # | 现有模块 | 行为 |
|---|---|---|
| A1 | 页面外壳与入口 | 紫色顶栏「记一笔 / 编辑账单」的压栈二级页；`/add-bill` 新建、`/add-bill/{BillId}` 编辑；入口：Tab 栏「＋」、首页 / 账单页的空状态引导；保存成功后 `NavController.Pop()` 回到来源页 |
| A2 | 语音录入栏 | 引导语 +「麦克风」：浏览器 Web Speech 点击开始（15 秒兜底，环境不支持时置灰并提示「当前环境不支持语音识别，可手动填写账单」）；原生按住录音（松开结束、滑出 / 指针取消则丢弃、最长 60 秒），录音中显示「听到：…」，识别后显示「识别到「…」，正在解析…」 |
| A3 | 语音预填 | `BillVoiceParser` 解析金额 / 类型 / 分类 / 支付渠道 / 时间 / 原文（做备注）；只填识别到的字段（分类、渠道必须在候选里且类型匹配）；缺金额时不改表单并 Toast「未能识别金额…」；命中提示「已识别：金额、分类「…」…」；成功预填后标记语音来源，保存时数据来源为语音识别 |
| A4 | 账单类型 | 支出 / 收入按钮组；切换时分类被清空（保存时才提示「请选择分类」） |
| A5 | 金额 | 数字输入框（¥ 前缀、步长 0.01、最小 0）；规则「金额必须大于0」「最多保留两位小数」；失焦时规整到两位小数；系统数字键盘 |
| A6 | 分类选择 | 3 列按钮网格，按当前类型过滤，选中高亮；必选 |
| A7 | 支付渠道 | 下拉，必选，选项带图标 |
| A8 | 账本（必填） | 账本下拉（自己的账本 + 共享账本，共享的带「（共享）」）；**一个账本都没有（加载完成后）时不显示下拉，而是提示「账单必须归属一个账本…」+「去创建账本」→ 压栈到账本页，建完回来刷新并自动选中** |
| A9 | 交易时间 | `datetime-local`，默认当前（应用时区墙上时间）；编辑时库里的 UTC 经 `AppTimeZone` 转本地回填 |
| A10 | 备注 | 多行文本框，可选 |
| A11 | 默认值 | 读 `UserDefaults`：按账单类型取默认分类、默认支付渠道，没有则取第一项；默认账本 = 用户设为默认的自己的账本，否则第一个自己的账本（共享账本不会被默认选中） |
| A12 | 保存校验 | 未登录 → Toast「请先登录」并跳登录；金额 > 0 →「请输入金额」；「请选择分类」；「请选择支付渠道」；「请选择账本」；共享账本写权限（`EnsureCanWriteAsync` 读离线缓存，失败 → 警告 Toast，不登出） |
| A13 | 保存 | 新建 `BillService.CreateBillAsync`（数据来源：语音预填 ? 语音识别 : 手动录入），编辑 `UpdateBillAsync`；Toast「账单创建成功 / 账单更新成功」，失败「保存失败: …」；时间由 `BillService` 经 `AppTimeZone` 换成 UTC 入库 |
| A14 | 超支提醒 | 支出保存后查预算（总预算 + 该分类预算，账本维度）：≥ 100% 红色「预算超支提醒…」、≥ 80% 黄色「预算预警…」；失败不影响保存 |
| A15 | 保存后的通知 | `BillEventService.NotifyBillChanged()`，首页 / 账单 / 统计据此重读 |
| A16 | 编辑模式 | 读账单回填全部字段；账单不存在 → Toast「账单不存在」并返回；历史无账本账单编辑时必须补上账本才能保存 |
| A17 | 数据读取与降级 | 分类 / 支付渠道 / 自己的账本读本地库；共享账本先联网刷新成员关系，**放在表单就绪之后**，云端慢或不可用只影响共享账本；加载失败 Toast |

### 完成记录（agent 实现）

**交付物**

| 层 | 文件 | 作用 |
|---|---|---|
| 壳 | `Web/Services/AddBillSheetService.cs`（新，Scoped）：`Open()` / `Close()` / `IsOpen` / `OpenCount`（记账会话编号）/ `Changed` / 常量 `LegacyRoute`、`OpenUri` / `IsOpenRequest(uri)`；`MauiProgram.cs` 与 `Web.Server/Program.cs` 注册；`Web.Server/Program.cs` 把旧地址 `/add-bill` 302 到 `/?add-bill=1`；`Layouts/MainLayout.razor` 在 `PPageStack` 之前挂 `<AddBillSheet />` | 布局级覆盖层的开关与旧地址兼容 |
| 组件 | `Web/Components/Bills/AddBillSheet.razor`（标记）+ `.razor.cs`（会话、取数、保存、引导）+ `.Voice.cs`（语音：原按住 / 点按两种交互沿用，预填表单、来源标记语音）| 记一笔 Sheet 本体 |
| 组件 | `Web/Components/Bills/CategoryGrid.razor`（新）：5 列 × 2 行一页、横向 `scroll-snap` 翻页、分页点、末尾「新建分类」；选中项变化后翻到它所在的页 | 分类网格 |
| 组件 | `Web/Components/Ui/AmountKeypad.razor`（新）：0–9 / 小数点 / `00` / 退格（长按清空）+ 右侧跨三行的「保存」键（`SaveEnabled` 置灰用 `aria-disabled`、`Busy` 显示「保存中」）| 自定义数字键盘 |
| 组件 | `Web/Components/Ui/PopoverMenu.razor` · `PopoverMenuItem.razor`（新）：实色白底的弹出菜单，脚本定位（下方优先、放不下翻到上方、限高滚动），点外面只收起不穿透，Esc 收起 | 支付渠道 / 账本菜单共用 |
| 组件 | `Web/Components/Pickers/LedgerMenu.razor`（新）：账本菜单——自己的账本 + 可记账的共享账本（带「共享」徽标）+ 末项「新建账本…」；`Visible` / `Anchor` / `Ledgers` / `SelectedId` / `OnSelect` / `OnCreate` | **19（待确认账单）、20（导入）直接复用** |
| 组件 | `Web/Components/Ui/BottomSheet.razor`：新增 `Tall`（全高）、`ScaleBackground`（背景缩放）、`Guarded` + `BeforeClose`（关闭前确认，下拉收起时弹回原位再弹确认）| 覆盖层所需能力 |
| 纯函数 | `Web/Models/AmountInput.cs`（键盘规则：`Apply` / `Parse` / `FromDecimal` / `Format` / `SizeOf` / `Spoken`）· `Web/Models/AddBillPresentation.cs`（`CheckSave` 引导顺序、`BlockMessage`、`PickCategory` / `PickChannel` 默认值、`HasInput`、`TimeLabel`、时间输入格式、分类网格页码）| 规则与文案不散落在 Razor 里，单元测试覆盖 |
| 脚本 / 样式 | `wwwroot/js/ui.js`：长按退格、分类网格页码（`data-page`）与 `scrollPagerTo`、弹出菜单定位、备注聚焦时让出键盘（`data-typing`）、拖拽收起时的 `Guarded` 分支；`wwwroot/css/ui.css`：Sheet 全高 + 背景缩放、玻璃层预算（Sheet 升起时关掉被盖住的页面与 Tab 栏的 `backdrop-filter`）、金额 / 分类网格 / 设置行 / 键盘 / 菜单 / 抖动与光标动画 | 只引用令牌；弹出菜单不是玻璃 |
| 入口改造 | `Components/GlassTabBar.razor`（＋ 圆钮调 `Open()`）· `Pages/Index.razor` · `Pages/Bills.razor`（空状态「记第一笔」调 `Open()`）· `Pages/AddBill.razor`（只剩 `/add-bill/{BillId}` 编辑，16 号并入后整页删除）| 旧的 `NavController.Push("/add-bill")` 全部移除；各 Tab 页里「路由前缀 `/add-bill` 的二级页关闭后重读」的判断沿用，编辑页还要用 |
| 测试（单元） | `tests/unit/.../Services/AmountInputTests.cs`（61 项：每条键盘规则、边界、上限、`FromDecimal` / `Format` / `SizeOf` / `Spoken`）· `AddBillPresentationTests.cs`（41 项：引导顺序、默认值挑选、`TimeLabel` 今天 / 昨天 / 更早 / 跨年、分类网格页码）· `AddBillSheetServiceTests.cs`（29 项：开 / 关 / 会话编号 / 事件、`IsOpenRequest` 对各种地址形态）· `DesignTokensTests` 补层级链（Sheet < 弹出菜单 < Alert）与弹出菜单项行高 ≥ 44 的断言 | 累计 1418（比 14 号结束时多 136 项：三个新测试类 131 项 + 令牌测试补充 5 项）|
| 测试（E2E） | `tests/e2e/addbill_sheet_test.py`（新，174 项：A–O 十五节，见脚本头部说明）· `login_addbill_test.py`（改写为 Sheet 流程，7 项）· `voice_addbill_test.py`（改写：深链 `/add-bill` → Sheet，麦克风与降级，6 项）· `tab_shell_test.py` / `tab_slider_test.py` / `home_page_test.py` / `bills_page_test.py`（「记一笔」入口断言由「压栈到 `/add-bill`」改为「升起 Sheet、地址不变」）· `e2e_base.py` 新增 Sheet helper（`addbill_sheet` / `open_add_bill_sheet` / `close_add_bill_sheet` / `press_keys` / `press_key` / `amount_text` / `wait_amount_text` / `pick_category` / `pick_ledger` / `pick_channel` / `save_bill_via_sheet`）、`create_bill(..., page=…)` 经 Sheet 造账单、`LocalDb.set_user_defaults` · `e2e_base_test.py` 新增 `set_user_defaults` 自测（134 项）· `README.md` 登记脚本、helper、观察属性与已知限制 | 见下 |
| 文档 | `docs/adr/0007-ios-visual-system.md` 第 17 点与已知限制 · `tests/e2e/README.md` | 覆盖层架构、判断项、已知取舍 |

**逐项对照（开工前清单 → 迁移后）**

| # | 迁移后 | 对照结果 |
|---|---|---|
| A1 | Tab 栏「＋」、首页 / 账单页空状态都调 `AddBillSheetService.Open()`，Sheet 从底部升起（地址不变、不压栈）；旧地址 `/add-bill` 由 Web.Server 302 到 `/?add-bill=1`，首次渲染后升起；`/add-bill/{BillId}` 暂留原页面；保存或确认放弃后收起，回到来源页（滚动位置 / 筛选 / 大标题折叠状态原样，E2E 在账单 Tab 上验证关闭与保存两条路径）| 一致（压栈页 → 覆盖层，见判断记录第 1 条） |
| A2 | 麦克风移到标题栏右侧：不支持按住的环境（浏览器）点按开始、环境不支持时置灰（`aria-disabled`）并点按给出说明；原生环境按住录音、松开结束、滑出取消、最长 60 秒；识别进度显示在 Sheet 顶部的状态行（`addbill-voice-status`）| 一致（入口位置换了；状态行与交互沿用，覆盖层样式由 17 重做） |
| A3 | `AddBillSheet.Voice.cs` 沿用解析与预填规则：只填识别到的字段、缺金额不改表单并提示、命中提示「已识别：…」（`addbill-voice-hint`）；预填后保存的来源 = 语音识别 | 一致（E2E 在 Web.Server 里覆盖降级路径与深链；真实麦克风待人工验收） |
| A4 | 标题下的「支出 \| 收入」`SegmentedControl`（`addbill-type`）；切换时金额保留、分类网格换成新类型的项、金额与分段文字颜色同步（支出红 / 收入绿）；**新类型下自动选中它的默认分类**（旧页是清空）| 一致 + 改进（见判断记录第 4 条） |
| A5 | 金额是展示区（`amount-display`：大字 + 闪烁光标，`div`、不是输入框），输入全走自定义键盘；规则由 `AmountInput` 集中：最多 2 位小数、整数 ≤ 8 位且 ≤ 99,999,999.99、无前导零、`00` 键、退格、长按清空；字号随位数收缩；保存键右侧高大跨三行 | 一致 + 改进（系统键盘不再唤起；规则由单元测试逐条锁定）|
| A6 | `CategoryGrid`：5 列 × 2 行一页、可横向滑动翻页 + 分页点、末尾「新建分类」；选中高亮；「新建分类」打开 14 号的 `CategoryFormSheet`，保存后自动选中并翻到它所在的页 | 一致（3 列 → 5 列网格；自建分类排序见判断记录第 2 条）|
| A7 | 「支付渠道」设置行 → 点按弹出 `PopoverMenu`（带图标、选中打勾）；默认值同 A11 | 一致 |
| A8 | 「账本」设置行 → 点按弹出 `LedgerMenu`：自己的账本 + **有写权限（Owner / Editor）的**共享账本（带「共享」徽标）+ 末项「新建账本…」（打开 05 号的 `LedgerFormSheet`，保存后自动选中）；缺账本时保存键置灰，点按 → 账本行抖动并直接弹出账本菜单；一个账本都没有的用户在菜单里只有「新建账本…」，不再需要「去创建账本」压栈页 | 一致 + 改进（见判断记录第 3 条：只读成员的共享账本不再列出）|
| A9 | 「时间」设置行：显示「今天 HH:mm / 昨天 HH:mm / M月d日 HH:mm / yyyy年M月d日 HH:mm」，点按透明的 `datetime-local` 覆盖层，系统原生选择器；界面显示 / 输入的是应用时区墙上时间，`BillService` 经 `AppTimeZone` 换成 UTC 入库，界面层不调 `ToUniversalTime` / `ToLocalTime`；清空 / 非法输入沿用原值 | 一致 |
| A10 | 「备注」设置行：单行输入（16px，免 iOS 聚焦放大），可选，保存时 `Trim`，空白不入库；聚焦时自定义键盘让位（`data-typing`），回车 / 失焦恢复 | 一致（多行 → 单行：设计稿如此）|
| A11 | 打开时读 `UserDefaults`：默认账本（自己的）→ 否则第一个自己的账本；默认分类按类型（切类型时换成对应类型的默认）；默认支付渠道；都没有取第一项；默认值指向的项已被删除时退回第一项 | 一致 |
| A12 | 保存键点亮 = 金额 > 0、分类、支付渠道、账本齐了；置灰（`aria-disabled`）时点按按「金额 → 分类 → 支付渠道 → 账本」的顺序引导：缺金额抖金额区 + Toast「请输入金额」，缺分类抖网格 + Toast，缺支付渠道 Toast，缺账本抖账本行并弹账本菜单；共享账本写权限 `EnsureCanWriteAsync` 先本地校验，`ForbiddenAccessException` 只弹警告 Toast、不登出；未登录 Toast「请先登录」并收起 | 一致（置灰 + 点按引导取代「点保存才报错」）|
| A13 | `BillService.CreateBillAsync`；来源 = 语音预填 ? 语音识别 : 手动录入；成功后 Toast「已保存到「账本名」」（取代「账单创建成功」）；失败「保存失败: …」且 Sheet 保持打开；保存中键盘 / 关闭不可操作（`Dismissible=false`）| 一致 + 文案按 ticket 更新 |
| A14 | 写入与预算检查在同一个 `PageDataGate` 里，随后沿用「预算超支提醒 / 预算预警」Toast 文案与阈值；检查失败不影响保存 | 一致 |
| A15 | 写入成功后在闸门外发 `BillEvents.NotifyBillChanged()`，再 `Sheet.Close()`；首页、账单、统计据事件重读 | 一致 |
| A16 | 不在本 ticket：`/add-bill/{BillId}` 仍由原页面承担，16 号把编辑并入同一个 Sheet | 延后（按 ticket）|
| A17 | 分两段读取：先读本地（分类 / 渠道 / 自己的账本 / 默认值，经 `PageDataGate`），表单立即可用（`data-ready="true"`）；再读共享账本（云端短期限，失败只是菜单里少了共享账本）；每次打开是一次新会话，迟到的上一次结果按会话编号丢弃；加载失败 Toast，未登录收起 | 一致 + 增强（会话编号防串）|

**判断记录（均已评估，评审阶段会再向用户确认）**

1. **呈现方式：布局级覆盖层，不是样式化的路由页**（ticket 把这个选择交给本 ticket）。页面栈只会做「从右侧滑入」，做不出「底部升起 + 背景缩小变暗」的模态 Sheet；做成覆盖层还让来源页状态天然保留（不压栈 = 不销毁、不重载、地址不变），也不与页面栈的转场 / 左边缘右滑返回打架。代价：地址栏不反映 Sheet 状态；Android 系统返回键目前不会先收起 Sheet；底下的页面仍在 DOM 里可被读屏 / 键盘到达——后两条留给 23 号（`inert` + 焦点陷阱 + 返回键）（**23 号没有处理**：它们不在 23 号的验收范围——完整读屏适配审计明确排除、返回键要改原生宿主且只能真机验证——仍然开着，已记入 ADR 0007「已知限制」，需要单独立项）。
2. **自建分类排在预设分类之前**：分类网格顺序沿用 `CategoryService`（类型 → 排序数字），自建分类的排序数字默认 0，所以排在预设（排序 1…10）前面，与旧记账页一致；用户用分类管理里的「排序」数字控制位置。设计稿没画自建分类的位置。若希望「预设在前、自建在后」，改网格的排序规则即可（但「排序」输入的语义会变成组内排序）。**待用户确认**。
3. **账本菜单只列有写权限的共享账本**（按 ticket 条文：「自己的账本 + 有写权限的共享账本」）。旧页面把 Editor / Viewer 的共享账本都列出，Viewer 选了保存时才被拦截；现在只读成员的共享账本不出现——行为比旧版更严格但更直接，保存前的写权限校验仍在。
4. **切换账单类型自动选中新类型的默认分类**（旧页清空分类，保存时才提示「请选择分类」）：避免用户切到「收入」后还得先点一个分类才能保存；金额保留。
5. **保存键置灰用 `aria-disabled`，不用 `disabled`**：被禁用的按钮收不到点击，做不了「点按置灰的保存键 → 引导」。样式据 `aria-disabled="true"` 置灰（浅灰底 + 加深的灰字，达到大号文字对比度，比设计稿的灰略深）；读屏读作「不可用」。自动化里点它必须 `click(force=True)`。
6. **深链 `/?add-bill=1` 不从地址栏剥离**：旧地址 `/add-bill` 的书签用户打开后 Sheet 升起；关闭后地址仍带参数，刷新会再次升起。影响面仅限直接访问旧地址（应用内入口都是服务调用）；剥离需要在 Blazor 里 `NavigateTo(replace)`，会与首次渲染的读参数时序纠缠，没有做。
7. **玻璃层预算只处理本 Sheet**：Sheet 升起时屏幕上原有 Tab 栏 + 标题栏两个圆钮 + 底下页面自己的玻璃钮（账单页导航栏两个、统计页一个）最多 5 层，超出 ADR 0007 §3 的「每屏 ≤ 3」。现用同级后继选择器把被遮罩盖住的页面与 Tab 栏的 `backdrop-filter` 关掉（只剩标题栏两个圆钮，E2E 在四个 Tab 上量过）；其它 Sheet（筛选、新建分类 / 账本）仍是页面内的 Sheet，同屏层数的统一治理归 23 号（**23 号已统一治理**：所有 Sheet 升起期间都由 `<html data-ft-sheet-open>` 关掉被盖住的页面与 Tab 栏的毛玻璃，见 ADR 0007 §26）。
8. **备注聚焦时自定义键盘隐藏而不是上推**：系统键盘弹出时，键盘区整块让位，Sheet 内容区不被遮挡；回车 / 失焦恢复。真机上系统键盘与自定义键盘之间的切换手感是人工验收项。
9. **弹出菜单是实色白底，不是玻璃**：玻璃层预算；层级 `--ft-z-popover`（125）介于 Sheet（120）与 Alert（130）之间，点外面只收起菜单、不穿透到下面的行。
10. **时间用系统原生选择器**（透明 `datetime-local` 盖在设置行上），没有做自定义滚轮：设计稿 04 只画了「时间」一行的值；原生选择器带来 iOS / Android 各自的日期时间体验，也保证时区与格式处理沿用既有逻辑。iOS WKWebView 里点行是否稳定唤起原生选择器是人工验收项。
11. **语音：Sheet 内沿用旧交互，覆盖层样式由 17 重做**：麦克风移到标题栏，识别进度在 Sheet 顶部状态行；预填 / 来源标记 / 缺金额提示规则不变。

**契约（后续 ticket 直接使用）**

- **`AddBillSheetService`**（Scoped）：`Open()` 升起 Sheet（每次是一次全新的记账会话，`OpenCount` 自增），`Close()` 收起，`IsOpen`、`Changed` 事件；常量 `LegacyRoute = "/add-bill"`、`OpenUri = "/?add-bill=1"`，静态 `IsOpenRequest(uri)` 判断地址是否带 `add-bill=1`（缺值或 `true` 也算）。**16 号**在这里扩展「编辑」入口（给 `Open` 一个账单 Id）并并入编辑模式。
- **`BottomSheet`**：`Tall`（全高，高度固定、内容切换不跳）、`ScaleBackground`（升起时背景缩放；要求宿主与 `.m-page-stack` 同级，渲染在它之前）、`Guarded` + `BeforeClose`（关闭前确认：返回 false 即拦下用户发起的关闭；下拉收起时脚本弹回原位再「点击」遮罩触发确认）、`OnClosed`（退出动画结束、已卸载）。
- **`LedgerMenu`**（19、20 复用）：`Visible` / `VisibleChanged`、`Anchor`（触发行的 `ElementReference`，菜单据此定位）、`Ledgers`（`LedgerModel` 列表；`IsShared` 的带「共享」徽标）、`SelectedId`、`OnSelect(Guid)`、`OnCreate`（末项「新建账本…」，不传则不显示）、`TestId`（默认 `ledger-menu`）。选项观察属性 `ledger-option`（`data-value` = 账本 Id、`data-selected`），共享徽标 `ledger-option-shared`，末项 `ledger-option-new`。
- **`PopoverMenu` / `PopoverMenuItem`**：通用弹出菜单；`Visible` / `VisibleChanged` / `Anchor` / `AriaLabel` / `TestId`；定位、限高、点外面收起（不穿透）、Esc 都在组件里。
- **`AmountKeypad`**：`OnKey(string)`（`"0"`–`"9"`、`"."`、`"00"`、`"backspace"`、`"clear"`）、`SaveEnabled` / `Busy` / `SaveLabel` / `BusyLabel` / `OnSave` / `SaveTestId`。**18（预算金额）可直接复用**。
- **`AmountInput` / `AddBillPresentation`**：金额文本的唯一规则来源；16 号编辑回填用 `AmountInput.FromDecimal`，时间回填用 `AddBillPresentation.ToInputValue`；`CheckSave` 的引导顺序（金额 → 分类 → 支付渠道 → 账本）与 `BlockMessage` 文案 16 号保持一致。
- **E2E**：`create_bill(login_info, ..., page=page)` 经 Sheet 造账单（要求当前在 Tab 根页面；只适用于有账本的手动录入账单，其余仍直接写库）；`save_bill_via_sheet` / `open_add_bill_sheet` / `press_keys` / `pick_ledger` 等见 README；观察属性 `addbill-sheet` / `amount-display` / `category-tile` / `ledger-row` / `channel-row` / `time-row` / `note-row` / `keypad` / `key-*` / `addbill-save` 等；`LocalDb.set_user_defaults` 写用户默认值；`click(force=True)` 点 `aria-disabled` 的保存键。

**与 ticket / 设计稿的差异（均已评估）**

1. **默认值不是「最近使用」**（设计稿 04 如此写）：按 ticket / spec 沿用「用户默认值」。
2. **保存键置灰的底色 / 字色比设计稿略深**（判断记录第 5 条）：对比度达到大号文字的 3:1。
3. **自建分类排在预设之前**（判断记录第 2 条）：设计稿没画自建分类的位置。
4. **玻璃只剩标题栏两个圆钮**（判断记录第 7 条）：设计稿示意图里 Tab 栏透过遮罩可见，实现里被遮罩盖住且不再模糊。
5. **语音仍是旧交互（状态行 + 标题栏麦克风）**：设计稿 06 的语音覆盖层由 17 号实现。
6. **编辑账单暂走旧页**：`/add-bill/{BillId}` 由 16 号并入。

**实现要点 / 发现**

1. **MASA 的 `<p>` 外边距泄漏**（`.m-application p { margin-bottom: 16px }`）：Sheet 内所有文字块用 `div` / `span`（全局修复归 23 号——**23 号已处理**：`.m-application p { margin-bottom: 0 }` 全局归零）。
2. **Playwright 把 `aria-disabled="true"` 当成不可点**：`is_disabled()` 为真、普通 `click()` 会一直等「可点」直到超时——点置灰的保存键要 `click(force=True)`，断言「没有真的 `disabled`」用 `get_attribute("disabled") is None`。
3. **自建分类排在预设之前**（判断记录第 2 条）：测试起初写成「排在最后、落在第二页」，失败后按分类服务的真实顺序更正；顺带让 `CategoryGrid` 在选中项还不在列表里时不翻页（新建分类时「选中」先于「列表刷新」到达，按找不到处理会误翻回第一页——防御性修复，没有复现过）。
4. **数据库金额是 REAL，读出来是文本 `'12000.0'`**：E2E 的金额断言按数值比（`amount_equals`），不要比字符串。
5. **抖动只持续 460 ms，轮询很可能错过**：E2E 在点按之前装一个 `MutationObserver` 记录「曾经抖过的元素」（金额区 / 分类网格 / 账本行各有自己的 `data-shake` 载体），事后断言。
6. **Toast 本身是带 `backdrop-filter` 的玻璃胶囊**：测「屏幕上有几层玻璃」前要等 Toast 消失（`wait_no_toasts`），否则会把短暂的 Toast 数进去。
7. **Tab 根页面常驻、共用一个 `DbContext`**：保存与随后的预算检查、Sheet 开场读库都经 `PageDataGate`，否则账单变更事件触发的各 Tab 重读会与之并行，同一个 DbContext 报「第二个操作」错误。
8. **`BillChanged` 之后账单 Tab 重读不会丢滚动位置 / 分段**：E2E 在账单 Tab 上滚到 ≥ 600px、选了分段「支出」后升起 / 关闭 / 保存，三种路径下地址、大标题折叠、分段、滚动位置都原样（保存后列表里出现新账单）。
9. 回归：`dotnet build FinanceTracker.sln` 0 警告 0 错误；`dotnet test` 1418/1418；`run_all.py` 18/18 个脚本通过（共 1777 项检查：基座自测 134、记一笔 Sheet 174、账单页 278、冒烟 7、分类 133、首页 93、沉浸式 / 安全区 31、账本成员 138、账本 147、登录 7、支付渠道 106、我的 97、造数可见性 9、统计 114、趋势与年度 157、Tab 壳 64、Tab 滑块 82、语音 6）。

- **验收阶段修正（24 号「验收阶段修正」②）**：对照设计稿 05 / 07，在 390×844 + 47/34 安全区下设置行只露约 2.5 行（设计稿 4 行）。量设计稿像素后逐段收紧：全局 Sheet 把手区 20 → 14px；记一笔 Sheet 的标题栏 / 底栏 / 金额区 / 分类网格 / 设置行间距；令牌 `--ft-cat-size` 52 → 48、`--ft-cat-row-height` 84 → 77、`--ft-cell-height-compact` 46 → 44、`--ft-key-height` 50 → 48（设计稿 50 / 42，为了四行放得下与 44pt 点按面积下限留的折中）。分类网格的行高改成 `minmax(令牌, auto)`（系统字号放大时名称行长高，不再被固定行高裁掉）。

### 待人工验收（设备 / 模拟器）

- [ ] 对照设计稿 04 与 screens 的 05-add-expense、07-add-income，在 Android 与 iOS（CI 产物）上确认：标题栏（✕ / 「记一笔」/ 麦克风）、「支出 \| 收入」分段、金额大字与光标、分类网格（5 列、分页点、虚线圆「新建分类」）、四行设置（图标 / 标题 / 值 / 箭头）、右侧高大的保存键、置灰态、账本 / 支付渠道弹出菜单、放弃确认 Alert
- [ ] **Sheet 升起与背景缩放手感**：整页 `transform` + 圆角 + `overflow: hidden` 在 WKWebView / 低端 Android WebView 上是否掉帧；升起 / 收起的弹簧曲线、背景变暗的时机；Sheet 下拉收起手势与分类网格横向滑动是否互相抢手势
- [ ] **备注的系统键盘与自定义键盘之间的切换**：聚焦备注时系统键盘是否会顶起 / 遮挡 Sheet、自定义键盘让位与恢复是否闪烁、回车 / 失焦后恢复是否及时；金额区在系统键盘弹出时是否仍可见
- [ ] **键盘手感**：按键点按反馈与触感、长按退格清空的时长是否顺手、连点速度、误触（保存键在右侧高大）；金额区字号收缩的观感
- [ ] **时间**：「时间」行点按是否稳定唤起 iOS / Android 的原生日期时间选择器，选完是否回到 Sheet 且值正确
- [ ] **语音**：真机上按住麦克风说话（标题栏麦克风 + 顶部状态行），识别后预填金额 / 分类 / 渠道 / 时间 / 备注，保存后数据来源为语音识别；麦克风权限被拒时的提示
- [ ] 弹出菜单（账本 / 支付渠道）在小屏与不同安全区上的定位：下方放不下时是否翻到上方、限高滚动是否顺手、点外面收起是否会误触下面的行
- [ ] Android 系统返回键（目前不会先收起 Sheet，原记在 23 号名下，**23 号没有处理**，仍待单独立项）与 iOS 左边缘右滑在 Sheet 打开时的表现
- [ ] **判断项（请一并确认）**：呈现方式为覆盖层（第 1 条）；自建分类排在预设之前（第 2 条）；账本菜单不列只读成员的共享账本（第 3 条）；切类型自动选默认分类（第 4 条）；深链参数不剥离（第 6 条）
