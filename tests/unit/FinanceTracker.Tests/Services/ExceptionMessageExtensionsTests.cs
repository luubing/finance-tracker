using FinanceTracker.Core.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// <see cref="ExceptionMessageExtensions.GetUserMessage"/> 测试：
/// 保证用户看到的是真实原因，而不是 EF Core 恒定的外层文案
/// "An error occurred while saving the entity changes. See the inner exception for details."
/// </summary>
public class ExceptionMessageExtensionsTests
{
    private const string EfGenericMessage =
        "An error occurred while saving the entity changes. See the inner exception for details.";

    private static DbUpdateException Wrap(Exception inner) => new(EfGenericMessage, inner);

    [Fact]
    public void 非数据库异常_原样返回消息()
    {
        Assert.Equal("账本不存在", new ArgumentException("账本不存在").GetUserMessage());
        Assert.Equal("无权操作", new InvalidOperationException("无权操作").GetUserMessage());
    }

    [Fact]
    public void 外键失败_给出中文说明并附带内层技术原因()
    {
        var ex = Wrap(new SqliteException("FOREIGN KEY constraint failed", 19));

        var message = ex.GetUserMessage();

        Assert.DoesNotContain("An error occurred while saving", message);
        Assert.Contains("关联数据不存在", message);
        Assert.Contains("FOREIGN KEY constraint failed", message);
    }

    [Theory]
    [InlineData("UNIQUE constraint failed: Users.PhoneNumber", 19, "数据已存在")]
    [InlineData("NOT NULL constraint failed: Bills.Note", 19, "必填项缺失")]
    [InlineData("database is locked", 5, "数据库正忙")]
    [InlineData("database table is locked", 6, "数据库正忙")]
    [InlineData("attempt to write a readonly database", 8, "只读")]
    [InlineData("disk I/O error", 10, "存储空间不足")]
    [InlineData("database or disk is full", 13, "存储空间不足")]
    [InlineData("unable to open database file", 14, "无法打开本地数据库")]
    public void 常见SQLite错误_给出对应中文说明(string sqliteMessage, int errorCode, string expectedFragment)
    {
        var message = Wrap(new SqliteException(sqliteMessage, errorCode)).GetUserMessage();

        Assert.Contains(expectedFragment, message);
        Assert.Contains(sqliteMessage, message);
    }

    [Theory]
    [InlineData("23503: insert or update on table \"Bills\" violates foreign key constraint \"FK_Bills_Categories\"", "关联数据不存在")]
    [InlineData("23505: duplicate key value violates unique constraint \"IX_Users_PhoneNumber\"", "数据已存在")]
    [InlineData("23502: null value in column \"Name\" violates not-null constraint", "必填项缺失")]
    [InlineData("22001: value too long for type character varying(500)", "内容过长")]
    public void 常见PostgreSQL错误_给出对应中文说明(string pgMessage, string expectedFragment)
    {
        var message = Wrap(new InvalidOperationException(pgMessage)).GetUserMessage();

        Assert.Contains(expectedFragment, message);
    }

    [Theory]
    [InlineData("23505: duplicate key value violates unique constraint \"IX_Users_PhoneNumber\"\r\n\r\nDETAIL: Key (\"PhoneNumber\")=(13800138000) already exists.", "13800138000")]
    [InlineData("23503: insert or update on table \"Bills\" violates foreign key constraint \"FK_Bills_Categories\"\r\n\r\nDETAIL: Key (CategoryId)=(7b1c0e1e-1111-2222-3333-444455556666) is not present in table \"Categories\".", "7b1c0e1e")]
    [InlineData("23502: null value in column \"Name\" of relation \"Categories\" violates not-null constraint\r\n\r\nDETAIL: Failing row contains (a, b, 午饭30元, null).", "午饭30元")]
    public void PostgreSQL开启错误详情时_用户文案不带键值和整行数据(string pgMessage, string sensitiveFragment)
    {
        // Npgsql 默认不带 DETAIL；一旦连接串里打开 "Include Error Detail" 排查问题，DETAIL 就会带上冲突的键值（手机号）
        // 和整行数据（备注、金额）。这些文案会展示给用户，服务端还会经推送结果返回给客户端，不能原样透出
        var message = Wrap(new InvalidOperationException(pgMessage)).GetUserMessage();

        Assert.DoesNotContain(sensitiveFragment, message);
        Assert.DoesNotContain("DETAIL", message);
        Assert.Contains("constraint", message);   // 约束名等排查信息仍然保留
    }

    [Fact]
    public void 非数据库异常带PostgreSQL详情时_同样去掉敏感部分()
    {
        var ex = new InvalidOperationException(
            "23505: duplicate key value violates unique constraint \"IX_Users_PhoneNumber\"\r\n\r\nDETAIL: Key (\"PhoneNumber\")=(13800138000) already exists.");

        var message = ex.GetUserMessage();

        Assert.DoesNotContain("13800138000", message);
        Assert.Contains("IX_Users_PhoneNumber", message);
    }

    [Fact]
    public void 无法识别的数据库错误_至少展示内层真实原因()
    {
        var message = Wrap(new InvalidOperationException("some unknown provider failure")).GetUserMessage();

        Assert.Equal("some unknown provider failure", message);
    }

    [Fact]
    public void 没有内层异常_只能原样返回外层消息()
    {
        Assert.Equal(EfGenericMessage, new DbUpdateException(EfGenericMessage).GetUserMessage());
    }

    [Fact]
    public void 并发冲突_提示数据已被修改()
    {
        var ex = new DbUpdateConcurrencyException("expected 1 row(s), but actually affected 0 row(s)");

        Assert.Contains("已被其他操作修改或删除", ex.GetUserMessage());
    }

    [Fact]
    public void 被AggregateException包装的数据库异常_同样能识别()
    {
        var wrapped = new AggregateException(Wrap(new SqliteException("FOREIGN KEY constraint failed", 19)));

        Assert.Contains("关联数据不存在", wrapped.GetUserMessage());
    }
}
