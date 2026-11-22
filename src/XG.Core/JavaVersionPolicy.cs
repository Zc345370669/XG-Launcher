// SPDX-License-Identifier: MIT
namespace XG.Core;

/// <summary>仅用于缺省建议；官方版本元数据的 Java 要求始终优先，不猜测快照或未知版本。</summary>
public static class JavaVersionPolicy
{
    public static int? Recommend(string minecraft)
    {
        if (!Version.TryParse(minecraft, out var version) || version.Major != 1 || version.Minor < 0) return null;
        return version.Minor switch
        {
            < 17 => 8,
            17 => 16,
            < 20 => 17,
            20 when Math.Max(0, version.Build) < 5 => 17,
            20 or 21 => 21,
            _ => null
        };
    }
}
