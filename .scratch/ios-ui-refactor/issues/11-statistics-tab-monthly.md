# 11 — 统计 Tab：分段壳 + 月度

**What to build:** 「统计」Tab 按设计稿重做外壳与月度视图：大标题 + 「月度 / 趋势 / 年度」分段 + 右上角「自定义报表」入口。月度视图重做为玻璃卡片：月份胶囊（上一月 / 下一月）、账本选择、KPI 单卡（支出 / 收入 / 结余）、支出构成圆环与图例占比（可切换看支出或收入）、分类排行、支出日历、累计曲线，沿用现有图表与数据口径。趋势与年度两个分段暂时内嵌现有内容（抽成可嵌入的组件，行为与视觉不变），由 12 重做。

**Blocked by:** 08 — 账单 Tab：列表

**Status:** completed（自动化验收全部通过——`dotnet build` 0 警告 0 错误、`dotnet test` 1068/1068、`run_all.py` 13/13（新增 `statistics_page_test.py` 117 项）；真机 / 设备上的人工验收待办，见下方「待人工验收」）

- [x] 开工前先列出现有月度统计、趋势分析、年度报告的全部模块与数据口径并写入本 ticket 的实现说明，迁移后逐项对照——见下方「开工前清单」，对照结果见「完成记录 · 逐项对照」
- [x] 统计壳：大标题（复用 08 的大标题页）、三项分段（月度 / 趋势 / 年度）、右上角「自定义报表」入口通向现有的自定义报表页（由 21 迁移）——`Pages/Statistics.razor`；`stats-report` 压栈到 `/custom-report`
- [x] 月度视图：月份胶囊切换、账本选择（「未归属账本」仍单独归类，口径与以前一致）、KPI 单卡、支出构成圆环 + 图例占比（可切换看支出 / 收入）、分类排行、支出日历、累计曲线；数据口径不变（应用时区、区间左闭右开）；切换分段时保持当前账本筛选——`MonthlyStatisticsView`；E2E 的 B1–B3 / C 段逐项验证
- [x] 趋势与年度：先把现有页面主体抽成可嵌入的组件，在分段里原样展示；旧页面与其路由继续可用（使用同一组件）——`TrendAnalysisView` / `AnnualReportView`；`/trend`、`/annual-report` 变成薄包装（页面栏 + 账本下拉 + 同一个视图），E2E C 段验证旧地址可用
- [x] 新建复用组件：月份 / 年份胶囊切换器、KPI 卡、玻璃卡片外观的图表容器——`PeriodSwitcher` / `KpiCard` / `ChartPanel`（另有 `FilterPill` / `DonutChart` / `LegendList`）；图表容器是**不透明白卡**而不是玻璃，见差异第 4 条
- [x] 自动化验收（E2E，手机视口）：统计 Tab 展示三项分段与自定义报表入口；在唯一账本下创建账单后，KPI 与所建账单一致；上一月显示为零；切换账本与分段后账本筛选保持；自定义报表入口可达；依赖旧统计页文案的断言同步更新——`statistics_page_test.py` A–G 七段 117 项；`immersive_safe_area_test.py` 的「兼容层」改指仍是旧结构的 `/profile`
- [ ] 人工验收（**待人工**，需要设备 / 模拟器，见下方清单）：对照设计稿 01 与 screens 的 03-stats；图表在手机上的可读性与滚动性能；Android 与 iOS（CI 产物）
- [x] 回归：既有 E2E 与 `dotnet test` 全部通过，`dotnet build` 零错误

## 实现说明

- 覆盖 spec 用户故事：5、49–55。
- 设计稿：01 · 四个 Tab 根页面；screens 的 03-stats。
- 图表继续使用 ECharts；图表库与 MDI 图标走 CDN 是既有问题，本次不处理。
- 月度的账本选择、月份胶囊用 05 的操作菜单等组件即可；待确认账单 / 导入用的共用「账本选择器」重绘由 15 负责，不属于本 ticket。
- 统计口径遵守 ADR 0005（时间口径）：用户看到的"某天 / 某月 / 某年"边界一律按应用时区，经 AppTimeZone 换算。

### 开工前清单：现有统计模块与数据口径（迁移后逐项对照，对照结果见下方「完成记录」）

