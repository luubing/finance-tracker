# 12 — 统计：趋势与年度重做 + 旧地址兼容

**What to build:** 统计页里的「趋势」「年度」两个分段按设计稿重做为玻璃卡片，保留原有全部能力（趋势的时间维度、年度的同比等）；旧地址 `/trend`、`/annual-report` 仍然可用，访问后落到统计页对应的分段，旧入口、书签或通知跳转不会失效。

**Blocked by:** 11 — 统计 Tab：分段壳 + 月度

**Status:** completed（自动化验收全部通过——`dotnet build FinanceTracker.sln` 0 警告 0 错误、`dotnet test` 1200/1200、`run_all.py` 14/14（新增 `statistics_trend_annual_test.py` 163 项，`statistics_page_test.py` 改为 114 项）；真机 / 设备上的人工验收待办，见下方「待人工验收」）

- [x] 开工前先列出现有趋势分析与年度报告的功能点并写入本 ticket 的实现说明，迁移后逐项对照（趋势的时间维度与图表、年度的年份切换、同比、月度分布等）——见下方「开工前清单」，对照结果见「完成记录 · 逐项对照」
- [x] 趋势分段：沿用现有全部能力（含时间维度切换），改为玻璃卡片外观；账本筛选与月度保持一致；数据口径不变——`TrendAnalysisView`；时间范围胶囊 + 菜单、时间维度分段、KPI 单卡、三张折线图、可排序明细表；E2E T 段逐项验证（图表读 ECharts 实例的配置核对横轴 / 数据 / 线色）。图表容器是**不透明白卡**而不是玻璃（同 11 号差异第 4 条）
- [x] 年度分段：年份用玻璃胶囊切换（上一年 / 下一年）；KPI 卡；同比与月度分布等既有内容保留；数据口径不变——`AnnualReportView`；年份胶囊（`PeriodSwitcher`，2000–2100）、KPI 单卡、同比分析、月度折线、分类统计圆环 + 图例；E2E Y / H 段逐项验证。**「玻璃胶囊」同样是 11 号的白色胶囊**（玻璃层预算）
- [x] 旧地址 `/trend`、`/annual-report` 保留：访问后落到 `/statistics` 的对应分段，不残留空白中间页，返回行为合理；`/custom-report` 保持独立二级页——直接访问由 Web.Server 回 HTTP 302；应用内压栈由 `StatisticsRedirect` 跳转页换成统计 Tab；E2E R 段验证（含查询串、不残留二级页 / 占位页、返回不回到旧地址）
- [x] 「我的」里现有的趋势 / 年度入口此时落到上述跳转（入口本身由 13 迁出）——12 号完成时 E2E R 段在「我的」页真实点击两个入口验证；**13 号移除入口后这一段已删除**（应用内没有调用方了，PageStack 只响应自己的导航，`Blazor.navigateTo` 压不进去，没法再自动化），R 段只保留直接访问（HTTP 302）的路径
- [x] 自动化验收（E2E，手机视口）：直接访问 `/trend` 落到「统计」且「趋势」分段激活，访问 `/annual-report` 落到「年度」分段；在唯一账本下创建账单后，趋势与年度的数字与所建账单一致；切换分段时账本筛选保持；图表容器渲染且无控制台 / 页面错误——`statistics_trend_annual_test.py`（R / T / Y / H / F 五节 163 项）；`statistics_page_test.py` C 段更新为新的年份胶囊并移除旧地址检查（改由 R 段负责）
- [ ] 人工验收（**待人工**，需要设备 / 模拟器，见下方清单）：对照设计稿 01 与 screens 的 03-stats；趋势与年度图表在手机上的可读性；Android 与 iOS（CI 产物）
- [x] 回归：既有 E2E 与 `dotnet test` 全部通过，`dotnet build` 零错误

## 实现说明

- 覆盖 spec 用户故事：10、49、56。
- 设计稿：01 · 四个 Tab 根页面；screens 的 03-stats；07 · 逐页改动清单中 `/trend`、`/annual-report` 两行（合并入统计）。
- 设计稿 07 里趋势"沿用同一月份 / 账本筛选条"标注为"需确认"。spec 只要求切换分段时账本筛选保持、各分段保留原有能力；月份 / 时间范围控件是否与月度共用，由本 ticket 在不丢失既有能力的前提下决定，并写入实现说明——**决定见下方「判断记录」**。
- 遵守 ADR 0005（时间口径）。

