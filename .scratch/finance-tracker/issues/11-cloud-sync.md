# 11 — 云端同步服务

**What to build:** 本地账单数据自动同步到云端服务器，支持离线操作（最多1000条），联网后自动同步，冲突时采用后写入优先策略。

**Blocked by:** 08 — 账单列表与筛选

**Status:** completed（「联网后自动触发同步」除外，见下方「未实现」）

**已实现：**
- [x] 实现批量同步 API（`POST /api/sync/push`、`POST /api/sync/pull`；分类/支付渠道/账本/账本成员见 `sync/categories|paymentchannels|ledgers|ledgermembers/*`）
- [x] 离线数据缓存（SQLite）
- [x] 同步状态标记（待同步/已同步/同步失败）
- [x] 同步队列：以同步状态（Pending/Failed）查询作为队列。原先独立的内存队列 `SyncQueueService` 从未被任何代码调用，已移除
- [x] 网络状态监听服务（NetworkService 使用 MAUI Connectivity API）
- [x] 同步进度提示（SyncStatus 组件）
- [x] 离线缓存上限1000条（GetPendingBillsAsync 中限制）
- [x] 冲突处理（后写入优先）：服务端按 `UpdatedAt` 裁决，本地更旧时回传云端版本并覆盖本地

**未实现：**
- [ ] 联网后自动触发同步：原 `BackgroundSyncService` 虽在 DI 中注册，但从未被解析/启动，实际从未生效，已移除。目前同步由用户在「同步状态」组件手动触发，导入账单后会自动触发一次。
  - 联网事件源（`INetworkService.ConnectivityChanged`）仍保留，后续如需实现可在其上订阅。

**遗留说明：**
- 数据压缩与传输加密（依赖部署时的 HTTPS）未做专门实现
