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
        var root = Column(Action("导入本地 JAR", async () =>
        {
            EnsureIdle(instance);
            var files = await StorageProvider.OpenFilePickerAsync(new()
            {
                Title = Localizer.Text("选择模组"), AllowMultiple = true, FileTypeFilter = [new("JAR") { Patterns = ["*.jar"] }]
            });
            EnsureIdle(instance);
            foreach (var file in files)
                if (file.TryGetLocalPath() is { } path) LocalMods.Import(path, model.ModsFolder(instance));
            Refresh();
        }, true));
        foreach (var file in model.LocalFiles(instance))
        {
            var disabled = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            root.Children.Add(Frame(Header(Path.GetFileName(file), Row(
                Action(disabled ? "启用" : "禁用", () =>
                {
                    EnsureIdle(instance);
                    File.Move(file, disabled ? file[..^9] : file + ".disabled");
                    Refresh(); return Task.CompletedTask;
                }), Action("卸载", async () =>
                {
                    if (!await ConfirmAsync("卸载 " + Path.GetFileName(file) + "？")) return;
                    EnsureIdle(instance); File.Delete(file); Refresh();
                }))), new(10)));
        }
        if (root.Children.Count == 1) root.Children.Add(Label("暂无已安装模组"));
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
        root.Children.Add(Action("导入 .mrpack", async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new()
            {
                Title = Localizer.Text("选择整合包"), FileTypeFilter = [new("Modrinth") { Patterns = ["*.mrpack"] }]
            });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } local) return;
            maintenance++;
            try
            {
                model.Selected = await model.QueuePackAsync(Path.GetFileName(local), null, local, progress => status.Text = progress);
            }
            finally { maintenance--; }
            status.Text = "整合包导入完成";
            Refresh();
        }, true));
        foreach (var instance in model.Store.Entries.Where(x => x.Pack)) root.Children.Add(InstanceRow(instance));
        if (root.Children.Count == 3) root.Children.Add(Label("暂无本地整合包"));
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
        view.Total.Value = progress.Percent;
        view.Percent.Text = $"{Math.Floor(progress.Percent):0}%";
        var active = item.State == "下载中";
        var description = TransferText(item.Bytes, item.TotalBytes, active ? item.BytesPerSecond : 0);
        view.Summary.Text = description.Contains("% · ", StringComparison.Ordinal) ? description[(description.IndexOf("% · ", StringComparison.Ordinal) + 4)..] : description;
        view.Counts.Text = item.TotalFiles > 0 ? $"{item.CompletedFiles}/{item.TotalFiles}" : "";
        view.Current.Text = FriendlyResource(item.CurrentFile, item.CurrentStage ?? item.Kind);
        // 旧任务可能缺少加载器显示元数据，只从该任务恢复目标对应的实例回补。
        var loader = model.Store.Entries.FirstOrDefault(x => x.Id == item.Recovery?.InstanceId)?.Loader;
        var nodes = progress.Stages.Select(stage => stage.Kind == "模组加载器" && stage.DisplayName == null && loader is not (null or "原版")
            ? stage with { DisplayName = loader } : stage).ToArray();
        var slots = model.Resources.Items.Count(child => child.ParentId == item.Id && !child.Skipped) + 1;
        ((StageRail)view.Stages).Update(nodes,item.CurrentStage,item.State,progress.Percent,slots);
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
        var jobs = model.Downloads.Items.Where(x => downloadTab switch
        {
            1 => x.State is "已完成" or "已取消", 2 => x.State == "失败", _ => x.State is not ("已完成" or "已取消" or "失败")
        });
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
        foreach (var item in model.Resources.MainJobs.Where(x => downloadTab switch
        {
            1 => x.State is "已完成" or "已取消", 2 => x.State == "失败", _ => x.State is not ("已完成" or "已取消" or "失败")
        }).Reverse())
        {
            cards.Add(() => {
            var total = TransferBar();
            var percentage = Label("0%");
            var stages = new StageRail();
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
        if (cards.Count == 0) cards.Add(() => Label("暂无任务"));
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
