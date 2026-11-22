// SPDX-License-Identifier: MIT
using System.Net;
using System.Text;
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

internal static class ResponsiveUiTests
{
    internal static void Run(MainWindow window, string? screenshots)
    {
        if (!OperatingSystem.IsWindows()) window.FontFamily = new FontFamily("Noto Sans CJK SC");
        var title = window.GetVisualDescendants().OfType<Grid>().Single(grid => grid.Name == "LauncherTitleBar");
        Check(title.GetVisualDescendants().OfType<Button>().Count() == 5, "title bar must contain account, mode switch and three caption buttons");
        Check(title.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == "瞎搞启动器"), "Chinese title missing");
        Check(!title.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text is "XG Launcher" or "v0.1.0"), "duplicate title text");
        Click("最大化");
        Check(window.WindowState == WindowState.Maximized, "maximize button failed");
        Click("还原");
        Check(window.WindowState == WindowState.Normal, "restore button failed");

        Click("首页");
        Capture("16-home-navigation-title");
        Click("模组管理");
        Click("在线下载");
        var mods = Find<CatalogSurface>("ModCatalog");
        var query = mods.GetVisualDescendants().OfType<TextBox>().First(box => box.Watermark == "搜索模组或项目 ID");
        var filters = mods.GetVisualDescendants().OfType<UnifiedExpandableSelect>().ToArray();
        Check(filters.Select(x => x.Title).SequenceEqual(new[] { "版本", "加载器", "来源", "排序", "分类" }), "five uniform filters missing");
        Capture("00-filters");
        var loaderSelect = filters[1];
        var heightBefore = mods.Results.Bounds.Top;
        loaderSelect.Open();
        Pump();
        Check(loaderSelect.IsOpen && mods.Results.Bounds.Top == heightBefore, "select overlay shifted the catalogue");
        Capture("08-loader-expanded");
        var loaderOptions = window.GetVisualDescendants().OfType<ListBox>().Single(x => !ReferenceEquals(x, mods.Results));
        loaderOptions.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
        loaderOptions.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Check(!loaderSelect.IsOpen && Equals(loaderSelect.SelectedItem, "NeoForge"), "keyboard did not commit loader selection");
        loaderSelect.SelectedIndex = 0;
        filters[0].Open();
        Pump();
        var choiceSearch = window.GetVisualDescendants().OfType<TextBox>().Single(x => x.Watermark == "搜索选项");
        Capture("09-version-expanded");
        choiceSearch.Text = "fixture-179";
        Pump();
        var versionOptions = window.GetVisualDescendants().OfType<ListBox>().Single(x => !ReferenceEquals(x, mods.Results));
        Check(versionOptions.ItemCount == 1, "inline list search did not filter");
        Capture("10-version-filtered");
        choiceSearch.Text = "";
        Click("快照版");
        Check(versionOptions.ItemsSource!.Cast<object>().Select(x => x.ToString()).SequenceEqual(new[] { "全部", "25w04a" }),
            "snapshot filtering did not follow official metadata");
        Click("全部");
        Check(versionOptions.ItemsSource!.Cast<object>().Any(x => Equals(x, "rd-132211")), "historical types were lost from All");
        choiceSearch.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Check(!filters[0].IsOpen, "Escape did not close the overlay");
        Console.WriteLine("PASS unified five filters, overlay layout, keyboard selection, in-panel search, and Esc");
        query.Text = "sodium";
        Click("搜索");
        WaitFor(() => mods.Rows.Count == 2);
        Check(!mods.IsDetailOpen, "catalog initially open");
        Capture("01-mods-wide");
        var wide = mods.Results.Bounds.Width;
        Click("查看 Sodium 详情");
        WaitFor(() => mods.Details.Children.OfType<TextBlock>().Any(label => label.Text?.StartsWith("许可证") == true));
        Check(mods.IsDetailOpen && mods.Results.Bounds.Width < wide && mods.Compact, "catalog did not reserve detail width");
        Check(Buttons().Count(button => AutomationProperties.GetName(button) == "关闭详情") == 1, "catalog close count");
        CheckCatalogAlignment(mods);
        Capture("02-mods-detail");
        Click("查看 Lithium 详情");
        WaitFor(() => mods.Details.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "Lithium"));
        Click("关闭详情");
        Check(!mods.IsDetailOpen && Math.Abs(mods.Results.Bounds.Width - wide) <= 1, "closing pane did not restore width");

        window.Width = 730;
        WaitFor(() => window.ClientSize.Width < 800);
        Click("查看 Sodium 详情");
        Check(mods.Compact && mods.Results.Bounds.Width >= 230, "too little space for narrow catalog");
        Check(Buttons().Any(button => AutomationProperties.GetName(button) == "安装 Sodium"
            && button.Bounds.Width > 35), "narrow layout lost installation button");
        Capture("03-mods-narrow");
        Click("首页");
        Click("模组管理");
        Check(ReferenceEquals(mods, Find<CatalogSurface>("ModCatalog")) && query.Text == "sodium" && mods.Rows.Count == 2,
            "catalog query or results were reset by navigation");

        Click("整合包");
        var packs = Find<CatalogSurface>("PackCatalog");
        Click("搜索");
        WaitFor(() => packs.Rows.Count == 1);
        Click("查看 Simply Optimized 详情");
        WaitFor(() => packs.Details.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "Simply Optimized"));
        Check(packs.IsDetailOpen && packs.Compact, "pack detail did not open");
        CheckCatalogAlignment(packs);
        Capture("04-packs-narrow");
        Click("关闭详情");
        Check(!packs.IsDetailOpen, "pack detail did not close");
        Click("本地整合包");
        Click("整合包市场");
        Check(ReferenceEquals(packs, Find<CatalogSurface>("PackCatalog")) && packs.Rows.Count == 1, "pack state was reset by tab");

        Click("模组管理");
        query.Text = "none";
        Click("搜索");
        WaitFor(() => (mods.Feedback.Content as TextBlock)?.Text == "没有找到符合条件的内容");
        query.Text = "stress-retry";
        Click("搜索");
        WaitFor(() => mods.Rows.Count == 30);
        var scrollMods = mods.Results.TranslatePoint(new Point(160, 100), window) ?? throw new Exception("Mod list detached");
        for (var wheel = 0; wheel < 8 && mods.Feedback.Content is not Button; wheel++)
        {
            window.MouseWheel(scrollMods, new Vector(0, -20));
            Pump();
        }
        WaitFor(() => mods.Feedback.Content is Button);
        Check(mods.Rows.Count == 30, "pagination error cleared first page");
        Click("加载失败，点击重试");
        WaitFor(() => mods.Rows.Count == 59);
        Check(mods.Results.GetVisualDescendants().OfType<ScrollViewer>().First().Offset.Y > 0,
            "page retry reset scrolling");

        Click("整合包");
        var packQuery = packs.GetVisualDescendants().OfType<TextBox>().First(box => box.Watermark == "搜索整合包或项目 ID");
        for (var i = 0; i < 14; i++)
        {
            Click("模组管理");
            query.Text = "stress" + i;
            Click("搜索");
            Click("整合包");
            packQuery.Text = "stress" + i;
            Click("搜索");
        }
        WaitFor(() => packs.Rows.Count == 30);
        var listPoint = packs.Results.TranslatePoint(new Point(200, 80), window) ?? throw new Exception("Catalog disconnected");
        for (var page = 2; page <= 6; page++)
        {
            var viewport = packs.Results.GetVisualDescendants().OfType<ScrollViewer>().First();
            for (var wheel = 0; wheel < 12 && packs.Rows.Count < page * 30; wheel++)
            {
                window.MouseWheel(listPoint, new Vector(0, -20));
                Pump();
            }
            WaitFor(() => packs.Rows.Count == page * 30);
            Check(viewport.Offset.Y > 0, "pagination reset the scroll position");
        }
        Check((packs.Feedback.Content as TextBlock)?.Text == "已经到底了，没有更多内容了" &&
            packs.Results.GetVisualDescendants().OfType<ListBoxItem>().Count() < 100,
            "end state was missing or the full list was realized");
        Capture("12-modpacks-paginated");
        packQuery.Text = "cancel-on-close";
        Click("搜索");
        Click("设置");
        var settings = Find<SettingsSurface>("SettingsSurface");
        var scroll = settings.ContentScroll;
        WaitFor(() => scroll.Extent.Height > scroll.Viewport.Height && scroll.Viewport.Height > 0);
        Capture("05-settings");
        Check(!window.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Any(x => x.Title is "下载限速" or "文件下载并发数"), "duplicate download settings remain");
        Click("备份与恢复");
        try { WaitFor(() => settings.ActiveSection == 3 && Math.Abs(scroll.Offset.Y - settings.Sections[3].Bounds.Top) < 2); } catch { Console.WriteLine($"Scroll diagnostic active={settings.ActiveSection}, offset={scroll.Offset.Y}, target={settings.Sections[3].Bounds.Top}, viewport={scroll.Viewport.Height}, extent={scroll.Extent.Height}"); throw; }
        Click("常规");
        Click("许可证与鸣谢");
        WaitFor(() => settings.ActiveSection == 5 && Math.Abs(scroll.Offset.Y - settings.Sections[5].Bounds.Top) < 2);
        Advance(TimeSpan.FromMilliseconds(60));
        var point = scroll.TranslatePoint(new Point(40, 70), window) ?? throw new Exception("scroll is detached");
        for (var step = 0; step < 24 && settings.ActiveSection > 2; step++)
        {
            window.MouseWheel(point, new Vector(0, 1));
            Pump();
            Advance(TimeSpan.FromMilliseconds(25));
        }
        Check(settings.ActiveSection == 2, $"manual wheel did not select the correct category (actual {settings.ActiveSection}, offset {scroll.Offset.Y})");
        Click("关于");
        WaitFor(() => settings.ActiveSection == 6);
        WaitFor(() => settings.ActiveSection == 6 && Math.Abs(scroll.Offset.Y - (scroll.Extent.Height - scroll.Viewport.Height)) < .5);
        Capture("06-settings-about");
        point = scroll.TranslatePoint(new Point(40, 70), window) ?? throw new Exception("scroll is detached");
        window.MouseWheel(point, new Vector(0, -1));
        WaitFor(() => settings.ShowsEndMessage);
        Capture("07-settings-end-feedback");
        WaitFor(() => !settings.ShowsEndMessage);
        window.MouseWheel(point, new Vector(0, -1));
        Pump();
        Check(!settings.ShowsEndMessage, "bottom feedback retriggered without leaving bottom");
        var bar = scroll.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>()
            .First(item => item.Name == "PART_VerticalScrollBar" && item.TemplatedParent == scroll);
        WaitFor(() => bar.Opacity < .01);
        window.Height = 510;
        WaitFor(() => window.ClientSize.Height < 550);
        foreach (var page in new[] { "首页", "个人游玩", "整合包", "模组管理", "下载管理", "设置" })
        {
            Click(page);
            Check(!Buttons().Any(button => button.Content is string label && label == "▶  启动游戏"),
                $"global launch toolbar still displayed: {page}");
            if (page == "个人游玩")
                Check(Buttons().Any(button => button.Content is string label && label is "启动" or "下载" or "校验中"),
                    "narrow Personal page lost the instance launch button");
        }
        Check(ReferenceEquals(Find<SettingsSurface>("SettingsSurface"), settings), "settings page recreated");
        Click("下载管理");
        var concurrencySelect = window.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x => x.Title == "文件下载并发数");
        concurrencySelect.Open();
        Pump();
        Check(concurrencySelect.IsOpen, "download concurrency select could not open");
        Capture("11-download-settings-narrow");
        concurrencySelect.Close();
        Click("个人游玩");
        Click("创建实例");
        WaitFor(() => window.OwnedWindows.OfType<Window>().Any(x => x.Title == "创建实例"));
        var create = window.OwnedWindows.OfType<Window>().Single(x => x.Title == "创建实例");
        Capture("23-create-instance", create);
        create.Height = 375;
        Pump();
        var gameSelect = create.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x => x.Title == "Minecraft 版本");
        Check(!create.GetVisualDescendants().OfType<TextBox>().Any(x => x.Watermark == "筛选版本号"), "standalone version filter remains");
        Check(create.SystemDecorations == SystemDecorations.None, "editor has native caption");
        Check(create.IsVisible && !create.OwnedWindows.Any() &&
            create.GetVisualDescendants().OfType<TextBox>().Any(x => x.Watermark == "实例名称（可选，留空自动命名）") &&
            !create.GetVisualDescendants().OfType<TextBlock>().Any(x => x.IsEffectivelyVisible && x.Text == "请输入实例名称。"),
            "blank name is still required");
        Check(!create.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x => x.Title == "加载器版本").IsEffectivelyVisible,
            "Vanilla still shows a loader version selector");
        Capture("17-optional-instance-name", create);
        var memoryMode = create.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x => x.Title == "内存分配");
        var resolutionMode = create.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x => x.Title == "分辨率");
        memoryMode.SelectedIndex = 1;
        resolutionMode.SelectedIndex = 1;
        create.GetVisualDescendants().OfType<TextBox>().Single(x => x.Watermark == "内存 MB").Text = "0";
        create.GetVisualDescendants().OfType<TextBox>().Single(x => x.Watermark == "宽度").Text = "0";
        ClickIn(create, "保存");
        var messages = create.GetVisualDescendants().OfType<TextBlock>().Where(x => x.IsEffectivelyVisible).Select(x => x.Text).ToArray();
        Check(messages.Any(x => x?.Contains("整数内存值") == true) && messages.Any(x => x?.Contains("整数分辨率") == true),
            "memory and resolution ranges were not reported inline");
        memoryMode.SelectedIndex = 0;
        resolutionMode.SelectedIndex = 0;
        gameSelect.Open();
        Pump();
        Check(gameSelect.IsOpen, "new instance version control did not open");
        Capture("13-create-instance-version", create);
        gameSelect.Close();
        var advanced = create.GetVisualDescendants().OfType<StackPanel>().Single(x => x.Name == "InstanceParameters");
        Check(create.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Any(x=>x.Title == "Java 运行环境"), "integrated Java selector missing");
        Check(!create.GetVisualDescendants().OfType<Button>().Any(x=>Equals(x.Content,"Java 管理")||Equals(x.Content,"检测 Java")), "legacy Java buttons remain");
        Check(!create.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text == "高级设置"), "advanced disclosure remains");
        var scrollEditor = create.GetVisualDescendants().OfType<ScrollViewer>().Single(x => x.Name == "DialogScroll");
        var footer = create.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "DialogFooter");
        Pump();
        scrollEditor.Offset = new Vector(0, scrollEditor.Extent.Height);
        Pump();
        var footerTop = footer.TranslatePoint(default, create)?.Y ?? -1;
        var end = advanced.GetVisualDescendants().OfType<TextBlock>().Last(x => x.IsEffectivelyVisible);
        var lastBottom = end.TranslatePoint(new(0, end.Bounds.Height), create)?.Y ?? double.MaxValue;
        Check(lastBottom <= footerTop && lastBottom > 35 && scrollEditor.Offset.Y > 0,
            $"advanced settings last field obscured: {lastBottom}, footer {footerTop}");
        Capture("18-advanced-scrolled-small-dialog", create);
        var javaSelect = create.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x => x.Title == "Java 运行环境");
        javaSelect.BringIntoView();
        Pump();
        javaSelect.Open();
        Pump();
        Check(javaSelect.IsOpen && Equals(javaSelect.SelectedItem, "自动选择（推荐）"), "Java auto default missing");
        Capture("19-java-selection", create);
        javaSelect.Close();
        var loader = create.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x => x.Title == "加载器类型");
        var loaderVersion = create.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x => x.Title == "加载器版本");
        loader.SelectedItem = "Fabric";
        WaitFor(() => Equals(loaderVersion.SelectedItem, "0.16.5"));
        loaderVersion.SelectedItem = "0.16.4";
        gameSelect.SelectedItem = "25w04a";
        WaitFor(() => loaderVersion.IsEnabled && Equals(loaderVersion.SelectedItem, "0.16.4"));
        loaderVersion.BringIntoView();
        Pump();
        loaderVersion.Open();
        Pump();
        Capture("20-loader-version-recommended", create);
        loaderVersion.Close();
        ClickIn(create, "保存");
        WaitFor(() => !create.IsVisible);
        Check(window.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "Minecraft 25w04a Fabric"), "valid instance was not saved");
        Click("编辑");
        WaitFor(() => window.OwnedWindows.OfType<Window>().Any(x => x.Title == "编辑实例"));
        var edit = window.OwnedWindows.OfType<Window>().Single(x => x.Title == "编辑实例");
        Capture("21-edit-instance", edit);
        var loaderType = edit.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x => x.Title == "加载器类型");
        loaderType.Open();
        Pump();
        Check(loaderType.IsOpen, "edit instance loader control did not open");
        Capture("14-edit-instance-loader", edit);
        edit.Close();
        Pump();
        window.Width = 575;
        Pump();
        Click("模组管理");
        query.Text = "sodium";
        Click("搜索");
        WaitFor(() => mods.Rows.Count == 2);
        Click("查看 Sodium 详情");
        Check(mods.Results.Bounds.Width > 350 && mods.IsDetailOpen, "extra narrow catalogue did not stack its panels");
        Capture("24-extra-narrow-catalogue");
        var errorTask = window.ErrorAsync(window, "无法校验下载文件，请重试。");
        WaitFor(() => window.OwnedWindows.OfType<LauncherDialog>().Any(x => x.Title == "操作失败"));
        var error = window.OwnedWindows.OfType<LauncherDialog>().Single(x => x.Title == "操作失败");
        Capture("22-launcher-error-dialog", error);
        ClickIn(error, "关闭");
        WaitFor(() => errorTask.IsCompleted);
        Console.WriteLine("PASS fixed dialog footer, advanced scroll, inline validation, Fabric recommendation, preserved loader, saved new instance, and XG error dialog");
        Check(!window.GetVisualDescendants().OfType<ComboBox>().Any(), "legacy ComboBox is still visible");
        Console.WriteLine("PASS single caption, six-page reflow, mod/pack details, narrow row install, preserved search, multi-page append, empty/end states, retry and deduplication, 28 alternating searches, virtualized fast scroll, scroll-linked settings and bottom feedback");

        void CheckCatalogAlignment(CatalogSurface surface)
        {
            var card = surface.Results.GetVisualDescendants().OfType<Border>().First(x => x.Name == "CatalogResultCard");
            var detailFrame = surface.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "CatalogDetailsFrame");
            var left = card.TranslatePoint(default, surface)?.Y ?? double.MinValue;
            var right = detailFrame.TranslatePoint(default, surface)?.Y ?? double.MaxValue;
            Check(Math.Abs(left - right) <= 1.1, $"catalog columns misaligned: {left}, {right}");
        }

        void ClickIn(Window owner, string caption)
        {
            var button = owner.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && Equals(b.Content, caption));
            ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
            Pump();
        }

        T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(x => x.Name == name);
        IEnumerable<Button> Buttons() => window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible);
        void Click(string title)
        {
            var button = Buttons().First(b => AutomationProperties.GetName(b) == title || b.Content is string text && text == title ||
                b.Content is StackPanel row && row.Children.OfType<TextBlock>().Any(x => x.Text == title));
            ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
            Pump();
        }

        void Capture(string filename, Window? target = null)
        {
            if (string.IsNullOrWhiteSpace(screenshots)) return;
            Directory.CreateDirectory(screenshots);
            Advance(TimeSpan.FromMilliseconds(210));
            using var png = (target ?? window).CaptureRenderedFrame() ?? throw new Exception("Renderer did not produce a frame.");
            png.Save(Path.Combine(screenshots, filename + ".png"));
        }

        void Pump()
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        void Advance(TimeSpan duration)
        {
            using var until = new CancellationTokenSource(duration);
            Dispatcher.UIThread.MainLoop(until.Token);
        }

        void WaitFor(Func<bool> condition)
        {
            for (var retry = 0; retry < 400; retry++)
            {
                Pump();
                if (condition()) return;
                Advance(TimeSpan.FromMilliseconds(10));
            }
            throw new Exception("UI state did not settle before timeout.");
        }
    }

    private static void Check(bool condition, string problem)
    {
        if (!condition) throw new InvalidOperationException(problem);
    }
}

