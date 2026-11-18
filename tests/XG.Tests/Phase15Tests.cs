// SPDX-License-Identifier: MIT
using XG.Core;
internal static class Phase15Tests {
 internal static async Task RunAsync(string root) {
  using var first=await JavaInstallGate.EnterAsync(root);
  using var cancel=new CancellationTokenSource();
  var waiting=JavaInstallGate.EnterAsync(root,cancel.Token);
  if(waiting.IsCompleted)throw new Exception("shared commit admitted concurrently");
  cancel.Cancel();try{await waiting;throw new Exception("waiter cancellation ignored");}catch(OperationCanceledException){}
  first.Dispose();using var next=await JavaInstallGate.EnterAsync(root);
  var registry=await JavaDiscovery.DiscoverAsync(root);
  if(registry.Select(x=>x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=registry.Count)throw new Exception("duplicate runtime paths");
  using var stopped=new CancellationTokenSource();stopped.Cancel();
  try{await JavaDiscovery.DiscoverAsync(root,[root],stopped.Token,deep:true);throw new Exception("deep cancellation ignored");}catch(OperationCanceledException){}
  Console.WriteLine("PASS phase15 shared runtime commit serialization, cancelled waiter isolation, runtime discovery deduplication and deep-scan cancellation");
 }
}
