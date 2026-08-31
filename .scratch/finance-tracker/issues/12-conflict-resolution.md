# 12 — 冲突处理（后写入优先）

**What to build:** 实现基于时间戳的冲突解决逻辑，确保多设备同步时数据一致性。

**Blocked by:** 11 — 云端同步服务

**Status:** completed

- [x] 使用 UpdatedAt 字段作为版本号
- [x] 同步时比较本地和云端的 Version
- [x] 后写入优先策略：Version 更新的记录覆盖旧记录
- [x] 添加同步日志记录
