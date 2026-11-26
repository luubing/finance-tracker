# 04 — Tab 滑块切换效果

**What to build:** 切换 Tab 时有 App Store 那样的玻璃滑块：按下时选中项的高光"抬起"；高光沿水平方向被拉长地滑到目标 Tab，越过后轻微回弹；图标由线框变实心；页面内容交叉淡入；带触感反馈。系统开启「减弱动态效果」时只剩简短淡入。向下滚动后 Tab 栏收缩成"当前页胶囊"。

**Blocked by:** 03 — 设计令牌 + 悬浮玻璃 Tab 栏 + 四 Tab 壳

**Status:** completed（自动化验收全部通过；设备 / 模拟器上的人工验收项待办，见下方「待人工验收」）

- [x] 高光滑块由弹簧动力学驱动（响应约 0.42s、阻尼比约 0.62）；水平拉伸倍数随速度增加，最大约 1.7×，垂直方向压缩不超过 7%
- [x] 按下时高光放大约 1.08 并加深投影、图标缩至约 0.9（约 120ms）；图标线框 → 实心约 160ms；页面交叉淡入约 180ms（目标页由约 0.985 轻微放大到 1）
- [x] 连续点按多个 Tab 时以最后一次为准：动画可被打断，高光与页面最终停在最后点按的 Tab，无残留状态
- [x] 点按触感通过共享库中的触感接口抽象（做法同 02 的平台能力）：iOS 轻触反馈、Android 时钟刻度反馈；Web.Server 与其余平台使用空实现
- [x] 向下滚动超过约 24pt 时 Tab 栏收缩为「当前页胶囊」，向上滚动或点按时展开；「记一笔」圆钮始终可见；按下「记一笔」缩放约 0.92，松手后进入记账页
- [x] 减弱动态效果：不做弹簧与拉伸，仅约 100ms 淡入；页面保活行为不变
- [x] 自动化验收（E2E，手机视口）：切换后滑块最终落在目标 Tab 的位置；切换过程中采样到滑块宽度大于静止宽度（出现拉伸）；开启减弱动态效果模拟后全程无拉伸；快速连点多个 Tab 后最终激活的是最后点按的 Tab；滚动超过阈值后 Tab 栏收缩、上滚后展开且「记一笔」可点；滑块位置 / 拉伸的可观察方式登记到 E2E 说明文档
- [ ] 人工验收（**待人工**，需要设备 / 模拟器，见下方清单）：真机或模拟器上对照设计稿 03，感受弹簧手感、拉伸幅度与回弹；触感在 iOS 与 Android 真机上生效；中端 Android 设备上切换与滚动保持流畅（无明显掉帧）
- [x] 回归：既有 E2E 与 `dotnet test` 全部通过，`dotnet build` 零错误

## 实现说明

- 覆盖 spec 用户故事：20–27、105（触感部分）。
- 设计稿：03 · Tab 切换效果。
- 动效参数放进设计令牌，方便统一调整。
- 玻璃层数每屏不超过 3 层的约束对滑块同样适用；减弱动态效果的全局核查由 23 统一完成，本 ticket 只保证 Tab 栏自身降级正确。

### 完成记录（agent 实现）

**交付物**

