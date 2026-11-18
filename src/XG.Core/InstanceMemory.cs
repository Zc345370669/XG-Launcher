// SPDX-License-Identifier: MIT
namespace XG.Core;

public static class InstanceMemory
{
    public static int Recommend(Instance instance, int modCount)
    {
        if (SystemMemory.TryWindows() is { } windows)
            return Recommend(windows.Total, windows.Available, instance.Version, modCount, instance.Pack);
        var info = GC.GetGCMemoryInfo();
        var total = info.TotalAvailableMemoryBytes / (1024 * 1024);
        var available = info.MemoryLoadBytes > 0
            ? Math.Max(0, (info.HighMemoryLoadThresholdBytes - info.MemoryLoadBytes) / (1024 * 1024))
            : total / 2;
        return Recommend(total, available, instance.Version, modCount, instance.Pack);
    }

    public static int Recommend(long total, long available, string version, int modCount, bool pack)
    {
        var parts = version.Split('.');
        var older = parts.Length >= 2 && parts[0] == "1" && int.TryParse(parts[1], out var minor) && minor <= 16;
        var desired = (older ? 2048 : 3072) + Math.Clamp(modCount, 0, 250) * 24 + (pack ? 1024 : 0);
        var budget = Math.Min(total - 2048, Math.Min(total * 2 / 3, available - 768));
        return (int)Math.Clamp(Math.Min(desired, budget) / 256 * 256, 512, 12288);
    }
}
