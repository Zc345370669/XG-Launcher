// SPDX-License-Identifier: MIT
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using XG.Core;

internal static class Phase9TransferTests
{
    internal static async Task RunAsync(string root)
    {
        var directory = Path.Combine(root, "phase9-fixture");
        Directory.CreateDirectory(directory);
        var payload = "expected test payload"u8.ToArray();
        using var server = new ResponseFixture(payload);
        using var http = new HttpClient(server);
        var queue = new Downloads(http, directory);
        var file = new ModFile("retry.jar", new Uri("https://cdn.modrinth.com/fixture/file.jar"), payload.Length,
            Convert.ToHexString(SHA512.HashData(payload)));
        var job = queue.Enqueue(file, directory);
        var partial = job.Target + "." + job.Id.ToString("N") + ".part";
        File.WriteAllBytes(partial, payload[..3]);
        var identity = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(partial)+"|"+file.Url.AbsoluteUri+"|"+file.Size+"|"+file.Sha512)));
        File.WriteAllText(partial+".identity.json",System.Text.Json.JsonSerializer.Serialize(new { Identity=identity, State="Pending" }));
        var samples = new List<long>();
        var states = new List<string>();
        if (!await queue.RunAsync(job.Id, () => states.Add(queue.Items.Single().State), (bytes, _) => samples.Add(bytes), () => true))
            throw new Exception("Transfer fixture did not complete after retry.");
        if (!server.RangeRequests.SequenceEqual([3L, 3L]) ||
            !states.Contains("连接中") || !states.Contains("校验中") || !states.Contains("安装中") ||
            !samples.Contains(payload.Length) || !File.ReadAllBytes(job.Target).SequenceEqual(payload))
            throw new Exception("Range restart, real byte reporting or transfer states did not match.");

        var denied = queue.Enqueue(file with { Name = "denied.jar" }, directory);
        server.Deny = true;
        if (await queue.RunAsync(denied.Id, () => { }, (_, _) => { }, () => true) ||
            !queue.Items.Single(item => item.Id == denied.Id).Error!.Contains("403") || server.DeniedRequests != 1)
            throw new Exception("Permanent HTTP 403 must fail without retry.");

        var operations = new ResourceOperations(directory);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var active = operations.RunAsync("fixture", "整合包", "fixture", "instance", async mark =>
        {
            await operations.Current!.RunFileAsync(async token =>
            {
                mark("下载中");
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    entered.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
            });
            return true;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var operation = operations.Items.Single().Id;
        operations.Pause(operation);
        if (operations.Items.Single().State != "已暂停") throw new Exception("Resource task failed to pause.");
        operations.Resume(operation);
        if (!await active.WaitAsync(TimeSpan.FromSeconds(5)) || attempts != 2 || operations.Items.Single().State != "已完成")
            throw new Exception("Resource task did not resume and finish.");
        Console.WriteLine("PASS fixtures: 429 retry, 403 stops, ignored Range restarts, true bytes/stages, resource pause and resume");
    }

    private sealed class ResponseFixture(byte[] payload) : HttpMessageHandler
    {
        public List<long> RangeRequests { get; } = [];
        public bool Deny { get; set; }
        public int DeniedRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (Deny)
            {
                DeniedRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { RequestMessage = request });
            }
            RangeRequests.Add(request.Headers.Range?.Ranges.Single().From ?? 0);
            if (RangeRequests.Count == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { RequestMessage = request });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { RequestMessage = request, Content = new ByteArrayContent(payload) });
        }
    }
}
