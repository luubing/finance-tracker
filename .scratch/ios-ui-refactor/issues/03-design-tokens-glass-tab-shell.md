# 03 — 设计令牌 + 悬浮玻璃 Tab 栏 + 四 Tab 壳

**What to build:** App 的外壳换成 iOS 液态玻璃风格：底部是悬浮玻璃 Tab 栏（首页 / 账单 / 统计 / 我的）和右侧独立的圆形「记一笔」按钮；「账单」成为独立 Tab。Tab 栏只在四个一级页面出现，进入二级页面或登录页后隐藏，再次点按当前 Tab 回到顶部。设计令牌（颜色、字号、圆角、间距、材质）作为统一的样式基础，两个宿主共用。各页面内容暂沿用现有页面，随后续 ticket 逐页替换。本 ticket 同时新增 ADR 0007。

**Blocked by:** 02 — 沉浸式状态栏与安全区

**Status:** completed（自动化验收全部通过；设备 / 模拟器上的人工验收项待办，见下方「待人工验收」）

- [x] 设计令牌以 CSS 自定义属性提供：品牌色 #4318FF 与渐变、页面底色、玻璃材质（浅色玻璃 / Hero 之上的深色玻璃）、圆角、字号层级、间距、动效参数、语义色；Web.Server 与 MAUI 两个宿主加载同一份；MASA 主题的 primary 与令牌品牌色保持同步；数值以设计稿 06 为准
- [x] 悬浮玻璃 Tab 栏（四个 Tab + 独立「记一笔」圆钮）：高 62、左右边距 16、下沿离屏幕底边 21（iPhone 上压住 Home 指示条所在的安全区，不再叠加安全区；键盘 / 三键导航栏等实心占用才再整个避开，见 ADR 0007 §29）；选中项图标由线框变实心并染品牌色，选中项底部有静态高光（滑动效果由 04 实现）；每屏同时叠加的玻璃层不超过 3 层
- [x] 「账单」成为 Tab 根页面并加入 Tab 保活规则；Tab 栏只在四个根页面显示，二级页面与登录页不显示；切走再切回，页面保持滚动位置与筛选状态
- [x] 再次点按当前 Tab：滚动到顶部；若处于该 Tab 的二级页面，先回到根页面
- [x] 「记一笔」圆钮在四个根页面可见，点按进入现有记账页（样式由后续 ticket 重做）
- [x] 旧的 3 项 Material 底部导航移除；「我的」里原指向账单列表的入口改为切换到「账单」Tab（不能把一个 Tab 根页面压栈）；一级页面底部留出 Tab 栏高度，最后一项内容可完整滚出 Tab 栏之上
- [x] 网页端宽屏（桌面浏览器）：应用壳与 Tab 栏限制最大宽度并居中，保持手机比例，不拉伸错位
- [x] 新增 ADR 0007（iOS 风格视觉体系：设计令牌 + 复用组件层 + 原生能力抽象，说明对 ADR 0001 风格描述的覆盖）；ADR 0001 中标注「风格描述被 ADR 0007 取代」，不得悄悄改写 0001 的原文
- [x] 自动化验收（E2E，手机视口）：Tab 栏出现四个 Tab 与「记一笔」；点按 Tab 后，文档化的「当前激活 Tab」属性随之变化并显示对应页面；进入二级页面（如「我的」→ 账本）Tab 栏隐藏，返回后恢复；再次点按当前 Tab 回顶；未登录时没有 Tab 栏；宽屏视口（如 1280×800）下 Tab 栏居中且宽度受限；既有 E2E 中依赖旧底部导航的步骤同步更新；「当前激活 Tab」属性登记到 E2E 说明文档
- [ ] 人工验收（**待人工**，需要设备 / 模拟器，见下方清单）：对照设计稿 01 与 03 的静态 Tab 栏外观（玻璃、圆角、位置：下沿离屏幕底约 21、在 Home 指示条之上且不盖住它）；Android 与 iOS（CI 产物）上玻璃模糊正常显示，Tab 栏不被手势条 / 导航栏遮挡
- [x] 回归：既有 E2E 与 `dotnet test` 全部通过，`dotnet build` 零错误

## 实现说明

