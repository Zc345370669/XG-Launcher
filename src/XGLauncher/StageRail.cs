// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using XG.Core;

namespace XGLauncher;

/// <summary>
/// 将真实安装阶段呈现为无连接线的节点与名称；不绘制第二条百分比进度条。
/// </summary>
internal sealed class StageRail : Canvas
{
    private const double NodeSize = 18;
    private const double RasterInset = 2;
    private const double CaptionGap = 6;
    private const double LabelWidth = 96;
    private const double NodeStep = LabelWidth + 8;
    private readonly ScrollViewer viewport = new() { Name = "StageNodeViewport", HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = true };
    private readonly Canvas axis = new() { Height = NodeSize + RasterInset * 3, UseLayoutRounding = false };
    private readonly Canvas labels = new() { Margin = new(0, CaptionGap, 0, 0), UseLayoutRounding = false };
    private readonly Grid layout = new() { RowDefinitions = new("Auto,*"), UseLayoutRounding = false };
    // 保留不可见轴供统一定位；总百分比只在卡片上方显示，下方不能再绘制连接线或填充条。
    private readonly Border track = new() { Name = "StageTrack", Height = 1, CornerRadius = new(.5),
        Background = Brushes.Transparent, Opacity = 0, IsHitTestVisible = false, UseLayoutRounding = false };
    private readonly LucideIcon moving = new("circle", NodeSize) { Name = "StageCurrentIcon", Foreground = ThemePalette.Accent, UseLayoutRounding = false };
    private readonly Border movingNode;
    private readonly List<(Border Node, LucideIcon Icon, TextBlock Label)> checkpoints = [];
    private readonly TextBlock caption = new() { Name = "StageCaption", FontSize = 13,
        TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap,
        TextAlignment = TextAlignment.Center, Foreground = ThemePalette.Text, UseLayoutRounding = false };
    private readonly Transitions motion = [new DoubleTransition { Property = DisplayPositionProperty,
        Duration = TimeSpan.FromMilliseconds(180), Easing = new CubicEaseOut() }];
    private IReadOnlyList<StageProgress> stages = [];
    private string state = "";
    private double targetPosition;
    private bool hasPosition;
    private int activeIndex = -1;
    private int layoutSlots = 1;
    private double percent;
    internal double Percent => percent;
    private static readonly StyledProperty<double> DisplayPositionProperty =
        AvaloniaProperty.Register<StageRail, double>("DisplayPosition");

    internal StageRail()
    {
        Height = 52; ClipToBounds = true; UseLayoutRounding = false;
        movingNode = Badge(moving); movingNode.Name = "CurrentStageNode"; movingNode.ZIndex = 1;
        axis.Children.Add(track); axis.Children.Add(movingNode);
        labels.Children.Add(caption);
        layout.Children.Add(axis); Grid.SetRow(labels, 1); layout.Children.Add(labels); viewport.Content = layout; Children.Add(viewport);
        viewport.PointerWheelChanged += (_, e) =>
        { if (viewport.Extent.Width > viewport.Viewport.Width) { viewport.Offset = new(Math.Clamp(viewport.Offset.X - (e.Delta.X != 0 ? e.Delta.X : e.Delta.Y) * 40, 0, viewport.Extent.Width - viewport.Viewport.Width), 0); e.Handled = true; } };
        viewport.PointerPressed += (_, _) => viewport.Focus();
        viewport.KeyDown += (_, e) =>
        { if (e.Key is Key.Left or Key.Right) { viewport.Offset = new(Math.Clamp(viewport.Offset.X + (e.Key == Key.Left ? -NodeStep : NodeStep), 0, Math.Max(0, viewport.Extent.Width - viewport.Viewport.Width)), 0); e.Handled = true; } };
        SizeChanged += (_, _) => { viewport.Width = Bounds.Width; viewport.Height = Bounds.Height; layout.Height = Bounds.Height; UpdateGeometry(); };
        axis.SizeChanged += (_, _) => UpdateGeometry();
        caption.PropertyChanged += (_, e) => { if (e.Property == TextBlock.TextProperty) UpdateGeometry(); };
    }

