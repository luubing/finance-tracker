# E2E 测试（Playwright）

针对 FinanceTracker.Web.Server（Blazor Server）的端到端冒烟与回归测试。

## 环境准备

```powershell
# 1. 安装依赖
pip install -r tests/e2e/requirements.txt
python -m playwright install chromium

# 2. 启动 PostgreSQL（API 需要时）
docker compose up -d

# 3. 启动 FinanceTracker.Web.Server（VS F5 调试，或命令行）
dotnet run --project server/FinanceTracker.Web.Server
```

脚本默认访问 `http://localhost:59947`（Web.Server 的 http 端口，见其
`Properties/launchSettings.json`）。端口不同时请修改脚本顶部的 `BASE_URL`。

## 脚本说明

| 脚本 | 作用 | 对应历史 Bug |
|---|---|---|
| `blazor_smoke_test.py` | 页面加载、SignalR 电路连接、无 404/控制台错误 | `blazor.server.js` 404 导致页面一直转圈（缺 `RequiresAspNetWebAssets`） |
| `login_addbill_test.py` | 登录 → localStorage 写入 userId → 进入记账页 | `IsPrerendering` 判断错误导致 userId 未持久化 → 记账时 `UserId=Guid.Empty` 外键失败 |
| `db_check.py` | 只读检查本地 SQLite 数据（Users/Categories/PaymentChannels/Bills 及外键有效性） | 诊断 `FOREIGN KEY constraint failed` |

```powershell
python tests/e2e/blazor_smoke_test.py
python tests/e2e/login_addbill_test.py
python tests/e2e/db_check.py
```

退出码：`0` 通过，`1` 失败，可用于 CI。

## 已知限制

- `login_addbill_test.py` 目前验证到"记账页正常加载"为止。MASA 的 `MSelect`
  下拉框在无头浏览器中难以自动化选择，完整的"保存账单"提交请手动验证，
  或用 `db_check.py` 核对落库结果。
- 脚本直接运行 exe（而非 `dotnet run`）时需设置
  `$env:ASPNETCORE_ENVIRONMENT='Development'`，否则静态资源不生效。

## 排错速查

| 现象 | 检查 |
|---|---|
| 页面一直转圈 | `blazor.server.js` 是否 200；csproj 是否有 `<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>` |
| 底部"发生了未处理的异常" | 浏览器 F12 控制台；`_Layout.cshtml` 的脚本引用是否为 `_content/Masa.Blazor/js/masa-blazor.js` |
| 保存账单报 FOREIGN KEY | `python tests/e2e/db_check.py` 查看 Users/Bills 数据 |
