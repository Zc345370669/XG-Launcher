// SPDX-License-Identifier: MIT
using System.Text.Json;
using XG.Core;

namespace XGLauncher;

internal sealed record LauncherAccount(string Id, string Name, string Kind, string? MicrosoftId = null)
{
    internal string Caption => Name + (Kind == "Microsoft" ? " · 微软账号" : " · 离线账号");
}

// 仅存账号元数据；微软凭据仍由既有MSAL安全缓存保存。
/// <summary>
/// 持久化离线和微软账号元数据及当前选择，不在此索引中保存认证令牌。
/// </summary>
internal sealed class AccountRegistry
{
    private sealed record State(List<LauncherAccount> Accounts, string? Selected);
    private readonly string path;
    private State state = new([], null);
    internal IReadOnlyList<LauncherAccount> Items => state.Accounts.AsReadOnly();
    internal LauncherAccount? Selected => state.Accounts.FirstOrDefault(x => x.Id == state.Selected);
    internal AccountRegistry(string root)
    {
        Directory.CreateDirectory(root);
        path = Path.Combine(root, "accounts.json");
        if (File.Exists(path))
        {
            // 损坏账号索引必须显式报错，不能静默写入空列表覆盖用户数据。
            state = JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? throw new InvalidDataException("账号列表为空或损坏。");
            if (state.Accounts == null || state.Accounts.Any(x => string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Name) || x.Kind is not ("Offline" or "Microsoft")))
                throw new InvalidDataException("账号列表格式无效。");
        }
        else
        {
            // 迁移旧单离线玩家记录，不凭空创建一个默认账号。
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "selection.json")));
                if (doc.RootElement.TryGetProperty("Player", out var name) && name.GetString() is { } player)
                    AddOffline(Instance.ValidatePlayer(player));
            }
            catch (Exception ex) when (ex is IOException or JsonException or ArgumentException) { }
            if (!File.Exists(path)) Commit(state);
        }
    }
    internal LauncherAccount AddOffline(string name)
    {
        name = Instance.ValidatePlayer(name);
        var entry = state.Accounts.FirstOrDefault(x => x.Kind == "Offline" && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? new LauncherAccount("offline:" + Guid.NewGuid().ToString("N"), name, "Offline");
        Upsert(entry, true); return entry;
    }
    internal LauncherAccount AddMicrosoft(string id, string name, bool select)
    {
        var entry = new LauncherAccount("microsoft:" + id, name, "Microsoft", id);
        Upsert(entry, select); return entry;
    }
    private void Upsert(LauncherAccount entry, bool select)
    {
        var entries = state.Accounts.Where(x => x.Id != entry.Id).Append(entry).ToList();
        Commit(new(entries, select ? entry.Id : state.Selected));
    }
    internal void Deselect() => Commit(state with { Selected = null });
    internal void Select(string id)
    {
        if (!state.Accounts.Any(x => x.Id == id)) throw new InvalidOperationException("账号已被移除。");
        Commit(state with { Selected = id });
    }
    internal void Remove(string id) => Commit(new(state.Accounts.Where(x => x.Id != id).ToList(), state.Selected == id ? null : state.Selected));
    internal void ClearMicrosoft() => Commit(new(state.Accounts.Where(x => x.Kind != "Microsoft").ToList(), Selected?.Kind == "Microsoft" ? null : state.Selected));
    private void Commit(State value) { InstanceStore.WriteJson(path, value); state = value; }
}
