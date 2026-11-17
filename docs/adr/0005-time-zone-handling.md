# 账单时间的时区处理

## 决策

**存储与同步一律用 UTC；用户看到和输入的时间一律是"应用时区"的墙上时间。**
两种口径之间只经 `FinanceTracker.Core.Time.AppTimeZone` 换算，业务代码不再直接调用
`ToUniversalTime()` / `ToLocalTime()`。

"应用时区"默认等于运行环境的本地时区：

| 宿主 | 应用时区 |
|------|----------|
| MAUI App（Android/iOS） | 设备时区 |
| Blazor Server（Web.Server） | **服务器**时区（浏览器时区不参与，见"已知限制"） |
| REST API（Api） | 服务器时区 |

## 口径速查

| 位置 | 口径 | 说明 |
|------|------|------|
| `Bill.TransactionTime`（SQLite / PostgreSQL） | UTC | EF 值转换器读出时统一标记 `Kind=Utc` |
| 同步 DTO（`BillSyncDto`） | UTC | 既有约定：Unspecified 视为 UTC |
| 表单输入、编辑回填、列表时间 | 应用时区墙上时间 | 页面只拿 `AppTimeZone.ToLocal(...)` 之后的值 |
| CSV / 短信 / 通知 / 语音解析出的时间 | 应用时区墙上时间 | 解析结果不带时区，创建账单时由 `BillService` 换算为 UTC |
| "某天/某月/某年"、周起点、预算月份 | 应用时区的自然日历 | 查询时把本地边界换算成 UTC，分组时先转回本地 |
| `PendingBill.TransactionTime`、`CapturedAt` | 应用时区墙上时间 | 待确认账单，尚未入账，不做换算 |

## `Kind` 解释规则

`DateTime.Kind` 不可靠：EF Core 从 SQLite 读出的 `DateTime` 其 `Kind` 为 `Unspecified`，
任何后续的 `ToUniversalTime()` 都会把它误当本地时间再减一次偏移。规则如下：

- `AppTimeZone.ToUtc(value)`：`Utc` 原样返回；`Local` 与 `Unspecified` **都按应用时区的墙上时间**解释
  （`Local` 也按应用时区而不是系统时区，这样测试可以用固定时区覆盖，结果与机器无关）。
- `AppTimeZone.ToLocal(value)`：面向"库里读出来的值"，`Utc` 与 `Unspecified` 都当 UTC；
  `Local` 视为已经是本地时间。返回值 `Kind=Unspecified`，可直接格式化、取 `.Date`、分组。
- EF 值转换器（`ApplicationDbContext`）：`Bill.TransactionTime` 读出时标记为 `Utc`；
  写入时 `Local` 按**应用时区**转 UTC（走 `AppTimeZone.ToUtc`，不是系统时区的 `ToUniversalTime()`）、
  `Unspecified` 按 UTC 对待。提供程序类型仍是 `DateTime`，列类型不变，**不需要迁移**
  （`ModelSnapshotTests` 会守住这一点）。

## 各层职责

- **`BillService`**：创建/更新时把 `TransactionTime` 经 `AppTimeZone.ToUtc` 入库；
  列表/计数的 `startDate` / `endDate` 筛选条件是应用时区时刻，先换算成 UTC 再与库里比较。
  其中 **`endDate` 是"含端点的结束时刻"**（`<=`）。
- **`StatisticsService`、`BudgetService`**：月度/年度/分类/预算等汇总区间统一**左闭右开**，本地边界
  （月初、年初、当日 00:00 的次日 00:00）换算为 UTC 后查询；按天/周/月/年分组前先 `ToLocal`。
  否则东八区用户 3 月 1 日凌晨记的账（UTC 为 2 月 28 日）会被算进 2 月。
  唯一的例外是 `GetTrendDataAsync` 的 `endDate`，它沿用"含端点的结束时刻"契约（"最近 N 天"以此刻为终点）。
