// SPDX-License-Identifier: MIT
namespace XG.Core;
public sealed record DownloadActivity(int Active, int Limit, int Maximum, int Retries);
/// <summary>所有可信文件传输复用同一 HTTP 会话门控；不另建下载队列或任务数据库。</summary>
public static class DownloadTuning
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HttpClient,BoundedDownloadHandler> transports=new();
    public static HttpClient CreateHttpClient(HttpMessageHandler inner)
    {
        HttpClient? client=null;
        var budget=new BoundedDownloadHandler(inner,()=>VerifiedTransfer.Activity(client!).Limit,(host,token)=>VerifiedTransfer.WaitHostAsync(client!,host,token));
        client=new HttpClient(budget);transports.Add(client,budget);
        return client;
    }
    public static DownloadActivity Activity(HttpClient http) { var status=VerifiedTransfer.Activity(http);return transports.TryGetValue(http,out var budget)?status with {Active=budget.Active}:status; }
    public static void Configure(HttpClient http, int maximum, bool adaptive = true) => VerifiedTransfer.Configure(http, Math.Clamp(maximum, 1, 32), adaptive);
}
