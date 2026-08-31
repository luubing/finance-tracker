using FinanceTracker.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

/// <summary>
/// 分类控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly IPresetDataService _presetDataService;

    public CategoriesController(IPresetDataService presetDataService)
    {
        _presetDataService = presetDataService;
    }

    /// <summary>
    /// 获取分类列表
    /// </summary>
    /// <param name="userId">用户ID（可选）</param>
    /// <returns>分类列表</returns>
    [HttpGet]
    public async Task<IActionResult> GetCategories([FromQuery] Guid? userId = null)
    {
        var categories = await _presetDataService.GetCategoriesAsync(userId);

        return Ok(categories.Select(c => new
        {
            id = c.Id,
            name = c.Name,
            icon = c.Icon,
            type = c.Type.ToString(),
            isPreset = c.IsPreset,
            sortOrder = c.SortOrder
        }));
    }
}
