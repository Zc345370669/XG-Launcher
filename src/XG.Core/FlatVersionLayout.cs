// SPDX-License-Identifier: MIT
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XG.Core;

/// <summary>合并实例内部旧 versions；仅清理逐字节一致的重复文件，冲突时不覆盖。</summary>
internal static class FlatVersionLayout
{
    private sealed record Entry(string Source, string Destination, bool Duplicate);
    private sealed record Backup(string Path, byte[] Bytes);
    private sealed record Journal(Entry[] Entries, Backup[] Backups, bool Committed = false);
    private static readonly object Gate = new();
    private static readonly string[] IndexFiles = ["instances.json", "java-bindings.json", "java-catalog.json", "downloads.json", "resource-operations.json"];

    internal static void Upgrade(string root, string directory, string? sharedMinecraft = null)
    {
        lock (Gate)
        {
            var legacy = Path.Combine(directory, "versions");
            var journalFile = Path.Combine(directory, ".xg-flat-versions-migration.json");
            if (File.Exists(journalFile))
            {
                var pending = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalFile)) ?? throw new InvalidDataException("版本目录迁移记录无效。");
                Validate(pending, root, directory, legacy);
                if (pending.Committed) Finish(pending, legacy);
                else Rollback(pending);
                File.Delete(journalFile);
            }
            if (!Directory.Exists(legacy)) return;
            if (IsLink(directory) || IsLink(legacy)) throw new IOException("版本目录含链接，不能自动合并。");
            // 既有扫描会拒绝链接及不安全树；这里只移动版本资源，不触碰兄弟目录内的用户数据。
            _ = VerifiedDirectoryMigration.Preview(legacy, directory + ".flatten-preflight");
            var entries = new List<Entry>();
            var destinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(legacy, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var parts = Path.GetRelativePath(legacy, file).Split(Path.DirectorySeparatorChar);
                var relative = parts.Length == 1 ? parts : parts.Skip(1).ToArray();
                if (parts.Length > 2 && parts[1] == "natives") relative = new[] { "natives", parts[0] }.Concat(parts.Skip(2)).ToArray();
                var destination = relative.Aggregate(directory, InstanceStore.SafeFile);
                // 已存在目标路径的任一级不能是链接，防止合并写出隔离目录。
                for (var parent = destination; parent != directory; parent = Path.GetDirectoryName(parent)!)
                    if ((File.Exists(parent) || Directory.Exists(parent)) && IsLink(parent)) throw new IOException("目标路径含链接，未合并版本资源。");
                if (Directory.Exists(destination)) throw new IOException("版本资源目标是同名目录，未覆盖任何文件。");
                var duplicate = File.Exists(destination);
                if (duplicate && !Same(file, destination)) throw new IOException("版本目录存在同名但内容不同的文件，未自动覆盖：" + Path.GetFileName(destination));
                if (destinations.TryGetValue(destination, out var prior))
                {
                    if (!Same(file, prior)) throw new IOException("旧版本目录包含冲突文件，未自动合并：" + Path.GetFileName(destination));
                    duplicate = true;
                }
                else destinations.Add(destination, file);
                entries.Add(new(file, destination, duplicate));
            }
            if (entries.Count == 0) { RemoveEmpty(legacy); return; }
            var backups = IndexFiles.Select(n => Path.Combine(root, n)).Append(Path.Combine(directory, ".xg-installation.json"))
                .Where(File.Exists).Select(p => new Backup(p, File.ReadAllBytes(p))).ToArray();
            var journal = new Journal(entries.ToArray(), backups);
            // 安装凭据相对于工作区（或无共享目录时的 data 父目录），优先匹配具体文件路径。
            var configuredWorkspace = Path.GetDirectoryName(sharedMinecraft ?? root)!;
            var javaWorkspace = sharedMinecraft == null ? root : configuredWorkspace;
            var replacements = entries.SelectMany(e => new[] {
                (e.Source, e.Destination),
                (Path.GetRelativePath(configuredWorkspace, e.Source), Path.GetRelativePath(configuredWorkspace, e.Destination)),
                ("portable:" + Path.GetRelativePath(javaWorkspace, e.Source), "portable:" + Path.GetRelativePath(javaWorkspace, e.Destination)) })
                .OrderByDescending(p => p.Item1.Length).ToArray();
            string Rewrite(string text)
            {
                var normalized = text.Replace('\\', '/');
                foreach (var (from, to) in replacements)
                {
                    var prefix = from.Replace('\\', '/');
                    if (!normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    return text.Contains('\\') ? to.Replace('/', '\\') : to.Replace('\\', '/');
                }
                return text;
            }
            JsonNode? RewriteNode(JsonNode? node)
            {
                if (node is JsonObject obj)
                {
                    var updated = new JsonObject();
                    foreach (var (key, value) in obj)
                        updated[key] = key is "Configuration" or "InstanceConfiguration" && value is JsonValue config && config.TryGetValue<string>(out var serialized)
                            ? JsonValue.Create(RewriteNode(JsonNode.Parse(serialized))?.ToJsonString()) : RewriteNode(value);
                    return updated;
                }
                if (node is JsonArray array) return new JsonArray(array.Select(RewriteNode).ToArray());
                return node is JsonValue val && val.TryGetValue<string>(out var text) ? JsonValue.Create(Rewrite(text)) : node?.DeepClone();
            }
            var updates = backups.Select(b => (b.Path, Value: RewriteNode(JsonNode.Parse(b.Bytes)))).ToArray();
            InstanceStore.WriteJson(journalFile, journal);
            try
            {
                foreach (var item in entries.Where(e => !e.Duplicate)) { Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!); File.Move(item.Source, item.Destination); }
                foreach (var item in updates) InstanceStore.WriteJson(item.Path, item.Value);
                // 提交后才清理重复源文件；中断恢复不会把已经去重的资源错误回滚为丢失状态。
                InstanceStore.WriteJson(journalFile, journal with { Committed = true });
            }
            catch { Rollback(journal); File.Delete(journalFile); throw; }
            Finish(journal, legacy);
            File.Delete(journalFile);
            InstanceStore.WriteJson(Path.Combine(directory, ".xg-directory-layout-report.json"),
                new { MovedFiles = entries.Count(e => !e.Duplicate), IdenticalDuplicatesRemoved = entries.Count(e => e.Duplicate), ConflictingFilesOverwritten = 0 });
        }
    }
    private static bool IsLink(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
    private static bool Same(string a, string b)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        using var first = File.OpenRead(a); using var second = File.OpenRead(b);
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(first), SHA256.HashData(second));
    }
    private static void Finish(Journal journal, string legacy)
    {
        if (journal.Entries.Any(e => !File.Exists(e.Destination))) throw new IOException("迁移目标文件缺失，停止清理旧目录。");
        foreach (var item in journal.Entries.Where(e => e.Duplicate && File.Exists(e.Source)))
        { if (!Same(item.Source, item.Destination)) throw new IOException("重复文件内容已变化，保留源文件并停止清理。"); File.Delete(item.Source); }
        RemoveEmpty(legacy);
    }
    private static void RemoveEmpty(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var child in Directory.GetDirectories(directory)) RemoveEmpty(child);
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
    }
    private static void Rollback(Journal journal)
    {
        foreach (var item in journal.Entries.Where(e => !e.Duplicate).Reverse())
        {
            if (!File.Exists(item.Destination)) continue;
            if (File.Exists(item.Source)) throw new IOException("回滚存在同名文件，保留双方数据。");
            Directory.CreateDirectory(Path.GetDirectoryName(item.Source)!); File.Move(item.Destination, item.Source);
        }
        foreach (var backup in journal.Backups)
        {
            var temporary = backup.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, backup.Bytes); File.Move(temporary, backup.Path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    private static void Validate(Journal journal, string root, string directory, string legacy)
    {
        bool Inside(string parent, string path) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(parent) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        if (journal.Entries.Any(e => !Inside(legacy, e.Source) || !Inside(directory, e.Destination) || Inside(legacy, e.Destination)))
            throw new InvalidDataException("版本迁移资源路径无效。");
        var allowed = IndexFiles.Select(n => Path.Combine(root, n)).Append(Path.Combine(directory, ".xg-installation.json")).ToHashSet(StringComparer.Ordinal);
        if (journal.Backups.Any(b => !allowed.Contains(b.Path))) throw new InvalidDataException("版本迁移备份路径无效。");
        foreach (var path in journal.Entries.SelectMany(e => new[] { e.Source, e.Destination }))
            for (var parent = path; parent != directory; parent = Path.GetDirectoryName(parent)!)
                if ((File.Exists(parent) || Directory.Exists(parent)) && IsLink(parent)) throw new IOException("版本迁移恢复路径含链接。");
    }
}
