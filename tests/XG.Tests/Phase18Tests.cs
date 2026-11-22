// SPDX-License-Identifier: MIT
using System.IO.Compression;
using XG.Core;
internal static class Phase18Tests {
 internal static async Task RunAsync(string root) {
  var workspace=Path.Combine(root,"workspace18");var store=new InstanceStore(Path.Combine(workspace,"data"),Path.Combine(workspace,"minecraft"));
  var instance=new Instance(Guid.NewGuid(),"Migrating","1.21.1");store.Save(instance);var game=store.DirectoryFor(instance);Directory.CreateDirectory(Path.Combine(game,"saves","world"));await File.WriteAllTextAsync(Path.Combine(game,"saves","world","level.dat"),"save-content");
  var runtime=Path.Combine(workspace,"minecraft","runtimes","test","bin");Directory.CreateDirectory(runtime);await File.WriteAllTextAsync(Path.Combine(runtime,"java.exe"),"not an executable; migration fixture only");store.BindJava(instance.Id,Path.Combine(runtime,"java.exe"));
  var destination=Path.Combine(root,"workspace18-new");await WorkspaceMigration.MigrateAsync(workspace,destination);
  var migrated=new InstanceStore(Path.Combine(destination,"data"),Path.Combine(destination,"minecraft"));
  if(await File.ReadAllTextAsync(Path.Combine(migrated.DirectoryFor(instance),"saves","world","level.dat"))!="save-content"||!File.Exists(Path.Combine(game,"saves","world","level.dat")))throw new Exception("migration save lost");
  if(migrated.JavaBinding(instance.Id)!=Path.Combine(destination,"runtimes","test","bin","java.exe"))throw new Exception("runtime binding not rewritten");
  var source=Path.Combine(root,"prism18");Directory.CreateDirectory(Path.Combine(source,".minecraft","saves"));
  await File.WriteAllTextAsync(Path.Combine(source,"mmc-pack.json"),"""{"formatVersion":1,"components":[{"uid":"net.minecraft","version":"1.21.1"},{"uid":"net.fabricmc.fabric-loader","version":"0.16.5"}]}""");
  var identified=GameImport.InspectDirectory(source);if(identified.Version!="1.21.1"||identified.Loader!="Fabric")throw new Exception("Prism schema recognition failed");
  var imported=await GameImport.ImportDirectoryAsync(store,identified.Root,"Imported","1.21.1","Fabric","0.16.5",null,default);
  if(!store.Entries.Any(x=>x.Id==imported.Id)||!Directory.Exists(identified.Root))throw new Exception("import failed registration or changed source");
  try{await GameImport.ImportDirectoryAsync(store,identified.Root,"Imported","1.21.1","Fabric","0.16.5",null,default);throw new Exception("duplicate name accepted");}catch(InvalidDataException){}
  await SmartImportRegressionAsync(root,store);
  var zip=Path.Combine(root,"unsafe18.zip");using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)){using var output=new StreamWriter(archive.CreateEntry("../escape").Open());output.Write("bad");}
  try{await GameImport.ExtractZipAsync(zip,Path.Combine(root,"unsafe18"),default);throw new Exception("traversal accepted");}catch(InvalidDataException){}
  Console.WriteLine("PASS phase18 workspace migration preserves saves/source and rewrites managed Java binding; directory import registers isolated copy, protects name and rejects ZIP traversal (controlled schema fixtures)");
 }
 private static async Task SmartImportRegressionAsync(string root, InstanceStore store)
 {
  var game = Path.Combine(root, "smart-game");
  var versions = Path.Combine(game, "versions");
  Directory.CreateDirectory(game);
  await File.WriteAllTextAsync(Path.Combine(game, "launcher_profiles.json"), "{}");
  var vanilla = CreateVersionFolder("1.21.1");
  await File.WriteAllTextAsync(Path.Combine(vanilla, "1.21.1.json"),
   """{"id":"1.21.1","mainClass":"net.minecraft.client.main.Main","libraries":[]}""");
  await File.WriteAllTextAsync(Path.Combine(vanilla, "1.21.1.jar"), "fixture");
  await File.WriteAllTextAsync(Path.Combine(game, "unrelated.json"), "[{},1,null]");
  foreach (var (id, document) in new[] { ("array", "[{}]"), ("broken", "{"), ("string", "\"text\""), ("null", "null") })
   await File.WriteAllTextAsync(Path.Combine(CreateVersionFolder(id), id + ".json"), document);
  foreach (var (loader, coordinate, build) in new[]
  {
   ("Fabric","net.fabricmc:fabric-loader:0.16.5","0.16.5"),
   ("Forge","net.minecraftforge:forge:1.21.1-52.0.1","52.0.1"),
   ("NeoForge","net.neoforged:neoforge:21.1.10","21.1.10"),
   ("Quilt","org.quiltmc:quilt-loader:0.26.0","0.26.0")
  })
  {
   var folder = CreateVersionFolder(loader);
   await File.WriteAllTextAsync(Path.Combine(folder, loader + ".json"),
    $$"""{"id":"{{loader}}","inheritsFrom":"1.21.1","libraries":[null,[],{"name":null},{"name":"{{coordinate}}"}]}""");
   Directory.CreateDirectory(Path.Combine(folder,"saves"));
   await File.WriteAllTextAsync(Path.Combine(folder,"saves","fixture.dat"),loader);
  }
  var scan = GameImport.Scan(game);
  if (scan.Instances.Count != 5 || scan.Issues.Count != 4)
   throw new Exception($"Smart import did not isolate malformed JSON or identify all versions: {scan.Instances.Count}/{scan.Issues.Count}");
  if (GameImport.Scan(versions).Instances.Count != 5 || GameImport.Scan(vanilla).Instances.Single().Inspection.Version != "1.21.1")
   throw new Exception("different folder levels produced inconsistent scans");
  foreach (var candidate in scan.Instances)
  {
   var inspection = candidate.Inspection;
   if (inspection.Version != "1.21.1" || !candidate.Status.Contains("Libraries"))
    throw new Exception("base version inheritance or completeness status missing");
   var expected = inspection.Loader switch { "Forge" => "52.0.1", "NeoForge" => "21.1.10", "Quilt" => "0.26.0", "Fabric" => "0.16.5", _ => null };
   if (inspection.LoaderVersion != expected) throw new Exception("loader version incorrectly resolved");
   var instance = new Instance(Guid.NewGuid(), store.AvailableName("Minecraft " + inspection.Version + " " + inspection.Loader),
    inspection.Version, inspection.Loader, LoaderVersion: inspection.LoaderVersion);
   await GameImport.CopyCandidateAsync(store, instance, candidate, true, null, default);
   if (!store.Entries.Any(x => x.Id == instance.Id) || !File.Exists(Path.Combine(store.DirectoryFor(instance), candidate.Name + ".json")))
    throw new Exception("copy did not register or preserve the chosen profile");
  }
  const string pclId = "1.21.1-NeoForge_21.1.265";
  var pcl = CreateVersionFolder(pclId);
  Directory.CreateDirectory(Path.Combine(pcl, "PCL"));
  Directory.CreateDirectory(Path.Combine(pcl, "saves"));
  File.WriteAllText(Path.Combine(pcl, "PCL", "Setup.ini"), "VersionArgumentIndieV2:True");
  File.WriteAllText(Path.Combine(pcl, "saves", "fixture.dat"), "source save");
  File.WriteAllText(Path.Combine(pcl, pclId + ".json"), $$"""{"id":"{{pclId}}","mainClass":"fixture.Main"}""");
  var candidatePcl = GameImport.Scan(pcl).Instances.Single();
  if (candidatePcl.Inspection is not { Version: "1.21.1", Loader: "NeoForge", LoaderVersion: "21.1.265" })
   throw new Exception("Flattened PCL2 profile was misidentified");
  var relative = Path.Combine("assets", "indexes", "fixture.json");
  Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(game, relative))!);
  var shared = Path.Combine(store.SharedMinecraft!, relative);
  Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
  File.WriteAllText(Path.Combine(game, relative), "source");
  File.WriteAllText(shared, "target");
  var copy = new Instance(Guid.NewGuid(), "PCL copy", "1.21.1", "NeoForge", LoaderVersion: "21.1.265");
  var conflicts = new List<string>();
  await GameImport.CopyCandidateAsync(store, copy, candidatePcl, true, null, default, conflicts.Add);
  if (!store.Entries.Any(x => x.Id == copy.Id) || File.ReadAllText(shared) != "target" ||
      File.ReadAllText(Path.Combine(store.DirectoryFor(copy), "saves", "fixture.dat")) != "source save" ||
      !conflicts.Single().Contains(shared))
   throw new Exception("Shared conflict prevented copy or changed existing files");
  conflicts.Clear();
  var pack = new Instance(Guid.NewGuid(), "PCL pack copy", "1.21.1", "NeoForge", Pack: true, LoaderVersion: "21.1.265");
  await GameImport.CopyCandidateAsync(store, pack, candidatePcl, true, null, default, conflicts.Add);
  if (!store.Entries.Any(x => x.Id == pack.Id && x.Pack) || conflicts.Count != 1 ||
      File.ReadAllText(shared) != "target" ||
      File.ReadAllText(Path.Combine(store.DirectoryFor(pack), "saves", "fixture.dat")) != "source save")
   throw new Exception("Local pack copy did not preserve its data or registration after a shared conflict");
  File.Delete(Path.Combine(game, relative));
  Console.WriteLine("PASS PCL2 flattened NeoForge identification and copy through a shared-cache conflict");
  var fabric = scan.Instances.Single(x => x.Inspection.Loader == "Fabric");
  var reference = new Instance(Guid.NewGuid(), "Reference", "1.21.1", "Fabric", LoaderVersion: "0.16.5",
   ReferencedGameDirectory: fabric.Profile, ImportSource: fabric.Profile);
  store.Save(reference);
  var paths = new InstanceMinecraftPath(store, reference);
  if (paths.Runtime != store.LegacyManagedJavaRoot)
   throw new Exception("Game runtime writes must remain in the portable workspace.");
  if (OperatingSystem.IsWindows() && paths.Runtime == store.ManagedJavaRoot)
   throw new Exception("Game runtime writes must not target the official Java discovery directory.");
  if (Path.GetFullPath(paths.BasePath) != fabric.Profile || Path.GetDirectoryName(paths.GetVersionJsonPath(reference.Version)) != store.DirectoryFor(reference))
   throw new Exception("reference does not separate game data from internal metadata");
  store.Remove(reference, true);
  if (!File.Exists(Path.Combine(fabric.Profile,"saves","fixture.dat"))) throw new Exception("removing a reference deleted external data");
  if (!store.AvailableName("Minecraft 1.21.1 Fabric").EndsWith(" (2)", StringComparison.Ordinal))
   throw new Exception("duplicate names were not numbered");
  using var cancel = new CancellationTokenSource();
  cancel.Cancel();
  try { _ = GameImport.Scan(game, cancel.Token); throw new Exception("cancelled scan completed"); }
  catch (OperationCanceledException) { }
  Console.WriteLine("PASS smart import: mixed JSON kinds, malformed neighbors, five loader profiles, nested selection, inheritance, copy and reference isolation, completeness, names and cancellation");

  string CreateVersionFolder(string id)
  {
   var path = Path.Combine(versions, id);
   Directory.CreateDirectory(path);
   return path;
  }
 }

}
