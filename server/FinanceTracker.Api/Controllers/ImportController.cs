using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
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

    public ImportController(
        ICsvParserService csvParserService,
        IBillService billService,
        ICategoryService categoryService,
        IPaymentChannelService paymentChannelService)
    {
        _csvParserService = csvParserService;
        _billService = billService;
        _categoryService = categoryService;
        _paymentChannelService = paymentChannelService;
    }

    /// <summary>
    /// 导入微信账单
    /// </summary>
    /// <param name="request">CSV 内容</param>
    /// <returns>导入结果</returns>
    [HttpPost("wechat")]
    public async Task<IActionResult> ImportWeChatBill([FromBody] ImportRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CsvContent))
        {
            return BadRequest(new { message = "CSV 内容不能为空" });
        }

        var userId = GetUserId();

        var importedBills = await _csvParserService.ParseWeChatCsvAsync(request.CsvContent);

        return await ProcessImportedBills(userId, importedBills, "微信支付");
    }

    /// <summary>
    /// 导入支付宝账单
    /// </summary>
    /// <param name="request">CSV 内容</param>
    /// <returns>导入结果</returns>
    [HttpPost("alipay")]
    public async Task<IActionResult> ImportAlipayBill([FromBody] ImportRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CsvContent))
        {
            return BadRequest(new { message = "CSV 内容不能为空" });
        }

        var userId = GetUserId();

        var importedBills = await _csvParserService.ParseAlipayCsvAsync(request.CsvContent);

        return await ProcessImportedBills(userId, importedBills, "支付宝");
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

        var importedBills = await _csvParserService.ParseWeChatCsvAsync(request.CsvContent);

        return Ok(new { totalCount = importedBills.Count, bills = importedBills });
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

    private async Task<IActionResult> ProcessImportedBills(Guid userId, List<Core.Interfaces.ImportedBill> importedBills, string defaultChannel)
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
                    TransactionTime = importedBill.TransactionTime,
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
                errors.Add($"第 {i + 1} 条: {ex.Message}");
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
/// 导入请求体（CSV 内容）
/// </summary>
public class ImportRequest
{
    /// <summary>
    /// CSV 文件内容
    /// </summary>
    public string CsvContent { get; set; } = string.Empty;
}
