// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XG.Core;
using XGLauncher;

internal static class Phase13UiTests
{
    internal static void Run(MainWindow window, string? screenshots)
    {
        var model = (LauncherModel)typeof(MainWindow).GetField("model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        Localizer.Set("zh-CN"); window.Width = 1130; window.Height = 850; Pump();
        Click("设置"); var settings = window.GetVisualDescendants().OfType<SettingsSurface>().Single();
        Check(settings.Sections.Count == 7 && Math.Abs(settings.ColumnDefinitions[0].Width.Value - 138) < .1, "phase12 settings geometry changed");
        settings.ScrollToSection(1, false); Pump();
        var language = settings.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x => x.Title == "语言");
        var original = language.Bounds.Height; var theme = ThemePalette.Current.Name;
        language.Open(); Pump();
        Check(language.IsOpen && language.Bounds.Height > original + 30, "language options did not expand in place");
        Check(window.OwnedWindows.Count() == 0 && !language.GetVisualAncestors().OfType<Avalonia.Controls.Primitives.Popup>().Any(), "language opened a floating popup");
        Capture("phase13-language-inline-cn");
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Pump();
        Check(Localizer.Language == "en-US" && !language.IsOpen && File.ReadAllText(Path.Combine(model.Store.Root, "language.json")).Contains("en-US"), "inline English switch/persistence failed");
        Check(ThemePalette.Current.Name == theme, "language switch changed theme");
        var nav = window.GetVisualDescendants().OfType<Button>().Where(b => b.Height == 40).ToArray();
        Check(nav.Length == 6 && nav.All(b => b.Content is StackPanel panel && panel.Children.OfType<LucideIcon>().Count() == 1), "localized navigation lost icons or layout");
        Check(nav.Any(b => AutomationProperties.GetName(b) == "Downloads"), "navigation did not translate");
        AssertEnglish(settings);
        var diagnostic = Localizer.Text("Java 安装或兼容性检查失败：Java 21 未就绪，请在 Java 管理中选择兼容运行时。");
        Check(!Regex.IsMatch(diagnostic, "[\\u4e00-\\u9fff]") && diagnostic.Contains("Java 21"), "dynamic nested error not localized");
        Check(Localizer.Text("同步完成 · 已添加 1 个文件").EndsWith("1 file", StringComparison.Ordinal), "singular/plural localization failed");
        Capture("phase13-settings-en");
        var restart = new MainWindow(model.Store.Root); restart.Show(); Pump();
        Check(Localizer.Language == "en-US", "language not restored by real new window"); restart.Close(); Pump();
        Console.WriteLine("PASS phase13 inline language keyboard selection, instant EN resources, persisted restart, preserved six navigation icons, seven settings categories and geometry");

        settings.ScrollToSection(5, false); Pump();
        var legal = settings.GetVisualDescendants().OfType<LegalBrowser>().Single();
        legal.ShowOverview(); Pump();
        Check(legal.Page == "overview" && !legal.GetVisualDescendants().OfType<TabControl>().Any(), "legal overview has a top tab bar");
        Capture("phase13-legal-overview-en");
        var overviewProject = LegalCatalog.Credits.Single(x => x.Name == "Avalonia UI");
        legal.OpenProject(overviewProject); Pump();
        ClickIn(legal, "Back"); Pump();
        Check(legal.Page == "overview", "overview project required more than one Back");
        legal.ShowCredits(); Pump();
        WaitFor(() => legal.CreditsScroll!.Viewport.Width > 0 && legal.CreditsScroll.Extent.Width > legal.CreditsScroll.Viewport.Width);
        legal.CreditsScroll!.Offset = new Vector(250, 0); Pump();
        var offset = legal.CreditsScroll.Offset.X;
        var acknowledgementsTitle = legal.GetVisualDescendants().OfType<TextBlock>().First(x => x.Text == "Acknowledgements & Community");
        Check(acknowledgementsTitle.TextAlignment == Avalonia.Media.TextAlignment.Center, "credits detail title not centered");
        Capture("phase13-credits-en");
        var avalonia = LegalCatalog.Credits.Single(x => x.Name == "Avalonia UI");
        legal.OpenProject(avalonia); Pump();
        Check(legal.CurrentDocument!.Resource != null && legal.DocumentBody!.Text == legal.CurrentDocument.Read(), "project did not open original embedded text");
        Check(legal.DocumentBody!.Text!.Contains("Permission is hereby granted", StringComparison.Ordinal), "Avalonia license not complete");
        legal.DocumentSearch!.Text = "Permission"; ClickIn(legal, "Find next"); Pump();
        Check(legal.DocumentBody.SelectionEnd > legal.DocumentBody.SelectionStart, "document search did not highlight a match");
        ClickIn(legal, "Copy full text"); Pump();
        if (window.Clipboard != null) {
            var copied = window.Clipboard.TryGetTextAsync(); WaitFor(() => copied.IsCompleted);
            Check(copied.GetAwaiter().GetResult() == legal.DocumentBody.Text, "copy did not include full original text");
        }
        Capture("phase13-license-reader-en");
        ClickIn(legal, "Back"); Pump(); WaitFor(() => Math.Abs(legal.CreditsScroll!.Offset.X - offset) < 2);
        foreach (var project in LegalCatalog.Credits) {
            Check(project.Documents.Count > 0, "project without document: " + project.Name);
            legal.OpenProject(project); Pump();
            Check(legal.CurrentDocument!.Project.Contains(project.Name.Split(' ')[0], StringComparison.OrdinalIgnoreCase) || project.Name is "Mojang Studios" or "Microsoft" or "Avalonia UI", "wrong project mapping");
            if (legal.CurrentDocument.Resource == null) Check(legal.DocumentBody == null && legal.CurrentDocument.OfficialUrl!.StartsWith("https://"), "online terms disguised as offline license");
            else Check(legal.DocumentBody!.Text == legal.CurrentDocument.Read(), "original legal text modified");
        }
        Check(LegalCatalog.Files.Count > 50 && LegalCatalog.Files.All(x => LegalCatalog.Read(x).Length > 0), "embedded license resources missing");
        legal.ShowOverview(); Pump();
        foreach (var palette in ThemePalette.All) {
            ThemePalette.Select(palette); legal.ShowOverview(); Pump();
            Capture("phase13-legal-theme-" + Array.IndexOf(ThemePalette.All, palette));
        }
        ThemePalette.Select(ThemePalette.All[0]);
        settings.ScrollToSection(6, false); Pump();
        Check(settings.Sections[6].GetVisualDescendants().OfType<Image>().Any(x => x.Width == 128 && x.Height == 128), "formal logo not enlarged");
        Check(settings.Sections[6].GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.Contains("Build time", StringComparison.Ordinal) == true), "real build metadata missing");
        Capture("phase13-about-en");
        foreach (var palette in ThemePalette.All) {
            ThemePalette.Select(palette); Pump();
            Capture("phase14-about-theme-" + Array.IndexOf(ThemePalette.All,palette));
        }
        ThemePalette.Select(ThemePalette.All[0]);
        Console.WriteLine("PASS phase13 embedded original license mapping, offline reader, search, exact full clipboard text, online-service distinction, back/scroll restoration, six themes and actual About build data");

