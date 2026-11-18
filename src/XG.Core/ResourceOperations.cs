// SPDX-License-Identifier: MIT
using System.Text.Json;

namespace XG.Core;

public sealed record ResourceRecovery(string Type, Guid InstanceId, string? Configuration = null,
    ModFile? Archive = null, string? StagedFile = null);

public sealed record ResourceOperation(Guid Id, string Name, string Kind, string Source, string Instance,
    string State, string? Error = null, long Bytes = 0, long TotalBytes = 0, double BytesPerSecond = 0, string? CurrentFile = null,
    int CompletedFiles = 0, int TotalFiles = 0, long FileBytes = 0, long FileTotalBytes = 0, ResourceRecovery? Recovery = null, Guid? ParentId = null, string? CurrentStage = null);

public sealed class ResourceOperations
{
    private readonly string index;
    private readonly List<ResourceOperation> operations;
    private readonly Dictionary<Guid, ResourceControl> active = [];
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

    public void PlanStages(IEnumerable<(string Kind, long Bytes, int Files, bool Ready)> stages, string instance)
    {
        if (current.Value is not { } owner) return;
        foreach (var stage in stages)
        {
            var i = operations.FindIndex(x => x.ParentId == owner.Id && x.Kind == stage.Kind);
            if (i < 0) operations.Add(new ResourceOperation(Guid.NewGuid(), stage.Kind, stage.Kind, "官方资源", instance,
                stage.Ready ? "已完成" : "等待中", TotalBytes: stage.Bytes, CompletedFiles: stage.Ready ? stage.Files : 0, TotalFiles: stage.Files, ParentId: owner.Id));
            else operations[i] = operations[i] with { TotalBytes = stage.Bytes, TotalFiles = stage.Files,
                State = stage.Ready ? "已完成" : operations[i].State,
                Bytes = stage.Ready ? stage.Bytes : Math.Min(operations[i].Bytes, stage.Bytes > 0 ? stage.Bytes : long.MaxValue),
                CompletedFiles = stage.Ready ? stage.Files : Math.Min(operations[i].CompletedFiles, stage.Files) };
        }
        InstanceStore.WriteJson(index, operations);
        Changed?.Invoke();
    }

    public async Task<T> RunAsync<T>(string name, string kind, string source, string target,
        Func<Action<string>, Task<T>> run, ResourceRecovery? recovery = null)
    {
        if (current.Value is { } parent) await parent.Control.WaitIfPausedAsync();
        var item = current.Value is { } owner
            ? operations.LastOrDefault(x => x.ParentId == owner.Id && x.Kind == kind) : null;
        item ??= new ResourceOperation(Guid.NewGuid(), name, kind, source, target, "等待中",
            Recovery: recovery, ParentId: current.Value?.Id);
        if (!operations.Any(x => x.Id == item.Id)) operations.Add(item);
        return await ExecuteAsync(item, run);
    }

    public Task<T> RetryAsync<T>(Guid id, Func<Action<string>, Task<T>> run)
    {
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
            Set("已取消");
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
        int completedFiles = 0, int totalFiles = 0, long fileBytes = 0, long fileTotalBytes = 0, bool measureSpeed = true)
    {
        var i = current.Value is { } scope ? operations.FindIndex(x => x.Id == scope.Id) : -1;
        if (i < 0 || Current?.Paused == true || Terminal(operations[i].State)) return;
        var item = operations[i];
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var speed = 0.0;
        if (!measureSpeed) samples[item.Id] = (bytes, now);
        else if (samples.TryGetValue(item.Id, out var previous))
        {
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(previous.Timestamp, now).TotalSeconds;
            if (elapsed >= .25)
            {
                speed = Math.Max(0, bytes - previous.Bytes) / elapsed;
                samples[item.Id] = (bytes, now);
            }
            else speed = item.BytesPerSecond;
        }
        else samples[item.Id] = (bytes, now);
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

    public void ClearCompleted()
    {
        var removed = MainJobs.Where(x => x.State is "已完成" or "已取消")
            .SelectMany(x => new[] { x.Id }.Concat(Descendants(x.Id))).ToHashSet();
        operations.RemoveAll(x => removed.Contains(x.Id));
        InstanceStore.WriteJson(index, operations);
        Changed?.Invoke();
    }
}
