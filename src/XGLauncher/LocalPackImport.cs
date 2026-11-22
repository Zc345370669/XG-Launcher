// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using XG.Core;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private async Task ImportLocalPackAsync()
    {
        var dialog = Dialog("导入整合包", editor: true);
        using var lifetime = new CancellationTokenSource();
        var notice = Label("选择整合包文件或已解压的整合包实例目录。");
        var importing = false;
        async Task ImportAsync(string path)
        {
            if (importing) return;
            importing = true;
            maintenance++;
            string? staging = null;
            var sharedConflicts = new List<string>();
            try
            {
                Instance imported;
                if (File.Exists(path) && IsModrinthPack(path))
                {
                    imported = await model.QueuePackAsync(Path.GetFileNameWithoutExtension(path), null, path, progress => notice.Text = progress);
                }
                else
                {
                    if (File.Exists(path))
                    {
                        staging = Path.Combine(model.DownloadsDirectory, "pack-import-" + Guid.NewGuid().ToString("N"));
                        await GameImport.ExtractZipAsync(path, staging, lifetime.Token);
                        path = staging;
                    }
                    var manifest = Path.Combine(path, "manifest.json");
                    if (File.Exists(manifest))
                    {
                        await using var input = File.OpenRead(manifest);
                        using var document = await JsonDocument.ParseAsync(input, cancellationToken: lifetime.Token);
                        var pack = document.RootElement;
                        var files = PackValue(pack, "files");
                        if (files.ValueKind == JsonValueKind.Array && files.GetArrayLength() > 0)
                            throw new InvalidDataException("此 CurseForge 包含项目依赖。请在原启动器安装后，选择已安装的整合包实例目录。");
                        var minecraft = PackValue(pack, "minecraft");
                        var version = PackString(minecraft, "version") ?? throw new InvalidDataException("整合包缺少 Minecraft 版本。");
                        var loaders = PackValue(minecraft, "modLoaders");
                        var id = loaders.ValueKind == JsonValueKind.Array
                            ? loaders.EnumerateArray().Select(x => PackString(x, "id")).OfType<string>().FirstOrDefault() : null;
                        var (loader, build) = ParsePackLoader(id);
                        var directory = InstanceStore.SafeFile(path, PackString(pack, "overrides") ?? "overrides");
                        Directory.CreateDirectory(directory);
                        imported = await GameImport.ImportDirectoryAsync(model.Store, directory,
                            model.Store.AvailableName(PackString(pack, "name") ?? "导入的整合包"), version, loader, build, null, lifetime.Token, sharedConflicts.Add, pack: true);
                    }
                    else
                    {
                        var scan = await Task.Run(() => GameImport.Scan(path, lifetime.Token), lifetime.Token);
                        if (scan.Instances.Count != 1) throw new InvalidDataException("请选择只包含一个有效实例的整合包目录。");
                        var candidate = scan.Instances[0];
                        var inspection = candidate.Inspection;
                        if (inspection.Loader != "原版" && string.IsNullOrWhiteSpace(inspection.LoaderVersion))
                            throw new InvalidDataException("整合包的加载器版本信息不完整。");
                        imported = new Instance(Guid.NewGuid(), model.Store.AvailableName(candidate.Name), inspection.Version!,
                            inspection.Loader, Pack: true, LoaderVersion: inspection.LoaderVersion, AutomaticMemory: true, AutomaticResolution: true);
                        await GameImport.CopyCandidateAsync(model.Store, imported, candidate, true, null, lifetime.Token, sharedConflicts.Add);
                    }
                }
                model.Selected = imported;
                Refresh();
                if (sharedConflicts.Count > 0) WriteImportDiagnostics(sharedConflicts);
                status.Text = sharedConflicts.Count == 0 ? "整合包导入完成" :
                    $"整合包导入完成。保留了 {sharedConflicts.Count} 个同名共享文件，请先校验安装。详情见 game-import.log。";
            }
            finally
            {
                if (staging != null && Directory.Exists(staging)) Directory.Delete(staging, true);
                maintenance--;
                importing = false;
            }
            dialog.Close();
        }
        var content = Column(notice,
            Action("选择整合包文件", async () =>
            {
                var files = await dialog.StorageProvider.OpenFilePickerAsync(new()
                {
                    FileTypeFilter = [new("Minecraft 整合包") { Patterns = ["*.mrpack", "*.zip"] }]
                });
                if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await ImportAsync(path);
            }),
            Action("选择整合包实例目录", async () =>
            {
                var folders = await dialog.StorageProvider.OpenFolderPickerAsync(new());
                if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) await ImportAsync(path);
            }));
        dialog.SetBody(content, CloseDialog(dialog));
        dialog.Closing += (_, e) => { if (importing) e.Cancel = true; };
        dialog.FitContent(content);
        await dialog.ShowDialog(this);
    }

    private static bool IsModrinthPack(string file)
    {
        using var archive = ZipFile.OpenRead(file);
        return archive.GetEntry("modrinth.index.json") != null;
    }

    private static JsonElement PackValue(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) ? property : default;

    private static string? PackString(JsonElement value, string name) =>
        PackValue(value, name) is { ValueKind: JsonValueKind.String } property ? property.GetString() : null;

    private static (string Loader, string? Version) ParsePackLoader(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return ("原版", null);
        var loader = new[] { "NeoForge", "Forge", "Fabric", "Quilt" }
            .FirstOrDefault(kind => id.StartsWith(kind + "-", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("此整合包使用未知加载器。");
        return (loader, id[(loader.Length + 1)..]);
    }
}