读自 `Pages/MonthlyStatistics.razor`（月度，路由 `/statistics`）、`Pages/TrendAnalysis.razor`（`/trend`）、`Pages/AnnualReport.razor`（`/annual-report`）与 `Core/Services/StatisticsService.cs`。

**月度（`/statistics`）**

| # | 现有模块 | 数据口径 / 行为 |
|---|---|---|
| M1 | 导航栏「月度统计」+ 刷新按钮（转圈） | 手动重新加载；加载失败 Snackbar「加载失败: …」 |
| M2 | 月份选择器（上一月 / 下一月，跨年进位） | 默认当前月；**不能切到未来月份**（审查阶段按用户决定：当月时「下一月」置灰；最初实现沿用旧页允许切到未来）；区间 = 应用时区自然月，左闭右开 |
| M3 | 账本筛选（MSelect，可清除 = 全部账本） | 选项 = 用户**自己的**账本（`LedgerService.GetLedgersAsync`）；没有账本时不显示；账本加载失败不影响统计；统计只算**当前用户自己**记的账单 |
| M4 | 汇总三卡：总支出 / 总收入 / 净收支 | `GetMonthlyStatisticsAsync(ledgerId)`；净收支 ≥ 0 绿、< 0 红 |
| M5 | 预算执行情况（每项：图标 + 分类名 +（账本名）· 已用 / 额度 · 进度条 · 已使用 x%、剩余 ¥y · 已超支 / 接近上限） | `BudgetService.GetBudgetStatusAsync(用户, 年, 月, 账本)`——账本传 null = 全部账本的预算；≥ 100% 红、≥ 80% 黄；当月没有预算整块不显示 |
| M6 | 「支出统计」饼图 + 列表 | **全部账本且用户有账本** → 按账本汇总（`GetLedgerStatisticsAsync`，无账本的历史账单归「未归属账本」，不受账本筛选影响）；选了具体账本或一个账本都没有 → 按分类汇总；列表行 = 图标、名称、占比（笔数）、金额；空 =「暂无支出数据」 |
| M7 | 「收入分类统计」饼图 + 列表 | `GetCategoryStatisticsAsync(收入, ledgerId)`；空 =「暂无收入数据」 |
| M8 | 分类支出排行（横向条形，Top 8） | 同 M6 的支出分类数据（始终按分类，不随账本汇总切换）；空 =「暂无数据」 |
| M9 | 支出日历热力图 | 日粒度趋势 `GetTrendDataAsync(月初, EndOfMonth, Day, ledgerId)`；空（当月没有支出）=「当月暂无支出账单」 |
| M10 | 累计收支曲线 | 同日粒度趋势；空 =「当月暂无收支账单」 |
| M11 | 支出 / 收入构成对比（双环） | 同 M6 / M7 的分类数据；空 =「当月暂无收支账单」 |
| M12 | 订阅 `BillEventService.BillChanged`，账单变更后自动重载 | 读库经 `PageDataGate`（四个 Tab 根页面共用一个 DbContext） |

**趋势（`/trend`）**

| # | 现有模块 | 数据口径 / 行为 |
|---|---|---|
| T1 | 筛选卡：时间范围（最近 7 天 / 30 天 / 90 天 / 12 个月，默认 7 天）、粒度（按日 / 周 / 月）、账本 | 范围的终点是「现在」、起点往前推；`GetTrendDataAsync(范围, 粒度, ledgerId)` |
| T2 | 汇总三卡（总支出 / 总收入 / 净收支） | 对所选范围内趋势点求和 |
| T3 | 三张折线图：支出（红）、收入（绿）、净收支（蓝） | 各自空状态「暂无支出数据 / 暂无收入数据 / 暂无数据」 |
| T4 | 趋势数据明细表（日期 / 支出 / 收入 / 净收支，可排序） | — |

**年度（`/annual-report`）**

| # | 现有模块 | 数据口径 / 行为 |
|---|---|---|
| A1 | 年份选择器（上一年 / 下一年）、账本筛选 | `GetAnnualStatisticsAsync(年, ledgerId)` |
| A2 | 总览三卡（总支出 / 总收入 / 净收支） | — |
| A3 | 同比分析：支出同比、收入同比、去年同期金额、「（无对比基数）」 | `GetAnnualYearOverYearDataAsync`：已结束的年份比整年；当年比「年初至今 vs 去年同期」；去年同期为 0 时变化率为 null，显示「—」 |
| A4 | 月度趋势折线（12 个月支出 / 收入 / 净收支） | — |
| A5 | 分类统计饼图 + 列表（图标、名称、占比（笔数）、金额） | `AnnualStatistics.CategoryStats`：全年所有账单（支出与收入混在一起）按分类聚合，占比相对「支出 + 收入」之和；这是既有口径，本 ticket 原样内嵌，是否拆开留给 12 |

