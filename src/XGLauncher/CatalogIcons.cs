// SPDX-License-Identifier: MIT
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace XGLauncher;

internal sealed class CatalogIcons(HttpClient http, Action<string> diagnostic, TimeSpan? deadline = null)
{
    private const int MaxBytes = 500_000;
    private readonly SemaphoreSlim slots = new(3);
    private readonly SemaphoreSlim[] stripes = Enumerable.Range(0, 16).Select(_ => new SemaphoreSlim(1)).ToArray();
    private readonly Dictionary<Uri, byte[]?> cache = new();

    public async Task<Bitmap?> LoadAsync(string? source, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var url) || url.Scheme != "https" ||
            url.Host != "cdn.modrinth.com" || !url.IsDefaultPort || url.UserInfo.Length != 0) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(deadline ?? TimeSpan.FromSeconds(10));
        try
        {
            var stripe = stripes[(url.GetHashCode() & int.MaxValue) % stripes.Length];
            await stripe.WaitAsync(timeout.Token);
            try
            {
                byte[]? thumbnail;
                bool found;
                lock (cache) found = cache.TryGetValue(url, out thumbnail);
                if (!found)
                {
                    await slots.WaitAsync(timeout.Token);
                    try
                    {
                        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength > MaxBytes) throw new InvalidDataException();
                        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                        using var stream = new MemoryStream();
                        var buffer = new byte[8192];
                        int n;
                        while ((n = await input.ReadAsync(buffer, timeout.Token)) != 0)
                        {
                            if (stream.Length + n > MaxBytes) throw new InvalidDataException();
                            stream.Write(buffer, 0, n);
                        }
                        thumbnail = await Task.Run(() => Decode(stream.ToArray()), timeout.Token);
                        if (thumbnail == null) diagnostic("catalog-icon: invalid image");
                        lock (cache)
                        {
                            if (cache.Count >= 96) cache.Remove(cache.Keys.First());
                            cache[url] = thumbnail;
                        }
                    }
                    finally { slots.Release(); }
                }
                if (thumbnail == null) return null;
                return await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    using var png = new MemoryStream(thumbnail);
                    return new Bitmap(png);
                });
            }
            finally { stripe.Release(); }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception error)
        {
            diagnostic("catalog-icon: " + error.GetType().Name);
            return null;
        }
    }

    internal static byte[]? Decode(byte[] bytes)
    {
        if (bytes.Length < 8) return null;
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec == null || codec.Info.Width is < 1 or > 2048 || codec.Info.Height is < 1 or > 2048) return null;
        using var original = new SKBitmap(codec.Info.Width, codec.Info.Height);
        if (codec.GetPixels(original.Info, original.GetPixels()) != SKCodecResult.Success) return null;
        using var thumbnail = original.Resize(new SKImageInfo(68, 68), SKFilterQuality.Low);
        using var encoded = thumbnail?.Encode(SKEncodedImageFormat.Png, 100);
        return encoded?.ToArray();
    }
}
