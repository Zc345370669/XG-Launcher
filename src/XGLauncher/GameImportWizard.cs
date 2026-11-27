// SPDX-License-Identifier: MIT
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using XG.Core;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private Button PersonalIcon(string icon, string hint, Func<Task> action)
    {
        var button = Action("", action);
        button.Width = button.Height = 36;
        button.Padding = new(8);
        button.Content = new LucideIcon(icon, 18);
        ToolTip.SetTip(button, hint);
        Avalonia.Automation.AutomationProperties.SetName(button, hint);
        return button;
    }

    internal Func<Instance, Task>? ImportedRepairOverride { get; set; }

    private Task GameImportWizardAsync() => GameImportWizardAsync(async dialog =>
    {
        var folders = await dialog.StorageProvider.OpenFolderPickerAsync(new() { AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    });

    internal async Task GameImportWizardAsync(Func<LauncherDialog, Task<string?>> selectFolder)
    {
        var dialog = Dialog("导入游戏", editor: true);
        using var lifetime = new CancellationTokenSource();
        CancellationTokenSource? scanJob = null;
        string? source = null;
        var importing = false;
        var rows = new List<(GameImportCandidate Candidate, CheckBox Selected, TextBox Version, UnifiedExpandableSelect Loader, TextBox Build)>();
        var results = Column();
        StackPanel? content = null;
        var info = Label("智能识别 Minecraft 游戏");
        var sourceLabel = Label("");
        var copy = new RadioButton { Content = "复制导入", IsChecked = true };
        var reference = new RadioButton { Content = "引用原目录" };
        var keep = new CheckBox { Content = "保留模组、配置、存档、资源包、光影、设置和截图", IsChecked = true };
        var shared = Label("其他启动器和 XG Launcher 可能共用游戏文件、配置和存档。");
        var completeResources = new CheckBox { Content = "导入后自动补齐缺失资源", IsChecked = true };
        var modes = Column(Row(copy, reference), keep, shared, completeResources);
        var resultsScroll = new ScrollViewer
        {
            Content = results,
            MaxHeight = 340,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        IdleScrollbar.Attach(resultsScroll);
        shared.IsVisible = modes.IsVisible = false;
        Button execute = null!;
        Button choose = null!;
        Button rescan = null!;
        bool Valid((GameImportCandidate Candidate, CheckBox Selected, TextBox Version, UnifiedExpandableSelect Loader, TextBox Build) row) =>
            Instance.IsValidVersion(row.Version.Text?.Trim()) &&
            (row.Loader.SelectedItem as string == "原版" || Instance.IsValidVersion(row.Build.Text?.Trim()));
        bool CanImport() => !importing && scanJob == null && rows.Any(x => x.Selected.IsChecked == true) &&
            rows.Where(x => x.Selected.IsChecked == true).All(Valid);
        void Update()
        {
            shared.IsVisible = reference.IsChecked == true;
            keep.IsVisible = !shared.IsVisible;
            completeResources.IsVisible = rows.Any(x => x.Selected.IsChecked == true);
            execute.IsEnabled = CanImport();
        }

        void AddRow(GameImportCandidate candidate)
        {
            var inspection = candidate.Inspection;
            var registered = model.Store.Entries.Any(x => string.Equals(model.Store.GameDirectoryFor(x), candidate.Profile, StringComparison.OrdinalIgnoreCase));
            var box = new CheckBox { Content = candidate.Name + (registered ? " · 已注册" : ""), IsChecked = !registered && !rows.Any(x => x.Selected.IsChecked == true), IsEnabled = !registered };
            var version = Input("Minecraft 版本", inspection.Version);
            var loader = Picker(["原版", "Forge", "NeoForge", "Fabric", "Quilt"], title: "加载器类型");
            loader.SelectedItem = inspection.Loader;
            var build = Input("加载器版本", inspection.LoaderVersion);
            var edit = Column(version, loader, build);
            edit.IsVisible = !Instance.IsValidVersion(inspection.Version) ||
                (inspection.Loader != "原版" && !Instance.IsValidVersion(inspection.LoaderVersion));
            void UpdateBuild() { build.IsVisible = loader.SelectedItem as string != "原版"; Update(); }
            box.IsCheckedChanged += (_, _) => Update();
            version.TextChanged += (_, _) => Update();
            loader.SelectionChanged += (_, _) => UpdateBuild();
            build.TextChanged += (_, _) => Update();
            rows.Add((candidate, box, version, loader, build));
            UpdateBuild();
            var requiredJava = JavaVersionPolicy.Recommend(inspection.Version ?? "");
            var summary = Label(string.Join(" · ", new[] { inspection.Version ?? "版本待补充", inspection.Loader,
                inspection.LoaderVersion, inspection.Format, requiredJava.HasValue ? $"Java {requiredJava}" : null }.OfType<string>()));
            var toggle = Action("编辑识别信息", () => { edit.IsVisible = !edit.IsVisible; return Task.CompletedTask; });
            results.Children.Add(Column(box, summary, Label(candidate.Profile), Label(candidate.Status), toggle, edit));
        }

        async Task ScanAsync()
        {
            if (source == null || importing || scanJob != null) return;
            var path = source;
            using var job = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            scanJob = job;
            choose.IsEnabled = rescan.IsEnabled = false;
            rows.Clear();
            results.Children.Clear();
            modes.IsVisible = false;
            info.Text = "正在扫描…";
            Update();
            try
            {
                var (scan, manual) = await Task.Run(() =>
                {
                    var found = GameImport.Scan(path, job.Token);
                    return (found, found.Instances.Count == 0 ? GameImport.ManualCandidate(path) : null);
                }, job.Token);
                if (job.IsCancellationRequested) return;
                var java = new Dictionary<int, string>();
                if (scan.Instances.Count > 0)
                {
                    info.Text = $"已识别 {scan.Instances.Count} 个实例，正在检查 Java…";
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(job.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(2));
                    foreach (var major in scan.Instances.Select(x => JavaVersionPolicy.Recommend(x.Inspection.Version ?? ""))
                        .OfType<int>().Distinct().Take(5))
                    {
                        try
                        {
                            var runtime = await JavaDiscovery.CompatibleAsync(null, model.Store.ManagedJavaRoot, major,
                                model.Store.JavaSearchRoots(), deadline.Token);
                            java[major] = runtime == null ? $"未检测到 Java {major}" : $"已检测到 Java {major}";
                        }
                        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { break; }
                    }
                }
                if (job.IsCancellationRequested) return;
                foreach (var item in scan.Instances)
                {
                    var major = JavaVersionPolicy.Recommend(item.Inspection.Version ?? "");
                    var runtime = major.HasValue ? java.GetValueOrDefault(major.Value) ?? $"Java {major} 待校验" : "Java 版本待校验";
                    AddRow(item with { Status = item.Status + " · " + runtime });
                }
                var registeredCount = rows.Count(x => !x.Selected.IsEnabled);
                info.Text = $"扫描路径：{path}\n发现 {rows.Count} 个实例，已注册 {registeredCount} 个，可导入或恢复 {rows.Count - registeredCount} 个。"
                    + (rows.Count == 0 ? " 未发现有效的版本 JSON 或受支持的实例元数据，请查看扫描详情或选择具体目录。" : " 启动前将校验依赖。");
                if (scan.Issues.Count > 0)
                {
                    info.Text += $" {scan.Issues.Count} 项无法读取或解析，其他结果仍可导入。";
                    WriteImportDiagnostics(scan.Issues);
                    results.Children.Add(new Expander { Header = "扫描详情", Content = Label(string.Join("\n", scan.Issues.Take(20))) });
                }
                if (manual != null)
                {
                    results.Children.Add(Action("手动补充实例信息", () =>
                    {
                        results.Children.Clear();
                        AddRow(manual);
                        modes.IsVisible = true;
                        if (content != null) dialog.FitContent(content, 490);
                        return Task.CompletedTask;
                    }));
                }
                modes.IsVisible = rows.Count > 0;
                if (content != null) dialog.FitContent(content, 490);
            }
            catch (OperationCanceledException) when (job.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                WriteImportDiagnostics([error.ToString()]);
                info.Text = "无法扫描该目录，请检查文件夹权限、路径和文件完整性。";
            }
            finally
            {
                scanJob = null;
                choose.IsEnabled = rescan.IsEnabled = true;
                Update();
            }
        }

        choose = Action("选择游戏或其他启动器的目录", async () =>
        {
            if (await selectFolder(dialog) is not { } path) return;
            source = path;
            sourceLabel.Text = path;
            await ScanAsync();
        });
        rescan = Action("重新扫描", ScanAsync);
        rescan.IsEnabled = false;
        execute = Action("导入游戏", async () =>
        {
            if (!CanImport()) return;
            var selected = rows.Where(x => x.Selected.IsChecked == true).ToArray();
            var useReference = reference.IsChecked == true;
            var completeAfterImport = completeResources.IsChecked == true;
            importing = true;
            maintenance++;
            choose.IsEnabled = rescan.IsEnabled = results.IsEnabled = modes.IsEnabled = false;
            Update();
            var errors = new List<string>();
            var sharedConflicts = new List<string>();
            var imported = 0;
            var importedInstances = new List<Instance>();
            try
            {
                foreach (var row in selected)
                {
                    var version = row.Version.Text!.Trim();
                    var kind = (string)row.Loader.SelectedItem!;
                    var build = kind == "原版" ? null : row.Build.Text?.Trim();
                    var candidate = row.Candidate;
                    var referencePath = candidate.Isolated ? candidate.Profile : candidate.Inspection.Root;
                    var sourceKey = candidate.Profile;
                    if (model.Store.Entries.Any(x => string.Equals(x.ImportSource, sourceKey, StringComparison.OrdinalIgnoreCase) &&
                        x.Version == version && x.Loader == kind && x.LoaderVersion == build &&
                        (x.ReferencedGameDirectory != null) == useReference))
                    {
                        errors.Add(candidate.Name + "：已导入该来源和版本。");
                        continue;
                    }
                    var instance = new Instance(Guid.NewGuid(), model.Store.AvailableName(candidate.Name), version, kind,
                        LoaderVersion: build, AutomaticMemory: true, AutomaticResolution: true,
                        ReferencedGameDirectory: useReference ? referencePath : null, ImportSource: sourceKey);
                    try
                    {
                        info.Text = "正在导入 " + candidate.Name + "…";
                        if (Directory.GetParent(Path.GetFullPath(candidate.Profile))?.FullName.Equals(model.Store.InstanceRoot, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            model.Store.AdoptDirectory(instance with { ReferencedGameDirectory = null }, Path.GetFileName(candidate.Profile));
                            instance = model.Store.Entries.Single(x => x.Id == instance.Id);
                        }
                        else if (useReference)
                        {
                            _ = model.Store.GameDirectoryFor(instance);
                            model.Store.Save(instance);
                        }
                        else
                        {
                            var preserve = keep.IsChecked == true;
                            var progress = new Progress<MigrationFile>(file => { if (importing) info.Text = "正在复制 " + file.RelativePath; });
                            await Task.Run(() => GameImport.CopyCandidateAsync(model.Store, instance, candidate, preserve, progress, lifetime.Token, sharedConflicts.Add), lifetime.Token);
                        }
                        model.Selected = instance;
                        importedInstances.Add(instance);
                        imported++;
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
                    {
                        WriteImportDiagnostics([error.ToString()]);
                        errors.Add(candidate.Name + "：导入失败。" + error.Message);
                    }
                }
                if (sharedConflicts.Count > 0) WriteImportDiagnostics(sharedConflicts);
                if (imported > 0)
                {
                    Refresh();
                    status.Text = sharedConflicts.Count == 0 ? $"已导入 {imported} 个实例。" :
                        $"已导入 {imported} 个实例。保留了 {sharedConflicts.Count} 个同名共享文件，请先校验安装。详情见 game-import.log。";
                }
                info.Text = $"已登记 {imported} 个实例。缺失资源可通过实例的安装或修复入口补齐。"
                    + (errors.Count == 0 ? "" : "\n" + string.Join("\n", errors));
            }
            finally
            {
                maintenance--;
                importing = false;
                choose.IsEnabled = rescan.IsEnabled = results.IsEnabled = modes.IsEnabled = true;
                Update();
            }
            if (importedInstances.Count == 0) return;
            if (errors.Count > 0) await ErrorAsync(dialog, string.Join("\n", errors));
            dialog.Close();
            if (completeAfterImport) foreach (var importedInstance in importedInstances) await (ImportedRepairOverride?.Invoke(importedInstance) ?? CompleteImportedGameAsync(importedInstance));
        }, true);
        copy.IsCheckedChanged += (_, _) => Update();
        reference.IsCheckedChanged += (_, _) => Update();
        var sourceActions = new WrapPanel { ItemSpacing = 8, Children = { choose, rescan } };
        content = Column(Label("游戏文件夹"), sourceActions, sourceLabel, info, modes, resultsScroll);
        dialog.SetBody(content, CloseDialog(dialog), execute);
        dialog.FitContent(content, 490);
        execute.IsDefault = true;
        dialog.Closing += (_, e) => { if (importing) e.Cancel = true; else lifetime.Cancel(); };
        Update();
        await dialog.ShowDialog(this);
    }
    private async Task CompleteImportedGameAsync(Instance instance)
    {
        try
        {
            status.Text = "正在检查导入实例的资源…";
            await PrepareImportedGameAsync(
                () => model.Game.InspectInstallationAsync(instance),
                () =>
                {
                    section = 4;
                    downloadTab = 0;
                    Refresh();
                    return model.InstallInstanceAsync(instance, message =>
                        Dispatcher.UIThread.Post(() => status.Text = message));
                });
            if (windowClosed) return;
            section = 1;
            status.Text = "导入完成，启动资源已就绪。";
        }
        catch (OperationCanceledException)
        {
            status.Text = "已取消安装。导入的实例可在个人游玩中继续使用。";
        }
        catch (Exception error)
        {
            await ErrorAsync(this, "实例已导入。自动补齐资源失败：" + error.Message);
        }
        finally
        {
            installationStates.Remove(instance.Id);
            if (!windowClosed) Refresh();
        }
    }

    internal static async Task PrepareImportedGameAsync(Func<Task<string>> inspect, Func<Task> install)
    {
        if (await inspect() != "已安装")
        {
            await install();
            if (await inspect() != "已安装")
                throw new IOException("启动文件尚未通过校验，请在下载管理或个人游玩中检查安装状态。");
        }
    }

    private void WriteImportDiagnostics(IEnumerable<string> issues)
    {
        var details = issues.Select(x => new GameLogSanitizer(null).Redact(x)).ToArray();
        try
        {
            var path = Path.Combine(model.Store.Root, "game-import.log");
            File.AppendAllLines(path, details.Select(x => $"[{DateTimeOffset.Now:O}] {x}"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning(string.Join("\n", details));
        }
    }

}
