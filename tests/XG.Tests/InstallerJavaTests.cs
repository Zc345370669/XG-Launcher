// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Text.Json;
using CmlLib.Core.Installer.Forge;
using XG.Core;

internal static class InstallerJavaTests
{
    internal static async Task RunAsync(string root)
    {
        var home = Path.Combine(root, "installer Java with spaces", "bin"); Directory.CreateDirectory(home);
        var java = Path.Combine(home, "java.exe"); var gui = Path.Combine(home, "javaw.exe");
        await File.WriteAllTextAsync(java, "fixture-not-executed");
        try { InstallerJava.Resolve(java, true); throw new Exception("missing GUI entry silently fell back to console Java"); }
        catch (InvalidOperationException error) when (error.Message.Contains("javaw.exe", StringComparison.Ordinal)) { }
        await File.WriteAllTextAsync(gui, "fixture-not-executed");
        Check(InstallerJava.Resolve(java, true) == gui && InstallerJava.Resolve(gui, true) == gui, "silent entry not selected in same Java home");
        Check(InstallerJava.Resolve(java, false) == java && InstallerJava.Resolve(null, false) == null, "non-Windows Java selection changed");
        if (!OperatingSystem.IsWindows())
        {
            var libs = Path.Combine(root, "installer-process-libraries"); var jar = Path.Combine(libs, "fixture", "processor", "1", "processor-1.jar");
            Directory.CreateDirectory(Path.GetDirectoryName(jar)!);
            using (var archive = ZipFile.Open(jar, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("META-INF/MANIFEST.MF").Open()))
                await writer.WriteAsync("Manifest-Version: 1.0\nMain-Class: fixture.Processor\n");
            var output = new List<string>(); var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var sink = new Sink(line => { lock(output) output.Add(line); if (line.Contains("processor-ready",StringComparison.Ordinal)) ready.TrySetResult(); });
            async Task Script(string body) {
                await File.WriteAllTextAsync(gui, "#!/bin/sh\necho processor-stdout\necho processor-stderr >&2\n" + body + "\n");
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(gui, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            using var json = JsonDocument.Parse("""[{"jar":"fixture:processor:1","args":[],"sides":["client"]}]""");
            var processor = new ForgeInstallProcessor(InstallerJava.Resolve(java, true)!);
            await Script("exit 0");
            await processor.StartProcessors(json.RootElement, new(), libs, null, sink, default);
            lock(output) Check(output.Any(x=>x.Contains("processor-stdout",StringComparison.Ordinal)) && output.Any(x=>x.Contains("processor-stderr",StringComparison.Ordinal)), "installer output/error pipes lost");
            await Script("exit 17");
            try { await processor.StartProcessors(json.RootElement, new(), libs, null, sink, default); throw new Exception("installer failure hidden"); }
            catch (InvalidOperationException error) when(error.Message.Contains("17",StringComparison.Ordinal)) { }
            await Script("echo processor-ready\nexec sleep 30"); using var cancel = new CancellationTokenSource();
            var running = processor.StartProcessors(json.RootElement, new(), libs, null, sink, cancel.Token);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(8)); cancel.Cancel();
            try { await running.WaitAsync(TimeSpan.FromSeconds(8)); throw new Exception("installer cancellation ignored"); } catch (OperationCanceledException) { }
        }
        var paths = new CmlLib.Core.MinecraftPath(Path.Combine(root,"silent-forge-public-api")); paths.CreateDirs();
        var baseJson = paths.GetVersionJsonPath("fixture-base"); Directory.CreateDirectory(Path.GetDirectoryName(baseJson)!);
        InstanceStore.WriteJson(baseJson,new {id="fixture-base",type="release",mainClass="fixture.Main",libraries=Array.Empty<object>(),time="2024-08-08T00:00:00Z",releaseTime="2024-08-08T00:00:00Z",arguments=new{game=Array.Empty<string>(),jvm=Array.Empty<string>()}});
        using var local = new HttpClient(new Denied());var parameters=CmlLib.Core.MinecraftLauncherParameters.CreateDefault(paths,local);
        parameters.VersionLoader=new CmlLib.Core.VersionLoader.LocalJsonVersionLoader(paths);parameters.FileExtractors=new CmlLib.Core.FileExtractors.FileExtractorCollection();
        var launcher=new CmlLib.Core.MinecraftLauncher(parameters);var backend=new Backend();var options=new ForgeInstallOptions {JavaPath=gui};
        Check(await GameService.InstallForgeWithoutBrowserAsync(launcher,"fixture-base",backend,options)==backend.VersionName&&backend.Calls==1,"silent public installer flow failed");
        Check(await GameService.InstallForgeWithoutBrowserAsync(launcher,"fixture-base",backend,options)==backend.VersionName&&backend.Calls==1,"already installed loader executed twice");
        Console.WriteLine("PASS silent installer selects same-home javaw on Windows, refuses missing entry without console fallback; actual CmlLib processor stdout/stderr, exit-17 error and cancellation preserved with Linux script fixture (Windows GUI unverified)");
    }
    private sealed class Sink(Action<string> write) : IProgress<string> { public void Report(string value) => write(value); }
    private sealed class Denied : HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>throw new Exception("Unexpected network in public installer fixture");}
    private sealed class Backend : IForgeInstaller
    {
        public string VersionName=>"fixture-loader";public int Calls{get;private set;}
        public Task Install(CmlLib.Core.MinecraftPath path,CmlLib.Core.Installers.IGameInstaller installer,ForgeInstallOptions options)
        {
            options.CancellationToken.ThrowIfCancellationRequested();Calls++;
            var json=path.GetVersionJsonPath(VersionName);Directory.CreateDirectory(Path.GetDirectoryName(json)!);
            InstanceStore.WriteJson(json,new{id=VersionName,inheritsFrom="fixture-base",type="release",mainClass="fixture.LoaderMain",libraries=Array.Empty<object>(),time="2024-08-08T00:00:00Z",releaseTime="2024-08-08T00:00:00Z"});return Task.CompletedTask;
        }
    }
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
}
