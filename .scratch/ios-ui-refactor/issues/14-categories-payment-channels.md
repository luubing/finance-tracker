# 14 — 分类管理 + 支付渠道

**What to build:** 分类管理与支付渠道两个管理页按二级页面模板重做。分类页用「支出 / 收入」分段，下分「自定义」「预设」两组，新建分类时选择图标与颜色；支付渠道页同样用分组列表，默认项带「默认」徽标，「设为默认」通过菜单完成。其中的「新建分类」面板做成可被「记一笔」复用的共用面板。

**Blocked by:** 05 — 二级页面模板 + 账本管理

**Status:** completed（自动化验收全部通过——`dotnet build FinanceTracker.sln` 0 警告 0 错误、`dotnet test` 1282/1282、`run_all.py` 17/17 个脚本通过（共 1601 项检查；新增 `categories_page_test.py` 133 项、`payment_channels_page_test.py` 106 项，基座自测 132 项）；**一处与 ticket 条文不符：颜色选择未做**（`Category` / `PaymentChannel` 没有颜色列，见判断记录第 1 条，待用户确认）；真机 / 设备上的人工验收待办，见下方「待人工验收」）

- [x] 开工前先列出这两个页面现有的全部功能点并写入本 ticket 的实现说明，迁移后逐项对照（自定义分类的新增 / 编辑 / 删除 / 设为默认、预设项只读、支出与收入的区分；支付渠道的新增 / 编辑 / 删除 / 设为默认等）——见下方「开工前清单」，对照结果见「完成记录 · 逐项对照」
- [x] 分类页：「支出 / 收入」分段；「自定义」「预设」分组；预设项只读；自定义项可编辑、删除（软删除，Alert 确认）、设为默认——`categories-segment`（支出 / 收入）+ `custom-categories` / `preset-categories` 两组；预设行只读（没有左滑托盘与箭头，点按只弹「设为 / 取消默认」菜单）；自定义行左滑「编辑」「删除」、点按进编辑 Sheet；删除 Alert 确认后软删除；分类下有账单时被服务层拦截并提示
- [ ] 新建 / 编辑分类使用底部 Sheet：名称、图标（图标网格）、颜色；图标名必须存在于 MDI 7.2.96（数据库保存的是 mdi 图标名）——**名称 / 图标（42 个候选的 6 列网格）/ 排序已做，且全部候选图标已在 MDI 7.2.96 字体里逐个核对字形；「颜色」未做**（实体没有颜色列，加列要迁移 + 同步 DTO，超出「只改展示」的边界；色块由名称 / Id 稳定取色，见判断记录第 1 条），待用户确认
- [x] 把新建分类的 Sheet 内容做成可被其他页面复用的共用面板（接收账单类型，保存后返回新分类），供 15 的「新建分类」使用——`CategoryFormSheet`（`Type` 参数 = 账单类型，`OnSaved(Category)` 把保存后的分类交给调用方），底层是通用的 `NameIconFormSheet`
- [x] 支付渠道页：分组列表；默认项带「默认」徽标；「设为默认」通过菜单或编辑 Sheet 完成（取代行内图标按钮）；新增 / 编辑 / 删除（自定义项）；预设项只读——`custom-channels` / `preset-channels`；自定义项的「设为默认」在编辑 Sheet 的开关里，预设项在点按弹出的菜单里；删除 Alert 确认后软删除
- [x] 所有左滑操作都有等价的非滑动入口（点按行进入含删除 / 设为默认的编辑 Sheet 或菜单）——编辑 Sheet 里有「设为默认」开关与「删除…」行；预设项点按即菜单
- [x] 自动化验收（E2E，手机视口）：分类——新建自定义分类（选图标；颜色见上）→ 出现在「自定义」→ 编辑 → 设为默认 → 删除；支出 / 收入切换；支付渠道——新增 → 设为默认（出现「默认」徽标）→ 编辑 → 删除；左滑与非滑动入口各验证一遍——`categories_page_test.py`（133 项）与 `payment_channels_page_test.py`（106 项），共用检查在 `managed_items_base.py`
- [ ] 人工验收（**待人工**，需要设备 / 模拟器，见下方清单）：对照设计稿 05 与 screens 的 11-categories；图标网格与颜色选择的可用性；Android 与 iOS（CI 产物）
- [x] 回归：既有 E2E 与 `dotnet test` 全部通过，`dotnet build` 零错误——`dotnet build FinanceTracker.sln` 0 警告 0 错误；`dotnet test` 1282/1282；`python tests/e2e/run_all.py` 17/17 个脚本通过、共 1601 项检查

