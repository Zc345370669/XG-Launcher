// SPDX-License-Identifier: MIT
using System.Text.Json;
using XG.Core;

internal static class FlatVersionTests
{
    internal static async Task RunAsync(string root)
    {
        var workspace = Path.Combine(root, "flat-version-fixture");
        var data = Path.Combine(workspace, "data"); var minecraft = Path.Combine(workspace, "minecraft");
        var store = new InstanceStore(data, minecraft);
        var instance = new Instance(Guid.NewGuid(), "Flat fixture", "1.21.1", "NeoForge"); store.Save(instance);
        var directory = store.DirectoryFor(instance);
        var legacy = Path.Combine(directory, "versions", "1.21.1"); Directory.CreateDirectory(legacy);
        var jar = Path.Combine(legacy, "1.21.1.jar"); await File.WriteAllTextAsync(jar, "fixture-client");
        await File.WriteAllTextAsync(Path.Combine(directory, "1.21.1.jar"), "fixture-client");
        var metadata = Path.Combine(legacy, "1.21.1.json");
        InstanceStore.WriteJson(metadata, new { id = "1.21.1", type = "release", mainClass = "fixture.Main", libraries = Array.Empty<object>(),
            time = "2024-08-08T00:00:00Z", releaseTime = "2024-08-08T00:00:00Z", arguments = new { game = Array.Empty<string>(), jvm = Array.Empty<string>() } });
        var profileDir = Path.Combine(directory, "versions", "fixture-neoforge"); Directory.CreateDirectory(profileDir);
        var profile = Path.Combine(profileDir, "fixture-neoforge.json");
        InstanceStore.WriteJson(profile, new { id = "fixture-neoforge", inheritsFrom = "1.21.1", type = "release", mainClass = "fixture.LoaderMain", libraries = Array.Empty<object>(),
            time = "2024-08-08T00:00:00Z", releaseTime = "2024-08-08T00:00:00Z", arguments = new { game = Array.Empty<string>(), jvm = Array.Empty<string>() } });
        var java = Path.Combine(legacy, "java", "bin", "java.exe"); Directory.CreateDirectory(Path.GetDirectoryName(java)!); await File.WriteAllTextAsync(java, "fixture-java-not-executed");
        instance = instance with { Java = store.StoreJavaPath(java) }; store.Save(instance);
        store.BindJava(instance.Id, java);
        var natives = Path.Combine(profileDir, "natives"); Directory.CreateDirectory(natives); await File.WriteAllTextAsync(Path.Combine(natives, "fixture.dll"), "native-fixture");
        Directory.CreateDirectory(Path.Combine(directory, "saves")); await File.WriteAllTextAsync(Path.Combine(directory, "saves", "sentinel"), "world");
        await InstallationReceipt.WriteAsync(store, instance, [jar, metadata, profile, java], default, "fixture-neoforge", java);
        InstanceStore.WriteJson(Path.Combine(data, "downloads.json"), new[] { new { Target = Path.GetRelativePath(workspace, jar).Replace('\\', '/'), InstanceConfiguration = InstallationReceipt.Configuration(instance) } });

        store = new InstanceStore(data, minecraft); instance = store.Entries.Single();
        Check(!Directory.Exists(Path.Combine(directory, "versions")), "nested versions retained");
        Check(File.Exists(Path.Combine(directory, "1.21.1.json")) && File.Exists(Path.Combine(directory, "fixture-neoforge.json")), "profiles not flattened");
        Check(File.ReadAllText(Path.Combine(directory, "saves", "sentinel")) == "world", "save changed");
        using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, ".xg-directory-layout-report.json"))))
            Check(doc.RootElement.GetProperty("IdenticalDuplicatesRemoved").GetInt32() == 1, "same-content JAR not deduplicated");
        Check(await InstallationReceipt.InspectAsync(store, instance, force: true) == "已安装", "receipt not rebased");
        Check(store.ResolveJavaPath(instance.Java) == Path.Combine(directory, "java", "bin", "java.exe") && store.JavaBinding(instance.Id) == Path.Combine(directory, "java", "bin", "java.exe"), "Java references not rebased");
        var queue = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "downloads.json")));
        using (queue) Check(!queue.RootElement[0].GetProperty("Target").GetString()!.Contains("/versions/1.21.1/"), "queue retains inner versions");

        var paths = new InstanceMinecraftPath(store, instance); paths.CreateDirs();
        Check(!Directory.Exists(Path.Combine(directory, "versions")), "CreateDirs recreated nested versions");
        Check(paths.GetVersionJsonPath("fixture-neoforge") == Path.Combine(directory, "fixture-neoforge.json") &&
            paths.GetVersionJarPath("1.21.1") == Path.Combine(directory, "1.21.1.jar") &&
            paths.GetNativePath("fixture-neoforge") == Path.Combine(directory, "natives", "fixture-neoforge"), "flat path routing wrong");
        Check(paths.IsVersionResource(paths.GetVersionJarPath("1.21.1")) && !paths.IsVersionResource(Path.Combine(paths.Assets, "objects", "fixture")) &&
            !paths.IsVersionResource(Path.Combine(paths.Library, "fixture.jar")), "flat base path misclassified shared resources as game body");
        using var http = new HttpClient(new Reject()); var game = new GameService(http, store);
        using var process = await game.BuildInstalledAsync(instance, "Fixture", () => Task.FromResult<CmlLib.Core.Auth.MSession?>(null));
        Check(process.StartInfo.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar) == directory && process.StartInfo.CreateNoWindow, "installed flat launch process config wrong");
        Check(!Directory.Exists(Path.Combine(directory, "versions")), "launch recreated inner versions");
        Check(new InstanceStore(data, minecraft).DirectoryFor(instance) == directory, "restart not idempotent");

        var other = new Instance(Guid.NewGuid(), "Shared client", "1.21.1", "Fabric"); store.Save(other);
        var shared = Path.Combine(minecraft, "versions", "1.21.1"); Directory.CreateDirectory(shared);
        await File.WriteAllTextAsync(Path.Combine(shared, "1.21.1.jar"), "shared-client");
        var sharedPaths = new InstanceMinecraftPath(store, other);
        Check(sharedPaths.GetVersionJarPath("1.21.1") == Path.Combine(shared, "1.21.1.jar") && !File.Exists(Path.Combine(store.DirectoryFor(other), "1.21.1.jar")), "shared client unnecessarily copied");

        foreach (var committed in new[] { false, true })
        {
            var interrupted = new Instance(Guid.NewGuid(), "Interrupted " + committed, "1.21.1"); store.Save(interrupted);
            var folder = store.DirectoryFor(interrupted); var nested = Path.Combine(folder, "versions", "1.21.1"); Directory.CreateDirectory(nested);
            var source = Path.Combine(nested, "1.21.1.jar"); var target = Path.Combine(folder, "1.21.1.jar");
            await File.WriteAllTextAsync(source, "recover");
            if (committed) await File.WriteAllTextAsync(target, "recover"); else File.Move(source, target);
            InstanceStore.WriteJson(Path.Combine(folder, ".xg-flat-versions-migration.json"), new {
                Entries = new[] { new { Source = source, Destination = target, Duplicate = committed } },
                Backups = Array.Empty<object>(), Committed = committed
            });
            store = new InstanceStore(data, minecraft);
            Check(File.ReadAllText(target) == "recover" && !Directory.Exists(Path.Combine(folder, "versions")) &&
                !File.Exists(Path.Combine(folder, ".xg-flat-versions-migration.json")), "interrupted transaction recovery failed");
        }
        var empty = new Instance(Guid.NewGuid(), "Old empty versions", "1.21.1"); store.Save(empty);
        Directory.CreateDirectory(Path.Combine(store.DirectoryFor(empty), "versions"));
        store = new InstanceStore(data, minecraft);
        Check(!Directory.Exists(Path.Combine(store.DirectoryFor(empty), "versions")) &&
            await InstallationReceipt.InspectAsync(store, empty) == "未安装", "empty directory treated as installed or retained");

        var customData = Path.Combine(root, "separate-config", "data");
        var customMinecraft = Path.Combine(root, "separate-games", "minecraft");
        var custom = new InstanceStore(customData, customMinecraft);
        var customInstance = new Instance(Guid.NewGuid(), "Separate roots", "1.21.1"); custom.Save(customInstance);
        var customDirectory = custom.DirectoryFor(customInstance);
        var customOld = Path.Combine(customDirectory, "versions", "1.21.1"); Directory.CreateDirectory(customOld);
        var customJava = Path.Combine(customOld, "java.exe"); await File.WriteAllTextAsync(customJava, "custom-java-fixture");
        customInstance = customInstance with { Java = custom.StoreJavaPath(customJava) }; custom.Save(customInstance);
        await InstallationReceipt.WriteAsync(custom, customInstance, [customJava], default, "1.21.1", customJava);
        custom = new InstanceStore(customData, customMinecraft); customInstance = custom.Entries.Single();
        Check(custom.ResolveJavaPath(customInstance.Java) == Path.Combine(customDirectory, "java.exe") &&
            await InstallationReceipt.InspectAsync(custom, customInstance, force: true) == "已安装", "separate data/game roots broke receipt or portable Java rebasing");

        var conflict = new Instance(Guid.NewGuid(), "Conflict", "1.21.1"); store.Save(conflict);
        var conflictRoot = store.DirectoryFor(conflict); var old = Path.Combine(conflictRoot, "versions", "1.21.1"); Directory.CreateDirectory(old);
        await File.WriteAllTextAsync(Path.Combine(old, "1.21.1.jar"), "old"); await File.WriteAllTextAsync(Path.Combine(conflictRoot, "1.21.1.jar"), "new");
        try { _ = new InstanceStore(data, minecraft); throw new Exception("different-content conflict merged"); } catch (IOException) { }
        Check(File.ReadAllText(Path.Combine(old, "1.21.1.jar")) == "old" && File.ReadAllText(Path.Combine(conflictRoot, "1.21.1.jar")) == "new", "conflict data overwritten");
        Console.WriteLine("PASS flat version profiles/JAR/natives, identical duplicate removal, conflicting file preservation, user saves, Java/receipt/queue rebasing, actual installed process construction without HTTP, no recreated inner versions, idempotent restart and shared-client reuse (controlled fixtures)");
    }
    private sealed class Reject : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new Exception("Unexpected network in installed flat-profile fixture"); }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
