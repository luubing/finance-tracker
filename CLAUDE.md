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

# 单元测试（真实 SQLite + 迁移，无需数据库服务）
dotnet test tests/unit/FinanceTracker.Tests/FinanceTracker.Tests.csproj

# E2E（Playwright，手机视口；先用脚本启动 Web.Server :5999，详见 tests/e2e/README.md）
powershell -File tests/e2e/server.ps1 -Action start
python tests/e2e/run_all.py

# EF Core 数据库迁移 (在 server 目录下)
dotnet ef migrations add <MigrationName> --project ../src/FinanceTracker.Infrastructure
dotnet ef database update --project ../src/FinanceTracker.Infrastructure
```

## 技术栈

- **目标框架**: .NET 10.0
- **移动端**: .NET MAUI Blazor (Android/iOS) + MASA Blazor（基础组件与页面栈）；视觉是 iOS 风格设计系统（设计令牌 + 复用组件，见 docs/adr/0007-ios-visual-system.md）
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
- **Ledger (账本)**: 用户自建账本（无预设，不自动创建默认账本），账单通过 LedgerId 归属到账本。**新建/编辑账单时账本必填**（`BillService` 统一校验，界面各录入入口、账单 REST 接口、导入接口都会先拦截）；`Bill.LedgerId` 在实体/数据库/同步 DTO 中仍保持可空，只为兼容历史账单——历史上没有账本的账单照常展示与同步，统计里归入「未归属账本」，可通过编辑或账单列表的批量「归属账本」补上
- **User (用户)**: 手机号作为唯一标识

所有实体使用 `IsDeleted` 字段实现软删除，使用 `Guid` 作为主键。

## 关键架构决策

1. **同步策略**: 目标是实时同步 + 离线缓存(最多1000条)，冲突处理采用"后写入优先"(Last Write Wins)。**现状**：同步由用户手动触发（「同步状态」组件）+ 导入账单后自动触发一次，"联网后自动同步"尚未实现（原 `SyncQueueService`/`BackgroundSyncService` 从未接入业务流程，已删除）
2. **认证方式**: 手机号手动输入，无验证码，本地存储会话状态
3. **API 风格**: REST，标准 CRUD 操作
4. **数据来源**: 手动录入、导入(微信/支付宝CSV)、短信识别、通知栏、语音识别（语音仅预填表单，用户确认后保存，来源标记为 BillSource.Voice）
5. **预算管理**: Budget 实体（总预算+分类预算，可按账本独立设置），唯一性由应用层校验；超支本地通知每日最多一次（ADR 见 issue 16/17）
6. **共享账本**: 成员关系以云端数据库为真相源（LedgerMember 实体，Owner/Editor/Viewer 三角色，邀请确认制），客户端仅缓存成员关系用于本地可见性判定；架构详见 docs/adr/0004-shared-ledger-permissions.md
7. **错误码语义分离**: 401（认证失败，客户端触发重新登录）与 403（已认证但无权操作，ForbiddenAccessException）严格区分——服务层权限错误必须抛 ForbiddenAccessException，绝不能抛 UnauthorizedAccessException（否则客户端会误判为登录过期而登出）；控制器中禁止使用 Forbid(string)（参数是认证方案名而非消息，误用会导致 500），统一用 StatusCode(StatusCodes.Status403Forbidden, new { message })
8. **时间口径**: 账单时间（Bill.TransactionTime）在数据库和同步 DTO 中一律是 UTC；用户看到/输入的时间、"某天/某月/某年"边界、预算月份一律按应用时区（默认=运行环境本地时区）。两种口径之间只经 `FinanceTracker.Core.Time.AppTimeZone` 换算，业务代码禁止直接调用 `ToUniversalTime()` / `ToLocalTime()`（EF 从 SQLite 读出的 DateTime 其 Kind 为 Unspecified，极易二次换算）；统计区间统一左闭右开；导入接口由客户端上报 `UtcOffsetMinutes`；详见 docs/adr/0005-time-zone-handling.md
9. **同步身份与时间戳**: 本地库与云端库各自生成用户 Id（同一账号在云端是 C，在各设备上分别是 LA/LB），`SyncService` 在同步边界把云端返回的"我"映射回本地用户 Id，历史上误挂在云端 Id 名下的数据每次同步幂等归还；同步代码在 `SyncTimestampScope` 内保存，不自动盖 `UpdatedAt`（标记同步状态不算编辑），采用云端版本时沿用云端的 `UpdatedAt`；服务端推送账本时所有者以 JWT 为准，不信任 DTO 的 `OwnerId`；云端令牌必须属于当前登录手机号（换号即作废，复用前核对令牌里的手机号）；服务端处理客户端推送时不得创建全局预设（`allowCreatePreset=false`）；成员关系的本地合并只走 `ISyncService.MergeLedgerMembersAsync`；读路径（页面加载顺带联网刷新账本成员缓存）不得被不可用的云端拖住——只给 `CloudAvailability` 的短期限（3 秒，真正取消请求），失败后 30 秒内直接读本地缓存，用户主动操作后的刷新不受此限；详见 docs/adr/0006-sync-identity-and-timestamps.md
10. **视觉体系（iOS 风格、仅浅色）**: 样式的唯一来源是 `src/FinanceTracker.Web/wwwroot/css/tokens.css` 的 `--ft-*` 令牌（C# 侧给图表用的同值常量只放在 `Design/DesignTokens.cs`），页面只用 `Components/Ui` 的复用组件 + 令牌——Razor 里不写静态内联样式、不写死颜色，除白名单（`MApp` / `MErrorHandler` / `PPageStack` / `PStackPageBar` / `MECharts`）外不用 MASA 控件（`VisualSystemGuardTests` 扫描源码拦截）。应用壳是四个一级 Tab（首页 / 账单 / 统计 / 我的）+ 独立的「记一笔」圆钮：**切换 Tab 只用 `NavigationManager.NavigateTo(根路径)`，不得对 Tab 根路径 `Push`**；四个 Tab 的刷新只靠下拉（`PullToRefresh`，只重读本页数据、不联网同步），导航栏 / Hero 里不放刷新按钮（用户拍板，新增页面也不要补）；二级页面用 `SecondaryPage`，弹层（Sheet / Alert / ActionSheet）走 `OverlayBase`；每屏同时叠加的玻璃（`backdrop-filter`）≤ 3 层，玻璃只取 `--ft-glass-*` 令牌（「降低透明度」「低端设备」「不支持 backdrop-filter」三条降级路径共用一组不透明令牌）；金额统一经 `AmountDisplay`；安全区只读 `--ft-safe-*` 变量、不直接用 `env()`；Tab 栏（及与它同口径的 Toast / 多选操作栏 / 一级页面底部留白）的距底只经 `--ft-tabbar-offset`——Tab 栏下沿离屏幕底 21，iPhone 上压住 Home 指示条所在的安全区，只有键盘 / Android 三键导航栏这类实心占用才再避开（宿主经 `SafeAreaInsets.BottomIndicator` 上报「只是指示条」的部分，页面内容与 Sheet 仍避开整个 `--ft-safe-bottom`），不得再自己拼「`--ft-tabbar-bottom` + `--ft-safe-bottom`」（见 ADR 0007 §29）；原生能力（安全区 / 状态栏 / 触感 / 渲染降级）经 Web 层接口抽象，由 MAUI 宿主实现、Web.Server 提供空实现。UI 重构只改展示——不改实体 / 迁移 / REST / 同步 / 权限 / 时区 / 预算规则 / 导入解析；详见 docs/adr/0007-ios-visual-system.md

## 开发注意事项

- MAUI Blazor 项目需要 Android/iOS 目标框架，Windows 上主要用 Android 模拟器调试
- FinanceTracker.Web 是共享组件库，同时被 MAUI App 和 Blazor Server 引用
- 两个宿主（MAUI `src/FinanceTracker.App/wwwroot/index.html`、Web.Server `_Layout.cshtml`）各有一份宿主页，E2E 只访问后者，MAUI 给 `NavigationManager` 的根地址也随平台而异（Android `https://0.0.0.0/`、Windows `https://0.0.0.1/`、**iOS `app://0.0.0.0/`**）：解析 `NavigationManager.Uri` 不得假设 http(s) 协议（统一走 `TabNavigation.NormalizePath`）；宿主页里的样式 / 标记不要各写一份，样式放共用的 `app.css`（`#blazor-error-ui` 的隐藏规则就是漏在 MAUI 宿主页，iOS 真机上才暴露，见 ADR 0007 §28）
- UI 的自动化验收只覆盖网页层：`tests/e2e` 的 Playwright 脚本用手机视口（390×844、触摸）访问 Web.Server；改 Razor / C# 后要重启 Web.Server 才生效（`wwwroot` 下的 CSS / JS 是实时的），跑 E2E 期间不要重启服务或同时跑别的会改本地库的脚本。玻璃的真实渲染、手势手感、状态栏图标、真机字体放大等只能在真机 / 模拟器人工验收
- 预设分类和支付渠道在 IMPLEMENTATION-PLAN.md 中有完整列表
