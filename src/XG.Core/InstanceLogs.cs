// SPDX-License-Identifier: MIT
namespace XG.Core;

/// <summary>新日志写入隔离版本 logs，读取时兼容旧版留在版本根目录的日志。</summary>
public static class InstanceLogs
{
    public static string Resolve(string directory, string name, bool write = false)
    {
        if (name is not ("xg-launch.log" or "xg-loader.log")) throw new ArgumentException("未知日志类型。", nameof(name));
        var logs = Path.Combine(directory, "logs"); var current = Path.Combine(logs, name);
        if (write) { Directory.CreateDirectory(logs); return current; }
        return File.Exists(current) ? current : Path.Combine(directory, name);
    }
}
