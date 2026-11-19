// SPDX-License-Identifier: MIT
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace XGLauncher;

internal sealed class SettingsSurface : Grid
{
    private static readonly IBrush Selected = ThemePalette.Hover;
    private readonly StackPanel body = new() { ClipToBounds = true };
    private readonly ScrollViewer scroll;
    private readonly Control[] categories;
    private readonly Button[] buttons;
    private readonly Border reachedEnd;
    private readonly DispatcherTimer move = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer hideBar = new() { Interval = TimeSpan.FromMilliseconds(620) };
    private readonly DispatcherTimer hideEnd = new() { Interval = TimeSpan.FromMilliseconds(850) };
    private readonly Stopwatch motion = new();
    private ScrollBar? vertical;
    private bool dragging;
    private bool endShown;
    private double from;
    private double to;
    private int active = -1;

    internal ScrollViewer ContentScroll => scroll;
    internal IReadOnlyList<Control> Sections => categories;
    internal int ActiveSection => active;
    internal bool ShowsEndMessage => reachedEnd.IsVisible;

    internal SettingsSurface(string[] names, Control[] sections)
    {
        Name = "SettingsSurface";
        categories = sections;
        buttons = new Button[names.Length];
        ColumnDefinitions = new ColumnDefinitions("138,*");
        var menu = new StackPanel { Margin = new(6, 13) };
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            var item = new Button
            {
                Content = new TextBlock { Text = names[i], TextWrapping = TextWrapping.Wrap }, BorderThickness = new(0), CornerRadius = new(0), Background = Brushes.Transparent,
                Padding = new(12, 11), HorizontalAlignment = HorizontalAlignment.Stretch
            };
            if (Motion.Enabled)
                item.Transitions = new Transitions
                {
                    new BrushTransition { Property = TemplatedControl.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(110) }
                };
            AutomationProperties.SetName(item, names[i]);
            item.Click += (_, _) => ScrollToSection(index);
            ScrollViewer.SetBringIntoViewOnFocusChange(item, false);
            menu.Children.Add(item);
            buttons[i] = item;
        }
        var menuViewport = new ScrollViewer
        {
            Content = menu, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Children.Add(new Border
        {
            Background = ThemePalette.Navigation, BorderBrush = ThemePalette.Line,
            BorderThickness = new(0, 0, 1, 0), Child = menuViewport
        });

        foreach (var child in categories) body.Children.Add(child);
        scroll = new ScrollViewer
        {
            Name = "SettingsContentScroll", Content = body, ClipToBounds = true, AllowAutoHide = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        ScrollViewer.SetBringIntoViewOnFocusChange(scroll, false);
        scroll.TemplateApplied += (_, args) => ConnectBar(args.NameScope.Find<ScrollBar>("PART_VerticalScrollBar"));
        scroll.ScrollChanged += (_, args) =>
        {
            if (args.ViewportDelta.Y != 0 && scroll.Viewport.Height > 0)
                categories[^1].MinHeight = scroll.Viewport.Height;
            if (args.OffsetDelta.Y != 0) ShowScrollbar();
            if (scroll.Offset.Y < MaximumOffset - 24) endShown = false;
            if (!move.IsEnabled) UpdateFromScroll();
        };
        scroll.AddHandler(PointerWheelChangedEvent, WheelInput, RoutingStrategies.Tunnel, true);
        scroll.AddHandler(PointerPressedEvent, (_, _) => InterruptAnimation(), RoutingStrategies.Tunnel, true);
        scroll.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End) InterruptAnimation();
        }, RoutingStrategies.Tunnel, true);

        reachedEnd = new Border
        {
            Name = "SettingsScrollEnd", Background = ThemePalette.Accent, CornerRadius = new(10), Padding = new(12, 7),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new(0, 0, 0, 12), IsHitTestVisible = false, IsVisible = false,
            Child = new TextBlock { Text = "已经到底了～", Foreground = ThemePalette.OnAccent }
        };
        AutomationProperties.SetName(reachedEnd, "已经到底了～");
        var content = new Grid { ClipToBounds = true };
        content.Children.Add(scroll);
        content.Children.Add(reachedEnd);
        SetColumn(content, 1); Children.Add(content);

        move.Tick += (_, _) =>
        {
            var step = Math.Clamp(motion.Elapsed.TotalMilliseconds / 180, 0, 1);
            scroll.Offset = new Vector(0, from + (to - from) * (1 - Math.Pow(1 - step, 3)));
            if (step >= 1) move.Stop();
        };
        hideBar.Tick += (_, _) =>
        {
            if (dragging) return;
            hideBar.Stop();
            if (vertical != null) vertical.Opacity = 0;
        };
        hideEnd.Tick += (_, _) =>
        {
            hideEnd.Stop();
            reachedEnd.IsVisible = false;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            move.Stop(); hideBar.Stop(); hideEnd.Stop();
            reachedEnd.IsVisible = false;
            body.RenderTransform = null;
            dragging = false;
            if (vertical != null) vertical.Opacity = 0;
        };
        SetActive(0);
    }

    private double MaximumOffset => Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);

    internal void ScrollToSection(int index, bool? animate = null)
    {
        move.Stop();
        SetActive(index);
        from = scroll.Offset.Y;
        to = Math.Clamp(categories[index].Bounds.Top + .5, 0, MaximumOffset);
        if (animate.GetValueOrDefault(Motion.Enabled) && Math.Abs(to - from) > 2)
        {
            motion.Restart();
            move.Start();
        }
        else scroll.Offset = new Vector(0, to);
    }

    private void InterruptAnimation()
    {
        if (!move.IsEnabled) return;
        move.Stop();
        UpdateFromScroll();
    }

    private void UpdateFromScroll()
    {
        var offset = scroll.Offset.Y;
        var index = 0;
        for (var i = 1; i < categories.Length; i++)
            if (categories[i].Bounds.Height > 0 && categories[i].Bounds.Top <= offset + 28) index = i;
        SetActive(index);
    }

    private void SetActive(int index)
    {
        if (index == active) return;
        if (active >= 0) buttons[active].Background = Brushes.Transparent;
        active = index;
        buttons[active].Background = Selected;
        buttons[active].BringIntoView();
    }

    private void ConnectBar(ScrollBar? bar)
    {
        if (vertical != null)
        {
            vertical.RemoveHandler(Thumb.DragStartedEvent, DragStarted);
            vertical.RemoveHandler(Thumb.DragCompletedEvent, DragCompleted);
        }
        vertical = bar;
        if (vertical == null) return;
        vertical.Opacity = 0;
        if (Motion.Enabled)
            vertical.Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(130) }
            };
        vertical.AddHandler(Thumb.DragStartedEvent, DragStarted, RoutingStrategies.Bubble, true);
        vertical.AddHandler(Thumb.DragCompletedEvent, DragCompleted, RoutingStrategies.Bubble, true);
    }

    private void DragStarted(object? sender, VectorEventArgs args)
    {
        InterruptAnimation();
        dragging = true;
        ShowScrollbar();
    }

    private void DragCompleted(object? sender, VectorEventArgs args)
    {
        dragging = false;
        ShowScrollbar();
    }

    private void ShowScrollbar()
    {
        if (vertical == null) return;
        if (!Motion.Enabled) vertical.Transitions = null;
        vertical.Opacity = 1;
        hideBar.Stop();
        hideBar.Start();
    }

    private void WheelInput(object? sender, PointerWheelEventArgs args)
    {
        InterruptAnimation();
        ShowScrollbar();
        if (args.Delta.Y >= 0 || MaximumOffset < 1 || scroll.Offset.Y < MaximumOffset - 1 || endShown) return;
        endShown = true;
        reachedEnd.IsVisible = true;
        hideEnd.Stop();
        hideEnd.Start();
        if (!Motion.Enabled) return;
        var bounce = new TranslateTransform(0, -7)
        {
            Transitions = new Transitions
            {
                new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(165), Easing = new CubicEaseOut() }
            }
        };
        body.RenderTransform = bounce;
        Dispatcher.UIThread.Post(() => bounce.Y = 0, DispatcherPriority.Background);
    }
}