| 层 | 文件 | 作用 |
|---|---|---|
| 令牌 | `wwwroot/css/tokens.css` | 动效参数：`--ft-spring-response/damping`（.42 / .62）、`--ft-stretch-max`（.7）、`--ft-squash-max`（.07）、`--ft-lift-scale`（1.08）、`--ft-lift-icon-scale`（.9）、`--ft-fab-press-scale`（.92）、`--ft-collapse-threshold`（24）、`--ft-tabbar-capsule-width`（124px）、`--ft-glass-thumb-shadow-lifted`、时长 120 / 160 / 180 / 100ms |
| 脚本 | `wwwroot/js/tabBar.js` | `attachTabBar(wrap, index)` → `{ sync, dispose }`：弹簧数值积分驱动高光（亚步长半隐式欧拉，单帧时长上限 50ms）、水平拉伸与垂直压缩、按下抬起、点按乐观移动与迟到确认处理、滚动收缩、减弱动态效果分支；原有 `scrollActiveRootToTop` 保留 |
| 样式 | `wwwroot/css/ui.css` | 高光改用独立属性 `translate` / `scale`（scale 绕自身中心、translate 在最外层，两者互不干扰，且不加 CSS transition 以免与脚本逐帧写入相互拖拽）；按下 / 收缩 / 胶囊 / 圆钮按下缩放；页面进入动画 `ft-page-enter` / `ft-page-fade`；减弱动态效果下的降级规则 |
| 组件 | `Components/GlassTabBar.razor` | 增加胶囊按钮、`data-testid="tab-bar-thumb"`；每次渲染后 `sync(index)`；点按时调用触感；脚本写的状态不进渲染树 |
| 接口 | `Services/Haptics/IHapticFeedbackService.cs` | 共享库里的触感抽象 + `NoOpHapticFeedbackService` |
| 平台 | `FinanceTracker.App/Platforms/Android/Services/HapticFeedbackService.cs`、`Platforms/iOS/Services/HapticFeedbackService.cs` | Android：视图级 `PerformHapticFeedback(CLOCK_TICK)`（不需要震动权限）；iOS：`UIImpactFeedbackGenerator(.light)`，复用实例并 `Prepare()` |
| 宿主 | `MauiProgram.cs`、`server/.../Program.cs` | MAUI 按平台注册（Android / iOS 原生、其余空实现）；Web.Server 注册空实现 |
| 测试 | `tests/e2e/tab_slider_test.py`（82 项）、`HapticFeedbackTests`（6 项）、`DesignTokensTests` 新增动效令牌（15 项）、`e2e_base.py` 新 helper | 见下 |
| 文档 | `docs/adr/0007-ios-visual-system.md`（新增第 6 节「Tab 滑块动效」）、`tests/e2e/README.md`（脚本、helper、5 组可观察属性） | 架构决策与可观察属性登记 |

**契约（后续 ticket 直接使用）**

- 脚本写的状态（`data-collapsed` / `data-pressed` / `data-pressing`、高光的 `data-state` 与内联 `translate` / `scale`）一律**不由 Razor 渲染**，Blazor 的差量更新不会冲掉它们；新增脚本状态沿用这个办法。
- **脚本接管后，高光位置的唯一来源是内联 `translate`**；`--ft-tab-x` 只是预渲染 / 脚本加载失败时的回退。别再在 CSS 里依赖 `--ft-tab-x` 决定高光位置。
- **向下滚动超过 24 后 Tab 栏收缩，四个 Tab 是 `inert`**：E2E 里用脚本把页面滚下去再点 Tab（或点「再次点按当前 Tab 回顶」）之前必须 `expand_tab_bar(page)`；`open_tab` 已内置。
- 页面进入动画落在 `.p-page-container__item:not(.d-none)` 上（只有 Tab 根页面，压栈的二级页面不是这个类，不受影响）。动画的 180ms 内根页面是 `transform` 的包含块：放在 Tab 根页面里的 `position: fixed` 元素会跟着错位——需要钉在屏幕上的元素放在页面栈之外或用 `position: sticky`。
- 测试 helper：`thumb_box` / `tab_box` / `wait_thumb_idle` / `wait_thumb_at` / `start_thumb_sampler` + `stop_thumb_sampler` / `tab_collapsed` / `expand_tab_bar`（见 `tests/e2e/README.md`）。**Blazor 触发的跳转（非点按）要等 `sync` 往返后高光才开始运动，只看 `idle` 会误判已到位，用 `wait_thumb_at`。**
- 其它页面里要做类似的「弹簧」动效时，复用 `--ft-spring-*` 令牌与 `unitStepPeakSpeed` 的思路（未抽成公共模块——目前只有 Tab 栏一处使用，等第二处出现再提）。

**与 ticket 文字的差异（均已评估）**

1. **弹簧由脚本数值积分，而不是 CSS 缓动曲线**：设计稿 03 里给的 `cubic-bezier(.34,1.45,.52,1)` 只是弹簧曲线的近似，而「动画可被打断（保留速度继续）」与「拉伸倍数随实时速度」都需要真实速度，CSS transition 给不出（ADR 0007 第 6 节）。
2. **点按乐观响应**：高光在客户端立刻开始动画，不等 Blazor Server 往返；Blazor 的确认（`sync`）与最后一次点按一致才算数，更早那次点按的迟到确认被忽略（否则连点时高光会折返），2.5s 仍无确认（跳转没发生）回到真实 Tab。这三条行为用「Tab 栏副本 + 同一份脚本」在页面里精确控制 `sync` 的次序验证（真实连点在本机往返只有几毫秒，复现不了「旧确认在下一次点按之后才到」）；把「迟到确认被忽略」的逻辑去掉后对应检查会失败（已做变异验证）。
3. **「记一笔」靠右**：设计稿 03 右下角「Tab 栏的三种状态」明确圆钮始终在右侧；Tab 栏缩成胶囊后用 `margin-left: auto` 让圆钮留在原位（展开时没有剩余空间，不起作用）。
4. **收缩态下四个 Tab 不可点**（`inert`），要先点胶囊或向上滚动展开——这是设计稿「上滑或点按展开」的直接结果，但它改变了 E2E 的写法（见契约）。`tab_shell_test.py` 里「回顶」一步已相应加了 `expand_tab_bar`。

