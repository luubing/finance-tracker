# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

FinanceTracker (记账本) - 个人记账应用，支持多支付渠道、分类统计和云端同步。

## 常用命令

```bash
# 构建整个解决方案
dotnet build FinanceTracker.sln

# 构建单个项目
dotnet build src/FinanceTracker.App/FinanceTracker.App.csproj
dotnet build server/FinanceTracker.Api/FinanceTracker.Api.csproj

# 运行 API 服务器
dotnet run --project server/FinanceTracker.Api

# 运行 Blazor Server (Web端)
dotnet run --project server/FinanceTracker.Web.Server

# 还原依赖
dotnet restore

# EF Core 数据库迁移 (在 server 目录下)
dotnet ef migrations add <MigrationName> --project ../src/FinanceTracker.Infrastructure
dotnet ef database update --project ../src/FinanceTracker.Infrastructure
```

## 技术栈

- **目标框架**: .NET 10.0
- **移动端**: .NET MAUI Blazor (Android/iOS) + MASA Blazor UI 组件库
- **后端**: ASP.NET Core Web API
- **ORM**: Entity Framework Core + Npgsql (PostgreSQL)
- **本地数据库**: SQLite
- **认证**: JWT Bearer (手机号作为唯一标识，无验证码)

## 项目结构与依赖关系

```
src/
├── FinanceTracker.App          # MAUI Blazor 主应用 (Android/iOS)，依赖 Core/Web/Infrastructure/Shared
├── FinanceTracker.Web          # Blazor 组件库，被 App 和 Web.Server 共享
├── FinanceTracker.Core         # 核心业务逻辑、实体定义、接口 (依赖 Shared)
├── FinanceTracker.Shared       # 共享模型/DTO/常量 (无依赖)
└── FinanceTracker.Infrastructure # EF Core 上下文、仓储实现、外部服务

server/
├── FinanceTracker.Api          # REST API 服务 (依赖 Core/Shared/Infrastructure)
└── FinanceTracker.Web.Server   # Blazor Server 托管 (依赖 Web 项目)
```

关键依赖链: `App → Web → Core → Shared`, `Api → Core → Shared`, `Infrastructure → Core`

## 领域语言约定

代码中必须使用以下中文对应术语，避免使用括号内的替代词：

| 术语 | 英文 | 避免使用 |
|------|------|----------|
| 账单 | Bill | 交易、记录、流水 |
| 分类 | Category | 标签、类型 |
| 支付渠道 | PaymentChannel | 支付方式、付款方式 |
| 账本 | Ledger | 账簿、笔记本 |
| 数据来源 | BillSource | 录入方式、来源 |
| 同步状态 | SyncStatus | 状态 |
| 软删除 | Soft Delete | 删除、物理删除 |

## 核心数据模型

- **Bill (账单)**: 金额、类型(支出/收入)、分类、支付渠道、账本、交易时间、数据来源、同步状态
- **Category (分类)**: 预设分类(UserId=null) + 用户自定义分类，区分支出/收入类型
- **PaymentChannel (支付渠道)**: 预设渠道 + 用户自定义渠道
- **Ledger (账本)**: 用户自建账本（无预设），账单通过 LedgerId 归属到账本（可空，null 表示未归属账本）
- **User (用户)**: 手机号作为唯一标识

所有实体使用 `IsDeleted` 字段实现软删除，使用 `Guid` 作为主键。

## 关键架构决策

1. **同步策略**: 实时同步 + 离线缓存(最多1000条)，冲突处理采用"后写入优先"(Last Write Wins)
2. **认证方式**: 手机号手动输入，无验证码，本地存储会话状态
3. **API 风格**: REST，标准 CRUD 操作
4. **数据来源**: 手动录入、导入(微信/支付宝CSV)、短信识别、通知栏、语音识别（语音仅预填表单，用户确认后保存，来源标记为 BillSource.Voice）

## 开发注意事项

- MAUI Blazor 项目需要 Android/iOS 目标框架，Windows 上主要用 Android 模拟器调试
- FinanceTracker.Web 是共享组件库，同时被 MAUI App 和 Blazor Server 引用
- 预设分类和支付渠道在 IMPLEMENTATION-PLAN.md 中有完整列表