### 开工前清单：现有趋势与年度的功能点（迁移后逐项对照）

读自 11 号 ticket 抽出的 `TrendAnalysisView.razor` / `AnnualReportView.razor`（即原 `/trend`、`/annual-report` 页面主体）、`Pages/Profile.razor` 与 `Core/Services/StatisticsService.cs`。

**趋势**

| # | 现有模块 | 数据口径 / 行为 |
|---|---|---|
| T1 | 时间范围：最近 7 天 / 30 天 / 90 天 / 12 个月，默认 7 天 | 终点 = 应用时区的「现在」，起点往前推 N 天（12 个月 = 往前 12 个月）；`GetTrendDataAsync(用户, 起, 终, 维度, 账本)`，区间两端都含 |
| T2 | 时间维度：按日 / 按周 / 按月，默认按日 | 周从周一起、月从 1 号起；统计服务**只返回有账单的日期**（不补零；第 2 轮评审后折线图的横轴在页面层补零，见判断记录第 4 条） |
| T3 | 汇总三卡：总支出 / 总收入 / 净收支 | 对所选范围内的趋势点求和 |
| T4 | 三张折线图：支出（红）、收入（绿）、净收支（蓝） | 各自空状态「暂无支出数据 / 暂无收入数据 / 暂无数据」 |
| T5 | 趋势数据明细表：日期 / 支出 / 收入 / 净收支，点表头排序 | 原表每页 10 行翻页 |
| T6 | 账本筛选 | 来自 11 号外壳（三个分段共用）；统计只算当前用户自己记的账单 |
| T7 | 账单变更自动重载、读库经 `PageDataGate`、失败 Snackbar 并保留上一次数据、刷新只读本页数据 | `StatisticsViewBase` 共用流程 |

**年度**

| # | 现有模块 | 数据口径 / 行为 |
|---|---|---|
| A1 | 年份选择（上一年 / 下一年），默认当年（应用时区） | 2000–2100 |
| A2 | 总览三卡：总支出 / 总收入 / 净收支 | `GetAnnualStatisticsAsync(年, 账本)` |
| A3 | 同比分析：支出同比、收入同比、去年同期金额、「（无对比基数）」 | `GetAnnualYearOverYearDataAsync`：已结束 / 未来的年份比整年；当年比「年初至今 vs 去年同期」（同一月日截止，左闭右开）；去年同期为 0 → 变化率 null → 显示「—」；涨跌着色 |
| A4 | 月度趋势折线（12 个月 支出 / 收入 / 净收支） | 空 =「当年暂无账单」 |
| A5 | 分类统计饼图 + 列表（图标、名称、占比（笔数）、金额） | `AnnualStatistics.CategoryStats`：全年**所有账单（支出与收入混在一起）**按分类汇总，占比相对两者之和——既有口径（第 2 轮评审后页面改用拆开的 `ExpenseCategoryStats` / `IncomeCategoryStats`，见判断记录第 3 条） |
| A6 | 账本筛选、账单变更自动重载、刷新 | 同 T6 / T7 |

**旧地址与入口**

| # | 现有模块 | 行为 |
|---|---|---|
| L1 | 路由 `/trend`、`/annual-report` | 11 号保留为薄包装（页面栏 + 账本下拉 + 视图） |
| L2 | 「我的」里的「趋势分析」「年度报告」入口 | `NavController.Push("/trend")` / `Push("/annual-report")`，入口本身由 13 号迁出 |
| L3 | `/custom-report` | 保持独立二级页（21 号迁移） |

### 完成记录（agent 实现）

**交付物**

