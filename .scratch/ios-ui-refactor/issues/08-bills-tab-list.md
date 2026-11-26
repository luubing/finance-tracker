# 08 — 账单 Tab：列表

**What to build:** 「账单」成为独立的一级 Tab，列表按设计稿重做：大标题（滚动后折叠为居中小标题）、全部 / 支出 / 收入分段、汇总卡、按日分组、行内左滑「编辑 / 删除」。汇总卡显示当前筛选条件下全部账单的支出、收入与笔数（全量统计，不再只是已加载的那几页）。筛选暂时沿用现有筛选面板（09 替换），搜索由 10 增加。

**Blocked by:** 07 — 首页重做

**Status:** completed（自动化验收全部通过——`dotnet build` 0 警告 0 错误、`dotnet test` 831/831、`run_all.py` 12/12（新增 `bills_page_test.py` 150 项）；设备 / 模拟器上的人工验收待办，见下方「待人工验收」）。**第 2 轮评审后账单 Tab 默认只看「本月」（用户拍板，见差异 #7），`bills_page_test.py` 现为 297 项（含 09 / 10 号的筛选与搜索）**，下文的计数是 08 号完成当时的快照

- [x] 开工前先列出现有账单页的全部功能点并写入本 ticket 的实现说明，迁移后逐项对照（筛选维度、批量选择与「归属账本」、加载更多、编辑、删除、「未归属账本」标注、共享账本账单的权限、数据来源标识等）——见「实现说明 · 现有功能点对照」
- [x] 新建复用组件：大标题页（滚动后折叠为居中小标题 + 右侧玻璃按钮）、分段控件——`LargeTitlePage`、`SegmentedControl`（+ `SegmentOption`）
- [x] 数据层：新增只读的「账单汇总」查询——给定与列表相同的筛选条件，返回支出合计、收入合计与笔数；列表与汇总使用同一套条件，汇总不受分页影响；遵守软删除与账本可见性（含共享账本）——`IBillService.GetBillSummaryAsync(userId, BillFilter)`，列表 / 总数 / 汇总共用 `BuildBillQueryAsync`
- [x] 汇总卡显示支出 / 收入 / 笔数（口径＝当前筛选下的全部账单）；分段（全部 / 支出 / 收入）与汇总、列表联动
- [x] 按日分组：组头显示日期、星期与当日收支合计（沿用现有口径，基于已加载账单）；日期按应用时区（AppTimeZone），业务代码不直接调用 ToUniversalTime / ToLocalTime
- [x] 行内左滑「编辑 / 删除」，并有等价入口（点按行进入编辑页，其中含删除）；删除（软删除）前用 Alert 确认，并随同步传播；没有写权限的账单（如只读共享账本成员）不提供编辑 / 删除入口，或给出明确的「无权限」提示，且不会被登出
- [x] 编辑暂时仍进入现有编辑页（由 15、16 替换为记一笔 Sheet）
- [x] 加载更多、空状态（没有账单 / 筛选无结果）、下拉刷新（只刷新本页数据）；现有筛选面板暂时保留并保持可用，接入新的汇总
- [x] 金额颜色：支出用常规文字色带负号，收入用绿色带正号；历史无账本的账单标注「未归属账本」
- [x] 自动化验收（xUnit，使用真实 SQLite 迁移）：汇总在各筛选条件（时间范围、账本、分类、支付渠道、账单类型）下与列表条件一致；不受分页影响；排除软删除；共享账本可见性——`BillSummaryTests` 21 项
- [x] 自动化验收（E2E，手机视口）：在唯一账本下创建若干账单，账单 Tab 的汇总与所建账单合计一致（用该账本筛选隔离数据）；分段切换；按日分组；左滑删除；点按进入编辑；软删除后列表与汇总同步变化——`bills_page_test.py` 150 项
- [ ] 人工验收（**待人工**，需要设备 / 模拟器，见下方清单）：对照设计稿 01 与 screens 的 02-bills；大标题折叠、左滑手感；Android 与 iOS（CI 产物）
- [x] 回归：既有 E2E 与 `dotnet test` 全部通过，`dotnet build` 零错误

