// SPDX-License-Identifier: MIT
using System.Text.Json;
namespace XG.Core;

/// <summary>把工作区内的游戏根保存为相对路径，整体搬迁不依赖当前工作目录。</summary>
public static class GameRootPreference
{
    public static string Resolve(string workspace, string data)
    {
        var file = Path.Combine(data, "game-root.json");
        if (!File.Exists(file)) return Path.Combine(Path.GetFullPath(workspace), ".minecraft");
        var value = JsonSerializer.Deserialize<string>(File.ReadAllText(file));
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("游戏目录配置为空。");
        return Path.GetFullPath(value, Path.GetFullPath(workspace));
    }
    public static void Save(string workspace, string data, string chosen)
    {
        var full = Path.GetFullPath(chosen);
        Directory.CreateDirectory(full);
        using (File.Create(Path.Combine(full, ".xg-write-" + Guid.NewGuid().ToString("N")), 1, FileOptions.DeleteOnClose)) { }
        var relative = Path.GetRelativePath(workspace, full);
        InstanceStore.WriteJson(Path.Combine(data, "game-root.json"), relative.Split(Path.DirectorySeparatorChar)[0] == ".." ? full : relative);
    }
}
