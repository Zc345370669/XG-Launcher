// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Text.Json;
using XG.Core;

if (!OperatingSystem.IsWindows() || args.Length != 2 || args[0] != "--apply-update") return 2;
var planFile = Path.GetFullPath(args[1]);
try
{
    var plan = JsonSerializer.Deserialize<UpdateApplyRequest>(await File.ReadAllTextAsync(planFile)) ?? throw new InvalidDataException();
    var executable = Path.Combine(plan.InstallDirectory, "XGLauncher.exe");
    if (!File.Exists(executable) || plan.ParentProcessId < 1) throw new InvalidDataException();
    // 等原进程真正退出，避免只关闭窗口却仍锁定旧 EXE；不终止游戏或其他进程。
    try
    {
        using var parent = Process.GetProcessById(plan.ParentProcessId);
        if (parent.StartTime.ToUniversalTime().Ticks == plan.ParentStartTicks)
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await parent.WaitForExitAsync(limit.Token);
        }
    }
    catch (ArgumentException) { /* 原进程已经退出。 */ }
    using (var transaction = UpdateFiles.Prepare(plan))
    {
        transaction.Apply();
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = plan.InstallDirectory });
        transaction.Commit();
    }
    await File.WriteAllTextAsync(planFile + ".result", "Updated");
    return 0;
}
catch (Exception error)
{
    // 只记录错误类型；请求内的本地路径和任何用户数据不写入公开诊断。
    try { await File.WriteAllTextAsync(planFile + ".result", error.GetType().Name); } catch (IOException) { }
    return 1;
}
