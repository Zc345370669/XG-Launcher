// SPDX-License-Identifier: MIT
using System.Text.Json;

namespace XG.Core;

public sealed record ResourceRecovery(string Type, Guid InstanceId, string? Configuration = null,
    ModFile? Archive = null, string? StagedFile = null);

public sealed record ResourceOperation(Guid Id, string Name, string Kind, string Source, string Instance,
    string State, string? Error = null, long Bytes = 0, long TotalBytes = 0, double BytesPerSecond = 0, string? CurrentFile = null,
    int CompletedFiles = 0, int TotalFiles = 0, long FileBytes = 0, long FileTotalBytes = 0, ResourceRecovery? Recovery = null, Guid? ParentId = null, string? CurrentStage = null, bool Skipped = false);

/// <summary>
/// 管理安装主任务及子阶段，聚合真实进度并持久化可恢复任务。
/// </summary>
public sealed class ResourceOperations
{
    private readonly string index;
    private readonly List<ResourceOperation> operations;
    private readonly Dictionary<Guid, ResourceControl> active = [];
    private readonly Dictionary<Guid, TaskCompletionSource> completions = [];
    private readonly HashSet<Guid> suspending = [];
    private bool preparingExit;
    private readonly Dictionary<Guid, long> lastCheckpoint = [];
    private static bool Terminal(string state) => state is "已完成" or "失败" or "已取消";
    public bool CanRecover(Guid id) => !active.ContainsKey(id) &&
        operations.Any(x => x.Id == id && x.Recovery != null && x.State is "等待恢复" or "失败");
    private readonly AsyncLocal<(Guid Id, ResourceControl Control)?> current = new();
    public ResourceControl? Current => current.Value?.Control;
    public bool CanControl(Guid id) => active.ContainsKey(id);
    public bool IsPaused(Guid id) => active.TryGetValue(id, out var control) && control.Paused;
    private bool HasInstaller(Guid id) => Descendants(id).Append(id).Any(child => active.ContainsKey(child) &&
        operations.Any(x => x.Id == child && x.Kind == "模组加载器"));
    private void Pending(Guid id, string state)
    {
        var i = operations.FindIndex(x => x.Id == id);
        if (i >= 0) operations[i] = operations[i] with { State = state };
        InstanceStore.WriteJson(index, operations); Changed?.Invoke();
    }
    public void Pause(Guid id)
    {
        foreach (var child in Descendants(id).Prepend(id))
            if (active.TryGetValue(child, out var job)) job.Pause();
        if (HasInstaller(id)) Pending(id, "等待当前安装步骤结束后暂停");
    }
    public void Resume(Guid id)
    {
        foreach (var child in Descendants(id).Prepend(id))
            if (active.TryGetValue(child, out var job)) job.Resume();
    }
    private readonly HashSet<Guid> deferredCancellation = [];
    public void Cancel(Guid id)
    {
        // 加载器原子安装步骤不强行打断；先恢复当前步骤，待步骤结束后再取消父任务。
        if (HasInstaller(id))
        { deferredCancellation.Add(id); Resume(id); Pending(id, "等待当前安装步骤结束后取消"); return; }
        if (active.TryGetValue(id, out var job)) job.Cancel();
    }
    public IEnumerable<Guid> Descendants(Guid id) => operations.Where(x => x.ParentId == id)
        .Select(x => x.Id).ToArray().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    public IReadOnlyList<ResourceOperation> MainJobs => operations.Where(x => x.ParentId == null).ToArray();
    public int UnfinishedCount => MainJobs.Count(x => x.State is not ("已完成" or "已取消"));
    public IReadOnlyList<ResourceOperation> Items => operations.AsReadOnly();
    public event Action? Changed;
    public event Action<ResourceOperation>? ProgressChanged;
    private readonly Dictionary<Guid, (long Bytes, long Timestamp)> samples = [];

