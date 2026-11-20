// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Text.Json;

namespace XG.Core;

/// <summary>
/// 验证并导入整合包，受限解压覆盖文件并通过可信哈希提交下载资源。
/// </summary>
public sealed class PackImporter(HttpClient http, InstanceStore store)
{
    private readonly SemaphoreSlim[] cacheLocks = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1)).ToArray();

    /// <summary>
    /// 在受限路径中导入整合包；已有恢复目标先验证一致性，避免重复创建实例。
    /// </summary>
    public async Task<Instance> ImportAsync(string archive, Action<string> progress,
        ResourceOperations? resources = null, Action<string>? stage = null, Guid? destinationId = null)
    {
        if (destinationId is { } id && store.Entries.FirstOrDefault(item => item.Id == id) is { } registered)
        {
            if (registered.Pack && Directory.Exists(store.EnsureLocalDirectory(registered))) return registered;
            throw new IOException("整合包任务的目标实例已变化，请检查原实例。");
        }
        using var zip = ZipFile.OpenRead(archive);
        var index = zip.GetEntry("modrinth.index.json") ?? throw new InvalidDataException("缺少 Modrinth 清单。");
        if (index.Length > 8 * 1024 * 1024) throw new InvalidDataException("整合包清单过大。");
        using var source = index.Open();
        using var document = await JsonDocument.ParseAsync(source);
        var raw = document.RootElement;
        if (raw.GetProperty("formatVersion").GetInt32() != 1 || raw.GetProperty("game").GetString() != "minecraft")
            throw new InvalidDataException("无法识别整合包格式。");
        var dependencies = raw.GetProperty("dependencies");
        var game = dependencies.GetProperty("minecraft").GetString()!;
        var loaderNames = new Dictionary<string, string> { ["fabric-loader"] = "Fabric", ["quilt-loader"] = "Quilt",
            ["forge"] = "Forge", ["neoforge"] = "NeoForge" };
        var loaders = dependencies.EnumerateObject().Where(d => d.Name != "minecraft").ToArray();
        if (loaders.Length > 1 || loaders.Any(d => !loaderNames.ContainsKey(d.Name)))
            throw new InvalidDataException("清单包含不支持的加载器配置。");
        var loader = loaders.SingleOrDefault();
        var instance = new Instance(destinationId ?? Guid.NewGuid(), raw.GetProperty("name").GetString()!, game,
            loaders.Length == 0 ? "原版" : loaderNames[loader.Name], Pack: true,
            LoaderVersion: loaders.Length == 0 ? null : loader.Value.GetString());
        instance.Validate();
        var destination = store.DirectoryFor(instance);
        if (Directory.Exists(destination)) throw new IOException("整合包目标已存在，未覆盖旧数据。");
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".importing";
        Directory.CreateDirectory(staging);
        try
        {
            var files = raw.GetProperty("files").EnumerateArray().ToArray();
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var downloads = new List<(ModFile Item, string Target)>();
            foreach (var file in files)
            {
                if (file.TryGetProperty("env", out var env) && env.TryGetProperty("client", out var client) &&
                    client.GetString() is "unsupported" or "optional") continue;
                var relative = file.GetProperty("path").GetString()!;
                var safe = SafePackPath(staging, relative);
                if (!written.Add(safe)) throw new InvalidDataException("整合包包含重复文件。");
                var urls = file.GetProperty("downloads").EnumerateArray().Select(u => new Uri(u.GetString()!));
                var url = urls.FirstOrDefault(u => u.Host == "cdn.modrinth.com") ??
                    throw new InvalidDataException($"文件需通过原站取得。{relative}");
                var item = new ModFile(Path.GetFileName(safe), url, file.GetProperty("fileSize").GetInt64(),
                    file.GetProperty("hashes").GetProperty("sha512").GetString()!);
                Downloads.Validate(item);
                downloads.Add((item, safe));
            }
            var total = downloads.Sum(x => x.Item.Size);
            long complete = 0;
            for (var indexOfFile = 0; indexOfFile < downloads.Count; indexOfFile++)
            {
                var (item, target) = downloads[indexOfFile];
                var position = indexOfFile;
                progress(item.Name);
                void Report(long bytes, long size) => resources?.Report("整合包", instance.Name, complete + bytes, total,
                    item.Name, position, downloads.Count, bytes, size);
                Task Download(CancellationToken token) => WriteVerifiedAsync(item, target, Report, stage, token,
                    Path.Combine(store.Root, "pack-download-cache"));
                if (resources?.Current is { } control) await control.RunFileAsync(Download);
                else await Download(default);
                complete += item.Size;
                resources?.Report("整合包", instance.Name, complete, total, item.Name, position + 1, downloads.Count, item.Size, item.Size);
            }
            resources?.Current?.Token.ThrowIfCancellationRequested();
            stage?.Invoke("安装中");
            long overrideBytes = 0;
            foreach (var folder in new[] { "overrides/", "client-overrides/" })
                foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(folder, StringComparison.Ordinal) && e.Name.Length != 0))
                {
                    var relative = entry.FullName[folder.Length..];
                    var safe = SafePackPath(staging, relative);
                    overrideBytes = checked(overrideBytes + entry.Length);
                    if (overrideBytes > 512L * 1024 * 1024) throw new InvalidDataException("整合包覆盖文件过大。");
                    Directory.CreateDirectory(Path.GetDirectoryName(safe)!);
                    // 按普通文件解压，不恢复压缩包中的链接属性。
                    entry.ExtractToFile(safe, overwrite: true);
                }
            Directory.Move(staging, destination);
            try { store.Save(instance); }
            catch { Directory.Delete(destination, true); throw; }
            return instance;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    public async Task WriteVerifiedAsync(ModFile item, string path, Action<long, long>? progress = null,
        Action<string>? stage = null, CancellationToken token = default, string? resumeCache = null)
    {
        Downloads.Validate(item);
        var partial = resumeCache == null ? path + ".part" : Path.Combine(resumeCache, item.Sha512.ToLowerInvariant() + ".part");
        var slot = cacheLocks[(StringComparer.OrdinalIgnoreCase.GetHashCode(partial) & int.MaxValue) % cacheLocks.Length];
        await slot.WaitAsync(token);
        try
        {
            if (await VerifiedTransfer.MatchesAsync(path, item.Size, item.Sha512, token))
            { progress?.Invoke(item.Size, item.Size); return; }
            if (File.Exists(path)) throw new IOException("目标包含未通过校验的文件，未覆盖。");
            await VerifiedTransfer.StageAsync(http, item.Url, partial, item.Size, item.Sha512,
                progress ?? ((_, _) => { }), stage ?? (_ => { }), token);
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // 便携缓存与目标可能跨磁盘，先在目标目录暂存，再执行同卷提交。
            var ready = path + "." + Guid.NewGuid().ToString("N") + ".ready";
            try
            {
                File.Copy(partial, ready);
                File.Move(ready, path, overwrite: false);
                if (resumeCache == null) VerifiedTransfer.Reset(partial);
            }
            finally { if (File.Exists(ready)) File.Delete(ready); }
        }
        finally { slot.Release(); }
    }

    private static string SafePackPath(string root, string relative)
    {
        var parts = relative.Split('/');
        if (parts.Length > 30 || new[] { "libraries", "versions", "runtime", "assets", "natives", "launcher_profiles.json" }
            .Contains(parts[0], StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("整合包试图覆盖游戏核心目录。");
        return parts.Aggregate(root, InstanceStore.SafeFile);
    }
}
