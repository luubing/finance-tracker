using System.Globalization;
using FinanceTracker.Core.Services;

namespace FinanceTracker.Web.Models;

/// <summary>
/// 云端同步行的状态。优先级从高到低：正在同步 &gt; 还没读到数量 &gt; 状态未知（读取待同步账单失败）&gt; 有待同步账单 &gt; 已同步。
/// </summary>
public enum SyncRowState
{
    /// <summary>正在同步</summary>
    Syncing,

    /// <summary>待同步数量还没读到：不能先显示「已同步」误导用户</summary>
    Loading,

    /// <summary>读取待同步账单失败：数量不可信，不能显示「已同步」误导用户</summary>
    Unknown,

    /// <summary>有待同步账单（含上次同步失败、等待重试的）</summary>
    Pending,

    /// <summary>没有待同步账单</summary>
    Synced,
}

/// <summary>
/// 「我的」Tab 的展示规则（纯函数，单元测试覆盖）：资料卡文字、云端同步行的状态文案 / 图标 / 配色、入口行右侧的摘要值与角标。
/// 页面只负责读数据与摆放；这里不读库、不依赖界面，也不改变任何业务口径。
/// </summary>
public static class ProfilePresentation
{
    /// <summary>页面标题</summary>
    public const string Title = "我的";

    /// <summary>云端同步行的标题（设计稿 screens 04-me）</summary>
    public const string SyncTitle = "云端同步";

    /// <summary>云端同步行右侧按钮的文字（始终显示：待同步为 0 时也允许手动触发同步）</summary>
    public const string SyncButtonText = "立即同步";

    /// <summary>退出登录确认框的标题</summary>
    public const string LogoutTitle = "退出登录？";

    /// <summary>退出登录确认框的说明：退出只清登录状态，本机账单数据保留</summary>
    public const string LogoutMessage = "退出后需要重新输入手机号登录，本机已有的账单数据会保留。";

    /// <summary>退出登录确认框的确认按钮</summary>
    public const string LogoutConfirmText = "退出登录";

    /// <summary>入口行摘要值：短信识别权限已授予</summary>
    public const string SmsEnabledText = "已开启";

    /// <summary>入口行摘要值：短信识别权限未授予（含不支持短信读取的平台）</summary>
    public const string SmsDisabledText = "未开启";

    /// <summary>入口行摘要值：本月没有预算</summary>
    public const string BudgetEmptyText = "未设置";

    /// <summary>
    /// 资料卡里的脱敏手机号：11 位保留前 3 后 4（138****5678），其它长度只保留末 2 位（与账本成员页等处共用同一脱敏规则）。
    /// </summary>
    public static string MaskedPhone(string? phoneNumber) => PhoneNumberMasker.Mask(phoneNumber);

    /// <summary>
    /// 资料卡里的注册时间：「注册于 2026-03-12」（参数是应用时区的墙上时间，由调用方经 AppTimeZone 换算）。
    /// </summary>
    public static string RegisteredText(DateTime registeredLocal) =>
        $"注册于 {registeredLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// 「N 个」摘要值（账本管理 / 分类管理 / 支付渠道）。读取失败（null）返回 null——该行不显示摘要，不误导成 0。
    /// </summary>
    public static string? CountValue(int? count) =>
        count is { } n ? $"{Math.Max(n, 0).ToString(CultureInfo.InvariantCulture)} 个" : null;

    /// <summary>
    /// 预算管理的摘要值：「本月 3 项」；本月没有预算是「未设置」；读取失败（null）不显示。
    /// </summary>
    public static string? BudgetValue(int? count) => count switch
    {
        null => null,
        <= 0 => BudgetEmptyText,
        { } n => $"本月 {n.ToString(CultureInfo.InvariantCulture)} 项",
    };

    /// <summary>
    /// 短信识别的摘要值：权限已授予「已开启」、未授予「未开启」；读取失败（null）不显示。
    /// </summary>
    public static string? SmsValue(bool? enabled) => enabled switch
    {
        true => SmsEnabledText,
        false => SmsDisabledText,
        null => null,
    };

    /// <summary>
    /// 待确认账单的角标文字：超过 99 显示 99+
    /// </summary>
    public static string PendingBadge(int pendingCount) =>
        pendingCount > 99 ? "99+" : pendingCount.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// 待确认账单行的读屏名称（角标只是图形，读屏要读出笔数）
    /// </summary>
    public static string PendingLabel(int pendingCount) => pendingCount > 0 ? $"待确认账单，{pendingCount} 笔" : "待确认账单";

    /// <summary>
    /// 云端同步行的状态
    /// </summary>
    /// <param name="isSyncing">正在同步</param>
    /// <param name="isLoaded">待同步数量已经读到过（成功或失败都算）</param>
    /// <param name="loadFailed">读取待同步账单数量失败（数量不可信）</param>
    /// <param name="pendingCount">待同步账单数</param>
    public static SyncRowState ResolveSyncState(bool isSyncing, bool isLoaded, bool loadFailed, int pendingCount) =>
        isSyncing ? SyncRowState.Syncing
        : !isLoaded ? SyncRowState.Loading
        : loadFailed ? SyncRowState.Unknown
        : pendingCount > 0 ? SyncRowState.Pending
        : SyncRowState.Synced;

    /// <summary>
    /// 云端同步行的副标题文案
    /// </summary>
    public static string SyncText(SyncRowState state, int pendingCount) => state switch
    {
        SyncRowState.Syncing => "正在同步...",
        SyncRowState.Loading => "正在读取同步状态...",
        SyncRowState.Unknown => "状态未知（读取待同步账单失败）",
        SyncRowState.Pending => $"{pendingCount.ToString(CultureInfo.InvariantCulture)} 条待同步",
        _ => "已同步",
    };

    /// <summary>
    /// 云端同步行的图标：已同步是对勾云，其余是同步云
    /// </summary>
    public static string SyncIcon(SyncRowState state) => state == SyncRowState.Synced ? "mdi-cloud-check" : "mdi-cloud-sync";

    /// <summary>
    /// 云端同步行图标块的配色：已同步绿色，还没读到数量时中性灰，其余（同步中 / 未知 / 有待同步）橙色——需要留意
    /// </summary>
    public static string SyncTone(SyncRowState state) => state switch
    {
        SyncRowState.Synced => "green",
        SyncRowState.Loading => "gray",
        _ => "orange",
    };

    /// <summary>
    /// 云端同步行状态的稳定名称（写在 data-state 上，供样式与 E2E 使用）
    /// </summary>
    public static string SyncStateName(SyncRowState state) => state switch
    {
        SyncRowState.Syncing => "syncing",
        SyncRowState.Loading => "loading",
        SyncRowState.Unknown => "unknown",
        SyncRowState.Pending => "pending",
        _ => "synced",
    };
}
