// SPDX-License-Identifier: MIT
using System.ComponentModel;
using System.Diagnostics;
using CmlLib.Core.Auth;
using System.Net;
using XG.Core;

namespace XGLauncher;

/// <summary>
/// 持有窗口共享的账号、实例、安装、下载与启动服务；切换模式不创建第二份业务状态。
/// </summary>
internal sealed partial class LauncherModel : IDisposable
{
    private readonly HttpClient http = CreateDownloadClient();
    private static HttpClient CreateDownloadClient() { var client=DownloadTuning.CreateHttpClient(new HttpClientHandler { AllowAutoRedirect=false, AutomaticDecompression=DecompressionMethods.All });client.Timeout=TimeSpan.FromMinutes(3);return client; }
    public bool PortableSession { get; }
    public InstanceStore Store { get; }
    public string DownloadsDirectory { get; }
    public string BackupsDirectory { get; }
    public string LogsDirectory { get; }
    public string LicensesDirectory { get; }
    public GameService Game { get; }
    public Modrinth Mods { get; }
    public Downloads Downloads { get; }
    public ResourceOperations Resources { get; }
    public PackImporter Packs { get; }
    private sealed record Selection(Guid? Instance, string Player = "Player");
    private Selection selection = new(null);
    private Instance? selected;
    private string SelectionPath => Path.Combine(Store.Root, "selection.json");
    public Instance? Selected { get => selected; set { selected = value; selection = selection with { Instance = value?.Id }; SaveSelection(); } }
    public string Player { get => Accounts.SelectedProfile is { Kind: "Offline" } profile ? profile.Name : selection.Player; set { selection = selection with { Player = Instance.ValidatePlayer(value) }; SaveSelection(); } }
    private void SaveSelection() => InstanceStore.WriteJson(SelectionPath, selection);
    internal bool IsDisposed { get; private set; }
    public void Dispose() { if (IsDisposed) return; IsDisposed = true; http.Dispose(); }
    public MicrosoftAccounts Accounts { get; }
    public HashSet<Guid> Busy { get; } = [];
    public LauncherModel(string? root = null, Modrinth? catalog = null, HttpClient? gameMetadata = null)
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("XGLauncher/0.1.2");
        var portable = root is null ? PortableStorage.Resolve() : null;
        PortableSession = portable != null;
        root ??= Path.Combine(portable!, "data");
        Store = new(root, portable is null ? null : GameRootPreference.Resolve(portable, root),
            Path.Combine(AppContext.BaseDirectory, "data", "downloads", "java-installers"), upgradeLegacy: portable == null);
        DownloadsDirectory = portable == null ? Path.Combine(root, "downloads") : PortableStorage.Auxiliary(portable, root, "downloads");
        BackupsDirectory = portable == null ? Path.Combine(root, "backups") : PortableStorage.Auxiliary(portable, root, "backups");
        LogsDirectory = portable == null ? Path.Combine(root, "logs") : PortableStorage.Auxiliary(portable, root, "logs");
        LicensesDirectory = Path.Combine(portable ?? root, "licenses");
        foreach (var folder in new[] { DownloadsDirectory, BackupsDirectory, LogsDirectory })
            Directory.CreateDirectory(folder);
        Accounts = new(Store.Root);
        Game = new(gameMetadata ?? http, Store);
        Mods = catalog ?? new(http);
        Downloads = new(http, root, portable);
        Resources = new(root);
        Packs = new(http, Store);
        try { if (File.Exists(SelectionPath)) selection = System.Text.Json.JsonSerializer.Deserialize<Selection>(File.ReadAllText(SelectionPath)) ?? new(null); }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException) { selection = new(null); }
        try { Instance.ValidatePlayer(selection.Player); } catch (ArgumentException) { selection = selection with { Player = "Player" }; }
        selected = Store.Entries.FirstOrDefault(x => x.Id == selection.Instance) ?? Store.Entries.FirstOrDefault();
    }

    public string ModsFolder(Instance instance) => Path.Combine(Store.GameDirectoryFor(instance), "mods");
    public string[] LocalFiles(Instance instance) =>
        (instance.ReferencedGameDirectory == null || Directory.Exists(instance.ReferencedGameDirectory)) && Directory.Exists(ModsFolder(instance))
        ? Directory.GetFiles(ModsFolder(instance)).Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)).ToArray() : [];

    /// <summary>
    /// 阻止同一实例重复启动，等待游戏退出后释放进程和日志，并恢复Busy状态。
    /// </summary>
    public async Task LaunchAsync(Instance instance, Action<string> status)
    {
        if (!Busy.Add(instance.Id)) return;
        try
        {
            var player = Player;
            var provider = await Accounts.CaptureLaunchSessionAsync();
            MSession? session = null;
            using var process = await Game.BuildInstalledAsync(instance, player,
                async () => session = await provider());
            var sanitizer = new GameLogSanitizer(session);
            var directory = Store.DirectoryFor(instance);
            using var log = TextWriter.Synchronized(File.CreateText(InstanceLogs.Resolve(directory, "xg-launch.log", write: true)));
            Exception? logFailure = null;
            void Receive(object? sender, DataReceivedEventArgs e) {
                if(e.Data == null)return;
                try { log.WriteLine(sanitizer.Redact(e.Data)); }
                catch(Exception error) when(error is IOException or UnauthorizedAccessException or ObjectDisposedException) { Interlocked.CompareExchange(ref logFailure,error,null); }
            }
            process.OutputDataReceived += Receive;
            process.ErrorDataReceived += Receive;
            try { process.Start(); }
            catch (Win32Exception error)
            {
                throw new InvalidOperationException($"Java 进程未能启动（系统错误 {error.NativeErrorCode}）。请检查 Java 路径和运行权限。");
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var updated = instance with { LastPlayed = DateTimeOffset.Now };
            try
            {
                Store.Save(updated);
                if (Selected?.Id == instance.Id) Selected = updated;
                status("游戏运行中");
            }
            finally { await process.WaitForExitAsync(); await Task.Run(process.WaitForExit); }
            status(logFailure == null ? $"游戏已退出（{process.ExitCode}）" : $"游戏已退出（{process.ExitCode}），日志写入失败，请检查磁盘空间和权限。");
        }
        finally { Busy.Remove(instance.Id); }
    }
}
