# 02 — 沉浸式状态栏与安全区

**What to build:** App 在 Android 和 iOS 上内容铺满到屏幕最顶端：状态栏透明，不再有写死的紫色色带或白色空白条；刘海、灵动岛、手势条都不遮挡内容；状态栏的时间 / 电量图标颜色随页面顶部背景自动切换。网页端不受影响。本 ticket 先把「沉浸式能力」从原生一路打通到网页层。已有页面在迁移期保持可用可读：尚未迁移的紫色顶栏与底部导航补上安全区内边距，状态栏按深色背景显示浅色图标。

**Blocked by:** 01 — E2E 手机视口基座

**Status:** completed（自动化验收全部通过；设备 / 模拟器上的人工验收项待办，见下方「待人工验收」）

- [x] Android：状态栏与导航栏透明、页面铺满（edge-to-edge），移除写死的状态栏颜色；Android 15 及以上（系统强制全屏显示）与旧版本系统表现一致（实现完成并通过 Android 目标编译；设备表现见人工验收）
- [x] iOS：网页视图铺满屏幕并启用视口铺满语义，顶部不再露出空白条；系统环境变量可作为安全区的后备来源（实现完成并通过 iOS 目标编译；设备表现见人工验收）
- [x] 在 Web 共享库中定义「安全区」「状态栏样式」两项平台能力的接口：MAUI 宿主按平台实现（做法与现有语音、短信、通知读取一致，条件编译注册）；Web.Server 与其余平台使用空实现（安全区全为 0，状态栏样式无操作）
- [x] 网页层把安全区（上 / 下 / 左 / 右）写成文档根上的 CSS 变量；在尺寸、方向、系统键盘弹出 / 收起时随原生更新（Android 以原生注入为准）
- [x] 提供「页面声明顶部背景明暗」的公用方式（深色背景 → 浅色图标，浅色背景 → 深色图标），可随滚动更新，切换过渡约 200ms；该状态以文档根上的可观察属性对外暴露，原生宿主据此切换系统状态栏图标颜色
- [x] 迁移期兼容：尚未迁移的页面（紫色实心顶栏、旧底部导航、固定高度容器）补上安全区内边距，不被状态栏 / 手势条遮挡；默认声明「深色背景」以匹配紫色顶栏
- [x] 自动化验收（E2E，手机视口）：Web.Server 下安全区变量默认为 0，既有页面布局无偏移；把文档根上的变量改成非零（如上 47、下 34）后，顶栏内容整体下移、底部导航内容整体上移且不被遮挡；页面声明的状态栏样式变化时，根上的可观察属性随之变化；变量名、属性名与取值登记到 E2E 说明文档的「文档化可观察属性」小节
- [ ] 人工验收（**待人工**，需要设备 / 模拟器，见下方清单）：Android 模拟器（API 35 及一个更低版本，如 API 33 / 34）无紫色色带、状态栏图标清晰；有挖孔的设备内容不被遮挡；弹出系统键盘时底部安全区正确。iOS 通过 CI 产物在真机或模拟器上确认顶部无空白条、灵动岛区域不遮挡、状态栏图标颜色随页面切换
- [x] 回归：既有 E2E 与 `dotnet test` 全部通过，`dotnet build` 零错误（含 Android 目标）

## 实现说明

- 覆盖 spec 用户故事：12–19、105（安全区与状态栏样式部分）。
- 设计稿：02 · 沉浸式状态栏；06 · 设计规范。
- Android WebView 的 `env(safe-area-inset-*)` 不可靠，以原生注入为准；iOS 才用 `env()` 作后备；Web.Server 默认全 0。
- 状态栏图标颜色的"页面声明 → 根属性 → 原生切换"是本 ticket 定下的契约，07（首页 Hero）、22（登录页）等后续 ticket 直接复用。
- MAUI 页面不再由系统自动留出安全区；.NET 10 的 MAUI 安全区控制可参考 maui-safe-area 技能。
- iOS 构建只能在 CI 的 macOS 环境验证，iOS 人工验收通过 CI 产物完成。

