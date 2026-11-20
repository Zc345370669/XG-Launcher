// SPDX-License-Identifier: MIT
using System.Security.Cryptography;
using XG.Core;
internal static class Phase17Tests {
 internal static async Task RunAsync(string root) {
  var bytes="shared trusted resource"u8.ToArray();var hash=Convert.ToHexString(SHA256.HashData(bytes));var handler=new Payload(bytes);
  var partial=Path.Combine(root,"identity-fixture.part");using(var http=new HttpClient(handler)){await Task.WhenAll(Enumerable.Range(0,3).Select(_=>VerifiedResourceDownload.StageAsync(http,new Uri("https://example.invalid/resource"),partial,bytes.Length,hash)));}
  if(handler.Count!=1||!File.Exists(partial+".identity.json"))throw new Exception("persistent shared download failed");
  using(var offline=new HttpClient(new Offline()))await VerifiedResourceDownload.StageAsync(offline,new Uri("https://example.invalid/resource"),partial,bytes.Length,hash);
  await File.WriteAllBytesAsync(partial,new byte[bytes.Length]);using(var http=new HttpClient(handler))await VerifiedResourceDownload.StageAsync(http,new Uri("https://example.invalid/resource"),partial,bytes.Length,hash);
  if(handler.Count!=2||!File.ReadAllBytes(partial).SequenceEqual(bytes))throw new Exception("corrupt resource not repaired");
  var store = new InstanceStore(Path.Combine(root,"java-binding-fixture")); var id=Guid.NewGuid(); store.BindJava(id,partial); var restored=new InstanceStore(store.Root); if(restored.JavaBinding(id)!=partial||!restored.CachedJavaPaths().Contains(partial))throw new Exception("Java binding persistence failed");
  Console.WriteLine("PASS phase17 real HTTP fixture concurrent transfer coalescing, persisted identity, offline verified reuse and same-size corruption repair");
 }
 sealed class Payload(byte[] bytes):HttpMessageHandler {public int Count;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Interlocked.Increment(ref Count);return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(bytes)});}}
 sealed class Offline:HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>throw new Exception("verified reuse accessed network");}
}
