// SPDX-License-Identifier: MIT
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace XG.Core;

public sealed record JavaInstallerPackage(int Major, string Name, Uri Url, long Size, string Sha256);

/// <summary>
/// 从 Adoptium 官方元数据获取 Windows x64 MSI，校验后交给 Windows Installer。
/// 不自行解压 Java，不改写 PATH/JAVA_HOME，也不删除系统或旧版 AppData Java。
/// </summary>
public sealed class OfficialJavaInstaller(HttpClient http)
{
    private const long Maximum = 512L * 1024 * 1024;
    public async Task<JavaInstallerPackage> ResolveAsync(int major, CancellationToken token)
    {
        if (major is < 8 or > 99) throw new InvalidDataException("缺少可靠的 Java 主版本要求，请手动选择已安装 Java。");
        var url = new Uri($"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture=x64&image_type=jdk&os=windows&vendor=eclipse");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("XGLauncher/0.1.1");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var memory = new MemoryStream(); var buffer = new byte[16384]; int n;
        while ((n = await input.ReadAsync(buffer, token)) > 0)
        { if (memory.Length + n > 2 * 1024 * 1024) throw new InvalidDataException("Java 元数据过大。"); memory.Write(buffer, 0, n); }
        using var json = JsonDocument.Parse(memory.ToArray());
        foreach (var entry in json.RootElement.EnumerateArray())
        {
            var binary = entry.GetProperty("binary");
            if (binary.GetProperty("architecture").GetString() != "x64" || binary.GetProperty("os").GetString() != "windows" ||
                binary.GetProperty("image_type").GetString() != "jdk" || !binary.TryGetProperty("installer", out var installer)) continue;
            var package = new JavaInstallerPackage(major, installer.GetProperty("name").GetString()!,
                new Uri(installer.GetProperty("link").GetString()!), installer.GetProperty("size").GetInt64(), installer.GetProperty("checksum").GetString()!);
            Validate(package); return package;
        }
        throw new InvalidDataException("官方未提供兼容的 Windows x64 MSI，请选择本地已安装 Java。");
    }
    internal static void Validate(JavaInstallerPackage package)
    {
        var uri = package.Url;
        if (package.Size is < 1 or > Maximum || !Regex.IsMatch(package.Sha256, "\\A[0-9a-fA-F]{64}\\z") ||
            !Regex.IsMatch(package.Name, "\\A[A-Za-z0-9_.+()-]+\\.msi\\z") ||
            uri.Scheme != "https" || uri.Host != "github.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !uri.AbsolutePath.StartsWith($"/adoptium/temurin{package.Major}-binaries/releases/download/", StringComparison.Ordinal) ||
            Uri.UnescapeDataString(uri.Segments.Last()) != package.Name)
            throw new InvalidDataException("Java 安装器来源、大小或校验信息无效。");
    }
    /// <summary>每次下载使用独占缓存；失败保留校验诊断，成功并探测通过后只清理本次缓存。</summary>
    public async Task<string> DownloadAsync(JavaInstallerPackage package, string executableDirectory, Action<long, long>? progress, CancellationToken token, Func<Task>? checkpoint = null)
    {
        Validate(package);
        var session = CreateCache(executableDirectory);
        InstanceStore.WriteJson(Path.Combine(session, "recovery.json"), new { package.Major, package.Name, package.Sha256, State = "Downloading" });
        var target = Path.Combine(session, package.Name);
        var uri = package.Url;
        for (var redirects = 0; redirects < 5; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("XGLauncher/0.1.1");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                uri = response.Headers.Location is { } location ? new Uri(uri, location) : throw new InvalidDataException("Java 重定向无效。");
                if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Host is not ("release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                    throw new InvalidDataException("Java 下载离开官方发行渠道。");
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != package.Size) throw new InvalidDataException("Java 安装器长度不符。");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(target + ".part", FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536]; long total = 0; int n;
                while (true)
                {
                    if (checkpoint != null) await checkpoint();
                    n = await input.ReadAsync(buffer, token); if (n == 0) break;
                    total += n; if (total > package.Size) throw new InvalidDataException("Java 安装器长度不符。");
                    hash.AppendData(buffer, 0, n); await output.WriteAsync(buffer.AsMemory(0, n), token); progress?.Invoke(total, package.Size);
                }
                if (total != package.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Java 安装器 SHA-256 校验失败。");
            }
            token.ThrowIfCancellationRequested(); File.Move(target + ".part", target); return target;
        }
        throw new InvalidDataException("Java 安装器重定向次数过多。");
    }
    internal static string CreateCache(string executableDirectory)
    {
        foreach (var parent in new[] { Path.Combine(executableDirectory, ".xg-java-downloads"), Path.Combine(Path.GetTempPath(), "XGLauncher", "java-installers") })
        {
            try
            {
                var session = Path.Combine(parent, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(session);
                for (var directory = new DirectoryInfo(session); directory != null; directory = directory.Parent)
                    if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Java 缓存不能跟随目录链接。");
                using (File.Create(Path.Combine(session, ".owned"))) { }
                return session;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        throw new IOException("没有可写的 Java 安装器缓存目录。");
    }
    internal static bool SuccessfulExit(int code) => code is 0 or 3010;
    public async Task<JavaInstallation> InstallAsync(int major, string executableDirectory, Func<Task<bool>> consent,
        Action<string> state, Action<long, long>? progress, CancellationToken token, Func<Task>? checkpoint = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("官方 MSI 安装仅支持 Windows。");
        if (!await consent()) throw new OperationCanceledException("已取消安装 Java。", token);
        var package = await ResolveAsync(major, token); state("下载中");
        var installer = await DownloadAsync(package, executableDirectory, progress, token, checkpoint);
        token.ThrowIfCancellationRequested(); if (checkpoint != null) await checkpoint(); state("安装中：请确认 Windows 授权");
        // ADDLOCAL 只安装核心组件，不选择修改环境变量的可选特性；不传 INSTALLDIR，由 MSI 采用发行商默认目录。
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"))
        { UseShellExecute = true, Verb = "runas", Arguments = $"/i \"{installer}\" ADDLOCAL=FeatureMain /passive /norestart" };
        // 持有只读共享句柄，防止校验后到 UAC/安装期间文件被替换。
        await using var packageLock = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!Convert.ToHexString(await SHA256.HashDataAsync(packageLock, token)).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Java 安装器在下载后发生变化。");
        Process process;
        try { process = Process.Start(start) ?? throw new IOException("无法启动官方 Java 安装器。"); }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { throw new OperationCanceledException("已拒绝 Windows 授权，Java 未安装。", e, token); }
        using (process)
        {
            // MSI 事务不能因下载取消而被强杀；等待其自行退出，随后验证并清理。
            await process.WaitForExitAsync();
            if (process.ExitCode == 1602) throw new OperationCanceledException("已取消 Java 安装。", token);
            if (!SuccessfulExit(process.ExitCode)) throw new IOException($"官方 Java 安装失败，Windows Installer 返回 {process.ExitCode}。");
        }
        state("校验中");
        var runtime = await JavaDiscovery.CompatibleAsync(null, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), major, cancellationToken: CancellationToken.None)
            ?? throw new IOException("安装器已退出，但未检测到兼容的 x64 Java；缓存保留供重试。");
        await packageLock.DisposeAsync();
        CleanupVerified(installer);
        token.ThrowIfCancellationRequested(); return runtime;
    }
    internal static void CleanupVerified(string installer)
    {
        var session = Path.GetDirectoryName(installer)!;
        if (!Guid.TryParseExact(Path.GetFileName(session), "N", out _) || !File.Exists(Path.Combine(session, ".owned"))) throw new IOException("不是本次受管理的 Java 缓存。");
        // 不递归删除目录，未知文件留在原处；不会触及安装后的运行时。
        foreach (var file in new[] { installer, installer + ".part", Path.Combine(session, "recovery.json"), Path.Combine(session, ".owned") })
            if (File.Exists(file)) File.Delete(file);
        if (!Directory.EnumerateFileSystemEntries(session).Any()) Directory.Delete(session);
    }
}
