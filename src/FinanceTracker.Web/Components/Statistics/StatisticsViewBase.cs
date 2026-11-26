using FinanceTracker.Core.Exceptions;
using FinanceTracker.Web.Components.Ui;
using FinanceTracker.Web.Services;
using Masa.Blazor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace FinanceTracker.Web.Components.Statistics;

/// <summary>
/// 统计 Tab 三个视图（月度 / 趋势 / 年度）共用的加载流程。
/// <para>
/// 视图只关心「取哪些数据、怎么摆」，下面这些每个视图都要做、又容易各写各的错的事情放在这里：
/// </para>
/// <list type="bullet">
/// <item>读库经 <see cref="PageDataGate"/> 排队：四个 Tab 根页面常驻、共用一个 DbContext，
/// 一次账单变更会让首页 / 账单页 / 统计页同时重新加载，不排队 EF Core 会报「第二个操作在上下文里开始」。</item>
/// <item>参数（用户、账本、视图自己声明的 <see cref="LoadKey"/>）变化时自动重新加载；
/// 视图内部状态（趋势的时间范围、年度的年份）变化时视图自己调 <see cref="ReloadAsync"/>。</item>
/// <item>排队等锁期间被更新的加载取代的旧加载直接放弃，只有最新一次真正查询（快速连点上一月 / 下一月时不会白查）。</item>
/// <item>订阅账单变更事件：记一笔 / 编辑 / 导入 / 删除之后自动重新加载（统计 Tab 常驻，用户回到这里时看到的必须是最新的）。</item>
/// <item>失败只弹 Snackbar 并保留上一次的数据；从未成功加载过时由视图显示「加载失败」占位（<see cref="LoadFailed"/>）。</item>
/// </list>
/// </summary>
public abstract class StatisticsViewBase : ComponentBase, IDisposable
{
    private int _loadVersion;
    private bool _disposed;
    private bool _hasLoadedKey;
    private (Guid UserId, Guid? LedgerId, object? Extra) _loadedKey;

    [Inject] protected BillEventService BillEvents { get; set; } = null!;

    [Inject] protected PageDataGate DataGate { get; set; } = null!;

    [Inject] protected IPopupService PopupService { get; set; } = null!;

    [Inject] protected ILogger<StatisticsViewBase> Logger { get; set; } = null!;

    /// <summary>当前登录用户（统计只算这个用户自己记的账单）</summary>
    [Parameter, EditorRequired] public Guid UserId { get; set; }

    /// <summary>账本筛选；null = 全部账本</summary>
    [Parameter] public Guid? LedgerId { get; set; }

    /// <summary>正在加载（含重新加载；重新加载时界面保留旧数据，只做标记）</summary>
    protected bool IsLoading { get; private set; }

    /// <summary>至少成功加载过一次（此前显示骨架）</summary>
    protected bool HasLoaded { get; private set; }

    /// <summary>最近一次加载失败，并且从未成功加载过（视图显示「加载失败」占位而不是一片空白）</summary>
    protected bool LoadFailed { get; private set; }

    /// <summary>
    /// 视图的加载状态（根元素的 data-state，E2E 观察用）：loading（首次加载中）/ ready / reloading（保留旧数据重新加载）/ failed（从未成功过）
    /// </summary>
    protected string StateKey => HasLoaded ? (IsLoading ? "reloading" : "ready") : LoadFailed ? "failed" : "loading";

    /// <summary>
    /// 视图自己的参数（月度的年月、是否有账本……）：和用户、账本一起决定「要不要重新加载」。
    /// 用值相等的类型（元组、record）；不依赖参数的视图返回 null。
    /// </summary>
    protected virtual object? LoadKey => null;

    /// <summary>
    /// 三栏 KPI（支出 / 收入 / 结余）。三个视图的栏标题与配色一致，只有各栏的 E2E 钩子不同，由视图给。
    /// </summary>
    protected static IReadOnlyList<KpiItem> BuildKpi(decimal expense, decimal income, string expenseTestId, string incomeTestId, string balanceTestId) =>
        KpiItem.Summary(expense, income, expenseTestId, incomeTestId, balanceTestId);

    /// <summary>
    /// 取数并写入视图的字段。在 <see cref="PageDataGate"/> 里执行（不要再进闸门，不可重入）；失败直接抛出。
    /// 实现要先取完所有数据再一起写入字段，避免渲染到「新的汇总 + 旧的明细」。
    /// </summary>
    protected abstract Task LoadAsync();

    protected override void OnInitialized()
    {
        BillEvents.BillChanged += OnBillChanged;
    }

    protected override async Task OnParametersSetAsync()
    {
        if (UserId == Guid.Empty)
        {
            return;
        }

        var key = (UserId, LedgerId, LoadKey);
        if (_hasLoadedKey && key.Equals(_loadedKey))
        {
            return;
        }

        _hasLoadedKey = true;
        _loadedKey = key;
        await ReloadAsync();
    }

    /// <summary>
    /// 重新加载（下拉刷新、二级页面关闭、视图内部状态变化时调用）。等加载结束才返回；失败不抛出（已提示）。
    /// <para>
    /// 父页面（统计 Tab 壳）的刷新入口是直接调用它：此时框架不会重新渲染这个视图（视图的参数没变，父页面重绘时会跳过），
    /// 所以开始与结束都要自己 <c>StateHasChanged</c>，否则字段换成了新数据、界面还停在旧数据上。
    /// </para>
    /// </summary>
    public async Task ReloadAsync()
    {
        if (_disposed || UserId == Guid.Empty)
        {
            return;
        }

        var version = ++_loadVersion;
        string? failure = null;
        IsLoading = true;
        StateHasChanged();

        await DataGate.RunAsync(async () =>
        {
            // 排队期间又有更新的加载：这一次不用做了，最新的那次会读到最新的参数
            if (version != _loadVersion || _disposed)
            {
                return;
            }

            try
            {
                await LoadAsync();
                HasLoaded = true;
                LoadFailed = false;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "加载统计数据失败");
                failure = ex.GetUserMessage();
                LoadFailed = !HasLoaded;
            }
        });

        if (version == _loadVersion)
        {
            IsLoading = false;
        }

        if (!_disposed)
        {
            StateHasChanged();
        }

        if (failure is not null && !_disposed)
        {
            await PopupService.EnqueueSnackbarAsync($"加载失败: {failure}", AlertTypes.Error);
        }
    }

    // 记一笔 / 编辑 / 导入 / 删除都会发出。事件可能来自别的线程，回到渲染线程上重新加载；
    // 失败只记日志（电路可能已关闭），不能把异常抛回事件源影响其他订阅者
    private void OnBillChanged() => _ = RunOnUiAsync();

    private async Task RunOnUiAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await InvokeAsync(async () =>
            {
                if (_disposed)
                {
                    return;
                }

                await ReloadAsync();
                StateHasChanged();
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "账单变更后刷新统计失败");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        BillEvents.BillChanged -= OnBillChanged;
        GC.SuppressFinalize(this);
    }
}