## 实现说明

- 覆盖 spec 用户故事：4、34、35、36–39、43、44、46–48。
- 设计稿：01 · 四个 Tab 根页面；screens 的 02-bills。
- 日分组头的合计沿用现有口径（基于已加载账单），与汇总卡的全量口径不同，这是 spec 里刻意保留的差异。
- 本 ticket 引入的汇总查询是之后 09（确认按钮上的笔数）与 10（搜索）共用的条件对象，设计时要留出扩展关键字条件的余地。
- 术语：「账单」「软删除」，界面按钮文案仍用「删除」。

### 现有功能点对照（迁移前列出、迁移后逐项核对）

迁移前的账单页（`PStackPageBar` + `PDrawer` 筛选抽屉 + `MContainer` 里的统计卡 / 分组 `MCard` + 两个 `MDialog`；是从首页「查看全部」压栈出来的二级页面）具备的全部功能：

| 迁移前账单页的功能 | 迁移后的位置 / 行为 | E2E 段（`bills_page_test.py`） |
|---|---|---|
| 顶栏：标题「账单列表」、`＋` 记一笔、刷新（转圈、加载中禁用）、筛选（开抽屉）、多选开关 | 大标题页导航栏：筛选（玻璃圆钮，带角标与 `aria-expanded`）、多选（玻璃圆钮，`aria-pressed`）；**刷新按钮已撤掉**（验收后修订，见 07 号「验收后修订」，与设计稿一致只有这两个）；`＋` 不再出现——Tab 栏右侧「记一笔」圆钮覆盖它，空状态里也有「记一笔」入口 | A、B |
| 筛选抽屉：开始 / 结束日期（结束日含当天）、分类、支付渠道、账本（自有 + 共享）、账单类型、「查询」「重置」 | 日期 / 账本 / 分类 / 支付渠道在**临时筛选面板**里（09 号换成 Sheet）：任一条件变化自动查询（日期框防抖 400 ms），没有「查询」按钮；「重置筛选」只清面板条件；账单类型移到**分段控件**（全部 / 支出 / 收入）；账本下拉仍是自有 + 共享，结束日期仍含当天（`TimeBoundaries.EndOfDay`）；筛选按钮角标 = 面板里生效的条件数（日期范围算一项） | B、E |
| 统计卡：总支出 / 总收入——**只加总已加载的账单** | **汇总卡**：支出 / 收入 / **笔数**，口径 = 当前筛选下的**全部**账单（`GetBillSummaryAsync`，不受分页影响）；分段、筛选、删除、归属后都随之变化 | B、C、G |
| 按日分组：`yyyy年MM月dd日` + 当日支出 / 收入合计（基于已加载账单） | 组头：今天 / 昨天 / `11月23日`（跨年带年份）、星期、当日合计（仍基于已加载账单——与汇总卡的口径不同，是 spec 刻意保留的差异）；日期按应用时区（`AppTimeZone`） | B、G |
| 行：+/- 图标、「分类 - 备注」、支付渠道图标 + 名、账本 chip、`HH:mm`、金额（红 / 绿带符号）、行尾垃圾桶 | `BillRow`（07 号）：柔和图标块、「分类 · 备注」、「支付渠道 · 账本」、金额与时间两行；支出常规文字色带负号、收入深绿带正号；历史无账本标「未归属账本」；行尾垃圾桶**移除**，换成左滑「删除」 | B |
| 点按行：进入编辑页（多选模式下切换选中） | 同（编辑页仍是现有 `/add-bill/{id}`，由 15、16 号替换）；**只读账单**（共享账本里别人记的）点按给「这笔账单由账本里的其他成员记录，只有记录它的人可以修改或删除」提示（Snackbar，不是错误、不登出）；多选模式下只读行置灰不可选 | B、E |
| 删除：确认对话框 → `DeleteBillAsync`（软删除）→ Snackbar「账单已删除」→ `BillChanged` → 按已加载页数重载；账单已不存在时给警告 | 左滑「删除」→ **GlassAlert**，点名是哪一笔（「分类 · 备注，金额」）并说明会随同步传播 → 软删除（`IsDeleted = 1`，同步状态变回「待同步」）→ Toast → `BillChanged`（本页按已加载页数重载，列表与汇总同步）；「账单不存在或已删除」「删除失败」的提示保留 | C |
| （迁移前没有）行内左滑 | **新增**：左滑「编辑 / 删除」（`SwipeRow`，05 号）；等价入口 = 点按整行进入编辑页（页内可删除）；多选模式与只读行不可左滑 | C |
| 批量选择：全选、已选 N 笔、清空、「归属账本」 | 吸顶的批量操作栏（全选 / 清空 / 已选 N 笔 / 归属账本）；全选**只含自己记的账单**；换筛选或删除后，已选里不在列表中的账单自动剔除（09 号重做为底部操作栏） | E、F |
| 归属账本对话框：目标账本（可清空 = **移出账本**）→ `AssignBillsToLedgerAsync` → Snackbar → `BillChanged` → 重载 | 对话框保留（09 号换成 Sheet）；**账本必选**——「不选 = 移出账本」取消（CLAUDE.md「账本必填」，服务层 `ledgerId` 已是非空 `Guid` 且必须是自己的账本）；目标下拉只列自己的账本，只有一个时预选；未选账本时确定按钮禁用 | F |
| 加载更多：按钮 +「本页取满 20 笔才有下一页」 | 同（每页 20 笔）；追加不跳回顶部、按 Id 去重；整页重载时一次取够已加载页数的账单，删除 / 刷新后不丢页 | G |
| 加载中：整页转圈 | 骨架屏（首次进入 / 换账号）；之后改条件、刷新保留旧内容直到新数据到达，不闪烁 | A、D |
| 空状态：（迁移前没有） | **新增**：只受默认的「本月」限制 →「本月还没有账单」+「记一笔」+「查看全部账单」（第 2 轮评审：默认视图改为本月，不能笼统说「还没有账单」）；取消时间范围后一笔都没有 →「还没有账单」+「记一笔」；筛选无结果 →「没有符合条件的账单」+「清除筛选」（清掉含账单类型在内的全部条件，默认的「本月」保留） | A、E |
| 刷新：只有顶栏刷新按钮（顺带重读筛选下拉的数据） | **下拉刷新**（手势）取代刷新按钮（验收后修订：实现时曾先保留按钮、与下拉共用同一套加载，用户拍板撤掉按钮）；加载含筛选下拉数据，**只重读本页数据、不触发云端同步** | D |
| 共享账本账单的可见性：筛选共享账本时账本内全部成员的账单可见（`BillService` 处理）；共享账本列表读本地缓存，失败不影响自有账本 | 沿用，`BuildBillQueryAsync` 原样保留这段规则；共享账本下列表 / 总数 / 汇总口径一致（`BillSummaryTests`）；共享账本列表加载失败只记日志 | E |
| 登录态：`GetUserIdAsync() ?? Guid.Empty`，未登录时照常渲染空列表；不订阅 `AuthStateChanged` | 未登录（退出后 Tab 根页面仍留在页面栈里）不渲染任何账单；订阅 `AuthStateChanged`，换账号先清掉上一个用户的数据并回到骨架 | A（登录后首次进入） |
| 变更后刷新：不订阅 `BillChanged`（从本页的 `＋` 记一笔返回后列表不会更新），只在本页自己删除 / 归属后重载 | 订阅 `BillChanged`（记一笔 / 编辑 / 导入 / 本页删除与归属都会按已加载页数重载）；**新增**二级页面关闭后重新加载（账本 / 分类 / 支付渠道 / 导入 / 待确认账单都可能改了这里展示的数据） | B、C |
| 数据来源标识 | 迁移前账单页**没有**展示数据来源，设计稿 02 也没有这一项——本 ticket 不新增 | — |
| 并发读库：无保护（二级页面同一时刻只有它在读） | 四个 Tab 根页面常驻，一次账单变更让首页 / 账单页 / 统计页同时重载，而 EF Core 不允许同一上下文并行查询 → 本页读库全部经过 `PageDataGate`（与首页、月度统计共用） | C、D |

