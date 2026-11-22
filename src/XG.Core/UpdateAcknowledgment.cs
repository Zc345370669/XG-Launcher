// SPDX-License-Identifier: MIT
namespace XG.Core;

/// <summary>不可预测的启动确认标记，只允许在固定临时目录写入 GUID 文件名。</summary>
public static class UpdateAcknowledgment
{
    public static string PathFor(string nonce)
    {
        if (!Guid.TryParseExact(nonce, "N", out _)) throw new InvalidDataException("更新确认标识无效。");
        return Path.Combine(Path.GetTempPath(), "XGLauncher", "update-acks", nonce + ".ack");
    }
    public static void Write(string nonce)
    {
        var file = PathFor(nonce); var parent = Path.GetDirectoryName(file)!; Directory.CreateDirectory(parent);
        for (var directory = new DirectoryInfo(parent); directory != null; directory = directory.Parent)
            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("更新确认目录不能是链接。");
        var temporary = file + ".tmp";
        try
        {
            using (var output = new StreamWriter(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)))
                output.Write(Environment.ProcessId);
            File.Move(temporary, file); // 确认文件只在内容完整后可见，避免读取半写入的 PID。
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
