# 09 — 账单筛选 Sheet 与批量归属账本

**What to build:** 账单的筛选改为半屏 Sheet，当前生效的条件以芯片形式展示在列表上方；批量选择模式与批量「归属账本」也按新界面重做。

**Blocked by:** 08 — 账单 Tab：列表

**Status:** completed（自动化验收全部通过——`dotnet build` 0 警告 0 错误、`dotnet test` 907/907、`run_all.py` 12/12（`bills_page_test.py` 由 150 项重写为 233 项）；设备 / 模拟器上的人工验收待办，见下方「待人工验收」）。**第 2 轮评审后（用户拍板：分类多选、默认「本月」、查看 0 笔不置灰、只读成员不能删除 / 批量归属）`bills_page_test.py` 重写部分用例、含 10 号的搜索共 297 项**，下文的计数是各自完成当时的快照，差异见「与 ticket 文字的差异」前三条。

- [x] 筛选 Sheet（半屏）：时间（今天 / 本周 / 本月 / 本年 / 自定义范围）、账本（含「未归属账本」）、分类、支付渠道；一键重置；确认按钮显示符合条件的账单笔数，随选择实时更新，口径与 08 的汇总查询一致——`BillFilterSheet`，底部「查看 N 笔账单」每次改草稿都经页面传入的 `CountAsync`（`GetBillCountAsync`，与列表 / 汇总共用 `BuildBillQueryAsync`）重新估算
- [x] 筛选芯片行：展示生效中的条件（账本、月份、分类等），点按可调整，可单个移除；与汇总卡、列表联动；时间按应用时区，区间左闭右开——`BillFilterChip` × 4 组成条件条（始终四颗，已设置的排前面）；时间区间的取舍见「与 ticket 文字的差异」第 5 条
- [x] 移除 08 暂时保留的旧筛选面板；现有筛选维度与语义不变——临时面板、`bills-filter-*` 钩子与批量归属 `MDialog` 全部移除；四个维度与账单类型分段控件的语义不变（结束日含当天、共享账本可见性、「重置」不清账单类型）
- [x] 批量选择模式：从右上角进入，可多选账单（含全选），底部操作栏「归属账本」→ 选择账本（必填）→ 批量更新 → Toast；沿用现有权限规则（只能操作有权编辑的账单）与「未归属账本」语义——底部悬浮操作栏（全选 / 清空 / 已选 N 笔 / 归属账本）+ `AssignLedgerSheet`；成功 Toast「已将 N 笔账单归属到「X」」
- [x] 筛选与批量状态在切换 Tab 后保持（沿用 Tab 保活）——E2E 的 E3 段与 `tab_shell_test.py` D 段
- [x] 自动化验收（E2E，手机视口）：账本、时间预设、分类、支付渠道各筛选一遍，列表、汇总卡、芯片、确认按钮上的笔数一致；重置；移除单个芯片；批量选择 → 归属账本 → 账单所属账本更新——`bills_page_test.py` 233 项（E1–E4、F 段），含落库断言
- [x] 批量归属需要「历史上没有账本的账单」作为前置数据：使用基座（01）提供的造数 helper 写入；若仍无法稳定造出，则该项降级为人工验收，并在 ticket 中说明原因——用 `LocalDb` 直接写入 `LedgerId = NULL` 的账单，稳定可造，没有降级
- [ ] 人工验收（**待人工**，需要设备 / 模拟器，见下方清单）：对照设计稿 05 与 screens 的 13-filter；Sheet 弹起 / 收起、芯片换行与溢出表现；Android 与 iOS（CI 产物）
- [x] 回归：既有 E2E 与 `dotnet test` 全部通过，`dotnet build` 零错误

## 实现说明

- 覆盖 spec 用户故事：41、42、45、47。
- 设计稿：05 · 二级页面与弹层；screens 的 13-filter。
- 批量「归属账本」用于补齐历史上没有账本的账单；新建 / 编辑账单时账本已是必填，界面不能再造出无账本的账单。
- 术语：「未归属账本」「归属账本」。

