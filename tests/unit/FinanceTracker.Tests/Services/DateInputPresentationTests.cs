using System.Globalization;
using FinanceTracker.Web.Models;
using Xunit;

namespace FinanceTracker.Tests.Services;

/// <summary>日期输入行（短信识别的开始日期、自定义报表的起止日期共用）的展示规则：行上的文字、输入控件的值、控件值的解析。</summary>
public sealed class DateInputPresentationTests
{
    [Fact]
    public void 行上的文字是年月日_月和日不补零()
    {
        Assert.Equal("2026年3月5日", DateInputPresentation.Label(new DateTime(2026, 3, 5)));
        Assert.Equal("2026年11月25日", DateInputPresentation.Label(new DateTime(2026, 11, 25, 23, 59, 0)));
    }

    [Fact]
    public void 输入控件的值是固定的yyyyMMdd_不随设备区域设置变化()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            Assert.Equal("2026-03-05", DateInputPresentation.ToInputValue(new DateTime(2026, 3, 5, 18, 30, 0)));
            Assert.True(DateInputPresentation.TryParse("2026-03-05", out var date));
            Assert.Equal(new DateTime(2026, 3, 5), date);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void 输入控件值往返不丢天()
    {
        foreach (var day in new[] { new DateTime(2000, 1, 1), new DateTime(2024, 2, 29), new DateTime(2026, 12, 31), new DateTime(2100, 12, 31) })
        {
            Assert.True(DateInputPresentation.TryParse(DateInputPresentation.ToInputValue(day), out var parsed));
            Assert.Equal(day, parsed);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-date")]
    [InlineData("2026/10/26")]
    [InlineData("26-10-2026")]
    [InlineData("2026-13-01")]
    [InlineData("2026-02-30")]
    [InlineData("2026-2-3")]
    [InlineData("275760-09-13")]
    public void 格式不对或不存在的日期解析失败(string? value)
    {
        Assert.False(DateInputPresentation.TryParse(value, out _));
    }

    [Fact]
    public void 短信识别页的日期规则就是这里的规则_两处不会各写各的()
    {
        var date = new DateTime(2026, 3, 5);

        Assert.Equal(DateInputPresentation.Label(date), SmsRecognitionPresentation.DateLabel(date));
        Assert.Equal(DateInputPresentation.ToInputValue(date), SmsRecognitionPresentation.ToInputValue(date));
        Assert.Equal(DateInputPresentation.InputFormat, SmsRecognitionPresentation.InputDateFormat);
        Assert.Equal(
            DateInputPresentation.TryParse("2026-03-05", out var shared),
            SmsRecognitionPresentation.TryParseInputValue("2026-03-05", out var sms));
        Assert.Equal(shared, sms);
    }
}
