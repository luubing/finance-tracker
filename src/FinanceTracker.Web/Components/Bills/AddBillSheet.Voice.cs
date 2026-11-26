using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Interfaces;
using FinanceTracker.Web.Models;
using Masa.Blazor;
using Microsoft.AspNetCore.Components.Web;

namespace FinanceTracker.Web.Components.Bills;

/// <summary>
/// 记一笔 Sheet 的语音预填：标题栏右上角的麦克风 + 说话期间的全屏覆盖层（<see cref="VoiceOverlay"/>，设计稿 screens 06-add-voice）。
/// 识别结果只预填表单，不直接保存，由用户确认后保存（账单来源记为语音识别）。
/// <para>
/// 两种识别方式沿用原记账页（解析规则不变，只换了入口与反馈）：
/// 原生流式识别（MAUI 宿主）——按住麦克风录音、松开结束，上滑 / 滑开进入取消区后松手即放弃；
/// 浏览器 Web Speech（Blazor Server）——点一下开始，覆盖层里点大麦克风「说完了」或自动断句结束，覆盖层里的「取消」/ Esc 放弃。
/// 宿主都不支持时麦克风置灰，点按只给一句说明，手动填写不受影响。
/// </para>
/// <para>
/// 说话期间覆盖层实时显示转写文本：每来一段部分结果就用同一个解析器解析一次（<see cref="VoicePresentation.Live"/>），
/// 把认出的金额 / 分类 / 支付渠道 / 时间在转写里高亮并列成芯片——这只是「预览」，真正预填表单只发生在拿到最终结果之后。
/// </para>
/// <para>
/// 语音只用来预填「新建」：编辑模式（<c>AddBillSheetService.OpenEdit</c>）右上角没有麦克风（留空），这里的状态不会被触发。
/// </para>
/// </summary>
public partial class AddBillSheet
{
    /// <summary>语音录入交互阶段（识别 / 解析中不允许重复触发）</summary>
    private enum VoicePhase
    {
        Idle = 0,

        /// <summary>覆盖层显示中，正在收音</summary>
        Listening = 1,

        /// <summary>已经说完（松手 / 点了完成），在等最终结果、或结果已到正在预填</summary>
        Parsing = 2,
    }

    /// <summary>按住录音最长时长（秒）：超时自动结束，防止一直占用麦克风</summary>
    private const int VoiceMaxHoldSeconds = 60;

    /// <summary>点按识别最长时长（秒）：避免识别器异常挂起导致一直处于聆听状态</summary>
    private const int VoiceRecognitionSeconds = 15;

    /// <summary>拿到最终结果后覆盖层再停留的时间（毫秒）：让用户看清最终转写与高亮 / 芯片，再收起露出已预填的表单</summary>
    private const int VoiceResultHoldMs = 450;

    private const string VoiceUnsupportedMessage = "当前环境不支持语音识别，可手动填写账单";

    private VoicePhase _voicePhase = VoicePhase.Idle;

    /// <summary>预填完成后 Sheet 里那行状态（「已按「…」预填账单，请确认后保存」）；null 不显示。说话期间的反馈在覆盖层里</summary>
    private string? _voiceStatus;

    /// <summary>语音解析命中的字段提示（「已识别：金额、分类「餐饮」」）</summary>
    private string? _voiceMatchHint;

    /// <summary>本次表单是否由语音预填（保存时据此标记数据来源）</summary>
    private bool _voicePrefilled;

    /// <summary>
    /// 语音会话的「代数」：新会话开始 / 会话结束 / 销毁时推进。最终结果的处理（解析 → 预填 → 停留 450 ms → 收起覆盖层）
    /// 跨越几个 await，期间 Sheet 若被收起又重开，迟到的这一份不能再写进新会话的表单、也不能把新会话的覆盖层收掉——
    /// 处理开始时记下代数，每个 await 之后与它比较。
    /// </summary>
    private int _voiceGeneration;

    /// <summary>能力检测是否完成（完成前不把麦克风置灰，免得一闪而过）</summary>
    private bool _voiceChecked;

    /// <summary>浏览器 Web Speech API 是否可用（MAUI 原生语音可用时也为 true）</summary>
    private bool _voiceBrowserSupported;

    private bool _voiceSubscribed;

    /// <summary>能力检测只做一次（需要 JS interop，所以放在会话开始之后）</summary>
    private Task? _voiceReady;

