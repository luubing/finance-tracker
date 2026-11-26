# 10 — 账单搜索

**What to build:** 账单页新增关键字搜索：在大标题下的搜索框里输入，可按备注、分类名称或金额找到账单；搜索与分段、筛选芯片共同生效，汇总卡也随搜索结果更新。这是本次重构里数据层的第二项新增（只读查询），暂不暴露到 REST 接口。

**Blocked by:** 08 — 账单 Tab：列表

**Status:** completed（自动化验收全部通过——`dotnet build` 0 警告 0 错误、`dotnet test` 995/995、`run_all.py` 12/12（`bills_page_test.py` 由 233 项增至 278 项；第 2 轮评审改默认「本月」/ 分类多选 / 只读成员规则后现为 297 项，搜索的用例都改成先取消时间范围再查）；真机上的软键盘 / 真实输入法人工验收待办，见下方「待人工验收」）

- [x] 数据层：账单查询新增可选的关键字条件——备注与分类名称做不区分大小写的包含匹配；关键字能解析为数字时，同时匹配「金额等于该数值」的账单（数值相等，不是文本包含；解析与系统区域设置无关）；与现有筛选条件取交集；仍遵守软删除与账本可见性（含共享账本）；关键字为空时行为与现状完全一致；暂不暴露到 REST 接口——`BillFilter.Keyword` + 纯函数 `BillKeyword.Parse`；`BillService.BuildBillQueryAsync` 里**一个** `Where` 分支；REST / 实体 / 迁移 / 同步都没动
- [x] 汇总查询使用同一套条件（含关键字），列表与汇总口径一致，汇总不受分页影响——列表 / 总数 / 汇总 / 筛选 Sheet 的笔数估算都从同一个 `BuildBillQueryAsync` 取「符合条件的账单」，口径一致由结构保证
- [x] 界面：大标题下的搜索框（触发方式——防抖或提交——由本 ticket 决定；带清除按钮；与筛选芯片、分段共同生效）；无结果时的空状态；手机上系统键盘弹出时布局不被遮挡——`SearchField` + `ui.js` 里的提交时机（决定：**停顿 350 ms 提交 + 回车 / 搜索键 / 清除 / 失焦立即提交，输入法组词期间不提交**，见差异第 2 条）；空状态按「只有关键字 / 关键字 + 筛选 / 只有筛选」给三种文案。**「系统键盘弹出时布局不被遮挡」这一句 E2E 无法模拟，只能真机验证，已列入下方「待人工验收」，不在本项的勾选范围内**
- [x] 自动化验收（xUnit，使用真实 SQLite 迁移）：备注包含（大小写不敏感）；分类名称包含；数字关键字匹配金额相等（如 12.5 与 12.50 相等、不匹配 112.5）；含非数字字符的关键字只做文本匹配；与时间 / 账本 / 分类 / 支付渠道 / 账单类型筛选取交集；排除软删除；共享账本可见性；空关键字与无关键字等价；汇总与列表一致——`BillKeywordParseTests`（39 项，纯函数：空 / 全空白 / 小写化 / 数字判定 / 含非数字字符 / 超范围 / 与系统区域无关）+ `BillKeywordSearchTests`（27 项，真实 SQLite 迁移：上列各项逐条有用例，另含 `%` `_` 当字面量、引号等特殊字符、库里金额文本不规整、他人账单不可见、未归属账本、应用时区日期范围、共享账本四种情形）
- [x] 自动化验收（E2E，手机视口）：在唯一账本下创建备注与金额各异的账单，按备注片段、分类名、金额搜索，列表与汇总卡只含匹配项；叠加筛选与分段；清除后恢复——`bills_page_test.py` I1（39 项）与 I2（6 项，分页 + 字面量）两段
- [ ] 人工验收（**待人工**，需要设备 / 模拟器，见下方清单）：对照设计稿 screens 的 02-bills 中的搜索框外观；手机上系统键盘弹出与收起时的体验；Android 与 iOS（CI 产物）
- [x] 回归：既有 E2E 与 `dotnet test` 全部通过，`dotnet build` 零错误

## 实现说明

- 覆盖 spec 用户故事：40。
- 设计稿：01 · 四个 Tab 根页面；screens 的 02-bills。
- 关键字条件扩展 08 引入的查询条件对象，不要另起一套条件。
- 数据层改动只限只读查询；实体、迁移、REST 接口、同步策略都不动。
- 术语：搜索的对象是「账单」，匹配字段是备注、分类名称与金额。

