using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Tests.TestSupport;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// 账本页的展示规则（文案、配色、图标、名称校验）：规则集中在 <see cref="LedgerPresentation"/>，页面只负责取数与交互
/// </summary>
public class LedgerPresentationTests
{
    [Fact]
    public void 账本配色按账本Id稳定取色_同一账本每次一致且都在色板内()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        Assert.Equal(LedgerPresentation.ToneFor(id), LedgerPresentation.ToneFor(id));

        var tones = Enumerable.Range(0, 200)
            .Select(i => LedgerPresentation.ToneFor(Guid.NewGuid()))
            .ToHashSet();

        Assert.All(tones, tone => Assert.Contains(tone, LedgerPresentation.Tones));
        Assert.True(tones.Count >= 4, "不同账本应分散在多种颜色上，而不是全部同色");
    }

    [Theory]
    [InlineData("mdi-wallet", "mdi-wallet")]
    [InlineData("  mdi-book-open-variant  ", "mdi-book-open-variant")]
    [InlineData("mdi-numeric-1-box", "mdi-numeric-1-box")]
    public void 合法图标类名原样使用(string input, string expected) =>
        Assert.Equal(expected, LedgerPresentation.NormalizeIcon(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("book")]
    [InlineData("MDI-BOOK")]
    [InlineData("mdi-")]
    [InlineData("mdi--book")]
    [InlineData("mdi-book mdi-x")]
    [InlineData("mdi-book;color:red")]
    public void 空值或非法图标类名回退默认图标_不会渲染出空白色块(string? input) =>
        Assert.Equal(LedgerPresentation.DefaultIcon, LedgerPresentation.NormalizeIcon(input));

    [Fact]
    public void 候选图标都合法且互不重复_包含默认图标()
    {
        var choices = LedgerPresentation.IconChoices;

        Assert.Contains(choices, c => c.Icon == LedgerPresentation.DefaultIcon);
        Assert.Equal(choices.Count, choices.Select(c => c.Icon).Distinct().Count());
        Assert.Equal(choices.Count, choices.Select(c => c.Label).Distinct().Count());
        Assert.All(choices, c =>
        {
            Assert.Equal(c.Icon, LedgerPresentation.NormalizeIcon(c.Icon));
            Assert.False(string.IsNullOrWhiteSpace(c.Label), $"{c.Icon} 缺少读屏名称");
        });
    }

    [Theory]
    [InlineData(LedgerMemberRole.Owner, "所有者")]
    [InlineData(LedgerMemberRole.Editor, "可编辑")]
    [InlineData(LedgerMemberRole.Viewer, "只读")]
    public void 角色标签(LedgerMemberRole role, string expected) =>
        Assert.Equal(expected, LedgerPresentation.RoleLabel(role));

    [Theory]
    [InlineData("13912345678", "139****5678")]
    [InlineData(null, "未知用户")]
    [InlineData("", "未知用户")]
    [InlineData("   ", "未知用户")]
    public void 展示用手机号脱敏_没有手机号显示未知用户(string? phone, string expected) =>
        Assert.Equal(expected, LedgerPresentation.MaskPhone(phone));

    [Fact]
    public void 同步为满足外键临时造的占位手机号不当作真实手机号展示()
    {
        var stub = $"{User.StubPhonePrefix}{Guid.NewGuid():N}"[..(User.StubPhonePrefix.Length + 15)];

        Assert.True(User.IsStubPhoneNumber(stub));
        Assert.Equal("未知用户", LedgerPresentation.MaskPhone(stub));
        Assert.Equal("所有者 未知用户", LedgerPresentation.JoinedSubtitle(stub));
    }

    [Theory]
    [InlineData(0, 0, "暂无账单")]
    [InlineData(89, 0, "89 笔账单")]
    [InlineData(12, 1, "12 笔账单")] // 只有所有者自己：还不算共享
    [InlineData(3, 3, "3 位成员 · 你是所有者")]
    [InlineData(0, 2, "2 位成员 · 你是所有者")]
    public void 我的账本副标题_共享显示成员数_否则显示账单数(int billCount, int memberCount, string expected) =>
        Assert.Equal(expected, LedgerPresentation.OwnedSubtitle(billCount, memberCount));

    [Fact]
    public void 加入的共享账本副标题显示脱敏后的所有者() =>
        Assert.Equal("所有者 138****0099", LedgerPresentation.JoinedSubtitle("13800000099"));

    [Theory]
    [InlineData("家庭共享", "「家庭共享」邀请你加入")]
    [InlineData(null, "「未知账本」邀请你加入")]
    [InlineData("  ", "「未知账本」邀请你加入")]
    public void 邀请卡标题(string? ledgerName, string expected) =>
        Assert.Equal(expected, LedgerPresentation.InvitationTitle(ledgerName));

    [Fact]
    public void 邀请卡副标题含脱敏邀请人与角色() =>
        Assert.Equal("来自 139****0021 · 角色：可编辑", LedgerPresentation.InvitationSubtitle("13900000021", LedgerMemberRole.Editor));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void 账本名称为空_提示请输入(string? name) =>
        Assert.Equal("请输入账本名称", LedgerPresentation.ValidateName(name));

    [Fact]
    public void 账本名称按去掉首尾空白后的长度校验上限()
    {
        Assert.Null(LedgerPresentation.ValidateName(new string('账', LedgerPresentation.NameMaxLength)));
        Assert.Null(LedgerPresentation.ValidateName($"  {new string('账', LedgerPresentation.NameMaxLength)}  "));
        Assert.Equal(
            $"账本名称最多 {LedgerPresentation.NameMaxLength} 个字",
            LedgerPresentation.ValidateName(new string('账', LedgerPresentation.NameMaxLength + 1)));
    }

    [Fact]
    public async Task 名称长度上限与数据库对账本名称的长度配置一致()
    {
        await using var db = await SqliteTestDatabase.CreateAsync();

        var maxLength = db.Context.Model.FindEntityType(typeof(Ledger))!
            .FindProperty(nameof(Ledger.Name))!
            .GetMaxLength();

        Assert.Equal(LedgerPresentation.NameMaxLength, maxLength);
    }
}
