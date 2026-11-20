// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;

namespace XGLauncher;

internal static class Motion
{
    private const uint GetClientAreaAnimation = 0x1042;

    public static bool Enabled => OperatingSystem.IsWindows()
        ? !SystemParametersInfo(GetClientAreaAnimation, 0, out var enabled, 0) || enabled != 0
        : Environment.GetEnvironmentVariable("GTK_ENABLE_ANIMATIONS") != "0";

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint flags);
}
