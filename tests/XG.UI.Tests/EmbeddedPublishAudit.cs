// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using XG.Core;

internal static class EmbeddedPublishAudit
{
    internal static async Task RunAsync(string assemblyPath)
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
        var root = Path.Combine(Path.GetTempPath(), "xg-update-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var updater = Path.Combine(root, "XGUpdater.exe");
            helper.Position = 0;
            await using (var output = File.Create(updater)) await helper.CopyToAsync(output);
            var install = Path.Combine(root, "install"); Directory.CreateDirectory(install);
            var executable = Path.Combine(install, "XGLauncher.exe");
            File.Copy(updater, executable);
            var userFile = Path.Combine(install, "saves.keep"); await File.WriteAllTextAsync(userFile, "user data fixture");
            var archive = Path.Combine(root, "update.zip");
            using (var payload = ZipFile.Open(archive, ZipArchiveMode.Create)) payload.CreateEntryFromFile(updater, "XG-Launcher/XGLauncher.exe");
            using var stream = File.OpenRead(archive);
            var digest = Convert.ToHexString(SHA256.HashData(stream)); stream.Close();
            var plan = Path.Combine(root, "apply.json");
            await File.WriteAllTextAsync(plan, JsonSerializer.Serialize(new UpdateApplyRequest(archive, digest, install, int.MaxValue, 0)));
            var start = new ProcessStartInfo(updater) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(plan);
            using var process = Process.Start(start) ?? throw new Exception("Updater failed to start");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            if (process.ExitCode != 0 || await File.ReadAllTextAsync(plan + ".result") != "Updated" ||
                await File.ReadAllTextAsync(userFile) != "user data fixture") throw new Exception("Real updater process failed");
            Console.WriteLine("PASS Windows native single-file updater process applies verified package and starts replacement without touching user file (headless process test, not desktop GUI)");
        }
        finally
        {
            // The replacement fixture is the updater itself with no arguments and immediately exits.
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try { Directory.Delete(root, true); break; }
                catch (IOException) when (attempt < 19) { await Task.Delay(100); }
                catch (UnauthorizedAccessException) when (attempt < 19) { await Task.Delay(100); }
            }
        }
    }
}
