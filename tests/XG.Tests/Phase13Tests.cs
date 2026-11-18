// SPDX-License-Identifier: MIT
using System.Security.Cryptography;
using XG.Core;

internal static class Phase13Tests
{
    internal static async Task RunAsync(string root)
    {
        var main = new ResourceOperation(Guid.NewGuid(), "1.21.1", "实例安装", "official", "instance", "下载中");
        const long mib = 1048576;
        ResourceOperation Stage(string kind, long bytes, long total, string state) =>
            new(Guid.NewGuid(), kind, kind, "official", "instance", state, Bytes: bytes, TotalBytes: total, ParentId: main.Id);
        var stages = new[] { Stage("游戏本体", 50 * mib, 50 * mib, "已完成"), Stage("Minecraft Assets", 400 * mib, 800 * mib, "下载中"),
            Stage("资源与依赖", 0, 100 * mib, "等待中"), Stage("Java 环境", 0, 0, "已完成"), Stage("模组加载器", 0, 0, "等待中") };
        var progress = InstallationProgress.Calculate(main, stages);
        Check(progress.Percent > 40 && progress.Percent < 50 && progress.HasUnknownBytes, "real weighted work progress");
        Check(Math.Abs(progress.Stages[0].End - 20) > 10 && progress.Stages[1].End - progress.Stages[1].Start > 70, "stages are hard-coded to 20%");
        var all = stages.Select(x => x with { Bytes = x.TotalBytes, State = "已完成" }).ToArray();
        Check(InstallationProgress.Calculate(main with { State = "校验中" }, all).Percent < 100, "100% before final validation");
        Check(InstallationProgress.Calculate(main with { State = "已完成" }, all).Percent == 100, "completed root not 100%");

        var folder = Path.Combine(root, "phase13"); Directory.CreateDirectory(folder);
        var store = new InstanceStore(folder); var instance = new Instance(Guid.NewGuid(), "cache", "1.21.1"); store.Save(instance);
        Directory.CreateDirectory(store.DirectoryFor(instance)); var payload = Path.Combine(store.DirectoryFor(instance), "client.bin");
        await File.WriteAllTextAsync(payload, "first");
        await InstallationReceipt.WriteAsync(store, instance, new[] { payload }, default, instance.Version, payload);
        Check(await InstallationReceipt.InspectAsync(store, instance) == "已安装", "initial evidence");
        var count = FileVerificationCache.HashReads;
        Check(await InstallationReceipt.InspectAsync(store, instance) == "已安装" && FileVerificationCache.HashReads == count, "unchanged files rehashed");
        var oldTime = File.GetLastWriteTimeUtc(payload); await File.WriteAllTextAsync(payload, "other"); File.SetLastWriteTimeUtc(payload, oldTime);
        await Task.Delay(180);
        Check(await InstallationReceipt.InspectAsync(store, instance, force: true) == "文件损坏", "same-size restored-timestamp corruption reused");
        Check(await InstallationReceipt.InspectAsync(store, instance) == "文件损坏", "watcher failed to invalidate changed content");

        var operations = new ResourceOperations(folder);
        try {
            await operations.RunAsync("incomplete", "实例安装", "fixture", "instance", mark => {
                operations.PlanStages(new[] { ("missing", 10L, 1, false) }, "instance"); return Task.FromResult(true);
            }, new ResourceRecovery("game", instance.Id));
            throw new Exception("unfinished child accepted");
        } catch (InvalidOperationException) { }
        Check(operations.MainJobs.Single().State == "失败" && File.Exists(Path.Combine(folder, "resource-errors.log")), "failure persistence/diagnostic log");
        var reloaded = new ResourceOperations(folder); Check(reloaded.UnfinishedCount == 1 && reloaded.CanRecover(reloaded.MainJobs.Single().Id), "failed main recovery");
        Console.WriteLine("PASS phase13 weighted byte/file work progress, unknown installer work, final-validation 100% gate, cached hashes, change invalidation, failed child and error persistence");

        if (!OperatingSystem.IsWindows()) await LegacyAsync(folder);
    }
    private static async Task LegacyAsync(string folder)
    {
        var oldRoot = Path.Combine(folder, "legacy-game"); Directory.CreateDirectory(oldRoot);
        var java = Path.Combine(folder, "java-fixture");
        await File.WriteAllTextAsync(java, "#!/bin/sh\necho '    os.arch = amd64' >&2\necho 'openjdk version \"21.0.1\"' >&2\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(java, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var instance = new Instance(Guid.NewGuid(), "legacy local fixture", "1.21.1", Java: java);
        InstanceStore.WriteJson(Path.Combine(oldRoot, "instances.json"), new[] { instance });
        var store = new InstanceStore(oldRoot); var gameDir = store.DirectoryFor(instance);
        var version = Path.Combine(gameDir, "versions", "1.21.1"); Directory.CreateDirectory(version);
        var bytes = "local-client-fixture"u8.ToArray(); var hash = Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();
        await File.WriteAllBytesAsync(Path.Combine(version, "1.21.1.jar"), bytes);
        var metadata = new {
            id = "1.21.1", type = "release", mainClass = "fixture.Main", minimumLauncherVersion = 21,
            time = "2024-08-08T00:00:00Z", releaseTime = "2024-08-08T00:00:00Z",
            javaVersion = new { component = "java-runtime-delta", majorVersion = 21 }, libraries = Array.Empty<object>(),
            arguments = new { game = Array.Empty<string>(), jvm = Array.Empty<string>() },
            downloads = new { client = new { url = "https://piston-data.mojang.com/fixture.jar", size = bytes.Length, sha1 = hash } }
        };
        InstanceStore.WriteJson(Path.Combine(version, "1.21.1.json"), metadata);
        Directory.CreateDirectory(Path.Combine(gameDir, "saves"));
        await File.WriteAllTextAsync(Path.Combine(gameDir, "saves", "sentinel"), "preserve");
        using var http = new HttpClient(new Denied()); var game = new GameService(http, store);
        Check(await game.InspectInstallationAsync(instance) == "已安装", "valid legacy instance not adopted locally");
        Check(Path.GetFileName(gameDir) == instance.Id.ToString("N") && File.ReadAllText(Path.Combine(gameDir, "saves", "sentinel")) == "preserve", "legacy directory/data altered");
        Check(File.Exists(Path.Combine(gameDir, ".xg-installation.json")), "legacy evidence not recorded");
        Console.WriteLine("PASS phase13 controlled legacy GUID installation adopted without network, rename, redownload or user-data changes");
    }
    private sealed class Denied : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new Exception("Unexpected network in legacy fixture");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
