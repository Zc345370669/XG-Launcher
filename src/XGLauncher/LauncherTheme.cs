// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Controls.Primitives;

namespace XGLauncher;

/// <summary>
/// 在共享Fluent主题上统一控件状态填充和滚动外观，不为精简模式另建主题。
/// </summary>
internal static class LauncherTheme
{
    internal static void Apply(Application app)
    {
        // Fluent按下态缩放会露出外层背景，统一取消该缩放以保持圆角内填充完整。
        var pressed = new Style(x => x.OfType<Button>().Class(":pressed"));
        pressed.Setters.Add(new Setter(Button.RenderTransformProperty, new MatrixTransform(Matrix.Identity))); app.Styles.Add(pressed);
        var fill = new Style(x => x.OfType<Button>().Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
        fill.Setters.Add(new Setter(ContentPresenter.BackgroundSizingProperty, BackgroundSizing.OuterBorderEdge)); app.Styles.Add(fill);
        foreach (var pseudo in new[] { ":pointerover", ":pressed" })
        {
            var state = new Style(x => x.OfType<Button>().Class(pseudo).Not(s => s.Class(":disabled")).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
            state.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, ThemePalette.Hover));
            state.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, ThemePalette.Text)); app.Styles.Add(state);
            var primary = new Style(x => x.OfType<Button>().Class("xg-primary").Class(pseudo).Not(s => s.Class(":disabled")).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));
            primary.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, ThemePalette.Accent));
            primary.Setters.Add(new Setter(ContentPresenter.ForegroundProperty, ThemePalette.OnAccent)); app.Styles.Add(primary);
        }
        var textboxFill = new Style(x => x.OfType<TextBox>().Template().OfType<Border>().Name("PART_BorderElement"));
        textboxFill.Setters.Add(new Setter(Border.BackgroundSizingProperty, BackgroundSizing.OuterBorderEdge)); app.Styles.Add(textboxFill);
        // 所有滚动视口，包括列表和输入模板内的视口，都复用同一控制器。
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<ScrollViewer>((viewer, _) => IdleScrollbar.Attach(viewer));
        var resultItems = new Style(s => s.OfType<ListBox>().Class("xg-catalog-list").Descendant().OfType<ListBoxItem>());
        resultItems.Setters.Add(new Setter(TemplatedControlPadding, new Thickness(0)));
        resultItems.Setters.Add(new Setter(ListBoxItem.MarginProperty, new Thickness(0)));
        resultItems.Setters.Add(new Setter(ListBoxItem.HorizontalContentAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Stretch));
        app.Styles.Add(resultItems);
    }

    private static AvaloniaProperty<Thickness> TemplatedControlPadding => Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty;
}
