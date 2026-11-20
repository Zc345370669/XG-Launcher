// SPDX-License-Identifier: MIT
using Avalonia.Platform.Storage;

namespace XGLauncher;

public sealed partial class MainWindow
{
    /// <summary>单 EXE 发行仍可离线导出完整许可、历史声明及原有对应源码材料。</summary>
    private async Task ExportComplianceAsync()
    {
        var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer.Text("导出完整许可与对应源码"), SuggestedFileName = "XGLauncher-Compliance.zip",
            DefaultExtension = "zip"
        });
        if (target == null) return;
        using var source = typeof(MainWindow).Assembly.GetManifestResourceStream("XG.Compliance.zip") ??
            throw new InvalidOperationException("此开发构建未嵌入完整许可材料，请查看源码仓库的 licenses、licensing 和 third-party 目录。");
        await using var output = await target.OpenWriteAsync();
        if (output.CanSeek) output.SetLength(0);
        await source.CopyToAsync(output);
    }
}
