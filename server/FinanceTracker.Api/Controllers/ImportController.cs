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
    /// <param name="file">CSV 文件</param>
    /// <returns>导入结果</returns>
    [HttpPost("wechat")]
    public async Task<IActionResult> ImportWeChatBill(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "请选择文件" });
        }

        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "请上传 CSV 文件" });
        }

        var userId = GetUserId();

        using var reader = new StreamReader(file.OpenReadStream());
        var csvContent = await reader.ReadToEndAsync();

        var importedBills = await _csvParserService.ParseWeChatCsvAsync(csvContent);

        return await ProcessImportedBills(userId, importedBills, "微信支付");
    }

    /// <summary>
    /// 导入支付宝账单
    /// </summary>
    /// <param name="file">CSV 文件</param>
    /// <returns>导入结果</returns>
    [HttpPost("alipay")]
    public async Task<IActionResult> ImportAlipayBill(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "请选择文件" });
        }

        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "请上传 CSV 文件" });
        }

        var userId = GetUserId();

        using var reader = new StreamReader(file.OpenReadStream());
        var csvContent = await reader.ReadToEndAsync();

        var importedBills = await _csvParserService.ParseAlipayCsvAsync(csvContent);

        return await ProcessImportedBills(userId, importedBills, "支付宝");
    }

    private async Task<IActionResult> ProcessImportedBills(Guid userId, List<Core.Interfaces.ImportedBill> importedBills, string defaultChannel)
    {
        var categories = await _categoryService.GetCategoriesAsync(userId);
        var channels = await _paymentChannelService.GetPaymentChannelsAsync(userId);

        var successCount = 0;
        var failCount = 0;
        var errors = new List<string>();

        foreach (var importedBill in importedBills)
        {
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
                    CategoryId = category?.Id ?? categories.First(c => c.Name == "其他支出").Id,
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
                errors.Add($"行 {successCount + failCount}: {ex.Message}");
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
