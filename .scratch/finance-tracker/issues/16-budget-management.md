# 16 — 预算设置与管理

**What to build:** 用户可以设置每月的预算（总预算 + 分类预算），支持按账本设置独立预算，提供预算的增删改查。

**Blocked by:** 09 — 月度统计报表

**Status:** completed

- [x] 在 Core 项目中定义 Budget 实体（UserId、LedgerId 可空/null 表示全部账本、Year、Month、Amount、CategoryId 可空/null 表示总预算、IsDeleted 软删除）
- [x] 定义 IBudgetService 接口并实现 BudgetService（含同一用户同一月份同一账本范围的预算唯一性校验）
- [x] 在 IApplicationDbContext 中注册 Budget 实体，添加 PostgreSQL 与 SQLite 双端数据库迁移（AddBudget）
- [x] 实现预算 CRUD API (GET/POST/PUT/DELETE /api/budgets)，支持按年月与账本筛选
- [x] 创建预算设置页面（设置月度总预算、按分类设置子预算）
- [x] 预算列表展示与编辑（按月份切换查看历史预算）
- [x] 账单软删除后预算执行统计需排除已删除账单（通过 Bill 的全局查询过滤器自动排除）

**已实现：**
- 预算执行情况 API (GET /api/budgets/status) 与 BudgetStatus 模型（预算额、已用额、剩余额、使用百分比），为 issue 17 提供数据支撑
- 预算页面（/budgets）带月份切换、进度条展示（80% 黄色预警 / 100% 红色超支）
- "我的"页面新增"预算管理"入口
- IBudgetService 在 Api / Web.Server / MAUI App 三个宿主中注册