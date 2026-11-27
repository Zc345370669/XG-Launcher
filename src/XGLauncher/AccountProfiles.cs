// SPDX-License-Identifier: MIT
using Microsoft.Identity.Client;

namespace XGLauncher;

internal sealed partial class MicrosoftAccounts
{
    private readonly AccountRegistry registry = new(dataRoot);
    internal IReadOnlyList<LauncherAccount> Profiles => registry.Items;
    internal LauncherAccount? SelectedProfile => registry.Selected;
    internal event Action? Changed;

    internal async Task AddOfflineAsync(string name)
    {
        await gate.WaitAsync();
        try { registry.AddOffline(name); Deselect(); selectionRevision++; Changed?.Invoke(); }
        finally { gate.Release(); }
    }

    internal async Task SelectProfileAsync(LauncherAccount profile, CancellationToken token = default)
    {
        if (!Profiles.Any(x => x.Id == profile.Id)) throw new InvalidOperationException("账号已被移除。");
        if (profile.Kind == "Microsoft")
        {
            var accounts = await ListAsync(token);
            var cached = accounts.FirstOrDefault(x => x.HomeAccountId.Identifier == profile.MicrosoftId)
                ?? throw new InvalidOperationException("此微软账号的本地凭据不可用，请通过添加账号重新登录。");
            await SignInAsync(cached, token);
            return;
        }
        await gate.WaitAsync(token);
        try { registry.Select(profile.Id); Deselect(); selectionRevision++; Changed?.Invoke(); }
        finally { gate.Release(); }
    }

    /// <summary>在账号门控内按 HomeAccountId 清理单个 MSAL 账号，不删除共享缓存文件。</summary>
    internal async Task RemoveProfileAsync(LauncherAccount profile, CancellationToken token = default)
    {
        var wasSelected = false;
        await gate.WaitAsync(token);
        try
        {
            if (!Profiles.Any(x => x.Id == profile.Id)) return;
            wasSelected = SelectedProfile?.Id == profile.Id;
            if (profile.Kind == "Microsoft")
            {
                var app = enumerateAccounts == null ? await GetApplicationAsync() : null;
                var cached = (enumerateAccounts != null ? await enumerateAccounts() : (await app!.GetAccountsAsync()).ToArray())
                    .FirstOrDefault(x => x.HomeAccountId.Identifier == profile.MicrosoftId);
                if (cached != null)
                {
                    token.ThrowIfCancellationRequested();
                    if (removeAccount != null) await removeAccount(cached);
                    else await app!.RemoveAsync(cached);
                }
            }
            registry.Remove(profile.Id);
            if (wasSelected) { Deselect(); selectionRevision++; }
            Changed?.Invoke();
        }
        finally { gate.Release(); }
        if (wasSelected && SelectedProfile == null)
        {
            // 删除最后一个账号时保持空状态；其余微软账号仍需通过既有真实验证才能接续。
            Exception? last = null;
            foreach (var candidate in Profiles.OrderBy(x => x.Kind == "Offline" ? 0 : 1).ToArray())
            {
                if (SelectedProfile != null) break;
                try { await SelectProfileAsync(candidate, token); return; }
                catch (Exception error) when (error is not OperationCanceledException) { last = error; }
            }
            if (last != null) throw new InvalidOperationException("账号已移除；其余账号暂时无法验证，请重新选择或登录。", last);
        }
    }
}
