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
        Check(version.Content is string value && value.StartsWith("v0.1.0-alpha"), "version is not the clickable entry");
        Check(!settings.Sections[6].GetVisualDescendants().OfType<Button>().Any(x => Equals(x.Content, "检查更新")), "extra check-update button added");
        Invoke(version); Pump();
        var dialog = window.OwnedWindows.Single();
        Check(dialog.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.Contains("未发现可用的新版本") == true), "manual version click did not check releases");
        Invoke(dialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "知道了"))); Pump();
        handler.Available = true;
        Invoke(version); Pump();
        dialog = window.OwnedWindows.Single();
        Check(dialog.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.Contains("v0.2.0-alpha") == true), "new release prompt missing");
        Invoke(dialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "暂不更新"))); Pump();
        Check(window.IsVisible && handler.AssetRequests == 0 && !window.OwnedWindows.Any(), "declining update downloaded package or changed window");
        window.Close(); Pump();
        Console.WriteLine("PASS About version text is the only manual update entry; real click checks official release fixture, shows update/no-update prompts and decline performs no download (Linux headless)");
    }
    private sealed class Releases : HttpMessageHandler
    {
        internal bool Available; internal int AssetRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.Host != "api.github.com") AssetRequests++;
            var json = Available ? "[{\"draft\":false,\"tag_name\":\"v0.2.0-alpha\",\"assets\":[{\"name\":\"XGLauncher-portable-win-x64.zip\",\"state\":\"uploaded\",\"size\":1024,\"digest\":\"sha256:" + new string('a', 64) + "\",\"browser_download_url\":\"https://github.com/Zc345370669/XG-Launcher/releases/download/v0.2.0-alpha/XGLauncher-portable-win-x64.zip\"}]}]" : "[]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
    private static void Invoke(Button button) { ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke(); Pump(); }
    private static void Pump() { for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
    private static void Check(bool result, string message) { if (!result) throw new Exception(message); }
}
