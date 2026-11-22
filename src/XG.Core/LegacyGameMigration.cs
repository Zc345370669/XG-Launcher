// SPDX-License-Identifier: MIT
using System.Security.Cryptography;
using System.Text.Json;
namespace XG.Core;

public sealed record LegacyInstanceCandidate(Instance Instance, string Source, string DirectoryName);
public sealed record LegacyMigrationPlan(string Workspace, string[] Sources, LegacyInstanceCandidate[] Instances, int Files, long Bytes);

/// <summary>
/// 只读扫描历史游戏目录；确认后备份、逐文件散列校验并复制到新游戏根。
/// 不构造旧 InstanceStore，避免其兼容升级在预览阶段移动原始文件。
/// </summary>
public static class LegacyGameMigration
{
    public static LegacyMigrationPlan Preview(string workspace, InstanceStore destination)
    {
        workspace = Path.GetFullPath(workspace);
        var roots = new[] { "minecraft", "data/minecraft", "data/instances", "data/versions", "data/libraries", "data/assets" }
            .Select(x => Path.GetFullPath(Path.Combine(workspace, x))).Where(Directory.Exists)
            .Where(x => !x.Equals(destination.SharedMinecraft, StringComparison.OrdinalIgnoreCase)).ToArray();
        var sourceData = Path.Combine(workspace, "data");
        var entries = Read<Instance[]>(Path.Combine(sourceData, "instances.json")) ?? destination.Entries.ToArray();
        var mappings = Read<Dictionary<Guid, string>>(Path.Combine(sourceData, "instance-directories.json")) ?? destination.DirectorySnapshot();
        var candidates = new List<LegacyInstanceCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            CheckNoLinks(root);
            var containers = Path.GetFileName(root) is "instances" or "versions" ? new[] { root } : new[] { Path.Combine(root, "versions"), Path.Combine(root, "instances") };
            foreach (var container in containers.Where(Directory.Exists))
            foreach (var directory in Directory.EnumerateDirectories(container))
            {
                if (!seen.Add(Path.GetFullPath(directory))) continue;
                var name = Path.GetFileName(directory);
                var known = entries.FirstOrDefault(x => mappings.GetValueOrDefault(x.Id, x.Id.ToString("N")).Equals(name, StringComparison.OrdinalIgnoreCase));
                var inspection = GameImport.InspectDirectory(directory);
                if (known == null && string.IsNullOrWhiteSpace(inspection.Version)) continue;
                var instance = known ?? new Instance(Guid.NewGuid(), name, inspection.Version!, inspection.Loader, LoaderVersion: inspection.LoaderVersion, AutomaticMemory: true);
                if (instance.Java is { } java && java.StartsWith("portable:", StringComparison.Ordinal))
                    instance = instance with { Java = java["portable:".Length..].Split('/').Aggregate(workspace, InstanceStore.SafeFile) };
                instance.Validate();
                var targetName = known == null ? name : mappings.GetValueOrDefault(instance.Id, name);
                _ = InstanceStore.SafeFile(destination.InstanceRoot, targetName);
                // 已有目标一律不覆盖；不以时间戳或同名推断新旧。
                if (Directory.Exists(Path.Combine(destination.InstanceRoot, targetName))) continue;
                candidates.Add(new(instance, directory, targetName));
            }
        }
        if (candidates.Select(x => x.DirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != candidates.Count)
            throw new IOException("多个旧目录具有同名实例，请分别导入并选择新名称。");
        var count = 0; long bytes = 0;
        foreach (var root in roots)
        foreach (var file in Inventory(root)) { count++; bytes += new FileInfo(file).Length; }
        return new(workspace, roots, candidates.ToArray(), count, bytes);
    }
    public static async Task<string> ExecuteAsync(LegacyMigrationPlan plan, InstanceStore destination, string backups,
        IProgress<MigrationFile>? progress = null, CancellationToken token = default)
    {
        // 执行前重新预览，发现目录变化或新目标冲突时停止，不使用陈旧预览覆盖文件。
        var current = Preview(plan.Workspace, destination);
        if (!plan.Sources.SequenceEqual(current.Sources) || !plan.Instances.Select(x => x.Source).SequenceEqual(current.Instances.Select(x => x.Source)) || current.Files != plan.Files || current.Bytes != plan.Bytes)
            throw new IOException("旧数据在预览后已变化，请重新检测。");
        if (plan.Instances.Length == 0) throw new InvalidOperationException("没有可迁移的旧版实例。");
        var backup = Path.Combine(backups, "legacy-layout-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(backup);
        InstanceStore.WriteJson(Path.Combine(backup, "migration.json"), new { State = "BackingUp", plan.Sources });
        for (var i = 0; i < plan.Sources.Length; i++)
            await VerifiedDirectoryMigration.CopyVerifiedAsync(plan.Sources[i], Path.Combine(backup, "source-" + i), progress, token);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Instances)
        foreach (var file in Inventory(item.Source))
        {
            var relative = Path.GetRelativePath(item.Source, file).Replace('\\', '/');
            if (relative == ".xg-installation.json" || relative.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase)) continue;
            string target;
            if (relative.StartsWith("libraries/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                target = Path.Combine(destination.SharedMinecraft ?? destination.Root, relative);
            else
            {
                if (relative.StartsWith("versions/", StringComparison.OrdinalIgnoreCase))
                {
                    var pieces = relative.Split('/');
                    if (pieces.Length != 3 || !pieces[2].EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !pieces[2].EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                        throw new IOException("发现非标准嵌套版本文件，请使用智能导入确认范围。");
                    relative = pieces[2];
                }
                target = Path.Combine(destination.InstanceRoot, item.DirectoryName, relative);
            }
            Add(file, target);
        }
        foreach (var root in plan.Sources)
        foreach (var shared in new[] { "libraries", "assets" })
        {
            var source = Path.GetFileName(root) == shared ? root : Path.Combine(root, shared); if (!Directory.Exists(source)) continue;
            foreach (var file in Inventory(source)) Add(file, Path.Combine(destination.SharedMinecraft ?? destination.Root, shared, Path.GetRelativePath(source, file)));
        }
        var originalEntries = destination.Entries.ToArray();
        var originalDirectories = destination.DirectorySnapshot();
        var newFiles = new List<string>();
        var stage = Path.Combine(backup, "staged"); Directory.CreateDirectory(stage);
        var pairs = files.ToArray();
        try
        {
            // 先验证全部冲突，再提交。仅新增文件会进入回滚清单。
            for (var i = 0; i < pairs.Length; i++)
            {
                token.ThrowIfCancellationRequested(); var (target, source) = pairs[i]; CheckParents(target);
                if (File.Exists(target)) { if (!await SameAsync(source, target, token)) throw new IOException("目标存在不同内容的同名文件，未覆盖。"); continue; }
                var staged = Path.Combine(stage, i.ToString());
                File.Copy(source, staged);
                if (!await SameAsync(source, staged, token)) throw new IOException("迁移校验失败。");
                progress?.Report(new(Path.GetRelativePath(plan.Workspace, source), new FileInfo(source).Length));
            }
            for (var i = 0; i < pairs.Length; i++)
            {
                token.ThrowIfCancellationRequested(); var target = pairs[i].Key;
                var staged = Path.Combine(stage, i.ToString()); if (!File.Exists(staged)) continue;
                CheckParents(target); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(staged, target); newFiles.Add(target);
            }
            foreach (var item in plan.Instances)
            {
                destination.AdoptDirectory(item.Instance, item.DirectoryName);
                // 安装凭据不盲目复制。新路径由既有本地安装校验重新生成，缺失依赖不会伪装为就绪。
                using var offline = new HttpClient();
                await new GameService(offline, destination).InspectInstallationAsync(item.Instance, token, true);
            }
            InstanceStore.WriteJson(Path.Combine(backup, "migration.json"), new { State = "CopiedVerified", plan.Sources, Instances = plan.Instances.Select(x => x.Instance.Id).ToArray() });
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            return backup;
        }
        catch
        {
            destination.RestoreCatalog(originalEntries, originalDirectories);
            foreach (var file in newFiles.AsEnumerable().Reverse()) if (File.Exists(file)) File.Delete(file);
            InstanceStore.WriteJson(Path.Combine(backup, "migration.json"), new { State = "FailedOriginalsRetained", plan.Sources });
            throw;
        }
        void Add(string source, string target)
        {
            if (files.TryGetValue(target, out var previous) && previous != source)
            {
                using var a = File.OpenRead(source); using var b = File.OpenRead(previous);
                if (!SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b))) throw new IOException("旧目录内部存在冲突，原始数据保留。");
            }
            else files[target] = source;
        }
    }
    private static T? Read<T>(string file) => File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file)) : default;
    private static IEnumerable<string> Inventory(string root)
    {
        CheckNoLinks(root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        { CheckNoLinks(entry); if (Directory.Exists(entry)) { foreach (var file in Inventory(entry)) yield return file; } else yield return entry; }
    }
    private static void CheckNoLinks(string path) { if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("迁移不跟随目录或文件链接。"); }
    private static void CheckParents(string file) { for (var dir = Path.GetDirectoryName(file); dir != null; dir = Path.GetDirectoryName(dir)) if (Directory.Exists(dir)) CheckNoLinks(dir); }
    private static async Task<bool> SameAsync(string a, string b, CancellationToken token)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        await using var first = File.OpenRead(a); await using var second = File.OpenRead(b);
        var left = await SHA256.HashDataAsync(first, token); var right = await SHA256.HashDataAsync(second, token);
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
