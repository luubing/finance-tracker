# 05 — 二级页面模板 + 账本管理

**What to build:** 二级页面的统一模板首次落地，并用账本管理页验证：玻璃导航栏（返回 + 内联标题 + 右侧操作）、分组圆角列表（图标块、副标题、右值、徽标、箭头）、左滑操作行、底部 Sheet（新建 / 编辑表单）、玻璃 Toast / Alert / 操作菜单。账本页迁移后分三段：共享账本邀请卡（接受 / 拒绝）、我的账本（「默认」「共享」徽标）、加入的共享账本（角色标记）。新建入口在右上角「＋」，新建 / 编辑用底部 Sheet，删除有 Alert 确认。每个左滑操作都有不依赖滑动的等价入口。账本的全部现有功能保持不变。

**Blocked by:** 03 — 设计令牌 + 悬浮玻璃 Tab 栏 + 四 Tab 壳

**Status:** completed（自动化验收全部通过；设备 / 模拟器与云端环境上的人工验收项待办，见下方「待人工验收」）

- [x] 开工前先列出现有账本页的全部功能点并写入本 ticket 的实现说明，迁移后逐项对照（至少含：新建、编辑、删除、设为默认、共享账本邀请的接受 / 拒绝、成员入口、「我的」与「共享」的区分及角色标记）——见「实现说明 · 现有功能点对照」
- [x] 新建复用组件：玻璃按钮、玻璃导航栏、分组列表 / 单元格、左滑行、底部 Sheet（半屏）、玻璃 Toast、Alert、操作菜单；组件只暴露内容插槽与事件，不内嵌业务逻辑
- [x] Toast 通过全局样式统一外观（现有 PopupService 的调用方式不变）；账本页里原有的 MDialog 确认框与表单对话框换成 Alert / Sheet
- [x] 账本页三段 + 右上角「＋」+ 徽标；左滑出现操作（如成员入口 / 删除），并有等价的非滑动入口（点按行进入含删除 / 设为默认的编辑 Sheet）
- [x] 保存 / 删除 / 设为默认的服务层校验与权限语义不变（401 与 403 区分，无权操作不会误登出）
- [x] 自动化验收（E2E，手机视口）：基座的「创建账本」helper 改走新界面；用例覆盖：新建（Sheet）→ 出现在列表 → 编辑名称 → 设为默认（出现「默认」徽标）→ 删除（Alert 确认）→ 消失；左滑（触摸手势）与非滑动入口各验证一遍
- [x] 共享账本邀请的接受 / 拒绝依赖云端：云端环境可用时自动化或人工覆盖，不可用时人工验收并记录（06 号 ticket 补齐：E2E 基座新增**假云端**——`tests/e2e/fake_cloud.py`，Web.Server 由 `server.ps1` 指向它——于是「接受 → 邀请卡消失、账本出现在『加入的共享账本』」「拒绝 → 邀请卡消失」「云端业务错误 / 5xx / 不可用 → 提示 + 邀请卡保留 + 不登出」都已自动化，见 `ledgers_page_test.py` H 段；假云端 ≠ 真实 Api + PostgreSQL，真实联调仍列在下方「待人工验收」）
- [ ] 人工验收（**待人工**，需要设备 / 模拟器，见下方清单）：对照设计稿 05 与 screens 的 08-ledgers，在 Android 与 iOS（CI 产物）上确认左滑手感、Sheet 弹起 / 收起、Alert 外观
- [x] 回归：既有 E2E 与 `dotnet test` 全部通过，`dotnet build` 零错误

## 实现说明

- 覆盖 spec 用户故事：81、82、94、95、104（模板组件部分）。
- 设计稿：05 · 二级页面与弹层；screens 的 08-ledgers。
- 每个组件在第一个用到它的 ticket 里创建，后续 ticket 复用；本 ticket 只创建账本页确实用到的组件。分段控件、大标题页等留到首次使用的 ticket 再建。
- 必须遵守 ADR 0004（共享账本权限）；服务层权限错误一律走 403 语义（ForbiddenAccessException），界面不得因此触发重新登录。
- 术语：界面与代码使用「账本」，不用「账簿」「笔记本」。

### 现有功能点对照（迁移前列出、迁移后逐项核对）

