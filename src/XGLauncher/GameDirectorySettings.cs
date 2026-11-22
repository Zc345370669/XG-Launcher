// SPDX-License-Identifier: MIT
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using XG.Core;
namespace XGLauncher;

public sealed partial class MainWindow
{
    private async Task ScanLocalInstancesAsync()
    {
        var found = await Task.Run(() => GameInstanceScan.Scan(model.Store, catalogLifetime.Token), catalogLifetime.Token);
        if (windowClosed) return;
        foreach (var candidate in found) model.Store.AdoptDirectory(candidate.Instance, candidate.DirectoryName);
        if (model.Selected == null) model.Selected = model.Store.Entries.FirstOrDefault();
        Refresh();
        if (model.PortableSession)
        {
            var legacy = await Task.Run(() => LegacyGameMigration.Preview(Path.GetDirectoryName(model.Store.Root)!, model.Store), catalogLifetime.Token);
            if (!windowClosed && legacy.Instances.Length > 0)
            {
                var notice = Dialog("发现旧游戏数据");
                notice.SetBody(Label($"发现 {legacy.Instances.Length} 个待迁移实例。请先检查、备份再复制；原目录不会自动删除。"), CloseDialog(notice, "稍后"),
                    Action("检查迁移", async () => { notice.Close(); await PreviewLegacyGameMigrationAsync(); }));
                await notice.ShowDialog(this);
            }
        }
    }
    private async Task ChooseGameDirectoryAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "选择游戏根目录（其下使用 versions、libraries、assets）", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        if (!await ConfirmAsync("切换游戏目录将在下次启动生效，不移动或删除原文件。新目录中的有效版本将自动扫描。是否保存？")) return;
        GameRootPreference.Save(Path.GetDirectoryName(model.Store.Root)!, model.Store.Root, path);
        status.Text = "游戏目录已保存，下次启动生效。";
    }
    private Task PreviewLegacyGameMigrationAsync() => PreviewLegacyGameMigrationFromAsync(Path.GetDirectoryName(model.Store.Root)!);
    private async Task PreviewLegacyGameMigrationFromAsync(string workspace)
    {
        if (maintenance > 0 || model.Busy.Count > 0 || model.Resources.MainJobs.Any(x => x.State is not ("已完成" or "已取消" or "失败")) || model.Downloads.Items.Any(x => x.State is not ("已完成" or "已取消" or "失败")))
            throw new InvalidOperationException("请先结束游戏及下载、安装任务，再迁移旧目录。");
        var plan = await Task.Run(() => LegacyGameMigration.Preview(workspace, model.Store), catalogLifetime.Token);
        if (plan.Instances.Length == 0)
        {
            var notice = Dialog("旧数据检查"); notice.SetBody(Label("未发现待迁移的旧实例，或目标目录已存在。原始文件未修改。"), CloseDialog(notice, "知道了"));
            await notice.ShowDialog(this); return;
        }
        var names = string.Join("\n", plan.Instances.Select(x => $"{x.Instance.Name} · {x.Instance.Version} · {x.Instance.Loader}"));
        if (!await ConfirmAsync($"发现 {plan.Instances.Length} 个旧实例：\n{names}\n扫描范围 {plan.Files} 个文件，{plan.Bytes / 1048576d:0.0} MB。\n将先完整备份来源，再校验复制至 {model.Store.SharedMinecraft}。原始存档不会删除，冲突不会覆盖。是否继续？")) return;
        maintenance++;
        try
        {
            var backup = await Task.Run(() => LegacyGameMigration.ExecuteAsync(plan, model.Store, model.BackupsDirectory,
                new Progress<MigrationFile>(x => Avalonia.Threading.Dispatcher.UIThread.Post(() => status.Text = "正在校验复制：" + Path.GetFileName(x.RelativePath))), catalogLifetime.Token));
            status.Text = "旧目录已校验复制，备份保留。"; model.Selected ??= model.Store.Entries.FirstOrDefault(); Refresh();
        }
        finally { maintenance--; }
    }
}
