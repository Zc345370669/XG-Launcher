// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
namespace XG.Core;
// Serializes commits to the shared runtime root. Each waiter re-probes the actual
// required runtime after admission; no assumption that all Java 21 packages match.
public static class JavaInstallGate {
 static readonly ConcurrentDictionary<string,SemaphoreSlim> gates=new(StringComparer.OrdinalIgnoreCase);
 public static async Task<IDisposable> EnterAsync(string runtimeRoot,CancellationToken token=default) {
  var gate=gates.GetOrAdd(Path.GetFullPath(runtimeRoot),_=>new(1));await gate.WaitAsync(token);return new Lease(gate);
 }
 sealed class Lease(SemaphoreSlim gate):IDisposable {int disposed;public void Dispose(){if(Interlocked.Exchange(ref disposed,1)==0)gate.Release();}}
}
