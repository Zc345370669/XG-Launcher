// SPDX-License-Identifier: MIT
using System.Text.Json;

namespace XG.Core;

public sealed record GameImportCandidate(string Name, string Profile, ImportInspection Inspection, string Status, bool Isolated = false);
public sealed record GameImportScan(IReadOnlyList<GameImportCandidate> Instances, IReadOnlyList<string> Issues);

public static partial class GameImport
{
    private static JsonElement Property(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var result) ? result : default;

    private static string? String(JsonElement value, string name)
    {
        var result = Property(value, name);
        return result.ValueKind == JsonValueKind.String ? result.GetString() : null;
    }

    private static IEnumerable<JsonElement> Elements(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];

    private static bool Link(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    public static GameImportScan Scan(string source, CancellationToken token = default)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        if (source == Path.GetPathRoot(source)) throw new IOException("请选择启动器或游戏文件夹。");
        for (string? path = source; path != null; path = Path.GetDirectoryName(path))
            if (Link(path)) throw new IOException("扫描来源不能包含目录链接。");
        var candidates = new List<GameImportCandidate>();
        var issues = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((source, 0));
        var catalogRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Path.GetFileName(source) is "profiles" or "instances")
            ReadCatalog(Path.GetDirectoryName(source)!, source);
        else if (Path.GetDirectoryName(source) is { } group && Path.GetFileName(group) is "profiles" or "instances")
            ReadCatalog(Path.GetDirectoryName(group)!, source);
        while (seen.Count < 512 && candidates.Count < 512 && pending.TryDequeue(out var item))
        {
            token.ThrowIfCancellationRequested();
            var (directory, depth) = item;
            if (!seen.Add(directory)) continue;
            try
            {
                if (Link(directory)) continue;
                ReadCatalog(directory, source);
                if (candidates.Any(x => x.Profile.Equals(directory, StringComparison.OrdinalIgnoreCase) && x.Inspection.Format == "Modrinth App"))
                    continue;
                var launcherDirectory = Path.GetFileName(directory) is ".minecraft" or "minecraft"
                    ? Path.GetDirectoryName(directory) ?? directory : directory;
                if ((ReadLauncher(launcherDirectory) ?? ReadManagedInstance(directory)) is { } launcher)
                {
                    candidates.Add(launcher);
                    continue;
                }

                var parent = Path.GetDirectoryName(directory);
                var versionDirectory = Path.GetFileName(parent) == "versions";
                var root = versionDirectory ? Path.GetDirectoryName(parent)! : directory;
                var marker = Path.Combine(directory, Path.GetFileName(directory) + ".json");
                string[] files = File.Exists(marker) ? [marker] :
                    versionDirectory ? [] : Directory.EnumerateFiles(directory, "*.json").Take(32).ToArray();
                foreach (var file in files.Take(512 - candidates.Count))
                {
                    token.ThrowIfCancellationRequested();
                    TryRead(file, () =>
                    {
                        if (ReadVersion(file, root) is { } version)
                        {
                            var (format, isolated) = IdentifyVersionLayout(directory, root, versionDirectory);
                            var name = Path.GetFileNameWithoutExtension(file);
                            candidates.Add(new(name, directory,
                                new(root, version.Version, version.Loader, version.Build, format),
                                Status(root, directory, version.Version, version.MissingParent), isolated));
                        }
                    });
                }

                if (depth >= 4 || versionDirectory) continue;
                foreach (var child in Directory.EnumerateDirectories(directory).Take(512))
                {
                    if (pending.Count + seen.Count >= 512) break;
                    if (Path.GetFileName(child) is "assets" or "libraries" or "runtime" or "runtimes" or "mods" or
                        "saves" or "config" or "resourcepacks" or "shaderpacks" or "logs" or "screenshots" or "natives" or "crash-reports")
                        continue;
                    pending.Enqueue((child, depth + 1));
                }
            }
            catch (Exception error) when (IsReadError(error))
            {
                Report(directory, error);
            }
        }
        if (pending.Count > 0 || seen.Count >= 512 || candidates.Count >= 512) issues.Add("已达到扫描数量上限，请选择更具体的目录。");
        return new(candidates.DistinctBy(x => (x.Profile, x.Inspection.Version, x.Inspection.Loader, x.Inspection.LoaderVersion)).ToArray(), issues);