## 实现说明

- 覆盖 spec 用户故事：68（共用面板部分）、89、90、95。
- 设计稿：05 · 二级页面与弹层（分类与支付渠道同构）；screens 的 11-categories。
- 预设分类 / 支付渠道（UserId 为空）与用户自定义项的区分、软删除语义不变。
- 术语：「分类」「支付渠道」，避免「标签」「类型」「支付方式」。

### 开工前清单：现有页面的功能点（迁移后逐项对照）

读自改造前的 `Pages/Categories.razor` 与 `Pages/PaymentChannels.razor`（HEAD 版本）。

| # | 现有模块 | 行为 |
|---|---|---|
| C1 | 紫色顶栏「分类管理」+ 卡片标题行的「添加分类」按钮 | 压栈二级页；按钮打开创建对话框 |
| C2 | 「支出分类 / 收入分类」两个 Tab | 同一页按账单类型分开列出 |
| C3 | 分类列表行：图标 + 名称 | 预设行右侧是「预设」小标签；自定义行右侧是「编辑」「删除」图标按钮；**每一行（含预设）都有一颗星标按钮**用来设 / 取消默认（已默认的星标是黄色） |
| C4 | 创建 / 编辑对话框 | 分类名称（必填）、图标（**自由文本**，占位 `mdi-tag`）、**分类类型下拉（支出 / 收入，编辑时也可改）**、排序顺序（数字）；创建时类型默认取当前 Tab |
| C5 | 保存 | 编辑 → `UpdateCategoryAsync`「分类更新成功」；新建 → `CreateCategoryAsync`「分类创建成功」；失败「操作失败: …」；成功后关对话框并重读列表 |
| C6 | 删除（**没有确认**） | `DeleteCategoryAsync`：成功「分类删除成功」；不存在「分类不存在」（警告）；`ForbiddenAccessException`（预设）/ `InvalidOperationException`（分类下有账单，「该分类下有账单，无法删除」）→ 警告 Toast；其它「删除失败: …」 |
| C7 | 设为 / 取消默认 | 支出与收入各有一个默认（`SetDefaultExpenseCategoryAsync` / `SetDefaultIncomeCategoryAsync`，已默认再点则传 `null` 取消）；Toast「已设置为默认分类 / 已取消默认分类」；失败「操作失败: …」 |
| C8 | 数据读取 | `OnInitializedAsync` 取用户 Id；`GetCategoriesAsync` + `GetUserDefaultAsync`；按类型拆成两个列表 |
| P1 | 紫色顶栏「支付渠道」+ 卡片标题行的「添加渠道」按钮 | 同 C1 |
| P2 | 渠道列表行：图标 + 名称 | 同 C3：预设「预设」标签、自定义「编辑」「删除」、每一行都有星标 |
| P3 | 创建 / 编辑对话框 | 渠道名称（必填）、图标（自由文本，占位 `mdi-credit-card`）、排序顺序 |
| P4 | 保存 / 删除 / 设默认 | 同 C5–C7（文案换成「支付渠道…」；默认 = `SetDefaultPaymentChannelAsync`，只有一个） |
| P5 | 数据读取 | `GetPaymentChannelsAsync` + `GetUserDefaultAsync` |

### 完成记录（agent 实现）

**交付物**

