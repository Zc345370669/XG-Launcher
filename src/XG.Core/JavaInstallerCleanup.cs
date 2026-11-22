// SPDX-License-Identifier: MIT
using CmlLib.Core.Files;
using CmlLib.Core.Tasks;
using System.Security.Cryptography;
using System.Text;

namespace XG.Core;

/// <summary>只清理本次 Java 安装拥有的安装包及已校验文件对应的临时残留，不扫描删除运行时 ZIP/JAR。</summary>
internal static class JavaInstallerCleanup
{
    internal static GameFile[] OwnLegacyPackages(IEnumerable<GameFile> files, string managed, string? installerCache = null) => files.Select(file =>
    {
        var extraction = file.UpdateTask?.OfType<LegacyJavaExtractionTask>().FirstOrDefault();
        if (extraction == null) return file;
        // 按组件相对路径和下载来源生成稳定身份，重试及工作区搬迁后仍可复用失败安装的缓存。
        var identity = Path.GetRelativePath(managed, extraction.ExtractTo).Replace('\\', '/') + "\n" + file.Url;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return file with { Path = Path.Combine(installerCache ?? Path.Combine(managed, ".xg-install-cache"), key, "jre.lzma") };
    }).ToArray();

    internal static string TransferPart(GameFile file, string managed, string? installerCache)
    {
        if (installerCache == null || file.Path == null || !Inside(managed, file.Path)) return file.Path + ".xg-part";
        var identity = Path.GetRelativePath(managed, file.Path).Replace('\\', '/') + "\n" + file.Url;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return Path.Combine(installerCache, key, "runtime.part");
    }

    internal static async Task<int> AfterVerifiedAsync(string managed, string verifiedJava, IEnumerable<GameFile> files, Action<string>? warning = null, string? installerCache = null)
    {
        managed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(managed));
        if (!Inside(managed, verifiedJava) || !File.Exists(verifiedJava)) return 0;
        try { if (Linked(managed, verifiedJava)) return 0; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { warning?.Invoke("Java 清理前检查未完成：" + error.GetType().Name); return 0; }
        var removed = 0; var cache = Path.GetFullPath(installerCache ?? Path.Combine(managed, ".xg-install-cache"));
        foreach (var file in files)
        {
            if (file.Path is not { } path) continue;
            try
            {
                var scope = Inside(cache, path) ? cache : managed;
                if (!Inside(scope, path) || Linked(scope, path)) continue;
                var archive = file.UpdateTask?.OfType<LegacyJavaExtractionTask>().FirstOrDefault();
                if (archive != null && Inside(cache, path) &&
                    (Inside(managed, archive.ExtractTo) || Path.GetFullPath(archive.ExtractTo) == managed) && Inside(Path.GetFullPath(archive.ExtractTo), verifiedJava))
                {
                    if (File.Exists(path)) { VerifiedTransfer.Reset(path); removed++; }
                    var folder = Path.GetDirectoryName(path)!;
                    if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
                }
                else if (Inside(managed, path) && file.Size > 0 && file.Hash is { Length: 40 or 64 or 128 } hash && hash.All(Uri.IsHexDigit) &&
                    await VerifiedTransfer.MatchesAsync(path, file.Size, hash, CancellationToken.None))
                {
                    var part = TransferPart(file, managed, installerCache);
                    if (File.Exists(part) && !Linked(Inside(cache, part) ? cache : managed, part)) { File.Delete(part); removed++; }
                    VerifiedTransfer.ClearMetadata(part);
                    var folder = Path.GetDirectoryName(part)!;
                    if (Inside(cache, part) && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { warning?.Invoke("Java 安装临时文件清理未完成：" + error.GetType().Name + " · " + Path.GetFileName(path)); }
        }
        try { if (Directory.Exists(cache) && !Directory.EnumerateFileSystemEntries(cache).Any()) Directory.Delete(cache); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { warning?.Invoke("Java 安装临时目录暂时无法清理：" + error.GetType().Name); }
        return removed;
    }
    private static bool Inside(string root, string path) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool Linked(string root, string path)
    {
        for (var current = Path.GetFullPath(path); Inside(root, current); current = Path.GetDirectoryName(current)!)
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return true;
        return Directory.Exists(root) && File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint);
    }
}
