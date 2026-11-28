# 安全策略

## 报告漏洞

- 请使用 GitHub 的**私密漏洞报告**：仓库页面 → `Security` → `Report a vulnerability`。**不要**在公开的 Issue / PR / 讨论区里贴可被利用的细节。
- 报告里尽量包含：影响范围、复现步骤、涉及的提交或版本、你认为的严重程度。
- 这是个人维护的项目，不承诺固定的响应时限，但会尽力尽快回复，修复后按你的意愿致谢。
- 只维护 `master` 分支的最新代码，不为旧版本单独打补丁。
- 如果页面上看不到「Report a vulnerability」入口，请开一个**不含任何细节**的 Issue，说明想私下报告安全问题，维护者会联系你。

## 部署前要知道的安全边界

下面是已知且有意保留的现状（取舍见各 ADR），不算漏洞，但部署云端服务前请了解：

| 现状 | 影响与建议 |
|------|------------|
| **登录只凭手机号**，没有验证码或口令（[ADR 0003](docs/adr/0003-user-authentication.md)） | 任何人知道某个手机号，就能注册 / 登录对应账号并看到它的数据。云端服务请只给自己和家人用，并开启下面的「手机号白名单」 |
| JWT 有效期 7 天（`Jwt:ExpiryInMinutes` = 10080），服务端**没有吊销机制** | 登出只清除客户端本地的登录状态；令牌泄露后，到期前一直有效（开启白名单时，名单外手机号的令牌会被拒绝）。需要更短的有效期可以调小 `Jwt__ExpiryInMinutes` |
| 客户端把登录令牌、手机号、用户 Id 存在 WebView 的 `localStorage`，本地 SQLite 数据库**未加密** | 设备被 root / 越狱、或备份被提取时，这些数据可被读取。Android 的 `allowBackup` 目前为 `true`，允许系统备份应用数据 |
| Android 允许明文 HTTP（`usesCleartextTraffic="true"`） | 为了模拟器访问 `http://10.0.2.2` 的开发便利。正式部署请使用 HTTPS 地址；后续会收紧 |
| API 的 CORS 目前允许任意来源（`AllowAnyOrigin`） | 认证用的是 Bearer 令牌而不是 Cookie，风险有限；公开部署建议在反向代理层把允许的来源收紧到自己的域名；后续会收紧 |
| 数据库默认口令是 `postgres`（`appsettings.json`、`docker-compose.yml`），仍在使用时**只在启动日志里告警** | 生产环境务必改口令；`docker-compose.yml` 只用于本机开发，它只把 PostgreSQL 映射到本机回环地址（`127.0.0.1:5432`），但口令是公开的默认值，不要原样用到服务器上 |
| `FinanceTracker.Web.Server`（Blazor Server 宿主）自己的登录用的是它本机的 SQLite 用户库 | 手机号白名单只作用于 `FinanceTracker.Api`，不约束 Web.Server 自己的登录；若要把 Web.Server 部署到公网，请在反向代理层另做访问控制 |

已有的防护：非 Development 环境下，`Jwt:SecretKey` 仍是仓库里的占位值时 API **拒绝启动**；Swagger 默认只在 Development 环境开启；登录 / 注册相关的日志（服务端与客户端）里，手机号做了脱敏（保留前 3 位和后 4 位）。日志没有统一的脱敏层：新增的日志要带手机号时，请自己用 `PhoneNumberMasker.Mask`。

## 手机号白名单（可选，默认关闭）

针对第一行的取舍：给云端 API 配置一份手机号名单，**只有名单内的手机号能登录 / 注册**，适合自用和家用部署。

**配置**（改完需要重启 API，名单在启动时读取）：

```bash
# 环境变量（推荐）：英文逗号分隔，每个号码 11 位
export Auth__AllowedPhoneNumbers="13800138000,13900139000"
```

或写进 `appsettings.Production.json`：

```json
{
  "Auth": {
    "AllowedPhoneNumbers": "13800138000,13900139000"
  }
}
```

- 也支持 JSON 数组（`["13800138000","13900139000"]`）和环境变量的数组写法（`Auth__AllowedPhoneNumbers__0=...`）。
- 分隔符认英文逗号、分号和换行（中文的 `，；、` 也认）；号码里的空格、连字符、括号和开头的 `+86` 会被忽略。
- 名单为空或没配置 = **关闭**，与不加这个功能时完全一致。

