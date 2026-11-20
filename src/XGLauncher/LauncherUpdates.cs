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
    private string? pendingUpdatePlan;
    private string? updateHelper;
    internal Func<HttpClient> UpdateHttpFactory { get; set; } = () => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromMinutes(10) };
    private static string CurrentVersion => typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.1.0-alpha";

    private Button VersionUpdateLink(string version)
    {
        var link = Action("v" + version, () => CheckUpdatesAsync(true));
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
            // 无头测试不访问公网；实际桌面启动仅读取官方公开 Release，不上传用户资料。
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
                await CheckUpdatesAsync(false);
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

    private async Task ShowUpdatePageAsync(LauncherRelease release)
    {
        if (IsCompact) ToggleMode();
        section = 5; Refresh();
        var message = Label("正在下载更新…");
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 4, Foreground = ThemePalette.Accent };
        var install = Action("安装更新并重启", async () =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("在线替换程序仅支持 Windows。");
            var helper = Path.Combine(AppContext.BaseDirectory, "tools", "XGUpdater.exe");
            if (!File.Exists(helper)) throw new FileNotFoundException("缺少更新程序，请完整解压发布包。");
            var staged = Path.Combine(model.DownloadsDirectory, "updates", release.Version, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staged);
            var copied = Path.Combine(staged, "XGUpdater.exe"); File.Copy(helper, copied);
            using var current = Process.GetCurrentProcess();
            var plan = new UpdateApplyRequest(Path.Combine(model.DownloadsDirectory, "updates", release.Version, release.AssetName),
                release.Sha256, AppContext.BaseDirectory, current.Id, current.StartTime.ToUniversalTime().Ticks);
            pendingUpdatePlan = Path.Combine(staged, "apply.json");
            InstanceStore.WriteJson(pendingUpdatePlan, plan); updateHelper = copied;
            exitRequested = true; Close();
        }, true);
        install.IsEnabled = false;
        var back = Action("暂不更新", () => { pendingUpdatePlan = null; Refresh(); return Task.CompletedTask; });
        page.Content = PageViewport(Column(Label("启动器更新", 22, true), Label($"当前版本：v{CurrentVersion}"),
            Label($"新版本：{release.Version}"), Label("更新仅替换程序与许可文件，保留游戏、存档、账号和设置。"), message, progress, install, back));
        try
        {
            using var client = UpdateHttpFactory();
            var package = await new GitHubUpdates(client).DownloadAsync(release,
                Path.Combine(model.DownloadsDirectory, "updates", release.Version), value => progress.Value = value, catalogLifetime.Token);
            // 提前验证压缩包结构及允许写入范围；窗口退出后的独立更新器仍会再次校验散列和结构。
            await Task.Run(() => { using var verification = UpdateFiles.Prepare(new(package, release.Sha256, AppContext.BaseDirectory, 0, 0)); }, catalogLifetime.Token);
            if (windowClosed) return;
            message.Text = "更新包已校验。点击安装更新并重启，下载任务将保存后退出。";
            install.IsEnabled = OperatingSystem.IsWindows();
        }
        catch (OperationCanceledException) when (windowClosed) { }
        catch (Exception)
        {
            if (!windowClosed) message.Text = "更新下载或校验失败，原程序与用户数据未修改。请重新检查更新后重试。";
        }
    }
}
