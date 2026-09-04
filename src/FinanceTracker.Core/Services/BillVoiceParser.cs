using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FinanceTracker.Core.Entities;
using FinanceTracker.Core.Enums;
using FinanceTracker.Core.Interfaces;

namespace FinanceTracker.Core.Services;

/// <summary>
/// 账单语音解析器：将语音识别出的中文口语解析为账单草稿（金额/类型/交易时间/分类/支付渠道）。
/// 纯规则实现（正则 + 中文数字转换 + 同义词匹配），离线可用、无外部依赖。
/// 解析结果仅用于预填记账表单，由用户确认后再保存，不直接落库。
/// </summary>
public sealed partial class BillVoiceParser : IBillVoiceParser
{
    /// <summary>中文数字字符 → 数值</summary>
    private static readonly Dictionary<char, int> ChineseDigits = new()
    {
        ['零'] = 0, ['〇'] = 0, ['一'] = 1, ['二'] = 2, ['两'] = 2, ['三'] = 3,
        ['四'] = 4, ['五'] = 5, ['六'] = 6, ['七'] = 7, ['八'] = 8, ['九'] = 9
    };

    /// <summary>中文数字单位字符 → 倍数</summary>
    private static readonly Dictionary<char, long> ChineseUnits = new()
    {
        ['十'] = 10, ['百'] = 100, ['千'] = 1000, ['万'] = 10000
    };

    /// <summary>收入类关键词</summary>
    private static readonly string[] IncomeKeywords =
    {
        "收入", "收到", "到账", "转入", "工资", "薪资", "奖金", "分红", "红包", "退款", "报销", "赚", "挣", "卖了"
    };

    /// <summary>明确支出的关键词（优先于收入判断，如"发红包"是支出、"红包"是收入）</summary>
    private static readonly string[] ExpenseKeywords =
    {
        "花了", "花", "支出", "消费", "付了", "付款", "支付", "买了", "买", "发红包", "扣了", "扣款"
    };

    /// <summary>分类同义词表：value 中的任意关键词命中即匹配到对应的预设分类名</summary>
    private static readonly Dictionary<string, string[]> CategorySynonyms = new()
    {
        ["餐饮"] = ["吃饭", "早饭", "早餐", "午饭", "午餐", "晚饭", "晚餐", "外卖", "餐厅", "食堂", "饭店", "小吃", "奶茶", "咖啡", "宵夜", "夜宵", "点餐"],
        ["交通"] = ["打车", "出租车", "滴滴", "地铁", "公交", "高铁", "火车", "飞机", "机票", "加油", "油费", "停车", "共享单车", "骑车"],
        ["购物"] = ["淘宝", "京东", "拼多多", "超市", "买东西", "网购", "衣服", "日用品"],
        ["居住"] = ["房租", "水电", "水费", "电费", "物业", "燃气", "房贷"],
        ["娱乐"] = ["电影", "游戏", "旅游", "唱歌", "ktv", "演出", "门票"],
        ["医疗"] = ["看病", "医院", "挂号", "买药", "药费", "门诊"],
        ["教育"] = ["买书", "学费", "培训", "课程", "网课", "考试"],
        ["通讯"] = ["话费", "流量", "网费", "宽带", "充值"]
    };

    /// <summary>支付渠道同义词表：value 中的任意关键词命中即匹配到对应的预设渠道名</summary>
    private static readonly Dictionary<string, string[]> ChannelSynonyms = new()
    {
        ["微信支付"] = ["微信", "vx", "wx", "weixin"],
        ["支付宝"] = ["支付宝", "alipay", "花呗", "余额宝"],
        ["现金"] = ["现金", "钞票", "纸币", "付现"],
        ["银行卡"] = ["银行卡", "借记卡", "储蓄卡", "刷卡", "转账", "转帐"],
        ["信用卡"] = ["信用卡"],
        ["云闪付"] = ["云闪付", "银联"],
        ["京东支付"] = ["京东", "白条"],
        ["美团支付"] = ["美团"]
    };

    // ===== 金额提取正则 =====

    /// <summary>阿拉伯数字 + 单位：35块 / 35.5元</summary>
    [GeneratedRegex(@"(?<num>\d+(?:\.\d{1,2})?)\s*(?:块|元)")]
    private static partial Regex ArabicAmountWithUnitPattern();

    /// <summary>货币符号：¥35 / ￥35</summary>
    [GeneratedRegex(@"[¥￥]\s*(?<num>\d+(?:\.\d{1,2})?)")]
    private static partial Regex CurrencySymbolAmountPattern();

    /// <summary>"人民币35" 形式</summary>
    [GeneratedRegex(@"人民币\s*(?<num>\d+(?:\.\d{1,2})?)")]
    private static partial Regex RmbAmountPattern();

