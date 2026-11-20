// SPDX-License-Identifier: MIT
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XG.Core;

public sealed record LauncherRelease(string Version, string AssetName, Uri AssetUrl, long Size, string Sha256);

/// <summary>仅访问官方公开 Release；不发送账号、游戏目录、凭据或个人配置。</summary>
public sealed class GitHubUpdates(HttpClient http)
{
    public const string Repository = "https://github.com/Zc345370669/XG-Launcher";
    private const string Api = "https://api.github.com/repos/Zc345370669/XG-Launcher/releases?per_page=30";
    private const long MaxPackage = 512L * 1024 * 1024;

    public async Task<LauncherRelease?> CheckAsync(string currentVersion, CancellationToken token = default)
    {
        using var response = await SendAsync(new Uri(Api), token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var json = await ReadBoundedAsync(response.Content, 2 * 1024 * 1024, token);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("更新服务返回的数据无效。");
        LauncherRelease? best = null;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
                !item.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { } version ||
                !IsVersion(version) || CompareVersions(version, currentVersion) <= 0 ||
                !item.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (name != "XGLauncher-portable-win-x64.zip" && name != $"XGLauncher_{version}_Windows_x64.zip") continue;
                if (asset.GetProperty("state").GetString() != "uploaded" ||
                    !asset.GetProperty("size").TryGetInt64(out var size) || size < 1 || size > MaxPackage ||
                    !asset.TryGetProperty("digest", out var digestField) || digestField.GetString() is not { } digest ||
                    !Regex.IsMatch(digest, "\\Asha256:[0-9a-fA-F]{64}\\z") ||
                    !Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var uri) ||
                    !SafeAsset(uri, version, name)) continue;
                if (best == null || CompareVersions(version, best.Version) > 0)
                    best = new(version, name, uri, size, digest[7..].ToLowerInvariant());
            }
        }
        return best;
    }

    /// <summary>校验长度与 SHA-256 后才提交更新包；失败删除本次临时文件。</summary>
    public async Task<string> DownloadAsync(LauncherRelease release, string directory, Action<double> progress, CancellationToken token = default)
    {
        if (!IsVersion(release.Version) || !SafeAsset(release.AssetUrl, release.Version, release.AssetName) ||
            release.Size is < 1 or > MaxPackage || !Regex.IsMatch(release.Sha256, "\\A[0-9a-fA-F]{64}\\z"))
            throw new InvalidDataException("更新包信息无效。");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, release.AssetName);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            var uri = release.AssetUrl;
            for (var redirect = 0; redirect < 5; redirect++)
            {
                using var response = await SendAsync(uri, token);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    uri = response.Headers.Location is { } location ? new Uri(uri, location) : throw new InvalidDataException("更新下载重定向无效。");
                    if (uri.Scheme != "https" || uri.UserInfo.Length != 0 || !uri.IsDefaultPort ||
                        uri.Host is not ("release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                        throw new InvalidDataException("更新下载重定向到非官方地址。");
                    continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } length && length != release.Size)
                    throw new InvalidDataException("更新包大小不符。");
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var input = await response.Content.ReadAsStreamAsync(token))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                {
                    var buffer = new byte[65536]; long total = 0; int count;
                    while ((count = await input.ReadAsync(buffer, token)) != 0)
                    {
                        total += count;
                        if (total > release.Size) throw new InvalidDataException("更新包大小不符。");
                        hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), token);
                        progress(100d * total / release.Size);
                    }
                    if (total != release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("更新包校验失败。");
                }
                token.ThrowIfCancellationRequested();
                File.Move(temporary, destination, true);
                return destination;
            }
            throw new InvalidDataException("更新下载重定向次数过多。");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("XGLauncher/0.1.0-alpha");
        if (uri.Host == "api.github.com")
        {
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        }
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    }
    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken token)
    {
        await using var input = await content.ReadAsStreamAsync(token); using var output = new MemoryStream();
        var buffer = new byte[16384]; int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        { if (output.Length + count > limit) throw new InvalidDataException("更新服务数据过大。"); output.Write(buffer, 0, count); }
        return output.ToArray();
    }
    private static bool SafeAsset(Uri uri, string version, string name) => (name == "XGLauncher-portable-win-x64.zip" || name == $"XGLauncher_{version}_Windows_x64.zip") && uri.Scheme == "https" && uri.Host == "github.com" &&
        uri.UserInfo.Length == 0 && uri.IsDefaultPort && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        uri.AbsolutePath == $"/Zc345370669/XG-Launcher/releases/download/{version}/{name}";
    private static bool IsVersion(string value) => Regex.IsMatch(value, "\\Av?(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?\\z");
    internal static int CompareVersions(string left, string right)
    {
        if (!IsVersion(left) || !IsVersion(right)) throw new InvalidDataException("版本号无效。");
        var a = left.TrimStart('v').Split('-', 2); var b = right.TrimStart('v').Split('-', 2);
        var result = Version.Parse(a[0]).CompareTo(Version.Parse(b[0])); if (result != 0) return result;
        if (a.Length != b.Length) return a.Length == 1 ? 1 : -1;
        if (a.Length == 1) return 0;
        var x = a[1].Split('.'); var y = b[1].Split('.');
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            var xn = System.Numerics.BigInteger.TryParse(x[i], out var xv); var yn = System.Numerics.BigInteger.TryParse(y[i], out var yv);
            result = xn && yn ? xv.CompareTo(yv) : xn != yn ? xn ? -1 : 1 : StringComparer.Ordinal.Compare(x[i], y[i]);
            if (result != 0) return result;
        }
        return x.Length.CompareTo(y.Length);
    }
}