### 现有功能点对照（迁移前 = 08 号的临时面板与批量对话框，迁移后逐项核对）

| 迁移前（08 号）的功能 | 迁移后的位置 / 行为 | E2E 段（`bills_page_test.py`） |
|---|---|---|
| 导航栏筛选按钮：角标 = 面板里生效的条件数，`aria-expanded` | 同一颗按钮（`aria-haspopup="dialog"`，Sheet 是模态，不再有「展开」态）；角标 = 生效的维度数（时间 / 账本 / 分类 / 支付渠道各算一项，账单类型不计）；点按打开 Sheet | A、E1 |
| 临时面板：开始 / 结束日期（结束日含当天），日期框防抖 400 ms 后自动查询 | 「时间」组：今天 / 本周 / 本月 / 本年 + 「自定义」（起止日期，含当天，可只填一端）；Sheet 里改的是**草稿**，点「查看 N 笔账单」才生效，不再需要防抖 | E1、E2 |
| 账本下拉：自有账本 + 共享账本 | 「账本」组：全部 / 各账本（共享的标签带「（共享）」）/ **未归属账本（新增）**；三选一，再点已选的 = 取消 | E2、E4 |
| 分类下拉、支付渠道下拉 | 「分类」组标签**可多选**（再点已选的 = 取消这一个；第 2 轮评审），「支付渠道」组标签单选（再点 = 取消）；账单类型限定为支出 / 收入时分类只列对应的一类（已选中的除外） | E2 |
| 任一条件变化立即查询 | 草稿 + 确认：✕ / Esc / 遮罩 / 下拉把手 = 放弃；点「查看」时条件没变只收起、不重新查询 | E1 |
| 「重置筛选」：只清面板条件，保留账单类型 | Sheet 左上「重置」：草稿里的四个维度回到**默认视图**（时间「本月」、其余不限；账单类型仍由分段控件管）；已经是默认视图时置灰（不限时间不是默认，「重置」可点） | E1、E2 |
| （迁移前没有）生效条件的展示 | **新增**条件条：夹在分段控件与汇总卡之间，四颗标签一行排开、可横向滑动；已设置的排前面，点文字 = 调整（打开 Sheet 并定位到该组），点 × = 单独移除 | E1、E2 |
| 空状态「没有符合条件的账单」+「清除筛选」（清掉含账单类型在内的全部条件） | 同（清掉的是真正的条件，默认的「本月」保留；偏离默认的时间条件如「本年」也算条件，清掉后是「不限时间」）；只受「本月」限制时另有「本月还没有账单」+「查看全部账单」 | A、E2 |
| 批量操作栏（吸顶）：全选 / 清空 / 已选 N 笔 / 归属账本 | 底部悬浮操作栏（Tab 栏之上，非玻璃）；同四项；全选仍只含自己记的账单 | F |
| 归属账本 `MDialog`：下拉选自己的账本，只有一个时预选，未选时确定按钮禁用 | `AssignLedgerSheet`：单选列表，只列自己的账本，规则同左；没有自己的账本时给引导文案；提交中不可收起 | F |
| 筛选与批量状态切 Tab 后保持 | 保持（页面栈保活）；筛选条件常驻，已选账单随筛选结果剔除（`PruneSelection`） | E3 |
| （迁移前没有）条件指向的对象没了 | **新增**：被别处删除的账本 / 分类 / 支付渠道、退出的共享账本，在加载选项时自动移出条件（共享账本没读成功时不移） | E3 |

### 完成记录（agent 实现）

**交付物**

