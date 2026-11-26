# 24 — 收尾清理与全量回归

**What to build:** 所有页面迁移完成后的收尾：清理迁移期并存的旧样式与遗留，更新项目文档，并对照设计稿的逐页清单做一次全量回归（自动化 + 真机 / 模拟器人工走查），确认 iOS 液态玻璃视觉体系在全 App 内一致，且所有既有功能与数据行为没有变化。本 ticket 以清理与核对为主，不新增功能。

**Blocked by:** 06 — 账本成员页；09 — 账单筛选 Sheet 与批量归属账本；10 — 账单搜索；16 — 编辑账单与删除；17 — 语音记账覆盖层；18 — 预算页；19 — 待确认账单 + 短信识别；20 — 导入账单；21 — 自定义报表；22 — 登录页 + 错误日志；23 — 无障碍与降级加固

**Status:** completed（本机能做的自动化验收全部通过——`dotnet build FinanceTracker.sln` 0 警告 0 错误（含 Android 与 iOS 两个目标的 C# 层编译）、`dotnet test` 2183 个用例全部通过（T23 记录的是 2023；之后本 ticket 的源码守卫与图表颜色测试、并行冷启动工作的用例、第 2–4 轮评审与验收阶段的修正陆续新增）、`run_all.py` 29 个脚本共 3440 项检查全部通过（含并行冷启动工作的 `boot_test.py` 58 项；本 ticket 新增 `not_found_test.py` 22 项、基座自测 186 → 196 项；评审与验收阶段的修正使其余脚本的检查数各有增减，见下表）。**两项没有勾选、也不能算通过**：真机 / 模拟器上的逐页人工走查，以及 iOS 的 CI 构建（`build-ios.yml` 是手动触发、需要签名证书与蒲公英密钥的工作流，本机没有触发）——见下方「待人工验收」。截图对照做了：13 屏与 13 张补充状态全部截图并与设计稿并排，结构 / 层级 / 材质一致，发现的 6 处偏差已在验收阶段全部修正并重新截图复核（见「验收阶段修正」）；其余差异都是此前各 ticket 已记录的有意偏差。若干判断项见「判断记录」）

