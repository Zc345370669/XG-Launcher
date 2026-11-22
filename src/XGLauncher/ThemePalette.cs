// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace XGLauncher;

internal static class ThemePalette
{
    internal sealed record Palette(string Name, string Description, string Background, string Panel, string Navigation,
        string Text, string Muted, string Line, string Accent, string OnAccent, string Hover, bool Dark = false);
    internal static readonly Palette[] All = [
        new("雾蓝", "清爽、科技感", "#F7FAFE", "#F7FAFE", "#EAF0F9", "#24344A", "#596A80", "#CBD7E7", "#456B9E", "#FFFFFF", "#DFE9F6"),
        new("浅紫", "柔和、精致", "#FBF8FE", "#FBF8FE", "#EFE9F8", "#3E3450", "#746580", "#DDD0EA", "#8061A3", "#FFFFFF", "#E9DFF4"),
        new("薄荷绿", "自然、舒适", "#F8FCFA", "#F8FCFA", "#E5F2EC", "#25473A", "#577568", "#B5CEC4", "#387B63", "#FFFFFF", "#D9EDE2"),
        new("暖米白", "温暖、耐看", "#FCF9F3", "#FCF9F3", "#F1EADF", "#4D3E30", "#786957", "#E0D3C1", "#896748", "#FFFFFF", "#EDE2D2"),
        new("深海蓝", "深色、沉稳", "#111D30", "#1C2B42", "#15243A", "#EDF4FF", "#B0C2DA", "#3C526F", "#91BCEE", "#12223A", "#304764", true),
        new("经典黑白", "原有黑白灰风格", "#FFFFFF", "#FFFFFF", "#FBFBFB", "#181818", "#646464", "#DCDCDC", "#181818", "#FFFFFF", "#EEEEEE")
    ];
    // 共享可变画刷同时注册到全局资源，使既有控件、浮层和弹窗原地更新。
    internal static readonly SolidColorBrush Background = new();
    internal static readonly SolidColorBrush WindowBackground = new();
    internal static readonly SolidColorBrush Panel = new();
    internal static readonly SolidColorBrush DialogSurface = new();
    internal static readonly SolidColorBrush DialogBorder = new();
    internal static readonly SolidColorBrush Navigation = new();
    internal static readonly SolidColorBrush Text = new();
    internal static readonly SolidColorBrush Muted = new();
    internal static readonly SolidColorBrush Line = new();
    internal static readonly SolidColorBrush Accent = new();
    internal static readonly SolidColorBrush OnAccent = new();
    internal static readonly SolidColorBrush Hover = new();
    internal static Palette Current { get; private set; } = All[0];
    static ThemePalette() => Select(All[0]);
    /// <summary>
    /// 原地更新共享画刷，使既有控件、弹层和两种模式同步响应主题。
    /// </summary>
    internal static void Select(Palette palette, double opacity = .96)
    {
        Current = palette;
        Background.Color = Color.Parse(palette.Background); Panel.Color = Color.Parse(palette.Panel);
        var panel = Panel.Color;
        DialogSurface.Color = palette.Dark ? Color.FromRgb((byte)(panel.R + 5), (byte)(panel.G + 5), (byte)(panel.B + 5))
            : Color.FromRgb((byte)(panel.R - 4), (byte)(panel.G - 4), (byte)(panel.B - 4));
        DialogBorder.Color = Color.Parse(palette.Dark ? "#68717F" : "#A8ADB5");
        Navigation.Color = Color.Parse(palette.Navigation); Text.Color = Color.Parse(palette.Text);
        Muted.Color = Color.Parse(palette.Muted); Line.Color = Color.Parse(palette.Line);
        Accent.Color = Color.Parse(palette.Accent); OnAccent.Color = Color.Parse(palette.OnAccent); Hover.Color = Color.Parse(palette.Hover);
        var bg = Background.Color;
        WindowBackground.Color = Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, .85, 1) * 255), bg.R, bg.G, bg.B);
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = palette.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        foreach (var (key, brush) in new[] { ("XgBackground", Background), ("XgPanel", Panel), ("XgDialogSurface", DialogSurface), ("XgDialogBorder", DialogBorder), ("XgText", Text),
            ("XgAccent", Accent), ("XgOnAccent", OnAccent), ("XgLine", Line), ("XgHover", Hover), ("XgNavigation", Navigation) }) app.Resources[key] = brush;
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
            "SystemControlHighlightListLowBrush", "SystemControlHighlightListMediumBrush", "SystemControlHighlightListAccentLowBrush", "SystemControlHighlightListAccentMediumBrush", "SystemControlHighlightListAccentHighBrush" }) app.Resources[key] = Hover;
        foreach (var key in new[] { "ButtonForegroundPointerOver", "ButtonForegroundPressed", "TextControlForeground", "TextControlForegroundFocused", "MenuFlyoutItemForeground", "MenuFlyoutItemForegroundPointerOver", "SystemControlHighlightAltBaseHighBrush" }) app.Resources[key] = Text;
        foreach (var key in new[] { "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed", "TextControlBorderBrushPointerOver" }) app.Resources[key] = Line;
        foreach (var key in new[] { "ScrollBarThumbFillPointerOver", "ScrollBarThumbFillPressed", "SliderThumbBackground", "SliderTrackFill" }) app.Resources[key] = Accent;
        app.Resources["TextControlBackground"] = Panel; app.Resources["TextControlBorderBrush"] = Line;
        app.Resources["MenuFlyoutPresenterBackground"] = Panel; app.Resources["MenuFlyoutPresenterBorderBrush"] = Line;
        app.Resources["MenuFlyoutItemForegroundPressed"] = Text;
        app.Resources["ScrollBarSize"] = 8d;
        app.Resources["MenuFlyoutItemBackgroundPointerOver"] = Hover; app.Resources["MenuFlyoutItemBackgroundPressed"] = Hover;
        app.Resources["SystemAccentColor"] = Accent.Color;
        foreach (var shade in new[] { "Light1", "Light2", "Light3", "Dark1", "Dark2", "Dark3" }) app.Resources["SystemAccentColor" + shade] = Accent.Color;
    }
}