- **"含端点"的结束时刻必须取最后一个 tick**：需要覆盖整天/整月时，一律用 `TimeBoundaries.EndOfDay` /
  `TimeBoundaries.EndOfMonth` 构造（= 次日 00:00 减 **1 个 tick**），不要手写"减 1 秒"或 `23:59:59`——
  那会漏掉最后一秒内（如 23:59:59.500）的账单，导致账单列表、趋势图与按左闭右开统计的月度汇总对不上
  （`TimeZoneBoundaryTests` 有对应的回归用例）。
- **页面**（`Bills`、`Index`、`AddBill`、`Profile`）：展示与编辑回填前 `ToLocal`；保存时直接交出表单里的本地时间。
- **导入接口**：见下节。

## 导入：由客户端上报时区偏移

微信/支付宝 CSV 的交易时间是**不带时区的用户本地时间**，而导入发生在云端服务器。
服务器多半是 UTC，若按服务器时区解释，东八区用户导入的账单会差 8 小时。

因此 `ImportRequest` 增加可选的 `UtcOffsetMinutes`（东八区为 480），导入页确认导入时用
`AppTimeZone.GetUtcOffsetMinutes()` 上报设备时区偏移，服务器按该偏移把 CSV 时间换算为 UTC。

- 未上报（旧版客户端）：按服务器应用时区解释，保持原行为。
- 超出 [-840, 840] 分钟返回 400。
- 预览接口只解析不入库，展示的是 CSV 原始时间，不受影响。

## 夏令时

- 夏令时"不存在的时间"（春季跳变丢失的那一小时）和"重复的时间"不抛异常，按时区标准偏移处理——
  `TimeZoneInfo.ConvertTimeToUtc` 对前者会抛 `ArgumentException`，而这类时间可能来自手填或导入。
- 极值（`DateTime.MinValue` / `MaxValue`）换算时钳制，不抛 `ArgumentOutOfRangeException`。
- "一天"永远是应用时区的自然日（可能只有 23 或 25 小时），不是固定的 24 小时。

## 已知限制

1. **Blazor Server 用服务器时区**：`Web.Server` 的页面代码跑在服务器上，浏览器时区不参与。
   自托管给多时区用户共用时，所有人看到的都是服务器时区的时间；单人自用（服务器与用户同时区）没有影响。
2. **REST API**：`GET /api/bills` 等接口的不带时区的查询时间按服务器时区解释；
   调用方应传带 `Z` 或偏移的 ISO 8601 时间。
3. **导入偏移取导入时刻**：夏令时地区的用户导入跨越夏令时切换的账单，切换另一侧的账单会差 1 小时。
   中国等无夏令时地区不受影响。
4. **升级前的历史数据不自动迁移**：
   - 手动录入、短信/通知/语音创建的账单，升级前就已经是 UTC 入库，只是以前被直接按 UTC 展示（东八区会早 8 小时）；
     升级后展示恢复正确，肉眼会看到与升级前不同——这是修复，不是回归。
   - 经云端导入的账单（`Source = Import`）：升级前服务器把 CSV 本地时间按服务器时区解释。
     若服务器是 UTC，库里存的是"把本地时间当成 UTC"的值，升级后这些账单会多显示出一个时区偏移。
     修正需要知道导入当时的服务器时区与用户时区，且要同时处理云端库与各设备本地库、避免与同步（后写入优先）冲突，
     因此不做自动迁移。确需修正时：只处理 `Source = 1`（Import）的账单，
     把 `TransactionTime` 减去（用户偏移 − 当时服务器偏移），在云端库修改并更新 `UpdatedAt`
     让各设备拉取覆盖；操作前备份，并先在副本库验证。

## 考虑的替代方案

- **本地时间入库**：同步、跨设备、跨时区出差时语义混乱，放弃。
- **`DateTimeOffset`**：需要改实体、DTO 和两套迁移，收益主要是保留记账时的偏移；
  本应用只关心"用户眼里的那一天"，不需要保留，放弃。
- **仅在页面层换算**：统计、预算的月/年边界在服务层，页面换算无法保证统计与列表口径一致，放弃。
- **导入时由服务器猜用户时区**（按 IP / 请求头）：不可靠，放弃，改为客户端显式上报。