- [x] 清理迁移期并存的遗留：旧的紫色实心顶栏用法、旧底部导航的残留、固定高度的全屏容器、为旧页面临时加的安全区内边距、不再使用的旧组件与内联样式、已无任何入口的旧页面代码（如旧的记账 / 编辑页）；MASA 仍用于基础表单与图表容器的部分保留（旧记账页 `AddBill.razor`、旧月度统计页 `MonthlyStatistics.razor`、`ChartCard` / `ExpenseIncomeDonutChart` 已在 11 / 15 / 16 / 21 号随各自迁移删除；本 ticket 清掉最后一批：`app.css` 里迁移期的兼容层整体删除（只剩「禁止长按选择」与四个 `--ft-safe-*` 变量，唯一仍有用的「二级页面内容区上内边距」升为 `ui.css` 的正式规则）、`tabBar.js` 与 E2E 基座里「找不到 `data-scroll-root` 就退回旧 `.container`」的兜底、没有调用方的 `LedgerSelect.razor`、一条死 CSS（`.ft-home__section-empty`），`BillFilterChip` 的已设置形态合并到通用件 `ConditionChip`（渲染出来的 DOM 不变，`bills_page_test.py` 277/277 验证）；`Main.razor` 的兜底页（「页面未找到」「组件渲染发生异常」）是应用里最后一处 MASA 旧视觉，按设计系统重做。核查结果：没有无引用的 Razor 组件、没有无引用的 CSS 类、没有静态内联样式、没有写死的颜色（`DesignTokens.cs` 除外）、Razor 里只剩白名单内的 MASA 标记（`MApp` / `MErrorHandler` / `PPageStack` / `PStackPageBar` / `MECharts`）；`100vh` 的全屏容器都是有意的（登录页 / 兜底页 / 覆盖层）。守卫见 `VisualSystemGuardTests`，详情见 ADR 0007 §27）
- [x] 文档更新：CLAUDE.md 中与 UI 相关的说明（技术栈与开发注意事项）、README、E2E 说明文档（手机视口基座、文档化可观察属性、helper 清单）；ADR 0007 的状态更新为已落地（`CLAUDE.md` 技术栈一行改成「MASA Blazor（基础组件与页面栈）+ iOS 风格设计系统」、关键架构决策新增第 10 条「视觉体系」、常用命令补 E2E / 服务脚本；`README.md` 的技术栈 / 目录 / 测试说明；`tests/e2e/README.md` 登记 `not_found_test.py` 与新增的可观察属性 / 通用探针；`docs/adr/0007-ios-visual-system.md` 背景段改为「已全部完成」，新增 §27 与 24 号相关的「替代方案」「已知限制」）
- [x] 覆盖核对：对照 spec 的 108 条用户故事逐条确认已实现；发现缺口或偏差，记录为后续 issue，不在本 ticket 内扩大范围（做法：spec 的 108 个编号条目逐条对照 24 个 ticket 的「覆盖 spec 用户故事」——每一条都至少被一个 ticket 认领，没有遗漏、没有超出 1–108 的编号；再清点全部 ticket 里未勾选的验收项，除「人工验收」外只剩下面两处已声明的缺口，其余认领了故事的验收项都已勾选、各有自己的 E2E / 单元测试证据（这些测试都在本次全量回归里重跑通过）。缺口 / 偏差没有另开 issue 文件，全部记在各 ticket 的判断记录与 `review-backlog`，由审查阶段统一询问：① 14 号分类 / 支付渠道没有颜色选择（实体无颜色列，加列要迁移 + 同步 DTO，超出「只改展示」）；② 17 号 MAUI 原生的「按住」形态只能真机验收）
- [x] 自动化全量回归（本机可执行的部分）：`dotnet build FinanceTracker.sln` 零错误、`dotnet test` 全部通过、手机视口下的全部 E2E 通过（数字见 Status 与下方对照表；首轮全量 28/29，唯一失败是 `ledgers_page_test.py` 的造数据竞态——应用与造数同时往 `LedgerMembers` 写同一对 (账本, 用户) 撞了唯一索引，单独重跑 147/147，已把 `seed_ledger_member` 改成「先 UPDATE 再 INSERT」并给基座自测加了两项，之后的全量 29/29）。**评审与验收阶段修正之后又跑了一遍全量**：验收阶段的修正落地后首轮 28/29——`a11y_degradation_test.py` 的「字号 130%」体检 12 项失败（账单行备注的盒子比标题盒宽、分类网格固定行高裁掉名称下沿，见「验收阶段修正」后半），修复后该脚本单跑 346/346，最终全量 29/29、3440 项
- [ ] iOS 的 CI 构建通过：**未验证**。本机只验证了 iOS 目标的 C# 层随解决方案编译通过（`net10.0-ios` / `iossimulator-x64`，0 错误，含本重构新增的 `Platforms/iOS/Services/*`）；`.ipa` 的打包 / 签名 / 蒲公英上传只在 `.github/workflows/build-ios.yml`（`workflow_dispatch`，需要 `APPLE_CERTIFICATE` 等密钥）里做，我不触发——需要你手动跑一次并确认
- [ ] 人工全量回归：按设计稿 07 的 18 个路由逐页清单，在 Android 模拟器 / 真机与 iOS CI 产物上逐页走查——沉浸式状态栏、Tab 滑块、记一笔完整流程、各二级页的新建 / 编辑 / 删除、同步、导入、语音、待确认账单、共享账本权限（**未做**：没有设备 / 模拟器；各 ticket 的「人工验收」清单与 ADR 0007「已知限制」逐条列着具体要看什么，没有一项被当作已通过）
- [x] 用 E2E 基座的截图输出生成各主要页面的截图，与设计稿并排对照，差异记录在 ticket 中（不做像素级回归）（`.scratch/ios-ui-refactor/impl/design_capture.py`：用 `a11y_degradation_test` 的「什么都有」数据逐屏截图，390×844 / 像素比 3 / 用样式变量模拟刘海 47 + 手势条 34，与 `docs/design/ios-ui-refactor/screens/` 的 13 张并排输出到 `impl/shots/NN-name-compare.png`（已加进 `.gitignore`，随时可重新生成）；差异逐屏记在下面「截图对照」）
- [x] 数据与行为不变核对：账本必填、共享账本权限、同步、预算、导入、语音、软删除、时区口径、401 / 403 语义均与重构前一致；数据库迁移、REST 接口、同步策略无改动（逐项审计见下面「数据与行为不变核对」；**需要你知道的一点**：`server/FinanceTracker.Api` 的 4 个控制器在工作树里有未提交改动，它们属于重构开始之前就在工作树里的「账本必填」WIP，不是本次 UI 重构新增或改动的接口，与重构的改动混在同一个 `git diff` 里）