| 层 | 文件 | 作用 |
|---|---|---|
| 页面 | `Pages/Bills.razor` | 删除临时面板与归属 `MDialog`；条件条（四颗 `BillFilterChip`，`ChipOrder` 让已设置的排前面）、两个 Sheet 的宿主、底部操作栏；`OpenFilter(focus)`、`RemoveConditionAsync`、`ApplyFromSheetAsync`（条件没变只收起）、`PruneStaleConditions`、`PruneSelection`、`CountForSheetAsync`（经 `PageDataGate`）；多选时根节点加 `ft-bills--selecting` |
| 组件 | `Components/Bills/BillFilterSheet.razor` · `BillFilterChip.razor` · `AssignLedgerSheet.razor` | 筛选 Sheet（草稿 / 笔数估算 / 定位到某一组）、条件标签（未设置 / 已设置两种形态）、归属账本 Sheet |
| 展示规则（纯函数） | `Models/BillListFilter.cs`（`BillFilterDimension`、`BillTimePreset`、`BillTimePresets`；`Has` / `Clear(dimension)` / `SelectPreset` / `SetCustomRange` / `SelectLedger` / `SelectUnassignedLedger` / `Clone` / `CopyFrom` / `SameConditionsAs` / `ToQuery(nowLocal)`）· `BillListPresentation.cs`（条件条与 Sheet 文案、`CustomRangeText`、`DimensionName`）· `LedgerModel.DisplayText`（共享账本带「（共享）」）· `MdiIcons.cs`（`RetiredIcons`） | 页面与组件里不散落字符串拼接和条件判断 |
| 数据层 | `Core/Models/BillModels.cs`（`BillFilter.UnassignedLedger`）· `BillService.BuildBillQueryAsync` 里一个 `Where` 分支 | 列表 / 总数 / 汇总同时生效 |
| 样式 | `wwwroot/css/ui.css`（`ft-chip` 系、条件条 `ft-chiprow`、`ft-filtersec`、`ft-actionbar`、`ft-bills--selecting`）· `css/tokens.css`（`--ft-chip-*`、`--ft-actionbar-*`、`--ft-shadow-bar`、`--ft-z-actionbar`）· `wwwroot/js/ui.js`（`scrollSheetTo`） | |
| 测试（单元） | `BillListFilterTests`（58 项：预设换算含跨午夜 / 周一为一周之始 / 平闰年、维度互斥、`ToQuery`、副本与 `SameConditionsAs`、条件条 / Sheet / 归属的文案、选项映射）· `BillSummaryTests`（`UnassignedLedger`：与列表同口径、与其它条件取交集、与具体账本互斥、排除软删除 / 他人账单 / 共享账本里的账单、归属后离开）· `DesignTokensTests`（标签热区、操作栏高度、层级先后）· `BillPresentationTests`（`MdiIcons`：已下线图标替换、预设图标经 `Normalize` 后都在字形清单里） | 共新增 76 项，累计 907 |
| 测试（E2E） | `tests/e2e/bills_page_test.py`（233 项，段 A–H，其中 E1–E4 是本 ticket 重写的筛选部分，F 是批量归属）；`tab_shell_test.py` D 段改用新 Sheet 设置条件；`e2e_base.py` 新增 `LocalDb.channel_id_by_name` / `soft_delete_ledger`；`README.md` 登记 `bills-chip-*`、`filter-*`、`bills-batchbar`、`assign-*` 钩子 | 见下 |
| 文档 | `docs/adr/0007-ios-visual-system.md` 第 11 点与已知限制 | 条件条、草稿语义、操作栏、玻璃预算例外、图标字形 |

**契约（后续 ticket 直接使用）**

