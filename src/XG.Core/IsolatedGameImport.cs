// SPDX-License-Identifier: MIT
using System.Security.Cryptography;
namespace XG.Core;

/// <summary>将所选版本的用户数据与公共资源分别导入；不把整个 versions 树塞进一个实例。</summary>
internal static class IsolatedGameImport
{
    internal static async Task CopyAsync(InstanceStore store, Instance instance, string source, string destination,
        IProgress<MigrationFile>? progress, CancellationToken token, string? profile = null, bool keepData = true, bool inheritUserData = true, Action<string>? sharedConflict = null)
    {
        source = Path.GetFullPath(source); destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("导入目标已存在，未覆盖任何文件。");
        if (source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || source == destination)
            throw new IOException("导入来源与目标重叠。");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var versions = Path.Combine(source, "versions"); var selected = profile ?? source;
        if (profile == null && Directory.Exists(versions))
        {
            var matches = Directory.EnumerateDirectories(versions).Where(directory => ProfileMatches(directory, instance)).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("来源含多个匹配版本或无法确认所选版本，请直接选择具体版本目录后重新检测。");
            selected = matches[0];
        }
        foreach (var entry in Directory.EnumerateFileSystemEntries(selected))
        {
            var first = Path.GetFileName(entry);
            // 只过滤启动器根级认证资料，保留游戏 config 内正常的模组配置。
            var privateName = first.ToLowerInvariant();
            if (privateName is "versions" or "pcl" or "hmclversion.cfg" or "runtimes" or "runtime" or "launcher_accounts.json" or "launcher_profiles.json"
                or "launcher_profiles_microsoft_store.json" or "launcher_accounts_microsoft_store.json" or "accounts.json" or "hmcl_accounts.json"
                or "credentials.json" or "auth.json" or "app.db" or "app.db-wal" or "app.db-shm" or "minecraftinstance.json" or "profile.json" or "instance.json"
                or ".xg-installation.json" || privateName.StartsWith("microsoft-msal.bin", StringComparison.Ordinal)) continue;
            if (!keepData && first is not ("libraries" or "assets")
                && (Directory.Exists(entry) || Path.GetExtension(first) is not (".json" or ".jar"))) continue;
            if (File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("导入不跟随文件或目录链接。");
            if (Directory.Exists(entry)) foreach (var file in Inventory(entry)) AddSelected(file);
            else AddSelected(entry);
        }

        void AddSelected(string file)
        {
            var relative = Path.GetRelativePath(selected, file);
            var first = relative.Split(Path.DirectorySeparatorChar)[0];
            Add(file, Path.Combine(first is "libraries" or "assets" ? store.SharedMinecraft ?? destination : destination, relative));
        }
        if (selected != source)
        {
            foreach (var name in keepData && inheritUserData ? new[] { "mods", "config", "saves", "resourcepacks", "shaderpacks", "logs", "crash-reports", "screenshots" } : Array.Empty<string>())
            {
                var folder = Path.Combine(source, name); if (!Directory.Exists(folder)) continue;
                foreach (var file in Inventory(folder))
                {
                    var target = Path.Combine(destination, name, Path.GetRelativePath(folder, file));
                    if (!files.ContainsKey(target)) Add(file, target);
                }
            }
            foreach (var name in keepData && inheritUserData ? new[] { "options.txt", "optionsof.txt", "servers.dat" } : Array.Empty<string>())
                if (File.Exists(Path.Combine(source, name)) && !files.ContainsKey(Path.Combine(destination, name))) Add(Path.Combine(source, name), Path.Combine(destination, name));
            foreach (var name in new[] { "libraries", "assets" })
            {
                var folder = Path.Combine(source, name); if (!Directory.Exists(folder)) continue;
                foreach (var file in Inventory(folder)) Add(file, Path.Combine(store.SharedMinecraft ?? destination, name, Path.GetRelativePath(folder, file)));
            }
            if (instance.Loader is not ("原版" or "Vanilla"))
            {
                var baseRoot = InstanceStore.SafeFile(versions, instance.Version);
                foreach (var extension in new[] { ".json", ".jar" })
                {
                    var file = InstanceStore.SafeFile(baseRoot, instance.Version + extension);
                    if (File.Exists(file)) Add(file, Path.Combine(store.SharedMinecraft ?? store.Root, "versions", instance.Version, instance.Version + extension));
                }
            }
        }
        var staging = Path.Combine(store.Root, "import-staging-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(staging);
        var added = new List<string>(); var items = files.ToArray();
        var ownsDestination = false;
        try
        {
            for (var i = 0; i < items.Length; i++)
            {
                token.ThrowIfCancellationRequested(); var (target, original) = items[i]; CheckParents(target);
                if (File.Exists(target))
                {
                    if (await SameAsync(target, original, token)) continue;
                    var message = $"同名文件内容不同。来源：{original}；目标：{target}。已保留目标文件。";
                    if (sharedConflict == null || targetsInstance(target)) throw new IOException(message);
                    sharedConflict(message);
                    continue;
                }
                var temporary = Path.Combine(staging, i.ToString()); File.Copy(original, temporary);
                if (!await SameAsync(original, temporary, token)) throw new IOException("导入文件校验失败。");
                progress?.Report(new(Path.GetRelativePath(source, original), new FileInfo(original).Length));
            }
            // 先独占实例目录；失败时不删除其他进程或玩家创建的同名目录。
            if (Directory.Exists(destination)) throw new IOException("导入目标在校验期间出现，请重新确认。");
            Directory.CreateDirectory(destination);
            ownsDestination = true;
            for (var i = 0; i < items.Length; i++)
            {
                token.ThrowIfCancellationRequested(); var temporary = Path.Combine(staging, i.ToString()); if (!File.Exists(temporary)) continue;
                var target = items[i].Key; CheckParents(target); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(temporary, target); added.Add(target);
            }
            store.Save(instance);
        }
        catch
        {
            foreach (var file in added.AsEnumerable().Reverse()) if (File.Exists(file)) File.Delete(file);
            if (ownsDestination && !Directory.EnumerateFileSystemEntries(destination).Any()) Directory.Delete(destination);
            throw;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        bool targetsInstance(string target) =>
            target.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        void Add(string original, string target)
        {
            CheckParents(original);
            if (File.GetAttributes(original).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("导入不跟随文件链接。");
            if (files.TryGetValue(target, out var previous))
            {
                using var a = File.OpenRead(original); using var b = File.OpenRead(previous);
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(a), SHA256.HashData(b))) throw new IOException($"来源的全局和隔离配置存在冲突。来源：{previous} 与 {original}；目标：{target}。");
            }
            else files.Add(target, original);
        }
    }
    private static bool ProfileMatches(string directory, Instance instance)
    {
        var scan = GameImport.Scan(directory);
        return scan.Instances.Any(x => x.Profile == directory && x.Inspection.Version == instance.Version &&
            (x.Inspection.Loader == instance.Loader || x.Inspection.Loader == "原版" && instance.Loader == "Vanilla") &&
            (instance.LoaderVersion == null || x.Inspection.LoaderVersion == instance.LoaderVersion));
    }
    private static IEnumerable<string> Inventory(string root)
    {
        CheckParents(Path.Combine(root, "probe"));
        foreach (var file in Directory.EnumerateFileSystemEntries(root))
        {
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("导入不跟随文件或目录链接。");
            if (Directory.Exists(file)) { foreach (var child in Inventory(file)) yield return child; } else yield return file;
        }
    }
    private static void CheckParents(string file)
    { for (var directory = Path.GetDirectoryName(file); directory != null; directory = Path.GetDirectoryName(directory)) if (Directory.Exists(directory) && File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("导入路径不能包含链接。"); }
    private static async Task<bool> SameAsync(string a, string b, CancellationToken token)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        await using var first = File.OpenRead(a); await using var second = File.OpenRead(b);
        var left = await SHA256.HashDataAsync(first, token); var right = await SHA256.HashDataAsync(second, token);
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