    /// <summary>中文数字 + 块/元（含"点五"小数形式），仅用于文本规范化</summary>
    [GeneratedRegex(@"(?<num>[零〇一二两三四五六七八九十百千万点]+)\s*(?:块|元)")]
    private static partial Regex ChineseAmountPattern();

    /// <summary>"两块五"/"三十五块五" 形式（块后跟单个中文数字为角/小数）</summary>
    [GeneratedRegex(@"(?<int>[零〇一二两三四五六七八九十百千万]+)块(?<frac>[一二三四五六七八九])(?![零〇一二两三四五六七八九十百千万点])")]
    private static partial Regex ChineseFractionAfterUnitPattern();

    /// <summary>兜底：无单位的独立数字（仅当文本包含记账动词时启用）</summary>
    [GeneratedRegex(@"(?<![\d.])(?<num>\d{1,6}(?:\.\d{1,2})?)(?![\d.])")]
    private static partial Regex BareNumberPattern();

    // ===== 时间提取正则 =====

    [GeneratedRegex(@"大前天")]
    private static partial Regex ThreeDaysAgoPattern();

    [GeneratedRegex(@"前天")]
    private static partial Regex TwoDaysAgoPattern();

    [GeneratedRegex(@"昨天|昨日")]
    private static partial Regex YesterdayPattern();

    [GeneratedRegex(@"今天|今日")]
    private static partial Regex TodayPattern();

    /// <summary>上周X（X 为一~九/日/天）</summary>
    [GeneratedRegex(@"上周\s*(?<day>[一二三四五六七八九日天])")]
    private static partial Regex LastWeekDayPattern();

    /// <summary>"上周"（未指定星期几，指上周一）</summary>
    [GeneratedRegex(@"上周")]
    private static partial Regex LastWeekPattern();

    [GeneratedRegex(@"上个月|上月")]
    private static partial Regex LastMonthPattern();

    /// <summary>"5月20号" 形式（默认今年）</summary>
    [GeneratedRegex(@"(?<month>\d{1,2})月(?<day>\d{1,2})[号日]")]
    private static partial Regex MonthDayPattern();

    /// <summary>"15号"/"15日" 形式（默认本月，若在未来则视为上月）</summary>
    [GeneratedRegex(@"(?<!\d)(?<day>\d{1,2})[号日](?!\d)")]
    private static partial Regex DayOfMonthPattern();

    /// <inheritdoc />
    public Task<ParsedBillDraft> ParseAsync(
        string text,
        IReadOnlyList<Category> categories,
        IReadOnlyList<PaymentChannel> paymentChannels)
    {
        return Task.FromResult(Parse(text, categories, paymentChannels));
    }

    /// <summary>
    /// 解析语音识别文本为账单草稿（同步版本）
    /// </summary>
    public ParsedBillDraft Parse(
        string text,
        IReadOnlyList<Category> categories,
        IReadOnlyList<PaymentChannel> paymentChannels)
    {
        var draft = new ParsedBillDraft { RawText = text };

        // 中文数字规范化（三十五块 → 35块）后再提取金额
        var normalized = NormalizeChineseAmounts(text);
        draft.Amount = ExtractAmount(normalized, text);
        draft.Type = DetectType(text);
        draft.TransactionTime = ParseRelativeTime(text);
        draft.CategoryId = MatchCategory(text, categories, draft.Type);
        draft.PaymentChannelId = MatchChannel(text, paymentChannels);

        return draft;
    }

    // ===== 金额 =====

    /// <summary>
    /// 将文本中的中文金额规范化为阿拉伯数字形式：
    /// "三十五块" → "35块"、"三十五点五元" → "35.5元"、"两块五" → "2.5块"。
    /// </summary>
    private static string NormalizeChineseAmounts(string text)
    {
        var result = text;

        // 先处理 "两块五"/"三十五块五"（块后跟单个中文数字 = 小数部分）
        result = ChineseFractionAfterUnitPattern().Replace(result, match =>
        {
            if (!TryConvertChineseInteger(match.Groups["int"].Value, out var intValue))
            {
                return match.Value;
            }

            var frac = ChineseDigits[match.Groups["frac"].Value[0]];
            return $"{intValue}.{frac}块";
        });

        // 再处理 "三十五块" / "三十五点五元"
        result = ChineseAmountPattern().Replace(result, match =>
        {
            var converted = ConvertChineseNumber(match.Groups["num"].Value);
            return converted is null ? match.Value : $"{converted}{match.Value[^1]}";
        });

        return result;
    }

