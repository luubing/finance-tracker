# 23 — 无障碍与降级加固

**What to build:** 对已经完成的主要界面做一次统一的无障碍与降级加固：开启系统「减弱透明度」时，所有玻璃表面回退为不透明纯色；开启「减弱动态效果」时，所有动画只剩简短淡入或无动画；正文随系统字号缩放到至少 130% 而布局不破；文字对比度与点击区域达标；重要状态除颜色外有文字。低端 Android 设备上玻璃模糊自动降级，滚动保持流畅。

**Blocked by:** 04 — Tab 滑块切换效果；13 — 我的 Tab；15 — 记一笔 Sheet（新建）

**Status:** completed（自动化验收全部通过——`dotnet build FinanceTracker.sln` 0 警告 0 错误、`dotnet test` 2023 个用例全部通过（T22 时 1986，累计数含并行的冷启动工作新增的用例）、`run_all.py` 28 个脚本共 3314 项检查全部通过（1 项按设计跳过：「语音入口点按」在环境支持语音识别时由 `voice_overlay_test.py` 的假识别器覆盖）；其中新增 `a11y_degradation_test.py` 346 项（M 材质降级 36 / S 逐屏扫描 81 / Z 大字号 177 / R 减弱动态效果 35 / T 状态不只靠颜色 17）。全量回归之后又把 `--ft-warning-text` 与 `--ft-callout-warning-text` 合并（判断 12），受影响的 `home_page` / `profile_page` / `budgets_page` 三个脚本、a11y 的 S 节、单元测试、整个解决方案的构建都重跑通过。真机 / 模拟器上的验收（Android 大字号、低端设备、iOS 系统「降低透明度」「减弱动态效果」、对比度抽查）等人工，见下方「待人工验收」；若干判断项待用户确认，见判断记录）

