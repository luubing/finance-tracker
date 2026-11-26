namespace FinanceTracker.Web.Components.Ui;

/// <summary>
/// KPI 卡里的一栏。
/// </summary>
/// <param name="Label">栏标题，例如「支出」</param>
/// <param name="Amount">金额</param>
/// <param name="Tone">前面小圆点的颜色：expense（红）/ income（绿）/ balance（品牌色）</param>
/// <param name="TestId">金额的 E2E 钩子（栏本身是「{TestId}-cell」）</param>
public sealed record KpiItem(string Label, decimal Amount, string Tone, string TestId)
{
    /// <summary>
    /// 三栏收支汇总（支出 / 收入 / 结余）：统计 Tab 的月度、趋势、年度三个视图与自定义报表共用，栏标题与配色一致，
    /// 只有各栏的 E2E 钩子由调用方给。结余 = 收入 - 支出。
    /// </summary>
    public static IReadOnlyList<KpiItem> Summary(decimal expense, decimal income, string expenseTestId, string incomeTestId, string balanceTestId) =>
    [
        new KpiItem("支出", expense, "expense", expenseTestId),
        new KpiItem("收入", income, "income", incomeTestId),
        new KpiItem("结余", income - expense, "balance", balanceTestId),
    ];
}