| 层 | 文件 | 作用 |
|---|---|---|
| 页面 | `Web/Pages/Categories.razor`（重做）：`SecondaryPage` + 「支出 / 收入」分段 + 自定义 / 预设两组 + 编辑 Sheet + 预设项菜单 + 删除 Alert；读库经 `PageDataGate`，用户 Id 在 `OnAfterRenderAsync(firstRender)` 取；加载骨架 / 加载失败 + 重试 | 只调用既有服务，不绕过校验 |
| 页面 | `Web/Pages/PaymentChannels.razor`（重做）：同构，无分段 | 同上 |
| 组件 | `Web/Components/Ui/NameIconFormSheet.razor`（新）：通用「名称 + 排序 + 图标网格」Sheet——名称校验、保存中状态、成功 / 失败提示、`EditingExtras` 插槽（页面自己的「设为默认」「删除」）；表单只在「隐藏 → 显示」那一刻初始化，页面重绘不会冲掉正在输入的内容 | 分类 / 支付渠道共用 |
| 组件 | `Web/Components/Categories/CategoryFormSheet.razor`（新）：包一层「调用 `CategoryService` 落库」；`Type` 参数 = 账单类型，编辑时原样带回；`OnSaved(Category)` 交回保存后的分类 | **15 号「记一笔」的「新建分类」直接复用** |
| 纯函数 | `Web/Models/ManagedItemPresentation.cs`（名称校验、「N 笔」、「预设」标签、`NameIconTarget` / `NameIconValues`）· `CategoryPresentation.cs`（分段、42 个候选图标、配色、默认菜单 / Toast 文案、占位）· `PaymentChannelPresentation.cs`（24 个候选图标、配色、默认菜单 / Toast 文案） | 规则与文案不散落在 Razor 里，单元测试覆盖 |
| 样式 | `wwwroot/css/ui.css`：`.ft-tile--outline`（「新建…」行的描边加号块）、`.ft-cell--brand`（品牌色标题） | 只引用令牌；整页没有新增玻璃层 |
| 测试（单元） | `tests/unit/.../Services/ManagedItemPresentationTests.cs`（45 项）：名称校验（含 50 / 51 边界、全空格）、「N 笔」、图标规范化、分段、候选图标合法 / 唯一 / 铺满 6 列 / 含默认图标、`mdi-alipay` 不在候选、配色一致、默认文案 | 累计 1282 |
| 测试（E2E） | `tests/e2e/categories_page_test.py`（131 项）· `payment_channels_page_test.py`（103 项）· `managed_items_base.py`（共用检查，按 `Spec` 参数化——18 / 19 号这类同构管理页可照用）· `e2e_base.py` 新增 `LocalDb.category_row` / `channel_row` / `custom_categories` / `custom_channels` / `default_category_id` / `default_channel_id` / `preset_category_names` / `preset_channel_names`、`visible_category_count` 可按类型数，并核对 `UserDefaults` 的三个默认列 · `e2e_base_test.py` 新增 14 项自测（132 项）· `README.md` 登记脚本、观察属性与 `LocalDb` 清单 | 见下 |
| 文档 | `docs/adr/0007-ios-visual-system.md` 第 16 点与已知限制 · `tests/e2e/README.md` | 页面结构、共用件、判断项、已知取舍 |

**逐项对照（开工前清单 → 迁移后）**

