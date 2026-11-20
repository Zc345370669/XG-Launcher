// SPDX-License-Identifier: MIT
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XGLauncher;

/// <summary>
/// 只读取官方 GitHub Releases 元数据，不执行下载、替换或启动外部程序。
/// 仅接受官方仓库、精确资源路径及 GitHub 提供的 SHA-256；不发送账号或本地数据。
/// </summary>
internal static class GitHubReleaseFeed
{
    private static readonly Uri ReleasesUri =
        new("https://api.github.com/repos/Zc345370669/XG-Launcher/releases?per_page=30");
    private static readonly Regex TagPattern =
        new(@"\Av(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?<suffix>-(?:alpha|beta|rc)(?:\.\d+)?)?\z",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal sealed record ReleaseInfo(string Tag, Uri AssetUrl, long Size, string? Description, string Sha256);

    /// <summary>返回高于当前版本的最新候选；alpha 通道允许预发布版本。</summary>
    internal static async Task<ReleaseInfo?> CheckAsync(
        string currentTag, bool includePrerelease, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        return await CheckAsync(client, currentTag, includePrerelease, cancellationToken);
    }

    internal static async Task<ReleaseInfo?> CheckAsync(HttpClient client, string currentTag, bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("XGLauncher/0.1");
        if (!TryParseVersion(currentTag, out var current))
            throw new ArgumentException("Invalid application version.", nameof(currentTag));

        using var response = await client.GetAsync(ReleasesUri, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var bounded = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (bounded.Length + count > 2 * 1024 * 1024) throw new InvalidDataException("Release metadata exceeds limit.");
            bounded.Write(buffer, 0, count);
        }
        using var document = JsonDocument.Parse(bounded.ToArray());

        ReleaseInfo? best = null;
        Version? bestVersion = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() ||
                (!includePrerelease && release.GetProperty("prerelease").GetBoolean()))
                continue;

            var tag = release.GetProperty("tag_name").GetString();
            if (tag is null || !TryParseVersion(tag, out var candidate) ||
                CompareVersions(candidate, current) <= 0 ||
                (bestVersion is not null && CompareVersions(candidate, bestVersion) <= 0))
                continue;

            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (!string.Equals(asset.GetProperty("name").GetString(),
                        "XGLauncher-portable-win-x64.zip", StringComparison.OrdinalIgnoreCase))
                    continue;
                var urlText = asset.GetProperty("browser_download_url").GetString();
                if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url) ||
                    url.Scheme != Uri.UriSchemeHttps ||
                    !IsOfficialAsset(url, tag))
                    continue;
                var size = asset.GetProperty("size").GetInt64();
                if (size <= 0 || size > 512L * 1024 * 1024 ||
                    !asset.TryGetProperty("digest", out var digest) || digest.GetString() is not { } hash ||
                    !Regex.IsMatch(hash, @"\Asha256:[0-9a-fA-F]{64}\z")) continue;
                best = new ReleaseInfo(tag, url, size,
                    release.TryGetProperty("body", out var body) ? body.GetString() : null, hash[7..]);
                bestVersion = candidate;
                break;
            }
        }
        return best;
    }

    internal static bool IsOfficialAsset(Uri url, string tag) =>
        url.Scheme == "https" && url.Host == "github.com" && url.IsDefaultPort && url.UserInfo.Length == 0 &&
        url.Query.Length == 0 && url.Fragment.Length == 0 &&
        url.AbsolutePath == "/Zc345370669/XG-Launcher/releases/download/" + Uri.EscapeDataString(tag) + "/XGLauncher-portable-win-x64.zip";

    private static bool TryParseVersion(string tag, out Version version)
    {
        version = new Version(0, 0, 0);
        var match = TagPattern.Match(tag);
        if (!match.Success ||
            !int.TryParse(match.Groups["major"].Value, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, out var minor) ||
            !int.TryParse(match.Groups["patch"].Value, out var patch))
            return false;
        // Version.Revision encodes release maturity; alpha < beta < rc < stable.
        var suffix = match.Groups["suffix"].Value.ToLowerInvariant();
        var maturity = suffix.StartsWith("-alpha", StringComparison.Ordinal) ? 0 :
            suffix.StartsWith("-beta", StringComparison.Ordinal) ? 100000 :
            suffix.StartsWith("-rc", StringComparison.Ordinal) ? 200000 : 300000;
        if (suffix.Contains('.'))
        {
            if (!int.TryParse(suffix[(suffix.IndexOf('.') + 1)..], out var sequence) ||
                sequence >= 100000) return false;
            maturity += sequence;
        }
        version = new Version(major, minor, patch, maturity);
        return true;
    }

    private static int CompareVersions(Version left, Version right) => left.CompareTo(right);
}
