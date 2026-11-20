// SPDX-License-Identifier: MIT
using System.Text.Json;
namespace XG.Core;
public static class WorkspaceMigration {
 static readonly string[] ManagedFolders=["data","minecraft","runtimes","downloads","backups","logs","licenses"];
 public static IReadOnlyList<MigrationFile> Preview(string source,string destination) {
  source=Path.GetFullPath(source);destination=Path.GetFullPath(destination);
  if(Directory.Exists(destination)||File.Exists(destination)||destination.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||source.StartsWith(destination+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Migration destination exists or overlaps source.");
  var files=new List<MigrationFile>();
  foreach(var folder in ManagedFolders)if(Directory.Exists(Path.Combine(source,folder)))files.AddRange(VerifiedDirectoryMigration.Preview(Path.Combine(source,folder),Path.Combine(destination,folder)).Select(x=>x with {RelativePath=Path.Combine(folder,x.RelativePath)}));
  var legacy=Path.Combine(source,"minecraft","runtimes");
  if(Directory.Exists(legacy)&&!File.Exists(Path.Combine(source,"data","runtime-layout-v2"))) {
   if(Directory.Exists(Path.Combine(source,"runtimes")))throw new IOException("Legacy and new runtime directories conflict. Source is unchanged.");
   files.AddRange(VerifiedDirectoryMigration.Preview(legacy,Path.Combine(destination,"runtimes")).Select(x=>x with {RelativePath=Path.Combine("runtimes",x.RelativePath)}));
  }
  return files;
 }
 public sealed record Journal(string Source,string Destination,string State,string? Staging,string? Error);
 public static Journal? ReadJournal(string destination) {
  var file=Path.GetFullPath(destination)+".migration.json";
  if(!File.Exists(file))return null;
  try{return JsonSerializer.Deserialize<Journal>(File.ReadAllText(file)) ?? new Journal("",Path.GetFullPath(destination),"Unreadable",null,"Invalid journal");}
  catch(Exception error)when(error is IOException or JsonException){return new Journal("",Path.GetFullPath(destination),"Unreadable",null,"Invalid or inaccessible journal");}
 }
 public static async Task MigrateAsync(string source,string destination,IProgress<MigrationFile>? progress=null,CancellationToken token=default) {
  _=Preview(source,destination);source=Path.GetFullPath(source);destination=Path.GetFullPath(destination);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
  var previous=ReadJournal(destination);if(previous!=null&&(!string.Equals(previous.Source,source,StringComparison.Ordinal)||!string.Equals(previous.Destination,destination,StringComparison.Ordinal)))throw new IOException("Migration journal conflict; choose another destination. Existing file retained.");
  string? staging=null;
  void Save(string state,string? error=null)=>InstanceStore.WriteJson(destination+".migration.json",new Journal(source,destination,state,staging,error));
  Save("Checking");
  try {await ExecuteMigrationAsync(source,destination,progress,token,path=>{staging=path;Save("Copying");});Save("CopiedVerified");}
  catch(OperationCanceledException){Save("Cancelled");throw;}
  catch(Exception error){Save("Failed",new GameLogSanitizer(null).Redact(error.Message));throw;}
 }
 private static async Task ExecuteMigrationAsync(string source,string destination,IProgress<MigrationFile>? progress,CancellationToken token,Action<string> reportStaging) {
  source=Path.GetFullPath(source); destination=Path.GetFullPath(destination);
  var plan=Preview(source,destination);
  var finalDestination=destination;var staging=destination+".workspace-"+Guid.NewGuid().ToString("N");
  reportStaging(staging);
  var parent=Path.GetDirectoryName(destination)!;Directory.CreateDirectory(parent);
  if(new DriveInfo(Path.GetPathRoot(parent)!).AvailableFreeSpace<plan.Sum(x=>x.Bytes))throw new IOException("Insufficient migration space.");
  var oldStore=new InstanceStore(Path.Combine(source,"data"),Path.Combine(source,"minecraft"));
  var states=new Dictionary<Guid,string>();
  foreach(var instance in oldStore.Entries) states[instance.Id]=await InstallationReceipt.InspectAsync(oldStore,instance,token,true);
  Directory.CreateDirectory(staging);
  foreach(var folder in ManagedFolders)if(Directory.Exists(Path.Combine(source,folder)))await VerifiedDirectoryMigration.CopyVerifiedAsync(Path.Combine(source,folder),Path.Combine(staging,folder),progress,token);
  destination=staging;
  try {
   var newData=Path.Combine(destination,"data");var migrated=new InstanceStore(newData,Path.Combine(destination,"minecraft"));
   var oldManaged=oldStore.ManagedJavaRoot;var oldManagedRelative=Path.GetRelativePath(source,oldManaged);
   var copiedManaged=Path.Combine(destination,oldManagedRelative);var targetManaged=Path.Combine(destination,"runtimes");
   if(!Path.GetFullPath(copiedManaged).Equals(Path.GetFullPath(targetManaged),StringComparison.OrdinalIgnoreCase)&&Directory.Exists(copiedManaged)) {
    await VerifiedDirectoryMigration.CopyVerifiedAsync(copiedManaged,targetManaged,progress,token);
    File.WriteAllText(Path.Combine(newData,"runtime-layout-v2"),"runtimes");
   }
   string Rewrite(string path) {
    if(path.StartsWith("portable:",StringComparison.Ordinal)) {
     var prefix="portable:"+oldManagedRelative.Replace('\\','/').TrimEnd('/')+"/";
     return path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)?"portable:runtimes/"+path[prefix.Length..]:path;
    }
    var full=Path.IsPathFullyQualified(path)?Path.GetFullPath(path):null;
    if(full!=null&&full.StartsWith(oldManaged+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return Path.Combine(finalDestination,"runtimes",Path.GetRelativePath(oldManaged,full));
    var normalized=path.Replace('\\','/');var root=oldManagedRelative.Replace('\\','/').TrimEnd('/')+"/";
    return normalized.StartsWith(root,StringComparison.OrdinalIgnoreCase)?"runtimes/"+normalized[root.Length..]:path;
   }
   string ReceiptPath(string value) { var rewritten=Rewrite(value); return Path.IsPathFullyQualified(rewritten)&&rewritten.StartsWith(finalDestination+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)?Path.GetRelativePath(finalDestination,rewritten):rewritten; }
   foreach(var instance in migrated.Entries.ToArray()) {
    token.ThrowIfCancellationRequested();var updated=instance with {Java=instance.Java==null?null:Rewrite(instance.Java)};migrated.Save(updated);
    var receiptFile=Path.Combine(migrated.DirectoryFor(updated),".xg-installation.json");
    if(File.Exists(receiptFile)) {
     var receipt=JsonSerializer.Deserialize<InstallationReceipt.Receipt>(File.ReadAllText(receiptFile))??throw new InvalidDataException("Invalid receipt");
     InstanceStore.WriteJson(receiptFile,receipt with {Configuration=InstallationReceipt.Configuration(updated),Java=receipt.Java==null?null:ReceiptPath(receipt.Java),Files=receipt.Files.Select(f=>f with {Path=ReceiptPath(f.Path)}).ToArray()});
    }
   }
   foreach(var filename in new[]{"java-catalog.json","java-bindings.json"}) {
    var file=Path.Combine(newData,filename);if(!File.Exists(file))continue;
    if(filename=="java-catalog.json")InstanceStore.WriteJson(file,(JsonSerializer.Deserialize<string[]>(File.ReadAllText(file))??[]).Select(Rewrite).ToArray());
    else InstanceStore.WriteJson(file,(JsonSerializer.Deserialize<Dictionary<Guid,string>>(File.ReadAllText(file))??[]).ToDictionary(x=>x.Key,x=>Rewrite(x.Value)));
   }
   foreach(var instance in migrated.Entries)if(states[instance.Id]=="已安装"&&await InstallationReceipt.InspectAsync(migrated,instance,token,true)!="已安装")throw new IOException("迁移后安装验证失败，未切换目录。");
   token.ThrowIfCancellationRequested();
   Directory.Move(staging,finalDestination);
  }catch { // Destination is a new private copy; keep it for diagnostics. Source and live preferences remain untouched.
   throw;
  }
 }
}
