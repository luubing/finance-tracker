# FinanceTracker 实现计划

## 项目概述

个人记账应用，支持多支付渠道、分类统计和云端同步。

### 核心功能
- 手动记账（支出/收入）
- 分类管理（预设+自定义）
- 支付渠道管理（预设+自定义）
- 账单列表查看
- 月度统计（饼图）
- 趋势图（折线图）
- 用户注册/登录（手机号）
- 云端同步

### 第二版功能
- 导入账单（微信/支付宝CSV）
- 短信识别（Android）
- 年度报表

---

## 项目结构

```
FinanceTracker/
├── src/
│   ├── FinanceTracker.App/                 # MAUI Blazor 主应用
│   │   ├── Components/                     # Blazor组件
│   │   ├── Pages/                          # 页面
│   │   ├── Services/                       # 本地服务
│   │   ├── Platforms/                      # 平台特定代码
│   │   └── wwwroot/                        # 静态资源
│   ├── FinanceTracker.Core/                # 核心业务逻辑
│   │   ├── Entities/                       # 实体定义
│   │   ├── Enums/                          # 枚举定义
│   │   ├── Interfaces/                     # 接口定义
│   │   └── Services/                       # 业务服务
│   ├── FinanceTracker.Shared/              # 共享模型/DTO
│   │   ├── Models/                         # 数据传输对象
│   │   └── Constants/                      # 常量定义
│   └── FinanceTracker.Infrastructure/      # 数据访问/外部服务
│       ├── Data/                           # EF Core上下文
│       ├── Repositories/                   # 仓储实现
│       └── Services/                       # 外部服务实现
├── server/
│   └── FinanceTracker.Api/                 # ASP.NET Core Web API
│       ├── Controllers/                    # API控制器
│       ├── Services/                       # 业务服务
│       └── Middleware/                     # 中间件
└── docs/
    └── adr/                                # 架构决策记录
```

---

## 数据模型

### 核心实体

#### User (用户)
```csharp
public class User
{
    public Guid Id { get; set; }
    public string PhoneNumber { get; set; }  // 唯一标识
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
```

#### Bill (账单)
```csharp
public class Bill
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }        // 金额（正数）
    public BillType Type { get; set; }         // 支出/收入
    public Guid CategoryId { get; set; }
    public Guid PaymentChannelId { get; set; }
    public DateTime TransactionTime { get; set; }
    public string? Note { get; set; }          // 备注
    public BillSource Source { get; set; }     // 数据来源
    public SyncStatus SyncStatus { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

#### Category (分类)
```csharp
public class Category
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }          // null表示预设分类
    public string Name { get; set; }
    public string Icon { get; set; }
    public BillType Type { get; set; }         // 支出/收入
    public bool IsPreset { get; set; }
    public int SortOrder { get; set; }
    public bool IsDeleted { get; set; }
}
```

#### PaymentChannel (支付渠道)
```csharp
public class PaymentChannel
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }          // null表示预设渠道
    public string Name { get; set; }
    public string Icon { get; set; }
    public bool IsPreset { get; set; }
    public int SortOrder { get; set; }
    public bool IsDeleted { get; set; }
}
```

### 枚举定义

```csharp
public enum BillType
{
    Expense = 0,  // 支出
    Income = 1    // 收入
}

public enum BillSource
{
    Manual = 0,       // 手动
    Import = 1,       // 导入
    SmsRecognition = 2, // 短信识别
    Notification = 3   // 通知栏
}

