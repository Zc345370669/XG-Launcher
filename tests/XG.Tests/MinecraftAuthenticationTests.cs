// SPDX-License-Identifier: MIT
using System.Net;
using System.Text.Json;
using CmlLib.Core.Auth;
using XG.Core;

internal static class MinecraftAuthenticationTests
{
    public static async Task RunAsync()
    {
        using var handler = new Fixture();
        using var client = new HttpClient(handler);
        var service = new MinecraftAuthentication(client);
        var login = await service.ExchangeAsync("microsoft-fixture", default);
        Check(login.Identity.Name == "TestPlayer" && login.Identity.Id.ToString("N") == Fixture.PlayerId);
        Check(login.Identity.Skin?.Scheme == "https");
        Check(login.Expires > DateTimeOffset.UtcNow);
        Check(handler.Steps == 5);
        var session = new MSession(login.Identity.Name, login.AccessToken, login.Identity.Id.ToString("N"));
        Check(session.Username == "TestPlayer" && session.AccessToken == "game-fixture" && session.UUID == Fixture.PlayerId);

        handler.Reset("not-owned");
        await VerifyError(LoginStage.Ownership);
        Check(handler.Steps == 4);
        handler.Reset("not-java");
        await VerifyError(LoginStage.Ownership);
        handler.Reset("xbox-family");
        await VerifyError(LoginStage.Xsts, HttpStatusCode.Unauthorized, "家庭组织者");
        handler.Reset("no-xbox-profile");
        await VerifyError(LoginStage.Xsts, HttpStatusCode.Unauthorized, "尚未创建");
        handler.Reset("unapproved");
        await VerifyError(LoginStage.Minecraft, HttpStatusCode.Forbidden, "Invalid app registration");
        handler.Reset("coded-403");
        await VerifyError(LoginStage.Minecraft, HttpStatusCode.Forbidden, "Invalid app registration");
        handler.Reset("unclassified-403");
        await VerifyError(LoginStage.Minecraft, HttpStatusCode.Forbidden, "未确认应用注册被拒");
        handler.Reset("html-403");
        await VerifyError(LoginStage.Minecraft, HttpStatusCode.Forbidden, "未确认应用注册被拒");
        handler.Reset("oversized-403");
        await VerifyError(LoginStage.Minecraft, HttpStatusCode.Forbidden, "未确认应用注册被拒");
        handler.Reset("generic-coded-403");
        await VerifyError(LoginStage.Minecraft, HttpStatusCode.Forbidden, "未确认应用注册被拒");
        handler.Reset("minecraft-401");
        await VerifyError(LoginStage.Minecraft, HttpStatusCode.Unauthorized, "过期");
        handler.Reset("minecraft-429");
        await VerifyError(LoginStage.Minecraft, HttpStatusCode.TooManyRequests, "7 秒");
        handler.Reset("minecraft-500");
        await VerifyError(LoginStage.Minecraft, HttpStatusCode.InternalServerError, "暂时不可用");
        handler.Reset("timeout");
        await VerifyError(LoginStage.Minecraft, null, "超时", TimeSpan.FromMilliseconds(120));
        handler.Reset("rate-limited");
        await VerifyError(LoginStage.Ownership, HttpStatusCode.TooManyRequests, "频繁");
        handler.Reset("service-error");
        await VerifyError(LoginStage.Ownership, HttpStatusCode.ServiceUnavailable, "暂时不可用");
        handler.Reset("expired-game");
        await VerifyError(LoginStage.Profile, HttpStatusCode.Unauthorized, "过期");
        handler.Reset("no-profile");
        await VerifyError(LoginStage.Profile, HttpStatusCode.NotFound);
        handler.Reset("invalid-profile");
        await VerifyError(LoginStage.Profile);
        handler.Reset("invalid-xbox");
        await VerifyError(LoginStage.Xbox);
        handler.Reset("invalid-xbox-token");
        await VerifyError(LoginStage.Xbox);
        Check(handler.Steps == 1);
        handler.Reset("hash-mismatch");
        await VerifyError(LoginStage.Xsts);
        Check(handler.Steps == 2);
        handler.Reset("invalid-hash");
        await VerifyError(LoginStage.Xsts);
        Check(handler.Steps == 2);
        handler.Reset("quoted-xbox-error");
        await VerifyError(LoginStage.Xsts, HttpStatusCode.Unauthorized, "尚未创建");
        handler.Reset("outside-avatar-host");
        Check((await service.ExchangeAsync("microsoft-fixture", default)).Identity.Skin is null);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await service.ExchangeAsync("microsoft-fixture", cancelled.Token);
            throw new Exception("Cancellation was ignored.");
        }
        catch (OperationCanceledException) { }
        Console.WriteLine("PASS simulated HTTP 403 explicit/custom/nested/HTML/oversized response, endpoint/correlation output, no credential exposure, and timeout");
        Console.WriteLine("PASS simulated Xbox and Minecraft exchange, entitlement rejection, profile and skin validation, session mapping, cancellation");

