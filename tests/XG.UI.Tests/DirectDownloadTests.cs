// SPDX-License-Identifier: MIT
using System.Reflection;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XG.Core;
using XGLauncher;

internal static class DirectDownloadTests
{
    internal static void Run(string root, string? screenshots)
    {
        var folder = Path.Combine(root, "direct-download"); Directory.CreateDirectory(folder);
        var instance = new Instance(Guid.NewGuid(), "direct download fixture", "1.21.1"); new InstanceStore(folder).Save(instance);
        using var stalled = new HttpClient(new Stalled());
        var window = new MainWindow(folder, null, stalled); window.Show(); Pump();
        var model = (LauncherModel)typeof(MainWindow).GetField("model", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        var nav = window.GetVisualDescendants().OfType<Button>().First(b => b.Content is StackPanel p && p.Children.OfType<TextBlock>().Any(t => t.Text == "个人游戏"));
        Invoke(nav); WaitFor(() => window.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "下载")));
        Invoke(window.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "下载"))); Pump();
        Check(model.Resources.MainJobs.Count == 1 && model.Resources.CanControl(model.Resources.MainJobs.Single().Id), "click did not immediately create a live download task");
        Check(!window.OwnedWindows.Any(), "ordinary download opened a confirmation window");
        var again = (Task)typeof(MainWindow).GetMethod("InstancePrimaryAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, new object[] { instance })!;
        Pump(); Check(again.IsCompletedSuccessfully && model.Resources.MainJobs.Count == 1, "repeated click created duplicate tasks");
        Check(window.GetVisualDescendants().OfType<ProgressBar>().Count() == 1 && !window.GetVisualDescendants().OfType<Expander>().Any(), "download card contains extra progress or detail expander");
        Check(!model.Resources.Items.Any(x => x.Kind == "模组加载器"), "vanilla created a loader stage");
        foreach (var stage in new[] { "游戏本体", "Assets", "Libraries", "Java" })
            Check(window.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == stage), "missing directly displayed stage: " + stage);
        if (screenshots != null) { Pump(); using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(screenshots, "phase13-direct-download-waiting-fixture.png")); }
        Localizer.Set("en-US"); Pump();
        var englishStageText = window.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text).Where(x => x != null && System.Text.RegularExpressions.Regex.IsMatch(x, "[\\u4e00-\\u9fff]")).ToArray();
        Check(englishStageText.Length == 0, "untranslated live task: " + string.Join(" | ", englishStageText));
        var id = model.Resources.MainJobs.Single().Id;
        model.Resources.Pause(id); Pump(); Check(model.Resources.IsPaused(id), "main pause not real");
        model.Resources.Resume(id); Pump(); model.Resources.Cancel(id);
        WaitFor(() => !model.Busy.Contains(instance.Id) && model.Resources.MainJobs.Single().State == "已取消");
        Check(model.Resources.UnfinishedCount == 0 && !window.OwnedWindows.Any(), "normal cancellation left badge or error dialog");
        window.Close(); Pump();
        Console.WriteLine("PASS phase13 actual Download button creates one live task without confirmation; repeated click dedupes, five stages share one bar; pause/resume/cancel work against stalled HTTP fixture");
        void Pump() { for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
        void WaitFor(Func<bool> ready) { for (var i = 0; i < 250; i++) { Pump(); if (ready()) return; using var stop = new CancellationTokenSource(15); Dispatcher.UIThread.MainLoop(stop.Token); } throw new Exception("Direct download UI timeout"); }
    }
    private sealed class Stalled : HttpMessageHandler {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            await Task.Delay(Timeout.InfiniteTimeSpan, token); throw new Exception("unreachable");
        }
    }
    private static void Invoke(Button b) => ((IInvokeProvider)new ButtonAutomationPeer(b)).Invoke();
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
