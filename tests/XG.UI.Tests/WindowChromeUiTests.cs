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
using XGLauncher;

internal static class WindowChromeUiTests
{
    internal static void Run(string root, string? shots)
    {
        Check(!NativeWindowRounding.SupportsCornerPreference(false, new Version(10, 0, 26100)), "DWM requested on non-Windows");
        foreach (var build in new[] { 17763, 19041, 19045, 21999 })
            Check(!NativeWindowRounding.SupportsCornerPreference(true, new Version(10, 0, build)), "Windows 10 treated as DWM corner capable");
        foreach (var build in new[] { 22000, 22621, 26100 }) Check(NativeWindowRounding.SupportsCornerPreference(true, new Version(10, 0, build)), "Windows 11 capability gate wrong");
        Check(NativeWindowRounding.ColorRef(Color.FromRgb(1, 2, 3)) == 0x030201, "native border COLORREF byte order wrong");
        foreach (var dpi in new[] { 1d, 1.25, 1.5, 1.75, 2 })
        {
            var plan = AccountPopupLayout.Calculate(346, 285, 20, 240);
            Check(!plan.UseSidePopup && plan.Top >= 291 && plan.Top + plan.Height <= 342, "popup intersects primary or anchor");
            Check(Math.Abs(WindowChrome.ClientBorder(dpi, false, false).Left * dpi - 1) < .001, "single physical-pixel edge math");
        }
        Check(WindowChrome.ClientBorder(2, true, false) == new Thickness(0) && WindowChrome.ClientBorder(2, false, true) == new Thickness(0), "duplicate client/native border or maximized frame");
        Check(AccountPopupLayout.Calculate(276, 285, 20, 240).UseSidePopup, "insufficient gap did not choose side popup");
        Check(WindowChrome.ResizeEdge(new Point(1, 1), new Size(400, 400)) == WindowEdge.NorthWest && WindowChrome.ResizeEdge(new Point(200, 200), new Size(400, 400)) == null, "fallback resize hit regions wrong");
        root = Path.Combine(root, "window-chrome");
        var owner = new MainWindow(root) { FontFamily = new FontFamily("Noto Sans CJK SC") }; owner.Show(); Pump();
        var model = (LauncherModel)typeof(MainWindow).GetField("model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
        for (var i = 0; i < 8; i++) model.Accounts.AddOfflineAsync("Player" + i).GetAwaiter().GetResult();
        owner.ToggleMode(); Pump();
        if (WindowChrome.Strategy(owner) == "opaque-square-fallback")
        {
            var before = owner.ClientSize;
            owner.MouseDown(new Point(before.Width - 2, before.Height - 2), MouseButton.Left);
            owner.MouseMove(new Point(before.Width + 18, before.Height + 8)); owner.MouseUp(new Point(before.Width + 18, before.Height + 8), MouseButton.Left); Pump();
            Check(owner.Width > before.Width && owner.Height > before.Height, "opaque fallback lost border resize");
            owner.Width = 360; owner.Height = 400; Pump();
        }
        var surface = owner.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "WindowShell");
        Check(surface.Clip == null && surface.CornerRadius == new CornerRadius(0) && owner.Opacity == 1 && owner.TransparencyLevelHint.Contains(WindowTransparencyLevel.None), "second client rounding mask or translucency remains");
        Check(WindowChrome.Strategy(owner) != "unconfigured", "main window is not configured by shared chrome");
        var select = owner.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x => x.Name == "CompactAccounts");
        var primary = owner.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "CompactPrimary");
        var anchor = select.TranslatePoint(default, owner)!.Value; var height = owner.Height;
        select.Open(); Pump();
        Check(select.ExpansionStrategy == "joined-above" && select.ExpansionBounds is { } bounds && Math.Abs(bounds.Bottom - (anchor.Y + select.Bounds.Height)) < 1, "account header/list not one joined contour");
        Check(select.TranslatePoint(default, owner) == anchor && owner.Height == height, "expansion moved fixed account bar or window");
        var list = owner.GetVisualDescendants().OfType<ListBox>().Single();
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        Check(scroll.Extent.Height > scroll.Viewport.Height, "long account list does not scroll internally");
        list.ScrollIntoView(model.Accounts.Profiles.First()); Pump();
        Capture("rounding-account-gap-scroll");
        list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); Pump(); Check(!select.IsOpen, "Escape does not close account list");
        select.Open(); Pump(); owner.MouseDown(new Point(10, 100), MouseButton.Left); owner.MouseUp(new Point(10, 100), MouseButton.Left); Pump(); Check(!select.IsOpen, "outside click does not dismiss");
        owner.Height = 330; Pump(); select.Open(); Pump(); Check(select.ExpansionStrategy == "joined-above" && owner.Height == 330, "short window expanded or hid popup instead of using controlled side layer"); select.Close(); Pump();
        owner.Height = 400; Pump();
        owner.ToggleMode(); Pump();
        Invoke((Button)typeof(MainWindow).GetField("account", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!);
        var dialog = owner.OwnedWindows.OfType<LauncherDialog>().Single();
        var body = dialog.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "DialogSurface");
        Check(WindowChrome.Strategy(dialog) != "unconfigured" && body.Clip == null && body.CornerRadius == new CornerRadius(0) && ReferenceEquals(dialog.Background, ThemePalette.Background), "account dialog uses conflicting surface policy");
        Check(dialog.Height <= Math.Min(dialog.MaxHeight, 620) && dialog.Height < 600, "account management keeps oversized editor height");
        Check(dialog.GetVisualDescendants().OfType<ItemsControl>().Single(x => x.Name == "FullAccountsList").ItemsSource!.Cast<LauncherAccount>().Count() == 8, "inline account management list missing");
        Check(dialog.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "安全操作") && dialog.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "离线玩家名称"), "account and cache sections not labeled");
        var removeOther = dialog.GetVisualDescendants().OfType<Button>().Single(x => AutomationProperties.GetName(x) == "删除账号 Player0");
        removeOther.BringIntoView(); Pump();
        Capture("rounding-manager-before-delete", dialog);
        var removePoint = removeOther.TranslatePoint(new Point(removeOther.Bounds.Width / 2, removeOther.Bounds.Height / 2), dialog)!.Value;
        dialog.MouseMove(removePoint); Pump(); dialog.MouseDown(removePoint, MouseButton.Left); dialog.MouseUp(removePoint, MouseButton.Left); Pump();
        var removeConfirmation = dialog.OwnedWindows.OfType<LauncherDialog>().Single();
        Check(model.Accounts.SelectedProfile?.Name == "Player7", "inline minus switched current account");
        Invoke(removeConfirmation.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "取消")));
        Check(model.Accounts.Profiles.Count == 8 && model.Accounts.SelectedProfile?.Name == "Player7", "inline deletion cancellation mutated accounts");
        Capture("rounding-accounts-compact", dialog);
        var cache = Path.Combine(root, "microsoft-msal.bin"); File.WriteAllText(cache, "controlled cache fixture");
        Invoke(dialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "退出并清除账号缓存")));
        var confirm = dialog.OwnedWindows.OfType<LauncherDialog>().Single();
        Check(WindowChrome.Strategy(confirm) != "unconfigured", "confirmation not styled by common chrome");
        Invoke(confirm.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "取消")));
        Check(File.Exists(cache) && model.Accounts.Profiles.Count == 8, "cache removed without confirmation");
        foreach (var palette in ThemePalette.All)
        {
            ThemePalette.Select(palette); Pump();
            Check(((ISolidColorBrush)dialog.Background!).Color.A == 255 && ReferenceEquals(dialog.Background, ThemePalette.Background), "dialog theme diverged or became transparent");
            Capture("rounding-dialog-theme-" + Array.IndexOf(ThemePalette.All, palette), dialog);
        }
        dialog.Close(); Pump();
        owner.ToggleMode(); Pump(); owner.WindowState = WindowState.Maximized; Pump();
        Check(surface.Clip == null && surface.CornerRadius == new CornerRadius(0) && surface.BorderThickness == new Thickness(0), "maximized window has corner holes/client mask");
        owner.WindowState = WindowState.Normal; Pump(); owner.Close(); Pump(); ThemePalette.Select(ThemePalette.All[0]);
        Console.WriteLine("PASS window chrome Win10/11 capability gates, COLORREF, opaque shared main/dialog/confirmation surfaces, maximized no mask, 100-200% edge math; joined upward account outline, internal scrolling, Escape/outside dismissal, short-window joined overlay, compact labeled account management and cache cancellation (Linux headless, not Windows GUI)");

        void Capture(string name, Window? window = null)
        {
            if (shots == null) return; Directory.CreateDirectory(shots); using var frame = (window ?? owner).CaptureRenderedFrame(); frame!.Save(Path.Combine(shots, name + "-linux-headless.png"));
        }
    }
    static void Check(bool value, string error) { if (!value) throw new Exception(error); }
    static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    static void Invoke(Button button) { ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke(); Pump(); }
}