- 覆盖 spec 用户故事：1–4、7–9、11、103、104（令牌与 Tab 栏部分）、107。
- 设计稿：01 · 四个 Tab 根页面；03 · Tab 切换效果；06 · 设计规范。
- 迁移采用 expand–contract：新壳与旧页面共存，旧的紫色顶栏仍在各页面内，等页面逐个迁移后由 24 统一清理。
- 玻璃表面一律通过令牌取值，让 23 能在一处切换「减弱透明度」回退。
- 保留 PageStack 的路由栈与 Tab 常驻能力，只替换视觉层；「账单」需要加入 Tab 规则才能保活。
- 图标继续使用 MDI 成对的线框 / 实心图标；新增图标必须存在于 MDI 7.2.96。
- 滑块动效与触感由 04 负责，本 ticket 的高光是静态的。

### 完成记录（agent 实现）

**交付物**

| 层 | 文件 | 作用 |
|---|---|---|
| 样式 | `src/FinanceTracker.Web/wwwroot/css/tokens.css` | 全部设计令牌（`--ft-*`）：品牌色 / 渐变 / 语义色 / iOS 调色 / 中性色 / 玻璃材质（浅、深、滑块）/ 字体与字号层级 / 圆角 / 间距与尺寸（Tab 栏 62、边距 16、距底 21、圆钮 62、间距 10、`--ft-tabbar-clearance`）/ 动效时长、曲线与弹簧参数 / 层级 |
| 样式 | `wwwroot/css/ui.css` | 应用壳（页面底色、宽屏居中 ≤ 480）、玻璃材质类、Tab 栏与「记一笔」圆钮、隐藏过渡、`prefers-reduced-motion` 降级 |
| 样式 | `wwwroot/css/app.css`（兼容层一节） | 根页面容器：顶部让出状态栏、底部让出 Tab 栏（`--ft-tabbar-clearance`），移除旧底部导航的规则 |
| 脚本 | `wwwroot/js/tabBar.js` | 「再点当前 Tab 回顶」：定位当前可见根页面的滚动容器并平滑回顶（减弱动态时瞬回） |
| 组件 | `Components/GlassTabBar.razor`（挂在 `MainLayout`） | 四个 Tab（`role="tab"`）+「记一笔」圆钮；`data-testid="tab-bar"` / `data-active-tab`；二级页面与未登录时隐藏 |
| 逻辑 | `Shell/TabNavigation.cs` | Tab 定义（键 / 标题 / 根路径 / 线框与实心图标）、页面栈的 `TabRule` 集合（四个根路径全部常驻）、路径归一化、点按决策 `PlanTap`（切换 / 回顶 / 回根） |
| 逻辑 | `Design/DesignTokens.cs` | 品牌色等 C# 常量；`UseFinanceTrackerDesign()` 一处设置 MASA 主题与组件默认值，两个宿主共用 |
| 布局 | `Layouts/MainLayout.razor` | 去掉旧 `MBottomNavigation`，改为 `PPageStack`（`TabRules` 取自 `TabNavigation`）+ `GlassTabBar` |
| 宿主 | `server/.../_Layout.cshtml`、`src/FinanceTracker.App/wwwroot/index.html`、两处 `Program.cs` / `MauiProgram.cs` | 同序加载 `tokens.css` → `app.css` → `ui.css`；统一调用 `UseFinanceTrackerDesign()` |
| 页面 | `Pages/Index.razor`、`Pages/Profile.razor`、`Pages/Bills.razor` | 首页「账单」入口、我的「账单列表」入口改为 `NavigateTo("/bills")`（切 Tab，不压栈）；账单页的顶栏操作暂挪进页面内（Tab 根页面不渲染 `PStackPageBar`），08 / 09 重做时替换 |
| 测试 | `tests/e2e/tab_shell_test.py`（64 项）、`TabNavigationTests` + `DesignTokensTests`（56 项 xUnit）、`e2e_base.py` 新 helper、`immersive_safe_area_test.py` 兼容层断言改量 Tab 栏 | 见下 |
| 文档 | `docs/adr/0007-ios-visual-system.md`、`docs/adr/0001-technology-stack.md`（仅追加标注）、`tests/e2e/README.md` | ADR、可观察属性登记、helper 说明 |

**契约（后续 ticket 直接使用）**