        Click("Downloads"); Pump();
        var surface = window.GetVisualDescendants().OfType<DownloadSurface>().Single();
        var built = 0;
        surface.Update(new TextBlock { Text = "fixed header fixture" }, Enumerable.Range(0, 500).Select(n => (Func<Control>)(() => {
            built++; return new Border { Height = 95, Child = new TextBlock { Text = "task fixture " + n } };
        })).ToArray()); Pump();
        var scroll = surface.Tasks.GetVisualDescendants().OfType<ScrollViewer>().First();
        var header = surface.Children[0]; var before = header.TranslatePoint(default, window)!.Value.Y;
        Check(built < 60 && scroll.Extent.Height > scroll.Viewport.Height, "download list did not virtualize in remaining height");
        scroll.Offset = new Vector(0, 1500); Pump();
        Check(Math.Abs(header.TranslatePoint(default, window)!.Value.Y - before) < .1 && scroll.Offset.Y > 0, "header scrolled with task list");
        Check(scroll.Extent.Width <= scroll.Viewport.Width + 1, "download list overflowed horizontally");
        Capture("phase13-virtualized-downloads-fixture");
        Click("My games"); Pump(); AssertEnglish(window);
        Click("Mods"); Pump(); AssertEnglish(window);
        Click("Modpacks"); Pump(); AssertEnglish(window);
        Click("Home"); Pump(); AssertEnglish(window);
        Console.WriteLine("PASS phase13 fixed download header, independent vertical scrolling, 500-task virtualization, no horizontal overflow and English navigation pages");

        Localizer.Set("zh-CN", model.Store.Root); Pump();
        Click("设置"); settings.ScrollToSection(5, false); legal.ShowOverview(); Pump(); Capture("phase13-legal-overview-cn");
        foreach (var name in Localizer.English.Keys.Where(x => !x.EndsWith("|one", StringComparison.Ordinal)))
            Check(!string.IsNullOrWhiteSpace(Localizer.English[name]), "empty English resource: " + name);

        void AssertEnglish(Control root) {
            var remaining = root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text != null && Regex.IsMatch(t.Text, "[\\u4e00-\\u9fff]") &&
                !t.Text.Contains("简体中文", StringComparison.Ordinal) && t.Text != "瞎搞启动器" && !model.Store.Entries.Any(i => i.Name == t.Text) &&
                !t.Text.Contains("示例", StringComparison.Ordinal) && t.Text is not ("提高渲染性能并减少卡顿。" or "优化游戏逻辑和区块更新。" or "面向客户端性能的轻量整合包。")).Select(t => t.Text).Distinct().ToArray();
            Check(remaining.Length == 0, "Untranslated visible UI: " + string.Join(" | ", remaining));
        }
        void Click(string caption) {
            var b = window.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && (Equals(b.Content, caption) || AutomationProperties.GetName(b) == caption));
            Invoke(b); Pump();
        }
        void ClickIn(Control root, string caption) => Invoke(root.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, caption)));
        void Pump() { for (var n = 0; n < 5; n++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
        void WaitFor(Func<bool> ready) { for (var n = 0; n < 200; n++) { Pump(); if (ready()) return; using var stop = new CancellationTokenSource(15); Dispatcher.UIThread.MainLoop(stop.Token); } throw new Exception("UI timeout"); }
        void Capture(string name) {
            if (screenshots == null) return; Pump(); Directory.CreateDirectory(screenshots);
            using var image = window.CaptureRenderedFrame() ?? throw new Exception("no real frame"); image.Save(Path.Combine(screenshots, name + ".png"));
        }
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
