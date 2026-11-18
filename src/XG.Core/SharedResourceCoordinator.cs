// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
namespace XG.Core;
public static class SharedResourceCoordinator {
 static readonly ConcurrentDictionary<string,Lazy<Task>> active=new(StringComparer.Ordinal);
 public static int ActiveCount => active.Count(x=>x.Value.IsValueCreated && !x.Value.Value.IsCompleted);
 public static async Task RunAsync(string identity,Func<Task> operation,CancellationToken waiter=default) {
  waiter.ThrowIfCancellationRequested();
  var job=active.GetOrAdd(identity,_=>new Lazy<Task>(async () => await operation().ConfigureAwait(false),LazyThreadSafetyMode.ExecutionAndPublication));
  var task=job.Value;
  _=task.ContinueWith(completed=>{if(completed.IsFaulted)_=completed.Exception;active.TryRemove(new KeyValuePair<string,Lazy<Task>>(identity,job));},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
  await task.WaitAsync(waiter);
 }
}
