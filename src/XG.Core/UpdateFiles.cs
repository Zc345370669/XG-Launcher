// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace XG.Core;

public sealed record UpdateApplyRequest(string Archive, string Sha256, string InstallDirectory, int ParentProcessId, long ParentStartTicks);

/// <summary>更新只替换包内的启动器程序，许可与更新器嵌入程序内部；游戏、账号、存档和配置不属于写入范围。</summary>
public static class UpdateFiles
{
    private static bool Allowed(string relative) => relative == "XGLauncher.exe";
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
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
            if (zip.Entries.Count > 10000) throw new InvalidDataException("更新文件过多。");
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                total += entry.Length;
                if (total > 768L * 1024 * 1024 || !names.Add(entry.FullName.StartsWith("XG-Launcher/", StringComparison.Ordinal) ? entry.FullName[12..] : entry.FullName) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException("更新包结构无效。");
                var relative = entry.FullName.StartsWith("XG-Launcher/", StringComparison.Ordinal) ? entry.FullName[12..] : entry.FullName;
                var target = Resolve(staged, relative);
                _ = Resolve(request.InstallDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target);
            }
            if (names.Count != 1 || !names.Contains("XGLauncher.exe"))
                throw new InvalidDataException("更新包必须只包含 XG-Launcher/XGLauncher.exe。");
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
                changed.Add((relative, existed));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temporary = target + "." + Guid.NewGuid().ToString("N") + ".update";
                try { File.Copy(Resolve(Path.Combine(working, "new"), relative), temporary); File.Move(temporary, target, true); }
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
                    if (item.Existed) File.Copy(Resolve(Path.Combine(working, "old"), item.Relative), target, true);
                    else if (File.Exists(target)) File.Delete(target);
                }
            Directory.Delete(working, true);
        }
    }
}
