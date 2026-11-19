// SPDX-License-Identifier: MIT
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;

namespace XG.Core;

internal static class VerifiedTransfer
{
    private static readonly ConditionalWeakTable<HttpClient, TransferGate> gates = new();

    internal static void SetConcurrency(HttpClient client, int limit) => gates.GetOrCreateValue(client).SetLimit(limit);

    internal static void SetSpeedLimit(HttpClient client, long bytesPerSecond) =>
        gates.GetOrCreateValue(client).SetSpeedLimit(bytesPerSecond);

    internal static async Task StageAsync(HttpClient http, Uri uri, string partial, long size, string checksum,
        Action<long, long> progress, Action<string> state, CancellationToken token)
    {
        var identity = Path.GetFullPath(partial) + "|" + uri.AbsoluteUri + "|" + size + "|" + checksum;
        // 同一可信资源共享传输；等待者取消仅停止等待，底层操作继续供其他使用者复用。
        await SharedResourceCoordinator.RunAsync(identity, async () => {
            using var commit = await JavaInstallGate.EnterAsync(partial + ".transfer");
            var record = partial + ".identity.json";
            var fingerprint = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
            string? previous = null;
            if(File.Exists(record)) { try { previous = System.Text.Json.JsonSerializer.Deserialize<TransferIdentity>(File.ReadAllText(record))?.Identity; } catch(System.Text.Json.JsonException) {} }
            void Save(string status) {
                Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
                var temporary = record + "." + Guid.NewGuid().ToString("N");
                File.WriteAllText(temporary,System.Text.Json.JsonSerializer.Serialize(new TransferIdentity(fingerprint,status)));
                File.Move(temporary,record,true);
            }
            if(await MatchesAsync(partial,size,checksum,CancellationToken.None)) { Save("Verified"); return; }
            if(previous != fingerprint || (File.Exists(partial) && new FileInfo(partial).Length >= size)) Reset(partial);
            Save("Pending");
            var gate = gates.GetOrCreateValue(http);
            await gate.EnterAsync(CancellationToken.None);
            try { await StageCoreAsync(http, uri, partial, size, checksum,
                (bytes,total) => { if(!token.IsCancellationRequested) progress(bytes,total); },
                text => { if(!token.IsCancellationRequested) state(text); },gate,CancellationToken.None); }
            catch { Save("Failed"); throw; }
            finally { gate.Leave(); }
            Save("Verified");
        },token);
    }

    private sealed record TransferIdentity(string Identity,string State);

    // sync只保护此门控的并发计数、速率时间线与唤醒信号，不保护上层队列集合。
    private sealed class TransferGate
    {
        private readonly object sync = new();
        private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int active;
        private int limit = 1;
        private long bytesPerSecond;
        private long nextTick;

        public void SetSpeedLimit(long rate)
        {
            lock (sync) { bytesPerSecond = rate; nextTick = 0; }
        }

        public async Task ThrottleAsync(int bytes, CancellationToken token)
        {
            long wait;
            lock (sync)
            {
                if (bytesPerSecond <= 0) return;
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                nextTick = Math.Max(now, nextTick) + bytes * System.Diagnostics.Stopwatch.Frequency / bytesPerSecond;
                wait = nextTick - now;
            }
            await Task.Delay(TimeSpan.FromSeconds((double)wait / System.Diagnostics.Stopwatch.Frequency), token);
        }

        public async Task EnterAsync(CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                Task available;
                lock (sync)
                {
                    if (active < limit) { active++; return; }
                    available = changed.Task;
                }
                await available.WaitAsync(token);
            }
        }

        public void Leave() { lock (sync) { active--; Wake(); } }
        public void SetLimit(int value) { lock (sync) { limit = value; Wake(); } }