### 完成记录（agent 实现）

**交付物**

| 层 | 文件 | 作用 |
|---|---|---|
| 数据层 | `Core/Models/BillModels.cs`（`BillFilter.Keyword`，`IsEmpty` 把它算进去）· `Core/Models/BillKeyword.cs`（`Parse`：去首尾空白 → 空 = null；`Text` 小写化；整体能解析成数字时带 `Amount`）· `Core/Services/BillService.cs`（`BuildBillQueryAsync` 末尾一个 `Where` 分支） | 列表 / 总数 / 汇总同时生效 |
| 筛选状态 | `Web/Models/BillListFilter.cs`（`Keyword` 赋值时规范化；`HasKeyword`；`HasConditions` / `HasAny`；`ToQuery` / `Clone` / `CopyFrom` / `SameConditionsAs` / `Clear` 都带关键字，`ClearPanel` 保留它） | Sheet 草稿不丢关键字；「只改了关键字」不会被当成「条件没变」 |
| 展示规则（纯函数） | `Web/Models/BillListPresentation.cs`（`EmptyState(hasConditions, hasKeyword)`、`ClearConditionsText`、`SearchPlaceholder` / `SearchLabel` / `SearchClearLabel` / `SearchMaxLength` / `SearchResultText`） | 文案不散落在页面里 |
| 组件 | `Web/Components/Ui/SearchField.razor`（新） | 灰色填充搜索框：放大镜、不受控输入框、清除按钮、`role=status` 播报区 |
| 页面 | `Web/Pages/Bills.razor` | 搜索框放在大标题与分段控件之间；`ApplyKeywordAsync`；`_searchEpoch`（换 `@key` 清空搜索框）；`SearchStatusText`；空状态三种文案与清除按钮 |
| 脚本 | `Web/wwwroot/js/ui.js`（`SEARCH_DEBOUNCE_MS = 350`；`handleInput` / `handleCompositionEnd` / `handleChange` / `handleKeyDown` / `clearSearch` / `scheduleSearch` / `cancelSearch` / `commitSearch`） | 提交时机：停顿 / 回车 / 清除 / 失焦；避开输入法组词；内容没变不重复提交 |
| 样式 | `Web/wwwroot/css/ui.css`（`.ft-search*` 一组） | 外观、聚焦外圈、`:placeholder-shown` 控制清除按钮显隐 |
| 测试（单元） | `BillKeywordSearchTests.cs`（`BillKeywordParseTests` 39 + `BillKeywordSearchTests` 27）· `BillListFilterTests`（58 → 71）· `BillListPresentationTests`（新增空状态 / 清除文案 / 播报 / 固定文案） | 共新增 88 项，累计 995 |
| 测试（E2E） | `tests/e2e/bills_page_test.py`（233 → 278 项）：I1 `check_search` 39 项、I2 `check_search_paging` 6 项；`check_refresh` 排除成员缓存刷新的两条云端路径；`tests/e2e/README.md` 登记 `bills-search*` 钩子并更新 `bills-empty` | 见下 |
| 文档 | `docs/adr/0007-ios-visual-system.md` 第 12 点与已知限制 | 匹配规则、SQLite 三个事实、不受控输入、提交时机、外观 |

**契约（后续 ticket 直接使用）**

- **搜索框 `SearchField`**：`<SearchField @key Placeholder AriaLabel ClearLabel MaxLength StatusText TestId OnSearch Class>`。输入框是**不受控**的（不绑 `value`，服务端不回写）；服务端要清空时给它换 `@key`。`OnSearch` 的参数是输入框当前的完整内容（可能为空串），在「停顿 / 回车 / 失焦 / 清除」时触发。需要同款搜索的页面（目前没有）直接复用，提交时机由 `ui.js` 里按 `data-live-search*` 属性的文档级委托提供，不用在页面里写任何防抖。
- **条件对象**：关键字是 `BillFilter.Keyword` / `BillListFilter.Keyword`，**不是**筛选维度——不进 `BillFilterDimension`、不进筛选 Sheet、不计角标（`PanelCount`）、条件条上没有它的标签；`HasConditions`（有筛选维度或账单类型）与 `HasAny`（再加关键字）要分清：空状态文案用后者区分「还没有账单」与「没有找到」。
- **匹配规则**：`BillKeyword.Parse` 是唯一入口（不要在别处再写一份解析）；文本匹配用 `ToLower().Contains()`（SQLite 里 `instr(lower(x), @p)`，`%` / `_` 是字面量），金额相等用 `>= && <=` 而不是 `==`（原因见实现要点第 2 条）。
- **E2E 钩子**：`bills-search`（容器）/ `bills-search-input` / `bills-search-clear` / `bills-search-status`；空状态沿用 `bills-empty` 系。要点：`fill()` 会同时派发 `input` 与 `change`（立即提交），测停顿必须用 `press_sequentially`；连续两次搜索的期望结果集必须不同，否则 `wait_summary` 会在新结果到达之前就返回。