**生效范围**：

- 登录 / 注册：名单外的手机号返回 `403` 和提示「该手机号未获当前云端服务的使用许可，请联系服务器管理员」，也**不会**在云端创建用户。
- 已经签发的令牌：名单外手机号的令牌，下一次请求时同样返回 `403`——改名单后马上生效，不用等令牌过期。
- 共享账本按手机号邀请成员，被邀请人须已在云端注册过：名单启用后，名单外的号码既不能注册也不能登录，所以**要被邀请的人也得在名单里**。

**配置写错时的行为**：只要配了条目，名单就视为已启用。条目不是 11 位手机号（最常见的原因是用空格分隔了）永远匹配不上任何人，启动日志会给出告警——宁可把所有人挡在外面，也不会悄悄退回「不设防」。

**它的局限**：

- 它只是一道粗粒度的门，不是身份验证：名单内的手机号仍然只凭号码登录。
- 被拒绝时的 `403` 会让别人能通过状态码探测「某个手机号在不在名单里」。
- 不覆盖 `FinanceTracker.Web.Server` 自己的登录（见上表）。

真正的验证码 / 口令登录需要短信服务或口令体系，涉及成本和使用体验，是单独的产品决策，目前没有做。方案的取舍见 [ADR 0009](docs/adr/0009-cloud-access-hardening.md)。

## 给部署者的清单

1. 用环境变量或 `appsettings.Production.json` 提供至少 32 位的随机 `Jwt__SecretKey`。
2. 修改数据库口令，数据库端口不要对公网开放。
3. 用 Nginx 等反向代理提供 HTTPS，客户端的云端地址用 `https://`。
4. 仅供自己 / 家人使用时，配置上面的手机号白名单。
5. 保持 Swagger 关闭（生产环境默认关闭）。
6. 定期备份数据库，并妥善保管备份：登录相关的日志里手机号已脱敏，但数据库里存的是明文手机号。

## 仓库自身的约定

- **不提交**：`build/config.env`、签名证书 / 私钥 / 描述文件（`build/certificates/` 下除 `README.md`、`csr.conf`、`convert.bat` 之外的文件）、`appsettings.Production.json`（唯一的例外是 `server/FinanceTracker.Web.Server/` 里那份：只有日志级别和本机 API 地址，没有任何口令，保持受跟踪；`server/FinanceTracker.Api/` 里的那份放真实连接串和 JWT 密钥，一律不入库）。
- 云端地址、蒲公英的 API Key 和（可选的）安装密码、Apple 证书等只放 GitHub Secrets / Variables。本仓库是公开的，所以 iOS 打包的工作流不上传 IPA 产物、不在日志里打印下载地址和安装密码，把签名任务固定打印的证书主体 / Team ID / 证书指纹登记成遮罩，也只申请只读权限（见 [`.github/SETUP-IOS-BUILD.md`](.github/SETUP-IOS-BUILD.md)）。
- 蒲公英安装密码是**可选**的：不设置时工作流以公开安装上传，下载页地址就相当于访问凭证（安装包内嵌的描述文件含设备 UDID 和 Team ID），不要把它写进 Issue / PR / 日志；需要限制时再设置 `PGYER_INSTALL_PASSWORD`。
- 打包时由 `-p:CloudApiBaseUrl` 注入云端地址，源码里不内置线上域名，见 README 的「构建与发布」。
- 防止误提交密钥有三道防线：`.gitignore` 的通用兜底（按文件名，换个名字就绕过了）；CI 的 `secret-scan` 任务用 gitleaks 扫描每次推送 / PR 新增的提交内容（日志始终脱敏，命中时只显示位置和规则名）；GitHub 自带的密钥扫描与推送保护。被拦下时请换掉密钥，不要想办法绕过检查。万一已经推送了，见 [CONTRIBUTING.md](CONTRIBUTING.md) 的「不要提交的东西」：密钥要立刻轮换，光删文件没用。
- 仓库还开着 Dependabot 告警与安全更新、CodeQL 代码扫描（C# 和 GitHub Actions 工作流）；GitHub Actions 只允许官方 Action，并且必须按提交 SHA 引用；master 分支禁止强推和删除。
