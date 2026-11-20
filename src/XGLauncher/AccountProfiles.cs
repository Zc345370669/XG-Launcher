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

    internal async Task RemoveProfileAsync(LauncherAccount profile, CancellationToken token = default)
    {
        var wasSelected = SelectedProfile?.Id == profile.Id;
        if (profile.Kind == "Microsoft")
        {
            var accounts = await ListAsync(token);
            var cached = accounts.FirstOrDefault(x => x.HomeAccountId.Identifier == profile.MicrosoftId);
            if (cached != null) await RemoveAsync(cached, token);
            else { await gate.WaitAsync(token); try { registry.Remove(profile.Id); if (wasSelected) Deselect(); Changed?.Invoke(); } finally { gate.Release(); } }
        }
        else
        {
            await gate.WaitAsync(token);
            try { registry.Remove(profile.Id); selectionRevision++; Changed?.Invoke(); }
            finally { gate.Release(); }
        }
        if (wasSelected)
        {
            // 优先接续可立即使用的离线账号；接续微软账号仍必须完成真实验证。
            var remaining = Profiles.OrderBy(x => x.Kind == "Offline" ? 0 : 1).FirstOrDefault();
            if (remaining != null) await SelectProfileAsync(remaining, token);
        }
    }
}