## 实现说明

- 覆盖 spec 用户故事：101、102、108（全量回归部分），并兜底核对 1–107。
- 设计稿：07 · 逐页改动清单（逐页回归清单）。
- 迁移策略为 expand–contract，本 ticket 是最后的 contract：新组件已覆盖所有页面后才可删除旧样式。删除前先确认没有页面仍在引用。
- MDI 图标与 ECharts 走 CDN 是既有问题，重构后 Tab 栏更依赖图标，离线启动时缺失会更明显；建议另开 issue 处理，不在本 ticket 内解决。
- iOS 只能在 CI 的 macOS 环境构建，人工回归通过 CI 产物完成。

### 完成记录（agent 实现）

**交付物**

| 层 | 文件 | 作用 |
|---|---|---|
| 清理 | `wwwroot/css/app.css`（只剩禁止长按选择 + 四个 `--ft-safe-*` 变量）· `wwwroot/js/tabBar.js`（删旧 `.container` 兜底）· `Components/Bills/BillFilterChip.razor`（已设置形态直接渲染 `ConditionChip`）· `Components/Ui/ConditionChip.razor`（注释）· `LedgerSelect.razor`（删除）· `wwwroot/css/ui.css`（删死样式、新增「兜底页面」一节） | 迁移期遗留清零 |
| 兜底页 | `Web/Main.razor` | 「页面未找到」= 套 `AuthLayout` 的通用空状态 + 主按钮「返回首页」；「组件渲染发生异常」= 不依赖任何布局的整屏卡片 + 可长按复制的错误详情；都只引用令牌 |
| 图表颜色 | `Web/Design/DesignTokens.cs`（+ `ChartAxisTextColor` / `ChartGridLineColor` / `ChartEmptyCellColor` / `ChartEmptyCellBorderColor`）· `StatisticsChartOptions` · `CalendarHeatmapChart` | 坐标轴文字原先停在 23 号之前的 `.62`（注释写着与 `--ft-label-2` 相同，其实已经漂移），日历空格子写死色值——现在取常量 |
| 测试（单元） | `VisualSystemGuardTests.cs`（新，6 项源码扫描守卫：MASA 标记白名单 / 无静态内联样式 / 共享库无写死颜色 / `app.css` 不依赖页面栈内部 DOM / 二级页内容区正式规则存在 / `tabBar.js` 无旧容器兜底）· `DesignTokensTests.cs`（图表颜色与令牌同值、坐标轴文字在白卡上 ≥ 4.5:1） | 2183（本 ticket 交付时 2033，之后评审与验收阶段陆续新增）|
| 测试（E2E） | `not_found_test.py`（新，22 项：A 结构与样式 / B 刘海与手势条 / C 宽屏 / D 回首页）· `e2e_base.py`（`GLASS_LAYERS_JS` 统一——15 个脚本各抄一份的「玻璃层数」探针合并成一份；`seed_ledger_member` 改成「先 UPDATE 再 INSERT」；删旧容器兜底）· `e2e_base_test.py`（+2 项：同一 (账本, 用户) 再造覆盖角色 / 状态且只有一行、显式指定 Id 时改写主键）· `README.md` | 3440 项（本 ticket 交付时 3348，之后评审与验收阶段陆续新增）|
| 文档 | `docs/adr/0007-ios-visual-system.md`（背景改为已全部完成、§27、替代方案、已知限制）· `CLAUDE.md` · `README.md` · `tests/e2e/README.md` | 同上 |
| 工具 | `.scratch/ios-ui-refactor/impl/design_capture.py`（逐屏截图并与设计稿并排）· `.gitignore`（忽略 `impl/shots/` 与 E2E 调试产物） | 截图对照 |

**逐项对照（验收项 → 证据）**