internal sealed class CatalogFixture : HttpMessageHandler
{
    private int retryFailures;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var uri = request.RequestUri!;
        if (uri.Host == "meta.fabricmc.net")
            return Reply("""[{"loader":{"version":"0.17.0-beta","stable":false}},{"loader":{"version":"0.16.5","stable":true}},{"loader":{"version":"0.16.4","stable":true}}]""");
        var isPack = Uri.UnescapeDataString(uri.Query).Contains("modpack");
        if (uri.AbsolutePath == "/v2/search" && uri.Query.Contains("query=none"))
            return Reply("""{"hits":[],"total_hits":0}""");
        if (uri.AbsolutePath == "/v2/search" && Uri.UnescapeDataString(uri.Query).Contains("stress"))
        {
            var offset = int.Parse(uri.Query.Split('&').Single(part => part.StartsWith("offset=", StringComparison.Ordinal))[7..]);
            var retry = uri.Query.Contains("query=stress-retry") && offset == 30;
            if (retry && Interlocked.Increment(ref retryFailures) == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var hits = Enumerable.Range(0, 180).Skip(offset).Take(30).Select(i => retry && i == 30 ? 29 : i).Select(i => new
            {
                project_id = $"fixture-{i}", slug = $"fixture-{i}", title = $"Fixture {i}",
                icon_url = $"https://example.invalid/icon-{i}.png", description = "catalog stress fixture",
                downloads = i, versions = new[] { "1.21.1" }, categories = new[] { "fabric" }
            });
            return Reply(System.Text.Json.JsonSerializer.Serialize(new { hits, total_hits = 180 }));
        }
        var body = uri.AbsolutePath switch
        {
            "/v2/search" => isPack
                ? """{"hits":[{"project_id":"pack","slug":"simply-optimized","title":"Simply Optimized","description":"面向客户端性能的轻量整合包。","author":"示例作者","downloads":100,"versions":["1.21.1"],"categories":["fabric"]}]}"""
                : """{"hits":[{"project_id":"sodium","slug":"sodium","title":"Sodium","description":"提高渲染性能并减少卡顿。","author":"示例作者","downloads":100,"versions":["1.21.1"],"categories":["fabric"]},{"project_id":"lithium","slug":"lithium","title":"Lithium","description":"优化游戏逻辑和区块更新。","author":"示例作者","downloads":99,"versions":["1.21.1"],"categories":["fabric"]}]}""",
            not null when uri.AbsolutePath.EndsWith("/version", StringComparison.Ordinal) => """
                [{"id":"version","version_number":"示例版本","game_versions":["1.21.1"],"loaders":["fabric"],"dependencies":[],"files":[{"primary":true,"filename":"file.jar","url":"https://cdn.modrinth.com/data/file.jar","size":100,"hashes":{"sha512":"fixture"}}]}]
                """,
            _ => """{"license":{"id":"示例许可证"}}"""
        };
        return Reply(body);
    }

    private static Task<HttpResponseMessage> Reply(string body) => Task.FromResult(
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
}
