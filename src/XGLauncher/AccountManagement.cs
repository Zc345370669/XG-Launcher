// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using XG.Core;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private async Task AccountsAsync()
    {
        var dialog = Dialog("账号管理", editor: true);
        using var lifetime = new CancellationTokenSource();
        CancellationTokenSource? pending = null;
        var closed = false;
        var player = Input("离线玩家名", model.Player);
        var feedback = Label("", 12); feedback.Foreground = ThemePalette.Muted;
        var entries = new ItemsControl { Name = "FullAccountsList", Margin = new(0, 0, 18, 0) };
        var empty = Label("暂无账号，请在上方添加", 12);
        var listScroll = new ScrollViewer { Name = "ManagedAccountsScroll", Content = entries,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, MaxHeight = 114 };
        var panel = Column(); panel.Spacing = 8;
        Button offline = null!, browser = null!, cancel = null!;
        WrapPanel diagnosticActions = null!;
        offline = Action("添加离线账号", async () =>
        {
            try
            {
                var name = Instance.ValidatePlayer(player.Text ?? "");
                var exists = model.Accounts.Profiles.Any(x => x.Kind == "Offline" && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                await model.Accounts.AddOfflineAsync(name); model.Player = name;
                feedback.Text = exists ? "已选择已有离线账号。" : "已添加并选择离线账号。";
            }
            catch (Exception ex) { feedback.Text = new GameLogSanitizer(null).Redact(ex.Message); }
            Sync();
        });
        offline.Name = "AddOfflineAccount"; offline.IsDefault = true;
        browser = Action("微软正版登录", () => AuthenticateAsync(null));
        browser.IsEnabled = MicrosoftAccounts.ClientId != null;
        cancel = Action("取消登录", () => { pending?.Cancel(); return Task.CompletedTask; }); cancel.IsVisible = false;
        var copy = Action("复制诊断信息", async () =>
        {
            if (model.Accounts.LastDiagnostic is not { } detail || dialog.Clipboard == null) return;
            await dialog.Clipboard.SetTextAsync(detail); feedback.Text = "已复制脱敏诊断。";
        });
        var details = Action("查看详细错误", async () =>
        {
            if (model.Accounts.LastDiagnostic is not { } detail) return;
            var viewer = Dialog("脱敏认证诊断", dialog, editor: true);
            viewer.SetBody(new TextBox { Text = detail, IsReadOnly = true, AcceptsReturn = true,
                Height = 240, TextWrapping = TextWrapping.Wrap });
            await viewer.ShowDialog(dialog);
        });
        diagnosticActions = new WrapPanel { ItemSpacing = 8, Children = { copy, details } };
        entries.ItemTemplate = new FuncDataTemplate<LauncherAccount>((profile, _) =>
        {
            if (profile == null) return null;
            var select = Action("", async () =>
            {
                if (pending != null || model.Accounts.SelectedProfile?.Id == profile.Id) return;
                if (profile.Kind == "Microsoft") await AuthenticateAsync(profile);
                else
                {
                    try { await model.Accounts.SelectProfileAsync(profile, lifetime.Token); }
                    catch (Exception ex) { feedback.Text = MicrosoftAccounts.Describe(ex); }
                    Sync();
                }
            });
            select.Content = Row(new LucideIcon("check", 15) { IsVisible = profile.Id == model.Accounts.SelectedProfile?.Id }, Label(profile.Caption));
            select.Background = profile.Id == model.Accounts.SelectedProfile?.Id ? ThemePalette.Hover : Brushes.Transparent;
            select.BorderThickness = new(0); select.Padding = new(6, 5); select.MinHeight = 36;
            select.HorizontalAlignment = HorizontalAlignment.Stretch;
            Avalonia.Automation.AutomationProperties.SetName(select, "选择账号 " + profile.Name);
            var remove = Action("", async () =>
            {
                if (pending != null || !await ConfirmAccountRemovalAsync(profile, dialog)) return;
                try { await model.Accounts.RemoveProfileAsync(profile, lifetime.Token); }
                catch (Exception ex) { feedback.Text = MicrosoftAccounts.Describe(ex); }
                Sync();
            });
            remove.Content = new LucideIcon("trash-2", 16); remove.Width = remove.Height = 32; remove.Padding = new(0);
            remove.Background = Brushes.Transparent; remove.BorderThickness = new(0);
            Avalonia.Automation.AutomationProperties.SetName(remove, "删除账号 " + profile.Name);
            ToolTip.SetTip(remove, "删除账号");
            var row = new Grid { ColumnDefinitions = new("*,32"), ColumnSpacing = 4 };
            row.Children.Add(select); Grid.SetColumn(remove, 1); row.Children.Add(remove);
            return row;
        });
        panel.Children.Add(Column(Label("离线玩家名称", 13, true), player, offline));
        panel.Children.Add(Column(Label("Microsoft 正版账号", 13, true), browser,
            Label("登录将在微软官方浏览器页面完成。", 12)));
        panel.Children.Add(feedback); panel.Children.Add(cancel);
        panel.Children.Add(Column(Label("已有账号", 13, true), empty, listScroll));
        panel.Children.Add(diagnosticActions);
        foreach (var section in panel.Children.OfType<StackPanel>()) section.Spacing = 6;
        offline.Height = browser.Height = 36;
        dialog.SetBody(panel);
        dialog.Closed += (_, _) =>
        {
            closed = true; model.Accounts.Changed -= Sync; pending?.Cancel(); lifetime.Cancel();
        };
        model.Accounts.Changed += Sync;
        dialog.Opened += (_, _) => Sync();
        Sync();
        await dialog.ShowDialog(this);
        AccountsChanged();

        void Sync()
        {
            if (closed) return;
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Sync); return; }
            feedback.IsVisible = !string.IsNullOrWhiteSpace(feedback.Text);
            entries.ItemsSource = model.Accounts.Profiles.OrderByDescending(x => x.Id == model.Accounts.SelectedProfile?.Id).ToArray();
            empty.IsVisible = model.Accounts.Profiles.Count == 0;
            listScroll.IsVisible = !empty.IsVisible;
            listScroll.Height = Math.Min(114, model.Accounts.Profiles.Count * 38 + 2);
            diagnosticActions.IsVisible = model.Accounts.LastDiagnostic != null;
            offline.IsEnabled = entries.IsEnabled = pending == null;
            browser.IsEnabled = pending == null && MicrosoftAccounts.ClientId != null;
            dialog.FitContent(panel);
        }
        async Task AuthenticateAsync(LauncherAccount? profile)
        {
            if (pending != null) return;
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            pending = attempt; cancel.IsVisible = true;
            feedback.Text = profile == null ? "请在浏览器完成微软登录和授权。关闭此窗口可取消。" : "正在验证已保存的微软账号……";
            Sync();
            try
            {
                if (profile == null) await model.Accounts.SignInAsync(null, attempt.Token, new AuthDiagnosticTrace());
                else await model.Accounts.SelectProfileAsync(profile, attempt.Token);
                if (!closed)
                {
                    feedback.Text = "微软账号已验证并选择。";
                }
            }
            catch (OperationCanceledException) when (attempt.IsCancellationRequested) { if (!closed) feedback.Text = "已取消登录。"; }
            catch (Exception ex) { if (!closed) feedback.Text = MicrosoftAccounts.Describe(ex); }
            finally { pending = null; cancel.IsVisible = false; Sync(); }
        }
    }
}
