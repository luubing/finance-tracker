using System.Text;

namespace FinanceTracker.Web.Services;

/// <summary>
/// 把 Serilog 紧凑 JSON 里的消息模板（<c>@mt</c>，例如「注册/登录失败，手机号: {PhoneNumber}」）和同一行里的属性渲染成人能读的消息。
/// <para>
/// 应用写日志用的是 <c>CompactJsonFormatter</c>：它只写模板 <c>@mt</c>，不写渲染后的 <c>@m</c>（那是 <c>RenderedCompactJsonFormatter</c> 的行为），
/// 所以只认 <c>@m</c> 的话，真机上每条日志的「消息」都是空的。模板语法见 messagetemplates.org：
/// <c>{Name}</c> / <c>{@Name}</c> / <c>{$Name}</c> / <c>{Name,10:format}</c> / <c>{0}</c>，<c>{{</c> 与 <c>}}</c> 是转义的花括号。
/// </para>
/// 属性不存在（被丢弃、被脱敏过滤）时原样保留 <c>{Name}</c>，与 Serilog 自己渲染缺失属性的行为一致。
/// </summary>
public static class LogMessageTemplate
{
    public static string Render(string? template, IReadOnlyDictionary<string, string> properties)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var text = new StringBuilder(template.Length + 16);
        var index = 0;

        while (index < template.Length)
        {
            var current = template[index];

            if (current == '{')
            {
                if (index + 1 < template.Length && template[index + 1] == '{')
                {
                    text.Append('{');
                    index += 2;
                    continue;
                }

                var close = template.IndexOf('}', index + 1);
                if (close < 0)
                {
                    // 没有配对的右花括号：整段当作普通文字
                    text.Append(template, index, template.Length - index);
                    break;
                }

                var token = template.Substring(index + 1, close - index - 1);
                if (TryGetPropertyName(token, out var name) && properties.TryGetValue(name, out var value))
                {
                    text.Append(value);
                }
                else
                {
                    text.Append('{').Append(token).Append('}');
                }

                index = close + 1;
                continue;
            }

            if (current == '}' && index + 1 < template.Length && template[index + 1] == '}')
            {
                text.Append('}');
                index += 2;
                continue;
            }

            text.Append(current);
            index++;
        }

        return text.ToString();
    }

    /// <summary>
    /// 取花括号里的属性名：去掉开头的解构运算符（@ / $），名字到第一个逗号（对齐）或冒号（格式）为止；
    /// 名字只能是字母 / 数字 / 下划线，否则这不是一个属性占位符（按普通文字处理）
    /// </summary>
    private static bool TryGetPropertyName(string token, out string name)
    {
        name = string.Empty;

        var start = token.Length > 0 && token[0] is '@' or '$' ? 1 : 0;
        var end = token.IndexOfAny([',', ':'], start);
        if (end < 0)
        {
            end = token.Length;
        }

        var candidate = token.Substring(start, end - start).Trim();
        if (candidate.Length == 0 || !candidate.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            return false;
        }

        name = candidate;
        return true;
    }
}
