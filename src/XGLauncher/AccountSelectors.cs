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
        account.Content = model.Accounts.SelectedProfile?.Name ?? "添加账号";
        UpdateCompact();
    }

    /// <summary>
    /// 为两种模式提供共享账号选择、确认删除和内嵌添加入口，选择不复制账号状态。
    /// </summary>
    private UnifiedExpandableSelect CreateAccountSelector(string name, Func<LauncherAccount, Task>? selectProfile = null)
    {
        var syncing = false;
        UnifiedExpandableSelect picker = null!;
        picker = new UnifiedExpandableSelect
        {
            Name = name, PreferAbove = name == "CompactAccounts", PlaceholderText = "暂无账号",
            AuxiliaryIcon = name == "CompactAccounts" ? "user-round-plus" : null,
            ExpansionFactory = name == "CompactAccounts" ? () => CompactAccountForm(picker) : null, MaximumOpenHeight = 240,
            DisplayMember = item => ((LauncherAccount)item).Caption,
            RemoveItemAsync = async item =>
            {
                try
                {
                    var profile = (LauncherAccount)item;
                    if (await ConfirmAsync("删除账号「" + profile.Name + "」？", TopLevel.GetTopLevel(picker) as Window))
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
            try { if (selectProfile != null) await selectProfile(selected); else await model.Accounts.SelectProfileAsync(selected, catalogLifetime.Token); }
            catch (Exception ex) { await ErrorAsync(picker, MicrosoftAccounts.Describe(ex)); }
            finally { picker.IsEnabled = true; Sync(); AccountsChanged(); }
        };
        picker.AttachedToVisualTree += (_, _) => { model.Accounts.Changed += Sync; Sync(); };
        picker.DetachedFromVisualTree += (_, _) => model.Accounts.Changed -= Sync;
        Sync(); return picker;
    }
}