- [x] 减弱透明度：开启系统 / 媒体特性「减弱透明度」时，所有玻璃表面（Tab 栏、玻璃导航按钮、Sheet 背板、Toast / Alert）回退为不透明纯色；同时提供根节点级的单一开关，使原生宿主在判定设备性能不足时也能触发同样的回退（`tokens.css` 三条触发路径——媒体特性 / 根开关 `<html data-ft-solid>` / 浏览器不支持 `backdrop-filter`——共用一组不透明令牌，`DesignTokensTests` 校验三处逐条一致；根开关由 `UiRuntime` 经 `IRenderingProfileService` 同步；E2E M 节三条路径各验一遍）
- [x] 低端 Android 降级：原生宿主在判定性能不足时（判据由本 ticket 选定并写入实现说明，例如低内存设备）打开该开关；全部页面核查「每屏同时叠加的玻璃层不超过 3 层」并修正超标处（判据 = 系统标记的低内存设备，或总内存 < 3 GiB（`LowEndDeviceCriteria`，纯函数，单元测试覆盖边界）；Android / iOS 原生实现随解决方案构建通过，**真机未运行**，见人工验收；玻璃层：Sheet 升起时被盖住的页面与 Tab 栏让位（`<html data-ft-sheet-open>`，取代了 09 / 15 号留下的「4 层」例外），E2E S 节逐屏量到每屏 ≤ 3，Toast 是文档化的瞬时例外）
- [x] 减弱动态效果全局核查：Tab 滑块、Sheet 升起与背景缩放、Toast / Alert、页面淡入、下拉刷新、语音波形等所有动画都有降级（去掉弹簧与拉伸，只保留短淡入或无动画）（三层：`tokens.css` 在 reduce 下把全部 `--ft-dur-*` 压成 100 ms、各组件的 reduce 规则去掉位移 / 缩放 / 弹簧、`ui.css` 末尾的 MASA 过渡兜底；E2E R 节扫描 Tab 切换 / 记一笔 / 筛选 Sheet / 页面入栈出栈 / Alert / Toast / 语音覆盖层，没有 > 150 ms 的动画或过渡；`DesignTokensTests` 守住「reduce 规则写在基础规则之后」与「动效令牌取同一简短时长」）
- [x] 大字体：正文随字号缩放至少 130%；主要页面（四个 Tab、记一笔 Sheet、各二级页）布局不溢出、关键按钮仍可点（E2E Z 节用样式改写模拟 130%，在 390 与 320 两个宽度下扫描四个 Tab、记一笔 Sheet、各二级页面、登录页；**模拟不等价真机 `fontScale` / Dynamic Type**，真机大字号见人工验收）
- [x] 对比度与点击区域：文字与背景对比度不低于 4.5:1（玻璃之上的文字按最不利背景核对）；可点击区域不小于 44×44；核查并修正不达标处（S 节逐屏扫描：对比度先按计算样式合成背景算、再用截图像素复核；热区按 `elementFromPoint` 量实际点得到的区域；两张放行清单**都是空的**；修正处见「实现要点 / 发现」）
- [x] 状态不只靠颜色：预算与超支状态带文字说明；其他仅靠颜色区分的状态补上文字或图标（T 节逐项验证：Tab / 分段 / 多选 / 金额正负号 / 预算三档（文字 + 读屏文案）/ 同步状态 / 账本角色 / 日志级别 / 表单错误；预算与角色等在 07 / 18 / 06 号落地时已带文字，本 ticket 补的是自动化证明）
- [x] 自动化验收（E2E，手机视口）：分别以「减弱动态效果」「减弱透明度」模拟运行主要路径（Tab 切换、记一笔保存、账单列表），断言无拉伸动画、玻璃表面为不透明；设置根节点开关后同样回退；在字号放大到 130% 的近似模拟下，主要页面无横向溢出；对主要页面扫描可点击元素的尺寸不小于 44×44（允许带说明的白名单）（`a11y_degradation_test.py` + `a11y_probes.py`，346 项；`e2e_base.py` 新增 `set_rendering` / `rendering_state`）
- [ ] 人工验收：Android「显示大小与文字」调到大字号；低端 Android 设备（或受限性能的模拟器）上滚动与切换流畅、玻璃降级生效；iOS（CI 产物）开启「减弱透明度」「减弱动态效果」后的表现；对比度抽查（待设备 / 模拟器，见下方「待人工验收」）
- [x] 回归：既有 E2E 与 `dotnet test` 全部通过，`dotnet build` 零错误

## 实现说明

- 覆盖 spec 用户故事：96–100。
- 设计稿：06 · 设计规范（无障碍与降级相关项）。
- 玻璃表面一律通过设计令牌取值（03 已要求），所以回退应当在令牌层一处完成，而不是逐页改样式。
- 完整的读屏（VoiceOver / TalkBack）适配审计不在本次范围。
- 本 ticket 只加固已有界面，不新增功能；二级页面里后续才完成的页面由其各自 ticket 遵守同一套令牌，收尾 ticket 24 再做一次全量确认。

### 完成记录（agent 实现）

**交付物**