        void ReadCatalog(string directory, string selected)
        {
            var database = Path.Combine(directory, "app.db");
            if (!File.Exists(database) || !new[] { "instances", "profiles" }.Any(name => Directory.Exists(Path.Combine(directory, name))) ||
                !catalogRoots.Add(directory)) return;
            TryRead(database, () =>
            {
                if (new[] { database, database + "-wal", database + "-shm" }.Any(path => File.Exists(path) && Link(path)))
                    throw new InvalidDataException("Modrinth App 数据库路径包含链接。");
                foreach (var entry in ModrinthInstanceCatalog.Read(database, token))
                {
                    token.ThrowIfCancellationRequested();
                    if (candidates.Count >= 512) break;
                    TryRead(database, () =>
                    {
                        if (!Instance.IsValidVersion(entry.Version)) return;
                        foreach (var group in new[] { "instances", "profiles" })
                        {
                            var container = Path.Combine(directory, group);
                            var game = InstanceStore.SafeFile(container, entry.Path);
                            if (!Directory.Exists(game) || Link(container) || Link(game) ||
                                !(game.Equals(selected, StringComparison.OrdinalIgnoreCase) ||
                                  game.StartsWith(selected + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
                            var (loader, build) = LoaderInfo(entry.Loader, entry.Build, entry.Version!);
                            candidates.Add(new(string.IsNullOrWhiteSpace(entry.Name) ? entry.Path : entry.Name,
                                game, new(game, entry.Version, loader, build, "Modrinth App"), Status(game, game, entry.Version!, false), true));
                            break;
                        }
                    });
                }
            });
        }

        void TryRead(string path, Action read)
        {
            try { read(); }
            catch (Exception error) when (IsReadError(error)) { Report(path, error); }
        }

        void Report(string path, Exception error) =>
            issues.Add($"无法读取或解析 {path}。{error.Message}");
    }

    private static (string Format, bool Isolated) IdentifyVersionLayout(string directory, string root, bool versionDirectory)
    {
        var isolated = !versionDirectory ||
            new[] { "mods", "saves", "config", "options.txt" }.Any(x => Path.Exists(Path.Combine(directory, x)));
        var pclDirectory = Path.Combine(directory, "PCL");
        var pcl = Path.Combine(pclDirectory, "Setup.ini");
        if (File.Exists(pcl) && !Link(pclDirectory) && !Link(pcl))
        {
            if (new FileInfo(pcl).Length < 1024 * 1024)
            {
                foreach (var line in File.ReadLines(pcl))
                {
                    var setting = line.Split(['=', ':'], 2, StringSplitOptions.TrimEntries);
                    if (setting.Length != 2) continue;
                    if (setting[0] == "VersionArgumentIndieV2" && bool.TryParse(setting[1], out var enabled)) isolated = enabled;
                    if (setting[0] == "VersionArgumentIndie" && setting[1] is "0" or "1") isolated = setting[1] == "1";
                }
            }
            return ("PCL2", isolated);
        }
        var hmcl = Path.Combine(directory, "hmclversion.cfg");
        if (File.Exists(hmcl))
        {
            using var document = OpenMetadata(hmcl);
            isolated = String(document.RootElement, "gameDirType") switch
            {
                "VERSION_FOLDER" => true,
                "ROOT_FOLDER" => false,
                _ => isolated
            };
            return ("HMCL", isolated);
        }
        var official = File.Exists(Path.Combine(root, "launcher_profiles.json")) ||
            File.Exists(Path.Combine(root, "launcher_profiles_microsoft_store.json"));
        return (official ? "Minecraft Launcher" : "Minecraft 目录", isolated);
    }

    public static GameImportCandidate? ManualCandidate(string source)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        if (!Directory.Exists(source) || source == Path.GetPathRoot(source) || Link(source)) return null;
        var hasSettings = File.Exists(Path.Combine(source, "options.txt"));
        var hasData = Directory.Exists(Path.Combine(source, "saves")) || Directory.Exists(Path.Combine(source, "mods"));
        var hasClient = File.Exists(Path.Combine(source, Path.GetFileName(source) + ".jar"));
        if (!hasClient && !(hasSettings && hasData)) return null;
        return new(Path.GetFileName(source), source, new(source, null, "原版", null, "手动补充"),
            "未找到可用版本元数据。请补充版本和加载器，导入后使用安装或修复。", true);
    }

    private static bool IsReadError(Exception error) =>
        error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException;

    private static JsonDocument OpenMetadata(string file)
    {
        if (Link(file) || new FileInfo(file).Length > 16 * 1024 * 1024)
            throw new InvalidDataException("元数据文件为链接或超过大小限制。");
        using var stream = File.OpenRead(file);
        return JsonDocument.Parse(stream);
    }

    private static (string Version, string Loader, string? Build, bool MissingParent)? ReadVersion(string file, string root)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? version = null;
        string? build = null;
        var loader = "原版";
        while (true)
        {
            using var document = OpenMetadata(file);
            var value = document.RootElement;
            var id = String(value, "id");
            var parent = String(value, "inheritsFrom");
            if (id != Path.GetFileNameWithoutExtension(file) || !Instance.IsValidVersion(id) ||
                (String(value, "mainClass") == null && parent == null))
            {
                if (visited.Count > 0) throw new InvalidDataException("父版本元数据无效。");
                if (Path.GetFileName(Path.GetDirectoryName(file)) == Path.GetFileNameWithoutExtension(file))
                    throw new InvalidDataException("版本 JSON 缺少有效的 id 和 mainClass 或 inheritsFrom。");
                return null;
            }
            if (!visited.Add(id!) || visited.Count > 16) throw new InvalidDataException("版本继承循环或层级过多。");
            foreach (var library in Elements(Property(value, "libraries")))
            {
                var coordinate = String(library, "name")?.Split(':');
                if (coordinate is not { Length: >= 3 } || build != null) continue;
                var kind = (coordinate[0], coordinate[1]) switch
                {
                    ("net.fabricmc", "fabric-loader") => "Fabric",
                    ("org.quiltmc", "quilt-loader") => "Quilt",
                    ("net.minecraftforge", "forge" or "fmlloader") => "Forge",
                    ("net.neoforged", "neoforge") => "NeoForge",
                    _ => null
                };
                if (kind == null) continue;
                loader = kind;
                build = kind == "Forge" && coordinate[2].StartsWith((parent ?? id) + "-", StringComparison.Ordinal)
                    ? coordinate[2][((parent ?? id)!.Length + 1)..] : coordinate[2];
            }
            version = parent ?? id!;
            if (parent == null)
            {
                // PCL2 may rename a flattened profile ID to the instance directory name.
                var pcl = Path.Combine(Path.GetDirectoryName(file)!, "PCL", "Setup.ini");
                if (File.Exists(pcl) && (loader is "原版" or "NeoForge"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(id!,
                        @"\A(?<game>1\.[0-9]+(?:\.[0-9]+)?)-NeoForge[_-](?<build>[0-9]+\.[0-9]+\.[0-9]+(?:-beta)?)\z",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success && (build == null || build == match.Groups["build"].Value))
                        return (match.Groups["game"].Value, "NeoForge", match.Groups["build"].Value, false);
                }
                return (version, loader, build, false);
            }
            if (!Instance.IsValidVersion(parent)) throw new InvalidDataException("父版本标识无效。");
            var nested = Path.Combine(root, "versions", parent);
            file = new[] { Path.Combine(Path.GetDirectoryName(file)!, parent + ".json"), Path.Combine(nested, parent + ".json") }
                .FirstOrDefault(File.Exists) ?? "";
            if (file.Length == 0) return (version, loader, build, true);
            var folder = Path.GetDirectoryName(file)!;
            if (Link(folder) || Path.GetDirectoryName(folder) is { } container && Link(container))
                throw new InvalidDataException("父版本路径包含目录链接。");
        }
    }

    private static GameImportCandidate? ReadLauncher(string directory)
    {
        var pack = Path.Combine(directory, "mmc-pack.json");
        var config = Path.Combine(directory, "instance.cfg");
        if (!File.Exists(pack)) return null;
        using var document = OpenMetadata(pack);
        var value = document.RootElement;
        var components = Property(value, "components");
        if (components.ValueKind != JsonValueKind.Array) throw new InvalidDataException("实例组件清单 components 必须是数组。");
        string? version = null;
        string? build = null;
        var loader = "原版";
        foreach (var component in Elements(components))
        {
            var uid = String(component, "uid");
            var componentVersion = String(component, "version");
            switch (uid)
            {
                case "net.minecraft": version = componentVersion; break;
                case "net.fabricmc.fabric-loader": loader = "Fabric"; build = componentVersion; break;
                case "org.quiltmc.quilt-loader": loader = "Quilt"; build = componentVersion; break;
                case "net.minecraftforge": loader = "Forge"; build = componentVersion; break;
                case "net.neoforged": loader = "NeoForge"; build = componentVersion; break;
                case "org.lwjgl" or "org.lwjgl3" or "net.fabricmc.intermediary" or "org.quiltmc.hashed": break;
                default: throw new InvalidDataException($"实例包含无法转换的组件 {uid ?? "（无标识）"}。");
            }
        }
        if (!Instance.IsValidVersion(version)) throw new InvalidDataException("找不到有效的 Minecraft 组件版本。");
        var game = new[] { Path.Combine(directory, ".minecraft"), Path.Combine(directory, "minecraft") }
            .FirstOrDefault(path => Directory.Exists(path) && !Link(path));
        if (game == null) throw new InvalidDataException("实例缺少游戏目录。");
        var launcherRoot = Path.GetDirectoryName(Path.GetDirectoryName(directory) ?? directory);
        var format = launcherRoot != null && File.Exists(Path.Combine(launcherRoot, "prismlauncher.cfg")) ? "Prism Launcher" :
            launcherRoot != null && File.Exists(Path.Combine(launcherRoot, "multimc.cfg")) ? "MultiMC" : "Prism / MultiMC";
        var name = File.Exists(config) && !Link(config) && new FileInfo(config).Length < 1024 * 1024
            ? File.ReadLines(config).FirstOrDefault(line => line.StartsWith("name=", StringComparison.OrdinalIgnoreCase))?[5..]
            : null;
        return new(string.IsNullOrWhiteSpace(name) ? Path.GetFileName(directory) : name, game, new(game, version, loader, build, format),
            Status(game, game, version!, false), true);
    }

    private static GameImportCandidate? ReadManagedInstance(string directory)
    {
        foreach (var (file, format) in new[] { ("minecraftinstance.json", "CurseForge"), ("profile.json", "Modrinth App"), ("instance.json", "ATLauncher") })
        {
            var path = Path.Combine(directory, file);
            if (!File.Exists(path)) continue;
            using var document = OpenMetadata(path);
            var metadata = document.RootElement;
            var launch = Property(metadata, "launcher");
            var forge = Property(metadata, "baseModLoader");
            var version = format switch
            {
                "CurseForge" => String(metadata, "gameVersion") ?? String(forge, "minecraftVersion"),
                "Modrinth App" => String(metadata, "game_version"),
                _ => launch.ValueKind == JsonValueKind.Object ? String(metadata, "id") ?? String(launch, "minecraftVersion") : null
            };
            if (!Instance.IsValidVersion(version)) continue;
            var loaderId = format switch
            {
                "CurseForge" => String(forge, "name"),
                "Modrinth App" => String(metadata, "mod_loader") ?? String(metadata, "loader"),
                _ => String(Property(launch, "loaderVersion"), "type") ?? String(launch, "loader")
            };
            var build = format switch
            {
                "CurseForge" => String(forge, "forgeVersion"),
                "Modrinth App" => String(metadata, "mod_loader_version") ?? String(Property(metadata, "loader_version"), "id") ?? String(metadata, "loader_version"),
                _ => String(Property(launch, "loaderVersion"), "version") ?? String(launch, "loaderVersion")
            };
            var (loader, loaderBuild) = LoaderInfo(loaderId, build, version!);
            var name = String(metadata, "name") ?? String(launch, "name");
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileName(directory);
            return new(name, directory, new(directory, version, loader, loaderBuild, format), Status(directory, directory, version!, false), true);
        }
        return null;
    }

    private static (string Loader, string? Build) LoaderInfo(string? id, string? build, string version)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Equals("vanilla", StringComparison.OrdinalIgnoreCase)) return ("原版", null);
        var loader = new[] { "NeoForge", "Forge", "Fabric", "Quilt" }.FirstOrDefault(kind =>
            id.Equals(kind, StringComparison.OrdinalIgnoreCase) || id.StartsWith(kind + "-", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"未知加载器：{id}");
        if (build == null && id.Length > loader.Length + 1) build = id[(loader.Length + 1)..];
        if (build?.StartsWith(version + "-", StringComparison.Ordinal) == true) build = build[(version.Length + 1)..];
        return (loader, build);
    }

    private static string Status(string root, string profile, string version, bool missingParent)
    {
        var missing = new List<string>();
        if (missingParent) missing.Add("父版本元数据");
        if (!File.Exists(Path.Combine(profile, version + ".jar")) &&
            !File.Exists(Path.Combine(root, "versions", version, version + ".jar"))) missing.Add("游戏本体");
        if (!Directory.Exists(Path.Combine(root, "libraries"))) missing.Add("Libraries");
        if (!Directory.Exists(Path.Combine(root, "assets", "indexes"))) missing.Add("Assets");
        return missing.Count == 0 ? "已找到基础资源，安装状态仍需校验" : "待校验或补齐：" + string.Join("、", missing);
    }

    public static Task CopyCandidateAsync(InstanceStore store, Instance instance, GameImportCandidate candidate, bool keepData,
        IProgress<MigrationFile>? progress, CancellationToken token, Action<string>? sharedConflict = null) =>
        IsolatedGameImport.CopyAsync(store, instance, candidate.Inspection.Root, store.DirectoryFor(instance), progress, token,
            candidate.Profile, keepData, inheritUserData: !candidate.Isolated, sharedConflict: sharedConflict);
}
