namespace FinanceTracker.Web.Components.Ui;

/// <summary>
/// 分段控件的一个选项
/// </summary>
/// <param name="Value">取值（与 <see cref="SegmentedControl.Value"/> 比较，E2E 通过 data-value 观察）</param>
/// <param name="Label">显示文字（同时是读屏名称）</param>
public sealed record SegmentOption(string Value, string Label);
