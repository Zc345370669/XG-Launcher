// SPDX-License-Identifier: MIT
using Avalonia.Platform.Storage;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using XG.Core;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private Control Tabs(string[] names, int selected, Action<int> update)
    {
        var tabs = new WrapPanel();
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            var tab = Action(names[i], () => { update(index); Refresh(); return Task.CompletedTask; }, i == selected);
            tab.Margin = new(0, 0, 6, 5);
            tabs.Children.Add(tab);
        }
        return tabs;
    }

    private Control Mods()
    {
        if (modTab == 1) return OnlineCatalog(false);
        var root = Column(Label("模组管理", 22, true),
            Tabs(["已安装模组", "在线下载", "模组更新"], modTab, i => modTab = i));
        root.Children.Add(modTab switch
        {
            0 => Installed(), _ => Updates()
        });
        return root;
    }

    private Control Installed()
    {
        if (model.Selected is not { } instance) return Label("请先选中游戏实例");
        var root = Column(Action("导入模组", async () =>
        {
            EnsureIdle(instance);
            var files = await StorageProvider.OpenFilePickerAsync(new()
            {
                Title = Localizer.Text("选择模组"), AllowMultiple = true, FileTypeFilter = [new("JAR") { Patterns = ["*.jar"] }]
            });
            EnsureIdle(instance);
            var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
            if (paths.Length == 0) return;
            var directory = model.ModsFolder(instance);
            var failures = new List<string>();
            var added = 0;
            var skipped = 0;
            foreach (var path in paths)
            {
                try
                {
                    if (LocalMods.Import(path, directory)) added++;
                    else skipped++;
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    failures.Add($"{Path.GetFileName(path)}：{error.Message}");
                }
            }
            Refresh();
            var summary = Dialog("模组导入结果");
            summary.SetBody(Label($"已导入 {added} 个，已存在 {skipped} 个，失败 {failures.Count} 个。"
                + (failures.Count == 0 ? "" : "\n" + string.Join("\n", failures))), CloseDialog(summary, "关闭"));
            await summary.ShowDialog(this);
        }, true));
        var installed = model.Store.Entries
            .Select(item => (Instance: item, Files: model.LocalFiles(item)))
            .Where(item => item.Files.Length > 0)
            .ToArray();
        root.Name = "InstalledModsList";
        root.Children.Add(Label($"导入目标：{instance.Name}", 12));
        if (installed.Length == 0)
        {
            root.Children.Add(Label("暂无已安装模组"));
            return root;
        }

        foreach (var version in installed.GroupBy(item => item.Instance.Version)
            .OrderByDescending(group => group.Any(item => item.Instance.Id == instance.Id))
            .ThenByDescending(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            root.Children.Add(Label($"Minecraft {version.Key}", 17, true));
            foreach (var (owner, files) in version)
            {
                root.Children.Add(Label($"{owner.Name} · {owner.Loader} · {files.Length} 个模组", 12, localize: false));
                foreach (var file in files.OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
                {
                    var disabled = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    var card = Frame(Header(Path.GetFileName(file), Row(
                        Action(disabled ? "启用" : "禁用", () =>
                        {
                            EnsureIdle(owner);
                            File.Move(file, disabled ? file[..^9] : file + ".disabled");
                            Refresh(); return Task.CompletedTask;
                        }), Action("卸载", async () =>
                        {
                            if (!await ConfirmAsync("卸载 " + Path.GetFileName(file) + "？")) return;
                            EnsureIdle(owner); File.Delete(file); Refresh();
                        }))), new(10));
                    root.Children.Add(card);
                }
            }
        }
        return root;
    }

    private Control Updates()
    {
        if (model.Selected is not { } instance) return Label("请先选中游戏实例");
        var results = Column();
        return Column(Action("检查 Modrinth 更新", async () =>
        {
            results.Children.Clear();
            results.Children.Add(Label("正在按文件校验值识别模组…"));
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var installed = await new ModInstallPlanner(model.Mods).IdentifyAsync(model.LocalFiles(instance), deadline.Token);
            results.Children.Clear();
            foreach (var item in installed)
            {
                if (item.Version is not { ProjectId.Length: > 0 } current)
                {
                    results.Children.Add(Label($"{Path.GetFileName(item.Path)} · 无法识别"));
                    continue;
                }
                var project = new Project(current.ProjectId, current.ProjectId, current.ProjectId, "", "", null, 0, false);
                var latest = (await model.Mods.VersionsAsync(project, instance, deadline.Token))
                    .FirstOrDefault(value => ModInstallPlanner.Compatible(value, instance));
                results.Children.Add(Label($"{Path.GetFileName(item.Path)} · {current.Number} · " +
                    (latest == null ? "无兼容版本" : latest.Id == current.Id ? "已是最新兼容版本" : $"发现版本 {latest.Number}")));
            }
            if (installed.Length == 0) results.Children.Add(Label("实例没有已安装模组。"));
        }), results, Action("打开在线下载", () => { modTab = 1; Refresh(); return Task.CompletedTask; }));
    }

    private Control Packs()
    {
        if (packTab == 0) return OnlineCatalog(true);
        var root = Column(Label("整合包", 22, true),
            Tabs(["整合包市场", "本地整合包"], packTab, i => packTab = i));
        root.Children.Add(Action("导入整合包", ImportLocalPackAsync, true));
        foreach (var instance in model.Store.Entries.Where(x => x.Pack)) root.Children.Add(InstanceRow(instance));
        if (root.Children.Count == 3) root.Children.Add(Label("暂无本地整合包"));
        if (status.Parent is Panel previous) previous.Children.Remove(status);
        root.Children.Insert(3, status);
        return root;
    }

    private readonly Dictionary<Guid, (double Percent, string Text)> transferProgress = [];
    private readonly Dictionary<Guid, (ProgressBar Bar, TextBlock Text)> transferControls = [];

    private readonly Dictionary<Guid, (ProgressBar Total, TextBlock Percent, TextBlock Summary, TextBlock Current, TextBlock Counts, Canvas Stages)> resourceControls = [];

    private static string TransferText(long bytes, long total, double speed)
    {
        var size = total > 0 ? $"{Math.Clamp(100.0 * bytes / total, 0, 100):0.0}% · {bytes / 1048576.0:0.00} / {total / 1048576.0:0.00} MB"
            : $"{bytes / 1048576.0:0.00} MB · 总大小未知";
        var eta = total > bytes && speed > 0
            ? $" · 剩余约 {TimeSpan.FromSeconds(Math.Min(86400, Math.Ceiling((total - bytes) / speed))):\\hh\\:mm\\:ss}" : "";
        return $"{size} · {speed / 1048576:0.00} MB/s{eta}";
    }

    internal static string ResourceTransferText(ResourceOperation item, IReadOnlyList<ResourceOperation> children)
    {
        var speed = item.State == "下载中" ? item.BytesPerSecond : 0;
        var required = children.Where(child => !child.Skipped).ToArray();
        var known = required.Sum(child => Math.Max(0, child.TotalBytes));
        var partial = required.Any(child => child.TotalBytes <= 0 && (child.Kind == "模组加载器" || child.State != "已完成" || child.TotalFiles > 0 || child.Bytes > 0));
        if (known > 0 && partial)
            // 已知字节仍可展示；不拿部分总量计算整项剩余时间或伪装成完整容量。
            return $"{Localizer.Text("已处理")} {item.Bytes / 1048576.0:0.00} MB · {Localizer.Text("已知总量")} {known / 1048576.0:0.00} MB · {Localizer.Text("部分资源大小未提供")} · {speed / 1048576:0.00} MB/s";
        var total = required.Length > 0 ? partial ? 0 : known : item.TotalBytes;
        var description = TransferText(item.Bytes, total, speed);
        return description.Contains("% · ", StringComparison.Ordinal) ? description[(description.IndexOf("% · ", StringComparison.Ordinal) + 4)..] : description;
    }

    private static ProgressBar TransferBar() => new()
    {
        Maximum = 100, Foreground = ThemePalette.Accent, Background = ThemePalette.Line
    };

    private void UpdateResourceProgress(ResourceOperation item)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateResourceProgress(item)); return; }
        if (windowClosed) return;
        UpdateCompactProgress();
        if (!resourceControls.TryGetValue(item.Id, out var view)) return;
        var progress = SharedProgress(item);
        view.Total.IsIndeterminate = false;
        view.Percent.Text = $"{Math.Floor(progress.Percent):0}%";
        view.Summary.Text = ResourceTransferText(item, model.Resources.Items.Where(child => child.ParentId == item.Id).ToArray());
        view.Counts.Text = item.TotalFiles > 0 ? $"{item.CompletedFiles}/{item.TotalFiles}" : "";
        view.Current.Text = FriendlyResource(item.CurrentFile, item.CurrentStage ?? item.Kind);
        // 旧任务可能缺少加载器显示元数据，只从该任务恢复目标对应的实例回补。
        var loader = model.Store.Entries.FirstOrDefault(x => x.Id == item.Recovery?.InstanceId)?.Loader;
        var nodes = progress.Stages.Select(stage => stage.Kind == "模组加载器" && stage.DisplayName == null && loader is not (null or "原版")
            ? stage with { DisplayName = loader } : stage).ToArray();
        ((StageRail)view.Stages).Update(nodes,item.CurrentStage,item.State,progress.Percent);
        view.Total.Value = ((StageRail)view.Stages).DisplayedPercent;
    }

    private Control DownloadSettings()
    {
        var limit = Picker(["1", "2", "3", "4", "5", "6"], model.Downloads.Concurrency - 1, "文件下载并发数");
        limit.SelectionChanged += (_, _) =>
        {
            if (limit.SelectedIndex >= 0 && model.Downloads.Concurrency != limit.SelectedIndex + 1) model.Downloads.SetConcurrency(limit.SelectedIndex + 1);
        };
        int[] rates = [0, 1, 2, 5, 10, 20, 50, 100];
        var speed = Picker(rates.Select(x => x == 0 ? "不限速" : $"{x} MiB/s").ToArray(),
            Math.Max(0, Array.IndexOf(rates, model.Downloads.SpeedLimitMiB)), "下载限速");
        speed.SelectionChanged += (_, _) =>
        {
            if (speed.SelectedIndex >= 0 && model.Downloads.SpeedLimitMiB != rates[speed.SelectedIndex]) model.Downloads.SetSpeedLimit(rates[speed.SelectedIndex]);
        };
        var panel = new WrapPanel { ItemSpacing = 6, Children = { limit, speed } };
        panel.AttachedToVisualTree += (_, _) =>
        {
            limit.SelectedIndex = model.Downloads.Concurrency - 1;
            speed.SelectedIndex = Math.Max(0, Array.IndexOf(rates, model.Downloads.SpeedLimitMiB));
        };
        return panel;
    }

    private Guid? focusedDownload;
    private DownloadSurface? downloadSurface;
    private readonly Dictionary<Guid, TextBlock> transferPercentControls = [];
    private Control Queue()
    {
        var root = Column(Header("下载管理", Action("清理已完成", () =>
        {
            model.Downloads.ClearCompleted();
            model.Resources.ClearCompleted();
            Refresh();
            return Task.CompletedTask;
        })),
            Tabs(["下载中", "已完成", "失败"], downloadTab, i => downloadTab = i));
        root.Children.Add(DownloadSettings());
        var cards = new List<Func<Control>>();
        var jobs = model.Downloads.Items.Where(x => InDownloadTab(x.State, downloadTab));
        foreach (var job in jobs)
        {
            cards.Add(() => {
            var active = job.State is "等待中" or "连接中" or "下载中" or "校验中" or "安装中";
            var action = Action(active ? "暂停" : "继续 / 重试", () =>
            {
                if (active) { model.Downloads.Pause(job.Id); return Task.CompletedTask; }
                return RunDownloadAsync(job.Id);
            });
            action.IsVisible = job.State is not ("已完成" or "已取消");
            var current = transferProgress.GetValueOrDefault(job.Id);
            var text = Label(job.Error ?? current.Text ?? TransferText(job.State == "已完成" ? job.File.Size : 0, job.File.Size, 0));
            var progress = TransferBar();
            progress.Value = job.State == "已完成" ? 100 : current.Percent;
            var percentage = Label($"{Math.Floor(progress.Value):0}%");
            transferPercentControls[job.Id] = percentage;
            transferControls[job.Id] = (progress, text);
            progress.DetachedFromVisualTree += (_, _) =>
            {
                if (transferControls.TryGetValue(job.Id, out var shown) && ReferenceEquals(shown.Bar, progress))
                    transferControls.Remove(job.Id);
            };
            var cancel = Action("取消", async () =>
            {
                if (!await ConfirmAsync("取消任务并删除未完成的临时文件？")) return;
                model.Downloads.Cancel(job.Id);
                Refresh();
            });
            cancel.IsVisible = action.IsVisible;
            var priority = Action(job.Priority == 0 ? "设为优先" : "恢复普通", () =>
            {
                model.Downloads.SetPriority(job.Id, job.Priority == 0);
                Refresh();
                return Task.CompletedTask;
            });
            priority.IsVisible = action.IsVisible;
            var controls = new WrapPanel { Children = { action, cancel, priority } };
            return Frame(Column(Label(job.File.Name, 15, true), controls,
                Label($"{job.Category} · Modrinth · {job.Instance ?? "已有实例"} · {job.File.Size / 1048576.0:0.00} MB"),
                Label($"当前文件　{job.File.Name} · 文件 {(job.State == "已完成" ? 1 : 0)}/1"), Label(job.State), text, ProgressRow(progress, percentage)), new(10));
            });
        }
        foreach (var item in model.Resources.MainJobs.Where(x => InDownloadTab(x.State, downloadTab)).Reverse())
        {
            cards.Add(() => {
            var total = TransferBar();
            total.UseLayoutRounding = false;
            total.MinWidth = 0;
            var percentage = Label("0%");
            var stages = new StageRail { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            // 下方节点与上方总条使用同一可用宽度，不把右侧百分比列算进100%终点。
            total.SizeChanged += (_, e) => stages.Width = e.NewSize.Width;
            stages.DisplayedPercentChanged += value => total.Value = value;
            var summary = Label("");
            var current = Label("");
            var counts = Label("");
            resourceControls[item.Id] = (total, percentage, summary, current, counts, stages);
            total.DetachedFromVisualTree += (_, _) =>
            {
                if (resourceControls.TryGetValue(item.Id, out var shown) && ReferenceEquals(shown.Total, total))
                    resourceControls.Remove(item.Id);
            };
            UpdateResourceProgress(item);
            var actions = new WrapPanel();
            if (model.Resources.CanControl(item.Id))
            {
                var paused = model.Resources.IsPaused(item.Id);
                actions.Children.Add(Action(paused ? "继续" : "暂停", () =>
                {
                    if (paused) model.Resources.Resume(item.Id); else model.Resources.Pause(item.Id);
                    return Task.CompletedTask;
                }));
                var cancelResource = Action("取消", async () =>
                {
                    if (await ConfirmAsync("取消此资源任务？")) model.Resources.Cancel(item.Id);
                });
                actions.Children.Add(cancelResource);
            }
            if (model.Resources.CanRecover(item.Id))
            {
                actions.Children.Add(Action("恢复 / 重试", async () =>
                {
                    maintenance++;
                    try { await model.ResumeResourceAsync(item, message => status.Text = message); }
                    catch (OperationCanceledException) { status.Text = "已取消恢复"; }
                    finally { maintenance--; }
                    if (section == 4) Refresh();
                }));
                actions.Children.Add(Action("取消任务", async () =>
                {
                    if (await ConfirmAsync("取消这个恢复任务？")) model.Resources.Discard(item.Id);
                }));
            }
            var target = model.Store.Entries.FirstOrDefault(x => x.Id == item.Recovery?.InstanceId);
            var title = target is null ? item.Name : $"Minecraft {target.Version} · {target.Loader}";
            var card = Frame(Column(Header(title, actions),
                Label(item.Error ?? (item.CurrentStage is { } stage ? $"{item.State} · {stage}" : item.State)),
                summary, Header("当前文件", counts), current, ProgressRow(total, percentage), stages), new(10));
            card.Name = "DownloadJob_" + item.Id.ToString("N");
            card.Margin = new(0, 0, 0, 8);
            if (focusedDownload == item.Id)
            {
                card.BorderBrush = ThemePalette.Accent; card.BorderThickness = new(2);
                card.AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => card.BringIntoView(), Avalonia.Threading.DispatcherPriority.Loaded);
            }
            return card;
            });
        }
        if (cards.Count == 0)
        {
            var failed = downloadTab == 0 ? CountDownloads(2) : 0;
            cards.Add(() => failed == 0 ? Label("暂无任务") :
                Action($"有 {failed} 个失败任务，点击查看", () =>
                {
                    downloadTab = 2;
                    Refresh();
                    return Task.CompletedTask;
                }));
        }
        var sync = Column(); AddSyncTasks(sync);
        if (sync.Children.Count > 0) cards.Add(() => sync);
        downloadSurface ??= new DownloadSurface();
        downloadSurface.Update(root, cards);
        return downloadSurface;
    }

    private static Control ProgressRow(ProgressBar bar, TextBlock percentage)
    {
        var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10 };
        bar.VerticalAlignment = VerticalAlignment.Center; percentage.MinWidth = 34; percentage.TextAlignment = Avalonia.Media.TextAlignment.Right;
        row.Children.Add(bar); Grid.SetColumn(percentage, 1); row.Children.Add(percentage); return row;
    }

    private async Task<bool> RunDownloadAsync(Guid id)
    {
        var job = model.Downloads.Items.Single(x => x.Id == id);
        long? previous = null;
        var timer = Stopwatch.StartNew();
        var completed = await model.Downloads.RunAsync(id, () => { UpdateBadge(); if (section == 4) Refresh(); },
            (bytes, total) =>
            {
                var speed = previous.HasValue ? Math.Max(0, bytes - previous.Value) / Math.Max(0.001, timer.Elapsed.TotalSeconds) : 0;
                previous = bytes;
                timer.Restart();
                var percent = total > 0 ? Math.Clamp(100.0 * bytes / total, 0, 100) : 0;
                var text = TransferText(bytes, total, speed);
                transferProgress[id] = (percent, text);
                if (transferControls.TryGetValue(id, out var shown))
                {
                    shown.Bar.Value = percent;
                    shown.Text.Text = text;
                    if (transferPercentControls.TryGetValue(id, out var label)) label.Text = $"{Math.Floor(Math.Min(99, percent)):0}%";
                }
                status.Text = text;
                UpdateCompactProgress();
            }, () => model.Store.Entries.Any(x => !model.Busy.Contains(x.Id) &&
                (job.InstanceId == null || job.InstanceId == x.Id && job.InstanceConfiguration == LauncherModel.GameConfiguration(x)) &&
                string.Equals(Path.GetDirectoryName(job.Target), model.ModsFolder(x), StringComparison.OrdinalIgnoreCase)));
        if (completed) { status.Text = "下载完成"; transferProgress.Remove(id); }
        if (section is 0 or 4) Refresh();
        return completed;
    }

    private void EnsureIdle(Instance instance)
    {
        if (model.Busy.Contains(instance.Id)) throw new InvalidOperationException("请先关闭游戏。");
    }

    private async Task<bool> ConfirmAsync(string text, Window? owner = null)
    {
        var dialog = Dialog("确认操作", owner);
        var confirmed = false;
        dialog.SetBody(Label(text), CloseDialog(dialog),
            Action("确认", () => { confirmed = true; dialog.Close(); return Task.CompletedTask; }, true));
        await dialog.ShowDialog(owner ?? this);
        return confirmed;
    }

    internal Func<string, Task> OpenExternalLink { get; set; } = destination =>
    {
        Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });
        return Task.CompletedTask;
    };
    private Button Link(string label, string destination)
    {
        var button = Action(label, () => OpenExternalLink(destination));
        button.Content = Label(label, 12);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Avalonia.Automation.AutomationProperties.SetName(button, destination);
        return button;
    }
}