| 层 | 文件 | 作用 |
|---|---|---|
| 宿主判定 | `Web/Services/Rendering/IRenderingProfileService.cs`（接口 + 恒为 false 的 `NoOpRenderingProfileService`）· `Web/Services/Rendering/LowEndDeviceCriteria.cs`（低端判据，纯函数）· `App/Platforms/Android/Services/RenderingProfileService.cs`（读 `ActivityManager` 的低内存标记与总内存，构造时判定一次）· `App/Platforms/iOS/Services/RenderingProfileService.cs`（`UIAccessibility.IsReduceTransparencyEnabled` + 状态变化通知）· `App/MauiProgram.cs`（注册）· `Web.Server/Program.cs` + `E2ERendering.cs`（E2E 假实现与 `/__e2e/rendering`，与 `E2ECapture` 同一个开关，MAUI 不引用） | 「宿主判定 → 根开关」这条链路 |
| 行为层 | `Web/Components/UiRuntime.razor`（先订阅再读当前值，变化事件即时跟随）· `wwwroot/js/ui.js`（`setSolid`；`syncSheetOpen` 用 MutationObserver 维护 `<html data-ft-sheet-open>`） | 写两个根属性 |
| 样式 | `wwwroot/css/tokens.css`：不透明实色令牌、三处材质降级覆盖块、reduce 下的动效令牌覆盖、`--ft-label-2`（.62 → .76）、`--ft-success-text`、`--ft-herocard-scrim`、`--ft-amount-fit-*`、`--ft-warning-text` 加深并吸收 `--ft-callout-warning-text` · `wwwroot/css/ui.css`：Sheet 升起时页面 / Tab 栏让位、`.m-application p { margin-bottom: 0 }`、MASA 过渡兜底、金额光标 reduce 规则、预算「编辑」热区、Hero 金额收缩、转圈放慢等 | 令牌层一处回退 |
| 图表 | `Web/Design/DesignTokens.cs`（+ `ExpenseColor` / `IncomeColor` / `BrandTintColor`）· 三张 ECharts（`CumulativeNetChart` / `CalendarHeatmapChart` / `CategoryRankChart`）改用设计令牌 | 配色与 CSS 令牌同值 |
| 测试（单元） | `RenderingProfileTests.cs`（新：低端判据 / 空实现 / 三处降级覆盖块一致且不漏令牌 / 降级后模糊为 none 底色取实色令牌 / 组件的 `backdrop-filter` 只引用令牌 / 玻璃底色不写死 / `ui.js` 导出根开关与 Sheet 标记）· `DesignTokensTests.cs`（图表主色浅底 / 语义色与 C# 常量一致 / 次要文字与占位文字对比度 / Hero 小卡暗纱 / 成功文字色 / 「reduce 覆盖规则必须写在基础规则之后」/「reduce 下所有动效令牌取同一简短时长」等） | 累计 2023 |
| 测试（E2E） | `tests/e2e/a11y_degradation_test.py`（新，346 项：M / S / Z / R / T 五节）· `tests/e2e/a11y_probes.py`（新：对比度 / 热区 / 溢出 / 材质 / 动画记录器的页面探针与逐屏扫描驱动）· `e2e_base.py`（`set_rendering` / `rendering_state`）· `home_page_test.py` / `profile_page_test.py`（预警文字色的钉死值随令牌合并改为 `rgb(154, 80, 0)`）· `README.md`（登记脚本、观察属性 `data-ft-solid` / `data-ft-sheet-open`、`/__e2e/rendering`、已知限制） | 见脚本头部 |
| 文档 | `docs/adr/0007-ios-visual-system.md` 第 26 点（并回填 §7 / §9 / §11 / §13 / §15 / §16 / §20 与「已知限制」里所有「留给 23 号」的备注）· `tests/e2e/README.md` · 07 / 08 / 09 / 13 / 15 / 18 / 19 / 22 号 ticket 里「留给 23 号」的条目标注了处理结果 | 架构、判断项、未处理项 |

**逐项对照（验收项 → 证据）**

