// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private Control CreateTitleBar(Image logo)
    {
        if (OperatingSystem.IsWindows())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = 0;
        }

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
        var close = CaptionButton("关闭", "x", Close);
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
        account.MinHeight = 34;
        account.VerticalAlignment = VerticalAlignment.Center;
        account.Margin = new(0, 0, 8, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { account, minimize, maximize, close }) actions.Children.Add(button);
        var grid = new Grid { Name = "LauncherTitleBar", Height = 44, ColumnDefinitions = new("*,Auto") };
        grid.Children.Add(caption);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return Frame(grid, new(0), new(0, 0, 0, 1));
    }

    private void ToggleMaximized() =>
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
