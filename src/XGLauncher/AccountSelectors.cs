// SPDX-License-Identifier: MIT
using Avalonia.Controls;
using Avalonia.Threading;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private void AccountsChanged()
    {
        if (windowClosed) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(AccountsChanged); return; }
        UpdateCompact();
    }

    /// <summary>
    /// 为两种模式提供共享账号选择、确认删除和内嵌添加入口，选择不复制账号状态。
    /// </summary>
    private Task<bool> ConfirmAccountRemovalAsync(LauncherAccount profile, Window? owner = null) =>
        ConfirmAsync("移除本地账号「" + profile.Caption + "」？\n将清除此账号的本地记录及对应认证缓存。其他账号、游戏和存档不受影响，不会删除 Microsoft 账户本身。", owner);

    private UnifiedExpandableSelect CreateAccountSelector()
    {
        var syncing = false;
        UnifiedExpandableSelect picker = null!;
        picker = new UnifiedExpandableSelect
        {
            Name = "CaptionAccounts", PlaceholderText = "未登录", RemoveIcon = "trash-2", AllowRemoveSelected = true,
            ExpansionFactory = () => CompactAccountForm(picker), MaximumOpenHeight = 280,
            FooterText = "添加账号", FooterAction = () => { picker.OpenAuxiliary(); return Task.CompletedTask; },
            IconOnly = true, HeaderIcon = "user-round", PopupMinimumWidth = 262, AutoContentWidth = true, HideEmptyMessage = true, LiteralItems = true,
            Height = CaptionButtonSize,
            DisplayMember = item => ((LauncherAccount)item).Name,
            ItemIcon = item => ((LauncherAccount)item).Kind == "Microsoft" ? "building" : "user-round",
            ItemIconLabel = item => ((LauncherAccount)item).Kind == "Microsoft" ? "Microsoft 正版账号" : "离线账号",
            RemoveItemAsync = async item =>
            {
                try
                {
                    var profile = (LauncherAccount)item;
                    if (await ConfirmAccountRemovalAsync(profile, TopLevel.GetTopLevel(picker) as Window))
                        await model.Accounts.RemoveProfileAsync(profile, catalogLifetime.Token);
                }
                catch (Exception ex) { await ErrorAsync(picker, MicrosoftAccounts.Describe(ex)); }
                finally { Sync(); AccountsChanged(); }
            }
        };
        void Sync()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Sync); return; }
            syncing = true;
            try { picker.ItemsSource = model.Accounts.Profiles.ToArray(); picker.SelectedItem = model.Accounts.SelectedProfile; }
            finally { syncing = false; }
        }
        picker.SelectionChanged += async (_, _) =>
        {
            if (syncing || picker.SelectedItem is not LauncherAccount selected || selected.Id == model.Accounts.SelectedProfile?.Id) return;
            picker.IsEnabled = false;
            try { await model.Accounts.SelectProfileAsync(selected, catalogLifetime.Token); }
            catch (Exception ex) { await ErrorAsync(picker, MicrosoftAccounts.Describe(ex)); }
            finally { picker.IsEnabled = true; Sync(); AccountsChanged(); }
        };
        picker.AttachedToVisualTree += (_, _) => { model.Accounts.Changed += Sync; Sync(); };
        picker.DetachedFromVisualTree += (_, _) => model.Accounts.Changed -= Sync;
        Sync(); return picker;
    }
}
