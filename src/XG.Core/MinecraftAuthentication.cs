// SPDX-License-Identifier: MIT
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XG.Core;

public enum LoginStage { Configuration, Browser, Microsoft, Xbox, Xsts, Minecraft, Ownership, Profile, Cache, Callback, Refresh, Launch }

public sealed class LoginException(LoginStage stage, string message, HttpStatusCode? status = null) : Exception(message)
{
    public LoginStage Stage { get; } = stage;
    public HttpStatusCode? Status { get; } = status;
}

public sealed record MinecraftIdentity(string Name, Guid Id, Uri? Skin);
public sealed class MinecraftLogin(MinecraftIdentity identity, string accessToken, DateTimeOffset expires)
{
    public MinecraftIdentity Identity { get; } = identity;
    public string AccessToken { get; } = accessToken;
    public DateTimeOffset Expires { get; } = expires;
}

public sealed class MinecraftAuthentication(HttpClient http, TimeSpan? requestTimeout = null)
{
    private static readonly JsonSerializerOptions RequestJson = new();
    private const string Services = "https://api.minecraftservices.com/";
    private static readonly HashSet<string> JavaEntitlements = new(StringComparer.Ordinal)
    {
        "game_minecraft", "product_minecraft", "game_minecraft_java", "product_minecraft_java"
    };

    public async Task<MinecraftLogin> ExchangeAsync(string microsoftToken, CancellationToken cancellationToken,
        AuthDiagnosticTrace? trace = null)
    {
        trace?.RegisterSecret(microsoftToken);
        var stage = LoginStage.Xbox;
        try
        {
            return await ExchangeCoreAsync(microsoftToken, cancellationToken, trace, next => stage = next);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            trace?.Record(stage, AuthStepState.Cancelled);
            throw;
        }
        catch (LoginException error)
        {
            trace?.Record(error.Stage, AuthStepState.Failed);
            throw;
        }
    }

    private async Task<MinecraftLogin> ExchangeCoreAsync(string microsoftToken, CancellationToken cancellationToken,
        AuthDiagnosticTrace? trace, Action<LoginStage> enter)
    {
        async Task<JsonElement> Post(string address, object body, LoginStage stage) =>
            await RequestAsync(new HttpRequestMessage(HttpMethod.Post, address) { Content = JsonContent.Create(body, options: RequestJson) },
                stage, cancellationToken, trace);
        async Task<JsonElement> Get(string path, string token, LoginStage stage)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, Services + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await RequestAsync(request, stage, cancellationToken, trace);
        }

