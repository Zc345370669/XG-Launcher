// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using XG.Core;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private sealed class SyncJob(string title)
    {
        public string Title { get; } = title;
        public int Percent { get; set; }
        public string Message { get; set; } = "等待同步";
        public int Tab { get; set; }
        public TextBlock? StatusView { get; set; }
        public ProgressBar? ProgressView { get; set; }
        public void Update(int percent, string message)
        {
            Percent = percent;
            Message = message;
            if (StatusView is { } statusView) statusView.Text = message;
            if (ProgressView is { } progressView) progressView.Value = percent;
        }
    }

    private readonly List<SyncJob> syncJobs = [];

    private async Task SyncPackAsync(Instance source)
    {
        var dialog = Dialog("同步到其他实例");
        var options = new[]
        {
            new CheckBox { Content = "模组和已安装依赖", IsChecked = true },
            new CheckBox { Content = "配置文件", IsChecked = true },
            new CheckBox { Content = "资源包" }, new CheckBox { Content = "光影包" }, new CheckBox { Content = "存档" }
        };
        var choices = new List<(Instance Target, CheckBox Box)>();
        var panel = Column(Label("选择目标实例", 17, true));
        foreach (var target in model.Store.Entries.Where(x => x.Id != source.Id))
        {
            var valid = InstanceSynchronization.Compatible(source, target);
            var item = new CheckBox
            {
                Content = $"{target.Name} · {target.Version} / {target.Loader}" + (valid ? "" : "（版本不兼容或加载器未固定）"),
                IsEnabled = valid
            };
            choices.Add((target, item));
            panel.Children.Add(item);
        }
        if (choices.Count == 0) panel.Children.Add(Label("暂无其他实例。"));
        panel.Children.Add(Label("同步内容", 16, true));
        var categories = new WrapPanel();
        foreach (var option in options) { option.Margin = new(0, 0, 12, 0); categories.Children.Add(option); }
        panel.Children.Add(categories);
        panel.Children.Add(Label("同步前备份目标文件。同名文件内容不同时停止；只复制源实例已有的文件。"));
        Instance[]? selected = null;
        var apply = Action("开始同步", () =>
        {
            if (!options.Any(item => item.IsChecked == true))
                throw new InvalidOperationException("请勾选要同步的内容。");
            selected = choices.Where(item => item.Box.IsChecked == true).Select(item => item.Target).ToArray();
            if (selected.Length == 0) throw new InvalidOperationException("请选择目标实例。");
            dialog.Close();
            return Task.CompletedTask;
        }, true);
        apply.IsEnabled = choices.Any(item => item.Box.IsEnabled);
        dialog.SetBody(panel, CloseDialog(dialog), apply);
        await dialog.ShowDialog(this);
        if (selected is not { Length: > 0 }) return;

        var involved = selected.Prepend(source).ToArray();
        foreach (var instance in involved) EnsureIdle(instance);
        foreach (var instance in involved) model.Busy.Add(instance.Id);
        maintenance++;
        var job = new SyncJob($"整合包同步 · {source.Name}");
        syncJobs.Insert(0, job);
        var selectedFolders = new SyncSelection(options[0].IsChecked == true, options[1].IsChecked == true,
            options[2].IsChecked == true, options[3].IsChecked == true, options[4].IsChecked == true);
        try
        {
            var updates = new Progress<SyncProgress>(update =>
            {
                if (job.Tab != 0) return;
                job.Update(update.Percent, update.Message);
                status.Text = update.Message;
            });
            var result = await Task.Run(() => InstanceSynchronization.Run(model.Store, source, selected, selectedFolders, updates));
            job.Tab = 1;
            job.Update(100, result.FilesAdded == 0 ? "所有选中的文件已一致" : $"同步完成 · 已添加 {result.FilesAdded} 个文件");
            status.Text = job.Message;
        }
        catch (Exception ex)
        {
            job.Tab = 2;
            job.Update(job.Percent, "同步失败 · " + new XG.Core.GameLogSanitizer(null).Redact(ex.Message));
            status.Text = "整合包同步失败，详见下载管理。";
        }
        finally
        {
            maintenance--;
            foreach (var instance in involved) model.Busy.Remove(instance.Id);
            if (section == 4) Refresh();
        }
    }

    private void AddSyncTasks(StackPanel root)
    {
        foreach (var job in syncJobs.Where(job => job.Tab == downloadTab))
        {
            var text = Label(job.Message);
            var progress = new ProgressBar { Maximum = 100, Value = job.Percent };
            job.StatusView = text;
            job.ProgressView = progress;
            root.Children.Add(Frame(Column(Label(job.Title, 16, true), text, progress), new(10)));
            progress.DetachedFromVisualTree += (_, _) =>
            {
                if (!ReferenceEquals(job.ProgressView, progress)) return;
                job.ProgressView = null;
                job.StatusView = null;
            };
        }
    }
}
