// SPDX-License-Identifier: MIT
using XG.Core;

namespace XGLauncher;

internal sealed partial class LauncherModel
{
    internal static string GameConfiguration(Instance instance) =>
        System.Text.Json.JsonSerializer.Serialize(new[] { instance.Version, instance.Loader, instance.LoaderVersion, instance.Java });

    private static ResourceRecovery GameRecovery(Instance instance) =>
        new("game", instance.Id, GameConfiguration(instance));

    internal async Task<Instance> QueuePackAsync(string project, ModFile? remote, string? local, Action<string> progress)
    {
        var id = Guid.NewGuid();
        var staged = $"pack-{id:N}.mrpack";
        string? fingerprint = null;
        if (local != null)
        {
            if (new FileInfo(local).Length > 1024L * 1024 * 1024) throw new IOException("整合包文件超过 1 GiB。");
            var target = InstanceStore.SafeFile(DownloadsDirectory, staged);
            try
            {
                await using (var input = File.OpenRead(local))
                await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                    await input.CopyToAsync(output);
                fingerprint = await FileHashAsync(target);
            }
            catch
            {
                if (File.Exists(target)) File.Delete(target);
                throw;
            }
        }
        var recovery = new ResourceRecovery("pack", id, Configuration: fingerprint, Archive: remote, StagedFile: staged);
        return await Resources.RunAsync(remote?.Name ?? Path.GetFileName(local!), "整合包", remote == null ? "本地 Modrinth 清单" : "Modrinth",
            project, mark => PreparePackAsync(recovery, progress, mark), recovery);
    }

    private async Task<Instance> PreparePackAsync(ResourceRecovery recovery, Action<string> progress, Action<string> mark)
    {
        if (recovery.StagedFile != $"pack-{recovery.InstanceId:N}.mrpack")
            throw new InvalidDataException("整合包暂存记录无效。");
        if (Store.Entries.FirstOrDefault(item => item.Id == recovery.InstanceId && item.Pack) is { } installed &&
            Directory.Exists(Store.EnsureLocalDirectory(installed))) return installed;
        var staged = InstanceStore.SafeFile(DownloadsDirectory, recovery.StagedFile);
        var control = Resources.Current!;
        if (recovery.Archive is { } archive)
        {
            await control.RunFileAsync(token => Packs.WriteVerifiedAsync(archive, staged,
                (count, total) => Resources.Report("整合包", "", count, total, archive.Name, 0, 1, count, total),
                mark, token, Path.Combine(DownloadsDirectory, "pack-cache")));
        }
        if (!File.Exists(staged)) throw new FileNotFoundException("找不到暂存的整合包，请重新导入。");
        if (recovery.Archive == null && recovery.Configuration != await FileHashAsync(staged))
            throw new InvalidDataException("本地整合包暂存文件校验失败，请重新导入。");
        var instance = await Packs.ImportAsync(staged, progress, Resources, mark, recovery.InstanceId);
        try { File.Delete(staged); }
        catch (IOException error) { System.Diagnostics.Trace.TraceWarning("could not clear staged pack: " + error.GetType().Name); }
        catch (UnauthorizedAccessException error) { System.Diagnostics.Trace.TraceWarning("could not clear staged pack: " + error.GetType().Name); }
        return instance;
    }

    private static async Task<string> FileHashAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        return Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream));
    }

    internal ResourceOperation? InstallationJob(Instance instance) => Resources.Items.LastOrDefault(item =>
        item.ParentId == null && item.Recovery?.Type == "game" && item.Recovery.InstanceId == instance.Id && item.Recovery.Configuration == GameConfiguration(instance) &&
        item.State is not ("已完成" or "已取消"));

    internal async Task InstallInstanceAsync(Instance instance, Action<string> progress)
    {
        if (InstallationJob(instance) is { } prior)
        {
            if (Resources.CanRecover(prior.Id)) await ResumeResourceAsync(prior, progress);
            return;
        }
        if (!Busy.Add(instance.Id)) return;
        foreach (var stale in Resources.MainJobs.Where(x => x.Recovery?.Type == "game" && x.Recovery.InstanceId == instance.Id &&
            x.Recovery.Configuration != GameConfiguration(instance) && Resources.CanRecover(x.Id)).ToArray()) Resources.Discard(stale.Id);
        try
        {
            using var prepared = await Resources.RunAsync(instance.Name, "实例安装", "Minecraft 官方", instance.Name,
                mark => {
                    Resources.PlanStages(new[] { ("游戏本体", 0L, 0, false), ("Minecraft Assets", 0L, 0, false),
                        ("资源与依赖", 0L, 0, false), ("Java 环境", 0L, 0, false), ("模组加载器", 0L, 0, instance.Loader == "原版") }.Where(stage => instance.Loader != "原版" || stage.Item1 != "模组加载器").ToArray(), instance.Name);
                    return PrepareAssetsAsync(instance, progress, mark, false);
                }, GameRecovery(instance));
        }
        finally { Busy.Remove(instance.Id); }
    }

    internal async Task CheckOfficialJavaAsync(Instance instance, Action<string> progress)
    {
        if (!Busy.Add(instance.Id)) throw new InvalidOperationException("目标实例正在使用。");
        try
        {
            var recovery = GameRecovery(instance) with { Type = "java" };
            using var prepared = await Resources.RunAsync(instance.Version, "Java 安装检查", "Minecraft 官方", instance.Name,
                mark => PrepareAssetsAsync(instance, progress, mark, true), recovery);
        }
        finally { Busy.Remove(instance.Id); }
    }

    private Task<System.Diagnostics.Process> PrepareAssetsAsync(Instance instance, Action<string> progress, Action<string> mark, bool checkJava) =>
        Game.PrepareWithSessionAsync(instance, "Player", () => Task.FromResult<CmlLib.Core.Auth.MSession?>(null),
            progress, Resources, mark, verifyManagedJava: checkJava);

    internal async Task ResumeResourceAsync(ResourceOperation item, Action<string> progress)
    {
        var recovery = item.Recovery ?? throw new InvalidOperationException("此任务没有恢复信息。");
        if (recovery.Type == "pack")
        {
            Selected = await Resources.RetryAsync(item.Id, mark => PreparePackAsync(recovery, progress, mark));
            return;
        }
        if (recovery.Type is not ("game" or "java")) throw new InvalidDataException("无法识别下载任务。");
        var instance = Store.Entries.FirstOrDefault(entry => entry.Id == recovery.InstanceId);
        if (instance == null || GameConfiguration(instance) != recovery.Configuration)
            throw new InvalidOperationException("目标实例已删除或配置已变更，请在实例页面重新开始。");
        if (!Busy.Add(instance.Id)) throw new InvalidOperationException("目标实例正在使用。");
        try
        {
            using var prepared = await Resources.RetryAsync(item.Id, mark =>
                PrepareAssetsAsync(instance, progress, mark, recovery.Type == "java"));
        }
        finally { Busy.Remove(instance.Id); }
    }
}
