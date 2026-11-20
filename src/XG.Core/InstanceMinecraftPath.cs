// SPDX-License-Identifier: MIT
using System.Text.Json;
using CmlLib.Core;
using CmlLib.Core.VersionLoader;
using CmlLib.Core.VersionMetadata;

namespace XG.Core;

/// <summary>
/// 版本元数据直接位于隔离实例根目录；通用依赖仍沿用共享路径，不再创建内层 versions。
/// </summary>
public sealed class InstanceMinecraftPath : MinecraftPath
{
    private readonly string version;
    private readonly string? shared;
    public InstanceMinecraftPath(InstanceStore store, Instance instance) : base(Flatten(store, instance))
    {
        version = instance.Version; shared = store.SharedMinecraft;
        Versions = BasePath;
        if (shared != null)
        {
            Library = Path.Combine(shared, "libraries"); Assets = Path.Combine(shared, "assets");
            Runtime = store.ManagedJavaRoot;
        }
    }
    private static string Flatten(InstanceStore store, Instance instance)
    {
        var directory = store.EnsureLocalDirectory(instance);
        FlatVersionLayout.Upgrade(store.Root, directory, store.SharedMinecraft);
        return directory;
    }
    public override string GetVersionJsonPath(string versionName) => InstanceStore.SafeFile(BasePath, versionName + ".json");
    public override string GetVersionJarPath(string versionName)
    {
        var local = InstanceStore.SafeFile(BasePath, versionName + ".jar");
        // 旧实例已有独立本体时继续使用；否则同一原版版本的客户端 JAR 可在加载器实例间共用。
        return versionName == version && shared != null && !File.Exists(local)
            ? InstanceStore.SafeFile(InstanceStore.SafeFile(Path.Combine(shared, "versions"), versionName), versionName + ".jar") : local;
    }
    public override string GetNativePath(string versionName) => InstanceStore.SafeFile(Path.Combine(BasePath, "natives"), versionName);

    public IEnumerable<string> MetadataFiles()
    {
        if (Directory.Exists(BasePath))
            foreach (var file in Directory.EnumerateFiles(BasePath, "*.json", SearchOption.TopDirectoryOnly))
            {
                if (Path.GetFileName(file).StartsWith('.')) continue;
                yield return file;
            }
        if (shared != null && !File.Exists(GetVersionJsonPath(version)))
        {
            var cached = InstanceStore.SafeFile(InstanceStore.SafeFile(Path.Combine(shared, "versions"), version), version + ".json");
            if (File.Exists(cached)) yield return cached;
        }
    }
    public bool IsVersionResource(string? path) => path != null &&
        (string.Equals(Path.GetFullPath(path), Path.GetFullPath(GetVersionJarPath(version)), StringComparison.OrdinalIgnoreCase) ||
         string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(BasePath)), StringComparison.OrdinalIgnoreCase)
         && Path.GetExtension(path) is ".json" or ".jar");

    public MinecraftLauncherParameters CreateParameters(HttpClient http, bool installedOnly = false)
    {
        var parameters = MinecraftLauncherParameters.CreateDefault(this, http);
        var loaders = new VersionLoaderCollection { new FlatLocalLoader(this) };
        if (!installedOnly) loaders.Add(parameters.VersionLoader!);
        parameters.VersionLoader = loaders;
        return parameters;
    }

    private sealed class FlatLocalLoader(InstanceMinecraftPath paths) : IVersionLoader
    {
        public async ValueTask<VersionMetadataCollection> GetVersionMetadatasAsync(CancellationToken cancellationToken = default)
        {
            var result = new VersionMetadataCollection();
            foreach (var file in paths.MetadataFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file, cancellationToken));
                    var data = doc.RootElement;
                    if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("id", out var id) ||
                        id.GetString() is not { } name || name != Path.GetFileNameWithoutExtension(file) ||
                        !Instance.IsValidVersion(name) || (!data.TryGetProperty("mainClass", out _) && !data.TryGetProperty("inheritsFrom", out _))) continue;
                    result.AddVersion(new LocalVersionMetadata(new JsonVersionMetadataModel { Id = name, Type = "local" }, file));
                }
                catch (JsonException) { /* 无关或损坏 JSON 不得被当作可启动版本。 */ }
            }
            return result;
        }
    }
}
