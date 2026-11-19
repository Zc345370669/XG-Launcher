// SPDX-License-Identifier: MIT
using XG.Core;
internal static class DualModeProgressTests
{
    internal static void Run()
    {
        var tracker = new RelayProgressTracker();
        var measured = new InstallationProgress(20, [new("游戏本体", "下载中", 0, 40, .5, true), new("Assets", "等待中", 40, 99, 0, true), new("完成检查", "等待中", 99, 100, 0, true)], false);
        var first = tracker.Update(measured, "下载中");
        Check(first.Stages.Count == 1 && first.Percent > 0 && first.Percent < 40, "unstarted stages visible");
        var end = first.Stages[0].End;
        measured = measured with { Stages = [new("游戏本体", "已完成", 0, 5, 1, true), new("Assets", "下载中", 5, 99, .2, true), new("完成检查", "等待中", 99, 100, 0, true)] };
        var second = tracker.Update(measured, "下载中");
        Check(second.Percent >= first.Percent && second.Stages[0].End == end && second.Stages[1].Start == end, "checkpoint moved or relay reset on dynamic weights");
        var retry = tracker.Update(measured with { Stages = measured.Stages.Select(x => x with { Fraction = 0 }).ToArray() }, "下载中");
        Check(retry.Percent >= second.Percent && retry.Percent < 100, "retry regressed or false completion");
        var main = new ResourceOperation(Guid.NewGuid(), "fixture", "实例安装", "fixture", "fixture", "校验中");
        var cached = new ResourceOperation(Guid.NewGuid(), "Java", "Java 环境", "fixture", "fixture", "已完成", TotalBytes: 500, Skipped: true);
        var noWork = InstallationProgress.Calculate(main, [cached]);
        Check(noWork.Stages.All(x => x.Kind != "Java 环境" && x.Kind != "模组加载器") && noWork.Percent < 100, "cached/no-loader stage not skipped");
        Check(InstallationProgress.Calculate(main with { State = "已完成" }, [cached]).Percent == 100, "completion not bound to successful root");
        Console.WriteLine("PASS phase17 relay dynamic weights, immutable completed checkpoints, contiguous next-stage start, monotonic retry, hidden unstarted/cached stages and completion gate");
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
