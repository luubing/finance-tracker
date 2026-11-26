# 01 — E2E 手机视口基座

**What to build:** 为整个 UI 重构准备统一的验收基座：所有 E2E 都在手机视口（约 390×844、触摸、高 DPR）下运行，并共享同一套登录、造数据、手势、错误采集与等待工具。此后每个 ticket 都靠它验收。本 ticket 完成后，既有三个 E2E 脚本迁到基座，并在手机视口下全部通过。这是前置重构（先让后续改动容易验证），不改任何业务代码和界面。

**Blocked by:** None — can start immediately.

**Status:** completed

- [x] 提供共享 E2E 基座：手机视口（约 390×844）+ 触摸 + 高 DPR（≥2）的浏览器上下文；可用参数开启「减弱动态效果」「减弱透明度」等媒体特性模拟（后续 ticket 使用）
- [x] 提供稳定签名的 helper：等待 Blazor 就绪、登录（每次用唯一手机号）、手势（点按、长按、左滑、下拉）、收集控制台与页面错误（支持白名单，例如 CDN 资源加载失败）、基于状态的等待（不使用固定 sleep）
- [x] 提供造数 helper：创建账本、创建账单优先经界面；旧界面的选择器（MASA 下拉）无法稳定自动化时，改为直接向本机 SQLite 写入（写入前先检查表结构，列变化时给出明确错误）。直接写库的数据必须符合现有约束：账单时间为 UTC（ADR 0005）、账本归属正确、同步状态为待同步、归属当前登录用户的本地 Id。该 helper 同时支持写入「历史上没有账本的账单」与「待确认账单」，供 09、16、19 使用
- [x] 手势 helper 用一个与应用无关的静态测试页自测，证明点按 / 长按 / 滑动 / 下拉确实触发触摸事件
- [x] 可选截图输出：默认关闭，开启后按用例命名保存到约定目录（该目录不入库、加入忽略规则），供人工对照设计稿
- [x] 既有三个 E2E 脚本迁移到基座，在手机视口下全部通过（因「账本必填」或手机视口造成的失败在本 ticket 内修复）
- [x] E2E 说明文档更新：如何启动 Web.Server 与运行基座、helper 清单、数据不隔离的约定（带时间戳的唯一名称、只断言自己创建的数据）；并预留「文档化可观察属性」小节，供后续 ticket 登记对外契约
- [x] 人工验收：以有头模式运行一次基座自测，确认视口尺寸、触摸模拟与截图输出生效
- [x] 回归：`dotnet build` 零错误、`dotnet test` 全部通过（本 ticket 不改业务代码，仅作确认）

## 实现说明

- 覆盖 spec 用户故事：106、108（基座部分）。
- **开工前提醒**：工作区里还有未提交的「账本必填」相关改动，涉及的页面正是后续 ticket 要改的页面。先把那批改动提交或合并，否则既会产生冲突，也会让既有 E2E 的失败原因混在一起。
- 「造账本 / 造账单」等 helper 对外签名保持稳定。后续哪个 ticket 改了对应界面，就由哪个 ticket 同步修改 helper 的内部实现，其余用例不受影响（05 把造账本改走新界面，15 把造账单改走新界面）。
- 直接写库只用于造数，是测试代码，不是业务路径；但写入的值要遵守同步与时间口径（UTC、待同步），否则会污染后续用例对同步状态的断言。
- E2E 只能覆盖网页层（Web.Server 与 MAUI 共用）；原生能力（状态栏、安全区注入、触感）靠各 ticket 的人工验收。
- Web.Server 共用本机 SQLite，测试数据不隔离：一律用带时间戳的唯一名称，只断言自己创建的数据。
- 现有说明文档记载 MASA 的下拉选择在无头浏览器里难以自动化。基座不要为它写补丁，后续 ticket 会用更易自动化的自定义选择器取代它。

### 完成记录（agent 实现）

- **交付物**（均在 `tests/e2e/`）：`e2e_base.py`（基座）、`e2e_base_test.py`（基座自测，56 项，不依赖 Web.Server）、`seed_visibility_test.py`（造数据端到端自证）、`run_all.py`（总入口）、`server.ps1`（启动 / 停止 / 重启 Web.Server）、README 重写；既有 `blazor_smoke_test.py` / `login_addbill_test.py` / `voice_addbill_test.py` 已迁到基座，手机视口下全部通过。`.gitignore` 新增 `tests/e2e/screenshots/`、`artifacts/`、`__pycache__/`。
- **造数据策略**：`create_ledger(page)` **经界面**（账本页「添加账本」对话框）；`create_bill(login_info, ledger=…)`、历史无账本账单、待确认账单**直接写本机库**（MASA 下拉无法稳定自动化）。写库值遵守 UTC / 待同步(0) / 大写 Guid / 当前登录用户的本地 Id；写入前 `verify_schema()` 核对表列，缺列抛 `SchemaMismatch`。
- **发现（影响 19）**：Web.Server 注册的是 `NoOpPendingBillService` / `NoOpSmsService` / `NoOpNotificationListenerPermissionService`，页面读不到库里的待确认账单，所以 `seed_pending_bill` 目前只在基座自测里验证写库格式。**19 号 ticket 需决定**：Development 下让 Web.Server 注册真实 `PendingBillService`（需 `AddDbContextFactory`）以自动化覆盖「修正后确认入账 / 忽略 / 全部忽略」，或降级为人工验收并说明原因。
- **发现（影响 02 / 03）**：PageStack 把每一页渲染成 `.m-dialog--fullscreen.m-page-stack-item`（带 `page-stack-id`），状态栏 / 安全区 / Tab 切换的实现要基于这套 DOM。
- 电路就绪判定：预渲染组件带 `<!--Blazor:{…}-->` 注释标记，电路接管后消失，`wait_circuit_ready` 以此为准（实测：DOMContentLoaded 后为 false，约 1.8s 后为 true）。
- 手机视口 = 390×844、DPR 3、`is_mobile` + `has_touch`；截图 PNG 实测 1170×2532。`prefers-reduced-motion` 用 Playwright 原生参数，`prefers-reduced-transparency` 经 CDP `Emulation.setEmulatedMedia`（Chromium 151 实测生效）。
- **人工验收项**：已以有头模式（`E2E_HEADED=1`）运行基座自测，并开启 `E2E_SCREENSHOTS=1` 生成截图，核对视口 390×844、截图尺寸 1170×2532、触摸手势事件均生效。
- 回归：`dotnet build FinanceTracker.sln` 0 警告 0 错误；`dotnet test` 416/416 通过；`run_all.py` 5/5 通过。
- 「开工前提醒」中的未提交「账本必填」改动：未提交（用户未要求提交），在现有工作区上直接开工；本 ticket 只新增测试基座，不触碰业务代码，所以没有冲突风险。
