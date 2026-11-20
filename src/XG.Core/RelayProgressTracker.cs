// SPDX-License-Identifier: MIT
namespace XG.Core;

/// <summary>
/// 按真实开始的阶段分配剩余区间，固定完成检查点并保持进度高水位。
/// </summary>
public sealed class RelayProgressTracker
{
    private readonly Dictionary<string, (double Start, double End)> ranges = [];
    private double highWater;
    private readonly Dictionary<string, double> fractions = [];
    /// <summary>
    /// 只为已开始阶段建立区间；后续权重变化不移动既有完成检查点。
    /// </summary>
    public InstallationProgress Update(InstallationProgress measured, string state)
    {
        var values = measured.Stages.Where(x => x.Required).ToArray();
        var result = new List<StageProgress>();
        double cursor = 0;
        for (var i = 0; i < values.Length; i++)
        {
            var stage = values[i];
            // 等待阶段尚未开始，不分配可见检查点，也不提前标记完成。
            var started = stage.State is not ("等待中" or "等待恢复") || stage.Fraction > 0;
            if (!ranges.TryGetValue(stage.Kind, out var range))
            {
                if (!started) continue;
                var remaining = values.Skip(i).Where(x => !ranges.ContainsKey(x.Kind)).Sum(x => Math.Max(0, x.End - x.Start));
                var weight = Math.Max(0, stage.End - stage.Start);
                var end = stage.Kind == "完成检查" ? 100 : cursor + (99 - cursor) * (remaining > 0 ? weight / remaining : 0);
                range = (cursor, Math.Max(cursor, end));
                ranges[stage.Kind] = range;
            }
            var fraction = stage.State == "已完成" ? 1 : Math.Clamp(stage.Fraction, 0, 1);
            fraction = Math.Max(fractions.GetValueOrDefault(stage.Kind), fraction);
            fractions[stage.Kind] = fraction;
            highWater = Math.Max(highWater, range.Start + (range.End - range.Start) * fraction);
            result.Add(stage with { Start = range.Start, End = range.End, Fraction = fraction });
            cursor = range.End;
        }
        return new(state == "已完成" ? 100 : Math.Min(99, highWater), result, measured.HasUnknownBytes);
    }
}
