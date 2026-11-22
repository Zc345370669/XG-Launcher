// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace XG.Core;

public sealed record UpdateTheme(string Background, string Text, string Accent, string Border, bool Dark);
public sealed record UpdateApplyRequest(string Archive, string Sha256, string InstallDirectory, int ParentProcessId, long ParentStartTicks, string? TargetVersion = null, UpdateTheme? Theme = null);

/// <summary>更新只替换包内的程序与许可文件；游戏、账号、存档和配置不属于写入范围。</summary>
public static class UpdateFiles
{
    private static bool Allowed(string relative) => relative is "XGLauncher.exe" or "LICENSE" or "tools/XGUpdater.exe" || relative.StartsWith("licenses/", StringComparison.Ordinal);
    private static string Resolve(string root, string relative)
    {
        if (relative.Length > 240 || relative.Contains('\\') || relative.Split('/').Any(x => x is "" or "." or ".." || x.IndexOfAny([':', '\0', '\r', '\n']) >= 0) || !Allowed(relative))
            throw new InvalidDataException("更新包含不允许的文件路径。");
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新路径越界。");
        for (var path = full; path != null; path = Path.GetDirectoryName(path))
        {
            try
            {
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException("更新路径不能包含目录链接。");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return full;
    }

    /// <summary>先校验完整包并安全解压，随后创建可回滚的文件替换事务。</summary>
    public static Transaction Prepare(UpdateApplyRequest request)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(request.Sha256, "\\A[0-9a-fA-F]{64}\\z")) throw new InvalidDataException("更新校验值无效。");
        using var stream = File.OpenRead(request.Archive);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(request.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("更新包校验失败。");
        stream.Position = 0;
        var working = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(request.Archive))!, "apply-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(working);
        try
        {
            var staged = Path.Combine(working, "new"); Directory.CreateDirectory(staged);
            var directExe = stream.ReadByte() == 'M' && stream.ReadByte() == 'Z'; stream.Position = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
            if (directExe)
            {
                if (stream.Length > 512L * 1024 * 1024) throw new InvalidDataException("更新程序过大。");
                _ = Resolve(request.InstallDirectory, "XGLauncher.exe");
                using var output = File.Create(Resolve(staged, "XGLauncher.exe")); stream.CopyTo(output);
                names.Add("XGLauncher.exe");
            }
            else
            {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var singleExe = zip.Entries.Count(x => !x.FullName.EndsWith('/')) == 1 && zip.GetEntry("XG-Launcher/XGLauncher.exe") != null;
            if (zip.Entries.Count > 10000) throw new InvalidDataException("更新文件过多。");
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                var relative = singleExe ? "XGLauncher.exe" : entry.FullName;
                total += entry.Length;
                if (total > 768L * 1024 * 1024 || !names.Add(relative) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException("更新包结构无效。");
                var target = Resolve(staged, relative);
                _ = Resolve(request.InstallDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target);
            }
            if (!names.Contains("XGLauncher.exe") || !singleExe && (!names.Contains("LICENSE") || !names.Contains("tools/XGUpdater.exe")))
                throw new InvalidDataException("更新包缺少程序或许可证。");
            }
            using (var exe = File.OpenRead(Path.Combine(staged, "XGLauncher.exe")))
            using (var reader = new BinaryReader(exe))
            {
                if (reader.ReadUInt16() != 0x5A4D) throw new InvalidDataException("更新程序不是 Windows EXE。");
                exe.Position = 0x3c; var offset = reader.ReadInt32();
                if (offset < 0x40 || offset > exe.Length - 6) throw new InvalidDataException("更新程序结构无效。");
                exe.Position = offset;
                if (reader.ReadUInt32() != 0x00004550 || reader.ReadUInt16() != 0x8664) throw new InvalidDataException("更新程序不是 Windows x64。");
            }
            return new(request.InstallDirectory, working, names.ToArray());
        }
        catch { Directory.Delete(working, true); throw; }
    }

    public sealed class Transaction : IDisposable
    {
        private readonly string root, working;
        private readonly string[] files;
        private readonly List<(string Relative, bool Existed)> changed = [];
        private bool committed;
        internal Transaction(string root, string working, string[] files) { this.root = Path.GetFullPath(root); this.working = working; this.files = files; }
        /// <summary>旧文件先备份；任一步失败由 Dispose 恢复，未登记的用户文件不会删除。</summary>
        public void Apply()
        {
            foreach (var relative in files)
            {
                var target = Resolve(root, relative); var backup = Resolve(Path.Combine(working, "old"), relative);
                var existed = File.Exists(target);
                if (existed) { Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Copy(target, backup); }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temporary = target + "." + Guid.NewGuid().ToString("N") + ".update";
                try { File.Copy(Resolve(Path.Combine(working, "new"), relative), temporary); File.Move(temporary, target, true); changed.Add((relative, existed)); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        public void Commit() { committed = true; }
        public void Dispose()
        {
            if (!committed)
                foreach (var item in changed.AsEnumerable().Reverse())
                {
                    var target = Resolve(root, item.Relative);
                    if (item.Existed)
                    {
                        var restoring = target + "." + Guid.NewGuid().ToString("N") + ".rollback";
                        try { File.Copy(Resolve(Path.Combine(working, "old"), item.Relative), restoring); File.Move(restoring, target, true); }
                        finally { if (File.Exists(restoring)) File.Delete(restoring); }
                    }
                    else if (File.Exists(target)) File.Delete(target);
                }
            try { Directory.Delete(working, true); }
            catch (Exception e) when (committed && e is IOException or UnauthorizedAccessException) { /* 成功后的缓存清理失败不能撤销已完成更新。 */ }
        }
    }
}
