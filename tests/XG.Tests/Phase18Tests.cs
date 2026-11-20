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
  var zip=Path.Combine(root,"unsafe18.zip");using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)){using var output=new StreamWriter(archive.CreateEntry("../escape").Open());output.Write("bad");}
  try{await GameImport.ExtractZipAsync(zip,Path.Combine(root,"unsafe18"),default);throw new Exception("traversal accepted");}catch(InvalidDataException){}
  Console.WriteLine("PASS phase18 workspace migration preserves saves/source and rewrites managed Java binding; directory import registers isolated copy, protects name and rejects ZIP traversal (controlled schema fixtures)");
 }
}