        Begin(LoginStage.Xbox);
        var user = await Post("https://user.auth.xboxlive.com/user/authenticate", new
        {
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + microsoftToken },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        }, LoginStage.Xbox);
        var xboxToken = ReadToken(user, "Token", LoginStage.Xbox);
        trace?.RegisterSecret(xboxToken);
        CheckExpiry(user, LoginStage.Xbox);
        var xboxHash = user.TryGetProperty("DisplayClaims", out _) ? ReadUserHash(user, LoginStage.Xbox) : null;
        Pass(LoginStage.Xbox);
        Begin(LoginStage.Xsts);
        var xsts = await Post("https://xsts.auth.xboxlive.com/xsts/authorize", new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xboxToken } },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        }, LoginStage.Xsts);
        var xstsToken = ReadToken(xsts, "Token", LoginStage.Xsts);
        trace?.RegisterSecret(xstsToken);
        CheckExpiry(xsts, LoginStage.Xsts);
        var userHash = ReadUserHash(xsts, LoginStage.Xsts);
        trace?.RegisterSecret(userHash);
        if (xboxHash != null && !string.Equals(xboxHash, userHash, StringComparison.Ordinal))
            throw new LoginException(LoginStage.Xsts, "Xbox Live 与 XSTS 返回的用户哈希不一致，已停止认证。");
        Pass(LoginStage.Xsts);
        Begin(LoginStage.Minecraft);
        var minecraft = await Post(Services + "authentication/login_with_xbox",
            new { identityToken = $"XBL3.0 x={userHash};{xstsToken}" }, LoginStage.Minecraft);
        var token = ReadToken(minecraft, "access_token", LoginStage.Minecraft);
        trace?.RegisterSecret(token);
        if (!minecraft.TryGetProperty("expires_in", out var ttl) || ttl.ValueKind != JsonValueKind.Number ||
            !ttl.TryGetInt32(out var seconds) || seconds <= 0)
            throw InvalidResponse(LoginStage.Minecraft);
        var expires = DateTimeOffset.UtcNow.AddSeconds(seconds);
        Pass(LoginStage.Minecraft);
        Begin(LoginStage.Ownership);
        var ownership = await Get("entitlements/mcstore", token, LoginStage.Ownership);
        if (!ownership.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw InvalidResponse(LoginStage.Ownership);
        if (!items.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String &&
            JavaEntitlements.Contains(name.GetString()!)))
        {
            trace?.Record(LoginStage.Ownership, AuthStepState.Response, reason: AuthServiceReason.NoEntitlement);
            throw new LoginException(LoginStage.Ownership, "此微软账号未通过 Minecraft Java 版所有权验证。请检查是否选中了购买游戏或拥有有效订阅的账号。");
        }

        Pass(LoginStage.Ownership);
        Begin(LoginStage.Profile);
        var profile = await Get("minecraft/profile", token, LoginStage.Profile);
        var rawId = RequireString(profile, "id", LoginStage.Profile);
        var player = RequireString(profile, "name", LoginStage.Profile);
        if (!Guid.TryParseExact(rawId, "N", out var id) || id == Guid.Empty ||
            !Regex.IsMatch(player, @"\A[A-Za-z0-9_]{3,16}\z"))
            throw InvalidResponse(LoginStage.Profile);
        Uri? skinUri = null;
        if (profile.TryGetProperty("skins", out var skins) && skins.ValueKind == JsonValueKind.Array)
            foreach (var candidate in skins.EnumerateArray())
            {
                if (candidate.ValueKind != JsonValueKind.Object || !candidate.TryGetProperty("state", out var state) ||
                    state.ValueKind != JsonValueKind.String || state.GetString() != "ACTIVE" ||
                    !candidate.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String) continue;
                if (Uri.TryCreate(url.GetString(), UriKind.Absolute, out var texture) &&
                    texture.Host.Equals("textures.minecraft.net", StringComparison.OrdinalIgnoreCase) &&
                    texture.IsDefaultPort && texture.UserInfo.Length == 0 && texture.Scheme is "http" or "https" &&
                    Regex.IsMatch(texture.AbsolutePath, @"\A/texture/[0-9a-fA-F]{32,64}\z"))
                    skinUri = new UriBuilder(texture) { Scheme = "https", Port = -1, Query = "", Fragment = "" }.Uri;
                break;
            }
        Pass(LoginStage.Profile);
        return new MinecraftLogin(new(player, id, skinUri), token, expires);

        void Begin(LoginStage stage)
        {
            enter(stage);
            trace?.Record(stage, AuthStepState.Started);
        }
        void Pass(LoginStage stage) => trace?.Record(stage, AuthStepState.Passed);
    }

    private async Task<JsonElement> RequestAsync(HttpRequestMessage request, LoginStage stage, CancellationToken token, AuthDiagnosticTrace? trace)
    {
        var requestId = Guid.NewGuid();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (stage is LoginStage.Xbox or LoginStage.Xsts) request.Headers.Add("x-xbl-contract-version", "1");
        using (request)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            deadline.CancelAfter(requestTimeout ?? TimeSpan.FromSeconds(30));
            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (!response.IsSuccessStatusCode)
                    throw await ServiceErrorAsync(response, stage, token, deadline.Token, trace, requestId, elapsed.ElapsedMilliseconds);
                trace?.Record(stage, AuthStepState.Response, response.StatusCode, headers: response.Headers, requestId: requestId, elapsedMilliseconds: elapsed.ElapsedMilliseconds);
                var result = await ReadJsonAsync(response.Content, 512 * 1024, deadline.Token);
                return result.ValueKind == JsonValueKind.Object ? result : throw InvalidResponse(stage);
            }
            catch (JsonException)
            {
                trace?.Record(stage, AuthStepState.Response, reason: AuthServiceReason.InvalidResponse);
                throw InvalidResponse(stage);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                trace?.Record(stage, AuthStepState.Failed, requestId: requestId, elapsedMilliseconds: elapsed.ElapsedMilliseconds);
                throw new LoginException(stage, $"{StageName(stage)}连接超时，请稍后重试。");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                trace?.Record(stage, AuthStepState.Failed, requestId: requestId, elapsedMilliseconds: elapsed.ElapsedMilliseconds);
                throw new LoginException(stage, $"{StageName(stage)}网络连接失败，请检查网络后重试。");
            }
        }
    }

    private static async Task<LoginException> ServiceErrorAsync(HttpResponseMessage response, LoginStage stage, CancellationToken caller, CancellationToken deadline, AuthDiagnosticTrace? trace, Guid requestId, long elapsedMilliseconds)
    {
        var status = response.StatusCode;
        string label = StageName(stage);
        string text;
        long? xboxCode = null;
        AuthServiceReason? knownServerCode = null;
        var reason = status switch
        {
            HttpStatusCode.Unauthorized => AuthServiceReason.InvalidToken,
            HttpStatusCode.Forbidden => AuthServiceReason.Forbidden,
            HttpStatusCode.TooManyRequests => AuthServiceReason.TooManyRequests,
            _ when (int)status >= 500 => AuthServiceReason.ServiceUnavailable,
            _ => AuthServiceReason.Unclassified
        };
        JsonElement body = default;
        try { body = await ReadJsonAsync(response.Content, 12 * 1024, deadline); }
        catch (Exception error) when (error is JsonException or IOException or HttpRequestException ||
            error is OperationCanceledException && !caller.IsCancellationRequested) { }
        trace?.RecordServiceFields(stage, body);
        if (stage == LoginStage.Minecraft && status == HttpStatusCode.Forbidden &&
            !HasErrorCode(body))
            trace?.RecordServiceFields(stage, JsonSerializer.SerializeToElement(new { message = "HTTP 403，服务未提供可识别的错误代码。" }));
        if (status == HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter;
            var delay = retry?.Delta ?? (retry?.Date - DateTimeOffset.UtcNow);
            text = delay is { TotalSeconds: > 0 and <= 86400 }
                ? $"请求过于频繁，请在约 {Math.Ceiling(delay.Value.TotalSeconds)} 秒后重试。"
                : "请求过于频繁，请稍后重试。";
        }
        else if ((int)status >= 500) text = "服务暂时不可用，请稍后重试。";
        else if (stage == LoginStage.Profile && status == HttpStatusCode.NotFound)
            text = "账号没有 Minecraft Java 玩家档案，请先通过官方启动器创建玩家名称。";
        else if ((stage is LoginStage.Xbox or LoginStage.Xsts) || (stage == LoginStage.Minecraft && status == HttpStatusCode.Forbidden))
        {
            // Only classify fixed error codes/messages. Never store raw service bodies.
            if (stage is LoginStage.Xbox or LoginStage.Xsts)
            {
                var number = ReadXboxError(body);
                xboxCode = number;
                reason = number switch
                {
                    2148916233 => AuthServiceReason.XboxNoProfile,
                    2148916235 => AuthServiceReason.XboxRegion,
                    2148916236 or 2148916237 => AuthServiceReason.XboxAge,
                    2148916238 => AuthServiceReason.XboxFamily,
                    _ => reason
                };
                text = number switch
                {
                    2148916233 => "该微软账号尚未创建 Xbox 档案，请先在 Xbox 官方页面完成设置。",
                    2148916235 => "该 Xbox 账号所在地区的服务不可用。",
                    2148916236 or 2148916237 => "Xbox 账号需要完成成人验证。",
                    2148916238 => "Xbox 账号需要家庭组织者完成年龄或权限设置。",
                    _ => "Xbox 身份或权限验证失败，请检查 Xbox 账号状态。"
                };
            }
            else
            {
                var registration = IsInvalidRegistration(body, out knownServerCode);
                if (knownServerCode == null && HasForbiddenCode(body)) knownServerCode = AuthServiceReason.Forbidden;
                if (registration) reason = AuthServiceReason.InvalidAppRegistration;
                text = registration
                    ? "Invalid app registration。服务明确拒绝应用注册，请开发者核实 Minecraft Java 游戏服务 API 审批。"
                    : "服务拒绝访问。响应未确认应用注册被拒，请复制诊断信息以便核查。";
            }
        }
        else if (status == HttpStatusCode.Unauthorized) text = "凭据无效或已过期，请重新登录。";
        else if (status == HttpStatusCode.Forbidden) text = "当前账号或应用无权访问该服务。";
        else text = "服务未完成验证，请稍后重试。";
        trace?.Record(stage, AuthStepState.Response, status, reason, response.Headers, xboxCode, knownServerCode, requestId, elapsedMilliseconds);
        return new LoginException(stage, $"{label}（HTTP {(int)status}）：{text}", status);
    }

    private static bool HasErrorCode(JsonElement body) => body.ValueKind == JsonValueKind.Object &&
        (new[] { "error", "errorCode", "code", "errorType", "type" }.Any(key =>
            body.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(field.GetString())) ||
         body.TryGetProperty("error", out var nested) && nested.ValueKind == JsonValueKind.Object && HasErrorCode(nested));

    private static void CheckExpiry(JsonElement response, LoginStage stage)
    {
        // Xbox services normally return NotAfter. Reject a supplied invalid or
        // expired value without decoding or trusting JWT claims ourselves.
        if (response.TryGetProperty("NotAfter", out var end) &&
            (end.ValueKind != JsonValueKind.String || !DateTimeOffset.TryParse(end.GetString(),
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal,
                out var expiry) || expiry <= DateTimeOffset.UtcNow))
            throw new LoginException(stage, "Xbox 身份令牌已过期或有效期无效，请重新登录。");
    }

    private static bool HasForbiddenCode(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return false;
        return Known(body) || (body.TryGetProperty("error", out var error) &&
            error.ValueKind == JsonValueKind.Object && Known(error));

        static bool Known(JsonElement value) =>
            new[] { "code", "errorCode", "error" }.Any(key =>
                value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String &&
                (string.Equals(field.GetString(), "ForbiddenOperationException", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(field.GetString(), "FORBIDDEN", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsInvalidRegistration(JsonElement body, out AuthServiceReason? knownCode)
    {
        knownCode = null;
        if (body.ValueKind != JsonValueKind.Object) return false;
        if (HasRegistrationCode(body) || (body.TryGetProperty("error", out var error) &&
            error.ValueKind == JsonValueKind.Object && HasRegistrationCode(error)))
            knownCode = AuthServiceReason.InvalidAppRegistration;
        return knownCode.HasValue || HasRegistrationMessage(body) ||
            (body.TryGetProperty("error", out var detail) && detail.ValueKind == JsonValueKind.Object && HasRegistrationMessage(detail));

        static bool HasRegistrationCode(JsonElement value) =>
            new[] { "code", "errorCode", "error" }.Any(key =>
                value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String &&
                field.GetString()!.Trim() is { } raw &&
                (raw.Equals("InvalidAppRegistration", StringComparison.OrdinalIgnoreCase) ||
                 raw.Equals("invalid_app_registration", StringComparison.OrdinalIgnoreCase)));
        static bool HasRegistrationMessage(JsonElement value) =>
            new[] { "message", "errorMessage", "error", "detail" }.Any(key =>
                value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String &&
                field.GetString()!.Trim().TrimEnd('.').Equals("Invalid app registration", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpContent content, int limit, CancellationToken token)
    {
        if (content.Headers.ContentLength > limit) throw new JsonException("Authentication response exceeds limit.");
        await using var stream = await content.ReadAsStreamAsync(token);
        using var destination = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (destination.Length + count > limit) throw new JsonException("Authentication response exceeds limit.");
            destination.Write(buffer, 0, count);
        }
        using var document = JsonDocument.Parse(destination.GetBuffer().AsMemory(0, (int)destination.Length));
        return document.RootElement.Clone();
    }

    private static string StageName(LoginStage stage) => stage switch
    {
        LoginStage.Xbox => "Xbox Live",
        LoginStage.Xsts => "Xbox XSTS",
        LoginStage.Ownership => "Minecraft 所有权",
        LoginStage.Profile => "Minecraft 玩家档案",
        _ => "Minecraft 会话"
    };

    private static long? ReadXboxError(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("XErr", out var code)) return null;
        if (code.ValueKind == JsonValueKind.Number && code.TryGetInt64(out var numeric)) return numeric;
        return code.ValueKind == JsonValueKind.String &&
            long.TryParse(code.GetString(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var quoted) ? quoted : null;
    }

    private static string ReadUserHash(JsonElement result, LoginStage stage)
    {
        if (!result.TryGetProperty("DisplayClaims", out var claims) || claims.ValueKind != JsonValueKind.Object ||
            !claims.TryGetProperty("xui", out var users) || users.ValueKind != JsonValueKind.Array ||
            users.GetArrayLength() != 1)
            throw InvalidResponse(stage);
        var hash = RequireString(users[0], "uhs", stage);
        return hash.Length <= 128 && hash.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            ? hash : throw InvalidResponse(stage);
    }

    private static string ReadToken(JsonElement body, string field, LoginStage stage)
    {
        var token = RequireString(body, field, stage);
        return token.Length <= 65536 && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '+' or '/' or '=')
            ? token : throw InvalidResponse(stage);
    }

    private static string RequireString(JsonElement element, string property, LoginStage stage) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())
            ? item.GetString()! : throw InvalidResponse(stage);

    private static LoginException InvalidResponse(LoginStage stage) => new(stage, stage switch
    {
        LoginStage.Xsts => "Xbox XSTS 返回的身份响应无效，请稍后重试。",
        LoginStage.Xbox => "Xbox 返回的身份响应无效，请稍后重试。",
        LoginStage.Ownership => "Minecraft 所有权响应无效，请稍后重试。",
        LoginStage.Profile => "Minecraft 玩家档案响应无效，请稍后重试。",
        _ => "Minecraft 会话响应无效，请稍后重试。"
    });
}
