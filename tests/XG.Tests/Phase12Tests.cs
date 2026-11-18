// SPDX-License-Identifier: MIT
using System.Text.Json;
using XG.Core;

internal static class Phase12Tests
{
    internal static async Task RunAsync(string root)
    {
        var path = Path.Combine(root, "phase12"); Directory.CreateDirectory(path);
        var store = new InstanceStore(path);
        var a = new Instance(Guid.NewGuid(), "个人", "1.21.1", "NeoForge", LoaderVersion: "21.1.267");
        Directory.CreateDirectory(Path.Combine(store.InstanceRoot, "1.21.1-neoforge"));
        store.Save(a);
        var folder = store.DirectoryFor(a);
        Check(Path.GetFileName(folder) == "1.21.1-NeoForge-2", "case insensitive collision");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "sentinel"), "keep");
        store.Save(a with { Name = "renamed", LoaderVersion = "21.1.268" });
        Check(store.DirectoryFor(a) == folder, "rename changed path");
        var copy = await store.CopyAsync(a);
        Check(Path.GetFileName(store.DirectoryFor(copy)) == "1.21.1-NeoForge-3" && File.ReadAllText(Path.Combine(folder, "sentinel")) == "keep", "copy collision overwrote data");
        Check(new InstanceStore(path).DirectoryFor(a) == folder, "mapping restart");
        var legacy = new Instance(Guid.NewGuid(), "legacy", "1.20.1");
        var oldRoot = Path.Combine(path, "legacy"); Directory.CreateDirectory(oldRoot);
        InstanceStore.WriteJson(Path.Combine(oldRoot, "instances.json"), new[] { legacy });
        Check(Path.GetFileName(new InstanceStore(oldRoot).DirectoryFor(legacy)) == legacy.Id.ToString("N"), "legacy path migrated");
        Check(await InstallationReceipt.InspectAsync(store, a) == "未安装", "new instance falsely launchable");
        var evidence = Path.Combine(folder, "client.jar"); await File.WriteAllTextAsync(evidence, "valid");
        await InstallationReceipt.WriteAsync(store, a, new[] { evidence }, default, a.Version, evidence);
        Check(await InstallationReceipt.InspectAsync(store, a) == "已安装", "verified files not ready");
        await File.WriteAllTextAsync(evidence, "other"); // same length
        Check(await InstallationReceipt.InspectAsync(store, a) == "文件损坏", "same-size corruption missed");
        File.Delete(evidence);
        Check(await InstallationReceipt.InspectAsync(store, a) == "文件损坏", "missing file missed");
        if (!OperatingSystem.IsWindows())
        {
            await File.WriteAllTextAsync(evidence, "symlink target fixture");
            var link = Path.Combine(folder, "java-link"); File.CreateSymbolicLink(link, evidence);
            await InstallationReceipt.WriteAsync(store, a, new[] { link }, default, a.Version, link);
            Check(await InstallationReceipt.InspectAsync(store, a) == "已安装", "Java symlink target mismatches length");
        }
        await File.WriteAllTextAsync(Path.Combine(folder, ".xg-installation.json"), "{\"Files\":null}");
        Check(await InstallationReceipt.InspectAsync(store, a) == "文件损坏", "invalid installation metadata escaped audit");
        var portableRoot = Path.Combine(path, "receipt-portable");
        var portableStore = new InstanceStore(Path.Combine(portableRoot, "data"), Path.Combine(portableRoot, "minecraft"));
        portableStore.Save(a);
        var resource = Path.Combine(portableStore.SharedMinecraft!, "client.jar");
        await File.WriteAllTextAsync(resource, "game resource");
        var outsideJava = Path.Combine(path, "external-java"); await File.WriteAllTextAsync(outsideJava, "external executable fixture");
        Directory.CreateDirectory(portableStore.DirectoryFor(a));
        await InstallationReceipt.WriteAsync(portableStore, a, new[] { resource, outsideJava }, default, a.Version, outsideJava);
        var relocated = Path.Combine(path, "relocated-receipt"); Directory.Move(portableRoot, relocated);
        var reopened = new InstanceStore(Path.Combine(relocated, "data"), Path.Combine(relocated, "minecraft"));
        Check(await InstallationReceipt.InspectAsync(reopened, a) == "已安装", "relocation broke internal files or external Java evidence");
        Console.WriteLine("PASS portable installation evidence relocation, external Java absolute path and symlink target size");
        Console.WriteLine("PASS phase12 naming, Windows case collisions, immutable folders, copy, legacy restart, installation evidence, same-size corruption and missing file");

        var jobs = new ResourceOperations(path);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovery = new ResourceRecovery("game", a.Id, InstallationReceipt.Configuration(a));
        var work = jobs.RunAsync("personal", "实例安装", "official", "personal", async mark => {
            jobs.PlanStages(new[] { ("Assets", 200L, 2, false), ("Libraries", 300L, 3, false) }, "personal");
            await jobs.RunAsync("assets", "Assets", "official", "personal", async child => {
                child("下载中"); jobs.Report("Assets", "personal", 100, 200, "object", 1, 2, 50, 100);
                entered.SetResult(); await release.Task; await jobs.Current!.WaitIfPausedAsync();
                jobs.Report("Assets", "personal", 200, 200, "object", 2, 2, 100, 100); return true;
            });
            await jobs.RunAsync("libs", "Libraries", "official", "personal", child => {
                jobs.Report("Libraries", "personal", 300, 300, "lib.jar", 3, 3, 100, 100); return Task.FromResult(true);
            });
            return true;
        }, recovery);
        await entered.Task;
        var main = jobs.MainJobs.Single();
        Check(jobs.UnfinishedCount == 1 && jobs.Items.Count == 3 && main.Bytes == 100 && main.TotalBytes == 500, "root count or byte aggregation");
        Check(new ResourceOperations(path).MainJobs.Single().State == "等待恢复", "restart recovery missing");
        jobs.Pause(main.Id); release.SetResult();
        await Task.Delay(80);
        Check(!work.IsCompleted && jobs.Items.Single(x => x.Kind == "Libraries").State == "等待中", "pause started next stage");
        jobs.Resume(main.Id); Check(await work, "resume failed");
        Check(jobs.UnfinishedCount == 0 && jobs.MainJobs.Single().Bytes == 500, "completion count or bytes");
        jobs.ClearCompleted(); Check(jobs.Items.Count == 0, "children orphaned after clear");
        var fail = true;
        async Task<bool> Stages(Action<string> mark) {
            await jobs.RunAsync("loader", "模组加载器", "official", "personal", child => {
                if (fail) throw new IOException("fixture failure"); return Task.FromResult(true);
            }); return true;
        }
        try { await jobs.RunAsync("fail", "实例安装", "official", "personal", Stages, recovery); } catch (IOException) { }
        var failed = jobs.MainJobs.Single(); var childId = jobs.Items.Single(x => x.ParentId == failed.Id).Id;
        Check(jobs.CanRecover(failed.Id) && jobs.UnfinishedCount == 1, "failed main not counted");
        fail = false; await jobs.RetryAsync(failed.Id, Stages);
        Check(jobs.Items.Single(x => x.ParentId == failed.Id).Id == childId && jobs.UnfinishedCount == 0, "retry duplicated child");
        jobs.ClearCompleted();
        var installerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var installerRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downstream = false;
        var atomic = jobs.RunAsync("atomic", "实例安装", "fixture", "personal", async mark => {
            await jobs.RunAsync("loader", "模组加载器", "fixture", "personal", async child => {
                child("正在执行加载器安装程序…"); installerEntered.SetResult();
                await installerRelease.Task;
                Check(!jobs.Current!.Token.IsCancellationRequested, "installer was force cancelled");
                return true;
            });
            await jobs.RunAsync("next", "next", "fixture", "personal", child => { downstream = true; return Task.FromResult(true); });
            return true;
        }, recovery);
        await installerEntered.Task;
        var atomicId = jobs.MainJobs.Single().Id;
        jobs.Pause(atomicId);
        Check(jobs.MainJobs.Single().State.Contains("安装步骤结束后暂停"), "deferred pause not explicit");
        jobs.Cancel(atomicId);
        Check(jobs.MainJobs.Single().State.Contains("安装步骤结束后取消"), "deferred cancel not explicit");
        installerRelease.SetResult();
        try { await atomic; throw new Exception("deferred cancel was ignored"); } catch (OperationCanceledException) { }
        Check(!downstream && jobs.MainJobs.Single().State == "已取消" && jobs.UnfinishedCount == 0, "cancel started downstream stage");
        Console.WriteLine("PASS safe installer deferred pause and cancellation, explicit pending state and no downstream stage");
        Console.WriteLine("PASS phase12 one main task, staged byte aggregation, child reuse, pause gates next stage, restart, failed badge count and history cleanup");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