**自定义报表（`/custom-report`）**：本 ticket 只提供入口，页面由 21 迁移。

### 完成记录（agent 实现）

**交付物**

| 层 | 文件 | 作用 |
|---|---|---|
| 页面 | `Web/Pages/Statistics.razor`（新，`/statistics`：`LargeTitlePage` + 分段 + 月份胶囊 + 账本胶囊 + 账本菜单）· `Pages/TrendAnalysis.razor` / `AnnualReport.razor`（改成薄包装，路由不变）· `Pages/MonthlyStatistics.razor`（**删除**：旧的重复 `/statistics` 页面） | 外壳持有分段 / 账本筛选（三个分段共用）/ 月度年月 |
| 视图 | `Web/Components/Statistics/StatisticsViewBase.cs`（共用加载流程）· `MonthlyStatisticsView.razor` · `TrendAnalysisView.razor` · `AnnualReportView.razor` | 三个可嵌入视图：按参数取数 + 摆放 |
| 通用件 | `Web/Components/Ui/{PeriodSwitcher, FilterPill, KpiCard + KpiItem, DonutChart, LegendList, ChartPanel}` | 月份 / 年份胶囊、筛选胶囊、KPI 单卡、SVG 构成圆环、图例、图表容器 |
| 纯函数 | `Web/Models/StatisticsPresentation.cs`（月份平移 / 标签 / 色板 / 图例排序 / 占比文字 / 文案）· `DonutGeometry.cs`（圆环几何） | 规则与文案不散落在页面里，单元测试覆盖 |
| 样式 | `wwwroot/css/tokens.css`（新增 `--ft-hue` / `--ft-shadow-capsule` / `--ft-donut-size` / `--ft-type-amount-donut(-long)`）· `ui.css`（「统计 Tab」一组、`.ft-hue--0…9` 色板、`.ft-actionsheet__group` 限高滚动） | 只引用令牌 |
| 测试（单元） | `StatisticsPresentationTests`（分段规整、月份 / 年份平移与边界、胶囊文字与读屏名称、账本胶囊文字与筛选校验、按账本是否可用、图例排序 / 配色 / 过滤、占比文字、构成文案、长金额）· `DonutGeometryTests`（占比之和为 1、缺口、单段整圈、零 / 负金额跳过但序号不错位、极小切片至少剩一半弧长、孔径与设计稿一致） | 新增 73 项，累计 1068 |
| 测试（E2E） | `tests/e2e/statistics_page_test.py`（117 项：A 新用户 / B1 数据口径 / B2 月份切换 / B3 账本筛选 / C 分段与旧地址 / D 刷新与二级页 / E 只有历史账单 / F 账本很多名称很长 / G 大额）；`immersive_safe_area_test.py` 兼容层改指 `/profile`；`README.md` 登记脚本与三组观察属性 | 见下 |
| 文档 | `docs/adr/0007-ios-visual-system.md` 第 13 点与已知限制 | 外壳 / 视图分工、重绘规则、圆环与色板、口径 |

**逐项对照（开工前清单 → 迁移后）**

