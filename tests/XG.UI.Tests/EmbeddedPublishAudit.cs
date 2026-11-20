// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using XG.Core;

internal static class EmbeddedPublishAudit
{
    internal static async Task RunAsync(string assemblyPath, string? probePath = null)
    {
        var assembly = Assembly.LoadFile(Path.GetFullPath(assemblyPath));
        using var compliance = assembly.GetManifestResourceStream("XG.Compliance.zip") ?? throw new Exception("Missing compliance resource");
        using var zip = new ZipArchive(compliance);
        foreach (var name in new[] { "LICENSE", "NOTICE.md", "licenses/Lucide-LICENSE.txt", "THIRD-PARTY-NOTICES.md", "third-party/native-sources/libgifcodec-d06d2a6d.tar.gz" })
        {
            using var embedded = zip.GetEntry(name)?.Open() ?? throw new Exception("Missing compliance material: " + name);
            using var original = File.OpenRead(name);
            if (!SHA256.HashData(embedded).SequenceEqual(SHA256.HashData(original))) throw new Exception("Changed upstream bytes: " + name);
        }
        using var helper = assembly.GetManifestResourceStream("XG.EmbeddedUpdater.exe") ?? throw new Exception("Missing updater");
        if (helper.Length < 1024 * 1024 || helper.ReadByte() != 'M' || helper.ReadByte() != 'Z') throw new Exception("Updater is not a published Windows EXE");
        Console.WriteLine("PASS actual publish embeds updater, complete MIT/ISC/third-party notices and byte-identical native corresponding-source archive");
        if (!OperatingSystem.IsWindows()) return;
        if (probePath == null) throw new Exception("Windows update audit requires the separate restart probe");
        var root = Path.Combine(Path.GetTempPath(), "xg-update-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var updater = Path.Combine(root, "XGUpdater.exe");
            helper.Position = 0;
            await using (var output = File.Create(updater)) await helper.CopyToAsync(output);
            var errors = new List<Exception>();
            foreach (var scenario in new[] { "success", "checksum", "launch-failure", "locked", "parent", "user-data", "unicode", "denied", "interrupted" })
                try { await ScenarioAsync(scenario, updater, Path.GetFullPath(probePath), root); }
                catch (Exception error) { errors.Add(new Exception("Updater scenario: " + scenario, error)); }
            if (errors.Count != 0) throw new AggregateException(errors);
        }
        finally
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                try { Directory.Delete(root, true); break; }
                catch (IOException) when (attempt < 49) { await Task.Delay(100); }
                catch (UnauthorizedAccessException) when (attempt < 49) { await Task.Delay(100); }
            }
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task ScenarioAsync(string scenario, string updater, string probe, string root)
    {
        var folder = Path.Combine(root, scenario == "unicode" ? "中文 路径 ' & 测试" : scenario); Directory.CreateDirectory(folder);
        var install = Path.Combine(folder, "install"); Directory.CreateDirectory(install);
        var executable = Path.Combine(install, "XGLauncher.exe");
        var success = scenario is "success" or "parent" or "unicode" or "interrupted";
        File.Copy(success || scenario == "denied" ? updater : probe, executable);
        var original = await File.ReadAllBytesAsync(executable);
        var preserved = new[] { "minecraft/versions/Test/saves/world.keep", "minecraft/versions/Test/mods/mod.keep",
            "data/settings.keep", "java/runtime.keep" };
        foreach (var name in preserved)
        {
            var file = Path.Combine(install, name); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "user fixture");
        }
        var stage = Path.Combine(folder, "updates", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        var archive = Path.Combine(stage, "update.zip");
        using (var payload = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            var entry = payload.CreateEntry("XG-Launcher/XGLauncher.exe");
            using (var output = entry.Open())
            {
                if (scenario == "launch-failure")
                {
                    // 通过结构检查的 PE 头仍不能保证 Windows 能执行；必须由真实进程启动验证回滚。
                    var invalid = new byte[128]; invalid[0] = 0x4d; invalid[1] = 0x5a; invalid[0x3c] = 0x40;
                    invalid[0x40] = 0x50; invalid[0x41] = 0x45; invalid[0x44] = 0x64; invalid[0x45] = 0x86; output.Write(invalid);
                }
                else { using var input = File.OpenRead(probe); input.CopyTo(output); }
            }
            if (scenario == "user-data") using (var output = new StreamWriter(payload.CreateEntry("data/settings.keep").Open())) output.Write("forbidden");
        }
        string digest;
        using (var input = File.OpenRead(archive)) digest = Convert.ToHexString(SHA256.HashData(input));
        if (scenario == "checksum") digest = new string('0', 64);
        Process? parent = null;
        FileStream? locked = null;
        DirectorySecurity? originalPermissions = null;
        var release = Path.Combine(folder, "release.marker");
        try
        {
            if (scenario is "parent" or "interrupted")
            {
                var ready = Path.Combine(folder, "parent.marker");
                var start = new ProcessStartInfo(probe) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--hold-parent"); start.ArgumentList.Add(ready); start.ArgumentList.Add(release);
                parent = Process.Start(start) ?? throw new Exception("Parent fixture did not start");
                await UntilAsync(() => File.Exists(ready));
            }
            if (scenario == "locked") locked = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (scenario == "denied")
            {
                var directory = new DirectoryInfo(install);
                originalPermissions = directory.GetAccessControl();
                var restricted = directory.GetAccessControl();
                restricted.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.Write | FileSystemRights.Delete,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny));
                directory.SetAccessControl(restricted);
            }
            var plan = Path.Combine(stage, "apply.json");
            await File.WriteAllTextAsync(plan, JsonSerializer.Serialize(new UpdateApplyRequest(archive, digest, install,
                parent?.Id ?? int.MaxValue, parent?.StartTime.ToUniversalTime().Ticks ?? 0)));
            var startHelper = new ProcessStartInfo(updater) { UseShellExecute = false, CreateNoWindow = true };
            startHelper.ArgumentList.Add("--apply-update"); startHelper.ArgumentList.Add(plan);
            using var process = Process.Start(startHelper) ?? throw new Exception("Updater failed to start");
            if (parent != null)
            {
                await UntilAsync(() => File.Exists(plan + ".status") && File.ReadAllText(plan + ".status") == "WaitingForExit");
                if (process.HasExited || parent.HasExited || !original.SequenceEqual(File.ReadAllBytes(executable)))
                    throw new Exception("Updater replaced executable before original process exited");
                if (scenario == "interrupted")
                {
                    process.Kill(true); await process.WaitForExitAsync();
                    if (!original.SequenceEqual(File.ReadAllBytes(executable))) throw new Exception("Interrupted wait changed original executable");
                }
                await File.WriteAllTextAsync(release, "Release"); await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            int exitCode = process.ExitCode;
            if (scenario == "interrupted")
            {
                using var retry = Process.Start(startHelper) ?? throw new Exception("Interrupted updater retry did not start");
                await retry.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); exitCode = retry.ExitCode;
            }
            var outcome = UpdateReceipt.Read(Path.Combine(folder, "updates"), plan + ".result");
            if (success)
            {
                if (exitCode != 0 || outcome != UpdateOutcome.Updated ||
                    !File.ReadAllBytes(probe).SequenceEqual(File.ReadAllBytes(executable))) throw new Exception("Update did not commit replacement");
            }
            else
            {
                var expected = scenario switch { "checksum" or "user-data" => UpdateOutcome.InvalidPackage,
                    "launch-failure" => UpdateOutcome.LaunchFailed, "denied" => UpdateOutcome.AccessDenied, _ => UpdateOutcome.FilesBusy };
                // Windows 删除共享被拒时可能返回 ERROR_ACCESS_DENIED，而不是 SHARING_VIOLATION。
                // 两种结果都必须保持原 EXE 完整，不能因此放宽文件恢复断言。
                var expectedOutcome = scenario == "locked" ? outcome is UpdateOutcome.FilesBusy or UpdateOutcome.AccessDenied : outcome == expected;
                if (exitCode != 1 || !expectedOutcome || !original.SequenceEqual(File.ReadAllBytes(executable)))
                    throw new Exception("Failed update did not preserve/restore original: " + scenario + "/" + outcome);
            }
            if (scenario is not ("locked" or "denied")) await UntilAsync(() => File.Exists(Path.Combine(install, "launched.marker")));
            foreach (var name in preserved) if (await File.ReadAllTextAsync(Path.Combine(install, name)) != "user fixture") throw new Exception("Updater modified user data");
            Console.WriteLine("PASS Windows actual updater/restart probe: " + scenario + "; outcome=" + outcome + "; user data preserved (process test, not desktop GUI)");
        }
        finally
        {
            if (originalPermissions != null) new DirectoryInfo(install).SetAccessControl(originalPermissions);
            locked?.Dispose();
            if (parent != null) { await File.WriteAllTextAsync(release, "Release"); await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); parent.Dispose(); }
        }
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            try { if (condition()) return; } catch (IOException) { }
            await Task.Delay(25, timeout.Token);
        }
    }
}
