// SPDX-License-Identifier: MIT
using System.Net;
using System.Text;
using XG.Core;

internal static class Phase11Tests
{
    internal static async Task RunAsync(string path)
    {
        var dir = Path.Combine(path, "phase11-test");
        Directory.CreateDirectory(dir);
        using var client = new HttpClient(new GameFixture());
        var game = new GameService(client, new InstanceStore(dir));
        var manifest = await game.OfficialVersionsAsync();
        if (manifest.Length != 2 || manifest[0].Id != "25w04a" || manifest[0].Type != "snapshot" ||
            await game.RequiredJavaMajorAsync("1.21.1") != 21)
            throw new Exception("Official release ordering, types or Java metadata were lost.");
        if (!Instance.IsValidVersion("1.14 Pre-Release 5") || Instance.IsValidVersion("../version"))
            throw new Exception("Supported version names or path safety regressions.");
        if (OperatingSystem.IsLinux())
        {
            var bin = Path.Combine(dir, "bin");
            Directory.CreateDirectory(bin);
            var java = Path.Combine(bin, "java");
            await WriteJavaAsync(java, "amd64");
            var found = await JavaDiscovery.CompatibleAsync(null, dir, 21);
            if (found is not { Major: 21, Architecture: "amd64" })
                throw new Exception("Compatible managed Java was not selected.");
            if (await JavaDiscovery.CompatibleAsync(java, dir, 17) is { Major: not 17 }) throw new Exception("incompatible manual path selected");
            await WriteJavaAsync(java, "x86");
            if (await JavaDiscovery.CompatibleAsync(java, dir, 21) is { Architecture: "x86" }) throw new Exception("incompatible architecture selected");
            if (File.Exists("/usr/bin/java"))
            {
                var real = await JavaDiscovery.InspectAsync("/usr/bin/java", dir);
                if (real is null) throw new Exception("Real installed Java could not be probed.");
                Console.WriteLine($"PASS locally installed JVM probe, major version {real.Value.Major}");
            }
        }
        Console.WriteLine("PASS Mojang metadata, release order, safe historical IDs; Linux JVM compatibility fixture");

        static async Task WriteJavaAsync(string file, string arch)
        {
            await File.WriteAllTextAsync(file, $"#!/bin/sh\necho '    os.arch = {arch}' >&2\necho 'openjdk version \"21.0.1\"' >&2\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }


    }

    private sealed class GameFixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var json = request.RequestUri!.AbsolutePath.EndsWith("version_manifest_v2.json", StringComparison.Ordinal)
                ? """{"versions":[{"id":"1.21.1","type":"release","url":"https://piston-meta.mojang.com/example.json","releaseTime":"2024-08-08T00:00:00Z"},{"id":"25w04a","type":"snapshot","url":"https://piston-meta.mojang.com/snapshot.json","releaseTime":"2025-01-20T00:00:00Z"}]}"""
                : """{"javaVersion":{"majorVersion":21}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
