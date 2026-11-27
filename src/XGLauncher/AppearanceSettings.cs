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
    private sealed record AppearancePreferences(string Theme = "雾蓝");
    private AppearancePreferences appearance = new();
    private readonly List<(Button Button, string Name)> themeCards = [];
    private string AppearancePath => Path.Combine(model.Store.Root, "appearance.json");
    private void LoadAppearance()
    {
        try { if (File.Exists(AppearancePath)) appearance = JsonSerializer.Deserialize<AppearancePreferences>(File.ReadAllText(AppearancePath)) ?? new(); }
        catch (Exception error) when (error is IOException or JsonException) { status.Text = "外观设置读取失败，使用默认主题。"; }
        ApplyAppearance(false);
    }
    private void ApplyAppearance(bool save = true)
    {
        ThemePalette.Select(ThemePalette.All.FirstOrDefault(x => x.Name == appearance.Theme) ?? ThemePalette.All[0]);
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
        cards.SizeChanged += (_, _) => cards.Columns = cards.Bounds.Width < 360 ? 1 : 2;
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
        ApplyAppearance(false);
        return Column(SettingRow("语言", language), Label("主题", 17, true), cards);
    }
}
