// SPDX-License-Identifier: MIT
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XG.Core;
using XGLauncher;

internal static class DualModeUiTests
{
    internal static void Run(string root, string? shots)
    {
        root = Path.Combine(root, "dual-mode");
        var store = new InstanceStore(root);
        var first = new Instance(Guid.NewGuid(), "Relay fixture", "1.21.1");
        var second = new Instance(Guid.NewGuid(), "Second instance", "1.21.1", "Fabric");
        store.Save(first); store.Save(second);
        Install(first); Install(second);
        var owner = new MainWindow(root); owner.FontFamily = new FontFamily("Noto Sans CJK SC"); owner.Show(); Pump();
        var model = (LauncherModel)typeof(MainWindow).GetField("model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
        var resources = model.Resources; var downloads = model.Downloads; var accounts = model.Accounts;
        var handlers = Subscribers(resources, "ProgressChanged");
        owner.Width = 900; owner.Height = 650; Pump();
        for (var i = 0; i < 12; i++) { owner.ToggleMode(); Pump(); }
        Check(!owner.IsCompact && Subscribers(resources, "ProgressChanged") == handlers, "mode leaked progress subscriptions");
        owner.ToggleMode(); Pump();
        Check(owner.IsCompact && owner.Width < 500, "compact default width");
        Check(owner.GetVisualDescendants().OfType<StackPanel>().Where(x => x.Children.OfType<Button>().Count(b => b.Height == 40) == 6).All(x => !x.IsEffectivelyVisible), "sidebar visible in compact mode");
        var picker = owner.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x => x.Name == "CompactInstances");
        Wait(() => picker.ItemsSource!.Cast<Instance>().Count() == 2);
        Check(picker.ItemsSource!.Cast<Instance>().Count() == 2, "shared instance index missing");
        picker.SelectedItem = second; Pump(); Check(model.Selected?.Id == second.Id, "compact selection not shared");
        owner.ToggleMode(); Pump(); Check(model.Selected?.Id == second.Id && Math.Abs(owner.Width - 900) < 1, "full selection or window restore lost");
        var third = first with { Id = Guid.NewGuid(), Name = "Newly installed fixture" }; model.Store.Save(third); Install(third); model.Selected = third;
        owner.ToggleMode(); Pump(); Wait(() => ((Instance?)picker.SelectedItem)?.Id == third.Id); Check(((Instance?)picker.SelectedItem)?.Id == third.Id && picker.ItemsSource!.Cast<Instance>().Count() == 3, "full changes not reflected in compact");
        var previousTheme = ThemePalette.Current;
        foreach (var palette in ThemePalette.All)
        {
            ThemePalette.Select(palette); owner.ToggleMode(); owner.ToggleMode(); Pump();
            Check(((ISolidColorBrush)owner.Background!).Color.A is 0 or 255, "transparent client background");
        }
        ThemePalette.Select(previousTheme);
        owner.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x => x.Name == "CaptionAccounts").OpenAuxiliary(); Pump();
        Check(!owner.OwnedWindows.Any(), "compact add opened account manager");
        Invoke(owner.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "CompactOfflineOption")); Pump();
        owner.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "CompactOfflineName").Text = "ModePlayer";
        Invoke(owner.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "CompactAddOffline"));
        Wait(() => model.Accounts.SelectedProfile?.Name == "ModePlayer");
        Check(model.Player == "ModePlayer" && owner.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x => x.Name == "CaptionAccounts").SelectedItem is LauncherAccount { Name: "ModePlayer" }, "account not synchronized");

        var gate = new TaskCompletionSource();
        var operation = resources.RunAsync(third.Name, "实例安装", "fixture", third.Name, async mark =>
        {
            resources.PlanStages([("游戏本体", 100L, 1, false), ("Minecraft Assets", 300L, 3, false)], third.Name);
            await resources.RunAsync("game", "游戏本体", "fixture", third.Name, async child =>
            {
                child("下载中"); resources.Report("游戏本体", third.Name, 50, 100, new string('a', 40), 0, 1);
                await gate.Task; return true;
            });
            await resources.RunAsync("assets", "Minecraft Assets", "fixture", third.Name, child => Task.FromResult(true));
            return true;
        }, new ResourceRecovery("game", third.Id, LauncherModel.GameConfiguration(third)));
        Pump(); Check(!operation.IsCompleted, "controlled task did not wait");
        var ids = resources.Items.Select(x => x.Id).ToArray();
        for (var i = 0; i < 10; i++) { owner.ToggleMode(); Pump(); }
        Check(owner.IsCompact && resources.Items.Select(x => x.Id).SequenceEqual(ids), "switch duplicated tasks");
        Check(ReferenceEquals(resources, model.Resources) && ReferenceEquals(downloads, model.Downloads) && ReferenceEquals(accounts, model.Accounts), "services replaced on mode switch");
        var progress = owner.GetVisualDescendants().OfType<Control>().Single(x => x.Name == "CompactProgress");
        Check(progress.IsVisible && !progress.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.Contains(new string('a', 40)) == true), "compact progress leaks internal hash");
        Capture("phase17-compact-active-fixture-linux-headless");
        var rootJob = resources.MainJobs.Last();
        resources.Pause(rootJob.Id); Pump();
        var resume = owner.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "CompactPrimary");
        Check(!resume.IsVisible && resources.IsPaused(rootJob.Id), "install target showed launch/download button");
        resources.Resume(rootJob.Id); Check(!resources.IsPaused(rootJob.Id), "shared task resume failed");
        owner.WindowState = WindowState.Minimized; Pump(); Check(!operation.IsCompleted, "minimize stopped task"); owner.WindowState = WindowState.Normal;
        owner.Close(); Pump(); Check(owner.IsVisible && !model.IsDisposed, "active close bypassed safe guard");
        var exitConfirmation = owner.OwnedWindows.Single();
        Invoke(exitConfirmation.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "取消"))); Pump();
        Check(resources.CanControl(rootJob.Id), "cancelling exit stopped the shared task");
        gate.SetResult(); Wait(() => operation.IsCompleted); Check(operation.IsCompletedSuccessfully, "shared task failed");
        Wait(() => picker.ItemsSource!.Cast<Instance>().Any(x => x.Id == third.Id));
        owner.ToggleMode(); Pump();
        var settingsButton = owner.GetVisualDescendants().OfType<Button>().Single(x => AutomationProperties.GetName(x) == "设置"); Invoke(settingsButton);
        var settings = owner.GetVisualDescendants().OfType<SettingsSurface>().Single(); settings.ScrollToSection(6, false); Pump();
        var about = settings.Sections[6];
        Check(about.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "作者 / 维护者：Zc345370669"), "official author missing from About");
        var link = about.GetVisualDescendants().OfType<Button>().Single(x => AutomationProperties.GetName(x) == MainWindow.OfficialRepository);
        string? opened = null; owner.OpenExternalLink = url => { opened = url; return Task.CompletedTask; }; Invoke(link);
        Check(opened == "https://github.com/Zc345370669/XG-Launcher", "official repository handler wrong");
        Check(!about.GetVisualDescendants().OfType<Button>().Any(x => Equals(x.Content, "查看 MIT License") || Equals(x.Content, "第三方许可证")), "duplicate About license entry returned");
        var legal = settings.Sections[5].GetVisualDescendants().OfType<LegalBrowser>().Single();
        legal.ShowDocument(LegalCatalog.File("XGLauncher-MIT.txt", "XG Launcher"), legal.ShowOverview); Pump();
        Check(legal.DocumentBody?.Text == LegalCatalog.Read("XGLauncher-MIT.txt"), "integrated MIT legal text incomplete");
        legal.ShowThirdParty(); Pump(); Check(legal.Page == "third-party", "integrated third-party section missing"); legal.ShowOverview();
        owner.Width = 600; owner.Height = 420; Pump(); settings.ContentScroll.Offset = new Vector(0, settings.ContentScroll.Extent.Height); Pump(); Capture("phase17-about-small-linux-headless");
        owner.ToggleMode(); Pump(); owner.Width = 350; owner.Height = 380; Pump(); Capture("phase17-compact-minimum-linux-headless");
        owner.Close(); Pump(); Check(model.IsDisposed, "model HTTP lifetime not released");
        var restored = new MainWindow(root); restored.Show(); Pump();
        var restoredModel = (LauncherModel)typeof(MainWindow).GetField("model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(restored)!;
        Check(restored.IsCompact && Math.Abs(restored.Width - 350) < 1 && restoredModel.Player == "ModePlayer" && restoredModel.Selected?.Id == third.Id, "restart mode geometry account selection persistence failed");
        restored.Close(); Pump();
        InstanceStore.WriteJson(Path.Combine(root, "window-session.json"), new WindowSession(true, Small: new ModeGeometry(1e12, 1e12, int.MaxValue, int.MinValue)));
        var boundary = new MainWindow(root); boundary.Show(); Pump(); Check(boundary.Width < 10000 && boundary.Height < 10000, "corrupt/offscreen geometry not clamped"); boundary.Close(); Pump();
        File.WriteAllText(Path.Combine(root, "window-session.json"), "invalid-json");
        Check(!WindowSession.Load(Path.Combine(root, "window-session.json")).Compact, "invalid session did not default safely");
        Console.WriteLine("PASS phase17 dual-mode same window/services, repeated switching no task duplication/subscription leak, selection/account/theme sync, active task close guard/minimize, complete MIT and third-party entry, official GitHub handler, mode/geometry/selection/player restart persistence, invalid geometry, disposed HTTP lifetime (Linux headless fixtures)");

        void Install(Instance entry)
        {
            var directory = store.DirectoryFor(entry); Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "fixture-content.bin"); File.WriteAllText(file, "Controlled test evidence only; not Minecraft.");
            Task.Run(() => InstallationReceipt.WriteAsync(store, entry, [file], default, entry.Version, file)).GetAwaiter().GetResult();
        }
        void Capture(string name) { if (shots == null) return; Directory.CreateDirectory(shots); using var frame = owner.CaptureRenderedFrame(); frame!.Save(Path.Combine(shots, name + ".png")); }
    }
    private static int Subscribers(object source, string field) => ((Delegate?)source.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source))?.GetInvocationList().Length ?? 0;
    private static void Check(bool value, string error) { if (!value) throw new Exception(error); }
    private static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Invoke(Button button) { ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke(); Pump(); }
    private static void Wait(Func<bool> predicate) { var until = DateTime.UtcNow.AddSeconds(10); while (!predicate() && DateTime.UtcNow < until) { Pump(); Thread.Sleep(10); } Pump(); Check(predicate(), "async UI wait timed out"); }
}
