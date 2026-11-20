// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;

namespace XG.Core;

/// <summary>
/// 协调官方版本、Java、依赖与加载器准备，并构造隔离实例的游戏进程。
/// </summary>
public sealed partial class GameService(HttpClient http, InstanceStore store)
{
    private readonly SemaphoreSlim versionsLock = new(1);
    private readonly SemaphoreSlim installationLock = new(1);
    public sealed record OfficialVersion(string Id, string Type, string? Url = null, DateTimeOffset? Released = null);

    public async Task<string[]> VersionsAsync() => (await OfficialVersionsAsync())
        .Where(version => version.Type == "release").Select(version => version.Id).ToArray();

    public OfficialVersion[] CachedVersions() => ReadCache(Path.Combine(store.Root, "official-versions.json"));

    private static OfficialVersion[] ReadCache(string path)
    {
        try { return JsonSerializer.Deserialize<OfficialVersion[]>(File.ReadAllText(path)) ?? []; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    public async Task<OfficialVersion[]> OfficialVersionsAsync(bool refresh = false, CancellationToken token = default)
    {
        await versionsLock.WaitAsync(token);
        try
        {
            var path = Path.Combine(store.Root, "official-versions.json");
            var cached = CachedVersions();
            if (!refresh && cached.Length > 0 && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromHours(6))
                return cached;
            var manifest = await http.GetFromJsonAsync<JsonElement>(
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", token);
            var versions = manifest.GetProperty("versions").EnumerateArray().Select(v =>
                new OfficialVersion(v.GetProperty("id").GetString()!, v.GetProperty("type").GetString()!,
                    v.GetProperty("url").GetString(), v.TryGetProperty("releaseTime", out var time) &&
                        time.TryGetDateTimeOffset(out var released) ? released : null))
                .OrderByDescending(version => version.Released).ToArray();
            InstanceStore.WriteJson(path, versions);
            return versions;
        }
        finally { versionsLock.Release(); }
    }

    public sealed record LoaderVersion(string Id, bool Stable, bool Recommended = false);

    public async Task<string[]> LoaderVersionsAsync(string game, string loader, CancellationToken token = default) =>
        (await CompatibleLoadersAsync(game, loader, token)).Select(x => x.Id).ToArray();

    public async Task<LoaderVersion[]> CompatibleLoadersAsync(string game, string loader, CancellationToken token = default)
    {
        if (loader is "Forge" or "NeoForge")
        {
            var launcher = new MinecraftLauncher(new MinecraftPath(store.Root));
            if (loader == "NeoForge")
            {
                var releases = await new NeoForgeInstaller(launcher, http).GetNeoForgeVersions(game).WaitAsync(token);
                return releases.Select(x => new LoaderVersion(x.NeoForgeVersionName, StableReleaseId(x.NeoForgeVersionName))).ToArray();
            }
            var versions = await new ForgeInstaller(launcher, http).GetForgeVersions(game).WaitAsync(token);
            var ids = versions.Select(x => x.ForgeVersionName).ToArray();
            if (ids.Length == 0) return [];
            string? recommended = null;
            try
            {
                var manifest = await http.GetFromJsonAsync<JsonElement>(
                    "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json", token);
                if (manifest.TryGetProperty("promos", out var promos) && promos.TryGetProperty(game + "-recommended", out var entry))
                    recommended = entry.GetString();
            }
            catch (Exception e) when (e is HttpRequestException or JsonException && !token.IsCancellationRequested)
            { Trace.TraceInformation("Forge promotion unavailable: " + e.GetType().Name); }
            return ids.Select(id => new LoaderVersion(id, id == recommended, id == recommended)).ToArray();
        }
        if (loader is not ("Fabric" or "Quilt")) return [];
        var url = loader == "Fabric" ? "https://meta.fabricmc.net/v2" : "https://meta.quiltmc.org/v3";
        using var response = await http.GetAsync($"{url}/versions/loader/{Uri.EscapeDataString(game)}", token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return [];
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: token);
        return result.EnumerateArray().Select(x =>
        {
            var info = x.GetProperty("loader");
            var id = info.GetProperty("version").GetString()!;
            var stable = info.TryGetProperty("stable", out var marker)
                ? marker.ValueKind == JsonValueKind.True : StableReleaseId(id);
            return new LoaderVersion(id, stable);
        }).ToArray();
    }

    private static bool StableReleaseId(string version) =>
        System.Text.RegularExpressions.Regex.IsMatch(version, @"\A[0-9]+(?:\.[0-9]+)+\z");

    public async Task<int> RequiredJavaMajorAsync(string version, CancellationToken token = default)
    {
        var versions = await OfficialVersionsAsync(token: token);
        if (Uri.TryCreate(versions.FirstOrDefault(item => item.Id == version)?.Url, UriKind.Absolute, out var link) &&
            link.Scheme == "https" && link.UserInfo.Length == 0 && link.IsDefaultPort &&
            link.Host is "piston-meta.mojang.com" or "launchermeta.mojang.com")
        {
            var metadataResponse = await http.GetFromJsonAsync<JsonElement>(link, token);
            if (metadataResponse.TryGetProperty("javaVersion", out var java) &&
                java.TryGetProperty("majorVersion", out var majorVersion) && majorVersion.TryGetInt32(out var officialMajor))
                return officialMajor;
        }
        var launcher = new MinecraftLauncher(new MinecraftPath(store.SharedMinecraft ?? store.Root));
        var metadata = await launcher.GetVersionAsync(version, token);
        return int.TryParse(metadata.JavaVersion?.MajorVersion, out var major) ? major : 0;
    }

    public Task<Process> PrepareAsync(Instance instance, string player, MSession? online, Action<string> progress) =>
        PrepareWithSessionAsync(instance, player, () => Task.FromResult(online), progress);

    /// <summary>
    /// 准备资源并构造游戏进程；不会在此方法中启动返回的进程。
    /// </summary>
    /// <param name="instance">已选择的实例配置，用于确定版本、加载器和隔离目录。</param>
    /// <param name="player">离线会话所用玩家名，仍需通过原名称验证。</param>
    /// <param name="resolveSession">准备结束后获取会话；正版认证失败应抛出异常，不伪装为离线成功。</param>
    /// <param name="progress">面向调用方的安装状态通知。</param>
    /// <param name="resources">可选的共享安装任务服务，负责真实阶段及恢复信息。</param>
    /// <param name="stage">可选的主任务状态回调。</param>
    /// <param name="verifyManagedJava">是否重新验证受管理Java安装。</param>
    /// <returns>尚未启动、由调用方负责启动和释放的进程对象。</returns>
    public async Task<Process> PrepareWithSessionAsync(Instance instance, string player,
        Func<Task<MSession?>> resolveSession, Action<string> progress, ResourceOperations? resources = null, Action<string>? stage = null, bool verifyManagedJava = false)
    {
        instance.Validate();
        // 解析既有Java绑定；后续仍校验实际运行时和版本要求，不能只信配置路径。
        var manualJava = store.ResolveJavaPath(instance.Java);
        
        var directory = store.EnsureLocalDirectory(instance);
        Directory.CreateDirectory(directory);
        CheckGameDirectory(directory);
        var paths = new InstanceMinecraftPath(store, instance);
        foreach (var folder in new[] { paths.Library, paths.Assets, paths.Runtime, paths.Versions }) Directory.CreateDirectory(folder);
        var launcherParameters = paths.CreateParameters(http);
        var launcher = new MinecraftLauncher(launcherParameters);
        launcher.FileProgressChanged += (_, args) => progress($"{args.EventType}  {args.ProgressedTasks}/{args.TotalTasks}");
        var preparationToken = resources?.Current?.Token ?? default;
        await installationLock.WaitAsync(preparationToken);
        var lockHeld = true;
        try
        {
            stage?.Invoke("准备中");
            var metadata = await launcher.GetVersionAsync(instance.Version, preparationToken);
            var major = int.TryParse(metadata.JavaVersion?.MajorVersion, out var parsedMajor) ? parsedMajor : 0;
            var javaPath = await JavaDiscovery.FindAsync(manualJava ?? store.JavaBinding(instance.Id), paths.Runtime, major, store.CachedJavaPaths());
            verifyManagedJava |= javaPath != null && IsIn(paths.Runtime,javaPath);
            if (javaPath != null && !verifyManagedJava) {
                var extractors = CmlLib.Core.FileExtractors.DefaultFileExtractors.CreateDefault(http,launcherParameters.RulesEvaluator!,launcherParameters.JavaPathResolver!);
                extractors.Java = null;
                launcherParameters.FileExtractors = extractors.ToExtractorCollection();
            }
            if (resources is null)
                await InstallWithRetryAsync(launcher, instance.Version, progress, preparationToken);
            else
            {
                var files = (await launcher.ExtractFiles(metadata)).ToArray();
                files = JavaInstallerCleanup.OwnLegacyPackages(files, paths.Runtime);
                var game = files.Where(file => paths.IsVersionResource(file.Path)).ToArray();
                var assets = files.Where(file => IsIn(paths.Assets, file.Path)).ToArray();
                var dependencies = files.Where(file => !IsIn(paths.Runtime, file.Path) && !paths.IsVersionResource(file.Path) &&
                    !IsIn(paths.Assets, file.Path)).ToArray();
                var java = files.Where(file => IsIn(paths.Runtime, file.Path)).ToArray();
                long Size(CmlLib.Core.Files.GameFile[] group) => group.All(x => x.Size > 0) ? group.Sum(x => x.Size) : 0;
                resources.PlanStages(new[] {
                    ("游戏本体", Size(game), game.Length, false),
                    ("Minecraft Assets", Size(assets), assets.Length, assets.Length == 0),
                    ("资源与依赖", Size(dependencies), dependencies.Length, dependencies.Length == 0),
                    ("Java 环境", javaPath != null && !verifyManagedJava ? 0L : Size(java), javaPath != null && !verifyManagedJava ? 1 : java.Length, javaPath != null && !verifyManagedJava),
                    ("模组加载器", 0L, 0, instance.Loader == "原版") }.Where(stage => instance.Loader != "原版" || stage.Item1 != "模组加载器").ToArray(), instance.Name);
                await resources.RunAsync(instance.Version, "游戏本体", "Minecraft 官方", instance.Name, mark => InstallFilesAsync(game, "游戏本体", mark));
                if (assets.Length > 0)
                    await resources.RunAsync(instance.Version, "Minecraft Assets", "Minecraft 官方", instance.Name,
                        mark => InstallFilesAsync(assets, "Minecraft Assets", mark));
                if (dependencies.Length > 0)
                    await resources.RunAsync(instance.Version, "资源与依赖", "Minecraft 官方", instance.Name, mark => InstallFilesAsync(dependencies, "资源与依赖", mark));
                if (javaPath is null || verifyManagedJava)
                    await resources.RunAsync(major > 0 ? $"Java {major}" : "Java", "Java 环境", "Minecraft 官方", instance.Name, async mark => {
                        using var lease = await JavaInstallGate.EnterAsync(paths.Runtime, resources.Current?.Token ?? default);
                        try {
                            javaPath = await JavaDiscovery.FindAsync(manualJava ?? store.JavaBinding(instance.Id),paths.Runtime,major,store.CachedJavaPaths());
                            if ((javaPath == null || verifyManagedJava) && java.Length > 0) await InstallFilesAsync(java, "Java 环境", mark);
                            mark("校验中");
                            javaPath = await JavaDiscovery.FindAsync(manualJava ?? store.JavaBinding(instance.Id), paths.Runtime, major, store.CachedJavaPaths());
                            if (javaPath == null) throw new InvalidOperationException($"Java {major} 未就绪，请在创建或编辑实例中选择兼容运行时。");
                            await JavaInstallerCleanup.AfterVerifiedAsync(paths.Runtime, javaPath, java, message => LogLoader(directory, message));
                            store.BindJava(instance.Id,javaPath);
                            return true;
                        }
                        catch (Exception error) when (error is not OperationCanceledException) {
                            LogLoader(directory, "Java: " + error.GetType().Name + " · " + error.Message);
                            throw new InvalidOperationException("Java 安装或兼容性检查失败：" + error.Message, error);
                        }
                    });
            }

            async Task<bool> InstallFilesAsync(CmlLib.Core.Files.GameFile[] files, string kind, Action<string>? mark)
            {
                var control = resources?.Current;
                var total = files.All(file => file.Size > 0) ? files.Sum(file => file.Size) : 0;
                long finished = 0;
                for (var i = 0; i < files.Length; i++)
                {
                    var file = files[i];
                    var position = i;
                    var filename = Path.GetFileName(file.Path) ?? file.Name;
                    void Report(long bytes, long size, bool network = false) => resources?.Report(kind, instance.Name, finished + bytes,
                        total, filename, position, files.Length, bytes, size, network);
                    async Task Install(CancellationToken token)
                    {
                        mark?.Invoke("下载中");
                        Report(0, file.Size);
                        if (file.Size > 0 && file.Hash is { Length: 40 or 64 or 128 } checksum && checksum.All(Uri.IsHexDigit) &&
                            file.Path is { } target && Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) &&
                            uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.IsDefaultPort &&
                            uri.Host is "piston-data.mojang.com" or "launcher.mojang.com" or "libraries.minecraft.net" or
                                "resources.download.minecraft.net" or "piston-meta.mojang.com" or "launchermeta.mojang.com")
                        {
                            await SharedResourceCoordinator.RunAsync("commit|" + Path.GetFullPath(target) + "|" + checksum,async () => {
                                using var commit = await JavaInstallGate.EnterAsync(target + ".resource-commit");
                                if (!await VerifiedTransfer.MatchesAsync(target, file.Size, checksum, CancellationToken.None)) {
                                    var part = target + ".xg-part";
                                    await VerifiedTransfer.StageAsync(http, uri, part, file.Size, checksum,(bytes,size)=> { if(!token.IsCancellationRequested) Report(bytes,size,true); },_=>{},CancellationToken.None);
                                    File.Move(part,target,overwrite:true);VerifiedTransfer.ClearMetadata(part);
                                }
                                await file.ExecuteUpdateTask(CancellationToken.None);
                            },token);
                        }
                        else
                        {
                            await launcher.GameInstaller.Install([file],
                                new Progress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>(_ => { }),
                                new Progress<ByteProgress>(value => Report(value.ProgressedBytes, value.TotalBytes, true)), token);
                        }
                        Report(Math.Max(0, file.Size), Math.Max(0, file.Size));
                    }
                    if (control != null) await control.RunFileAsync(Install);
                    else await Install(default);
                    finished += Math.Max(0, file.Size);
                    resources?.Report(kind, instance.Name, finished, total, filename, i + 1, files.Length,
                        Math.Max(0, file.Size), Math.Max(0, file.Size), measureSpeed: false);
                }
                return true;
            }

            if (resources?.Current is { } parentControl) await parentControl.WaitIfPausedAsync();
            stage?.Invoke("安装中");
            var profile = instance.Loader == "原版" ? instance.Version :
                resources is null ? await InstallLoaderAsync(null) : await resources.RunAsync(
                    instance.LoaderVersion ?? instance.Loader, "模组加载器", instance.Loader, instance.Name, InstallLoaderAsync);
            async Task<string> InstallLoaderAsync(Action<string>? loaderStage)
            {
                if (resources?.Current is { } loaderControl) await loaderControl.WaitIfPausedAsync();
                var token = resources?.Current?.Token ?? preparationToken;
                loaderStage?.Invoke("正在执行加载器安装程序…");
                var profile = instance.Version;
                if (instance.Loader is "Fabric" or "Quilt")
                {
                    var loader = instance.Loader;
                    var url = loader == "Fabric" ? "https://meta.fabricmc.net/v2" : "https://meta.quiltmc.org/v3";
                    var choices = await LoaderVersionsAsync(instance.Version, loader, token);
                    var selected = instance.LoaderVersion ?? choices.FirstOrDefault();
                    if (selected is null || !choices.Contains(selected)) throw new InvalidOperationException("加载器版本与游戏版本不兼容。");
                    var raw = await http.GetStringAsync($"{url}/versions/loader/{Uri.EscapeDataString(instance.Version)}/{Uri.EscapeDataString(selected)}/profile/json", token);
                    using var metadata = JsonDocument.Parse(raw);
                    profile = metadata.RootElement.GetProperty("id").GetString()!;
                    if (metadata.RootElement.GetProperty("inheritsFrom").GetString() != instance.Version)
                        throw new InvalidDataException("加载器配置的游戏版本不匹配。");
                    var profilePath = paths.GetVersionJsonPath(profile);
                    var temporary = profilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        await File.WriteAllTextAsync(temporary, raw, token);
                        File.Move(temporary, profilePath, overwrite: true);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    LogLoader(directory, $"{loader} profile {profile} · Minecraft {instance.Version}");
                    await InstallWithRetryAsync(launcher, profile, progress, token);
                }
                else if (instance.Loader is "Forge" or "NeoForge")
                {
                    var compatible = await CompatibleLoadersAsync(instance.Version, instance.Loader, token);
                    if (instance.LoaderVersion != null && !compatible.Any(x => x.Id == instance.LoaderVersion))
                        throw new InvalidOperationException("加载器版本与游戏版本不兼容。");
                    var installerJava = javaPath;
                    if (string.IsNullOrWhiteSpace(installerJava)) installerJava = launcher.GetJavaPath(metadata);
                    if (string.IsNullOrWhiteSpace(installerJava)) installerJava = launcher.GetDefaultJavaPath();
                    var installOptions = new ForgeInstallOptions
                    {
                        JavaPath = InstallerJava.Resolve(installerJava, OperatingSystem.IsWindows()),
                        CancellationToken = token,
                        SkipIfAlreadyInstalled = true,
                        InstallerOutput = new Progress<string>(message => LogLoader(directory, message)),
                        FileProgress = new Progress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>(args =>
                        {
                            progress($"{args.EventType}  {args.ProgressedTasks}/{args.TotalTasks}");
                            var name = string.IsNullOrWhiteSpace(args.Name) ? null : Path.GetFileName(args.Name);
                            resources?.Report("模组加载器", instance.Name, 0, 0, name, args.ProgressedTasks, args.TotalTasks);
                        })
                    };
                    if (instance.Loader == "NeoForge")
                    {
                        var installer = new NeoForgeInstaller(launcher, http);
                        profile = instance.LoaderVersion is { } loaderVersion
                            ? await installer.Install(instance.Version, loaderVersion, installOptions)
                            : await installer.Install(instance.Version, installOptions);
                    }
                    else
                    {
                        var installer = new ForgeInstaller(launcher, http);
                        var releases = (await installer.GetForgeVersions(instance.Version)).ToArray();
                        var selected = instance.LoaderVersion is { } loaderVersion
                            ? releases.FirstOrDefault(release => release.ForgeVersionName == loaderVersion)
                            : releases.FirstOrDefault(release => release.IsRecommendedVersion) ?? releases.FirstOrDefault(release => release.IsLatestVersion) ?? releases.FirstOrDefault();
                        if (selected == null) throw new InvalidOperationException("没有与游戏版本兼容的 Forge 安装版本。");
                        var backend = new CmlLib.Core.Installer.Forge.Versions.ForgeInstallerVersionMapper().CreateInstaller(selected);
                        profile = await InstallForgeWithoutBrowserAsync(launcher, instance.Version, backend, installOptions);
                    }
                    await InstallWithRetryAsync(launcher, profile, progress, token);
                }
                loaderStage?.Invoke("正在校验加载器安装结果…");
                var installed = await launcher.GetVersionAsync(profile, token);
                if (installed.Id != profile || (installed.InheritsFrom != instance.Version && installed.ParentVersion?.Id != instance.Version) || string.IsNullOrWhiteSpace(installed.MainClass))
                    throw new InvalidDataException("加载器安装后的版本配置未通过验证。");
                return profile;
            }
            installationLock.Release();
            lockHeld = false;
            var online = await resolveSession();
            var options = new MLaunchOption
            {
                Session = online ?? MSession.CreateOfflineSession(Instance.ValidatePlayer(player)),
                MaximumRamMb = instance.AutomaticMemory == true
                    ? InstanceMemory.Recommend(instance, Directory.Exists(Path.Combine(directory, "mods"))
                        ? Directory.EnumerateFiles(Path.Combine(directory, "mods"), "*.jar").Count() : 0)
                    : instance.Memory,
                ScreenWidth = instance.AutomaticResolution == true ? 0 : instance.Width,
                ScreenHeight = instance.AutomaticResolution == true ? 0 : instance.Height
            };
            if (javaPath != null) { options.JavaPath = javaPath; store.BindJava(instance.Id,javaPath); }
            var process = await launcher.BuildProcessAsync(profile, options);
            process.StartInfo.WorkingDirectory = directory;
            process.StartInfo.UseShellExecute = false;
            // 使用 java.exe 继续采集游戏日志，但不为 Windows 游戏进程创建额外的控制台窗口。
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            stage?.Invoke("校验中");
            var finalMetadata = await launcher.GetVersionAsync(profile, preparationToken);
            var requiredFiles = (await launcher.ExtractFiles(finalMetadata))
                .Where(file => !IsIn(paths.Runtime, file.Path) && !file.UpdateTask.Any(task => task is CmlLib.Core.Tasks.LegacyJavaExtractionTask)).Select(file => file.Path!)
                .Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
            // 启动配置和实际Java可执行文件均纳入安装就绪证据。
            requiredFiles.Add(process.StartInfo.FileName);
            if (IsIn(paths.Runtime, process.StartInfo.FileName) && Path.GetDirectoryName(Path.GetDirectoryName(process.StartInfo.FileName)) is { } javaHome)
                requiredFiles.AddRange(Directory.EnumerateFiles(javaHome, "*", new EnumerationOptions {
                    RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
                    .Where(path => !path.EndsWith(".xg-part", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".xg-part.etag", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".xg-part.identity.json", StringComparison.OrdinalIgnoreCase)));
            requiredFiles.AddRange(paths.MetadataFiles()
                .Where(path => Path.GetFileNameWithoutExtension(path) == profile || Path.GetFileNameWithoutExtension(path) == instance.Version));
            await InstallationReceipt.WriteAsync(store, instance, requiredFiles, preparationToken, profile, process.StartInfo.FileName, major);
            if (await InstallationReceipt.InspectAsync(store, instance, preparationToken) != "已安装")
                throw new InvalidDataException("安装后的必要资源或 Java 未通过最终校验。");
            return process;
        }
        finally { if (lockHeld) installationLock.Release(); }
    }
    /// <summary>
    /// 强制复查安装凭据后仅构造启动进程，不隐式调用安装器。
    /// </summary>
    /// <param name="instance">准备启动的隔离实例。</param>
    /// <param name="player">没有正版会话时使用的离线玩家名。</param>
    /// <param name="resolveSession">启动前获取或刷新会话的异步委托。</param>
    /// <returns>配置了工作目录和输出重定向、尚未启动的进程。</returns>
    /// <exception cref="InvalidOperationException">实例资源缺失、损坏或缺少启动元数据。</exception>
    public async Task<Process> BuildInstalledAsync(Instance instance, string player, Func<Task<MSession?>> resolveSession)
    {
        if (await InspectInstallationAsync(instance, force: true) != "已安装")
            throw new InvalidOperationException("实例资源缺失或损坏，请先修复安装。");
        var receipt = await InstallationReceipt.ReadAsync(store, instance);
        if (string.IsNullOrWhiteSpace(receipt.Profile) || string.IsNullOrWhiteSpace(receipt.Java))
            throw new InvalidOperationException("缺少启动元数据，请重新检查安装。");
        var directory = store.EnsureLocalDirectory(instance);
        CheckGameDirectory(directory);
        var paths = new InstanceMinecraftPath(store, instance);
        _ = InstanceStore.SafeFile(paths.Versions, receipt.Profile);
        // 回调失败直接中止；仅回调明确返回null时才构造离线会话，不用离线会话掩盖正版认证错误。
        var session = await resolveSession();
        var options = new MLaunchOption {
            Session = session ?? MSession.CreateOfflineSession(Instance.ValidatePlayer(player)),
            JavaPath = InstallationReceipt.ResolvePath(store, receipt.Java),
            MaximumRamMb = instance.AutomaticMemory == true
                ? InstanceMemory.Recommend(instance, Directory.Exists(Path.Combine(directory, "mods"))
                    ? Directory.EnumerateFiles(Path.Combine(directory, "mods"), "*.jar").Count() : 0) : instance.Memory,
            ScreenWidth = instance.AutomaticResolution == true ? 0 : instance.Width,
            ScreenHeight = instance.AutomaticResolution == true ? 0 : instance.Height
        };
        var process = await new MinecraftLauncher(paths.CreateParameters(http, installedOnly: true)).BuildProcessAsync(receipt.Profile, options);
        process.StartInfo.WorkingDirectory = directory; process.StartInfo.UseShellExecute = false;
        // 启动阶段只运行已安装的加载器配置；保留输出重定向且隐藏 Java 控制台。
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true; process.StartInfo.RedirectStandardError = true;
        return process;
    }

    private static void CheckGameDirectory(string directory)
    {
        var probe = Path.Combine(directory, ".xg-write-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            file.WriteByte(0);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new IOException("游戏目录无法写入。请在可写位置运行启动器，并检查可用磁盘空间。", error);
        }
    }

    /// <summary>使用原安装器公开接口保留校验、进程及取消流程，省略便利 API 的安装后广告浏览器回调。</summary>
    internal static async Task<string> InstallForgeWithoutBrowserAsync(MinecraftLauncher launcher, string minecraftVersion, IForgeInstaller installer, ForgeInstallOptions options)
    {
        var token = options.CancellationToken; token.ThrowIfCancellationRequested();
        if (options.SkipIfAlreadyInstalled)
        {
            try { await launcher.GetVersionAsync(installer.VersionName, token); return installer.VersionName; }
            catch (KeyNotFoundException) { }
        }
        var version = await launcher.GetVersionAsync(minecraftVersion, token);
        await launcher.InstallAsync(version, options.FileProgress, options.ByteProgress, token);
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(options.JavaPath)) throw new InvalidOperationException("兼容 Java 尚未就绪，无法安装加载器。");
        await installer.Install(launcher.MinecraftPath, launcher.GameInstaller, options);
        token.ThrowIfCancellationRequested();
        await launcher.GetAllVersionsAsync(token);
        return installer.VersionName;
    }

    private static bool IsIn(string root, string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative.Split(Path.DirectorySeparatorChar)[0] != "..";
    }

    private static void LogLoader(string directory, string message)
    {
        var path = Path.Combine(directory, "xg-loader.log");
        try
        {
            if (new FileInfo(path) is { Exists: true, Length: > 2_000_000 }) return;
            var clean = new GameLogSanitizer(null).Redact(message);
            clean = clean[..Math.Min(clean.Length, 4096)];
            File.AppendAllText(path, clean + Environment.NewLine);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Trace.TraceWarning("Installer output unavailable: " + error.GetType().Name); }
    }

    private static async Task InstallWithRetryAsync(MinecraftLauncher launcher, string profile, Action<string> progress, CancellationToken token)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { await launcher.InstallAsync(profile, token); return; }
            catch (HttpRequestException error) when (attempt < 3 && (error.StatusCode == null || (int)error.StatusCode >= 500 || (int)error.StatusCode is 408 or 429))
            {
                progress($"游戏下载连接中断，正在重试（{attempt}/2）");
                await Task.Delay(TimeSpan.FromSeconds(attempt), token);
            }
            catch (HttpIOException) when (attempt < 3)
            {
                progress($"游戏下载提前结束，正在重试（{attempt}/2）");
                await Task.Delay(TimeSpan.FromSeconds(attempt), token);
            }
        }
    }

}
