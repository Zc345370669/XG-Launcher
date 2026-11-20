// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Security.Cryptography;

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
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temporary = target + "." + Guid.NewGuid().ToString("N") + ".update";
                try { File.Copy(Resolve(Path.Combine(working, "new"), relative), temporary); File.Move(temporary, target, true); changed.Add((relative, existed)); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        public void Commit() { committed = true; }
        private static void ReplaceFromBackup(string backup, string target)
        {
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".rollback";
            try { File.Copy(backup, temporary); File.Move(temporary, target, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public void Dispose()
        {
            if (!committed)
                foreach (var item in changed.AsEnumerable().Reverse())
                {
                    var target = Resolve(root, item.Relative);
                    if (item.Existed) ReplaceFromBackup(Resolve(Path.Combine(working, "old"), item.Relative), target);
                    else if (File.Exists(target)) File.Delete(target);
                }
            // 备份恢复失败时异常在上面抛出，保留工作目录供人工恢复。
            // 已提交后的临时目录清理失败不得把成功更新误报为失败并再次启动程序。
            try { Directory.Delete(working, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}


/// <summary>只传递固定状态，不把本地路径、异常正文或凭据带回界面。</summary>
public enum UpdateOutcome { None, Updated, InvalidPackage, AccessDenied, FilesBusy, LaunchFailed, Failed }

public static class UpdateReceipt
{
    public static UpdateOutcome Failure(Exception error) => error switch
    {
        InvalidDataException => UpdateOutcome.InvalidPackage,
        UnauthorizedAccessException => UpdateOutcome.AccessDenied,
        System.ComponentModel.Win32Exception => UpdateOutcome.LaunchFailed,
        IOException => UpdateOutcome.FilesBusy,
        _ => UpdateOutcome.Failed
    };

    /// <summary>原子写入固定枚举；诊断写入失败不改变已提交的更新结果。</summary>
    public static void Write(string file, UpdateOutcome outcome)
    {
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, outcome.ToString()); File.Move(temporary, file, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    /// <summary>仅读取本次下载暂存根目录内的固定回执，不接受任意命令行路径或链接。</summary>
    public static UpdateOutcome Read(string stagingRoot, string candidate)
    {
        try
        {
            var root = Path.GetFullPath(stagingRoot).TrimEnd(Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(candidate);
            var parts = Path.GetRelativePath(root, full).Split(Path.DirectorySeparatorChar);
            if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out _) || parts[1] != "apply.json.result") return UpdateOutcome.None;
            for (string? path = full; path != null; path = Path.GetDirectoryName(path))
            {
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return UpdateOutcome.None;
                if (string.Equals(path, root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) break;
            }
            using var input = File.OpenRead(full);
            var bytes = new byte[81]; var count = input.ReadAtLeast(bytes, bytes.Length, false);
            if (count == 0 || count > 80) return UpdateOutcome.None;
            var text = System.Text.Encoding.UTF8.GetString(bytes, 0, count);
            return Enum.TryParse<UpdateOutcome>(text, out var outcome) && Enum.GetName(outcome) == text ? outcome : UpdateOutcome.None;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return UpdateOutcome.None; }
    }
}
