// SPDX-License-Identifier: MIT
namespace XG.Core;

/// <summary>
/// 将父任务取消与当前文件传输关联，暂停保留部分文件以供继续。
/// </summary>
public sealed class ResourceControl : IDisposable
{
    private readonly CancellationTokenSource lifetime;
    private readonly object sync = new();
    private readonly HashSet<CancellationTokenSource> transfers = [];
    private volatile TaskCompletionSource? resume;
    internal bool Paused => resume != null;
    public CancellationToken Token => lifetime.Token;
    private readonly Action<string> update;

    internal ResourceControl(CancellationToken parent, Action<string> update)
    {
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(parent);
        this.update = update;
    }

    internal void Pause()
    {
        if (resume != null || lifetime.IsCancellationRequested) return;
        resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // 暂停仅中断当前文件传输；lifetime保持有效，因此继续时可以利用保留的部分文件。
        lock (sync) foreach (var transfer in transfers) transfer.Cancel();
        update("已暂停");
    }

    internal void Resume()
    {
        if (resume is not { } signal) return;
        resume = null;
        update("等待中");
        signal.TrySetResult();
    }

    internal void Cancel() => lifetime.Cancel();

    public async Task WaitIfPausedAsync()
    {
        if (resume is { } signal) await signal.Task.WaitAsync(Token);
        Token.ThrowIfCancellationRequested();
    }

    public async Task RunFileAsync(Func<CancellationToken, Task> action)
    {
        while (true)
        {
            if (resume is { } signal) await signal.Task.WaitAsync(Token);
            Token.ThrowIfCancellationRequested();
            using var file = CancellationTokenSource.CreateLinkedTokenSource(Token);
            lock (sync) { transfers.Add(file); if (resume != null) file.Cancel(); }
            try { await action(file.Token); return; }
            catch (OperationCanceledException) when (file.IsCancellationRequested && !Token.IsCancellationRequested)
            { /* 保留磁盘上的部分文件，供继续传输使用。 */ }
            finally { lock (sync) transfers.Remove(file); }
        }
    }

    public void Dispose() => lifetime.Dispose();
}
