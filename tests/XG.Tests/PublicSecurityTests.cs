// SPDX-License-Identifier: MIT
using XG.Core;

internal static class PublicSecurityTests
{
    internal static void Run()
    {
        var redactor = new GameLogSanitizer(null);
        var cases = new[]
        {
            ("oauth state=fictional-state-value", "fictional-state-value"),
            ("{\"state\":\"fictional-state-value\"}", "fictional-state-value"),
            ("callback?code=fictional-once-value&state=fictional-state-value", "fictional-once-value"),
            ("https://localhost/callback?unknown=fictional-query-value", "fictional-query-value"),
            ("https://localhost/callback%3Fcode%3Dfictional-encoded-value", "fictional-encoded-value"),
            ("code_verifier=fictional-verifier-value", "fictional-verifier-value"),
            ("identityToken=\"XBL3.0 x=fictional-hash;fictional-xsts-value\"", "fictional-xsts-value"),
            ("XBL3.0 x=fictional-hash;fictional-xsts-value", "fictional-xsts-value"),
            ("xbox_token=fictional-xbox-value", "fictional-xbox-value"),
            ("xsts_token=fictional-xsts-value", "fictional-xsts-value"),
            ("Authorization: Custom fictional-header-value", "fictional-header-value"),
            ("Authorization: Bearer\r\nfictional-split-value", "fictional-split-value"),
            ("Cookie: sid=fictional-cookie-value", "fictional-cookie-value"),
            ("refresh_token=fictional-refresh-value", "fictional-refresh-value"),
            ("client_secret=fictional-client-value", "fictional-client-value"),
            ("api_key=fictional-api-value", "fictional-api-value"),
            ("Password=fictional-password-value;Server=example.invalid", "fictional-password-value"),
            ("user fixture-person@example.invalid", "fixture-person@example.invalid"),
            (@"failed 'C:\Users\Fixture Person\AppData\game.log'", "Fixture Person"),
            ("failed /home/fixture-person/.minecraft/logs/latest.log", "fixture-person"),
            ("failed /Users/fixture-person/Library/game.log", "fixture-person"),
            ("failed /workspace/scratch/fixture-device/build.log", "fixture-device"),
            (@"failed \\fixture-device\share\log", "fixture-device"),
            ("ComputerName=fixture-device", "fixture-device"),
        };
        foreach (var (input, secret) in cases)
        {
            var output = redactor.Redact(input);
            if (output.Contains(secret, StringComparison.Ordinal) || output.Contains('\r') || output.Contains('\n'))
                throw new Exception("Public security redaction assertion failed (values intentionally omitted).");
        }
        if (redactor.Redact("OpenGL error code 1281") != "OpenGL error code 1281" ||
            redactor.Redact("Minecraft Services HTTP 403 Forbidden") != "Minecraft Services HTTP 403 Forbidden")
            throw new Exception("Redaction hid a legitimate diagnostic status.");
        var privateKey = "-----BEGIN " + "PRIVATE KEY----- fictional-private-key-value";
        if (redactor.Redact(privateKey).Contains("fictional-private-key-value"))
            throw new Exception("Private key diagnostic not filtered.");
        var jwt = "eyJmaWN0aW9uYWw" + ".ZmljdGlvbmFs" + ".ZmljdGlvbmFs";
        if (redactor.Redact(jwt).Contains(jwt)) throw new Exception("JWT-shaped diagnostic not filtered.");
        Console.WriteLine("PASS public security: 24 credential/state/path cases, encoded URL, multiline header, JWT and preserved HTTP403/OpenGL diagnostics");
    }
}