| 迁移前账本页的功能 | 迁移后的位置 | E2E 段（`ledgers_page_test.py`） |
|---|---|---|
| 新建账本（名称必填、≤ 50 字、图标、排序） | 右上角「＋」→ 新建 Sheet；没有任何账本时空状态里也有「新建账本」主按钮 | A、B |
| 编辑账本（名称 / 图标 / 排序） | 点按「我的账本」整行 → 编辑 Sheet；列表按排序数字重排 | C |
| 删除账本（确认；账本下有账单时拦截；软删除） | 左滑「删除」，或编辑 Sheet 里的「删除账本」→ Alert 确认；拦截 / 失败提示走 Toast | E、F |
| 设为默认账本 / 取消默认 | 编辑 Sheet 里的开关；列表「默认」徽标，同一时刻只有一个 | D |
| 共享：邀请成员入口 → 成员页 | 左滑「共享」，或 Sheet 里的「共享账本」→ 操作菜单「邀请成员共享」→ 成员页；加入的共享账本行点按 → 成员页 | E、G、H |
| 复制账本信息（分享文案） | 同一个操作菜单里的「复制账本信息」 | G |
| 共享账本邀请：接受 / 拒绝 | 页面顶部邀请卡（每个邀请一张）；云端不可用时提示 + 卡片保留 + 不登出 | H |
| 「我的」与「共享」的区分、角色标记 | 三段：邀请卡 / 我的账本（「默认」「共享」徽标）/ 加入的共享账本（所有者 · 可编辑 · 只读徽标） | H |
| 副标题：账单数、成员数、所有者手机号（脱敏） | 「N 笔账单」「N 位成员 · 你是所有者」「所有者 138\*\*\*\*0000」（取不到时「所有者 未知用户」） | B、H |
| 从成员页返回后刷新成员数 / 共享状态 | 沿用 `PageClosed` 事件 → `LoadSharedDataAsync` | G |
| 云端不可用不阻塞本地列表（先渲染本地、再加载共享数据，失败回退本地缓存） | 沿用：先 `_isLoading = false` 渲染本地，再加载共享，失败给警告 Toast | H |
| 操作结果提示（成功 / 失败 / 警告） | `PopupService.EnqueueSnackbarAsync` 调用方式不变，Toast 的外观由全局样式统一 | B |

### 完成记录（agent 实现）

**交付物**

| 层 | 文件 | 作用 |
|---|---|---|
| 令牌 | `wwwroot/css/tokens.css` | 二级页面 / 弹层令牌：`--ft-nav-height`（44）/ `--ft-nav-fade`（14）/ `--ft-hit`（44）/ `--ft-cell-*` / `--ft-tile-*`（图标块与 7 种渐变）/ `--ft-swipe-action-width`（76）/ `--ft-scrim` / `--ft-alert-*` / `--ft-toast-bottom` / `--ft-z-sheet|alert|toast`（120 / 130 / 1100） |
| 样式 | `wwwroot/css/ui.css` | 玻璃按钮、导航栏 + 页面外壳、分组列表 / 单元格 / 徽标 / 卡片 / 表单行 / 骨架屏 / 空状态、左滑行、Sheet、Alert、操作菜单、Toast 玻璃胶囊、减弱动态效果降级 |
| 脚本 | `wwwroot/js/ui.js` | 文档级事件委托：左滑行（方向锁定 10px、松手速度续滑 120ms 判定、350ms 内吞 click、托盘 `inert`）与 Sheet 拖拽收起；`install` / `uninstall` 由 `UiRuntime` 调用 |
| 组件 | `Components/Ui/*`（17 个文件） | `GlassButton`、`GlassNavBar`、`SecondaryPage`、`GroupedList`、`ListCell`、`Badge`、`SwipeRow` + `SwipeAction`、`OverlayBase`（Sheet / Alert / 操作菜单共用的挂载 · 退出动画 · 焦点 · Esc 逻辑）、`BottomSheet`、`GlassAlert`、`ActionSheet` + `ActionSheetItem`、`ToggleSwitch`、`IconPicker`、`EmptyState`、`UiEnums.cs`；`Components/UiRuntime.razor`（在 `Main.razor` 根部装一份行为层） |
| 页面 | `Pages/Ledgers.razor` | 重写为 `SecondaryPage` + 三段 + Sheet / Alert / ActionSheet；只调用既有服务（`ILedgerService` / `ILedgerMemberService` / `IUserDefaultService` / `IBillService`），**零服务层改动** |
| 模型 | `Models/LedgerPresentation.cs` | 账本页的纯展示逻辑（名称校验、图标集合与归一、图标块配色、各段副标题、角色文案、邀请文案、手机号脱敏），与界面解耦以便单测 |
| 测试 | `tests/e2e/ledgers_page_test.py`（本 ticket 完成时 134 项 + 1 项跳过；06 号 ticket 接入假云端后为 147 项、无跳过）、`LedgerPresentationTests`（37 项）、`DesignTokensTests` 新增 15 项（二级页面 / 弹层令牌取值、层级先后关系、分隔线缩进、不使用 iOS 14 不支持的特性、组件样式引用的令牌都有定义） | 见下 |
| E2E 基座 | `tests/e2e/e2e_base.py`、`e2e_base_test.py`（62 项） | `create_ledger` 改走新界面；`LocalDb` 新增 `seed_user` / `seed_ledger_member` / `ledger_row` / `default_ledger_id` / `ledger_members`；角色 / 状态常量；`swipe_by` 的 `release_hold_ms`；`REQUIRED_SCHEMA` 增补成员表 |
| 文档 | `docs/adr/0007-ios-visual-system.md`（新增第 7 节「二级页面模板、弹层与 Toast」）、`tests/e2e/README.md`（脚本、helper、可观察属性登记、已知限制） | 架构决策与可观察属性登记 |

