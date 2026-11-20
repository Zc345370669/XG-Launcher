// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using XG.Core;
using XGLauncher;

/// <summary>仅供 Windows CI：使用官方公网 Release，在临时目录模拟旧版本，不发布或修改任何 Release。</summary>
internal static class LiveReleaseUpdateSmoke
{
    internal static async Task RunAsync(string updater, string probe, string installedFixtureTag = "v0.0.0")
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Live replacement smoke test requires Windows");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(4) };
        var current = "v" + (typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.1.1-alpha");
        var available = await GitHubReleaseFeed.CheckAsync(http, current, true, deadline.Token);
        Console.WriteLine("LIVE current=" + current + "; offered=" + (available?.Tag ?? "none (no newer compatible release)"));
        // 这是隔离测试的旧版本输入，不改生产程序集版本号，也不声称当前玩家已经有新版本。
        var release = await GitHubReleaseFeed.CheckAsync(http, installedFixtureTag, true, deadline.Token) ?? throw new Exception("No compatible public Release for live test");
        Console.WriteLine("LIVE simulated-installed=" + installedFixtureTag + "; candidate=" + release.Tag + "; bytes=" + release.Size);
        if (installedFixtureTag == "v0.1.0-alpha" && release.Tag != current) throw new Exception("Published update did not select the tested version");
        var root = Path.Combine(Path.GetTempPath(), "xg-live-release-" + Guid.NewGuid().ToString("N"));
        var install = Path.Combine(root, "install"); Directory.CreateDirectory(install);
        Process? launcher = null;
        try
        {
            var executable = Path.Combine(install, "XGLauncher.exe"); File.Copy(Path.GetFullPath(probe), executable);
            var users = new[] { "minecraft/versions/Test/saves/keep.fixture", "minecraft/versions/Test/mods/keep.fixture", "data/settings.keep", "java/keep.fixture" };
            foreach (var name in users)
            {
                var path = Path.Combine(install, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, "preserve fixture", deadline.Token);
            }
            var stages = new Reports();
            var staging = Path.Combine(install, "downloads", "updates");
            var staged = await GitHubUpdateDownload.StageAsync(http, release, staging, stages, deadline.Token);
            if (!stages.Ready || stages.Received != release.Size) throw new Exception("Live transfer did not reach verified ready state");
            string expected;
            using (var zip = ZipFile.OpenRead(staged.Archive))
            using (var input = (zip.GetEntry("XG-Launcher/XGLauncher.exe") ?? zip.GetEntry("XGLauncher.exe") ?? throw new Exception("No executable payload")).Open())
                expected = Convert.ToHexString(SHA256.HashData(input));
            var plan = Path.Combine(Path.GetDirectoryName(staged.Archive)!, "apply.json");
            await File.WriteAllTextAsync(plan, JsonSerializer.Serialize(new UpdateApplyRequest(staged.Archive, release.Sha256, install, int.MaxValue, 0)), deadline.Token);
            var start = new ProcessStartInfo(Path.GetFullPath(updater)) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(plan);
            using var apply = Process.Start(start) ?? throw new Exception("Updater did not start");
            await apply.WaitForExitAsync(deadline.Token);
            if (apply.ExitCode != 0 || UpdateReceipt.Read(staging, plan + ".result") != UpdateOutcome.Updated) throw new Exception("Public Release replacement failed");
            using (var installed = File.OpenRead(executable))
                if (Convert.ToHexString(SHA256.HashData(installed)) != expected) throw new Exception("Installed EXE differs from public ZIP payload");
            while (launcher == null)
            {
                deadline.Token.ThrowIfCancellationRequested();
                foreach (var candidate in Process.GetProcessesByName("XGLauncher"))
                {
                    try
                    {
                        if (!candidate.HasExited && string.Equals(candidate.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                        { launcher = candidate; break; }
                    }
                    catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                    candidate.Dispose();
                }
                if (launcher == null) await Task.Delay(100, deadline.Token);
            }
            using var windowDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            windowDeadline.CancelAfter(TimeSpan.FromSeconds(45));
            while (true)
            {
                launcher.Refresh();
                if (launcher.HasExited) throw new Exception("Downloaded public launcher exited during startup");
                if (launcher.MainWindowHandle != IntPtr.Zero) break;
                await Task.Delay(100, windowDeadline.Token);
            }
            foreach (var name in users) if (await File.ReadAllTextAsync(Path.Combine(install, name), deadline.Token) != "preserve fixture") throw new Exception("Live update changed user fixture");
            Console.WriteLine("PASS LIVE official GitHub HTTPS metadata/download, SHA-256, staging, actual EXE replacement and restart with native window; user fixtures preserved");
            Console.WriteLine("NOTE live test used simulated old version in disposable CI; not a player upgrade, manual desktop acceptance or a new Release publication");
        }
        finally
        {
            // 只清理本测试目录内、明确记录的进程；绝不关闭用户正在使用的启动器或游戏。
            if (launcher != null)
            {
                try
                {
                    if (!launcher.HasExited)
                    {
                        launcher.CloseMainWindow();
                        using var closing = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                        try { await launcher.WaitForExitAsync(closing.Token); }
                        catch (OperationCanceledException) { launcher.Kill(true); await launcher.WaitForExitAsync(); Console.WriteLine("NOTE disposable live fixture required forced cleanup"); }
                    }
                }
                finally { launcher.Dispose(); }
            }
            for (int attempt = 0; attempt < 50; attempt++)
                try { Directory.Delete(root, true); break; }
                catch (IOException) when (attempt < 49) { await Task.Delay(100); }
                catch (UnauthorizedAccessException) when (attempt < 49) { await Task.Delay(100); }
        }
    }

    private sealed class Reports : IProgress<GitHubUpdateDownload.Progress>
    {
        public bool Ready; public long Received; private int bucket = -1; private string? stage;
        public void Report(GitHubUpdateDownload.Progress report)
        {
            Received = report.Received; Ready = report.Stage == "Ready";
            var next = (int)(report.Received * 10 / report.Total);
            if (next != bucket || stage != report.Stage)
            { bucket = next; stage = report.Stage; Console.WriteLine("LIVE " + report.Stage + " " + report.Received + "/" + report.Total); }
        }
    }
}
