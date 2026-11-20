// SPDX-License-Identifier: MIT
using XG.Core;
internal static class Phase19Tests {
 internal static async Task RunAsync(string root) {
  var id="sync-fault-"+Guid.NewGuid();
  try{await SharedResourceCoordinator.RunAsync(id,()=>throw new IOException("fixture sync failure"));throw new Exception("failure hidden");}catch(IOException){}
  var runs=0;await SharedResourceCoordinator.RunAsync(id,()=>{runs++;return Task.CompletedTask;});if(runs!=1)throw new Exception("sync failure poisoned shared job");
  var source=Path.Combine(root,"journal-source");Directory.CreateDirectory(Path.Combine(source,"data"));Directory.CreateDirectory(Path.Combine(source,"minecraft"));await File.WriteAllTextAsync(Path.Combine(source,"data","keep.txt"),"original");
  var destination=Path.Combine(root,"journal-target");using var stopped=new CancellationTokenSource();stopped.Cancel();
  try{await WorkspaceMigration.MigrateAsync(source,destination,token:stopped.Token);throw new Exception("migration cancellation ignored");}catch(OperationCanceledException){}
  if(WorkspaceMigration.ReadJournal(destination)?.State!="Cancelled"||!File.Exists(Path.Combine(source,"data","keep.txt"))||Directory.Exists(destination))throw new Exception("cancel journal/source state incorrect");
  var malformed=Path.Combine(root,"journal-malformed");await File.WriteAllTextAsync(malformed+".migration.json","broken");if(WorkspaceMigration.ReadJournal(malformed)?.State!="Unreadable")throw new Exception("invalid journal not classified");
  try{await WorkspaceMigration.MigrateAsync(source,malformed);throw new Exception("unknown journal overwritten");}catch(IOException){}
  if(await File.ReadAllTextAsync(malformed+".migration.json")!="broken")throw new Exception("unknown journal changed");
  Console.WriteLine("PASS phase19 synchronous shared-worker failure is retryable; cancelled workspace migration persists diagnostic journal and preserves source/current destination");
 }
}
