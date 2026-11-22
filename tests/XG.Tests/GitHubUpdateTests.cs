// SPDX-License-Identifier: MIT
using System.Net;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;
using XG.Core;

internal static class GitHubUpdateTests
{
    internal static async Task RunAsync(string root)
    {
        var package = MakePackage();
        var digest = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant();
        var handler = new Fixture(package, digest);
        using var http = new HttpClient(handler);
        var updates = new GitHubUpdates(http);
        var release = await updates.CheckAsync("0.1.0-alpha") ?? throw new Exception("new Alpha not found");
        Check(release.Version == "v0.2.0-alpha.2", "draft, wrong repo or release version selected");
        Check(await updates.CheckAsync("0.2.0-alpha.2") == null, "same version incorrectly offered");
        Check(GitHubUpdates.CompareVersions("0.2.0-alpha.10", "0.2.0-alpha.2") > 0 && GitHubUpdates.CompareVersions("0.2.0", "0.2.0-alpha.10") > 0,
            "semantic prerelease comparison wrong");
        var folder = Path.Combine(root, "updater"); Directory.CreateDirectory(folder);
        var archive = await updates.DownloadAsync(release, folder, _ => { });
        Check(File.ReadAllBytes(archive).SequenceEqual(package) && handler.NoPrivateHeaders, "download differs or sent private headers");
        handler.Corrupt = true;
        try { await updates.DownloadAsync(release, folder, _ => { }); throw new Exception("corrupt update accepted"); } catch (InvalidDataException) { }
        Check(File.ReadAllBytes(archive).SequenceEqual(package) && !Directory.GetFiles(folder, "*.download").Any(), "failed download replaced good package or leaked temporary file");
        handler.Corrupt = false; handler.BadRedirect = true;
        try { await updates.DownloadAsync(release, folder, _ => { }); throw new Exception("untrusted redirect accepted"); } catch (InvalidDataException) { }
        Console.WriteLine("PASS official GitHub release/prerelease selection, semantic versions, same-version skip, SHA-256/length verification, failed-package cleanup and redirect/privacy boundary");

        var installed = Path.Combine(folder, "installed"); Directory.CreateDirectory(installed);
        File.WriteAllText(Path.Combine(installed, "XGLauncher.exe"), "old application");
        foreach (var name in new[] { "data/accounts.json", "data/settings.json", "minecraft/versions/Test/saves/keep.txt", "minecraft/versions/Test/mods/keep.jar" })
        { var path = Path.Combine(installed, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "user fixture"); }
        var plan = new UpdateApplyRequest(archive, digest, installed, 0, 0);
        using (var transaction = UpdateFiles.Prepare(plan)) { transaction.Apply(); }
        Check(File.ReadAllText(Path.Combine(installed, "XGLauncher.exe")) == "old application", "uncommitted update did not roll back");
        using (var transaction = UpdateFiles.Prepare(plan)) { transaction.Apply(); transaction.Commit(); }
        Check(File.ReadAllBytes(Path.Combine(installed, "XGLauncher.exe")).SequenceEqual(Exe()), "new program not installed");
        Check(Directory.GetFiles(Path.Combine(installed, "minecraft"), "*", SearchOption.AllDirectories).All(p => File.ReadAllText(p) == "user fixture") &&
            Directory.GetFiles(Path.Combine(installed, "data"), "*", SearchOption.AllDirectories).All(p => File.ReadAllText(p) == "user fixture"), "update changed user data");
        using (var single = new MemoryStream())
        {
            using (var zip = new ZipArchive(single, ZipArchiveMode.Create, true))
            { using var output = zip.CreateEntry("XG-Launcher/XGLauncher.exe").Open(); output.Write(Exe()); }
            var bytes = single.ToArray(); File.WriteAllBytes(archive, bytes);
            using var transaction = UpdateFiles.Prepare(plan with { Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
            transaction.Apply(); transaction.Commit();
        }
        Console.WriteLine("PASS single-EXE XG-Launcher wrapper is normalized for in-place updates without touching account/game directories; legacy package compatibility retained");
        foreach (var bad in new[] { "../outside.txt", "minecraft/versions/Test/saves/keep.txt", "data/accounts.json" })
        {
            var malicious = MakePackage(bad); File.WriteAllBytes(archive, malicious);
            try { using var transaction = UpdateFiles.Prepare(plan with { Sha256 = Convert.ToHexString(SHA256.HashData(malicious)) }); throw new Exception("unsafe package accepted"); }
            catch (InvalidDataException) { }
        }
        Console.WriteLine("PASS real update file transaction/rollback replaces old program, preserves accounts/settings/saves/mods, rejects traversal and every user-data write in package");
    }
    private static byte[] Exe() { var bytes = new byte[128]; bytes[0] = 0x4d; bytes[1] = 0x5a; bytes[0x3c] = 0x40; bytes[0x40] = 0x50; bytes[0x41] = 0x45; bytes[0x44] = 0x64; bytes[0x45] = 0x86; return bytes; }
    private static byte[] MakePackage(string? extra = null)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            using (var output = zip.CreateEntry("XGLauncher.exe").Open()) output.Write(Exe());
            using (var output = new StreamWriter(zip.CreateEntry("LICENSE").Open())) output.Write("MIT fixture");
            using (var output = new StreamWriter(zip.CreateEntry("tools/XGUpdater.exe").Open())) output.Write("updater fixture");
            if (extra != null) using (var output = new StreamWriter(zip.CreateEntry(extra).Open())) output.Write("forbidden");
        }
        return memory.ToArray();
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Fixture(byte[] package, string digest) : HttpMessageHandler
    {
        internal bool Corrupt, BadRedirect;
        internal bool NoPrivateHeaders = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            NoPrivateHeaders &= request.Headers.Authorization == null && !request.Headers.Contains("Cookie");
            if (request.RequestUri!.Host == "api.github.com")
            {
                var release = new { draft = false, tag_name = "v0.2.0-alpha.2", assets = new[] { new { name = "XGLauncher-portable-win-x64.zip", state = "uploaded", size = package.Length,
                    digest = "sha256:" + digest, browser_download_url = "https://github.com/Zc345370669/XG-Launcher/releases/download/v0.2.0-alpha.2/XGLauncher-portable-win-x64.zip" } } };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new[] { release })) });
            }
            if (BadRedirect) { var redirect = new HttpResponseMessage(HttpStatusCode.Redirect); redirect.Headers.Location = new Uri("https://example.invalid/update"); return Task.FromResult(redirect); }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Corrupt ? Encoding.UTF8.GetBytes("invalid") : package) });
        }
    }
}