        async Task VerifyError(LoginStage expected, HttpStatusCode? status = null, string? message = null, TimeSpan? timeout = null)
        {
            var trace = new AuthDiagnosticTrace();
            try
            {
                await new MinecraftAuthentication(client, timeout).ExchangeAsync("microsoft-fixture", default, trace);
                throw new Exception("Expected a controlled authentication failure.");
            }
            catch (LoginException failure)
            {
                Check(failure.Stage == expected && failure.Status == status && !failure.Message.Contains("fixture", StringComparison.Ordinal) && (message is null || failure.Message.Contains(message)));
                var report = trace.ToString();
                Check(!report.Contains("fixture", StringComparison.OrdinalIgnoreCase) && !report.Contains("Set-Cookie"));
                if (status == HttpStatusCode.Forbidden)
                {
                    Check(report.Contains("POST api.minecraftservices.com/authentication/login_with_xbox") && report.Contains("HTTP 403"));
                    Check(report.Contains("Xbox  Passed") && report.Contains("Xsts  Passed") && !report.Contains("Ownership  Started"));
                    Check(report.Contains("x-request-id=d23b5308-bb2a-49e0-8fba-ae264d658d5a"));
                    Check(report.Contains("Invalid app registration") == (message == "Invalid app registration"));
                    Check(report.Contains("服务端代码（标准化）=") == (handler.IsCodedForbidden));
                    Check(handler.Steps == 3);
                }
            }
        }
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new Exception("Authentication fixture assertion failed.");
    }

    private sealed class Fixture : HttpMessageHandler
    {
        public const string PlayerId = "9fd64142cc9a4d9785536c9a6ea1310c";
        public int Steps { get; private set; }
        public bool IsCodedForbidden => mode is "coded-403" or "generic-coded-403";
        private string mode = "";
        public void Reset(string failure)
        {
            mode = failure;
            Steps = 0;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = ++Steps;
            var endpoint = request.RequestUri!.GetLeftPart(UriPartial.Path);
            if (step <= 3)
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                var body = payload.RootElement;
                Check(request.Method == HttpMethod.Post);
                if (step == 1)
                {
                    Check(endpoint == "https://user.auth.xboxlive.com/user/authenticate");
                    Check(body.GetProperty("Properties").GetProperty("RpsTicket").GetString() == "d=microsoft-fixture");
                    var auth = body.GetProperty("Properties");
                    Check(auth.GetProperty("AuthMethod").GetString() == "RPS");
                    Check(auth.GetProperty("SiteName").GetString() == "user.auth.xboxlive.com");
                    Check(body.GetProperty("TokenType").GetString() == "JWT");
                    Check(body.GetProperty("RelyingParty").GetString() == "http://auth.xboxlive.com");
                    Check(request.Headers.GetValues("x-xbl-contract-version").Single() == "1");
                    return Reply(mode switch
                    {
                        "invalid-xbox" => "[]",
                        "invalid-xbox-token" => """{"Token":"line one;unexpected"}""",
                        "hash-mismatch" => """{"Token":"xbox-fixture","DisplayClaims":{"xui":[{"uhs":"other-account"}]}}""",
                        _ => """{"Token":"xbox-fixture","DisplayClaims":{"xui":[{"uhs":"hash-fixture"}]}}"""
                    });
                }
                if (step == 2)
                {
                    Check(endpoint == "https://xsts.auth.xboxlive.com/xsts/authorize");
                    Check(body.GetProperty("RelyingParty").GetString() == "rp://api.minecraftservices.com/");
                    Check(body.GetProperty("TokenType").GetString() == "JWT");
                    Check(body.GetProperty("Properties").GetProperty("SandboxId").GetString() == "RETAIL");
                    Check(body.GetProperty("Properties").GetProperty("UserTokens")[0].GetString() == "xbox-fixture");
                    Check(request.Headers.GetValues("x-xbl-contract-version").Single() == "1");
                    if (mode == "quoted-xbox-error") return Reply("""{"XErr":"2148916233"}""", HttpStatusCode.Unauthorized);
                    if (mode == "invalid-hash") return Reply("""{"Token":"xsts-fixture","DisplayClaims":{"xui":[{"uhs":"wrong;value"}]}}""");
                    if (mode == "no-xbox-profile") return Reply("""{"XErr":2148916233}""", HttpStatusCode.Unauthorized);
                    if (mode == "xbox-family") return Reply("""{"XErr":2148916238}""", HttpStatusCode.Unauthorized);
                    return Reply("""{"Token":"xsts-fixture","DisplayClaims":{"xui":[{"uhs":"hash-fixture"}]}}""");
                }
                Check(endpoint == "https://api.minecraftservices.com/authentication/login_with_xbox");
                Check(body.GetProperty("identityToken").GetString() == "XBL3.0 x=hash-fixture;xsts-fixture");
                Check(request.Content!.Headers.ContentType!.MediaType == "application/json");
                Check(request.Headers.Accept.Single().MediaType == "application/json");
                Check(request.Headers.Authorization is null);
                if (mode == "timeout") await Task.Delay(Timeout.Infinite, cancellationToken);
                if (mode == "minecraft-401") return Reply("{}", HttpStatusCode.Unauthorized);
                if (mode == "minecraft-500") return Reply("{}", HttpStatusCode.InternalServerError);
                if (mode == "minecraft-429")
                {
                    var throttled = Reply("{}", HttpStatusCode.TooManyRequests);
                    throttled.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                    return throttled;
                }
                if (mode is "unapproved" or "coded-403" or "unclassified-403" or "html-403" or "oversized-403" or "generic-coded-403")
                {
                    var reply = Reply(mode switch
                    {
                        "unapproved" => """{"errorMessage":"Invalid app registration","details":"fixture-SECRET"}""",
                        "coded-403" => """{"error":{"code":"INVALID_APP_REGISTRATION","message":"fixture-server-PII"}}""",
                        "generic-coded-403" => """{"error":"ForbiddenOperationException","message":"fixture-token-must-not-appear"}""",
                        "html-403" => "<html>fixture-email@example.invalid</html>",
                        "oversized-403" => new string('a', 13000),
                        _ => """{"code":"fixture-secret","message":"fixture-bearer-secret"}"""
                    }, HttpStatusCode.Forbidden);
                    reply.Headers.TryAddWithoutValidation("x-request-id", "d23b5308-bb2a-49e0-8fba-ae264d658d5a");
                    reply.Headers.TryAddWithoutValidation("x-correlation-id", "fixture-secret-correlation");
                    reply.Headers.TryAddWithoutValidation("Set-Cookie", "private-cookie=fixture-secret");
                    return reply;
                }
                return Reply("""{"access_token":"game-fixture","expires_in":7200}""");
            }

            Check(request.Method == HttpMethod.Get);
            Check(request.Headers.Authorization?.ToString() == "Bearer game-fixture");
            if (step == 4)
            {
                Check(endpoint == "https://api.minecraftservices.com/entitlements/mcstore");
                if (mode == "rate-limited") return Reply("{}", HttpStatusCode.TooManyRequests);
                if (mode == "service-error") return Reply("{}", HttpStatusCode.ServiceUnavailable);
                return Reply(mode switch
                {
                    "not-owned" => """{"items":[]}""",
                    "not-java" => """{"items":[{"name":"product_minecraft_bedrock"}]}""",
                    _ => """{"items":[{"name":"game_minecraft"}]}"""
                });
            }
            Check(step == 5 && endpoint == "https://api.minecraftservices.com/minecraft/profile");
            if (mode == "expired-game") return Reply("{}", HttpStatusCode.Unauthorized);
            if (mode == "no-profile") return Reply("{}", HttpStatusCode.NotFound);
            if (mode == "invalid-profile") return Reply("""{"name":"TestPlayer","id":"not-a-uuid"}""");
            var host = mode == "outside-avatar-host" ? "example.com" : "textures.minecraft.net";
            return Reply($$"""{"name":"TestPlayer","id":"{{PlayerId}}","skins":[{"state":"ACTIVE","url":"http://{{host}}/texture/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]}""");
        }

        private static HttpResponseMessage Reply(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
}
