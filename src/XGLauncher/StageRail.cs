// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
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
    // 圆心位于0%/100%时半个圆圈伸入卡片原有内边距；不用会裁切内容的滚动视口。
    private readonly Border viewport = new() { Name = "StageNodeViewport", ClipToBounds = false };
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
    private double percent;
    internal double Percent => percent;
    internal double DisplayedPercent => Clamp(GetValue(DisplayPositionProperty));
    internal event Action<double>? DisplayedPercentChanged;
    private static readonly StyledProperty<double> DisplayPositionProperty =
        AvaloniaProperty.Register<StageRail, double>("DisplayPosition");

    internal StageRail()
    {
        Height = 52; ClipToBounds = false; UseLayoutRounding = false;
        movingNode = Badge(moving); movingNode.Name = "CurrentStageNode"; movingNode.ZIndex = 1;
        axis.Children.Add(track); axis.Children.Add(movingNode);
        labels.Children.Add(caption);
        layout.Children.Add(axis); Grid.SetRow(labels, 1); layout.Children.Add(labels); viewport.Child = layout; Children.Add(viewport);
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
    internal void Update(IReadOnlyList<StageProgress> values, string? current, string jobState, double? totalPercent = null)
    {
        stages = values.Where(x => x.Required).ToArray(); state = jobState;
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
        movingNode.IsVisible = moving.IsVisible = active != null && (active.State is not ("等待中" or "等待恢复") || active.Fraction > 0) && state is not ("已完成" or "已取消");
        moving.Icon = state == "失败" ? "circle-exclamation-point" : "circle";
        targetPosition = totalPercent is { } actual ? Clamp(actual) : active != null ? Clamp(active.Start + (active.End - active.Start) * Fraction(active.Fraction)) :
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
        if (change.Property == DisplayPositionProperty && axis != null && movingNode != null)
        {
            PlaceActive();
            // 同一动画值驱动上方填充条，避免圆圈仍在过渡而进度条已跳到新位置。
            DisplayedPercentChanged?.Invoke(DisplayedPercent);
        }
    }
    private static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
    private static double Fraction(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    // 节点和名称统一使用DIP坐标；共享中心线并避免分别像素舍入，降低非整数DPI下的错位。
    private (double Inset, double Span, double Y) Coordinates()
    {
        // 使用圆心匹配整条进度条坐标：x = width × percent，不再扣除圆圈半径。
        return (0, Math.Max(0, Bounds.Width), axis.Bounds.Height / 2);
    }
    private void UpdateGeometry()
    {
        if (axis == null || movingNode == null) return;
        var (inset, span, y) = Coordinates();
        SetLeft(track, inset); SetTop(track, y - track.Height / 2); track.Width = span;
        for (var i = 0; i < checkpoints.Count; i++)
        {
            var node = checkpoints[i].Node;
            var x = inset + span * Clamp(stages[i].End) / 100;
            SetLeft(node, x - NodeSize / 2); SetTop(node, y - NodeSize / 2);
        }
        PlaceActive();
    }
    private void PlaceActive()
    {
        var (inset, span, y) = Coordinates();
        var position = Clamp(GetValue(DisplayPositionProperty));
        var x = inset + span * position / 100;
        SetLeft(movingNode, x - NodeSize / 2); SetTop(movingNode, y - NodeSize / 2);
        // 接力处允许圆圈同心，蓝环上层保持透明背景，让已完成的绿色勾仍可见。
        movingNode.Background = stages.Any(stage => stage.State == "已完成" && Math.Abs(Clamp(stage.End) - position) * span / 100 < NodeSize)
            ? Brushes.Transparent : ThemePalette.Panel;
        layout.Width = Bounds.Width;
        LayoutNames(x);
    }

    private void LayoutNames(double activeX)
    {
        // 名称拥挤时只在轨道下方分行，不推动圆圈、不改变真实进度的横向映射。
        var rows = new List<List<(double Left, double Right)>>();
        double lineHeight = 19;
        var nodes = checkpoints.Where(c => c.Node.IsVisible).Select(c => (Label: c.Label, X: GetLeft(c.Node) + NodeSize / 2)).ToList();
        if (caption.IsVisible) nodes.Add((caption, activeX));
        foreach (var item in nodes)
        {
            item.Label.Width = double.NaN;
            item.Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            lineHeight = Math.Max(lineHeight, item.Label.DesiredSize.Height);
        }
        foreach (var item in nodes)
        {
            var width = Math.Min(Math.Max(0, Bounds.Width), Math.Min(LabelWidth, item.Label.DesiredSize.Width));
            var left = Math.Clamp(item.X - width / 2, 0, Math.Max(0, Bounds.Width - width));
            var row = rows.FindIndex(intervals => intervals.All(interval => left >= interval.Right + 4 || left + width + 4 <= interval.Left));
            if (row < 0) { row = rows.Count; rows.Add([]); }
            rows[row].Add((left, left + width));
            item.Label.Width = width;
            SetLeft(item.Label, left); SetTop(item.Label, row * (lineHeight + 4));
        }
        var wanted = Math.Max(52, axis.Height + CaptionGap + rows.Count * (lineHeight + 4) - 4 + RasterInset);
        if (Height != wanted) Height = wanted;
    }

    /// <summary>
    /// 仅对已知阶段标识作精确显示映射，加载器名称来自真实任务元数据。
    /// </summary>
    internal static string StageName(StageProgress stage) => stage.Kind switch
    {
        "Minecraft Assets" => "Assets",
        "Minecraft Libraries" => "Libs",
        "Minecraft Version" => "Version",
        "Minecraft Runtime" => "Runtime",
        "Minecraft Java" or "Java 环境" => "Java",
        "Minecraft Client" => "Client",
        // 仅缩短节点文案，内部阶段Kind、日志和下载分组保持原样。
        "游戏本体" => "本体",
        "资源与依赖" => "依赖",
        "完成检查" => "校验",
        "Minecraft Loader" or "模组加载器" => string.IsNullOrWhiteSpace(stage.DisplayName) ? "模组加载器" : stage.DisplayName,
        _ => stage.Kind
    };
    private static string FullName(StageProgress stage) => string.IsNullOrWhiteSpace(stage.DisplayName) ? stage.Kind : stage.Kind + " · " + stage.DisplayName;
}
