// SPDX-License-Identifier: MIT
using System.Text.RegularExpressions;
using CmlLib.Core.Auth;

namespace XG.Core;

// 此转换仅用于诊断；参与认证、启动、导入或迁移的令牌、路径及设置
// 不得使用脱敏结果替代原始业务数据。
public sealed partial class GameLogSanitizer(MSession? online)
{
    private const string Hidden = "[已隐藏]";

    public string Redact(string line)
    {
        // HTTP异常文字可能包含百分号编码后的回调地址，
        // 最多解码两次再过滤，且不信任嵌入的换行内容。
        for (var i = 0; i < 2 && line.Contains('%'); i++)
        {
            try { var decoded = Uri.UnescapeDataString(line); if (decoded == line) break; line = decoded; }
            catch (UriFormatException) { break; }
        }
        line = string.Concat(line.Select(c => char.IsControl(c) || c is '\u2028' or '\u2029' ||
            char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ? ' ' : c));
        if (online != null)
        {
            if (!string.IsNullOrEmpty(online.AccessToken))
                line = line.Replace(online.AccessToken, Hidden, StringComparison.Ordinal);
            if (Guid.TryParse(online.UUID, out var uuid))
                line = line.Replace(uuid.ToString("N"), Hidden, StringComparison.OrdinalIgnoreCase)
                    .Replace(uuid.ToString("D"), Hidden, StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(online.Username))
                line = line.Replace(online.Username, Hidden, StringComparison.OrdinalIgnoreCase);
        }
        line = PrivateKey().Replace(line, Hidden);
        line = AuthorizationHeader().Replace(line, "$1" + Hidden);
        line = CookieHeader().Replace(line, "$1" + Hidden);
        line = IdentityToken().Replace(line, Hidden);
        line = CredentialField().Replace(line, "$1" + Hidden);
        line = CallbackParameter().Replace(line, "$1" + Hidden);
        line = Bearer().Replace(line, "$1" + Hidden);
        line = Jwt().Replace(line, Hidden);
        // 带查询、片段或用户信息的地址整体省略，包括未知参数名；
        // 固定认证路由诊断通过独立白名单提供。
        line = Url().Replace(line, match => Uri.TryCreate(match.Value, UriKind.Absolute, out var uri) &&
            (uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0) ? Hidden : match.Value);
        line = WindowsUserPath().Replace(line, Hidden);
        line = UnixUserPath().Replace(line, Hidden);
        line = UncPath().Replace(line, Hidden);
        return EmailAddress().Replace(line, Hidden);
    }

    [GeneratedRegex("""(?i)(\bauthorization["']?\s*[:=]\s*).+$""", RegexOptions.NonBacktracking)]
    private static partial Regex AuthorizationHeader();
    [GeneratedRegex(@"-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----.*", RegexOptions.NonBacktracking)]
    private static partial Regex PrivateKey();
    [GeneratedRegex("""(?i)(\b(?:set-cookie|cookie)["']?\s*[:=]\s*).+$""", RegexOptions.NonBacktracking)]
    private static partial Regex CookieHeader();
    [GeneratedRegex("""(?i)((?:\b|--)(?:access[_-]?token|refresh[_-]?token|id[_-]?token|xbox[_-]?token|xsts[_-]?token|identity[_-]?token|client[_-]?secret|api[_-]?key|password|pwd|accountkey|user[_ -]?(?:name|id)|phone|mobile|code[_-]?verifier|authorization[_-]?code|oauth[_-]?code|computer[_-]?name|machine[_-]?name)["']?\s*(?:[:=]\s*|\s+)\s*)("[^"]*"|'[^']*'|[^\s,;&]+)""", RegexOptions.NonBacktracking)]
    private static partial Regex CredentialField();
    [GeneratedRegex("""(?i)(\b(?:code|state)["']?\s*[:=]\s*)("[^"]*"|'[^']*'|[^\s,;&]+)""", RegexOptions.NonBacktracking)]
    private static partial Regex CallbackParameter();
    [GeneratedRegex("""(?i)XBL\s?3\.0\s+x=[^\r\n"']+""", RegexOptions.NonBacktracking)]
    private static partial Regex IdentityToken();
    [GeneratedRegex("""(?i)(\bbearer\s+)[^\s"',;]+""", RegexOptions.NonBacktracking)]
    private static partial Regex Bearer();
    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", RegexOptions.NonBacktracking)]
    private static partial Regex Jwt();
    [GeneratedRegex("""(?i)https?://[^\s<>"']+""", RegexOptions.NonBacktracking)]
    private static partial Regex Url();
    [GeneratedRegex("""(?i)[a-z]:[\\/](?:users|documents and settings)[\\/][^\r\n"<>|]+""", RegexOptions.NonBacktracking)]
    private static partial Regex WindowsUserPath();
    [GeneratedRegex("""/(?:home|Users|workspace/scratch)/[^\s"'<>]+""", RegexOptions.NonBacktracking)]
    private static partial Regex UnixUserPath();
    [GeneratedRegex("""\\\\[^\s"'<>]+""", RegexOptions.NonBacktracking)]
    private static partial Regex UncPath();
    [GeneratedRegex("""(?i)\b[a-z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-z0-9.-]+\.[a-z]{2,}\b""", RegexOptions.NonBacktracking)]
    private static partial Regex EmailAddress();
}
