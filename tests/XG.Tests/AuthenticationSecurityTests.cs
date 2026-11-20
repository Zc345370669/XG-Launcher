// SPDX-License-Identifier: MIT
using System.Net;
using System.Text;
using System.Text.Json;
using XG.Core;

internal static class AuthenticationSecurityTests
{
    internal static async Task RunAsync()
    {
        foreach (var status in new[] { 200, 400, 401, 403, 429, 500 })
        {
            using var handler = new ProtocolFixture(status);
            using var http = new HttpClient(handler);
            var trace = new AuthDiagnosticTrace();
            try
            {
                var result = await new MinecraftAuthentication(http).ExchangeAsync("fictional-ms-secret", default, trace);
                Check(status == 200 && result.Identity.Name == "FixtureUser" && result.Identity.Id != Guid.Empty, "full login");
                Check(handler.Calls == 5, "ownership and profile required");
            }
            catch (LoginException error)
            {
                Check(status != 200 && error.Stage == LoginStage.Minecraft && (int?)error.Status == status, "correct failure stage");
                Check(handler.Calls == 3, "no ownership/cache after failure");
            }
            var log = trace.ToString();
            Check(log.Contains("耗时ms=") && log.Contains("本地请求关联ID="), "request diagnostics");
            foreach (var secret in new[] { "fictional-ms-secret", "fictional-xbox-secret", "fictional-xsts-secret", "fictional-game-secret", "unknown-cookie-secret", "unknown-refresh-secret", "unknown-code-secret", "unknown-field-only", "unknown-identity-secret", "unknown-authorization-secret", "unknown-access-secret" })
                Check(!log.Contains(secret), "no confidential response content");
            Check(!log.Contains("\nEVIL") && !log.Contains("/callback?"), "no injection or callback URL");
            if (status != 200)
                Check(log.Contains("FixtureDenied") && log.Contains("PolicyDenied") && log.Contains("Server declined this request"), "allowlisted error fields");
            Console.WriteLine($"PASS auth13.1 protocol HTTP {status}, request contract, diagnostics and credential redaction (controlled handler)");
        }
        foreach (var body in new[] { "not-json <html>secret</html>", "{}", "{\"untrusted\":\"not-a-code\"}" })
        {
            using var handler = new ProtocolFixture(403) { ErrorBody = body };
            using var http = new HttpClient(handler);
            var trace = new AuthDiagnosticTrace();
            try { await new MinecraftAuthentication(http).ExchangeAsync("fictional-ms-secret", default, trace); throw new Exception("expected denial"); }
            catch (LoginException error) { Check(error.Stage == LoginStage.Minecraft, "403 stage"); }
            Check(trace.ToString().Contains("HTTP 403，服务未提供可识别的错误代码。"), "explicit absent error code");
            Check(!trace.ToString().Contains("<html>"), "no raw HTML");
        }
        Console.WriteLine("PASS auth13.1 non-JSON, empty and unknown response diagnostics (controlled handler)");
        foreach (var end in new[] { "2000-01-01T00:00:00Z", "not-a-date" })
        {
            using var handler = new ProtocolFixture(200) { NotAfter = end };
            using var http = new HttpClient(handler);
            try { await new MinecraftAuthentication(http).ExchangeAsync("fictional-ms-secret", default); throw new Exception("expired XSTS accepted"); }
            catch (LoginException error) { Check(error.Stage == LoginStage.Xsts && handler.Calls == 2, "reject XSTS before Minecraft request"); }
        }
        Console.WriteLine("PASS auth13.1 supplied XSTS expiry validation (controlled handler)");
    }

    private static void Check(bool valid, string name) { if (!valid) throw new Exception("Authentication security assertion: " + name); }
    private sealed class ProtocolFixture(int status) : HttpMessageHandler
    {
        internal int Calls;
        internal string? ErrorBody;
        internal string NotAfter = "2099-01-01T00:00:00Z";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var path = request.RequestUri!.AbsolutePath;
            string body;
            if (path == "/user/authenticate")
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Check(json.RootElement.GetProperty("Properties").GetProperty("RpsTicket").GetString() == "d=fictional-ms-secret", "Xbox RPS");
                body = """{"Token":"fictional-xbox-secret","DisplayClaims":{"xui":[{"uhs":"123456789"}]}}""";
            }
            else if (path == "/xsts/authorize")
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Check(json.RootElement.GetProperty("RelyingParty").GetString() == "rp://api.minecraftservices.com/" &&
                    json.RootElement.GetProperty("TokenType").GetString() == "JWT" &&
                    json.RootElement.GetProperty("Properties").GetProperty("UserTokens")[0].GetString() == "fictional-xbox-secret", "XSTS audience and token");
                body = JsonSerializer.Serialize(new { Token = "fictional-xsts-secret", NotAfter, DisplayClaims = new { xui = new[] { new { uhs = "123456789" } } } });
            }
            else if (path == "/authentication/login_with_xbox")
            {
                Check(request.Method == HttpMethod.Post && request.RequestUri.Host == "api.minecraftservices.com" &&
                    request.Content!.Headers.ContentType!.MediaType == "application/json" && request.Headers.Authorization == null, "Minecraft HTTP contract");
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Check(json.RootElement.EnumerateObject().Count() == 1 &&
                    json.RootElement.GetProperty("identityToken").GetString() == "XBL3.0 x=123456789;fictional-xsts-secret", "exact XSTS identityToken");
                if (status != 200)
                    return Response(status, ErrorBody ?? JsonSerializer.Serialize(new {
                        error = new { errorCode = "FixtureDenied", type = "PolicyDenied", message = "Server declined this request", detail = "fictional-xsts-secret fictional-ms-secret\r\nEVIL\ninjected-extra" },
                        errorMessage = "Authorization: Bearer unknown-authorization-secret", description = "Cookie: unknown-cookie-secret; refresh_token=unknown-refresh-secret",
                        message = "https://localhost/callback?code=unknown-code-secret&state=private-state",
                        detail = "identityToken=unknown-identity-secret access_token=unknown-access-secret",
                        untrusted = "unknown-field-only" }));
                body = """{"access_token":"fictional-game-secret","expires_in":3600}""";
            }
            else
            {
                Check(request.Headers.Authorization?.Parameter == "fictional-game-secret", "Minecraft bearer");
                body = path.Contains("entitlements") ? """{"items":[{"name":"game_minecraft"}]}""" :
                    """{"id":"123456781234123412341234567890ab","name":"FixtureUser"}""";
            }
            return Response(200, body);
        }
        private static HttpResponseMessage Response(int statusCode, string body)
        {
            var response = new HttpResponseMessage((HttpStatusCode)statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            response.Headers.Add("x-request-id", "00000000-0000-4000-8000-000000000123");
            response.Headers.Add("Set-Cookie", "do-not-record-this");
            return response;
        }
    }
}
