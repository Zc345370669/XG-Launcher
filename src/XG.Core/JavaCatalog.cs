// SPDX-License-Identifier: MIT
namespace XG.Core;

public static partial class JavaDiscovery
{
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
        // Keep deterministic source order after concurrent probes. java/javaw aliases
        // resolve to one console executable; distinct installations stay distinct.
        return result.OfType<JavaInstallation>().DistinctBy(x => x.Path, JavaLocations.PathComparer).ToArray();
    }
}