        private void Wake()
        {
            var previous = changed;
            changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            previous.TrySetResult();
        }
    }

    private static async Task StageCoreAsync(HttpClient http, Uri uri, string partial, long size, string checksum,
        Action<long, long> progress, Action<string> state, TransferGate gate, CancellationToken token)
    {
        var parent = Path.GetDirectoryName(partial)!;
        Directory.CreateDirectory(parent);
        var tagPath = partial + ".etag";
        var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (existing > size) { Reset(partial); existing = 0; }
        CheckSpace(parent, size - existing);
        for (var retry = 0; ; retry++)
        {
            try
            {
                var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                progress(offset, size);
                if (offset < size)
                {
                    state("连接中");
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    EntityTagHeaderValue? saved = null;
                    if (offset > 0)
                    {
                        request.Headers.Range = new(offset, null);
                        if (File.Exists(tagPath) && EntityTagHeaderValue.TryParse(await File.ReadAllTextAsync(tagPath, token), out var tagValue) &&
                            tagValue is { IsWeak: false })
                        {
                            saved = tagValue;
                            request.Headers.IfRange = new RangeConditionHeaderValue(saved);
                        }
                    }
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                    if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && offset > 0 && retry < 2)
                    { Reset(partial); continue; }
                    if (response.StatusCode == HttpStatusCode.TooManyRequests && retry < 2 &&
                        response.Headers.RetryAfter is { } retryAfter)
                    {
                        var delay = retryAfter.Delta ?? (retryAfter.Date - DateTimeOffset.UtcNow) ?? TimeSpan.Zero;
                        state("等待中");
                        await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 30)), token);
                        continue;
                    }
                    response.EnsureSuccessStatusCode();
                    if (response.RequestMessage?.RequestUri != uri)
                        throw new InvalidDataException("下载地址发生重定向。");
                    // 服务器忽略Range而返回完整文件时必须重置暂存，不能把完整响应追加到旧片段。
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        if (offset > 0) { Reset(partial); CheckSpace(parent, size); }
                        offset = 0;
                        if (response.Content.Headers.ContentLength is { } length && length != size)
                            throw new InvalidDataException("服务器提供的文件大小与元数据不符。");
                    }
                    else if (response.StatusCode != HttpStatusCode.PartialContent || offset == 0 ||
                        response.Content.Headers.ContentRange is not { } range || range.Unit != "bytes" ||
                        range.From != offset || range.To != size - 1 || range.Length != size ||
                        response.Content.Headers.ContentLength is { } partLength && partLength != size - offset ||
                        saved != null && response.Headers.ETag != saved)
                    {
                        Reset(partial);
                        if (retry < 2) continue;
                        throw new InvalidDataException("服务器返回了无效的续传范围或文件标识。");
                    }
                    if (response.Headers.ETag is { IsWeak: false } tag)
                        await File.WriteAllTextAsync(tagPath, tag.ToString(), token);
                    state("下载中");
                    await using var input = await response.Content.ReadAsStreamAsync(token);
                    await using var output = new FileStream(partial, offset == 0 ? FileMode.Create : FileMode.Append,
                        FileAccess.Write, FileShare.None, 65536, useAsync: true);
                    var buffer = new byte[65536];
                    var sample = System.Diagnostics.Stopwatch.StartNew();
                    while (true)
                    {
                        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                        readTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                        int count;
                        try { count = await input.ReadAsync(buffer, readTimeout.Token); }
                        catch (OperationCanceledException) when (!token.IsCancellationRequested)
                        { throw new TimeoutException("连接连续 30 秒未收到数据。"); }
                        if (count == 0) break;
                        await gate.ThrottleAsync(count, token);
                        offset += count;
                        if (offset > size) throw new InvalidDataException("下载文件超过声明大小。");
                        await output.WriteAsync(buffer.AsMemory(0, count), token);
                        if (sample.ElapsedMilliseconds >= 250) { progress(offset, size); sample.Restart(); }
                    }
                    if (offset != size) throw new EndOfStreamException("文件未完整接收。");
                }
                progress(size, size);
                state("校验中");
                // 只有声明大小和可信哈希均匹配才可提交；不把收到最后一个字节视为校验成功。
                if (!await MatchesAsync(partial, size, checksum, token))
                {
                    Reset(partial);
                    if (retry < 2) continue;
                    throw new InvalidDataException("文件哈希校验失败。");
                }
                return;
            }
            catch (Exception error) when (retry < 2 && IsTransient(error, token))
            {
                state("等待中");
                await Task.Delay(TimeSpan.FromSeconds(1 << retry), token);
            }
        }
    }

    private static bool IsTransient(Exception error, CancellationToken token) => !token.IsCancellationRequested &&
        (error is EndOfStreamException or TimeoutException or HttpIOException or OperationCanceledException ||
            error is HttpRequestException http && (http.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
                (int)http.StatusCode.Value >= 500));

    internal static async Task<bool> MatchesAsync(string path, long size, string checksum, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != size) return false;
        await using var input = File.OpenRead(path);
        var hash = checksum.Length switch
        {
            40 => await SHA1.HashDataAsync(input, token),
            64 => await SHA256.HashDataAsync(input, token),
            128 => await SHA512.HashDataAsync(input, token),
            _ => throw new InvalidDataException("缺少受支持的文件哈希。")
        };
        return Convert.ToHexString(hash).Equals(checksum, StringComparison.OrdinalIgnoreCase);
    }

    internal static void Reset(string partial)
    {
        if (File.Exists(partial)) File.Delete(partial);
        ClearMetadata(partial);
    }

    internal static void ClearMetadata(string partial)
    {
        var sidecar = partial + ".etag";
        if (File.Exists(sidecar)) File.Delete(sidecar);
        var identity = partial + ".identity.json";
        if (File.Exists(identity)) File.Delete(identity);
    }

    private static void CheckSpace(string path, long required)
    {
        var disk = new DriveInfo(Path.GetPathRoot(path)!);
        if (disk.IsReady && disk.AvailableFreeSpace < required)
            throw new IOException("磁盘可用空间不足。");
    }

    internal static string Describe(Exception error) => error switch
    {
        HttpRequestException { StatusCode: { } status } => $"下载服务器返回 HTTP {(int)status}。",
        HttpRequestException or HttpIOException => "下载连接失败，请检查网络后重试。",
        TimeoutException or OperationCanceledException => "下载连接超时，请重试。",
        UnauthorizedAccessException => "下载目录没有写入权限。",
        IOException io when (io.HResult & 0xffff) is 39 or 112 => "磁盘可用空间不足。",
        _ => new GameLogSanitizer(null).Redact(error.Message)
    };
}
