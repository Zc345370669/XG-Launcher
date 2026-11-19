// SPDX-License-Identifier: MIT
namespace XG.Core;

public static partial class JavaDiscovery
{
    /// <summary>
    /// 在线程池收集本地Java候选并受限并行探测，避免阻塞界面。
    /// </summary>
    public static Task<IReadOnlyList<JavaInstallation>> DiscoverAsync(string managed, IEnumerable<string>? extra = null,
        CancellationToken token = default, IProgress<string>? progress = null, bool deep = false) =>
        Task.Run(() => DiscoverFromInputsAsync(JavaLocations.Capture(managed, extra, token), token, progress, deep, true), token);

    internal static async Task<IReadOnlyList<JavaInstallation>> DiscoverFromInputsAsync(JavaSearchInputs inputs,
        CancellationToken token = default, IProgress<string>? progress = null, bool deep = false, bool includeCache = false)
    {
        token.ThrowIfCancellationRequested();
        var paths = JavaLocations.Collect(inputs, includeCache ? Catalog.Keys : [], deep, token, progress);
        var result = new JavaInstallation?[paths.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, paths.Count), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token },
            async (index, ct) => result[index] = await InspectPathAsync(paths[index], ct));
        // 并行探测后仍按候选来源顺序返回；java和javaw别名归一到控制台程序，
        // 不同真实安装目录不合并为同一运行时。
        return result.OfType<JavaInstallation>().DistinctBy(x => x.Path, JavaLocations.PathComparer).ToArray();
    }
}
