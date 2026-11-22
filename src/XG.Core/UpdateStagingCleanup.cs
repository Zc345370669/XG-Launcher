// SPDX-License-Identifier: MIT
namespace XG.Core;

/// <summary>新版启动后清理已成功退出的更新器；只删除带本程序标记的已完成会话文件。</summary>
public static class UpdateStagingCleanup
{
    public const string Marker = "XG Launcher updater v1";
    public static int CleanCompleted(string root)
    {
        if (!Directory.Exists(root)) return 0;
        CheckDirectory(root); var count = 0;
        foreach (var version in Directory.EnumerateDirectories(root).Take(100))
        {
            CheckDirectory(version);
            foreach (var session in Directory.EnumerateDirectories(version).Take(100))
            {
                if (!Guid.TryParseExact(Path.GetFileName(session), "N", out _)) continue;
                try
                {
                    CheckDirectory(session);
                    var marker = Path.Combine(session, ".xg-update-owned"); var result = Path.Combine(session, "apply.json.result");
                    if (!Exact(marker, Marker) || !Exact(result, "Updated")) continue;
                    foreach (var name in new[] { "XGUpdater.exe", "apply.json" })
                    {
                        var path = Path.Combine(session, name); if (!File.Exists(path)) continue;
                        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("更新缓存文件不能是链接。");
                        File.Delete(path); count++;
                    }
                    File.Delete(result); File.Delete(marker);
                    if (!Directory.EnumerateFileSystemEntries(session).Any()) Directory.Delete(session);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* 更新器仍在退出或缓存不可写时，下次启动再试。 */ }
            }
            if (!Directory.EnumerateFileSystemEntries(version).Any()) Directory.Delete(version);
        }
        return count;
    }
    private static bool Exact(string path, string text) => File.Exists(path) && !File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) && new FileInfo(path).Length < 128 && File.ReadAllText(path) == text;
    private static void CheckDirectory(string path)
    { for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent) if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("更新缓存目录不能是链接。"); }
}
