// SPDX-License-Identifier: MIT
using Avalonia.Controls;
using XG.Core;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private bool preparingExit;
    private bool exitReady;

    private async void WindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (exitReady) return;
        if (preparingExit) { e.Cancel = true; return; }
        if (minimizeToTray && !exitRequested && tray != null)
        {
            e.Cancel = true; Hide(); return;
        }
        var activeResources = model.Resources.MainJobs.Where(x => model.Resources.CanControl(x.Id)).ToArray();
        var installing = activeResources.Where(x => x.Recovery != null).Select(x => x.Recovery!.InstanceId).ToHashSet();
        if (model.Busy.Any(id => !installing.Contains(id)) || maintenance != 0 && activeResources.Length == 0)
        {
            e.Cancel = true;
            status.Text = "请先关闭正在运行的游戏，或等待文件操作结束。";
            compactStatus.Text = status.Text;
            pendingUpdatePlan = null;
            exitRequested = false;
            return;
        }
        var transferring = activeResources.Length > 0 || model.Downloads.Items.Any(x => x.State is "等待中" or "连接中" or "下载中" or "校验中" or "安装中");
        if (!transferring) return;
        e.Cancel = true;
        preparingExit = true;
        try
        {
            if (!await ConfirmAsync("保存下载进度并退出？下次打开可从下载管理恢复任务。"))
            { pendingUpdatePlan = null; exitRequested = false; return; }
            IsEnabled = false;
            status.Text = "正在保存下载任务，等待当前安装步骤安全结束…";
            compactStatus.Text = status.Text;
            // 先标记恢复状态，再中断传输；暂停任务也会解除等待并释放 Busy，而不是永久拦截关闭。
            var resources = model.Resources.SuspendForExitAsync();
            var downloads = model.Downloads.PauseForExitAsync();
            await Task.WhenAll(resources, downloads);
            // 任务返回后调用方仍需执行 finally，归还实例占用和恢复/安装入口的维护计数。
            for (var i = 0; i < 100 && (model.Busy.Count != 0 || maintenance != 0); i++) await Task.Delay(10);
            if (model.Busy.Count != 0 || maintenance != 0)
                throw new InvalidOperationException("请等待文件操作结束后重试关闭。");
            exitReady = true;
            exitRequested = true;
            Close();
        }
        catch (Exception error)
        {
            var message = new GameLogSanitizer(null).Redact(error.Message);
            status.Text = message; compactStatus.Text = message;
            pendingUpdatePlan = null;
            exitRequested = false;
            IsEnabled = true;
            if (!windowClosed) await ErrorAsync(this, message);
        }
        finally { preparingExit = false; if (!windowClosed) IsEnabled = true; }
    }
}
