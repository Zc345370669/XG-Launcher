// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
namespace XGLauncher;

/// <summary>弹窗保留真实模态 Owner，但几何始终围绕所属启动器主窗口，并约束到屏幕工作区。</summary>
internal static class DialogPlacement
{
    internal static Window Anchor(Window owner)
    {
        for (var current = owner; ;)
        {
            if (current is MainWindow) return current;
            if (current.Owner is not Window parent) return current;
            current = parent;
        }
    }
    internal static PixelPoint Center(PixelRect anchor, Size dialog, double scale, PixelRect work)
    {
        scale = double.IsFinite(scale) && scale > 0 ? scale : 1;
        var width = (int)Math.Min(work.Width, Math.Ceiling(Math.Max(1, dialog.Width) * scale));
        var height = (int)Math.Min(work.Height, Math.Ceiling(Math.Max(1, dialog.Height) * scale));
        return new(Math.Clamp(anchor.X + (anchor.Width - width) / 2, work.X, Math.Max(work.X, work.Right - width)),
            Math.Clamp(anchor.Y + (anchor.Height - height) / 2, work.Y, Math.Max(work.Y, work.Bottom - height)));
    }
}