**契约（后续 ticket 直接使用）**

- **二级页面一律用 `<SecondaryPage Title="…" TestId="xxx-page"><Actions>…</Actions>…</SecondaryPage>`**：玻璃导航栏、状态栏安全区、页面背景都由它负责，页面不再自己画顶栏。`Actions` 放右上角的玻璃按钮（`GlassButton Round`）。
- **弹层只用 `@bind-Visible`**：`BottomSheet` / `GlassAlert` / `ActionSheet` 的显示状态由页面持有，关闭动画结束后才触发 `OnClosed`——**页面的表单 / 目标对象清理放在 `OnClosed`**，不要放在「点关闭」的那一刻（Alert 的 `OnConfirm` 先于 `OnClosed`，此时目标对象仍可用）。保存中把 `Dismissible` 设为 `false`。Sheet 打开后焦点在容器上，**不要自动聚焦输入框**（移动端会直接弹出键盘）。
- **每个 `SwipeRow` 都要配等价的非滑动入口**（点按行进编辑 Sheet，里面含删除 / 设为默认等）；`SwipeRow` 把多余属性透传到根节点，业务属性（如 `data-ledger-id`）写在它上面。
- **组合约定**：`ListCell`（`Icon` + `Tone` / `Leading`、`Title`、`Subtitle`、`Value`、`Badges`、`Trailing`、`Chevron`、`Destructive`、`OnClick`）放进 `GroupedList`（`Header` / `Footer`）；开关用 `ToggleSwitch`（`role="switch"` + `aria-checked`）放在 `ListCell` 的 `Trailing` 里；图标选择用 `IconPicker`。
- **Toast 不换调用方式**：继续 `PopupService.EnqueueSnackbarAsync(消息, AlertTypes.xxx)`；外观与位置由全局样式统一（位置恒在 Tab 栏之上）。
- **E2E**：造账本用 `create_ledger`（经新界面）；造「别人的账本 / 我加入的共享账本 / 待处理邀请」用 `seed_user` + `seed_ledger_member`；左滑用 `swipe_by(page, 目标, -170, 0, release_hold_ms=150)`，**带速度松手会让 Chromium 吞掉下一次 `tap` 的 click**（见 README），之后要点按就停住再松手；等 Sheet / Alert 消失用 `wait_for(state="detached")`，退出动画期间遮罩仍挡触摸。

**与 ticket 文字的差异（均已评估）**

1. **点按整行的去向与设计稿 08 的脚注不同**：设计稿 08 底部脚注写「点按进入成员管理」，而 spec 的 95 号用户故事与本 ticket 明确要求「点按行进入含删除 / 设为默认的编辑 Sheet」（这是左滑操作的等价入口）。按 spec / ticket 执行：点按「我的账本」行进编辑 Sheet，成员页经左滑「共享」或 Sheet 里的「共享账本」→「邀请成员共享」到达；「加入的共享账本」行点按进成员页（没有编辑权限，不存在编辑入口）。页面脚注相应写成「左滑账本可共享或删除，点按可编辑、设为默认」。
2. **Toast 位置恒在 Tab 栏之上**：二级页面没有 Tab 栏，Toast 因此显得略高。设计稿没有给出「二级页面上 Toast 的位置」，所以取全局一致的方案（只维护一个 `--ft-toast-bottom`，Toast 不随页面类型跳动）；若人工验收觉得过高，只改这一个令牌即可。
3. **名称输入框打开 Sheet 时不自动聚焦**：设计稿 05 没有要求；自动聚焦会让移动端一打开 Sheet 就弹出键盘，遮住表单下半部分。

