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
    private readonly TextBlock state = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar progress = new() { Height = 4, Minimum = 0, Maximum = 1, IsIndeterminate = true };
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
        var body = new StackPanel { Spacing = 12, Margin = new(18), Children = { state, progress, new Expander { Header = "文件详情", Content = new ScrollViewer { Content = details, MaxHeight = 220, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } }, cancel } };
        Content = body;
        cancel.Click += (_, _) => { if (finished) Close(); else if (!critical) cancellation.Cancel(); };
        Closing += (_, e) => { if (!finished) { e.Cancel = true; if (!critical) cancellation.Cancel(); } };
        Closed += (_, _) => cancellation.Dispose();
        Opened += async (_, _) => await RunAsync(planFile);
    }
    private void ShowStage(string text, string detail)
    { state.Text = text; details.Text = detail; }
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
            ShowStage("等待主程序安全退出…", $"目标版本：{plan.TargetVersion}\n文件：XGLauncher.exe\n下载 SHA-256：{plan.Sha256}\n不修改游戏、账号、配置或 Java。");
            try
            {
                using var parent = Process.GetProcessById(plan.ParentProcessId);
                if (parent.StartTime.ToUniversalTime().Ticks == plan.ParentStartTicks)
                {
                    using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token); limit.CancelAfter(TimeSpan.FromMinutes(2));
                    await parent.WaitForExitAsync(limit.Token);
                }
            }
            catch (ArgumentException) { /* 原进程已经退出。 */ }
            parentExited = true;
            cancellation.Token.ThrowIfCancellationRequested();
            ShowStage("正在再次校验更新文件…", details.Text ?? "");
            using (var transaction = await Task.Run(() => UpdateFiles.Prepare(plan), cancellation.Token))
            {
                cancellation.Token.ThrowIfCancellationRequested();
                critical = true; cancel.IsEnabled = false;
                ShowStage("正在备份并替换程序…", "只替换经过校验的程序文件，失败将恢复备份。\n" + details.Text);
                await Task.Run(transaction.Apply);
                ShowStage("正在启动新版并等待确认…", "新版主窗口须在 45 秒内确认初始化。未确认将恢复旧版。\n" + details.Text);
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
            progress.IsIndeterminate = false; progress.Value = 1;
            ShowStage("更新完成，新版已启动。", details.Text ?? "");
            await File.WriteAllTextAsync(planFile + ".result", "Updated");
            try { File.Delete(plan.Archive); File.Delete(planFile); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
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
            ShowStage(error is OperationCanceledException ? "更新已取消。" : "更新失败。", (recovered ? "已恢复并重新打开原版本。\n" : "未确认更新成功；用户数据未在更新范围内。\n") + error.GetType().Name);
            try { await File.WriteAllTextAsync(planFile + ".result", error.GetType().Name); } catch (IOException) { }
        }
    }
}