**实现要点 / 发现**

- 弹簧参数：`response 0.42s / damping 0.62` → 过冲约 8.4%（实测：2 格 10.8px / 1 格 5.4px，各占距离 8%），约 450ms 停稳（实测运动时长 0.38–0.40s 内进入 ±1px）；拉伸 = `1 + 0.7 × |v| / 本次移动的峰值速度`，峰值速度用同一套积分数值求得（改令牌不用手算），实测最宽 1.70× / 最矮 0.930×，与设计一致。
- **一个差点漏掉的闪烁**：最初的设计是「静止后移除内联位置、回到 CSS 的 `--ft-tab-x`」。但代码触发的跳转（首页「账单」入口）是「Blazor 渲染先到、`sync` 后到」，渲染把 `--ft-tab-x` 改成新位置的那一刻高光会先瞬移过去、再被脚本拉回旧位置重新滑动（生产环境往返几十到几百毫秒，肉眼可见）。改成脚本接管后始终保留内联 `translate`，E2E 在副本里模拟「渲染先到」并断言高光不会瞬移。
- `tab_shell_test.py` / `immersive_safe_area_test.py` 里的既有断言不需要为滑块改动，除上面第 4 条。`open_tab` 内置展开后，其余脚本零改动。
- iOS 的 `UIImpactFeedbackGenerator()` 无参构造自 iOS 17.5 起标记为过时（推荐关联视图，用于多窗口 / 外接屏的触感路由）。本应用单窗口、最低版本早于 17.5，沿用旧构造并在调用点局部 `#pragma warning disable CA1422` 说明原因，不为一次轻触引入版本分支；构建保持 0 警告。
- `FeedbackConstants.ClockTick` 需要 API 级别 ≥ 21 的 `View.PerformHapticFeedback`，受系统「触摸反馈」开关控制，不需要 `VIBRATE` 权限。
- 回归：`dotnet build FinanceTracker.sln` 0 警告 0 错误；`-f net10.0-android` / `-f net10.0-ios` 各 0 警告 0 错误；`dotnet test` 506/506（新增 20）；`run_all.py` 8/8（新增 `tab_slider_test.py` 82 项，连跑 4 次无波动；`tab_shell_test.py` 64 项、`immersive_safe_area_test.py` 35 项仍全绿）。

### 待人工验收（设备 / 模拟器）

- [ ] 对照设计稿 03 的分镜（0 / 30 / 70 / 110 / 160 / 250 / 450ms）感受弹簧手感：加速、拉伸幅度（最大约 1.7×）、越过目标后约 8% 的回弹是否自然；必要时只改令牌（`--ft-spring-response/damping`、`--ft-stretch-max`）调整，不改代码
- [ ] iOS 真机：点按 Tab 松手瞬间有轻触反馈；Android 真机：有时钟刻度反馈；系统关闭「触摸反馈」/ 触感时不报错、不卡顿
- [ ] 中端 Android 真机：切换 Tab 与向下 / 向上滚动（收缩 / 展开）无明显掉帧；`backdrop-filter` 的玻璃叠加弹簧动画下仍然流畅
- [ ] 系统开启「减弱动态效果」后：iOS / Android 上高光不再弹动，页面只淡入；Tab 栏收缩 / 展开仍可用
- [ ] 向下滚动后 Tab 栏收缩成胶囊的外观对照设计稿 03 右下角三种状态：胶囊显示当前页图标与名称、「记一笔」留在右侧；按下「记一笔」缩放 0.92，松手进入记账页
- [ ] WebView 里页面交叉淡入（0.985 → 1，180ms）不出现闪白 / 闪烁；动画期间根页面里的 `position: fixed` 元素错位在目前页面里不可见（07 号起新做的页面若出现钉住元素需复核）
