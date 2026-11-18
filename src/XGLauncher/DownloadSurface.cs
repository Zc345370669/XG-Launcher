// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Styling;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace XGLauncher;

internal sealed class DownloadSurface : Grid
{
    private readonly ContentControl header = new();
    internal ListBox Tasks { get; } = new() {
        Name = "DownloadTasks", BorderThickness = new(0), Background = Avalonia.Media.Brushes.Transparent,
        ItemTemplate = new FuncDataTemplate<Func<Control>>((factory, _) => factory?.Invoke())
    };
    internal DownloadSurface()
    {
        Name = "DownloadSurface"; RowDefinitions = new("Auto,*"); Margin = new(18, 14);
        Tasks.Classes.Add("xg-catalog-list");
        // The default ListBoxItem selection/hover template can paint a second
        // blue rectangle beneath a task card. The card itself owns its border.
        // Suppress ListBoxItem chrome in normal, hover and selected states.
        // Apply a common local style so the task card is the only painted surface.
        var itemStyle = new Style(x => x.OfType<ListBoxItem>());
        itemStyle.Setters.Add(new Setter(ListBoxItem.TemplateProperty, new FuncControlTemplate<ListBoxItem>((item, scope) => new Avalonia.Controls.Presenters.ContentPresenter { [!Avalonia.Controls.Presenters.ContentPresenter.ContentProperty] = item[!ListBoxItem.ContentProperty], [!Avalonia.Controls.Presenters.ContentPresenter.ContentTemplateProperty] = item[!ListBoxItem.ContentTemplateProperty], HorizontalContentAlignment = HorizontalAlignment.Stretch })));
        itemStyle.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(ListBoxItem.BorderBrushProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        Tasks.Styles.Add(itemStyle);
        foreach (var pseudo in new[] { ":pointerover", ":selected", ":focus", ":focus-within" })
        {
            var stateStyle = new Style(x => x.OfType<ListBoxItem>().Class(pseudo));
            stateStyle.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, Brushes.Transparent));
            stateStyle.Setters.Add(new Setter(ListBoxItem.BorderBrushProperty, Brushes.Transparent));
            Tasks.Styles.Add(stateStyle);
        }
        ScrollViewer.SetHorizontalScrollBarVisibility(Tasks, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(Tasks, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        Children.Add(header); Grid.SetRow(Tasks, 1); Children.Add(Tasks);
    }
    internal void Update(Control heading, IReadOnlyList<Func<Control>> cards)
    {
        var scroll = Tasks.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var offset = scroll?.Offset ?? default;
        header.Content = heading; header.Margin = new(0, 0, 0, 10);
        Tasks.ItemsSource = cards;
        Dispatcher.UIThread.Post(() => {
            var current = Tasks.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (current != null) current.Offset = new Vector(0, offset.Y);
        }, DispatcherPriority.Loaded);
    }
}
