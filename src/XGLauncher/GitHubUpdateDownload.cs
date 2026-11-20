// SPDX-License-Identifier: MIT
using System.Security.Cryptography;
using XG.Core;

namespace XGLauncher;

/// <summary>在隔离目录中流式下载；重定向、大小、散列及 ZIP 写入范围均验证后才允许交给更新器。</summary>
internal static class GitHubUpdateDownload
{
    internal sealed record Progress(long Received, long Total, string Stage);
    internal sealed record StagedUpdate(string Archive);

    internal static async Task<StagedUpdate> StageAsync(GitHubReleaseFeed.ReleaseInfo release, string stagingRoot,
        IProgress<Progress>? progress, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(30) };
        return await StageAsync(client, release, stagingRoot, progress, cancellationToken);
    }

    internal static async Task<StagedUpdate> StageAsync(HttpClient client, GitHubReleaseFeed.ReleaseInfo release,
        string stagingRoot, IProgress<Progress>? progress, CancellationToken cancellationToken = default)
    {
        if (!GitHubReleaseFeed.IsOfficialAsset(release.AssetUrl, release.Tag) || release.Size <= 0 || release.Size > 512L * 1024 * 1024 ||
            !System.Text.RegularExpressions.Regex.IsMatch(release.Sha256, @"\A[0-9a-fA-F]{64}\z"))
            throw new InvalidDataException("Invalid official update metadata.");
        var working = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(working);
        var archive = Path.Combine(working, "update.zip");
        try
        {
            var url = release.AssetUrl;
            HttpResponseMessage? response = null;
            try
            {
                for (int hop = 0; hop < 6; hop++)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.UserAgent.ParseAdd("XGLauncher/0.1");
                    response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) break;
                    var location = response.Headers.Location ?? throw new InvalidDataException("Missing redirect.");
                    var next = location.IsAbsoluteUri ? location : new Uri(url, location);
                    response.Dispose(); response = null;
                    // 在发出下一请求之前验证主机；不接受任意 githubusercontent 子域或降级 HTTP。
                    if (next.Scheme != "https" || !next.IsDefaultPort || next.UserInfo.Length != 0 ||
                        !(next.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com" ||
                          GitHubReleaseFeed.IsOfficialAsset(next, release.Tag))) throw new InvalidDataException("Unsafe update redirect.");
                    url = next;
                }
                if (response == null) throw new InvalidDataException("Too many update redirects.");
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long size && size != release.Size) throw new InvalidDataException("Asset length mismatch.");
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var target = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    byte[] buffer = new byte[81920]; long received = 0; int count;
                    while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        received += count;
                        if (received > release.Size) throw new InvalidDataException("Asset exceeds size.");
                        hash.AppendData(buffer, 0, count);
                        await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                        progress?.Report(new(received, release.Size, "Downloading"));
                    }
                    if (received != release.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(release.Sha256)))
                        throw new InvalidDataException("Asset checksum mismatch.");
                }
                progress?.Report(new(release.Size, release.Size, "Verifying"));
                await Task.Run(() => { using var verified = UpdateFiles.Prepare(new(archive, release.Sha256, working, 0, 0)); }, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new(release.Size, release.Size, "Ready"));
                return new(archive);
            }
            finally { response?.Dispose(); }
        }
        catch
        {
            try { Directory.Delete(working, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}
