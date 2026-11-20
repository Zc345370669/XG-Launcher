// SPDX-License-Identifier: MIT
using XG.Core;
internal static class Phase16Tests {
 internal static async Task RunAsync(string root) {
  var complete=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var calls=0;
  async Task Download(){Interlocked.Increment(ref calls);await complete.Task;}
  using var cancelled=new CancellationTokenSource();var a=SharedResourceCoordinator.RunAsync(root,Download,cancelled.Token);var b=SharedResourceCoordinator.RunAsync(root,Download);
  cancelled.Cancel();try{await a;throw new Exception("cancel ignored");}catch(OperationCanceledException){}
  complete.SetResult();await b;if(calls!=1)throw new Exception("shared request duplicated");
  var source=Path.Combine(root,"migration-source");var target=Path.Combine(root,"migration-target");Directory.CreateDirectory(source);await File.WriteAllTextAsync(Path.Combine(source,"save.dat"),"keep this save");
  await VerifiedDirectoryMigration.CopyVerifiedAsync(source,target);
  if(!File.Exists(Path.Combine(source,"save.dat"))||await File.ReadAllTextAsync(Path.Combine(target,"save.dat"))!="keep this save")throw new Exception("migration data lost");
  try{await VerifiedDirectoryMigration.CopyVerifiedAsync(source,target);throw new Exception("overwrite allowed");}catch(IOException){}
  using var stop=new CancellationTokenSource();stop.Cancel();var aborted=Path.Combine(root,"migration-aborted");
  try{await VerifiedDirectoryMigration.CopyVerifiedAsync(source,aborted,token:stop.Token);throw new Exception("cancel ignored");}catch(OperationCanceledException){}
  if(Directory.Exists(aborted)||!File.Exists(Path.Combine(source,"save.dat")))throw new Exception("cancel modified source");
  Console.WriteLine("PASS phase16 shared operation deduplication and waiter cancellation isolation; verified copy preserves source, rejects overwrite and rolls back cancellation");
 }
}