- **10 号搜索**：① `BillFilter` 加 `Keyword`，`IsEmpty` 要把它算进去；`BuildBillQueryAsync` 里加**一个** `Where` 分支（列表、总数、汇总同时生效）。② `BillListFilter` 加 `Keyword`：`ToQuery` 要传、`Clone` / `CopyFrom` / `SameConditionsAs` 都要包含——否则 Sheet 的草稿会丢掉关键字，或「只改了关键字」被 `ApplyFromSheetAsync` 当成「条件没变」而不查询。③ 关键字不是 `BillFilterDimension`（四个维度是 Sheet 的四组标签）；若要在条件条上展示，**复用 `BillFilterChip`**（`Value` = 关键字、`OnRemove` = 清关键字），不要再造一种标签；`HasAny`（空状态说「没有账单」还是「没有符合条件的账单」）要算上关键字。
- **筛选 Sheet**：`<BillFilterSheet Visible VisibleChanged Applied Focus Ledgers Categories Channels InitialCount CountAsync OnApply OnClosed>`；`Applied` 只在「从隐藏变为显示」那一刻被复制成草稿；`CountAsync` 必须与页面汇总同一份 `BillFilter`，且自己经 `PageDataGate` 排队。
- **条件标签**：`<BillFilterChip Dimension Value OnOpen OnRemove TestId>`；`Value` 为空 = 未设置形态。
- **归属 Sheet**：`<AssignLedgerSheet Visible VisibleChanged SelectedCount Ledgers Busy OnConfirm OnClosed>`，`Ledgers` 只能是自己的账本。
- **筛选状态**：`BillListFilter.ToQuery(nowLocal)`（预设按传入的「现在」换算，单元测试可注入）；`PanelCount` / `Has(dimension)` / `HasAny` 的口径见 XML 注释。
- **通用件**：`ChoiceChip`（Sheet 里的可选标签，`Selected` / `Icon` / `Tone`）、`BottomSheet`（`Dismissible`、`HeaderStart`、`Footer`）、`GlassButton`（`Busy` / `Disabled` / `Large` / `Block`）——15 号「记一笔」Sheet、14 号分类 / 渠道编辑可直接复用。
- **E2E 钩子**：见 `tests/e2e/README.md`（新增行）与 `bills_page_test.py` 文件头。要点：关闭 Sheet 后等 `wait_for(state="detached")`（退场 260 ms）；点标签后用 `expect(...).to_have_attribute("data-selected", ...)` 等 DOM 更新，不要点完立刻读状态（见「实现要点」第 1 条）；`wait_apply_count` 等 `filter-apply` 的 `data-count` 变成期望值。

**与 ticket / 设计稿的差异（均已评估）**

1. **分类可多选**（**第 2 轮评审，用户拍板**；最初实现为单选，按 ticket「现有筛选维度与语义不变」保守处理，设计稿 13 的示意图里分类有两颗高亮「餐饮美食」「交通出行」）：条件对象改成 `BillFilter.CategoryIds`（满足其中任意一个即可，`IN` 查询；REST 的 `categoryId` 单值参数不变，服务端包成单元素列表）；`BillListFilter.ToggleCategory` 切换、`RetainCategories` 只保留仍存在的分类（别处删了分类后自动移出条件）；条件条上多选时写「第一个的名字等 N 项」（读屏名称「分类：餐饮美食等 2 项，点按调整」），一个维度一个 ×（一起移除）、角标只算 1 项；账单类型限定为支出 / 收入时，不属于它的已选分类自然不再列出。
2. **默认视图是「本月」，不是不限时间**（**第 2 轮评审，用户拍板**；最初实现沿用迁移前「默认不限时间」，设计稿 13 / 02 里时间标签都是「本月」）：账单 Tab 打开 = 本月，**默认的「本月」不算筛选条件**——不计入角标数、条件条上是中性的「本月 ⌄」（读屏名称「时间：本月，点按调整」，`data-active="false"`）、Sheet 的「重置」回到它（并且已经是默认视图时置灰）。取消「本月」（Sheet 里再点一次选中的预设，或条件条上点已设置时间标签的 ×）= **不限时间**：标签变成「时间」、同样不是条件、不计角标，但 Sheet 的「重置」可点（它偏离了默认）。空状态：只受「本月」限制时是「本月还没有账单」+「记一笔」+「查看全部账单」（后者 = 取消时间范围），不再笼统说「还没有账单」（更早的月份里可能有）；「清除筛选 / 清除搜索」只去掉真正的条件，**不顺手把默认的「本月」取消**。首页「查看全部」进来看到的是本月（口径变化，见 07 号）。纯逻辑在 `BillListFilter.DefaultTimePreset / IsDefaultTime / HasTimeCondition / IsDefaultPanel / ResetPanel / ClearConditions`，单元测试锁定。
3. **没有符合条件的账单时「查看」照常可点**（**第 2 轮评审，用户拍板**；最初实现 0 笔时置灰并写「没有符合条件的账单」）：按钮始终是「查看 N 笔账单」（设计稿 13），N 为 0 也如此、也可点——点了之后账单页给出「没有符合条件的账单」的空状态与「清除筛选」入口；笔数还没估出来 / 估算失败时是「查看账单」。一笔账单都没有的新用户，Sheet 里的按钮也不再是灰的。
4. **玻璃层：打开 Sheet 时同屏是 4 层**（Tab 栏 + 导航栏两个圆钮 + Sheet 关闭按钮），比 ADR 0007 §3 的「每屏 ≤ 3」多一层；操作栏与「重置」都刻意不用玻璃。收起后回到 3 层。是否在 Sheet 打开期间统一让页面玻璃层降级，留给 23 号（**23 号已统一处理**：任何 Sheet 升起期间，被盖住的页面与 Tab 栏关掉毛玻璃——`<html data-ft-sheet-open>`，见 ADR 0007 §26；此后打开 Sheet 同屏 ≤ 3 层，E2E 的 S 节量过）。
5. **时间区间的右端：沿用「含端点的当天最后一刻」**：ticket 写「区间左闭右开」，列表筛选沿用 08 号契约（`BillFilter.EndDate` 含端点，取 `TimeBoundaries.EndOfDay`）；到 tick 的精度下，「≤ 当天最后一刻」与「< 次日零点」选出的账单完全相同，只是表达方式不同。统计一侧仍是左闭右开，两处没有口径差。
6. **导航栏仍是三个按钮**（刷新 / 筛选 / 多选），沿用 08 号的取舍（见 08 号差异第 1 条）；条件条上「时间」标签的文案：预设显示名称（今天 / 本周 / 本月 / 本年），自定义范围显示成「M月D日至M月D日」（跨年带年份，只填一端写「起」/「至」）。
7. **批量操作栏是底部悬浮、不是吸顶**：ticket 写「底部操作栏」，08 号的临时实现是吸顶，现改到 Tab 栏之上（`--ft-z-actionbar` 90 < Tab 栏 100）。

