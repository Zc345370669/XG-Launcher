// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using XG.Core;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private bool checkingUpdate;
    private bool automaticUpdateChecked;
    private bool updateExitAuthorized;
    internal Func<LauncherRelease, string, Task>? UpdateInstallOverride { get; set; }
    private string? pendingUpdatePlan;
    private string? updateHelper;
    internal Func<HttpClient> UpdateHttpFactory { get; set; } = () => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromMinutes(10) };
    private static string CurrentVersion => typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.1.1";

    private Button VersionUpdateLink(string version)
    {
        Button? link = null;
        link = Action("v" + version, async () =>
        {
            link!.IsEnabled = false; link.Content = Localizer.Text("检查更新中…");
            try { await CheckUpdatesAsync(true); }
            finally { if (!windowClosed) { link.Content = "v" + version; link.IsEnabled = true; } }
        });
        link.Name = "VersionUpdateLink";
        link.Classes.Clear();
        link.Background = Brushes.Transparent; link.BorderThickness = new(0); link.Foreground = ThemePalette.Accent;
        link.HorizontalAlignment = HorizontalAlignment.Center;
        link.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        ToolTip.SetTip(link, Localizer.Text("点击版本号检查更新"));
        return link;
    }

    private void ConfigureUpdates()
    {
        Opened += async (_, _) =>
        {
            _ = CleanupCompletedUpdatesAsync();
            // 无头测试不访问公网；实际桌面启动仅读取官方公开 Release，不上传用户资料。
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime && !automaticUpdateChecked)
            { automaticUpdateChecked = true; await CheckUpdatesAsync(false); }
        };
        Closed += (_, _) =>
        {
            if (pendingUpdatePlan == null || updateHelper == null) return;
            try
            {
                var start = new ProcessStartInfo(updateHelper) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(pendingUpdatePlan);
                Process.Start(start);
            }
            catch (Exception error) { System.Diagnostics.Trace.TraceWarning(error.GetType().Name); }
        };
    }

    private async Task CleanupCompletedUpdatesAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), catalogLifetime.Token);
            if (!windowClosed) await Task.Run(() => UpdateStagingCleanup.CleanCompleted(Path.Combine(model.DownloadsDirectory, "updates")));
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or UnauthorizedAccessException) { }
    }

    internal async Task CheckUpdatesAsync(bool manual)
    {
        if (checkingUpdate || windowClosed) return;
        checkingUpdate = true;
        try
        {
            using var client = UpdateHttpFactory();
            var release = await new GitHubUpdates(client).CheckAsync(CurrentVersion, catalogLifetime.Token);
            if (windowClosed) return;
            if (release == null)
            {
                if (manual)
                {
                    var notice = Dialog("版本更新");
                    notice.SetBody(Label("未发现可用的新版本。"), CloseDialog(notice, "知道了"));
                    await notice.ShowDialog(this);
                }
                return;
            }
            // 暂不更新不会下载或替换任何文件；提示结束后才开始准备更新页。
            if (!manual && OwnedWindows.Any(x => x.IsVisible)) return;
            if (await PromptUpdateAsync(release))
                await ShowUpdatePageAsync(release);
        }
        catch (OperationCanceledException) when (windowClosed) { }
        catch (Exception)
        {
            if (manual && !windowClosed) await ErrorAsync(this, "检查更新失败，请检查网络后重试。");
        }
        finally { checkingUpdate = false; }
    }

    private async Task<bool> PromptUpdateAsync(LauncherRelease release)
    {
        var dialog = Dialog("发现新版本"); var accepted = false;
        dialog.SetBody(Label($"当前版本：v{CurrentVersion}\n新版本：{release.Version}\n是否更新？"),
            CloseDialog(dialog, "暂不更新"), Action("更新", () => { accepted = true; dialog.Close(); return Task.CompletedTask; }, true));
        await dialog.ShowDialog(this); return accepted;
    }

    internal LauncherDialog? ActiveUpdateWindow { get; private set; }
    private async Task ShowUpdatePageAsync(LauncherRelease release)
    {
        // 更新窗口独立于当前模式，不重建主页面或共享业务服务。
        var dialog = Dialog("正在更新 XG Launcher", editor: true); ActiveUpdateWindow = dialog;
        dialog.Width = Math.Min(360, dialog.MaxWidth);
        var message = Label("正在下载更新…"); message.Name = "UpdateStatus";
        var progress = new ProgressBar { Name = "UpdateProgress", Minimum = 0, Maximum = 100, Height = 4, Foreground = ThemePalette.Accent };
        var percentage = Label("0%"); percentage.Name = "UpdatePercentage";
        var amount = Label($"0 MB / {release.Size / 1048576d:0.00} MB"); amount.Name = "UpdateBytes";
        var speed = Label("0.00 MB/s · 剩余时间计算中");
        var file = Label(release.AssetName); file.Name = "UpdateFile";
        var details = new Expander { Header = "文件详情", Content = Column(file, Label(release.AssetUrl.ToString()), Label($"大小：{release.Size} bytes"), Label("SHA-256：" + release.Sha256)) };
        var timer = Stopwatch.StartNew(); long previous = 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(catalogLifetime.Token);
        var running = true; var critical = false;
        var back = Action("取消更新", () => { if (running) cancellation.Cancel(); else dialog.Close(); return Task.CompletedTask; });
        dialog.Closing += (_, args) =>
        {
            if (critical && running && !updateExitAuthorized && !windowClosed) { args.Cancel = true; return; }
            if (running && !critical) cancellation.Cancel();
        };
        var body = Column(Label($"v{CurrentVersion} → {release.Version}"), message, ProgressRow(progress, percentage), amount, speed, details);
        dialog.SetBody(body, back); dialog.FitContent(body, 360); dialog.CanResize = false;
        details.PropertyChanged += (_, args) => { if (args.Property == Expander.IsExpandedProperty) dialog.FitContent(body, 360); };
        _ = dialog.ShowDialog(this);
        try
        {
            using var client = UpdateHttpFactory();
            var package = await new GitHubUpdates(client).DownloadAsync(release,
                Path.Combine(model.DownloadsDirectory, "updates", release.Version), value =>
                {
                    progress.Value = value; percentage.Text = $"{Math.Floor(value):0}%";
                    var downloaded = (long)Math.Round(release.Size * value / 100);
                    amount.Text = $"{downloaded / 1048576d:0.00} MB / {release.Size / 1048576d:0.00} MB";
                    if (timer.ElapsedMilliseconds >= 500)
                    {
                        var rate = Math.Max(0, downloaded - previous) / Math.Max(.001, timer.Elapsed.TotalSeconds);
                        speed.Text = $"{rate / 1048576:0.00} MB/s · " + (rate > 0 ? $"约 {(release.Size - downloaded) / rate:0} 秒" : "剩余时间计算中");
                        previous = downloaded; timer.Restart();
                    }
                }, cancellation.Token);
            message.Text = "下载 100%，正在校验文件结构…";
            await Task.Run(() => { using var verification = UpdateFiles.Prepare(new(package, release.Sha256, AppContext.BaseDirectory, 0, 0)); }, cancellation.Token);
            if (windowClosed) return;
            cancellation.Token.ThrowIfCancellationRequested();
            progress.Value = 100; percentage.Text = "100%";
            message.Text = "下载及校验完成，等待主程序退出后替换并验证重启。";
            critical = true; back.IsEnabled = false;
            await (UpdateInstallOverride?.Invoke(release, package) ?? ApplyDownloadedUpdateAsync(release, package));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { if (dialog.IsVisible) dialog.Close(); }
        catch (Exception error)
        {
            critical = false; back.IsEnabled = true; back.Content = "关闭";
            if (!windowClosed) message.Text = "更新失败，原程序与用户数据未修改。" + new GameLogSanitizer(null).Redact(error.Message);
        }
        finally { running = false; }
    }

    private async Task ApplyDownloadedUpdateAsync(LauncherRelease release, string package)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("在线替换程序仅支持 Windows。");
        var staged = Path.Combine(model.DownloadsDirectory, "updates", release.Version, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staged);
        await File.WriteAllTextAsync(Path.Combine(staged, ".xg-update-owned"), UpdateStagingCleanup.Marker);
        var copied = Path.Combine(staged, "XGUpdater.exe");
        using (var embedded = typeof(MainWindow).Assembly.GetManifestResourceStream("XG.EmbeddedUpdater.exe"))
        {
            if (embedded != null)
            { await using var output = new FileStream(copied, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true); await embedded.CopyToAsync(output); }
            else File.Copy(Path.Combine(AppContext.BaseDirectory, "tools", "XGUpdater.exe"), copied);
        }
        using var current = Process.GetCurrentProcess();
        var plan = new UpdateApplyRequest(package, release.Sha256, AppContext.BaseDirectory, current.Id, current.StartTime.ToUniversalTime().Ticks, release.Version,
            new UpdateTheme(ThemePalette.Panel.Color.ToString(), ThemePalette.Text.Color.ToString(), ThemePalette.Accent.Color.ToString(), ThemePalette.Line.Color.ToString(), ThemePalette.Current.Dark));
        pendingUpdatePlan = Path.Combine(staged, "apply.json");
        InstanceStore.WriteJson(pendingUpdatePlan, plan); updateHelper = copied;
        updateExitAuthorized = true; exitRequested = true; Close();
    }
}
