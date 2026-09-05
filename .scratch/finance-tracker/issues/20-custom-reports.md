# 20 — 自定义报表与数据对比分析

**What to build:** 用户可自定义时间范围生成报表，并与上一周期对比分析，支持导出。

**Blocked by:** 19 — 数据可视化增强（更多图表类型）

**Status:** completed

- [x] 实现自定义时间范围报表 API (GET /api/statistics/custom?startDate=&endDate=&ledgerId=)，返回汇总、支出/收入分类统计、日粒度趋势数据
- [x] 实现环比分析 API（GET /api/statistics/category-comparison，与上一等长周期对比，返回各分类金额变化与变化率，分类取并集）
- [x] 创建自定义报表页面（/custom-report：起止日期、账本筛选、本月/上月/近30天/近90天快捷范围）
- [x] 分类环比对比展示（本期 vs 上期金额、升/降箭头 + 变化率，"新增/归零"边界标识，变化最大的分类排前面）
- [x] 报表数据导出为 CSV（汇总 + 支出分类明细，UTF-8 BOM 头保证 Excel 中文不乱码，data URI 触发下载）
- [x] 报表分享功能（Web Share API 系统分享面板，回退复制到剪贴板；摘要含总览、支出 Top3、环比增减最多的分类）

## 实现说明

- **Core 层**：`IStatisticsService` 新增 `GetCustomStatisticsAsync`（自定义范围汇总+分类统计+日趋势）与 `GetCategoryComparisonAsync`（支出分类环比，上一等长周期按当前周期天数紧邻回推），模型 `CustomStatistics` / `CategoryComparisonData`（`ChangeRate` 上期为 0 时为 null）；私有 `BuildCategoryStats` 统一分类聚合与占比计算（含导航 Include，服务端内存聚合）。
- **API 层**：`StatisticsController` 新增 `GET /api/statistics/custom` 与 `GET /api/statistics/category-comparison`（日期校验沿用现有风格）。
- **页面**：复用 issue 19 的 ChartCard/CategoryRankChart/ExpenseIncomeDonutChart 组件渲染报表图表；日期解析与范围校验在前端先行（Snackbar 提示），服务端双重校验。
- **领域术语**：CSV 表头与摘要遵循 CONTEXT.md 约定（账单/分类）。
- **验证**：`dotnet build FinanceTracker.sln` 通过（0 错误），单元测试 27/27 通过。