**实现要点 / 发现**

1. **Blazor Server 的事件往返让「点完立刻读 DOM」必然读到旧状态**：E2E 里 `.click()` 返回时服务端可能还没处理完，DOM 还是旧的；`wait_apply_count` 在笔数没变（本月 9 笔 → 本年 9 笔）时也会立即返回。首轮 8 处失败全是这一类测试竞态，不是产品缺陷。统一成 `toggle_chip`（点击 + 等 `data-selected` 变化）；事件在服务端按序处理，所以后续点击是安全的。需要「刷新后再断言」的地方用「种一笔标记账单 + 刷新 + 等汇总」做同步点（刷新会先重读账本 / 分类 / 渠道选项，再读账单）。
2. **`mdi-alipay` 在 `@mdi/font` 7.2.96 里不存在**（19 个预设图标逐个核对，只有它缺），「支付宝」标签的图标渲染成空白——设计稿 13 里它有图标。`MdiIcons.Normalize` 把它映射到 `mdi-qrcode-scan`；预设数据里的名字没改（已同步进用户库的历史数据不用迁移）。E2E 在字体已加载时逐个检查标签图标的 `::before` 有内容（字体来自 CDN，离线 / 未加载时跳过这项）。
3. **草稿只在「从隐藏变为显示」的那一刻复制**：之前页面重绘（后台刷新了账本列表）会冲掉用户正在调的条件；`Visible && !_wasVisible` 守住。归属 Sheet 的预选同理。
4. **笔数估算的过期结果用版本号丢弃**：连续点标签时有多次查询在途，只有最后一次生效；估算失败不拦用户（按钮退回「查看账单」）。
5. **预设存的是预设本身，不是日期**：`BillListFilter.ToQuery(nowLocal)` 在查询时才换算，常驻的账单 Tab 过了午夜再刷新，「今天」跟着变；单元测试注入「现在」覆盖跨午夜、周一为一周之始、年末。
6. **「未归属账本」分支必须放在账本分支之后**：共享账本分支会把整个查询起点换掉（`_context.Bills.Where(...)`），放在前面会被覆盖。与 `LedgerId` 同时给出时按交集处理（必然为空）。
7. **已设置的标签不能是「按钮里套按钮」**：无效 HTML，WebView 会重排 DOM；做成 `span[role=group]` 里的两个按钮，各自 44 高的命中区域，胶囊用伪元素画在中间（视觉 36 高）。
8. **悬空条件**：条件指向的账本 / 分类 / 渠道没了（别处删除、退出共享账本）会让结果必空又没有名字可显示——加载选项时移除；共享账本没读成功时不能据此移除（「没读到」不等于「不存在」）。
9. **Sheet 退场动画 260 ms**：Sheet 关闭后元素要等动画结束才从 DOM 移除，E2E 等的是 `detached`（不是 `hidden`），再去点页面上的控件。
10. **阴影与 z 序**：`--ft-z-actionbar` 90 < Tab 栏 100 < Sheet 120 < Alert 130 < Toast 1100；操作栏底边 = Tab 栏上沿 − 10，多选时页面多留出 56 + 10 的底部空间，最后一笔账单能滚到它之上。
11. **本机环境**：Web.Server 运行时 `dotnet build FinanceTracker.sln` 会因 DLL 被占用复制失败，先 `server.ps1 -Action stop`；改 C# / Razor 要重启服务，静态 JS / CSS 即时生效。
12. 回归：`dotnet build FinanceTracker.sln` 0 警告 0 错误；`dotnet test` 907/907（本 ticket 新增 76 项）；`run_all.py` 12/12（`bills_page_test.py` 233 项、`home_page_test.py` 93 项、`ledgers_page_test.py` 147 项、`ledger_members_page_test.py` 138 项、`tab_shell_test.py` 64 项、`tab_slider_test.py` 82 项、`immersive_safe_area_test.py` 35 项、`e2e_base_test.py` 120 项等通过）。