| # | 验收项 | 证据 | 结果 |
|---|---|---|---|
| A1 | 减弱透明度（媒体特性） | E2E M 节：九类玻璃场景（首页 / 账单 / 统计 / 我的 / 账本 / 记一笔 Sheet / 删除 Alert / Toast / 语音覆盖层）+ 登录页，没有任何 `backdrop-filter`、玻璃表面底色不透明、不透明表面上的文字仍 ≥ 4.5:1；功能照常（Tab 切换 / 记一笔保存 / 账单列表） | 通过 |
| A2 | 根节点单一开关 | M 节：`setSolid(true)` 后令牌取值与媒体路径逐条一致；`setSolid(false)` 后属性移除、玻璃恢复；`RenderingProfileTests` 守住三处覆盖块一致 | 通过 |
| A3 | 宿主判定链路 | M 节：假实现 `/__e2e/rendering` 报告「偏好实色」后不刷新页面即时降级、重载后仍降级、关掉后恢复；Android / iOS 原生实现随解决方案构建通过，**未真机运行** | 通过（原生部分待人工） |
| B1 | 低端判据 | `LowEndDeviceCriteria` + `RenderingProfileTests`（低内存标记 / 总内存边界 / 读不到时不降级） | 通过（门槛值待真机校准，判断 6） |
| B2 | 每屏玻璃层 ≤ 3 | S 节逐屏扫描（四个 Tab 含统计三个分段、全部二级页面、各类 Sheet / 菜单 / Alert / Toast / 语音覆盖层、登录页）；`data-ft-sheet-open` 让位规则；Toast 单列（判断 7） | 通过 |
| C1 | 减弱动态效果全局核查 | R 节：七类场景无 > 150 ms 的动画 / 过渡，没有无限循环的装饰动画（对照组证明探针没失灵）；单元测试守住 reduce 规则的顺序与动效令牌 | 通过 |
| D1 | 大字体 130% | Z 节：390 / 320 两个宽度 × 四个 Tab / 记一笔 Sheet / 各二级页面 / 登录页——无横向溢出、无元素越出视口、无文字被固定高度裁掉、点击区域仍 ≥ 44 | 通过（模拟；真机待人工） |
| E1 | 对比度 ≥ 4.5:1 | S 节：每屏的全部文字（> 400 段，防探针失灵的下限断言），大号文字 3:1；放行清单为空 | 通过 |
| E2 | 点击区域 ≥ 44 × 44 | S 节：每屏每个可点击元素的实际点击区域（容差 1 px）；Z 节 130% 下同样 | 通过 |
| F1 | 状态不只靠颜色 | T 节 17 项（Tab / 分段 / 多选 / 金额正负号 / 预算三档文字 + 读屏文案 + 总预算环 / 同步状态 / 账本角色 / 日志级别 / 登录表单错误） | 通过 |
| G1 | 自动化验收 | 同上，346 项；`set_rendering` / `rendering_state` 进基座 | 通过 |
| H1 | 回归 | `dotnet build` 0 警告 0 错误；`dotnet test` 2023/2023；`run_all.py` 28/28 脚本、3314 项检查通过（1 项按设计跳过）；令牌合并后重跑 home / profile / budgets 三个脚本、a11y S 节（81/81）、单元测试、解决方案构建 | 通过 |
| I1 | 人工验收 | 待设备 / 模拟器 | 未做 |

**判断记录（需要用户知晓 / 确认）**