| 层 | 文件 | 作用 |
|---|---|---|
| 页面 | `Web/Pages/Statistics.razor`（外壳：趋势 = 时间范围胶囊 + 范围菜单，年度 = 年份胶囊；持有范围 / 维度 / 年份；认领分段请求）· `Pages/TrendAnalysis.razor` / `AnnualReport.razor`（改成只放 `StatisticsRedirect` 的跳转页，路由不变） | 外壳持有切换分段时要保持的全部状态 |
| 视图 | `Web/Components/Statistics/TrendAnalysisView.razor`（重做）· `AnnualReportView.razor`（重做）· `StatisticsViewBase.cs`（新增 `StateKey`、`BuildKpi` 供三个视图共用）· `StatisticsChartOptions.cs`（折线图的 ECharts 配置）· `StatisticsRedirect.razor`（旧地址的应用内跳转页） | 视图只管取数与摆放 |
| 服务 | `Web/Services/StatisticsSegmentRequest.cs`（「请统计 Tab 落在某个分段」的一次性请求；Scoped，10 秒有效）· `Web.Server/Program.cs` 与 `App/MauiProgram.cs` 注册它；Web.Server 另把 `StatisticsPresentation.LegacyRoutes` 映射成 HTTP 302 | 旧地址的两条路径 |
| 纯函数 | `Web/Models/StatisticsPresentation.cs`（范围 / 维度 / 年份规整与文案、明细表排序与分页文案、同比文案 / 语义色 / 变化率格式 / 字号档位、旧地址与分段地址的解析） | 规则与文案不散落在 Razor 里，单元测试覆盖 |
| 样式 | `wwwroot/css/ui.css`：范围胶囊位置、维度分段、同比两栏 `.ft-yoy*`（含 `--long` / `--xlong` 字号档）、明细表 `.ft-table*` 与排序按钮、`.ft-redirect`；删除 11 号的 `.ft-stats__embed` | 只引用令牌 |
| 测试（单元） | `StatisticsPresentationTests`（扩展）· `StatisticsChartOptionsTests`（新：线色取自令牌、序列 / 横轴 / 数据点、纵轴格式、月度三条线）· `StatisticsSegmentRequestTests`（新：只取一次、覆盖、不认识的分段、过期） | 新增 132 项，累计 1200 |
| 测试（E2E） | `tests/e2e/statistics_trend_annual_test.py`（163 项：R 旧地址 / T 趋势 / Y 年度 / H 超长变化率 / F 刷新；可带小节名只跑一节）· `statistics_page_test.py` C 段更新（改用年份胶囊，移除旧地址检查，114 项）· `README.md` 登记脚本与三组观察属性 | 见下 |
| 文档 | `docs/adr/0007-ios-visual-system.md` 第 14 点与已知限制 | 旧地址机制、分段为什么不走地址、页面栈 250 ms 事实、表格 / 同比 / 图表约定 |

**逐项对照（开工前清单 → 迁移后）**

| # | 迁移后 | 对照结果 |
|---|---|---|
| T1 | `stats-range-pill`（左，白色胶囊，文字「最近7天」等，读屏名称「时间范围：…，点按切换」）→ `ActionSheet`「选择时间范围」四项，当前项带对勾与 `aria-current`，点「取消」不变；默认最近 7 天；窗口口径不变 | 一致（控件由「卡片里的下拉」换成胶囊 + 菜单） |
| T2 | 视图顶部 `SegmentedControl trend-dimension`「按日 / 按周 / 按月」（每段 44 高），默认按日；聚合规则不变，表格日期列按维度显示（日 / 周一 / `yyyy-MM`），图的横轴 `MM-dd` / `yy-MM` | 一致 |
| T3 | `KpiCard trend-kpi`：支出 / 收入 / 结余（净收支改叫「结余」，金额中性色，同 11 号） | 口径一致；命名与 11 号统一，见差异第 2 条 |
| T4 | 三张折线图（`StatisticsChartOptions`，线色取自令牌：支出 `#E8362C`、收入 `#1FA544`、净收支 `#4318FF`；审查阶段由最初的 iOS 系统红绿 `#FF3B30` / `#34C759` 统一过来，与月度图表、KPI 圆点同一套），各自空状态文案不变 | 一致（配色改用设计令牌） |
| T5 | `trend-table`：四列表头按钮（命中区域 ≥ 44 × 44，`aria-sort`），排序三态（升序 → 降序 → 取消回日期升序）、同值稳定；一次显示 10 行 +「显示更多（还有 N 条）」 | 能力一致（排序保留；翻页改为逐批展开，见差异第 3 条） |
| T6 | 账本来自外壳的账本胶囊，三个分段共用；E2E 验证选空账本全 0、选回来数据回来、切分段保持 | 一致 |
| T7 | 沿用 `StatisticsViewBase`：`BillChanged` / 参数变化重载、`PageDataGate`、Snackbar + 保留上一次数据；下拉刷新只读本页数据（E2E F 段用假云端验证不发请求；刷新按钮已在验收后撤掉，见 07 号「验收后修订」） | 一致 |
| A1 | `stats-year`（`PeriodSwitcher`）：上一年 / 下一年，读屏名称带目标年份，2000 / 2100 边界置灰；年份由外壳持有，切分段 / 切 Tab 保持 | 一致 |
| A2 | `KpiCard annual-kpi` | 同 T3 |
| A3 | `annual-yoy` 白卡：口径行（「年初至今 vs 去年同期」｜「2025年 vs 2024年」）+ 期间行 + 两栏（支出同比 / 收入同比：变化率 + 去年同期金额）；去年同期为 0 → 「—」+「（无对比基数）」；支出涨 = 红、收入涨 = 绿，下降相反，无基数 / 0 = 中性；变化率很长时按字数缩小字号 | 口径一致；语义色与「无对比基数」不变（文字色改用 `*-text` 令牌满足对比度） |
| A4 | `annual-monthly`：十二个月三条线（同令牌配色 + 图例），空状态「当年暂无账单」 | 一致 |
| A5 | ~~`annual-composition`：圆环 + 图例，口径不变（全年所有账单混合）+ 说明行~~ → **第 2 轮评审后（用户拍板 `annual_split`）拆成「支出构成」「收入构成」两节**：各自一个圆环 + 图例（复用 11 号的 `DonutChart` / `LegendList`），占比各自占本类合计（`AnnualStatistics.ExpenseCategoryStats` / `IncomeCategoryStats`，与月度分段的构成口径一致），圆环中心「全年支出 / 全年收入」，没有这一类账单时空环 +「当年暂无支出账单 / 当年暂无收入账单」；观察属性 `annual-{expense｜income}-header` / `-composition` / `-donut` / `-total` / `-legend` / `-empty` | 评审后改拆开（旧的混合口径只保留给 REST 接口 `CategoryStats`） |
| A6 | 同 T6 / T7 | 一致 |
| L1 | `/trend`、`/annual-report`：直接访问 → Web.Server 302 到 `/statistics?segment=trend｜annual`（不看旧地址的查询串）；应用内压栈 → `StatisticsRedirect` 登记请求 +「清栈切到统计 Tab」 | 能力保留（形态由「薄包装页」改为「跳转」） |
| L2 | 「我的」里两个入口不动，点了落到统计 Tab 对应分段（地址 `/statistics`，页面栈里没有残留页） | 一致（入口由 13 号移除） |
| L3 | `/custom-report` 没动 | 一致 |

