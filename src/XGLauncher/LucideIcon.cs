// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace XGLauncher;

// 仅绘制固定来源SVG的基础图形；不联网加载，也不引入外部SVG执行引擎。
internal sealed class LucideIcon : Control
{
    internal static readonly string[] Navigation = ["house", "gamepad-2", "layers", "package", "download", "settings"];
    private static readonly Dictionary<string, IReadOnlyList<Geometry>> Cache = new(StringComparer.Ordinal);
    public static readonly StyledProperty<string> IconProperty = AvaloniaProperty.Register<LucideIcon, string>(nameof(Icon), "circle");
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<LucideIcon, IBrush?>(nameof(Foreground));
    public string Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    private TemplatedControl? owner;
    static LucideIcon() { AffectsRender<LucideIcon>(IconProperty, ForegroundProperty); }
    internal LucideIcon(string name = "circle", double size = 18) { Icon = name; Width = Height = size; IsHitTestVisible = false; }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); owner = this.FindAncestorOfType<Button>() ?? (TemplatedControl?)this.FindAncestorOfType<MenuItem>();
        if (owner is not null) owner.PropertyChanged += Changed;
        ThemePalette.Text.PropertyChanged += Changed; ThemePalette.OnAccent.PropertyChanged += Changed;
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (owner is not null) owner.PropertyChanged -= Changed;
        ThemePalette.Text.PropertyChanged -= Changed; ThemePalette.OnAccent.PropertyChanged -= Changed;
        owner = null; base.OnDetachedFromVisualTree(e);
    }
    private void Changed(object? sender, AvaloniaPropertyChangedEventArgs e) => InvalidateVisual();
    internal IBrush EffectiveBrush => Foreground ?? owner?.Foreground ?? ThemePalette.Text;
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Math.Max(0, Math.Min(Bounds.Width, Bounds.Height) - 2);
        if (size <= 0) return;
        var brush = EffectiveBrush;
        using var transform = context.PushTransform(Matrix.CreateScale(size / 24, size / 24) *
            Matrix.CreateTranslation((Bounds.Width - size) / 2, (Bounds.Height - size) / 2));
        var pen = new Pen(brush, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        foreach (var geometry in Shapes(Icon)) context.DrawGeometry(null, pen, geometry);
    }
    internal static IReadOnlyList<Geometry> Shapes(string name)
    {
        // 固定上游元数据将trash-2标记为trash的旧别名，兼容现有资源键。
        if (name == "trash-2") name = "trash";
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var existing)) return existing;
            if (!name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) throw new ArgumentException("Unknown icon name.", nameof(name));
            using var stream = typeof(LucideIcon).Assembly.GetManifestResourceStream("XG.Lucide." + name + ".svg")
                ?? throw new FileNotFoundException("Missing pinned Lucide resource.", name);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 });
            var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("Invalid Lucide SVG.");
            if ((string?)root.Attribute("viewBox") != "0 0 24 24") throw new InvalidDataException("Unexpected icon viewbox.");
            var result = root.Elements().Select(Parse).ToArray();
            if (result.Length == 0) throw new InvalidDataException("Empty Lucide SVG.");
            Cache[name] = result; return result;
        }
    }
    private static Geometry Parse(XElement shape)
    {
        double N(string key, double fallback = 0) => double.TryParse((string?)shape.Attribute(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        string F(FormattableString s) => FormattableString.Invariant(s);
        return shape.Name.LocalName switch
        {
            "path" => Geometry.Parse((string?)shape.Attribute("d") ?? throw new InvalidDataException("SVG path missing data.")),
            "line" => Geometry.Parse(F($"M{N("x1")},{N("y1")} L{N("x2")},{N("y2")}")),
            "circle" => new EllipseGeometry(new Rect(N("cx") - N("r"), N("cy") - N("r"), N("r") * 2, N("r") * 2)),
            "ellipse" => new EllipseGeometry(new Rect(N("cx") - N("rx"), N("cy") - N("ry"), N("rx") * 2, N("ry") * 2)),
            "rect" => new RectangleGeometry(new Rect(N("x"), N("y"), N("width"), N("height")), N("rx"), N("ry", N("rx"))),
            "polyline" or "polygon" => Geometry.Parse("M" + (string?)shape.Attribute("points") + (shape.Name.LocalName == "polygon" ? " Z" : "")),
            _ => throw new InvalidDataException("Unsupported official SVG primitive: " + shape.Name.LocalName)
        };
    }
    internal static string Project(string name) => name switch
    {
        "PCL" => "panels-top-left", "Mojang Studios" => "building", "Microsoft" => "building", "Modrinth" => "package", "NeoForge" => "anvil", "Forge" => "wrench", "Lucide" => "shapes",
        "Fabric" or "Quilt" => "layers", ".NET" => "code-xml", "Avalonia UI" => "panels-top-left", "OpenJDK" => "coffee", _ => "library"
    };
    internal static string Operation(string title) => title switch
    {
        "启动游戏" => "play", "编辑实例" => "pencil", "复制实例" => "copy", "删除实例" => "trash",
        "备份实例" or "创建备份" => "archive", "恢复备份" => "folder-archive", "查看游戏日志" or "查看加载器日志" or "查看日志" => "file-text",
        "设为安装目标" => "download",
        _ => "folder-open"
    };
}
