// SPDX-License-Identifier: MIT
using System.Net;
using System.Text.Json;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;

namespace XG.Core;

public sealed partial class GameService
{
    private readonly SemaphoreSlim adoptionLock = new(1);
    public async Task<string> InspectInstallationAsync(Instance instance, CancellationToken token = default, bool force = false)
    {
        var state = await InstallationReceipt.InspectAsync(store, instance, token, force);
        if (state != "未安装" || File.Exists(Path.Combine(store.DirectoryFor(instance), ".xg-installation.json"))) return state;
        await adoptionLock.WaitAsync(token);
        try {
            if (await TryAdoptExistingAsync(instance, token)) return await InstallationReceipt.InspectAsync(store, instance, token, force);
            return "未安装";
        }
        finally { adoptionLock.Release(); }
    }

    // No downloads and no mutations of old directories: only a new readiness receipt after a successful local audit.
    private async Task<bool> TryAdoptExistingAsync(Instance instance, CancellationToken token)
    {
        try {
            var directory = store.EnsureLocalDirectory(instance);
            var paths = new MinecraftPath(directory);
            if (store.SharedMinecraft is { } shared) {
                paths.Library = Path.Combine(shared, "libraries"); paths.Assets = Path.Combine(shared, "assets"); paths.Runtime = store.ManagedJavaRoot;
                if (instance.Loader == "原版") paths.Versions = Path.Combine(shared, "versions");
            }
            if (!Directory.Exists(paths.Versions)) return false;
            string? profile = null;
            var metadataFiles = Directory.EnumerateFiles(paths.Versions, "*.json", SearchOption.AllDirectories)
                .Where(x => Path.GetFileNameWithoutExtension(x) == Path.GetFileName(Path.GetDirectoryName(x))).ToArray();
            foreach (var file in metadataFiles) {
                token.ThrowIfCancellationRequested();
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file, token));
                var value = doc.RootElement;
                if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("id", out var id)) continue;
                if (instance.Loader == "原版" && id.GetString() == instance.Version) { profile = instance.Version; break; }
                if (!value.TryGetProperty("inheritsFrom", out var parent) || parent.GetString() != instance.Version || !value.TryGetProperty("libraries", out var libraries)) continue;
                var prefix = instance.Loader switch { "NeoForge" => "net.neoforged:neoforge:", "Forge" => "net.minecraftforge:forge:", "Fabric" => "net.fabricmc:fabric-loader:", "Quilt" => "org.quiltmc:quilt-loader:", _ => "invalid:" };
                if (libraries.EnumerateArray().Any(x => x.TryGetProperty("name", out var name) && name.GetString() is { } dependency &&
                    dependency.StartsWith(prefix, StringComparison.Ordinal) && (instance.LoaderVersion == null || dependency.Split(':').ElementAtOrDefault(2) == instance.LoaderVersion || dependency.Split(':').ElementAtOrDefault(2) == instance.Version + "-" + instance.LoaderVersion)))
                { profile = id.GetString(); break; }
            }
            if (profile == null) return false;
            using var offline = new HttpClient(new RejectNetwork());
            var parameters = MinecraftLauncherParameters.CreateDefault(paths, offline);
            parameters.VersionLoader = new CmlLib.Core.VersionLoader.LocalJsonVersionLoader(paths);
            var extractors = CmlLib.Core.FileExtractors.DefaultFileExtractors.CreateDefault(offline, parameters.RulesEvaluator!, parameters.JavaPathResolver!);
            extractors.Java = null;
            parameters.FileExtractors = extractors.ToExtractorCollection();
            var launcher = new MinecraftLauncher(parameters);
            var metadata = await launcher.GetVersionAsync(profile, token);
            var major = int.TryParse(metadata.JavaVersion?.MajorVersion, out var parsed) ? parsed : 0;
            var java = await JavaDiscovery.FindAsync(store.ResolveJavaPath(instance.Java), paths.Runtime, major);
            if (java == null || string.IsNullOrWhiteSpace(metadata.MainClass)) return false;
            var files = (await launcher.ExtractFiles(metadata, token)).Where(x => x.Path != null && !IsIn(paths.Runtime, x.Path)).ToArray();
            foreach (var file in files) {
                if (!File.Exists(file.Path)) return false;
                if (file.Hash is { Length: 40 or 64 or 128 } hash && file.Size > 0 &&
                    !await VerifiedTransfer.MatchesAsync(file.Path!, file.Size, hash, token)) return false;
            }
            using var process = await launcher.BuildProcessAsync(profile, new MLaunchOption { Session = MSession.CreateOfflineSession("XGCheck"), JavaPath = java }, token);
            var required = files.Select(x => x.Path!).Concat(new[] { java }).Concat(metadataFiles.Where(path =>
                Path.GetFileNameWithoutExtension(path) == profile || Path.GetFileNameWithoutExtension(path) == instance.Version)).ToArray();
            await InstallationReceipt.WriteAsync(store, instance, required, token, profile, java, major);
            return true;
        }
        catch (Exception error) when (error is IOException or HttpRequestException or JsonException or InvalidOperationException or ArgumentException or UnauthorizedAccessException) {
            System.Diagnostics.Trace.TraceInformation("Local installation audit: " + error.GetType().Name); return false;
        }
    }
    private sealed class RejectNetwork : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("本地安装检查不会下载资源。", null, HttpStatusCode.ServiceUnavailable));
    }
}
