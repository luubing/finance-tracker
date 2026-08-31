using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 分类服务实现
/// </summary>
public class CategoryService : ICategoryService
{
    private readonly IApplicationDbContext _context;

    public CategoryService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<Category>> GetCategoriesAsync(Guid? userId, BillType? type = null)
    {
        var query = _context.Categories
            .Where(c => c.IsPreset || (userId.HasValue && c.UserId == userId.Value));

        if (type.HasValue)
        {
            query = query.Where(c => c.Type == type.Value);
        }

        return await query
            .OrderBy(c => c.Type)
            .ThenBy(c => c.SortOrder)
            .ToListAsync();
    }

    public async Task<Category?> GetCategoryByIdAsync(Guid categoryId)
    {
        return await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == categoryId);
    }

    public async Task<Category> CreateCategoryAsync(Category category)
    {
        // 确保是用户自定义分类
        category.IsPreset = false;
        category.Id = Guid.NewGuid();

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        return category;
    }

    public async Task<Category> UpdateCategoryAsync(Category category)
    {
        var existingCategory = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == category.Id && c.UserId == category.UserId);

        if (existingCategory == null)
        {
            throw new ArgumentException("分类不存在");
        }

        if (existingCategory.IsPreset)
        {
            throw new UnauthorizedAccessException("不能修改预设分类");
        }

        existingCategory.Name = category.Name;
        existingCategory.Icon = category.Icon;
        existingCategory.Type = category.Type;
        existingCategory.SortOrder = category.SortOrder;

        await _context.SaveChangesAsync();

        return existingCategory;
    }

    public async Task<bool> DeleteCategoryAsync(Guid categoryId, Guid userId)
    {
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == categoryId && c.UserId == userId);

        if (category == null)
        {
            return false;
        }

        if (category.IsPreset)
        {
            throw new UnauthorizedAccessException("不能删除预设分类");
        }

        // 检查是否有关联的账单
        var hasBills = await _context.Bills
            .AnyAsync(b => b.CategoryId == categoryId);

        if (hasBills)
        {
            throw new InvalidOperationException("该分类下有账单记录，无法删除");
        }

        category.IsDeleted = true;
        await _context.SaveChangesAsync();

        return true;
    }
}
