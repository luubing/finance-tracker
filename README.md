# FinanceTracker（记账本）

个人记账应用，支持多支付渠道、分类统计和云端同步。

## 功能特性

- **手动记账**：支持支出/收入账单录入
- **分类管理**：预设分类 + 用户自定义分类（区分支出/收入）
- **支付渠道管理**：预设渠道（微信支付、支付宝、现金等）+ 用户自定义渠道
- **账单列表**：查看历史账单
- **统计报表**：月度统计（饼图）、趋势图（折线图）
- **用户登录**：手机号注册/登录（JWT 认证）
- **云端同步**：实时同步 + 离线缓存（最多 1000 条），冲突采用"后写入优先"（Last Write Wins）

第二版规划：导入账单（微信/支付宝 CSV）、短信识别（Android）、年度报表。详见 [IMPLEMENTATION-PLAN.md](IMPLEMENTATION-PLAN.md)。

## 技术栈

| 类别 | 技术 |
|------|------|
| 运行时 | .NET 10.0 |
| 移动端 | .NET MAUI Blazor (Android/iOS) + MASA Blazor UI |
| Web 端 | Blazor Server |
| 后端 | ASP.NET Core Web API + Swagger |
| ORM | Entity Framework Core |
| 数据库 | 服务端 PostgreSQL (Npgsql)，客户端本地 SQLite |
| 认证 | JWT Bearer（手机号作为唯一标识） |

## 项目结构

```
FinanceTracker/
├── src/
│   ├── FinanceTracker.App                  # MAUI Blazor 主应用 (Android/iOS)
│   ├── FinanceTracker.Web                  # Blazor 共享组件库（App 与 Web.Server 共用）
│   ├── FinanceTracker.Core                 # 核心业务逻辑、实体定义、接口
│   ├── FinanceTracker.Shared               # 共享模型/DTO/常量（无依赖）
│   ├── FinanceTracker.Infrastructure       # EF Core 上下文（PostgreSQL）、仓储实现
│   └── FinanceTracker.Infrastructure.Sqlite# 本地 SQLite 基础设施
├── server/
│   ├── FinanceTracker.Api                  # REST API 服务
│   └── FinanceTracker.Web.Server           # Blazor Server 托管
├── tests/
│   └── e2e/                                # 端到端测试脚本（Python），见 tests/e2e/README.md
├── docs/
│   └── adr/                                # 架构决策记录 (ADR)
├── deploy/                                 # 部署辅助文件（glibc 兼容库等）
├── docker-compose.yml                      # PostgreSQL 开发环境
└── deploy.sh                               # 一键发布部署脚本
```

关键依赖链：`App → Web → Core → Shared`，`Api → Core → Shared`，`Infrastructure → Core`

## 快速开始

### 环境要求

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- Docker（用于运行 PostgreSQL）
- MAUI 工作负载（仅调试移动端需要）：`dotnet workload install maui`

### 1. 启动数据库

```bash
docker compose up -d
```

将启动 PostgreSQL 17（端口 `5432`，用户名/密码均为 `postgres`，数据库 `finance_tracker`）。

### 2. 运行 API 服务

```bash
dotnet run --project server/FinanceTracker.Api
```

- HTTP 地址：`http://localhost:5065`
- Swagger 文档随 API 一起提供

### 3. 运行 Web 端 (Blazor Server)

```bash
dotnet run --project server/FinanceTracker.Web.Server
```

- HTTPS 地址：`https://localhost:6001`

### 4. 运行移动端 (MAUI Blazor)

Windows 上建议使用 Android 模拟器调试，可在 Visual Studio 中将 `FinanceTracker.App` 设为启动项目，或：

```bash
dotnet build src/FinanceTracker.App/FinanceTracker.App.csproj -f net10.0-android
```

## 数据库迁移

EF Core 迁移操作（在仓库根目录执行）：

```bash
dotnet ef migrations add <MigrationName> --project src/FinanceTracker.Infrastructure
dotnet ef database update --project src/FinanceTracker.Infrastructure
```

## 配置说明

API 服务配置位于 `server/FinanceTracker.Api/appsettings.json`：

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=finance_tracker;Username=postgres;Password=postgres"
  },
  "Jwt": {
    "SecretKey": "<至少 32 位的密钥>",
    "Issuer": "FinanceTracker",
    "Audience": "FinanceTrackerApp",
    "ExpiryInMinutes": 10080
  }
}
```

> ⚠️ 生产环境请务必通过 `appsettings.Production.json` 或环境变量覆盖默认的数据库密码与 JWT 密钥。

## 部署

目标服务器为 Linux（.NET 运行时 + PostgreSQL + Nginx 反向代理），提供两种方式：

**一键部署（推荐）**：

```bash
./deploy/one-click-deploy.sh finance.peiran.site
```

**发布脚本**：构建并上传到服务器，重启 systemd 服务：

```bash
./deploy.sh [服务器IP] [用户名]
```

脚本会依次发布 API 与 Web.Server（`linux-x64`）、替换 glibc 兼容的 SQLite 原生库、打包上传并重启 systemd 服务。

详细的部署步骤、Docker 部署、SSL 证书配置请参见 [deploy/README.md](deploy/README.md)、[deploy/QUICK-DEPLOY.md](deploy/QUICK-DEPLOY.md) 和 [deploy/DEPLOY-CHECKLIST.md](deploy/DEPLOY-CHECKLIST.md)。glibc 兼容修复见 `deploy/GLIBC-FIX.md`。

## 领域术语约定

代码与文档中统一使用以下中文术语：

| 术语 | 英文 |
|------|------|
| 账单 | Bill |
| 分类 | Category |
| 支付渠道 | PaymentChannel |
| 数据来源 | BillSource |
| 同步状态 | SyncStatus |
| 软删除 | Soft Delete |

## 相关文档

- [CLAUDE.md](CLAUDE.md) — AI 辅助开发指引（架构、命令、注意事项）
- [CONTEXT.md](CONTEXT.md) — 领域语言定义
- [IMPLEMENTATION-PLAN.md](IMPLEMENTATION-PLAN.md) — 完整实现计划与数据模型
- [docs/adr/](docs/adr/) — 架构决策记录
- [tests/e2e/README.md](tests/e2e/README.md) — 端到端测试说明