| # | 验收项 | 证据 | 结果 |
|---|---|---|---|
| A1 | 清理遗留 | 源码扫描：无无引用的 Razor 组件 / CSS 类；`VisualSystemGuardTests` 6 项守卫；`not_found_test.py` 22 项；全量 E2E 的沉浸式 / Tab / 滚动根断言在删除兼容层后全绿 | 通过 |
| B1 | 文档 | ADR 0007 §27 + 状态；CLAUDE.md / README / e2e README 已更新 | 通过 |
| C1 | 覆盖核对 | 108 / 108 条用户故事都被 ticket 认领；缺口见上 | 通过（含两处声明的缺口） |
| D1 | `dotnet build` | 0 警告 0 错误，约 72 秒（Android + iOS 两个目标的 C# 层） | 通过 |
| D2 | `dotnet test` | 2183 / 2183 | 通过 |
| D3 | 全部 E2E | `run_all.py` 29 / 29 脚本、3440 项检查，用时约 32 分钟（见下表） | 通过 |
| D4 | iOS CI 构建 | 未触发 | **未验证** |
| E1 | 人工全量回归 | 无设备 | **未做** |
| F1 | 截图对照 | 13 屏 + 13 张补充状态并排，差异见下 | 通过（发现的 6 处偏差已在验收阶段修正并复核） |
| G1 | 数据与行为不变 | 见下面审计 | 通过 |

**最终全量 E2E（评审与验收阶段的修正全部落地之后：`python -u run_all.py`，29 个脚本、3440 项检查，全部通过，用时约 32 分钟）**

| 脚本 | 检查数 | 脚本 | 检查数 |
|---|---|---|---|
| `e2e_base_test`（基座自测） | 196 | `a11y_degradation_test` | 346 |
| `addbill_sheet_test` | 176 | `bills_page_test` | 297 |
| `blazor_smoke_test` | 7 | `boot_test`（冷启动，并行工作） | 58 |
| `budgets_page_test` | 129 | `categories_page_test` | 133 |
| `custom_report_page_test` | 145 | `edit_bill_sheet_test` | 113 |
| `home_page_test` | 94 | `immersive_safe_area_test` | 29 |
| `import_page_test` | 179 | `ledger_members_page_test` | 138 |
| `ledgers_page_test` | 147 | `log_viewer_page_test` | 180 |
| `login_addbill_test` | 7 | `login_page_test` | 78 |
| `not_found_test` | 22 | `payment_channels_page_test` | 106 |
| `pending_bills_page_test` | 202 | `profile_page_test` | 97 |
| `seed_visibility_test` | 9 | `statistics_page_test` | 116 |
| `statistics_trend_annual_test` | 174 | `tab_shell_test` | 64 |
| `tab_slider_test` | 82 | `voice_addbill_test` | 6 |
| `voice_overlay_test` | 110 | | |

（本 ticket 交付时的那一遍是 3348 项：基座自测 188、记一笔 174、账单页 277、编辑 104、导入 166、待确认 182、统计 114、趋势与年度 157、语音覆盖层 109，其余相同——之后的差额来自第 2–4 轮评审与验收阶段的修正与补测。）

**覆盖核对**

- spec 里编号条目 108 个（1–108 连续、无缺号）；24 个 ticket 的「覆盖 spec 用户故事」合起来认领了全部 108 个，没有一个无人认领，也没有超出范围的编号；多个 ticket 共同认领的（如 95 号被 05 / 06 / 14 / 18 / 19 共同认领）是该条跨多个页面。
- 全部 ticket 里未勾选的验收项共 174 条（评审与验收阶段之后重新清点；本 ticket 交付时是 175 条）：171 条是「人工验收」（真机 / 模拟器 / 手感 / 真实服务联调；含本 ticket 的人工全量回归），3 条是已声明的缺口 / 未验证（14 号的颜色选择、17 号原生按住形态、本 ticket 的 iOS CI 构建）。
- 这次核对检查的是「每条故事有 ticket 认领」与「未勾选项的真实构成」；已勾选项的实现细节依据各自 ticket 的证据表，没有在本 ticket 里逐条重新验证——此前写 19 号完成记录时发现过一次把「E2E 造不出的运行态」误写成已覆盖，已改正（见 review-backlog 的校对条目），这类问题留给审查阶段的 Spec 审查再抽查。

**截图对照（13 屏，实现 vs 设计稿）**

整体：13 屏的版式、层级、材质（悬浮玻璃 Tab 栏 + 玻璃圆钮 + 大标题折叠 + 分组白卡 + 品牌紫主按钮 + 分段控件 + 胶囊芯片）与设计稿一致；数字不同是因为用的是「什么都有」的压力数据（含 ¥10,000,000 级金额、超长名称），不是回归。

