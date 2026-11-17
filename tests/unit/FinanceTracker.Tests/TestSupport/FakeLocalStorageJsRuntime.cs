using Microsoft.JSInterop;

namespace FinanceTracker.Tests.TestSupport;

/// <summary>
/// 内存版 IJSRuntime，只实现 AuthenticationService 用到的三个 localStorage 调用：
/// getItem / setItem / removeItem。<see cref="Storage"/> 可直接读写，用来模拟"本地存储里已有/残留的内容"。
/// </summary>
public sealed class FakeLocalStorageJsRuntime : IJSRuntime
{
    public Dictionary<string, string> Storage { get; } = new();

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        var key = (string)args![0]!;
        switch (identifier)
        {
            case "localStorage.getItem":
                Storage.TryGetValue(key, out var value);
                return new ValueTask<TValue>((TValue)(object?)value!);
            case "localStorage.setItem":
                Storage[key] = (string)args[1]!;
                return new ValueTask<TValue>(default(TValue)!);
            case "localStorage.removeItem":
                Storage.Remove(key);
                return new ValueTask<TValue>(default(TValue)!);
            default:
                throw new NotSupportedException($"FakeLocalStorageJsRuntime 不支持 {identifier}");
        }
    }
}
