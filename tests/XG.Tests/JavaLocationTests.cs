// SPDX-License-Identifier: MIT
using XG.Core;
using CmlLib.Core.Files;
using CmlLib.Core.Tasks;

internal static class JavaLocationTests
{
    internal static async Task RunAsync(string root)
    {
        var profile = Path.Combine(root, "system-user", "AppData", "Local");
        var first = ManagedJavaLocation.Resolve(true, profile, Path.Combine(root, "exe-on-D", "runtimes"));
        var second = ManagedJavaLocation.Resolve(true, profile, Path.Combine(root, "exe-on-E", "runtimes"));
        if (first != second || first != Path.Combine(profile, "Eclipse Adoptium")) throw new Exception("Windows Java install target depends on EXE location");
        try { ManagedJavaLocation.Resolve(true, "", root); throw new Exception("missing system directory silently used portable root"); } catch (DirectoryNotFoundException) { }
        var cache = Path.Combine(root, "exe-on-D", "data", "downloads", "java-installers");
        var java = Path.Combine(first, "home", "bin", "java.exe"); Directory.CreateDirectory(Path.GetDirectoryName(java)!);
        await File.WriteAllTextAsync(java, "verified installed Java fixture");
        var archive = new GameFile("jre.lzma") { Path = "unused", Url = "https://example.invalid/java", UpdateTask = [new LegacyJavaExtractionTask(Path.Combine(first, "home"))] };
        var owned = JavaInstallerCleanup.OwnLegacyPackages([archive], first, cache).Single();
        if (!owned.Path!.StartsWith(cache + Path.DirectorySeparatorChar) || owned.Path.StartsWith(first + Path.DirectorySeparatorChar)) throw new Exception("Java installer archive stored at installed-runtime location");
        Directory.CreateDirectory(Path.GetDirectoryName(owned.Path)!); File.WriteAllText(owned.Path, "installer fixture"); File.WriteAllText(owned.Path + ".identity.json", "fixture");
        var keep = Path.Combine(cache, "unrelated.zip"); File.WriteAllText(keep, "unowned");
        if (await JavaInstallerCleanup.AfterVerifiedAsync(first, java + ".missing", [owned], installerCache: cache) != 0 || !File.Exists(owned.Path)) throw new Exception("failed install lost recovery archive");
        if (await JavaInstallerCleanup.AfterVerifiedAsync(first, java, [owned], installerCache: cache) != 1 || File.Exists(owned.Path) || File.Exists(owned.Path + ".identity.json") || !File.Exists(java) || !File.Exists(keep)) throw new Exception("cross-root installer cleanup lost runtime or unowned data");
        var raw = new GameFile("java.exe") { Path = java, Url = "https://example.invalid/java-file" };
        var temporary = JavaInstallerCleanup.TransferPart(raw, first, cache);
        if (!temporary.StartsWith(cache + Path.DirectorySeparatorChar) || JavaInstallerCleanup.TransferPart(raw, first, cache) != temporary) throw new Exception("manifest Java download is not in stable EXE cache");
        var store = new InstanceStore(Path.Combine(root,"manual-java","data"), javaInstallerCacheRoot: cache);
        var instance = new Instance(Guid.NewGuid(),"Manual Java fixture","1.21.1",Java:java); store.Save(instance); store.BindJava(instance.Id,java);
        if (store.ResolveJavaPath(store.Entries.Single().Java) != java || store.JavaBinding(instance.Id) != java || store.JavaInstallerCacheRoot != cache) throw new Exception("manual installed-Java selection rewritten as install destination");
        Console.WriteLine("PASS Windows vendor discovery root independent of EXE, separate stable EXE installer/manifest cache, verified-success cleanup, failed/unowned preservation and unchanged manual installed-Java selection");
    }
}