| # | 迁移后 | 对照结果 |
|---|---|---|
| M1 | 大标题「统计」；**刷新按钮（原 `stats-refresh`）已撤掉**——验收后修订，见 07 号「验收后修订」，刷新只靠下拉（`stats-ptr`），加载 / 刷新中页面根节点 `aria-busy`；加载失败仍是 Snackbar「加载失败: …」，视图保留上一次数据，从未成功过显示「统计加载失败」占位（文案由「点按右上角的刷新，或下拉再试一次」改为「下拉刷新，再试一次」） | 一致（导航栏换成大标题页；刷新入口改为下拉） |
| M2 | `PeriodSwitcher stats-period`：上一月 / 下一月、跨年进位；默认当前月（应用时区）；**往后最晚到当月**（`StatisticsPresentation.ShiftMonthUpTo`，当月时「下一月」置灰；往前限 2000 年——审查阶段按用户决定，由最初的「不限制未来月份」改来）；区间 = 应用时区自然月、左闭右开 | 一致 |
| M3 | `FilterPill stats-ledger-pill` + `ActionSheet`：选项 = 用户**自己的**账本，「全部账本」是第一项（清除 = 选它）；没有账本时胶囊不出现；账本加载失败当空列表、不影响统计；只算当前用户自己记的账单 | 一致（MSelect 换成胶囊 + 菜单） |
| M4 | `KpiCard`：支出 / 收入 / 结余，`GetMonthlyStatisticsAsync(ledgerId)` | 口径一致；「净收支」改叫「结余」，金额统一中性色（旧页按正负着色），见差异第 3 条 |
| M5 | `SectionHeader`「预算执行」+「管理」→ `GroupedList` + `BudgetRow`；`GetBudgetStatusAsync(用户, 年, 月, 账本)`，账本传 null = 全部账本预算；当月没有预算整节不显示 | 一致（行换成 07 号的 `BudgetRow`：≥ 100% 红、≥ 80% 黄） |
| M6 | 构成圆环 + 图例（支出）：按分类（`GetCategoryStatisticsAsync`）或按账本（`GetLedgerStatisticsAsync`，无账本账单归「未归属账本」，不受账本筛选影响）；空 =「当月暂无支出账单」 | 口径一致；**默认改成按分类**，「按账本」变成节标题右侧的切换，见差异第 1 条 |
| M7 | 同一个圆环，标题旁的小按钮切换看收入；空 =「当月暂无收入账单」 | 并入 M6（见差异第 2 条） |
| M8 | `stats-rank`：`CategoryRankChart` Top 8（始终按分类的支出数据），空 =「当月暂无支出账单」 | 一致（旧文案「暂无数据」→ 与其它节统一） |
| M9 | `stats-calendar`：`CalendarHeatmapChart`，日粒度 `GetTrendDataAsync(月初, EndOfMonth, Day, ledgerId)`，空 =「当月暂无支出账单」 | 一致 |
| M10 | `stats-cumulative`：`CumulativeNetChart`，同一份日粒度数据，空 =「当月暂无收支账单」 | 一致 |
| M11 | 双环对比图不再单独出现：构成圆环可切换支出 / 收入，已覆盖同样的信息 | 见差异第 2 条 |
| M12 | `StatisticsViewBase` 订阅 `BillChanged`；读库经 `PageDataGate`；排队期间被取代的加载放弃 | 一致 |
| T1–T4 | `TrendAnalysisView`：时间范围（最近 7 天 / 30 天 / 90 天 / 12 个月）、粒度、三张汇总、三张折线图、明细表**原样**；账本来自外壳（三个分段共用）；`/trend` 包装页保留原来的账本下拉 | 一致（视觉重做在 12 号） |
| A1–A5 | `AnnualReportView`：年份选择、总览三卡、同比分析、月度趋势折线、分类统计（支出与收入混在一起）**原样**；账本来自外壳；`/annual-report` 包装页保留账本下拉 | 一致（视觉重做 + 是否拆分类饼图在 12 号） |

**契约（后续 ticket 直接使用）**