### 完成记录（agent 实现）

**交付物**

| 层 | 文件 | 作用 |
|---|---|---|
| 页面 | `Pages/Bills.razor` | 重写：`LargeTitlePage`（导航栏三个按钮）→ 分段 → 临时筛选面板 → 批量操作栏 → 汇总卡 → 按日分组（`SwipeRow` + `BillRow`）→ 加载更多；`GlassAlert` 删除确认、临时的归属账本 `MDialog`；取数、版本号取代排队中的旧加载、事件订阅、已选剔除、换账号清数据 |
| 组件 | `Components/Ui/LargeTitlePage.razor` · `SegmentedControl.razor` · `SegmentOption.cs`；`BillRow.razor`（增加多选态 `Selectable` / `Selected` / `Disabled`） | 大标题页（统计、我的复用）与分段控件（统计的月度 / 趋势 / 年度复用） |
| 展示规则（纯函数） | `Models/BillListPresentation.cs`（按天分组、组头文案、空状态、分段取值、选择与删除文案、只读提示）· `BillListFilter.cs`（筛选状态 → `BillFilter`，`PanelCount` / `HasAny`） | 页面里不散落字符串拼接与条件判断 |
| 数据层 | `Core/Models/BillModels.cs`（`BillFilter`、`BillSummary`）；`IBillService` / `BillService`：`GetBillsAsync(userId, BillFilter, page, pageSize)`、`GetBillCountAsync`、`GetBillSummaryAsync`，三者共用唯一的条件构造点 `BuildBillQueryAsync`（旧的逐参数重载保留，内部转成 `BillFilter`） | 汇总在库里按账单类型分组求和，账单再多也只读回至多两行 |
| 服务 | `Services/PageDataGate.cs`（Scoped，`Program.cs` 与 `MauiProgram.cs` 都注册；首页、账单页、月度统计使用） | 读库单车道 |
| 脚本 / 样式 | `wwwroot/js/ui.js`（大标题折叠监听，状态写在滚动容器的 `data-title-collapsed`）、`css/ui.css`（大标题页、分段控件、汇总卡、按日分组、批量操作栏、筛选面板、骨架屏）、`css/tokens.css`（`--ft-seg-height` / `--ft-seg-pad` / `--ft-seg-thumb-shadow` / `--ft-lnav-title-max`） | |
| 测试（单元） | `BillSummaryTests`（21 项，真实 SQLite 迁移：各筛选条件、分页无关、软删除、共享账本可见性、与列表 / 总数同口径）· `BillListPresentationTests`（51 项）· `DesignTokensTests` +1（分段控件每一段的点击区域不小于热区下限） | 共新增 73 项，累计 831 |
| 测试（E2E） | `tests/e2e/bills_page_test.py`（150 项，段 A–H：新用户 / 列表与汇总 / 左滑与删除 / 刷新 / 筛选面板与共享账本 / 批量归属 / 分页 / 减弱动态效果）；`tab_shell_test.py`（D 段「账单页状态保留」先展开筛选面板）与 `immersive_safe_area_test.py`（旧布局检查改用 `/statistics`）因账单页改版同步 | 见下 |
| E2E 基建 | `server.ps1` / `fake_cloud.py`：云端地址统一为 `http://127.0.0.1:5271`（见「实现要点」）；`ledger_members_page_test.py`：`expect_toast` 触发前先等旧 Toast 消失、成功后等操作收尾，失败后断言菜单收起与行仍在；`tests/e2e/README.md`：脚本、可观察属性与 127.0.0.1 说明 | |
| 文档 | `docs/adr/0007-ios-visual-system.md` 第 10 点与已知限制 | 大标题页 / 分段控件 / 账单 Tab 的架构决策 |

