using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 「我的」Tab 的展示规则（资料卡文字、云端同步行的状态 / 文案 / 配色、入口行摘要值与角标）：规则集中在 <see cref="ProfilePresentation"/>
/// </summary>
public class ProfilePresentationTests
{
    // ---- 资料卡 ----

    [Theory]
    [InlineData("13812345678", "138****5678")]
    [InlineData("15900001111", "159****1111")]
    public void 手机号保留前3后4(string phone, string expected) =>
        Assert.Equal(expected, ProfilePresentation.MaskedPhone(phone));

    [Theory]
    [InlineData("12345", "****45")]
    [InlineData("1", "****")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void 异常长度的手机号不原样展示(string? phone, string expected) =>
        Assert.Equal(expected, ProfilePresentation.MaskedPhone(phone));

    [Fact]
    public void 注册时间是注册于加日期_补零()
    {
        Assert.Equal("注册于 2026-03-12", ProfilePresentation.RegisteredText(new DateTime(2026, 3, 12, 23, 59, 0)));
        Assert.Equal("注册于 2026-01-05", ProfilePresentation.RegisteredText(new DateTime(2026, 1, 5, 0, 0, 0)));
    }

    // ---- 入口行摘要值 ----

    [Theory]
    [InlineData(0, "0 个")]
    [InlineData(3, "3 个")]
    [InlineData(15, "15 个")]
    [InlineData(-2, "0 个")]
    public void 个数摘要是N个_负数按0(int count, string expected) =>
        Assert.Equal(expected, ProfilePresentation.CountValue(count));

    [Fact]
    public void 个数读取失败不显示摘要_不显示成0() =>
        Assert.Null(ProfilePresentation.CountValue(null));

    [Theory]
    [InlineData(1, "本月 1 项")]
    [InlineData(3, "本月 3 项")]
    [InlineData(12, "本月 12 项")]
    public void 预算摘要是本月N项(int count, string expected) =>
        Assert.Equal(expected, ProfilePresentation.BudgetValue(count));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 本月没有预算显示未设置(int count) =>
        Assert.Equal("未设置", ProfilePresentation.BudgetValue(count));

    [Fact]
    public void 预算读取失败不显示摘要() =>
        Assert.Null(ProfilePresentation.BudgetValue(null));

    [Fact]
    public void 短信识别摘要随权限状态变化_读取失败不显示()
    {
        Assert.Equal("已开启", ProfilePresentation.SmsValue(true));
        Assert.Equal("未开启", ProfilePresentation.SmsValue(false));
        Assert.Null(ProfilePresentation.SmsValue(null));
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(3, "3")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    [InlineData(1000, "99+")]
    public void 待确认角标超过99显示99加(int count, string expected) =>
        Assert.Equal(expected, ProfilePresentation.PendingBadge(count));

    [Fact]
    public void 待确认账单行的读屏名称带上笔数()
    {
        Assert.Equal("待确认账单", ProfilePresentation.PendingLabel(0));
        Assert.Equal("待确认账单，3 笔", ProfilePresentation.PendingLabel(3));
    }

    // ---- 云端同步行 ----

    [Theory]
    // 正在同步压过一切（包括还没读到数量、读取失败）
    [InlineData(true, false, false, 0, SyncRowState.Syncing)]
    [InlineData(true, true, true, 5, SyncRowState.Syncing)]
    // 数量还没读到：不能先显示「已同步」
    [InlineData(false, false, false, 0, SyncRowState.Loading)]
    [InlineData(false, false, true, 0, SyncRowState.Loading)]
    // 读取失败：数量不可信，状态未知（即使上次读到的数量是 0 也不能显示已同步）
    [InlineData(false, true, true, 0, SyncRowState.Unknown)]
    [InlineData(false, true, true, 7, SyncRowState.Unknown)]
    // 正常
    [InlineData(false, true, false, 2, SyncRowState.Pending)]
    [InlineData(false, true, false, 0, SyncRowState.Synced)]
    public void 同步行状态的优先级(bool syncing, bool loaded, bool failed, int pending, SyncRowState expected) =>
        Assert.Equal(expected, ProfilePresentation.ResolveSyncState(syncing, loaded, failed, pending));

    [Fact]
    public void 同步行副标题文案()
    {
        Assert.Equal("正在同步...", ProfilePresentation.SyncText(SyncRowState.Syncing, 3));
        Assert.Equal("正在读取同步状态...", ProfilePresentation.SyncText(SyncRowState.Loading, 0));
        Assert.Equal("状态未知（读取待同步账单失败）", ProfilePresentation.SyncText(SyncRowState.Unknown, 0));
        Assert.Equal("2 条待同步", ProfilePresentation.SyncText(SyncRowState.Pending, 2));
        Assert.Equal("已同步", ProfilePresentation.SyncText(SyncRowState.Synced, 0));
    }

    [Fact]
    public void 只有已同步用对勾云和绿色_其余用同步云_读取中是灰色_需留意的是橙色()
    {
        Assert.Equal("mdi-cloud-check", ProfilePresentation.SyncIcon(SyncRowState.Synced));
        Assert.Equal("green", ProfilePresentation.SyncTone(SyncRowState.Synced));

        foreach (var state in new[] { SyncRowState.Syncing, SyncRowState.Loading, SyncRowState.Unknown, SyncRowState.Pending })
        {
            Assert.Equal("mdi-cloud-sync", ProfilePresentation.SyncIcon(state));
        }

        Assert.Equal("gray", ProfilePresentation.SyncTone(SyncRowState.Loading));

        foreach (var state in new[] { SyncRowState.Syncing, SyncRowState.Unknown, SyncRowState.Pending })
        {
            Assert.Equal("orange", ProfilePresentation.SyncTone(state));
        }
    }

    [Fact]
    public void 同步行状态名称唯一且稳定()
    {
        var names = Enum.GetValues<SyncRowState>().Select(ProfilePresentation.SyncStateName).ToList();

        Assert.Equal(new[] { "syncing", "loading", "unknown", "pending", "synced" }, names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    // ---- 文案 ----

    [Fact]
    public void 退出登录的确认文案说明本机数据会保留()
    {
        Assert.Equal("退出登录？", ProfilePresentation.LogoutTitle);
        Assert.Contains("本机已有的账单数据会保留", ProfilePresentation.LogoutMessage);
        Assert.Equal("退出登录", ProfilePresentation.LogoutConfirmText);
    }
}
