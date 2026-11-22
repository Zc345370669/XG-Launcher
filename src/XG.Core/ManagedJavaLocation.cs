// SPDX-License-Identifier: MIT
namespace XG.Core;

/// <summary>定位 Windows 官方发行商的发现根目录；不把该候选目录当作可直接写入的安装目标。</summary>
internal static class ManagedJavaLocation
{
    internal static string Resolve(bool windows, string programFiles, string portableFallback)
    {
        if (!windows) return portableFallback;
        if (string.IsNullOrWhiteSpace(programFiles) || !Path.IsPathFullyQualified(programFiles))
            throw new DirectoryNotFoundException("Windows Program Files directory unavailable.");
        // 只把官方默认目录加入发现候选；不得由启动器创建、解压或任意写入 Program Files。
        return Path.Combine(programFiles, "Eclipse Adoptium");
    }
}
