// SPDX-License-Identifier: MIT
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace XGLauncher;

// Separate, loopback-only presentation server. It NEVER receives an OAuth code,
// state, token or redirect destination. MSAL owns the authentication listener.
internal sealed class OAuthCompletionPage : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromMinutes(6));
    private readonly Task worker;
    private readonly TaskCompletionSource<bool> outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal void CompleteAuthorization(bool success) => outcome.TrySetResult(success);
    internal Uri Arrival { get; }
    internal Uri Complete { get; }
    internal Task Stopped => worker;
    internal const string FailureHtml = """
        <!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>认证未完成 · XG Launcher</title><h1>认证未完成</h1>
        <p>请返回 XG Launcher 查看结果或重试登录。你可以关闭此标签页。</p>
        <p>Authorization did not complete. Return to XG Launcher for details or try again.</p></html>
        """;
    internal const string PendingHtml = """
        <!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>请查看启动器 · XG Launcher</title><h1>授权回调已接收</h1>
        <p>认证尚未确认完成，请返回 XG Launcher 查看最终结果。</p>
        <p>Authorization is still being verified. Check XG Launcher for the final result.</p></html>
        """;
    internal const string Html = """
        <!doctype html><html lang="zh-CN"><meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>认证已完成 · XG Launcher</title>
        <h1>认证已完成</h1>
        <p>你可以返回 XG Launcher，并关闭此浏览器标签页。</p>
        <p>此页面仅表示浏览器授权回调已结束。账号、游戏所有权与玩家档案验证结果请查看启动器。</p>
        <p>Browser authorization has ended. Return to XG Launcher for the final account verification result.</p></html>
        """;

    internal OAuthCompletionPage()
    {
        listener.Start(8);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Arrival = new Uri($"http://127.0.0.1:{port}/auth/arrive");
        Complete = new Uri($"http://127.0.0.1:{port}/auth/complete");
        worker = RunAsync();
    }

    // Keep the non-sensitive completion page available for refresh after MSAL
    // returns. Bound the lifetime, including cancellation and failed attempts.
    internal void ExpireSoon()
    {
        try { lifetime.CancelAfter(TimeSpan.FromSeconds(45)); }
        catch (ObjectDisposedException) { }
    }

    private async Task RunAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(lifetime.Token);
                using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                request.CancelAfter(TimeSpan.FromSeconds(40));
                try { await RespondAsync(client.GetStream(), request.Token); }
                catch (Exception error) when (error is IOException or SocketException or OperationCanceledException) { }
            }
        }
        catch (Exception error) when (error is SocketException or OperationCanceledException or ObjectDisposedException) { }
        finally { listener.Stop(); lifetime.Dispose(); }
    }

    private async Task RespondAsync(NetworkStream stream, CancellationToken token)
    {
        // Bounded HTTP header reader; never log request lines, headers or bodies.
        using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        readDeadline.CancelAfter(TimeSpan.FromSeconds(2));
        var buffer = new byte[8192];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count, 1), readDeadline.Token);
            if (read == 0) return;
            count += read;
            if (count >= 4 && buffer[count - 4] == 13 && buffer[count - 3] == 10 &&
                buffer[count - 2] == 13 && buffer[count - 1] == 10) break;
        }
        var header = Encoding.ASCII.GetString(buffer, 0, count);
        var lines = header.Split("\r\n");
        var exactHost = "Host: " + Complete.Authority;
        var hostValid = lines.Count(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)) == 1 &&
            lines.Any(line => line.Equals(exactHost, StringComparison.OrdinalIgnoreCase));
        var code = "400 Bad Request";
        var body = "Invalid request.";
        var location = "";
        if (hostValid && count < buffer.Length && header.EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (lines[0] == "GET /auth/arrive HTTP/1.1")
            {
                code = "303 See Other";
                location = "Location: " + Complete.AbsoluteUri + "\r\n";
                body = "";
            }
            else if (lines[0] == "GET /auth/complete HTTP/1.1")
            {
                code = "200 OK";
                try { body = await outcome.Task.WaitAsync(TimeSpan.FromSeconds(30), token) ? Html : FailureHtml; }
                catch (TimeoutException) { body = PendingHtml; }
            }
            else { code = "404 Not Found"; body = "Not found."; }
        }
        var bytes = Encoding.UTF8.GetBytes(body);
        var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {code}\r\n{location}" +
            "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nPragma: no-cache\r\n" +
            "Referrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'\r\n" +
            "X-Content-Type-Options: nosniff\r\nConnection: close\r\nContent-Length: " + bytes.Length + "\r\n\r\n");
        await stream.WriteAsync(response, token);
        await stream.WriteAsync(bytes, token);
    }

    public async ValueTask DisposeAsync()
    {
        try { await lifetime.CancelAsync(); }
        catch (ObjectDisposedException) { }
        await worker;
    }
}
