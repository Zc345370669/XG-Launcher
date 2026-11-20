// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Security.Cryptography;

namespace XG.Core;

public sealed record SyncSelection(bool Mods, bool Config, bool ResourcePacks, bool Shaders, bool Saves)
{
    internal IEnumerable<string> Folders
    {
        get
        {
            if (Mods) yield return "mods";
            if (Config) yield return "config";
            if (ResourcePacks) yield return "resourcepacks";
            if (Shaders) yield return "shaderpacks";
            if (Saves) yield return "saves";
        }
    }
}

public sealed record SyncProgress(int Percent, string Message);
public sealed record SyncResult(int FilesAdded, string BackupDirectory);

/// <summary>
/// 在兼容实例间同步选定目录，并为目标内容建立备份和路径校验。
/// </summary>
public static class InstanceSynchronization
{
    private sealed record Planned(string Source, string Relative, string Destination);

    public static bool Compatible(Instance source, Instance target) =>
        source.Id != target.Id && source.Version == target.Version && source.Loader == target.Loader &&
        (source.Loader == "原版" || !string.IsNullOrWhiteSpace(source.LoaderVersion) &&
            source.LoaderVersion == target.LoaderVersion);

    public static SyncResult Run(InstanceStore store, Instance source, IReadOnlyList<Instance> targets,
        SyncSelection selection, IProgress<SyncProgress> progress)
    {
        if (targets.Count == 0 || targets.Select(x => x.Id).Distinct().Count() != targets.Count ||
            targets.Any(target => !Compatible(source, target)))
            throw new InvalidOperationException("请选择游戏与加载器版本完全一致的目标实例。");
        var folders = selection.Folders.ToArray();
        if (folders.Length == 0) throw new InvalidOperationException("请勾选至少一种要同步的内容。");

        var sourceRoot = store.DirectoryFor(source);
        var originals = Inventory(sourceRoot, folders);
        if (originals.Count == 0) throw new InvalidOperationException("源实例没有选中类型的文件。");
        var planned = new List<Planned>();
        var backups = new List<(Instance Instance, Dictionary<string, string> Files)>();
        progress.Report(new(2, "检查目标兼容性和冲突"));
        foreach (var target in targets)
        {
            var targetRoot = store.DirectoryFor(target);
            var existing = Inventory(targetRoot, folders);
            if (selection.Mods && existing.Keys.Any(path =>
                path.StartsWith("mods" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                IsMod(path) && !originals.ContainsKey(path)))
                throw new IOException($"{target.Name} 含源实例之外的模组，可能存在同一模组的不同版本。请检查后重试。");
            foreach (var (relative, file) in originals)
            {
                if (existing.TryGetValue(relative, out var other))
                {
                    if (!SameFile(file, other)) throw new IOException($"{target.Name} 存在不同内容的同名文件：{relative}");
                    continue;
                }
                var destination = Path.Combine(targetRoot, relative);
                CheckParents(targetRoot, destination);
                planned.Add(new(file, relative, destination));
            }
            backups.Add((target, existing));
        }

        if (planned.Count == 0) return new(0, "");
        var run = Guid.NewGuid().ToString("N");
        var backupRoot = Path.Combine(store.Root, "sync-backups", run);
        var staging = Path.Combine(store.Root, "sync-staging", run);
        Directory.CreateDirectory(backupRoot);
        var published = new List<string>();
        try
        {
            progress.Report(new(10, "备份目标实例"));
            foreach (var (target, files) in backups)
            {
                var backup = Path.Combine(backupRoot, target.Id.ToString("N") + ".zip");
                using var archive = ZipFile.Open(backup + ".tmp", ZipArchiveMode.Create);
                foreach (var (relative, file) in files)
                    archive.CreateEntryFromFile(file, relative.Replace(Path.DirectorySeparatorChar, '/'));
            }
            foreach (var target in targets)
            {
                var backup = Path.Combine(backupRoot, target.Id.ToString("N") + ".zip");
                File.Move(backup + ".tmp", backup);
            }

            Directory.CreateDirectory(staging);
            var lastReported = -1;
            for (var index = 0; index < planned.Count; index++)
            {
                var file = planned[index];
                File.Copy(file.Source, Path.Combine(staging, index.ToString()));
                var percent = 15 + 65 * (index + 1) / planned.Count;
                if (percent != lastReported)
                {
                    lastReported = percent;
                    progress.Report(new(percent, $"准备文件 {index + 1}/{planned.Count}"));
                }
            }
            progress.Report(new(90, "写入目标实例"));
            for (var index = 0; index < planned.Count; index++)
            {
                var destination = planned[index].Destination;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(Path.Combine(staging, index.ToString()), destination);
                published.Add(destination);
            }
            progress.Report(new(100, "同步完成"));
            return new(planned.Count, backupRoot);
        }
        catch
        {
            var restored = true;
            foreach (var path in published.AsEnumerable().Reverse())
            {
                try { File.Delete(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { restored = false; }
            }
            if (!restored) throw new IOException($"同步未完成，部分文件无法回滚。请检查备份：{backupRoot}");
            throw;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                try { Directory.Delete(staging, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static bool IsMod(string path) => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> Inventory(string root, string[] folders)
    {
        var contents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        RejectLink(root);
        foreach (var folder in folders)
        {
            var directory = Path.Combine(root, folder);
            RejectLink(directory);
            if (File.Exists(directory)) throw new IOException($"文件挡住同步目录：{folder}");
            if (!Directory.Exists(directory)) continue;
            var pending = new Stack<string>();
            pending.Push(directory);
            while (pending.TryPop(out var current))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                {
                    _ = InstanceStore.SafeFile(current, Path.GetFileName(entry));
                    RejectLink(entry);
                    if (Directory.Exists(entry)) pending.Push(entry);
                    else if (!contents.TryAdd(Path.GetRelativePath(root, entry), entry))
                        throw new IOException($"无法同步仅大小写不同的路径：{entry}");
                }
            }
        }
        return contents;
    }

    private static void CheckParents(string root, string file)
    {
        for (var current = Path.GetDirectoryName(file); current != null && current != root; current = Path.GetDirectoryName(current))
        {
            RejectLink(current);
            if (File.Exists(current)) throw new IOException($"文件挡住目标目录：{current}");
        }
        RejectLink(file);
        if (Directory.Exists(file)) throw new IOException($"目标目录与文件冲突：{file}");
    }

    private static void RejectLink(string path)
    {
        try
        {
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException($"同步路径包含链接：{path}");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static bool SameFile(string source, string target)
    {
        using var left = File.OpenRead(source);
        using var right = File.OpenRead(target);
        return left.Length == right.Length &&
            SHA256.HashData(left).AsSpan().SequenceEqual(SHA256.HashData(right));
    }
}