### 完成记录（agent 实现）

**交付物**

| 层 | 文件 | 作用 |
|---|---|---|
| Web 库 | `Services/Immersive/{SafeAreaInsets,ISafeAreaService,IStatusBarService}.cs` | 两项平台能力的接口 + 空实现（`NoOpSafeAreaService` / `NoOpStatusBarService`） |
| Web 库 | `Services/Immersive/ImmersiveCoordinator.cs` | 连接原生与 JS 的 C# 一侧（初始同步、变化推送与去重、状态栏回调、释放） |
| Web 库 | `Components/ImmersiveHost.razor`（挂在 `Main.razor` 根部、`ErrorBoundary` 之外） | 全局唯一的无界面宿主组件 |
| Web 库 | `wwwroot/js/immersive.js` | 安全区注入（`setSafeArea` / `clearSafeArea`）与状态栏图标风格判定（采样 / 声明） |
| Web 库 | `wwwroot/css/app.css`（末尾新增一节） | `--ft-safe-*` 变量与「迁移期兼容层」 |
| MAUI | `Platforms/Android/{MainActivity.cs, Services/SafeAreaService.cs, Services/StatusBarService.cs}` | 边到边 + 透明系统栏；`WindowInsets`（系统栏 + 刘海 + 输入法）测量；`AppearanceLightStatusBars` |
| MAUI | `Platforms/iOS/Services/{SafeAreaService.cs, StatusBarService.cs}`、`MainPage.xaml(.cs)` | 关键窗口 `SafeAreaInsets` + 键盘通知；`OverrideUserInterfaceStyle`（200ms 动画块）；`ContentInsetAdjustmentBehavior.Never`；`SafeAreaEdges="None"` |
| MAUI / 宿主 | `MauiProgram.cs`、`Web.Server/Program.cs`、`index.html`、`_Layout.cshtml` | 条件编译注册 / 空实现注册；两处 viewport 加 `viewport-fit=cover` |
| 测试 | `tests/e2e/immersive_safe_area_test.py`（35 项）、`ImmersiveCoordinatorTests`（14 项 xUnit）、`FakeImmersiveHost.cs` | 见下 |

**契约（后续 ticket 直接使用）**

- 页面避让一律读 `--ft-safe-top|right|bottom|left`（`<html>` 上的 CSS 变量）。优先级：html 内联覆盖 → 原生注入 `--ft-native-safe-*` → `env(safe-area-inset-*)` → `0px`。
- 状态栏图标风格：`<html data-statusbar-icons="light|dark">`（`light` = 浅色图标，用于深色背景）。判定方法：对屏幕顶部像素（水平居中、y=1）做命中检测，沿命中栈取第一个「声明了 `data-statusbar-bg`」或「背景有效不透明度 ≥ 0.6」的元素；深（相对亮度 < 0.3）→ `light`，否则 `dark`；都没有 → `dark`。
- 页面声明：在状态栏下方的背景元素上写 `data-statusbar-bg="dark|light"`。**渐变 / 图片背景无法采样，必须声明**；声明元素必须可被命中检测（不要 `pointer-events: none`）。半透明遮罩（有效不透明度 < 0.6）与 `opacity: 0` 的元素不参与判定。
- 随滚动（任意滚动容器，捕获阶段）、DOM / 样式 / 声明变化、转场（`transitionrun` / `animationstart` 后逐帧判定 600ms）重新判定，用 rAF 合并到每帧最多一次。
- 变量与属性已登记到 `tests/e2e/README.md`「文档化可观察属性」。

**与 ticket 文字的差异（均已评估）**

