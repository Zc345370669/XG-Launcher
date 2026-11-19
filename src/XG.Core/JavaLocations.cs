// SPDX-License-Identifier: MIT
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.Versioning;

namespace XG.Core;

internal sealed record JavaSearchInputs(string Managed, string? JavaHome, string? JdkHome, string? SearchPath,
    IReadOnlyList<string> CommonRoots, IReadOnlyList<string> RegistryRoots, IReadOnlyList<string> Additional);

internal static class JavaLocations
{
    internal static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    internal static string ConsoleName => OperatingSystem.IsWindows() ? "java.exe" : "java";

    internal static JavaSearchInputs Capture(string managed, IEnumerable<string>? extra, CancellationToken token = default)
    {
        var common = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                         Environment.SpecialFolder.LocalApplicationData })
            {
                var root = Environment.GetFolderPath(folder);
                if (root.Length == 0) continue;
                foreach (var vendor in new[] { "Java", "Eclipse Adoptium", "Eclipse Foundation", "Microsoft", "Amazon Corretto", "Zulu", "Azul", "BellSoft", "GraalVM" })
                    common.Add(Path.Combine(root, vendor));
            }
            foreach (var folder in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.UserProfile })
            {
                var root = Environment.GetFolderPath(folder);
                if (root.Length == 0) continue;
                foreach (var location in new[] { ".minecraft/runtime", ".jdks", ".hmcl/java", "HMCL/java", "PCL/runtimes", "PrismLauncher/java", "MultiMC/java" })
                    common.Add(Path.Combine(root, location.Replace('/', Path.DirectorySeparatorChar)));
            }
        }
        else common.AddRange(["/usr/lib/jvm", "/usr/java", "/opt/java"]);
        return new(managed, Environment.GetEnvironmentVariable("JAVA_HOME"), Environment.GetEnvironmentVariable("JDK_HOME"),
            Environment.GetEnvironmentVariable("PATH"), common, OperatingSystem.IsWindows() ? ReadWindowsRegistry(token) : [], extra?.ToArray() ?? []);
    }

    internal static string? Executable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')));
            if (Directory.Exists(path)) path = Path.Combine(path, "bin", ConsoleName);
            if (OperatingSystem.IsWindows() && Path.GetFileName(path).Equals("javaw.exe", StringComparison.OrdinalIgnoreCase))
                path = Path.Combine(Path.GetDirectoryName(path)!, ConsoleName);
            var file = new FileInfo(path);
            if (!file.Exists) return null;
            path = file.ResolveLinkTarget(true)?.FullName ?? file.FullName;
            // 同时规范化目录别名和可执行文件符号链接；显式位置可以是链接，
            // 但递归搜索不跟随子目录链接，避免越界遍历。
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var changed = false;
                for (var parent = Directory.GetParent(path); parent is not null; parent = parent.Parent)
                {
                    if (parent.LinkTarget is null) continue;
                    var target = parent.ResolveLinkTarget(true);
                    if (target is null) return null;
                    path = Path.GetFullPath(Path.Combine(target.FullName, Path.GetRelativePath(parent.FullName, path)));
                    changed = true; break;
                }
                if (!changed) return path;
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    internal static IReadOnlyList<string> Collect(JavaSearchInputs input, IEnumerable<string> cached, bool deep,
        CancellationToken token, IProgress<string>? progress)
    {
        var found = new List<string>(); var seenFiles = new HashSet<string>(PathComparer);
        var roots = new List<string>(); var visited = new HashSet<string>(PathComparer);
        var clock = Stopwatch.StartNew(); var reported = false; var remaining = deep ? 50000 : 4096;
        // 显式路径先作为直接候选检查；深度遍历仅限用户指定根目录，
        // 启动时不枚举整个磁盘根目录。
        foreach (var value in input.Additional.Concat(cached)) Add(value, deep);
        if (!deep)
        {
            Add(input.JavaHome, true); Add(input.JdkHome, true);
            foreach (var entry in (input.SearchPath ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                if (!string.IsNullOrWhiteSpace(entry.Trim().Trim('"'))) Add(Path.Combine(entry.Trim().Trim('"'), ConsoleName), false);
            Add(input.Managed, true);
            foreach (var value in input.RegistryRoots.Concat(input.CommonRoots)) Add(value, true);
        }
        foreach (var root in roots)
        {
            var pending = new Queue<(string Path, int Depth)>(); pending.Enqueue((root, 0)); var perRoot = deep ? 50000 : 256;
            while (pending.TryDequeue(out var node) && remaining > 0 && perRoot > 0)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var dir = Path.GetFullPath(node.Path);
                    if (!visited.Add(dir) || !Directory.Exists(dir) || (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
                    remaining--; perRoot--;
                    if (!reported || clock.ElapsedMilliseconds >= 100) { progress?.Report(dir); reported = true; clock.Restart(); }
                    Add(Path.Combine(dir, ConsoleName), false);
                    Add(Path.Combine(dir, "bin", ConsoleName), false);
                    if (node.Depth >= (deep ? 32 : 5)) continue;
                    foreach (var child in Directory.EnumerateDirectories(dir).Take(Math.Max(0, Math.Min(remaining, perRoot) - pending.Count)))
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Enqueue((child, node.Depth + 1));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { }
            }
        }
        return found;

        void Add(string? value, bool searchDirectory)
        {
            token.ThrowIfCancellationRequested();
            if (Executable(value) is { } file && seenFiles.Add(file) && found.Count < (deep ? 1024 : 128)) found.Add(file);
            if (!searchDirectory || string.IsNullOrWhiteSpace(value)) return;
            try
            {
                var dir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')));
                if (Directory.Exists(dir)) roots.Add(dir);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }
    }

    internal static IEnumerable<(bool Machine, bool Is64)> RegistryTargets() =>
        from machine in new[] { false, true }
        from is64 in new[] { true, false }
        select (machine, is64);

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<string> ReadWindowsRegistry(CancellationToken token)
    {
        var values = new HashSet<string>(PathComparer);
        foreach (var target in RegistryTargets())
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var hive = RegistryKey.OpenBaseKey(target.Machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, target.Is64 ? RegistryView.Registry64 : RegistryView.Registry32);
                foreach (var vendor in new[] { "JavaSoft", "Eclipse Adoptium", "Eclipse Foundation", "Microsoft\\JDK", "Azul Systems", "BellSoft", "Amazon Corretto" })
                {
                    using var root = hive.OpenSubKey("SOFTWARE\\" + vendor);
                    if (root is null) continue;
                    var budget = 128; Visit(root, 0, ref budget);
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        return values.ToArray();
        void Visit(RegistryKey key, int depth, ref int budget)
        {
            token.ThrowIfCancellationRequested();
            if (budget-- <= 0 || depth > 5) return;
            try
            {
                foreach (var name in new[] { "JavaHome", "Path", "InstallationPath", "InstallationDirectory" })
                    if (key.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value)) values.Add(value);
                foreach (var child in key.GetSubKeyNames())
                {
                    if (budget <= 0) break;
                    try { using var subkey = key.OpenSubKey(child); if (subkey is not null) Visit(subkey, depth + 1, ref budget); }
                    catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
    }
}
