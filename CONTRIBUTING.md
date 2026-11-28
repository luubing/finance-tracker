# 参与贡献

谢谢你愿意改进 FinanceTracker（记账本）。这是一个个人维护的自托管项目，没有商业支持，Issue 和 PR 会尽力回复但不保证时效。

## 先看这几件事

- **安全问题**不要开公开 Issue，按 [SECURITY.md](SECURITY.md) 私下报告。
- **提 Issue** 时请附上复现步骤、平台（Android / iOS / Web）和版本。贴日志前先把手机号、金额、服务器地址等个人信息脱敏。
- **较大的改动**（新功能、改数据模型、改同步 / 权限 / 统计口径）建议先开 Issue 讨论，避免白做。
- 你提交的代码按本仓库的 [MIT 许可证](LICENSE) 授权。

## 开发环境

环境要求与启动方式见 [README 的「快速开始」](README.md#快速开始)：.NET 10 SDK、Docker（PostgreSQL）、MAUI 工作负载（只在调试移动端时需要）。改数据库迁移还需要 `dotnet tool install -g dotnet-ef`。

仓库根目录的 `global.json` 把 SDK 锁在 10.0.x（装了更高的 10.0.x 补丁版本会自动用上），`.editorconfig` 统一了编码、缩进和文件结尾——主流编辑器会自动读取，不用额外配置。

目录结构与依赖方向见 README 的「项目结构」：`App → Web → Core → Shared`，`Api → Core → Shared`，`Infrastructure → Core`。`FinanceTracker.Web` 是同时被 MAUI App 和 Blazor Server 引用的共享组件库，改它要考虑两个宿主。

## 提交前自查

```bash
# 单元测试（真实 SQLite + 迁移，不需要数据库服务）
dotnet test tests/unit/FinanceTracker.Tests/FinanceTracker.Tests.csproj
```

- **改了界面**：再跑一遍 E2E（Playwright、手机视口），做法见 [tests/e2e/README.md](tests/e2e/README.md)。启动脚本目前只有 PowerShell 版。E2E 只覆盖网页层，玻璃效果、手势手感、状态栏、安全区、真机字体放大等只能在真机或模拟器上看——PR 里请写明你验证了什么、没验证什么。
- **改了实体 / `ApplicationDbContext`**：两套迁移都要生成，命令见 README 的「数据库迁移」；`ModelSnapshotTests` 会检查快照与模型是否一致。
- **修 Bug**：先写一个能复现它的失败用例，再修。`StatisticsSoftDeleteTests` 这类回归用例就是这么来的。
- 测试尽量用真实 SQLite 加迁移（见 `TestSupport/SqliteTestDatabase.cs`），不要用 mock 替掉数据库——mock 测不出迁移、查询过滤器和软删除的问题。
- 每个 PR 和 master 上的每次推送都会跑 CI（`.github/workflows/ci.yml`，Ubuntu），分两个任务：`build-and-test`（仓库卫生检查、构建 Api 与 Web.Server、全量单元测试）和 `secret-scan`（用 gitleaks 扫描这次新增的提交里有没有密钥）。本地等价的做法就是上面的 `dotnet test`；MAUI App 需要 Android / iOS 工作负载，CI 里不构建。
- `secret-scan` 被判成误报时，把输出里的 `Fingerprint` 加进仓库根目录的 `.gitleaksignore`（一行一个）并在 PR 里说明为什么是误报；真是密钥就当作已经泄露处理（见下面「不要提交的东西」）。想在本地提前跑：装好 [gitleaks](https://github.com/gitleaks/gitleaks) 后在仓库根目录执行 `gitleaks git --redact --log-opts="origin/master..HEAD" .`。

## 提交信息

格式 `类型(范围): 中文描述`，范围可省略：

```
feat: 分类「不计入统计」开关
fix(ui): Tab 栏距底对齐设计稿，iPhone 上压住 Home 指示条
ci(ios): 锁定 .NET SDK 为 10.0.x
docs: 补充 iOS 风格重构的设计稿
```

常用类型：`feat`、`fix`、`docs`、`ci`、`chore`、`refactor`、`test`。一次提交只做一件事；描述写「做了什么、为什么」，不要只写「修改」「更新」。

代码注释、XML 文档、提交信息与文档以中文为主；Issue / PR 中文、英文都可以。

## 领域术语

代码、界面文案和文档里必须使用统一的中文术语，不要用括号后面「避免使用」的词：

| 术语 | 英文 | 避免使用 |
|------|------|----------|
| 账单 | Bill | 交易、记录、流水 |
| 分类 | Category | 标签、类型 |
| 支付渠道 | PaymentChannel | 支付方式、付款方式 |
| 账本 | Ledger | 账簿、笔记本 |
| 数据来源 | BillSource | 录入方式、来源 |
| 同步状态 | SyncStatus | 状态 |
| 软删除 | Soft Delete | 删除、物理删除 |
| 不计入统计 | ExcludeFromStatistics | 忽略、屏蔽、隐藏（统计页底部「已忽略 N 笔…」的提示句除外） |

完整定义在 [CONTEXT.md](CONTEXT.md)。所有实体用 `IsDeleted` 做软删除、用 `Guid` 做主键。

## 必须遵守的约定

完整清单是 [CLAUDE.md 的「关键架构决策」](CLAUDE.md)（虽然文件名是给 AI 助手用的，内容是写给所有开发者的），每条的来龙去脉在 [docs/adr/](docs/adr/)。下面这些最容易踩，其中不少有单元测试把关：

- **时间**：账单时间在数据库和同步 DTO 里一律是 UTC；用户看到、输入的时间以及「某天 / 某月 / 某年」的边界按应用时区。两者只经 `FinanceTracker.Core.Time.AppTimeZone` 换算，**不要**直接调用 `ToUniversalTime()` / `ToLocalTime()`（EF 从 SQLite 读出的 `DateTime` 其 `Kind` 是 `Unspecified`，极易二次换算）。统计区间左闭右开。见 [ADR 0005](docs/adr/0005-time-zone-handling.md)。
- **401 与 403 不能混**：401 表示认证失败，客户端会据此登出；403 表示已认证但无权操作。服务层的权限错误抛 `ForbiddenAccessException`，**不要**抛 `UnauthorizedAccessException`；控制器里不要用 `Forbid(string)`（参数是认证方案名而不是消息，会 500），统一用 `StatusCode(StatusCodes.Status403Forbidden, new { message })`。
- **统计与预算**：「不计入统计」的口径只写在 `Core/Services/StatisticsScope`，新增统计 / 预算类查询必须经它；不要把「被排除的分类」当子查询拼进账单查询（`IgnoreQueryFilters()` 会连账单的软删除过滤器一起关掉）。见 [ADR 0008](docs/adr/0008-category-exclude-from-statistics.md)。
- **同步**：同步代码在 `SyncTimestampScope` 内保存，标记同步状态不算编辑、不盖 `UpdatedAt`；服务端处理客户端推送时不得创建全局预设。见 [ADR 0006](docs/adr/0006-sync-identity-and-timestamps.md)。
- **界面**（iOS 风格、仅浅色）：样式的唯一来源是 `wwwroot/css/tokens.css` 里的 `--ft-*` 令牌；页面只用 `Components/Ui` 的复用组件加令牌，Razor 里**不写静态内联样式、不写死颜色**，除白名单外**不用 MASA 控件**，金额统一经 `AmountDisplay`。`VisualSystemGuardTests` 会扫描源码拦截。见 [ADR 0007](docs/adr/0007-ios-visual-system.md)。
- **只改展示的重构不动**实体、迁移、REST、同步、权限、时区、预算规则和导入解析。
- **云端地址不进仓库**：App 的云端 API 地址在构建时注入，不要在代码、配置或文档里写死任何线上域名（见 README「构建与发布」、[ADR 0009](docs/adr/0009-cloud-access-hardening.md)）。
- **日志里不写完整手机号**：手机号相当于登录凭据。日志没有统一的脱敏层，要带手机号就自己用 `PhoneNumberMasker.Mask`（保留前 3 位和后 4 位），参见 `AuthService`、`PhoneAllowListMiddleware` 的写法。

## 架构决策怎么记

改动改变了一条规则、或者做了一个以后有人会问「为什么这样」的取舍，就在 `docs/adr/` 下加一篇 `NNNN-短标题.md`（编号顺延），写清楚**背景、备选方案及否决理由、决策、后果**，涉及回归用例的列一张测试对照表；规则足够重要时，再在 CLAUDE.md 的「关键架构决策」里加一条指向它。可以参考 [ADR 0008](docs/adr/0008-category-exclude-from-statistics.md) 的写法。

## 不要提交的东西

- 签名与凭据：keystore、`.p12`、`.mobileprovision`、私钥、`build/config.env`、任何 token / 密码；
- 真实数据：你自己的账单导出（CSV）、本地数据库文件、日志、含手机号的截图；
- 线上信息：服务器地址、IP、部署脚本。

`.gitignore` 里有一层通用兜底（任意目录下的 `*.p12`、`*.pem`、`*.key`、`*.keystore`、`*.mobileprovision`，`.env*`、`appsettings.Production.json`、`*.csv`、本地数据库、`*.binlog` 等；`server/FinanceTracker.Web.Server/` 里那份没有任何口令的 `appsettings.Production.json` 是放行的例外），再加上 `build/` 下按路径写的规则，但**它不可能覆盖所有东西**（换个文件名就绕过了）。提交前请看一眼 `git status` 和 `git diff --staged`，别用 `git add -A` 一把梭。用 `git add -f` 强行添加被忽略的文件，会让 CI 的「仓库卫生」检查失败。

测试需要账单 CSV 样本时，只放**自己编造的假数据**，放在 `tests/**/fixtures/` 目录下（只有这里的 `.csv` 不被忽略）；不要拿真实导出脱敏后凑数。

万一提交了，不要只靠后续提交把它删掉——它还留在 git 历史里。请立刻轮换对应的密钥，并联系维护者处理历史。

## 提 PR

1. Fork 后从主分支拉一个新分支，改动尽量小而聚焦。
2. 保证 CI（`build-and-test` 与 `secret-scan`）通过；改了界面就跑 E2E。
3. PR 描述写清楚：改了什么、为什么、怎么验证的、哪些没法验证。
4. 同步更新受影响的文档（README、ADR、CLAUDE.md）。

`.scratch/` 是作者的工作草稿目录（工单、设计过程、临时脚本），一般的贡献不需要改它。

请友善、就事论事地讨论。
