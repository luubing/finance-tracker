using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Exceptions;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Core.Time;
using FinanceTracker.Web.Components.Ui;
using FinanceTracker.Web.Models;
using FinanceTracker.Web.Services;
using Masa.Blazor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace FinanceTracker.Web.Components.Bills;

/// <summary>
/// 记一笔 Sheet 的状态与交互（标记在 AddBillSheet.razor，语音在 AddBillSheet.Voice.cs）。
/// <para>
/// 生命周期：<see cref="AddBillSheetService"/> 的入口调用 Open → 开一次「记账会话」（重置草稿、后台读取分类 / 渠道 / 账本 / 默认值）→
/// 用户填写 → 保存成功或确认放弃后 Close。Sheet 常驻挂载（MainLayout），关闭时 <c>BottomSheet</c> 不渲染任何内容。
/// </para>
/// <para>
/// 取数分两段：先读本地库（分类 / 渠道 / 自己的账本 / 默认值，毫秒级，读完表单就可用），再读共享账本
/// （要向云端刷新成员关系，云端不可用时有短期限与退避，只影响共享账本，自己的账本照常可选）。
/// 所有读库都经过 <see cref="PageDataGate"/>：四个 Tab 页常驻，账单变更会让它们同时重读，同一个 DbContext 不能并行查询。
/// </para>
/// </summary>
public partial class AddBillSheet
{
    private static readonly IReadOnlyList<SegmentOption> TypeOptions =
        CategoryPresentation.Segments.Select(s => new SegmentOption(s.Value, s.Label)).ToList();

    /// <summary>「新建账本 / 抖动」之类短动画的持续时间；与样式里的 ft-shake 时长一致</summary>
    private const int ShakeMilliseconds = 460;

    // ===== 会话 =====

    /// <summary>当前这一次记账会话的编号（取自 <see cref="AddBillSheetService.OpenCount"/>），后台任务据此判断自己是否过期</summary>
    private int _session;

    private bool _visible;
    private bool _ready;
    private bool _saving;
    private bool _disposed;
    private Guid _userId;

    // ===== 编辑模式（新建时都是空） =====

    /// <summary>正在编辑的账单 Id（会话开始时取自 <see cref="AddBillSheetService.EditingBillId"/>）；新建为 null</summary>
    private Guid? _editingId;

    /// <summary>编辑模式下账单刚读出来、预填完成时的内容快照：与当前草稿比较得出「有没有改过」；还没读到（或新建）时为 null</summary>
    private BillDraft? _original;

    /// <summary>删除确认里点名这一笔的说明；用读出来时的账单内容，不是用户正在改的内容</summary>
    private string _deleteMessage = string.Empty;

    private bool _deleteOpen;
    private bool _deleting;

    /// <summary>
    /// 编辑的账单是自己记的，但所在的共享账本里自己现在只是只读成员：照常能打开、保存时才被拒绝，
    /// 但不给「删除」（<c>BillService</c> 同样以 403 拒绝只读成员删除）。读账单时顺带取得
    /// </summary>
    private bool _inReadOnlyLedger;

    // ===== 草稿 =====

    private BillType _type = BillType.Expense;
    private string _amount = string.Empty;
    private Guid _categoryId;
    private Guid _channelId;
    private Guid? _ledgerId;
    private DateTime _time;
    private string _note = string.Empty;

    // ===== 候选项 =====

    private UserDefault? _defaults;
    private List<CategoryOption> _categories = [];
    private List<ChannelOption> _channels = [];
    private List<LedgerModel> _ledgers = [];

    /// <summary>每个账单类型下最近选中的分类：来回切「支出 / 收入」时各自回到原来选的那一个，而不是被重置成默认项</summary>
    private readonly Dictionary<BillType, Guid> _categoryMemory = [];

    // ===== 浮层 =====

    private bool _ledgerMenuOpen;
    private bool _channelMenuOpen;
    private bool _categorySheetOpen;
    private bool _ledgerSheetOpen;
    private bool _discardOpen;

    private ElementReference _ledgerAnchor;
    private ElementReference _channelAnchor;

    // ===== 引导抖动 =====

