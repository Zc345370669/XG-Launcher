// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XG.Core;

/// <summary>
/// 将旧实例根目录迁移到 versions；只移动已登记实例，不合并或覆盖同名目录。
/// </summary>
internal static class InstanceDirectoryLayout
{
    private sealed record Move(string Source, string Destination);
    private sealed record Backup(string Path, byte[] Content);
    private sealed record Journal(Move[] Moves, Backup[] Backups);
    private static readonly object Gate = new();

    internal static void Upgrade(string root, string? minecraft)
    {
        lock (Gate)
        {
            var gameRoot = minecraft ?? root;
            var oldRoot = Path.Combine(gameRoot, "instances");
            var newRoot = Path.Combine(gameRoot, "versions");
            var journalPath = Path.Combine(root, "instance-layout-migration.json");
            if (Directory.Exists(oldRoot) && IsLink(oldRoot) || Directory.Exists(newRoot) && IsLink(newRoot))
                throw new IOException("实例根目录是链接，不能自动迁移。");
            // 上次进程若在改目录或改索引之间退出，先恢复旧布局，再重新尝试迁移。
            if (File.Exists(journalPath))
            {
                var interrupted = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath))
                    ?? throw new InvalidDataException("实例目录迁移记录无效。");
                Validate(interrupted, root, oldRoot, newRoot);
                Restore(interrupted);
                File.Delete(journalPath);
            }
            if (!Directory.Exists(oldRoot)) return;
            var index = Path.Combine(root, "instances.json");
            var entries = File.Exists(index) ? JsonSerializer.Deserialize<Instance[]>(File.ReadAllText(index)) ?? [] : [];
            var mappingPath = Path.Combine(root, "instance-directories.json");
            var mappings = File.Exists(mappingPath) ? JsonSerializer.Deserialize<Dictionary<Guid, string>>(File.ReadAllText(mappingPath)) ?? [] : [];
            var moves = entries.Select(i => mappings.GetValueOrDefault(i.Id, i.Id.ToString("N"))).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(name => new Move(InstanceStore.SafeFile(oldRoot, name), InstanceStore.SafeFile(newRoot, name)))
                .Where(m => Directory.Exists(m.Source)).ToArray();
            if (moves.Length == 0) { if (!Directory.EnumerateFileSystemEntries(oldRoot).Any()) Directory.Delete(oldRoot); return; }
            Directory.CreateDirectory(newRoot);
            var occupied = Directory.EnumerateFileSystemEntries(newRoot).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var move in moves)
            {
                if (occupied.Contains(Path.GetFileName(move.Destination)))
                    throw new IOException("versions 中存在同名目录；未覆盖任何文件，请先处理实例目录冲突。");
                _ = VerifiedDirectoryMigration.Preview(move.Source, move.Destination);
            }
            // 只重写启动器维护的路径索引；不扫描或修改用户存档、模组配置和账号缓存。
            var paths = new[] { "instances.json", "java-bindings.json", "java-catalog.json", "downloads.json", "resource-operations.json" }
                .Select(n => Path.Combine(root, n)).Concat(moves.Select(m => Path.Combine(m.Source, ".xg-installation.json")))
                .Where(File.Exists).ToArray();
            var journal = new Journal(moves, paths.Select(p => new Backup(p, File.ReadAllBytes(p))).ToArray());
            var workspace = Path.GetDirectoryName(minecraft ?? root)!;
            string Rewrite(string value)
            {
                var normalized = value.Replace('\\', '/');
                foreach (var move in moves)
                {
                    foreach (var pair in new[] {
                        (move.Source, move.Destination),
                        (Path.GetRelativePath(workspace, move.Source), Path.GetRelativePath(workspace, move.Destination)),
                        ("portable:" + Path.GetRelativePath(minecraft == null ? root : workspace, move.Source),
                         "portable:" + Path.GetRelativePath(minecraft == null ? root : workspace, move.Destination)) })
                    {
                        var prefix = pair.Item1.Replace('\\', '/');
                        if (normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase) || normalized.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                        {
                            var result = pair.Item2.Replace('\\', '/') + normalized[prefix.Length..];
                            return value.Contains('\\') ? result.Replace('/', '\\') : result;
                        }
                    }
                }
                return value;
            }
            JsonNode? RewriteNode(JsonNode? node)
            {
                if (node is JsonObject obj)
                {
                    var rewritten = new JsonObject();
                    foreach (var key in obj.Select(p => p.Key).ToArray())
                    {
                        if (key is "Configuration" or "InstanceConfiguration" && obj[key] is JsonValue config && config.TryGetValue<string>(out var serialized))
                            rewritten[key] = RewriteNode(JsonNode.Parse(serialized))?.ToJsonString();
                        else rewritten[key] = RewriteNode(obj[key]);
                    }
                    return rewritten;
                }
                else if (node is JsonArray array)
                    return new JsonArray(array.Select(RewriteNode).ToArray());
                else if (node is JsonValue value && value.TryGetValue<string>(out var text)) return JsonValue.Create(Rewrite(text));
                return node?.DeepClone();
            }
            // 在改动文件系统前完成解析，损坏的索引不能造成半迁移状态。
            var updates = journal.Backups.Select(b => (Path: Rewrite(b.Path), Value: RewriteNode(JsonNode.Parse(b.Content)))).ToArray();
            InstanceStore.WriteJson(journalPath, journal);
            try
            {
                foreach (var move in moves) Directory.Move(move.Source, move.Destination);
                foreach (var update in updates) InstanceStore.WriteJson(update.Path, update.Value);
                File.Delete(journalPath);
            }
            catch
            {
                Restore(journal);
                File.Delete(journalPath);
                throw;
            }
            // 不删除未登记的文件夹；它们可能包含尚未导入的用户数据。
            if (!Directory.EnumerateFileSystemEntries(oldRoot).Any()) Directory.Delete(oldRoot);
        }
    }

    private static void Validate(Journal journal, string root, string oldRoot, string newRoot)
    {
        foreach (var move in journal.Moves)
            if (Path.GetDirectoryName(move.Source) != oldRoot || Path.GetDirectoryName(move.Destination) != newRoot ||
                Path.GetFileName(move.Source) != Path.GetFileName(move.Destination))
                throw new InvalidDataException("实例目录迁移路径无效。");
        var allowed = new[] { "instances.json", "java-bindings.json", "java-catalog.json", "downloads.json", "resource-operations.json" }.Select(n => Path.Combine(root, n))
            .Concat(journal.Moves.Select(m => Path.Combine(m.Source, ".xg-installation.json"))).ToHashSet(StringComparer.Ordinal);
        if (journal.Backups.Any(b => !allowed.Contains(b.Path))) throw new InvalidDataException("实例目录迁移备份路径无效。");
    }

    private static void Restore(Journal journal)
    {
        foreach (var move in journal.Moves.Reverse())
        {
            if (!Directory.Exists(move.Destination)) continue;
            if (Directory.Exists(move.Source)) throw new IOException("迁移恢复发现同名目录，已保留两份数据。");
            Directory.Move(move.Destination, move.Source);
        }
        foreach (var backup in journal.Backups)
        {
            var temporary = backup.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, backup.Content); File.Move(temporary, backup.Path, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    private static bool IsLink(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}