    public ResourceOperations(string root)
    {
        index = Path.Combine(root, "resource-operations.json");
        operations = File.Exists(index) ? JsonSerializer.Deserialize<List<ResourceOperation>>(File.ReadAllText(index)) ?? [] : [];
        for (var i = 0; i < operations.Count; i++)
            if (!Terminal(operations[i].State))
            {
                var recovery = operations[i].Recovery != null;
                operations[i] = operations[i] with { State = recovery ? "等待恢复" : "失败",
                    Error = recovery ? "已加载上次任务。恢复时重新校验缓存与目标。" : "上次操作中断。相关子任务可通过父任务重新执行。" };
            }
    }

    /// <summary>
    /// 登记真实安装计划；已缓存阶段以Skipped标识，尚未执行阶段不能据此视为完成。
    /// </summary>
    public void PlanStages(IEnumerable<(string Kind, long Bytes, int Files, bool Ready)> stages, string instance)
    {
        if (current.Value is not { } owner) return;
        foreach (var stage in stages)
        {
            var i = operations.FindIndex(x => x.ParentId == owner.Id && x.Kind == stage.Kind);
            if (i < 0) operations.Add(new ResourceOperation(Guid.NewGuid(), stage.Kind, stage.Kind, "官方资源", instance,
                stage.Ready ? "已完成" : "等待中", TotalBytes: stage.Bytes, CompletedFiles: stage.Ready ? stage.Files : 0, TotalFiles: stage.Files, ParentId: owner.Id, Skipped: stage.Ready));
            else operations[i] = operations[i] with { TotalBytes = stage.Bytes, TotalFiles = stage.Files,
                State = stage.Ready ? "已完成" : operations[i].State, Skipped = stage.Ready,
                Bytes = stage.Ready ? stage.Bytes : Math.Min(operations[i].Bytes, stage.Bytes > 0 ? stage.Bytes : long.MaxValue),
                CompletedFiles = stage.Ready ? stage.Files : Math.Min(operations[i].CompletedFiles, stage.Files) };
        }
        InstanceStore.WriteJson(index, operations);
        Changed?.Invoke();
    }

    /// <summary>
    /// 在当前异步任务上下文中创建或复用子阶段，再执行现有状态与持久化流程。
    /// </summary>
    /// <remarks>AsyncLocal只传递父子上下文，不为operations或active集合加锁；事件订阅者应自行切回UI线程。</remarks>
    /// <param name="name">任务名称，不替换阶段Kind。</param>
    /// <param name="kind">内部阶段标识，用于计划复用与进度聚合。</param>
    /// <param name="source">资源来源；加载器开始时携带真实加载器名称。</param>
    /// <param name="target">关联实例或目标名称。</param>
    /// <param name="run">实际执行体，通过回调报告当前状态。</param>
    /// <param name="recovery">可选的重启恢复描述。</param>
    /// <returns>实际执行体的返回值；失败与取消按原流程传播。</returns>
    public async Task<T> RunAsync<T>(string name, string kind, string source, string target,
        Func<Action<string>, Task<T>> run, ResourceRecovery? recovery = null)
    {
        if (preparingExit && current.Value == null) throw new InvalidOperationException("正在保存下载任务，请稍后重试。");
        if (current.Value is { } parent) await parent.Control.WaitIfPausedAsync();
        var item = current.Value is { } owner
            ? operations.LastOrDefault(x => x.ParentId == owner.Id && x.Kind == kind) : null;
        item ??= new ResourceOperation(Guid.NewGuid(), name, kind, source, target, "等待中",
            Recovery: recovery, ParentId: current.Value?.Id);
        if (!operations.Any(x => x.Id == item.Id)) operations.Add(item);
        // 计划加载器来源最初是通用值，仅在真实安装器开始执行时补齐实际名称，
        // 保留内部标识、计数和状态，不制造加载器完成事件。
        if (kind == "模组加载器" && item.Source != source)
        {
            item = item with { Source = source };
            operations[operations.FindIndex(x => x.Id == item.Id)] = item;
        }
        return await ExecuteAsync(item, run);
    }