**契约（后续 ticket 直接使用）**

- **查询条件对象**：`BillFilter`（`StartDate` / `EndDate`（含端点，取 `TimeBoundaries.EndOfDay`）/ `CategoryId` / `PaymentChannelId` / `LedgerId` / `Type`）。**10 号搜索**：给 `BillFilter` 加 `Keyword`，在 `BuildBillQueryAsync` 里加一个 `Where` 分支，列表、总数、汇总同时生效；**09 号**确认按钮上的「查看 N 笔」用 `GetBillCountAsync(userId, filter)` 或 `GetBillSummaryAsync(...).Count`。
- **页面侧筛选状态**：`BillListFilter`（绑定控件的文本 / Guid，`ToQuery()` → `BillFilter`；`PanelCount` = 偏离默认「本月」的时间范围算一项 + 账本 + 分类（可多选，算一项）+ 支付渠道，账单类型不计；`HasAny` 含账单类型；`ClearPanel()` 保留账单类型，`Clear()` 全清；第 2 轮评审后另有 `ResetPanel()`（回到默认视图）与 `ClearConditions()`（只去真正的条件、保留默认的「本月」），见 09 号）。09 号换成 Sheet 时沿用它与 `PanelCount`（筛选按钮角标）。
- **展示规则**：`BillListPresentation.Group` / `ToItem` / `EmptyState` / `SelectionText` / `FilterButtonLabel` / `DeleteTitle` / `DeleteMessage` / `ReadOnlyHint`；账单行是否可改看 `BillListItem.CanModify`（只有自己记的账单）。
- **大标题页**：`<LargeTitlePage Title Actions Busy OnRefresh RefreshTestId Class TestId>`，根节点就是 Tab 根页面的滚动容器（`data-scroll-root`、`data-title-collapsed`）；导航栏右侧最多放两个玻璃圆按钮（加 Tab 栏正好 3 层玻璃，见 ADR 0007）。统计（11）、我的（13）直接复用。
- **分段控件**：`<SegmentedControl Options Value ValueChanged AriaLabel TestId>`；`Value` 是 `string?`，Razor 里必须写 `Value="@_segment"`（见「实现要点」）；每一段 `data-testid="segment"`、`data-value`、`aria-pressed`，根上 `data-value` 是当前值。
- **读库闸门**：`PageDataGate.RunAsync(...)`——任何 Tab 根页面里「查库刷新」的入口都要经过它；不可重入，写操作不经过它。
- **E2E 钩子**：见 `tests/e2e/README.md` 与 `bills_page_test.py` 文件头登记的 `bills-*` / `bill-*` / `large-*` 属性；筛选面板与归属对话框的钩子（`bills-filter-*`、`bills-assign-dialog`）会在 09 号替换时一起更新，`tab_shell_test.py` 的 D 段也要同步。

