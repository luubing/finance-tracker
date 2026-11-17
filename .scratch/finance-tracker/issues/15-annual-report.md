# 15 — 年度报表

**What to build:** 年度总览和同比/环比分析。

**Blocked by:** 09 — 月度统计报表

**Status:** completed

- [x] 实现年度统计 API (GET /api/statistics/annual)
- [x] 实现同比分析 API (GET /api/statistics/year-over-year)
- [x] 创建年度报表页面
- [x] 年度总览（总支出、总收入、净收支）
- [x] 月度趋势图（12个月）
- [x] 分类年度统计
- [x] 同比分析（与去年对比）

## 同比口径（评审 P10/P11 修订）

- `GET /api/statistics/year-over-year` 的 `month` 参数改为可选：
  - 带 `month`：月度同比，本月 vs 去年同月（整月比较；去年同月为闰月 2 月时包含 2 月 29 日）。
  - 省略 `month`：年度同比（年度报表页直接调用 `IStatisticsService.GetAnnualYearOverYearDataAsync`）。
- 年度同比的取数范围：
  - 已结束的年份：整年 vs 上一整年。
  - 当前年份：年初至今（含今天）vs 去年同月同日（含当天），避免"进行中的一年"与"去年整年"比较造成的假性下降。今天为 2 月 29 日时，去年同期截止到 2 月 28 日。"今天"按应用时区取（见 `docs/adr/0005-time-zone-handling.md`）。
  - 未来年份：当年无账单，按整年比较，对比上一整年。
- 变化率 `ExpenseChangeRate` / `IncomeChangeRate` 改为可空：上期金额 ≤ 0 时为 `null`（无对比基数），不再返回 0；页面显示"—"，并在"去年同期"旁标注"无对比基数"。
- 返回值新增 `CurrentPeriodStart/End`、`PreviousPeriodStart/End`（均含首尾日期），页面用它们展示"对比期间"，让用户看清实际对比的是哪两段时间。
- 回归测试：`tests/unit/FinanceTracker.Tests/Services/YearOverYearTests.cs`。
