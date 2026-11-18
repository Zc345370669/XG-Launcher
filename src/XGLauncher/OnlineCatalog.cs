// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using XG.Core;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private CatalogIcons? catalogIcons;
    private readonly CancellationTokenSource catalogLifetime = new();
    private static readonly HttpClient IconClient = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private CatalogSurface? modsOnline;
    private CatalogSurface? packsOnline;
    private static readonly Dictionary<string, string> ChineseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sodium"] = "钠", ["lithium"] = "锂", ["iris"] = "鸢尾花光影",
        ["jei"] = "JEI 物品管理器", ["fabric-api"] = "Fabric API",
        ["create"] = "机械动力", ["appleskin"] = "苹果皮", ["jade"] = "玉",
        ["journeymap"] = "旅行地图", ["alexs-mobs"] = "Alex 的生物"
    };

    private CatalogSurface OnlineCatalog(bool pack)
    {
        ref var stored = ref (pack ? ref packsOnline : ref modsOnline);
        if (stored == null) stored = CreateCatalog(pack);
        return stored;
    }

    private CatalogSurface CreateCatalog(bool pack)
    {
        var query = Input(pack ? "搜索整合包或项目 ID" : "搜索模组或项目 ID");
        var versionPicker = new MinecraftVersionPicker("版本", true);
        versionPicker.Update(model.Game.CachedVersions());
        var game = versionPicker.Select;
        var choice = Picker(["全部", "NeoForge", "Forge", "Fabric", "Quilt"], title: "加载器");
        var source = Picker(["全部", "Modrinth", "CurseForge 原站", "MC百科原站"], title: "来源");
        game.MinWidth = 150;
        choice.MinWidth = 115;
        source.MinWidth = 130;
        var order = Picker(["相关度", "下载量", "更新时间"], title: "排序");
        string[] sortKeys = ["relevance", "downloads", "updated"];
        var category = Picker(["全部", "科技", "冒险", "武器战斗", "建筑装饰", "生物", "性能", "世界生成", "HUD", "前置 API"], title: "分类");
        string[] categoryKeys = ["", "technology", "adventure", "equipment", "decoration", "mobs", "optimization", "worldgen", "utility", "library"];
        var filters = new WrapPanel();
        var detail = Column();
        CatalogSurface surface = null!;
        Guid? detailTarget = model.Selected?.Id;
        CancellationTokenSource? detailJob = null;
        var detailGeneration = 0;
        var searchGeneration = 0;
        CancellationTokenSource? searchJob = null;
        (string Query, string Game, string Loader, string Category, string Sort)? request = null;
        const int pageSize = 30;
        var offset = 0;
        var more = false;
        var failed = false;
        var resumeOnAttach = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void CloseDetails()
        {
            detailGeneration++;
            detailJob?.Cancel();
            detailJob = null;
            surface.SetDetails(false);
            detail.Children.Clear();
        }
        var close = Action("关闭", () => { CloseDetails(); return Task.CompletedTask; });
        close.Content = new LucideIcon("x", 14); ToolTip.SetTip(close,"关闭");
        close.Name = "CatalogDetailClose";
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new(0, 0, 0, 8);
        AutomationProperties.SetName(close, "关闭详情");

        var search = new Button { Content = "搜索", Background = ThemePalette.Accent, Foreground = ThemePalette.OnAccent, Padding = new(14, 8) };
        search.Click += async (_, _) => await SearchAsync();
        query.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && search.IsEnabled) search.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        };
        var searchBar = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        searchBar.Children.Add(query);
        Grid.SetColumn(search, 1); searchBar.Children.Add(search);
        var header = Column(Label(pack ? "整合包" : "模组管理", 22, true),
            pack ? Tabs(["整合包市场", "本地整合包"], packTab, i => packTab = i)
                 : Tabs(["已安装模组", "在线下载", "模组更新"], modTab, i => modTab = i),
            searchBar);

        foreach (var filter in new[] { game, choice, source, order, category })
        {
            filter.Margin = new Thickness(0, 0, 6, 5);
            filters.Children.Add(filter);
        }

        surface = new CatalogSurface(header, filters, close, detail);
        surface.Name = pack ? "PackCatalog" : "ModCatalog";
        surface.AttachedToVisualTree += (_, _) =>
        {
            if (!pack && surface.IsDetailOpen && detailTarget != model.Selected?.Id) CloseDetails();
            if (resumeOnAttach)
            {
                resumeOnAttach = false;
                Dispatcher.UIThread.Post(() => _ = LoadPageAsync(), DispatcherPriority.Background);
            }
        };
        surface.SetFeedback(Label(pack ? "搜索 Modrinth 整合包" : "搜索 Modrinth 模组"));
        surface.DetachedFromVisualTree += (_, _) =>
        {
            resumeOnAttach = searchJob != null && request != null;
            searchJob?.Cancel();
            searchJob = null;
            if (detailJob != null) CloseDetails();
            searchGeneration++;
        };

        surface.NearEnd += () => { if (!failed) _ = LoadPageAsync(); };
        query.TextChanged += async (_, _) => await SearchAsync(debounce: true);
        foreach (var filter in new[] { game, choice, source, order, category })
            filter.SelectionChanged += async (_, _) => await SearchAsync();
        game.Opening += async (_, _) =>
        {
            if (game.IsLoading) return;
            game.IsLoading = true;
            try
            {
                var versions = await model.Game.OfficialVersionsAsync(token: catalogLifetime.Token);
                versionPicker.Update(versions);
            }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or System.Text.Json.JsonException)
            { CatalogDiagnostic("catalog-versions: " + error.GetType().Name); }
            finally { game.IsLoading = false; }
        };
        return surface;

        Task SearchAsync(bool debounce = false)
        {
            searchGeneration++;
            searchJob?.Cancel();
            searchJob = null;
            failed = false;
            resumeOnAttach = false;
            CloseDetails();
            surface.Rows.Clear();
            seen.Clear();
            offset = 0;
            more = false;
            request = null;
            var value = query.Text?.Trim() ?? "";
            var gameVersion = game.SelectedIndex > 0 ? (string)game.SelectedItem! : "";
            var loaderFilterValue = choice.SelectedIndex > 0 ? (string)choice.SelectedItem! : "";
            if (source.SelectedIndex >= 2)
            {
                var link = source.SelectedIndex == 2 ? ("CurseForge 原站", "https://www.curseforge.com/minecraft") :
                    ("MC百科原站", "https://search.mcmod.cn/s?key=" + Uri.EscapeDataString(value));
                surface.SetFeedback(Link(link.Item1, link.Item2));
                return Task.CompletedTask;
            }

            request = (value, gameVersion, loaderFilterValue, categoryKeys[Math.Max(0, category.SelectedIndex)],
                sortKeys[Math.Max(0, order.SelectedIndex)]);
            more = true;
            return LoadPageAsync(debounce);
        }

        async Task LoadPageAsync(bool debounce = false)
        {
            if (!more || searchJob != null || request is not { } selected || catalogLifetime.IsCancellationRequested) return;
            var generation = searchGeneration;
            var next = offset;
            using var job = CancellationTokenSource.CreateLinkedTokenSource(catalogLifetime.Token);
            job.CancelAfter(TimeSpan.FromSeconds(25));
            searchJob = job;
            failed = false;
            surface.SetFeedback(Label(next == 0 ? "正在搜索…" : "正在加载更多内容…"));
            try
            {
                if (debounce) await Task.Delay(350, job.Token);
                var page = await model.Mods.SearchPageAsync(selected.Query, pack, selected.Game, selected.Loader,
                    selected.Category, selected.Sort, next, pageSize, job.Token);
                if (generation != searchGeneration) return;
                job.Token.ThrowIfCancellationRequested();
                foreach (var project in page.Results)
                {
                    if (!seen.Add(project.Id.Length > 0 ? project.Id : project.Slug)) continue;
                    surface.Rows.Add(() => CatalogRow(project, selected.Game, selected.Loader,
                        () => ShowAsync(project, selected.Game, selected.Loader),
                        () => InstallProjectAsync(project, null, selected.Game, selected.Loader)));
                }

                offset = checked(next + page.Results.Length);
                more = page.Results.Length >= pageSize && (page.TotalHits == null || offset < page.TotalHits.Value);
                if (next == 0) surface.ScrollToFirst();
                surface.SetFeedback(seen.Count == 0 ? Label("没有找到符合条件的内容") :
                    more ? null : Label("已经到底了，没有更多内容了"));
            }
            catch (Exception error)
            {
                if (generation != searchGeneration || catalogLifetime.IsCancellationRequested) return;
                failed = true;
                surface.SetFeedback(Action("加载失败，点击重试", () => LoadPageAsync()));
                if (error is not OperationCanceledException) CatalogDiagnostic("catalog-search: " + error.GetType().Name);
            }
            finally { if (ReferenceEquals(searchJob, job)) searchJob = null; }
        }

        async Task ShowAsync(Project project, string version, string loaderChoice)
        {
            var generation = ++detailGeneration;
            detailJob?.Cancel();
            using var job = CancellationTokenSource.CreateLinkedTokenSource(catalogLifetime.Token);
            detailJob = job;
            detail.Children.Clear();
            surface.ResetDetailsOffset();
            var identity = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 9 };
            identity.Children.Add(CatalogIcon(project));
            var title = Column(Label(ChineseNames.GetValueOrDefault(project.Slug, project.Name), 18, true), Label(project.Name));
            title.Spacing = 3;
            Grid.SetColumn(title, 1); identity.Children.Add(title);
            detail.Children.Add(identity);
            detail.Children.Add(Label($"作者  {project.Author}"));
            detail.Children.Add(Label(project.Description));
            detail.Children.Add(Link("查看 Modrinth", project.Url));
            var feedback = Label("正在读取版本与依赖…");
            detail.Children.Add(feedback);
            surface.SetDetails(true);
            try
            {
                var instance = model.Selected;
                detailTarget = instance?.Id;
                if (!project.Pack && instance != null) detail.Children.Add(Label($"目标实例  {instance.Name}"));
                var versions = await RelevantVersionsAsync(project, instance, version, loaderChoice, job.Token);
                if (generation != detailGeneration) return;
                if (versions.Length == 0)
                {
                    feedback.Text = !project.Pack && instance is null ? "请选择实例以查看兼容版本。" : "未找到符合条件的文件。";
                    return;
                }
                var versionsBox = Picker(versions.Select(item => item.Number).ToArray(), title: "项目版本", searchable: true);
                versionsBox.HorizontalAlignment = HorizontalAlignment.Stretch;
                detail.Children.Add(versionsBox);
                var selected = Label("");
                detail.Children.Add(selected);
                var install = Action("安装选中版本", async () =>
                {
                    if (versionsBox.SelectedIndex >= 0)
                        await InstallProjectAsync(project, versions[versionsBox.SelectedIndex], version, loaderChoice, instance);
                    Describe();
                }, true);
                detail.Children.Add(install);
                versionsBox.SelectionChanged += (_, _) => Describe();
                Describe();
                feedback.Text = "版本";
                var license = await model.Mods.LicenseAsync(project, job.Token);
                if (generation == detailGeneration) detail.Children.Add(Label($"许可证  {license}"));

                void Describe()
                {
                    if (versionsBox.SelectedIndex < 0) return;
                    var candidate = versions[versionsBox.SelectedIndex];
                    var installed = !project.Pack && instance is not null &&
                        model.LocalFiles(instance).Any(f => Path.GetFileName(f) is { } file &&
                            (file.Equals(candidate.File.Name, StringComparison.OrdinalIgnoreCase) ||
                             file.Equals(candidate.File.Name + ".disabled", StringComparison.OrdinalIgnoreCase)));
                    selected.Text = $"Minecraft  {ShortList(candidate.GameVersions)}\n加载器  {ShortList(candidate.Loaders)}\n依赖  {string.Join("、", candidate.Required.DefaultIfEmpty("无"))}\n冲突  {string.Join("、", candidate.Incompatible.DefaultIfEmpty("无"))}\n状态  {(project.Pack ? "目标为新实例" : installed ? "已安装" : "未安装")}";
                }
            }
            catch (OperationCanceledException) when (job.IsCancellationRequested) { }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or
                InvalidDataException or KeyNotFoundException or InvalidOperationException or UriFormatException or IOException)
            {
                CatalogDiagnostic("catalog-details: " + ex.GetType().Name);
                if (generation == detailGeneration) feedback.Text = "读取详情失败，请检查网络后重试。";
            }
            finally
            {
                if (ReferenceEquals(detailJob, job)) detailJob = null;
            }
        }
    }

    private Control CatalogRow(Project project, string version, string loader, Func<Task> open, Func<Task> install)
    {
        var info = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 10 };
        info.Children.Add(CatalogIcon(project));
        var primaryName = ChineseNames.GetValueOrDefault(project.Slug, project.Name);
        var labels = new StackPanel { Spacing = 3 };
        labels.Children.Add(SingleLine(primaryName, 15, true));
        if (primaryName != project.Name) labels.Children.Add(SingleLine(project.Name, 12));
        labels.Children.Add(SingleLine(project.Description, 12));
        labels.Children.Add(SingleLine($"MC {FilterText(version.Length == 0 ? ShortList(project.GameVersions) : version)}  ·  {(loader.Length == 0 ? ShortList(project.Loaders) : loader)}  ·  Modrinth", 11));
        Grid.SetColumn(labels, 1); info.Children.Add(labels);
        var openButton = Action(project.Name, open);
        openButton.Content = info;
        openButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        openButton.VerticalContentAlignment = VerticalAlignment.Center;
        openButton.BorderThickness = new Thickness(0);
        openButton.Padding = new Thickness(6);
        AutomationProperties.SetName(openButton, $"查看 {project.Name} 详情");
        ToolTip.SetTip(openButton, project.Description);
        var installButton = Action("安装", install, true);
        installButton.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(installButton, $"安装 {project.Name}");
        var layout = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        layout.Children.Add(openButton);
        Grid.SetColumn(installButton, 1); layout.Children.Add(installButton);
        var card = Frame(layout, new Thickness(6, 4, 9, 4));
        card.Name = "CatalogResultCard";
        return card;
    }

    private static string FilterText(string? value) => string.IsNullOrWhiteSpace(value) ? "全部" :
        value.Length > 17 ? value[..16] + "…" : value;
    private static string ShortList(string[] values) => values.Length == 0 ? "未标注" :
        string.Join(" / ", values.TakeLast(2)) + (values.Length > 2 ? $" +{values.Length - 2}" : "");

    private static TextBlock SingleLine(string text, double size, bool bold = false) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private Control CatalogIcon(Project project)
    {
        var image = new Image { Stretch = Stretch.UniformToFill };
        var fallback = new LucideIcon(project.Pack ? "layers" : "package", 21) { Foreground = Muted };
        var layers = new Grid();
        layers.Children.Add(fallback);
        layers.Children.Add(image);
        var icon = new Border { Width = 44, Height = 44, CornerRadius = new(7),
            Background = ThemePalette.Hover, ClipToBounds = true, Child = layers };
        CancellationTokenSource? active = null;
        icon.AttachedToVisualTree += (_, _) =>
        {
            active?.Cancel();
            active = CancellationTokenSource.CreateLinkedTokenSource(catalogLifetime.Token);
            _ = ApplyIconAsync(active);
        };
        icon.DetachedFromVisualTree += (_, _) =>
        {
            active?.Cancel();
            active = null;
            if (image.Source is Bitmap old)
            {
                image.Source = null;
                old.Dispose();
            }
        };

        async Task ApplyIconAsync(CancellationTokenSource job)
        {
            try
            {
                var loader = catalogIcons ??= new CatalogIcons(IconClient, CatalogDiagnostic);
                var bitmap = await loader.LoadAsync(project.Icon, job.Token);
                if (bitmap == null) return;
                if (ReferenceEquals(active, job) && !job.IsCancellationRequested) image.Source = bitmap;
                else bitmap.Dispose();
            }
            catch (Exception error) { CatalogDiagnostic("catalog-ui: " + error.GetType().Name); }
            finally
            {
                if (ReferenceEquals(active, job)) active = null;
                job.Dispose();
            }
        }
        return icon;
    }

    private void CatalogDiagnostic(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(model.LogsDirectory, "catalog-diagnostic.log"),
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Trace.TraceWarning("Catalog diagnostic unavailable: " + error.GetType().Name); }
    }

    private async Task<ProjectVersion[]> RelevantVersionsAsync(Project project, Instance? target,
        string version, string loader, CancellationToken token = default)
    {
        if (!project.Pack && target is null) return [];
        var choices = await model.Mods.VersionsAsync(project, project.Pack ? null : target, token);
        if (!project.Pack) return choices;
        return choices.Where(candidate =>
            (version.Length == 0 || candidate.GameVersions.Contains(version)) &&
            (loader.Length == 0 || candidate.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase))).ToArray();
    }

    private async Task InstallProjectAsync(Project project, ProjectVersion? chosen, string version,
        string loader, Instance? expectedInstance = null)
    {
        var instance = expectedInstance ?? model.Selected;
        if (expectedInstance is { } expected && !project.Pack)
        {
            instance = model.Store.Entries.FirstOrDefault(item => item.Id == expected.Id);
            if (instance == null || instance.Version != expected.Version || instance.Loader != expected.Loader ||
                instance.LoaderVersion != expected.LoaderVersion)
                throw new InvalidOperationException("目标实例已变化，请重新打开详情并选择版本。");
        }
        if (!project.Pack)
        {
            if (instance is null) throw new InvalidOperationException("请先选择游戏实例。");
            EnsureIdle(instance);
            if (instance.Loader == "原版") throw new InvalidOperationException("请选择使用模组加载器的实例。");
        }
        var file = chosen ?? (await RelevantVersionsAsync(project, instance, version, loader)).FirstOrDefault();
        if (file is null) throw new InvalidOperationException("没有找到兼容的安装文件。");
        if (project.Pack)
        {
            if (!file.File.Name.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("此文件不是 .mrpack。");
            if (!await ConfirmAsync($"安装整合包「{project.Name} · {file.Number}」并创建独立实例？\nMinecraft {string.Join(" / ", file.GameVersions)} · {string.Join(" / ", file.Loaders)}\n{file.File.Name} · {file.File.Size / 1048576.0:0.00} MB")) return;
            maintenance++;
            try
            {
                model.Selected = await model.QueuePackAsync(project.Name, file.File, null, progress => status.Text = progress);
                status.Text = "整合包安装完成";
                packTab = 1;
                Refresh();
            }
            finally { maintenance--; }
            return;
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var planner = new ModInstallPlanner(model.Mods);
        var installedPaths = model.LocalFiles(instance!).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var installed = await planner.IdentifyAsync(installedPaths, timeout.Token);
        var plan = await planner.PlanAsync(project, file, instance!, installed, timeout.Token);
        if (plan.Files.Length == 0) throw new InvalidOperationException("该模组及其必需依赖已安装。");
        var listing = string.Join("\n", plan.Files.Select(value => "· " + value.File.Name));
        var optional = plan.Optional.Length > 0 ? "\n可选依赖（需自行选择）：" + string.Join("、", plan.Optional) : "";
        if (!await ConfirmAsync($"安装到「{instance!.Name}」？\nMinecraft {instance.Version} · {instance.Loader}\n{listing}{optional}")) return;
        var current = model.Store.Entries.FirstOrDefault(candidate => candidate.Id == instance.Id);
        if (current is null || current.Version != instance.Version || current.Loader != instance.Loader ||
            current.LoaderVersion != instance.LoaderVersion ||
            !installedPaths.SequenceEqual(model.LocalFiles(current).Order(StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("实例配置或已安装文件有变化，请重新生成安装计划。");
        EnsureIdle(current);
        var folder = model.ModsFolder(current);
        foreach (var item in plan.Files)
        {
            var destination = InstanceStore.SafeFile(folder, item.File.Name);
            if (File.Exists(destination) || File.Exists(destination + ".disabled") ||
                model.Downloads.Items.Any(job => job.Target.Equals(destination, StringComparison.OrdinalIgnoreCase) &&
                    job.State is not ("已完成" or "已取消")))
                throw new IOException("文件已有任务或存在于目标实例：" + item.File.Name);
        }
        foreach (var item in plan.Files)
        {
            var task = model.Downloads.Enqueue(item.File, folder, current.Name,
                item.Id == file.Id ? "模组" : "模组依赖", current.Id, LauncherModel.GameConfiguration(current));
            status.Text = "已加入下载队列";
            if (!await RunDownloadAsync(task.Id))
                throw new IOException("模组安装计划未完成。请在下载管理检查失败或暂停的任务。");
        }
    }
}