**实现要点 / 发现**

- **MASA 特异性陷阱**：页面栈顶栏的背景由 `.m-app-bar.m-toolbar.m-sheet`（三个类）设置，只写 `.ft-subbar` 会静默失效；覆盖规则要写成 `.m-app-bar.m-toolbar.m-page-stack-item__bar.ft-subbar`。Toast 同理：MASA 的消息容器带内联 `bottom: 0` / `max-width`、颜色类带 `!important`，覆盖需要 `!important`。
- **二级页面的背景色不在对话框上**：页面栈二级页是全屏 `MDialog`（`.m-dialog` 自身是白色），可见的页面背景来自 `.ft-subpage { background: var(--ft-bg) }`——所以 `min-height: 100%` 必须保留。
- **左滑行的「吞 click」**：松手后 350ms 内行内的 click 被吞掉，防止松手那一下误触单元格；行已展开时点按内容只收起。这一条与 Chromium 的「惯性滑动后第一次 tap 被当作止住惯性」叠加，导致 E2E 里「带速度的左滑展开后紧接着点按」不会产生 click——用 `release_hold_ms` 停住再松手即可规避，鼠标点击不受影响。真机上是否出现留给人工验收。
- **Alert 退出动画（约 180ms）期间遮罩仍然拦截触摸**：连续操作时要先等 Alert 卸载。E2E 把「等待弹层 `detached`」写进了 helper 的使用约定。
- **成员数只统计「当前用户是 Active 成员」的账本**（`LedgerMemberQueries.CountActiveMembersAsync`）：E2E 造「自己的共享账本」时要同时写一行 Owner 成员，否则「共享」徽标不出现。
- **验证了 Tab 根页面里的 `position: fixed` + `z-index: var(--ft-z-sheet)` 元素确实盖在 Tab 栏之上**（四个 Tab 逐个探测，祖先链上没有创建层叠上下文的元素）——后续首页 / 账单 / 统计里的 Sheet（07、09、11、15 号）可以直接用，不需要挪出页面栈。
- 回归：`dotnet build FinanceTracker.sln` 0 警告 0 错误（含 `net10.0-android` / `net10.0-ios` 目标）；`dotnet test` 558/558（新增 52：`LedgerPresentationTests` 37 + `DesignTokensTests` 15）；`run_all.py` 9/9（新增 `ledgers_page_test.py` 134 项 + 1 项跳过，其余各脚本数量与结果不变）。

### 待人工验收（设备 / 模拟器 / 云端环境）

- [ ] 对照设计稿 05 与 screens 的 08-ledgers，在 Android 与 iOS（CI 产物）上确认整体观感：导航栏 / 分组列表 / 徽标 / 邀请卡 / Sheet / Alert / 操作菜单 / Toast 的玻璃质感与间距
- [ ] 左滑手感：跟手程度、阻尼、松手后展开 / 回弹的判定（轻扫能展开、慢拖不过半会回弹）；左滑行内手势与页面栈「左边缘右滑返回」互不干扰（从屏幕最左边缘起滑才触发返回）
- [ ] 真机「快速左滑展开后马上点按」的行为：E2E 里观察到 Chromium 合成触摸下该次点按不产生 click（见上），确认真机上点按的是否能正常响应（预期：松手 350ms 后正常）
- [ ] Sheet：弹起 / 收起的弹簧手感、按住把手或标题栏向下拖拽的跟手度；**软键盘弹出时「名称」输入框与「保存」按钮不被遮挡**（Web 层无法模拟软键盘，需要真机确认 Sheet 的避让）
- [ ] Alert：删除确认的外观与按钮热区；「降低透明度」开启后材质回退由 23 号 ticket 统一核查，这里只确认默认外观
- [ ] 刘海 + 手势条机型：导航栏避开状态栏与灵动岛、状态栏图标为深色；Sheet 底部的「保存」按钮不被手势条遮住
- [ ] **（可选）真实 Api + PostgreSQL 联调共享账本邀请**：接受 → 邀请卡消失且账本出现在「加入的共享账本」；拒绝 → 邀请卡消失；从成员页返回后「我的账本」的成员数 / 「共享」徽标刷新。（这些路径已由 06 号 ticket 的假云端 E2E 自动化覆盖——假云端镜像了真实控制器的状态码 / 消息 / 过滤规则，但不是真实服务；换成真实云端只为确认假云端没有偏离）
- [ ] 老系统 WebView（iOS < 15.5 / Android WebView < 102）上左滑托盘的 `inert` 被忽略时，托盘按钮仍被内容层盖住、点按不受影响