1. **`--ft-label-2`（次要文字，占位文字也用它）的不透明度由 .62 加深到 .76**。iOS 原生次要标签色是 60%，在白卡上只有约 3.6:1、在页面底色与搜索框底上更低，达不到验收项的 4.5:1；加深后白卡 5.3 / 页面底色 5.0 / 搜索框底 4.6。代价：次要文字与占位文字整体比设计稿 / iOS 原生深一点、层次略平。备选：保持 iOS 值并把次要文字全部放进放行清单（违背验收项），或只加深小字号 / 占位文字。**待确认**。
2. **占位文字没有单设更浅的令牌**，与次要文字共用 `--ft-label-2`（判断 1 的一部分；AA 对占位文字同样适用）。
3. **首页 Hero 小卡加了一层暗纱 `--ft-herocard-scrim`（rgba(20, 4, 100, .10)）**：小卡压在渐变最亮的一角（右下）时，白色说明文字只有约 4.3:1，加暗纱后达标；小卡比设计稿略深。备选：加深 Hero 渐变右下角（影响整张 Hero 的观感）、或把说明文字加粗加大走大号文字 3:1 的门槛（改变层级）。**待确认**。
4. **`.m-application p { margin-bottom: 0 }` 全局归零**（MASA 给全局 `<p>` 写了 16px 下外边距，13 号起各页各自绕开）。这是对基础框架样式的全局改写；本应用里的 `<p>` 本来都按「X 0 0」写，全部 E2E 通过未发现副作用。备选：继续「不用 `<p>` / 写 `p.ft-xxx`」的约定逐页绕开（那些写法现在仍保留、无害）。
5. **对比度的「最不利背景」口径偏保守**：玻璃上的文字取「玻璃底色分别合成在白 / 页面底色 / 品牌渐变各色标上」的最低值；渐变背景取各色标的最不利值；大号文字（≥ 24px，或 ≥ 18.66px 加粗）按 3:1。
6. **低端 Android 判据：系统低内存标记，或总内存 < 3 GiB**（读不到内存时不降级）。依据：毛玻璃的开销与内存 / GPU 档次相关，内存是 Android 上唯一稳定可读且不需要权限的信号；报告值比标称值少一截，3 GiB 刚好把「标称 3 GB 及以下」判为低端。**没有真机分布数据，是建议值**，低端机验收后再调。**待确认 / 真机校准**。
7. **Toast 不计入每屏 ≤ 3 层的预算**（瞬时、盖在最上面、几秒后自己消失），E2E 里单独列出并写明理由；Sheet 升起期间被盖住的页面与 Tab 栏关掉毛玻璃。备选：Toast 也改实色（失去玻璃感）。
8. **新增 `--ft-success-text`（#127436）**给「可编辑」徽标用：`--ft-income-text`（#188540）压在 16% 绿浅底上只有约 4.1:1；金额继续用 `--ft-income-text`。
9. **Hero 主指标与小卡金额的收缩系数留 1.3 倍余量**（`--ft-amount-fit-xl` .62 / `--ft-amount-fit-card` .70）：系统字体放大到 130% 时 ¥99,999,999.99 仍放得下；代价是特别长的金额在默认字号下也比「刚好放得下」缩得小一点。
10. **减弱动态效果：时长压到 100 ms，而不是 0**（MASA 依赖 `transitionend` 收尾，压成 0 会让对话框 / 菜单卡在中间态）；「简短」的口径 = ≤ 150 ms；加载转圈保留（唯一的「进行中」反馈）并放慢到 2.4 s 一圈；金额光标在 reduce 下不闪。MASA 的过渡兜底必须 `!important`（`.m-icon.m-icon` 叠类名）。
11. **三张 ECharts 图表的配色改成设计令牌**：分类排行柱 = 品牌色；累计收支的支出 / 收入 / 净收支 = 令牌里的红 / 绿 / 品牌色；日历热力色阶 = 品牌浅底 → 预警橙 → 支出红，无数据格 `#F2F2F7` + `#E5E5EA` 描边。观感与旧版（Material 配色）不同，需对照设计稿 03 验收。
12. **`--ft-warning-text` 与 `--ft-callout-warning-text` 合并成一个 #9A5000**（18 号为日均可用提示条另开了 #9A5000，因为通用的 #A15500 叠在页面底色上的 16% 橙上只有 4.39:1）。合并让首页 / 我的页的预警文字比以前略深（肉眼几乎无差），两处钉死 `rgb(161, 85, 0)` 的 E2E 同步改为 `rgb(154, 80, 0)`。`--ft-capture-ready-text`（19 号）等各自底色不同的文字令牌保留。
13. **E2E 探针的口径**：热区下限取 43（容差 1 px 的采样误差）；量到不达标的元素当场把滚动容器滚到顶端 / 底端 / 正中各量一次取最大并还原滚动位置（排除被吸底键盘 / 滚动折线切掉的行）；单行省略与行数截断不算裁字，裁字阈值 0.25em；横向滚动容器内的内容与页面栈压入二级页时的 40px 视差位移不算越界。放行清单（热区、对比度）当前都是空的。
14. **没有处理、仍然开着的三项**（原本分别记在 15 / 17 号名下「留给 23 号」，但不在 23 号的验收项里）：Sheet / 语音覆盖层升起时底下页面的 `inert` 与焦点陷阱；Android 系统返回键先收起 Sheet / 覆盖层（要改原生宿主、只能真机验证）；完整的读屏（VoiceOver / TalkBack）适配审计（ticket 明确排除）。已在 ADR「已知限制」与 15 / 17 号 ticket 如实标注。**是否单独立项待确认**。
15. **与并行的「冷启动」工作的交叉**：另一个会话在同一工作树里做冷启动（加载页 / 启动画面 / 应用图标：`boot.css` / `boot.js` / `StartupHost.razor` / `boot_test.py` 等），共用 Web.Server 端口、本地库、假云端端口与 `%TEMP%\ft_e2e_logs`；本 ticket 期间我重启过两次 Web.Server、`seed_world` 类步骤会清理待确认账单，可能打断过对方的运行。共享文件（`DesignTokensTests.cs`、`tests/e2e/README.md`、ADR 0007、图表组件）一律用小范围替换修改。

