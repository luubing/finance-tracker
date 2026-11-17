# 14 — 短信识别功能（Android）

**What to build:** 读取 Android 支付类短信，自动提取金额和支付渠道。

**Blocked by:** None

**Status:** completed

- [x] 创建短信读取服务接口 (ISmsService)
- [x] 实现 Android 短信读取权限请求
- [x] 创建短信解析规则（正则表达式）：统一在 `Core/Services/PaymentContentParser`（见下方"解析规则"）
- [x] 支持微信支付短信识别
- [x] 支持支付宝短信识别
- [x] 支持银行短信识别
- [x] 创建短信识别结果确认页面
- [x] 识别失败的短信提示用户手动补全

## 解析规则（评审 P12）

短信识别页（`SmsRecognition.razor`）、短信广播接收器（`SmsBroadcastReceiver`）、通知栏监听（`NotificationCaptureService`）三个入口现在都调用 `PaymentContentParser.Parse`，页面里自带的 `ParseSmsContent` 已删除——此前页面与另外两个入口各有一份实现，同一条短信可能得到不同结果。

规则（只在 `PaymentContentParser` 维护，回归测试 `tests/unit/FinanceTracker.Tests/Services/PaymentContentParserTests.cs`）：

- 金额按"数字+元"、"人民币+数字"、"¥/￥+数字"、"金额+数字"的顺序取第一个能解析的；提取不到金额时 `Amount` 为 0，调用方据此跳过（接收器/监听）或打开手动补全对话框（页面）。
- 数字支持千分位（`1,234.50元` = 1234.50，此前会被截成 234.50）；货币符号同时支持半角 `¥` 与全角 `￥`；解析固定使用 Invariant 区域（此前页面用区域相关的 `decimal.Parse`）；数字超出 `decimal` 范围时按"没提取到"处理，不抛异常。
- 含"收入/到账/转入/退款"视为收入，否则为支出。
- 渠道按微信 → 支付宝（含花呗/余额宝）→ 京东（含白条）→ 美团 → 云闪付（含银联）的顺序取第一个命中，都不命中为"银行卡"。
- 已知限制：一条短信里同时有"支出50元，余额1000元"这类多个金额时只取第一个"数字+元"；"余额"在前时会取到余额。
