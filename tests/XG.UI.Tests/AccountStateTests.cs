// SPDX-License-Identifier: MIT
using Microsoft.Identity.Client;
using XG.Core;
using XGLauncher;

internal static class AccountStateTests
{
    internal static async Task RunAsync(string root)
    {
        var clock = new ManualTime();
        var alice = new FixtureAccount("Alice");
        var bob = new FixtureAccount("Bob");
        var players = new Dictionary<IAccount, MinecraftIdentity>
        {
            [alice] = new("Alice", Guid.NewGuid(), null),
            [bob] = new("Bob", Guid.NewGuid(), null)
        };
        var removed = new List<IAccount>();
        var renewals = 0;
        var fail = false;
        var deny = false;
        var mismatch = false;
        var accounts = new MicrosoftAccounts(root, (account, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deny) throw new LoginException(LoginStage.Minecraft, "模拟服务拒绝", System.Net.HttpStatusCode.Forbidden);
            if (fail) throw new MsalUiRequiredException("invalid_grant", "private server diagnostic");
            var chosen = account ?? alice;
            var game = new MinecraftLogin(players[mismatch ? alice : chosen], $"fixture-{++renewals}",
                clock.GetUtcNow().AddMinutes(15));
            return Task.FromResult((chosen, game));
        }, clock, account => { removed.Add(account); return Task.CompletedTask; });

        Check((await accounts.SignInAsync(null, default)).Name == "Alice");
        Check((await accounts.GetGameSessionAsync()) is { AccessToken: "fixture-1", UserType: "msa" } && renewals == 1);
        deny = true;
        try
        {
            await accounts.SignInAsync(bob, default);
            throw new InvalidOperationException("A denied session was selected.");
        }
        catch (LoginException error) when (error.Status == System.Net.HttpStatusCode.Forbidden) { }
        deny = false;
        Check(accounts.Identity?.Name == "Alice");
        var report = await File.ReadAllTextAsync(Path.Combine(root, "oauth-diagnostic.txt"));
        Check(report == accounts.LastDiagnostic && report.Contains("Minecraft  Failed") &&
            report.Contains("Ownership  Skipped") && report.Contains("Launch  Skipped") &&
            !report.Contains("Alice") && !report.Contains("fixture-1"));
        Console.WriteLine("PASS simulated denied login leaves the existing account selected; persisted and copyable diagnostics contain no user or token");
        var cancelled = new CancellationToken(true);
        try
        {
            await accounts.SignInAsync(bob, cancelled);
            throw new Exception("Cancelled switch unexpectedly succeeded.");
        }
        catch (OperationCanceledException) { }
        Check(accounts.Identity?.Name == "Alice");
        fail = true;
        try
        {
            await accounts.SignInAsync(bob, default);
            throw new Exception("Failed switch unexpectedly succeeded.");
        }
        catch (MsalUiRequiredException) { }
        Check(accounts.Identity?.Name == "Alice");
        fail = false;
        Check((await accounts.SignInAsync(bob, default)).Name == "Bob");
        clock.Now += TimeSpan.FromMinutes(14);
        Check((await accounts.GetGameSessionAsync())?.AccessToken == "fixture-3");
        clock.Now += TimeSpan.FromMinutes(14);
        mismatch = true;
        try
        {
            await accounts.GetGameSessionAsync();
            throw new Exception("Mismatched refresh unexpectedly succeeded.");
        }
        catch (LoginException error) when (error.Stage == LoginStage.Profile) { }
        Check(accounts.Identity?.Name == "Bob");
        fail = true;
        try
        {
            await accounts.GetGameSessionAsync();
            throw new Exception("Expired Microsoft credential unexpectedly succeeded.");
        }
        catch (LoginException error)
        {
            Check(error.Message.Contains("重新") && !error.Message.Contains("private"));
        }

        var plannedGame = await accounts.CaptureLaunchSessionAsync();
        await accounts.RemoveAsync(alice, default);
        Check(accounts.Identity?.Name == "Bob" && ReferenceEquals(removed[0], alice));
        await accounts.RemoveAsync(bob, default);
        Check(accounts.Identity is null && ReferenceEquals(removed[1], bob));
        try
        {
            await plannedGame();
            throw new InvalidOperationException("An obsolete account reached game launch.");
        }
        catch (LoginException error) when (error.Stage == LoginStage.Microsoft) { }

        using var cancel = new CancellationTokenSource();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serialized = new MicrosoftAccounts(root, async (_, ct) =>
        {
            waiting.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException();
        });
        var active = serialized.SignInAsync(null, cancel.Token);
        await waiting.Task;
        try
        {
            await serialized.SignInAsync(null, default);
            throw new InvalidOperationException("Duplicate browser login was allowed.");
        }
        catch (LoginException error) when (error.Stage == LoginStage.Microsoft) { }
        cancel.Cancel();
        try { await active; }
        catch (OperationCanceledException) { }

        await accounts.UseOfflineAsync();
        Check(await accounts.GetGameSessionAsync() is null);
        Console.WriteLine("PASS simulated account switching, cancellation, refresh, expired credentials, identity mismatch and offline fallback");
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new Exception("Account state assertion failed.");
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record FixtureAccount(string Username) : IAccount
    {
        public AccountId HomeAccountId { get; } = new($"{Username}.tenant", Username, "tenant");
        public string Environment => "login.microsoftonline.com";
    }
}