    /// <summary>点按识别的取消令牌来源（识别超时控制）</summary>
    private CancellationTokenSource? _voiceCts;

    /// <summary>按住录音的取消令牌来源（最长按住时长控制）</summary>
    private CancellationTokenSource? _voiceHoldCts;

    /// <summary>按住录音结束通知：true = 松开按钮结束，false = 滑进取消区 / 滑出按钮 / 被系统打断</summary>
    private TaskCompletionSource<bool>? _voiceReleaseTcs;

    /// <summary>当前按住录音的指针 Id（防止多指 / 重复触发）</summary>
    private long? _micPointerId;

    /// <summary>按下时手指的位置：上滑 / 滑开的位移相对它计算</summary>
    private double _micStartX;

    private double _micStartY;

    // ------------------------------------------------------------------ 覆盖层的展示状态

    /// <summary>本次是按住说话（原生）还是点按（浏览器）</summary>
    private bool _voiceHold;

    /// <summary>手指已滑进取消区：松手会放弃本次录音</summary>
    private bool _voiceCancelling;

    /// <summary>覆盖层上的实时转写（部分结果 / 最终结果）</summary>
    private string _voiceTranscript = string.Empty;

    /// <summary>转写是最终结果</summary>
    private bool _voiceFinal;

    /// <summary>实时解析认出的片段（转写里高亮）</summary>
    private IReadOnlyList<VoiceMatchSpan> _voiceMatches = [];

    /// <summary>实时解析认出的字段（芯片）</summary>
    private IReadOnlyList<VoiceChip> _voiceChips = [];

    /// <summary>实时解析的序号：解析是异步的，慢回来的旧结果不能盖掉新的</summary>
    private int _voiceLiveVersion;

    // ------------------------------------------------------------------ 展示状态

    private bool VoiceUnsupported => _voiceChecked && !_voiceBrowserSupported;

    private string VoiceState => VoiceUnsupported
        ? "off"
        : _voicePhase switch
        {
            VoicePhase.Listening => "listening",
            VoicePhase.Parsing => "parsing",
            _ => "idle",
        };

    private string MicClass => string.Join(' ', new[]
    {
        "ft-btn ft-btn--glass ft-glass ft-btn--round ft-addbill__mic",
        _voicePhase == VoicePhase.Listening ? "ft-addbill__mic--on" : null,
        VoiceUnsupported ? "ft-addbill__mic--off" : null,
    }.Where(c => c is not null));

    // ------------------------------------------------------------------ 能力检测

    private Task EnsureVoiceReadyAsync() => _voiceReady ??= DetectVoiceAsync();

    private async Task DetectVoiceAsync()
    {
        // MAUI 宿主注册原生语音服务；Blazor Server 注入的是 NoOpSpeechService（无原生能力），
        // 此时检测浏览器 Web Speech API 是否可用，决定麦克风是否可用。
        // 需要 JS interop，必须在会话开始（电路已接管）之后执行：预渲染阶段 IJSRuntime 不可用
        VoiceInput.RegisterVoiceService(SpeechService);

        if (VoiceInput.IsUsingNativeVoice)
        {
            _voiceBrowserSupported = true;
        }
        else
        {
            try
            {
                _voiceBrowserSupported = await VoiceInput.IsBrowserSupportedAsync();
            }
            catch (Exception)
            {
                // JS 模块加载失败（如宿主不支持）一律按不支持处理，静默降级为手动录入
                _voiceBrowserSupported = false;
            }
        }

        if (_voiceBrowserSupported && !_voiceSubscribed)
        {
            _voiceSubscribed = true;
            VoiceInput.VoiceTextReceived += OnVoiceTextReceived;
            VoiceInput.ErrorOccurred += OnVoiceErrorOccurred;
            VoiceInput.PartialTextReceived += OnVoicePartialReceived;
        }

        _voiceChecked = true;
        await InvokeAsync(StateHasChanged);
    }

    // ------------------------------------------------------------------ 覆盖层的开 / 关

    /// <summary>打开覆盖层：进入聆听阶段，转写 / 高亮 / 芯片全部清空，上一次预填的提示也撤掉</summary>
    private void BeginVoiceOverlay(bool hold)
    {
        _voicePhase = VoicePhase.Listening;
        _voiceHold = hold;
        _voiceCancelling = false;
        _voiceTranscript = string.Empty;
        _voiceFinal = false;
        _voiceMatches = [];
        _voiceChips = [];
        _voiceLiveVersion++;
        _voiceStatus = null;
        _voiceMatchHint = null;
    }

