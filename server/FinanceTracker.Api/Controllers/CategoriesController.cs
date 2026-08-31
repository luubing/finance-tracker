using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FinanceTracker.Api.Controllers;

/// <summary>
/// 分类控制器
/// </summary>
public class CategoriesController : BaseApiController
{
    private readonly ICategoryService _categoryService;

    public CategoriesController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    /// <summary>
    /// 获取分类列表
    /// </summary>
    /// <param name="type">分类类型（Expense/Income）</param>
    /// <returns>分类列表</returns>
    [HttpGet]
    public async Task<IActionResult> GetCategories([FromQuery] BillType? type = null)
    {
        var userId = GetUserId();
        var categories = await _categoryService.GetCategoriesAsync(userId, type);

        return Ok(categories.Select(MapToResponse));
    }

    /// <summary>
    /// 获取分类详情
    /// </summary>
    /// <param name="id">分类ID</param>
    /// <returns>分类信息</returns>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetCategory(Guid id)
    {
        var userId = GetUserId();
        var category = await _categoryService.GetCategoryByIdAsync(id);

        if (category == null || (!category.IsPreset && category.UserId != userId))
        {
            return NotFound(new { message = "分类不存在" });
        }

        return Ok(MapToResponse(category));
    }

    /// <summary>
    /// 创建自定义分类
    /// </summary>
    /// <param name="request">分类请求</param>
    /// <returns>创建的分类</returns>
    [HttpPost]
    public async Task<IActionResult> CreateCategory([FromBody] CategoryRequest request)
    {
        var userId = GetUserId();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "分类名称不能为空" });
        }

        var category = new Core.Entities.Category
        {
            UserId = userId,
            Name = request.Name,
            Icon = request.Icon ?? "mdi-tag",
            Type = request.Type,
            SortOrder = request.SortOrder
        };

        var createdCategory = await _categoryService.CreateCategoryAsync(category);

        return CreatedAtAction(nameof(GetCategory), new { id = createdCategory.Id }, MapToResponse(createdCategory));
    }

    /// <summary>
    /// 更新自定义分类
    /// </summary>
    /// <param name="id">分类ID</param>
    /// <param name="request">分类请求</param>
    /// <returns>更新的分类</returns>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] CategoryRequest request)
    {
        var userId = GetUserId();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "分类名称不能为空" });
        }

        // 检查分类是否存在且属于当前用户
        var existingCategory = await _categoryService.GetCategoryByIdAsync(id);
        if (existingCategory == null || (!existingCategory.IsPreset && existingCategory.UserId != userId))
        {
            return NotFound(new { message = "分类不存在" });
        }

        if (existingCategory.IsPreset)
        {
            return Forbid("不能修改预设分类");
        }

        var category = new Core.Entities.Category
        {
            Id = id,
            UserId = userId,
            Name = request.Name,
            Icon = request.Icon ?? "mdi-tag",
            Type = request.Type,
            SortOrder = request.SortOrder
        };

        try
        {
            var updatedCategory = await _categoryService.UpdateCategoryAsync(category);
            return Ok(MapToResponse(updatedCategory));
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 删除自定义分类
    /// </summary>
    /// <param name="id">分类ID</param>
    /// <returns>删除结果</returns>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCategory(Guid id)
    {
        var userId = GetUserId();

        try
        {
            var result = await _categoryService.DeleteCategoryAsync(id, userId);

            if (!result)
            {
                return NotFound(new { message = "分类不存在" });
            }

            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private static object MapToResponse(Core.Entities.Category category) => new
    {
        id = category.Id,
        name = category.Name,
        icon = category.Icon,
        type = category.Type.ToString(),
        isPreset = category.IsPreset,
        sortOrder = category.SortOrder
    };
}

/// <summary>
/// 分类请求
/// </summary>
public class CategoryRequest
{
    /// <summary>
    /// 分类名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 图标
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// 分类类型
    /// </summary>
    public BillType Type { get; set; }

    /// <summary>
    /// 排序顺序
    /// </summary>
    public int SortOrder { get; set; }
}