1. **默认状态栏样式不写死「深色背景」**，改为自动采样。原因：ticket 要求「状态栏按深色背景显示浅色图标」是为了匹配紫色顶栏；但根页面（首页 / 统计 / 我的）顶部是白色、没有顶栏，一律浅色图标会在白底上看不清。采样方案下：紫色顶栏的二级页面 → 浅色图标（满足 ticket），白色根页面 → 深色图标（保持可读）。需要时页面仍可用 `data-statusbar-bg` 显式声明覆盖。
2. **`NoOpSafeAreaService.Current` 返回 `null` 而不是全 0**：`null` 表示「宿主没有原生安全区」，Web 层不写 `--ft-native-safe-*`，回落到 `env()`（iOS 浏览器 / Safari 里有真实值，其余为 0）。Web.Server 在桌面浏览器里的最终效果就是全 0，与 ticket 一致；多出的好处是 iOS Safari 访问网页端时也能避让刘海。
3. **「切换过渡约 200ms」**：iOS 的状态栏切换放进 200ms 动画块；**Android 系统不支持状态栏图标切换动画**，切换是即时的（系统限制，已在 `IStatusBarService` 文档中说明）。

**实现要点 / 发现**

- 键盘：Android 用 `WindowInsetsAnimationCompat` 回调（键盘动画结束）+ `GlobalLayout`（旋转 / 系统栏变化）；iOS 用 `UIKeyboard` 通知把键盘高度并入底部边距，旋转没有独立事件，靠 Web 层 `resize` 回调 `OnViewportChanged` 后重新读取。
- **iOS 目标可以在 Windows 上做编译级验证**：`dotnet build src/FinanceTracker.App -f net10.0-ios` 能编译出 `iossimulator` 程序集（只做不了打包 / 签名 / 运行）。后续所有触及 iOS 代码的 ticket 都应跑一次它，避免等到 CI 才发现 iOS 编译错误。
- PageStack **只响应它自己的 `Push`**：E2E 里直接 `Blazor.navigateTo('/xxx')` 只会改地址栏，页面栈不变；要进入二级页面必须点界面入口。
- 旧底部导航的高度有 CSS 过渡动画，E2E 读布局前要 `wait_animations_settled`。
- `server.ps1 -Action start` 的输出不要接管道（会因后台服务继承管道句柄而不返回），已写入 README 排错表。
- 迁移期兼容层（`app.css` 末尾「迁移期兼容层」一节，选择器依赖 PageStack / MASA 内部类名）与 E2E 脚本里标注【兼容层】的断言一起，**在 24 号 ticket 清理旧页面时整体删除**；其余契约（变量、属性、声明协议、`immersive.js`）长期保留。**（24 号已完成）**：兼容层整体删除，其中唯一仍有用的一条——二级页面内容区上内边距——升为 `ui.css` 的正式规则；`app.css` 只剩「禁止长按选择」与四个 `--ft-safe-*` 变量；E2E 里【兼容层】断言早已随最后一个旧二级页面迁移而删除。`VisualSystemGuardTests` 守住它们不会被加回来。
- 回归：`dotnet build FinanceTracker.sln` 0 警告 0 错误；`dotnet build -f net10.0-android` / `-f net10.0-ios` 各 0 警告 0 错误；`dotnet test` 430/430（新增 14）；`run_all.py` 6/6（新增 `immersive_safe_area_test.py` 35 项）。

### 待人工验收（设备 / 模拟器）

- [ ] Android API 35：无紫色色带，状态栏透明，深 / 浅页面间切换时图标颜色正确（切换即时）
- [ ] Android API 33 或 34：表现与 API 35 一致（顶栏下移、底部导航避开手势条 / 3 键导航栏，导航栏底部无灰带）
- [ ] Android 有挖孔 / 刘海的设备或模拟器（Cutout 配置）：横竖屏下内容不被遮挡
- [ ] Android 弹出系统键盘（如登录页输入手机号）：`--ft-safe-bottom` 随键盘变化，收起后恢复
- [ ] iOS（CI 产物，真机或模拟器，建议带灵动岛机型）：顶部无空白条，灵动岛区域不遮挡内容，进入 / 返回二级页面时状态栏图标颜色随之切换（约 200ms 过渡）
- [ ] iOS 横屏：左右安全区生效（本 ticket 的兼容层不使用左右边距，新页面各自处理）
