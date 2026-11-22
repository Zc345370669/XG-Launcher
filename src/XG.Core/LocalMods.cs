// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Security.Cryptography;

namespace XG.Core;

public static class LocalMods
{
    public static bool Import(string source, string directory)
    {
        var filename = Path.GetFileName(source);
        if (!filename.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("请选择 JAR 文件。");
        using (var zip = ZipFile.OpenRead(source))
            if (!zip.Entries.Any(e => e.FullName is "fabric.mod.json" or "quilt.mod.json" or
                "META-INF/mods.toml" or "META-INF/neoforge.mods.toml" or "mcmod.info"))
                throw new InvalidDataException("没有找到已知加载器的模组清单。");
        Directory.CreateDirectory(directory);
        var target = InstanceStore.SafeFile(directory, filename);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            File.Copy(source, temporary);
            try
            {
                File.Move(temporary, target, false);
                return true;
            }
            catch (IOException) when (File.Exists(target))
            {
                using var incoming = File.OpenRead(temporary);
                using var existing = File.OpenRead(target);
                if (incoming.Length == existing.Length &&
                    CryptographicOperations.FixedTimeEquals(SHA256.HashData(incoming), SHA256.HashData(existing)))
                    return false;
                throw new IOException($"同名模组内容不同，已保留现有文件。来源：{source}；目标：{target}。");
            }
        }
        finally { File.Delete(temporary); }
    }
}
