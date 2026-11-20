// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using XG.Core;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private readonly UnifiedExpandableSelect compactInstances = new()
    {
        Name = "CompactInstances", Title = "已安装游戏", TwoLineTitle = true,
        PlaceholderText = "暂无已安装游戏", Searchable = true, RemoveIcon = "trash-2", RemoveLabel = "删除实例",
        DisplayMember = value => value is Instance instance ? $"Minecraft {instance.Version} · {instance.Loader} · {instance.Name}" : ""
    };
    private Button compactPrimary = null!;
    private readonly TextBlock compactStatus = Label("");
    private readonly TextBlock compactFile = Label("");
    private readonly TextBlock compactCount = Label("");
    private readonly TextBlock compactPercent = Label("");
    private readonly ProgressBar compactBar = TransferBar();
    private Control compactProgress = null!;
    private bool updatingCompact;
    private Instance[] compactChoices = [];

    /// <summary>
    /// 构造紧凑模式入口，复用共享实例、账号和下载状态，不创建独立服务。
    /// </summary>
    private Control CreateCompactBody()
    {
        compactInstances.RemoveItemAsync = async item =>
        {
            try { await DeleteInstanceAsync((Instance)item); }
            catch (Exception ex) { await ErrorAsync(compactInstances, ex.Message); }
        };
        compactInstances.FooterText = "下载游戏实例";
        compactInstances.FooterAction = OpenInstanceDownloadAsync;
        compactInstances.EmptyAction = OpenInstanceDownloadAsync;
        // 无游戏和新增实例都在同一无缝组合框中安装，不改为跳转完整模式或另建下载器。
        compactInstances.ExpansionFactory = () => CompactDownloadForm(compactInstances);
        compactInstances.ExpansionChanged += (_, _) => UpdateCompact();
        var accounts = CreateAccountSelector("CompactAccounts");
        var accountRow = new Grid { Name = "CompactAccountBar" };
        accountRow.Children.Add(accounts);
        // 启动入口仍受已校验实例及账号选择约束，进度和启动Busy状态来自同一模型。
        compactPrimary = Action("启动游戏", async () =>
        {
            if (model.Accounts.SelectedProfile == null) { accounts.OpenAuxiliary(); return; }
            if (model.Selected is { } selected && compactChoices.Any(x => x.Id == selected.Id))
                await LaunchInstanceAsync(selected);
        }, true);
        compactPrimary.Name = "CompactPrimary";
        compactPrimary.HorizontalAlignment = HorizontalAlignment.Stretch;
        compactPrimary.HorizontalContentAlignment = HorizontalAlignment.Center;
        compactPrimary.MinHeight = 44;
        var versionRow = new Grid { Name = "CompactVersionRow" };
        versionRow.Children.Add(compactInstances);
        compactInstances.SelectionChanged += (_, _) =>
        {
            if (updatingCompact || compactInstances.SelectedItem is not Instance selected) return;
            model.Selected = selected; Refresh();
        };
        compactStatus.Name = "CompactInstallStatus"; compactStatus.FontSize = 12; compactStatus.Foreground = ThemePalette.Muted;
        compactFile.TextTrimming = TextTrimming.CharacterEllipsis; compactFile.TextWrapping = TextWrapping.NoWrap;
        compactFile.FontSize = compactCount.FontSize = compactPercent.FontSize = 11;
        var counts = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
        counts.Children.Add(compactFile); Grid.SetColumn(compactCount, 1); counts.Children.Add(compactCount);
        Grid.SetColumn(compactPercent, 2); counts.Children.Add(compactPercent);
        compactBar.Name = "CompactDownloadBar"; compactBar.Height = 4; compactBar.MinHeight = 4;
        compactProgress = Column(counts, compactBar); ((StackPanel)compactProgress).Spacing = 6;
        compactProgress.Name = "CompactProgress";
        compactProgress.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (IsCompact) ToggleMode(); section = 4; downloadTab = 0; Refresh();
        };
        var logo = OfficialLogo(126, 88); logo.Name = "CompactOfficialLogo"; logo.Margin = new(10, 2, 10, 8);
        var body = Column(logo, versionRow, compactProgress, compactPrimary, compactStatus);
        compactStatus.IsVisible = false;
        body.Spacing = 10;
        var grid = new Grid { Name = "CompactLayout", RowDefinitions = new("Auto,Auto"), Margin = new(20, 14, 20, 16), RowSpacing = 16,
            VerticalAlignment = VerticalAlignment.Top };
        var core = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        // 保持单列控件自然高度，避免拉高窗口后把启动区与账号栏分隔成大块空白。
        grid.SizeChanged += (_, _) => core.MaxHeight = Math.Max(100, ClientSize.Height - WindowChrome.CaptionHeight - 30 - accountRow.Bounds.Height - 16);
        SizeChanged += (_, _) => core.MaxHeight = Math.Max(100, ClientSize.Height - WindowChrome.CaptionHeight - 30 - accountRow.Bounds.Height - 16);
        grid.Children.Add(core);
        Grid.SetRow(accountRow, 1); grid.Children.Add(accountRow);
        return grid;
    }

    private Task OpenInstanceDownloadAsync()
    {
        compactInstances.OpenAuxiliary();
        return Task.CompletedTask;
    }

    private Button CompactIcon(string label, string icon, Func<Task> command)
    {
        var button = Action(label, command);
        button.Content = new LucideIcon(icon, 18); button.Padding = new(0);
        button.Background = Brushes.Transparent; button.BorderThickness = new(0); button.CornerRadius = new(7);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(button, label); Avalonia.Automation.AutomationProperties.SetName(button, label);
        return button;
    }

    private void UpdateCompact()
    {
        if (compactPrimary is null || windowClosed) return;
        updatingCompact = true;
        try
        {
            var all = model.Store.Entries.ToArray();
            foreach (var entry in all) _ = InstanceState(entry); // Existing receipt audit, never a download.
            compactInstances.IsLoading = inspecting.Count > 0;
            var entries = all.Where(x => installationStates.GetValueOrDefault(x.Id) == "已安装" && model.InstallationJob(x) == null).ToArray();
            if (!entries.SequenceEqual(compactChoices)) { compactChoices = entries; compactInstances.ItemsSource = entries; }
            var selected = model.Selected;
            compactInstances.SelectedItem = entries.FirstOrDefault(x => x.Id == selected?.Id);
            var installed = selected != null && entries.Any(x => x.Id == selected.Id);
            compactInstances.Title = entries.Length == 0 || compactInstances.IsAuxiliaryOpen ? "下载游戏实例" : "已安装游戏";
            compactInstances.PlaceholderText = entries.Length == 0 ? "选择游戏版本并安装" : "选择已安装游戏";
            var launching = selected != null && instanceStatuses.GetValueOrDefault(selected.Id) == "正在检查和准备游戏";
            var launchFailed = selected != null && instanceStatuses.GetValueOrDefault(selected.Id)?.StartsWith("启动失败", StringComparison.Ordinal) == true;
            compactPrimary.IsVisible = installed;
            compactPrimary.Content = launching ? "启动中…" : model.Busy.Contains(selected?.Id ?? Guid.Empty) ? "游戏运行中" : "启动游戏";
            compactPrimary.IsEnabled = installed && !launching && !model.Busy.Contains(selected!.Id);
            compactStatus.IsVisible = launchFailed;
            compactStatus.Text = launchFailed ? "启动失败，请检查实例设置后重试" : "";
            UpdateCompactProgress();
        }
        finally { updatingCompact = false; }
    }

    private ResourceOperation? CompactJob() => model.Resources.MainJobs.LastOrDefault(x =>
        x.Recovery?.InstanceId == model.Selected?.Id && x.State is not ("已完成" or "已取消")) ??
        model.Resources.MainJobs.LastOrDefault(x => x.State is not ("已完成" or "已取消"));

    private void UpdateCompactProgress()
    {
        if (compactProgress is null || windowClosed) return;
        var item = CompactJob();
        if (item is null)
        {
            var download = model.Downloads.Items.LastOrDefault(x => x.State is not ("已完成" or "已取消"));
            compactProgress.IsVisible = download is not null;
            if (download is null) return;
            var value = Math.Min(99, transferProgress.GetValueOrDefault(download.Id).Percent);
            compactFile.Text = FriendlyResource(download.File.Name, download.Category);
            compactCount.Text = "0/1"; compactPercent.Text = $"{value:0}%"; compactBar.Value = value;
            return;
        }
        compactProgress.IsVisible = true;
        var progress = SharedProgress(item);
        compactFile.Text = item.Name + (item.State == "失败" ? " · 下载失败，点击查看重试" : " · " + item.State);
        ToolTip.SetTip(compactFile, item.State == "失败" ? "切换完整模式的下载管理查看失败原因并重试" : item.Name);
        compactCount.Text = item.TotalFiles > 0 ? $"{item.CompletedFiles}/{item.TotalFiles}" : "";
        compactPercent.Text = $"{Math.Floor(progress.Percent):0}%"; compactBar.Value = progress.Percent;

    }

    internal static string FriendlyResource(string? file, string kind)
    {
        if (string.IsNullOrWhiteSpace(file)) return kind;
        var name = Path.GetFileName(file.Replace('\\', '/'));
        if (name.Length > 64 || System.Text.RegularExpressions.Regex.IsMatch(name, @"\A[0-9a-fA-F]{32,}(?:\.[A-Za-z0-9]+)?\z")) return kind;
        return name;
    }

    private readonly Dictionary<Guid, RelayProgressTracker> progressTrackers = [];
    /// <summary>
    /// 完整和精简模式共用同一任务的接力跟踪器，避免切换模式导致进度归零。
    /// </summary>
    private InstallationProgress SharedProgress(ResourceOperation item)
    {
        var target = model.Store.Entries.FirstOrDefault(x => x.Id == item.Recovery?.InstanceId);
        var children = model.Resources.Items.Where(x => x.ParentId == item.Id && (target?.Loader != "原版" || x.Kind != "模组加载器")).ToArray();
        var progress = InstallationProgress.Calculate(item, children);
        if (!progressTrackers.TryGetValue(item.Id, out var tracker)) progressTrackers[item.Id] = tracker = new();
        return tracker.Update(progress, item.State);
    }
}
