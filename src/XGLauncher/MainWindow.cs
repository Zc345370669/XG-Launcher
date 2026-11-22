// SPDX-License-Identifier: MIT
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using XG.Core;

namespace XGLauncher;

public sealed partial class MainWindow : Window
{
    private static readonly IBrush Line = ThemePalette.Line;
    private static readonly IBrush Muted = ThemePalette.Muted;
    private static readonly string[] Nav = ["首页", "个人游玩", "整合包", "模组管理", "下载管理", "设置"];

    private readonly LauncherModel model;
    internal bool HasLoadedWorkspace => model != null;
    private readonly ContentControl page = new();
    private readonly StackPanel navigation = Column();
    private readonly TextBlock status = Label("");
    private readonly Button account;
    private int section;
    private int drawnNavigation = -1;
    private int modTab;
    private int packTab;
    private int downloadTab;
    private bool exitRequested;
    private bool minimizeToTray;
    private int maintenance;
    private TrayIcon? tray;

    public MainWindow(string? dataRoot = null) : this(dataRoot, null) { }

    internal MainWindow(string? dataRoot, Modrinth? catalog, HttpClient? gameMetadata = null)
    {
        Title = "瞎搞启动器";
        SystemDecorations = SystemDecorations.BorderOnly;
        Width = 1130; Height = 720;
        MinWidth = 520; MinHeight = 395;
        if (OperatingSystem.IsWindows() && Screens.Primary is { } primary)
        {
            var usableWidth = Math.Max(320, primary.WorkingArea.Width / primary.Scaling - 20);
            var usableHeight = Math.Max(265, primary.WorkingArea.Height / primary.Scaling - 20);
            MinWidth = Math.Min(MinWidth, usableWidth);
            MinHeight = Math.Min(MinHeight, usableHeight);
            Width = Math.Min(Width, usableWidth);
            Height = Math.Min(Height, usableHeight);
        }
        Background = ThemePalette.Background; Foreground = ThemePalette.Text;
        if (OperatingSystem.IsWindows()) FontFamily = new FontFamily("Microsoft YaHei UI");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var logoUri = new Uri("avares://XGLauncher/Assets/XG.png");
        using var stream = AssetLoader.Open(logoUri);
        var logo = new Image { Source = new Bitmap(stream), Width = 39, Height = 35 };
        using var icon = AssetLoader.Open(logoUri);
        Icon = new WindowIcon(icon);
        try { model = new LauncherModel(dataRoot, catalog, gameMetadata); }
        catch (Exception ex)
        {
            account = new Button();
            model = null!;
            Content = new Border { Padding = new(24), Child = Column(
                Label("无法打开启动器目录，请选择一个可写位置。\n" + new XG.Core.GameLogSanitizer(null).Redact(ex.Message)),
                Action("选择可写目录", async () =>
                {
                    var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                    {
                        Title = Localizer.Text("选择 XG Launcher 工作目录"), AllowMultiple = false
                    });
                    if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } chosen) return;
                    PortableStorage.Use(chosen);
                    var replacement = new MainWindow();
                    if (Application.Current?.ApplicationLifetime is
                        Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                        desktop.MainWindow = replacement;
                    replacement.Show();
                    Close();
                })) };
            return;
        }
        model.Game.ConfirmJavaInstallation = major => ConfirmAsync($"缺少 Java {major} x64。是否下载 Eclipse Temurin 官方安装器并安装到发行商默认目录？\nWindows 将请求管理员授权；不会修改 PATH 或 JAVA_HOME。");
        Localizer.Load(model.Store.Root);
        LoadAppearance();
        account = Action("账号", () => AccountsAsync());
        var top = CreateTitleBar(logo);
        var body = new Grid { ColumnDefinitions = new("154,*") };
        var menu = new ScrollViewer { Content = navigation, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        body.Children.Add(new Border { BorderBrush = Line, BorderThickness = new(0,0,1,0),
            Padding = new(8,12), Child = menu, Background = ThemePalette.Navigation });
        Grid.SetColumn(page, 1); body.Children.Add(page);
        body.SizeChanged += (_, args) =>
        {
            var width = new GridLength(args.NewSize.Width < 940 ? 124 : 154);
            if (body.ColumnDefinitions[0].Width != width) body.ColumnDefinitions[0].Width = width;
        };
        var layout = new Grid { RowDefinitions = new("Auto,*") };
        layout.Children.Add(top);
        Grid.SetRow(body, 1); layout.Children.Add(body);
        completionToast = new ContentControl { IsVisible = false, Margin = new(18), Padding = new(16, 10),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetRow(completionToast, 1); layout.Children.Add(completionToast);
        toastTimer.Tick += (_, _) => { completionToast.IsVisible = false; toastTimer.Stop(); };
        foreach (var previous in model.Resources.MainJobs.Where(x => x.State == "已完成")) notifiedInstallations.Add(previous.Id);
        var notifyPath = Path.Combine(model.Store.Root, "download-notifications.json");
        if (File.Exists(notifyPath)) notificationsEnabled = System.Text.Json.JsonSerializer.Deserialize<bool>(File.ReadAllText(notifyPath));
        InitializeModes(body, layout);
        Grid.SetRowSpan(modalShade, 2);
        layout.Children.Add(modalShade);
        Content = windowShell;
        ApplyAppearance(false);
        PropertyChanged += (_, e) => { if (e.Property.Name == "ActualTransparencyLevel") ApplyAppearance(false); };
        Closed += (_, _) =>
        {
            windowClosed = true;
            catalogLifetime.Cancel();
            catalogLifetime.Dispose();
            model.Dispose();
            model.Accounts.Changed -= AccountsChanged;
            model.Resources.ProgressChanged -= UpdateResourceProgress;
            model.Resources.Changed -= ResourceJobsChanged;
            model.Downloads.Changed -= ResourceJobsChanged;
            toastTimer.Stop();
        };
        model.Accounts.Changed += AccountsChanged;
        AccountsChanged();
        model.Resources.ProgressChanged += UpdateResourceProgress;
        model.Resources.Changed += ResourceJobsChanged;
        model.Downloads.Changed += ResourceJobsChanged;
        UpdateBadge();
        ConfigureTray();
        Opened += async (_, _) =>
        {
            // 导入已存在的MSAL账号元数据不等于登录成功，也不声明已取得有效游戏会话。
            try { await ScanLocalInstancesAsync(); await model.Accounts.ListAsync(catalogLifetime.Token); }
            catch (OperationCanceledException) when (windowClosed) { }
            catch (Exception ex)
            {
                if (!windowClosed) { status.Text = MicrosoftAccounts.Describe(ex); compactStatus.Text = status.Text; }
            }
        };
        Closing += WindowClosing;
        ConfigureUpdates();
        Refresh();
    }

    private void ConfigureTray()
    {
        if (!OperatingSystem.IsWindows()) return;
        var preference = Path.Combine(model.Store.Root, "tray.json");
        if (File.Exists(preference))
        {
            try { minimizeToTray = System.Text.Json.JsonSerializer.Deserialize<bool>(File.ReadAllText(preference)); }
            catch (System.Text.Json.JsonException) { minimizeToTray = false; }
        }
        tray = new TrayIcon { Icon = Icon, ToolTipText = "瞎搞启动器", IsVisible = minimizeToTray };
        void RestoreWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
        var show = new NativeMenuItem("打开瞎搞启动器");
        show.Click += (_, _) => RestoreWindow();
        var exit = new NativeMenuItem("退出");
        exit.Click += (_, _) => { RestoreWindow(); exitRequested = true; Close(); };
        tray.Menu = new NativeMenu();
        tray.Menu.Items.Add(show); tray.Menu.Items.Add(exit);
        tray.Clicked += (_, _) => RestoreWindow();
        Closed += (_, _) => tray.Dispose();
    }

    private void Refresh()
    {
        if (windowClosed) return;
        UpdateCompact();
        if (IsCompact) return;
        if (drawnNavigation != section)
        {
            drawnNavigation = section;
            navigation.Children.Clear();
            for (var i = 0; i < Nav.Length; i++)
            {
                var tab = i;
                var image = new LucideIcon(LucideIcon.Navigation[i], 17) { Foreground = i == section ? ThemePalette.OnAccent : ThemePalette.Text };
                var item = Action(Nav[i], () => { section = tab; Refresh(); return Task.CompletedTask; }, i == section);
                image.VerticalAlignment = VerticalAlignment.Center;
                var label = Label(Nav[i]);
                label.VerticalAlignment = VerticalAlignment.Center;
                label.TextWrapping = TextWrapping.NoWrap;
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                var content = Row(image, label);
                content.Spacing = 9;
                content.VerticalAlignment = VerticalAlignment.Center;
                if (i == 4)
                {
                    downloadBadge = Label("");
                    content.Children.Add(downloadBadge);
                    UpdateBadge();
                }
                item.Content = content;
                item.Classes.Add(i == section ? "xg-primary" : "xg-navigation");
                Avalonia.Automation.AutomationProperties.SetName(item, Nav[i]);
                item.Height = 40;
                item.Padding = new(10, 0);
                item.HorizontalAlignment = HorizontalAlignment.Stretch;
                ToolTip.SetTip(item, Localizer.Text(Nav[i]));
                void FitNavigationLabel()
                {
                    var badges = content.Children.OfType<TextBlock>().Where(x => !ReferenceEquals(x, label) && x.IsVisible)
                        .Sum(x => x.Bounds.Width + content.Spacing);
                    label.MaxWidth = Math.Max(12, item.Bounds.Width - item.Padding.Left - item.Padding.Right - 2 - image.Width - content.Spacing - badges);
                }
                item.SizeChanged += (_, _) => FitNavigationLabel();
                content.SizeChanged += (_, _) => FitNavigationLabel();
                navigation.Children.Add(item);
            }
        }
        var view = section switch
        {
            0 => Home(), 1 => Personal(), 2 => Packs(), 3 => Mods(), 4 => Queue(), _ => Settings()
        };
        page.Content = view is CatalogSurface or SettingsSurface or DownloadSurface ? view : PageViewport(view);
    }

    private static ScrollViewer PageViewport(Control content) => new()
    {
        Content = new Border { Child = content, Padding = new(18, 14) },
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
    };

    private Control Home()
    {
        var cards = new UniformRows();
        cards.SizeChanged += (_, args) => cards.Columns = args.NewSize.Width < 440 ? 2 : 3;
        cards.Children.Add(Metric("游戏实例", model.Store.Entries.Count));
        cards.Children.Add(Metric("已安装模组", model.Store.Entries.Sum(x => model.LocalFiles(x).Count(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)))));
        cards.Children.Add(Metric("下载任务", UnfinishedDownloads));
        var content = Column(cards, Label("最近游玩", 19, true));
        var recent = model.Store.Entries.Where(x => x.LastPlayed != null).OrderByDescending(x => x.LastPlayed).Take(6);
        foreach (var instance in recent) content.Children.Add(InstanceRow(instance));
        if (!recent.Any()) content.Children.Add(Label("暂无游玩记录"));
        return content;
    }

    private Control Personal()
    {
        if (status.Parent is Panel previous) previous.Children.Remove(status);
        status.Foreground = Muted;
        var content = Column(Header("个人游玩", Row(PersonalIcon("plus","创建实例",()=>EditAsync(null)),PersonalIcon("folder-input","导入游戏",GameImportWizardAsync))), status);
        foreach (var instance in model.Store.Entries.Where(x => !x.Pack)) content.Children.Add(InstanceRow(instance));
        if (content.Children.Count == 2) content.Children.Add(Label("暂无个人实例"));
        return content;
    }

    private Control InstanceRow(Instance instance)
    {
        var loader = string.IsNullOrWhiteSpace(instance.LoaderVersion) ? instance.Loader : $"{instance.Loader} {instance.LoaderVersion}";
        var lastPlayed = instance.LastPlayed is { } last ? last.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "未游玩";
        var details = Column(Label(instance.Name, 16, true, false), Label($"{instance.Version} · {loader}"),
            Label(InstanceState(instance) + $" · 上次游玩 {lastPlayed}"),
            Label(instance.ReferencedGameDirectory ?? model.Store.DirectoryFor(instance), 11));
        details.Spacing = 3;
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, ItemSpacing = 12,
            Children = { Action("编辑", () => EditAsync(instance)) } };
        actions.Children.Add(Action(InstanceButton(instance), () => InstancePrimaryAsync(instance), true));
        if (instance.Pack) actions.Children.Add(Action("同步到其他实例", () => SyncPackAsync(instance)));
        actions.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10 };
        grid.Children.Add(details);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        if (model.Selected?.Id == instance.Id) details.Children.Add(Label("当前安装目标"));
        var card = Frame(grid, new(12));
        card.Name = "InstanceCard_" + instance.Id.ToString("N");
        var instanceMenu = InstanceMenu(instance);
        card.ContextMenu = instanceMenu;
        Point contextPoint = new(20, 60);
        instanceMenu.PlacementTarget = this;
        instanceMenu.Placement = PlacementMode.Custom;
        instanceMenu.CustomPopupPlacementCallback = placement => {
            placement.AnchorRectangle = new Rect(
                Math.Clamp(contextPoint.X, 0, Math.Max(0, ClientSize.Width - placement.PopupSize.Width)),
                Math.Clamp(contextPoint.Y, 0, Math.Max(0, ClientSize.Height - placement.PopupSize.Height)), 0, 0);
            placement.Anchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft;
            placement.Gravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight;
        };
        instanceMenu.MaxHeight = Math.Max(160, ClientSize.Height - 16);
        // 在卡片默认菜单处理前检查原始路由来源，避免内部按钮误触发卡片操作。
        card.AddHandler(Control.ContextRequestedEvent, (_, e) => {
            if (e.Source is Avalonia.Visual visual && Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(visual)
                .Prepend(visual).OfType<Button>().Any()) { e.Handled = true; return; }
            if (e.TryGetPosition(this, out var point)) contextPoint = point;
            instanceMenu.MaxHeight = Math.Max(160, ClientSize.Height - 16);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        details.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(details).Properties.IsLeftButtonPressed) return;
            model.Selected = instance;
            Refresh();
        };
        return card;
    }

    private static Control ProfileView(MinecraftIdentity identity)
    {
        var avatar = new Image { Width = 22, Height = 22 };
        if (identity.Skin is { } skin)
        {
            avatar.DetachedFromVisualTree += (_, _) =>
            {
                if (avatar.Source is CroppedBitmap { Source: Bitmap bitmap }) bitmap.Dispose();
                avatar.Source = null;
            };
            _ = LoadAvatarAsync(avatar, skin);
        }
        var profile = Row(avatar, Label(identity.Name));
        ToolTip.SetTip(profile, identity.Id.ToString("D"));
        return profile;
    }

    private static readonly HttpClient AvatarClient = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(8) };

    private static async Task LoadAvatarAsync(Image image, Uri skin)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var response = await AvatarClient.GetAsync(skin, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 262144) return;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (bytes.Length + count > 262144) return;
                bytes.Write(buffer, 0, count);
            }
            if (TopLevel.GetTopLevel(image) is null || bytes.Length < 24) return;
            var png = bytes.GetBuffer();
            if (!png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
                System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)) != 64 ||
                System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)) is not (32 or 64)) return;
            bytes.Position = 0;
            var bitmap = new Bitmap(bytes);
            if (bitmap.PixelSize.Width == 64 && bitmap.PixelSize.Height is 32 or 64)
                image.Source = new CroppedBitmap(bitmap, new PixelRect(8, 8, 8, 8));
            else bitmap.Dispose();
        }
        catch (Exception) { /* A missing avatar must not interrupt a verified account. */ }
    }

    private static TextBlock Label(string text, double size = 13, bool bold = false, bool localize = true)
    {
        var block = new TextBlock { FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        if (!localize) LocalizedView.Preserve(block);
        block.Text = text; return block;
    }
    private static StackPanel Column(params Control[] children) => Panel(Orientation.Vertical, children);
    private static StackPanel Row(params Control[] children) => Panel(Orientation.Horizontal, children);
    private static StackPanel Panel(Orientation orientation, Control[] children)
    {
        var panel = new StackPanel { Orientation = orientation, Spacing = 10 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
    private static Border Frame(Control child, Thickness padding, Thickness? border = null) => new()
    {
        Child = child, Background = ThemePalette.Panel, Padding = padding, BorderBrush = Line, BorderThickness = border ?? new(1),
        CornerRadius = border == null ? new(10) : new(0)
    };
    private static Control Metric(string title, int count) => Frame(Column(Label(title), Label(count.ToString(), 29, true)), new(14));
    private static Control Header(string title, Control right)
    {
        var panel = new DockPanel();
        DockPanel.SetDock(right, Dock.Right); panel.Children.Add(right);
        panel.Children.Add(Label(title, 17, true));
        return panel;
    }
    private Button Action(string title, Func<Task> action, bool primary = false)
    {
        var button = new Button
        {
            Content = title, CornerRadius = new(9), Padding = new(13,8),
            Background = primary ? ThemePalette.Accent : ThemePalette.Panel,
            Foreground = primary ? ThemePalette.OnAccent : ThemePalette.Text, BorderBrush = Line, BorderThickness = new(1)
        };
        button.Classes.Add(primary ? "xg-primary" : "xg-action");
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            catch (Exception ex) { status.Text = new XG.Core.GameLogSanitizer(null).Redact(ex.Message); await ErrorAsync(button, new XG.Core.GameLogSanitizer(null).Redact(ex.Message)); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }
    internal Border GetModalShade() => modalShade;
    private readonly Border modalShade = LauncherDialog.CreateShade();

    internal async Task ErrorAsync(Control origin, string message)
    {
        message = new GameLogSanitizer(null).Redact(message);
        if (TopLevel.GetTopLevel(origin) is not Window owner || !owner.IsVisible) return;
        var window = Dialog("操作失败", owner);
        window.SetBody(Label(message), CloseDialog(window, "关闭"));
        await window.ShowDialog(owner);
    }
    private static TextBox Input(string hint, string? value = null) => new() { Watermark = hint, Text = value, MinWidth = 80 };
    private static UnifiedExpandableSelect Picker(string[] values, int index = 0, string title = "", bool searchable = false) =>
        new() { ItemsSource = values, SelectedIndex = index, Title = title, Searchable = searchable };
    private LauncherDialog Dialog(string title, Window? owner = null, bool editor = false) =>
        new(title, owner ?? this, editor);

    private Button CloseDialog(LauncherDialog dialog, string text = "取消") =>
        Action(text, () => { dialog.Close(); return Task.CompletedTask; });

    private sealed class UniformRows : Avalonia.Controls.Primitives.UniformGrid
    {
        public UniformRows() { Columns = 3; ColumnSpacing = 10; }
    }
}
