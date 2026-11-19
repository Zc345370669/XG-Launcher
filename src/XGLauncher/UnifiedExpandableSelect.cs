// SPDX-License-Identifier: MIT
using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace XGLauncher;

/// <summary>
/// 复用同一外框和浮层呈现选择列表或内嵌表单，保持原布局槽及窗口高度。
/// </summary>
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
    public Func<object, Task>? RemoveItemAsync { get; set; }
    public bool PreferAbove { get; init; }
    public bool TwoLineTitle { get; init; }
    public string RemoveIcon { get; init; } = "minus";
    public string RemoveLabel { get; init; } = "删除账号";
    public string? FooterText { get; set; }
    public Func<Task>? FooterAction { get; set; }
    public Func<Task>? EmptyAction { get; set; }
    private string? auxiliaryIcon;
    public string? AuxiliaryIcon { get => auxiliaryIcon; init { auxiliaryIcon = value; if (value != null) auxiliary.Name = AuxiliaryName; } }
    public string AuxiliaryName { get; init; } = "AddAccount";
    public Func<Control>? ExpansionFactory { get; set; }
    public UnifiedExpandableSelect? ExpansionParent { get; set; }
    public event EventHandler? ExpansionChanged;
    private bool IsBelow(UnifiedExpandableSelect ancestor)
    { for (var p = ExpansionParent; p != null; p = p.ExpansionParent) if (ReferenceEquals(p, ancestor)) return true; return false; }
    private readonly Grid header;
    private readonly Button auxiliary;
    private DockPanel? joined;
    private Border? expansion;
    private bool auxiliaryOpen;
    internal bool IsAuxiliaryOpen => IsOpen && auxiliaryOpen;
    public Control? AvoidOverlapWith { get; set; }
    internal string ExpansionStrategy { get; private set; } = "closed";
    internal Rect? ExpansionBounds { get; private set; }
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
    private TopLevel? keyOwner;
    private Window? positionOwner;
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
        auxiliary = new Button { Width = 36, MinHeight = 34, Padding = new(0), BorderThickness = new(0), Background = Brushes.Transparent };
        auxiliary.Click += (_, e) => { e.Handled = true; OpenAuxiliary(); };
        header = new Grid { ColumnDefinitions = new("*,Auto") };
        header.Children.Add(anchor); Grid.SetColumn(auxiliary, 1); header.Children.Add(auxiliary);
        closedFrame = Frame(header);
        Content = closedFrame;
        anchor.Click += async (_, _) =>
        {
            if (IsOpen && !auxiliaryOpen) { Close(); return; }
            if (choices.Length == 0 && IsLoading) return;
            if (choices.Length == 0 && EmptyAction != null) { Close(false); await EmptyAction(); return; }
            if (IsOpen) Close(false);
            Open();
        };
        AddHandler(KeyDownEvent, (_, e) => { if (InlineExpansion && IsOpen) HandleKeys(this, e); }, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => { Subscribe(ItemsSource as INotifyCollectionChanged); UpdateTitle(); };
        DetachedFromVisualTree += (_, _) => { Close(false); Subscribe(null); };
        UpdateTitle();
    }

    private Button Header() => new()
    {
        Padding = new Thickness(10, 7), MinHeight = 34, Background = Brushes.Transparent,
        BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
    };

    private static Border Frame(Control child) => new()
    {
        Background = ThemePalette.Panel, BorderBrush = Outline, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8), ClipToBounds = true, UseLayoutRounding = true, Child = child
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
            UpdateList();
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
        var value = IsLoading && SelectedItem == null ? Localizer.Text("正在扫描已安装游戏…") : Display(SelectedItem);
        var title = string.IsNullOrWhiteSpace(Title) ? value : $"{Title}: {value}";
        anchor.Content = Caption(TwoLineTitle ? value : title);
        AutomationProperties.SetName(anchor, title);
        if (!IsOpen) HeaderCorners(null);
        auxiliary.IsVisible = AuxiliaryIcon != null;
        auxiliary.Content = AuxiliaryIcon == null ? null : new LucideIcon(AuxiliaryIcon, 18);
        AutomationProperties.SetName(auxiliary, "添加账号"); ToolTip.SetTip(auxiliary, "添加账号");
    }

    private Control Caption(string title)
    {
        var grid = new Grid { ColumnDefinitions = new("*,16"), ColumnSpacing = 8 };
        var value = new TextBlock { Text = title, Foreground = ThemePalette.Text, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(value, title);
        if (TwoLineTitle)
        {
            var lines = new StackPanel { Spacing = 3, Children = {
                new TextBlock { Text = Localizer.Text(Title), FontSize = 11, Foreground = ThemePalette.Muted }, value } };
            grid.Children.Add(lines);
        }
        else grid.Children.Add(value);
        var icon = new LucideIcon(IsOpen ? "chevron-up" : "chevron-down", 16) { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(icon, 1); grid.Children.Add(icon); return grid;
    }

    internal void OpenAuxiliary()
    {
        if (IsAuxiliaryOpen) { Close(); return; }
        if (IsOpen) Close(false);
        auxiliaryOpen = true; Open();
    }

    internal void Open()
    {
        var overlay = OverlayLayer.GetOverlayLayer(this);
        if (IsOpen || !IsEffectivelyEnabled || overlay == null) return;
        foreach (var other in TopLevel.GetTopLevel(this)!.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Where(x => !ReferenceEquals(x, this) && !IsBelow(x)).ToArray()) other.Close(false);
        Opening?.Invoke(this, EventArgs.Empty);
        layer = overlay;
        keyOwner = TopLevel.GetTopLevel(this);
        keyOwner?.AddHandler(KeyDownEvent, HandleKeys, RoutingStrategies.Tunnel);
        positionOwner = keyOwner as Window;
        if (positionOwner != null) { positionOwner.PositionChanged += WindowMoved; positionOwner.ScalingChanged += WindowScaled; }
        shield = new Canvas { Background = Brushes.Transparent };
        shield.PointerPressed += (_, e) =>
        {
            if (e.Source == shield) { e.Handled = true; Close(); }
        };
        shield.AddHandler(KeyDownEvent, HandleKeys, RoutingStrategies.Tunnel);
        openTitle = anchor;
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto") };

        if (!auxiliaryOpen && (Searchable || choices.Length > 14))
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
            Padding = new Thickness(0),
            ItemTemplate = new FuncDataTemplate<object>((item, _) =>
            {
                if (item == null) return null;
                var row = new Grid { ColumnDefinitions = new("*,32"), ColumnSpacing = 6 };
                var caption = new TextBlock { Text = Display(item), Foreground = ThemePalette.Text,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                ToolTip.SetTip(caption, Display(item)); row.Children.Add(caption);
                if (Equals(item, SelectedItem))
                {
                    var mark = new LucideIcon("check", 18) { Name = "SelectedCheck", Foreground = new SolidColorBrush(Color.Parse("#22A65A")),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(mark, 1); row.Children.Add(mark);
                }
                else if (RemoveItemAsync != null)
                {
                    var remove = new Button { Width = 32, Height = 32, Padding = new(0), CornerRadius = new(6),
                        Background = Brushes.Transparent, BorderThickness = new(0), Content = new LucideIcon(RemoveIcon, 17),
                        HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
                    remove.Classes.Add("xg-caption");
                    AutomationProperties.SetName(remove, RemoveLabel + " " + Display(item)); ToolTip.SetTip(remove, RemoveLabel);
                    remove.Click += async (_, e) => { e.Handled = true; Close(false); await RemoveItemAsync(item); };
                    Grid.SetColumn(remove, 1); row.Children.Add(remove);
                }
                return row;
            })
        };
        list.Resources["ListBoxItemPadding"] = new Thickness(10, 6);
        foreach (var variant in new[] { "", "Alt" })
            foreach (var shade in new[] { "Low", "Medium", "High" })
                list.Resources[$"SystemControlHighlight{variant}ListAccent{shade}Brush"] = ThemePalette.Hover;
        list.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is { } button && !ReferenceEquals(button, openTitle)) return;
            if (e.InitialPressMouseButton == MouseButton.Left && e.Source is Visual visual &&
                visual.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.Content is { } item)
            { e.Handled = true; Commit(item); }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        Grid.SetRow(list, 3);
        body.Children.Add(list);
        message = new TextBlock { Margin = new Thickness(10, 7), Foreground = ThemePalette.Text };
        Grid.SetRow(message, 3);
        body.Children.Add(message);
        Control expandedBody = body;
        if (auxiliaryOpen && ExpansionFactory != null)
        {
            var form = ExpansionFactory();
            expandedBody = new ScrollViewer { Content = form, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }
        else if (FooterText != null && FooterAction != null)
        {
            var add = new Button { Name = "ComboFooter", Background = Brushes.Transparent, BorderThickness = new(0),
                Padding = new(10, 9), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = {
                    new LucideIcon("plus", 17), new TextBlock { Text = Localizer.Text(FooterText), Foreground = ThemePalette.Text } } } };
            add.Click += async (_, e) => { e.Handled = true; Close(false); await FooterAction(); };
            var separator = new Border { BorderBrush = ThemePalette.Line, BorderThickness = new(0, 1, 0, 0), Child = add };
            Grid.SetRow(separator, 4); body.Children.Add(separator);
        }
        expansion = new Border { Child = expandedBody, BorderBrush = ThemePalette.Line };
        // 将实际主框移入同一浮层轮廓，原位置仅保留布局槽，不推动页面。
        closedFrame.Height = Math.Max(Bounds.Height, header.DesiredSize.Height);
        closedFrame.Child = null;
        joined = new DockPanel { LastChildFill = true };
        joined.Children.Add(header); joined.Children.Add(expansion);
        openFrame = Frame(joined);
        TextElement.SetFontFamily(openFrame, FontFamily); TextElement.SetForeground(openFrame, ThemePalette.Text);
        shield.Children.Add(openFrame); overlay.Children.Add(shield);
        closedFrame.Opacity = 0;
        overlay.LayoutUpdated += Position;
        UpdateTitle(); UpdateList(); Position(null, EventArgs.Empty);
        ExpansionChanged?.Invoke(this, EventArgs.Empty);
        if (auxiliaryOpen) expandedBody.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus();
        else if (search != null) search.Focus(); else list?.Focus();
    }

    private void Position(object? sender, EventArgs args)
    {
        if (layer == null || shield == null || openFrame == null || joined == null || expansion == null) return;
        if (TopLevel.GetTopLevel(this) is not { } window || this.TranslatePoint(default, layer) is not { } point) return;
        var available = window.ClientSize;
        if (available.Width <= 0 || available.Height <= 0) return;
        if (point.Y + Bounds.Height < 0 || point.Y >= available.Height) { Close(false); return; }
        shield.Width = available.Width; shield.Height = available.Height;
        double desired = Math.Max(46, choices.Length * 44 + (search == null ? 0 : 38) + (FilterLabels.Length > 0 ? 36 : 0) + (FooterText == null ? 0 : 42));
        if (auxiliaryOpen && expansion.Child is ScrollViewer { Content: Control form })
        {
            form.Measure(new Size(Math.Max(1, Bounds.Width - 2), double.PositiveInfinity));
            desired = Math.Max(46, form.DesiredSize.Height + 2);
        }
        var viewport = new Rect(available);
        if (window is Window desktop)
        {
            var origin = desktop.PointToScreen(default);
            var anchorPoint = desktop.PointToScreen(new Point(point.X + Bounds.Width / 2, point.Y + Bounds.Height / 2));
            var screen = desktop.Screens.ScreenFromPoint(anchorPoint) ?? desktop.Screens.ScreenFromWindow(desktop);
            if (screen != null)
            {
                var work = screen.WorkingArea; var scale = desktop.RenderScaling;
                viewport = viewport.Intersect(new Rect((work.X - origin.X) / scale, (work.Y - origin.Y) / scale, work.Width / scale, work.Height / scale));
            }
        }
        if (viewport.Width <= 0 || viewport.Height <= 0) { Close(false); return; }
        var plan = ComboPlacement.Calculate(new Rect(point, Bounds.Size), viewport, Math.Min(MaximumOpenHeight, desired), PreferAbove);
        ExpansionStrategy = plan.Up ? "joined-above" : "joined-below";
        ExpansionBounds = plan.Bounds;
        DockPanel.SetDock(header, plan.Up ? Dock.Bottom : Dock.Top);
        HeaderCorners(plan.Up);
        expansion.BorderThickness = plan.Up ? new(0, 0, 0, 1) : new(0, 1, 0, 0);
        openFrame.Width = plan.Bounds.Width; openFrame.Height = plan.Bounds.Height;
        Canvas.SetLeft(openFrame, plan.Bounds.Left); Canvas.SetTop(openFrame, plan.Bounds.Top);
    }

    private void HeaderCorners(bool? up)
    {
        var right = AuxiliaryIcon == null;
        anchor.CornerRadius = new(up == true ? 0 : 7, right && up != true ? 7 : 0, right && up != false ? 7 : 0, up == false ? 0 : 7);
        auxiliary.CornerRadius = new(0, up == true ? 0 : 7, up == false ? 0 : 7, 0);
        auxiliary.VerticalAlignment = VerticalAlignment.Stretch;
    }

    private void WindowMoved(object? sender, PixelPointEventArgs args) => Position(sender, EventArgs.Empty);
    private void WindowScaled(object? sender, EventArgs args) => Position(sender, args);

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
        if (keyOwner?.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Any(x => x.IsOpen && x.IsBelow(this)) == true) return;
        if (e.Key == Key.Escape) { e.Handled = true; Close(); return; }
        if (RemoveItemAsync != null && e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is { } button && !ReferenceEquals(button, openTitle)) return;
        if (auxiliaryOpen || list == null) return;
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
        foreach (var child in keyOwner?.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Where(x => x.IsOpen && x.IsBelow(this)).ToArray() ?? []) child.Close(false);
        ExpansionStrategy = "closed"; ExpansionBounds = null;
        if (shield == null) { auxiliaryOpen = false; return; }
        keyOwner?.RemoveHandler(KeyDownEvent, HandleKeys); keyOwner = null;
        if (positionOwner != null) { positionOwner.PositionChanged -= WindowMoved; positionOwner.ScalingChanged -= WindowScaled; positionOwner = null; }
        if (layer != null) { layer.LayoutUpdated -= Position; layer.Children.Remove(shield); }
        if (joined != null) joined.Children.Remove(header);
        closedFrame.Child = header; closedFrame.Height = double.NaN;
        joined = null; expansion = null; auxiliaryOpen = false;
        shield = null;
        layer = null;
        list = null;
        search = null;
        openFrame = null;
        openTitle = null;
        message = null;
        closedFrame.Opacity = 1;
        UpdateTitle();
        ExpansionChanged?.Invoke(this, EventArgs.Empty);
        if (restoreFocus) anchor.Focus();
    }
}