**与 ticket / 设计稿的差异（均已评估）**

1. **关键字不在条件条上展示**：09 号的契约里留了「若要展示就复用 `BillFilterChip`」。本 ticket 没有加标签——搜索框里的文字本身就是展示，再加一颗标签会重复；同时关键字不计入筛选按钮的角标。**记入审查待办等用户判断**（是否希望条件条上也出现「关键字」标签）。
2. **提交时机是「停顿 + 回车 / 清除 / 失焦」，不是「必须点搜索键」**：ticket 把「防抖或提交」交给本 ticket 决定。纯提交式要多一步；纯防抖在输入法组词时会闪、在每个按键都往返服务端的 Blazor Server 下也浪费。所以两者并用：350 ms 停顿自动查询、回车 / 软键盘搜索键立即查询并收起键盘、组词期间不查、清除立即查且键盘不收。判断题，记入审查待办。
3. **搜索框不吸顶**：设计稿 02 里搜索框在大标题下面，没有画吸顶态；吸顶要与大标题折叠后的导航栏叠在一起，还要多占一块不透明区域。现在随页面滚动，与分段控件、条件条同层。
4. **SQLite 的 `lower()` 只折叠 ASCII**：中文没有大小写、英文备注 / 分类名不受影响；带重音的拉丁字母（É / é）不区分大小写会失效。记账场景几乎遇不到，不为它放弃「查询全在库里」，记入审查待办。
5. **系统键盘弹出时的布局只能真机验证**：Chromium 桌面的手机视口模拟不会因为软键盘缩小可视区域，E2E 里没有可测的东西——只能保证搜索框在页面顶部、不会被键盘盖住；结果列表在键盘下面时的体验见人工验收。

**实现要点 / 发现**