    private static Border Badge(LucideIcon icon) => new()
    {
        Width = NodeSize, Height = NodeSize, CornerRadius = new(NodeSize / 2),
        Background = ThemePalette.Panel, Child = icon, UseLayoutRounding = false
    };

    /// <summary>
    /// 使用真实阶段记录更新完成节点、活动节点和UI名称，不改变任务百分比。
    /// </summary>
    internal void Update(IReadOnlyList<StageProgress> values, string? current, string jobState, double? totalPercent = null, int? plannedSlots = null)
    {
        stages = values.Where(x => x.Required).ToArray(); state = jobState;
        // 按真实计划预留配对间距，不绘制未开始的绿色节点；窄窗口仅在内部横向滚动。
        layoutSlots = Math.Max(stages.Count, Math.Max(1, plannedSlots ?? values.Count));
        percent = totalPercent ?? (jobState == "已完成" ? 100 : Math.Min(99,
            stages.Sum(x => (x.End - x.Start) * Math.Clamp(x.Fraction, 0, 1))));
        while (checkpoints.Count > stages.Count)
        { axis.Children.Remove(checkpoints[^1].Node); labels.Children.Remove(checkpoints[^1].Label); checkpoints.RemoveAt(checkpoints.Count - 1); }
        while (checkpoints.Count < stages.Count)
        {
            var icon = new LucideIcon("circle-check", NodeSize) { Foreground = Brushes.ForestGreen, UseLayoutRounding = false };
            var node = Badge(icon); node.Name = "CompletedStageNode"; axis.Children.Add(node);
            var doneLabel = new TextBlock { Name = "CompletedStageCaption", FontSize = 13, Width = LabelWidth, TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, Foreground = ThemePalette.Text, UseLayoutRounding = false };
            labels.Children.Add(doneLabel); checkpoints.Add((node, icon, doneLabel));
        }
        for (var i = 0; i < checkpoints.Count; i++)
        {
            var complete = stages[i].State == "已完成";
            checkpoints[i].Node.IsVisible = checkpoints[i].Icon.IsVisible = checkpoints[i].Label.IsVisible = complete;
            checkpoints[i].Label.Text = StageName(stages[i]);
            ToolTip.SetTip(checkpoints[i].Label, FullName(stages[i]));
            ToolTip.SetTip(checkpoints[i].Node, stages[i].Kind + " · 已完成");
        }
        var active = stages.FirstOrDefault(x => x.Kind == current && x.State != "已完成") ?? stages.FirstOrDefault(x => x.State == "校验中");
        activeIndex = active == null ? -1 : Array.FindIndex(stages.ToArray(), s => ReferenceEquals(s, active));
        movingNode.IsVisible = moving.IsVisible = active != null && (active.State is not ("等待中" or "等待恢复") || active.Fraction > 0) && state is not ("已完成" or "已取消");
        moving.Icon = state == "失败" ? "circle-exclamation-point" : "circle";
        targetPosition = active != null ? Clamp(active.Start + (active.End - active.Start) * Fraction(active.Fraction)) :
            state == "已完成" ? 100 : hasPosition ? targetPosition : 0;
        // 暂停、失败或取消固定到最后真实报告位置，继续仅对后续报告恢复过渡。
        var animate = Motion.Enabled && hasPosition && movingNode.IsVisible && state is not ("已暂停" or "等待恢复" or "失败") && this.IsAttachedToVisualTree();
        Transitions = animate ? motion : null;
        SetValue(DisplayPositionProperty, targetPosition);
        hasPosition = true;
        var label = state switch { "已完成" => "已完成", "已取消" => "已取消", _ => active == null ? "" : StageName(active) };
        caption.Text = label;
        caption.IsVisible = movingNode.IsVisible && !string.IsNullOrWhiteSpace(caption.Text);
        ToolTip.SetTip(caption, active == null ? label : FullName(active));
        ToolTip.SetTip(movingNode, active == null ? state : active.Kind + " · " + state);
        UpdateGeometry();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DisplayPositionProperty && axis != null && movingNode != null) PlaceActive();
    }
    private static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
    private static double Fraction(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    // 节点和名称统一使用DIP坐标；共享中心线并避免分别像素舍入，降低非整数DPI下的错位。
    private (double Inset, double Span, double Y) Coordinates()
    {
        var inset = LabelWidth / 2 + RasterInset;
        return (inset, Math.Max(0, Bounds.Width - 2 * inset - (layoutSlots - 1) * NodeStep), axis.Bounds.Height / 2);
    }
    private void UpdateGeometry()
    {
        if (axis == null || movingNode == null) return;
        var (inset, span, y) = Coordinates();
        SetLeft(track, inset); SetTop(track, y - track.Height / 2); track.Width = span;
        for (var i = 0; i < checkpoints.Count; i++)
        {
            var node = checkpoints[i].Node;
            var x = inset + span * Clamp(stages[i].End) / 100 + i * NodeStep;
            SetLeft(node, x - NodeSize / 2); SetTop(node, y - NodeSize / 2);
            SetLeft(checkpoints[i].Label, x - LabelWidth / 2); SetTop(checkpoints[i].Label, 0);
        }
        caption.Width = LabelWidth;
        caption.Measure(new Size(LabelWidth, double.PositiveInfinity));
        var wanted = Math.Max(52, axis.Height + CaptionGap + caption.DesiredSize.Height + RasterInset);
        if (Height != wanted) Height = wanted;
        PlaceActive();
    }
    private void PlaceActive()
    {
        var (inset, span, y) = Coordinates();
        var position = Clamp(GetValue(DisplayPositionProperty));
        if (activeIndex >= 0)
            // 阶段切换时的旧动画值不能越过新阶段起点，否则新名称会暂时回到完成节点上。
            position = Math.Clamp(position, Clamp(stages[activeIndex].Start), Math.Max(Clamp(stages[activeIndex].Start), Clamp(stages[activeIndex].End)));
        var x = inset + span * position / 100 + Math.Max(0, activeIndex) * NodeStep;
        SetLeft(movingNode, x - NodeSize / 2); SetTop(movingNode, y - NodeSize / 2);
        movingNode.Background = ThemePalette.Panel;
        SetLeft(caption, x - LabelWidth / 2); SetTop(caption, 0);
        var right = checkpoints.Where(c => c.Node.IsVisible).Select(c => GetLeft(c.Label) + LabelWidth + RasterInset).DefaultIfEmpty(0).Max();
        if (movingNode.IsVisible) right = Math.Max(right, x + LabelWidth / 2 + RasterInset);
        layout.Width = Math.Max(Bounds.Width, right);
        // 仅跟随真实活动节点；历史完成节点仍可由用户滚动查看。
        if (movingNode.IsVisible && viewport.Viewport.Width > 0)
        {
            var offset = viewport.Offset.X;
            if (x + LabelWidth / 2 > offset + viewport.Viewport.Width) offset = x + LabelWidth / 2 - viewport.Viewport.Width + RasterInset;
            if (x - LabelWidth / 2 < offset) offset = Math.Max(0, x - LabelWidth / 2 - RasterInset);
            viewport.Offset = new(Math.Clamp(offset, 0, Math.Max(0, layout.Width - viewport.Viewport.Width)), 0);
        }
    }

    /// <summary>
    /// 仅对已知阶段标识作精确显示映射，加载器名称来自真实任务元数据。
    /// </summary>
    internal static string StageName(StageProgress stage) => stage.Kind switch
    {
        "Minecraft Assets" => "Assets",
        "Minecraft Libraries" => "Libraries",
        "Minecraft Version" => "Version",
        "Minecraft Runtime" => "Runtime",
        "Minecraft Java" or "Java 环境" => "Java",
        "Minecraft Client" => "Client",
        // 现有游戏文件组和合并依赖组并非只有客户端JAR或单独Libraries，保留其准确中文含义。
        "游戏本体" => "游戏本体",
        "资源与依赖" => "资源与依赖",
        "Minecraft Loader" or "模组加载器" => string.IsNullOrWhiteSpace(stage.DisplayName) ? "模组加载器" : stage.DisplayName,
        _ => stage.Kind
    };
    private static string FullName(StageProgress stage) => string.IsNullOrWhiteSpace(stage.DisplayName) ? stage.Kind : stage.Kind + " · " + stage.DisplayName;
}
