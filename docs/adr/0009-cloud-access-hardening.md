# 云端访问加固：地址构建时注入、可选的手机号白名单

## 背景

仓库要公开。公开前发现两个与「云端」有关的问题：

1. App 的云端 API 地址以常量形式写死在 `MauiProgram`——源码一公开，线上服务的地址也跟着公开了；而且调试构建同样连的是线上。
2. 云端登录只凭手机号、没有验证码（[ADR 0003](0003-user-authentication.md)）——线上地址一公开，任何人都能调用登录接口，注册并登录任意手机号的账号。

真正的登录加固（短信验证码 / 口令）要接短信服务或设计一套口令体系，涉及成本和使用体验，是单独的产品决策，这一轮不做。
这一轮只做两件事：**仓库里不再内置线上地址**，以及给自用 / 家用部署提供一个**可选的「门」**。

## 决策

### 1. 云端地址：构建时注入，仓库不内置

| 方案 | 结论 |
|------|------|
| 继续写死在源码里 | 否决：就是要解决的问题 |
| 运行时配置文件 / 让用户在 App 里填服务器地址 | 暂不做：要新增界面和存储，首次登录体验变复杂；构建时注入已经满足「仓库里没有线上域名」 |
| 环境变量 / appsettings | 否决：MAUI 客户端没有可配置的「运行环境」 |
| **构建时注入（`-p:CloudApiBaseUrl=...`）** | **采用** |

- **注入**：MSBuild 属性 `CloudApiBaseUrl` 写进 App 程序集的 `AssemblyMetadata("CloudApiBaseUrl")`（`FinanceTracker.App.csproj`）。运行时由
  `FinanceTracker.Web.Services.CloudApiBaseUrlResolver.FromAssembly` 读出并规整，`MauiProgram` 只调用它，不再有任何地址字面量。
- **规整规则**（`CloudApiBaseUrlResolver.Normalize`）：去首尾空白；只接受带主机名的 `http` / `https` 绝对地址；**拒绝**用户名密码、查询参数、片段；
  去掉结尾的 `/api`；结尾补 `/`。不合法就抛 `InvalidOperationException`，信息里带 `-p:CloudApiBaseUrl=` 的用法提示，且不回显凭据。
- **调试构建**不指定时用本机开发地址——README「快速开始」里 `dotnet run` 起的 API（端口 5065）：Android 模拟器 `http://10.0.2.2:5065/`，其余平台 `http://localhost:5065/`。
- **发布构建**（`dotnet publish`）**必须显式指定**，否则构建直接报错（避免打出一个连不上服务器的安装包）。csproj 的 `ValidateCloudApiBaseUrl` 目标还会在构建期校验格式：
  值里含 `@` 时报错但**不回显值**（避免凭据进构建日志）；缺协议、缺主机名、带查询或片段、端口不是 0–65535 的数字，同样只报规则、不回显值。
  构建期和 iOS 工作流预检各有一份正则，只用来**尽早失败**，以运行时 `Normalize` 为准；`CloudApiBaseUrlResolverTests` 用同一批样例守着三处不漂移——
  正则放行的地址，运行时必须也认（否则会装出一个一启动就崩的包），常见合法写法也不能被正则误拒。
- **入口**：`build/build.sh` / `build.ps1` 读 `build/config.env` 里的 `CLOUD_API_BASE_URL`，没配直接报错；iOS 的 GitHub Actions 读 Secret 或 Variable `CLOUD_API_BASE_URL`（同名时 Secret 优先），并在工作流第一步预检格式。
- **注意**：地址仍然会写进安装包——装了 App 的人能拿到它，这是预期的（客户端必须知道往哪连）。目的是把它从公开的**源码仓库**里拿掉，而不是对用户保密；所以地址里不能带账号密码。

### 2. 手机号白名单：API 层的可选开关，默认关闭

配置 `Auth:AllowedPhoneNumbers`（环境变量 `Auth__AllowedPhoneNumbers`，用法见 [SECURITY.md](../../SECURITY.md)）后，只有名单内的手机号能登录 / 注册云端。名单为空 = 关闭 = 行为与此前完全一致，现有部署不受影响。

- **放在哪一层**：`FinanceTracker.Api`（`PhoneAllowList`、`PhoneAllowListMiddleware`、`AuthController`），**不放进 Core 的 `AuthService`**。
  白名单是「这台云端服务愿意为谁服务」的部署策略，不是业务规则；`AuthService` 同时被客户端的本地登录使用，放进去会让本地登录也受名单约束，而本地登录必须永远可用。
