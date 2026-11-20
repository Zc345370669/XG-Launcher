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
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XG.Core;
using XGLauncher;

internal static class AdaptiveComboUiTests
{
    internal static void Run(string root, string? shots)
    {
        root = Path.Combine(root, "adaptive-combo");
        var empty = new MainWindow(root + "-empty"); empty.Show(); Pump(); empty.ToggleMode(); Pump();
        var ev = Find<UnifiedExpandableSelect>(empty, "CompactInstances");
        Check(!Find<Button>(empty, "CompactPrimary").IsVisible && ev.Title == "下载游戏实例", "empty state shows launch/download button");
        Invoke(ev.GetVisualDescendants().OfType<Button>().First());
        Check(empty.IsCompact && !empty.OwnedWindows.Any() && ev.IsAuxiliaryOpen, "empty combo did not open inline download");
        ev.Close(); Pump(); empty.Close(); Pump();

        var store = new InstanceStore(root);
        var ready = Enumerable.Range(0, 18).Select(i => new Instance(Guid.NewGuid(), "很长的实例名称" + new string('长', 45) + i, "1.21.1", "NeoForge")).ToArray();
        foreach (var item in ready) { store.Save(item); Receipt(item); }
        var pending = new Instance(Guid.NewGuid(), "待下载实例", "1.21.1"); store.Save(pending);
        var broken = pending with { Id = Guid.NewGuid(), Name = "损坏实例" }; store.Save(broken); Receipt(broken);
        File.WriteAllText(Path.Combine(store.DirectoryFor(broken), "fixture.bin"), "corrupted evidence");
        Directory.CreateDirectory(Path.Combine(store.InstanceRoot, "folder-only-is-not-a-game"));
        var owner = new MainWindow(root) { FontFamily = new FontFamily("Noto Sans CJK SC") }; owner.Show(); Pump(); owner.ToggleMode(); Pump();
        var model = (LauncherModel)typeof(MainWindow).GetField("model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
        var versions = Find<UnifiedExpandableSelect>(owner, "CompactInstances");
        var accounts = Find<UnifiedExpandableSelect>(owner, "CompactAccounts");
        var primary = Find<Button>(owner, "CompactPrimary"); var progress = Find<Control>(owner, "CompactProgress");
        Wait(() => !versions.IsLoading);
        Check(Path.GetFullPath(model.Store.Root) == Path.GetFullPath(root) && versions.ItemsSource!.Cast<Instance>().Count() == 18, "async scan accepted pending, corrupt, or folder-only instance");
        Check(versions.TwoLineTitle && versions.Title == "已安装游戏" && !owner.GetVisualDescendants().OfType<Control>().Any(x => x.Name == "CompactInstall"), "two-line installed header or no external plus contract");
        Check(!progress.IsVisible && primary.IsVisible, "idle progress/launch visibility wrong");
        var before = versions.TranslatePoint(default, owner)!.Value; var windowHeight = owner.Height;
        versions.Open(); Pump();
        Check(versions.ExpansionBounds is { } rect && rect.Left == before.X && rect.Width == versions.Bounds.Width && (rect.Top == before.Y || Math.Abs(rect.Bottom - before.Y - versions.Bounds.Height) < 1), "combo contour is not flush with original anchor");
        Check(versions.TranslatePoint(default, owner) == before && owner.Height == windowHeight, "open moved anchor/window");
        var list = owner.GetVisualDescendants().OfType<ListBox>().Single();
        Check(list.GetVisualDescendants().OfType<ScrollViewer>().Any(x => x.Extent.Height > x.Viewport.Height), "installed long list is not internally scrollable");
        Check(owner.GetVisualDescendants().OfType<LucideIcon>().Any(x => x.Name == "SelectedCheck" && x.Foreground is ISolidColorBrush b && b.Color.G > b.Color.R), "selected item missing green check");
        var footer = Find<Button>(owner, "ComboFooter"); Check(footer.IsEffectivelyVisible, "sticky add instance footer hidden");
        var selectedName = model.Selected!.Name;
        Check(!owner.GetVisualDescendants().OfType<Button>().Any(x => AutomationProperties.GetName(x) == "删除实例 Minecraft 1.21.1 · NeoForge · " + selectedName), "selected instance has trash button");
        var trash = owner.GetVisualDescendants().OfType<Button>().First(x => AutomationProperties.GetName(x)?.StartsWith("删除实例 ") == true);
        Check(trash.Content is LucideIcon { Icon: "trash-2" } && trash.Width == 32, "instance trash slot wrong");
        trash.BringIntoView(); Pump(); Invoke(trash);
        var deletion = owner.OwnedWindows.OfType<LauncherDialog>().Single();
        Check(!versions.IsOpen && deletion.Title == "删除实例", "delete confirmation under combo");
        deletion.Close(); Pump(); Check(model.Store.Entries.Count == 20 && model.Selected!.Name == selectedName, "cancel deleted/selected instance");
        versions.Open(); Pump(); trash = owner.GetVisualDescendants().OfType<Button>().First(x => AutomationProperties.GetName(x)?.StartsWith("删除实例 ") == true);
        var deleted = versions.ItemsSource!.Cast<Instance>().First(x => x.Id != model.Selected!.Id);
        Invoke(trash); deletion = owner.OwnedWindows.OfType<LauncherDialog>().Single();
        Invoke(deletion.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "仅移除记录")));
        var confirm = owner.OwnedWindows.OfType<LauncherDialog>().Single();
        Invoke(confirm.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "确认")));
        Wait(() => !model.Store.Entries.Any(x => x.Id == deleted.Id));
        Check(File.Exists(Path.Combine(store.DirectoryFor(deleted), "fixture.bin")) && versions.ItemsSource!.Cast<Instance>().Count() == 17, "safe record removal lost files or did not refresh shared list");
        versions.Open(); Pump(); Invoke(Find<Button>(owner, "ComboFooter"));
        Check(owner.IsCompact && !owner.OwnedWindows.Any() && versions.IsAuxiliaryOpen, "footer did not open inline install flow"); versions.Close(); Pump();

        // Inline add/list use one reusable outline and are mutually exclusive.
        Invoke(Find<Button>(owner, "AddAccount"));
        Check(accounts.IsAuxiliaryOpen && !owner.OwnedWindows.Any(), "add account opened separate manager");
        Capture("adaptive-inline-account-add");
        var name = Find<TextBox>(owner, "CompactOfflineName"); name.Text = "bad name";
        name.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Pump();
        Check(accounts.IsAuxiliaryOpen && model.Accounts.Profiles.Count == 0 && Find<TextBlock>(owner, "CompactAccountError").IsVisible, "invalid account accepted or no error");
        name.Text = "AdaptivePlayer"; name.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Wait(() => !accounts.IsOpen && model.Accounts.SelectedProfile?.Name == "AdaptivePlayer");
        Invoke(Find<Button>(owner, "AddAccount")); Find<TextBox>(owner, "CompactOfflineName").Text = "adaptiveplayer";
        Invoke(Find<Button>(owner, "CompactAddOffline")); Wait(() => !accounts.IsOpen);
        Check(model.Accounts.Profiles.Count == 1, "duplicate offline account added");
        accounts.Open(); Pump(); Invoke(Find<Button>(owner, "AddAccount"));
        Check(accounts.IsAuxiliaryOpen && !owner.GetVisualDescendants().OfType<ListBox>().Any(), "list not replaced by inline add");
        // The real header is moved into the overlay; find its selector button by caption automation.
        var anchorButton = owner.GetVisualDescendants().OfType<Button>().Single(x => AutomationProperties.GetName(x) == model.Accounts.SelectedProfile!.Caption);
        Invoke(anchorButton); Check(accounts.IsOpen && !accounts.IsAuxiliaryOpen && !owner.GetVisualDescendants().OfType<TextBox>().Any(x => x.Name == "CompactOfflineName"), "dropdown did not close add panel");
        foreach (var palette in ThemePalette.All) { ThemePalette.Select(palette); Pump(); Check(accounts.IsOpen && ((ISolidColorBrush)owner.Background!).Color.A is 0 or 255, "open combo theme/state diverged"); }
        Capture("adaptive-accounts-open");
        owner.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Pump(); Check(!accounts.IsOpen, "Escape leaves combo open");
        // Live task + completion reinspection, without synthetic UI percentages.
        model.Selected = pending; owner.ToggleMode(); owner.ToggleMode(); Pump(); Check(!primary.IsVisible, "pending target shows primary button");
        var gate = new TaskCompletionSource();
        var task = model.Resources.RunAsync("受控实例下载", "实例安装", "fixture", pending.Name, async mark =>
        { mark("下载中"); model.Resources.Report("游戏本体", pending.Name, 1, 2, "fixture.bin", 0, 1); await gate.Task; Receipt(pending); return true; }, new ResourceRecovery("game", pending.Id, LauncherModel.GameConfiguration(pending)));
        Pump(); Check(progress.IsVisible && !primary.IsVisible, "live download area/button visibility wrong");
        var jobs = model.Resources.Items.Select(x => x.Id).ToArray(); owner.ToggleMode(); owner.ToggleMode(); Pump();
        Check(model.Resources.Items.Select(x => x.Id).SequenceEqual(jobs) && !task.IsCompleted, "mode switch duplicated or ended download");
        gate.SetResult(); Wait(() => task.IsCompleted && versions.ItemsSource!.Cast<Instance>().Any(x => x.Id == pending.Id));
        Check(!progress.IsVisible && primary.IsVisible, "completion did not refresh installed/button/progress");
        foreach (var size in new[] { new Size(320, 330), new Size(360, 400), new Size(460, 500) })
        {
            owner.Width = size.Width; owner.Height = size.Height; Pump(); versions.Open(); Pump();
            Check(versions.ExpansionBounds is { } bounds && bounds.X >= 0 && bounds.Right <= owner.ClientSize.Width && bounds.Y >= 0 && bounds.Bottom <= owner.ClientSize.Height + 1, "resized popup outside client viewport");
            Check(owner.GetVisualDescendants().OfType<TextBlock>().Where(x => x.Text?.StartsWith("Minecraft 1.21.1") == true).All(x => x.TextTrimming == TextTrimming.CharacterEllipsis), "long names lack ellipsis");
            Capture("adaptive-size-" + size.Width); versions.Close();
        }
        foreach (var dpi in new[] { 1d, 1.25, 1.5, 1.75, 2 })
        foreach (var nearEdge in new[] { new Rect(0, 10, 300, 54), new Rect(10, 290, 300, 36) })
        {
            var plan = ComboPlacement.Calculate(nearEdge, new Size(320, 330), 240, true);
            var scaled = ComboPlacement.Calculate(new Rect(nearEdge.X*dpi, nearEdge.Y*dpi, nearEdge.Width*dpi, nearEdge.Height*dpi), new Size(320*dpi,330*dpi),240*dpi,true);
            Check(plan.Bounds.X >= 0 && plan.Bounds.Right <= 320 && plan.Bounds.Y >= 0 && plan.Bounds.Bottom <= 330, "edge placement escapes viewport");
            Check(Math.Abs(plan.Bounds.Top*dpi-scaled.Bounds.Top)<.1 && Math.Abs(plan.Bounds.Width*dpi-scaled.Bounds.Width)<.1, "DPI geometry breaks edge alignment");
        }
        foreach (var dpi in new[] { 1d, 1.25, 1.5, 1.75, 2 })
        foreach (var symbol in new[] { "chevron-down", "chevron-up", "plus", "check", "trash-2", "minus", "user-round-plus", "play" })
        {
            var icon = new LucideIcon(symbol, 24) { Foreground = Brushes.Black };
            icon.Measure(new Size(24,24)); icon.Arrange(new Rect(0,0,24,24));
            using var rendered = new RenderTargetBitmap(new PixelSize((int)(24*dpi),(int)(24*dpi)), new Vector(96*dpi,96*dpi)); rendered.Render(icon);
            using var buffer = new MemoryStream(); rendered.Save(buffer); buffer.Position = 0;
            using var bitmap = SkiaSharp.SKBitmap.Decode(buffer);
            var ink = false;
            for(var y=0;y<bitmap.Height;y++) for(var x=0;x<bitmap.Width;x++)
            {
                var alpha=bitmap.GetPixel(x,y).Alpha; ink |= alpha>0;
                if(x==0||y==0||x==bitmap.Width-1||y==bitmap.Height-1) Check(alpha==0,"scaled icon edge clipped "+symbol);
            }
            Check(ink,"scaled icon blank "+symbol);
        }
        foreach (var viewport in new[] { new Rect(45,0,275,280), new Rect(0,50,280,280) })
        {
            var plan = ComboPlacement.Calculate(new Rect(10,290,300,36),viewport,240,true);
            Check(viewport.Contains(plan.Bounds), "partially visible/multi-monitor working-area constraint failed");
        }
        owner.Close(); Pump(); ThemePalette.Select(ThemePalette.All[0]);
        Console.WriteLine("PASS adaptive combo async installed receipt scan/reject invalid folders, empty/inline install route, two-line header, one joined contour/stable anchor, green selected check/no delete, safe trash cancel/confirm/sticky footer, inline offline Enter/validation/dedup/mutual panels, open theme, real task completion refresh, mode sync, long names/scroll/resize and 100-200% geometry (Linux headless, not Windows GUI)");

        void Receipt(Instance item)
        {
            var dir = store.DirectoryFor(item); Directory.CreateDirectory(dir); var file = Path.Combine(dir, "fixture.bin"); File.WriteAllText(file, "Controlled receipt evidence only; not a real Minecraft installation.");
            Task.Run(() => InstallationReceipt.WriteAsync(store, item, [file], default, item.Version, file)).GetAwaiter().GetResult();
        }
        void Capture(string title) { if (shots == null) return; Directory.CreateDirectory(shots); using var frame=owner.CaptureRenderedFrame(); frame!.Save(Path.Combine(shots,title+"-linux-headless.png")); }
    }
    static T Find<T>(Window owner,string name) where T:Control => owner.GetVisualDescendants().OfType<T>().Single(x=>x.Name==name);
    static void Check(bool good,string error) { if(!good)throw new Exception(error); }
    static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    static void Invoke(Button b) { ((IInvokeProvider)new ButtonAutomationPeer(b)).Invoke(); Pump(); }
    static void Wait(Func<bool> ready) { var end=DateTime.UtcNow.AddSeconds(15); while(!ready()&&DateTime.UtcNow<end) { Pump(); Thread.Sleep(10); } Pump(); Check(ready(),"adaptive wait timed out"); }
}
