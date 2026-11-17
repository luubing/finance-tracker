using Microsoft.EntityFrameworkCore;

namespace FinanceTracker.Core.Exceptions;

/// <summary>
/// 异常消息转换：把异常转成面向用户（并便于排查）的文案。
/// </summary>
public static class ExceptionMessageExtensions
{
    /// <summary>
    /// 获取面向用户的错误文案。
    /// EF Core 的 <see cref="DbUpdateException"/> 外层消息恒为
    /// "An error occurred while saving the entity changes. See the inner exception for details."，
    /// 对用户和排查都毫无信息量，真正的原因在内层异常（如 "SQLite Error 19: 'FOREIGN KEY constraint failed'"）。
    /// 对数据库写入失败：常见错误给出中文说明并附带内层原因，其余错误至少展示内层原因；
    /// 其它异常保持原消息（业务/校验异常本身已是面向用户的文案，如「账本不存在」「无权操作」）。
    /// </summary>
    /// <param name="exception">捕获到的异常</param>
    /// <returns>可直接展示给用户的文案</returns>
    public static string GetUserMessage(this Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var dbUpdateException = FindDbUpdateException(exception);
        if (dbUpdateException == null)
        {
            return StripSensitiveDetail(exception.Message);
        }

        var rootMessage = dbUpdateException.GetBaseException().Message;
        var explanation = Explain(dbUpdateException, rootMessage);
        var technicalReason = StripSensitiveDetail(rootMessage);

        return explanation == null ? technicalReason : $"{explanation}（技术原因：{technicalReason}）";
    }

    /// <summary>
    /// 去掉 PostgreSQL 错误消息里的 DETAIL 段。Npgsql 默认不带 DETAIL，一旦连接串打开 "Include Error Detail"
    /// 排查问题，DETAIL 就会带上冲突的键值（如手机号）和整行数据（如备注、金额）；
    /// 这些文案会直接展示给用户，服务端还会经推送结果返回给客户端，不能原样透出。
    /// 约束名等排查信息在 DETAIL 之前，会保留。
    /// </summary>
    private static string StripSensitiveDetail(string message)
    {
        var index = message.IndexOf("DETAIL:", StringComparison.Ordinal);
        return index < 0 ? message : message[..index].TrimEnd();
    }

    /// <summary>
    /// 沿内层异常链查找数据库写入异常（兼容被 AggregateException 等包装的情况）
    /// </summary>
    private static DbUpdateException? FindDbUpdateException(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is DbUpdateException dbUpdateException)
            {
                return dbUpdateException;
            }
        }

        return null;
    }

    /// <summary>
    /// 把常见的数据库错误（SQLite / PostgreSQL）翻译成中文说明；无法识别时返回 null
    /// </summary>
    private static string? Explain(DbUpdateException exception, string rootMessage)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            return "数据已被其他操作修改或删除，请刷新后重试";
        }

        // SQLite: "FOREIGN KEY constraint failed"；PostgreSQL(23503): "violates foreign key constraint"
        if (Contains(rootMessage, "FOREIGN KEY constraint failed") ||
            Contains(rootMessage, "violates foreign key constraint"))
        {
            return "关联数据不存在：用户、分类、支付渠道或账本可能已被删除或尚未同步，请刷新页面或重新登录后重试";
        }

        // SQLite: "UNIQUE constraint failed"；PostgreSQL(23505): "violates unique constraint"
        if (Contains(rootMessage, "UNIQUE constraint failed") ||
            Contains(rootMessage, "violates unique constraint"))
        {
            return "数据已存在，请勿重复提交";
        }

        // SQLite: "NOT NULL constraint failed"；PostgreSQL(23502): "violates not-null constraint"
        if (Contains(rootMessage, "NOT NULL constraint failed") ||
            Contains(rootMessage, "violates not-null constraint"))
        {
            return "有必填项缺失，请补全后再试";
        }

        // PostgreSQL(22001): "value too long for type character varying(n)"
        if (Contains(rootMessage, "value too long"))
        {
            return "输入内容过长，请缩短后再试";
        }

        if (Contains(rootMessage, "database is locked") ||
            Contains(rootMessage, "database table is locked"))
        {
            return "数据库正忙，请稍后重试";
        }

        if (Contains(rootMessage, "readonly database"))
        {
            return "数据库当前为只读状态，请重启应用后重试";
        }

        if (Contains(rootMessage, "disk I/O error") ||
            Contains(rootMessage, "database or disk is full"))
        {
            return "存储空间不足或磁盘读写失败，请清理空间后重试";
        }

        if (Contains(rootMessage, "unable to open database file"))
        {
            return "无法打开本地数据库文件，请重启应用后重试";
        }

        return null;
    }

    private static bool Contains(string text, string value) =>
        text.Contains(value, StringComparison.OrdinalIgnoreCase);
}
