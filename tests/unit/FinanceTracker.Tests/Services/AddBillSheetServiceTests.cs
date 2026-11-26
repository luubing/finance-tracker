using FinanceTracker.Web.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>「记一笔」Sheet 的开关：入口只管打开，Sheet 订阅变化渲染；每次打开都是新的一轮。</summary>
public class AddBillSheetServiceTests
{
    [Fact]
    public void 初始是关闭的()
    {
        var service = new AddBillSheetService();

        Assert.False(service.IsOpen);
        Assert.Equal(0, service.OpenCount);
    }

    [Fact]
    public void 打开_状态变为打开_计数加一_通知订阅者一次()
    {
        var service = new AddBillSheetService();
        var changes = 0;
        service.Changed += () => changes++;

        service.Open();

        Assert.True(service.IsOpen);
        Assert.Equal(1, service.OpenCount);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void 已经打开时再打开什么也不做_不重置用户刚输入的内容()
    {
        var service = new AddBillSheetService();
        var changes = 0;
        service.Open();
        service.Changed += () => changes++;

        service.Open();
        service.Open();

        Assert.True(service.IsOpen);
        Assert.Equal(1, service.OpenCount);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void 关闭_状态变为关闭_通知订阅者_计数不变()
    {
        var service = new AddBillSheetService();
        service.Open();
        var changes = 0;
        service.Changed += () => changes++;

        service.Close();

        Assert.False(service.IsOpen);
        Assert.Equal(1, service.OpenCount);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void 已经关闭时再关闭什么也不做()
    {
        var service = new AddBillSheetService();
        var changes = 0;
        service.Changed += () => changes++;

        service.Close();

        Assert.False(service.IsOpen);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void 每次重新打开计数加一_Sheet据此从空白表单开始()
    {
        var service = new AddBillSheetService();

        service.Open();
        service.Close();
        service.Open();
        service.Close();
        service.Open();

        Assert.True(service.IsOpen);
        Assert.Equal(3, service.OpenCount);
    }

    [Fact]
    public void 没有订阅者也不报错()
    {
        var service = new AddBillSheetService();

        var exception = Record.Exception(() =>
        {
            service.Open();
            service.Close();
        });

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("http://localhost:5999/?add-bill=1", true)]
    [InlineData("http://localhost:5999/?add-bill=true", true)]
    [InlineData("http://localhost:5999/?add-bill=TRUE", true)]
    [InlineData("http://localhost:5999/?add-bill", true)]
    [InlineData("http://localhost:5999/?add-bill=", true)]
    [InlineData("http://localhost:5999/?ADD-BILL=1", true)]
    [InlineData("http://localhost:5999/?foo=bar&add-bill=1", true)]
    [InlineData("http://localhost:5999/?add-bill=1&foo=bar", true)]
    [InlineData("http://localhost:5999/statistics?add-bill=1#top", true)]
    [InlineData("http://localhost:5999/?add-bill=0", false)]
    [InlineData("http://localhost:5999/?add-bill=false", false)]
    [InlineData("http://localhost:5999/?add-bill=yes", false)]
    [InlineData("http://localhost:5999/?add-billing=1", false)]
    [InlineData("http://localhost:5999/?prefix-add-bill=1", false)]
    [InlineData("http://localhost:5999/", false)]
    [InlineData("http://localhost:5999/add-bill", false)]
    [InlineData("http://localhost:5999/?", false)]
    public void 地址里带add_bill参数才算要求打开(string uri, bool expected)
    {
        Assert.Equal(expected, AddBillSheetService.IsOpenRequest(uri));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a uri")]
    [InlineData("/?add-bill=1")]
    public void 空地址或不是绝对地址_不算要求打开(string? uri)
    {
        Assert.False(AddBillSheetService.IsOpenRequest(uri));
    }

    // ------------------------------------------------------------------ 编辑模式

    private static readonly Guid BillA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BillB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void 打开编辑_记下账单Id_状态变为打开_通知订阅者一次()
    {
        var service = new AddBillSheetService();
        var changes = 0;
        service.Changed += () => changes++;

        service.OpenEdit(BillA);

        Assert.True(service.IsOpen);
        Assert.Equal(BillA, service.EditingBillId);
        Assert.Equal(1, service.OpenCount);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void 新建打开时没有正在编辑的账单()
    {
        var service = new AddBillSheetService();

        Assert.Null(service.EditingBillId);

        service.Open();

        Assert.True(service.IsOpen);
        Assert.Null(service.EditingBillId);
    }

    [Fact]
    public void 已经打开时再打开编辑什么也不做_不把新建的内容换成别的账单()
    {
        var service = new AddBillSheetService();
        service.Open();
        var changes = 0;
        service.Changed += () => changes++;

        service.OpenEdit(BillA);

        Assert.Null(service.EditingBillId);
        Assert.Equal(1, service.OpenCount);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void 已经在编辑时再打开编辑或新建什么也不做_不切换账单()
    {
        var service = new AddBillSheetService();
        service.OpenEdit(BillA);
        var changes = 0;
        service.Changed += () => changes++;

        service.OpenEdit(BillB);
        service.Open();

        Assert.Equal(BillA, service.EditingBillId);
        Assert.Equal(1, service.OpenCount);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void 关闭后账单Id清空_再新建打开不会带着上一次的账单()
    {
        var service = new AddBillSheetService();
        service.OpenEdit(BillA);

        service.Close();

        Assert.False(service.IsOpen);
        Assert.Null(service.EditingBillId);

        service.Open();

        Assert.True(service.IsOpen);
        Assert.Null(service.EditingBillId);
        Assert.Equal(2, service.OpenCount);
    }

    [Fact]
    public void 打开编辑_空Guid是调用方的错_抛参数异常且不打开()
    {
        var service = new AddBillSheetService();

        Assert.Throws<ArgumentException>(() => service.OpenEdit(Guid.Empty));

        Assert.False(service.IsOpen);
        Assert.Equal(0, service.OpenCount);
    }

    [Fact]
    public void 打开请求_带1是新建_带账单Id是编辑那一笔()
    {
        var create = AddBillSheetService.ReadOpenRequest("http://localhost:5999/?add-bill=1");
        var edit = AddBillSheetService.ReadOpenRequest($"http://localhost:5999/?add-bill={BillA:D}");

        Assert.NotNull(create);
        Assert.Null(create.Value.BillId);
        Assert.False(create.Value.IsEdit);

        Assert.NotNull(edit);
        Assert.Equal(BillA, edit.Value.BillId);
        Assert.True(edit.Value.IsEdit);
    }

    [Theory]
    [InlineData("http://localhost:5999/?add-bill=11111111-1111-1111-1111-111111111111")]
    [InlineData("http://localhost:5999/?add-bill=11111111111111111111111111111111")]
    [InlineData("http://localhost:5999/?ADD-BILL=11111111-1111-1111-1111-111111111111")]
    [InlineData("http://localhost:5999/?foo=bar&add-bill=11111111-1111-1111-1111-111111111111&x=1")]
    [InlineData("http://localhost:5999/statistics?add-bill=11111111-1111-1111-1111-111111111111#top")]
    public void 打开请求_账单Id的几种写法都认(string uri)
    {
        var request = AddBillSheetService.ReadOpenRequest(uri);

        Assert.NotNull(request);
        Assert.Equal(BillA, request.Value.BillId);
    }

    [Theory]
    [InlineData("http://localhost:5999/?add-bill=00000000-0000-0000-0000-000000000000")]
    [InlineData("http://localhost:5999/?add-bill=not-a-guid")]
    [InlineData("http://localhost:5999/?add-bill=0")]
    [InlineData("http://localhost:5999/?add-bill=false")]
    [InlineData("http://localhost:5999/?add-bill=1111")]
    [InlineData("http://localhost:5999/")]
    [InlineData("")]
    [InlineData(null)]
    public void 打开请求_认不出的参数值或没有参数_不算要求打开(string? uri)
    {
        Assert.Null(AddBillSheetService.ReadOpenRequest(uri));
        Assert.False(AddBillSheetService.IsOpenRequest(uri));
    }

    [Fact]
    public void 编辑地址_与旧地址302的目标互相对应()
    {
        var uri = "http://localhost:5999" + AddBillSheetService.EditUri(BillA);

        Assert.Equal($"/?add-bill={BillA:D}", AddBillSheetService.EditUri(BillA));
        Assert.True(AddBillSheetService.IsOpenRequest(uri));
        Assert.Equal(BillA, AddBillSheetService.ReadOpenRequest(uri)!.Value.BillId);
    }
}
