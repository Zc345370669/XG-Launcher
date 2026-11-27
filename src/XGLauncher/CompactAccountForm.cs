// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using XG.Core;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private bool compactAuthenticationPending;
    private string compactAuthenticationFeedback = "";
    private event Action? CompactAuthenticationChanged;
    /// <summary>
    /// 在账号组合框内部添加离线账号或发起正规Microsoft登录，复用全局账号服务。
    /// </summary>
    private Control CompactAccountForm(UnifiedExpandableSelect picker)
    {
        var name = Input("离线玩家名字", ""); name.Name = "CompactOfflineName";
        var error = Label("", 11); error.Name = "CompactAccountError"; error.IsVisible = false;
        var adding = false;
        async Task Add()
        {
            if (adding || compactAuthenticationPending) return;
            adding = true;
            try
            {
                var valid = Instance.ValidatePlayer(name.Text ?? "");
                await model.Accounts.AddOfflineAsync(valid); model.Player = valid;
                compactAuthenticationFeedback = ""; AccountsChanged(); picker.Close();
            }
            catch (Exception ex) { error.Text = new GameLogSanitizer(null).Redact(ex.Message); error.IsVisible = true; }
            finally { adding = false; }
        }
        var offline = Action("添加离线账号", Add);
        offline.Name = "CompactAddOffline";
        offline.HorizontalAlignment = HorizontalAlignment.Stretch;
        var offlineEditor = Column(name, offline);
        offlineEditor.Spacing = 6;
        offlineEditor.Margin = new(10, 2, 10, 6);
        offlineEditor.IsVisible = false;
        var offlineChoice = AccountOption("离线账号", "user-round", () =>
        {
            offlineEditor.IsVisible = !offlineEditor.IsVisible;
            if (offlineEditor.IsVisible) name.Focus();
            return Task.CompletedTask;
        });
        offlineChoice.Name = "CompactOfflineOption";
        var microsoft = AccountOption("Microsoft 正版登录", "building", async () =>
        {
            if (compactAuthenticationPending) return;
            compactAuthenticationPending = true;
            compactAuthenticationFeedback = "请在浏览器完成微软登录和授权。";
            CompactAuthenticationChanged?.Invoke();
            try
            {
                await model.Accounts.SignInAsync(null, catalogLifetime.Token, new AuthDiagnosticTrace());
                compactAuthenticationFeedback = ""; AccountsChanged(); picker.Close();
            }
            catch (OperationCanceledException) { compactAuthenticationFeedback = "已取消登录。"; }
            catch (Exception ex) { compactAuthenticationFeedback = MicrosoftAccounts.Describe(ex); }
            finally { compactAuthenticationPending = false; CompactAuthenticationChanged?.Invoke(); }
        });
        microsoft.Name = "CompactMicrosoftLogin"; microsoft.IsEnabled = MicrosoftAccounts.ClientId != null && !compactAuthenticationPending;
        offline.IsEnabled = !compactAuthenticationPending;
        name.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await Add(); } };
        error.Margin = new(10, 2, 10, 6);
        var form = Column(offlineChoice, offlineEditor, microsoft, error);
        form.Spacing = 0;
        form.Margin = new(3);
        void SyncAuthentication()
        {
            offline.IsEnabled = offlineChoice.IsEnabled = !compactAuthenticationPending;
            microsoft.IsEnabled = MicrosoftAccounts.ClientId != null && !compactAuthenticationPending;
            error.Text = compactAuthenticationFeedback; error.IsVisible = !string.IsNullOrEmpty(error.Text);
        }
        form.AttachedToVisualTree += (_, _) => { CompactAuthenticationChanged += SyncAuthentication; SyncAuthentication(); };
        form.DetachedFromVisualTree += (_, _) => CompactAuthenticationChanged -= SyncAuthentication;
        return form;
    }

    private Button AccountOption(string text, string icon, Func<Task> execute)
    {
        var button = Action(text, execute);
        var label = Label(text);
        label.TextWrapping = TextWrapping.NoWrap;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        var content = new Grid { ColumnDefinitions = new("17,*"), ColumnSpacing = 8 };
        content.Children.Add(new LucideIcon(icon, 17) { VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(label, 1);
        content.Children.Add(label);
        button.Content = content;
        button.MinHeight = 38;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Background = Brushes.Transparent;
        button.BorderThickness = new(0);
        button.Padding = new(10, 0);
        return button;
    }
}
