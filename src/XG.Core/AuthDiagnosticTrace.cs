// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace XG.Core;

public enum AuthStepState { Started, Response, Passed, Failed, Cancelled, Skipped }
public enum AuthServiceReason
{
    Unclassified, InvalidAppRegistration, InvalidToken, Forbidden, TooManyRequests, ServiceUnavailable,
    XboxNoProfile, XboxRegion, XboxAge, XboxFamily, InvalidResponse, NoEntitlement
}

/// <summary>
/// 记录白名单诊断字段和阶段结果，先登记秘密以在诊断边界脱敏。
/// </summary>
public sealed partial class AuthDiagnosticTrace
{
    private readonly StringBuilder lines = new();
    private readonly object sync = new();
    private readonly HashSet<LoginStage> visited = [];
    private readonly HashSet<string> secrets = new(StringComparer.Ordinal);

    internal void RegisterSecret(string value) { lock (sync) secrets.Add(value); }

    internal void RecordServiceFields(LoginStage stage, System.Text.Json.JsonElement body)
    {
        lock (sync)
        {
            var fields = AuthSafeFields.Read(body, secrets);
            lines.Append(stage).Append("  允许字段=").AppendLine(fields.Length > 0 ? fields : "服务未提供可安全记录的错误字段。");
        }
    }

    public AuthDiagnosticTrace()
    {
        lines.AppendLine("XG Launcher · OAuth 脱敏诊断");
        lines.AppendLine($"本地诊断编号：{Guid.NewGuid():D}");
    }

    public void Record(LoginStage stage, AuthStepState state, HttpStatusCode? status = null,
        AuthServiceReason reason = AuthServiceReason.Unclassified, HttpResponseHeaders? headers = null,
        long? xboxCode = null, AuthServiceReason? knownServerCode = null, Guid? requestId = null, long? elapsedMilliseconds = null)
    {
        lock (sync)
        {
            visited.Add(stage);
            lines.Append(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture))
                .Append("  ").Append(stage).Append("  ").Append(state);
            var route = stage switch
            {
                LoginStage.Xbox => "POST user.auth.xboxlive.com/user/authenticate",
                LoginStage.Xsts => "POST xsts.auth.xboxlive.com/xsts/authorize",
                LoginStage.Minecraft => "POST api.minecraftservices.com/authentication/login_with_xbox",
                LoginStage.Ownership => "GET api.minecraftservices.com/entitlements/mcstore",
                LoginStage.Profile => "GET api.minecraftservices.com/minecraft/profile",
                _ => null
            };
            if (route != null) lines.Append("  ").Append(route);
            if (requestId.HasValue) lines.Append("  本地请求关联ID=").Append(requestId.Value.ToString("D"));
            if (elapsedMilliseconds.HasValue) lines.Append("  耗时ms=").Append(Math.Max(0, elapsedMilliseconds.Value));
            if (status.HasValue) lines.Append("  HTTP ").Append((int)status.Value);
            if (reason != AuthServiceReason.Unclassified)
                lines.Append("  服务分类=").Append(reason).Append("  脱敏消息=").Append(SafeMessage(reason));
            else if (status is { } value && (int)value is < 200 or >= 300)
                lines.Append("  服务分类=Unclassified  脱敏消息=服务端详情未能安全识别");
            if (knownServerCode is { } code && code != AuthServiceReason.Unclassified)
                lines.Append("  服务端代码（标准化）=").Append(code);
            // XErr仅接受Xbox已知保留错误范围内的数值状态，不记录任意服务字段。
            if ((stage is LoginStage.Xbox or LoginStage.Xsts) && xboxCode is >= 2148916200 and <= 2148916299)
                lines.Append("  XErr=").Append(xboxCode.Value);
            if (headers != null)
            {
                foreach (var name in new[] { "x-request-id", "request-id", "x-correlation-id", "correlation-id", "x-ms-request-id", "x-ms-correlation-request-id", "x-minecraft-request-id", "traceparent", "cf-ray" })
                    if (headers.TryGetValues(name, out var values))
                        foreach (var value in values.Take(2))
                            if (IsCorrelationId(name, value))
                                lines.Append("  ").Append(name).Append('=').Append(value);
            }
            lines.AppendLine();
        }
    }

    public void Finish(params LoginStage[] stages)
    {
        lock (sync)
            foreach (var stage in stages)
                if (!visited.Contains(stage)) Record(stage, AuthStepState.Skipped);
    }

    public override string ToString()
    {
        lock (sync) return lines.ToString();
    }

    private static bool IsCorrelationId(string header, string value) => header switch
    {
        "traceparent" => value.Length == 55 && TraceParent().IsMatch(value),
        "cf-ray" => value.Length == 20 && CloudflareRay().IsMatch(value),
        _ => value.Length == 36 && Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty
    };

    [GeneratedRegex(@"\A[0-9a-f]{2}-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TraceParent();

    [GeneratedRegex(@"\A[0-9a-f]{16}-[A-Z]{3}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CloudflareRay();

    private static string SafeMessage(AuthServiceReason reason) => reason switch
    {
        AuthServiceReason.InvalidAppRegistration => "Invalid app registration",
        AuthServiceReason.InvalidToken => "凭据无效或过期",
        AuthServiceReason.Forbidden => "服务拒绝访问；具体原因未确认",
        AuthServiceReason.TooManyRequests => "请求受到限流",
        AuthServiceReason.ServiceUnavailable => "服务暂时不可用",
        AuthServiceReason.XboxNoProfile => "Xbox 档案缺失",
        AuthServiceReason.XboxRegion => "Xbox 地区限制",
        AuthServiceReason.XboxAge => "Xbox 年龄验证要求",
        AuthServiceReason.XboxFamily => "Xbox 家庭权限限制",
        AuthServiceReason.InvalidResponse => "服务响应格式无效",
        AuthServiceReason.NoEntitlement => "未查到受支持的 Java 权益",
        _ => "服务端详情未能安全识别"
    };
}
