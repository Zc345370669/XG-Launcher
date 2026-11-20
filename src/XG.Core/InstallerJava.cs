// SPDX-License-Identifier: MIT
namespace XG.Core;

/// <summary>安装处理器使用同一已选 Java 运行时的无控制台入口，不改动实例的 Java 绑定。</summary>
internal static class InstallerJava
{
    internal static string? Resolve(string? javaPath, bool windows)
    {
        if (!windows) return javaPath;
        if (string.IsNullOrWhiteSpace(javaPath)) throw new InvalidOperationException("兼容 Java 尚未就绪，无法安装加载器。");
        var full = Path.GetFullPath(javaPath);
        var gui = Path.Combine(Path.GetDirectoryName(full)!, "javaw.exe");
        // CmlLib 的 ProcessWrapper 会把 CreateNoWindow 重设为 false；仅设置调用方标志不能消除安装黑框。
        // javaw 保持同一 JVM 及参数，输出仍通过现有安装器的两个重定向管道收集。
        if (!File.Exists(gui)) throw new InvalidOperationException("当前 Java 缺少 javaw.exe，无法静默安装加载器；请在 Java 管理中选择完整的 Windows Java 运行时。");
        return gui;
    }
}
