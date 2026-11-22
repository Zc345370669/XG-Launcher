// SPDX-License-Identifier: MIT
using System.IO.Compression;
namespace XG.Core;
public sealed record ImportInspection(string Root,string? Version,string Loader,string? LoaderVersion,string Format);
public static partial class GameImport {
 public static ImportInspection InspectDirectory(string source) {
  var scan = Scan(source);
  return scan.Instances.FirstOrDefault()?.Inspection
      ?? new(Path.GetFullPath(source), null, "原版", null, "需要手动配置");
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
 public static async Task<Instance> ImportDirectoryAsync(InstanceStore store,string source,string name,string version,string loader,string? build,IProgress<MigrationFile>? progress,CancellationToken token, Action<string>? sharedConflict = null, bool pack = false) {
  var instance=new Instance(Guid.NewGuid(),name,version,loader,Pack:pack,LoaderVersion:build,AutomaticMemory:true,AutomaticResolution:true);instance.Validate();
  if(store.Entries.Any(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("An instance with this name exists; choose another name.");
  var destination=store.DirectoryFor(instance);
  await IsolatedGameImport.CopyAsync(store,instance,source,destination,progress,token,sharedConflict: sharedConflict);
  return instance;
 }
}
