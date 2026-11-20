// SPDX-License-Identifier: MIT
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace XG.Core;

public sealed record Download(Guid Id, ModFile File, string Target, string State = "已暂停", string? Error = null, string Category = "模组", string? Instance = null, int Priority = 0, Guid? InstanceId = null, string? InstanceConfiguration = null);

/// <summary>
/// 维护模组文件队列、优先级和持久化状态，校验完成后才提交目标文件。
/// </summary>
public sealed class Downloads
{
    private readonly HttpClient http;
    private readonly string index;
    private readonly string? portableRoot;
    private readonly List<(Download Job, TaskCompletionSource Ready)> waiting = [];
    private int running;
    private int concurrency = 1;
    public int Concurrency => concurrency;
    public int SpeedLimitMiB { get; private set; }
    private readonly HashSet<Guid> cancelled = [];
    private readonly Dictionary<Guid, CancellationTokenSource> active = [];
    private readonly Dictionary<Guid, TaskCompletionSource> completions = [];
    private bool preparingExit;
    private readonly List<Download> items;
    public event Action? Changed;
    public IReadOnlyList<Download> Items => items.AsReadOnly();
    /// <summary>暂停只取消等待者时，底层共享传输仍可能写入；退出必须等待其释放。</summary>
    public bool HasPendingWrites => VerifiedTransfer.HasPendingWrites(http);

    public Downloads(HttpClient http, string root, string? portableRoot = null)
    {
        this.http = http;
        this.portableRoot = portableRoot;
        var options = Path.Combine(root, "download-concurrency.json");
        if (File.Exists(options)) concurrency = Math.Clamp(JsonSerializer.Deserialize<int>(File.ReadAllText(options)), 1, 6);
        VerifiedTransfer.SetConcurrency(http, concurrency);
        index = Path.Combine(root, "downloads.json");
        var speed = Path.Combine(root, "download-speed.json");
        if (File.Exists(speed)) SpeedLimitMiB = Math.Clamp(JsonSerializer.Deserialize<int>(File.ReadAllText(speed)), 0, 100);
        VerifiedTransfer.SetSpeedLimit(http, SpeedLimitMiB * 1048576L);
        items = File.Exists(index) ? JsonSerializer.Deserialize<List<Download>>(File.ReadAllText(index))
            ?? throw new InvalidDataException("下载索引为空。") : [];
        for (var i = 0; i < items.Count; i++)
        {
            if (portableRoot != null && !Path.IsPathRooted(items[i].Target))
            {
                var parts = items[i].Target.Split('/');
                items[i] = items[i] with { Target = parts.Aggregate(portableRoot, InstanceStore.SafeFile) };
            }
            if (items[i].State is "等待中" or "连接中" or "下载中" or "校验中" or "安装中") items[i] = items[i] with { State = "已暂停" };
        }
    }

    public static void Validate(ModFile file)
    {
        if (file.Url.Scheme != "https" || file.Url.Host != "cdn.modrinth.com" ||
            file.Url.UserInfo.Length != 0 || !file.Url.IsDefaultPort || file.Size < 1 ||
            file.Sha512.Length != 128 || !file.Sha512.All(Uri.IsHexDigit))
            throw new InvalidDataException("缺少受支持的文件地址或 SHA-512 校验值。");
    }

