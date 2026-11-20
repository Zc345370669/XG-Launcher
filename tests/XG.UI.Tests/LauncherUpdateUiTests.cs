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
using XG.Core;

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
        Invoke(dialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "取消"))); Pump();
        Check(window.IsVisible && handler.AssetRequests == 0 && !window.OwnedWindows.Any(), "declining update downloaded package or changed window");
        var automatic = window.CheckUpdatesAsync(false); Pump();
        dialog = window.OwnedWindows.Single();
        Check(dialog.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.Contains("v0.2.0-alpha") == true), "startup check did not use update prompt");
        Invoke(dialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "取消"))); Pump();
        Check(automatic.IsCompletedSuccessfully && handler.AssetRequests == 0, "startup decline downloaded package");
        handler.Stall = true;
        Invoke(version); Pump();
        dialog = window.OwnedWindows.Single();
        Invoke(dialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "更新"))); Pump();
        var updating = window.OwnedWindows.Single();
        Check(updating.GetVisualDescendants().OfType<ProgressBar>().Count() == 1 &&
            updating.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.Contains("正在下载更新") == true), "real update progress dialog missing");
        Invoke(updating.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "取消")));
        for (int i = 0; i < 50 && !handler.Cancelled; i++) { Pump(); Thread.Sleep(10); }
        Check(handler.Cancelled && window.IsVisible && !window.OwnedWindows.Any(), "download cancellation did not release HTTP or preserve launcher");
        var model = (LauncherModel)typeof(MainWindow).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var receipts = Path.Combine(model.DownloadsDirectory, "updates");
        var receipt = Path.Combine(receipts, Guid.NewGuid().ToString("N"), "apply.json.result");
        Directory.CreateDirectory(Path.GetDirectoryName(receipt)!);
        UpdateReceipt.Write(receipt, UpdateOutcome.LaunchFailed);
        var failedRestart = window.ShowUpdateOutcomeAsync(["--update-result", receipt]); Pump();
        var failureDialog = window.OwnedWindows.Single();
        Check(failureDialog.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.Contains("新程序无法启动") == true), "rollback failure feedback missing");
        Invoke(failureDialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "关闭"))); Pump();
        Check(failedRestart.IsCompletedSuccessfully && failedRestart.Result, "failed update should suppress immediate automatic retry");
        File.WriteAllText(receipt, "untrusted private diagnostic fixture");
        Check(window.ShowUpdateOutcomeAsync(["--update-result", receipt]).GetAwaiter().GetResult() == false && !window.OwnedWindows.Any(), "untrusted receipt shown to user");
        UpdateReceipt.Write(receipt, UpdateOutcome.Updated);
        Check(!window.ShowUpdateOutcomeAsync(["--update-result", receipt]).GetAwaiter().GetResult() && !window.OwnedWindows.Any(), "successful restart must not require another confirmation");
        window.Close(); Pump();
        Console.WriteLine("PASS About version text is the only manual update entry; real click checks official release fixture, shows update/no-update prompts and decline performs no download (Linux headless)");
    }
    private sealed class Releases : HttpMessageHandler
    {
        internal bool Available, Stall, Cancelled; internal int AssetRequests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.Host != "api.github.com")
            {
                AssetRequests++;
                if (Stall) try { await Task.Delay(Timeout.Infinite, token); } finally { Cancelled = true; }
            }
            var json = Available ? "[{\"draft\":false,\"prerelease\":true,\"tag_name\":\"v0.2.0-alpha\",\"assets\":[{\"name\":\"XGLauncher-portable-win-x64.zip\",\"state\":\"uploaded\",\"size\":1024,\"digest\":\"sha256:" + new string('a', 64) + "\",\"browser_download_url\":\"https://github.com/Zc345370669/XG-Launcher/releases/download/v0.2.0-alpha/XGLauncher-portable-win-x64.zip\"}]}]" : "[]";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }
    private static void Invoke(Button button) { ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke(); Pump(); }
    private static void Pump() { for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
    private static void Check(bool result, string message) { if (!result) throw new Exception(message); }
}
