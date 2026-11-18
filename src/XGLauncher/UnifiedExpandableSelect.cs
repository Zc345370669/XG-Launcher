// SPDX-License-Identifier: MIT
using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace XGLauncher;

public sealed class UnifiedExpandableSelect : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<UnifiedExpandableSelect, IEnumerable?>(nameof(ItemsSource));
    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<UnifiedExpandableSelect, object?>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<object?> SelectedValueProperty =
        AvaloniaProperty.Register<UnifiedExpandableSelect, object?>(nameof(SelectedValue), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<UnifiedExpandableSelect, string>(nameof(Title), "");
    public static readonly StyledProperty<string> PlaceholderTextProperty =
        AvaloniaProperty.Register<UnifiedExpandableSelect, string>(nameof(PlaceholderText), "请选择");
    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<UnifiedExpandableSelect, bool>(nameof(IsLoading));
    public static readonly StyledProperty<double> MaximumOpenHeightProperty =
        AvaloniaProperty.Register<UnifiedExpandableSelect, double>(nameof(MaximumOpenHeight), 320);

    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public object? SelectedValue { get => GetValue(SelectedValueProperty); set => SetValue(SelectedValueProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string PlaceholderText { get => GetValue(PlaceholderTextProperty); set => SetValue(PlaceholderTextProperty, value); }
    public bool IsLoading { get => GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }
    public double MaximumOpenHeight { get => GetValue(MaximumOpenHeightProperty); set => SetValue(MaximumOpenHeightProperty, value); }
    public Func<object, string>? DisplayMember { get; init; }
    public Func<object, object?>? ValueMember { get; init; }
    public bool Searchable { get; init; }
    public bool InlineExpansion { get; init; }
    public string[] FilterLabels { get; init; } = [];
    public Func<int, object, bool>? GroupFilter { get; init; }
    private int group;
    public event EventHandler? SelectionChanged;
    public event EventHandler? Opening;

    private static readonly IBrush Outline = ThemePalette.Line;
    private readonly Button anchor;
    private readonly Border closedFrame;
    private object[] choices = [];
    private INotifyCollectionChanged? notifying;
    private ListBox? list;
    private TextBox? search;
    private TextBlock? message;
    private Button? openTitle;
    private Canvas? shield;
    private Border? openFrame;
    private OverlayLayer? layer;
    private bool synchronizing;

    public int SelectedIndex
    {
        get => SelectedItem is { } selected ? Array.IndexOf(choices, selected) : -1;
        set => SetCurrentValue(SelectedItemProperty, value >= 0 && value < choices.Length ? choices[value] : null);
    }

    public bool IsOpen => shield != null;

    public UnifiedExpandableSelect()
    {
        MinWidth = 100;
        anchor = Header();
        closedFrame = Frame(anchor);
        Content = closedFrame;
        anchor.Click += (_, _) => Open();
        AddHandler(KeyDownEvent, (_, e) => { if (InlineExpansion && IsOpen) HandleKeys(this, e); }, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => Subscribe(ItemsSource as INotifyCollectionChanged);
        DetachedFromVisualTree += (_, _) => { Close(false); Subscribe(null); };
        UpdateTitle();
    }

    private Button Header() => new()
    {
        Padding = new Thickness(10, 7), MinHeight = 34, Background = Brushes.Transparent,
        BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Left
    };

    private static Border Frame(Control child) => new()
    {
        Background = ThemePalette.Panel, BorderBrush = Outline, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = child
    };

    private string Display(object? item) => item == null ? PlaceholderText : Localizer.Text(DisplayMember?.Invoke(item) ?? item.ToString() ?? "");
    private object? Value(object? item) => item == null || ValueMember == null ? item : ValueMember(item);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (anchor == null) return;
        if (change.Property == ItemsSourceProperty)
        {
            if (this.IsAttachedToVisualTree()) Subscribe(ItemsSource as INotifyCollectionChanged);
            RebuildItems();
        }
        else if (change.Property == SelectedItemProperty)
        {
            if (!synchronizing)
            {
                synchronizing = true;
                try { SetCurrentValue(SelectedValueProperty, Value(SelectedItem)); }
                finally { synchronizing = false; }
            }
            UpdateTitle();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (change.Property == SelectedValueProperty && !synchronizing)
        {
            synchronizing = true;
            try { SetCurrentValue(SelectedItemProperty, choices.FirstOrDefault(x => Equals(Value(x), SelectedValue))); }
            finally { synchronizing = false; }
        }
        else if (change.Property == IsEffectivelyEnabledProperty && !IsEffectivelyEnabled) Close(false);
        else if (change.Property == TitleProperty || change.Property == PlaceholderTextProperty || change.Property == IsLoadingProperty)
        {
            UpdateTitle();
            UpdateList();
        }
    }

    private void Subscribe(INotifyCollectionChanged? source)
    {
        if (ReferenceEquals(notifying, source)) return;
        if (notifying != null) notifying.CollectionChanged -= ItemsChanged;
        notifying = source;
        if (notifying != null) notifying.CollectionChanged += ItemsChanged;
    }

    private void ItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildItems();

    private void RebuildItems()
    {
        choices = ItemsSource?.Cast<object>().Where(x => x != null).ToArray() ?? [];
        if (SelectedItem is { } selected && !choices.Contains(selected))
            SetCurrentValue(SelectedItemProperty, null);
        else if (SelectedItem == null && SelectedValue != null)
            SetCurrentValue(SelectedItemProperty, choices.FirstOrDefault(x => Equals(Value(x), SelectedValue)));
        UpdateList();
    }

    private void UpdateTitle()
    {
        var value = Display(SelectedItem);
        var title = string.IsNullOrWhiteSpace(Title) ? value : $"{Title}: {value}";
        anchor.Content = Caption(title);
        AutomationProperties.SetName(anchor, title);
        if (openTitle != null) openTitle.Content = Caption(title);
    }

    private Control Caption(string title) {
        var grid=new Grid { ColumnDefinitions=new("*,14"), ColumnSpacing=6 };
        grid.Children.Add(new TextBlock { Text=title, TextTrimming=TextTrimming.CharacterEllipsis });
        var icon=new LucideIcon(IsOpen?"chevron-up":"chevron-down",14) { VerticalAlignment=VerticalAlignment.Center };Grid.SetColumn(icon,1);grid.Children.Add(icon);return grid;
    }

    internal void Open()
    {
        var overlay = OverlayLayer.GetOverlayLayer(this);
        if (IsOpen || !IsEffectivelyEnabled || !InlineExpansion && overlay == null) return;
        Opening?.Invoke(this, EventArgs.Empty);
        layer = overlay;
        shield = new Canvas { Background = Brushes.Transparent };
        shield.PointerPressed += (_, e) =>
        {
            if (e.Source == shield) { e.Handled = true; Close(); }
        };
        shield.AddHandler(KeyDownEvent, HandleKeys, RoutingStrategies.Tunnel);
        openTitle = Header();
        openTitle.Click += (_, _) => Close();
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*") };
        body.Children.Add(openTitle);
        if (Searchable || choices.Length > 14)
        {
            search = new TextBox { Watermark = "搜索选项", Margin = new Thickness(7, 2, 7, 4), MinHeight = 29 };
            search.TextChanged += (_, _) => UpdateList();
            Grid.SetRow(search, 1);
            body.Children.Add(search);
        }
        if (FilterLabels.Length > 0)
        {
            var groups = new WrapPanel { ItemSpacing = 4, Margin = new Thickness(7, 0, 7, 5) };
            Grid.SetRow(groups, 2);
            body.Children.Add(groups);
            var buttons = new List<Button>();
            for (var i = 0; i < FilterLabels.Length; i++)
            {
                var index = i;
                var toggle = new Button { Content = FilterLabels[i], Padding = new Thickness(6, 4), FontSize = 12 };
                buttons.Add(toggle);
                toggle.Click += (_, _) => { group = index; PaintGroups(); UpdateList(); };
                groups.Children.Add(toggle);
            }
            void PaintGroups()
            {
                for (var i = 0; i < buttons.Count; i++)
                {
                    buttons[i].Background = i == group ? ThemePalette.Accent : ThemePalette.Panel;
                    buttons[i].Foreground = i == group ? ThemePalette.OnAccent : ThemePalette.Text;
                }
            }
            PaintGroups();
        }
        list = new ListBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(3, 0, 3, 4),
            ItemTemplate = new FuncDataTemplate<object>((item, _) =>
            {
                if (item == null) return null;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("17,*") };
                row.Children.Add(new LucideIcon("check", 14) { IsVisible = Equals(item, SelectedItem) });
                var caption = new TextBlock { Text = Display(item), TextTrimming = TextTrimming.CharacterEllipsis };
                ToolTip.SetTip(caption, Display(item));
                Grid.SetColumn(caption, 1);
                row.Children.Add(caption);
                return row;
            })
        };
        list.Resources["ListBoxItemPadding"] = new Thickness(7, 6);
        foreach (var variant in new[] { "", "Alt" })
            foreach (var shade in new[] { "Low", "Medium", "High" })
                list.Resources[$"SystemControlHighlight{variant}ListAccent{shade}Brush"] = ThemePalette.Hover;
        list.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && e.Source is Visual visual &&
                visual.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.Content is { } item)
            { e.Handled = true; Commit(item); }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        Grid.SetRow(list, 3);
        body.Children.Add(list);
        message = new TextBlock { Margin = new Thickness(10, 7) };
        Grid.SetRow(message, 3);
        body.Children.Add(message);
        openFrame = Frame(body);
        if (InlineExpansion) {
            openFrame.MaxHeight = MaximumOpenHeight;
            Content = openFrame; UpdateTitle(); UpdateList(); openTitle?.Focus(); return;
        }
        shield.Children.Add(openFrame);
        overlay!.Children.Add(shield);
        closedFrame.Opacity = 0;
        overlay.LayoutUpdated += Position;
        UpdateTitle();
        UpdateList();
        Position(null, EventArgs.Empty);
        if (search != null) search.Focus();
        else openTitle?.Focus();
    }

    private void Position(object? sender, EventArgs args)
    {
        if (layer == null || shield == null || openFrame == null) return;
        if (TopLevel.GetTopLevel(this) is not { } window || this.TranslatePoint(default, layer) is not { } point) return;
        var available = window.ClientSize;
        if (available.Width <= 0 || available.Height <= 0) return;
        if (point.Y + Bounds.Height < 0 || point.Y >= available.Height) { Close(false); return; }
        shield.Width = available.Width;
        shield.Height = available.Height;
        var width = Math.Min(available.Width, Bounds.Width);
        var left = Math.Clamp(point.X, 0, Math.Max(0, available.Width - width));
        var minimum = Math.Min(available.Height, search == null ? 130 : FilterLabels.Length == 0 ? 165 : 220);
        var top = Math.Clamp(point.Y, 0, Math.Max(0, available.Height - minimum));
        openFrame.Width = width;
        openFrame.MaxHeight = Math.Max(0, Math.Min(MaximumOpenHeight, available.Height - top));
        Canvas.SetLeft(openFrame, left);
        Canvas.SetTop(openFrame, top);
    }

    private void UpdateList()
    {
        if (list == null || message == null) return;
        var text = search?.Text?.Trim() ?? "";
        var results = choices.Where(item => (GroupFilter?.Invoke(group, item) ?? true) && Display(item).Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
        list.ItemsSource = results;
        list.SelectedIndex = Math.Max(0, Array.IndexOf(results, SelectedItem));
        message.Text = IsLoading ? "正在加载…" : "无可选项";
        message.IsVisible = results.Length == 0;
        list.IsVisible = results.Length > 0;
    }

    private void HandleKeys(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); return; }
        if (list == null) return;
        if (e.Key == Key.Enter && list.SelectedItem is { } selected)
        { e.Handled = true; Commit(selected); }
        else if (e.Key is Key.Up or Key.Down && list.ItemCount > 0)
        {
            list.SelectedIndex = Math.Clamp(list.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, list.ItemCount - 1);
            list.ScrollIntoView(list.SelectedIndex);
            e.Handled = true;
        }
    }

    private void Commit(object item)
    {
        Close();
        SetCurrentValue(SelectedItemProperty, item);
    }

    internal void Close(bool restoreFocus = true)
    {
        if (shield == null) return;
        if (layer != null) { layer.LayoutUpdated -= Position; layer.Children.Remove(shield); }
        if (InlineExpansion) Content = closedFrame;
        shield = null;
        layer = null;
        list = null;
        search = null;
        openFrame = null;
        openTitle = null;
        message = null;
        closedFrame.Opacity = 1;
        if (restoreFocus) anchor.Focus();
    }
}
