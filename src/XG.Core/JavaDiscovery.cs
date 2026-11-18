// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace XG.Core;

public sealed record JavaInstallation(string Path, int Major, string Architecture, string Version = "", string Vendor = "");

// Native implementation written for this revision. Public entry points remain
// compatible with the launcher; no external discovery library or Python bridge.
public static partial class JavaDiscovery
{
    private sealed record Verified(JavaInstallation Runtime, string Stamp, DateTimeOffset Checked);
    private static readonly ConcurrentDictionary<string, Verified> Catalog = new(JavaLocations.PathComparer);
    private static readonly SemaphoreSlim ProbeSlots = new(4);

    public static bool IsCompatible(JavaInstallation runtime, int major) => major > 0 && runtime.Major == major &&
        JavaProbe.NormalizeArchitecture(runtime.Architecture) == (RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64", Architecture.Arm64 => "arm64", Architecture.X86 => "x86", _ => "unsupported"
        });

    public static void Forget(string path)
    {
        if (JavaLocations.Executable(path) is { } canonical) Catalog.TryRemove(canonical, out _);
    }

    internal static async Task<string?> FindAsync(string? manual, string managed, int major, IEnumerable<string>? extra = null) =>
        major <= 0 && string.IsNullOrWhiteSpace(manual) ? null : (await CompatibleAsync(manual, managed, major, extra))?.Path;

    public static Task<JavaInstallation?> CompatibleAsync(string? manual, string managed, int major,
        IEnumerable<string>? extra = null, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        // Validate an explicit choice before enumerating other sources. Selection
        // never writes instance preferences; invalid choices can still fall back.
        if (!string.IsNullOrWhiteSpace(manual) && await InspectPathAsync(manual, cancellationToken) is { } preferred && Fits(preferred))
            return preferred;
        foreach (var path in extra ?? [])
            if (await InspectPathAsync(path, cancellationToken) is { } saved && Fits(saved)) return saved;
        foreach (var previous in Catalog.Values.OrderByDescending(x => x.Checked))
            if (await InspectPathAsync(previous.Runtime.Path, cancellationToken) is { } cached && Fits(cached)) return cached;
        var found = await DiscoverAsync(managed, extra, cancellationToken);
        return found.FirstOrDefault(Fits);
        bool Fits(JavaInstallation candidate) => major > 0 ? IsCompatible(candidate, major) :
            IsCompatible(candidate, candidate.Major);
    }, cancellationToken);

    public static async Task<(string Path, int Major)?> InspectAsync(string? manual, string managed)
    {
        var runtime = await CompatibleAsync(manual, managed, 0);
        return runtime is null ? null : (runtime.Path, runtime.Major);
    }

    public static Task<JavaInstallation?> InspectPathAsync(string candidate, CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (JavaLocations.Executable(candidate) is not { } path) return null;
            await ProbeSlots.WaitAsync(cancellationToken);
            try
            {
                var stamp = JavaProbe.Fingerprint(path);
                if (stamp is null) { Catalog.TryRemove(path, out _); return null; }
                if (Catalog.TryGetValue(path, out var entry) && entry.Stamp == stamp &&
                    DateTimeOffset.UtcNow - entry.Checked < TimeSpan.FromSeconds(30)) return entry.Runtime;
                var runtime = await JavaProbe.ExecuteAsync(path, TimeSpan.FromSeconds(3), cancellationToken);
                // Do not cache a probe if its executable/runtime changed during validation.
                if (runtime is null || JavaProbe.Fingerprint(path) != stamp) { Catalog.TryRemove(path, out _); return null; }
                Catalog[path] = new(runtime, stamp, DateTimeOffset.UtcNow);
                if (Catalog.Count > 256)
                    foreach (var stale in Catalog.OrderBy(x => x.Value.Checked).Take(Catalog.Count - 256)) Catalog.TryRemove(stale.Key, out _);
                return runtime;
            }
            finally { ProbeSlots.Release(); }
        }, cancellationToken);
}
