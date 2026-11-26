using FinanceTracker.Web.Services;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>
/// Serilog 消息模板的渲染（messagetemplates.org 的语法）：错误日志页要靠它把紧凑 JSON 里的 <c>@mt</c> 变回一句人话
/// </summary>
public class LogMessageTemplateTests
{
    private static readonly IReadOnlyDictionary<string, string> Props = new Dictionary<string, string>
    {
        ["PhoneNumber"] = "138****5678",
        ["Count"] = "12",
        ["0"] = "位置参数",
        ["SourceContext"] = "A.B",
    };

    [Theory]
    [InlineData("手机号: {PhoneNumber}", "手机号: 138****5678")]
    [InlineData("共 {Count} 条", "共 12 条")]
    [InlineData("{PhoneNumber} / {Count}", "138****5678 / 12")]
    [InlineData("没有任何占位符", "没有任何占位符")]
    [InlineData("", "")]
    public void 基本占位符代入属性值(string template, string expected) =>
        Assert.Equal(expected, LogMessageTemplate.Render(template, Props));

    [Theory]
    [InlineData("对象 {@PhoneNumber}", "对象 138****5678")]
    [InlineData("字符串 {$PhoneNumber}", "字符串 138****5678")]
    [InlineData("对齐 {Count,6}", "对齐 12")]
    [InlineData("格式 {Count:000}", "格式 12")]
    [InlineData("对齐加格式 {Count,-6:N0}", "对齐加格式 12")]
    [InlineData("位置 {0}", "位置 位置参数")]
    [InlineData("来源 {SourceContext}", "来源 A.B")]
    public void 解构运算符_对齐_格式只决定写法_名字照常查找(string template, string expected) =>
        Assert.Equal(expected, LogMessageTemplate.Render(template, Props));

    [Theory]
    [InlineData("缺失 {Nope}", "缺失 {Nope}")]
    [InlineData("缺失 {@Nope,5:x}", "缺失 {@Nope,5:x}")]
    [InlineData("不是占位符 {a b}", "不是占位符 {a b}")]
    [InlineData("空花括号 {}", "空花括号 {}")]
    [InlineData("括号里是标点 {, }", "括号里是标点 {, }")]
    public void 属性不存在或不是合法名字时_原样保留(string template, string expected) =>
        Assert.Equal(expected, LogMessageTemplate.Render(template, Props));

    [Theory]
    [InlineData("字面量 {{PhoneNumber}}", "字面量 {PhoneNumber}")]
    [InlineData("{{{PhoneNumber}}}", "{138****5678}")]
    [InlineData("右花括号 }} 单独出现", "右花括号 } 单独出现")]
    [InlineData("孤立的右花括号 } 保持原样", "孤立的右花括号 } 保持原样")]
    [InlineData("没有闭合 {PhoneNumber", "没有闭合 {PhoneNumber")]
    public void 转义的花括号与残缺的括号(string template, string expected) =>
        Assert.Equal(expected, LogMessageTemplate.Render(template, Props));

    [Fact]
    public void 模板为空返回空串() =>
        Assert.Equal(string.Empty, LogMessageTemplate.Render(null, Props));
}
