# 第三方组件声明

本项目自身的代码以 [MIT 许可证](LICENSE) 发布。构建与运行时还会用到下列第三方组件，它们各自遵循自己的许可证；完整许可证文本以各组件随附的 LICENSE 文件、对应 NuGet / npm 页面为准（SPDX 标识的全文见 <https://spdx.org/licenses/>）。

> 信息核对于 2026-11-27，来源是各 NuGet 包的 `.nuspec`、npm 包的 `package.json` / `LICENSE`、字体文件自带的名称表。本文件只是整理清单，**不构成法律意见**。
>
> 如果你把打包好的 App（APK / IPA）再分发给别人，MIT、Apache-2.0 等许可证要求随附版权与许可声明——可以把本文件放进安装包或应用的「关于」页。

## 1. 运行时从 CDN 加载的前端资源

这两项**不在仓库里**，App / Web 宿主页（`src/FinanceTracker.App/wwwroot/index.html`、`server/FinanceTracker.Web.Server/Pages/_Layout.cshtml`）在运行时从 jsDelivr 加载。

| 组件 | 版本 | 许可证 | 说明 |
|------|------|--------|------|
| [Apache ECharts](https://echarts.apache.org) | 5.4.3 | Apache-2.0 | 统计图表。NOTICE：*Apache ECharts — Copyright 2017-2023 The Apache Software Foundation. This product includes software developed at The Apache Software Foundation (https://www.apache.org/).* |
| [@mdi/font](https://materialdesignicons.com)（Material Design Icons 字体） | 7.2.96 | Pictogrammers Free License：图标与字体 Apache-2.0，代码 MIT | 界面图标。作者 Austin Andrews / Pictogrammers |

## 2. 仓库内置的资源

| 资源 | 位置 | 许可证 | 说明 |
|------|------|--------|------|
| Open Sans Regular / SemiBold | `src/FinanceTracker.App/Resources/Fonts/` | Apache-2.0 | 字体文件名称表标注：*Digitized data copyright © 2010-2011, Google Corporation*，制作方 Ascender Corporation；随 .NET MAUI 项目模板而来 |
| `wallet-outline` 图形 | `src/FinanceTracker.App/Resources/AppIcon/appiconfg.svg`、`Resources/Splash/splash.svg`，以及加载页的 Logo | Apache-2.0（Pictogrammers Free License 所述） | 取自 Material Design Icons；应用图标的底色渐变是本项目自己的 |
| .NET MAUI 模板文件 | `Resources/Styles/Colors.xaml`、`Styles.xaml`、`Resources/Raw/AboutAssets.txt` | MIT（Microsoft / .NET Foundation） | 由项目模板生成，之后按需修改 |
| Inter、Noto Sans SC | `docs/design/` 里的设计稿截图 | SIL OFL 1.1 | 渲染设计稿时用它们近似 SF Pro / 苹方；字体本身不在仓库里，也不随应用分发 |

## 3. NuGet 直接依赖

| 包 | 版本 | 许可证 | 用于 |
|----|------|--------|------|
| Microsoft.Maui.Controls | 10.0.30 | MIT | MAUI 应用壳 |
| Microsoft.AspNetCore.Components.WebView.Maui | 10.0.30 | MIT | MAUI Blazor WebView |
| Microsoft.AspNetCore.Components.Web | 10.0.11 | MIT | Blazor 组件库 |
| Microsoft.Extensions.Logging.Debug | 10.0.0 | MIT | 调试日志 |
| Microsoft.EntityFrameworkCore / .Sqlite / .Design | 10.0.11 | MIT | ORM、本地 SQLite、迁移工具 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | PostgreSQL License | 服务端 PostgreSQL（传递依赖 Npgsql 10.0.3，同一许可证） |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.11 | MIT | API 的 JWT 认证 |
| Microsoft.AspNetCore.OpenApi | 10.0.11 | MIT | API 的 OpenAPI 文档 |
| Swashbuckle.AspNetCore | 10.2.3 | MIT | Swagger UI |
| [Masa.Blazor](https://github.com/masastack/MASA.Blazor) / Masa.Blazor.MobileComponents | 1.11.9 | MIT（© 2021 MASA-Stack；部分代码受 ant-design-blazor 启发，© ant-design-blazor contributors，MIT） | 基础组件与页面栈 |
| Serilog | 4.2.0 | Apache-2.0 | 日志 |
| Serilog.Extensions.Logging | 8.0.0 | Apache-2.0 | 日志接入 |
| Serilog.Sinks.File | 6.0.0 | Apache-2.0 | 日志写文件 |
| Serilog.Formatting.Compact | 3.0.0 | Apache-2.0 | 日志格式 |

仅用于测试、**不随应用分发**：Microsoft.NET.Test.Sdk 17.11.1（MIT）、xunit 2.9.2（Apache-2.0）、xunit.runner.visualstudio 2.8.2（Apache-2.0）。

## 4. 值得单列的传递依赖

| 包 | 版本 | 许可证 | 从哪来 / 说明 |
|----|------|--------|----------------|
| SQLitePCLRaw.\*（bundle_e_sqlite3 等） | 2.1.12 | Apache-2.0（© 2014-2024 SourceGear, LLC） | EF Core SQLite；内含的 SQLite 本身是公有领域（public domain） |
| FluentValidation、FluentValidation.DependencyInjectionExtensions | 11.4.0 | Apache-2.0 | Masa.Blazor |
| OneOf、OneOf.SourceGenerator | 3.0.223 | MIT | Masa.Blazor |
| DeepCloner.Core | 0.1.0 | MIT | Masa.Blazor |
| GoogleGson | 2.13.1.1 | MIT AND Apache-2.0 | Android 绑定库（微软发布） |
| Humanizer.Core | 2.14.1 | MIT | EF Core 设计时工具（迁移） |
| Microsoft.OpenApi | 2.7.5 | MIT | Swashbuckle / OpenAPI |
| Swashbuckle.AspNetCore.Swagger / SwaggerGen / SwaggerUI | 10.2.3 | MIT | Swashbuckle.AspNetCore |
| 其余 `Microsoft.*`、`System.*`、`runtime.*` 包 | — | MIT | .NET / ASP.NET Core / MAUI / EF Core 的组成部分 |

### 许可证信息缺失的传递依赖

下面两个包都是 Masa.Blazor 1.11.9 的传递依赖，**包元数据里没有许可证字段**，上游也没有给出许可证（截至 2026-11-27）：

| 包 | 版本 | 作者 | 情况 |
|----|------|------|------|
| BemIt | 2.2.0 | capdiem | 包元数据无许可证；上游仓库 [capdiem/BemIt](https://github.com/capdiem/BemIt) 没有 LICENSE 文件 |
| Util.Reflection | 1.0.3 | wuweilai | 包元数据既无许可证也无仓库地址 |

它们不是本项目直接引用的。如果你的使用场景对此敏感（例如商业再分发），请自行向上游确认授权，或评估替换 Masa.Blazor。

## 5. 如何更新这份清单

```bash
# 列出某个项目的全部包（含传递依赖）
dotnet list src/FinanceTracker.App/FinanceTracker.App.csproj package --include-transitive
```

许可证在本地 NuGet 缓存的 `~/.nuget/packages/<包名>/<版本>/<包名>.nuspec` 里（`<license>` 元素）。新增或升级依赖时请同步更新这里。
