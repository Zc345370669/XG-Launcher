// SPDX-License-Identifier: MIT
using System.Security.Cryptography;

namespace XG.Core;

public sealed record InstalledMod(string Path, ProjectVersion? Version);
public sealed record ModInstallPlan(ProjectVersion[] Files, string[] Optional);

public sealed class ModInstallPlanner(Modrinth catalog)
{
    public static bool Compatible(ProjectVersion value, Instance instance) =>
        value.GameVersions.Contains(instance.Version, StringComparer.OrdinalIgnoreCase) &&
        value.Loaders.Contains(instance.Loader, StringComparer.OrdinalIgnoreCase);

    public async Task<InstalledMod[]> IdentifyAsync(IEnumerable<string> files, CancellationToken token)
    {
        var paths = new List<(string Path, string Hash)>();
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            {
                paths.Add((file, ""));
                continue;
            }
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            paths.Add((file, Convert.ToHexString(await SHA512.HashDataAsync(input, token)).ToLowerInvariant()));
        }
        var metadata = await catalog.VersionsForHashesAsync(paths.Where(x => x.Hash.Length > 0).Select(x => x.Hash).ToArray(), token);
        return paths.Select(x => new InstalledMod(x.Path, metadata.GetValueOrDefault(x.Hash))).ToArray();
    }

    public async Task<ModInstallPlan> PlanAsync(Project root, ProjectVersion chosen, Instance instance,
        IReadOnlyList<InstalledMod> installed, CancellationToken token)
    {
        var resolved = new Dictionary<string, ProjectVersion>(StringComparer.Ordinal);
        var order = new List<ProjectVersion>();
        var optional = new HashSet<string>(StringComparer.Ordinal);
        var provided = installed.Where(item => !item.Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)).ToArray();
        await VisitAsync(chosen, root.Id);
        var proposed = order.Select(value => new InstalledMod(value.File.Name, value)).Concat(provided).ToArray();
        foreach (var item in resolved.Values)
            foreach (var conflict in item.Dependencies.Where(x => x.Type == "incompatible"))
                if (proposed.Any(other => other.Version?.Id != item.Id && Matches(conflict, other)))
                    throw new InvalidOperationException("检测到不兼容模组：" + conflict);
        foreach (var other in provided)
            if (other.Version?.Dependencies.Any(conflict => conflict.Type == "incompatible" &&
                order.Any(candidate => candidate.Id != other.Version.Id && Matches(conflict, new InstalledMod(candidate.File.Name, candidate)))) == true)
                throw new InvalidOperationException("已安装模组声明与此安装计划不兼容：" + FileName(other.Path));
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in order)
        {
            Downloads.Validate(item.File);
            if (!item.File.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || !targets.Add(item.File.Name) ||
                installed.Any(existing => FileName(existing.Path).Equals(item.File.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"模组文件不适用或文件名冲突：{item.File.Name}");
        }
        return new ModInstallPlan(order.ToArray(), optional.ToArray());

        async Task VisitAsync(ProjectVersion version, string projectId)
        {
            token.ThrowIfCancellationRequested();
            if (projectId.Length == 0 || !Compatible(version, instance) || version.ProjectId.Length > 0 && version.ProjectId != projectId)
                throw new InvalidOperationException("模组版本与目标实例或指定项目不兼容：" + version.Number);
            if (resolved.TryGetValue(projectId, out var previous))
            {
                if (previous.Id != version.Id) throw new InvalidOperationException("依赖版本冲突：" + projectId);
                return;
            }
            if (resolved.Count >= 100) throw new InvalidOperationException("依赖项过多，请分批核对。");
            resolved.Add(projectId, version);
            var already = installed.Where(item => item.Version?.ProjectId == projectId).ToArray();
            if (already.Any(item => item.Version?.Id != version.Id || item.Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("目标实例已有同项目的其他版本或禁用文件：" + projectId);

            foreach (var dependency in version.Dependencies)
            {
                if (dependency.Type == "optional")
                {
                    if (!provided.Any(other => Matches(dependency, other))) optional.Add(dependency.ToString());
                    continue;
                }
                if (dependency.Type != "required") continue;
                if (provided.Any(item => Matches(dependency, item) && item.Version is { } existing && Compatible(existing, instance)))
                    continue;
                if (dependency.VersionId.Length > 0)
                {
                    var exact = await catalog.VersionAsync(dependency.VersionId, token);
                    await VisitAsync(exact, dependency.ProjectId.Length > 0 ? dependency.ProjectId : exact.ProjectId);
                }
                else if (dependency.ProjectId.Length > 0)
                {
                    if (resolved.ContainsKey(dependency.ProjectId)) continue;
                    var target = new Project(dependency.ProjectId, "", "", "", "", null, 0, false);
                    var compatible = (await catalog.VersionsAsync(target, instance, token)).FirstOrDefault(x => Compatible(x, instance));
                    await VisitAsync(compatible ?? throw new InvalidOperationException("无兼容依赖版本：" + dependency), dependency.ProjectId);
                }
                else if (!provided.Any(item => Matches(dependency, item)))
                    throw new InvalidOperationException("缺少无法在 Modrinth 解析的本地依赖：" + dependency);
            }
            if (already.Length == 0) order.Add(version);
        }
    }

    private static string FileName(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? name[..^9] : name;
    }

    private static bool Matches(ModDependency dependency, InstalledMod item)
    {
        if (dependency.VersionId.Length > 0) return item.Version?.Id == dependency.VersionId;
        if (dependency.ProjectId.Length > 0) return item.Version?.ProjectId == dependency.ProjectId;
        return dependency.FileName.Length > 0 && FileName(item.Path).Equals(dependency.FileName, StringComparison.OrdinalIgnoreCase);
    }
}
