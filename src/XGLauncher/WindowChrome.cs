// SPDX-License-Identifier: MIT
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Platform;
using Avalonia.Threading;

namespace XGLauncher;

internal static class WindowChrome
{
    internal const double CaptionHeight = 44;
    internal const double ResizeGrip = 8; // Avalonia输入位置为DIP，平台统一换算物理像素。
    internal static Size DialogLimits(Size work) => new(Math.Max(1, work.Width - 24), Math.Max(1, work.Height - 24));
    internal static Thickness ClientBorder(double scaling, bool native, bool maximized)
    {
        var scale = double.IsFinite(scaling) ? Math.Max(1, scaling) : 1;
        return native || maximized ? new(0) : new(1 / scale);
    }
    internal static WindowEdge? ResizeEdge(Point point, Size size)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || size.Width <= 0 || size.Height <= 0 ||
            point.X < 0 || point.Y < 0 || point.X > size.Width || point.Y > size.Height) return null;
        const double edge = ResizeGrip;
        var left = point.X <= edge; var right = point.X >= size.Width - edge;
        var top = point.Y <= edge; var bottom = point.Y >= size.Height - edge;
        if (left && top) return WindowEdge.NorthWest;
        if (right && top) return WindowEdge.NorthEast;
        if (left && bottom) return WindowEdge.SouthWest;
        if (right && bottom) return WindowEdge.SouthEast;
        return left ? WindowEdge.West : right ? WindowEdge.East : top ? WindowEdge.North : bottom ? WindowEdge.South : null;
    }
    internal static StandardCursorType ResizeCursor(WindowEdge edge) => edge switch
    {
        WindowEdge.West or WindowEdge.East => StandardCursorType.SizeWestEast,
        WindowEdge.North or WindowEdge.South => StandardCursorType.SizeNorthSouth,
        WindowEdge.NorthWest => StandardCursorType.TopLeftCorner,
        WindowEdge.NorthEast => StandardCursorType.TopRightCorner,
        WindowEdge.SouthWest => StandardCursorType.BottomLeftCorner,
        _ => StandardCursorType.BottomRightCorner
    };
    private static readonly Dictionary<WindowEdge, Cursor> resizeCursors = Enum.GetValues<WindowEdge>()
        .ToDictionary(edge => edge, edge => new Cursor(ResizeCursor(edge)));
    private static readonly ConditionalWeakTable<Window, Controller> controllers = new();
    internal static void Attach(Window window, Border surface)
    {
        if (!controllers.TryGetValue(window, out _)) controllers.Add(window, new Controller(window, surface));
    }
    internal static string Strategy(Window window) => controllers.TryGetValue(window, out var controller) ? controller.Strategy : "unconfigured";

    private sealed class Controller
    {
        private readonly Window window;
        private readonly Border surface;
        private bool native = NativeWindowRounding.CompositionAvailable();
        private readonly bool nativeFrame = OperatingSystem.IsWindows();
        private bool opened;
        private bool disposed;
        private bool updating;
        private IPointer? resizePointer;
        private WindowEdge resizeEdge;
        private PixelPoint resizeStart;
        private PixelPoint resizeOrigin;
        private Size resizeSize;
        private double resizeScale;
        internal string Strategy => native ? "windows11-dwm-requested" : nativeFrame ? "windows-native-square" : "opaque-square-fallback";
        internal Controller(Window window, Border surface)
        {
            this.window = window; this.surface = surface;
            window.TransparencyLevelHint = [WindowTransparencyLevel.None];
            window.Background = window is LauncherDialog ? ThemePalette.DialogSurface : ThemePalette.Background;
            window.TransparencyBackgroundFallback = window is LauncherDialog ? ThemePalette.DialogSurface : ThemePalette.Background;
            window.Foreground = ThemePalette.Text;

            surface.Background = window is LauncherDialog ? ThemePalette.DialogSurface : ThemePalette.Background;
            surface.CornerRadius = new(0); surface.Clip = null; surface.ClipToBounds = false; surface.Opacity = 1;
            surface.UseLayoutRounding = true; window.UseLayoutRounding = true;
            ConfigureFrame(); Update();
            window.Opened += Opened; window.Closed += Closed; window.ScalingChanged += Scaled;
            window.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
            window.PointerMoved += Moved; window.PointerReleased += Released; window.PointerCaptureLost += CaptureLost;
            window.SizeChanged += Sized; window.PropertyChanged += Changed;
            ThemePalette.Background.PropertyChanged += ThemeChanged;
            ThemePalette.Line.PropertyChanged += ThemeChanged;
            ThemePalette.DialogBorder.PropertyChanged += ThemeChanged;
        }
        private void ConfigureFrame()
        {
            var useNativeFrame = nativeFrame && (window is not LauncherDialog || native);
            window.SystemDecorations = useNativeFrame ? SystemDecorations.Full : SystemDecorations.None;
            window.ExtendClientAreaToDecorationsHint = useNativeFrame;
            window.TransparencyLevelHint = window is LauncherDialog && nativeFrame && !native
                ? [WindowTransparencyLevel.Transparent, WindowTransparencyLevel.None] : [WindowTransparencyLevel.None];
            window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
            window.ExtendClientAreaTitleBarHeightHint = 0;
        }
        private void Opened(object? sender, EventArgs args) { opened = true; Update(); }
        private void Sized(object? sender, SizeChangedEventArgs args) => Update();
        private void Scaled(object? sender, EventArgs args) => Update();
        private void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
        { if (args.Property == Window.WindowStateProperty || args.Property.Name == "ActualTransparencyLevel") { Released(null, null!); window.Cursor = null; Update(); } }
        private void ThemeChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Update); return; }
            Update();
        }
        private void Update()
        {
            if (disposed || updating) return;
            updating = true;
            try
            {
                if (native && opened && window.WindowState != WindowState.Minimized && !NativeWindowRounding.TryApply(window)) { native = false; ConfigureFrame(); }
                // 顶层轮廓由DWM负责，不能再叠加HRGN或第二层客户端圆角遮罩。
                surface.Clip = null;
                if (window is LauncherDialog)
                {
                    // An opaque top-level cannot expose pixels outside a rounded client surface.
                    var transparent = !native && window.WindowState == WindowState.Normal &&
                        window.ActualTransparencyLevel == WindowTransparencyLevel.Transparent;
                    window.Background = transparent ? Brushes.Transparent : ThemePalette.DialogSurface;
                    surface.CornerRadius = new(transparent ? 8 : 0);
                }
                else
                {
                    surface.CornerRadius = new(0);
                }
                surface.BorderBrush = window is LauncherDialog ? ThemePalette.DialogBorder : ThemePalette.Line;
                surface.BorderThickness = ClientBorder(window.RenderScaling, nativeFrame && (window is not LauncherDialog || native), window.WindowState == WindowState.Maximized);
            }
            finally { updating = false; }
        }
        private void Pressed(object? sender, PointerPressedEventArgs args)
        {
            if (!window.CanResize || window.WindowState != WindowState.Normal ||
                !args.GetCurrentPoint(window).Properties.IsLeftButtonPressed) return;
            if (args.Source is Visual source && source.GetVisualAncestors().Prepend(source).Any(x => x is Button or TextBox or ScrollBar)) return;
            var point = args.GetPosition(window);
            if (ResizeEdge(point, window.ClientSize) is not { } edge) return;
            // Windows完全交给系统拖拽，避免客户端逐帧改Position与原生Snap/工作区计算互相竞争。
            if (nativeFrame) { window.BeginResizeDrag(edge, args); args.Handled = true; return; }
            resizeEdge = edge; resizePointer = args.Pointer; resizeStart = window.PointToScreen(point);
            resizeOrigin = window.Position; resizeSize = window.ClientSize; resizeScale = window.RenderScaling;
            args.Pointer.Capture(window); args.Handled = true;
        }
        private void Moved(object? sender, PointerEventArgs args)
        {
            if (resizePointer == null)
            {
                var interactive = args.Source is Visual visual && visual.GetVisualAncestors().Prepend(visual).Any(x => x is Button or TextBox or ScrollBar);
                var edge = window.CanResize && window.WindowState == WindowState.Normal && !interactive ? ResizeEdge(args.GetPosition(window), window.ClientSize) : null;
                window.Cursor = edge is { } grip ? resizeCursors[grip] : null;
                return;
            }
            if (args.Pointer.Id != resizePointer.Id) return;
            var now = window.PointToScreen(args.GetPosition(window));
            var dx = (now.X - resizeStart.X) / resizeScale; var dy = (now.Y - resizeStart.Y) / resizeScale;
            var west = resizeEdge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest;
            var east = resizeEdge is WindowEdge.East or WindowEdge.NorthEast or WindowEdge.SouthEast;
            var north = resizeEdge is WindowEdge.North or WindowEdge.NorthWest or WindowEdge.NorthEast;
            var south = resizeEdge is WindowEdge.South or WindowEdge.SouthWest or WindowEdge.SouthEast;
            var width = Math.Clamp(resizeSize.Width + (east ? dx : west ? -dx : 0), window.MinWidth, window.MaxWidth);
            var height = Math.Clamp(resizeSize.Height + (south ? dy : north ? -dy : 0), window.MinHeight, window.MaxHeight);
            window.Width = width; window.Height = height;
            window.Position = new PixelPoint(resizeOrigin.X + (west ? (int)Math.Round((resizeSize.Width - width) * resizeScale) : 0),
                resizeOrigin.Y + (north ? (int)Math.Round((resizeSize.Height - height) * resizeScale) : 0));
            args.Handled = true;
        }
        private void CaptureLost(object? sender, PointerCaptureLostEventArgs args) => Released(null, null!);
        private void Released(object? sender, PointerReleasedEventArgs args)
        { var pointer = resizePointer; resizePointer = null; pointer?.Capture(null); }
        private void Closed(object? sender, EventArgs args)
        {
            disposed = true; Released(null, null!);
            window.Cursor = null;
            window.RemoveHandler(InputElement.PointerPressedEvent, Pressed); window.PointerMoved -= Moved; window.PointerReleased -= Released; window.PointerCaptureLost -= CaptureLost;
            window.Opened -= Opened; window.Closed -= Closed; window.ScalingChanged -= Scaled; window.SizeChanged -= Sized; window.PropertyChanged -= Changed;
            ThemePalette.Background.PropertyChanged -= ThemeChanged; ThemePalette.Line.PropertyChanged -= ThemeChanged;
            ThemePalette.DialogBorder.PropertyChanged -= ThemeChanged;
        }
    }
}
