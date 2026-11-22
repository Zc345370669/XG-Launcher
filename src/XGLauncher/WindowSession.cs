// SPDX-License-Identifier: MIT
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using XG.Core;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.Input;

namespace XGLauncher;

internal sealed record ModeGeometry(double Width, double Height, int? X = null, int? Y = null, bool Maximized = false);
internal static class WindowPlacement
{
    internal static (ModeGeometry Geometry, Size Minimum) Fit(ModeGeometry geometry, bool compact, PixelRect work, double scaling)
    {
        var scale = double.IsFinite(scaling) && scaling > 0 ? scaling : 1;
        var available = new Size(Math.Max(1, work.Width / scale), Math.Max(1, work.Height / scale));
        var min = new Size(Math.Min(compact ? 320 : 520, available.Width), Math.Min(compact ? 330 : 395, available.Height));
        var width = Math.Clamp(double.IsFinite(geometry.Width) ? geometry.Width : compact ? 360 : 1130, min.Width, available.Width);
        var height = Math.Clamp(double.IsFinite(geometry.Height) ? geometry.Height : compact ? 400 : 720, min.Height, available.Height);
        var x = Math.Clamp(geometry.X ?? work.X, work.X, Math.Max(work.X, work.Right - (int)Math.Ceiling(width * scale)));
        var y = Math.Clamp(geometry.Y ?? work.Y, work.Y, Math.Max(work.Y, work.Bottom - (int)Math.Ceiling(height * scale)));
        return (geometry with { Width = width, Height = height, X = x, Y = y }, min);
    }
}
internal sealed record WindowSession(bool Compact = false, ModeGeometry? Full = null, ModeGeometry? Small = null)
{
    internal static WindowSession Load(string path, bool defaultCompact = false)
    {
        try { return JsonSerializer.Deserialize<WindowSession>(File.ReadAllText(path)) ?? new(defaultCompact); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(defaultCompact); }
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
    private readonly DispatcherTimer saveGeometry = new() { Interval = TimeSpan.FromMilliseconds(250) };
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
        windowSession = WindowSession.Load(WindowSessionPath, model.PortableSession);
        fullBody.IsVisible = !IsCompact;
        compactBody.IsVisible = IsCompact;
        UpdateCaptionMode();
        Screens.Changed += ScreenConfigurationChanged;
        ScalingChanged += ScreenConfigurationChanged;
        saveGeometry.Tick += (_, _) => { saveGeometry.Stop(); if (!windowClosed) SaveWindowSession(); };
        Opened += (_, _) =>
        {
            ApplyMode();
            windowReady = true;
        };
        void Remember()
        {
            if (!windowReady || changingMode || windowClosed) return;
            CaptureGeometry(); saveGeometry.Stop(); saveGeometry.Start();
        }
        PositionChanged += (_, _) => Remember();
        SizeChanged += (_, _) => Remember();
        PropertyChanged += (_, args) => { if (args.Property == WindowStateProperty) Remember(); };
        Closed += (_, _) =>
        {
            Screens.Changed -= ScreenConfigurationChanged;
            ScalingChanged -= ScreenConfigurationChanged;
            saveGeometry.Stop();
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
        try
        {
            // 显示器/DPI变化只约束当前几何，不重建页面、不打断浮层或安装任务。
            FitGeometry(new(ClientSize.Width, ClientSize.Height, Position.X, Position.Y), Screens.ScreenFromWindow(this) ?? Screens.Primary);
            CaptureGeometry(); SaveWindowSession();
        }
        finally { changingMode = false; }
    }

    private void ApplyMode()
    {
        WindowState = WindowState.Normal;
        var geometry = (IsCompact ? windowSession.Small : windowSession.Full) ??
            (IsCompact ? new ModeGeometry(360, 400) : new ModeGeometry(1130, 720));
        var point = geometry.X is { } x && geometry.Y is { } y ? new PixelPoint(x, y) : Position;
        var screen = Screens.All.FirstOrDefault(s => s.WorkingArea.Contains(point)) ?? Screens.ScreenFromWindow(this) ?? Screens.Primary;
        FitGeometry(geometry with { X = point.X, Y = point.Y }, screen);
        fullBody!.IsVisible = !IsCompact;
        compactBody!.IsVisible = IsCompact;
        UpdateCaptionMode();
        if (geometry.Maximized) WindowState = WindowState.Maximized;
        Refresh();
        Dispatcher.UIThread.Post(() =>
        {
            if (windowClosed || FocusManager?.GetFocusedElement() is Control { IsEffectivelyVisible: true }) return;
            (IsCompact ? compactBody : fullBody)?.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(x => x.IsEffectivelyVisible && x.IsEffectivelyEnabled && x.Focusable)?.Focus(NavigationMethod.Unspecified);
        }, DispatcherPriority.Background);
    }

    private void FitGeometry(ModeGeometry geometry, Screen? screen)
    {
        var fit = WindowPlacement.Fit(geometry, IsCompact, screen?.WorkingArea ?? new(0, 0, 1920, 1080), screen?.Scaling ?? RenderScaling);
        MinWidth = fit.Minimum.Width; MinHeight = fit.Minimum.Height; CanResize = true;
        if (WindowState == WindowState.Normal)
        {
            Width = fit.Geometry.Width; Height = fit.Geometry.Height;
            Position = new(fit.Geometry.X!.Value, fit.Geometry.Y!.Value);
        }
    }

    private void CaptureGeometry()
    {
        if (changingMode && !windowReady || WindowState == WindowState.Minimized) return;
        var prior = (IsCompact ? windowSession.Small : windowSession.Full) ?? new ModeGeometry(Width, Height);
        var client = ClientSize.Width > 0 && ClientSize.Height > 0 ? ClientSize : new Size(Width, Height);
        var geometry = WindowState == WindowState.Normal
            ? new ModeGeometry(client.Width, client.Height, Position.X, Position.Y)
            : prior with { Maximized = WindowState == WindowState.Maximized };
        windowSession = IsCompact ? windowSession with { Small = geometry } : windowSession with { Full = geometry };
    }

    private void SaveWindowSession()
    {
        try { InstanceStore.WriteJson(WindowSessionPath, windowSession); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "窗口设置保存失败，请检查目录权限。"; }
    }
}
