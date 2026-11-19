// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
namespace XG.Core;
// 共享运行时根按提交门控串行化；获得准入后仍需重新探测实际所需运行时，
// 不能假定所有Java 21安装包内容相同。
/// <summary>
/// 按规范化运行时根目录串行化提交，调用方持有的租约必须释放。
/// </summary>
public static class JavaInstallGate {
 static readonly ConcurrentDictionary<string,SemaphoreSlim> gates=new(StringComparer.OrdinalIgnoreCase);
 public static async Task<IDisposable> EnterAsync(string runtimeRoot,CancellationToken token=default) {
  var gate=gates.GetOrAdd(Path.GetFullPath(runtimeRoot),_=>new(1));await gate.WaitAsync(token);return new Lease(gate);
 }
 sealed class Lease(SemaphoreSlim gate):IDisposable {int disposed;public void Dispose(){if(Interlocked.Exchange(ref disposed,1)==0)gate.Release();}}
}