    public Download Enqueue(ModFile file, string directory, string? instance = null, string category = "模组", Guid? instanceId = null, string? instanceConfiguration = null)
    {
        Validate(file);
        var target = InstanceStore.SafeFile(directory, file.Name);
        if (items.Any(d => d.State is not ("已完成" or "已取消") && string.Equals(d.Target, target, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("此文件已有下载任务。");
        var task = new Download(Guid.NewGuid(), file, target, Category: category, Instance: instance, InstanceId: instanceId, InstanceConfiguration: instanceConfiguration);
        items.Add(task);
        Persist();
        return task;
    }

    public void Pause(Guid id)
    {
        if (active.TryGetValue(id, out var source)) source.Cancel();
    }

    public void Cancel(Guid id)
    {
        var position = items.FindIndex(x => x.Id == id);
        if (position < 0 || items[position].State is "已完成" or "已取消") return;
        if (active.TryGetValue(id, out var source))
        {
            cancelled.Add(id);
            source.Cancel();
            return;
        }
        var job = items[position];
        VerifiedTransfer.Reset(job.Target + "." + id.ToString("N") + ".part");
        items[position] = job with { State = "已取消", Error = null };
        Persist();
    }

    /// <summary>暂停活动和排队中的传输，保留续传文件并等待句柄释放后保存队列。</summary>
    public async Task PauseForExitAsync()
    {
        if (preparingExit) throw new InvalidOperationException("正在保存下载任务，请稍后重试。");
        preparingExit = true;
        try
        {
            var pending = completions.Values.Select(x => x.Task).ToArray();
            foreach (var source in active.Values.ToArray()) source.Cancel();
            // 单个暂停只解除等待；退出必须等待同一 HTTP 会话拥有的真实共享传输停止写入。
            await VerifiedTransfer.StopForExitAsync(http);
            await Task.WhenAll(pending);
            Persist();
        }
        finally { preparingExit = false; }
    }

    public void ClearCompleted()
    {
        items.RemoveAll(x => x.State is "已完成" or "已取消");
        Persist();
    }

    private void Persist()
    {
        InstanceStore.WriteJson(index, items.Select(job =>
        {
            if (portableRoot is null) return job;
            var relative = Path.GetRelativePath(portableRoot, job.Target);
            return !Path.IsPathRooted(relative) && relative.Split(Path.DirectorySeparatorChar)[0] != ".."
                ? job with { Target = relative.Replace('\\', '/') } : job;
        }).ToArray());
        Changed?.Invoke();
    }

    public void SetConcurrency(int value)
    {
        value = Math.Clamp(value, 1, 6);
        InstanceStore.WriteJson(Path.Combine(Path.GetDirectoryName(index)!, "download-concurrency.json"), value);
        concurrency = value;
        VerifiedTransfer.SetConcurrency(http, value);
        Dispatch();
    }

    public void SetSpeedLimit(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
        InstanceStore.WriteJson(Path.Combine(Path.GetDirectoryName(index)!, "download-speed.json"), value);
        SpeedLimitMiB = value;
        VerifiedTransfer.SetSpeedLimit(http, value * 1048576L);
    }

    public void SetPriority(Guid id, bool high)
    {
        var position = items.FindIndex(job => job.Id == id);
        if (position < 0) return;
        items[position] = items[position] with { Priority = high ? 1 : 0 };
        Persist();
        Dispatch();
    }

    private void Dispatch()
    {
        while (running < concurrency && waiting.Count > 0)
        {
            var next = waiting.MaxBy(entry => items.First(job => job.Id == entry.Job.Id).Priority);
            waiting.Remove(next);
            running++;
            next.Ready.SetResult();
        }
    }

    /// <summary>
    /// 等待队列准入，下载并校验暂存文件，提交前再次检查实例是否允许写入。
    /// </summary>
    /// <remarks>任务查找或文件预检异常仍可向调用方传播。队列集合未提供通用并发读写保证；调用方需保持既有调度上下文。底层门控锁不等于整个服务线程安全。</remarks>
    /// <param name="id">已有下载任务标识。</param>
    /// <param name="changed">任务状态更新通知。</param>
    /// <param name="progress">已传输字节和总字节通知。</param>
    /// <param name="mayCommit">提交前的实例状态检查；返回false时保留错误状态。</param>
    /// <returns>提交成功返回true；未执行或已处理的暂停、取消、传输失败返回false。</returns>
    public async Task<bool> RunAsync(Guid id, Action changed, Action<long, long> progress, Func<bool> mayCommit)
    {
        var job = items.Single(x => x.Id == id);
        if (preparingExit || job.State is "已完成" or "已取消" || active.ContainsKey(id)) return false;
        Validate(job.File);
        using var source = new CancellationTokenSource();
        active.Add(id, source);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completions.Add(id, completion);
        var token = source.Token;
        var acquired = false;
        var temporary = job.Target + "." + id.ToString("N") + ".part";
        void Update(string state, string? error = null)
        {
            var position = items.FindIndex(x => x.Id == id);
            items[position] = items[position] with { State = state, Error = error };
            Persist();
            changed();
        }

        try
        {
            Update("等待中");
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entry = (job, ready);
            waiting.Add(entry);
            Dispatch();
            try { await ready.Task.WaitAsync(token); }
            finally
            {
                acquired = ready.Task.IsCompletedSuccessfully;
                waiting.Remove(entry);
            }
            token.ThrowIfCancellationRequested();
            await VerifiedTransfer.StageAsync(http, job.File.Url, temporary, job.File.Size, job.File.Sha512, progress,
                state => Update(state), token);
            token.ThrowIfCancellationRequested();
            if (!mayCommit()) throw new IOException("实例正在运行，请在退出游戏后重试。");
            Update("安装中");
            if (await VerifiedTransfer.MatchesAsync(job.Target, job.File.Size, job.File.Sha512, token))
                VerifiedTransfer.Reset(temporary);
            else File.Move(temporary, job.Target, false);
            VerifiedTransfer.ClearMetadata(temporary);
            Update("已完成");
            return true;
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            if (cancelled.Remove(id)) { VerifiedTransfer.Reset(temporary); Update("已取消"); }
            else Update("已暂停");
        }
        catch (Exception ex) { Update("失败", VerifiedTransfer.Describe(ex)); }
        // 只归还真正获得的队列名额，随后唤醒等待任务；using负责释放当前取消源。
        finally
        {
            if (acquired) running--;
            Dispatch();
            active.Remove(id);
            cancelled.Remove(id);
            completions.Remove(id);
            completion.TrySetResult();
        }
        return false;
    }
}