**判断记录（均已评估，评审阶段会再向用户确认）**

1. **设计稿 07「需确认」——时间范围放哪**：筛选行三个分段保持一致——左 = 本分段的时间控件（月度 = 月份胶囊；趋势 = 时间范围胶囊；年度 = 年份胶囊），右 = 账本胶囊（三个分段共用）；时间维度（按日 / 周 / 月）是趋势内部的切换，放在趋势视图顶部的分段控件里。
2. **KPI 沿用 11 号约定**：「净收支」改叫「结余」、金额统一中性色（旧页按正负着色）。
3. **年度分类统计**：最初保持原口径（全年所有账单收入与支出混在一起汇总，占比相对两者之和），用一行说明讲清楚，拆开要改统计服务的返回形态，当时记为待判断项。**第 2 轮评审后用户拍板「拆成支出构成 / 收入构成两个圆环」（`annual_split`）**：`AnnualStatistics` 新增 `ExpenseCategoryStats` / `IncomeCategoryStats`（只含各自类型的账单、占比各自占本类合计，金额降序；`StatisticsService` 复用 `BuildCategoryStats`，不改查询），原来的混合 `CategoryStats` 保留给 REST 接口（`GET /api/statistics/annual` 的响应不变）；页面不再有「含支出与收入分类，占比按两者合计计算」那行说明。新增单测 `AnnualCategoryStatsTests`（9 项：各自占比 / 排序 / 笔数、合计对得上总支出总收入、混合口径不变、账本筛选对三份同时生效、年份按应用时区、只有支出没有收入、空年份、别人的账单不计入）与 `StatisticsPresentationTests` 的文案 / 图例用例；E2E Y 段按类型逐节核对。
4. **趋势折线图横轴补零**（第 2 轮评审后用户拍板 `trend_zero`）：原先统计服务只返回「有账单的日 / 周 / 月」（开工前清单 T2 注明「不补零」），折线直接连着有数据的点画，没有账单的日子凭空消失、相邻两点的时间跨度被拉平。现在 `TrendAnalysisView` 用 `StatisticsPresentation.FillTrendBuckets` 把三张折线图的横轴补成连续的时间桶——从窗口起点所在的日 / 周（周一）/ 月（月初）走到终点所在的桶，每桶一个点，没有账单的桶是 0；**只补趋势视图的三张折线图**：明细表仍只列有账单的桶（补零行没有信息量，也避免 12 个月按日出现 300+ 行），KPI 合计不变（补的全是 0），没有任何账单时三张图仍是空状态（不画一条 0 的平线）；月度分段的累计收支图与支出日历取数不动（用户确认只补趋势视图）。窗口起点那一天因为「现在往前推 N 天」是半天窗口（只含当前时刻之后的账单），补零后会作为一个点出现，是既有窗口口径的自然结果。单测 `StatisticsPresentationTests` 8 个方法 12 个用例（按日 / 周 / 月、跨年、合计不变且日期升序不重复、空数据、窗口外的桶保留、同桶多行合并、终点早于起点）；E2E T 段每种（范围 × 维度）组合按补零后的横轴核对三张图，并单独写死「最近 7 天·按日 = 连续 8 个点、两头没账单的点是 0」。

