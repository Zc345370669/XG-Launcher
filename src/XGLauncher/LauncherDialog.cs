// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace XGLauncher;

internal sealed class LauncherDialog : Window
{
    private readonly ScrollViewer scroller;
    private Border? messageHost;

    internal LauncherDialog(string title, Window owner, bool editor)
    {
        Title = title;
        SystemDecorations = SystemDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = editor;
        var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        var scale = screen?.Scaling ?? owner.RenderScaling;
        var availableWidth = (screen?.WorkingArea.Width ?? 900) / scale;
        var availableHeight = (screen?.WorkingArea.Height ?? 700) / scale;
        MaxWidth = Math.Max(300, availableWidth - 24);
        MaxHeight = Math.Max(240, availableHeight - 24);
        Width = Math.Min(editor ? 490 : 360, MaxWidth);
        MinWidth = Math.Min(editor ? 320 : 300, Width);
        MinHeight = Math.Min(editor ? 240 : 140, MaxHeight);
        var ownerHeight = owner.ClientSize.Height;
        Height = Math.Clamp(editor ? Math.Max(460, Math.Min(650, ownerHeight - 32)) : 158, MinHeight, MaxHeight);
        SizeToContent = SizeToContent.Manual;
        Background = ThemePalette.Panel;
        FontFamily = owner.FontFamily;

        var close = new Button { Content = new LucideIcon("x", 14), Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right };
        Avalonia.Automation.AutomationProperties.SetName(close, "关闭弹窗");
        close.Click += (_, _) => Close();
        var titleLabel = new TextBlock
        {
            Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new(14, 6) };
        header.Children.Add(titleLabel);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        titleLabel.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        scroller = new ScrollViewer
        {
            Name = "DialogScroll", HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, AllowAutoHide = true
        };
        IdleScrollbar.Attach(scroller);
        Footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, ItemSpacing = 12 };
        var footerFrame = new Border { Name = "DialogFooter", Child = Footer, Padding = new(14, 8),
            BorderBrush = ThemePalette.Line, BorderThickness = new(0) };
        var layout = new Grid { RowDefinitions = new RowDefinitions(editor ? "Auto,*,Auto" : "Auto,Auto,Auto") };
        layout.Children.Add(header);
        Grid.SetRow(scroller, 1);
        if (editor) layout.Children.Add(scroller);
        else { messageHost = new Border(); Grid.SetRow(messageHost,1); layout.Children.Add(messageHost); }
        Grid.SetRow(footerFrame, 2);
        layout.Children.Add(footerFrame);
        Content = new Border { Background = ThemePalette.Panel, BorderBrush = ThemePalette.Line, BorderThickness = new(1),
            CornerRadius = new(10), ClipToBounds = true, Child = layout };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || this.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Any(x => x.IsOpen)) return;
            e.Handled = true;
            Close();
        }, RoutingStrategies.Tunnel);
    }

    internal WrapPanel Footer { get; }

    internal void SetBody(Control content, params Control[] actions)
    {
        var body = new Border { Child = content, Padding = new(16, 10, 16, 14),
            MinHeight = CanResize ? 0 : 54,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        if (messageHost != null) messageHost.Child = body; else scroller.Content = body;
        if (!CanResize && content is TextBlock text)
        {
            text.TextAlignment = TextAlignment.Center;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            scroller.VerticalAlignment = VerticalAlignment.Stretch;
            text.TextWrapping = TextWrapping.Wrap;
        }
        // Keep notification/confirmation dialogs compact, but allow long localized
        // messages to grow without clipping. Editors retain their own fixed layout.
        if (!CanResize && content is TextBlock message)
        {
            var complete = message.Text ?? "";
            var maxSummary = Math.Max(160,(int)((MaxHeight - 150) / 22) * 35);
            if (complete.Length > maxSummary) {
                message.Text = complete[..maxSummary] + "…";
                var details = new Button { Content = "查看详情" };
                details.Click += async (_,_) => {
                    var viewer = new LauncherDialog("查看详情",this,true);
                    viewer.SetBody(new TextBox { Text = complete, IsReadOnly = true, TextWrapping = TextWrapping.Wrap });
                    await viewer.ShowDialog(this);
                };
                actions = actions.Append(details).ToArray();
            }
            Width = Math.Min(complete.Length > 180 ? 560 : actions.Length > 1 ? 380 : 340,MaxWidth);
            message.TextTrimming = TextTrimming.None;
            message.MaxWidth = Math.Max(100,Width - 40);
            message.Width = message.MaxWidth;
            message.Measure(new Size(Math.Max(100,Width-40),double.PositiveInfinity));
            MinHeight = 0;
            Height = Math.Min(MaxHeight, message.DesiredSize.Height + 120);
            scroller.MinHeight = 0;
        }
        Footer.Children.Clear();
        foreach (var action in actions) Footer.Children.Add(action);
        Footer.IsVisible = actions.Length > 0;
        if (!CanResize) {
            body.Measure(new Size(Math.Max(100,Width - 2),double.PositiveInfinity));
            Footer.Measure(new Size(Math.Max(100,Width - 30),double.PositiveInfinity));
            Height = Math.Min(MaxHeight, Math.Max(120,body.DesiredSize.Height + Footer.DesiredSize.Height + 60));
            SizeToContent = SizeToContent.Height;
        }
    }
}