**与 ticket 文字的差异（均已评估）**

1. ~~**导航栏有三个按钮，不是设计稿里的两个**：设计稿 02 只画了筛选与多选两个玻璃圆钮。刷新按钮是下拉刷新的等价入口（spec 要求保留不依赖手势的入口，桌面浏览器也要能用），用无玻璃的文字按钮，不占玻璃层预算（账单页同屏 = Tab 栏 + 筛选 + 多选 = 3 层）。~~ **验收后修订：刷新按钮已撤掉**（用户拍板，见 07 号「验收后修订」），导航栏现在与设计稿一样只有筛选与多选两个按钮。
2. **筛选面板是临时的，且改为「改一个条件就查一次」**：迁移前是「查询 / 重置」按钮的抽屉；临时面板放在分段控件下方，没有「查询」按钮（避免新增一个马上要被 09 号删掉的按钮），日期框防抖 400 ms。09 号换成 Sheet 后按设计稿（确认按钮上带笔数）。
3. **批量归属不再支持「移出账本」**：迁移前的目标账本可清空，表示移出；账本必填（CLAUDE.md）后服务层已要求非空且必须是自己的账本，旧入口随之取消。目标下拉只列自己的账本（迁移前页面把共享账本也列进了目标下拉，而服务层要求目标必须是自己的账本）。
4. **全选只含自己记的账单**：共享账本里别人记的账单只读，不能被批量归属；多选时它们置灰。
5. **只读账单给提示而不是隐藏入口**：ticket 允许「不提供入口，或给出明确的『无权限』提示」，选了后者——左滑不可用，点按给一句说明（Snackbar 的 Info 级，不是 Error，不触发重新登录）。
6. **日期范围在筛选角标里算一个条件**：起止日期是同一个「时间范围」维度，起止都填也只算 1 项。
7. **账单 Tab 默认看「本月」**（**第 2 轮评审，用户拍板**；最初沿用迁移前的「默认不限时间」，设计稿 02 的时间标签是「本月」）：汇总卡与列表默认是本月的账单；默认的「本月」不算筛选条件（不计角标、条件条上是中性的「本月」），取消它 = 不限时间。详见 09 号差异第 2 条与 `BillListFilter.DefaultTimePreset / IsDefaultTime / HasTimeCondition`。首页「查看全部」进来看到的是本月。E2E 里测列表本身的小节统一先 `show_all_time` 看全部时间，不受造数据日期跨不跨月影响。

