// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;

namespace XGLauncher;

internal sealed class CatalogSurface : Grid
{
    private readonly Grid columns = new() { ColumnDefinitions = new("*,0"), ClipToBounds = true };
    private readonly Border detail = new() { Padding = new(12), IsVisible = false, ClipToBounds = true };
    private readonly ScrollViewer detailScroll = Scroll();
    public ObservableCollection<Func<Control>> Rows { get; } = [];
    public ListBox Results { get; }
    public ContentControl Feedback { get; } = new() { IsVisible = false };
    public event Action? NearEnd;

    public void SetFeedback(Control? control)
    {
        Feedback.Content = control;
        Feedback.IsVisible = control != null;
    }

    public void ScrollToFirst()
    {
        if (Results.ItemCount > 0) Results.ScrollIntoView(0);
    }
    public bool IsDetailOpen => detail.IsVisible;
    public event Action? DensityChanged;
    public bool Compact => IsDetailOpen || Bounds.Width < 660;

    public CatalogSurface(Control header, Control filters, Control close, StackPanel detailContent)
    {
        Margin = new(18, 14);
        RowDefinitions = new("Auto,*");
        Details = detailContent;
        Results = new ListBox
        {
            ItemsSource = Rows,
            Background = Brushes.Transparent,
            BorderThickness = new(0),
            Padding = new(0),
            ItemTemplate = new FuncDataTemplate<Func<Control>>((factory, _) => factory?.Invoke(), supportsRecycling: false)
        };
        Results.Classes.Add("xg-catalog-list");
        Results.Resources["ListBoxItemPadding"] = new Thickness(0);
        Results.Resources["ListBoxBorderThemeThickness"] = new Thickness(0);
        ScrollViewer.SetHorizontalScrollBarVisibility(Results, ScrollBarVisibility.Disabled);
        Results.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
        Results.AddHandler(ScrollViewer.ScrollChangedEvent, (_, e) =>
        {
            if (e.Source is ScrollViewer viewer && e.OffsetDelta.Y > 0 &&
                viewer.Extent.Height - viewer.Viewport.Height - viewer.Offset.Y <= 260)
                NearEnd?.Invoke();
        });
        detail.Name = "CatalogDetailsFrame";
        var resultsArea = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        resultsArea.Children.Add(Results);
        SetRow(Feedback, 1);
        resultsArea.Children.Add(Feedback);
        columns.Children.Add(resultsArea);
        var detailGrid = new Grid { RowDefinitions = new("Auto,*") };
        detailGrid.Children.Add(close);
        detailScroll.Content = Details;
        SetRow(detailScroll, 1); detailGrid.Children.Add(detailScroll);
        detail.Child = detailGrid;
        detail.Background = ThemePalette.Panel;
        detail.BorderBrush = ThemePalette.Line;
        detail.BorderThickness = new(1);
        detail.CornerRadius = new(10);
        SetColumn(detail, 1); columns.Children.Add(detail);
        var toolbar = new StackPanel { Spacing = 6, Margin = new(0, 0, 0, 10) };
        toolbar.Children.Add(header);
        toolbar.Children.Add(filters);
        Children.Add(toolbar);
        SetRow(columns, 1); Children.Add(columns);
        SizeChanged += (_, _) => ApplyColumns();
    }

    public StackPanel Details { get; }

    public void SetDetails(bool isOpen)
    {
        if (detail.IsVisible == isOpen) return;
        detail.IsVisible = isOpen;
        ApplyColumns();
        if (isOpen && Motion.Enabled)
        {
            var slide = new TranslateTransform(26, 0)
            {
                Transitions = new Transitions
                {
                    new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(150), Easing = new CubicEaseOut() }
                }
            };
            detail.RenderTransform = slide;
            Dispatcher.UIThread.Post(() => slide.X = 0, DispatcherPriority.Background);
        }
        else detail.RenderTransform = null;
    }

    public void ResetDetailsOffset() => detailScroll.Offset = default;

    private void ApplyColumns()
    {
        var stacked = IsDetailOpen && Bounds.Width < 490;
        columns.ColumnDefinitions[1].Width = new GridLength(IsDetailOpen && !stacked ? Math.Clamp(Bounds.Width * 0.38, 218, 350) : 0);
        columns.ColumnSpacing = IsDetailOpen && !stacked ? 10 : 0;
        if (stacked != (columns.RowDefinitions.Count == 2))
            columns.RowDefinitions = new RowDefinitions(stacked ? "*,*" : "*");
        columns.RowSpacing = stacked ? 8 : 0;
        SetColumn(detail, stacked ? 0 : 1);
        SetRow(detail, stacked ? 1 : 0);
        DensityChanged?.Invoke();
    }

    private static ScrollViewer Scroll() => new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
}
