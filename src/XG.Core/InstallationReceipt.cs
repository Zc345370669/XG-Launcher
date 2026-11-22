// SPDX-License-Identifier: MIT
using System.Security.Cryptography;
using System.Text.Json;

namespace XG.Core;

/// <summary>
/// 通过配置、必要文件和Java证据判断安装状态，不以下载百分比代替本地校验。
/// </summary>
public static class InstallationReceipt
{
    public sealed record FileEvidence(string Path, long Size, string Sha256);
    public sealed record Receipt(string Configuration, FileEvidence[] Files, string? Profile = null, string? Java = null, int JavaMajor = 0);
    public static string Configuration(Instance i) => JsonSerializer.Serialize(new[] { i.Version, i.Loader, i.LoaderVersion, i.Java });
    private static string Index(InstanceStore store, Instance i) => Path.Combine(store.DirectoryFor(i), ".xg-installation.json");
    public static async Task WriteAsync(InstanceStore store, Instance i, IEnumerable<string> required, CancellationToken token, string? profile = null, string? java = null, int javaMajor = 0)
    {
        var root = Path.GetDirectoryName(store.SharedMinecraft ?? store.Root)!;
        var files = new List<FileEvidence>();
        foreach (var path in required.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) throw new FileNotFoundException("安装校验缺少必要文件。", path);
            var verified = await FileVerificationCache.ReadAsync(path, token);
            files.Add(new(StoredPath(root, path), verified.Size, verified.Hash));
        }
        if (files.Count == 0) throw new InvalidDataException("安装文件清单为空。");
        InstanceStore.WriteJson(Index(store, i), new Receipt(Configuration(i), files.ToArray(), profile, java is null ? null : StoredPath(root, java), javaMajor));
    }
    private static string StoredPath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative.Split(Path.DirectorySeparatorChar)[0] != ".."
            ? relative : Path.GetFullPath(path);
    }
    public static async Task<Receipt> ReadAsync(InstanceStore store, Instance i) =>
        JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(Index(store, i))) ?? throw new InvalidDataException("安装证据无效。");
    public static string ResolvePath(InstanceStore store, string path) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(store.SharedMinecraft ?? store.Root)!, path));

    public static async Task<string> InspectAsync(InstanceStore store, Instance i, CancellationToken token = default, bool force = false)
    {
        try
        {
            if (!File.Exists(Index(store, i))) return "未安装";
            var receipt = JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(Index(store, i), token));
            if (receipt?.Files is not { Length: > 0 }) return "文件损坏";
            if (receipt.Configuration != Configuration(i) || string.IsNullOrWhiteSpace(receipt.Profile) ||
                string.IsNullOrWhiteSpace(receipt.Java)) return "未安装";
            var root = Path.GetDirectoryName(store.SharedMinecraft ?? store.Root)!;
            FileVerificationCache.Watch(store.SharedMinecraft ?? store.InstanceRoot);
            foreach (var file in receipt.Files)
            {
                if (file == null || string.IsNullOrWhiteSpace(file.Path)) return "文件损坏";
                var path = Path.GetFullPath(Path.Combine(root, file.Path));
                if (!File.Exists(path)) return "文件损坏";
                var rootRelative = Path.GetRelativePath(store.SharedMinecraft ?? store.InstanceRoot, path);
                if (rootRelative.Split(Path.DirectorySeparatorChar)[0] == "..") FileVerificationCache.Watch(Path.GetDirectoryName(path)!, false);
                var verified = await FileVerificationCache.ReadAsync(path, token, force);
                if (verified.Size != file.Size || !verified.Hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) return "文件损坏";
            }
            if (receipt.JavaMajor > 0 && await JavaDiscovery.CompatibleAsync(ResolvePath(store, receipt.Java),
                store.ManagedJavaRoot, receipt.JavaMajor) == null) return "文件损坏";
            return "已安装";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException or InvalidOperationException) { return "文件损坏"; }
    }
}
