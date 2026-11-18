// SPDX-License-Identifier: MIT
namespace XG.Core;

public sealed class ResourceControl : IDisposable
{
    private readonly CancellationTokenSource lifetime;
    private CancellationTokenSource? transfer;
    private TaskCompletionSource? resume;
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
        transfer?.Cancel();
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
            transfer = file;
            try { await action(file.Token); return; }
            catch (OperationCanceledException) when (file.IsCancellationRequested && !Token.IsCancellationRequested)
            { /* The partial file stays on disk for the next transfer. */ }
            finally { transfer = null; }
        }
    }

    public void Dispose() => lifetime.Dispose();
}
