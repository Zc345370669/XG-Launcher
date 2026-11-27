// SPDX-License-Identifier: MIT
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XG.Core;
using XGLauncher;

internal static class CompactFinalUiTests
{
    internal static void Run(string root, string? shots)
    {
        root = Path.Combine(root, "compact-final");
        var legacy = Path.Combine(root, "legacy"); Directory.CreateDirectory(legacy);
        InstanceStore.WriteJson(Path.Combine(legacy, "selection.json"), new { Player = "Player" });
        Check(new MicrosoftAccounts(legacy).SelectedProfile?.Name == "Player", "phase17 offline player migration lost identity");
        var store = new InstanceStore(root);
        var installed = new Instance(Guid.NewGuid(), "Installed fixture", "1.21.1", "NeoForge");
        var pending = installed with { Id = Guid.NewGuid(), Name = "Pending fixture" };
        store.Save(installed); store.Save(pending);
        var directory = store.DirectoryFor(installed); Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "fixture.bin"); File.WriteAllText(file, "Receipt fixture only, not a game.");
        Task.Run(() => InstallationReceipt.WriteAsync(store, installed, [file], default, "1.21.1", file)).GetAwaiter().GetResult();
        var owner = new MainWindow(root) { FontFamily = new FontFamily("Noto Sans CJK SC") }; owner.Show(); Pump(); owner.ToggleMode(); Pump();
        var model = (LauncherModel)typeof(MainWindow).GetField("model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
        var versions = owner.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x => x.Name == "CompactInstances");
        Wait(() => versions.ItemsSource!.Cast<Instance>().Count() == 1);
        Check(versions.ItemsSource!.Cast<Instance>().Single().Id == installed.Id, "uninstalled entry masquerades as installed");
        Check(owner.GetVisualDescendants().OfType<Image>().Single(x => x.Name == "CompactOfficialLogo").Source != null, "official logo not loaded");
        var versionRow = owner.GetVisualDescendants().OfType<Grid>().Single(x => x.Name == "CompactVersionRow");
        Check(versionRow.Background == null, "outer card added to compact selectors");
        Check(owner.GetVisualDescendants().OfType<Grid>().Single(x => x.Name == "LauncherTitleBar").GetVisualDescendants().OfType<Button>().Count(x => x.IsEffectivelyVisible) == 3, "compact caption has extra buttons");
        var accountPicker = owner.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x => x.Name == "CaptionAccounts");
        Check(!model.Accounts.Profiles.Any() && accountPicker.SelectedItem == null, "fresh empty account list manufactured a default account");
        Add("Steve"); Add("Alex");
        Check(model.Accounts.Profiles.Count == 2 && model.Accounts.SelectedProfile?.Name == "Alex", "offline add not persisted/shared");
        try { model.Accounts.AddOfflineAsync("bad name").GetAwaiter().GetResult(); throw new Exception("invalid offline name accepted"); }
        catch (ArgumentException) { }
        Check(model.Accounts.Profiles.Count == 2, "invalid name mutated account list");
        Capture("compact-final-account-bar");
        accountPicker.Open(); Pump(); Capture("compact-final-account-list");
        Check(owner.GetVisualDescendants().OfType<Button>().Count(x => AutomationProperties.GetName(x)?.StartsWith("删除账号 ") == true) == 2, "not every account row exposes its own removal button");
        Check(!owner.GetVisualDescendants().OfType<ListBox>().SelectMany(x => x.ItemsSource?.Cast<object>() ?? []).OfType<string>().Any(x => x.Contains("添加")), "add action is an account list item");
        ClickMinus("Steve");
        var confirm = owner.OwnedWindows.OfType<LauncherDialog>().Single();
        Check(model.Accounts.SelectedProfile?.Name == "Alex", "minus click switched account");
        Capture("compact-final-delete-confirm", confirm);
        Invoke(confirm.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "取消")));
        Check(model.Accounts.Profiles.Count == 2 && model.Accounts.SelectedProfile?.Name == "Alex", "cancel changed account state");
        accountPicker.Open(); Pump();
        var steve = owner.GetVisualDescendants().OfType<TextBlock>().Single(x => x.IsEffectivelyVisible && x.Text == "Steve");
        var stevePoint = steve.TranslatePoint(new Point(Math.Min(10, steve.Bounds.Width / 2), steve.Bounds.Height / 2), owner)!.Value;
        owner.MouseDown(stevePoint, MouseButton.Left); owner.MouseUp(stevePoint, MouseButton.Left); Pump();
        Wait(() => model.Accounts.SelectedProfile?.Name == "Steve" && !accountPicker.IsOpen);
        accountPicker.Open(); Pump(); ClickMinus("Alex"); Confirm();
        Wait(() => model.Accounts.Profiles.Count == 1 && model.Accounts.SelectedProfile?.Name == "Steve");
        Check(((LauncherAccount?)accountPicker.SelectedItem)?.Name == "Steve", "current deletion fallback not synchronized");
        accountPicker.Open(); Pump();
        Check(owner.GetVisualDescendants().OfType<Button>().Count(x => AutomationProperties.GetName(x)?.StartsWith("删除账号 ") == true) == 1, "last selected account missing its delete button");
        ClickMinus("Steve");
        var lastConfirmation = owner.OwnedWindows.OfType<LauncherDialog>().Single();
        Check(lastConfirmation.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.Contains("Steve") == true && x.Text.Contains("离线账号")), "delete confirmation omitted account name/type");
        Invoke(lastConfirmation.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "取消")));
        Check(model.Accounts.Profiles.Count == 1 && model.Accounts.SelectedProfile?.Name == "Steve", "cancel last-account deletion changed identity");
        accountPicker.Open(); Pump(); ClickMinus("Steve"); Confirm();
        Wait(() => !model.Accounts.Profiles.Any()); Check(accountPicker.SelectedItem == null, "last account removal left selection");
        Check(new MicrosoftAccounts(root).Profiles.Count == 0, "empty account list was not persisted");
        Capture("compact-final-empty-account-list");
        Add("Steve");
        owner.ToggleMode(); Pump();
        Check(accountPicker.IconOnly && accountPicker.SelectedItem is LauncherAccount { Name: "Steve" }, "full caption did not synchronize offline account");
        accountPicker.Open(); Pump();
        Check(owner.GetVisualDescendants().OfType<LucideIcon>().Any(x => x.Name == "SelectedCheck" && x.IsEffectivelyVisible && x.Opacity == 1), "selected account mark is missing");
        accountPicker.Close(); owner.ToggleMode(); Pump();
        foreach (var palette in ThemePalette.All)
        {
            ThemePalette.Select(palette); Pump();
            Check(((ISolidColorBrush)owner.Background!).Color.A is 0 or 255, "compact background uses private theme");
            Check(((SolidColorBrush)owner.GetVisualDescendants().OfType<ProgressBar>().Single(x => x.Name == "CompactDownloadBar").Foreground!).Color == ThemePalette.Accent.Color, "progress theme stale");
            Capture("compact-final-theme-" + Array.IndexOf(ThemePalette.All, palette));
        }
        ThemePalette.Select(ThemePalette.All[0]);
        model.Selected = pending; owner.ToggleMode(); owner.ToggleMode(); Pump();
        var primary = owner.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "CompactPrimary");
        Check(!primary.IsVisible && versions.SelectedItem == null, "pending target shown as an installed version");
        versions.SelectedItem = installed; Pump(); Check(model.Selected?.Id == installed.Id && Equals(primary.Content, "启动游戏"), "installed version does not select shared launch action");
        var launchStates = (Dictionary<Guid, string>)typeof(MainWindow).GetField("instanceStatuses", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
        model.Busy.Add(installed.Id); launchStates[installed.Id] = "正在检查和准备游戏";
        owner.ToggleMode(); owner.ToggleMode(); Pump();
        Check(!primary.IsEnabled && Equals(primary.Content, "启动中…"), "switch reset launching state");
        var launch = typeof(MainWindow).GetMethod("LaunchInstanceAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var firstLaunch = (Task)launch.Invoke(owner, [installed])!; var secondLaunch = (Task)launch.Invoke(owner, [installed])!;
        Check(firstLaunch.IsCompletedSuccessfully && secondLaunch.IsCompletedSuccessfully && model.Busy.Count == 1 && model.Resources.MainJobs.Count == 0, "duplicate launch bypassed busy guard");
        model.Busy.Remove(installed.Id); launchStates[installed.Id] = "启动失败 · fixture";
        owner.ToggleMode(); owner.ToggleMode(); Pump();
        Check(primary.IsEnabled && Equals(primary.Content, "启动游戏"), "failed launch cannot retry");
        launchStates.Remove(installed.Id); owner.ToggleMode(); owner.ToggleMode(); Pump();
        owner.Width = 330; owner.Height = 400; Pump(); Capture("compact-final-narrow");
        Check(versionRow.TranslatePoint(new Point(0, versionRow.Bounds.Height), owner)!.Value.Y <= owner.ClientSize.Height && primary.Bounds.Width > 200, "narrow layout overflows");
        owner.Close(); Pump();
        var restored = new MicrosoftAccounts(root);
        Check(restored.SelectedProfile?.Name == "Steve" && restored.Profiles.Count == 1, "account registry did not survive restart");
        var json = File.ReadAllText(Path.Combine(root, "accounts.json"));
        Check(!json.Contains("AccessToken") && !json.Contains("refresh_token"), "account metadata contains credentials");
        Console.WriteLine("PASS compact final installed-only receipt selector, formal logo, unframed selector rows, caption user/mode/close icons, inline account form/offline persistence, minus mouse hit isolation, delete cancel/confirm/current fallback/last empty, full account sync, six themes and narrow layout (Linux headless fixtures)");

        void Add(string name)
        {
            owner.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x => x.Name == "CaptionAccounts").OpenAuxiliary(); Pump();
            Check(!owner.OwnedWindows.Any(), "inline account form opened a dialog");
            Invoke(owner.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "CompactOfflineOption")); Pump();
            owner.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "CompactOfflineName").Text = name;
            Invoke(owner.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "CompactAddOffline"));
            Wait(() => model.Accounts.SelectedProfile?.Name == name); Pump();
        }
        void ClickMinus(string name)
        {
            var button = owner.GetVisualDescendants().OfType<Button>().Single(x => AutomationProperties.GetName(x)?.Equals("删除账号 " + name) == true);
            button.BringIntoView(); Pump();
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), owner)!.Value;
            owner.MouseDown(point, MouseButton.Left); owner.MouseUp(point, MouseButton.Left); Pump();
        }
        void Confirm()
        {
            var confirmation = owner.OwnedWindows.OfType<LauncherDialog>().Single();
            Invoke(confirmation.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "确认")));
        }
        void Capture(string name, Window? target = null)
        {
            if (shots == null) return; Directory.CreateDirectory(shots);
            using var frame = (target ?? owner).CaptureRenderedFrame(); frame!.Save(Path.Combine(shots, name + "-linux-headless.png"));
        }
    }
    static void Check(bool value, string error) { if (!value) throw new Exception(error); }
    static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    static void Invoke(Button b) { ((IInvokeProvider)new ButtonAutomationPeer(b)).Invoke(); Pump(); }
    static void Wait(Func<bool> ready) { var end = DateTime.UtcNow.AddSeconds(10); while (!ready() && DateTime.UtcNow < end) { Pump(); Thread.Sleep(10); } Pump(); Check(ready(), "compact final asynchronous wait timed out"); }
}