**契约（后续 ticket 直接使用）**

- **新增玻璃表面一律只用 `--ft-glass-*` / `--ft-voice-*` 令牌**（模糊取令牌里的 filter、底色取令牌里的 bg）：降级自动覆盖；写死 `backdrop-filter: blur(…)` 或半透明底色会被 `RenderingProfileTests` 拦下。
- **新增动画 / 过渡的时长一律用 `--ft-dur-*` 令牌**，reduce 下自动变短；弹簧 / 位移 / 缩放 / 循环动画要在自己的 `@media (prefers-reduced-motion: reduce)` 规则里去掉，且**该规则必须写在基础规则之后**（同选择器同优先级，后写的赢；`DesignTokensTests` 会拦）。
- **新增文字颜色只用文字专用的令牌**（`--ft-label` / `--ft-label-2` / `--ft-danger-text` / `--ft-income-text` / `--ft-warning-text` / `--ft-success-text` / 各专用文字令牌）；`--ft-label-3` 与 `--ft-expense` / `--ft-income` 等「图形色」不得作文字色。新增一屏就把它加进 `a11y_degradation_test.py` 的场景清单（`all_scenes()`），对比度 / 热区 / 玻璃层 / 大字号自动扫描。
- **可点击元素的实际点击区域 ≥ 44 × 44**（负外边距扩出来的热区要 `position: relative; z-index: 1`，否则会被下一行盖住）。
- **新增 Sheet 不需要自己处理玻璃层预算**：`ui.js` 在任何 Sheet 升起时写 `<html data-ft-sheet-open>`，页面与 Tab 栏的玻璃自动让位。
- **写在 `ui.css` 开头的 `.m-application p { margin-bottom: 0 }`** 之后，`<p>` 可以放心用；老页面里绕开它的写法留着无害。
- **E2E**：`set_rendering(True|False)` / `rendering_state()` 驱动宿主判定；用到它的脚本开始 / 结束各 `set_rendering(False)` 一次。扫描类脚本运行期间不要同时跑别的会改库 / 重启服务的脚本。

**与 ticket / 设计稿的差异（均已评估）**

- `--ft-label-2` 比设计稿与 iOS 原生深（判断 1）；Hero 小卡多了一层暗纱（判断 3）；图表配色改为设计令牌（判断 11）——三处观感与设计稿有细微差异，验收阶段需对照。
- 「读屏适配」「Sheet 的 `inert` 与焦点陷阱」「Android 返回键先收起 Sheet」没有做（判断 14）。

**实现要点 / 发现**

