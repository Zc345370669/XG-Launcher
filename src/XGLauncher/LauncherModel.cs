// SPDX-License-Identifier: MIT
using System.ComponentModel;
using System.Diagnostics;
using CmlLib.Core.Auth;
using System.Net;
using XG.Core;

namespace XGLauncher;

internal sealed partial class LauncherModel
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All })
    { Timeout = TimeSpan.FromMinutes(3) };
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
    public Instance? Selected { get; set; }
    public string Player { get; set; } = "Player";
    public MicrosoftAccounts Accounts { get; }
    public HashSet<Guid> Busy { get; } = [];
    public LauncherModel(string? root = null, Modrinth? catalog = null, HttpClient? gameMetadata = null)
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("XGLauncher/0.1.0-alpha");
        var portable = root is null ? PortableStorage.Resolve() : null;
        root ??= Path.Combine(portable!, "data");
        Store = new(root, portable is null ? null : Path.Combine(portable, "minecraft"));
        DownloadsDirectory = Path.Combine(portable ?? root, "downloads");
        BackupsDirectory = Path.Combine(portable ?? root, "backups");
        LogsDirectory = Path.Combine(portable ?? root, "logs");
        LicensesDirectory = Path.Combine(portable ?? root, "licenses");
        foreach (var folder in new[] { DownloadsDirectory, BackupsDirectory, LogsDirectory })
            Directory.CreateDirectory(folder);
        Accounts = new(Store.Root);
        Game = new(gameMetadata ?? http, Store);
        Mods = catalog ?? new(http);
        Downloads = new(http, root, portable);
        Resources = new(root);
        Packs = new(http, Store);
        Selected = Store.Entries.FirstOrDefault();
    }

    public string ModsFolder(Instance instance) => Path.Combine(Store.DirectoryFor(instance), "mods");
    public string[] LocalFiles(Instance instance) => Directory.Exists(ModsFolder(instance))
        ? Directory.GetFiles(ModsFolder(instance)).Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)).ToArray() : [];

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
            using var log = TextWriter.Synchronized(File.CreateText(Path.Combine(directory, "xg-launch.log")));
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
