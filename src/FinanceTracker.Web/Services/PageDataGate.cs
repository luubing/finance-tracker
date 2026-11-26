namespace FinanceTracker.Web.Services;

/// <summary>
/// 页面读取数据的「单车道」。
/// <para>
/// 页面读库用的 <c>DbContext</c> 是 Scoped：MAUI Blazor Hybrid 里等同于应用级单例，Blazor Server 里是同一条电路里共用一个，
/// 而 EF Core 不允许同一个上下文并行执行查询（"A second operation was started on this context instance"）。
/// 四个 Tab 根页面常驻（页面栈保活），一次账单变更会让首页、账单页、统计页同时「重新加载本页数据」，
/// 所以所有页面里「查库刷新」的入口（初次加载、下拉刷新、账单变更事件、二级页面关闭后刷新……）都要经过这个闸门串行执行。
/// </para>
/// <para>
/// 约定：只在最外层使用；<b>不可重入</b>——闸门里的动作不能再调用闸门（信号量不可重入，会死锁）。
/// </para>
/// <list type="bullet">
/// <item>读本地库的刷新：都进闸门。</item>
/// <item>只写本地库的保存 / 删除 / 批量修改（新建分类、保存账本、删除账单……）：也进闸门——用户点保存时后台正好有别的页面在重新加载并不少见，写与读同样不能并行。</item>
/// <item>读共享账本、账本成员这类「顺带联网刷新成员缓存」的读取：可以进闸门——联网刷新只有 3 秒上限、失败后 30 秒内直接读本地缓存，占住闸门的时间有界（CLAUDE.md 同步身份与时间戳一节）。</item>
/// <item>真正要联网的用户操作（同步、导入、邀请 / 移除 / 转让成员）：<b>不要</b>放进闸门——网络请求没有这么短的上限，会把所有页面的加载堵住；
/// 它们在网络返回后写本地库的那一小段目前仍不经过闸门（已知的遗留，要补需要把服务拆成「联网」与「落库」两步）。</item>
/// </list>
/// </summary>
public sealed class PageDataGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>排队执行一个会读库的动作；前一个动作结束后才开始</summary>
    public async Task RunAsync(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        await _gate.WaitAsync();
        try
        {
            await work();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>排队执行一个会读库的动作并返回结果</summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        await _gate.WaitAsync();
        try
        {
            return await work();
        }
        finally
        {
            _gate.Release();
        }
    }
}
