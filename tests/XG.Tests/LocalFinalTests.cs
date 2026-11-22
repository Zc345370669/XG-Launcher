// SPDX-License-Identifier: MIT
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using XG.Core;

internal static class LocalFinalTests
{
    internal static async Task RunAsync(string root)
    {
        Check(JavaVersionPolicy.Recommend("1.12.2") == 8 && JavaVersionPolicy.Recommend("1.20.4") == 17 && JavaVersionPolicy.Recommend("1.20.5") == 21 && JavaVersionPolicy.Recommend("snapshot") == null, "Java recommendation guessed incompatible/unknown version");
        var work = Path.Combine(root, "local-final"); Directory.CreateDirectory(work);
        var data = Path.Combine(work, "data"); var game = GameRootPreference.Resolve(work, data);
        Check(game == Path.Combine(work, ".minecraft"), "default root is not EXE sibling .minecraft");
        var store = new InstanceStore(data, game);
        var vanilla = new Instance(Guid.NewGuid(), "Vanilla", "1.21.1");
        var neo = new Instance(Guid.NewGuid(), "NeoForge", "1.21.1", "NeoForge", LoaderVersion: "21.1.1");
        store.Save(vanilla); store.Save(neo);
        Check(Path.GetFileName(store.DirectoryFor(vanilla)) == "1.21.1" && Path.GetFileName(store.DirectoryFor(neo)) == "1.21.1-NeoForge", "new version directory names wrong");
        var paths = new InstanceMinecraftPath(store, neo);
        Check(paths.BasePath == store.DirectoryFor(neo) && paths.Library == Path.Combine(game, "libraries") && paths.Assets == Path.Combine(game, "assets"), "isolation/shared resources wrong");
        GameRootPreference.Save(work, data, game);
        var moved = Path.Combine(root, "moved-local-final"); Directory.CreateDirectory(Path.Combine(moved, "data"));
        File.Copy(Path.Combine(data, "game-root.json"), Path.Combine(moved, "data", "game-root.json"));
        Check(GameRootPreference.Resolve(moved, Path.Combine(moved, "data")) == Path.Combine(moved, ".minecraft"), "portable relative game preference lost");
        Directory.CreateDirectory(Path.Combine(game, "versions", "empty"));
        var discovered = Path.Combine(game, "versions", "1.20.1"); Directory.CreateDirectory(discovered);
        await File.WriteAllTextAsync(Path.Combine(discovered, "1.20.1.json"), """{"id":"1.20.1","mainClass":"net.minecraft.client.main.Main"}""");
        var candidates = GameInstanceScan.Scan(store);
        Check(candidates.Count == 1 && candidates[0].Instance.Version == "1.20.1", "folder-only counted as installed candidate");
        Check(await new GameService(new HttpClient(), store).InspectInstallationAsync(candidates[0].Instance) != "已安装", "metadata alone claimed fully installed");
        var importedSource = Path.Combine(root, "import-root-final");
        Directory.CreateDirectory(Path.Combine(importedSource, "versions", "1.18.2", "mods"));
        Directory.CreateDirectory(Path.Combine(importedSource, "libraries", "fixture"));
        await File.WriteAllTextAsync(Path.Combine(importedSource, "versions", "1.18.2", "1.18.2.json"), """{"id":"1.18.2","mainClass":"net.minecraft.client.main.Main"}""");
        await File.WriteAllTextAsync(Path.Combine(importedSource, "versions", "1.18.2", "mods", "test.jar"), "mod-fixture");
        await File.WriteAllTextAsync(Path.Combine(importedSource, "libraries", "fixture", "lib.jar"), "shared-fixture");
        var imported = await GameImport.ImportDirectoryAsync(store, importedSource, "Imported final", "1.18.2", "原版", null, null, default);
        Check(File.Exists(Path.Combine(store.DirectoryFor(imported), "1.18.2.json")) && !Directory.Exists(Path.Combine(store.DirectoryFor(imported), "versions")) &&
            File.Exists(Path.Combine(store.DirectoryFor(imported), "mods", "test.jar")) && File.Exists(Path.Combine(game, "libraries", "fixture", "lib.jar")) &&
            !Directory.Exists(Path.Combine(store.DirectoryFor(imported), "libraries")), "import did not separate isolated metadata/mods and shared libraries");
        Check(File.Exists(Path.Combine(importedSource, "versions", "1.18.2", "mods", "test.jar")) && imported.AutomaticMemory == true, "import changed source or lost automatic memory recommendation");
        Console.WriteLine("PASS local-final real directory import separates version JSON/mods and shared libraries without nested versions or altering source");
        Console.WriteLine("PASS local-final .minecraft default, isolated version names, shared libraries/assets, movable relative preference and invalid/incomplete scan rejection");

        var old = Path.Combine(work, "minecraft", "instances", "old-guid");
        Directory.CreateDirectory(Path.Combine(old, "versions", "1.19.4")); Directory.CreateDirectory(Path.Combine(old, "saves", "world"));
        await File.WriteAllTextAsync(Path.Combine(old, "versions", "1.19.4", "1.19.4.json"), """{"id":"1.19.4","mainClass":"net.minecraft.client.main.Main"}""");
        await File.WriteAllTextAsync(Path.Combine(old, "saves", "world", "level.dat"), "save-fixture");
        var preview = LegacyGameMigration.Preview(work, store);
        Check(preview.Instances.Length == 1 && File.Exists(Path.Combine(old, "saves", "world", "level.dat")) && !Directory.Exists(Path.Combine(game, "versions", "old-guid")), "preview mutated source or target");
        var backup = await LegacyGameMigration.ExecuteAsync(preview, store, Path.Combine(work, "backups"));
        var migrated = Path.Combine(game, "versions", "old-guid");
        Check(File.ReadAllText(Path.Combine(migrated, "saves", "world", "level.dat")) == "save-fixture" && File.Exists(Path.Combine(old, "saves", "world", "level.dat")), "migration lost saves/source");
        Check(File.Exists(Path.Combine(migrated, "1.19.4.json")) && !Directory.Exists(Path.Combine(migrated, "versions")), "nested versions not flattened");
        Check(Directory.GetFiles(backup, "level.dat", SearchOption.AllDirectories).Length == 1 && LegacyGameMigration.Preview(work, store).Instances.Length == 0, "backup missing or repeated migration");
        Console.WriteLine("PASS local-final legacy read-only preview, explicit verified copy, original save retention, complete backup, nested versions flattening and repeat protection");

        Check(!GitHubUpdates.AllowedChannel("0.1.0", "0.2.0-alpha") && !GitHubUpdates.AllowedChannel("0.1.0-beta", "0.2.0-alpha") && GitHubUpdates.AllowedChannel("0.1.0-alpha", "0.2.0-beta"), "update channels wrong");
        Check(GitHubUpdates.AllowedChannel("0.1.0-alpha", "v0.1.1") && GitHubUpdates.AllowedChannel("0.1.0", "v0.1.1") && GitHubUpdates.CompareVersions("v0.1.1", "0.1.0-alpha") > 0, "Alpha and stable upgrade to 0.1.1");
        var bytes = new byte[128]; bytes[0] = 0x4d; bytes[1] = 0x5a; bytes[0x3c] = 0x40; bytes[0x40] = 0x50; bytes[0x41] = 0x45; bytes[0x44] = 0x64; bytes[0x45] = 0x86;
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        using var client = new HttpClient(new ExeFeed(bytes, sha)); var updates = new GitHubUpdates(client);
        var release = await updates.CheckAsync("0.1.0-alpha") ?? throw new Exception("direct EXE release missing");
        Check(release.AssetName == "XGLauncher.exe" && await updates.CheckAsync("0.3.0") == null, "EXE preference or no downgrade wrong");
        var downloaded = await updates.DownloadAsync(release, Path.Combine(work, "updates"), _ => { });
        var install = Path.Combine(work, "update-target"); Directory.CreateDirectory(install); var executable = Path.Combine(install, "XGLauncher.exe"); await File.WriteAllTextAsync(executable, "old");
        var saves = Path.Combine(install, ".minecraft", "versions", "1.21.1", "saves", "world"); Directory.CreateDirectory(saves); await File.WriteAllTextAsync(Path.Combine(saves, "level.dat"), "keep");
        var plan = new UpdateApplyRequest(downloaded, sha, install, 0, 0);
        using (var transaction = UpdateFiles.Prepare(plan)) transaction.Apply();
        Check(File.ReadAllText(executable) == "old", "direct EXE rollback failed");
        using (var transaction = UpdateFiles.Prepare(plan)) { transaction.Apply(); transaction.Commit(); }
        Check(File.ReadAllBytes(executable).SequenceEqual(bytes) && File.ReadAllText(Path.Combine(saves, "level.dat")) == "keep", "EXE apply changed user files");
        try { UpdateAcknowledgment.PathFor("../write-anywhere"); throw new Exception("ack traversal accepted"); } catch (InvalidDataException) { }
        var nonce = Guid.NewGuid().ToString("N"); UpdateAcknowledgment.Write(nonce);
        Check(File.ReadAllText(UpdateAcknowledgment.PathFor(nonce)) == Environment.ProcessId.ToString() && !File.Exists(UpdateAcknowledgment.PathFor(nonce) + ".tmp"), "update acknowledgment is not complete/atomic");
        File.Delete(UpdateAcknowledgment.PathFor(nonce));
        var updateCache = Path.Combine(work, "owned-update-cache"); var session = Path.Combine(updateCache, "v0.2.0", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(session);
        File.WriteAllText(Path.Combine(session, ".xg-update-owned"), UpdateStagingCleanup.Marker); File.WriteAllText(Path.Combine(session, "apply.json.result"), "Updated");
        File.WriteAllText(Path.Combine(session, "XGUpdater.exe"), "helper fixture"); File.WriteAllText(Path.Combine(session, "unrelated.txt"), "keep");
        Check(UpdateStagingCleanup.CleanCompleted(updateCache) == 1 && File.Exists(Path.Combine(session, "unrelated.txt")) && !File.Exists(Path.Combine(session, "XGUpdater.exe")), "completed update cleanup deleted unowned data or retained helper");
        Console.WriteLine("PASS local-final direct-EXE release/download/SHA/staging/rollback, prerelease channels, no downgrade and .minecraft preservation; nonce paths constrained");

        using var javaHttp = new HttpClient(new JavaFeed(bytes, sha)); var java = new OfficialJavaInstaller(javaHttp);
        var package = await java.ResolveAsync(21, default);
        var installer = await java.DownloadAsync(package, work, null, default);
        Check(installer.StartsWith(Path.Combine(work, ".xg-java-downloads")) && File.ReadAllBytes(installer).SequenceEqual(bytes), "official Java cache misplaced or corrupted");
        var keep = Path.Combine(Path.GetDirectoryName(installer)!, "unrelated.txt"); await File.WriteAllTextAsync(keep, "unowned");
        OfficialJavaInstaller.CleanupVerified(installer);
        Check(!File.Exists(installer) && File.Exists(keep), "verified installer cleanup deletes unrelated data");
        try { OfficialJavaInstaller.Validate(package with { Url = new Uri("https://example.invalid/java.msi") }); throw new Exception("untrusted MSI accepted"); } catch (InvalidDataException) { }
        Check(OfficialJavaInstaller.SuccessfulExit(3010) && !OfficialJavaInstaller.SuccessfulExit(1602), "MSI restart/cancel codes wrong");
        Console.WriteLine("PASS local-final official Temurin MSI metadata/source boundary, SHA-256 download, EXE cache, owned-file cleanup and installer cancellation/reboot exit classification (Windows execution unverified)");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class ExeFeed(byte[] bytes, string sha) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.Host != "api.github.com") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            var asset = new { name = "XGLauncher.exe", state = "uploaded", size = bytes.Length, digest = "sha256:" + sha,
                browser_download_url = "https://github.com/Zc345370669/XG-Launcher/releases/download/v0.2.0-alpha/XGLauncher.exe" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new[] { new { draft = false, tag_name = "v0.2.0-alpha", assets = new[] { asset } } })) });
        }
    }
    private sealed class JavaFeed(byte[] bytes, string sha) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.Host != "api.adoptium.net") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new[] { new { binary = new { architecture = "x64", os = "windows", image_type = "jdk", installer = new { name = "OpenJDK21-test.msi", size = bytes.Length, checksum = sha, link = "https://github.com/adoptium/temurin21-binaries/releases/download/test/OpenJDK21-test.msi" } } } })) });
        }
    }
}