1. **预算卡片「编辑」的热区被下一行盖住**（热区扫描量出来的真问题）：用负外边距把 15px 文字的点击区域扩到 44 × 44，多出来的 12px 压在下一行金额的行框下面，点在那 12px 上命中的是金额，实际可点高度只有 32px。给它 `position: relative; z-index: 1` 修好。
2. **金额光标在「减弱动态效果」下照闪**（静态扫描抓到）：`.ft-amountentry__caret` 的 reduce 规则写在基础规则**之前**，同优先级下基础规则的 `animation` 把它盖了。规则挪到基础规则之后，并新增单元测试解析 CSS 规则顺序守住（改回去会失败，做过变异验证）。
3. **MASA 自带的过渡在 reduce 下仍是 200–300 ms**：兜底第一版没加 `!important`，被 `.m-icon.m-icon` 叠类名的规则盖了；第二版用 `:not([class*="ft-"])` 排除自家组件，结果二级页面的工具栏（同时带 `m-` 与 `ft-` 类名）漏网。最终版本不排除任何类，只压 `transition-duration`，不碰 `animation`。
4. **次要文字与占位文字对比度不足**（判断 1）；**首页 Hero 小卡的白色说明文字只有约 4.3:1**（判断 3）；**「可编辑」徽标文字在浅绿底上只有约 4.1:1**（判断 8）：都由 S 节扫描或 `DesignTokensTests` 计算发现。
5. **热区扫描的假阳性**：账单 / 渠道等列表的行被滚动折线或吸底数字键盘切掉一截时，射线只能量到露出来的那段（36–44），误报为不达标；探针对不达标的元素当场滚到三个位置重量取最大并还原滚动位置（判断 13），Z 节 177 项全绿靠它。
6. **探针的 JS 字符串要经过 Python 的 `%` 格式化**：`HITS_JS` 的注释里出现「80%–100%」直接让模块导入失败（`TypeError: not enough arguments for format string`）。已在字符串开头写明「JS 与注释里都不能写百分号」。
7. **页面栈压栈要点真实入口**：`Blazor.navigateTo` 压不进页面栈（README 既有限制），扫描「页面入栈出栈」时点「我的 → 账本管理」的真实入口、等 `nav-back`、点返回、再等二级页面全部收起。
8. **读状态要等过渡落定**：多选勾选圈的填充色有淡入过渡，勾选后立刻读到的是中间值；先 `wait_animations_settled` 再读（与 21 号的发现同一类）。
9. **运动扫描里看到的 Toast 是预算超支提醒**（造的数据里有超支预算，首页加载时按既有逻辑弹一次，不是假云端的同步提示——扫描时假云端没起）；它的图标是 MASA 的 snackbar 图标，所以兜底要覆盖 MASA 组件。
10. **进程名**：E2E 的 Python 进程在这台机器上叫 `python3.13`，用 `Get-Process -Name python` 查不到；检查「是否有别的 E2E 在跑」要用 `python*`。

### 待人工验收（设备 / 模拟器）

以下只能在真机 / 模拟器上确认，**不能由 Web.Server 下的 E2E 代替**，所以验收清单的那一格保持未勾选：

- [ ] **Android 大字号**：系统「字体大小」调到最大、「显示大小」放大后，四个 Tab、记一笔 Sheet、各二级页面、登录页没有溢出 / 重叠 / 被裁，关键按钮仍可点（自动化只能用样式改写近似 130%，与真实 `fontScale` 不等价）；iOS 的「更大字体」（Dynamic Type）同理。
- [ ] **低端 Android 设备（或受限性能的模拟器）**：① 判据是否触发（低内存标记 / 总内存 < 3 GiB 的门槛是否合适）；② 降级为实色之后滚动、Tab 切换、Sheet 升起是否真的更流畅；③ 没触发降级的中端机上整屏玻璃是否仍流畅。
- [ ] **iOS（CI 产物）**：系统「降低透明度」「减弱动态效果」开关切换后应用即时跟随（`RenderingProfileService` 的状态变化通知）；原生实现是否真的随设置变化触发（Windows 上只验证了能编译）。
- [ ] **Android 系统「移除动画」是否被 WebView 映射成 `prefers-reduced-motion`**（取决于系统与 WebView 版本）；映射不上的话 reduce 路径在 Android 上不会生效，需要宿主像降级材质一样主动写一个根开关。
- [ ] **对比度抽查**：Hero 渐变上的白字、玻璃上的文字、次要文字与占位文字在真机屏幕、强光与低亮度下的可读性（自动扫描按样式与截图像素算，不含屏幕差异）。
- [ ] **读屏冒烟（不在本 ticket 范围，仅记录）**：VoiceOver / TalkBack 下的朗读顺序、Sheet / 语音覆盖层升起时底下页面仍可被焦点到达、Android 返回键先收起 Sheet（判断 14）。
- [ ] **判断项 1–15 的确认**（尤其 1、3、6、11、14）。
