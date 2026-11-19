// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XGLauncher;

internal sealed record LegalDocument(string Id, string Project, string Type, string? Resource, string? OfficialUrl, string Role)
{
    internal string? Read() => Resource == null ? null : LegalCatalog.Read(Resource);
}
internal sealed record CreditProject(string Name, string Role, IReadOnlyList<LegalDocument> Documents);

internal static class LegalCatalog
{
    private const string Prefix = "XG.Licenses.";
    internal static readonly Assembly Assembly = typeof(LegalCatalog).Assembly;
    internal static IReadOnlyList<string> Files => Assembly.GetManifestResourceNames().Where(x => x.StartsWith(Prefix, StringComparison.Ordinal))
        .Select(x => x[Prefix.Length..]).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    internal static string Read(string name)
    {
        using var stream = Assembly.GetManifestResourceStream(Prefix + name) ?? throw new FileNotFoundException("缺少内置许可证文件。", name);
        using var reader = new StreamReader(stream, Encoding.UTF8, true); return reader.ReadToEnd();
    }
    internal static string Digest(string name) {
        using var stream = Assembly.GetManifestResourceStream(Prefix + name) ?? throw new FileNotFoundException(name);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static readonly Dictionary<string, (string Status, string Scope)> evidence = ReadEvidence();
    private static Dictionary<string, (string, string)> ReadEvidence() {
        using var stream = Assembly.GetManifestResourceStream(Prefix + "LICENSE-EVIDENCE.json");
        if (stream == null) return [];
        using var data = JsonDocument.Parse(stream);
        return data.RootElement.EnumerateArray().ToDictionary(x => x.GetProperty("file").GetString()!,
            x => (x.GetProperty("status").GetString()!, x.GetProperty("scope").GetString()!));
    }
    internal static long Length(string name) { using var stream = Assembly.GetManifestResourceStream(Prefix + name)!; return stream.Length; }
    private static string Role(string name, string role) {
        if (!evidence.TryGetValue(name, out var value)) return role;
        role = value.Scope switch {
            "other-platform-reference" => "其他平台依赖参考，不表示其二进制随 Windows EXE 分发。",
            "legacy-reference-not-published" => "历史参考文件，不对应当前 Windows 发布依赖。",
            "build-or-installer-reference" => "构建或安装工具参考，不随 EXE 分发工具二进制。",
            "metadata" => "文件清单或项目说明，不是第三方授权正文。", _ => role
        };
        if (value.Status.Contains("version-reference", StringComparison.Ordinal)) role += " · 原文已核对上游；具体包版本对应关系待核对。";
        if (value.Status.Contains("unverified", StringComparison.Ordinal) || value.Status.Contains("unknown", StringComparison.Ordinal)) role += " · 原始文件来源尚未独立核实，不能视为合规结论。";
        return role;
    }
    internal static LegalDocument File(string name, string project, string role = "随程序提供的依赖许可或通知") =>
        new(name, project, name.Contains("NOTICE", StringComparison.OrdinalIgnoreCase) ? "NOTICE / 版权声明" :
            name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || name.Contains("INVENTORY") ? "文件与来源清单" : "许可证与版权声明", name, null, Role(name, role));
    internal static LegalDocument Online(string id, string project, string type, string url) => new(id, project, type, null, url,
        "官方在线条款，不是随 EXE 分发的开源依赖许可证");
    private static IReadOnlyList<LegalDocument> Matching(string prefix, string project, string role) =>
        Files.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(x => File(x, project, role)).ToArray();
    internal static IReadOnlyList<CreditProject> Credits => new[] {
        new CreditProject("Mojang Studios", "游戏元数据与资源服务", new[] { Eula, Guidelines }),
        new CreditProject("Microsoft", "账号服务与身份库", new[] { Online("microsoft-terms", "Microsoft", "Microsoft 服务协议", "https://www.microsoft.com/en-US/servicesagreement") }
            .Concat(Matching("Microsoft.Identity.Client", "Microsoft MSAL", "随程序分发的身份库；此许可不替代微软服务条款")).ToArray()),
        new CreditProject("Modrinth", "模组、整合包与 API 服务", new[] { Online("modrinth-terms", "Modrinth", "服务与 API 使用条款", "https://modrinth.com/legal/terms") }),
        new CreditProject("NeoForge", "按需安装的加载器", Matching("NeoForge-upstream-", "NeoForge", "上游参考许可；加载器按需下载，不随 EXE 分发")),
        new CreditProject("Forge", "按需安装的加载器", Matching("Forge-upstream-", "Forge", "上游参考许可；加载器按需下载，不随 EXE 分发")),
        new CreditProject("Fabric", "按需安装的加载器", Matching("Fabric-upstream-", "Fabric", "上游参考许可；加载器按需下载，不随 EXE 分发")),
        new CreditProject("Quilt", "按需安装的加载器", Matching("Quilt-upstream-", "Quilt", "上游参考许可；加载器按需下载，不随 EXE 分发")),
        new CreditProject(".NET", "随程序提供的运行时", Matching("dotnet-", ".NET", "Windows 自包含发布运行时")),
        new CreditProject("Lucide", "界面图标", Matching("Lucide", "Lucide", "随程序分发的图标资源及版权声明")),
        new CreditProject("Avalonia UI", "界面框架", Matching("Avalonia", "Avalonia UI", "随程序分发的 UI 框架与相关本地组件")),
        new CreditProject("OpenJDK", "按需安装的 Java 运行时", Matching("OpenJDK-upstream-", "OpenJDK", "OpenJDK 上游参考许可；实际 Java 发行版以其随附文件为准，不随 EXE 分发")),
        new CreditProject("CmlLib", "游戏启动与安装库", Matching("CmlLib", "CmlLib", "随程序分发的游戏启动与安装库"))
    };
    internal static LegalDocument Eula => Online("minecraft-eula", "Minecraft", "Minecraft 最终用户许可协议", "https://www.minecraft.net/en-us/eula");
    internal static LegalDocument Guidelines => Online("minecraft-guidelines", "Minecraft", "Minecraft 使用指南", "https://www.minecraft.net/en-us/usage-guidelines");
    internal static IReadOnlyList<LegalDocument> ThirdParty => Files.Where(x => !x.StartsWith("XGLauncher-", StringComparison.Ordinal) && !x.Contains("upstream-") && !x.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !x.Contains("INVENTORY") && x != "PROJECT-NOTICE.md")
        .Select(x => File(x, x.Split('-')[0])).ToArray();
}
