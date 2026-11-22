// SPDX-License-Identifier: MIT
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using XG.Core;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private SettingsSurface? settingsPage;

    private Control Settings()
    {
        settingsPage ??= CreateSettings();
        return settingsPage;
    }

    private SettingsSurface CreateSettings()
    {
        string[] names = ["常规", "界面", "通知与托盘", "备份与恢复", "隐私与安全", "许可证与鸣谢", "关于"];
        var general = Column(SettingRow("数据目录", "打开目录", () => OpenFolder(model.Store.Root), model.Store.Root));
        general.Children.Add(SettingRow("游戏根目录", "选择目录", ChooseGameDirectoryAsync, model.Store.SharedMinecraft ?? model.Store.InstanceRoot));
        general.Children.Add(Action("检查并迁移旧游戏目录", PreviewLegacyGameMigrationAsync));
        general.Children.Add(Action("迁移游戏数据",MigrateWorkspaceAsync));
        var legacyRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XGLauncher", "data");
        var appearance = AppearanceSettings();
        var notify = new CheckBox { Content = "下载完成提示", IsChecked = notificationsEnabled };
        notify.IsCheckedChanged += (_, _) => {
            notificationsEnabled = notify.IsChecked == true;
            InstanceStore.WriteJson(Path.Combine(model.Store.Root, "download-notifications.json"), notificationsEnabled);
        };
        var notifications = Column(notify);
        if (tray != null)
        {
            var trayOption = new CheckBox { IsChecked = minimizeToTray };
            Avalonia.Automation.AutomationProperties.SetName(trayOption, "关闭窗口时缩到系统托盘");
            trayOption.IsCheckedChanged += (_, _) =>
            {
                var enabled = trayOption.IsChecked == true;
                try
                {
                    InstanceStore.WriteJson(Path.Combine(model.Store.Root, "tray.json"), enabled);
                    minimizeToTray = enabled;
                    tray.IsVisible = enabled;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    status.Text = "无法保存托盘设置：" + new XG.Core.GameLogSanitizer(null).Redact(ex.Message);
                    trayOption.IsChecked = minimizeToTray;
                }
            };
            notifications.Children.Add(SettingRow("关闭时缩到托盘", trayOption));
        }
        else notifications.Children.Add(SettingRow("系统托盘", Label("仅 Windows")));

        var backup = Column(SettingRow("全局备份管理", "打开备份目录", () => OpenFolder(model.BackupsDirectory)), SettingRow("从 ZIP 恢复到新实例", "恢复备份", RestoreBackupAsync));
        if (File.Exists(Path.Combine(legacyRoot, "instances.json")) && !legacyRoot.Equals(model.Store.Root, StringComparison.OrdinalIgnoreCase))
            backup.Children.Add(SettingRow("旧版实例", "备份并复制", () => ImportLegacyAsync(legacyRoot)));
        var security = Column(Label("账号凭据由系统支持的安全缓存管理。账号退出和缓存清除请进入顶部账号管理。"),
            Label("游戏及模组资源使用官方渠道；下载文件会校验后再安装。用户数据保留于本地工作目录。"));
        var legal = Column(new LegalBrowser(this), Action("导出完整许可与对应源码", ExportComplianceAsync));
        var about = AboutPage();
        var sections = new Control[] { general, appearance, notifications, backup, security, legal, about }
            .Select((item, index) => (Control)new Border
            {
                Name = $"SettingsSection{index}", Child = item, Background = Brushes.Transparent, MinHeight = 120, Padding = new(19, 14),
                BorderBrush = Line, BorderThickness = new(0, 0, 0, index == 6 ? 0 : 1)
            }).ToArray();
        return new SettingsSurface(names, sections);
    }

    private async Task RestoreBackupAsync()
    {
            var files = await StorageProvider.OpenFilePickerAsync(new() { FileTypeFilter = [new("ZIP") { Patterns = ["*.zip"] }] });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } local) return;
            maintenance++;
            try { model.Selected = await Backups.RestoreAsync(model.Store, local); }
            finally { maintenance--; }
            status.Text = "备份恢复完成"; Refresh();
    }

    private Task ImportLegacyAsync(string legacyRoot) => PreviewLegacyGameMigrationFromAsync(Path.GetDirectoryName(legacyRoot)!);

    private Instance SelectedForSettings() => model.Selected ?? throw new InvalidOperationException("请先选择游戏实例。");
    private Task EditCurrentAsync() => EditAsync(SelectedForSettings());

    private Control SettingRow(string title, string action, Func<Task> command, string? value = null,
        TextBlock? valueControl = null)
        => SettingRow(title, Action(action, command), value ?? valueControl?.Text, valueControl);

    private static Control SettingRow(string title, Control right, string? value = null, TextBlock? valueControl = null)
    {
        var labels = Column(Label(title, 14, true));
        labels.Spacing = 3;
        if (valueControl != null) labels.Children.Add(valueControl);
        else if (!string.IsNullOrWhiteSpace(value)) labels.Children.Add(Label(value));
        var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10, MinHeight = 35 };
        right.VerticalAlignment = VerticalAlignment.Center;
        if (right is TextBlock) right.MaxWidth = 155;
        row.Children.Add(labels);
        Grid.SetColumn(right, 1); row.Children.Add(right);
        row.SizeChanged += (_, _) =>
        {
            var stacked = row.Bounds.Width < 340;
            if ((row.RowDefinitions.Count == 2) == stacked) return;
            row.ColumnDefinitions = new(stacked ? "*" : "*,Auto");
            row.RowDefinitions = new(stacked ? "Auto,Auto" : "Auto");
            row.RowSpacing = stacked ? 7 : 0;
            row.ColumnSpacing = stacked ? 0 : 10;
            Grid.SetColumn(right, stacked ? 0 : 1); Grid.SetRow(right, stacked ? 1 : 0);
            right.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        };
        return row;
    }

    private static Task OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return Task.CompletedTask;
    }
}
