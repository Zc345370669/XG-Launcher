// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using XG.Core;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private string? compactDownloadVersion;
    private string compactDownloadLoader = "原版";
    private Guid? compactDownloadTarget;
    private bool compactDownloadPending;
    private string compactDownloadFeedback = "";
    /// <summary>
    /// 在现有实例组合框内部选择版本和加载器，调用共享安装流程并回收表单订阅。
    /// </summary>
    private Control CompactDownloadForm(UnifiedExpandableSelect parent)
    {
        var closed = false;
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(catalogLifetime.Token);
        var picker = new MinecraftVersionPicker("游戏版本", false);
        var versions = picker.Select; versions.Name = "CompactDownloadVersions"; versions.ExpansionParent = parent;
        picker.Update(model.Game.CachedVersions(), compactDownloadVersion);
        var loaders = Picker(["原版", "NeoForge", "Forge", "Fabric", "Quilt"], title: "加载器");
        loaders.Name = "CompactDownloadLoader"; loaders.ExpansionParent = parent; loaders.SelectedItem = compactDownloadLoader;
        var note = Label(compactDownloadFeedback, 11); note.Name = "CompactDownloadFeedback";
        Button install = null!;
        install = Action("下载并安装", async () =>
        {
            compactDownloadVersion = versions.SelectedItem as string;
            compactDownloadLoader = loaders.SelectedItem as string ?? "原版";
            install.Content = "正在准备…";
            note.Text = "正在准备安装任务…"; note.IsVisible = true;
            await StartCompactDownloadAsync();
            if (!closed) { note.Text = compactDownloadFeedback; Sync(); }
        }, true);
        install.Name = "CompactDownloadInstall";
        versions.SelectionChanged += (_, _) => { compactDownloadVersion = versions.SelectedItem as string; Sync(); };
        loaders.SelectionChanged += (_, _) => { compactDownloadLoader = loaders.SelectedItem as string ?? "原版"; Sync(); };
        var panel = Column(versions, loaders, install, note); panel.Margin = new(12, 12, 18, 12); panel.Spacing = 8;
        if (compactChoices.Length > 0)
        {
            var back = Action("返回已安装游戏", () => { parent.Close(false); parent.Open(); return Task.CompletedTask; });
            back.Name = "CompactDownloadBack"; panel.Children.Add(back);
        }
        panel.AttachedToVisualTree += async (_, _) =>
        {
            try
            {
                var manifest = await model.Game.OfficialVersionsAsync(token: lifetime.Token);
                if (!closed) { picker.Update(manifest, compactDownloadVersion); compactDownloadVersion = versions.SelectedItem as string; Sync(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!closed) { note.Text = new GameLogSanitizer(null).Redact(ex.Message); Sync(); } }
        };
        panel.DetachedFromVisualTree += (_, _) => { closed = true; lifetime.Cancel(); lifetime.Dispose(); };
        Sync(); return panel;
        void Sync()
        {
            note.IsVisible = !string.IsNullOrEmpty(note.Text);
            install.IsEnabled = !compactDownloadPending && versions.SelectedItem is string;
            install.Content = compactDownloadPending ? "正在安装…" : "下载并安装";
        }
    }

    private async Task StartCompactDownloadAsync()
    {
        if (compactDownloadPending || !Instance.IsValidVersion(compactDownloadVersion)) return;
        compactDownloadPending = true; compactDownloadFeedback = "";
        var version = compactDownloadVersion!; var loader = compactDownloadLoader;
        try
        {
            string? loaderVersion = null;
            if (loader != "原版")
            {
                var choices = await model.Game.CompatibleLoadersAsync(version, loader, catalogLifetime.Token);
                var choice = choices.FirstOrDefault(x => x.Recommended) ?? choices.FirstOrDefault(x => x.Stable) ?? choices.FirstOrDefault();
                loaderVersion = choice?.Id ?? throw new InvalidOperationException("此游戏版本没有兼容的加载器版本。");
            }
            var target = model.Store.Entries.FirstOrDefault(x => x.Id == compactDownloadTarget && x.Version == version && x.Loader == loader && x.LoaderVersion == loaderVersion);
            if (target == null)
            {
                var name = "Minecraft " + version + (loader == "原版" ? "" : " · " + loader);
                target = new Instance(Guid.NewGuid(), name[..Math.Min(80, name.Length)], version, loader, LoaderVersion: loaderVersion);
                model.Store.Save(target); compactDownloadTarget = target.Id;
            }
            model.Selected = target; compactInstances.Close(false); Refresh();
            await model.InstallInstanceAsync(target, text => Dispatcher.UIThread.Post(() => { if (!windowClosed) status.Text = text; }));
            installationStates.Remove(target.Id);
            if (await model.Game.InspectInstallationAsync(target) == "已安装")
            { model.Selected = target; compactInstances.Close(false); }
            else compactDownloadFeedback = "安装尚未完成，请查看下载任务。";
        }
        catch (OperationCanceledException) { compactDownloadFeedback = "下载已取消，可重新选择版本安装。"; }
        catch (Exception ex) { compactDownloadFeedback = new GameLogSanitizer(null).Redact(ex.Message); }
        finally { compactDownloadPending = false; if (!windowClosed) Refresh(); }
    }
}
