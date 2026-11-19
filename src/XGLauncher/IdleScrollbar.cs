// SPDX-License-Identifier: MIT
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace XGLauncher;

/// <summary>
/// 统一滚动视口的轻量滑块和空闲淡出，避免显隐引起内容布局跳动。
/// </summary>
internal static class IdleScrollbar
{
    private static readonly ConditionalWeakTable<ScrollViewer, Controller> controllers = new();
    internal static void Attach(ScrollViewer viewer)
    { if (!controllers.TryGetValue(viewer, out _)) controllers.Add(viewer, new Controller(viewer)); }
    internal static void HorizontalWheel(ScrollViewer viewer)
    { Attach(viewer); controllers.GetValue(viewer, v => new Controller(v)).Horizontal = true; }
    internal static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(1500);
    private sealed class Controller
    {
        private readonly ScrollViewer viewer;
        private readonly DispatcherTimer idle = new() { Interval = IdleDelay };
        private ScrollBar[] bars = [];
        private bool dragging;
        internal bool Horizontal;
        internal Controller(ScrollViewer viewer)
        {
            this.viewer = viewer; viewer.AllowAutoHide = true;
            viewer.TemplateApplied += (_, _) => Configure();
            viewer.AttachedToVisualTree += (_, _) => Configure();
            viewer.DetachedFromVisualTree += (_, _) => { idle.Stop(); dragging = false; Hide(); };
            viewer.ScrollChanged += (_, e) => { if (e.OffsetDelta != default) Show(); };
            viewer.PointerEntered += (_, _) => Show(); viewer.PointerMoved += (_, _) => Show();
            viewer.AddHandler(InputElement.PointerWheelChangedEvent, Wheel, RoutingStrategies.Tunnel);
            idle.Tick += (_, _) => { idle.Stop(); if (!dragging) Hide(); };
            Configure();
        }
        private void Configure()
        {
            viewer.AllowAutoHide = true;
            bars = viewer.GetVisualDescendants().OfType<ScrollBar>().Where(b => ReferenceEquals(b.TemplatedParent, viewer)).ToArray();
            foreach (var bar in bars)
            {
                bar.Background = Brushes.Transparent; bar.MinWidth = bar.MinHeight = 0;
                if (bar.Orientation == Orientation.Vertical) bar.Width = 8; else bar.Height = 8;
                bar.Opacity = 0; bar.IsHitTestVisible = false;
                if (Motion.Enabled) bar.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) }];
                if (!bar.Classes.Contains("xg-idle-bar"))
                {
                    bar.Classes.Add("xg-idle-bar");
                    bar.AddHandler(InputElement.PointerPressedEvent, (_, _) => { dragging = true; Show(); }, RoutingStrategies.Tunnel, true);
                    bar.AddHandler(InputElement.PointerReleasedEvent, (_, _) => { dragging = false; Show(); }, RoutingStrategies.Tunnel, true);
                    bar.PointerCaptureLost += (_, _) => { dragging = false; Show(); };
                }
                bar.ApplyTemplate();
                foreach (var line in bar.GetVisualDescendants().OfType<RepeatButton>().Where(x => x.Name is "PART_LineUpButton" or "PART_LineDownButton")) line.IsVisible = false;
                foreach (var track in bar.GetVisualDescendants().OfType<Rectangle>().Where(x => x.Name == "TrackRect")) track.IsVisible = false;
                foreach (var thumb in bar.GetVisualDescendants().OfType<Thumb>())
                {
                    thumb.Background = ThemePalette.Muted; thumb.CornerRadius = new(3);
                    thumb.RenderTransform = new ScaleTransform(1,1);
                    if (bar.Orientation == Orientation.Vertical) thumb.Width = 6; else thumb.Height = 6;
                }
            }
        }
        private void Show()
        {
            if (!viewer.IsAttachedToVisualTree()) return;
            foreach (var b in bars) { b.Opacity = 1; b.IsHitTestVisible = true; }
            idle.Stop(); if (!dragging) idle.Start();
        }
        private void Hide() { foreach (var b in bars) { b.Opacity = 0; b.IsHitTestVisible = false; } }
        private void Wheel(object? sender, PointerWheelEventArgs e)
        {
            var nearest = (e.Source as Visual)?.GetVisualAncestors().Prepend(e.Source as Visual).OfType<ScrollViewer>().FirstOrDefault();
            if (nearest != null && !ReferenceEquals(nearest, viewer)) return;
            Show();
            if ((Horizontal || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) && viewer.Extent.Width > viewer.Viewport.Width)
            {
                var delta = e.Delta.X != 0 ? e.Delta.X : e.Delta.Y;
                viewer.Offset = new Vector(Math.Clamp(viewer.Offset.X - delta * 60, 0, Math.Max(0, viewer.Extent.Width - viewer.Viewport.Width)), viewer.Offset.Y);
                e.Handled = true;
            }
        }
    }
}
