# 18 — 共享账本

**What to build:** 支持多用户共享一个账本：成员邀请、角色权限（所有者/可编辑/只读）、共享账本内账单对成员可见。多账本与账本间数据隔离已由现有 Ledger 实体、ILedgerService、LedgersController、Ledgers.razor 实现，本 issue 只覆盖共享能力。

**Blocked by:** 11 — 云端同步服务

**Status:** completed

- [x] 编写架构决策记录 docs/adr/0004-shared-ledger-permissions.md（权限模型与数据隔离方案）
- [x] 在 Core 项目中定义 LedgerMember 实体（LedgerId、UserId、Role：Owner/Editor/Viewer、JoinedAt；另含 Status：Pending/Active 支撑邀请确认）
- [x] 添加数据库迁移（PostgreSQL + SQLite 双端 AddLedgerMember），并为 (LedgerId, UserId) 建立唯一约束
- [x] 实现邀请机制（账本所有者按手机号邀请用户加入，生成 Pending 邀请，被邀请人在账本管理页确认后生效；拒绝/被移除后可重新邀请复用原记录）
- [x] 实现成员管理 API（LedgerMembersController：邀请/移除成员、修改角色、转让所有权、退出账本、待处理邀请列表/响应）
- [x] 权限校验（SyncController.Push 校验账本写权限；Viewer 只读、Editor 可记账、Owner 管理成员与账本；历史账本懒补 Owner 成员行）
- [x] 共享账本数据拉取：sync/pull 纳入共享账本内其他成员的账单（只读展示），客户端合并时保留原作者 UserId 并标记 Synced 不回推
- [x] 共享账本内账单归属与同步策略（成员各自记账，账单通过 LedgerId 归入共享账本，云端合并可见；本地 Bills 页按共享账本筛选时展示全部成员账单）
- [x] 账本成员管理页面（LedgerMembers.razor：成员列表、邀请、移除、修改角色、转让所有权、退出）
- [x] Ledgers.razor 区分"我的账本"与"共享账本"展示，并标注当前用户在共享账本中的角色
- [x] 处理边界情况：Owner 不能退出/被移除，须先转让所有权（转让后原 Owner 降为 Editor）；账本删除通过既有账本同步的 IsDeleted 标记传播到成员端

## 实现说明

- **架构（见 ADR 0004）**：成员关系以云端数据库为真相源；Api 宿主用 DB 实现（`LedgerMemberService`），Web.Server/MAUI 宿主用 `HttpLedgerMemberService`（管理操作走云端 API，权限判定读本地 SQLite 缓存，离线可用）。
- **本地缓存**：客户端通过 `sync/ledgermembers/pull` 同步成员关系到本地 SQLite，合并时自动补建外部用户存根（仅 Id+手机号，满足外键）与账本存根；账本/账单拉取端点同步扩展了共享范围。
- **云同步集成**：`SyncBillsAsync` 在同步账本后追加 `SyncLedgerMembersAsync`；拉取的共享账单保留原作者并标记 Synced（推送队列只取 `UserId == 本地用户` 的待同步账单，不会回推）。
- **Code Review 补遗（2026-09-05）**：共享账本此前未接入 AddBill/Bills 的账本选择器（成员无法记账到共享账本、无法按共享账本筛选账单），已修复——两页合并自有+共享账本并标注"（共享）"，AddBill 保存前本地 `EnsureCanWriteAsync` 即时校验。
- **已知边界**：统计/预算按 UserId 过滤，保持个人口径（暂不纳入共享账本内他人账单）；成员同步需网络，离线时使用上次缓存。
- **验证**：`dotnet build FinanceTracker.sln` 通过（0 错误），单元测试 27/27 通过，SQLite 迁移已实际应用验证。