// SPDX-License-Identifier: MIT
using System.IO.Compression;
using XG.Core;

internal static class SynchronizationTests
{
    internal static void Run(string root)
    {
        var store = new InstanceStore(Path.Combine(root, "synchronization"));
        var source = new Instance(Guid.NewGuid(), "Source", "1.21.1", "Fabric", Pack: true, LoaderVersion: "0.16.4");
        var first = source with { Id = Guid.NewGuid(), Name = "A", Pack = false };
        var second = first with { Id = Guid.NewGuid(), Name = "B" };
        var rollback = first with { Id = Guid.NewGuid(), Name = "Rollback" };
        var options = new SyncSelection(true, true, false, false, false);
        Check(!InstanceSynchronization.Compatible(source, first with { LoaderVersion = "0.16.5" }));
        Check(!InstanceSynchronization.Compatible(source, first with { Loader = "NeoForge" }));
        Check(!InstanceSynchronization.Compatible(source, first with { Version = "1.20.1" }));
        Put(source, "mods/a.jar", "mod a");
        Put(source, "config/settings.toml", "config");
        Put(source, "saves/world/level.dat", "never copy this");
        Put(second, "config/settings.toml", "conflicting config");
        ExpectConflict(() => InstanceSynchronization.Run(store, source, [first, second], options, new InlineProgress(_ => { })));
        Check(!File.Exists(At(first, "mods/a.jar")));
        Check(File.ReadAllText(At(second, "config/settings.toml")) == "conflicting config");
        File.Delete(At(second, "config/settings.toml"));
        Put(second, "config/existing.toml", "old context");
        var final = -1;
        var result = InstanceSynchronization.Run(store, source, [first, second], options, new InlineProgress(x => final = x.Percent));
        Check(result.FilesAdded == 4 && final == 100);
        Check(File.Exists(At(first, "mods/a.jar")) && File.Exists(At(second, "config/settings.toml")));
        Check(!Directory.Exists(At(first, "saves")));
        using (var backup = ZipFile.OpenRead(Path.Combine(result.BackupDirectory, second.Id.ToString("N") + ".zip")))
            Check(backup.GetEntry("config/existing.toml") != null);

        ExpectConflict(() => InstanceSynchronization.Run(store, source, [rollback], options,
            new InlineProgress(progress =>
            {
                if (progress.Percent == 90) Put(rollback, "config/settings.toml", "external file");
            })));
        Check(!File.Exists(At(rollback, "mods/a.jar")));
        Check(File.ReadAllText(At(rollback, "config/settings.toml")) == "external file");
        Console.WriteLine("PASS synchronization compatibility, preflight conflict, multi-instance copy, backup, default save exclusion and failed commit rollback");

        string At(Instance instance, string relative) => Path.Combine(store.DirectoryFor(instance), relative);
        void Put(Instance instance, string relative, string value)
        {
            var path = At(instance, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value);
        }
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Synchronization fixture assertion failed.");
    }

    private static void ExpectConflict(Action run)
    {
        try { run(); }
        catch (IOException) { return; }
        throw new InvalidOperationException("Expected synchronization conflict.");
    }

    private sealed class InlineProgress(Action<SyncProgress> update) : IProgress<SyncProgress>
    {
        public void Report(SyncProgress value) => update(value);
    }
}