    private bool _shakeAmount;
    private bool _shakeCategory;
    private bool _shakeLedger;

    // ------------------------------------------------------------------ 派生状态

    private string TypeKey => _type == BillType.Income ? "income" : "expense";

    private bool HasInput => AddBillPresentation.HasInput(_amount, _note);

    private bool IsEditing => _editingId.HasValue;

    /// <summary>
    /// 要不要显示内容底部的红字「删除账单」：编辑模式、账单读出来了，并且不在自己只读的共享账本里
    /// （只读成员删除会被服务层 403，不给入口）
    /// </summary>
    private bool CanDelete => IsEditing && _ready && _original is not null && !_inReadOnlyLedger;

    private string SheetTitle => IsEditing ? AddBillPresentation.EditTitle : AddBillPresentation.CreateTitle;

    /// <summary>当前草稿的内容快照（编辑模式用它与 <see cref="_original"/> 比较）</summary>
    private BillDraft CurrentDraft() =>
        BillDraft.Of(AmountInput.Parse(_amount), _type, _categoryId, _channelId, _ledgerId, _time, _note);

    /// <summary>
    /// 用户发起关闭时是否要先确认：新建是输入过内容；编辑是内容与读出来时不同（改回原样视为没改，不打扰）。
    /// 账单还没读出来时（<see cref="_original"/> 为空）没有什么可丢的。
    /// </summary>
    private bool NeedsConfirmClose => IsEditing ? _original is not null && CurrentDraft() != _original : HasInput;

    private IReadOnlyList<CategoryOption> CurrentCategories => _categories.Where(c => c.Type == _type).ToList();

    private LedgerModel? SelectedLedger => _ledgerId is { } id ? _ledgers.FirstOrDefault(l => l.Id == id) : null;

    private bool LedgerMissing => SelectedLedger is null;

    private string LedgerValue => SelectedLedger?.Name ?? "请选择账本";

    private string ChannelValue => _channels.FirstOrDefault(c => c.Id == _channelId)?.Name ?? "请选择支付渠道";

    private string TimeLabel => AddBillPresentation.TimeLabel(_time, NowLocal());

    /// <summary>保存键是否点亮：金额、分类、支付渠道、账本都齐了（置灰时仍可点按，用来引导补全）</summary>
    private bool CanSave => _ready && AddBillPresentation.CheckSave(AmountInput.Parse(_amount), _categoryId, _channelId, _ledgerId) == AddBillBlock.None;

    /// <summary>应用时区的「现在」（墙上时间）</summary>
    private static DateTime NowLocal() => AppTimeZone.ToLocal(DateTime.UtcNow);