| # | 迁移后 | 对照结果 |
|---|---|---|
| C1 | `SecondaryPage`（玻璃导航栏：返回 + 居中 h1「分类管理」+ 右上角「＋」，读屏名称「新建分类」）；自定义组末尾另有一行「新建分类」；二者打开同一个新建 Sheet | 一致（入口从卡片按钮换成导航栏「＋」与列表末行） |
| C2 | `categories-segment`（「支出 / 收入」分段，按钮高 44）；两种类型的列表一次读完，切换不再读库；预设组与自定义组随分段切换 | 一致（Tab → 分段控件） |
| C3 | 自定义行：图标色块 + 名称 + 右侧「N 笔」+ 箭头，左滑「编辑」「删除」，点按进编辑 Sheet；预设行：图标色块 + 名称 + 右侧「预设」，只读，点按弹菜单；「默认」徽标在名称旁（自定义与预设行都可能出现） | 一致 + 新增（账单数）；**星标按钮 → 徽标 + 编辑 Sheet 开关 / 预设菜单**（设计稿取代行内图标按钮） |
| C4 | `CategoryFormSheet`：名称（必填，≤ 50 字，即时校验）、**图标网格（42 个候选，替代自由文本）**、排序（数字，小在前）；类型 = 当前分段（新建）/ 原样（编辑，**不可改**）| 一致，**三处变化**：图标改为网格选择、编辑时分类类型不可改、新增即时校验（见判断记录第 2、4、5 条） |
| C5 | 保存沿用 `CreateCategoryAsync` / `UpdateCategoryAsync`；Toast「分类创建成功 / 更新成功」，失败「操作失败: …」且 Sheet 保持打开；成功后 Sheet 收起并重读列表 | 一致 |
| C6 | 删除走 Alert「删除分类「…」？」（取消 / 删除），确认后 `DeleteCategoryAsync` 软删除；Toast 文案与分支不变（成功 / 不存在 / `Forbidden…` 与 `InvalidOperation…` 警告 / 其它错误）；入口：左滑「删除」、编辑 Sheet 里的「删除分类」 | 一致 + **新增确认**（原来点了就删，spec 要求危险操作用 Alert 确认） |
| C7 | 编辑 Sheet 里的「设为默认分类」开关（副标题随类型：记支出 / 记收入时默认选中这个分类）、预设项菜单「设为默认支出分类 / 设为默认收入分类 / 取消默认」；**立即生效**，Toast 文案不变，列表徽标跟着变；支出 / 收入各一个默认、互不影响 | 一致（入口换了） |
| C8 | 读库经 `PageDataGate`；用户 Id 在首次渲染后取；各自定义项的账单数在同一次读里算好；读失败保留旧数据并 Toast，首次失败整页错误状态 + 重试 | 一致 + 增强（错误状态） |
| P1–P5 | `PaymentChannels.razor` 同构：「新建支付渠道」导航栏「＋」与末行、「自定义」「预设渠道」两组、`NameIconFormSheet`（24 个候选图标）、左滑编辑 / 删除（Alert 确认，渠道下有账单被拦截）、默认渠道（开关 / 预设菜单，只有一个默认） | 一致，变化同上（图标网格、即时校验、删除确认、徽标 + 开关 / 菜单取代星标） |

**判断记录（均已评估，评审阶段会再向用户确认）**

1. **颜色选择不做（与 ticket 条文不符）**：设计稿 11 的新建面板里有颜色选择，但 `Category` / `PaymentChannel` 实体没有颜色列；加列要迁移 + 同步 DTO + 云端表，超出 spec「只改展示」的边界（不得改实体 / 迁移 / 同步）。所以表单里没有颜色，色块由名称（预设分类用固定色）或 Id（自定义分类 / 支付渠道）稳定哈希取色，同一项在账单行 / 统计 / 管理页里颜色一致。**待用户确认**：是否值得新增 `Color` 列（含迁移与同步）。
2. **分类类型编辑时不可改**：原对话框编辑时也能改「分类类型」。但分类下已有账单的类型是固定的，把分类从支出改成收入会让历史账单与分类类型对不上、统计口径错乱，且设计稿的编辑面板没有类型字段。spec 要求保留的是「自定义新增 / 编辑 / 删除 / 设默认」，改类型不在其中。新建时类型 = 当前分段。若要保留，在编辑 Sheet 里加一个分段控件、把选择带进 `CategoryFormSheet.Type` 即可（成本很小）。
3. **预设项点按弹菜单**：设计稿的预设行是只读行；原页面每一行都有星标可设默认，所以保留这个能力——点按预设行弹出只含「设为 / 取消默认」的 ActionSheet（说明「预设分类不能修改或删除」）。预设行不显示账单数（右侧是「预设」），省掉对应的查询。
4. **图标由自由文本改为图标网格**：spec 要求图标名必须存在于 MDI 7.2.96，网格里只放核对过的候选（分类 42 个、支付渠道 24 个）。原来可以输入任意 `mdi-xxx` 的能力不再提供；已有的自定义图标照常显示，编辑时若不在网格里则不高亮任何候选、保存时按原值保留。候选集合是否够用留待人工验收。
5. **名称校验提前到页面**：空 / 全空格 →「请输入分类（支付渠道）名称」，超过 50 个字 →「…名称最多 50 个字」（与实体 `MaxLength` 一致）；原来靠服务层 / 数据库报错。输入框 `maxlength=50`，脚本粘贴超长仍被页面拦住。
6. **保留「排序」输入**：设计稿没画，但原页面有，属于现有功能，放在名称下方（占位「数字越小越靠前」）。
7. **设为默认立即生效、不依赖「保存」**：与账本页（05 号）一致；开关与预设菜单操作后 Toast 提示，编辑 Sheet 保持打开。
8. **删除新增 Alert 确认**：原页面点了就删；spec 要求危险操作确认。分类下有账单时服务层拦截，Alert 文案里提前说明「分类下还有账单时无法删除，请先把账单改到其他分类」。

