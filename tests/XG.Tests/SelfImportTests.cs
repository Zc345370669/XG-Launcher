// SPDX-License-Identifier: MIT
using XG.Core;
internal static class SelfImportTests {
 internal static void Run(string root) {
  var workspace=Path.Combine(root,"self-import");var store=new InstanceStore(Path.Combine(workspace,"data"),Path.Combine(workspace,".minecraft"));
  var instance=new Instance(Guid.NewGuid(),"My NeoForge","1.21.1","NeoForge",LoaderVersion:"21.1.10");var dir=store.DirectoryFor(instance);Directory.CreateDirectory(dir);store.Save(instance);
  File.WriteAllText(Path.Combine(dir,Path.GetFileName(dir)+".json"),"""{"id":"1.21.1-NeoForge","inheritsFrom":"1.21.1","libraries":[{"name":"net.neoforged:neoforge:21.1.10"}]}""");
  foreach(var selected in new[]{workspace,store.SharedMinecraft!,store.InstanceRoot,dir}) {var scan=GameImport.Scan(selected);if(!scan.Instances.Any(x=>x.Profile==dir&&x.Inspection.Loader=="NeoForge"))throw new Exception("self scan failed "+selected);}
  var scanRoot=GameImport.Scan(workspace);if(scanRoot.Instances.Count(x=>x.Profile==dir)!=1)throw new Exception("registered self duplicated");
  var recover=Path.Combine(store.InstanceRoot,"1.20.1");Directory.CreateDirectory(recover);File.WriteAllText(Path.Combine(recover,"1.20.1.json"),"""{"id":"1.20.1","mainClass":"fixture.Main","libraries":[]}""");
  if(!GameInstanceScan.Scan(store).Any(x=>x.Source==recover))throw new Exception("unregistered recovery missing");
  var recovered=new Instance(Guid.NewGuid(),"Recovered","1.20.1");File.WriteAllText(Path.Combine(recover,"options.txt"),"keep");store.AdoptDirectory(recovered,Path.GetFileName(recover));
  if(GameInstanceScan.Scan(store).Any(x=>x.Source==recover)||File.ReadAllText(Path.Combine(recover,"options.txt"))!="keep")throw new Exception("recovery duplicated or overwrote data");
  Console.WriteLine("PASS XG self root/data registry/.minecraft/versions/single-version scan, registered dedup and in-place unregistered recovery (no network or user-file overwrite)");
 }
}
