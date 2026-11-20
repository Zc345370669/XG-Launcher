// SPDX-License-Identifier: MIT
// 仅供 CI 的实际 Windows 进程测试；不进入发行 ZIP 或生产 EXE。
if (args.Length == 3 && args[0] == "--hold-parent")
{
    await File.WriteAllTextAsync(args[1], "Ready");
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    while (!File.Exists(args[2])) await Task.Delay(25, deadline.Token);
    return;
}
await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "launched.marker"), "Started");
