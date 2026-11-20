// SPDX-License-Identifier: MIT
using System.Buffers;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace XGLauncher;

/// <summary>
/// Downloads a GitHub release into an isolated staging directory.
/// This component deliberately never modifies the running executable or user data.
/// </summary>
internal static class GitHubUpdateDownload
{
    internal sealed record Progress(long Received, long Total, string Stage);

    internal static async Task<string> StageAsync(
        GitHubReleaseFeed.ReleaseInfo release,
        string stagingRoot,
        IProgress<Progress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.AssetUrl.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(release.AssetUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Update URL must be an official GitHub HTTPS asset.");
        if (release.Size <= 0 || release.Size > 2L * 1024 * 1024 * 1024)
            throw new InvalidOperationException("Invalid update size.");

        Directory.CreateDirectory(stagingRoot);
        var working = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(working);
        var archive = Path.Combine(working, "update.zip");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("XGLauncher", "0.1"));
            using var response = await client.GetAsync(release.AssetUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is not { Scheme: "https" } finalUrl ||
                !(finalUrl.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                  finalUrl.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Unexpected update download host.");
            var reportedLength = response.Content.Headers.ContentLength;
            if (reportedLength.HasValue && reportedLength.Value != release.Size)
                throw new InvalidDataException("Update asset length differs from release metadata.");

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(archive, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, useAsync: true))
            {
                var buffer = ArrayPool<byte>.Shared.Rent(81920);
                try
                {
                    long received = 0;
                    int count;
                    while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                    {
                        received += count;
                        if (received > release.Size)
                            throw new InvalidDataException("Update exceeds expected size.");
                        await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                        progress?.Report(new Progress(received, release.Size, "Downloading"));
                    }
                    if (received != release.Size)
                        throw new InvalidDataException("Incomplete update download.");
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
            }

            // Reject traversal, symlinks and zip bombs before extraction.
            using (var zip = ZipFile.OpenRead(archive))
            {
                long expanded = 0;
                if (zip.Entries.Count > 10000)
                    throw new InvalidDataException("Too many archive entries.");
                foreach (var entry in zip.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = entry.FullName.Replace('\\', '/');
                    if (name.StartsWith('/') || name.Contains(':') ||
                        name.Split('/').Any(x => x == ".."))
                        throw new InvalidDataException("Unsafe archive path.");
                    var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
                    if (unixType == 0xA000)
                        throw new InvalidDataException("Symbolic links are not supported.");
                    expanded = checked(expanded + entry.Length);
                    if (expanded > 4L * 1024 * 1024 * 1024)
                        throw new InvalidDataException("Expanded archive exceeds size limit.");
                }
            }
            var extracted = Path.Combine(working, "payload");
            ZipFile.ExtractToDirectory(archive, extracted);
            if (!Directory.EnumerateFiles(extracted, "XGLauncher.exe", SearchOption.AllDirectories).Any())
                throw new InvalidDataException("Update does not contain XGLauncher.exe.");
            progress?.Report(new Progress(release.Size, release.Size, "Ready"));
            return extracted;
        }
        catch
        {
            try { Directory.Delete(working, recursive: true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}
