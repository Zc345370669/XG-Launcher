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

internal static class Phase12UiTests
{
    internal static void Run(MainWindow window, string? screenshots)
    {
        var model = (LauncherModel)typeof(MainWindow).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        window.Width = 1130; window.Height = 850; Pump();
        Click("个人游戏");
        var card = window.GetVisualDescendants().OfType<Border>().First(x => x.Name?.StartsWith("InstanceCard_") == true);
        var menu = card.ContextMenu!;
        var title = card.GetVisualDescendants().OfType<TextBlock>().First();
        Right(title, new Point(5, 6));
        Check(menu.IsOpen, "instance name did not open context menu");
        Capture("phase12-context-menu");
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); Pump(); Check(!menu.IsOpen, "Esc failed to close menu");
        foreach (var text in card.GetVisualDescendants().OfType<TextBlock>())
        {
            if (text.GetVisualAncestors().OfType<Button>().Any()) continue;
            Right(text, new Point(2, 2)); Check(menu.IsOpen, "card metadata not right clickable"); menu.Close(); Pump();
        }
        Right(card, new Point(3, card.Bounds.Height - 4)); Check(menu.IsOpen, "empty card margin not right clickable");
        menu.Close(); Pump();
        foreach (var button in card.GetVisualDescendants().OfType<Button>())
        {
            Right(button, new Point(7, 7)); Check(!menu.IsOpen, "independent button right click bubbled to card");
        }
        Check(menu.Items.OfType<MenuItem>().Any(x => Equals(x.Header, "打开 saves 文件夹")), "saves menu missing");
        Check(!menu.Items.OfType<MenuItem>().Single(x => Equals(x.Header, "启动游戏")).IsEnabled, "uninstalled game launch enabled");
        Capture("phase12-instance-card");
        Console.WriteLine("PASS actual headless mouse: instance title and empty card context menu, Esc, button right-click exclusion and uninstalled launch disabled");

        Click("设置"); Click("界面");
        var settings = window.GetVisualDescendants().OfType<SettingsSurface>().Single();
        Check(!settings.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Any(x => x.Title is "下载限速" or "文件下载并发数"), "settings duplicated download controls");
        var reference = ThemePalette.Panel;
        foreach (var palette in ThemePalette.All)
        {
            var button = settings.GetVisualDescendants().OfType<Button>().Single(x => AutomationProperties.GetName(x)?.StartsWith(palette.Name) == true);
            Invoke(button); Pump();
            Check(ReferenceEquals(reference, ThemePalette.Panel) && ThemePalette.Panel.Color == Color.Parse(palette.Panel) && ThemePalette.Text.Color == Color.Parse(palette.Text), "theme did not update live global brushes");
            Check(Application.Current!.RequestedThemeVariant == (palette.Dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light), "Fluent theme variant mismatch");
            Check(File.ReadAllText(Path.Combine(model.Store.Root, "appearance.json")).Contains("Theme"), "theme not persisted");
            settings.ScrollToSection(1, false); Pump(); Capture("phase12-theme-" + Array.IndexOf(ThemePalette.All, palette));
            if (palette.Dark)
            {
                var show = window.ErrorAsync(window, "主题与统一弹窗真实渲染检查"); Pump();
                var dialog = window.OwnedWindows.OfType<LauncherDialog>().Single(x => x.Title == "操作失败");
                Check(ReferenceEquals(dialog.Background, ThemePalette.Panel), "dialog not globally themed"); Capture("phase12-dark-dialog", dialog);
                Invoke(dialog.GetVisualDescendants().OfType<Button>().First(x => Equals(x.Content, "关闭"))); Pump();
            }
        }
        var slider = settings.GetVisualDescendants().OfType<Slider>().Single(); slider.Value = 85; Pump();
        Check(ThemePalette.WindowBackground.Color.A == (byte)Math.Round(.85 * 255) && window.Opacity == 1, "opacity reduced child readability");
        slider.Value = 100; Pump(); Check(ThemePalette.WindowBackground.Color.A == 255, "opacity 100 not opaque");
        var blur = settings.GetVisualDescendants().OfType<CheckBox>().Single(x => Equals(x.Content, "轻微毛玻璃效果"));
        blur.IsChecked = true; Pump();
        if (!OperatingSystem.IsWindows()) Check(window.TransparencyLevelHint.Contains(WindowTransparencyLevel.None), "unsupported blur fallback missing");
        // Persisted theme is restored by a new actual window, without resetting download settings.
        var selected = ThemePalette.Current.Name;
        var restart = new MainWindow(model.Store.Root); restart.Show(); Pump();
        Check(ThemePalette.Current.Name == selected, "restart theme restore"); restart.Close(); Pump();
        Click("下载管理"); Check(window.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Count(x => x.Title is "下载限速" or "文件下载并发数") == 2, "central download settings missing");
        Console.WriteLine("PASS six real theme cards, live shared palette, Fluent dark mode, persisted restart, themed dialog, 85–100% background opacity and Linux blur fallback; deduplicated settings");

