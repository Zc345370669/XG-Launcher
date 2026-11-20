// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Net;
using System.Text;
using XG.Core;

internal static class JavaNativeTests
{
    internal static async Task RunAsync(string root)
    {
        var work = Path.Combine(root, "native-discovery"); Directory.CreateDirectory(work);
        void Pass(string name) => Console.WriteLine("PASS Java native: " + name);
        void Require(bool condition, string message) { if (!condition) throw new Exception("Java native: " + message); }
        JavaSearchInputs Inputs(string? home = null, string? jdk = null, string? path = null, string[]? common = null, string[]? registry = null, string[]? extra = null) =>
            new(Path.Combine(work, "empty-managed"), home, jdk, path, common ?? [], registry ?? [], extra ?? []);
        string Properties(string version, string arch, string vendor) => $"Property settings:\n    java.version = {version}\n    java.vendor = {vendor}\n    os.arch = {arch}\n";

        foreach (var sample in new[] { ("1.8.0_442", 8, "Oracle Corporation"), ("16.0.2", 16, "Oracle Corporation"),
                     ("17.0.14+7", 17, "Eclipse Adoptium"), ("21.0.6", 21, "Microsoft"), ("25-ea", 25, "OpenJDK") })
        {
            var parsed = JavaProbe.Parse("fixture-java", Properties(sample.Item1, "amd64", sample.Item3), 0);
            Require(parsed?.Major == sample.Item2 && parsed.Version == sample.Item1 && parsed.Vendor == sample.Item3, "property version/vendor parsing");
        }
        Pass("Java 8/16/17/21/EA versions and Oracle, Temurin, Microsoft, OpenJDK properties");
        Require(JavaProbe.Parse("fixture", "os.arch = amd64\nopenjdk version \"21.0.1\"", 0)?.Major == 21, "banner fallback");
        Pass("version banner fallback without java.version property");
        foreach (var sample in new[] { ("amd64", "x64"), ("x86_64", "x64"), ("aarch64", "arm64"), ("i386", "x86"), ("x64", "x64"), ("alien", "unknown") })
            Require(JavaProbe.NormalizeArchitecture(sample.Item1) == sample.Item2, "architecture alias");
        Require(JavaProbe.Parse("fixture", Properties("21", "alien", "Fixture"), 0) is null, "unrecognized architecture accepted");
        Pass("x64/x86/ARM64 architecture normalization and rejection");
        Require(JavaProbe.Parse("fixture", Properties("21", "amd64", "Fixture"), 1) is null &&
            JavaProbe.Parse("fixture", "not Java", 0) is null && JavaProbe.Parse("fixture", Properties("invalid", "amd64", "Fixture"), 0) is null &&
            JavaProbe.Parse("fixture", new string('x', 70000), 0) is null, "invalid output accepted");
        Pass("nonzero exit, invalid version, missing fields and oversized output rejected");
        var combinations = JavaLocations.RegistryTargets().ToArray();
        Require(combinations.Length == 4 && combinations.Distinct().Count() == 4 && combinations.Count(x => x.Machine) == 2 && combinations.Count(x => x.Is64) == 2, "registry target coverage");
        Pass("HKCU/HKLM and 32/64-bit registry target plan (no registry access on Linux)");
        Require(await JavaDiscovery.InspectPathAsync("\0invalid") is null && await JavaDiscovery.InspectPathAsync(Path.Combine(work, "missing")) is null, "invalid path accepted");
        Pass("invalid and missing manual paths filtered");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await JavaDiscovery.DiscoverAsync(work, token: cancelled.Token); throw new Exception("discovery ignored cancellation"); } catch (OperationCanceledException) { }
            try { await JavaDiscovery.InspectPathAsync("missing", cancelled.Token); throw new Exception("probe ignored cancellation"); } catch (OperationCanceledException) { }
        }
        Pass("pre-cancelled discovery and validation propagate cancellation");

        using (var client = new HttpClient(new MetadataFixture()))
        {
            var game = new GameService(client, new InstanceStore(Path.Combine(work, "metadata-store")));
            foreach (var pair in MetadataFixture.Requirements)
                Require(await game.RequiredJavaMajorAsync(pair.Key) == pair.Value, "Minecraft metadata requirement " + pair.Key);
        }
        Pass("Minecraft 1.16.5=8, 1.17=16, 1.18/1.20.4=17, 1.20.5/1.21.1=21 from metadata");
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64" : "amd64";
        Require(JavaDiscovery.IsCompatible(new("fixture", 16, arch), 16) && !JavaDiscovery.IsCompatible(new("fixture", 21, arch), 16), "selected newest instead of exact requirement");
        Pass("exact required major preferred over numerically highest Java");

        if (OperatingSystem.IsWindows())
        {
            // Read-only Windows integration hook: exercised only on Windows.
            // No assertion that registry-installed Java exists on every machine.
            var inventory = await JavaDiscovery.DiscoverAsync(work);
            if (Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } home && JavaLocations.Executable(home) is { } configured)
            {
                var validated = await JavaDiscovery.InspectPathAsync(configured);
                Require(validated is not null && inventory.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.Path, validated.Path)), "Windows JAVA_HOME runtime not discovered");
                Pass("actual Windows JAVA_HOME runtime and read-only multi-source discovery");
            }
            else Console.WriteLine("SKIP Java native: Windows JAVA_HOME integration requires an installed configured runtime");
            Console.WriteLine("SKIP Java native: POSIX subprocess fixtures are Linux-only");
            return;
        }
        if (!OperatingSystem.IsLinux())
        {
            Console.WriteLine("SKIP Java native: executable integration fixtures require Linux or Windows");
            return;
        }
        async Task<string> Make(string name, string version = "21.0.6", string? suffix = null)
        {
            var file = Path.Combine(work, name, "bin", "java"); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "#!/bin/sh\n" + suffix + "\nprintf '%s\\n' '" + Properties(version, arch, "Synthetic Test Runtime").TrimEnd() + "' >&2\n");
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return file;
        }
        var j8 = await Make("jdk8", "1.8.0_442"); var j16 = await Make("jdk16", "16.0.2");
        var j17 = await Make("jdk17", "17.0.14"); var j21 = await Make("jdk21 with spaces");
        async Task CheckSource(JavaSearchInputs inputs, string expected, string label)
        {
            var values = await JavaDiscovery.DiscoverFromInputsAsync(inputs);
            Require(values.Any(x => x.Path == expected), label); Pass(label);
        }
        var savedHome = Environment.GetEnvironmentVariable("JAVA_HOME"); var savedJdk = Environment.GetEnvironmentVariable("JDK_HOME"); var savedPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("JAVA_HOME", Path.GetDirectoryName(Path.GetDirectoryName(j21)));
            Environment.SetEnvironmentVariable("JDK_HOME", Path.GetDirectoryName(Path.GetDirectoryName(j17)));
            Environment.SetEnvironmentVariable("PATH", '"' + Path.GetDirectoryName(j16) + '"');
            var captured = JavaLocations.Capture("", []);
            await CheckSource(captured with { CommonRoots = [], RegistryRoots = [] }, j21, "JAVA_HOME real environment capture and executable probe");
            await CheckSource(Inputs(jdk: captured.JdkHome), j17, "JDK_HOME capture and executable probe");
            await CheckSource(Inputs(path: captured.SearchPath), j16, "quoted PATH entry and executable probe");
        }
        finally { Environment.SetEnvironmentVariable("JAVA_HOME", savedHome); Environment.SetEnvironmentVariable("JDK_HOME", savedJdk); Environment.SetEnvironmentVariable("PATH", savedPath); }
        await CheckSource(Inputs(common: [Path.Combine(work, "jdk8")]), j8, "common vendor directory plan and bounded traversal");
        await CheckSource(Inputs(registry: [Path.Combine(work, "jdk17")]), j17, "injected registry value to candidate pipeline (simulated source)");
        await CheckSource(Inputs(extra: [j21]), j21, "custom/manual path through common discovery service");
        var alias = Path.Combine(work, "jdk alias"); Directory.CreateSymbolicLink(alias, Path.Combine(work, "jdk21 with spaces"));
        var duplicate = await JavaDiscovery.DiscoverFromInputsAsync(Inputs(home: alias, path: Path.GetDirectoryName(j21), extra: [j21, Path.Combine(alias, "bin", "java")]));
        Require(duplicate.Count == 1 && duplicate[0].Path == j21, "alias duplication");
        Pass("symlink directory, environment and manual duplicates coalesced by canonical executable");
        var loop = Path.Combine(work, "loop"); Directory.CreateDirectory(loop); Directory.CreateSymbolicLink(Path.Combine(loop, "cycle"), loop);
        using (var limit = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
            Require((await JavaDiscovery.DiscoverFromInputsAsync(Inputs(extra: [loop]), limit.Token, deep: true)).Count == 0, "symlink loop followed");
        Pass("recursive scan skips directory symlinks and cycles");
        var deepRoot = Path.Combine(work, "deep"); var folder = deepRoot;
        for (var i = 0; i < 8; i++) { folder = Path.Combine(folder, "level"); Directory.CreateDirectory(folder); }
        var deepJava = await Make(Path.GetRelativePath(work, folder));
        Require(!(await JavaDiscovery.DiscoverFromInputsAsync(Inputs(common: [deepRoot]))).Any(x => x.Path == deepJava), "quick depth bound lost");
        Require((await JavaDiscovery.DiscoverFromInputsAsync(Inputs(extra: [deepRoot]), deep: true)).Any(x => x.Path == deepJava), "explicit deep scan failed");
        Pass("quick recursion bounded; explicit custom deep scan can find deeper runtime");
        var tokenSource = new CancellationTokenSource();
        var progressCalls = 0;
        var syncProgress = new InlineProgress(_ => { progressCalls++; tokenSource.Cancel(); });
        try { await JavaDiscovery.DiscoverFromInputsAsync(Inputs(extra: [deepRoot]), tokenSource.Token, syncProgress, true); throw new Exception("cancel ignored"); } catch (OperationCanceledException) { }
        Require(progressCalls > 0, "scan cancelled before traversal rather than during it");
        tokenSource.Dispose();
        Pass("deep-scan cancellation leaves no global scan running");
        foreach (var pair in new[] { (j8, 8), (j16, 16), (j17, 17), (j21, 21) })
            Require((await JavaDiscovery.CompatibleAsync(pair.Item1, work, pair.Item2))?.Path == pair.Item1, "valid explicit preference replaced");
        Require((await JavaDiscovery.CompatibleAsync(Path.Combine(work, "invalid manual"), "", 21, [j21]))?.Path == j21, "invalid manual blocks fallback");
        Require((await JavaDiscovery.CompatibleAsync(j21, "", 16, [j16]))?.Path == j16, "incompatible manual blocks required Java");
        Pass("manual priority, invalid/incompatible fallback and multiple instance Java requirements");
        var counter = Path.Combine(work, "probe-count");
        var cached = await Make("cached", suffix: "echo probe >> '" + counter + "'");
        await JavaDiscovery.InspectPathAsync(cached); await JavaDiscovery.InspectPathAsync(cached);
        Require(File.ReadAllLines(counter).Length == 1, "cache did not reuse verified stamp");
        await File.WriteAllTextAsync(Path.Combine(work, "cached", "release"), "changed runtime fingerprint");
        await JavaDiscovery.InspectPathAsync(cached);
        Require(File.ReadAllLines(counter).Length == 2, "runtime change not revalidated");
        JavaDiscovery.Forget(cached); await JavaDiscovery.InspectPathAsync(cached);
        Require(File.ReadAllLines(counter).Length == 3, "forget failed");
        Pass("cache reuse, runtime file change invalidation and explicit revalidation");
        var slow = await Make("slow", suffix: "exec /bin/sleep 20");
        var watch = Stopwatch.StartNew();
        Require(await JavaProbe.ExecuteAsync(slow, TimeSpan.FromMilliseconds(150), default) is null && watch.Elapsed < TimeSpan.FromSeconds(3), "timeout not bounded");
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
        {
            try { await JavaDiscovery.InspectPathAsync(slow, cancel.Token); throw new Exception("running process ignored cancellation"); } catch (OperationCanceledException) { }
        }
        Pass("real child process timeout and in-flight cancellation");
        var flood = await Make("flood", suffix: "i=0; while [ $i -lt 40000 ]; do printf x; i=$((i+1)); done");
        Require(await JavaDiscovery.InspectPathAsync(flood) is null, "oversized output accepted");
        Pass("stream output memory cap rejects excessive process output");
        var notExecutable = Path.Combine(work, "not-executable"); await File.WriteAllTextAsync(notExecutable, "invalid");
        Require(await JavaDiscovery.InspectPathAsync(notExecutable) is null, "invalid process did not fail safely");
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => JavaDiscovery.InspectPathAsync(j21)));
        Require(concurrent.All(x => x?.Major == 21), "parallel validation mismatch");
        Pass("OS execution errors safely filtered and concurrent probes consistent");
        watch.Restart();
        var reused = await JavaDiscovery.CompatibleAsync(null, Path.Combine(work, "slow"), 21);
        Require(reused?.Major == 21 && watch.Elapsed < TimeSpan.FromSeconds(1), "compatible cache did not bypass unrelated stalled runtime");
        Pass("verified compatible cache avoids unrelated slow managed-runtime search");
        if (File.Exists("/usr/bin/java"))
        {
            var actual = await JavaDiscovery.InspectPathAsync("/usr/bin/java");
            Require(actual is { Major: > 0 }, "installed Linux JVM invalid");
            Pass("actual installed Linux JVM validated (not a Windows JVM test)");
        }
    }
    private sealed class InlineProgress(Action<string> action) : IProgress<string> { public void Report(string value) => action(value); }
    private sealed class MetadataFixture : HttpMessageHandler
    {
        internal static readonly Dictionary<string, int> Requirements = new() { ["1.16.5"] = 8, ["1.17"] = 16, ["1.18"] = 17, ["1.20.4"] = 17, ["1.20.5"] = 21, ["1.21.1"] = 21 };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var content = request.RequestUri!.AbsolutePath.EndsWith("version_manifest_v2.json")
                ? System.Text.Json.JsonSerializer.Serialize(new { versions = Requirements.Keys.Select(id => new { id, type = "release", url = "https://piston-meta.mojang.com/" + id + ".json" }) })
                : System.Text.Json.JsonSerializer.Serialize(new { javaVersion = new { majorVersion = Requirements[Path.GetFileNameWithoutExtension(request.RequestUri.AbsolutePath)] } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") });
        }
    }
}
