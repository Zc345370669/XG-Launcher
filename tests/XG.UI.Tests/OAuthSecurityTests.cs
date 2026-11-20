// SPDX-License-Identifier: MIT
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Identity.Client;
using XGLauncher;

internal static class OAuthSecurityTests
{
    internal static async Task RunAsync()
    {
        var safeError = MicrosoftAccounts.Describe(new XG.Core.LoginException(XG.Core.LoginStage.Microsoft,
            "Failed state=fictional-state-value at /home/fixture-person/account.log"));
        if (safeError.Contains("fictional-state-value") || safeError.Contains("fixture-person"))
            throw new Exception("Authentication error display leaked private input.");
        await using (var page = new OAuthCompletionPage())
        {
            page.CompleteAuthorization(true);
            using var http = LocalHttp();
            using var arrival = await http.GetAsync(page.Arrival);
            Check(arrival.StatusCode == HttpStatusCode.SeeOther && arrival.Headers.Location == page.Complete, "303 clean URL");
            for (var i = 0; i < 2; i++)
            {
                using var complete = await http.GetAsync(page.Complete);
                var html = await complete.Content.ReadAsStringAsync();
                Check(complete.IsSuccessStatusCode && complete.Headers.CacheControl?.NoStore == true &&
                    complete.Headers.Contains("Referrer-Policy") && complete.Headers.Contains("Content-Security-Policy"), "safe headers");
                Check(html.Contains("认证已完成") && !html.Contains("<script") && !html.Contains("code=") && !html.Contains("state="), "clean offline page");
            }
            foreach (var path in new[] { "/", "/auth/complete?code=fictional", "/auth/arrive?next=https://example.org", "/auth/complete/" })
            {
                using var invalid = await http.GetAsync(new Uri(page.Complete, path));
                Check(invalid.StatusCode == HttpStatusCode.NotFound && invalid.Headers.Location == null, "no query acceptance or open redirect");
            }
            using var wrongHost = new HttpRequestMessage(HttpMethod.Get, page.Complete);
            wrongHost.Headers.Host = "example.org";
            using var wrongReply = await http.SendAsync(wrongHost);
            Check(wrongReply.StatusCode == HttpStatusCode.BadRequest, "Host validation");
        }
        Console.WriteLine("PASS auth13.1 local completion 303, clean refresh, strict paths/Host, security headers and no open redirect (real loopback HTTP)");
        foreach (var scenario in new[] { "success", "code", "wrong-state", "missing-state", "missing-code", "denied", "illegal-path", "timeout", "cancel" })
        {
            using var fixture = new OAuthHttpFixture();
            fixture.Success = scenario == "success";
            var app = MicrosoftAccounts.CreatePublicClient("dbccdda9-9c7d-4435-9309-1e84eb908462", fixture);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var local = LocalHttp();
            Task? submitted = null;
            Uri? callback = null;
            Exception? failure = null;
            try
            {
                var result = await MicrosoftAccounts.RequestBrowserAuthorization(app, address =>
                {
                    var query = Parse(address.Query);
                    Check(query["code_challenge_method"] == "S256" && query["code_challenge"].Length > 20 && query["state"].Length > 0,
                        "MSAL state and PKCE");
                    callback = new Uri(query["redirect_uri"]);
                    Check(callback.IsLoopback && callback.Scheme == "http" && callback.AbsolutePath == "/", "MSAL loopback only");
                    if (scenario is "timeout" or "cancel")
                    {
                        deadline.CancelAfter(100);
                        return Task.CompletedTask;
                    }
                    var builder = new UriBuilder(callback)
                    {
                        Query = (scenario == "denied" ? "error=access_denied&error_description=fictional-private-message" :
                            scenario == "missing-code" ? "" : "code=fictional-once-code") +
                            (scenario == "missing-state" ? "" : "&state=" + Uri.EscapeDataString(scenario == "wrong-state" ? "wrong-state" : query["state"])),
                        Path = scenario == "illegal-path" ? "/not-the-callback" : "/"
                    };
                    submitted = SubmitAsync(builder.Uri);
                    return Task.CompletedTask;
                }, deadline.Token);
                Check(scenario == "success" && result.Account != null && result.AccessToken == "fictional-msal-access", "controlled OAuth result");
                var cached = await app.AcquireTokenSilent(new[] { "XboxLive.signin" }, result.Account).ExecuteAsync(deadline.Token);
                Check(cached.AccessToken == result.AccessToken && fixture.Exchanges == 1, "MSAL cache reuse");
                var refreshed = await app.AcquireTokenSilent(new[] { "XboxLive.signin" }, result.Account).WithForceRefresh(true).ExecuteAsync(deadline.Token);
                Check(refreshed.AccessToken == result.AccessToken && fixture.Refreshes == 1, "MSAL refresh");
            }
            catch (Exception error) when (error is MsalException or OperationCanceledException) { failure = error; }
            Check((failure == null) == (scenario == "success"), "only controlled success fixture may sign in");
            if (submitted != null) await submitted;
            Check(fixture.Exchanges == (scenario is "code" or "success" ? 1 : 0), "code consumed only after valid state and path");
            if (scenario is "code" or "success") Check(fixture.SawVerifier, "PKCE verifier in token exchange");
            if (scenario is "timeout" or "cancel") Check(failure is OperationCanceledException, "cancelled listener");
            if (callback != null)
            {
                using var probe = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                probe.CancelAfter(TimeSpan.FromMilliseconds(700));
                var beforeReplay = fixture.Exchanges;
                try
                {
                    using var duplicate = await local.GetAsync(callback, probe.Token);
                    Check(duplicate.StatusCode != HttpStatusCode.OK && duplicate.StatusCode != HttpStatusCode.Found, "listener closed/replay rejected");
                }
                catch (HttpRequestException) { }
                // Windows CI may time out rather than refuse a closed SDK callback URL.
                // Bound the probe independently; it must never consume another authorization code.
                catch (OperationCanceledException) when (probe.IsCancellationRequested && !deadline.IsCancellationRequested) { }
                Check(fixture.Exchanges == beforeReplay, "closed callback probe must not exchange another code");
            }
            Console.WriteLine($"PASS auth13.1 MSAL {scenario}, state/PKCE, one-use exchange and listener cleanup (synthetic callback + controlled OAuth HTTP)");

            async Task SubmitAsync(Uri destination)
            {
                await Task.Delay(100);
                using var reply = await local.GetAsync(destination, deadline.Token);
                // MSAL itself sends 302 (not 303). It must never reflect the
                // callback's code, state or error_description into its redirect.
                Check(reply.StatusCode == HttpStatusCode.Found && reply.Headers.Location is { IsLoopback: true } clean &&
                    clean.AbsolutePath == "/auth/arrive" && clean.Query.Length == 0, "MSAL safe redirect");
                using var bridge = await local.GetAsync(reply.Headers.Location, deadline.Token);
                Check(bridge.StatusCode == HttpStatusCode.SeeOther && bridge.Headers.Location?.AbsolutePath == "/auth/complete" &&
                    bridge.Headers.Location.Query.Length == 0, "completion bridge 303");
                using var final = await local.GetAsync(bridge.Headers.Location, deadline.Token);
                var html = await final.Content.ReadAsStringAsync();
                Check(final.StatusCode == HttpStatusCode.OK && !html.Contains("fictional"), "no reflected callback fields");
                Check(html.Contains(scenario == "success" ? "认证已完成" : "认证未完成"), "browser result matches actual MSAL outcome");
            }
        }
    }

