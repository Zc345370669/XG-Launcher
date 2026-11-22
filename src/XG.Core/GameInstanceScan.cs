// SPDX-License-Identifier: MIT
namespace XG.Core;

/// <summary>元数据扫描只发现候选；真正的“已安装”状态仍须经过本地文件、Java 和加载器审计。</summary>
public static class GameInstanceScan
{
    public static IReadOnlyList<LegacyInstanceCandidate> Scan(InstanceStore store, CancellationToken token = default)
    {
        var result = new List<LegacyInstanceCandidate>();
        var occupied = store.Entries.Select(store.DirectoryFor).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.EnumerateDirectories(store.InstanceRoot))
        {
            token.ThrowIfCancellationRequested();
            if (occupied.Contains(directory) || File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) continue;
            try
            {
                var inspection = GameImport.InspectDirectory(directory);
                if (string.IsNullOrWhiteSpace(inspection.Version)) continue;
                var name = Path.GetFileName(directory);
                var instance = new Instance(Guid.NewGuid(), name, inspection.Version, inspection.Loader, LoaderVersion: inspection.LoaderVersion, AutomaticMemory: true);
                instance.Validate(); result.Add(new(instance, directory, name));
            }
            catch (Exception error) when (error is IOException or System.Text.Json.JsonException or ArgumentException or InvalidDataException) { }
        }
        return result;
    }
}