| 屏 | 结论 | 差异 |
|---|---|---|
| 01 首页 | 一致 | 顶栏原先多一枚刷新图标，已撤掉（验收后用户拍板，07 号「验收后修订」）；Hero 金额在千万级仍放得下；预算超支 Toast 是压力数据触发的真实提示 |
| 02 账单 | 一致（验收阶段已修 ①） | 导航栏原先多一枚刷新，已撤掉（同上）；账单行的备注原先与分类同粗同色（设计稿「餐饮美食 · 午餐」备注是灰色常规字重），已改成分类加粗 + 备注灰色常规（见下「验收阶段修正」①） |
| 03 统计 | 一致（验收阶段缩小了圆环 ⑥） | 导航栏原先多刷新，已撤掉（同上）；「支出构成」标题旁多一枚「⇄ 收入」切换（11 号差异第 2 条：旧的收入分类统计与双环对比并入同一个圆环）；圆环单色是压力数据里一笔巨额账单占 100%；圆环原先比设计稿大一圈（盒子 224 vs 设计稿外径约 184），已收到 200（见 ⑥） |
| 04 我的 | 一致 | 原先多刷新，已撤掉（同上），导航栏右侧现在与设计稿一样没有按钮 |
| 05 记一笔（支出） | 一致（验收阶段已修 ②） | Sheet 顶边在刘海模拟下原先距屏顶约 89pt（设计稿约 58pt），到「账本」行的垂直距离比设计稿多约 39pt——390×844 + 47/34 的安全区下设置行只露出约 2.5 行（设计稿 4 行：账本 / 支付渠道 / 时间 / 备注）；量 DOM 后逐段收紧，现在四行都在键盘上方完整可见（见 ②） |
| 06 语音 | 一致（已记录的偏差） | 配色已按用户决定（第 2 轮评审）改成贴近设计稿的浅磨砂，状态 / 提示 / 说明 / 取消用 19px 粗体的大号文字补对比度（17 号判断项）；点按形态「说完后点一下麦克风结束 + 取消」取代设计稿的按住形态（17 号判断项） |
| 07 记一笔（收入） | 一致 | 设计稿画的是「请选择账本」+ 账本菜单展开的示意态，实现默认已选默认账本；② 的收紧同样适用（复核了 07 与编辑态 x09、账本菜单 x13、确认账单 x10） |
| 08 账本 | 一致 | — |
| 09 预算 | 一致（验收阶段已修 ③） | 设计稿「日均可用」提示条在超支态按 spec 不显示；极端超支时环中心「999%+」原先压到环线上，已按文字长度缩小字号（见 ③） |
| 10 待确认 | 一致（状态不同；验收阶段已修 ⑤） | 设计稿是「权限均已开启」的细绿条，E2E 环境没有权限所以是两行「去开启」；卡片分类显示「其他支出」（19 号判断项）；卡片底部的账本芯片原先被截成「日常…」，已显示完整账本名（见 ⑤） |
| 11 分类 | 一致 | 设计稿画的是左滑展开态 |
| 12 登录 | 一致 | 缺面板底部「用户协议」一行（22 号判断项：没有协议文本）；手机号前缀没有设计稿的下拉小箭头（国家码不可选） |
| 13 筛选 | 一致（验收阶段已修 ④） | 超长账本名的芯片原先溢出 Sheet 右缘（没有省略号）、「时间」一行 5 个芯片折成两行（设计稿一行），已改（见 ④）；设计稿画的是已选态（本月 / 日常开销…），实现默认全部未选 |

**验收阶段修正（评审结束后启动 Web.Server，按设计稿逐屏对照；量设计稿像素与 DOM 后修改，修完重新截图复核并重跑受影响的 E2E）**

13 屏与 13 张补充状态截图（`x01`–`x13`：趋势 / 年度 / 支付渠道 / 短信识别 / 导入 / 自定义报表 / 错误日志 / 成员 / 编辑 / 确认账单 / 多选 / 删除 Alert / 账本菜单）全部逐张看过。上表原先记的 4 处差异，加上量设计稿像素时新发现的 2 处，共修正 6 处：

