// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;

namespace XGLauncher;

/// <summary>
/// 在支持的Windows版本上请求DWM圆角偏好，不保证系统一定采用。
/// </summary>
internal static class NativeWindowRounding
{
    internal static bool SupportsCornerPreference(bool windows, Version version) => windows &&
        (version.Major > 10 || version.Major == 10 && version.Build >= 22000);

    internal static bool CompositionAvailable()
    {
        if (!SupportsCornerPreference(OperatingSystem.IsWindows(), Environment.OSVersion.Version)) return false;
        try { return DwmIsCompositionEnabled(out var enabled) >= 0 && enabled; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    internal static bool TryApply(Window window)
    {
        if (!SupportsCornerPreference(OperatingSystem.IsWindows(), Environment.OSVersion.Version) ||
            window.TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero) return false;
        try
        {
            // 仅Windows11能力路径请求圆角；最大化取消圆角偏好，兼容回退不使用整数HRGN裁切。
            var corner = window.WindowState == WindowState.Normal ? 2 : 1;
            var dark = ThemePalette.Current.Dark ? 1 : 0;
            var border = ColorRef(ThemePalette.Line.Color);
            var caption = ColorRef(ThemePalette.Background.Color);
            // 调用成功仅表示偏好已提交，不证明DWM已对顶层窗口应用圆角。
            return DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int)) >= 0 &&
                DwmSetWindowAttribute(handle, 34, ref border, sizeof(int)) >= 0 &&
                DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int)) >= 0 &&
                DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) >= 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }
    internal static int ColorRef(Color color) => color.R | color.G << 8 | color.B << 16;
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
}