- **三个视图的嵌入方式**（12 号重做趋势 / 年度时沿用）：`<XxxView @key="_userId" @ref UserId LedgerId />`；视图继承 `StatisticsViewBase`，只实现 `LoadAsync()`（已在 `PageDataGate` 里，**不要再进闸门**，先取齐再一起写字段）和（可选）`LoadKey`（视图自己的参数，值相等的元组）；视图内部状态变化（趋势的范围、年度的年份）自己调 `ReloadAsync()`。`HasLoaded` / `IsLoading` / `LoadFailed` 三个标志决定骨架 / 重新加载中 / 失败占位。**`ReloadAsync` 自带重绘**（见实现要点第 1 条）。
- **外壳持有的状态**：分段、账本筛选、月度年月都在 `Statistics.razor`；视图经参数拿，不自己存账本。没有全局状态服务；12 号要把 `/trend`、`/annual-report` 重定向到某个分段时，再决定加小的共享状态还是用查询参数。
- **通用件**：`KpiCard`（`Items = KpiItem(标签, 金额, 色调 expense｜income｜balance, TestId)`，`Loading` 骨架，长金额自动缩小）；`PeriodSwitcher`（`Label` / `CanPrevious` / `CanNext` / `OnPrevious` / `OnNext` / `PreviousLabel` / `NextLabel`；年度用同一个，平移规则在 `StatisticsPresentation.ShiftMonth` / `ShiftYear`）；`FilterPill`；`DonutChart`（`Rows = LegendRow(名称, 金额, 占比, 笔数, ColorIndex)`，中心放 `ChildContent`）+ `LegendList`（同一批 `LegendRow`，环与图例颜色同源）；`ChartPanel`（`Loading` / `Empty` / 内容三态，不透明白卡）。
- **色板**：金额排名 → `ColorIndex`（0–9 循环）→ `ft-hue--N` → `--ft-hue`，只引用设计令牌；任何新的「按排名上色」的图例都复用它。
- **E2E 钩子**：`stats-*` 一组、`donut-arc` / `legend-row` / `budget-row` 等，全部登记在 `tests/e2e/README.md`；断言一律用 `stats-page` 限定范围（Home / 账单页常驻在隐藏的 Tab 页里）。

**与 ticket / 设计稿的差异（均已评估）**

1. **构成默认「按分类」，「按账本」是切换**：旧页面在「全部账本且有账本」时直接按账本汇总；设计稿 03 的构成图例是分类、节标题右侧有「按账本」入口，采用设计稿。账本汇总口径、「未归属账本」归类、「选了具体账本就没有按账本」都不变。
2. **旧的「收入分类统计」与「支出 / 收入构成对比（双环）」并入同一个构成圆环**（标题旁小按钮切换看支出 / 收入）：信息不丢。双环组件 `ExpenseIncomeDonutChart` 与 `ChartCard` 原先还被自定义报表使用，已在 21 号迁移自定义报表时随旧页一并删除（全仓库无其它引用）。
3. **「净收支」改叫「结余」、金额统一中性色**：设计稿 03 如此；沿用「金额中性色」的既定决定（颜色只出现在 KPI 小圆点上）。
4. **图表容器是不透明白卡，不是「玻璃卡片」**：spec 第 53 条字面写「玻璃卡片外观」，设计稿画的是白卡，而且玻璃层预算每屏 ≤ 3（ADR 0007 §3，统计页本身已有 Tab 栏 + 「自定义报表」按钮两层）。
5. **账本选择从 `MSelect` 换成胶囊 + 操作菜单**：`MSelect` 在手机上体验差、也无法自动化；「全部账本」是菜单第一项，选中项带对勾。账本很多时菜单在面板内滚动（见发现第 6 条）。
6. **趋势 / 年度分段原样嵌入**：本 ticket 不改它们的外观与交互（范围 / 粒度 / 年份选择仍在视图里）；账本筛选改由外壳的胶囊提供，所以嵌入后视图里没有账本下拉（`/trend`、`/annual-report` 包装页仍有）。

**实现要点 / 发现**

