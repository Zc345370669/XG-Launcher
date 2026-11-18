// SPDX-License-Identifier: MIT
using System.Security.Cryptography;
namespace XG.Core;
public sealed record MigrationFile(string RelativePath,long Bytes);
public static class VerifiedDirectoryMigration {
 public static IReadOnlyList<MigrationFile> Preview(string source,string destination) {
  source=Path.GetFullPath(source);destination=Path.GetFullPath(destination);
  if(!Directory.Exists(source)||Directory.Exists(destination)||File.Exists(destination))throw new IOException("Migration requires an existing source and a new destination.");
  if(destination.StartsWith(source+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||source.StartsWith(destination+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Migration paths overlap.");
  var list=new List<MigrationFile>();var pending=new Stack<string>();pending.Push(source);
  while(pending.TryPop(out var dir)) {
   if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)throw new IOException("Migration does not follow links.");
   foreach(var file in Directory.EnumerateFiles(dir)){if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Migration does not follow links.");list.Add(new(Path.GetRelativePath(source,file),new FileInfo(file).Length));}
   foreach(var child in Directory.EnumerateDirectories(dir))pending.Push(child);
  }
  return list;
 }
 public static async Task CopyVerifiedAsync(string source,string destination,IProgress<MigrationFile>? progress=null,CancellationToken token=default) {
  var plan=Preview(source,destination);source=Path.GetFullPath(source);destination=Path.GetFullPath(destination);
  var parent=Path.GetDirectoryName(destination)!;Directory.CreateDirectory(parent);
  for(var d=new DirectoryInfo(parent);d!=null;d=d.Parent)if((d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Destination links are not supported.");
  if(new DriveInfo(Path.GetPathRoot(parent)!).AvailableFreeSpace<plan.Sum(x=>x.Bytes))throw new IOException("Insufficient migration space.");
  var staging=destination+".migration-"+Guid.NewGuid().ToString("N");Directory.CreateDirectory(staging);
  try {
   foreach(var item in plan) {
    token.ThrowIfCancellationRequested();var from=Path.Combine(source,item.RelativePath);var to=Path.Combine(staging,item.RelativePath);
    if((File.GetAttributes(from)&FileAttributes.ReparsePoint)!=0)throw new IOException("Source changed to a link.");
    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
    await using(var input=new FileStream(from,FileMode.Open,FileAccess.Read,FileShare.Read))await using(var output=new FileStream(to,FileMode.CreateNew,FileAccess.Write,FileShare.None))await input.CopyToAsync(output,token);
    await using var original=File.OpenRead(from);await using var copied=File.OpenRead(to);
    var originalHash = await SHA256.HashDataAsync(original,token); var copiedHash = await SHA256.HashDataAsync(copied,token);
    if(!CryptographicOperations.FixedTimeEquals(originalHash,copiedHash))throw new IOException("Migration verification failed.");
    progress?.Report(item);
   }
   token.ThrowIfCancellationRequested();Directory.Move(staging,destination);
  }finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
 }
}
