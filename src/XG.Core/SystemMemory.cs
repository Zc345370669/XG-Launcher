// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;

namespace XG.Core;

internal static class SystemMemory
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    internal static (long Total, long Available)? TryWindows()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status)
            ? ((long)(status.TotalPhysical / 1048576), (long)(status.AvailablePhysical / 1048576)) : null;
    }
}
