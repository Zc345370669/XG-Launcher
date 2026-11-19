// SPDX-License-Identifier: MIT
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using XG.Core;
using Avalonia.VisualTree;

namespace XGLauncher;

internal sealed record ModeGeometry(double Width, double Height, int? X = null, int? Y = null, bool Maximized = false);
internal sealed record WindowSession(bool Compact = false, ModeGeometry? Full = null, ModeGeometry? Small = null)
{
    internal static WindowSession Load(string path)
    {
        try { return JsonSerializer.Deserialize<WindowSession>(File.ReadAllText(path)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
}

public sealed partial class MainWindow
{
    private WindowSession windowSession = new();
    private Border? windowShell;
    private Control? fullBody;
    private Control? compactBody;
    private bool changingMode;
    private bool windowReady;
    private bool windowClosed;
    internal bool IsCompact => windowSession.Compact;
    private string WindowSessionPath => Path.Combine(model.Store.Root, "window-session.json");

    /// <summary>
    /// 保存当前模式几何并切换同一窗口布局，保留模型、正在安装的任务和已选账号。
    /// </summary>
    internal void ToggleMode()
    {
        if (changingMode || windowClosed || OwnedWindows.Any(x => x.IsVisible)) return;
        changingMode = true;
        try
        {
            foreach (var select in this.GetVisualDescendants().OfType<UnifiedExpandableSelect>()) select.Close(false);
            CaptureGeometry();
            windowSession = windowSession with { Compact = !IsCompact };
            ApplyMode();
            SaveWindowSession();
        }
        finally { changingMode = false; }
    }

    private void InitializeModes(Control body, Grid layout)
    {
        fullBody = body;
        compactBody = CreateCompactBody();
        Grid.SetRow(compactBody, 1);
        layout.Children.Insert(2, compactBody);
        windowShell = new Border { Name = "WindowShell", Background = ThemePalette.Background, BorderBrush = ThemePalette.Line,
            BorderThickness = new(1), Child = layout };
        WindowChrome.Attach(this, windowShell);
        windowSession = WindowSession.Load(WindowSessionPath);
        fullBody.IsVisible = !IsCompact;
        compactBody.IsVisible = IsCompact;
        UpdateCaptionMode();
        Screens.Changed += ScreenConfigurationChanged;
        Opened += (_, _) =>
        {
            ApplyMode();
            windowReady = true;
        };
        PositionChanged += (_, _) => { if (windowReady && !changingMode) CaptureGeometry(); };
        SizeChanged += (_, _) => { if (windowReady && !changingMode) CaptureGeometry(); };
        Closed += (_, _) =>
        {
            Screens.Changed -= ScreenConfigurationChanged;
            CaptureGeometry();
            SaveWindowSession();
            windowClosed = true;
        };
    }

    /// <summary>
    /// 显示器配置变化时重新校正窗口可见区域，避免保存的旧位置落在屏幕外。
    /// </summary>
    private void ScreenConfigurationChanged(object? sender, EventArgs args)
    {
        if (!windowReady || windowClosed || changingMode) return;
        CaptureGeometry();
        changingMode = true;
        try { ApplyMode(); SaveWindowSession(); }
        finally { changingMode = false; }
    }

    private void ApplyMode()
    {
        WindowState = WindowState.Normal;
        var geometry = (IsCompact ? windowSession.Small : windowSession.Full) ??
            (IsCompact ? new ModeGeometry(360, 400) : new ModeGeometry(1130, 720));
        var point = geometry.X is { } x && geometry.Y is { } y ? new PixelPoint(x, y) : Position;
        var screen = Screens.All.FirstOrDefault(s => s.WorkingArea.Contains(point)) ?? Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var scale = screen?.Scaling ?? 1;
        var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        var availableWidth = Math.Max(200, area.Width / scale);
        var availableHeight = Math.Max(200, area.Height / scale);
        MinWidth = Math.Min(IsCompact ? 320 : 520, availableWidth);
        MinHeight = Math.Min(IsCompact ? 330 : 395, availableHeight);
        CanResize = true;
        static double Finite(double value, double fallback) => double.IsFinite(value) ? value : fallback;
        Width = Math.Clamp(Finite(geometry.Width, IsCompact ? 360 : 1130), MinWidth, availableWidth);
        Height = Math.Clamp(Finite(geometry.Height, IsCompact ? 400 : 720), MinHeight, availableHeight);
        Position = new PixelPoint(
            Math.Clamp(point.X, area.X, Math.Max(area.X, area.Right - (int)Math.Ceiling(Width * scale))),
            Math.Clamp(point.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)Math.Ceiling(Height * scale))));
        fullBody!.IsVisible = !IsCompact;
        compactBody!.IsVisible = IsCompact;
        UpdateCaptionMode();
        if (!IsCompact && geometry.Maximized) WindowState = WindowState.Maximized;
        Refresh();
    }

    private void CaptureGeometry()
    {
        if (changingMode && !windowReady || WindowState == WindowState.Minimized) return;
        var prior = (IsCompact ? windowSession.Small : windowSession.Full) ?? new ModeGeometry(Width, Height);
        var geometry = WindowState == WindowState.Normal
            ? new ModeGeometry(Width, Height, Position.X, Position.Y)
            : prior with { Maximized = WindowState == WindowState.Maximized };
        windowSession = IsCompact ? windowSession with { Small = geometry } : windowSession with { Full = geometry };
    }

    private void SaveWindowSession()
    {
        try { InstanceStore.WriteJson(WindowSessionPath, windowSession); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "窗口设置保存失败，请检查目录权限。"; }
    }
}
