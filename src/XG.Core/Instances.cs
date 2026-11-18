// SPDX-License-Identifier: MIT
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XG.Core;

public sealed record Instance(Guid Id, string Name, string Version, string Loader = "原版",
    string? Java = null, int Memory = 4096, int Width = 1280, int Height = 720,
    DateTimeOffset? LastPlayed = null, bool Pack = false, string? LoaderVersion = null, bool? AutomaticMemory = null, bool? AutomaticResolution = null)
{
    public static bool IsValidVersion(string? version) => version != null &&
        Regex.IsMatch(version, @"\A[A-Za-z0-9](?:[A-Za-z0-9._+ -]{0,68}[A-Za-z0-9_+-])?\z");

    public void Validate()
    {
        if (Id == Guid.Empty) throw new ArgumentException("实例标识无效。");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80) throw new ArgumentException("实例名称须为 1–80 个字符。");
        if (!IsValidVersion(Version)) throw new ArgumentException("Minecraft 版本标识无效。");
        if (Memory is < 512 or > 65536) throw new ArgumentException("内存须为 512–65536 MB。");
        if (Width is < 320 or > 7680 || Height is < 240 or > 4320) throw new ArgumentException("分辨率超出支持范围。");
    }

    public static string ValidatePlayer(string name) =>
        Regex.IsMatch(name, @"\A[A-Za-z0-9_]{3,16}\z") ? name
        : throw new ArgumentException("玩家名须为 3–16 位英文字母、数字或下划线。");
}

