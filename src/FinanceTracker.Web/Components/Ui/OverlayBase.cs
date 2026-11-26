using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace FinanceTracker.Web.Components.Ui;

/// <summary>
/// 弹层（底部 Sheet / Alert / 操作面板）的共同行为：
/// - <see cref="Visible"/> / <see cref="VisibleChanged"/> 双向绑定显示状态；
/// - 打开时挂载并把焦点交给弹层容器（<see cref="HostRef"/>），关闭时先播放退出动画（<see cref="ClosingMilliseconds"/>）再卸载，
///   卸载后触发 <see cref="OnClosed"/>（页面在这里清理表单状态）；
/// - 用户发起的关闭（点遮罩 / Esc / 按钮 / 拖拽收起）统一走 <see cref="RequestCloseAsync"/>：
///   即使父组件没有把 Visible 回写（只用了 Visible 而不是 @bind-Visible），弹层也会自己收起。
/// 弹层只渲染标记与动画，不含任何业务逻辑；业务由内容插槽与事件回调承担。
/// </summary>
public abstract class OverlayBase : ComponentBase, IDisposable
{
    private CancellationTokenSource? _closeCts;
    private bool _focusPending;
    private bool _disposed;

    /// <summary>是否显示</summary>
    [Parameter]
    public bool Visible { get; set; }

    /// <summary>显示状态变化（用户关闭时回写 false）</summary>
    [Parameter]
    public EventCallback<bool> VisibleChanged { get; set; }

    /// <summary>退出动画结束、弹层卸载之后触发</summary>
    [Parameter]
    public EventCallback OnClosed { get; set; }

    /// <summary>
    /// 是否允许用户通过点遮罩 / Esc / 拖拽收起。正在保存等不能中断的时刻置为 false
    /// </summary>
    [Parameter]
    public bool Dismissible { get; set; } = true;

    /// <summary>
    /// 用户发起的关闭（点遮罩 / Esc / ✕ / 拖拽收起）之前的拦截：返回 false 表示拦下，弹层保持打开——
    /// 调用方自己决定接下来怎么办（例如「记一笔」弹出「放弃本次记账？」确认，确认后再把 Visible 置为 false）。
    /// 程序发起的关闭（<see cref="CloseCoreAsync"/>）不经过它。
    /// </summary>
    [Parameter]
    public Func<Task<bool>>? BeforeClose { get; set; }

    /// <summary>E2E 钩子：弹层容器的 data-testid</summary>
    [Parameter]
    public string? TestId { get; set; }

    /// <summary>弹层容器元素（子类在标记里用 @ref="HostRef" 绑定），打开时接收焦点以便响应 Esc</summary>
    protected ElementReference HostRef;

    /// <summary>已挂载（正在显示或正在播放退出动画）</summary>
    protected bool Mounted { get; private set; }

    /// <summary>正在播放退出动画</summary>
    protected bool Closing { get; private set; }

    /// <summary>退出动画时长，须与 ui.css 里对应的动画时长一致</summary>
    protected virtual int ClosingMilliseconds => 260;

    protected override void OnParametersSet()
    {
        if (Visible)
        {
            if (Closing)
            {
                // 退出动画途中又被要求打开：取消卸载，保持挂载
                _closeCts?.Cancel();
                Closing = false;
            }

            if (!Mounted)
            {
                Mounted = true;
                _focusPending = true;
            }
        }
        else if (Mounted && !Closing)
        {
            StartClosing();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusPending || !Mounted)
        {
            return;
        }

        _focusPending = false;

        try
        {
            await HostRef.FocusAsync();
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException
            or TaskCanceledException or ObjectDisposedException)
        {
            // 焦点只是为了响应 Esc；电路断开 / 元素已被替换时忽略
        }
    }

    /// <summary>
    /// 用户发起的关闭：点遮罩、Esc、取消 / 关闭按钮、拖拽收起。不允许关闭或已在关闭中时什么也不做；
    /// 给了 <see cref="BeforeClose"/> 时先问它，被拦下就保持打开
    /// </summary>
    protected async Task RequestCloseAsync()
    {
        if (!Mounted || Closing || !Dismissible)
        {
            return;
        }

        if (BeforeClose is { } guard && !await guard())
        {
            return;
        }

        await CloseCoreAsync();
    }

    /// <summary>
    /// 程序发起的关闭（确认按钮等，不受 <see cref="Dismissible"/> 限制）
    /// </summary>
    protected async Task CloseCoreAsync()
    {
        if (!Mounted || Closing)
        {
            return;
        }

        Visible = false;
        await VisibleChanged.InvokeAsync(false);

        // 父组件没有把 Visible 回写（没有用 @bind-Visible）时，自己收起
        if (Mounted && !Closing)
        {
            StartClosing();
        }
    }

    /// <summary>
    /// Esc 关闭（挂在弹层容器的 @onkeydown 上）
    /// </summary>
    protected Task OnHostKeyDownAsync(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e) =>
        e.Key == "Escape" ? RequestCloseAsync() : Task.CompletedTask;

    private void StartClosing()
    {
        Closing = true;
        _closeCts?.Cancel();
        _closeCts?.Dispose();
        _closeCts = new CancellationTokenSource();
        _ = FinishClosingAsync(_closeCts.Token);
        StateHasChanged();
    }

    private async Task FinishClosingAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(ClosingMilliseconds, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_disposed)
        {
            return;
        }

        await InvokeAsync(() =>
        {
            Mounted = false;
            Closing = false;
            StateHasChanged();
        });

        await OnClosed.InvokeAsync();
    }

    public virtual void Dispose()
    {
        _disposed = true;
        _closeCts?.Cancel();
        _closeCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
