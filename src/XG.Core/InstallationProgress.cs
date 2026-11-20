// SPDX-License-Identifier: MIT
namespace XG.Core;

/// <summary>
/// 记录真实阶段状态与工作区间；DisplayName仅用于界面，不替换Kind标识。
/// </summary>
public sealed record StageProgress(string Kind, string State, double Start, double End, double Fraction, bool Required)
{
    public string? DisplayName { get; init; }
}
/// <summary>
/// 按已知字节或文件工作量计算进度，最终安装校验前不报告百分之百。
/// </summary>
public sealed record InstallationProgress(double Percent, IReadOnlyList<StageProgress> Stages, bool HasUnknownBytes)
{
    // 字节未知时，以文件校验或原子安装步骤作为工作单位；
    // 该值只衡量实际工作，不推测未知网络字节或安装耗时。
    private const double WorkUnit = 1048576;
    /// <summary>
    /// 根据同一主任务的实际子记录分配工作权重，保留真实零字节安装阶段名称。
    /// </summary>
    public static InstallationProgress Calculate(ResourceOperation main, IReadOnlyList<ResourceOperation> children)
    {
        if (children.Count == 0)
            return new(main.State == "已完成" ? 100 : main.TotalBytes > 0 ? Math.Clamp(100.0 * main.Bytes / main.TotalBytes, 0, 99) : 0, [], main.TotalBytes <= 0);
        children = children.Where(x => !x.Skipped).ToArray();
        var weights = children.Select(x => x.State == "已完成" && x.TotalBytes == 0 && x.TotalFiles == 0 ? 0 : x.TotalBytes > 0 ? (double)x.TotalBytes : Math.Max(1, x.TotalFiles) * WorkUnit).ToArray();
        var total = weights.Sum() + WorkUnit; // 最终安装及Java验证始终保留一个必要工作单位。
        double cursor = 0, completed = 0;
        var stages = new List<StageProgress>();
        for (var n = 0; n < children.Count; n++)
        {
            var x = children[n];
            var fraction = x.State == "已完成" ? 1 : x.TotalBytes > 0 ? Math.Clamp((double)x.Bytes / x.TotalBytes, 0, 1)
                : x.TotalFiles > 0 ? Math.Clamp((double)x.CompletedFiles / x.TotalFiles, 0, 1) : 0;
            stages.Add(new(x.Kind, x.State, cursor / total * 100, (cursor + weights[n]) / total * 100, fraction,
                true) // 未跳过的真实阶段保留名称，包括零字节原子安装。
                { DisplayName = x.Kind == "模组加载器" && x.Source != "官方资源" ? x.Source : null });
            cursor += weights[n]; completed += weights[n] * fraction;
        }
        var complete = main.State == "已完成";
        var validating = children.All(x => x.State == "已完成") && main.State is "安装中" or "校验中" or "准备中";
        stages.Add(new("完成检查", complete ? "已完成" : validating ? "校验中" : "等待中", cursor / total * 100, 100, complete ? 1 : 0, true));
        return new(complete ? 100 : Math.Clamp(completed / total * 100, 0, 99), stages,
            children.Any(x => x.TotalBytes <= 0 && x.State != "已完成"));
    }
}