| # | 屏 | 偏差 | 修正 | 复核 |
|---|---|---|---|---|
| ① | 02 账单 | 账单行备注与分类同粗同色 | `BillRowModel` 多带 `CategoryName` / `NoteText`（备注的换行 / 连续空白折成一个空格）；`ListCell` 新增 `TitleNote`：有备注时标题盒是 flex，分类名一个子项（放不下才省略）、「 · 备注」一个子项（灰色常规字重，占剩下的宽度并自己省略）；DOM 文字与之前完全相同（E2E 读到的仍是「餐饮美食 · 午饭」） | 02 / 01 截图；`BillPresentationTests` +9 项（分开给两段、去首尾空白、折叠中间空白） |
| ② | 05 / 07 记一笔 | 设置行只露 2.5 行（设计稿 4 行） | 逐段对设计稿像素：全局 Sheet 把手区 20 → 14px；记一笔 Sheet 的标题栏 `min-height` 48 + 下内边距 4、底栏下内边距 = `max(12px, 底部安全区)`（键盘底边与手势条上沿对齐）、金额区 `min-height` / `line-height` 56、分类网格与设置行上外边距 6 → 4 / 8 → 6；令牌：分类图标圆 52 → 48（设计稿实测 48）、分类一行 84 → 77（设计稿行距 77）、紧凑设置行 46 → 44（设计稿 42，但 44 是全 App 的点按面积下限）、数字键高 50 → 48（设计稿 50；让出 8pt 才能在 393×852 的刘海手机上放下四行） | 05 / 07 / x09 / x10 / x13 截图（四行都在键盘上方完整可见）；量得（390×844 + 47/34）：Sheet 顶 59pt（设计稿 58）、第四行设置行底 583pt、键盘顶 594pt（设计稿 594）、键盘底 810pt（= 屏高 − 34，设计稿同样与手势条上沿对齐）；`addbill_sheet_test` 176、`edit_bill_sheet_test` 113、`a11y_degradation_test` 346 |
| ③ | 09 预算 | 极端超支时环中心「999%+」压到环线上 | `BudgetOverviewCard` 在文字超过 4 个字符时打 `data-long`，字号换成 `--ft-type-ring-percent-long`（`clamp(16px, 5.6vw, 22px)`） | 09 截图；`budgets_page_test` 129 |
| ④ | 13 筛选 | 超长账本名的芯片溢出 Sheet 右缘；「时间」一行 5 个芯片折成两行 | `.ft-chips > .ft-chip { max-width: 100% }`（长名字自己省略）、`.ft-chiprow > .ft-chip { max-width: 85% }`；`--ft-chip-pad` 14 → 12，五个时间芯片在 390 宽下一行放得下 | 13 截图；`bills_page_test` 297 |
| ⑤ | 10 待确认 | 卡片底部账本芯片被截成「日常…」（量设计稿时发现） | `.ft-pcard__foot > .ft-btn` 内边距 `0 14px`、字号 15（按钮高度仍是 44pt 的点按面积，设计稿约 36pt） | 10 截图；`pending_bills_page_test` 202（含 320 宽小节） |
| ⑥ | 03 统计 | 圆环比设计稿大一圈（量设计稿时发现） | `--ft-donut-size` 224 → 200（设计稿外径约 184，再小中心的孔放不下长金额）、中心总额字号 24 / 30 → 21 / 28、长金额 17 / 24 → 16 / 22；`statistics_page_test` 的圆环宽度上限 224.5 → 200.5 | 03 / x01 / x02 截图；`statistics_page_test` 116 |

**复核时又发现并修掉的两处回归**（`a11y_degradation_test` 的「字号 130%」体检抓到，12 项失败，修后 346/346）：
- 账单行备注最初写成标题里的行内元素：系统字号放大后它的盒子比标题盒宽得多（只是被 `overflow` 裁掉），被「没有元素越出视口左右边」抓到——改成上面 ① 的 flex 子项结构，两个盒子都不比标题盒宽。
- 分类网格的行高是固定令牌值（84 → 77 之后更紧）：130% 字号下名称行变高、下沿被裁——网格行改成 `minmax(var(--ft-cat-row-height), auto)`、名称行 `flex: none`；100% 字号下像素不变（行高仍是 77）。

**数据与行为不变核对**

核对方法：逐项对照 `git diff HEAD` 的后端部分（Core / Api / Infrastructure / Shared / Web.Server）与单元测试；`dotnet test` 全绿（既有测试的断言没有被删除或放宽——tracked 的单元测试文件里没有任何被删除的 `Assert`）。

