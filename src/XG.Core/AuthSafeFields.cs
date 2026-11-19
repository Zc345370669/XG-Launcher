// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XG.Core;

// 只允许明确选择的标量错误字段越过诊断边界，
// 不保留原始响应、请求地址、异常全文或任意响应头。
internal static partial class AuthSafeFields
{
    internal static string Read(JsonElement body, IEnumerable<string> secrets)
    {
        if (body.ValueKind != JsonValueKind.Object) return "";
        var fields = new List<string>();
        Add(body);
        if (body.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object) Add(error);
        return string.Join("  ", fields.Take(10));

        void Add(JsonElement value)
        {
            foreach (var key in new[] { "error", "errorCode", "code", "errorType", "type", "errorMessage", "message", "detail", "description" })
            {
                if (!value.TryGetProperty(key, out var item) || item.ValueKind != JsonValueKind.String) continue;
                var text = item.GetString()!;
                // 过大字段整体丢弃，避免先截断再脱敏留下敏感尾段。
                if (text.Length > 1024) { fields.Add(key + "=[omitted: length]"); continue; }
                foreach (var secret in secrets.Where(s => s.Length > 0).OrderByDescending(s => s.Length))
                    text = text.Replace(secret, "[redacted]", StringComparison.Ordinal)
                        .Replace(Uri.EscapeDataString(secret), "[redacted]", StringComparison.Ordinal);
                text = Url().Replace(text, "[URL omitted]");
                // 包含凭据的字段整体省略；对任意JSON、HTML或引用片段只做局部替换，
                // 可能遗漏后续敏感内容，不能当作安全保证。
                if (Credential().IsMatch(text)) text = "[omitted: credential-bearing field]";
                else
                {
                    text = Opaque().Replace(text, "[opaque omitted]");
                    text = new GameLogSanitizer(null).Redact(text);
                    text = string.Concat(text.Select(c => char.IsControl(c) || c is '\u2028' or '\u2029' ||
                        char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ? ' ' : c));
                    text = text.Replace('<', ' ').Replace('>', ' ');
                    text = text[..Math.Min(240, text.Length)];
                }
                fields.Add(key + "=" + text);
            }
        }
    }

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex Url();
    [GeneratedRegex(@"authorization|cookie|token|secret|credential|\bPII\b|code_verifier|[?&](?:code|state)=|XBL3\.0|\bBearer\b|refresh[_ -]?token|access[_ -]?token|oauth[_ -]?code|\b(?:code|state)\s*[:=]", RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex Credential();
    [GeneratedRegex(@"[A-Za-z0-9_./+=-]{32,}", RegexOptions.NonBacktracking)]
    private static partial Regex Opaque();
}
