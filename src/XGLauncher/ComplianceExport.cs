// SPDX-License-Identifier: MIT
using Avalonia.Platform.Storage;

namespace XGLauncher;

public sealed partial class MainWindow
{
    /// <summary>从 EXE 导出原始许可、鸣谢、历史授权及必须提供的第三方对应源码，不依赖联网。</summary>
    private async Task ExportComplianceAsync()
    {
        using var source = typeof(MainWindow).Assembly.GetManifestResourceStream("XG.Compliance.zip")
            ?? throw new InvalidOperationException("Complete compliance materials are available in the published single-file build.");
        var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer.Text("导出完整许可与对应源码"), SuggestedFileName = "XGLauncher-Licenses-and-Sources.zip",
            DefaultExtension = "zip", FileTypeChoices = [new FilePickerFileType("ZIP") { Patterns = ["*.zip"] }]
        });
        if (target == null) return;
        await using var output = await target.OpenWriteAsync();
        if (output.CanSeek) output.SetLength(0);
        await source.CopyToAsync(output);
    }
}
