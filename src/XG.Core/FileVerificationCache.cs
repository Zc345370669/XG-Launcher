// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace XG.Core;

/// <summary>
/// 缓存本次进程内的文件哈希；元数据、文件通知和通知溢出使结果失效。
/// </summary>
public static class FileVerificationCache
{
    private sealed record Entry(long Size, long Written, long Created, long Generation, string Digest);
    private static readonly ConcurrentDictionary<string, Entry> cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, long> generations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);
    private static long reads;
    public static long HashReads => Interlocked.Read(ref reads);
    public static void Watch(string directory, bool recursive = true)
    {
        if (!Directory.Exists(directory)) return;
        var key = Path.GetFullPath(directory) + (recursive ? "|tree" : "|one");
        watchers.GetOrAdd(key, _ => {
            var watcher = new FileSystemWatcher(directory) { IncludeSubdirectories = recursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                InternalBufferSize = 65536 };
            watcher.Changed += (_, e) => Invalidate(e.FullPath);
            watcher.Created += (_, e) => Invalidate(e.FullPath);
            watcher.Deleted += (_, e) => Invalidate(e.FullPath);
            watcher.Renamed += (_, e) => { Invalidate(e.OldFullPath); Invalidate(e.FullPath); };
            watcher.Error += (_, _) => cache.Clear(); watcher.EnableRaisingEvents = true; return watcher;
        });
    }
    public static void Invalidate(string path)
    {
        path = Path.GetFullPath(path); cache.TryRemove(path, out _);
        generations.AddOrUpdate(path, 1, (_, n) => n + 1);
    }
    public static async Task<(long Size, string Hash)> ReadAsync(string path, CancellationToken token, bool force = false)
    {
        path = Path.GetFullPath(path);
        // 跟随文件符号链接取得实际目标，使元数据和读取流描述同一个文件。
        var info = new FileInfo(path);
        if (info.LinkTarget != null && info.ResolveLinkTarget(true) is FileInfo target) info = target;
        info.Refresh();
        var generation = generations.GetValueOrDefault(path);
        // 命中还依赖大小、时间戳和通知世代；强制检查绕过缓存，避免启动前沿用失效证据。
        if (!force && cache.TryGetValue(path, out var saved) && saved.Size == info.Length &&
            saved.Written == info.LastWriteTimeUtc.Ticks && saved.Created == info.CreationTimeUtc.Ticks && saved.Generation == generation)
            return (saved.Size, saved.Digest);
        var written = info.LastWriteTimeUtc.Ticks; var created = info.CreationTimeUtc.Ticks;
        await using var input = File.OpenRead(path);
        var size = input.Length;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
        Interlocked.Increment(ref reads);
        info.Refresh();
        if (info.Length != size || info.LastWriteTimeUtc.Ticks != written || info.CreationTimeUtc.Ticks != created || generation != generations.GetValueOrDefault(path))
            throw new IOException("文件在校验时发生变化，请重试。");
        if (cache.Count > 32768) cache.Clear();
        cache[path] = new(size, written, created, generation, hash);
        return (size, hash);
    }
}
