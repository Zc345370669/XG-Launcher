// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Text.Json;

namespace XG.Core;

/// <summary>
/// 备份允许的用户数据范围，恢复到新实例，避免覆盖原存档及跟随目录链接。
/// </summary>
public static class Backups
{
    private static readonly string[] Folders = ["saves", "mods", "config", "resourcepacks", "shaderpacks"];
    private static readonly string[] Files = ["options.txt", "servers.dat"];

    public static void Create(InstanceStore store, Instance instance, string destination)
    {
        var game = store.DirectoryFor(instance);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                using (var entry = archive.CreateEntry("instance.json").Open())
                    JsonSerializer.Serialize(entry, instance);
                foreach (var name in Files)
                {
                    var source = Path.Combine(game, name);
                    if (File.Exists(source) && !File.GetAttributes(source).HasFlag(FileAttributes.ReparsePoint))
                        archive.CreateEntryFromFile(source, name);
                }
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };
                foreach (var name in Folders)
                {
                    var folder = Path.Combine(game, name);
                    if (!Directory.Exists(folder) || File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint)) continue;
                    foreach (var path in Directory.EnumerateFiles(folder, "*", options))
                        archive.CreateEntryFromFile(path, Path.GetRelativePath(game, path).Replace('\\', '/'));
                }
            }
            File.Move(temporary, destination, true);
        }
        finally { File.Delete(temporary); }
    }

    public static Instance Restore(InstanceStore store, string source) =>
        RegisterRestore(store, ExtractRestore(store, source));

    public static async Task<Instance> RestoreAsync(InstanceStore store, string source)
    {
        var restored = await Task.Run(() => ExtractRestore(store, source));
        return RegisterRestore(store, restored);
    }

    private static Instance RegisterRestore(InstanceStore store, Instance restored)
    {
        try { store.Save(restored); return restored; }
        catch { Directory.Delete(store.DirectoryFor(restored), true); throw; }
    }

    private static Instance ExtractRestore(InstanceStore store, string source)
    {
        using var archive = ZipFile.OpenRead(source);
        var index = archive.GetEntry("instance.json") ?? throw new InvalidDataException("缺少实例配置。");
        if (index.Length > 16384) throw new InvalidDataException("实例配置文件过大。");
        using var input = index.Open();
        var settings = JsonSerializer.Deserialize<Instance>(input) ?? throw new InvalidDataException("实例配置无效。");
        var restored = settings with { Id = Guid.NewGuid(), Java = null, LastPlayed = null };
        restored.Validate();
        var destination = store.DirectoryFor(restored);
        Directory.CreateDirectory(destination);
        try
        {
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName == "instance.json" || entry.Name.Length == 0) continue;
                var parts = entry.FullName.Split('/');
                if (parts.Length == 1 ? !Files.Contains(parts[0]) : !Folders.Contains(parts[0]))
                    throw new InvalidDataException("备份包含范围以外的文件。");
                var path = parts.Aggregate(destination, InstanceStore.SafeFile);
                total = checked(total + entry.Length);
                if (total > 16L * 1024 * 1024 * 1024) throw new InvalidDataException("备份解压超过 16 GB。");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path);
            }
            return restored;
        }
        catch { Directory.Delete(destination, true); throw; }
    }
}
