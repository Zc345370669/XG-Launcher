// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private bool checkingUpdate, updateExitAuthorized, helperStarted, startupUpdateChecked;
    private string? pendingUpdatePlan, updateHelper;
    internal Func<HttpClient> UpdateHttpFactory { get; set; } = () => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromMinutes(15) };
    private static string CurrentVersion => typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.1.0-alpha";

    private Button VersionUpdateLink(string version)
    {
        var link = Action("v" + version, () => CheckUpdatesAsync(true));
        link.Name = "VersionUpdateLink"; link.Classes.Clear();
        link.Background = Brushes.Transparent; link.BorderThickness = new(0); link.Foreground = ThemePalette.Accent;
        link.HorizontalAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(link, Localizer.Text("点击版本号检查更新"));
        return link;
    }

    private void ConfigureUpdates()
    {
        Opened += async (_, _) =>
        {
            // 测试宿主不访问公网；桌面启动只读取官方公开 Release，不上传本地用户资料。
            if (!startupUpdateChecked && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
            { startupUpdateChecked = true; if (!await ShowUpdateOutcomeAsync(Environment.GetCommandLineArgs())) await CheckUpdatesAsync(false); }
        };
    }

    internal async Task<bool> ShowUpdateOutcomeAsync(string[] arguments)
    {
        var position = Array.IndexOf(arguments, "--update-result");
        if (position < 0 || position + 1 >= arguments.Length) return false;
        var outcome = XG.Core.UpdateReceipt.Read(Path.Combine(model.DownloadsDirectory, "updates"), arguments[position + 1]);
        if (outcome == XG.Core.UpdateOutcome.None) return false;
        if (outcome == XG.Core.UpdateOutcome.Updated)
        {
            status.Text = Localizer.Text("启动器已更新并重新启动。"); compactStatus.Text = status.Text;
            return false;
        }
        var message = outcome switch
        {
            XG.Core.UpdateOutcome.InvalidPackage => "更新包校验失败，已保留原程序。请重新下载更新。",
            XG.Core.UpdateOutcome.AccessDenied => "启动器文件被占用或没有替换权限，已尝试恢复原程序。请关闭占用程序并检查安装目录权限。",
            XG.Core.UpdateOutcome.FilesBusy => "启动器文件被占用，已尝试恢复原程序。请关闭占用文件的程序后重试。",
            XG.Core.UpdateOutcome.LaunchFailed => "新程序无法启动，已尝试恢复原程序。请重新下载更新。",
            _ => "更新未完成，已尝试恢复原程序。请稍后重试。"
        };
        if (!windowClosed) await ErrorAsync(this, message);
        return true; // 本次失败不立即再次弹出同一更新，用户仍可点击版本号重试。
    }

    internal async Task CheckUpdatesAsync(bool manual)
    {
        if (checkingUpdate || windowClosed) return;
        checkingUpdate = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(catalogLifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var client = UpdateHttpFactory();
            var release = await GitHubReleaseFeed.CheckAsync(client, "v" + CurrentVersion.TrimStart('v'), CurrentVersion.Contains('-'), timeout.Token);
            if (windowClosed) return;
            if (release == null)
            {
                if (manual) { var notice = Dialog("版本更新"); notice.SetBody(Label("未发现可用的新版本。"), CloseDialog(notice, "知道了")); await notice.ShowDialog(this); }
                return;
            }
            if (!manual && OwnedWindows.Any(x => x.IsVisible)) return;
            if (await PromptUpdateAsync(release))
                await ShowUpdateDialogAsync(release);
        }
        catch (OperationCanceledException) when (windowClosed) { }
        catch (Exception)
        {
            if (manual && !windowClosed) await ErrorAsync(this, "检查更新失败，请检查网络后重试。");
        }
        finally { checkingUpdate = false; }
    }

    private async Task<bool> PromptUpdateAsync(GitHubReleaseFeed.ReleaseInfo release)
    {
        var dialog = Dialog("版本更新");
        var accepted = false;
        dialog.SetBody(Label($"发现新版本 {release.Tag}。是否下载更新并自动重启？游戏、存档、模组、配置和 Java 将保留。"),
            CloseDialog(dialog, "取消"), Action("更新", () => { accepted = true; dialog.Close(); return Task.CompletedTask; }, true));
        await dialog.ShowDialog(this);
        return accepted;
    }

    private async Task ShowUpdateDialogAsync(GitHubReleaseFeed.ReleaseInfo release)
    {
        var dialog = Dialog("启动器更新");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(catalogLifetime.Token);
        var state = Label("正在下载更新…");
        var details = Label("0 / " + release.Size.ToString("N0") + " B");
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 5, Foreground = ThemePalette.Accent };
        bool finished = false, alive = true;
        var cancel = Action("取消", () => { cancellation.Cancel(); dialog.Close(); return Task.CompletedTask; });
        var body = Column(Label($"{CurrentVersion} → {release.Tag}"), state, details, progress);
        dialog.SetBody(body, cancel);
        dialog.FitContent(body, 430);
        dialog.Height = Math.Min(dialog.MaxHeight, Math.Max(dialog.Height, 260));
        dialog.Closing += (_, _) => { alive = false; if (!finished) cancellation.Cancel(); };
        Task? operation = null;
        dialog.Opened += (_, _) => operation = DownloadAsync();
        await dialog.ShowDialog(this);
        if (operation != null) await operation;

        async Task DownloadAsync()
        {
            try
            {
                using var client = UpdateHttpFactory();
                var staged = await GitHubUpdateDownload.StageAsync(client, release, Path.Combine(model.DownloadsDirectory, "updates"),
                    new Progress<GitHubUpdateDownload.Progress>(report =>
                    {
                        if (!alive || cancellation.IsCancellationRequested) return;
                        state.Text = Localizer.Text(report.Stage == "Downloading" ? "正在下载更新…" : "正在校验更新包…");
                        progress.Value = report.Stage == "Ready" ? 100 : Math.Min(99, report.Received * 100d / report.Total);
                        details.Text = $"{report.Received:N0} / {report.Total:N0} B";
                    }), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                var directory = Path.GetDirectoryName(staged.Archive)!;
                updateHelper = Path.Combine(directory, "XGUpdater.exe");
                using (var resource = typeof(MainWindow).Assembly.GetManifestResourceStream("XG.EmbeddedUpdater.exe") ?? throw new InvalidDataException("Missing embedded updater."))
                await using (var target = File.Create(updateHelper)) await resource.CopyToAsync(target, cancellation.Token);
                using var current = Process.GetCurrentProcess();
                pendingUpdatePlan = Path.Combine(directory, "apply.json");
                await File.WriteAllTextAsync(pendingUpdatePlan, JsonSerializer.Serialize(new XG.Core.UpdateApplyRequest(staged.Archive,
                    release.Sha256, AppContext.BaseDirectory, current.Id, current.StartTime.ToUniversalTime().Ticks)), cancellation.Token);
                // 用户已经确认更新；退出仍等待可恢复下载安全挂起，绝不终止游戏或强杀文件操作。
                finished = true; dialog.Close(); updateExitAuthorized = true; exitRequested = true; Close();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception)
            {
                pendingUpdatePlan = null; updateHelper = null;
                if (alive) { state.Text = Localizer.Text("更新失败，原程序与用户数据未修改。请稍后重试。"); cancel.Content = Localizer.Text("关闭"); }
            }
        }
    }

    private bool TryLaunchUpdater()
    {
        if (pendingUpdatePlan == null || updateHelper == null || helperStarted) return true;
        try
        {
            var start = new ProcessStartInfo(updateHelper) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(pendingUpdatePlan);
            using var helper = Process.Start(start) ?? throw new InvalidOperationException();
            helperStarted = true; return true;
        }
        catch (Exception)
        {
            pendingUpdatePlan = null; updateExitAuthorized = false; exitRequested = false; exitReady = false;
            status.Text = Localizer.Text("无法启动更新器，启动器保持打开。请稍后重试。");
            return false;
        }
    }
}
