# 同步：用户 Id 映射与更新时间口径

## 背景

同一个账号在三处各有一个用户 Id，且**互不相同**：本地库（SQLite，每台设备各自生成）里是 `LA` / `LB`，
云端库（PostgreSQL）里是 `C`。云端接口以 JWT 里的 `C` 为准；客户端各表的外键与列表过滤用的是本地的 `LA` / `LB`。

同步用 `UpdatedAt` 做"后写入优先"的裁决，因此 `UpdatedAt` 必须表示"这一版内容发生的时间"，
而不是"这一行最后一次被写入本地库的时间"。

## 决策 1：云端用户 Id 只在同步边界映射成本地用户 Id

`SyncService` 在每次同步开头问 `ICloudSyncClient.GetCloudUserIdAsync()`（读云端 JWT 里的用户声明，
`HttpCloudSyncClient` 实现），之后凡是从云端拉回来的用户 Id 都过一遍 `MapToLocalUserId`
（单独刷新成员关系时顺序相反：先拉取、再读 Id——拉取会顺带换取云端令牌，云端连不上时只失败一次，
不会为了读 Id 多等一次连接失败）：

| 数据 | 映射的字段 |
|------|-----------|
| 账单 | `BillSyncDto.UserId`（作者）；映射后仍不是自己的才建用户存根 |
| 账本 | `LedgerSyncDto.OwnerId` |
| 成员关系 | `LedgerMemberSyncDto.UserId`、`LedgerOwnerId` |

- 等于"云端的我"（或为空，旧服务端兜底）→ 本地用户 Id；共享账本里的**他人**保持云端 Id（本地以它建存根用户）。
- 拿不到云端 Id（未登录/离线）→ 不映射，同步照常进行，后续请求会暴露真正的连接/认证错误。
- 成员关系的唯一索引是 `(LedgerId, UserId)`：本地可能已有懒补的 Owner 行（随机 Id），
  拉到云端的同一成员时**用云端这条取代**（Id 一并对齐），不再新增去撞索引。
- **历史数据修复**：旧版本把"自己的账单/账本/成员关系"挂在以云端 Id 建的存根用户名下，换设备/重装后列表里一条都看不到。
  每次同步开头（云端 Id ≠ 本地 Id 时）幂等地把它们归还给本地用户。

> 不选"让服务端返回本地 Id"：服务端并不知道各设备的本地 Id（每台设备都不同），且要改接口与云端数据。

## 决策 2：同步不改写"编辑时间"

`ApplicationDbContext.SaveChangesAsync` 平时对每次保存盖 `UpdatedAt = UtcNow`。同步代码改在
`SyncTimestampScope`（`AsyncLocal` 计数，只影响当前异步流程，可嵌套）内保存，作用域内**不自动盖章**：

- 只把同步状态标成 `Synced` / `Failed` 不算编辑，不动 `UpdatedAt`；
- 采用云端版本（拉取新增、拉取合并、推送被云端更晚版本覆盖）时，**连同云端的 `UpdatedAt` 一起采用**（新增时含 `CreatedAt`）。

否则本机会把云端版本盖成"拉取那一刻"，之后云端里更晚发生的编辑（时间戳仍早于本机的拉取时刻，
例如两端时钟有偏差，或另一台设备离线编辑后才同步）会被判为"比本机旧"而被永远忽略。

## 决策 3：推送账本时，所有者以 JWT 为准

`SyncController.PushLedgers` 强制 `entity.UserId = JWT 用户`，忽略 DTO 里的 `OwnerId`。
`OwnerId` 只用于拉取方向（共享账本拉到成员端时标明真正的所有者）。此前直接信任它：
正常客户端自报的是本地 Id（云端不存在 → 外键失败，账本推不上来），
恶意客户端则可以把账本建到别人名下。

## 决策 4：云端令牌必须属于当前登录的手机号

本地登录不产生云端令牌，`HttpService` 用已登录的手机号向云端换取，存进本地存储。同一设备换号登录时，
上一个手机号的令牌还留着的话，新账号的数据会带着旧令牌推进旧账号的云端数据；再经决策 1 的映射，
旧账号的云端数据还会被当成新账号自己的拉下来。两道防线：

- `AuthenticationService.SaveUserInfoAsync` 发现手机号变了就 `ClearTokenAsync()`；
- `HttpService.EnsureRemoteTokenAsync` 复用令牌前用 `JwtClaimReader.TryReadPhoneNumber` 核对令牌里的手机号，
  不一致就丢弃并按当前手机号重新换取。令牌里读不到手机号（旧令牌 / 非本服务签发）或本地没有手机号时无法判断，
  按"属于"处理以保持原有行为。

## 决策 5：预设数据的信任边界

预设分类 / 支付渠道（`UserId == null && IsPreset`）对所有用户可见，只应由服务端启动时的 `PresetDataService` 创建。
`BillSyncDto.EnsureCategoryExistsAsync` / `EnsurePaymentChannelExistsAsync` 的 `allowCreatePreset` 参数划出边界：

- 服务端处理客户端**推送**的账单时为 `false`：账单引用了云端没有的预设，只补建成推送者的私有分类 / 渠道，
  不能让客户端凭自报的 `IsPreset` 往全局预设里塞数据；
- 客户端**拉取**云端账单时为 `true`：这是云端已确认的预设，本地缺了就按预设补建（自愈）。

## 决策 6：成员关系的本地合并只有一个入口

拉取成员关系、邀请接口返回的成员行，都走 `ISyncService.MergeLedgerMembersAsync`，`HttpLedgerMemberService`
不再自带一份合并逻辑：用户 Id 映射、用户存根、账本存根、`(账本, 用户)` 对账、沿用云端更新时间。
此前它自己合并却不做用户 Id 映射，受邀人的"待确认邀请 / 我的共享账本"挂在云端用户 Id 名下，按本地用户查永远是空的。

