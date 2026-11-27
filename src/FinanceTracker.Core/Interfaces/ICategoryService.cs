using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;

namespace FinanceTracker.Core.Interfaces;

/// <summary>
/// 分类服务接口
/// </summary>
public interface ICategoryService
{
    /// <summary>
    /// 获取分类列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="type">分类类型（可选）</param>
    /// <returns>分类列表</returns>
    Task<List<Category>> GetCategoriesAsync(Guid? userId, BillType? type = null);

    /// <summary>
    /// 根据ID获取分类
    /// </summary>
    /// <param name="categoryId">分类ID</param>
    /// <returns>分类信息</returns>
    Task<Category?> GetCategoryByIdAsync(Guid categoryId);

    /// <summary>
    /// 创建自定义分类
    /// </summary>
    /// <param name="category">分类信息</param>
    /// <returns>创建的分类</returns>
    Task<Category> CreateCategoryAsync(Category category);

    /// <summary>
    /// 更新自定义分类：名称 / 图标 / 类型 / 排序 / 「不计入统计」开关都按传入的值整体覆盖（不是「只改传了的字段」）
    /// </summary>
    /// <param name="category">分类信息，字段要带完整；REST 里「请求没带开关 = 保持原值」由控制器先合并成完整实体再调用这里</param>
    /// <returns>更新的分类</returns>
    Task<Category> UpdateCategoryAsync(Category category);

    /// <summary>
    /// 删除自定义分类（软删除）
    /// </summary>
    /// <param name="categoryId">分类ID</param>
    /// <param name="userId">用户ID</param>
    /// <returns>是否成功</returns>
    Task<bool> DeleteCategoryAsync(Guid categoryId, Guid userId);
}
