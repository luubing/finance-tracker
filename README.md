# FinanceTracker（记账本）

个人记账应用，支持多支付渠道、分类统计和云端同步。

## 功能特性

- **手动记账**：支持支出/收入账单录入
- **分类管理**：预设分类 + 用户自定义分类（区分支出/收入）
- **支付渠道管理**：预设渠道（微信支付、支付宝、现金等）+ 用户自定义渠道
- **账单列表**：查看历史账单
- **账单导入**：微信/支付宝账单 CSV 导入，自动匹配分类与支付渠道
- **自动识别**：短信识别、通知栏支付监听（Android）、语音记账（识别结果仅预填表单，确认后保存）
- **统计报表**：月度统计（饼图）、趋势图（折线图）、年度报表、自定义报表（分类环比、CSV 导出）
- **预算管理**：总预算 + 分类预算（可按账本独立设置），超支本地通知（每日最多一次）
- **多账本与共享账本**：账本数据隔离；共享账本支持邀请确认与 Owner/Editor/Viewer 三角色权限
- **默认值偏好**：在分类、支付渠道、账本管理页点击星标，可把常用的支出分类、收入分类、支付渠道、账本设为默认（再点一次取消），记账时自动预选；每个用户独立保存，共享账本不能设为默认
- **用户登录**：手机号注册/登录（JWT 认证）
- **云端同步**：离线缓存（最多 1000 条）+ 手动触发同步（「同步状态」组件；导入账单后会自动触发一次），冲突采用"后写入优先"（Last Write Wins）。联网后自动同步尚未实现，详见 [Issue 11](.scratch/finance-tracker/issues/11-cloud-sync.md)
- **错误日志查看**：用 Serilog 把 Error 及以上级别的日志写入本地文件（按天滚动、保留 14 天、单文件 ≤ 20 MB），在「我的 → 错误日志」里按时间/级别/关键字筛选、查看详情、导出与清空（MAUI 端）

各版本功能的拆解与完成情况详见 [IMPLEMENTATION-PLAN.md](IMPLEMENTATION-PLAN.md)。

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
│   ├── unit/                               # xUnit 单元测试（真实 SQLite + 迁移）
│   └── e2e/                                # 端到端测试脚本（Python），见 tests/e2e/README.md
├── docs/
│   └── adr/                                # 架构决策记录 (ADR)
└── docker-compose.yml                      # PostgreSQL 开发环境
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
- Swagger UI：Development 环境在根路径 `http://localhost:5065/` 自动开启；其他环境默认关闭（见下方「配置说明」）

### 3. 运行 Web 端 (Blazor Server)

```bash
dotnet run --project server/FinanceTracker.Web.Server
```

- 默认启动配置：`https://localhost:6001`
- e2e 测试用的 `http` 启动配置：`http://localhost:5999`（不自动打开浏览器）：

  ```bash
  dotnet run --project server/FinanceTracker.Web.Server --launch-profile http
  ```

### 4. 运行移动端 (MAUI Blazor)

Windows 上建议使用 Android 模拟器调试，可在 Visual Studio 中将 `FinanceTracker.App` 设为启动项目，或：

```bash
dotnet build src/FinanceTracker.App/FinanceTracker.App.csproj -f net10.0-android
```

### 5. 运行测试

```bash
# 单元测试（真实 SQLite + 迁移，无需数据库服务）
dotnet test tests/unit/FinanceTracker.Tests/FinanceTracker.Tests.csproj
```

端到端测试（Playwright，针对上面的 `http` 启动配置）见 [tests/e2e/README.md](tests/e2e/README.md)。

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
>
> - **JWT 密钥**：非 Development 环境下，若 `Jwt:SecretKey` 仍是仓库内的占位值，API 会**拒绝启动**（否则任何人都能凭公开的占位密钥伪造 JWT）。用环境变量 `Jwt__SecretKey` 或 `appsettings.Production.json` 提供至少 32 位的随机密钥。
> - **数据库口令**：连接串仍在使用默认口令 `postgres` 时只会在启动日志里给出警告，不会阻止启动。
> - **Swagger**：只在 Development 环境默认开启，生产环境默认关闭（公开完整的接口清单会降低被探测的门槛，而登录只需要手机号）。生产需要时设置环境变量 `Swagger__Enabled=true`；任何环境设为 `false` 都会关闭。

## 部署

目标服务器为 Linux（.NET 运行时 + PostgreSQL + Nginx 反向代理）。

> **仓库里没有部署脚本。** `deploy.sh` 与 `deploy/` 目录（一键部署脚本、glibc 兼容库、部署清单等）是作者本机使用的辅助文件，已在 `.gitignore` 中忽略、未纳入版本库，克隆仓库后不会有这些文件，请不要依赖它们。

通用步骤：

1. **发布**：分别发布 API 与 Web.Server，例如 `dotnet publish server/FinanceTracker.Api -c Release -r linux-x64 -o publish/api`（Web.Server 同理）。
2. **上传并托管**：把发布目录上传到服务器，用 systemd 托管进程，Nginx 反向代理并配置 HTTPS。
3. **生产配置**：通过 `appsettings.Production.json` 或环境变量提供数据库连接串与 `Jwt__SecretKey`（见上方「配置说明」；JWT 密钥仍是占位值时 API 拒绝启动）。
4. **数据库**：API 启动时会执行 EF Core 迁移（`MigrateAsync`）；首次部署前先准备好空的 PostgreSQL 数据库。
5. **旧版 glibc**：Web.Server 使用 SQLite，目标系统的 glibc 较旧时，SQLite 原生库可能需要替换为兼容版本。

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