public enum SyncStatus
{
    Pending = 0,     // 待同步
    Synced = 1,      // 已同步
    Failed = 2       // 同步失败
}
```

---

## API设计

### 认证相关
```
POST   /api/auth/register          # 注册（输入手机号）
POST   /api/auth/login             # 登录（输入手机号）
```

### 账单相关
```
GET    /api/bills                   # 获取账单列表（分页、筛选）
POST   /api/bills                   # 创建账单
PUT    /api/bills/{id}              # 更新账单
DELETE /api/bills/{id}              # 删除账单（软删除）
POST   /api/bills/sync              # 批量同步账单
```

### 分类相关
```
GET    /api/categories              # 获取分类列表
POST   /api/categories              # 创建自定义分类
PUT    /api/categories/{id}         # 更新分类
DELETE /api/categories/{id}         # 删除分类
```

### 支付渠道相关
```
GET    /api/payment-channels        # 获取支付渠道列表
POST   /api/payment-channels        # 创建自定义渠道
PUT    /api/payment-channels/{id}   # 更新渠道
DELETE /api/payment-channels/{id}   # 删除渠道
```

### 预算相关
```
GET    /api/budgets                            # 获取预算列表（?year=&month=&ledgerId=）
GET    /api/budgets/status                     # 预算执行情况（预算额/已用/剩余/百分比）
POST   /api/budgets                            # 创建预算
PUT    /api/budgets/{id}                       # 更新预算
DELETE /api/budgets/{id}                       # 删除预算（软删除）
```

### 共享账本相关
```
GET    /api/ledgers/{id}/members               # 获取账本成员列表
POST   /api/ledgers/{id}/members               # 邀请成员（Owner，按手机号）
PUT    /api/ledgers/{id}/members/{memberId}/role  # 修改成员角色（Owner）
DELETE /api/ledgers/{id}/members/{memberId}    # 移除成员（Owner）
POST   /api/ledgers/{id}/transfer-ownership    # 转让所有权（Owner）
POST   /api/ledgers/{id}/exit                  # 退出共享账本
GET    /api/invitations                        # 当前用户待处理邀请
POST   /api/invitations/{memberId}/respond     # 响应邀请（接受/拒绝）
```

### 统计相关
```
GET    /api/statistics/monthly      # 月度统计
GET    /api/statistics/trend        # 趋势数据
GET    /api/statistics/category     # 分类统计
GET    /api/statistics/annual       # 年度统计
GET    /api/statistics/year-over-year          # 同比分析
GET    /api/statistics/custom                  # 自定义时间范围报表
GET    /api/statistics/category-comparison     # 分类环比对比
```

### 同步相关（补充）
```
POST   /api/sync/push                # 批量推送账单（含共享账本写权限校验）
POST   /api/sync/pull                # 拉取账单（含共享账本内成员账单）
POST   /api/sync/ledgers/push        # 推送账本
POST   /api/sync/ledgers/pull        # 拉取账本（含共享账本）
POST   /api/sync/ledgermembers/pull  # 拉取账本成员关系（客户端缓存）
```

---

## 实现阶段

### 第一阶段：基础框架搭建 (1-2周)

#### 1.1 后端项目搭建
- [ ] 创建 ASP.NET Core Web API 项目
- [ ] 配置 EF Core + PostgreSQL
- [ ] 创建数据库迁移
- [ ] 实现基础 CRUD API

#### 1.2 移动端项目搭建
- [ ] 创建 MAUI Blazor 项目
- [ ] 配置 MASA Blazor
- [ ] 搭建基础页面结构
- [ ] 配置本地 SQLite

#### 1.3 核心实体实现
- [ ] 实现 User 实体
- [ ] 实现 Bill 实体
- [ ] 实现 Category 实体
- [ ] 实现 PaymentChannel 实体

### 第二阶段：核心功能开发 (2-3周)

#### 2.1 用户模块
- [ ] 注册/登录页面
- [ ] 手机号输入验证
- [ ] 本地会话管理

#### 2.2 记账模块
- [ ] 手动记账页面
- [ ] 金额输入组件
- [ ] 分类选择组件
- [ ] 支付渠道选择组件
- [ ] 日期时间选择

#### 2.3 账单列表
- [ ] 账单列表页面
- [ ] 按日期分组显示
- [ ] 筛选功能（日期、分类、渠道）
- [ ] 编辑/删除功能

#### 2.4 分类管理
- [ ] 分类列表页面
- [ ] 添加/编辑/删除分类
- [ ] 预设分类初始化

#### 2.5 支付渠道管理
- [ ] 渠道列表页面
- [ ] 添加/编辑/删除渠道
- [ ] 预设渠道初始化

### 第三阶段：统计与报表 (1-2周)

#### 3.1 月度统计
- [ ] 月度总览页面
- [ ] 支出/收入汇总
- [ ] 分类饼图

#### 3.2 趋势分析
- [ ] 趋势图页面
- [ ] 日/周/月趋势线图
- [ ] 数据筛选功能

### 第四阶段：数据同步 (1-2周)

#### 4.1 本地同步服务
- [ ] 实现同步队列
- [ ] 网络状态监听
- [ ] 离线数据缓存

#### 4.2 云端同步
- [ ] 实现同步API
- [ ] 冲突处理逻辑
- [ ] 同步状态管理

### 第五阶段：测试与优化 (1周)

#### 5.1 功能测试
- [ ] 单元测试
- [ ] 集成测试
- [ ] UI测试

#### 5.2 性能优化
- [ ] 数据库查询优化
- [ ] UI性能优化
- [ ] 内存优化

---

## 预设数据

### 预设分类（支出）
1. 餐饮美食
2. 交通出行
3. 日用百货
4. 购物消费
5. 娱乐休闲
6. 医疗健康
7. 教育培训
8. 居住生活
9. 通讯物流
10. 其他支出

### 预设分类（收入）
1. 工资薪酬
2. 奖金补贴
3. 投资理财
4. 兼职副业
5. 其他收入

### 预设支付渠道
1. 微信支付
2. 支付宝
3. 京东支付
4. 美团支付
5. 云闪付
6. Apple Pay
7. 现金
8. 银行卡
9. 信用卡

---

## 关键技术点

### 1. 本地数据库设计
- 使用 SQLite 作为本地存储
- 设计合理的索引提升查询性能
- 实现数据迁移机制

### 2. 同步机制设计
- 基于时间戳的变更检测
- 批量同步减少网络请求
- 冲突解决策略实现

### 3. 图表实现
- 使用 MASA Blazor 的图表组件
- 支持饼图、折线图
- 响应式设计适配不同屏幕

### 4. 离线支持
- 网络状态监听
- 离线队列管理
- 联网后自动同步

---

## 后续扩展

### 第二版功能
1. **导入账单**
   - 微信账单CSV解析
   - 支付宝账单CSV解析
   - 自动分类识别

2. **短信识别**
   - Android短信读取权限
   - 正则表达式匹配
   - 识别结果确认

3. **年度报表**
   - 年度总览
   - 同比/环比分析
   - 数据导出

### 第三版功能
> 拆解后的执行任务见 `.scratch/finance-tracker/issues/`（issue 16–20），已全部完成（2026-09-05）

1. **预算管理** ✅ 已实现（issue 16、17：Budget 实体/预算 CRUD API/预算页面/执行进度/超支提醒与本地通知）
2. **多账本** ✅ 已实现（多账本与数据隔离为既有能力；共享账本见 issue 18：LedgerMember 实体/邀请确认/角色权限/成员管理页面/共享账单拉取，ADR 0004）
3. **数据可视化增强** ✅ 已实现（issue 19：可复用图表组件、分类排行、日历热力图、累计曲线、构成对比；issue 20：自定义报表、分类环比对比、CSV 导出、报表分享）
