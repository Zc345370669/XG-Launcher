// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using XG.Core;

internal static class IntegratedDownloadTests
{
    private static readonly byte[] Payload = Enumerable.Range(0, 2048).Select(x => (byte)x).ToArray();
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(Payload));
    internal static async Task RunAsync(string root, bool benchmark = false)
    {
        var dir=Path.Combine(root,"integrated-download");Directory.CreateDirectory(dir);
        var measurements=new List<object>();
        foreach(var count in benchmark ? new[]{100,1000,4000}:new[]{100})
        foreach(var cap in benchmark ? new[]{1,4,8,16,32}:new[]{4,16})
        {
            using var handler=new Fixture();using var http=new HttpClient(handler);DownloadTuning.Configure(http,cap,false);
            var process=Process.GetCurrentProcess();var cpu=process.TotalProcessorTime;var watch=Stopwatch.StartNew();
            var paths=Enumerable.Range(0,count).Select(i=>Path.Combine(dir,$"{count}-{cap}-{i}.part")).ToArray();
            await Task.WhenAll(paths.Select(path=>VerifiedResourceDownload.StageAsync(http,new("https://fixture.invalid/file"),path,Payload.Length,Hash)));
            watch.Stop(); Check(handler.Peak<=cap&&handler.Peak>1== (cap>1),"global concurrency bound");
            foreach(var path in paths)Check(await VerifiedTransfer.MatchesAsync(path,Payload.Length,Hash,default),"checksum");
            measurements.Add(new{Files=count,Concurrency=cap,ElapsedMs=watch.Elapsed.TotalMilliseconds,BytesPerSecond=count*Payload.Length/watch.Elapsed.TotalSeconds,SuccessRate=1.0,Retries=DownloadTuning.Activity(http).Retries,CpuMs=(process.TotalProcessorTime-cpu).TotalMilliseconds,WorkingSetBytes=process.WorkingSet64,Peak=handler.Peak,Environment="controlled HttpMessageHandler, 2ms response latency; not public-service/Windows UI benchmark"});
            foreach(var path in paths)VerifiedTransfer.Reset(path);
        }
        using(var handler=new Fixture{Failures=2})using(var http=new HttpClient(handler)) {
            DownloadTuning.Configure(http,16,true);await VerifiedResourceDownload.StageAsync(http,new("https://fixture.invalid/retry"),Path.Combine(dir,"retry.part"),Payload.Length,Hash);
            Check(handler.Requests==3&&DownloadTuning.Activity(http).Retries==2&&DownloadTuning.Activity(http).Limit<=8,"5xx retry adaptive decrease");
        }
        using(var handler=new Fixture{Failures=1,Status=HttpStatusCode.TooManyRequests})using(var http=new HttpClient(handler)) {
            var watch=Stopwatch.StartNew();await VerifiedResourceDownload.StageAsync(http,new("https://fixture.invalid/429"),Path.Combine(dir,"429.part"),Payload.Length,Hash);
            Check(handler.Requests==2&&watch.Elapsed>=TimeSpan.FromMilliseconds(200),"Retry-After ignored");
        }
        foreach(var status in new[]{HttpStatusCode.Forbidden,HttpStatusCode.NotFound})using(var handler=new Fixture{Failures=10,Status=status})using(var http=new HttpClient(handler)) {
            try { await VerifiedResourceDownload.StageAsync(http,new("https://fixture.invalid/denied"),Path.Combine(dir,status+".part"),Payload.Length,Hash);throw new Exception("permanent status accepted"); }catch(HttpRequestException){}
            Check(handler.Requests==1,"permanent status retried");
        }
        using(var handler=new Fixture())using(var http=new HttpClient(handler)) {
            var path=Path.Combine(dir,"shared.part");await Task.WhenAll(Enumerable.Range(0,20).Select(_=>VerifiedResourceDownload.StageAsync(http,new("https://fixture.invalid/same"),path,Payload.Length,Hash)));
            Check(handler.Requests==1,"shared resource duplicate");
            var before=handler.Requests;await VerifiedResourceDownload.StageAsync(http,new("https://fixture.invalid/same"),path,Payload.Length,Hash);Check(handler.Requests==before,"verified local resource redownloaded");
        }
        // Actual sidecar identity and strong ETag; verify 206 append and ignored-Range 200 replacement.
        foreach(var ignore in new[]{false,true})using(var handler=new Fixture{IgnoreRange=ignore})using(var http=new HttpClient(handler)) {
            var path=Path.Combine(dir,"range-"+ignore+".part");var uri=new Uri("https://fixture.invalid/range");File.WriteAllBytes(path,Payload[..100]);
            File.WriteAllText(path+".etag","\"fixture\"");
            var identity=Path.GetFullPath(path)+"|"+uri.AbsoluteUri+"|"+Payload.Length+"|"+Hash;
            File.WriteAllText(path+".identity.json",JsonSerializer.Serialize(new{Identity=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity))),State="Pending"}));
            await VerifiedResourceDownload.StageAsync(http,uri,path,Payload.Length,Hash);Check(handler.SawRange&&await VerifiedTransfer.MatchesAsync(path,Payload.Length,Hash,default),"safe range restart");
        }
        using(var handler=new Fixture())using(var http=DownloadTuning.CreateHttpClient(handler)) {
            DownloadTuning.Configure(http,4,false);
            await Task.WhenAll(Enumerable.Range(0,80).Select(async i=> {using var response=await http.GetAsync("https://fixture.invalid/library/"+i,HttpCompletionOption.ResponseHeadersRead);await response.Content.ReadAsByteArrayAsync();}));
            Check(handler.Peak<=4&&handler.Requests==80,"third-party HTTP bypassed shared transport budget");
            await Task.WhenAll(Enumerable.Range(0,20).Select(async i=>{await using var input=await http.GetStreamAsync("https://fixture.invalid/stream/"+i);await input.CopyToAsync(Stream.Null);})).WaitAsync(TimeSpan.FromSeconds(5));
            Check(DownloadTuning.Activity(http).Active==0,"GetStreamAsync leaked transport permits");
        }
        using(var control=new ResourceControl(default,_=>{})) {
            var started=0;var resumed=0;
            var tasks=Enumerable.Range(0,16).Select(_=> {
                var attempt=0;
                return control.RunFileAsync(async token=> {if(Interlocked.Increment(ref attempt)==1){Interlocked.Increment(ref started);await Task.Delay(Timeout.Infinite,token);}else Interlocked.Increment(ref resumed);});
            }).ToArray();
            while(Volatile.Read(ref started)<16)await Task.Delay(1);
            control.Pause();await Task.Delay(30);Check(resumed==0,"paused workers restarted");control.Resume();await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));Check(resumed==16,"not all parallel transfers resumed");
        }
        using(var control=new ResourceControl(default,_=>{})) {
            var tasks=Enumerable.Range(0,8).Select(_=>control.RunFileAsync(token=>Task.Delay(Timeout.Infinite,token))).ToArray();control.Cancel();try{await Task.WhenAll(tasks);throw new Exception("cancel ignored");}catch(OperationCanceledException){}
        }
        var output=Environment.GetEnvironmentVariable("XG_PERFORMANCE_REPORT");if(output!=null)File.WriteAllText(output,JsonSerializer.Serialize(measurements,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS integrated global bounded transfers, actual verified bytes, adaptive 5xx backoff, Retry-After, 403/404 no retry, dedup/cache and strong ETag Range/200 fallback; controlled performance samples "+measurements.Count);
    }
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private sealed class Fixture:HttpMessageHandler {
        private int active,peak,requests;public int Peak=>peak;public int Requests=>requests;public int Failures;public HttpStatusCode Status=HttpStatusCode.ServiceUnavailable;public bool IgnoreRange,SawRange;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
            var running=Interlocked.Increment(ref active);int old;do{old=peak;if(old>=running)break;}while(Interlocked.CompareExchange(ref peak,running,old)!=old);
            var number=Interlocked.Increment(ref requests);
            try {await Task.Delay(2,token);if(number<=Failures){var failure=new HttpResponseMessage(Status){RequestMessage=request};if(Status==HttpStatusCode.TooManyRequests)failure.Headers.RetryAfter=new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(200));return failure;}
                var offset=(int)(request.Headers.Range?.Ranges.First().From??0);SawRange|=offset>0;
                var partial=offset>0&&!IgnoreRange;var result=new HttpResponseMessage(partial?HttpStatusCode.PartialContent:HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(partial?Payload[offset..]:Payload)};
                result.Headers.ETag=new EntityTagHeaderValue("\"fixture\"");if(partial)result.Content.Headers.ContentRange=new ContentRangeHeaderValue(offset,Payload.Length-1,Payload.Length);return result;
            } finally{Interlocked.Decrement(ref active);}
        }
    }
}
