// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Text.Json;
namespace XG.Core;
public sealed record ImportInspection(string Root,string? Version,string Loader,string? LoaderVersion,string Format);
public static class GameImport {
 public static ImportInspection InspectDirectory(string source) {
  source=Path.GetFullPath(source);if(!Directory.Exists(source))throw new DirectoryNotFoundException(source);var root=new[]{Path.Combine(source,".minecraft"),Path.Combine(source,"minecraft"),source}.First(Directory.Exists);
  var prism=Path.Combine(source,"mmc-pack.json");
  if(File.Exists(prism)) {
   using var doc=JsonDocument.Parse(File.ReadAllText(prism));var parts=doc.RootElement.GetProperty("components").EnumerateArray().ToArray();
   if(parts.Any(x=>x.GetProperty("uid").GetString() is not ("net.minecraft" or "org.lwjgl" or "org.lwjgl3" or "net.fabricmc.intermediary" or "org.quiltmc.hashed" or "net.fabricmc.fabric-loader" or "net.minecraftforge" or "net.neoforged" or "org.quiltmc.quilt-loader")))throw new InvalidDataException("Custom launcher components are unsupported. Provide a standard game directory and verify the configuration manually.");
   var game=parts.FirstOrDefault(x=>x.GetProperty("uid").GetString()=="net.minecraft");
   var loader=parts.FirstOrDefault(x=>x.GetProperty("uid").GetString() is "net.fabricmc.fabric-loader" or "net.minecraftforge" or "net.neoforged" or "org.quiltmc.quilt-loader");
   var uid=loader.ValueKind==JsonValueKind.Undefined?null:loader.GetProperty("uid").GetString();
   return new(root,game.ValueKind==JsonValueKind.Undefined?null:game.GetProperty("version").GetString(),uid switch {"net.fabricmc.fabric-loader"=>"Fabric","net.minecraftforge"=>"Forge","net.neoforged"=>"NeoForge","org.quiltmc.quilt-loader"=>"Quilt",_=>"原版"},uid==null?null:loader.GetProperty("version").GetString(),"Prism / MultiMC");
  }
  var versions=Path.Combine(root,"versions");var files=Directory.Exists(versions)?Directory.EnumerateDirectories(versions).SelectMany(x=>Directory.EnumerateFiles(x,"*.json",SearchOption.TopDirectoryOnly)).ToArray():Directory.GetFiles(root,"*.json");
  foreach(var file in files) {
   try {using var doc=JsonDocument.Parse(File.ReadAllText(file));var data=doc.RootElement;if(!data.TryGetProperty("id",out var id)||(!data.TryGetProperty("mainClass",out _)&&!data.TryGetProperty("inheritsFrom",out _)))continue;
    var version=data.TryGetProperty("inheritsFrom",out var parent)?parent.GetString():id.GetString();var kind="原版";string? build=null;
    if(data.TryGetProperty("libraries",out var libs))foreach(var lib in libs.EnumerateArray())if(lib.TryGetProperty("name",out var coordinate)) {
     var parts=(coordinate.GetString()??"").Split(':');if(parts.Length<3)continue;
     var found=parts[0] switch {"net.fabricmc" when parts[1]=="fabric-loader"=>"Fabric","org.quiltmc" when parts[1]=="quilt-loader"=>"Quilt","net.minecraftforge"=>"Forge","net.neoforged"=>"NeoForge",_=>null};if(found!=null){kind=found;build=parts[2];}
    }
    return new(root,version,kind,build,"Minecraft metadata");
   }catch(JsonException){}
  }
  return new(root,null,"原版",null,"Manual metadata required");
 }
 public static async Task ExtractZipAsync(string archive,string target,CancellationToken token) {
  using var zip=ZipFile.OpenRead(archive);if(zip.Entries.Count>50000||zip.Entries.Sum(x=>x.Length)>8L*1024*1024*1024)throw new InvalidDataException("Archive exceeds import limits.");
  if(Directory.Exists(target)||File.Exists(target))throw new IOException("Extraction target must not exist.");
  Directory.CreateDirectory(target);try {long total=0;
  foreach(var entry in zip.Entries) {
   token.ThrowIfCancellationRequested();var parts=entry.FullName.Replace('\\','/').Split('/',StringSplitOptions.RemoveEmptyEntries);
   if(parts.Length==0)continue;
   if(entry.FullName.StartsWith('/')||((entry.ExternalAttributes>>16)&0xF000)==0xA000||parts.Any(x=>x is "." or ".."||x.IndexOfAny("<>:\"|?*".ToCharArray())>=0||x.EndsWith('.')||x.EndsWith(' ')))throw new InvalidDataException("Unsafe archive path.");
   var path=parts.Aggregate(Path.GetFullPath(target),InstanceStore.SafeFile);
   if(entry.FullName.EndsWith('/')){Directory.CreateDirectory(path);continue;}
   if(entry.Length>2L*1024*1024*1024||entry.Length>Math.Max(1048576,entry.CompressedLength*300))throw new InvalidDataException("Unsafe archive expansion ratio.");
   Directory.CreateDirectory(Path.GetDirectoryName(path)!);await using var input=entry.Open();await using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
   var buffer=new byte[65536];long written=0;int count;while((count=await input.ReadAsync(buffer,token))>0){written+=count;total+=count;if(written>entry.Length||total>8L*1024*1024*1024)throw new InvalidDataException("Archive size mismatch.");await output.WriteAsync(buffer.AsMemory(0,count),token);}
  }
  }catch{if(Directory.Exists(target))Directory.Delete(target,true);throw;}
 }
 public static async Task<Instance> ImportDirectoryAsync(InstanceStore store,string source,string name,string version,string loader,string? build,IProgress<MigrationFile>? progress,CancellationToken token) {
  var instance=new Instance(Guid.NewGuid(),name,version,loader,LoaderVersion:build);instance.Validate();
  if(store.Entries.Any(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("An instance with this name exists; choose another name.");
  var destination=store.DirectoryFor(instance);
  try {await VerifiedDirectoryMigration.CopyVerifiedAsync(source,destination,progress,token);token.ThrowIfCancellationRequested();
   // Imported receipts must never assert readiness for a newly assigned configuration.
   File.Delete(Path.Combine(destination,".xg-installation.json"));store.Save(instance);return instance;
  }catch{if(Directory.Exists(destination))Directory.Delete(destination,true);throw;}
 }
}