- 用户存根默认带云端下发的真实手机号；手机号有唯一索引，已被本机另一个账号占用时退回占位手机号（`stub_…`），
  否则整个合并都会失败；
- 成员关系引用的账本本地可能还没有（刚收到别人的邀请），按 DTO 附带的名称 / 图标补建账本存根以满足外键。

## 决策 7：云端不可用时，读路径不能被拖住

账单、账本、记一笔、成员管理页面加载都会经过 `HttpLedgerMemberService` 的读路径（成员列表 / 待确认邀请 / 共享账本）：
先联网刷新成员缓存，再读本地。云端连不上时每次刷新都要等连接失败——部分环境下好几秒
（Windows 上被拒绝的回环连接约 2 秒，`localhost` 双栈约 4 秒），`HttpClient` 默认超时还有 100 秒——页面被一次次拖住。三层处理：

1. **不多连**：刷新先拉取、再读云端用户 Id（见决策 1），失败只算一次连接；`AddBill` 先渲染表单、再加载共享账本。
2. **期限**：读路径刷新的**联网部分**（换取令牌 + 拉取成员关系，合计）只给 `CloudAvailability.RefreshTimeout`（3 秒）。
   `CancellationToken` 一路传到 `HttpClient`（`HttpService → HttpCloudSyncClient → SyncService`），到期**真正取消请求**，
   不遗留后台任务；`EnsureRemoteTokenAsync` 对调用方的取消照常向上抛（不当成"换取失败"吞掉再去发后面的请求）。
   取消发生在拉取阶段，本地缓存原样不动；拉取成功之后的本地合并不受取消影响（避免合并做到一半被打断），
   它不计入这 3 秒——只在本地数据量很大时才会明显超过毫秒级。
3. **退避**：刷新因连接失败 / 超时而失败后，`CloudAvailability.DefaultBackoff`（30 秒）之内读路径直接读本地缓存，不再联网。

- 只有 `HttpRequestException` 和"我们设的期限到了"才计入退避，这是有意收窄的：
  - 页面（电路）被销毁、`HttpClient` 自己的超时、本地写库失败等不计——`CloudAvailability` 是单例
    （描述"设备 / 服务进程到云端"的连通性，Blazor Server 下所有电路共享），不能让一个用户关掉页面就让所有人跳过联网；
  - 云端可达但返回 4xx / 5xx（例如网关 502）也不计：它们是快速失败，不会拖慢页面，而 `HttpService` 把它们抛成普通异常，
    刷新处按意外错误记 Warning——这条日志正是"云端在报错"的信号，不该被退避吞掉。读路径遇到 401 照旧登出（见 CLAUDE.md 决策 7）。
- 用户主动发起的云端操作（响应邀请 / 移除成员 / 修改角色 / 转让所有权 / 退出账本 / 邀请）**一成功就清除退避**
  （云端刚确认可达），不管随后的刷新结果如何；操作后的刷新**不受期限和退避限制**
  （刷新结果关系到这次操作有没有反映到本地）。
- 取舍：3 秒对很慢的移动网络偏紧——换取令牌 + 拉取两个串行请求超过 3 秒时本次刷新放弃，30 秒内显示本地缓存
  （成员 / 邀请可能略旧），之后自动恢复；手动同步和各项操作不受影响。数值是 `CloudAvailability` 里的常量，需要时一处调整。

## 已知限制（未改）

- 服务端 `UpdatedAt` 盖的是**接收时刻**，而"后写入优先"比较的是**客户端编辑时刻**，二者口径不同：
  时钟偏差较大或长时间离线后推送，可能与直觉不符。服务端的盖章同时是拉取游标（`since`）的依据，
  改动它属于协议层变更，需另行决策。
- 分类/支付渠道/账本每次全量推送，已存在的行在推送阶段就被云端结果合并，
  拉取阶段"合并已有行"的分支对它们基本不可达（保留作为冗余保险）。
- 同步与界面共用一个 `DbContext`（MAUI 的 Scoped 等同单例）本就不支持并发使用；
  `SyncTimestampScope` 只按异步流程区分，不改变这一前提。
  `ApplicationDbContext.DiscardPendingChanges`（保存失败后丢弃本次未提交的变更）同样假定同一时刻只有一个保存流程在用这个上下文。
- `Bills.razor` 仍然先等成员刷新、再加载账单列表：云端不可用时现在最多多等 3 秒（退避期内几乎为 0），没有改成"先渲染本地账单、共享账本稍后补上"。

## 测试

`TwoDeviceSyncTests`：真实 `SyncController` + 真实 `SyncService` + 三个 SQLite（两台设备 + 云端），
覆盖映射、对账、归还、各实体的时间戳采用，以及伪造所有者；
`SyncTimestampScopeTests`、`JwtClaimReaderTests` 覆盖作用域与 JWT 解析（用户 Id、手机号）；
`CloudTokenOwnershipTests`：换号作废与令牌归属核对（决策 4）；`SyncTrustBoundaryTests`：预设数据的信任边界（决策 5）；
`HttpLedgerMemberServiceTests`：受邀人 / 所有者两条成员关系链路，含手机号被占用（决策 6）、邀请成功清除退避（决策 7）；
`HttpLedgerMemberServiceOfflineTests`：云端不可用时的连接次数与日志级别，以及决策 7（期限真正取消请求、退避、
操作后的刷新不受限、哪些失败计入退避）；`CloudAvailabilityTests`：退避的进入 / 到期 / 清除与默认数值（3 秒 / 30 秒）。