- **两道关口**：
  1. 登录：`AuthController.Login` 在 `RegisterOrLoginAsync` **之前**检查，名单外的号码不会在云端建出用户。
  2. 已签发的令牌：中间件排在认证之后、授权之前，读令牌里的手机号（`ClaimTypes.MobilePhone`）。
     不拦已签发令牌的话，令牌 7 天内都有效，名单形同虚设；而且名单改错时，要等令牌过期才会集体暴露。
- **返回 403，不是 401**：与架构决策 7 一致。401 会让客户端当作「登录过期」而登出；白名单拒绝是「没有使用许可」，不能触发登出。
  响应体是 `{ message }`，文案由服务端给出，客户端原样展示。
- **失败关闭**：配置里出现了至少一个条目就算启用。条目不是 11 位手机号（最常见的原因是用空格分隔）永远匹配不上任何人，启动日志里告警——
  宁可把所有人挡在外面，也不能悄悄退回「不设防」。日志里只记名单数量和脱敏后的号码。
- **客户端**：`HttpService.EnsureRemoteTokenAsync` 收到云端登录的 403 时，抛 `ForbiddenAccessException`（带服务端的提示），不再和其他失败一样静默忽略。
  其余状态码（400 / 500 / 502 等）仍然静默——离线或云端不可用时 App 要照常用本地数据；调用方主动取消仍按取消处理。
  `LedgerMemberPresentation` 已经把 `ForbiddenAccessException` 显示成「无权限：…」且不登出。

## 后果与已接受的局限

- 名单只是一道粗粒度的门，**不是身份验证**：名单内的手机号仍然只凭号码登录。验证码 / 口令登录是后续的产品决策，到时另出 ADR。
- 被拒绝时的 `403` 能让人探测「某个手机号在不在名单里」。自用 / 家用场景下接受。
- 共享账本按手机号邀请成员，**被邀请人也必须在名单里**才能用云端，配置名单时要把家人的号码一起写进去。
- 白名单只作用于 `FinanceTracker.Api`。`FinanceTracker.Web.Server` 自己的登录用的是它本机的 SQLite 用户库，不受约束；公网部署 Web.Server 需要在反向代理层另做访问控制。
- 名单在启动时读取，改名单要重启 API。
- **旧版客户端**不认识 403：登录被拒后它会静默带着空令牌发业务请求，收到 401 而被登出。只影响名单外的用户（他们本来就不该用这台云端），但开启名单前要先让常用客户端升级。
- 构建期的注入与校验只覆盖 App；Web.Server 的云端地址本来就是 `ApiSettings:BaseUrl` 配置，没有变化。

## 不在本决策范围

真正的登录加固（验证码 / 口令）、令牌的服务端吊销、Android 的 `usesCleartextTraffic` / `allowBackup`、CORS 收紧——现状和建议见 [SECURITY.md](../../SECURITY.md) 的「已知安全边界」，之后分别决策。

## 测试

| 测试 | 守什么 |
|------|--------|
| `CloudApiBaseUrlResolverTests` | 规整规则与非法值（含端口越界）；`MauiProgram` 里没有地址字面量；csproj 的注入与发布校验目标还在、错误信息不回显地址；**构建期正则 / 工作流 `url_pattern` / 运行时 `Normalize` 三处不漂移**（正则放行的地址运行时必须也认，常见合法写法不被误拒） |
| `PhoneAllowListTests` | 默认关闭、写法与分隔符、`+86` / 空格 / 括号、写错时失败关闭、`appsettings.json` 默认关闭 |
| `PhoneAllowListMiddlewareTests` | 名单内放行、名单外 403、不回显手机号、真实令牌的声明名、`Program.cs` 里的注册与顺序 |
| `PhoneAllowListPipelineTests` | 真 Kestrel + 真 JwtBearer 的整条管线：无令牌 / 签名不对 → 401，令牌有效但手机号不在名单内 → 403（不带认证质询头），名单内放行，名单关闭不受影响——架构决策 7 的 401 / 403 区分在整条链路上的体现 |
| `AuthControllerAllowListTests` | 名单外不建用户、不发令牌、返回 403；无效手机号仍先返回 400 |
| `CloudLoginForbiddenTests` | 客户端登录遇 403 抛 `ForbiddenAccessException`、本地登录状态不丢；其他状态码仍静默；取消不被当成权限拒绝 |