        var badgeRoot = Path.Combine(model.Store.Root, "badge-fixture"); Directory.CreateDirectory(badgeRoot);
        InstanceStore.WriteJson(Path.Combine(badgeRoot, "resource-operations.json"), Enumerable.Range(0, 101)
            .Select(n => new ResourceOperation(Guid.NewGuid(), "fixture", "实例安装", "fixture", "fixture", "失败")));
        var badgeWindow = new MainWindow(badgeRoot); badgeWindow.Show(); Pump();
        var badge = (TextBlock)typeof(MainWindow).GetField("downloadBadge", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(badgeWindow)!;
        Check(badge.IsVisible && badge.Text == "99+", "99+ badge or restored count wrong");
        badgeWindow.Close(); Pump();
        Console.WriteLine("PASS restored 101 unfinished main jobs display 99+, without counting child files");

        // Real manager with controlled fixture work: no network or installer success is implied.
        var entered = new TaskCompletionSource(); var done = new TaskCompletionSource();
        var a = model.Store.Entries.First();
        var fixture = model.Resources.RunAsync(a.Name, "实例安装", "fixture", a.Name, async stage => {
            model.Resources.PlanStages(new[] { ("Assets", 100L, 1, false), ("Libraries", 200L, 2, false) }, a.Name);
            await model.Resources.RunAsync("objects", "Assets", "fixture", a.Name, async child => {
                child("下载中"); model.Resources.Report("Assets", a.Name, 50, 100, "asset-fixture", 0, 1, 50, 100);
                entered.SetResult(); await done.Task; return true;
            });
            await model.Resources.RunAsync("libraries", "Libraries", "fixture", a.Name, child => {
                model.Resources.Report("Libraries", a.Name, 200, 200, "fixture-lib", 2, 2, 200, 200); return Task.FromResult(true);
            }); return true;
        }, new ResourceRecovery("game", a.Id, InstallationReceipt.Configuration(a)));
        Pump(); if (fixture.IsFaulted) throw fixture.Exception!; Check(entered.Task.IsCompleted, "fixture job did not start");
        var rootId = model.Resources.MainJobs.Last().Id;
        Check(window.GetVisualDescendants().OfType<ProgressBar>().Count() == 1, "installation must show exactly one horizontal progress bar");
        Check(window.GetVisualDescendants().OfType<TextBlock>().Any(x => x.IsVisible && x.Text == "1"), "unfinished main badge absent");
        Capture("phase12-unified-task-fixture");
        model.Resources.Pause(rootId); Pump(); model.Resources.Resume(rootId); Pump();
        done.SetResult(); Pump(); Check(fixture.IsCompletedSuccessfully && model.Resources.UnfinishedCount == 0, "fixture completion count");
        var toast = (ContentControl)typeof(MainWindow).GetField("completionToast", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        Check(toast.IsVisible, "main completion did not notify");
        toast.IsVisible = false;
        Click("设置");
        var notify = window.GetVisualDescendants().OfType<CheckBox>().Single(x => Equals(x.Content, "下载完成提示"));
        notify.IsChecked = false; Pump();
        var silent = model.Resources.RunAsync(a.Name, "实例安装", "fixture", a.Name,
            mark => Task.FromResult(true), new ResourceRecovery("game", a.Id, InstallationReceipt.Configuration(a)));
        Pump(); Check(silent.IsCompletedSuccessfully && !toast.IsVisible, "disabled completion preference ignored");
        Check(File.ReadAllText(Path.Combine(model.Store.Root, "download-notifications.json")) == "false", "notification preference not persisted");
        Console.WriteLine("PASS one completion toast per main job and disabled notification preference persistence");
        Console.WriteLine("PASS actual UI with controlled fixture: one installation card, staged details, live main-job badge and completion");

        void Right(Control control, Point point)
        {
            var at = control.TranslatePoint(point, window) ?? throw new Exception("detached target");
            window.MouseDown(at, MouseButton.Right); window.MouseUp(at, MouseButton.Right); Pump();
        }
        void Click(string title)
        {
            var button = window.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, title) || AutomationProperties.GetName(b) == title);
            Invoke(button); Pump();
        }
        void Capture(string name, Window? target = null)
        {
            if (screenshots == null) return;
            Directory.CreateDirectory(screenshots); Pump();
            using var image = (target ?? window).CaptureRenderedFrame() ?? throw new Exception("no rendered frame");
            image.Save(Path.Combine(screenshots, name + ".png"));
        }
        void Pump()
        {
            for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
        }
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