    public Task<T> RetryAsync<T>(Guid id, Func<Action<string>, Task<T>> run)
    {
        if (preparingExit) throw new InvalidOperationException("正在保存下载任务，请稍后重试。");
        if (!CanRecover(id)) throw new InvalidOperationException("此任务目前不可恢复。");
        return ExecuteAsync(operations.Single(x => x.Id == id), run);
    }

    public void Discard(Guid id)
    {
        if (!CanRecover(id)) return;
        var i = operations.FindIndex(x => x.Id == id);
        operations[i] = operations[i] with { State = "已取消", Error = null };
        InstanceStore.WriteJson(index, operations);
        Changed?.Invoke();
    }

    private async Task<T> ExecuteAsync<T>(ResourceOperation item, Func<Action<string>, Task<T>> run)
    {
        var parent = current.Value;
        using var control = new ResourceControl(parent?.Control.Token ?? default, status => Set(status));
        current.Value = (item.Id, control);
        active.Add(item.Id, control);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completions.Add(item.Id, completion);
        try
        {
            Set("等待中");
            var result = await run(stage => { if (!control.Paused) Set(stage); });
            if (control.Paused) Set("已暂停");
            await control.WaitIfPausedAsync();
            if (operations.Any(x => x.ParentId == item.Id && x.State != "已完成"))
                throw new InvalidOperationException("仍有未完成的安装阶段，请重试。");
            Set("已完成");
            return result;
        }
        catch (OperationCanceledException) when (control.Token.IsCancellationRequested)
        {
            // 退出是挂起，不是用户取消；保留恢复描述及部分文件，重启后重新校验。
            Set(suspending.Contains(item.Id) ? (item.Recovery != null ? "等待恢复" : "已暂停") : "已取消");
            throw;
        }
        catch (Exception error)
        {
            var detail = new GameLogSanitizer(null).Redact(VerifiedTransfer.Describe(error));
            try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(index)!, "resource-errors.log"),
                System.Text.Json.JsonSerializer.Serialize(new { Time = DateTimeOffset.UtcNow, item.Id, item.ParentId, item.Kind, Error = detail, Type = error.GetType().Name }) + Environment.NewLine); }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.TraceWarning(logError.GetType().Name); }
            Set("失败", detail);
            throw;
        }
        finally
        {
            active.Remove(item.Id);
            foreach (var deferred in deferredCancellation.ToArray())
                if (!Descendants(deferred).Any(child => active.ContainsKey(child) && operations.Any(x => x.Id == child && x.Kind == "模组加载器")))
                { deferredCancellation.Remove(deferred); if (active.TryGetValue(deferred, out var pending)) pending.Cancel(); }
            samples.Remove(item.Id);
            lastCheckpoint.Remove(item.Id);
            current.Value = parent;
            completions.Remove(item.Id);
            completion.TrySetResult();
        }

        void Set(string stage, string? error = null)
        {
            var i = operations.FindIndex(x => x.Id == item.Id);
            if (operations[i].State == stage && operations[i].Error == error && stage != "等待中") return;
            operations[i] = operations[i] with { State = stage, Error = error };
            InstanceStore.WriteJson(index, operations);
            Aggregate(item.ParentId);
            Changed?.Invoke();
        }
    }

    private void Aggregate(Guid? parentId)
    {
        if (parentId is not { } id) return;
        var position = operations.FindIndex(x => x.Id == id);
        if (position < 0) return;
        var children = operations.Where(x => x.ParentId == id).ToArray();
        var current = children.LastOrDefault(x => active.ContainsKey(x.Id)) ?? children.Last();
        var known = children.All(x => x.TotalBytes > 0 || x.State == "已完成");
        operations[position] = operations[position] with {
            State = deferredCancellation.Contains(id) ? "等待当前安装步骤结束后取消" :
                IsPaused(id) ? (current.State == "已暂停" ? "已暂停" : operations[position].State) :
                !active.ContainsKey(id) ? operations[position].State : (current.State == "已完成" ? "准备中" : current.State),
            CurrentStage = current.Kind,
            Bytes = children.Sum(x => x.Bytes), TotalBytes = known ? children.Sum(x => x.TotalBytes) : 0,
            BytesPerSecond = current.BytesPerSecond, CurrentFile = current.CurrentFile,
            CompletedFiles = children.Sum(x => x.CompletedFiles), TotalFiles = children.Sum(x => x.TotalFiles),
            FileBytes = current.FileBytes, FileTotalBytes = current.FileTotalBytes };
        ProgressChanged?.Invoke(operations[position]);
        Aggregate(operations[position].ParentId);
    }

    public void Report(string kind, string instance, long bytes, long total, string? file = null,
        int completedFiles = 0, int totalFiles = 0, long fileBytes = 0, long fileTotalBytes = 0, bool measureSpeed = true, long? networkBytes = null)
    {
        var i = current.Value is { } scope ? operations.FindIndex(x => x.Id == scope.Id) : -1;
        if (i < 0 || Current?.Paused == true || Terminal(operations[i].State)) return;
        var item = operations[i];
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var speed = 0.0;
        var sampledBytes = networkBytes ?? bytes;
        if (!measureSpeed) samples[item.Id] = (sampledBytes, now);
        else if (samples.TryGetValue(item.Id, out var previous))
        {
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(previous.Timestamp, now).TotalSeconds;
            if (elapsed >= .25)
            {
                speed = Math.Max(0, sampledBytes - previous.Bytes) / elapsed;
                samples[item.Id] = (sampledBytes, now);
            }
            else speed = item.BytesPerSecond;
        }
        else samples[item.Id] = (sampledBytes, now);
        operations[i] = item with { Bytes = bytes, TotalBytes = total, BytesPerSecond = speed,
            CurrentFile = file, CompletedFiles = completedFiles, TotalFiles = totalFiles,
            FileBytes = fileBytes, FileTotalBytes = fileTotalBytes };
        if (!lastCheckpoint.TryGetValue(item.Id, out var checkpoint) ||
            System.Diagnostics.Stopwatch.GetElapsedTime(checkpoint, now).TotalSeconds >= 2)
        {
            InstanceStore.WriteJson(index, operations);
            lastCheckpoint[item.Id] = now;
        }
        ProgressChanged?.Invoke(operations[i]);
        Aggregate(operations[i].ParentId);
    }

    /// <summary>
    /// 在既有调度上下文中停止任务并写入可恢复状态；原子加载器步骤沿用延迟取消策略。
    /// </summary>
    /// <remarks>调用方应同时禁止界面发起新操作；等待期间不阻塞 UI 线程，也不删除部分下载文件。</remarks>
    public async Task SuspendForExitAsync()
    {
        if (preparingExit) throw new InvalidOperationException("正在保存下载任务，请稍后重试。");
        preparingExit = true;
        try
        {
            var roots = MainJobs.Where(x => CanControl(x.Id)).ToArray();
            foreach (var root in roots)
                foreach (var id in Descendants(root.Id).Prepend(root.Id)) suspending.Add(id);
            var pending = completions.Values.Select(x => x.Task).ToArray();
            foreach (var root in roots) Cancel(root.Id);
            await Task.WhenAll(pending);
            // 只有所有执行体释放文件句柄后，才允许调用方关闭窗口并释放 HTTP 客户端。
            InstanceStore.WriteJson(index, operations);
        }
        finally { suspending.Clear(); preparingExit = false; }
    }

    public void ClearCompleted()
    {
        var removed = MainJobs.Where(x => x.State is "已完成" or "已取消")
            .SelectMany(x => new[] { x.Id }.Concat(Descendants(x.Id))).ToHashSet();
        operations.RemoveAll(x => removed.Contains(x.Id));
        InstanceStore.WriteJson(index, operations);
        Changed?.Invoke();
    }
}
