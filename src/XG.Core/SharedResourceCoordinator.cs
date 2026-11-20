// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
namespace XG.Core;
/// <summary>
/// 按资源身份共享进行中的任务；取消等待者不等于取消共享下载。
/// </summary>
public static class SharedResourceCoordinator {
 static readonly ConcurrentDictionary<string,Lazy<Task>> active=new(StringComparer.Ordinal);
 public static int ActiveCount => active.Count(x=>x.Value.IsValueCreated && !x.Value.Value.IsCompleted);
 public static async Task RunAsync(string identity,Func<Task> operation,CancellationToken waiter=default) {
  waiter.ThrowIfCancellationRequested();
  var job=active.GetOrAdd(identity,_=>new Lazy<Task>(async () => await operation().ConfigureAwait(false),LazyThreadSafetyMode.ExecutionAndPublication));
  var task=job.Value;
  _=task.ContinueWith(completed=>{if(completed.IsFaulted)_=completed.Exception;active.TryRemove(new KeyValuePair<string,Lazy<Task>>(identity,job));},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
  // 等待者自己的取消令牌不传给共享operation，防止一个界面操作终止其他任务的共同资源。
  await task.WaitAsync(waiter);
 }
}
