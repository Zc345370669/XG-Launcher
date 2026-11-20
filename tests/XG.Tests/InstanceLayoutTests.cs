// SPDX-License-Identifier: MIT
using System.Text.Json;
using XG.Core;

internal static class InstanceLayoutTests
{
    internal static async Task RunAsync(string root)
    {
        var workspace = Path.Combine(root, "version-layout");
        var data = Path.Combine(workspace, "data"); var minecraft = Path.Combine(workspace, "minecraft");
        Directory.CreateDirectory(data);
        var instance = new Instance(Guid.NewGuid(), "My NeoForge", "1.21.1", "NeoForge", Java: "portable:minecraft/instances/1.21.1-NeoForge/java/bin/java.exe");
        var name = "1.21.1-NeoForge";
        var old = Path.Combine(minecraft, "instances", name);
        var next = Path.Combine(minecraft, "versions", name);
        var sentinel = new[] { "mods/sample.jar", "shaderpacks/sample.zip", "resourcepacks/sample.zip", "saves/world/level.dat", "config/options.toml", "logs/latest.log", "xg-launch.log", "xg-loader.log", "java/bin/java.exe" };
        foreach (var relative in sentinel)
        {
            var file = Path.Combine(old, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "fixture:" + relative);
        }
        InstanceStore.WriteJson(Path.Combine(data, "instances.json"), new[] { instance });
        InstanceStore.WriteJson(Path.Combine(data, "instance-directories.json"), new Dictionary<Guid, string> { [instance.Id] = name });
        // 构造真实旧版安装凭据，不通过新 InstanceStore 提前触发迁移。
        var receipt = new InstallationReceipt.Receipt(InstallationReceipt.Configuration(instance),
            [new(Path.GetRelativePath(workspace, Path.Combine(old, "mods/sample.jar")),
                new FileInfo(Path.Combine(old, "mods/sample.jar")).Length,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(old, "mods/sample.jar")))))],
            "fixture-neoforge", Path.GetRelativePath(workspace, Path.Combine(old, "mods/sample.jar")));
        InstanceStore.WriteJson(Path.Combine(old, ".xg-installation.json"), receipt);
        var job = new Download(Guid.NewGuid(), new ModFile("sample.jar", new Uri("https://cdn.modrinth.com/sample.jar"), 1, new string('0', 128)),
            "minecraft/instances/" + name + "/mods/sample.jar", InstanceConfiguration: InstallationReceipt.Configuration(instance));
        InstanceStore.WriteJson(Path.Combine(data, "downloads.json"), new[] { job });
        InstanceStore.WriteJson(Path.Combine(data, "java-bindings.json"), new Dictionary<Guid, string> { [instance.Id] = Path.Combine(old, "java/bin/java.exe") });
        InstanceStore.WriteJson(Path.Combine(data, "java-catalog.json"), new[] { Path.Combine(old, "java/bin/java.exe") });
        var sharedVersion = Path.Combine(minecraft, "versions", "1.21.1"); Directory.CreateDirectory(sharedVersion);
        await File.WriteAllTextAsync(Path.Combine(sharedVersion, "1.21.1.json"), "shared metadata");
        var store = new InstanceStore(data, minecraft);
        instance = store.Entries.Single();
        Check(store.ResolveJavaPath(instance.Java) == Path.Combine(next, "java/bin/java.exe") &&
            store.JavaBinding(instance.Id) == Path.Combine(next, "java/bin/java.exe") &&
            store.CachedJavaPaths().Single() == Path.Combine(next, "java/bin/java.exe"), "Java paths not rebased");
        Check(store.DirectoryFor(instance) == next && store.InstanceRoot == Path.Combine(minecraft, "versions"), "new instance root incorrect");
        Check(!Directory.Exists(Path.Combine(minecraft, "instances")), "empty old root retained");
        foreach (var relative in sentinel) Check(await File.ReadAllTextAsync(Path.Combine(next, relative)) == "fixture:" + relative, "user/game/log data not preserved");
        Check(await InstallationReceipt.InspectAsync(store, instance, force: true) == "已安装", "installation evidence invalid after migration");
        Check(JsonSerializer.Deserialize<Download[]>(await File.ReadAllTextAsync(Path.Combine(data, "downloads.json")))![0].Target == "minecraft/versions/" + name + "/mods/sample.jar", "queue path not rebased");
        Check(JsonSerializer.Deserialize<Download[]>(await File.ReadAllTextAsync(Path.Combine(data, "downloads.json")))![0].InstanceConfiguration == InstallationReceipt.Configuration(instance), "queue configuration does not match migrated instance");
        Check(await File.ReadAllTextAsync(Path.Combine(sharedVersion, "1.21.1.json")) == "shared metadata", "shared vanilla metadata overwritten");
        Check(new InstanceStore(data, minecraft).DirectoryFor(instance) == next, "migration not idempotent");
        var clone = await store.CopyAsync(instance); Check(store.DirectoryFor(clone).StartsWith(Path.Combine(minecraft, "versions") + Path.DirectorySeparatorChar), "copy uses old root");
        Check(Path.GetFileName(store.DirectoryFor(clone)) == "1.21.1-NeoForge-2", "version/loader collision suffix lost");
        store.Remove(clone, true); Check(File.Exists(Path.Combine(next, "saves/world/level.dat")), "copy/delete affected source save");

        var conflictData = Path.Combine(root, "layout-conflict"); Directory.CreateDirectory(conflictData);
        InstanceStore.WriteJson(Path.Combine(conflictData, "instances.json"), new[] { instance });
        InstanceStore.WriteJson(Path.Combine(conflictData, "instance-directories.json"), new Dictionary<Guid, string> { [instance.Id] = name });
        var conflictOld = Path.Combine(conflictData, "instances", name); var conflictNew = Path.Combine(conflictData, "versions", name);
        Directory.CreateDirectory(conflictOld); Directory.CreateDirectory(conflictNew);
        await File.WriteAllTextAsync(Path.Combine(conflictOld, "sentinel"), "old"); await File.WriteAllTextAsync(Path.Combine(conflictNew, "sentinel"), "new");
        try { _ = new InstanceStore(conflictData); throw new Exception("collision accepted"); }
        catch (IOException) { }
        Check(File.ReadAllText(Path.Combine(conflictOld, "sentinel")) == "old" && File.ReadAllText(Path.Combine(conflictNew, "sentinel")) == "new", "collision overwrote data");
        // 模拟目录已移动、索引尚未提交时退出；下次构造应恢复旧状态后完成迁移。
        Directory.Delete(conflictNew, true);
        var indexPath = Path.Combine(conflictData, "instances.json"); var indexBytes = File.ReadAllBytes(indexPath);
        InstanceStore.WriteJson(Path.Combine(conflictData, "instance-layout-migration.json"), new {
            Moves = new[] { new { Source = conflictOld, Destination = conflictNew } },
            Backups = new[] { new { Path = indexPath, Content = indexBytes } }
        });
        Directory.Move(conflictOld, conflictNew); File.WriteAllText(indexPath, "[]");
        var resumed = new InstanceStore(conflictData);
        Check(resumed.Entries.Single().Id == instance.Id && File.ReadAllText(Path.Combine(resumed.DirectoryFor(instance), "sentinel")) == "old", "interrupted move not recovered");
        Check(!File.Exists(Path.Combine(conflictData, "instance-layout-migration.json")), "successful recovery retained pending journal");
        Console.WriteLine("PASS version-root migration preserves mods/shaders/packs/saves/config/logs, rewrites verified receipts/queue, retains shared vanilla metadata, restarts idempotently, copies/deletes in isolation and refuses collisions");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
