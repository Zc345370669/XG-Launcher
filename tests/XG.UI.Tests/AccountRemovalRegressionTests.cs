// SPDX-License-Identifier: MIT
using Microsoft.Identity.Client;
using XG.Core;
using XGLauncher;

internal static class AccountRemovalRegressionTests
{
    internal static async Task RunAsync(string root)
    {
        var location = Path.Combine(root, "account-removal-regression");
        var alice = new TestAccount("Alice"); var bob = new TestAccount("Bob");
        var cache = new List<IAccount> { alice, bob };
        var removals = new List<string>();
        var service = new MicrosoftAccounts(location, (selected, token) =>
        {
            var account = selected ?? alice;
            return Task.FromResult((account, new MinecraftLogin(new MinecraftIdentity(account.Username, Guid.NewGuid(), null), "controlled-token", DateTimeOffset.UtcNow.AddHours(1))));
        }, removeAccount: account => { removals.Add(account.HomeAccountId.Identifier); cache.Remove(account); return Task.CompletedTask; },
        enumerateAccounts: () => Task.FromResult(cache.ToArray()));
        await service.SignInAsync(alice, default); await service.SignInAsync(bob, default);
        var selected = service.SelectedProfile!;
        await service.RemoveProfileAsync(service.Profiles.Single(x => x.Name == "Alice"));
        Check(service.SelectedProfile?.Id == selected.Id && cache.Count == 1 && ReferenceEquals(cache[0], bob) && removals.SequenceEqual(new[] { alice.HomeAccountId.Identifier }), "removal touched the other account cache or selection");
        await service.RemoveProfileAsync(service.SelectedProfile!);
        Check(!service.Profiles.Any() && service.SelectedProfile == null && service.Identity == null && !cache.Any(), "last Microsoft account not removed");
        var restart = new MicrosoftAccounts(location, enumerateAccounts: () => Task.FromResult(cache.ToArray()));
        await restart.ListAsync(default);
        Check(restart.SelectedProfile == null && restart.Profiles.Count == 0, "deleted Microsoft/default account restored after restart");
        try { await restart.CaptureLaunchSessionAsync(); throw new Exception("empty account allowed game launch"); } catch (LoginException) { }
        await restart.AddOfflineAsync("OfflineA"); await restart.AddOfflineAsync("OfflineB");
        await restart.RemoveProfileAsync(restart.SelectedProfile!);
        Check(restart.SelectedProfile?.Name == "OfflineA", "removing selected account failed fallback");
        await restart.RemoveProfileAsync(restart.SelectedProfile!);
        Check(restart.Profiles.Count == 0 && new AccountRegistry(location).Items.Count == 0 && new AccountRegistry(location).Selected == null, "last offline account returned after restart");
        Console.WriteLine("PASS targeted MSAL account eviction, unselected identity preservation, last Microsoft/offline removal, fallback, persistent empty state and no-account launch guard (controlled cache; not live Microsoft)");
    }
    private sealed record TestAccount(string Username) : IAccount
    {
        public AccountId HomeAccountId => new(Username + ".tenant", Username, "tenant");
        public string Environment => "login.microsoftonline.com";
    }
    private static void Check(bool value, string error) { if (!value) throw new Exception(error); }
}
