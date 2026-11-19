// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
        Button offline = null!, microsoft = null!;
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
        offline = Action("添加离线账号", Add); offline.Name = "CompactAddOffline";
        microsoft = Action("Microsoft 正版登录", async () =>
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
        var form = Column(Label("离线玩家名字", 12, true), name, offline, microsoft, error);
        form.Spacing = 8; form.Margin = new(12, 16, 18, 16);
        void SyncAuthentication()
        {
            offline.IsEnabled = !compactAuthenticationPending;
            microsoft.IsEnabled = MicrosoftAccounts.ClientId != null && !compactAuthenticationPending;
            error.Text = compactAuthenticationFeedback; error.IsVisible = !string.IsNullOrEmpty(error.Text);
        }
        form.AttachedToVisualTree += (_, _) => { CompactAuthenticationChanged += SyncAuthentication; SyncAuthentication(); };
        form.DetachedFromVisualTree += (_, _) => CompactAuthenticationChanged -= SyncAuthentication;
        return form;
    }
}
