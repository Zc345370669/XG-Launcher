// SPDX-License-Identifier: MIT
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XGLauncher;

internal static class DialogRemediationTests
{
    internal static void Run(string root)
    {
        foreach (var scale in new[] { 1d, 1.25, 1.5, 1.75, 2 })
        {
            var workArea = new PixelRect(-1600, 0, 1600, 900);
            var anchor = new PixelRect(-1400, 100, 650, 500);
            var position = DialogPlacement.Center(anchor, new Size(380, 240), scale, workArea);
            Check(position.X >= workArea.X && position.Y >= workArea.Y && position.X + 380 * scale <= workArea.Right && position.Y + 240 * scale <= workArea.Bottom, "dialog off screen at DPI " + scale);
        }
        var owner = new MainWindow(Path.Combine(root, "dialog-remediation")); owner.Show(); Pump();
        owner.Position = new(180, 100); owner.Width = 850; owner.Height = 650; Pump();
        var dialog = new LauncherDialog("账号删除确认", owner, false);
        dialog.SetBody(new TextBlock { Text = "账号删除确认" }, new Button { Content = "取消" }, new Button { Content = "确认", IsDefault = true });
        _ = dialog.ShowDialog(owner); Pump();
        var child = new LauncherDialog("嵌套确认", dialog, false);
        child.SetBody(new TextBlock { Text = "仅主窗口为中心" });
        Check(ReferenceEquals(DialogPlacement.Anchor(dialog), owner), "nested modal anchor is not main window");
        _ = child.ShowDialog(dialog); Pump();
        var work = owner.Screens.ScreenFromWindow(owner)?.WorkingArea ?? new PixelRect(0,0,1920,1080);
        var expected = DialogPlacement.Center(new(owner.Position, new PixelSize((int)Math.Ceiling(owner.ClientSize.Width * owner.RenderScaling), (int)Math.Ceiling(owner.ClientSize.Height * owner.RenderScaling))), child.ClientSize, child.RenderScaling, work);
        Check(Math.Abs(child.Position.X - expected.X) <= 1 && Math.Abs(child.Position.Y - expected.Y) <= 1, "actual nested dialog not centered on main");
        foreach (var palette in ThemePalette.All)
        {
            ThemePalette.Select(palette); Pump();
            Check(ThemePalette.WindowBorder.Color == Color.Parse(palette.Dark ? "#A7B5C9" : "#333333") && ThemePalette.DialogBorder.Color == ThemePalette.WindowBorder.Color, "top-level border palette diverged");
            var surface = dialog.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "DialogSurface");
            Check(surface.BorderBrush == ThemePalette.DialogBorder, "modal border not using shared brush");
        }
        child.Close(); dialog.Close(); owner.Close(); Pump(); ThemePalette.Select(ThemePalette.All[0]);
        Console.WriteLine("PASS primary-window modal centering including nested owner, 100–200% multi-monitor bounds and shared light/dark outline palette (Linux headless; native Windows not verified)");
    }
    private static void Pump() { for(var i=0;i<5;i++){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();} }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
