// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace XG.Core;

internal static class JavaProbe
{
    internal static string NormalizeArchitecture(string architecture) => architecture.Trim().ToLowerInvariant() switch
    {
        "amd64" or "x86_64" or "x64" => "x64",
        "aarch64" or "arm64" => "arm64",
        "x86" or "i386" or "i486" or "i586" or "i686" => "x86",
        _ => "unknown"
    };

    internal static JavaInstallation? Parse(string executable, string output, int exitCode)
    {
        if (exitCode != 0 || output.Length > 65536) return null;
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        string? bannerVersion = null;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            var split = line.IndexOf('=');
            if (split > 0) properties.TryAdd(line[..split].Trim(), line[(split + 1)..].Trim());
            if (line.StartsWith("java version ", StringComparison.Ordinal) || line.StartsWith("openjdk version ", StringComparison.Ordinal))
            {
                var first = line.IndexOf('"');
                var last = first < 0 ? -1 : line.IndexOf('"', first + 1);
                if (last > first) bannerVersion = line[(first + 1)..last];
            }
        }
        var version = properties.GetValueOrDefault("java.version") ?? bannerVersion;
        var architecture = properties.GetValueOrDefault("os.arch")?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(version) || architecture is null || NormalizeArchitecture(architecture) == "unknown") return null;
        var start = version.StartsWith("1.", StringComparison.Ordinal) ? 2 : 0;
        var end = start;
        while (end < version.Length && char.IsAsciiDigit(version[end])) end++;
        if (end == start || !int.TryParse(version.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out var major) || major < 2 || major > 999)
            return null;
        return new(executable, major, architecture, version, properties.GetValueOrDefault("java.vendor") ?? "");
    }

    internal static string? Fingerprint(string executable)
    {
        try
        {
            var bin = Path.GetDirectoryName(executable)!;
            var home = Path.GetDirectoryName(bin)!;
            var names = new[] { executable, Path.Combine(home, "release"), Path.Combine(bin, "server", "jvm.dll"),
                Path.Combine(home, "jre", "bin", "server", "jvm.dll"), Path.Combine(home, "lib", "server", "libjvm.so") };
            if (!File.Exists(executable)) return null;
            return string.Join(";", names.Select(p => { var f = new FileInfo(p); return f.Exists ? $"{f.Length}:{f.LastWriteTimeUtc.Ticks}" : "missing"; }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    /// <summary>
    /// 在超时和取消约束下运行Java属性探测，退出后收集输出并释放进程资源。
    /// </summary>
    internal static async Task<JavaInstallation?> ExecuteAsync(string executable, TimeSpan limit, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(limit);
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo { FileName = executable, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true };
        process.StartInfo.ArgumentList.Add("-XshowSettings:properties");
        process.StartInfo.ArgumentList.Add("-version");
        Task<string>? stdout = null, stderr = null;
        try
        {
            if (!process.Start()) return null;
            stdout = ReadBoundedAsync(process.StandardOutput, deadline.Token);
            stderr = ReadBoundedAsync(process.StandardError, deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            var text = await stdout + "\n" + await stderr;
            return Parse(executable, text, process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            Stop(); token.ThrowIfCancellationRequested(); return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        { Stop(); return null; }
        finally
        {
            deadline.Cancel();
            // 观察管道读取的取消与异常，包括进程先退出而读取仍超时的情况。
            if (stdout is not null && stderr is not null)
                try { await Task.WhenAll(stdout, stderr); } catch (Exception e) when (e is OperationCanceledException or IOException) { }
        }
        void Stop()
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[2048]; var text = new StringBuilder(); var overflow = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            if (text.Length + count > 32768) overflow = true;
            else if (!overflow) text.Append(buffer, 0, count);
        }
        // 无效标记不能被解析成JVM属性集，避免错误输出成为可用Java证据。
        return overflow ? new string('?', 65537) : text.ToString();
    }
}
