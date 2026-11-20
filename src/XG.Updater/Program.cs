// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Text.Json;
using XG.Core;

if (!OperatingSystem.IsWindows() || args.Length != 2 || args[0] != "--apply-update") return 2;
var planFile = Path.GetFullPath(args[1]);
string? restart = null; bool originalExited = false;
try
{
    var plan = JsonSerializer.Deserialize<UpdateApplyRequest>(await File.ReadAllTextAsync(planFile)) ?? throw new InvalidDataException();
    var executable = Path.Combine(plan.InstallDirectory, "XGLauncher.exe");
    if (!File.Exists(executable) || plan.ParentProcessId < 1) throw new InvalidDataException();
    Status("WaitingForExit");
    // 必须等待原进程（包括互斥锁及 EXE 句柄）退出；不终止游戏或其他进程。
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
    originalExited = true; restart = executable;
    Status("Verifying");
    using (var transaction = UpdateFiles.Prepare(plan))
    {
        Status("Replacing"); transaction.Apply();
        // 新进程可能立即读取回执，先原子写入；启动失败时回滚并覆盖为失败状态。
        UpdateReceipt.Write(planFile + ".result", UpdateOutcome.Updated);
        Status("Restarting"); Restart(executable);
        transaction.Commit();
    }
    Status("Finished");
    return 0;
}
catch (Exception error)
{
    UpdateReceipt.Write(planFile + ".result", UpdateReceipt.Failure(error));
    Status("Failed");
    if (originalExited && restart != null && File.Exists(restart))
        try { Restart(restart); } catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) { }
    return 1;
}

void Restart(string executable)
{
    var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
    start.ArgumentList.Add("--update-result"); start.ArgumentList.Add(planFile + ".result");
    using var child = Process.Start(start) ?? throw new InvalidOperationException("Replacement process did not start.");
}

void Status(string phase)
{
    try { File.WriteAllText(planFile + ".status", phase); } catch (IOException) { } catch (UnauthorizedAccessException) { }
}
