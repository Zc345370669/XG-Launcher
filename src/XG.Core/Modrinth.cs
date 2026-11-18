// SPDX-License-Identifier: MIT
using System.Net.Http.Json;
using System.Text.Json;

namespace XG.Core;

public sealed record Project(string Id, string Slug, string Name, string Description, string Author,
    string? Icon, long Downloads, bool Pack)
{
    public string[] GameVersions { get; init; } = [];
    public string[] Loaders { get; init; } = [];

    public string Url => "https://modrinth.com/" + (Pack ? "modpack/" : "mod/") + Uri.EscapeDataString(Slug);
}
public sealed record ModFile(string Name, Uri Url, long Size, string Sha512);
public sealed record ModDependency(string Type, string ProjectId, string VersionId, string FileName)
{
    public override string ToString() => string.Join(" / ", new[] { ProjectId, VersionId, FileName }.Where(s => s.Length > 0));
}

public sealed record ProjectVersion(string Id, string Number, ModFile File, string[] Required, string[] Incompatible)
{
    public string[] GameVersions { get; init; } = [];
    public string[] Loaders { get; init; } = [];
    public string ProjectId { get; init; } = "";
    public ModDependency[] Dependencies { get; init; } = [];
}

public sealed record ProjectPage(Project[] Results, int? TotalHits);

public sealed class Modrinth(HttpClient http)
{
    private const string Api = "https://api.modrinth.com/v2/";
    public async Task<Project[]> SearchAsync(string query, bool pack, string version, string loader, string category, string sort, CancellationToken cancellationToken = default) =>
        (await SearchPageAsync(query, pack, version, loader, category, sort, 0, 30, cancellationToken)).Results;

    public async Task<ProjectPage> SearchPageAsync(string query, bool pack, string version, string loader, string category,
        string sort, int offset, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);
        List<string[]> facets = [[pack ? "project_type:modpack" : "project_type:mod"]];
        if (version.Length > 0) facets.Add(["versions:" + version]);
        if (loader.Length > 0 && loader != "原版") facets.Add(["categories:" + loader.ToLowerInvariant()]);
        if (category.Length > 0) facets.Add(["categories:" + category]);
        var response = await http.GetFromJsonAsync<JsonElement>(Api + $"search?limit={limit}&offset={offset}&query=" +
            Uri.EscapeDataString(query) + "&index=" + Uri.EscapeDataString(sort) + "&facets=" +
            Uri.EscapeDataString(JsonSerializer.Serialize(facets)), cancellationToken);
        var hits = response.GetProperty("hits").EnumerateArray().Select(h => new Project(
            S(h,"project_id"), S(h,"slug"), S(h,"title"), S(h,"description"), S(h,"author"),
            S(h,"icon_url"), h.GetProperty("downloads").GetInt64(), pack)
        {
            GameVersions = Values(h, "versions"),
            Loaders = Values(h, "categories").Where(IsLoader).ToArray()
        }).ToArray();
        int? total = response.TryGetProperty("total_hits", out var count) && count.TryGetInt32(out var number) && number >= 0
            ? number : null;
        return new ProjectPage(hits, total);
    }

    public async Task<string> LicenseAsync(Project project, CancellationToken cancellationToken = default)
    {
        var data = await http.GetFromJsonAsync<JsonElement>(Api + "project/" + Uri.EscapeDataString(project.Id), cancellationToken);
        return S(data.GetProperty("license"),"id");
    }

    public async Task<ProjectVersion[]> VersionsAsync(Project project, Instance? instance, CancellationToken cancellationToken = default)
    {
        var url = Api + "project/" + Uri.EscapeDataString(project.Id) + "/version";
        if (instance != null) url += "?game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { instance.Version }));
        if (instance != null && instance.Loader != "原版")
            url += "&loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { instance.Loader.ToLowerInvariant() }));
        var data = await http.GetFromJsonAsync<JsonElement>(url, cancellationToken);
        return data.EnumerateArray().Where(x => x.GetProperty("files").GetArrayLength() != 0)
            .Select(x => ReadVersion(x, project.Id)).ToArray();
    }

    public async Task<ProjectVersion> VersionAsync(string versionId, CancellationToken token)
    {
        var data = await http.GetFromJsonAsync<JsonElement>(Api + "version/" + Uri.EscapeDataString(versionId), token);
        return ReadVersion(data);
    }

    public async Task<IReadOnlyDictionary<string, ProjectVersion>> VersionsForHashesAsync(string[] sha512, CancellationToken token)
    {
        var result = new Dictionary<string, ProjectVersion>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in sha512.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(100))
        {
            using var response = await http.PostAsJsonAsync(Api + "version_files",
                new { hashes = batch, algorithm = "sha512" }, token);
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
            foreach (var entry in data.EnumerateObject()) result[entry.Name] = ReadVersion(entry.Value);
        }
        return result;
    }

    private static ProjectVersion ReadVersion(JsonElement value, string fallbackProject = "")
    {
        var files = value.GetProperty("files").EnumerateArray().ToArray();
        if (files.Length == 0) throw new InvalidDataException("版本不包含可用文件。");
        var file = files.FirstOrDefault(f => f.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.True, files[0]);
        var dependencies = value.TryGetProperty("dependencies", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(d => new ModDependency(S(d,"dependency_type"), S(d,"project_id"), S(d,"version_id"), S(d,"file_name"))).ToArray()
            : [];
        return new ProjectVersion(S(value, "id"), S(value, "version_number"),
            new ModFile(S(file,"filename"), new Uri(S(file,"url")), file.GetProperty("size").GetInt64(), S(file.GetProperty("hashes"),"sha512")),
            dependencies.Where(d => d.Type == "required").Select(d => d.ToString()).ToArray(),
            dependencies.Where(d => d.Type == "incompatible").Select(d => d.ToString()).ToArray())
        {
            ProjectId = S(value, "project_id") is { Length: > 0 } pid ? pid : fallbackProject,
            GameVersions = Values(value, "game_versions"), Loaders = Values(value, "loaders"), Dependencies = dependencies
        };
    }

    private static bool IsLoader(string value) => value is "fabric" or "forge" or "neoforge" or "quilt" or "liteloader" or "rift" or "minecraft";

    private static string[] Values(JsonElement value, string key) =>
        value.TryGetProperty(key, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray() : [];

    private static string S(JsonElement value, string key) => value.TryGetProperty(key, out var property)
        && property.ValueKind == JsonValueKind.String ? property.GetString()! : "";
}