public sealed class InstanceStore
{
    public string Root { get; }
    public string[] CachedJavaPaths() {
        try { return JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(Root,"java-catalog.json"))) ?? []; }
        catch(Exception e) when(e is IOException or JsonException) { return []; }
    }
    public string? JavaBinding(Guid id) {
        try { var bindings=JsonSerializer.Deserialize<Dictionary<Guid,string>>(File.ReadAllText(Path.Combine(Root,"java-bindings.json"))); return bindings?.GetValueOrDefault(id); }
        catch(Exception e) when(e is IOException or JsonException) { return null; }
    }
    public void BindJava(Guid id,string path) {
        var target=Path.Combine(Root,"java-bindings.json");
        Dictionary<Guid,string> bindings;
        try { bindings=JsonSerializer.Deserialize<Dictionary<Guid,string>>(File.ReadAllText(target))??[]; }catch(Exception e)when(e is IOException or JsonException){bindings=[];}
        bindings[id]=path;WriteJson(target,bindings);
        WriteJson(Path.Combine(Root,"java-catalog.json"),CachedJavaPaths().Append(path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
    public string InstanceRoot { get; }
    public string? SharedMinecraft { get; }
    public string ManagedJavaRoot {
        get {
            if(File.Exists(Path.Combine(Root,"runtime-layout-v2")))return Path.Combine(Workspace,"runtimes");
            var legacy=Path.Combine(SharedMinecraft??Root,"runtimes");
            return Directory.Exists(legacy) ? legacy : Path.Combine(Workspace,"runtimes");
        }
    }
    private readonly string index;
    private readonly List<Instance> entries;
    private readonly Dictionary<Guid, string> directories;
    private readonly string directoryIndex;
    public IReadOnlyList<Instance> Entries => entries.AsReadOnly();

    public InstanceStore(string root, string? minecraft = null)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        SharedMinecraft = minecraft is null ? null : Path.GetFullPath(minecraft);
        InstanceRoot = Path.Combine(SharedMinecraft ?? Root, "instances");
        Directory.CreateDirectory(InstanceRoot);
        index = Path.Combine(Root, "instances.json");
        entries = File.Exists(index) ? JsonSerializer.Deserialize<List<Instance>>(File.ReadAllText(index))
            ?? throw new InvalidDataException("实例索引为空。") : [];
        foreach (var instance in entries) instance.Validate();
        directoryIndex = Path.Combine(Root, "instance-directories.json");
        directories = File.Exists(directoryIndex) ? JsonSerializer.Deserialize<Dictionary<Guid, string>>(File.ReadAllText(directoryIndex)) ?? [] : [];
        // Existing records retain their original directory even if it has not been created yet.
        foreach (var instance in entries) directories.TryAdd(instance.Id, instance.Id.ToString("N"));
        foreach (var value in directories.Values) SafeFile(InstanceRoot, value);
    }

    private const string PortableJavaPrefix = "portable:";
    private string Workspace => SharedMinecraft is { } shared ? Path.GetDirectoryName(shared)! : Root;

    public string? StoreJavaPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var absolute = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(Workspace, absolute);
        return !Path.IsPathRooted(relative) && relative.Split(Path.DirectorySeparatorChar)[0] != ".."
            ? PortableJavaPrefix + relative.Replace('\\', '/') : absolute;
    }

    public string? ResolveJavaPath(string? stored) =>
        stored is { } value && value.StartsWith(PortableJavaPrefix, StringComparison.Ordinal)
            ? value[PortableJavaPrefix.Length..].Split('/').Aggregate(Workspace, SafeFile)
            : stored;

    public string DirectoryFor(Instance instance)
    {
        lock (directories)
        {
            if (directories.TryGetValue(instance.Id, out var existing)) return SafeFile(InstanceRoot, existing);
            var loader = instance.Loader is "原版" or "Vanilla" ? "Vanilla" : instance.Loader;
            var stem = Regex.Replace(instance.Version + "-" + loader, @"[^A-Za-z0-9._+-]", "_").TrimEnd('.', ' ');
            var budget = Math.Min(100, 235 - InstanceRoot.Length - 12);
            if (budget < 12) throw new PathTooLongException("实例根目录过长，请选择更短的便携目录。");
            stem = stem[..Math.Min(stem.Length, budget)];
            var used = Directory.EnumerateFileSystemEntries(InstanceRoot).Select(Path.GetFileName)
                .Concat(directories.Values).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var candidate = stem;
            for (var n = 2; used.Contains(candidate); n++) candidate = stem + "-" + n;
            var path = SafeFile(InstanceRoot, candidate);
            directories.Add(instance.Id, candidate);
            try { WriteJson(directoryIndex, directories); }
            catch { directories.Remove(instance.Id); throw; }
            return path;
        }
    }
    public void Save(Instance instance)
    {
        instance.Validate();
        _ = DirectoryFor(instance);
        var next = entries.ToList();
        var position = next.FindIndex(item => item.Id == instance.Id);
        if (position < 0) next.Add(instance);
        else next[position] = instance;
        WriteJson(index, next);
        entries.Clear();
        entries.AddRange(next);
    }

    public async Task<Instance> ImportLegacyAsync(InstanceStore legacy, Instance source, string backup)
    {
        if (Path.GetFullPath(legacy.InstanceRoot).Equals(InstanceRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("来源与目标是同一个实例目录。");
        var clone = source with { Id = Guid.NewGuid() };
        var origin = legacy.EnsureLocalDirectory(source);
        var destination = DirectoryFor(clone);
        try
        {
            await Task.Run(() =>
            {
                Backups.Create(legacy, source, backup);
                CopyTree(origin, destination);
            });
            Save(clone);
            return clone;
        }
        catch
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            throw;
        }
    }

    public async Task<Instance> CopyAsync(Instance source)
    {
        var clone = source with { Id = Guid.NewGuid(), Name = source.Name[..Math.Min(76, source.Name.Length)] + " 副本",
            LastPlayed = null };
        var destination = DirectoryFor(clone);
        var origin = EnsureLocalDirectory(source);
        try
        {
            await Task.Run(() => CopyTree(origin, destination));
            Save(clone);
            return clone;
        }
        catch
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            throw;
        }
    }

    public void Remove(Instance instance, bool deleteFiles)
    {
        var directory = DirectoryFor(instance);
        var staged = directory + ".removed-" + Guid.NewGuid().ToString("N");
        var move = deleteFiles && Directory.Exists(directory);
        if (move)
        {
            EnsureLocalDirectory(instance);
            CheckTree(directory);
            Directory.Move(directory, staged);
        }
        try
        {
            var next = entries.Where(item => item.Id != instance.Id).ToList();
            WriteJson(index, next);
            entries.RemoveAll(item => item.Id == instance.Id);
        }
        catch
        {
            if (move) Directory.Move(staged, directory);
            throw;
        }
        if (move) Directory.Delete(staged, recursive: true);
    }

    public string EnsureLocalDirectory(Instance instance)
    {
        var directory = DirectoryFor(instance);
        if (IsLink(InstanceRoot) || (Directory.Exists(directory) && IsLink(directory)))
            throw new IOException("实例目录是符号链接或目录联接。");
        return directory;
    }

    private static bool IsLink(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    private static void CheckTree(string source)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            if (IsLink(entry)) throw new IOException("实例含符号链接或目录联接，请先手动移除这些链接。");
            if (Directory.Exists(entry)) CheckTree(entry);
        }
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        if (!Directory.Exists(source)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            if (IsLink(entry)) throw new IOException("实例含符号链接或目录联接，无法安全复制。");
            var target = SafeFile(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry)) CopyTree(entry, target);
            else File.Copy(entry, target);
        }
    }

    public static void WriteJson<T>(string path, T data)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Open(temporary, FileMode.CreateNew))
            {
                JsonSerializer.Serialize(stream, data, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }

    public static string SafeFile(string root, string filename)
    {
        if (filename is "." or ".." || filename != Path.GetFileName(filename) ||
            filename.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0 ||
            filename.Any(char.IsControl) || string.IsNullOrWhiteSpace(filename) ||
            filename.EndsWith('.') || filename.EndsWith(' ') ||
            Regex.IsMatch(filename.Split('.')[0], @"\A(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])\z", RegexOptions.IgnoreCase))
            throw new InvalidDataException("文件名无效。");
        return Path.Combine(root, filename);
    }
}
