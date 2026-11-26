namespace FinanceTracker.Tests.TestSupport;

/// <summary>
/// 定位仓库根目录（含 FinanceTracker.sln 的目录），供需要读取源码 / 静态资源文件的测试使用。
/// </summary>
internal static class RepoRoot
{
    private static readonly Lazy<string> Root = new(Find);

    /// <summary>仓库根目录的绝对路径。</summary>
    public static string Path => Root.Value;

    /// <summary>仓库内相对路径（用 / 分隔）的绝对路径。</summary>
    public static string Combine(string relativePath) =>
        System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(System.IO.Path.Combine(directory.FullName, "FinanceTracker.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("未能从测试输出目录向上找到仓库根目录（FinanceTracker.sln）");
    }
}
