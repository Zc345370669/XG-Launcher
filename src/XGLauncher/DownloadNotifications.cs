// SPDX-License-Identifier: MIT
using Avalonia.Controls;
using Avalonia.Threading;
using XG.Core;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private TextBlock? downloadBadge;
    private readonly HashSet<Guid> notifiedInstallations = [];
    private bool notificationsEnabled = true;
    private int UnfinishedDownloads => model.Resources.UnfinishedCount + model.Downloads.Items.Count(x => x.State is not ("已完成" or "已取消"));
    private void UpdateBadge()
    {
        if (downloadBadge == null) return;
        var count = UnfinishedDownloads;
        downloadBadge.Text = count > 99 ? "99+" : count.ToString();
        downloadBadge.IsVisible = count > 0;
    }
    private void ResourceJobsChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(ResourceJobsChanged); return; }
        if (windowClosed) return;
        UpdateCompact();
        UpdateBadge();
        foreach (var job in model.Resources.MainJobs.Where(x => x.State == "已完成" && x.Recovery?.Type == "game"))
        {
            if (!notifiedInstallations.Add(job.Id)) continue;
            installationStates.Remove(job.Recovery!.InstanceId);
            if (notificationsEnabled && model.Store.Entries.FirstOrDefault(x => x.Id == job.Recovery.InstanceId) is { } instance)
            {
                var message = $"{instance.Name} · Minecraft {instance.Version} 已安装完成，可以启动游戏。";
                status.Text = message;
                ShowCompletion(message);
            }
        }
        if (IsCompact || section is 0 or 1 or 2 or 4) Refresh();
    }
    private void ShowCompletion(string message)
    {
        if (completionToast == null || IsCompact) return;
        completionToast.Content = new Border { Background = ThemePalette.Accent, CornerRadius = new(9), Padding = new(14, 10), MaxWidth = 500,
            Child = new TextBlock { Text = message, Foreground = ThemePalette.OnAccent, TextWrapping = Avalonia.Media.TextWrapping.Wrap } };
        completionToast.IsVisible = true;
        toastTimer.Stop(); toastTimer.Start();
    }
    private ContentControl? completionToast;
    private readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(6) };
}
