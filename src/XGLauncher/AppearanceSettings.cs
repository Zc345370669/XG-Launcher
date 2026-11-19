// SPDX-License-Identifier: MIT
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using XG.Core;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private sealed record AppearancePreferences(string Theme = "雾蓝", double Opacity = .96, bool Blur = false);
    private AppearancePreferences appearance = new();
    private readonly List<(Button Button, string Name)> themeCards = [];
    private TextBlock? appearanceEffectStatus;
    private string AppearancePath => Path.Combine(model.Store.Root, "appearance.json");
    private void LoadAppearance()
    {
        try { if (File.Exists(AppearancePath)) appearance = JsonSerializer.Deserialize<AppearancePreferences>(File.ReadAllText(AppearancePath)) ?? new(); }
        catch (Exception error) when (error is IOException or JsonException) { status.Text = "外观设置读取失败，使用默认主题。"; }
        ApplyAppearance(false);
    }
    private void ApplyAppearance(bool save = true)
    {
        appearance = appearance with { Opacity = WindowEffects.SafeOpacity(appearance.Opacity) };
        ThemePalette.Select(ThemePalette.All.FirstOrDefault(x => x.Name == appearance.Theme) ?? ThemePalette.All[0], appearance.Opacity);
        WindowEffects.Apply(this, appearance.Opacity, appearance.Blur);
        if (appearanceEffectStatus != null) appearanceEffectStatus.Text = ActualTransparencyLevel == WindowTransparencyLevel.None
            ? "当前系统不支持透明效果，已使用不透明显示；设置会保留。"
            : appearance.Blur && ActualTransparencyLevel != WindowTransparencyLevel.AcrylicBlur
                ? "当前系统未提供毛玻璃，已使用普通透明显示。" : "透明度实时应用；窗口圆角由系统管理。";
        Background = ActualTransparencyLevel == WindowTransparencyLevel.None ? ThemePalette.Background : Brushes.Transparent;
        if (windowShell != null) windowShell.Background = ActualTransparencyLevel == WindowTransparencyLevel.None ? ThemePalette.Background : ThemePalette.WindowBackground;
        TransparencyBackgroundFallback = ThemePalette.Background;
        foreach (var (button, name) in themeCards)
        {
            button.BorderThickness = new(name == ThemePalette.Current.Name ? 2 : 1);
            button.BorderBrush = name == ThemePalette.Current.Name ? ThemePalette.Accent : ThemePalette.Line;
            Avalonia.Automation.AutomationProperties.SetName(button, name + (name == ThemePalette.Current.Name ? " 已选中" : ""));
            if (button.Content is StackPanel content && content.Children.LastOrDefault() is TextBlock marker)
                { marker.Text = name == ThemePalette.Current.Name ? "已选中" : "点击应用"; if (content.Children.FirstOrDefault() is Grid preview && preview.Children.OfType<LucideIcon>().FirstOrDefault() is {} check) check.IsVisible = name == ThemePalette.Current.Name; }
        }
        if (save) InstanceStore.WriteJson(AppearancePath, appearance);
    }
    private Control AppearanceSettings()
    {
        var language = new UnifiedExpandableSelect { Title = "语言", ItemsSource = new[] { "简体中文", "English" }, InlineExpansion = true, MaximumOpenHeight = 150 };
        language.SelectedIndex = Localizer.Language == "en-US" ? 1 : 0;
        language.SelectionChanged += (_, _) => Localizer.Set(language.SelectedIndex == 1 ? "en-US" : "zh-CN", model.Store.Root);
        var cards = new UniformGrid { Columns = 2, RowSpacing = 10, ColumnSpacing = 10 };
        themeCards.Clear();
        foreach (var palette in ThemePalette.All)
        {
            var mini = new Grid { ColumnDefinitions = new("28,*"), Height = 70, Background = Brush.Parse(palette.Background) };
            mini.Children.Add(new Border { Background = Brush.Parse(palette.Navigation), Padding = new(5),
                Child = new StackPanel { Spacing = 6, Children = {
                    new Border { Height = 5, Background = Brush.Parse(palette.Accent) },
                    new Border { Height = 4, Background = Brush.Parse(palette.Line) },
                    new Border { Height = 4, Background = Brush.Parse(palette.Line) } } } });
            var detail = new StackPanel { Margin = new(9), Spacing = 7, Children = {
                new Border { Height = 4, Width = 60, HorizontalAlignment = HorizontalAlignment.Left, Background = Brush.Parse(palette.Text) },
                new Border { Height = 15, Background = Brush.Parse(palette.Panel), BorderBrush = Brush.Parse(palette.Line), BorderThickness = new(1) },
                new Border { Height = 12, Width = 36, HorizontalAlignment = HorizontalAlignment.Right, CornerRadius = new(3), Background = Brush.Parse(palette.Accent) } } };
            Grid.SetColumn(detail, 1); mini.Children.Add(detail);
            var selectedIcon = new LucideIcon("check",14) { Foreground=ThemePalette.Accent, HorizontalAlignment=HorizontalAlignment.Right, VerticalAlignment=VerticalAlignment.Top, Margin=new(0,2,2,0) }; Grid.SetColumn(selectedIcon,1); mini.Children.Add(selectedIcon);
            var button = Action("", () => { appearance = appearance with { Theme = palette.Name }; ApplyAppearance(); return Task.CompletedTask; });
            button.Content = Column(mini, Label(palette.Name, 15, true), Label(palette.Description, 12), Label("", 11));
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            themeCards.Add((button, palette.Name)); cards.Children.Add(button);
        }
        var opacity = new Slider { Minimum = 85, Maximum = 100, Value = appearance.Opacity * 100, TickFrequency = 1, IsSnapToTickEnabled = true, MinWidth = 140 };
        Avalonia.Automation.AutomationProperties.SetName(opacity, "窗口不透明度");
        var percent = Label($"{opacity.Value:0}%");
        opacity.ValueChanged += (_, _) => { appearance = appearance with { Opacity = opacity.Value / 100 }; percent.Text = $"{opacity.Value:0}%"; ApplyAppearance(); };
        var blur = new CheckBox { IsChecked = appearance.Blur, Content = "轻微毛玻璃效果" };
        blur.IsCheckedChanged += (_, _) => { appearance = appearance with { Blur = blur.IsChecked == true }; ApplyAppearance(); };
        opacity.IsEnabled = true;
        blur.IsEnabled = WindowEffects.MayRequestBlur(OperatingSystem.IsWindows(), Environment.OSVersion.Version);
        ToolTip.SetTip(blur, blur.IsEnabled ? "由系统决定毛玻璃是否可用，不支持时自动回退。" : "当前平台的窗口合成不支持此毛玻璃选项。");
        appearanceEffectStatus = Label("", 12);
        ApplyAppearance(false);
        return Column(SettingRow("语言", language), Label("主题", 17, true), cards,
            Label("窗口不透明度", 14, true), opacity, percent, blur, appearanceEffectStatus);
    }
}