    /// <summary>应用时区的「现在」，精确到分钟（时间输入控件的精度）</summary>
    private static DateTime NowLocalMinute()
    {
        var now = NowLocal();
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Unspecified);
    }

    // ------------------------------------------------------------------ 生命周期

    protected override void OnInitialized()
    {
        Sheet.Changed += OnSheetChanged;
    }

    protected override void OnAfterRender(bool firstRender)
    {
        // 旧地址 /add-bill、/add-bill/{BillId} 经服务端 302 到 /?add-bill=1、/?add-bill={BillId}：电路接管后的第一次渲染读到它，升起 Sheet（后者是编辑那一笔）。
        // 不能放在 OnInitialized：预渲染阶段也会执行它，而预渲染里没有登录态与脚本，开出来也读不到数据
        if (firstRender && AddBillSheetService.ReadOpenRequest(Navigation.Uri) is { } request)
        {
            if (request.BillId is { } billId)
            {
                Sheet.OpenEdit(billId);
            }
            else
            {
                Sheet.Open();
            }
        }
    }

    /// <summary>入口调用了 Open / Close（可能来自任何线程上下文）</summary>
    private void OnSheetChanged() => _ = InvokeAsync(() =>
    {
        if (_disposed)
        {
            return;
        }

        if (Sheet.IsOpen)
        {
            if (!_visible || _session != Sheet.OpenCount)
            {
                BeginSession();
            }
        }
        else
        {
            _visible = false;
        }

        StateHasChanged();
    });

    /// <summary>开一次新的记账会话：草稿归零、浮层收起，后台读取数据</summary>
    private void BeginSession()
    {
        _session = Sheet.OpenCount;
        _visible = true;
        _ready = false;
        _saving = false;

        // 编辑还是新建，在打开的这一刻就定下（标题与「删除」立即就位；账单内容读到之后再预填）
        _editingId = Sheet.EditingBillId;
        _original = null;
        _deleteMessage = string.Empty;
        _deleteOpen = false;
        _deleting = false;
        _inReadOnlyLedger = false;

        _type = BillType.Expense;
        _amount = string.Empty;
        _categoryId = Guid.Empty;
        _channelId = Guid.Empty;
        _ledgerId = null;
        _time = NowLocalMinute();
        _note = string.Empty;

        _categories = [];
        _channels = [];
        _ledgers = [];
        _defaults = null;
        _categoryMemory.Clear();

        _ledgerMenuOpen = false;
        _channelMenuOpen = false;
        _categorySheetOpen = false;
        _ledgerSheetOpen = false;
        _discardOpen = false;
        _shakeAmount = false;
        _shakeCategory = false;
        _shakeLedger = false;

        ResetVoiceSession();

        var session = _session;
        _ = RunSafelyAsync(() => LoadAsync(session), "加载记账所需数据失败");
    }

    /// <summary>这一次会话仍然有效：Sheet 还开着、没有被关掉重开、组件没销毁</summary>
    private bool IsCurrent(int session) => !_disposed && _visible && _session == session;

    private async Task RunSafelyAsync(Func<Task> work, string failureLead)
    {
        try
        {
            await work();
        }
        catch (Exception ex) when (!_disposed)
        {
            await PopupService.EnqueueSnackbarAsync($"{failureLead}: {ex.GetUserMessage()}", AlertTypes.Error);
        }
    }

    private async Task LoadAsync(int session)
    {
        var editingId = _editingId;

        // 语音只用来预填新账单：编辑模式没有麦克风，不必探测语音能力
        if (editingId is null)
        {
            await EnsureVoiceReadyAsync();
            if (!IsCurrent(session))
            {
                return;
            }
        }

        // 优先用认证服务缓存的用户 Id（同一电路内可靠）
        var userId = await AuthState.GetUserIdAsync();
        if (!IsCurrent(session))
        {
            return;
        }

        if (userId is not { } id)
        {
            await PopupService.EnqueueSnackbarAsync("请先登录", AlertTypes.Warning);
            Sheet.Close();
            return;
        }

        _userId = id;

        List<Category> categories = [];
        List<PaymentChannel> channels = [];
        List<Ledger> ledgers = [];
        UserDefault? defaults = null;
        Bill? editing = null;
        var inReadOnlyLedger = false;

        await DataGate.RunAsync(async () =>
        {
            categories = await CategoryService.GetCategoriesAsync(id);
            channels = await PaymentChannelService.GetPaymentChannelsAsync(id);
            ledgers = await LedgerService.GetLedgersAsync(id);
            defaults = await UserDefaultService.GetUserDefaultAsync(id);

            if (editingId is { } billId)
            {
                editing = await BillService.GetBillByIdAsync(billId);

                // 我是只读成员的共享账本里自己记的账单：照常打开、保存时才会被拒绝（与旧编辑页一致，不在打开时就拦），
                // 只是不给「删除」——服务层同样拒绝只读成员删除。读本地成员缓存，与服务层是同一条规则
                if (editing is { IsDeleted: false } && editing.UserId == id && editing.LedgerId is { } ledgerId)
                {
                    inReadOnlyLedger = (await LedgerMemberService.GetReadOnlyLedgerIdsAsync(id, [ledgerId])).Contains(ledgerId);
                }
            }
        });

        if (!IsCurrent(session))
        {
            return;
        }

        _categories = categories.Select(ToOption).ToList();
        _channels = channels.Select(c => new ChannelOption(c.Id, c.Name, c.Icon)).ToList();
        _ledgers = ledgers.Select(l => LedgerModel.FromEntity(l)).ToList();
        _defaults = defaults;

        if (editingId is null)
        {
            ApplyDefaults();
        }
        else if (!await PrefillEditingAsync(editing, id))
        {
            return;
        }

        _inReadOnlyLedger = inReadOnlyLedger;
        _ready = true;
        StateHasChanged();

        await LoadSharedLedgersAsync(session);
    }

    // ------------------------------------------------------------------ 编辑模式：预填

    /// <summary>
    /// 把读到的账单预填进表单。账单不存在 / 已删除、不是自己记的时给出提示并收起 Sheet（返回 false）。
    /// 账本里有没有写权限<b>不在这里拦</b>：没有写权限（例如被改成只读成员）的账单也照常打开，保存时才被拒绝——
    /// 那是「已认证但无权操作」（403 语义），只提示、不登出（<see cref="SaveAsync"/>）。
    /// 预填后记下内容快照 <see cref="_original"/>：之后与它比较得出「改过没有」（用于关闭前的确认）。
    /// </summary>
    /// <param name="bill">读到的账单（含分类 / 渠道 / 账本导航属性）；没有这笔账单为 null</param>
    /// <param name="userId">当前用户</param>
    private async Task<bool> PrefillEditingAsync(Bill? bill, Guid userId)
    {
        if (bill is null || bill.IsDeleted)
        {
            await RejectEditingAsync(AddBillPresentation.BillGoneText, AlertTypes.Warning);
            return false;
        }

        if (bill.UserId != userId)
        {
            await RejectEditingAsync(BillListPresentation.ReadOnlyHint, AlertTypes.Info);
            return false;
        }

        _type = bill.Type;
        _amount = AmountInput.FromDecimal(bill.Amount);
        _categoryId = bill.CategoryId;
        _channelId = bill.PaymentChannelId;
        _ledgerId = bill.LedgerId;
        // 库里是 UTC：回填应用时区的墙上时间，保存时再由 BillService 换回 UTC
        _time = AppTimeZone.ToLocal(bill.TransactionTime);
        _note = bill.Note ?? string.Empty;

        // 共享账本里自己记的账单：它所在的账本要立刻出现在账本行上（共享账本的完整列表稍后才联网刷新，且只列有写权限的）；
        // 本地读不到这个账本时当作没有账本，保存前重新选（账本行显示什么，保存键就按什么放行）
        if (_ledgerId is { } ledgerId && _ledgers.All(l => l.Id != ledgerId))
        {
            if (bill.Ledger is { IsDeleted: false } ledger)
            {
                _ledgers.Add(LedgerModel.FromEntity(ledger, isShared: ledger.UserId != userId));
            }
            else
            {
                _ledgerId = null;
            }
        }

        _categoryMemory[_type] = _categoryId;
        _deleteMessage = BillListPresentation.DeleteMessage(
            BillPresentation.Title(string.IsNullOrWhiteSpace(bill.Category?.Name) ? BillPresentation.UnknownCategoryLabel : bill.Category!.Name, bill.Note),
            MoneyPresentation.Signed(bill.Amount, bill.Type == BillType.Income));
        _original = CurrentDraft();
        return true;
    }

    /// <summary>不能编辑：提示原因并收起 Sheet</summary>
    private async Task RejectEditingAsync(string message, AlertTypes type)
    {
        Sheet.Close();
        await PopupService.EnqueueSnackbarAsync(message, type);
    }

    /// <summary>
    /// 把共享账本里「我能记账的」加进账本菜单。要先联网刷新成员关系（云端不可用时有短期限与退避），
    /// 所以放在表单可用之后；失败（离线等）只影响共享账本，自己的账本照常可选。
    /// 菜单只列有写权限的（所有者 / 可编辑成员）：只读成员选了也记不进去，不如不给；保存前仍由服务层统一校验。
    /// </summary>
    private async Task LoadSharedLedgersAsync(int session)
    {
        try
        {
            var writable = await DataGate.RunAsync(async () =>
            {
                var shared = await LedgerMemberService.GetSharedLedgersAsync(_userId);
                var result = new List<Ledger>();
                foreach (var ledger in shared)
                {
                    var role = await LedgerMemberService.GetMyRoleAsync(ledger.Id, _userId);
                    if (role is LedgerMemberRole.Owner or LedgerMemberRole.Editor)
                    {
                        result.Add(ledger);
                    }
                }

                return result;
            });

            if (!IsCurrent(session))
            {
                return;
            }

            _ledgers.AddRange(writable
                .Where(l => _ledgers.All(existing => existing.Id != l.Id))
                .Select(l => LedgerModel.FromEntity(l, isShared: true)));
            StateHasChanged();
        }
        catch (Exception)
        {
            // 共享账本加载失败不影响自己的账本
        }
    }

    private static CategoryOption ToOption(Category category) =>
        new(category.Id, category.Name, category.Icon, category.Type);

    /// <summary>读完数据后挑默认值：用户设置的默认项优先，没有就取第一项；默认账本只会是自己的账本</summary>
    private void ApplyDefaults()
    {
        _categoryId = PickCategory(_type);
        _channelId = AddBillPresentation.PickChannel(_channels, _defaults?.DefaultPaymentChannelId);
        _ledgerId = LedgerModel.PickDefault(_ledgers, _defaults?.DefaultLedgerId);
    }

    /// <summary>某个类型下该选哪个分类：这一次会话里在该类型下选过的优先（仍在可选项里才算数），否则用户设置的默认分类，再否则第一项</summary>
    private Guid PickCategory(BillType type) => AddBillPresentation.PickCategory(
        _categories,
        type,
        _categoryMemory.TryGetValue(type, out var remembered) && remembered != Guid.Empty
            ? remembered
            : type == BillType.Income ? _defaults?.DefaultIncomeCategoryId : _defaults?.DefaultExpenseCategoryId);

    // ------------------------------------------------------------------ 关闭

    /// <summary>BottomSheet 通知可见性变化（点遮罩 / 关闭键 / 拖拽收起 / Esc，已通过 <see cref="BeforeCloseAsync"/>）</summary>
    private Task OnVisibleChangedAsync(bool visible)
    {
        _visible = visible;
        if (!visible)
        {
            Sheet.Close();
        }

        return Task.CompletedTask;
    }

    /// <summary>退出动画结束、Sheet 已卸载：这次会话彻底结束</summary>
    private Task OnSheetClosedAsync()
    {
        EndVoiceSession();
        _discardOpen = false;
        _deleteOpen = false;
        _ledgerMenuOpen = false;
        _channelMenuOpen = false;
        return Task.CompletedTask;
    }

    /// <summary>用户发起关闭：新建已经输入了内容、或编辑已经改了内容，就先确认，避免误丢</summary>
    private Task<bool> BeforeCloseAsync()
    {
        if (!NeedsConfirmClose)
        {
            return Task.FromResult(true);
        }

        _discardOpen = true;
        StateHasChanged();
        return Task.FromResult(false);
    }

    private Task DiscardAsync()
    {
        _discardOpen = false;
        Sheet.Close();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ 输入

    private Task OnTypeChangedAsync(string segment)
    {
        var type = CategoryPresentation.TypeOf(segment);
        if (type == _type)
        {
            return Task.CompletedTask;
        }

        // 切类型：金额保留，分类换成新类型下的项（原分类属于另一个类型，不适用）：
        // 这个类型下之前选过的分类优先（来回切不丢），否则取默认项
        _categoryMemory[_type] = _categoryId;
        _type = type;
        _categoryId = PickCategory(type);
        return Task.CompletedTask;
    }

    private Task OnKeyAsync(string key)
    {
        _amount = AmountInput.Apply(_amount, key);
        return Task.CompletedTask;
    }

    private void SelectCategory(Guid id) => _categoryId = id;

    private Task SelectChannelAsync(Guid id)
    {
        _channelId = id;
        return Task.CompletedTask;
    }

    private Task SelectLedgerAsync(Guid id)
    {
        _ledgerId = id;
        return Task.CompletedTask;
    }

    private void OpenLedgerMenu() => _ledgerMenuOpen = true;

    private void OpenChannelMenu() => _channelMenuOpen = true;

    private void OpenCategorySheet() => _categorySheetOpen = true;

    private void OpenLedgerSheet() => _ledgerSheetOpen = true;

    private void OnTimeChanged(ChangeEventArgs e)
    {
        // 清空日期（浏览器允许）或格式不对：沿用原值，不让时间落到「没有值」
        if (AddBillPresentation.TryParseInputValue(e.Value?.ToString(), out var time))
        {
            _time = time;
        }
    }

    private void OnNoteChanged(ChangeEventArgs e) => _note = e.Value?.ToString() ?? string.Empty;

    // ------------------------------------------------------------------ 新建分类 / 账本（行内）

    /// <summary>「新建分类」保存后：重读分类，并自动选中刚建的那个</summary>
    private async Task OnCategoryCreatedAsync(Category created)
    {
        try
        {
            await DataGate.RunAsync(async () =>
            {
                var all = await CategoryService.GetCategoriesAsync(_userId);
                _categories = all.Select(ToOption).ToList();
            });

            if (created.Type == _type && _categories.Any(c => c.Id == created.Id))
            {
                _categoryId = created.Id;
            }
        }
        catch (Exception ex)
        {
            await PopupService.EnqueueSnackbarAsync($"刷新分类失败: {ex.GetUserMessage()}", AlertTypes.Warning);
        }
    }

    /// <summary>「新建账本…」保存后：把新账本放进菜单（自己的账本之后、共享账本之前）并自动选中</summary>
    private Task OnLedgerCreatedAsync(Ledger created)
    {
        if (_ledgers.All(l => l.Id != created.Id))
        {
            var index = _ledgers.FindLastIndex(l => !l.IsShared) + 1;
            _ledgers.Insert(index, LedgerModel.FromEntity(created));
        }

        _ledgerId = created.Id;
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ 保存

    private async Task SaveAsync()
    {
        // 删除进行中（等服务层软删除返回）也不响应：两个写库动作不并行，否则「刚点了确认删除又点保存」会对同一笔账单同时写
        if (_saving || _deleting)
        {
            return;
        }

        var amount = AmountInput.Parse(_amount);
        var block = _ready
            ? AddBillPresentation.CheckSave(amount, _categoryId, _channelId, _ledgerId)
            : AddBillBlock.Category;

        if (block != AddBillBlock.None || amount is not { } value || _ledgerId is not { } ledgerId)
        {
            await GuideAsync(block);
            return;
        }

        var editingId = _editingId;

        // 编辑后什么都没改也照常写库（与旧编辑页一致）：保存就是「按现在的内容再存一次」，同步状态变回待同步、修改时间更新

        _saving = true;
        StateHasChanged();

        BudgetAlert? alert = null;
        var ledgerName = SelectedLedger?.Name;
        var billGone = false;

        // 用户输入的时间（应用时区的墙上时间）。CreateBillAsync 会就地把 bill.TransactionTime 换成 UTC，
        // 预算月份要用这一份，不能在保存之后再读 bill.TransactionTime
        var wallTime = _time;

        try
        {
            var bill = new Bill
            {
                UserId = _userId,
                Amount = value,
                Type = _type,
                CategoryId = _categoryId,
                PaymentChannelId = _channelId,
                LedgerId = ledgerId,
                // 用户看到 / 输入的是应用时区的墙上时间（Kind 为 Unspecified），由 BillService 经 AppTimeZone 换成 UTC 入库
                TransactionTime = wallTime,
                Note = string.IsNullOrWhiteSpace(_note) ? null : _note.Trim(),
            };

            if (editingId is { } billId)
            {
                // 更新沿用原账单：数据来源不变（UpdateBillAsync 不碰 Source），同步状态由服务层标记为待同步
                bill.Id = billId;
            }
            else
            {
                // 语音预填的账单标记来源为语音识别，便于后续统计分析
                bill.Source = _voicePrefilled ? BillSource.Voice : BillSource.Manual;
            }

            // 写入与随后的预算检查都读写同一个 DbContext，和各 Tab 页的重读串行执行
            await DataGate.RunAsync(async () =>
            {
                // 共享账本写权限先在本地校验（读离线缓存，即时反馈）；服务端推送时仍有兜底校验
                await LedgerMemberService.EnsureCanWriteAsync(ledgerId, _userId);

                if (editingId is null)
                {
                    await BillService.CreateBillAsync(bill);
                }
                else
                {
                    await BillService.UpdateBillAsync(bill);
                }

                alert = await TryGetBudgetAlertAsync(bill, wallTime);
            });
        }
        catch (ForbiddenAccessException ex)
        {
            // 没有写权限（共享账本里的只读成员）：「已认证但无权操作」（403 语义），只提示、不登出，Sheet 留着让用户改选账本。
            // 编辑时点明是「无权限编辑这笔账单」+ 原因（打开时不拦，保存时才拒绝）
            await PopupService.EnqueueSnackbarAsync(
                editingId is not null ? AddBillPresentation.NoPermissionText(ex.GetUserMessage()) : ex.GetUserMessage(),
                AlertTypes.Warning);
            return;
        }
        catch (Exception ex)
        {
            // 编辑期间账单被别处删掉了（同步 / 另一台设备）：服务层读不到它，抛「账单不存在」。
            // 换成与「删除」一致的说明并收起——别让用户对着一笔已经不存在的账单继续改
            if (editingId is { } missingId && await IsBillGoneAsync(missingId))
            {
                billGone = true;
            }
            else
            {
                await PopupService.EnqueueSnackbarAsync($"保存失败: {ex.GetUserMessage()}", AlertTypes.Error);
                return;
            }
        }
        finally
        {
            _saving = false;
        }

        if (billGone)
        {
            _original = CurrentDraft();
            BillEvents.NotifyBillChanged();
            Sheet.Close();
            await PopupService.EnqueueSnackbarAsync(AddBillPresentation.BillGoneText, AlertTypes.Warning);
            return;
        }

        // 已经保存：不再有「未保存的输入」，直接收起；其他页面据事件刷新
        if (editingId is null)
        {
            _amount = string.Empty;
            _note = string.Empty;
        }
        else
        {
            _original = CurrentDraft();
        }

        BillEvents.NotifyBillChanged();
        Sheet.Close();

        await PopupService.EnqueueSnackbarAsync(
            editingId is not null
                ? AddBillPresentation.UpdatedText
                : string.IsNullOrEmpty(ledgerName) ? "已保存" : $"已保存到「{ledgerName}」",
            AlertTypes.Success);
        await NotifyBudgetAlertAsync(alert);
    }

    /// <summary>
    /// 这笔账单现在还读得到吗：读不到（已被软删除）就是「没了」。读取本身出错时按「还在」处理——
    /// 那是别的故障，让调用方照常报「保存失败」，不要把它误说成账单已删除。
    /// </summary>
    private async Task<bool> IsBillGoneAsync(Guid billId)
    {
        try
        {
            var current = await DataGate.RunAsync(() => BillService.GetBillByIdAsync(billId));
            return current is null or { IsDeleted: true };
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ 删除（编辑模式）

    /// <summary>点「删除账单」：先确认。账单还没读出来、在自己只读的共享账本里、正在保存 / 删除时不响应</summary>
    private void AskDelete()
    {
        if (!CanDelete || _saving || _deleting)
        {
            return;
        }

        _deleteOpen = true;
    }

    /// <summary>
    /// 确认删除：软删除（随同步传播），收起 Sheet，其他页面据事件刷新列表 / 汇总 / 统计，并给 Toast。
    /// 账单已经被别处删了（返回 false）也算处理完——刷新一下、说明情况即可。
    /// </summary>
    private async Task DeleteConfirmedAsync()
    {
        if (_editingId is not { } billId || _deleting || _saving)
        {
            return;
        }

        _deleting = true;
        StateHasChanged();

        bool deleted;

        try
        {
            deleted = await DataGate.RunAsync(() => BillService.DeleteBillAsync(billId, _userId));
        }
        catch (ForbiddenAccessException ex)
        {
            // 打开之后我被改成了只读成员：服务层拒绝删除（403 语义），只提示、不登出，Sheet 留着
            await PopupService.EnqueueSnackbarAsync(ex.GetUserMessage(), AlertTypes.Warning);
            return;
        }
        catch (Exception ex)
        {
            await PopupService.EnqueueSnackbarAsync($"删除失败: {ex.GetUserMessage()}", AlertTypes.Error);
            return;
        }
        finally
        {
            _deleting = false;
        }

        // 已经删了：没有「未保存的修改」可言，直接收起
        _original = CurrentDraft();
        BillEvents.NotifyBillChanged();
        Sheet.Close();

        await PopupService.EnqueueSnackbarAsync(
            deleted ? AddBillPresentation.DeletedText : AddBillPresentation.BillGoneText,
            deleted ? AlertTypes.Success : AlertTypes.Warning);
    }

    /// <summary>
    /// 预算预警检查：支出账单保存后，检查相关预算（总预算 + 该分类预算）的使用情况。检查失败不影响保存。
    /// 预算月份取 <paramref name="wallTime"/>（用户输入的应用时区墙上时间）的年月：
    /// 新建时 <c>CreateBillAsync</c> 已就地把 <c>bill.TransactionTime</c> 换成 UTC，读它会在月初 / 月末落到相邻的月份。
    /// </summary>
    private async Task<BudgetAlert?> TryGetBudgetAlertAsync(Bill bill, DateTime wallTime)
    {
        if (bill.Type != BillType.Expense)
        {
            return null;
        }

        try
        {
            return await BudgetService.GetBudgetAlertAsync(
                bill.UserId,
                wallTime.Year,
                wallTime.Month,
                bill.LedgerId,
                bill.CategoryId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>超支（≥100%）红色提示、接近上限（≥80%）黄色提示</summary>
    private async Task NotifyBudgetAlertAsync(BudgetAlert? alert)
    {
        if (alert is null)
        {
            return;
        }

        if (alert.IsExceeded)
        {
            await PopupService.EnqueueSnackbarAsync(
                $"预算超支提醒：{alert.CategoryName}已使用 {alert.UsagePercentage:0.0}%（超支 {Math.Abs(alert.RemainingAmount):N2} 元）",
                AlertTypes.Error);
        }
        else if (alert.IsWarning)
        {
            await PopupService.EnqueueSnackbarAsync(
                $"预算预警：{alert.CategoryName}已使用 {alert.UsagePercentage:0.0}%，剩余 {alert.RemainingAmount:N2} 元，请注意控制支出",
                AlertTypes.Warning);
        }
    }

    /// <summary>
    /// 点按置灰的保存键：告诉用户缺什么。缺账本时让账本行抖动并直接弹出账本菜单（里面有「新建账本…」），
    /// 其余缺项让对应区域抖动并给一句提示。
    /// </summary>
    private async Task GuideAsync(AddBillBlock block)
    {
        switch (block)
        {
            case AddBillBlock.Ledger:
                _ledgerMenuOpen = true;
                Shake(v => _shakeLedger = v);
                break;

            case AddBillBlock.Amount:
                Shake(v => _shakeAmount = v);
                await PopupService.EnqueueSnackbarAsync(AddBillPresentation.BlockMessage(block), AlertTypes.Warning);
                break;

            case AddBillBlock.Category:
            case AddBillBlock.Channel:
                if (block == AddBillBlock.Category)
                {
                    Shake(v => _shakeCategory = v);
                }

                await PopupService.EnqueueSnackbarAsync(AddBillPresentation.BlockMessage(block), AlertTypes.Warning);
                break;
        }
    }

    /// <summary>把一个抖动标志置为 true，过一会儿再复位（样式据 data-shake 播放动画）</summary>
    private void Shake(Action<bool> set)
    {
        set(true);
        _ = InvokeAsync(async () =>
        {
            await Task.Delay(ShakeMilliseconds);
            if (_disposed)
            {
                return;
            }

            set(false);
            StateHasChanged();
        });
    }

    // ------------------------------------------------------------------ 销毁

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        Sheet.Changed -= OnSheetChanged;
        DisposeVoice();
        return ValueTask.CompletedTask;
    }
}
