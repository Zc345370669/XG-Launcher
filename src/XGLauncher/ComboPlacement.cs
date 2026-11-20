// SPDX-License-Identifier: MIT
using Avalonia;
namespace XGLauncher;

/// <summary>
/// 按当前客户端可用区域计算浮层方向和边界，使用DIP而非固定屏幕坐标。
/// </summary>
internal static class ComboPlacement
{
    internal sealed record Plan(Rect Bounds, bool Up);
    // 按设备无关客户端几何计算，每次浮层布局、移动或缩放都重新求值。
    internal static Plan Calculate(Rect anchor, Size client, double expansion, bool preferAbove) =>
        Calculate(anchor, new Rect(client), expansion, preferAbove);

    internal static Plan Calculate(Rect anchor, Rect viewport, double expansion, bool preferAbove)
    {
        var headerHeight = Math.Min(Math.Max(0, anchor.Height), Math.Max(0, viewport.Height));
        var headerY = Math.Clamp(anchor.Top, viewport.Top, Math.Max(viewport.Top, viewport.Bottom - headerHeight));
        var above = Math.Max(0, headerY - viewport.Top);
        var below = Math.Max(0, viewport.Bottom - headerY - headerHeight);
        var up = preferAbove ? above >= Math.Min(expansion, 46) || above >= below : below < expansion && above > below;
        var extra = Math.Min(Math.Max(0, expansion), up ? above : below);
        var width = Math.Min(Math.Max(0, anchor.Width), Math.Max(0, viewport.Width));
        var x = Math.Clamp(anchor.X, viewport.Left, Math.Max(viewport.Left, viewport.Right - width));
        return new(new Rect(x, up ? headerY - extra : headerY, width, headerHeight + extra), up);
    }
}
