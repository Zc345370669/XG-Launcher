// SPDX-License-Identifier: MIT
using XG.Core;
using System.Net;
using System.Security.Cryptography;

internal static class ShutdownTests
{
    internal static async Task RunAsync(string root)
    {
        foreach (var paused in new[] { false, true })
        {
            var folder = Path.Combine(root, "shutdown-resource-" + paused); Directory.CreateDirectory(folder);
            var resources = new ResourceOperations(folder);
            var partial = Path.Combine(folder, "fixture.part");
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var run = resources.RunAsync("fixture", "实例安装", "fixture", "fixture", async _ =>
            {
                await resources.Current!.RunFileAsync(async token =>
                {
                    await File.WriteAllTextAsync(partial, "verified partial fixture", token);
                    started.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                });
                return true;
            }, new ResourceRecovery("game", Guid.NewGuid()));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var id = resources.MainJobs.Single().Id;
            if (paused) resources.Pause(id);
            await resources.SuspendForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            try { await run; throw new Exception("shutdown task unexpectedly completed"); } catch (OperationCanceledException) { }
            Check(File.Exists(partial) && resources.CanRecover(id) && resources.MainJobs.Single().State == "等待恢复", "shutdown lost checkpoint or partial data");
            var restored = new ResourceOperations(folder);
            Check(restored.CanRecover(id), "restart cannot restore suspended resource");
            Check(await restored.RetryAsync(id, _ => Task.FromResult(true)), "restart recovery failed");
        }
        Console.WriteLine("PASS exit suspends running and paused installation tasks, releases controls, preserves partial files and restores after restart");

        var atomicRoot = Path.Combine(root, "shutdown-atomic"); Directory.CreateDirectory(atomicRoot);
        var atomic = new ResourceOperations(atomicRoot);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var install = atomic.RunAsync("fixture", "实例安装", "fixture", "fixture", async _ =>
        {
            await atomic.RunAsync("NeoForge", "模组加载器", "NeoForge", "fixture", async _ =>
            { entered.SetResult(); await release.Task; return true; });
            return true;
        }, new ResourceRecovery("game", Guid.NewGuid()));
        await entered.Task;
        var shutdown = atomic.SuspendForExitAsync();
        Check(!shutdown.IsCompleted, "atomic loader forcibly interrupted");
        release.SetResult();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(3));
        try { await install; throw new Exception("atomic parent should suspend"); } catch (OperationCanceledException) { }
        Check(atomic.MainJobs.Single().State == "等待恢复" && atomic.Items.Single(x => x.ParentId != null).State == "已完成", "atomic result or restore state lost");
        Console.WriteLine("PASS exit waits for the atomic loader step, preserves its real completion and suspends the parent for recovery");

        var modRoot = Path.Combine(root, "shutdown-mods"); Directory.CreateDirectory(modRoot);
        using var handler = new Stalled(); using var http = new HttpClient(handler);
        var downloads = new Downloads(http, modRoot);
        var file = new ModFile("fixture.jar", new Uri("https://cdn.modrinth.com/data/fixture.jar"), 64,
            Convert.ToHexString(SHA512.HashData(new byte[64])));
        var a = downloads.Enqueue(file, Path.Combine(modRoot, "a"));
        var b = downloads.Enqueue(file, Path.Combine(modRoot, "b"));
        var partialPath = a.Target + "." + a.Id.ToString("N") + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        var first = downloads.RunAsync(a.Id, () => { }, (_, _) => { }, () => true);
        var queued = downloads.RunAsync(b.Id, () => { }, (_, _) => { }, () => true);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        File.WriteAllText(partialPath, "partial");
        await downloads.PauseForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Check(!await first && !await queued && File.Exists(partialPath), "mod exit deleted partial or left queue running");
        Check(new Downloads(http, modRoot).Items.All(x => x.State == "已暂停"), "mod queue was not persisted paused");
        Console.WriteLine("PASS exit pauses running and queued Modrinth transfers without cancelling/deleting their resumable data");

        Check(handler.Released, "real shared HTTP worker remained alive after exit");
        var copying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCopy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commit = VerifiedTransfer.CommitOwnedAsync(http, async () => { copying.SetResult(); await finishCopy.Task; });
        await copying.Task;
        var exitWhileCopying = VerifiedTransfer.StopForExitAsync(http);
        Check(!exitWhileCopying.IsCompleted, "exit skipped a local file commit after HTTP completed");
        finishCopy.SetResult(); await exitWhileCopying.WaitAsync(TimeSpan.FromSeconds(3)); await commit;

        using var otherHandler = new Stalled(); using var other = new HttpClient(otherHandler);
        var independent = VerifiedTransfer.StageAsync(other, file.Url, Path.Combine(modRoot, "other.part"), file.Size, file.Sha512,
            (_, _) => { }, _ => { }, default);
        await otherHandler.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await VerifiedTransfer.StopForExitAsync(http);
        Check(!independent.IsCompleted, "exit cancelled a different HTTP session");
        await VerifiedTransfer.StopForExitAsync(other);
        try { await independent; throw new Exception("independent worker not stopped"); } catch (OperationCanceledException) { }
        Console.WriteLine("PASS whole-launcher exit stops actual shared HTTP workers and releases writes, without cancelling another HTTP session");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Stalled : HttpMessageHandler
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Released { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { try { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return new(HttpStatusCode.OK); } finally { Released = true; } }
    }
}
