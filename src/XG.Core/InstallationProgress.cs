// SPDX-License-Identifier: MIT
namespace XG.Core;

public sealed record StageProgress(string Kind, string State, double Start, double End, double Fraction, bool Required);
public sealed record InstallationProgress(double Percent, IReadOnlyList<StageProgress> Stages, bool HasUnknownBytes)
{
    // One file verification/atomic installer step is one work unit when bytes are unavailable.
    // This is a work-progress model, never an estimate of unknown network bytes or installer time.
    private const double WorkUnit = 1048576;
    public static InstallationProgress Calculate(ResourceOperation main, IReadOnlyList<ResourceOperation> children)
    {
        if (children.Count == 0)
            return new(main.State == "已完成" ? 100 : main.TotalBytes > 0 ? Math.Clamp(100.0 * main.Bytes / main.TotalBytes, 0, 99) : 0, [], main.TotalBytes <= 0);
        var weights = children.Select(x => x.State == "已完成" && x.TotalBytes == 0 && x.TotalFiles == 0 ? 0 : x.TotalBytes > 0 ? (double)x.TotalBytes : Math.Max(1, x.TotalFiles) * WorkUnit).ToArray();
        var total = weights.Sum() + WorkUnit; // Actual final installation/Java validation is one mandatory step.
        double cursor = 0, completed = 0;
        var stages = new List<StageProgress>();
        for (var n = 0; n < children.Count; n++)
        {
            var x = children[n];
            var fraction = x.State == "已完成" ? 1 : x.TotalBytes > 0 ? Math.Clamp((double)x.Bytes / x.TotalBytes, 0, 1)
                : x.TotalFiles > 0 ? Math.Clamp((double)x.CompletedFiles / x.TotalFiles, 0, 1) : 0;
            stages.Add(new(x.Kind, x.State, cursor / total * 100, (cursor + weights[n]) / total * 100, fraction,
                !(x.State == "已完成" && x.TotalBytes == 0 && x.Bytes == 0)));
            cursor += weights[n]; completed += weights[n] * fraction;
        }
        return new(main.State == "已完成" ? 100 : Math.Clamp(completed / total * 100, 0, 99), stages,
            children.Any(x => x.TotalBytes <= 0 && x.State != "已完成"));
    }
}
