// SPDX-License-Identifier: MIT
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace XGLauncher;

internal sealed class LegalBrowser : ContentControl
{
    private readonly MainWindow owner;
    private double creditsOffset;
    internal ScrollViewer? CreditsScroll { get; private set; }
    internal TextBox? DocumentBody { get; private set; }
    internal TextBox? DocumentSearch { get; private set; }
    internal string Page { get; private set; } = "overview";
    internal LegalDocument? CurrentDocument { get; private set; }
    internal LegalBrowser(MainWindow owner) { ClipToBounds = true; this.owner = owner; Name = "LegalBrowser"; ShowOverview(); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e) {
        base.OnPropertyChanged(e);
        if (e.Property == ContentProperty && e.NewValue is Control view && this.IsAttachedToVisualTree())
            Dispatcher.UIThread.Post(() => { if (view.IsAttachedToVisualTree()) view.BringIntoView(new Rect(0, 0, Math.Max(1, view.Bounds.Width), 40)); }, DispatcherPriority.Loaded);
    }
    private static TextBlock Text(string text, double size = 13, bool bold = false) => new() {
        Text = text, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap };
    private static StackPanel Stack(params Control[] controls) {
        var panel = new StackPanel { Spacing = 10 }; foreach (var c in controls) panel.Children.Add(c); return panel;
    }
    private Button Button(string caption, Action run) {
        var b = new Button { Content = caption, CornerRadius = new(9), Padding = new(12, 8), Background = ThemePalette.Panel,
            BorderBrush = ThemePalette.Line, BorderThickness = new(1), Foreground = ThemePalette.Text };
        b.Classes.Add("xg-action"); b.Click += async (_, _) => { try { run(); } catch (Exception e) { await owner.ErrorAsync(this, new XG.Core.GameLogSanitizer(null).Redact(e.Message)); } }; return b;
    }
    private Control Heading(string title, Action back) {
        var grid = new Grid { ColumnDefinitions = new("100,*,100"), ColumnSpacing = 5 };
        grid.Children.Add(Button("返回", back));
        var label = Text(title, 19, true); label.TextAlignment = TextAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(label, 1); grid.Children.Add(label); return grid;
    }
    private Button Arrow(string icon, Action action) { var button=Button("",action); button.Content=new LucideIcon(icon,18); ToolTip.SetTip(button,icon=="chevron-left"?"向左滚动":"向右滚动"); Avalonia.Automation.AutomationProperties.SetName(button,icon=="chevron-left"?"向左滚动":"向右滚动"); return button; }
    private Control Card(string title, string description, Action open) {
        var b = Button("", open); b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var labels = Stack(Text(title, 16, true), Text(description));
        foreach (var t in labels.Children.OfType<TextBlock>()) t.TextAlignment = TextAlignment.Center;
        labels.Margin = new(24, 4);
        var grid = new Grid(); grid.Children.Add(labels);
        grid.Children.Add(new LucideIcon("chevron-right", 18) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 4, 0) });
        b.Content = grid; return b;
    }
    private string? expandedSection;
    private Control? expandedView;
    private bool changingSection;
    private async void ToggleSection(string section) {
        if (changingSection) return;
        changingSection = true;
        try {
            if (expandedView is { } previous && Motion.Enabled) {
                previous.Height = previous.Bounds.Height;
                previous.Transitions = [new Avalonia.Animation.DoubleTransition { Property = HeightProperty, Duration = TimeSpan.FromMilliseconds(200) }];
                previous.Height = 0;
                await Task.Delay(200);
            }
            expandedSection = expandedSection == section ? null : section;
            ShowOverview();
        } finally { changingSection = false; }
    }
    private Control OverviewTile(string label, string section) {
        var button = Button(label, () => ToggleSection(section));
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.MinHeight = 72;
        button.FontWeight = FontWeight.SemiBold;
        button.Background = ThemePalette.Panel;
        return button;
    }
    private Control OverviewExpanded() {
        var content = new StackPanel { Spacing = 9, Margin = new Thickness(10, 8) };
        if (expandedSection == "own") {
            content.Children.Add(Text("MIT License · MIT", 15, true));
            content.Children.Add(Text("XG Launcher 采用标准 MIT License；第三方内容保留各自授权。"));
            content.Children.Add(Button("查看内置说明", () => ShowDocument(LegalCatalog.File("XGLauncher-MIT.txt", "XG Launcher", "项目自身许可证状态"), ShowOverview)));
        } else if (expandedSection == "third") {
            content.Children.Add(Text("第三方许可证", 15, true));
            content.Children.Add(Text("选择项目，离线阅读实际收录的许可证及 NOTICE。"));
            var scroll = new ScrollViewer { MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var list = new StackPanel { Spacing = 5 };
            foreach (var d in LegalCatalog.ThirdParty)
                list.Children.Add(Button(d.Project + " · " + d.Id, () => ShowDocument(d, ShowOverview)));
            scroll.Content = list; content.Children.Add(scroll);
        } else {
            content.Children.Add(Text("免责声明", 15, true));
            content.Children.Add(Text("XG Launcher（瞎搞启动器）是独立开发的 Minecraft Java 版启动器，不是 Minecraft 官方产品，未获得 Mojang 或 Microsoft 的批准或认可。"));
            content.Children.Add(Text("游戏资源和商标归各自权利人所有；第三方鸣谢不代表赞助、认可或合作。"));
        }
        return new Border { BorderBrush = ThemePalette.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(4, 8), Child = content };
    }
    private Control CreditsStrip()
    {
        var items = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var project in LegalCatalog.Credits)
        {
            var tile = Button("", () => OpenProject(project)); tile.Width = 110; tile.MinHeight = 94;
            tile.BorderThickness = new(0); tile.Background = Brushes.Transparent;
            tile.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            Avalonia.Automation.AutomationProperties.SetName(tile, project.Name);
            var name = Text(project.Name, 12, true); name.TextAlignment = TextAlignment.Center; name.VerticalAlignment = VerticalAlignment.Center; name.MaxLines = 2; name.TextTrimming = TextTrimming.CharacterEllipsis;
            var icon = new Border { Width = 32, Height = 32, CornerRadius = new(6), Background = ThemePalette.Hover,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new LucideIcon(LucideIcon.Project(project.Name), 18) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            tile.Content = Stack(icon, new Border { Height = 34, Child = name }); ToolTip.SetTip(tile, project.Name + "\n" + project.Role); items.Children.Add(tile);
        }
        var scroll = new ScrollViewer { Name = "CreditsScroll", Content = items, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MinHeight = 113 };
        CreditsScroll = scroll; IdleScrollbar.HorizontalWheel(scroll);
        var previous = creditsOffset;
        scroll.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => scroll.Offset = new Vector(previous, 0), DispatcherPriority.Loaded);
        scroll.ScrollChanged += (_, _) => creditsOffset = scroll.Offset.X;
        return scroll;
    }
    private Control OverviewCredits()
    {
        var heading = Text("鸣谢项目与社区", 17, true); heading.TextAlignment = TextAlignment.Center;
        return new Border { BorderBrush = ThemePalette.Line, BorderThickness = new(1), CornerRadius = new(12), Padding = new(14),
            Background = ThemePalette.Panel, Child = Stack(heading, CreditsStrip()) };
    }
    internal void ShowOverview() {
        Page = "overview"; CurrentDocument = null;
        var layout = new StackPanel { Spacing = 17 };
        var title = Text("许可证与鸣谢", 21, true); title.TextAlignment = TextAlignment.Center;
        layout.Children.Add(title);
        var three = new Grid { ColumnDefinitions = new("*,*,*"), ColumnSpacing = 12 };
        var own = OverviewTile("自身许可证", "own"); three.Children.Add(own);
        var third = OverviewTile("第三方许可证", "third"); Grid.SetColumn(third, 1); three.Children.Add(third);
        var disclaimer = OverviewTile("免责声明", "disclaimer"); Grid.SetColumn(disclaimer, 2); three.Children.Add(disclaimer);
        layout.Children.Add(three);
        expandedView = null;
        if (expandedSection != null) {
            var region = OverviewExpanded(); region.ClipToBounds = true;
            expandedView = region; layout.Children.Add(region);
            region.AttachedToVisualTree += (_,_) => {
                if (!Motion.Enabled) return;
                region.Measure(new Size(Math.Max(200,Bounds.Width),double.PositiveInfinity));
                var height = region.DesiredSize.Height;
                region.Height = 0;
                region.Transitions = [new Avalonia.Animation.DoubleTransition { Property = HeightProperty, Duration = TimeSpan.FromMilliseconds(200) }];
                Dispatcher.UIThread.Post(() => region.Height = height,DispatcherPriority.Loaded);
            };
        }
        layout.Children.Add(OverviewCredits());
        var bottom = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 14 };
        
        // 官方许可文档继续使用既有链接验证和正文读取流程。
        var guide = Button("Minecraft 使用指南", () => ShowDocument(LegalCatalog.Guidelines, ShowOverview));
        guide.MinHeight = 74; guide.HorizontalAlignment = HorizontalAlignment.Stretch;
        guide.HorizontalContentAlignment = HorizontalAlignment.Center; guide.VerticalContentAlignment = VerticalAlignment.Center; guide.FontSize = 13; guide.FontWeight = FontWeight.SemiBold; guide.Content = new TextBlock { Text = "Minecraft 使用指南", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center }; bottom.Children.Add(guide);
        var eula = Button("Minecraft 最终用户许可协议", () => ShowDocument(LegalCatalog.Eula, ShowOverview));
        eula.MinHeight = 74; eula.HorizontalAlignment = HorizontalAlignment.Stretch;
        eula.HorizontalContentAlignment = HorizontalAlignment.Center; eula.VerticalContentAlignment = VerticalAlignment.Center; eula.FontSize = 13; eula.FontWeight = FontWeight.SemiBold; eula.Content = new TextBlock { Text = "Minecraft 最终用户许可协议", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center }; Grid.SetColumn(eula, 1); bottom.Children.Add(eula);
        layout.Children.Add(bottom);
        Content = layout;
    }
    internal void ShowThirdParty() {
        Page = "third-party";
        var list = Stack(Heading("第三方许可证", ShowOverview), Text("按原始文件阅读，不将未知授权状态标为已合规。"));
        foreach (var d in LegalCatalog.ThirdParty) list.Children.Add(Card(d.Project, d.Id, () => ShowDocument(d, ShowThirdParty)));
        Content = list;
    }
    internal void ShowCredits()
    {
        Page = "credits";
        Content = Stack(Heading("鸣谢项目与社区", ShowOverview), Text("点击项目即可阅读对应文件。"), CreditsStrip(),
            Text("Mojang、Microsoft、Modrinth 属于官方服务关系；加载器与 Java 按需下载。第三方依赖的原始许可单独列出。"));
    }
    internal void OpenProject(CreditProject project) {
        if (project.Documents.Count == 0) throw new InvalidOperationException("该项目的授权文件尚未取得，请查看未验证事项。");
        var returnToOverview = Page == "overview";
        var viewport = this.GetVisualAncestors().OfType<SettingsSurface>().FirstOrDefault()?.ContentScroll;
        var savedOffset = viewport?.Offset ?? default;
        void Return() {
            if (returnToOverview) ShowOverview(); else ShowCredits();
            Dispatcher.UIThread.Post(() => { if (viewport != null) viewport.Offset = savedOffset; },DispatcherPriority.Loaded);
        }
        ShowDocument(project.Documents[0], Return, project.Documents);
    }
    private void ShowDisclaimer() {
        Page = "disclaimer";
        Content = Stack(Heading("免责声明", ShowOverview),
            Text("XG Launcher（瞎搞启动器）是独立开发的 Minecraft Java 版启动器。它不是官方 Minecraft 产品，未获得 Mojang 或 Microsoft 批准，也不与两者关联。"),
            Text("游戏资源和商标归各自权利人所有。第三方鸣谢不代表获得官方认可、赞助或合作，在线服务须遵守相应条款。"));
    }
    internal void ShowDocument(LegalDocument document, Action back, IReadOnlyList<LegalDocument>? related = null) {
        Page = "document"; CurrentDocument = document;
        var body = document.Read();
        var content = Stack(Heading(document.Project, back), Text(document.Type, 15, true), Text(document.Role));
        if (related is { Count: > 1 }) {
            var choose = new UnifiedExpandableSelect { Title = "授权文件", ItemsSource = related, DisplayMember = x => ((LegalDocument)x).Id };
            choose.SelectedItem = document;
            choose.SelectionChanged += (_, _) => { if (choose.SelectedItem is LegalDocument next && next.Id != document.Id) ShowDocument(next, back, related); };
            content.Children.Add(choose);
        }
        if (body == null) {
            DocumentBody = null; DocumentSearch = null;
            content.Children.Add(Text("完整在线条款未内置。这里没有虚构的离线正文；请主动打开经过核实的官方入口阅读。"));
            content.Children.Add(Text(document.OfficialUrl ?? ""));
            content.Children.Add(Button("打开官方在线文档", () => Process.Start(new ProcessStartInfo(document.OfficialUrl!) { UseShellExecute = true })));
        }
        else {
            content.Children.Add(Text(document.Id + " · " + LegalCatalog.Length(document.Resource!) + " bytes", 11));
            content.Children.Add(Text("SHA-256 · " + LegalCatalog.Digest(document.Resource!), 10));
            var search = new TextBox { Watermark = "搜索正文", MinWidth = 90 };
            var feedback = Text("");
            var text = new TextBox { Text = body, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                SelectionBrush = ThemePalette.Hover, SelectionForegroundBrush = ThemePalette.Text,
                MinHeight = 170, Height = Math.Clamp(owner.ClientSize.Height - 280, 190, 450) };
            ScrollViewer.SetHorizontalScrollBarVisibility(text, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Auto);
            DocumentBody = text; DocumentSearch = search;
            void Find() {
                var query = search.Text ?? ""; if (query.Length == 0) { feedback.Text = "请输入搜索内容。"; return; }
                var index = body.IndexOf(query, Math.Min(text.SelectionEnd, body.Length), StringComparison.OrdinalIgnoreCase);
                if (index < 0) index = body.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (index < 0) { feedback.Text = "没有找到匹配内容。"; return; }
                text.CaretIndex = index + query.Length; text.SelectionStart = index; text.SelectionEnd = index + query.Length;
                feedback.Text = $"找到匹配内容 · {index + 1}/{body.Length}";
            }
            var find = Button("查找下一个", Find);
            search.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Find(); e.Handled = true; } };
            var copy = Button("复制全文", async () => {
                try { if (owner.Clipboard == null) throw new InvalidOperationException("剪贴板不可用。"); await owner.Clipboard.SetTextAsync(body); feedback.Text = "已复制全文。"; }
                catch (Exception error) { await owner.ErrorAsync(this, new XG.Core.GameLogSanitizer(null).Redact(error.Message)); }
            });
            var tools = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 6 };
            tools.Children.Add(search); Grid.SetColumn(find, 1); tools.Children.Add(find); Grid.SetColumn(copy, 2); tools.Children.Add(copy);
            content.Children.Add(tools); content.Children.Add(feedback); content.Children.Add(text);
        }
        Content = content;
    }
}
