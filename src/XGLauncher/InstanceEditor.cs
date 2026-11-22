// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using XG.Core;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private async Task EditAsync(Instance? existing, string? createTitle = null)
    {
        using var lifetime = new CancellationTokenSource();
        CancellationTokenSource? loaderJob = null;
        var dialog = Dialog(existing is null ? createTitle ?? "创建实例" : "编辑实例", editor: true);
        var name = Input(existing == null ? "实例名称（可选，留空自动命名）" : "实例名称", existing?.Name);
        var versionPicker = new MinecraftVersionPicker("Minecraft 版本", false);
        var versions = versionPicker.Select;
        var loader = Picker(["原版", "NeoForge", "Forge", "Fabric", "Quilt"], title: "加载器类型");
        loader.SelectedItem = existing?.Loader ?? "原版";
        var choices = Picker([], title: "加载器版本", searchable: true);
        var javaMode = Picker(["自动（推荐）", "手动选择"], string.IsNullOrWhiteSpace(existing?.Java) ? 0 : 1, "Java");
        var java = Input("手动覆盖 Java 路径（可选）", model.Store.ResolveJavaPath(existing?.Java));
        var automatic = Picker(["自动", "手动"], existing is { AutomaticMemory: not true } ? 1 : 0, "内存分配");
        var memory = Input("内存 MB", (existing?.Memory ?? 4096).ToString());
        var resolution = Picker(["自动", "手动"], existing is { AutomaticResolution: not true } ? 1 : 0, "分辨率");
        var width = Input("宽度", (existing?.Width ?? 1280).ToString());
        var height = Input("高度", (existing?.Height ?? 720).ToString());
        var versionStatus = Label("");
        var loaderStatus = Label("");
        var memoryStatus = Label("");
        var available = model.Game.CachedVersions();
        var filtering = false;
        var loadingVersions = false;
        string? verifiedGame = null;
        string? verifiedLoader = null;
        string? activeLoader = existing?.Loader;
        var previousChoices = new Dictionary<string, string>(StringComparer.Ordinal);

        var advanced = new StackPanel { Name = "InstanceParameters" };
        var fields = Enumerable.Range(0, 7).Select(_ => Label("")).ToArray();
        foreach (var field in fields) field.IsVisible = false;
        var save = Action("保存", async () =>
        {
            foreach (var field in fields) field.IsVisible = false;
            void Error(int index, string text) { fields[index].Text = text; fields[index].IsVisible = true; }
            var title = name.Text?.Trim() ?? "";
            var game = versions.SelectedItem as string;
            var kind = loader.SelectedItem as string ?? "原版";
            var selected = choices.SelectedItem as string;
            var autoMemory = automatic.SelectedIndex == 0;
            var autoResolution = resolution.SelectedIndex == 0;
            var manualJava = javaMode.SelectedIndex == 1 ? java.Text?.Trim() : null;
            if (title.Length > 80) Error(0, "实例名称不能超过 80 个字符。");
            if (!Instance.IsValidVersion(game)) Error(1, game == null ? "请选择 Minecraft 版本。" : "Minecraft 版本标识无效。");
            if (game != null && kind != "原版" && (selected == null || verifiedGame != game || verifiedLoader != kind))
                Error(2, "请先选择已确认兼容的加载器版本。");
            var mb = existing?.Memory ?? 4096;
            var w = existing?.Width ?? 1280;
            var h = existing?.Height ?? 720;
            if (!autoMemory && (!int.TryParse(memory.Text, out mb) || mb is < 512 or > 65536))
                Error(3, "请输入 512–65536 之间的整数内存值（MB）。");
            if (!autoResolution && (!int.TryParse(width.Text, out w) || !int.TryParse(height.Text, out h) ||
                w is < 320 or > 7680 || h is < 240 or > 4320))
                Error(4, "请输入有效的整数分辨率。宽度 320–7680，高度 240–4320。");
            if (javaMode.SelectedIndex == 1)
            {
                if (string.IsNullOrWhiteSpace(manualJava) || !Path.IsPathFullyQualified(manualJava) || !File.Exists(manualJava))
                    Error(5, "请选择存在的 Java 可执行文件。");
                else if (!fields.Any(field => field.IsVisible) && game != null)
                {
                    try { var verified = await ValidateJavaAsync(manualJava, game); if(verified == null || !string.Equals(verified.Path,manualJava,StringComparison.OrdinalIgnoreCase)) Error(5,"请选择存在的 Java 可执行文件。"); }
                    catch (InvalidOperationException e) { Error(5, new XG.Core.GameLogSanitizer(null).Redact(e.Message)); }
                    catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
                    {
                        if (existing?.Version != game || existing.Loader != kind ||
                            !string.Equals(model.Store.ResolveJavaPath(existing.Java), manualJava, StringComparison.Ordinal))
                            Error(5, "暂时无法校验所选 Java，请检查网络后重试。");
                    }
                    if (!Equals(versions.SelectedItem, game) || !Equals(loader.SelectedItem, kind) || java.Text?.Trim() != manualJava)
                        Error(5, "校验期间设置已变更，请重新保存。");
                }
            }
            if (lifetime.IsCancellationRequested) return;
            if (!fields.Any(field => field.IsVisible))
            {
                try
                {
                    var path = existing == null ? model.Store.InstanceRoot : model.Store.EnsureLocalDirectory(existing);
                    if (!Directory.Exists(path)) path = model.Store.InstanceRoot;
                    using var probe = new FileStream(Path.Combine(path, ".xg-" + Guid.NewGuid().ToString("N")),
                        FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { Error(6, "实例目录不可写。请检查目录权限和可用空间。"); }
            }
            if (fields.FirstOrDefault(field => field.IsVisible) is { } first)
            {
                if (first == fields[0]) name.Focus();
                if (first == fields[5] || first == fields[6]) advanced.IsVisible = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => first.BringIntoView(), Avalonia.Threading.DispatcherPriority.Loaded);
                return;
            }
            var suggestedName = title.Length == 0 ? "Minecraft " + game + (kind == "原版" ? "" : " " + kind) : title;
            title = model.Store.AvailableName(suggestedName, existing?.Id);
            var instance = new Instance(existing?.Id ?? Guid.NewGuid(), title, game!,
                kind, model.Store.StoreJavaPath(manualJava), mb, w, h, existing?.LastPlayed, existing?.Pack ?? false,
                kind == "原版" ? null : selected, autoMemory, autoResolution, existing?.ReferencedGameDirectory, existing?.ImportSource);
            EnsureIdle(instance);
            model.Store.Save(instance);
            installationStates.Remove(instance.Id);
            model.Selected = instance;
            dialog.Close();
            Refresh();
        }, true);
        Button retryVersions = null!;
        Button retryLoader = null!;
        retryVersions = Action("重试版本列表", () => LoadVersionsAsync(true));
        retryLoader = Action("重试加载器列表", LoadLoadersAsync);
        retryVersions.IsVisible = retryLoader.IsVisible = false;

        void ApplyVersions()
        {
            filtering = true;
            try { versionPicker.Update(available, existing?.Version); }
            finally { filtering = false; }
        }

        async Task LoadVersionsAsync(bool reload)
        {
            if (loadingVersions) return;
            loadingVersions = true;
            retryVersions.IsVisible = false;
            versionStatus.Text = "正在读取官方版本列表…";
            ApplyVersions();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                available = await model.Game.OfficialVersionsAsync(reload, timeout.Token);
                if (lifetime.IsCancellationRequested) return;
                ApplyVersions();
                versionStatus.Text = "";
            }
            catch (Exception error) when (error is HttpRequestException or IOException or System.Text.Json.JsonException or OperationCanceledException)
            {
                if (lifetime.IsCancellationRequested) return;
                versionStatus.Text = available.Length == 0 ? "无法获取版本列表，请检查网络。" : "刷新失败，正在显示缓存版本。";
                retryVersions.IsVisible = true;
            }
            finally { loadingVersions = false; }
            if (!lifetime.IsCancellationRequested) await LoadLoadersAsync();
        }

        async Task LoadLoadersAsync()
        {
            loaderJob?.Cancel();
            using var current = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            loaderJob = current;
            current.CancelAfter(TimeSpan.FromSeconds(15));
            verifiedGame = verifiedLoader = null;
            var game = versions.SelectedItem as string;
            var kind = loader.SelectedItem as string;
            var selected = choices.SelectedItem as string;
            if (selected != null && activeLoader != null) previousChoices[activeLoader] = selected;
            activeLoader = kind;
            choices.ItemsSource = Array.Empty<string>();
            choices.IsEnabled = false;
            retryLoader.IsVisible = false;
            loaderStatus.Text = "";
            try
            {
                if (game is null || kind is null || kind == "原版") return;
                loaderStatus.Text = "正在获取兼容加载器版本…";
                var result = await model.Game.CompatibleLoadersAsync(game, kind, current.Token);
                if (!ReferenceEquals(loaderJob, current) || current.IsCancellationRequested) return;
                choices.ItemsSource = result.Select(x => x.Id).ToArray();
                choices.IsEnabled = result.Length > 0;
                verifiedGame = game;
                verifiedLoader = kind;
                var preference = previousChoices.GetValueOrDefault(kind) ?? (existing?.Loader == kind ? existing.LoaderVersion : null);
                var eligible = result.FirstOrDefault(x => x.Id == preference);
                var recommendation = result.FirstOrDefault(x => x.Recommended) ?? result.FirstOrDefault(x => x.Stable) ?? result.FirstOrDefault();
                choices.SelectedItem = (eligible ?? recommendation)?.Id;
                loaderStatus.Text = recommendation == null ? "当前 Minecraft 版本暂无兼容的加载器版本。" :
                    eligible != null ? "已保留兼容的加载器版本。" :
                    (preference == null ? "" : "原选版本不兼容，已调整。") +
                    (recommendation.Stable ? "已推荐兼容的稳定版，可手动更换。" : "官方数据未标记稳定版，已选择一个兼容版本，可手动更换。");
            }
            catch (Exception error) when (error is HttpRequestException or IOException or InvalidOperationException or
                ArgumentException or System.Text.Json.JsonException or OperationCanceledException)
            {
                if (!lifetime.IsCancellationRequested && ReferenceEquals(loaderJob, current))
                {
                    loaderStatus.Text = "无法获取兼容版本，请检查网络或稍后重试。";
                    retryLoader.IsVisible = true;
                }
            }
            finally { if (ReferenceEquals(loaderJob, current)) loaderJob = null; }
        }

        void UpdateMemory()
        {
            memory.IsVisible = automatic.SelectedIndex == 1;
            var fallback = new Instance(Guid.Empty, "", versions.SelectedItem as string ?? "");
            var instance = (existing ?? fallback) with { Version = versions.SelectedItem as string ?? "" };
            var recommendation = InstanceMemory.Recommend(instance, existing is null ? 0 : model.LocalFiles(existing).Length);
            memoryStatus.Text = !memory.IsVisible ? $"当前建议 {recommendation} MB，启动时重新评估。"
                : int.TryParse(memory.Text, out var mb) && mb > recommendation * 1.5
                    ? $"此设置超过当前建议值 {recommendation} MB。" : "";
        }

        versions.SelectionChanged += async (_, _) => { if (!filtering) { UpdateMemory(); await LoadLoadersAsync(); } };
        loader.SelectionChanged += async (_, _) => await LoadLoadersAsync();
        automatic.SelectionChanged += (_, _) => UpdateMemory();
        memory.TextChanged += (_, _) => UpdateMemory();
        var dimensions = Row(width, height);
        resolution.SelectionChanged += (_, _) => dimensions.IsVisible = resolution.SelectedIndex == 1;
        dimensions.IsVisible = resolution.SelectedIndex == 1;
        UpdateMemory();
        foreach (var picker in new[] { versions, loader, choices, automatic, resolution })
            picker.HorizontalAlignment = HorizontalAlignment.Stretch;
        var javaStatus = Label("");
        async Task<JavaInstallation?> ValidateJavaAsync(string? manual, string game)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            var major = await model.Game.RequiredJavaMajorAsync(game, deadline.Token);
            var managed = model.Store.ManagedJavaRoot;
            return await JavaDiscovery.CompatibleAsync(manual, managed, major);
        }
        var javaSettings = InstanceJavaSettings(dialog, versions, javaMode, java, lifetime.Token);
        advanced.Children.Add(Column(javaSettings, fields[5],Label("实例目录"), Label(existing == null ? model.Store.InstanceRoot : model.Store.DirectoryFor(existing)),fields[6]));
        var loaderVersionGroup = Column(Label("加载器版本"), choices, loaderStatus, fields[2], retryLoader);
        void UpdateLoaderFields() => loaderVersionGroup.IsVisible = loader.SelectedItem as string != "原版";
        loader.SelectionChanged += (_, _) => UpdateLoaderFields();
        UpdateLoaderFields();
        var panel = Column(name, fields[0], versions, versionStatus, fields[1], retryVersions,
            Label("加载器类型"), loader, loaderVersionGroup,
            Label("内存"), automatic, memory, memoryStatus, fields[3], Label("分辨率"), resolution, dimensions, fields[4], advanced);
        save.IsDefault = true;
        dialog.SetBody(panel, CloseDialog(dialog), save);
        dialog.Opened += async (_, _) => await LoadVersionsAsync(false);
        dialog.Closed += (_, _) => lifetime.Cancel();
        await dialog.ShowDialog(this);
    }
}
