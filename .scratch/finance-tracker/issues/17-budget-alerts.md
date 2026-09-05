# 17 — 预算执行情况与超支提醒

**What to build:** 实时展示预算执行进度（已用/剩余/百分比），接近预算时预警，超支时提醒。

**Blocked by:** 16 — 预算设置与管理

**Status:** completed

- [x] 实现预算执行情况 API (GET /api/budgets/status?year=&month=&ledgerId=)，返回每个预算的预算额、已用额、剩余额、使用百分比（issue 16 阶段已提前实现）
- [x] 在首页 (Index.razor) 展示当月预算进度条与剩余额度（最多展示 3 条 + 汇总提示，点击"管理"跳转 /budgets）
- [x] 在月度统计页面 (MonthlyStatistics.razor) 增加预算执行情况区块（随月份/账本筛选联动）
- [x] 接近预算预警（使用率达到 80% 时黄色警示）
- [x] 超支提醒（使用率达到 100% 时红色警示 + 应用内提示）
- [x] 记账保存后若导致超支，给出超支提示（在 AddBill.razor 保存流程中集成：Core 新增 IBudgetService.GetBudgetAlertAsync，返回与该笔支出相关的预算中使用率最高的一条预警状态）
- [x] 超支本地通知（MAUI 本地通知，Android/iOS，仅在超支时每日最多提醒一次）

## 实现说明

- **Core 层**：`IBudgetService` 新增 `GetBudgetAlertAsync`（账单保存场景的预警查询）与 `BudgetAlert` 模型；新增 `IBudgetNotificationService` 抽象（发送通知 + 当天去重标记）与 `NoOpBudgetNotificationService` 空实现。
- **Web 层**：新增 `BudgetAlertService`（存在超支预算时调用本地通知服务，每日最多一次）。
- **MAUI App**：`BudgetNotificationService` 通过 `#if ANDROID/#elif IOS` 提供 Android NotificationChannel 通知与 iOS UNUserNotificationCenter 通知，使用 Preferences 记录当天已提醒（每日最多一次）；点击通知回到应用。
- **宿主注册**：App 注册原生实现；Web.Server 注册 NoOp 实现；`BudgetAlertService` 在两宿主均为 Scoped。
- **验证**：`dotnet build FinanceTracker.sln` 通过（0 错误），单元测试 27/27 通过。
- **注意**：Android 13+ 发布通知需要 POST_NOTIFICATIONS 运行时权限，当前实现未主动申请（静默失败，后续可按需补充权限引导）。