**契约（后续 ticket 直接使用）**

- **`CategoryFormSheet`（15 号「记一笔」的「新建分类」）**：`@bind-Visible`、`UserId`、`Type`（账单类型，决定占位提示与新建分类的类型）、`OnSaved(Category)`（保存成功后触发，此时 Sheet 已被要求收起；调用方自动选中这个分类）、`OnClosed`（退出动画结束后）。不传 `Editing` 即新建；落库走 `CategoryService.CreateCategoryAsync`，校验与失败提示都在里面。观察属性 `category-sheet` / `category-name` / `category-sort` / `category-form-error` / `category-save` 与分类页的相同。
- **默认值**：`UserDefaults` 的 `DefaultExpenseCategoryId` / `DefaultIncomeCategoryId` / `DefaultPaymentChannelId`（经 `IUserDefaultService.GetUserDefaultAsync`）；15 号新建账单时按账单类型取默认分类、取默认支付渠道，没有默认时的选中规则沿用旧「记一笔」页。
- **配色**：分类用 `BillPresentation.CategoryTone(name, id)`（`CategoryPresentation.ToneFor` 是它的别名），支付渠道用 `PaymentChannelPresentation.ToneFor(id)`；`.ft-tile--soft.ft-tile--{purple｜teal｜pink｜orange｜blue｜green｜red｜gray}` 是柔和底 + 深色图标。
- **图标候选**：`CategoryPresentation.IconChoices` / `PaymentChannelPresentation.IconChoices`（`IconChoice(Icon, Label)`，6 列网格，全部在 MDI 7.2.96 里）；`IconPicker` 的每个图标是 `aria-pressed` 按钮、`data-icon="mdi-…"`。
- **E2E**：`managed_items_base.py` 的 `Spec` + `check_*` 共用检查可直接给 18（预算）/ 19 等同构管理页借鉴；`LocalDb` 新增的读库 helper 见 README。

**与 ticket / 设计稿的差异（均已评估）**

1. **没有颜色选择**（判断记录第 1 条）。
2. **编辑时没有分类类型**（判断记录第 2 条）。
3. **多了「排序」输入**（判断记录第 6 条）：设计稿 11 没有，原页面有。
4. **预设行点按弹菜单**（判断记录第 3 条）：设计稿是纯只读行。

**实现要点 / 发现**

