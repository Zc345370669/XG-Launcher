// SPDX-License-Identifier: MIT
using System.Reflection;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XG.Core;
using XGLauncher;

internal static class WindowShutdownTests
{
    internal static void Run(string root)
    {
        foreach (var compact in new[] { false, true })
        foreach (var paused in new[] { false, true })
        {
            var folder = Path.Combine(root, "shutdown-ui-" + compact + "-" + paused);
            var window = new MainWindow(folder); window.Show(); Pump();
            if (compact) { window.ToggleMode(); Pump(); }
            var model = (LauncherModel)typeof(MainWindow).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var instance = new Instance(Guid.NewGuid(), "shutdown fixture", "1.21.1"); model.Store.Save(instance);
            var file = Path.Combine(folder, "retained.part");
            var entered = false;
            var run = Run();
            async Task Run()
            {
                model.Busy.Add(instance.Id);
                try
                {
                    await model.Resources.RunAsync("fixture", "实例安装", "fixture", instance.Name, async _ =>
                    {
                        await model.Resources.Current!.RunFileAsync(async token =>
                        {
                            File.WriteAllText(file, "partial fixture"); entered = true;
                            await Task.Delay(Timeout.Infinite, token);
                        });
                        return true;
                    }, new ResourceRecovery("game", instance.Id));
                }
                catch (OperationCanceledException) { }
                finally { model.Busy.Remove(instance.Id); }
            }
            Check(entered, "fixture task not active");
            var id = model.Resources.MainJobs.Single().Id;
            if (paused) model.Resources.Pause(id);
            window.Close(); Pump();
            var confirmation = window.OwnedWindows.Single();
            Invoke(confirmation, "取消"); Pump();
            Check(window.IsVisible && !model.IsDisposed && model.Resources.CanControl(id) && model.Resources.IsPaused(id) == paused,
                "cancel exit changed task/window state");
            window.Close(); Pump();
            confirmation = window.OwnedWindows.Single();
            window.Close(); Pump();
            Check(window.OwnedWindows.Count() == 1, "repeated close created duplicate confirmation");
            Invoke(confirmation, "确认");
            WaitFor(() => model.IsDisposed && run.IsCompleted);
            Check(!window.IsVisible && File.Exists(file) && model.Resources.MainJobs.Single().State == "等待恢复" && model.Busy.Count == 0,
                "confirmed exit failed to drain or preserve recovery");
            Check(new ResourceOperations(folder).CanRecover(id), "restart cannot recover closed window task");
        }
        Console.WriteLine("PASS full/compact window close while running/paused: cancel leaves tasks intact, repeat close dedupes, confirm saves recovery and releases model/Busy before exit (Linux headless)");
    }
    private static void Invoke(Window window, string title)
    {
        var button = window.GetVisualDescendants().OfType<Button>().Single(x => x.Content is string name && name == title);
        ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke(); Pump();
    }
    private static void Pump() { for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); } }
    private static void WaitFor(Func<bool> done)
    {
        for (var i = 0; i < 250; i++)
        {
            Pump(); if (done()) return;
            using var stop = new CancellationTokenSource(15); Dispatcher.UIThread.MainLoop(stop.Token);
        }
        throw new Exception("safe exit UI timed out");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
