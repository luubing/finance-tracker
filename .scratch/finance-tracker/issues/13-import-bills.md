# 13 — 导入账单功能

**What to build:** 支持导入微信和支付宝导出的 CSV 账单文件。

**Blocked by:** None

**Status:** completed

- [x] 创建 CSV 解析服务接口 (ICsvParserService)
- [x] 实现微信账单 CSV 解析器
- [x] 实现支付宝账单 CSV 解析器
- [x] 创建导入 API (POST /api/import/wechat, POST /api/import/alipay)
- [x] 创建导入页面（文件选择、预览、确认）
- [x] 自动识别分类（根据商户名/交易描述）
- [x] 导入结果统计