**契约（后续 ticket 直接使用）**

- **统计 Tab 的分段怎么指定**：地址里的 `?segment=trend|annual` **只在统计页创建时读一次**（宿主 302 的直接访问）；应用内要让「已存在的统计 Tab」落在某个分段，调用 `StatisticsSegmentRequest.Request(segment)` 然后 `NavController.GoBackToTab("/statistics")`（`StatisticsRedirect` 就是这样做的）。**不要靠带参数的地址传分段**——见发现第 2 条。点 Tab 栏回统计、从二级页面返回都不带请求，保持用户停留的分段。
- **页面栈的连续导航**：250 ms 内的第二次导航调用被静默丢弃；「刚 Push 出来的页面里立刻再导航」要先等过这个窗口（`StatisticsRedirect` 先等 300 ms，被丢就重试，最多 3 次）。
- **折线图**：用 `StatisticsChartOptions.Line(...)` / `Monthly(...)` 构造 ECharts 配置，线色取 `ExpenseColor` / `IncomeColor` / `NetColor`（= `DesignTokens` 的 `ExpenseColor` / `IncomeColor` / `BrandColor`；审查阶段由 `DangerColor` / `SuccessColor` 统一过来）；以后新增图表沿用，不要在页面里手写颜色。
- **E2E 钩子**：`stats-range-*`、`stats-year*`、`trend-*`、`annual-*`、`stats-redirect` 全部登记在 `tests/e2e/README.md`；图表在 E2E 里读 `window.echarts.getInstanceByDom(div).getOption()`。

**与 ticket / 设计稿的差异（均已评估）**

1. **图表容器是不透明白卡，不是玻璃**：同 11 号差异第 4 条（玻璃层预算每屏 ≤ 3）。年份 / 时间范围「玻璃胶囊」同样是白色胶囊。
2. **「净收支」→「结余」、金额中性色**：与 11 号一致，趋势 / 年度的 KPI 与月度同一张卡。
3. **明细表由翻页改为「显示更多」**：原表每页 10 行带翻页器；手机上翻页器占地方，改为一次 10 行 + 「显示更多（还有 N 条）」逐批展开。排序、列、数据不变。
4. **旧地址由「薄包装页」改为跳转**：书签 / 刷新直接访问的 URL 会被 302 改写成 `/statistics?segment=…`（浏览器历史里不留旧地址）；应用内入口跳转后，「我的」那条历史记录被替换，返回键回到它之前的一条（已接受的权衡：不回到旧地址 / 空白页）。

**实现要点 / 发现**

