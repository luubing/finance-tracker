# FinanceTracker（记账本）

个人记账应用，支持多支付渠道、分类统计和云端同步。

> **自托管的开源项目**：仓库里没有线上服务地址、测试账号或预置的云端数据。云端服务需要自己部署（见「部署」），App 连接哪台服务器在打包时指定（见「构建与发布」）。
> 采用 [MIT 许可证](LICENSE)；数据的去向见 [PRIVACY.md](PRIVACY.md)，安全边界与漏洞报告方式见 [SECURITY.md](SECURITY.md)。

## 功能特性

- **手动记账**：支持支出/收入账单录入
- **分类管理**：预设分类 + 用户自定义分类（区分支出/收入）
- **支付渠道管理**：预设渠道（微信支付、支付宝、现金等）+ 用户自定义渠道
- **账单列表**：独立的「账单」Tab，按日分组、左滑编辑 / 删除；可按时间、分类、支付渠道、账本、类型等条件筛选，也可按关键字（备注 / 分类名称 / 金额）搜索；顶部汇总卡显示当前条件下**全部**账单的支出、收入与笔数（不受分页影响）；历史上没有账本的账单可批量「归属账本」
- **账单导入**：微信/支付宝账单 CSV 导入，自动匹配分类与支付渠道
- **自动识别**：短信识别、通知栏支付监听（Android）、语音记账（识别结果仅预填表单，确认后保存）
- **统计报表**：「统计」Tab 里的月度 / 趋势 / 年度三个分段（月度环形图与分类排行、趋势折线图、年度同比），自定义报表（分类环比、CSV 导出）从统计页右上角进入
- **预算管理**：总预算 + 分类预算（可按账本独立设置），环形总览与「日均可用」提示（查看当前月份时），超支本地通知（每日最多一次）
- **多账本与共享账本**：账本数据隔离；共享账本支持邀请确认与 Owner/Editor/Viewer 三角色权限
- **默认值偏好**：在分类、支付渠道、账本管理页点击星标，可把常用的支出分类、收入分类、支付渠道、账本设为默认（再点一次取消），记账时自动预选；每个用户独立保存，共享账本不能设为默认
- **用户登录**：手机号注册/登录（JWT 认证）
- **云端同步**：离线缓存（最多 1000 条）+ 手动触发同步（「同步状态」组件；导入账单后会自动触发一次），冲突采用"后写入优先"（Last Write Wins）。联网后自动同步尚未实现，详见 [Issue 11](.scratch/finance-tracker/issues/11-cloud-sync.md)
- **错误日志查看**：用 Serilog 把 Error 及以上级别的日志写入本地文件（按天滚动、保留 14 天、单文件 ≤ 20 MB），在「我的 → 错误日志」里按时间/级别/关键字筛选、查看详情、导出与清空（MAUI 端）
- **iOS 风格界面**：液态玻璃的四个 Tab（首页 / 账单 / 统计 / 我的）+ 独立的「记一笔」圆钮，沉浸式状态栏与安全区，Tab 滑块切换；系统「降低透明度」「减弱动态效果」、低端设备与大字号下自动降级。设计令牌与复用组件见 [ADR 0007](docs/adr/0007-ios-visual-system.md)，设计稿在 [docs/design/](docs/design/)；只有浅色主题

各版本功能的拆解与完成情况详见 [IMPLEMENTATION-PLAN.md](IMPLEMENTATION-PLAN.md)。

## 技术栈

| 类别 | 技术 |
|------|------|
| 运行时 | .NET 10.0 |
| 移动端 | .NET MAUI Blazor (Android/iOS) + MASA Blazor（基础组件与页面栈）；视觉是 iOS 风格设计系统（设计令牌 + 复用组件，见 [ADR 0007](docs/adr/0007-ios-visual-system.md)） |
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
│   ├── adr/                                # 架构决策记录 (ADR)
│   └── design/                             # UI 设计稿（iOS 风格重构、冷启动）
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

项目有**两套**迁移历史：改了实体或 `ApplicationDbContext` 后要各生成一份（在仓库根目录执行，需要先 `dotnet tool install -g dotnet-ef`）：

```bash
# 本地 SQLite（App 与 Web.Server 使用）
dotnet ef migrations add <MigrationName> --project src/FinanceTracker.Infrastructure.Sqlite

# 云端 PostgreSQL（Api 使用）：设计时工厂默认走 SQLite，要用环境变量切到 Npgsql
export FT_DB_PROVIDER=postgresql        # PowerShell：$env:FT_DB_PROVIDER = 'postgresql'
dotnet ef migrations add <MigrationName> --project src/FinanceTracker.Infrastructure
```

不需要手动 `database update`：App、Api、Web.Server 启动时都会自动执行迁移（`MigrateAsync`）。`ModelSnapshotTests` 会检查两套迁移的快照与当前模型是否一致，漏生成其中一套时单元测试会失败。

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
> - **手机号白名单（可选）**：登录只凭手机号、没有验证码，公网部署时建议用环境变量 `Auth__AllowedPhoneNumbers`（英文逗号分隔，如 `13800000001,13800000002`；分号、换行也认）限定能登录、能调用 API 的手机号；不设置 = 不限制。共享账本按手机号邀请成员，要一起用的家人也得在名单里。名单只在 API 启动时读取，修改后要重启；详见 [SECURITY.md](SECURITY.md) 与 [ADR 0009](docs/adr/0009-cloud-access-hardening.md)。