| 项 | 结论 |
|---|---|
| 数据库迁移 | 无：`Infrastructure` / `Infrastructure.Sqlite` / `Shared` 没有任何改动；实体只有注释与不入库的成员（`User.StubPhonePrefix` 常量 + `IsStubPhoneNumber`、`Bill.LedgerId` 的注释），没有新增列 |
| 同步策略 | 不变：`SyncService` 只有 2 行把 `"stub_"` 字面量换成 `User.StubPhonePrefix` / `IsStubPhoneNumber`（同值同义）；同步仍是手动触发 + 导入后自动一次 |
| 共享账本权限 | 不变：`LedgerMemberService` 只把一个查询挪进 `LedgerMemberQueries`（同样的过滤与排序）、新增只读的 `GetActiveMemberCountsAsync`（给账本列表标「共享」与成员数，只读本地缓存、不联网）；权限判定与 403 / `ForbiddenAccessException` 路径未动 |
| 401 / 403 语义 | 不变：控制器里没有 `Forbid(string)`；新增的写权限助手用 `StatusCode(StatusCodes.Status403Forbidden, new { message })`（属于「账本必填」WIP） |
| 账本必填 | 不变：`BillService` 统一校验仍是唯一入口；E2E 验证了记一笔 / 编辑 / 导入 / 待确认入账四个入口「没选账本不能保存 / 导入」（`addbill_sheet_test` / `edit_bill_sheet_test` / `import_page_test` / `pending_bills_page_test`）；`AssignBillsToLedgerAsync` 的 `ledgerId` 由可空改为必填（只能归属、不能「移出账本」）——属于「账本必填」WIP |
| 软删除 | 不变：删除账单仍走 `IsDeleted`；E2E 用 `LocalDb.bill_row(include_deleted=True)` 核对整行 |
| 时区口径 | 不变：没有新增 `ToUniversalTime()` / `ToLocalTime()` 调用；`TimeBoundaries` 只新增 `StartOfWeek(DateTime)`（周一起点，语音记账的「本周 / 上周」与趋势统计共用）；统计区间仍左闭右开 |
| 预算 | 不变：预算实体、唯一性（应用层校验）、超支通知（每日最多一次）都没改；新增的只是展示用的「日均可用」（展示层按应用时区的今天算） |
| 导入 | 微信解析规则没改；支付宝解析器在第 2 轮评审后（用户拍板）改为按表头名称定位列（见 20 号 ticket 实现要点 6）；`ImportController` 的改动属于「账本必填」WIP（账本必填 + 写权限校验） |
| 语音 | 不变：`BillVoiceParser` 只新增「匹配片段位置」的只读输出（给覆盖层高亮用），解析结果不变；语音仍只预填表单、来源标记 `BillSource.Voice` |
| REST 接口 | 本次 UI 重构没有新增或改动接口。工作树里有 4 个控制器的未提交改动（`BaseApiController` / `BillsController` / `ImportController` / `StatisticsController`，含新增的 `GET /api/statistics/ledger`），属于重构开始之前就在工作树里的「账本必填」WIP（账本必填校验、写权限助手、按账本统计），与重构的改动混在同一个 `git diff` 里 |
| 统计服务 | 数值口径不变。一处展示层修正（21 号发现并记录）：`GetCategoryComparisonAsync` 里本期归零的分类取上期的名称与图标，不再显示成「未知分类」；另有新增的 `GetLedgerStatisticsAsync`（按账本汇总，无账本的历史账单归「未归属账本」，属于「账本必填」WIP）。另：`GetCategoryComparisonAsync` 回推上一等长周期时对 `DateTime.MinValue` 下溢出会抛异常，自定义报表页把日期范围限制在 2000–2100 年规避（21 号判断项），服务层没有改 |
| 新增的服务接口 | `IBillService` 的按条件列表 / 总数 / 全量汇总（`BillFilter`，对应 spec 允许的账单搜索与汇总查询）、`ILedgerMemberService.GetActiveMemberCountsAsync`、`IStatisticsService.GetLedgerStatisticsAsync`——都是只读查询，不改数据 |

**发现与处理**

