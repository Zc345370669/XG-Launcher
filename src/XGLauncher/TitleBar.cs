// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private Image? captionLogo;
    private Border? captionFrame;
    private Button? modeButton;
    private Button? maximizeButton;
    private Button? minimizeButton;

    private void UpdateCaptionMode()
    {
        if (modeButton == null) return;
        var text = IsCompact ? "切换完整模式" : "切换精简模式";
        ToolTip.SetTip(modeButton, text);
        AutomationProperties.SetName(modeButton, text);
        account.IsVisible = !IsCompact;
        if (captionLogo != null) captionLogo.IsVisible = !IsCompact;
        if (captionFrame != null) { captionFrame.BorderThickness = IsCompact ? new(0) : new(0, 0, 0, 1); captionFrame.Background = IsCompact ? Brushes.Transparent : ThemePalette.Panel; }
        maximizeButton!.IsVisible = minimizeButton!.IsVisible = !IsCompact;
    }

    private Control CreateTitleBar(Image logo)
    {
        captionLogo = logo;
        var caption = new Border
        {
            Background = Brushes.Transparent, Padding = new(14, 0, 0, 0),
            Child = Row(logo, Label("瞎搞启动器", 16, true))
        };
        logo.Width = 32;
        logo.Height = 28;
        logo.VerticalAlignment = VerticalAlignment.Center;
        caption.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (args.ClickCount == 2) ToggleMaximized();
            else if (args.ClickCount == 1) BeginMoveDrag(args);
            args.Handled = true;
        };

        var minimize = CaptionButton("最小化", "minus", () => WindowState = WindowState.Minimized);
        var maximize = CaptionButton("最大化", "square", ToggleMaximized);
        minimizeButton = minimize; maximizeButton = maximize;
        modeButton = CaptionButton("切换精简模式", "panels-top-left", ToggleMode);
        modeButton.Name = "SwitchMode";
        var close = CaptionButton("关闭", "x", () => { exitRequested = true; Close(); });
        PropertyChanged += (_, args) =>
        {
            if (args.Property != WindowStateProperty) return;
            var maximized = WindowState == WindowState.Maximized;
            ((LucideIcon)maximize.Content!).Icon = maximized ? "copy" : "square";
            var label = maximized ? "还原" : "最大化";
            ToolTip.SetTip(maximize, label);
            AutomationProperties.SetName(maximize, label);
        };

        account.MaxWidth = 175;
        account.Styles.Add(new Style(x => x.OfType<TextBlock>())
        {
            Setters = { new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis),
                new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap) }
        });
        account.PropertyChanged += (_, args) =>
        { if (args.Property == ContentControl.ContentProperty && account.Content is string text) ToolTip.SetTip(account, text); };
        account.MinHeight = 34;
        account.VerticalAlignment = VerticalAlignment.Center;
        account.Margin = new(0, 0, 8, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { account, modeButton, minimize, maximize, close }) actions.Children.Add(button);
        var grid = new Grid { Name = "LauncherTitleBar", Height = WindowChrome.CaptionHeight, ColumnDefinitions = new("*,Auto") };
        // 窄完整窗口先收拢账号入口，仍保留可见可点的标题和所有窗口按钮。
        grid.SizeChanged += (_, _) => account.MaxWidth = grid.Bounds.Width < 700 ? 100 : 175;
        grid.Children.Add(caption);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        captionFrame = Frame(grid, new(0), new(0, 0, 0, 1));
        return captionFrame;
    }

    internal void ToggleMaximized() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private static Button CaptionButton(string label, string path, System.Action click)
    {
        var button = new Button
        {
            Width = 43, Height = 42, CornerRadius = new(7), Padding = new(0),
            Background = Brushes.Transparent, BorderThickness = new(0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Content = new LucideIcon(path, 15)

        };
        button.Classes.Add("xg-caption");
        ToolTip.SetTip(button, label);
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => click();
        return button;
    }
}
