// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using XG.Core;

namespace XGLauncher;

internal static class Localizer
{
    private static Dictionary<string, string> Read(string locale) {
        using var stream = typeof(Localizer).Assembly.GetManifestResourceStream("XG.Strings." + locale + ".json");
        return stream == null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
    internal static readonly IReadOnlyDictionary<string, string> English = Read("en-US");
    private sealed record Pattern(string Key, string English, Regex Matcher, int[] Indices);
    private static readonly Pattern[] patterns = English.Where(x => x.Key.Contains('{')).Select(x => {
        var placeholders = Regex.Matches(x.Key, @"\{(\d+)\}");
        var expression = Regex.Escape(x.Key);
        foreach (Match p in placeholders) expression = expression.Replace(Regex.Escape(p.Value), "(.*?)", StringComparison.Ordinal);
        return new Pattern(x.Key, x.Value, new Regex("\\A" + expression + "\\z", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(30)),
            placeholders.Select(x => int.Parse(x.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray());
    }).OrderByDescending(x => x.Key.Length - x.Indices.Length * 3).ToArray();
    internal static string Language { get; private set; } = "zh-CN";
    internal static event Action? Changed;
    internal static void Load(string root) {
        string language = "zh-CN";
        try { if (File.Exists(Path.Combine(root, "language.json"))) language = JsonSerializer.Deserialize<string>(File.ReadAllText(Path.Combine(root, "language.json"))) ?? "zh-CN"; }
        catch (Exception error) when (error is IOException or JsonException) { System.Diagnostics.Trace.TraceWarning(error.GetType().Name); }
        Set(language);
    }
    internal static void Set(string language, string? root = null) {
        language = language == "en-US" ? "en-US" : "zh-CN";
        if (root != null) InstanceStore.WriteJson(Path.Combine(root, "language.json"), language);
        Language = language;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;
        Changed?.Invoke();
    }
    internal static string Text(string? value) => Translate(value ?? "", 0);
    private static string Translate(string value, int depth) {
        if (Language != "en-US" || depth > 5 || !Regex.IsMatch(value, "[\\u4e00-\\u9fff]")) return value;
        if (English.TryGetValue(value, out var direct)) return direct;
        foreach (var entry in English.Where(x => !x.Key.Contains('{') && x.Key.Length > 2 && (x.Key.EndsWith(' ') || x.Key.EndsWith('：'))).OrderByDescending(x => x.Key.Length))
            if (value.StartsWith(entry.Key, StringComparison.Ordinal)) return entry.Value + Translate(value[entry.Key.Length..], depth + 1);
        foreach (var pattern in patterns) {
            var match = pattern.Matcher.Match(value); if (!match.Success) continue;
            var arguments = new object[pattern.Indices.DefaultIfEmpty(-1).Max() + 1];
            for (var i = 0; i < pattern.Indices.Length; i++) arguments[pattern.Indices[i]] = Translate(match.Groups[i + 1].Value, depth + 1);
            var key = pattern.English;
            if (arguments.Length > 0 && Convert.ToString(arguments[0], CultureInfo.InvariantCulture) == "1" && English.TryGetValue(pattern.Key + "|one", out var one)) key = one;
            return string.Format(CultureInfo.GetCultureInfo("en-US"), key, arguments);
        }
        foreach (var separator in new[] { "\n", " · ", "：", ": ", "　" }) {
            if (!value.Contains(separator, StringComparison.Ordinal)) continue;
            return string.Join(separator == "：" ? ": " : separator, value.Split(separator).Select(x => Translate(x, depth + 1)));
        }
        return value; // User data, file paths and legal text are never machine-translated.
    }
}