    /// <summary>收起覆盖层（不管是识别完成、取消还是失败）：回到空闲，实时状态清空</summary>
    private void EndVoiceOverlay()
    {
        _voicePhase = VoicePhase.Idle;
        _voiceCancelling = false;
        _voiceTranscript = string.Empty;
        _voiceFinal = false;
        _voiceMatches = [];
        _voiceChips = [];
        _voiceLiveVersion++;
    }

    /// <summary>说完了（松手 / 点了「完成」）：覆盖层转入「正在识别…」，等最终结果</summary>
    private void MarkVoiceParsing()
    {
        if (_voicePhase == VoicePhase.Listening)
        {
            _voicePhase = VoicePhase.Parsing;
            _voiceCancelling = false;
        }
    }

    // ------------------------------------------------------------------ 点按识别（浏览器 Web Speech）

    private async Task OnMicClickAsync()
    {
        await EnsureVoiceReadyAsync();

        if (VoiceUnsupported)
        {
            await PopupService.EnqueueSnackbarAsync(VoiceUnsupportedMessage, AlertTypes.Info);
            return;
        }

        await StartVoiceInputAsync();
    }

    /// <summary>点击麦克风：升起覆盖层并启动一次语音识别</summary>
    private async Task StartVoiceInputAsync()
    {
        if (_voicePhase != VoicePhase.Idle)
        {
            return;
        }

        BeginVoiceOverlay(hold: false);
        StateHasChanged();

        // 局部持有这一次的 CTS：「取消 → 马上再点麦克风」时，上一次迟到的收尾既不能把新一次的 CTS 释放掉，也不能收起新一次的覆盖层
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(VoiceRecognitionSeconds));
        _voiceCts = cts;
        try
        {
            await VoiceInput.StartAsync(cts.Token);
        }
        finally
        {
            cts.Dispose();

            // 字段里还是自己这一次的 CTS 才由自己收尾；已经被新一次识别换掉了，新的那次归它自己管
            if (ReferenceEquals(_voiceCts, cts))
            {
                _voiceCts = null;

                if (_voicePhase == VoicePhase.Listening)
                {
                    // 识别结束但未收到文本（如静默超时、被取消），收起覆盖层
                    EndVoiceOverlay();
                    StateHasChanged();
                }
            }
        }
    }

    /// <summary>覆盖层里点大麦克风（点按形态）：说完了，让识别器结束并给出最终结果</summary>
    private async Task OnVoiceFinishAsync()
    {
        if (_voicePhase != VoicePhase.Listening || _voiceHold)
        {
            return;
        }

        MarkVoiceParsing();
        StateHasChanged();
        await VoiceInput.StopAsync();
    }

    /// <summary>覆盖层里点「取消」或按 Esc（点按形态）：放弃本次识别，不预填、不提示</summary>
    private async Task OnVoiceCancelAsync()
    {
        // 最终结果已经到手（表单已预填、覆盖层在做收尾停留）就不能再「取消」了
        if (_voicePhase == VoicePhase.Idle || _voiceFinal || _voiceHold)
        {
            return;
        }

        EndVoiceOverlay();
        _voiceCts?.Cancel();
        StateHasChanged();
        await VoiceInput.CancelAsync();
    }

    // ------------------------------------------------------------------ 按住录音（MAUI 原生流式识别）

    /// <summary>按住麦克风：升起覆盖层并开始录音（原生流式识别，实时显示部分识别文本）</summary>
    private void OnMicPointerDown(PointerEventArgs e)
    {
        if (_voicePhase != VoicePhase.Idle || _micPointerId is not null)
        {
            return;
        }

        _micPointerId = e.PointerId;
        _micStartX = e.ClientX;
        _micStartY = e.ClientY;
        BeginVoiceOverlay(hold: true);
        _voiceReleaseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // 后台执行「开始录音 → 等待松开 → 结束并解析」，期间界面保持聆听状态
        _voiceHoldCts = new CancellationTokenSource(TimeSpan.FromSeconds(VoiceMaxHoldSeconds));
        _ = ProcessHoldRecordingAsync(_voiceHoldCts.Token);
    }

    /// <summary>
    /// 下一次重绘跳过一次（<see cref="OnMicPointerMove"/> 用）：Blazor 在每个事件处理完之后都会自动重绘整个记一笔 Sheet，
    /// 而按住期间 <c>pointermove</c> 的频率很高（每秒几十次）、绝大多数什么都没变，白白重绘会让覆盖层的动画掉帧
    /// </summary>
    private bool _skipNextRender;

    protected override bool ShouldRender()
    {
        if (_skipNextRender)
        {
            _skipNextRender = false;
            return false;
        }

        return true;
    }

    /// <summary>按住期间手指移动：上滑超过阈值 / 滑开太远 → 进入取消区（覆盖层泛红、提示换成「松开手指，取消」）；滑回来则退出取消区</summary>
    private void OnMicPointerMove(PointerEventArgs e)
    {
        // 框架在本方法返回后马上自动调一次 StateHasChanged（同步，ShouldRender 在其中判定）：除非跨过取消区边界，否则让它跳过
        _skipNextRender = true;

        if (_micPointerId is null || e.PointerId != _micPointerId || _voicePhase != VoicePhase.Listening)
        {
            return;
        }

        var cancelling = VoicePresentation.SlideCancels(e.ClientX - _micStartX, e.ClientY - _micStartY);
        if (cancelling != _voiceCancelling)
        {
            _voiceCancelling = cancelling;
            _skipNextRender = false;
            StateHasChanged();
        }
    }

    /// <summary>松开麦克风：在取消区里松手 = 放弃本次录音；否则结束录音并解析</summary>
    private void OnMicPointerUp(PointerEventArgs e)
    {
        if (_micPointerId is null || e.PointerId != _micPointerId)
        {
            return;
        }

        _micPointerId = null;
        _voiceReleaseTcs?.TrySetResult(!_voiceCancelling);
    }

    /// <summary>手指滑出麦克风：取消本次录音并丢弃结果（沿用原来的「滑出取消」，有的 WebView 不会给被捕获指针发 move，靠它兜底）</summary>
    private void OnMicPointerLeave(PointerEventArgs e)
    {
        if (_micPointerId is null || e.PointerId != _micPointerId)
        {
            return;
        }

        _micPointerId = null;
        _voiceReleaseTcs?.TrySetResult(false);
    }

    /// <summary>指针取消（来电、手势冲突等系统打断）：取消本次录音</summary>
    private void OnMicPointerCancel(PointerEventArgs e) => OnMicPointerLeave(e);

    /// <summary>按住录音全流程：请求权限 → 开始录音 → 等待松开 / 超时 → 结束并解析（或取消）</summary>
    private async Task ProcessHoldRecordingAsync(CancellationToken token)
    {
        try
        {
            var started = await VoiceInput.StartHoldRecordingAsync();
            if (!started)
            {
                // 失败原因已通过 ErrorOccurred 提示，收起覆盖层
                await ResetVoiceToIdleAsync();
                return;
            }

            // 权限弹窗期间用户可能已经松开：此时直接取消，避免误识别出错误提示
            if (_voiceReleaseTcs?.Task.IsCompleted == true)
            {
                await VoiceInput.CancelHoldRecordingAsync();
                await ResetVoiceToIdleAsync();
                return;
            }

            bool release;
            try
            {
                // 等待松开按钮；按住超过上限时自动结束（识别器会产出已有内容）
                release = await (_voiceReleaseTcs?.Task ?? Task.FromResult(true)).WaitAsync(token);
            }
            catch (Exception)
            {
                // 超时 / 取消：按松开处理
                release = true;
            }

            if (!release)
            {
                // 滑进取消区后松手 / 滑出 / 被打断：取消录音并丢弃结果
                await VoiceInput.CancelHoldRecordingAsync();
                await ResetVoiceToIdleAsync();
                return;
            }

            MarkVoiceParsing();
            await InvokeAsync(StateHasChanged);

            // 结束录音并等待最终结果（服务层内部已有 10 秒兜底超时）
            await VoiceInput.StopHoldRecordingAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"按住录音流程异常: {ex.Message}");
            await VoiceInput.CancelHoldRecordingAsync();
            await ResetVoiceToIdleAsync();
        }
        finally
        {
            _micPointerId = null;
            var cts = _voiceHoldCts;
            _voiceHoldCts = null;
            cts?.Dispose();
        }
    }

    /// <summary>收起覆盖层并恢复空闲（仅当仍处于聆听 / 识别中的残留状态时生效）</summary>
    private async Task ResetVoiceToIdleAsync()
    {
        EndVoiceOverlay();
        await InvokeAsync(StateHasChanged);
    }

    // ------------------------------------------------------------------ 识别结果

    /// <summary>语音识别完成：解析文本并预填表单（不直接保存，由用户确认），覆盖层停留片刻后收起</summary>
    private async Task OnVoiceTextReceived(string text)
    {
        // 空闲 = 这次识别已经被取消 / 收起（比如取消的瞬间最终结果刚好到了），丢弃
        if (!_visible || _voicePhase == VoicePhase.Idle)
        {
            return;
        }

        // 第一个 await 之前就把阶段推进到「解析中」：并发回调（识别结束、取消）据此判断结果已经到手
        var generation = _voiceGeneration;
        _voicePhase = VoicePhase.Parsing;
        _voiceCancelling = false;
        _voiceTranscript = text;
        _voiceFinal = true;
        _voiceLiveVersion++;
        await InvokeAsync(StateHasChanged);

        try
        {
            var draft = await ParseVoiceAsync(text);

            if (generation != _voiceGeneration)
            {
                // 解析期间会话已经结束 / 换了新的：这一份作废（finally 里也据此不去动新会话的覆盖层）
                return;
            }

            // 最终结果的高亮与芯片先显示出来，表单在覆盖层后面同步预填好
            ShowVoiceLive(draft);
            ApplyVoiceDraft(draft);

            if (!draft.Amount.HasValue)
            {
                // 关键字段缺失：提示用户重试（已识别的其他字段保留，只差金额）
                _voiceStatus = null;
                await PopupService.EnqueueSnackbarAsync(
                    "未能识别金额，请说出金额后重试，例如：昨天午饭花了三十五块",
                    AlertTypes.Warning);
            }
            else
            {
                _voicePrefilled = true;
                _voiceStatus = $"已按「{text}」预填账单，请确认后保存";
            }

            await InvokeAsync(StateHasChanged);

            // 让用户看清最终转写再收起覆盖层
            await Task.Delay(VoiceResultHoldMs);
        }
        catch (Exception ex)
        {
            // 会话已经换了的话这一份的失败与新会话无关，不去打扰
            if (generation == _voiceGeneration)
            {
                _voiceStatus = null;
                await PopupService.EnqueueSnackbarAsync($"解析语音内容失败：{ex.Message}", AlertTypes.Error);
            }
        }
        finally
        {
            // 只收自己这一场的覆盖层：停留期间会话换了（Sheet 收起又重开），新会话的覆盖层不归这一份处理管
            if (generation == _voiceGeneration)
            {
                EndVoiceOverlay();
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    /// <summary>识别过程中实时显示部分识别文本，并用同一个解析器预览认出了哪些字段（只预览，不预填）</summary>
    private async Task OnVoicePartialReceived(string text)
    {
        if (!_visible || _voicePhase == VoicePhase.Idle || _voiceFinal || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var version = ++_voiceLiveVersion;
        _voiceTranscript = text;

        ParsedBillDraft? draft = null;
        try
        {
            draft = await ParseVoiceAsync(text);
        }
        catch (Exception)
        {
            // 实时预览失败不打断识别，只是这一刻没有高亮
        }

        // 解析期间有更新的结果 / 已经收起 / 最终结果已到：这次预览作废
        if (version != _voiceLiveVersion || _voicePhase == VoicePhase.Idle || _voiceFinal)
        {
            return;
        }

        ShowVoiceLive(draft);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>语音识别失败：收起覆盖层并提示（这次识别已经被取消时静默）</summary>
    private async Task OnVoiceErrorOccurred(string message)
    {
        if (_voicePhase == VoicePhase.Idle)
        {
            return;
        }

        EndVoiceOverlay();
        await InvokeAsync(StateHasChanged);

        if (_visible)
        {
            await PopupService.EnqueueSnackbarAsync(message, AlertTypes.Error);
        }
    }

    // ------------------------------------------------------------------ 解析 / 预填

    private Task<ParsedBillDraft> ParseVoiceAsync(string text) => VoiceParser.ParseAsync(
        text,
        _categories.Select(c => new Category { Id = c.Id, Name = c.Name, Type = c.Type }).ToList(),
        _channels.Select(c => new PaymentChannel { Id = c.Id, Name = c.Name }).ToList());

    /// <summary>
    /// 把解析结果显示成覆盖层上的高亮与芯片。口径与预填一致：分类必须属于识别出的账单类型、渠道必须在候选列表里，
    /// 所以「芯片里有的 = 一会儿表单里会被填上的」。
    /// </summary>
    private void ShowVoiceLive(ParsedBillDraft? draft)
    {
        CategoryOption? category = null;
        ChannelOption? channel = null;

        if (draft is not null)
        {
            var type = draft.Type;
            if (draft.CategoryId is { } categoryId)
            {
                category = _categories.FirstOrDefault(c => c.Id == categoryId && c.Type == type);
            }

            if (draft.PaymentChannelId is { } channelId)
            {
                channel = _channels.FirstOrDefault(c => c.Id == channelId);
            }
        }

        var live = VoicePresentation.Live(draft, category?.Name, category?.Icon, channel?.Name, channel?.Icon, NowLocal());
        _voiceMatches = live.Matches;
        _voiceChips = live.Chips;
    }

    /// <summary>
    /// 将解析结果预填到表单：只填识别到的字段，未识别字段保持原值由用户手动补充。
    /// 分类必须属于当前（识别后的）账单类型、渠道必须在候选列表里才会应用。
    /// </summary>
    private void ApplyVoiceDraft(ParsedBillDraft draft)
    {
        var applied = new List<string>();

        if (draft.Amount is decimal amount && amount > 0)
        {
            _amount = AmountInput.FromDecimal(amount);
            applied.Add("金额");
        }

        if (draft.Type != _type)
        {
            // 类型切换后原分类不适用：先换成新类型下的默认项，再看识别结果有没有更具体的分类
            _type = draft.Type;
            _categoryId = PickCategory(_type);
        }

        if (draft.CategoryId is Guid categoryId
            && _categories.FirstOrDefault(c => c.Id == categoryId && c.Type == _type) is { } category)
        {
            _categoryId = category.Id;
            applied.Add($"分类「{category.Name}」");
        }

        if (draft.PaymentChannelId is Guid channelId
            && _channels.FirstOrDefault(c => c.Id == channelId) is { } channel)
        {
            _channelId = channel.Id;
            applied.Add($"支付渠道「{channel.Name}」");
        }

        if (draft.TransactionTime is DateTime time)
        {
            // 解析出的是应用时区的墙上时间；时间输入控件精确到分钟
            _time = new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
            applied.Add("交易时间");
        }

        if (!string.IsNullOrWhiteSpace(draft.RawText))
        {
            _note = draft.RawText;
        }

        _voiceMatchHint = applied.Count > 0 ? $"已识别：{string.Join("、", applied)}" : null;
    }

    // ------------------------------------------------------------------ 会话与销毁

    /// <summary>新会话开始：丢掉上一次残留的语音状态</summary>
    private void ResetVoiceSession()
    {
        _voiceGeneration++;
        AbortVoice();
        _voicePrefilled = false;
        EndVoiceOverlay();
        _voiceStatus = null;
        _voiceMatchHint = null;
    }

    /// <summary>会话结束（Sheet 已卸载）：还在录音的就取消</summary>
    private void EndVoiceSession()
    {
        _voiceGeneration++;
        var wasActive = _voicePhase != VoicePhase.Idle;
        AbortVoice();
        EndVoiceOverlay();
        _voiceStatus = null;
        _voiceMatchHint = null;

        if (wasActive)
        {
            _ = VoiceInput.SupportsHoldToTalk ? VoiceInput.CancelHoldRecordingAsync() : VoiceInput.CancelAsync();
        }
    }

    /// <summary>让正在等待松开的录音流程以「取消」收场，并取消点按识别的超时</summary>
    private void AbortVoice()
    {
        _micPointerId = null;
        _voiceReleaseTcs?.TrySetResult(false);
        _voiceCts?.Cancel();
    }

    private void DisposeVoice()
    {
        _voiceGeneration++;

        if (_voiceSubscribed)
        {
            VoiceInput.VoiceTextReceived -= OnVoiceTextReceived;
            VoiceInput.ErrorOccurred -= OnVoiceErrorOccurred;
            VoiceInput.PartialTextReceived -= OnVoicePartialReceived;
            _voiceSubscribed = false;
        }

        AbortVoice();
        _voiceHoldCts?.Dispose();
    }
}