**实现要点 / 发现**

- **Razor 陷阱**：`<SegmentedControl Value="_segment">` 里 `Value` 是 `string?` 参数，写成 `Value="_segment"` 传进去的是**字面量** `"_segment"`（不是字段值），滑块永远停在第一段、选中态错乱——必须 `Value="@_segment"`。单元测试看不到，E2E 的「滑块跟随」断言抓到。
- **滑块对齐**：滑块与轨道之间的留白（`--ft-seg-pad`）要和 `.ft-seg` 的水平内边距一致，否则滑块与文字错位 2 px；每一段的点击区域取整条轨道高度（44），不是滑块高度。
- **GlassAlert 的退出动画（180 ms）期间遮罩仍在 DOM 且挡住触摸**：确认 / 取消后立刻点别的会被吞掉——E2E 必须等 `alert-host` 摘除。`MDialog` 关闭后内容仍留在 DOM（只是 hidden），等「关闭」要等 `state="hidden"` 而不是 detached。
- **`<p>` 的外边距泄漏**：MASA 的 `.m-application p { margin-bottom: 16px }` 比单个 class 选择器优先，紧凑标签里别用 `<p>`（`EmptyState` 里仍有，留给 23 号无障碍审计时统一处理——**23 号已处理**：`ui.css` 开头 `.m-application p { margin-bottom: 0 }` 全局归零，见 ADR 0007 §26）。
- **排队中的旧加载直接放弃**：多个来源（刷新、`BillChanged`、二级页面关闭、改筛选）几乎同时要求重载时，`_loadVersion` 让排队等锁的旧加载放弃，只有最新一次真正查询，读到的是最新的筛选条件。
- **分页的确定性**：服务端排序是（交易时间、创建时间、Id），所以「一次取够 N 页」与逐页累加等价；加载更多按 Id 去重（两次加载之间有新账单插入时相邻两页会重叠）。
- **汇总的口径**：汇总卡 = 当前筛选下全部账单；组头合计 = 已加载账单。两者同时出现在一屏里，E2E 的 G 段专门断言它们在加载更多前后的变化。
- **隐藏的 Tab 根页面保留滚动位置**：Chromium 下 `d-none` 的根页面滚动位置保留（切 Tab 回来仍在原处，E2E 已验证）；iOS WKWebView / Android WebView 是否同样保留列入人工验收。
- **E2E 云端地址必须是 `127.0.0.1`，不能是 `localhost`**（Windows 11 实测）：.NET 解析 `localhost` 先试 `::1`，而假云端只监听 IPv4，被拒绝的回环连接要约 2 s 才失败——每个云端请求白白多 2 s（`localhost` ≈ 2.05 s，`127.0.0.1` ≈ 1 ms；端口没开时分别 4 s 与 2 s）。这会把 3 s 的 `CloudAvailability` 读路径期限直接吃掉，也是 06 号 ticket 里那次偶发失败的嫌疑之一。`server.ps1` 与 `fake_cloud.py` 已统一，README 写明了原因。
- **成员页 E2E 的三处同步竞态**（提速后暴露）：连续两条相同文案的 Toast 不是两个元素（触发前先等旧 Toast 消失）；菜单关闭动画 260 ms 内别急着 `count()`；操作进行中 `_busy` 期间行按钮是 `disabled`，滑动会落在正在重绘的列表上（成功后等 `member-cell` 都可用）。
- **仍未解释的偶发现象**：全量回归里出现过一次成员页「转让」失败，服务端日志是 `HttpRequestException ← IOException ← SocketException 10053`（本机软件中止了已建立的连接，发生在读响应首字节时），假云端没收到该请求。排除了假云端自身（Python 并发压测 3600 次请求 0 错误），只定位到并修复了 `localhost` 慢 2 s 这个会放大一切时序问题的隐患。10053 本身未能解释，**修复后的全量回归连续通过、未再复现，但不能证明已根除**；再出现时按 06 号 ticket 记录的诊断信息定位，不要自动重试掩盖。
- 回归：`dotnet build FinanceTracker.sln` 0 警告 0 错误；`dotnet test` 831/831（本 ticket 新增 73 项）；`run_all.py` 12/12（新增 `bills_page_test.py` 150 项；`home_page_test.py` 93 项、`ledgers_page_test.py` 147 项、`ledger_members_page_test.py` 138 项、`tab_shell_test.py` 64 项、`tab_slider_test.py` 82 项、`immersive_safe_area_test.py` 35 项等通过）。

