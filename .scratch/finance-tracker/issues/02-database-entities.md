# 02 — 数据库与实体定义

**What to build:** 定义所有核心实体（User、Bill、Category、PaymentChannel）和枚举，配置 EF Core 上下文，创建 PostgreSQL 数据库并生成初始迁移。

**Blocked by:** 01 — 项目脚手架搭建

**Status:** completed

- [x] 在 Core 项目中定义 User 实体
- [x] 在 Core 项目中定义 Bill 实体
- [x] 在 Core 项目中定义 Category 实体
- [x] 在 Core 项目中定义 PaymentChannel 实体
- [x] 定义 BillType、BillSource、SyncStatus 枚举
- [x] 在 Infrastructure 项目中配置 EF Core DbContext
- [x] 配置实体关系和索引
- [x] 创建初始数据库迁移
- [ ] 数据库可以成功创建（需要 PostgreSQL 运行）
