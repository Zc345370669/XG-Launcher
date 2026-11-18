// SPDX-License-Identifier: MIT
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace XGLauncher;

internal static class IdleScrollbar
{
    internal static void Attach(ScrollViewer viewer)
    {
        ScrollBar? bar = null;
        var idle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        var dragging = false;
        viewer.AllowAutoHide = true;
        viewer.TemplateApplied += (_, e) =>
        {
            bar = e.NameScope.Find<ScrollBar>("PART_VerticalScrollBar");
            if (bar == null) return;
            bar.Opacity = Motion.Enabled ? 0 : 1;
            if (!Motion.Enabled) return;
            bar.Transitions = [new DoubleTransition { Property = Avalonia.Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) }];
            bar.AddHandler(InputElement.PointerPressedEvent, (_, _) => { dragging = true; Show(); }, RoutingStrategies.Tunnel, true);
            bar.AddHandler(InputElement.PointerReleasedEvent, (_, _) => { dragging = false; Show(); }, RoutingStrategies.Tunnel, true);
            bar.PointerCaptureLost += (_, _) => { dragging = false; Show(); };
        };
        viewer.ScrollChanged += (_, e) => { if (e.OffsetDelta.Y != 0) Show(); };
        viewer.PointerEntered += (_, _) => Show();
        viewer.DetachedFromVisualTree += (_, _) => idle.Stop();
        idle.Tick += (_, _) =>
        {
            idle.Stop();
            if (!dragging && bar is not null) bar.Opacity = 0;
        };

        void Show()
        {
            if (bar is null || !Motion.Enabled || !viewer.IsAttachedToVisualTree()) return;
            bar.Opacity = 1;
            idle.Stop();
            if (!dragging) idle.Start();
        }
    }
}