## 部署

目标服务器为 Linux（.NET 运行时 + PostgreSQL + Nginx 反向代理）。

> **仓库里没有部署脚本。** `deploy.sh` 与 `deploy/` 目录（一键部署脚本、glibc 兼容库、部署清单等）是作者本机使用的辅助文件，已在 `.gitignore` 中忽略、未纳入版本库，克隆仓库后不会有这些文件，请不要依赖它们。

通用步骤：

1. **发布**：分别发布 API 与 Web.Server，例如 `dotnet publish server/FinanceTracker.Api -c Release -r linux-x64 -o publish/api`（Web.Server 同理）。
2. **上传并托管**：把发布目录上传到服务器，用 systemd 托管进程，Nginx 反向代理并配置 HTTPS。
3. **生产配置**：通过 `appsettings.Production.json` 或环境变量提供数据库连接串与 `Jwt__SecretKey`（见上方「配置说明」；JWT 密钥仍是占位值时 API 拒绝启动）。
4. **数据库**：API 启动时会执行 EF Core 迁移（`MigrateAsync`）；首次部署前先准备好空的 PostgreSQL 数据库。
5. **旧版 glibc**：Web.Server 使用 SQLite，目标系统的 glibc 较旧时，SQLite 原生库可能需要替换为兼容版本。

## 构建与发布

### 云端 API 地址（构建时注入）

仓库**不内置任何线上域名**。App 连接哪台云端服务器，由构建时的 MSBuild 属性 `CloudApiBaseUrl` 决定，值写进程序集元数据，运行时读取并校验：

| 构建方式 | 行为 |
|----------|------|
| 日常开发（`dotnet build` / IDE 调试），不指定 | 用本机开发地址，也就是「快速开始」里 `dotnet run` 起的 API（端口 5065）：Android 模拟器 `http://10.0.2.2:5065/`，其余平台 `http://localhost:5065/` |
| 打包发布（`dotnet publish`） | **必须指定**，没指定直接报错，避免打出一个连不上服务器的安装包 |

地址写服务器的根地址即可：带不带结尾的 `/` 都行，结尾的 `/api` 会自动去掉；必须以 `http://` 或 `https://` 开头，不能带用户名密码和 `?` 查询参数（地址会随安装包一起公开）。正式环境请使用 HTTPS。

> `dotnet build -c Release` 不算「发布」，不指定时同样用本机开发地址。要分发给别人的包请走 `dotnet publish`，或者用下面的打包脚本。

### 打包脚本

```bash
dotnet publish src/FinanceTracker.App/FinanceTracker.App.csproj -f net10.0-android -c Release -p:CloudApiBaseUrl=https://你的服务器/
```

[`build/build.sh`](build/build.sh) / [`build/build.ps1`](build/build.ps1) 把签名、版本号、上传蒲公英串在一起：先把 [`build/config.example.env`](build/config.example.env) 复制为 `build/config.env`（已被 `.gitignore` 忽略），填好 `CLOUD_API_BASE_URL` 与 Android 签名信息再运行，`CLOUD_API_BASE_URL` 没填脚本会直接报错。详见 [build/README.md](build/README.md)。

### iOS 的 GitHub Actions 构建

[`.github/workflows/build-ios.yml`](.github/workflows/build-ios.yml) 只能手动触发，构建、签名后上传到蒲公英。需要在你自己的仓库里配置 7 项 Secrets / Variables（Apple 证书及密码、描述文件及其名称、蒲公英 API Key 与安装密码、`CLOUD_API_BASE_URL`），缺哪项会在第一步就报出来。配置步骤见 [.github/SETUP-IOS-BUILD.md](.github/SETUP-IOS-BUILD.md)，证书材料的生成与轮换见 [build/certificates/README.md](build/certificates/README.md)。

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
- [docs/adr/](docs/adr/) — 架构决策记录（UI 的视觉体系见 [0007](docs/adr/0007-ios-visual-system.md)，云端地址注入与手机号白名单见 [0009](docs/adr/0009-cloud-access-hardening.md)）
- [docs/design/](docs/design/) — UI 设计稿（iOS 风格重构总览与分屏、冷启动）
- [tests/e2e/README.md](tests/e2e/README.md) — 端到端测试说明
- [CONTRIBUTING.md](CONTRIBUTING.md) — 参与贡献：开发流程、提交约定、必须守的规则
- [SECURITY.md](SECURITY.md) — 安全边界、部署检查清单、漏洞报告方式
- [PRIVACY.md](PRIVACY.md) — 数据存在哪、发到哪、用到哪些权限
- [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) — 第三方组件与许可证

## 许可证

[MIT](LICENSE) © 2026 luubing。第三方组件各自的许可证见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
