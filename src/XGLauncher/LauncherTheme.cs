// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;

namespace XGLauncher;

internal static class LauncherTheme
{
    internal static void Apply(Application app)
    {
        var quietHover = ThemePalette.Hover;
        var darkHover = ThemePalette.Accent;
        foreach (var name in new[] { "xg-navigation", "xg-caption", "xg-primary", "xg-action" })
        {
            var style = new Style(s => s.OfType<Button>().Class(name).Class(":pointerover")
                .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
            style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, name == "xg-primary" ? darkHover : quietHover));
            style.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, name == "xg-primary" ? ThemePalette.OnAccent : ThemePalette.Text));
            app.Styles.Add(style);
        }

        var resultItems = new Style(s => s.OfType<ListBox>().Class("xg-catalog-list").Descendant().OfType<ListBoxItem>());
        resultItems.Setters.Add(new Setter(TemplatedControlPadding, new Thickness(0)));
        resultItems.Setters.Add(new Setter(ListBoxItem.MarginProperty, new Thickness(0)));
        resultItems.Setters.Add(new Setter(ListBoxItem.HorizontalContentAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Stretch));
        app.Styles.Add(resultItems);
    }

    private static AvaloniaProperty<Thickness> TemplatedControlPadding => Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty;
}