1. **MDI 7.2.96 里有些旧名字已经退役**：`mdi-alipay` 在 7.x 里被移除（由 `MdiIcons.Normalize` 回退为 `mdi-qrcode-scan`）。候选图标的核对不靠肉眼：脚本读官方 CSS 核对类名（`.scratch/check_mdi.py`），E2E 再在应用**实际加载**的字体里逐个检查 `::before` 的 `content`（缺字形的类名会渲染成空白）。**这个检查依赖 jsdelivr 的 MDI CSS（应用本身也是从 CDN 加载），离线环境会整批失败——那是环境问题，不是产品问题。**
2. **Playwright 的 `fill()` 受 `maxlength` 约束**（走输入法路径会被截断），超长名称的校验要用脚本直接赋值并派发 `input` 事件（与账本页的做法一致）。
3. **分类 Sheet 因 42 个图标顶到视口顶部下 12px**（`.ft-sheet { max-height: calc(100% - safe-top - 12px) }`），遮罩区只剩顶上一条——E2E 点这一条的中间收起；真机上用左上角关闭 / 拖拽把手更自然，是否收窄网格高度留待人工验收。
4. **表单只在「隐藏 → 显示」那一刻初始化**：编辑中切换「设为默认」会让页面重新读库并重绘，若每次参数更新都重置表单，用户正在输入的名称会被冲掉。`NameIconFormSheet.OnParametersSet` 只在 `Visible` 由假变真时初始化；E2E 覆盖了这条规则：编辑中先改名称与排序（不保存）→ 切换「设为默认」→ 草稿仍在，关闭 Sheet 后库里仍是原值。
5. **历史数据的图标类名可能不合法**：列表与编辑 Sheet 都回退到默认图标（高亮默认图标、预览同步），不会渲染成空白；不改图标直接保存时库里规范化为默认图标。E2E 用 `legacy` 这样的非法值造数覆盖。
6. **`ListCell` 没有 `OnClick` 时渲染成 `div`，有 `OnClick` 才是 `button`**：预设行有点按菜单所以是 `button`，没有箭头靠不传 `Chevron`。
7. **同构页面的共用 E2E 检查**：两页只在 `Spec` 里写差异（测试钩子前缀、文案、占位、候选图标数、造数 / 读库适配函数），共用 A–J 十节检查写一遍；分类页另有 K 节（分段、类型、按类型分的默认）。
8. 回归：`dotnet build FinanceTracker.sln` 0 警告 0 错误；`dotnet test` 1282/1282；`run_all.py` 17/17 个脚本通过（共 1601 项检查：基座自测 132、账单 278、冒烟 7、分类 133、首页 93、沉浸式 31、账本成员 138、账本 147、登录记账 7、支付渠道 106、我的 97、种子可见性 9、统计 114、趋势年度 157、Tab 壳 63、Tab 滑块 82、语音记账 7）。

### 待人工验收（设备 / 模拟器）

- [ ] 对照设计稿 05 与 screens 的 11-categories，在 Android 与 iOS（CI 产物）上确认：「自定义」「预设分类」分组与脚注、图标色块、「N 笔」与「预设」的右侧值、描边的「新建分类」行、「默认」徽标、新建 / 编辑 Sheet（预览色块、名称 / 排序行、6 列图标网格）、预设菜单、删除 Alert
- [ ] **图标网格的可用性**：42 个图标在小屏上的点选手感、Sheet 内滚动与外层不串滚、选中态是否足够醒目；候选集合是否够用（不再能输入任意 `mdi-xxx`）
- [ ] **软键盘**：名称 / 排序框聚焦时 Sheet 的避让、「保存」按钮是否被键盘遮住、回车是否等同保存
- [ ] **左滑**：松手手感与速度判定、点内容只收起、纵向滑动不误触；Sheet 弹起 / 拖拽收起（含顶部只剩 12px 遮罩条时的收起方式）
- [ ] 预设项菜单与 Alert 的转场、取消方式；有账单的分类 / 渠道删除被拦截时 Toast 的位置与文案
- [ ] **判断项（请一并确认）**：颜色选择是否需要（判断记录第 1 条，需要则新增 `Color` 列 + 迁移 + 同步）；编辑时分类类型是否可改（第 2 条）；图标只能从网格选（第 4 条）；预设项点按弹菜单（第 3 条）；保留「排序」输入（第 6 条）
