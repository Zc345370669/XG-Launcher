// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using XG.Core;

internal static class Program
{
    internal static string PlanFile = "";
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 2 || args[0] != "--apply-update") return 2;
        PlanFile = Path.GetFullPath(args[1]);
        return AppBuilder.Configure<UpdateApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime([]);
    }
}

internal sealed class UpdateApplication : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new UpdateWindow(Program.PlanFile);
        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>独立进程在主程序退出后继续显示真实步骤；替换阶段不接受强制取消。</summary>
internal sealed class UpdateWindow : Window
{
    private readonly TextBlock state = new() { Text = "正在准备更新…", TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar progress = new() { Height = 4, Minimum = 0, Maximum = 100 };
    private readonly TextBlock percent = new() { Text = "0%", HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock details = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly Button cancel = new() { Content = "取消更新", HorizontalAlignment = HorizontalAlignment.Right };
    private readonly CancellationTokenSource cancellation = new();
    private bool critical, finished;
    internal UpdateWindow(string planFile)
    {
        Title = "正在更新 XG Launcher"; Width = 360; MinWidth = 300; MaxWidth = 420;
        MinHeight = 180; MaxHeight = 600; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; SystemDecorations = SystemDecorations.Full;
        Background = Brushes.White; Foreground = Brushes.Black;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        header.Children.Add(state);
        Grid.SetColumn(percent, 1);
        header.Children.Add(percent);
        var body = new StackPanel { Spacing = 12, Margin = new(18), Children =
        {
            header, progress, details, cancel
        } };
        Content = body;
        cancel.Click += (_, _) => { if (finished) Close(); else if (!critical) cancellation.Cancel(); };
        Closing += (_, e) => { if (!finished) { e.Cancel = true; if (!critical) cancellation.Cancel(); } };
        Closed += (_, _) => cancellation.Dispose();
        Opened += async (_, _) => await RunAsync(planFile);
    }
    private void ShowStage(string text, string detail)
    { state.Text = text; details.Text = detail; }
    private void SetProgress(double value)
    {
        progress.Value = value;
        percent.Text = $"{Math.Floor(progress.Value):0}%";
    }

    private async Task RunAsync(string planFile)
    {
        UpdateApplyRequest? plan = null; byte[]? originalHash = null; var parentExited = false;
        try
        {
            if (new FileInfo(planFile).Length > 65536) throw new InvalidDataException("更新计划过大。");
            plan = JsonSerializer.Deserialize<UpdateApplyRequest>(await File.ReadAllTextAsync(planFile)) ?? throw new InvalidDataException("更新计划无效。");
            var executable = Path.Combine(plan.InstallDirectory, "XGLauncher.exe");
            if (!File.Exists(executable) || plan.ParentProcessId < 1) throw new InvalidDataException("原程序不存在或进程标识无效。");
            if (plan.Theme is { } theme)
            {
                Background = new SolidColorBrush(Color.Parse(theme.Background)); Foreground = new SolidColorBrush(Color.Parse(theme.Text));
                progress.Foreground = new SolidColorBrush(Color.Parse(theme.Accent));
                Application.Current!.RequestedThemeVariant = theme.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
            }
            if (Screens.ScreenFromWindow(this) is { } screen)
            { Width = Math.Min(360, screen.WorkingArea.Width / screen.Scaling - 20); MaxHeight = Math.Max(140, screen.WorkingArea.Height / screen.Scaling - 24); }
            await using (var original = File.OpenRead(executable)) originalHash = await SHA256.HashDataAsync(original, cancellation.Token);
            var fileDetails = $"目标版本：{plan.TargetVersion}\n文件：{plan.Release?.AssetName ?? Path.GetFileName(plan.Archive)}\nSHA-256：{plan.Sha256}";
            if (plan.Release is { } release)
            {
                if (release.Version != plan.TargetVersion || release.Sha256 != plan.Sha256 ||
                    plan.CurrentVersion == null || !GitHubUpdates.AllowedChannel(plan.CurrentVersion, release.Version) ||
                    GitHubUpdates.CompareVersions(release.Version, plan.CurrentVersion) <= 0)
                    throw new InvalidDataException("更新版本不匹配。");
                ShowStage("正在下载更新…", fileDetails);
                using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
                var downloaded = await new GitHubUpdates(http).DownloadAsync(release, Path.GetDirectoryName(planFile)!, value =>
                {
                    SetProgress(value * .85);
                    details.Text = $"{fileDetails}\n已下载 {release.Size * value / 100 / 1048576d:0.00} / {release.Size / 1048576d:0.00} MiB";
                }, cancellation.Token);
                if (!string.Equals(downloaded, plan.Archive, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新文件路径不匹配。");
            }
            SetProgress(85);
            ShowStage("正在校验更新文件…", details.Text ?? fileDetails);
            using (var transaction = await Task.Run(() => UpdateFiles.Prepare(plan), cancellation.Token))
            {
                cancellation.Token.ThrowIfCancellationRequested();
                critical = true;
                cancel.IsEnabled = false;
                if (plan.Release != null) File.WriteAllText(planFile + ".ready", "");
                SetProgress(90);
                state.Text = "等待主程序安全退出…";
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                limit.CancelAfter(TimeSpan.FromMinutes(3));
                try
                {
                    using var parent = Process.GetProcessById(plan.ParentProcessId);
                    if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStartTicks)
                        throw new InvalidDataException("主程序进程已变更。");
                    while (!parent.HasExited)
                    {
                        if (File.Exists(planFile + ".abort")) throw new OperationCanceledException();
                        await Task.Delay(120, limit.Token);
                    }
                }
                catch (ArgumentException) { }
                if (plan.Release != null && !File.Exists(planFile + ".exit"))
                    throw new OperationCanceledException("没有收到主程序的安全退出确认。");
                parentExited = true;
                cancellation.Token.ThrowIfCancellationRequested();
                state.Text = "正在备份并替换程序…";
                await Task.Run(transaction.Apply);
                SetProgress(97);
                state.Text = "正在启动新版…";
                var nonce = Guid.NewGuid().ToString("N"); var confirmation = UpdateAcknowledgment.PathFor(nonce);
                using var child = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = plan.InstallDirectory,
                    ArgumentList = { "--xg-update-ack", nonce } }) ?? throw new IOException("新版本未能启动。");
                var deadline = Stopwatch.StartNew();
                try
                {
                    while (deadline.Elapsed < TimeSpan.FromSeconds(45) && !child.HasExited && !File.Exists(confirmation)) await Task.Delay(100);
                    if (child.HasExited || !File.Exists(confirmation) || await File.ReadAllTextAsync(confirmation) != child.Id.ToString())
                    {
                        if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); }
                        throw new IOException("新版本未确认启动。");
                    }
                    transaction.Commit();
                }
                finally { if (File.Exists(confirmation)) File.Delete(confirmation); }
            }
            SetProgress(100);
            ShowStage("更新完成，新版已启动。", details.Text ?? "");
            try { await File.WriteAllTextAsync(planFile + ".result", "Updated"); File.Delete(plan.Archive); File.Delete(planFile + ".ready"); File.Delete(planFile + ".exit"); File.Delete(planFile); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            finished = true; Close();
        }
        catch (Exception error)
        {
            critical = false; finished = true; cancel.IsEnabled = true; cancel.Content = "关闭"; progress.IsIndeterminate = false;
            // 只有原主进程已退出且 EXE 已确认恢复时才重启；避免并行启动两个实例。
            var recovered = false;
            if (plan != null && originalHash != null && parentExited)
            {
                var executable = Path.Combine(plan.InstallDirectory, "XGLauncher.exe");
                try
                {
                    using var original = File.OpenRead(executable);
                    if (CryptographicOperations.FixedTimeEquals(SHA256.HashData(original), originalHash))
                    { Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = plan.InstallDirectory }); recovered = true; }
                }
                catch (Exception recoveryError) { Trace.TraceWarning(recoveryError.GetType().Name); }
            }
            ShowStage(error is OperationCanceledException ? "更新已取消。" : "更新失败。", (recovered ? "已恢复并重新打开原版本。\n" : "未确认更新成功；用户数据未在更新范围内。\n") + error.Message);
            try { await File.WriteAllTextAsync(planFile + ".result", error.GetType().Name); } catch (IOException) { }
        }
    }
}
