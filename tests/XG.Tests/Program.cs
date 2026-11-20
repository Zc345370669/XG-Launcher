// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using XG.Core;
using CmlLib.Core.Auth;

PublicSecurityTests.Run();
DualModeProgressTests.Run();
await MinecraftAuthenticationTests.RunAsync();
await AuthenticationSecurityTests.RunAsync();
var fakeGameSession = new MSession("FixturePlayer", "fixture-game-secret", "1f37c31e94e34000b131e73fea962b2b") { UserType = "msa" };
var sanitizer = new GameLogSanitizer(fakeGameSession);
var redacted = sanitizer.Redact("player FixturePlayer 1f37c31e94e34000b131e73fea962b2b --accessToken fixture-game-secret fixture@example.invalid");
Equal(false, redacted.Contains("FixturePlayer") || redacted.Contains("fixture-game-secret") || redacted.Contains("example.net") || redacted.Contains("1f37c31e"));
Equal(false, sanitizer.Redact("""request?code=opaque&x=1 authorization: Bearer other-token""").Contains("opaque"));
Equal(false, sanitizer.Redact("""{"refresh_token":"secret-value"}""").Contains("secret-value"));
Equal(false, sanitizer.Redact("Set-Cookie: sid=private-cookie; Path=/").Contains("private-cookie"));
Equal(false, sanitizer.Redact("""{"Authorization": "Bearer json-secret"}""").Contains("json-secret"));
Equal(false, sanitizer.Redact("""{"Cookie":"sid=json-cookie"}""").Contains("json-cookie"));
Equal("OpenGL error code 1281", sanitizer.Redact("OpenGL error code 1281"));
Console.WriteLine("PASS game log token, OAuth code, cookie and profile redaction");