- 样式只引用 `--ft-*` 令牌；玻璃表面只用 `.ft-glass` / `--ft-glass-*`（23 号在令牌层一处做「降低透明度」回退）；`--ft-brand` = C# `DesignTokens.BrandColor` = MASA 主题 primary，由 `DesignTokensTests` 守住。
- Tab ↔ 路径的对应关系只在 `TabNavigation` 里维护；**切换 Tab 一律 `NavigationManager.NavigateTo(根路径)`，不得 `Push` Tab 根路径**（`TabNavigationTests` 扫描 Razor 页面守住）；回到某 Tab 根页面用 `PageStackNavController.GoBackToTab(根路径)`。
- 新做的 Tab 根页面把滚动容器标记 `data-scroll-root`（旧页面退回根页面里的第一层 `.container`）；内容区底部预留 `--ft-tabbar-clearance`。
- E2E：Tab 根页面常驻 DOM（切走只加 `d-none`），点按页面内容用 `active_root(page)` 限定范围；首次进入页面后先 `wait_scroll_root_stable` 再设置滚动位置 / 量布局。`data-testid="tab-bar"`、`data-active-tab`、`data-testid="tab-bar-add"`、`data-scroll-root`、`--ft-*` 已登记到 `tests/e2e/README.md`。

**与 ticket 文字的差异（均已评估）**

1. **Tab 栏在客户端确认已登录之后才显示**：预渲染阶段读不到 localStorage，若按路径直接显示，未登录用户进入 `/` 时会闪一下 Tab 栏再被重定向到登录页。「未登录没有 Tab 栏」因此更严格（登录页上元素根本不存在）。
2. **账单页暂时自带一条页内操作栏**：Tab 根页面没有 `PStackPageBar`，原来挂在顶栏上的「记一笔 / 刷新 / 批量选择」暂时放进页面内，保证旧功能不丢；08 / 09 重做账单页时整体替换。
3. **宽屏下应用壳加 `transform: translateZ(0)` 与 `overflow-x: clip`**：前者让页面栈的 `position: fixed` 页面、Tab 栏、弹层都以壳为包含块（不会撑满整个窗口），后者裁掉压栈时向左错开的下层页面（E2E 用壳两侧点位命中检测守住，去掉 `clip` 时会失败）。

**实现要点 / 发现**

- Chromium 在 `display: none` 期间保留元素的滚动偏移，所以「Tab 根页面常驻 + 切走只加 `d-none`」天然保留滚动位置；Android WebView / iOS WKWebView 的行为未验证，列入人工验收。
- 页面栈只响应它自己的 `Push` 与 `NavigateTo`（对 Tab 路径），切换 Tab 用 `NavigateTo` 才会同步显示根页面；首次进入某个 Tab 时创建页面比地址变化晚一拍，E2E 要等「可见根页面换成新页面」而不是只等地址。
- `Uri.TryCreate("/bills?x=1", UriKind.Absolute)` 在类 Unix 运行时上成功（被当作文件路径），会吃掉查询串，所以路径归一化只把 `http` / `https` 当绝对地址。
- 回归：`dotnet build FinanceTracker.sln` 0 警告 0 错误；`dotnet build -f net10.0-android` / `-f net10.0-ios` 各 0 警告 0 错误；`dotnet test` 486/486（新增 56）；`run_all.py` 7/7（新增 `tab_shell_test.py` 64 项，`immersive_safe_area_test.py` 35 项仍全绿）。

### 待人工验收（设备 / 模拟器）

- [ ] 对照设计稿 01 / 03 的静态 Tab 栏：玻璃质感、圆角（Tab 栏 31、圆钮正圆）、位置（下沿离屏幕底 21、左右 16）、选中项实心图标与高光
- [ ] Android（API 35 与一个更低版本）：玻璃模糊正常显示（`backdrop-filter` 在 WebView 上生效）；Tab 栏不被手势条 / 3 键导航栏遮挡；中低端机上滚动与切换 Tab 不明显掉帧
- [ ] iOS（CI 产物，真机或模拟器，建议带灵动岛机型）：玻璃模糊正常显示；Tab 栏停在手势条之上、不被遮挡
- [ ] Android / iOS：切走再切回「我的」「账单」，WebView 里滚动位置与筛选状态保持
- [ ] Android / iOS：在二级页面（如「我的」→「账本管理」）按系统返回键 / 侧滑返回，Tab 栏恢复显示且所属 Tab 高亮正确
