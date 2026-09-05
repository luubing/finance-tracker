# 19 — 数据可视化增强（更多图表类型）

**What to build:** 在现有饼图（分类占比）和折线图（趋势）基础上，增加更多图表类型，提升数据洞察力。图表基于 MASA Blazor 图表组件，需适配移动端屏幕。

**Blocked by:** 09 — 月度统计报表、10 — 趋势分析

**Status:** completed

- [x] 封装可复用的图表组件（`Components/Charts/`：ChartCard 统一外壳（标题/图标/加载态/空数据态）、CategoryRankChart、CalendarHeatmapChart、CumulativeNetChart、ExpenseIncomeDonutChart，组件直接接收 Core 统计类型，无页面私有模型转换）
- [x] 分类支出排行柱状图（横向条形图，Top N 分类，最大值在最上方）
- [x] 日历热力图（月视图，按日展示支出强度，颜色渐变蓝→橙→红，自动补齐整月格子）
- [x] 收支结余累计曲线（每日累计支出/收入/净收支三线，带面积填充）
- [x] 支出/收入构成对比环形图（左右并排双环，统一调色板）
- [x] 图表支持按账本筛选联动（复用现有统计 API 的 ledgerId 参数，随月份/账本筛选联动刷新）
- [x] 移动端适配（紧凑 grid + containLabel、字号 10、超宽分类名 truncate 截断、tooltip 精简；构建验证通过，真机显示效果纳入后续 iOS/Android 打包回归）

## 实现说明

- **组件位置**：`src/FinanceTracker.Web/Components/Charts/`，命名空间 `FinanceTracker.Web.Components.Charts`（页面显式 `@using`；`_Imports.razor` 因文件被 VS 锁定未能加入全局 using，后续可补）。
- **集成页面**：`MonthlyStatistics.razor` 新增 4 个图表区块（排行/热力图/累计曲线/构成对比），复用现有 `GetCategoryStatisticsAsync` 与 `GetTrendDataAsync`（day 维度）API，无新增后端代码；月份/账本筛选切换时联动刷新。
- **数据口径**：分类排行与构成对比用 `CategoryStatistics`；热力图与累计曲线用日粒度 `TrendData`，组件内部补齐整月空日（热力图完整格子、累计曲线连续折线）。
- **验证**：`dotnet build FinanceTracker.sln` 通过（0 错误），单元测试 27/27 通过。