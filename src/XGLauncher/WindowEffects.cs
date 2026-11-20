// SPDX-License-Identifier: MIT
using Avalonia.Controls;

namespace XGLauncher;

/// <summary>
/// 根据平台能力请求透明或毛玻璃；前景始终保持不透明。
/// </summary>
internal static class WindowEffects
{
    internal static bool MayRequestBlur(bool windows, Version version) => windows &&
        (version.Major > 10 || version.Major == 10 && version.Build >= 17763);
    internal static double SafeOpacity(double value) => double.IsFinite(value) ? Math.Clamp(value, .85, 1) : .96;
    internal static void Apply(Window window, double opacity, bool blur)
    {
        var acrylic = blur && MayRequestBlur(OperatingSystem.IsWindows(), Environment.OSVersion.Version);
        var hints = acrylic ? new[] { WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None } :
            opacity < 1 ? new[] { WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None } : [WindowTransparencyLevel.None];
        if (!window.TransparencyLevelHint.SequenceEqual(hints)) window.TransparencyLevelHint = hints;
        // 不降低整个窗口树的Opacity；背景层独立使用alpha，避免文字和弹层透出底层内容。
        window.Opacity = 1;
    }
}
