using System.Reflection;

namespace FinanceTracker.Web.Services;

/// <summary>
/// 云端 API 基址的解析。
/// 地址不写在源码里：打包时由 MSBuild 属性 <c>CloudApiBaseUrl</c> 写进程序集元数据
/// （<see cref="AssemblyMetadataAttribute"/>，键为 <see cref="MetadataKey"/>，见 FinanceTracker.App.csproj），
/// 运行时在这里读出来、规整、校验。仓库不内置任何线上域名；这里不依赖 MAUI，便于单元测试。
/// </summary>
public static class CloudApiBaseUrlResolver
{
    /// <summary>程序集元数据里存放云端 API 基址的键（与 FinanceTracker.App.csproj 一致）</summary>
    public const string MetadataKey = "CloudApiBaseUrl";

    /// <summary>
    /// 读取并规整指定程序集里注入的云端 API 基址。
    /// </summary>
    /// <exception cref="InvalidOperationException">没有注入（项目文件被改动过）或取值不合法</exception>
    public static string FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return FromMetadata(assembly.GetCustomAttributes<AssemblyMetadataAttribute>());
    }

    /// <summary>
    /// 从程序集元数据里取出云端 API 基址并规整（与 <see cref="FromAssembly"/> 同一规则，便于不依赖真实程序集测试）。
    /// </summary>
    /// <exception cref="InvalidOperationException">没有注入或取值不合法</exception>
    public static string FromMetadata(IEnumerable<AssemblyMetadataAttribute> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var raw = metadata
            .FirstOrDefault(m => string.Equals(m.Key, MetadataKey, StringComparison.Ordinal))
            ?.Value;
        return Normalize(raw);
    }

    /// <summary>
    /// 规整云端 API 基址：去首尾空白，必须是带主机名的 http / https 绝对地址，不得带用户名密码、查询参数或片段；
    /// 结尾的 <c>/api</c> 会被去掉（与 <see cref="HttpCloudSyncClient"/> 统一补 /api/ 的约定一致，
    /// 否则 HttpClient 的相对路径请求会拼出 /api/api），结果一定以 / 结尾（HttpClient.BaseAddress 的要求）。
    /// </summary>
    /// <exception cref="InvalidOperationException">空值或不合法</exception>
    public static string Normalize(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException(
                "未配置云端 API 地址（CloudApiBaseUrl）。打包时请用 -p:CloudApiBaseUrl=https://你的服务器/ 指定，详见 README「构建与发布」。");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            throw new InvalidOperationException(
                $"云端 API 地址必须是以 http:// 或 https:// 开头的完整地址，当前值：{value}");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("云端 API 地址不能包含用户名或密码（会随安装包一起公开）。");
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException($"云端 API 地址不能带查询参数或片段（? / #），当前值：{value}");
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^4];
        }

        return $"{uri.GetLeftPart(UriPartial.Authority)}{path}/";
    }
}
