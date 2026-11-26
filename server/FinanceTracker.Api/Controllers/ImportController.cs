using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Exceptions;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Time;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

/// <summary>
/// 导入控制器
/// </summary>
public class ImportController : BaseApiController
{
    private readonly ICsvParserService _csvParserService;
    private readonly IBillService _billService;
    private readonly ICategoryService _categoryService;
    private readonly IPaymentChannelService _paymentChannelService;
    private readonly ILedgerMemberService _ledgerMemberService;

    public ImportController(
        ICsvParserService csvParserService,
        IBillService billService,
        ICategoryService categoryService,
        IPaymentChannelService paymentChannelService,
        ILedgerMemberService ledgerMemberService)
    {
        _csvParserService = csvParserService;
        _billService = billService;
        _categoryService = categoryService;
        _paymentChannelService = paymentChannelService;
        _ledgerMemberService = ledgerMemberService;
    }

    /// <summary>
    /// 导入微信账单
    /// </summary>
    /// <param name="request">CSV 内容与目标账本</param>
    /// <returns>导入结果</returns>
    [HttpPost("wechat")]
    public async Task<IActionResult> ImportWeChatBill([FromBody] ImportRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CsvContent))
        {
            return BadRequest(new { message = "CSV 内容不能为空" });
        }

        if (!IsValidUtcOffset(request.UtcOffsetMinutes))
        {
            return BadRequest(new { message = InvalidUtcOffsetMessage });
        }

        var userId = GetUserId();

        var ledgerError = await ValidateImportLedgerAsync(userId, request.LedgerId);
        if (ledgerError != null)
        {
            return ledgerError;
        }

        var importedBills = await _csvParserService.ParseWeChatCsvAsync(request.CsvContent);

        return await ProcessImportedBills(userId, importedBills, "微信支付", request.UtcOffsetMinutes, request.LedgerId!.Value);
    }

    /// <summary>
    /// 导入支付宝账单
    /// </summary>
    /// <param name="request">CSV 内容与目标账本</param>
    /// <returns>导入结果</returns>
    [HttpPost("alipay")]
    public async Task<IActionResult> ImportAlipayBill([FromBody] ImportRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CsvContent))
        {
            return BadRequest(new { message = "CSV 内容不能为空" });
        }

        if (!IsValidUtcOffset(request.UtcOffsetMinutes))
        {
            return BadRequest(new { message = InvalidUtcOffsetMessage });
        }

        var userId = GetUserId();

        var ledgerError = await ValidateImportLedgerAsync(userId, request.LedgerId);
        if (ledgerError != null)
        {
            return ledgerError;
        }

        var importedBills = await _csvParserService.ParseAlipayCsvAsync(request.CsvContent);

        return await ProcessImportedBills(userId, importedBills, "支付宝", request.UtcOffsetMinutes, request.LedgerId!.Value);
    }

    /// <summary>
    /// 预览微信账单（仅解析 CSV，不入库，供导入页面"预览-确认"流程使用）
    /// </summary>
    /// <param name="request">CSV 内容</param>
    /// <returns>解析出的账单列表</returns>
    [HttpPost("wechat/preview")]
    public async Task<IActionResult> PreviewWeChatBill([FromBody] ImportRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CsvContent))
        {
            return BadRequest(new { message = "CSV 内容不能为空" });
        }

        // 跳过中性交易（充值/提现/零钱通存取等）与已撤销等无效交易，避免污染收支统计
        var parseResult = await _csvParserService.ParseWeChatCsvWithStatsAsync(request.CsvContent);

        return Ok(new
        {
            totalCount = parseResult.Bills.Count,
            skippedCount = parseResult.SkippedCount,
            bills = parseResult.Bills
        });
    }

    /// <summary>
    /// 预览支付宝账单（仅解析 CSV，不入库，供导入页面"预览-确认"流程使用）
    /// </summary>
    /// <param name="request">CSV 内容</param>
    /// <returns>解析出的账单列表</returns>
    [HttpPost("alipay/preview")]
    public async Task<IActionResult> PreviewAlipayBill([FromBody] ImportRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CsvContent))
        {
            return BadRequest(new { message = "CSV 内容不能为空" });
        }

        var importedBills = await _csvParserService.ParseAlipayCsvAsync(request.CsvContent);

        return Ok(new { totalCount = importedBills.Count, bills = importedBills });
    }

    private const string InvalidUtcOffsetMessage = "时区偏移必须在 -840 到 840 分钟之间";

    /// <summary>
    /// 导入目标账本校验：账本必填；并且当前用户必须有该账本的写权限
    /// （自有账本，或拥有 Editor 以上角色的共享账本——否则任何人都能把账单灌进别人的账本，见 ADR 0004）。
    /// 放行返回 null。
    /// </summary>
    private async Task<IActionResult?> ValidateImportLedgerAsync(Guid userId, Guid? ledgerId)
    {
        if (ledgerId is null || ledgerId == Guid.Empty)
        {
            return BadRequest(new { message = "请选择要导入到的账本" });
        }

        return await CheckLedgerWritableAsync(_ledgerMemberService, ledgerId.Value, userId);
    }

    /// <summary>
    /// 客户端上报的 UTC 偏移（分钟）是否合法：未上报视为合法；全球时区范围为 UTC-12 ~ UTC+14
    /// </summary>
    private static bool IsValidUtcOffset(int? utcOffsetMinutes)
    {
        return utcOffsetMinutes is null or >= -840 and <= 840;
    }

    private async Task<IActionResult> ProcessImportedBills(
        Guid userId,
        List<Core.Interfaces.ImportedBill> importedBills,
        string defaultChannel,
        int? utcOffsetMinutes,
        Guid ledgerId)
    {
        var categories = await _categoryService.GetCategoriesAsync(userId);
        var channels = await _paymentChannelService.GetPaymentChannelsAsync(userId);

        var successCount = 0;
        var failCount = 0;
        var errors = new List<string>();

        for (var i = 0; i < importedBills.Count; i++)
        {
            var importedBill = importedBills[i];
            try
            {
                // 匹配分类
                var category = MatchCategory(importedBill, categories, importedBill.IsIncome ? BillType.Income : BillType.Expense);

                // 匹配支付渠道
                var channel = MatchChannel(importedBill, channels, defaultChannel);

                var bill = new Bill
                {
                    UserId = userId,
                    Amount = importedBill.Amount,
                    Type = importedBill.IsIncome ? BillType.Income : BillType.Expense,
                    CategoryId = category?.Id ?? categories
                        .First(c => c.Name == (importedBill.IsIncome ? "其他收入" : "其他支出")).Id,
                    PaymentChannelId = channel?.Id ?? channels.First(c => c.Name == defaultChannel).Id,
                    LedgerId = ledgerId,
                    // CSV 里的交易时间是不带时区的用户本地时间：客户端上报了偏移就按该偏移换算为 UTC；
                    // 没上报（旧版客户端）则交给 BillService 按服务器应用时区解释
                    TransactionTime = utcOffsetMinutes.HasValue
                        ? AppTimeZone.ToUtc(importedBill.TransactionTime, TimeSpan.FromMinutes(utcOffsetMinutes.Value))
                        : importedBill.TransactionTime,
                    Note = importedBill.Description,
                    Source = BillSource.Import,
                    SyncStatus = SyncStatus.Pending
                };

                await _billService.CreateBillAsync(bill);
                successCount++;
            }
            catch (Exception ex)
            {
                failCount++;
                errors.Add($"第 {i + 1} 条: {ex.GetUserMessage()}");
            }
        }

        return Ok(new
        {
            totalCount = importedBills.Count,
            successCount,
            failCount,
            errors = errors.Take(10).ToList() // 只返回前10个错误
        });
    }

    private Category? MatchCategory(ImportedBill importedBill, List<Category> categories, BillType type)
    {
        var description = importedBill.Description.ToLower();
        var merchantName = importedBill.MerchantName.ToLower();

        // 简单的关键词匹配规则
        var categoryRules = new Dictionary<string, string[]>
        {
            { "餐饮美食", new[] { "餐饮", "美食", "外卖", "美团", "饿了么", "肯德基", "麦当劳", "星巴克" } },
            { "交通出行", new[] { "交通", "出行", "滴滴", "打车", "地铁", "公交", "加油" } },
            { "日用百货", new[] { "日用", "百货", "超市", "便利店", "沃尔玛" } },
            { "购物消费", new[] { "购物", "消费", "淘宝", "京东", "拼多多" } },
            { "娱乐休闲", new[] { "娱乐", "休闲", "电影", "游戏", "旅游" } },
            { "医疗健康", new[] { "医疗", "健康", "医院", "药店" } },
            { "教育培训", new[] { "教育", "培训", "学费" } },
            { "居住生活", new[] { "居住", "生活", "房租", "水电", "物业" } },
            { "通讯物流", new[] { "通讯", "物流", "快递", "话费" } }
        };

        foreach (var rule in categoryRules)
        {
            if (rule.Value.Any(keyword => description.Contains(keyword) || merchantName.Contains(keyword)))
            {
                return categories.FirstOrDefault(c => c.Name == rule.Key && c.Type == type);
            }
        }

        return categories.FirstOrDefault(c => c.Name == (type == BillType.Expense ? "其他支出" : "其他收入"));
    }

    private PaymentChannel? MatchChannel(ImportedBill importedBill, List<PaymentChannel> channels, string defaultChannel)
    {
        var channelName = importedBill.PaymentChannel;

        if (string.IsNullOrEmpty(channelName))
        {
            return channels.FirstOrDefault(c => c.Name == defaultChannel);
        }

        return channels.FirstOrDefault(c => c.Name.Contains(channelName)) ??
               channels.FirstOrDefault(c => c.Name == defaultChannel);
    }
}

/// <summary>
/// 导入请求体（CSV 内容 + 目标账本）
/// </summary>
public class ImportRequest
{
    /// <summary>
    /// CSV 文件内容
    /// </summary>
    public string CsvContent { get; set; } = string.Empty;

    /// <summary>
    /// 导入到哪个账本（正式导入必填；仅解析不入库的预览接口忽略它）。
    /// 类型保持可空，是为了缺省时返回"请选择要导入到的账本"，而不是被反序列化成 Guid.Empty 后报"账本不存在"
    /// </summary>
    public Guid? LedgerId { get; set; }

    /// <summary>
    /// CSV 中交易时间所属时区相对 UTC 的偏移（分钟，如东八区为 480；可选）。
    /// 微信/支付宝导出的交易时间不带时区，服务器无从得知用户在哪个时区，应由客户端上报；
    /// 未上报时按服务器时区解释——服务器为 UTC 而用户在东八区时，账单时间会差 8 小时。
    /// </summary>
    public int? UtcOffsetMinutes { get; set; }
}
