// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Reflection;
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
    internal Func<LauncherRelease, Task>? UpdateSessionOverride { get; set; }
    private string? pendingUpdatePlan;
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
            if (pendingUpdatePlan is not { } plan) return;
            try { File.WriteAllText(plan + ".exit", ""); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
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
                await (UpdateSessionOverride?.Invoke(release) ?? StartIndependentUpdateAsync(release));
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

    private async Task StartIndependentUpdateAsync(LauncherRelease release)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("在线替换程序仅支持 Windows。");
        var staged = Path.Combine(model.DownloadsDirectory, "updates", release.Version, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staged);
        await File.WriteAllTextAsync(Path.Combine(staged, ".xg-update-owned"), UpdateStagingCleanup.Marker);
        var helper = Path.Combine(staged, "XGUpdater.exe");
        using (var embedded = typeof(MainWindow).Assembly.GetManifestResourceStream("XG.EmbeddedUpdater.exe"))
        {
            if (embedded != null)
            {
                await using var output = new FileStream(helper, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                await embedded.CopyToAsync(output);
            }
            else File.Copy(Path.Combine(AppContext.BaseDirectory, "tools", "XGUpdater.exe"), helper);
        }
        using var current = Process.GetCurrentProcess();
        var file = Path.Combine(staged, "apply.json");
        var plan = new UpdateApplyRequest(Path.Combine(staged, release.AssetName), release.Sha256, AppContext.BaseDirectory,
            current.Id, current.StartTime.ToUniversalTime().Ticks, release.Version,
            new UpdateTheme(ThemePalette.Panel.Color.ToString(), ThemePalette.Text.Color.ToString(), ThemePalette.Accent.Color.ToString(),
                ThemePalette.Line.Color.ToString(), ThemePalette.Current.Dark), release, CurrentVersion);
        InstanceStore.WriteJson(file, plan);
        if (windowClosed) return;
        using var updater = Process.Start(new ProcessStartInfo(helper)
        {
            UseShellExecute = false, CreateNoWindow = true,
            ArgumentList = { "--apply-update", file }
        }) ?? throw new IOException("无法打开独立更新窗口。");

        while (!updater.HasExited && !windowClosed)
        {
            if (!preparingExit && File.Exists(file + ".ready"))
            {
                pendingUpdatePlan = file;
                updateExitAuthorized = true;
                exitRequested = true;
                Close();
                // Keep the session active while shutdown is waiting for other transfers.
                while (pendingUpdatePlan != null && !windowClosed && !updater.HasExited)
                    await Task.Delay(120);
                if (!windowClosed && updater.HasExited && pendingUpdatePlan != null) AbortUpdateExit();
                return;
            }
            await Task.Delay(120);
        }
    }

    private void AbortUpdateExit()
    {
        var plan = pendingUpdatePlan;
        pendingUpdatePlan = null;
        updateExitAuthorized = false;
        if (plan != null)
        {
            try { File.WriteAllText(plan + ".abort", ""); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
