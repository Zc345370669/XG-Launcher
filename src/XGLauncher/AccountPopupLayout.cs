// SPDX-License-Identifier: MIT
namespace XGLauncher;

internal readonly record struct AccountPopupLayout(double Top, double Height, bool UseSidePopup)
{
    internal static AccountPopupLayout Calculate(double anchorTop, double obstacleBottom, int count, double maximum)
    {
        var desired = Math.Min(Math.Max(46, maximum), Math.Max(46, Math.Max(0, count) * 40.0 + 6));
        var gap = Math.Max(0, anchorTop - Math.Max(0, obstacleBottom + 6) - 4);
        if (gap < 46) return new(anchorTop, desired, true);
        var height = Math.Min(desired, gap);
        return new(anchorTop - height - 4, height, false);
    }
}