1. **数字关键字的判定**：整体能被 `decimal.TryParse(AllowLeadingSign | AllowDecimalPoint, InvariantCulture)` 接受才算数字——没有千分位、没有科学计数法、不看系统区域。「1,5」在德语区是一点五、在中文区是一千五百，当数字就会因设备而异，所以只做文本匹配；「35元」「¥35」「1e3」同理。数字关键字**同时**匹配备注 / 分类名里的文本：搜「618」能命中备注「618大促」、分类「618专区」，也命中金额正好 618 的账单，不命中 1618。
2. **金额相等写成 `>= && <=` 而不是 `==`**：SQLite 把 decimal 存成 TEXT，`==` 是文本比较，只有库里的文本恰好是 EF 写入时的规整格式才碰巧相等；`>= && <=` 走 EF 注册的 `ef_compare`（按数值比），对别的路径写入的 `12.500` 同样成立。只有单元测试「库里的金额文本格式不规整时也按数值比较」（原始 SQL 写入非规范文本）守得住这一点——普通的「12.5 与 12.50 相等」用例对 `==` 也能过（变异检查：把条件改成 `==`，27 项里恰好这一项失败，已改回）。PostgreSQL 的 numeric 上两种写法等价。
3. **输入框不受控 + `@key` 重建**：回写 `value` 会让「事件往返期间用户又敲的字」被服务端旧值盖掉（05 / 06 号的 `@oninput` + `value` 同类问题）。代价是服务端清空时不能改 `value`，而是换 `@key`；首次进入时搜索框本来就是空的，不换 key（重建只会丢掉用户这时已敲的字）。
4. **提交的实现细节**：「提交」就是在输入框上派发一次 `change`，Blazor 的 `@onchange` 接手。① 浏览器自己在失焦时也会派发 `change`，脚本派发的不会更新浏览器内部的「上次提交内容」，所以同一段文字可能收到两次——脚本用 `WeakMap` 记下每个输入框最近一次提交的内容避免重复派发，服务端 `ApplyKeywordAsync` 又在第一个 `await` 之前就写入关键字，第二个重复事件看到新值直接返回；② 输入法组词期间（`isComposing`）不提交，`compositionend` 再排一次停顿——Safari 的 `compositionend` 在最后一次 `input` 之前触发，所以 `input` 与 `compositionend` 两处都要排；③ 回车要排除组词期间的回车（`isComposing` 或 `keyCode 229` 是在确认候选字）。
5. **清除按钮的显隐用 CSS `:placeholder-shown`**：有占位文字且正显示 = 没有内容 = 不显示清除按钮，不依赖脚本也不依赖服务端状态；点它会清空、保持焦点（软键盘不收）、立即提交。
6. **输入框字号 16px**（`--ft-type-body`）：iOS Safari 对小于 16px 的输入框聚焦时会自动放大页面。
7. **E2E 测量教训**：Toast（MASA snackbar）本身是带 `backdrop-filter` 的玻璃胶囊，同屏测「玻璃层 ≤ 3」时若删除 / 提示的 Toast 还在，会多算一层——先量、后删。同样：`data-active` 是字符串 `"true"` / `"false"`，布尔判断要与字符串比较。
8. **一处既有的时间敏感检查**：`check_refresh`（D 段）的「刷新没有向云端发任何请求」在首轮全量运行里偶发失败——页面加载共享账本时会顺带刷新成员缓存（`POST /api/auth/login` + `POST /api/sync/ledgermembers/pull`，CLAUDE.md 第 9 条：3 秒期限、失败后 30 秒内不再尝试），前面的用例让这条读路径恰好落在窗口之外就会发请求。这是预期行为，不是缺陷；检查改为排除这两条路径并在注释里写明（`home_page_test.py` 里有同款检查，目前碰巧没失败，已记入审查待办）。
9. **搜索结果的读屏播报**：搜索框旁有视觉隐藏的 `role="status"` 区域，数据就绪后播报「找到 N 笔账单」/「没有找到相关账单」；没有关键字、或正在加载时为空，不会抢话。
10. **本机环境**：Web.Server 运行时 `dotnet build FinanceTracker.sln` 会因 DLL 被占用复制失败，先 `server.ps1 -Action stop`；改 C# / Razor 要重启服务，静态 JS / CSS 即时生效。`dotnet test` 不受运行中服务影响。
11. 回归：`dotnet build FinanceTracker.sln` 0 警告 0 错误；`dotnet test` 995/995（本 ticket 新增 88 项）；`run_all.py` 12/12（`bills_page_test.py` 278 项、`home_page_test.py` 93 项、`ledgers_page_test.py` 147 项、`ledger_members_page_test.py` 138 项、`tab_shell_test.py` 64 项、`tab_slider_test.py` 82 项、`immersive_safe_area_test.py` 35 项、`e2e_base_test.py` 120 项等通过）。

### 待人工验收（设备 / 模拟器）

- [ ] 对照设计稿 screens 的 02-bills，在 Android 与 iOS（CI 产物）上确认搜索框外观：高度、圆角、灰色填充与页面背景的反差、放大镜与清除按钮的大小 / 位置、聚焦时的品牌色外圈
- [ ] **软键盘弹出与收起**：搜索框在页面顶部、不会被键盘盖住；结果列表在键盘下面时是否需要点别处才能收起键盘（iOS Safari 里点按钮 / 空白处不会让输入框失焦，见 ADR 0007 已知限制）；键盘上的「搜索」键是否出现（`enterkeyhint="search"`）且触发提交并收起键盘
- [ ] **真实输入法**：拼音 / 手写 / 语音输入组词期间不触发查询、选字后停顿一下出结果；Android Gboard 敲拉丁字母（如「starbucks」）时「停顿提交」要等词结束才触发（Gboard 通常把整个词当作组词），回车 / 失焦不受影响——手感是否可接受
- [ ] **判断项（请一并确认）**：搜索不进条件条（见差异第 1 条）；提交时机用「停顿 + 回车 / 清除 / 失焦」（见差异第 2 条）
- [ ] 清除按钮点按后键盘不收起、可以接着输入；搜索中切到别的 Tab 再回来，搜索框里的文字与结果是否保留（WKWebView / Android WebView 上 Tab 保活）