1. **字符串类型的组件参数必须写 `@`**（E2E T 段 `data-range` 断言发现）：`Range="_trendRange"` 传给视图的是字面量字符串，范围永远不变；`int` / `Guid` 参数才按表达式处理。08 号分段控件的 `Value="_segment"` 踩过同一个坑——审查阶段排查其它页面的 string 参数。
2. **地址里残留的 `?segment=` 会把用户的选择冲掉**（E2E「落地后换分段、进自定义报表再返回」发现的真实缺陷）：最初用地址传分段，统计页在地址变化时按参数切分段；可用户落地后切了分段、进二级页再返回，地址又回到带参数的形态，分段被重置。改为**一次性请求服务 `StatisticsSegmentRequest`**：旧地址的跳转页登记、统计页取走（创建时 / 地址变到统计页的那一刻），10 秒有效；地址里的参数只在统计页创建时读一次。单元测试覆盖「只取一次 / 覆盖 / 过期」，E2E R 段覆盖直接访问与应用内两条路径。
3. **页面栈的几条事实**（探针验证）：`GoBackToTab` 一次完成「清栈 + 切到 Tab 根页面」，`NavigateTo(replace)` 会在统计页上面残留空白全屏页；`Replace(…, clearStack: true)` 在栈里只有一页时触发重载循环；250 ms 内的第二次导航调用被静默丢弃（跳转页先等 300 ms 再调用）；初始地址就是旧地址（书签 / 刷新）时页面栈没有干净的出路——所以直接访问在 HTTP 层 302。
4. **`.ft-pill` 自带 `margin-left: auto`，后定义的它压过单类名选择器**：范围胶囊被顶到筛选行右边（布局断言发现），覆盖时用两类名选择器 `.ft-stats__filters .ft-stats__range`。
5. **ECharts 最后一个横轴标签被卡片右缘裁切**：`grid.right` 12 → 22（`StatisticsChartOptions` 的 `Grid()` 与 `Monthly`）。
6. **明细表头的排序箭头**：箭头原来占列宽，表头文字与数字错位 14px、点击区域也小；改为按钮铺满整个单元格，箭头绝对定位在文字旁的 `.ft-table__sort-label` 里。
7. **变化率可能非常长**：去年同期只有 0.01 元、今年 9,999,999.99 时是 `+99999999800.0%`；`RateSize` 按字数分档（≥ 9 个字符一档、≥ 12 个字符再一档）缩小字号，E2E H 段验证不挤出栏；`FormatChangeRate` 对「四舍五入后为 0」固定输出 `0.0%`（避免 `-0.0%`）。
8. **同比的语义色**：支出涨 = 差、收入涨 = 好；文字色用 `--ft-danger-text` / `--ft-income-text`（≥ 4.5:1；`--ft-income` 绿色在白底只有约 3.2:1，只用于图形）。
9. **E2E 读图表配置而不是像素**：ECharts 实例（`getInstanceByDom(div).getOption()`）暴露横轴、系列数据与线色，断言「图与明细一致」不依赖画布；ECharts 走 CDN，离线时容器与空状态仍在。造数避开各范围的边界（账单在「几天前的 12:00」，天数 ≤ N−1 一定在窗口内、≥ N+1 一定在窗口外）。
10. **本机环境**：同 11 号（Web.Server 运行时 `dotnet build FinanceTracker.sln` 要先 `server.ps1 -Action stop`；改 C# / Razor 要重启服务）。
11. 回归：`dotnet build FinanceTracker.sln` 0 警告 0 错误（含 MAUI Android / iOS 目标）；`dotnet test` 1200/1200（本 ticket 新增 132 项）；`run_all.py` 14/14（`statistics_trend_annual_test.py` 163 项、`statistics_page_test.py` 114 项）。

### 待人工验收（设备 / 模拟器）

- [ ] 对照设计稿 01 与 screens 的 03-stats，在 Android 与 iOS（CI 产物）上确认趋势 / 年度分段：筛选行（时间范围 / 年份胶囊 + 账本胶囊）、维度分段、KPI 单卡、折线图卡片、同比卡、明细表的间距 / 圆角 / 字号 / 颜色
- [ ] **ECharts 折线图**：手机上的可读性（横轴标签是否挤、数据点多时的疏密）；在图表上纵向滑动是否会卡住页面滚动；切时间范围 / 维度 / 年份时重绘的流畅度；首次进入时 CDN 慢 / 失败的表现
- [ ] 明细表：表头排序按钮的点按手感与命中区域、「显示更多」展开后的滚动位置、四列金额在窄屏（小屏 iPhone SE / 小屏 Android）上是否仍不换行不挤出
- [ ] 同比卡：变化率颜色与箭头语义（支出涨为红、收入涨为绿）在真机上的观感；超长变化率不挤出栏
- [ ] 时间范围菜单（`ActionSheet`）在小屏与底部手势条下的位置与手感；年份快速连点时视图是否只响应最后一次
- [ ] **判断项（请一并确认）**：时间范围放左侧胶囊 + 维度分段放视图顶部（判断记录第 1 条）；年度分类统计已拆成「支出构成 / 收入构成」（第 2 轮评审，用户拍板，判断记录第 3 条）；趋势折线图横轴已补零（第 2 轮评审，用户拍板，判断记录第 4 条）；明细表「显示更多」代替翻页（差异第 3 条）