1. **`ledgers_page_test.py` 的造数据竞态**（首轮全量里唯一的失败）：`sqlite3.IntegrityError: UNIQUE constraint failed: LedgerMembers.LedgerId, LedgerMembers.UserId`——应用在账本创建 / 打开成员页 / 页面加载顺带刷新成员缓存时也会往 `LedgerMembers` 写同一对 (账本, 用户)，造数与应用写入的先后取决于当时的负载；单独重跑 147/147。**修复**：`seed_ledger_member` 改成先 `UPDATE`（拿到写锁并持有到提交）再按需 `INSERT`，冲突时以造数为准，调用方显式指定 Id（要与假云端对上）才改写主键；没用 `ON CONFLICT`，因为基座自测的临时库只有列、没有唯一索引（第一次就这样写，自测立刻报「不匹配任何唯一约束」，改后 188/188）。
2. **`not_found_test.py` 的期望写错了一处**：未登录点「返回首页」应该落在 `/login`（`/` 被带到登录页），不是 `/`；已改正，22/22。
3. **`DesignTokensTests` 的 xUnit2000 警告**（`Assert.Equal` 实参顺序），已改，构建 0 警告。
4. **`.gitignore`**：`design_capture.py` 的文档字符串写了「输出目录已被忽略」而当时并没有——已补上忽略规则并核对生效。

**判断记录（需要用户知晓 / 确认）**

1. **仓库根目录两个早先提交的页面 HTML 转储 `e2e_addbill_page.html` / `e2e_voice_addbill_page.html`**（已在 HEAD 里，不是本重构产物；脚本与文档都没有引用，内容是旧「记一笔」页面的渲染结果）：**审查阶段已问过用户，决定删除，已删除**（工作树里是未提交的删除）。
2. **`StatisticsRedirect` / `StatisticsSegmentRequest` / `TrendAnalysis.razor` / `AnnualReport.razor`（旧地址 `/trend`、`/annual-report` 的薄页面）保留**：应用内已无调用方，只剩外部深链兜底（MAUI 里没有 Web.Server 的 HTTP 302）；删了就是旧书签 404。审查阶段问过用户，**决定保留**。
3. **`p.ft-…` 这类绕开 `<p>` 外边距的选择器没有收回**：23 号已把 `.m-application p { margin-bottom: 0 }` 全局归零，保留无害，收回只会增加风险。
4. **几个页面里相近的文案常量没有抽共享件**（短信识别页与导入页各一份「加载中… / 加载失败，点按重试」、各页的账本行展示）：两三个字符串，抽出来要让两页互相依赖，成本大于收益（`review-backlog` 里 20 号「账本行文案重复」条按此结论关闭）。
5. **「组件渲染发生异常」兜底页没有任何自动化触发过**（渲染失败没法在 E2E 里注入）：样式与结构靠源码守卫，页面本身走查代码 + 人工验收（真机上故意制造一次渲染异常）。
6. **验收阶段照设计稿改时留下的几处小偏差**（都是「照设计稿」与「全 App 的 44pt 点按面积下限 / 在 393×852 的刘海手机上放下四个设置行 / 长金额放得进圆环中心」之间的折中，数字见「验收阶段修正」；这几项是我按硬约束就地做的取舍、没有逐项向用户确认——要改回设计稿数值，改 `tokens.css` 里对应的几个令牌即可，真机上的手感属于人工验收）：数字键高 48（设计稿 50）；紧凑设置行 44（设计稿 42）；待确认卡片按钮高 44（设计稿约 36）；统计圆环盒子 200（设计稿外径约 184）；筛选芯片水平内边距 12（原 14）；全局 Sheet 把手区 14px（原 20；把手与标题栏仍都是拖拽收起的热区）。

**待人工验收**

- 真机 / 模拟器上 18 条路由的逐页走查（见上，未做）；iOS CI 产物（未触发）。
- 沉浸式状态栏图标颜色真正切换、Tab 滑块与触感、玻璃模糊在 Android WebView / iOS WKWebView 上的真实渲染与低端机流畅度、软键盘避让、系统文件选择器 / 日期选择器 / 分享面板 / 下载落点、原生按住说话、通知栏与短信捕获的真实链路、真实云端联调（同步成功路径、共享账本邀请 / 成员管理）、Android 返回键与 Sheet 的配合——这些在各 ticket 的人工验收清单与 ADR 0007「已知限制」里都有逐条说明，**没有一项被当作已通过**。
- 23 号遗留的三项仍开着（需单独立项）：Sheet / 语音覆盖层升起时底下页面的 `inert` + 焦点陷阱、Android 返回键先收起 Sheet / 覆盖层（要改原生宿主）、完整读屏（VoiceOver / TalkBack）适配审计。