1. **父页面直接调用视图的 `ReloadAsync` 时，视图必须自己重绘**（E2E D 段「造一笔账单 → 刷新 → 读 KPI」发现，刷新点了「没反应」；当时入口是刷新按钮，按钮撤掉后同一条断言改成真实触摸下拉，路径相同）：视图参数没变，框架重绘父页面时会跳过它，字段换成新数据而界面停在旧数据。`ReloadAsync` 开始（标记重新加载中）与结束（换上新数据）都显式 `StateHasChanged`；经参数变化与 `BillChanged` 的路径本来就会重绘，重复调用无害。排查时用「同一页换月再换回」证明读库是新的、问题在刷新路径的重绘。
2. **一次加载取齐、一起写入**：月度汇总 + 支出 / 收入分类 + 支出 / 收入账本汇总 + 预算 + 日粒度趋势一次取回，所以「支出 ↔ 收入」「按分类 ↔ 按账本」不再查库；视图开头取参数快照，保证 await 期间参数变了也用同一份参数取完所有数据；`LoadKey = (年, 月, 是否有账本)`。
3. **快速连点上一月 / 下一月只让最后一次真正查询**：`_loadVersion` 递增，排队等 `PageDataGate` 期间被更新的加载取代的旧加载直接放弃（E2E B2 段验证）。
4. **占比的两处取整**：服务层 `Math.Round(amount / total * 100, 2)`（银行家舍入）写进 `data-percent`；图例文字一位小数、四舍五入远离零，`>0` 且 `<0.05` 显示「<0.1%」。E2E 用 `ROUND_HALF_EVEN` 复算 `data-percent`、用 `ROUND_HALF_UP` 复算文字，两条规则分开断言。
5. **圆环用 SVG 描边虚线弧而不是 ECharts**：每段是独立的 `<circle>`，E2E 直接数段数、读 `data-fraction`；金额 ≤ 0 的项跳过但序号不错位（配色不错位）；单段画整圈不留缺口；占比极小的切片缺口不超过弧长的一半（仍看得见）。尺寸用令牌 `--ft-donut-size` 的固定值——令牌 / 组件样式禁用 `aspect-ratio`（旧 WebView 兼容，`DesignTokensTests` 守着）。
6. **账本菜单选项很多 / 名称很长**（E2E F 段发现）：选项组原来会把面板顶出屏幕，现在 `.ft-actionsheet__group` 限高（屏高 − 安全区 − 120）并在面板内滚动；胶囊里的长名称省略，不撑破一行。
7. **统计口径只算自己的账单**：共享账本里别人记的账单不计入 KPI / 圆环 / 排行；E2E 造了一笔别人在共享账本里的账单专门断言不计入。`GetLedgerStatisticsAsync` 不接受账本筛选（按账本汇总本来就是在全部账本之上分组），所以「按账本」只在「全部账本」时出现。
8. **E2E 测量教训**：量分段滑块位置前要 `wait_animations_settled`（滑块有位移过渡；本机在 `dotnet test` 并行时偶发测到过渡中间值）；统计 / 断言用 `stats-page` 限定；`data-state` 在重新加载时是 `reloading`（保留旧数据），等「就绪」要等 `ready`。
9. **迁移期兼容层的目标页**：`immersive_safe_area_test.py` 的「兼容层」原来量的是 `/statistics`，现在改量仍是旧结构的 `/profile`（13 号迁移「我的」时整段删除根页面部分）。
10. **本机环境**：Web.Server 运行时 `dotnet build FinanceTracker.sln` 会因 DLL 被占用复制失败，先 `server.ps1 -Action stop`；改 C# / Razor 要重启服务，静态 JS / CSS 即时生效。
11. 回归：`dotnet build FinanceTracker.sln` 0 警告 0 错误；`dotnet test` 1068/1068（本 ticket 新增 73 项）；`run_all.py` 13/13（`statistics_page_test.py` 117 项）。

- **验收阶段修正（24 号「验收阶段修正」⑥）**：对照设计稿 03，构成圆环比设计稿大一圈。`--ft-donut-size` 224 → 200（设计稿外径约 184，再小中心的孔放不下长金额）、中心总额字号 24 / 30 → 21 / 28、长金额 17 / 24 → 16 / 22；`statistics_page_test` 的圆环宽度上限随之收到 200.5。

### 待人工验收（设备 / 模拟器）

- [ ] 对照设计稿 01 与 screens 的 03-stats，在 Android 与 iOS（CI 产物）上确认：大标题 + 分段 + 月份胶囊 + 账本胶囊 + KPI 单卡 + 圆环 + 图例的间距 / 圆角 / 字号 / 颜色
- [ ] **圆环渲染**：SVG 描边虚线弧在 Android WebView / iOS WKWebView 里段间缺口、起点（12 点钟）、弧段端点是否与设计稿一致；中心总额在长金额下是否仍在孔内
- [ ] **ECharts 三张图表**（分类排行 / 支出日历 / 累计收支）：手机上的可读性；在图表上纵向滑动是否会卡住页面滚动；切月份时重绘的流畅度；首次进入时 CDN 慢 / 失败的表现
- [ ] 大标题折叠、下拉刷新、月份快速连点与系统橡皮筋的配合；账本菜单在小屏与底部手势条下的滚动手感
- [x] **判断项**：构成默认按分类、「按账本」做成切换（差异第 1 条）、图表容器用白卡而不是玻璃（差异第 4 条）——第 2 轮评审用户已接受；月份可切到未来月份（沿用旧页）——**第 2 轮评审用户决定改为不能切到未来，已改**（`ShiftMonthUpTo`，当月时「下一月」置灰；预算页不变）
