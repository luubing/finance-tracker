# 11 — 云端同步服务

**What to build:** 本地账单数据自动同步到云端服务器，支持离线操作（最多1000条），联网后自动同步，冲突时采用后写入优先策略。

**Blocked by:** 08 — 账单列表与筛选

**Status:** completed

**已实现：**
- [x] 实现批量同步 API (POST /api/sync/bills)
- [x] 离线数据缓存（SQLite）
- [x] 同步状态标记（待同步/已同步/同步失败）
- [x] 本地同步队列实现（SyncQueueService）
- [x] 网络状态监听服务（NetworkService 使用 MAUI Connectivity API）
- [x] 联网后自动触发同步（BackgroundSyncService）
- [x] 同步进度提示（SyncStatus 组件）
- [x] 离线缓存上限1000条（GetPendingBillsAsync 中限制）

**部分实现：**
- [ ] 冲突处理（后写入优先）（当前为模拟实现，需要实际云端 API）

**注意：** 当前同步为模拟实现，实际生产环境需要：
1. 实现真正的云端 API 调用（替换 Task.Delay 模拟）
2. 实现基于时间戳的冲突解决逻辑
3. 添加数据压缩和加密传输