var root = Path.Combine(Path.GetTempPath(), "xg-test-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new InstanceStore(root);
    await JavaNativeTests.RunAsync(root);
    await InstallerJavaTests.RunAsync(root);
    await JavaCleanupTests.RunAsync(root);
    await Phase9TransferTests.RunAsync(root);
    await Phase10Tests.RunAsync(root);
    await Phase11Tests.RunAsync(root);
    await Phase12Tests.RunAsync(root);
    await Phase13Tests.RunAsync(root);
    await InstanceLayoutTests.RunAsync(root);
    await FlatVersionTests.RunAsync(root);
    await Phase15Tests.RunAsync(root);
    await Phase16Tests.RunAsync(root);
    await Phase17Tests.RunAsync(root);
    await Phase18Tests.RunAsync(root);
    await Phase19Tests.RunAsync(root);
    await ShutdownTests.RunAsync(root);
    await GitHubUpdateTests.RunAsync(root);
    var portable = Path.Combine(root, "portable");
    var portableData = Path.Combine(portable, "data");
    var portableStore = new InstanceStore(portableData, Path.Combine(portable, "minecraft"));
    var portableInstance = new Instance(Guid.NewGuid(), "Portable", "1.21.1", AutomaticMemory: true);
    portableStore.Save(portableInstance);
    var saveDir = Path.Combine(portableStore.DirectoryFor(portableInstance), "saves");
    Directory.CreateDirectory(saveDir);
    File.WriteAllText(Path.Combine(saveDir, "test.txt"), "source");
    var copied = await portableStore.CopyAsync(portableInstance);
    File.WriteAllText(Path.Combine(portableStore.DirectoryFor(copied), "saves", "test.txt"), "copy");
    Equal("source", File.ReadAllText(Path.Combine(saveDir, "test.txt")));
    portableStore.Remove(copied, true);
    Equal(false, Directory.Exists(portableStore.DirectoryFor(copied)));
    Equal(1, portableStore.Entries.Count);
    using (var localHttp = new HttpClient(new RangeFixture([1])))
    {
        var persistent = new Downloads(localHttp, portableData, portable);
        persistent.SetConcurrency(3);
        var portableJob = persistent.Enqueue(new ModFile("test.jar", new Uri("https://cdn.modrinth.com/a.jar"), 1,
            Convert.ToHexString(SHA512.HashData(new byte[] { 1 }))), Path.Combine(portableStore.DirectoryFor(portableInstance), "mods"));
        persistent.SetPriority(portableJob.Id, true);
        var moved = Path.Combine(root, "moved");
        Directory.Move(portable, moved);
        var movedStore = new InstanceStore(Path.Combine(moved, "data"), Path.Combine(moved, "minecraft"));
        var movedQueue = new Downloads(localHttp, movedStore.Root, moved);
        Equal(3, movedQueue.Concurrency);
        Equal(1, movedQueue.Items.Single().Priority);
        Equal(Path.Combine(movedStore.DirectoryFor(portableInstance), "mods", "test.jar"), movedQueue.Items.Single().Target);
        movedQueue.Cancel(portableJob.Id);
        Equal("已取消", movedQueue.Items.Single().State);
        movedQueue.ClearCompleted();
        Equal(0, movedQueue.Items.Count);
    }
    Equal(512, InstanceMemory.Recommend(2048, 300, "1.21.1", 100, true));
    Equal(true, InstanceMemory.Recommend(32768, 20000, "1.21.1", 90, true) >
        InstanceMemory.Recommend(32768, 20000, "1.21.1", 0, false));
    Console.WriteLine("PASS portable relocation, queue priority/limit/cancellation persistence, memory budget, isolated instance copy/delete");
    SynchronizationTests.Run(root);
    var instance = new Instance(Guid.NewGuid(), "测试实例", "1.21.1");
    store.Save(instance);
    Equal(instance, new InstanceStore(root).Entries.Single());
    Reject<ArgumentException>(() => store.Save(instance with { Memory = 2 }));
    Equal(instance, new InstanceStore(root).Entries.Single());
    Reject<ArgumentException>(() => Instance.ValidatePlayer("bad name"));
    Reject<InvalidDataException>(() => InstanceStore.SafeFile(root, "../evil.jar"));
    Reject<InvalidDataException>(() => InstanceStore.SafeFile(root, "AUX.jar"));
    Console.WriteLine("PASS instance round-trip, rejected settings, offline username, and unsafe paths");

    var mods = Path.Combine(store.DirectoryFor(instance), "mods");
    var local = Path.Combine(root, "sample.jar");
    using (var zip = ZipFile.Open(local, ZipArchiveMode.Create)) zip.CreateEntry("fabric.mod.json");
    LocalMods.Import(local, mods);
    Equal(true, File.Exists(Path.Combine(mods, "sample.jar")));
    Reject<IOException>(() => LocalMods.Import(local, mods));
    Console.WriteLine("PASS local JAR import and duplicate protection");

    var world = Path.Combine(store.DirectoryFor(instance), "saves", "world");
    Directory.CreateDirectory(world);
    File.WriteAllText(Path.Combine(world, "level.dat"), "world save");
    var backup = Path.Combine(root, "backup.zip");
    Backups.Create(store, instance, backup);
    var restored = Backups.Restore(store, backup);
    Equal(false, restored.Id == instance.Id);
    Equal("world save", File.ReadAllText(Path.Combine(store.DirectoryFor(restored), "saves", "world", "level.dat")));
    Console.WriteLine("PASS backup and isolated restore");

    var bytes = "known payload"u8.ToArray();
    var handler = new RangeFixture(bytes);
    using var http = new HttpClient(handler);
    var downloads = new Downloads(http, root);
    var url = new Uri("https://cdn.modrinth.com/data/test.jar");
    var valid = new ModFile("valid.jar", url, bytes.Length, Convert.ToHexString(SHA512.HashData(bytes)));
    var task = downloads.Enqueue(valid, mods);
    var partial = task.Target + "." + task.Id.ToString("N") + ".part";
    File.WriteAllBytes(partial, bytes[..3]);
    var transferIdentity = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(partial)+"|"+valid.Url.AbsoluteUri+"|"+valid.Size+"|"+valid.Sha512)));
    File.WriteAllText(partial+".identity.json",System.Text.Json.JsonSerializer.Serialize(new { Identity=transferIdentity, State="Pending" }));
    Equal(false, await downloads.RunAsync(task.Id, () => { }, (_, _) => { }, () => false));
    Equal(false, File.Exists(task.Target));
    Equal(3L, handler.LastOffset);
    Equal(true, await new Downloads(http, root).RunAsync(task.Id, () => { }, (_, _) => { }, () => true));
    Equal(Convert.ToHexString(bytes), Convert.ToHexString(File.ReadAllBytes(task.Target)));
    var bad = downloads.Enqueue(valid with { Name = "bad.jar", Sha512 = new string('0', 128) }, mods);
    Equal(false, await downloads.RunAsync(bad.Id, () => { }, (_, _) => { }, () => true));
    Equal(false, File.Exists(bad.Target));
    var paused = downloads.Enqueue(valid with { Name = "paused.jar" }, mods);
    Equal(false, await downloads.RunAsync(paused.Id, () => downloads.Pause(paused.Id), (_, _) => { }, () => true));
    Equal("已暂停", new Downloads(http, root).Items.Single(x => x.Id == paused.Id).State);
    Console.WriteLine("PASS range resume, persistence, busy-instance commit protection, bad hash and pause");

    var pack = Path.Combine(root, "sample.mrpack");
    void MakePack(string path, string entry)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open()))
            writer.Write("""{"formatVersion":1,"game":"minecraft","name":"Pack","dependencies":{"minecraft":"1.21.1","fabric-loader":"0.16.10"},"files":[]}""");
        using var contents = new StreamWriter(zip.CreateEntry(entry).Open());
        contents.Write("configuration");
    }
    MakePack(pack, "overrides/config/client.toml");
    var importer = new PackImporter(http, store);
    var imported = await importer.ImportAsync(pack, _ => { });
    Equal("Fabric", imported.Loader);
    Equal(true, imported.Pack);
    Equal("configuration", File.ReadAllText(Path.Combine(store.DirectoryFor(imported), "config", "client.toml")));
    var evil = Path.Combine(root, "bad.mrpack");
    MakePack(evil, "overrides/../escape.txt");
    var before = store.Entries.Count;
    try { await importer.ImportAsync(evil, _ => { }); throw new Exception("Traversal accepted"); }
    catch (InvalidDataException) { }
    Equal(before, store.Entries.Count);
    Equal(false, Directory.EnumerateDirectories(store.InstanceRoot, "*.importing").Any());
    Console.WriteLine("PASS mrpack import, traversal rejection and cleanup");

    if (args.Contains("--live") || args.Contains("--game") || args.Contains("--modrinth-live"))
    {
        using var live = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        live.DefaultRequestHeaders.UserAgent.ParseAdd("XGLauncher/0.1.0-alpha (integration-test)");
        var game = new GameService(live, store);
        if (!args.Contains("--modrinth-live"))
        {
            var versions = await game.VersionsAsync();
            Equal(true, versions.Contains("1.21.1"));
            Equal(true, (await game.LoaderVersionsAsync("1.21.1", "Fabric")).Length > 0);
            Equal(true, (await game.LoaderVersionsAsync("1.21.1", "Quilt")).Length > 0);
            Console.WriteLine("PASS live Mojang, Fabric and Quilt version catalogues");
            if (args.Contains("--loaders"))
            {
                foreach (var loader in new[] { "Forge", "NeoForge" })
                {
                    var selection = await game.LoaderVersionsAsync("1.21.1", loader);
                    Equal(true, selection.Length > 0);
                    Console.WriteLine($"PASS live {loader} version catalogue ({selection.Length} options)");
                }
            }
        }
        var catalog = new Modrinth(live);
        var project = (await catalog.SearchAsync("sodium", false, "1.21.1", "Fabric", "", "relevance"))
            .First(x => x.Slug == "sodium");
        var versionsFound = await catalog.VersionsAsync(project, instance with { Loader = "Fabric" });
        Equal(true, versionsFound.Length > 0);
        Equal(true, (await catalog.LicenseAsync(project)).Length > 0);
        var file = versionsFound[0].File;
        var liveDownload = new Downloads(live, root);
        var job = liveDownload.Enqueue(file, mods);
        if (!await liveDownload.RunAsync(job.Id, () => { }, (_, _) => { }, () => true))
            throw new IOException(liveDownload.Items.Single(x => x.Id == job.Id).Error);
        Equal(file.Size, new FileInfo(job.Target).Length);
        Equal(true, (await catalog.SearchAsync("adventure", true, "", "", "", "relevance")).Any(project => project.Pack));
        var firstPage = await catalog.SearchPageAsync("fabric", false, "", "", "", "relevance", 0, 10);
        var secondPage = await catalog.SearchPageAsync("fabric", false, "", "", "", "relevance", 10, 10);
        Equal(10, firstPage.Results.Length);
        Equal(10, secondPage.Results.Length);
        Equal(true, firstPage.TotalHits >= 20 && firstPage.Results[0].Id != secondPage.Results[0].Id);
        var hashes = await catalog.VersionsForHashesAsync([file.Sha512.ToLowerInvariant()], default);
        Equal(true, hashes.Values.Any(value => value.Id == versionsFound[0].Id));
        Console.WriteLine("PASS live Modrinth mod and modpack search, offset pagination, version hash lookup, license and verified JAR download");
        if (args.Contains("--game"))
        {
            var integrationRoot = Environment.GetEnvironmentVariable("XG_GAME_TEST_ROOT") ?? root;
            var launchStore = new InstanceStore(Path.Combine(integrationRoot, "launch-data"), Path.Combine(integrationRoot, "minecraft"));
            launchStore.Save(instance);
            var launchGame = new GameService(live, launchStore);
            var resourceOperations = new ResourceOperations(launchStore.Root);
            var previousMainCount = resourceOperations.MainJobs.Count;
            using var process = await resourceOperations.RunAsync(instance.Name, "实例安装", "Mojang", instance.Name,
                mark => launchGame.PrepareWithSessionAsync(instance, "XGTest", () => Task.FromResult<MSession?>(null),
                message => { if (message.EndsWith("/1")) Console.WriteLine(message); }, resourceOperations, mark),
                new ResourceRecovery("game", instance.Id, InstallationReceipt.Configuration(instance)));
            Equal(previousMainCount + 1, resourceOperations.MainJobs.Count);
            Equal("已安装", await InstallationReceipt.InspectAsync(launchStore, instance));
            Equal(true, File.Exists(Path.Combine(launchStore.SharedMinecraft!, "versions", "1.21.1", "1.21.1.jar")));
            Equal(true, resourceOperations.Items.Any(task => task.Kind == "资源与依赖" && task.State == "已完成"));
            Equal(launchStore.DirectoryFor(instance), process.StartInfo.WorkingDirectory);
            Console.WriteLine("PASS official 1.21.1 installation and Java process construction");
            var resolved = false;
            using var onlineShape = await launchGame.BuildInstalledAsync(instance, "XGTest", () =>
            {
                resolved = true;
                return Task.FromResult<MSession?>(fakeGameSession);
            });
            Equal(true, resolved);
            var start = onlineShape.StartInfo;
            var command = start.ArgumentList.Count == 0 ? start.Arguments : string.Join(' ', start.ArgumentList);
            foreach (var expected in new[] { "--uuid", fakeGameSession.UUID, "--username", fakeGameSession.Username,
                "--accessToken", fakeGameSession.AccessToken, "--version", instance.Version, "--gameDir",
                $"-Xmx{instance.Memory}M", "--width", "--height", "--userType", "msa" })
                if (string.IsNullOrEmpty(expected) || !command.Contains(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Generated authenticated game command is missing a required argument.");
            Equal(launchStore.DirectoryFor(instance), start.WorkingDirectory);
            Equal(true, command.Contains(launchStore.DirectoryFor(instance), StringComparison.Ordinal));
            Equal(true, File.Exists(start.FileName));
            Console.WriteLine("PASS real installed vanilla launch configuration with a fixture session; no authenticated game process started");
            Console.WriteLine(process.StartInfo.FileName);
            var jarPath = Path.Combine(launchStore.SharedMinecraft!, "versions", "1.21.1", "1.21.1.jar");
            var originalTime = File.GetLastWriteTimeUtc(jarPath);
            File.Delete(Path.Combine(launchStore.DirectoryFor(instance), ".xg-installation.json"));
            Equal("已安装", await launchGame.InspectInstallationAsync(instance));
            Equal(originalTime, File.GetLastWriteTimeUtc(jarPath));
            Equal(previousMainCount + 1, resourceOperations.MainJobs.Count);
            Console.WriteLine("PASS real installed vanilla re-adopted with network prohibited; client file reused unchanged and no new download task");
            if (args.Contains("--start"))
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                process.Start();
                var error = process.StandardError.ReadToEndAsync(cts.Token);
                try
                {
                    while (await process.StandardOutput.ReadLineAsync(cts.Token) is { } line)
                    {
                        Console.WriteLine(line);
                        if (line.Contains("OpenAL initialized", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("Created:", StringComparison.Ordinal) && line.Contains("-atlas", StringComparison.Ordinal))
                        {
                            Console.WriteLine("PASS Java game renderer reached resource initialization");
                            return;
                        }
                    }
                    throw new InvalidOperationException(await error);
                }
                finally
                {
                    if (!process.HasExited) process.Kill(true);
                    await process.WaitForExitAsync();
                }
            }
        }
    }
}
finally { Directory.Delete(root, true); }

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}, got {actual}");
}

static void Reject<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}

sealed class RangeFixture(byte[] bytes) : HttpMessageHandler
{
    public long LastOffset { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastOffset = request.Headers.Range?.Ranges.Single().From ?? 0;
        var content = new ByteArrayContent(bytes[(int)LastOffset..]);
        if (LastOffset > 0) content.Headers.ContentRange = new ContentRangeHeaderValue(LastOffset, bytes.Length - 1, bytes.Length);
        return Task.FromResult(new HttpResponseMessage(LastOffset == 0 ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
            { Content = content, RequestMessage = request });
    }
}
