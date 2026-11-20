// SPDX-License-Identifier: MIT
using XG.Core;

namespace XGLauncher;

/// <summary>
/// 从程序位置及已有工作区配置解析数据根，避免依赖不可靠的当前工作目录。
/// </summary>
internal static class PortableStorage
{
    private static string Preference => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "XGLauncher", "workspace.json");

    internal static string Resolve()
    {
        var path = AppContext.BaseDirectory;
        if (File.Exists(Preference))
            path = System.Text.Json.JsonSerializer.Deserialize<string>(File.ReadAllText(Preference)) ?? path;
        return Check(path);
    }

    internal static string Check(string path)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(path);
        var probe = Path.Combine(path, ".xg-write-" + Guid.NewGuid().ToString("N"));
        using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
        return path;
    }

    internal static void Use(string path)
    {
        path = Check(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Preference)!);
        InstanceStore.WriteJson(Preference, path);
    }

    internal static string Auxiliary(string workspace, string data, string name)
    {
        if (name is not ("downloads" or "backups" or "logs")) throw new ArgumentOutOfRangeException(nameof(name));
        var previous = Path.Combine(workspace, name);
        // 非空旧目录可能包含恢复中的下载、备份和日志，不自动迁走或删除。
        if (Directory.Exists(previous))
        {
            if (Directory.EnumerateFileSystemEntries(previous).Any()) return previous;
            try { Directory.Delete(previous); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return previous; }
        }
        return Path.Combine(data, name);
    }
}