    private static HttpClient LocalHttp() => new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(3) };
    private static Dictionary<string, string> Parse(string query) => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2)).ToDictionary(part => Uri.UnescapeDataString(part[0]), part => Uri.UnescapeDataString(part.ElementAtOrDefault(1) ?? ""));
    private static void Check(bool valid, string name) { if (!valid) throw new Exception("OAuth security assertion: " + name); }

    private sealed class OAuthHttpFixture : HttpMessageHandler, IMsalHttpClientFactory
    {
        private readonly HttpClient client;
        internal int Exchanges;
        internal int Refreshes;
        internal bool Success;
        internal bool SawVerifier;
        internal OAuthHttpFixture() { client = new HttpClient(this, disposeHandler: false); }
        public HttpClient GetHttpClient() => client;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body;
            if (request.RequestUri!.AbsolutePath.Contains("discovery/instance"))
                body = """{"tenant_discovery_endpoint":"https://login.microsoftonline.com/consumers/v2.0/.well-known/openid-configuration","metadata":[{"preferred_network":"login.microsoftonline.com","preferred_cache":"login.windows.net","aliases":["login.microsoftonline.com","login.windows.net","login.live.com"]}]}""";
            else if (request.RequestUri.AbsolutePath.Contains("openid-configuration"))
                body = """{"authorization_endpoint":"https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize","token_endpoint":"https://login.microsoftonline.com/consumers/oauth2/v2.0/token","issuer":"https://login.microsoftonline.com/consumers/v2.0","jwks_uri":"https://login.microsoftonline.com/consumers/discovery/v2.0/keys","end_session_endpoint":"https://login.microsoftonline.com/consumers/oauth2/v2.0/logout"}""";
            else if (request.RequestUri.AbsolutePath.EndsWith("/token"))
            {
                var form = Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                if (form.GetValueOrDefault("grant_type") == "refresh_token") Refreshes++;
                else Exchanges++;
                SawVerifier |= form.TryGetValue("code_verifier", out var verifier) && verifier.Length >= 43 &&
                    form.GetValueOrDefault("code") == "fictional-once-code" && form.GetValueOrDefault("grant_type") == "authorization_code";
                if (Success)
                {
                    static string B64(object value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                    var idToken = B64(new { alg = "none" }) + "." + B64(new {
                        aud = "dbccdda9-9c7d-4435-9309-1e84eb908462", iss = "https://login.microsoftonline.com/consumers/v2.0",
                        tid = "9188040d-6c67-4c5b-b112-36a304b66dad", oid = "00000000-0000-4000-8000-000000000001", sub = "fictional-subject",
                        preferred_username = "fictional@example.invalid", exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() }) + ".fixture";
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                        token_type = "Bearer", scope = "XboxLive.signin", expires_in = 3600, access_token = "fictional-msal-access",
                        refresh_token = "fictional-msal-refresh", id_token = idToken,
                        client_info = B64(new { uid = "00000000-0000-4000-8000-000000000001", utid = "9188040d-6c67-4c5b-b112-36a304b66dad" })
                    }), Encoding.UTF8, "application/json") };
                }
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    { Content = new StringContent("""{"error":"invalid_grant","error_description":"Controlled fixture rejects code; no real token."}""", Encoding.UTF8, "application/json") };
            }
            else throw new Exception("Unexpected OAuth HTTP fixture route");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
        protected override void Dispose(bool disposing) { if (disposing) client.Dispose(); base.Dispose(disposing); }
    }
}
