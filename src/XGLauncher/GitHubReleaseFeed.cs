// SPDX-License-Identifier: MIT
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XGLauncher;

/// <summary>
/// 只读取官方 GitHub Releases 元数据，不执行下载、替换或启动外部程序。
/// 后续更新 UI 与独立更新器必须在验证完成后再接入。
/// </summary>
internal static class GitHubReleaseFeed
{
    private static readonly Uri ReleasesUri =
        new("https://api.github.com/repos/Zc345370669/XG-Launcher/releases?per_page=30");
    private static readonly Regex TagPattern =
        new(@"^v(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?<suffix>-(?:alpha|beta|rc)(?:\.\d+)?)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal sealed record ReleaseInfo(string Tag, Uri AssetUrl, long Size, string? Description);

    /// <summary>返回高于当前版本的最新候选；alpha 通道允许预发布版本。</summary>
    internal static async Task<ReleaseInfo?> CheckAsync(
        string currentTag, bool includePrerelease, CancellationToken cancellationToken = default)
    {
        if (!TryParseVersion(currentTag, out var current))
            throw new ArgumentException("Invalid application version.", nameof(currentTag));

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("XGLauncher", "0.1"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await client.GetAsync(ReleasesUri, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

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
                    !string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                    continue;
                var size = asset.GetProperty("size").GetInt64();
                if (size <= 0) continue;
                best = new ReleaseInfo(tag, url, size,
                    release.TryGetProperty("body", out var body) ? body.GetString() : null);
                bestVersion = candidate;
                break;
            }
        }
        return best;
    }

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
