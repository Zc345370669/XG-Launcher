// SPDX-License-Identifier: MIT
using System.Net;
using System.Reflection;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XGLauncher;

internal static class LauncherUpdateUiTests
{
    internal static void Run(string root)
    {
        var window = new MainWindow(Path.Combine(root, "update-ui")); window.Show(); Pump();
        using var handler = new Releases();
        window.UpdateHttpFactory = () => new HttpClient(handler, disposeHandler: false);
        var nav = window.GetVisualDescendants().OfType<Button>().Single(x => x.Content is StackPanel row && row.Children.OfType<TextBlock>().Any(t => t.Text == "设置"));
        Invoke(nav);
        var settings = window.GetVisualDescendants().OfType<SettingsSurface>().Single(); settings.ScrollToSection(6, false); Pump();
        var version = settings.Sections[6].GetVisualDescendants().OfType<Button>().Single(x => x.Name == "VersionUpdateLink");
        Check(version.Content is string value && value.StartsWith("v0.1.1"), "version is not the clickable entry");
        Check(!settings.Sections[6].GetVisualDescendants().OfType<Button>().Any(x => Equals(x.Content, "检查更新")), "extra check-update button added");
        Invoke(version); Pump();
        var dialog = window.OwnedWindows.Single();
        Check(dialog.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.Contains("未发现可用的新版本") == true), "manual version click did not check releases");
        Invoke(dialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "知道了"))); Pump();
        handler.Available = true;
        Invoke(version); Pump();
        dialog = window.OwnedWindows.Single();
        Check(dialog.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.Contains("v0.2.0") == true), "new release prompt missing");
        Invoke(dialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "暂不更新"))); Pump();
        Check(window.IsVisible && handler.AssetRequests == 0 && !window.OwnedWindows.Any(), "declining update downloaded package or changed window");
        var automaticallyApplied = false;
        window.UpdateInstallOverride = (release, package) => { automaticallyApplied = File.Exists(package); return Task.CompletedTask; };
        handler.SinglePackage = true;
        Invoke(version); Pump();
        dialog = window.OwnedWindows.Single();
        Invoke(dialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "更新")));
        for (var i = 0; i < 300 && !automaticallyApplied; i++)
        { Pump(); using var tick = new CancellationTokenSource(10); Dispatcher.UIThread.MainLoop(tick.Token); }
        Check(automaticallyApplied && handler.AssetRequests == 1, "one confirmation did not automatically download, verify and request apply");
        var updateWindow = window.ActiveUpdateWindow ?? throw new Exception("standalone update window missing");
        updateWindow.GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true; Pump();
        Check(updateWindow.Width <= 360 && updateWindow.Height <= updateWindow.MaxHeight, "standalone update details overflowed compact window");
        Check(updateWindow.GetVisualDescendants().OfType<ProgressBar>().Any(x => x.Name == "UpdateProgress" && x.Value == 100) && updateWindow.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Name == "UpdateBytes") && updateWindow.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Name == "UpdateFile"), "update progress/details missing");
        Check(!window.GetVisualDescendants().OfType<Button>().Any(x => Equals(x.Content, "安装更新并重启")), "a second install confirmation is required");
        window.Close(); Pump();
        Console.WriteLine("PASS About version text is the only manual update entry; real click checks official release fixture, shows update/no-update prompts and decline performs no download (Linux headless)");
    }
    private sealed class Releases : HttpMessageHandler
    {
        internal bool Available, SinglePackage; internal int AssetRequests;
        private static byte[] Package()
        {
            using var memory = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, true))
            { using var output = zip.CreateEntry("XG-Launcher/XGLauncher.exe").Open(); var bytes = new byte[128]; bytes[0]=0x4d;bytes[1]=0x5a;bytes[0x3c]=0x40;bytes[0x40]=0x50;bytes[0x41]=0x45;bytes[0x44]=0x64;bytes[0x45]=0x86; output.Write(bytes); }
            return memory.ToArray();
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.Host != "api.github.com") AssetRequests++;
            var json = Available ? "[{\"draft\":false,\"tag_name\":\"v0.2.0\",\"assets\":[{\"name\":\"XGLauncher-portable-win-x64.zip\",\"state\":\"uploaded\",\"size\":1024,\"digest\":\"sha256:" + new string('a', 64) + "\",\"browser_download_url\":\"https://github.com/Zc345370669/XG-Launcher/releases/download/v0.2.0/XGLauncher-portable-win-x64.zip\"}]}]" : "[]";
            if (SinglePackage)
            {
                var bytes = Package();
                if (request.RequestUri!.Host != "api.github.com") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
                json = json.Replace("1024", bytes.Length.ToString()).Replace(new string('a',64), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
    private static void Invoke(Button button) { ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke(); Pump(); }
    private static void Pump() { for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
    private static void Check(bool result, string message) { if (!result) throw new Exception(message); }
}