- **验收阶段修正（24 号「验收阶段修正」①）**：对照设计稿 02，账单行的备注原先与分类同粗同色。`BillRowModel` 现在另带 `CategoryName` / `NoteText`，`ListCell` 的 `TitleNote` 把标题排成「分类名（加粗）+ 灰色常规的 · 备注」两个 flex 子项（备注先被省略、两个盒子都不比标题盒宽）；DOM 文字不变，`BillPresentation.NoteText` 把备注里的换行 / 连续空白折成一个空格。

### 待人工验收（设备 / 模拟器）

- [ ] 对照设计稿 01 与 screens 的 02-bills，在 Android 与 iOS（CI 产物）上确认：大标题字号与折叠后的小标题位置、汇总卡三栏的间距与数字大小、分组头与行的间距与配色、分段控件的滑块
- [ ] 大标题折叠手感：随滚动的跟手程度、折叠瞬间导航栏背景由透明变为页面背景色的过渡、灵动岛 / 刘海机型上导航栏与状态栏的衔接
- [ ] 左滑「编辑 / 删除」手感：阻尼、露出宽度、松手吸附 / 回弹；在 iOS 上与系统「边缘返回」手势、Android 手势导航互不冲突
- [ ] 下拉刷新：阻尼与阈值、回弹；与 iOS WKWebView 的橡皮筋、Android WebView 的原生过度滚动互不冲突（`overscroll-behavior-y: contain` 在较老的 WebView 上可能不生效）
- [ ] 隐藏的 Tab 根页面在 WKWebView / Android WebView 里是否保留滚动位置（切到别的 Tab 再回来仍在原处）
- [ ] 临时筛选面板里的日期框在 iOS / Android 上弹出原生日期选择器后的行为（防抖 400 ms 是否合适）；09 号换成 Sheet 后再整体验收
- [ ] 删除确认 Alert 的弹出与退出动画、批量选择的勾选反馈、归属账本后的 Toast
- [ ] 账单很多（几百笔）时的滚动流畅度与「加载更多」；中低端 Android 上账单页（3 层玻璃 + 长列表）的帧率