- **验收阶段修正（24 号「验收阶段修正」④）**：对照设计稿 13，超长账本名的芯片原先溢出 Sheet 右缘、「时间」一行的 5 个芯片折成两行。现在 `.ft-chips > .ft-chip { max-width: 100% }`（长名字自己省略）、`.ft-chiprow > .ft-chip { max-width: 85% }`，`--ft-chip-pad` 14 → 12，五个时间芯片在 390 宽下一行放得下。

### 待人工验收（设备 / 模拟器）

- [ ] 对照设计稿 05 与 screens 的 13-filter，在 Android 与 iOS（CI 产物）上确认：Sheet 半屏高度与圆角、「重置」「✕」的位置与点按范围、标签的字号 / 间距 / 换行（分类、支付渠道很多时换成多行的表现）、底部主按钮的渐变与外发光；已设置态标签的配色
- [x] ~~判断项：分类单选 / 0 笔置灰 / 默认不限时间~~——第 2 轮评审已由用户拍板：分类多选、0 笔照常可点、默认视图「本月」（见差异第 1–3 条）
- [ ] 真机上确认 3 个新行为的观感：分类多选时条件条「餐饮美食等 2 项」的截断；默认「本月」下首页「查看全部」进来的账单范围；「查看 0 笔账单」点了之后的空状态与「清除筛选」
- [ ] Sheet 弹起 / 收起手感：弹簧、下拉把手拖拽收起的跟手度；日期框弹出 iOS / Android 原生日期选择器时 Sheet 的避让与按钮是否被键盘遮挡
- [ ] 条件条横向滑动：惯性、与页面纵向滚动互不干扰、在 iOS 上与系统「边缘返回」手势不冲突；已设置标签的 × 点按精度
- [ ] 多选操作栏：在手势条 / 三键导航等不同底部安全区下与 Tab 栏的间距、遮挡、悬浮阴影的观感；归属 Sheet 的勾选反馈、归属后的 Toast
- [ ] 打开 Sheet 时的 4 层玻璃在中低端 Android 上的帧率与掉帧情况（`backdrop-filter` 在较老的 WebView 上可能不被支持，降级由 23 号统一处理）
- [ ] 「支付宝」图标 `mdi-qrcode-scan` 是否被接受，还是换成品牌图标 / 别的字形（见实现要点第 2 条）