    /// <summary>
    /// 从规范化后的文本提取金额，无法识别时返回 null。
    /// 无单位数字的兜底提取仅在文本包含记账动词时启用，且取最后一个数字
    /// （避免"地铁2号线花了50"把 2 当成金额）。
    /// </summary>
    private static decimal? ExtractAmount(string normalizedText, string originalText)
    {
        var patterns = new[] { ArabicAmountWithUnitPattern(), CurrencySymbolAmountPattern(), RmbAmountPattern() };

        foreach (var pattern in patterns)
        {
            var match = pattern.Match(normalizedText);
            if (match.Success &&
                decimal.TryParse(match.Groups["num"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                return amount;
            }
        }

        // 兜底：无单位数字，要求文本包含明确的记账动词
        if (!ExpenseKeywords.Concat(IncomeKeywords).Any(originalText.Contains))
        {
            return null;
        }

        var matches = BareNumberPattern().Matches(normalizedText);
        if (matches.Count > 0 &&
            decimal.TryParse(matches[^1].Groups["num"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var bare))
        {
            return bare;
        }

        return null;
    }

    /// <summary>
    /// 中文数字（含"点"小数形式）转换为字符串，如 "三十五" → "35"、"三点五" → "3.5"。
    /// 解析失败返回 null。
    /// </summary>
    private static string? ConvertChineseNumber(string text)
    {
        var parts = text.Split('点');
        if (parts.Any(p => p.Length == 0))
        {
            return null;
        }

        if (!TryConvertChineseInteger(parts[0], out var intValue))
        {
            return null;
        }

        if (parts.Length == 1)
        {
            return intValue.ToString(CultureInfo.InvariantCulture);
        }

        // 小数部分逐位转换
        var fracBuilder = new StringBuilder();
        foreach (var ch in string.Concat(parts.Skip(1)))
        {
            if (!ChineseDigits.TryGetValue(ch, out var digit))
            {
                return null;
            }

            fracBuilder.Append(digit);
        }

        return $"{intValue}.{fracBuilder}";
    }

    /// <summary>
    /// 中文整数转换，如 "三十五" → 35、"一百二十三" → 123、"三千五" → 3500。
    /// 支持十/百/千/万位值结构；"X千Y"（千后跟单个尾数字）按口语惯例解析为 X千+Y百，
    /// 解析失败返回 false。
    /// </summary>
    private static bool TryConvertChineseInteger(string text, out long value)
    {
        value = 0;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        long section = 0;
        long number = -1;
        long lastUnit = 0;

        foreach (var ch in text)
        {
            if (ChineseDigits.TryGetValue(ch, out var digit))
            {
                number = digit;
                continue;
            }

            if (ChineseUnits.TryGetValue(ch, out var unit))
            {
                // "十"开头（十五 = 15）时缺省系数为 1
                section += (number < 0 ? 1 : number) * unit;
                lastUnit = unit;
                number = -1;
                continue;
            }

            return false;
        }

        // 口语规则："三千五" = 3500（而非 3005），"两千五" = 2500
        if (lastUnit == 1000 && number > 0)
        {
            number *= 100;
        }

        value = section + Math.Max(number, 0);
        return true;
    }

    // ===== 账单类型 =====

    /// <summary>
    /// 检测账单类型：明确支出词优先（"发红包"是支出），其次收入词，默认支出。
    /// </summary>
    private static BillType DetectType(string text)
    {
        if (ExpenseKeywords.Any(text.Contains))
        {
            return BillType.Expense;
        }

        if (IncomeKeywords.Any(text.Contains))
        {
            return BillType.Income;
        }

        return BillType.Expense;
    }

    // ===== 交易时间 =====

    /// <summary>
    /// 解析相对时间（昨天/前天/上周X/15号/5月20号等），
    /// 未提及时间时返回 null（由上层使用当前时间）。日期部分解析，时刻保留当前时间。
    /// </summary>
    private static DateTime? ParseRelativeTime(string text)
    {
        var today = DateTime.Today;

        if (ThreeDaysAgoPattern().IsMatch(text)) return WithCurrentTime(today.AddDays(-3));
        if (TwoDaysAgoPattern().IsMatch(text)) return WithCurrentTime(today.AddDays(-2));
        if (YesterdayPattern().IsMatch(text)) return WithCurrentTime(today.AddDays(-1));
        if (TodayPattern().IsMatch(text)) return WithCurrentTime(today);

        // 上周X / 上周
        var lastWeekDay = LastWeekDayPattern().Match(text);
        if (lastWeekDay.Success)
        {
            var weekDay = ParseWeekDay(lastWeekDay.Groups["day"].Value);
            if (weekDay.HasValue)
            {
                // 上周一 = 本周一 - 7 天
                var lastMonday = GetMonday(today).AddDays(-7);
                return WithCurrentTime(lastMonday.AddDays(weekDay.Value - 1));
            }
        }

        if (LastWeekPattern().IsMatch(text)) return WithCurrentTime(GetMonday(today).AddDays(-7));
        if (LastMonthPattern().IsMatch(text)) return WithCurrentTime(today.AddMonths(-1));

        // "5月20号"：默认今年，若在未来则视为去年
        var monthDay = MonthDayPattern().Match(text);
        if (monthDay.Success &&
            int.TryParse(monthDay.Groups["month"].Value, out var month) &&
            int.TryParse(monthDay.Groups["day"].Value, out var day) &&
            TryCreateDate(DateTime.Today.Year, month, day, out var date))
        {
            if (date > today)
            {
                date = date.AddYears(-1);
            }

            return WithCurrentTime(date);
        }

        // "15号"：默认本月，若在未来则视为上月
        var dayOfMonth = DayOfMonthPattern().Match(text);
        if (dayOfMonth.Success &&
            int.TryParse(dayOfMonth.Groups["day"].Value, out day) &&
            TryCreateDate(DateTime.Today.Year, DateTime.Today.Month, day, out date))
        {
            if (date > today)
            {
                date = date.AddMonths(-1);
            }

            return WithCurrentTime(date);
        }

        return null;
    }

    /// <summary>星期几中文字符 → 1~7（一=1 ... 六=6，日/天=7）</summary>
    private static int? ParseWeekDay(string text)
    {
        return text switch
        {
            "一" => 1,
            "二" => 2,
            "三" => 3,
            "四" => 4,
            "五" => 5,
            "六" => 6,
            "日" or "天" => 7,
            _ => null
        };
    }

    /// <summary>获取本周周一</summary>
    private static DateTime GetMonday(DateTime date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }

    /// <summary>保留当前时刻（记账页默认使用当前时间）</summary>
    private static DateTime WithCurrentTime(DateTime date) => date.Date.Add(DateTime.Now.TimeOfDay);

    /// <summary>安全构造日期（处理 2月30号 之类的无效日期）</summary>
    private static bool TryCreateDate(int year, int month, int day, out DateTime date)
    {
        if (month is < 1 or > 12 || day is < 1 or > 31)
        {
            date = default;
            return false;
        }

        try
        {
            date = new DateTime(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            date = default;
            return false;
        }
    }

    // ===== 分类 / 支付渠道匹配 =====

    /// <summary>
    /// 匹配分类：优先账单类型一致的分类；先精确匹配分类名（支持用户自定义分类），
    /// 再按同义词表匹配预设分类。匹配不到返回 null。
    /// </summary>
    private static Guid? MatchCategory(string text, IReadOnlyList<Category> categories, BillType type)
    {
        var candidates = categories
            .Where(c => !c.IsDeleted && c.Type == type)
            .ToList();

        if (candidates.Count == 0)
        {
            candidates = categories.Where(c => !c.IsDeleted).ToList();
        }

        return MatchByName(text, candidates, c => c.Name, c => c.Id, CategorySynonyms);
    }

    /// <summary>
    /// 匹配支付渠道：先精确匹配渠道名（支持用户自定义渠道），再按同义词表匹配。匹配不到返回 null。
    /// </summary>
    private static Guid? MatchChannel(string text, IReadOnlyList<PaymentChannel> channels)
    {
        return MatchByName(
            text,
            channels.Where(c => !c.IsDeleted).ToList(),
            c => c.Name,
            c => c.Id,
            ChannelSynonyms);
    }

    /// <summary>
    /// 通用名称匹配：优先完整包含条目名称（分值高，名称越长越优先），
    /// 其次同义词表命中（关键词越长越优先）。未命中返回 null。
    /// </summary>
    private static Guid? MatchByName<T>(
        string text,
        IReadOnlyList<T> items,
        Func<T, string> nameSelector,
        Func<T, Guid> idSelector,
        IReadOnlyDictionary<string, string[]> synonymMap)
    {
        T? best = default;
        var bestScore = 0;

        foreach (var item in items)
        {
            var name = nameSelector(item);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var score = 0;
            if (text.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                // 名称直接命中，名称越长越优先（"微信支付" 优先于 "微信"）
                score = 1000 + name.Length;
            }
            else if (synonymMap.TryGetValue(name, out var synonyms))
            {
                var hit = synonyms
                    .Where(s => text.Contains(s, StringComparison.OrdinalIgnoreCase))
                    .Select(s => (int)s.Length)
                    .DefaultIfEmpty(0)
                    .Max();
                if (hit > 0)
                {
                    score = 100 + hit;
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = item;
            }
        }

        return best is null ? null : idSelector(best);
    }
}
