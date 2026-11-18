// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using XG.Core;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private readonly Dictionary<Guid, string> instanceStatuses = [];

    private readonly Dictionary<Guid, string> installationStates = [];
    private readonly HashSet<Guid> inspecting = [];
    private string InstanceState(Instance instance)
    {
        if (instanceStatuses.GetValueOrDefault(instance.Id) == "游戏运行中") return "游戏运行中";
        if (model.InstallationJob(instance) is { } job) return job.State;
        if (!installationStates.TryGetValue(instance.Id, out var state))
        {
            if (inspecting.Add(instance.Id)) _ = InspectInstanceAsync(instance);
            return "校验中";
        }
        return state;
    }
    private async Task InspectInstanceAsync(Instance instance)
    {
        var state = await Task.Run(() => model.Game.InspectInstallationAsync(instance));
        installationStates[instance.Id] = state;
        inspecting.Remove(instance.Id);
        if (section is 0 or 1 or 2) Refresh();
    }
    private string InstanceButton(Instance instance)
    {
        if (model.InstallationJob(instance) is { State: "下载中", TotalBytes: > 0 } task)
            return $"下载中 {Math.Clamp(100.0 * task.Bytes / task.TotalBytes, 0, 100):0}%";
        if (model.InstallationJob(instance) is { State: "失败", CurrentStage: "模组加载器" }) return "修复安装";
        return InstanceState(instance) switch
    {
        "已安装" => "启动", "游戏运行中" => "运行中", "未安装" => "下载", "文件损坏" => "修复",
        "失败" => "重试下载", "已暂停" or "等待恢复" => "继续下载", "等待中" => "排队中",
        "下载中" => "下载中", "准备中" => "准备中", "校验中" => "校验中", _ => "安装中"
        };
    }
    private async Task InstancePrimaryAsync(Instance instance)
    {
        if (model.InstallationJob(instance) is { } job && model.Resources.CanControl(job.Id))
        {
            focusedDownload = job.Id; section = 4; downloadTab = 0; Refresh(); return;
        }
        var actual = model.InstallationJob(instance) == null
            ? await model.Game.InspectInstallationAsync(instance) : "未安装";
        if (actual == "已安装") { await LaunchInstanceAsync(instance); return; }

        section = 4; downloadTab = 0; Refresh();
        try { await model.InstallInstanceAsync(instance, text => Dispatcher.UIThread.Post(() => status.Text = text)); }
        catch (OperationCanceledException) { status.Text = "下载已取消。"; }
        finally { installationStates.Remove(instance.Id); Refresh(); }
    }

    private async Task LaunchInstanceAsync(Instance instance)
    {
        if (model.Busy.Contains(instance.Id)) return;
        model.Selected = instance;
        SetStatus("正在检查和准备游戏");
        try
        {
            await model.LaunchAsync(instance, message => Dispatcher.UIThread.Post(() => SetStatus(message)));
        }
        catch (OperationCanceledException) { SetStatus("准备已取消，可重新启动以继续"); }
        catch
        {
            SetStatus("启动失败 · 可重新启动以修复资源，或在编辑实例中检查 Java");
            throw;
        }

        void SetStatus(string message)
        {
            status.Text = message;
            var important = message.StartsWith("游戏", StringComparison.Ordinal) ||
                message.StartsWith("准备", StringComparison.Ordinal) ||
                message.StartsWith("启动失败", StringComparison.Ordinal) ||
                message == "正在检查和准备游戏";
            if (!important || instanceStatuses.GetValueOrDefault(instance.Id) == message) return;
            instanceStatuses[instance.Id] = message;
            if (section is 0 or 1 or 2) Refresh();
        }
    }

    private ContextMenu InstanceMenu(Instance instance)
    {
        var menu = new ContextMenu { Background = ThemePalette.Panel, Foreground = ThemePalette.Text, BorderBrush = ThemePalette.Line };
        Add("启动游戏", () => InstancePrimaryAsync(instance));
        Add("编辑实例", () => EditAsync(instance));
        Add("设为安装目标", () => { model.Selected = instance; Refresh(); return Task.CompletedTask; });
        Add("打开游戏文件夹", () => FolderAsync(null));
        Add("打开 mods 文件夹", () => FolderAsync("mods"));
        Add("打开 saves 文件夹", () => FolderAsync("saves"));
        Add("打开资源包文件夹", () => FolderAsync("resourcepacks"));
        Add("打开光影包文件夹", () => FolderAsync("shaderpacks"));
        Add("查看游戏日志", () =>
        {
            var path = Path.Combine(model.Store.EnsureLocalDirectory(instance), "xg-launch.log");
            if (!File.Exists(path)) throw new FileNotFoundException("该实例尚未产生启动日志。");
            return OpenFolder(path);
        });
        Add("检查及更新官方 Java", async () =>
        {
            EnsureIdle(instance);
            await model.CheckOfficialJavaAsync(instance, message => Avalonia.Threading.Dispatcher.UIThread.Post(() => status.Text = message));
            status.Text = "官方 Java 检查完成。";
        });
        Add("查看加载器日志", () =>
        {
            var path = Path.Combine(model.Store.EnsureLocalDirectory(instance), "xg-loader.log");
            if (!File.Exists(path)) throw new FileNotFoundException("此实例还没有加载器日志。");
            return OpenFolder(path);
        });
        Add("打开日志文件夹", () => FolderAsync("logs"));
        Add("打开崩溃报告目录", () => FolderAsync("crash-reports"));
        Add("复制错误摘要", async () =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("剪贴板不可用。");
            var summary = instanceStatuses.GetValueOrDefault(instance.Id, "当前会话暂无退出状态。");
            await clipboard.SetTextAsync($"Minecraft {instance.Version} · {instance.Loader} {instance.LoaderVersion}\n{summary}");
        });
        Add("复制实例", async () =>
        {
            EnsureIdle(instance);
            model.Busy.Add(instance.Id);
            try { model.Selected = await model.Store.CopyAsync(instance); }
            finally { model.Busy.Remove(instance.Id); }
            Refresh();
        });
        Add("备份实例", async () =>
        {
            EnsureIdle(instance);
            var path = Path.Combine(model.BackupsDirectory, instance.Id.ToString("N") + "-" +
                DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".zip");
            model.Busy.Add(instance.Id);
            try { await Task.Run(() => Backups.Create(model.Store, instance, path)); }
            finally { model.Busy.Remove(instance.Id); }
            status.Text = "已备份到 " + path;
            await OpenFolder(model.BackupsDirectory);
        });
        Add("从 ZIP 恢复为新实例", RestoreBackupAsync);
        Add("删除实例", () => DeleteInstanceAsync(instance));
        return menu;

        Task FolderAsync(string? child)
        {
            var root = model.Store.EnsureLocalDirectory(instance);
            var path = child is null ? root : InstanceStore.SafeFile(root, child);
            Directory.CreateDirectory(path);
            if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("该文件夹是符号链接或目录联接。");
            return OpenFolder(path);
        }

        void Add(string title, Func<Task> run)
        {
            var enabled = title switch {
                "启动游戏" => InstanceState(instance) == "已安装",
                "查看游戏日志" => File.Exists(Path.Combine(model.Store.DirectoryFor(instance), "xg-launch.log")),
                "查看加载器日志" => File.Exists(Path.Combine(model.Store.DirectoryFor(instance), "xg-loader.log")),
                _ => true
            };
            var item = new MenuItem { Header = title, IsEnabled = enabled, Icon = new LucideIcon(LucideIcon.Operation(title),16) };
            item.Click += async (_, _) =>
            {
                try { await run(); }
                catch (Exception error) { status.Text = new XG.Core.GameLogSanitizer(null).Redact(error.Message); await ErrorAsync(this, new XG.Core.GameLogSanitizer(null).Redact(error.Message)); }
            };
            menu.Items.Add(item);
        }
    }

    private async Task DeleteInstanceAsync(Instance instance)
    {
        EnsureIdle(instance);
        var dialog = Dialog("删除实例");
        int? choice = null;
        Button Choose(string title, int value) => Action(title, () =>
        { choice = value; dialog.Close(); return Task.CompletedTask; });
        dialog.SetBody(Label($"删除「{instance.Name}」？可保留现有文件，也可同时删除实例目录。"),
            CloseDialog(dialog), Choose("仅移除记录", 0), Choose("同时删除实例文件", 1));
        await dialog.ShowDialog(this);
        if (choice is not (0 or 1) || !await ConfirmAsync(choice == 1
            ? "再次确认永久删除此实例的存档、配置和其他文件？" : "再次确认从启动器移除此实例记录？")) return;
        EnsureIdle(instance);
        var path = model.Store.DirectoryFor(instance);
        if (model.Downloads.Items.Any(job => (job.State is "等待中" or "连接中" or "下载中" or "校验中" or "安装中") &&
            Path.GetRelativePath(path, job.Target).Split(Path.DirectorySeparatorChar)[0] != ".."))
            throw new InvalidOperationException("请先停止此实例的下载。");
        model.Store.Remove(instance, choice == 1);
        if (model.Selected?.Id == instance.Id) model.Selected = model.Store.Entries.FirstOrDefault();
        Refresh();
    }
}
