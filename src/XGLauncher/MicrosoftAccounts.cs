// SPDX-License-Identifier: MIT
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using CmlLib.Core.Auth;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using XG.Core;

namespace XGLauncher;

internal sealed class MicrosoftAccounts(string dataRoot,
    Func<IAccount?, CancellationToken, Task<(IAccount Account, MinecraftLogin Login)>>? authenticate = null,
    TimeProvider? time = null, Func<IAccount, Task>? removeAccount = null)
{
    internal const string RedirectUri = "http://localhost";
    private static readonly string[] Scopes = ["XboxLive.signin"];
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly MinecraftAuthentication Minecraft = new(Http);
    private readonly SemaphoreSlim gate = new(1);
    private IPublicClientApplication? application;
    private MsalCacheHelper? cache;
    private (IAccount Account, MinecraftLogin Login)? current;
    private long selectionRevision;
    public MinecraftIdentity? Identity => current?.Login.Identity;
    public string? LastDiagnostic { get; private set; }
    private string CachePath => Path.Combine(dataRoot, "microsoft-msal.bin");

    internal static string? ClientId => NormalizeClientId(
        typeof(MicrosoftAccounts).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "XGMicrosoftClientId")?.Value);

    internal static string? NormalizeClientId(string? value) =>
        Guid.TryParseExact(value?.Trim(), "D", out var id) && id != Guid.Empty ? id.ToString("D") : null;

    public async Task<IAccount[]> ListAsync(CancellationToken cancellationToken)
    {
        if (ClientId is null) return [];
        await gate.WaitAsync(cancellationToken);
        try
        {
            var app = await GetApplicationAsync();
            return (await app.GetAccountsAsync()).ToArray();
        }
        finally { gate.Release(); }
    }

    public async Task<MinecraftIdentity> SignInAsync(IAccount? account, CancellationToken cancellationToken,
        AuthDiagnosticTrace? diagnostic = null)
    {
        if (!await gate.WaitAsync(0, cancellationToken))
        {
            diagnostic?.Record(LoginStage.Microsoft, AuthStepState.Failed);
            throw new LoginException(LoginStage.Microsoft, "另一个账号操作仍在进行，请稍后重试。");
        }
        var trace = diagnostic ?? new AuthDiagnosticTrace();
        try
        {
            var verified = await VerifyAccountAsync(account, cancellationToken, trace);
            cancellationToken.ThrowIfCancellationRequested();
            trace.Record(LoginStage.Cache, AuthStepState.Started);
            current = verified;
            selectionRevision++;
            trace.Record(LoginStage.Cache, AuthStepState.Passed);
            return verified.Login.Identity;
        }
        catch (LoginException error)
        {
            trace.Record(error.Stage, AuthStepState.Failed);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            trace.Record(LoginStage.Microsoft, AuthStepState.Cancelled);
            throw;
        }
        catch
        {
            trace.Record(LoginStage.Microsoft, AuthStepState.Failed);
            throw;
        }
        finally
        {
            try { await SaveDiagnosticAsync(trace); }
            finally { gate.Release(); }
        }
    }

    private async Task SaveDiagnosticAsync(AuthDiagnosticTrace trace)
    {
        trace.Finish(LoginStage.Browser, LoginStage.Callback, LoginStage.Microsoft, LoginStage.Xbox,
            LoginStage.Xsts, LoginStage.Minecraft, LoginStage.Ownership, LoginStage.Profile, LoginStage.Cache, LoginStage.Launch);
        var report = trace.ToString();
        LastDiagnostic = report;
        var path = Path.Combine(dataRoot, "oauth-diagnostic.txt");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, report);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporary); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
        }
    }

    private async Task<(IAccount Account, MinecraftLogin Login)> VerifyAccountAsync(IAccount? account, CancellationToken cancellationToken, AuthDiagnosticTrace trace)
    {
        if (authenticate != null) return await authenticate(account, cancellationToken);

        trace.Record(LoginStage.Configuration, AuthStepState.Started);
        var app = await GetApplicationAsync();
        trace.Record(LoginStage.Configuration, AuthStepState.Passed);
        trace.Record(LoginStage.Microsoft, AuthStepState.Started);
        var knownAccounts = account is null
            ? (await app.GetAccountsAsync()).Select(item => item.HomeAccountId.Identifier).ToHashSet(StringComparer.Ordinal)
            : null;
        AuthenticationResult result;
        using (var authorization = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            authorization.CancelAfter(account is null ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(45));
            try
            {
                result = account is null
                    ? await RequestBrowserAuthorization(app, async destination =>
                    {
                        trace.Record(LoginStage.Browser, AuthStepState.Started);
                        await OpenBrowserAsync(destination);
                        trace.Record(LoginStage.Browser, AuthStepState.Passed);
                        trace.Record(LoginStage.Callback, AuthStepState.Started);
                    }, authorization.Token)
                    : await app.AcquireTokenSilent(Scopes, account).ExecuteAsync(authorization.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (account is null) trace.Record(LoginStage.Callback, AuthStepState.Failed);
                throw new LoginException(LoginStage.Microsoft, account is null
                    ? "等待微软授权回调超时。请重新登录并保持默认浏览器打开。"
                    : "刷新微软凭据超时，请检查网络后重试。");
            }
        }
        if (account is null) trace.Record(LoginStage.Callback, AuthStepState.Passed);
        trace.Record(LoginStage.Microsoft, AuthStepState.Passed);
        try
        {
            MinecraftLogin login;
            try { login = await Minecraft.ExchangeAsync(result.AccessToken, cancellationToken, trace); }
            catch (LoginException error) when (account != null && error.Stage == LoginStage.Xbox &&
                error.Status == HttpStatusCode.Unauthorized)
            {
                trace.Record(LoginStage.Refresh, AuthStepState.Started);
                using var refresh = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                refresh.CancelAfter(TimeSpan.FromSeconds(45));
                result = await app.AcquireTokenSilent(Scopes, account).WithForceRefresh(true).ExecuteAsync(refresh.Token);
                trace.Record(LoginStage.Refresh, AuthStepState.Passed);
                login = await Minecraft.ExchangeAsync(result.AccessToken, cancellationToken, trace);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return (result.Account, login);
        }
        catch
        {
            if (knownAccounts != null && !knownAccounts.Contains(result.Account.HomeAccountId.Identifier))
            {
                try { await app.RemoveAsync(result.Account); }
                catch (Exception ex) when (ex is MsalException or IOException or UnauthorizedAccessException)
                {
                    // Preserve the original service stage, status, and explanation.
                    trace.Record(LoginStage.Cache, AuthStepState.Failed);
                }
            }
            throw;
        }
    }

    public async Task<Func<Task<MSession?>>> CaptureLaunchSessionAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await gate.WaitAsync(timeout.Token);
            try
            {
                var revision = selectionRevision;
                return () => GetGameSessionAsync(revision);
            }
            finally { gate.Release(); }
        }
        catch (OperationCanceledException)
        { throw new LoginException(LoginStage.Microsoft, "账号操作仍在进行，请完成后重新启动游戏。"); }
    }

    public async Task<MSession?> GetGameSessionAsync(long? expectedRevision = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            await gate.WaitAsync(timeout.Token);
            try
            {
                if (expectedRevision.HasValue && expectedRevision.Value != selectionRevision)
                    throw new LoginException(LoginStage.Microsoft, "准备游戏期间账号已更换，请重新点击启动。");
                if (current is not { } selected) return null;
                if (selected.Login.Expires <= (time ?? TimeProvider.System).GetUtcNow().AddMinutes(5))
                {
                    var trace = new AuthDiagnosticTrace();
                    trace.Record(LoginStage.Refresh, AuthStepState.Started);
                    try
                    {
                        var refreshed = await VerifyAccountAsync(selected.Account, timeout.Token, trace);
                        if (refreshed.Login.Identity.Id != selected.Login.Identity.Id)
                            throw new LoginException(LoginStage.Profile, "刷新后的 Minecraft 档案与所选账号不一致，请重新登录。");
                        current = selected = refreshed;
                        trace.Record(LoginStage.Refresh, AuthStepState.Passed);
                    }
                    catch (LoginException failure)
                    {
                        trace.Record(failure.Stage, AuthStepState.Failed);
                        trace.Record(LoginStage.Refresh, AuthStepState.Failed);
                        throw;
                    }
                    catch
                    {
                        trace.Record(LoginStage.Refresh, AuthStepState.Failed);
                        throw;
                    }
                    finally { await SaveDiagnosticAsync(trace); }
                }
                return new MSession(selected.Login.Identity.Name, selected.Login.AccessToken, selected.Login.Identity.Id.ToString("N")) { UserType = "msa" };
            }
            finally { gate.Release(); }
        }
        catch (LoginException) { throw; }
        catch (Exception ex) { throw new LoginException(LoginStage.Minecraft, Describe(ex)); }
    }

    public async Task UseOfflineAsync()
    {
        await gate.WaitAsync();
        try { Deselect(); }
        finally { gate.Release(); }
    }

    public async Task RemoveAsync(IAccount account, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (removeAccount != null) await removeAccount(account);
            else
            {
                var app = await GetApplicationAsync();
                var cached = (await app.GetAccountsAsync()).FirstOrDefault(item =>
                    item.HomeAccountId.Identifier == account.HomeAccountId.Identifier);
                if (cached != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await app.RemoveAsync(cached);
                }
            }
            if (current?.Account.HomeAccountId.Identifier == account.HomeAccountId.Identifier)
                Deselect();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or MsalException)
        {
            throw new LoginException(LoginStage.Cache, "无法移除所选账号，请检查本地加密缓存权限。");
        }
        finally { gate.Release(); }
    }

    public async Task ClearAsync()
    {
        await gate.WaitAsync();
        try
        {
            File.Delete(CachePath);
            if (application is { } app)
            {
                cache?.UnregisterCache(app.UserTokenCache);
                cache = null;
                application = null;
            }
            Deselect();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LoginException(LoginStage.Cache, "本地微软缓存删除失败，请检查启动器数据目录权限后重试。");
        }
        finally { gate.Release(); }
    }

    private void Deselect()
    {
        if (current is null) return;
        current = null;
        selectionRevision++;
    }

    private async Task<IPublicClientApplication> GetApplicationAsync()
    {
        if (application is { } existing) return existing;
        var id = ClientId ?? throw new LoginException(LoginStage.Configuration,
            "此版本尚未配置开发者的微软应用 ID。离线账号仍可使用。");
        var app = CreatePublicClient(id);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var helper = await MsalCacheHelper.CreateAsync(
                    new StorageCreationPropertiesBuilder(Path.GetFileName(CachePath), dataRoot).Build());
                helper.VerifyPersistence();
                helper.RegisterCache(app.UserTokenCache);
                cache = helper;
            }
            catch (Exception)
            {
                throw new LoginException(LoginStage.Cache,
                    "无法访问 Windows 加密账号缓存。请检查数据目录权限，或在设置中清除本地缓存后重试。");
            }
        }
        return application = app;
    }

    internal static IPublicClientApplication CreatePublicClient(string clientId, IMsalHttpClientFactory? httpFactory = null)
    {
        var builder = PublicClientApplicationBuilder.Create(clientId).WithTenantId("consumers")
            .WithRedirectUri(RedirectUri);
        if (httpFactory != null) builder.WithHttpClientFactory(httpFactory);
        return builder.Build();
    }

    internal static Task<AuthenticationResult> RequestBrowserAuthorization(IPublicClientApplication application,
        Func<Uri, Task> openBrowser, CancellationToken cancellationToken)
    {
        return AuthorizeAsync();
        async Task<AuthenticationResult> AuthorizeAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            OAuthCompletionPage page;
            try { page = new OAuthCompletionPage(); }
            catch (Exception error) when (error is System.Net.Sockets.SocketException or IOException)
            { throw new LoginException(LoginStage.Callback, "无法创建本地授权完成页面，请重试登录。"); }
            try
            {
                var result = await application.AcquireTokenInteractive(Scopes).WithPrompt(Prompt.SelectAccount)
                    .WithUseEmbeddedWebView(false)
                    .WithSystemWebViewOptions(new SystemWebViewOptions
                    {
                        OpenBrowserAsync = openBrowser,
                        // MSAL 4.61.3 owns the callback and emits 302. The separate
                        // presentation server follows with 303 to a clean page.
                        BrowserRedirectSuccess = page.Arrival,
                        BrowserRedirectError = page.Arrival,
                        HtmlMessageError = "Authorization ended. Return to XG Launcher."
                    }).ExecuteAsync(cancellationToken);
                page.CompleteAuthorization(true);
                return result;
            }
            catch { page.CompleteAuthorization(false); throw; }
            finally { page.ExpireSoon(); }
        }
    }

    private static Task OpenBrowserAsync(Uri address)
    {
        try
        {
            using var launched = Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
            return Task.CompletedTask;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            throw new LoginException(LoginStage.Browser, "无法打开系统默认浏览器，请检查 Windows 默认浏览器设置后重试。");
        }
    }

    internal static string Describe(Exception error) => error switch
    {
        LoginException { Stage: LoginStage.Minecraft, Status: HttpStatusCode.Forbidden } safe =>
            $"Minecraft 账号认证失败。\n微软账号和 Xbox 身份验证已经成功，但 Minecraft Services 拒绝了登录请求。\nHTTP 状态：403\n{new GameLogSanitizer(null).Redact(safe.Message)}\n请复制脱敏诊断，或重试登录。",
        LoginException safe => new GameLogSanitizer(null).Redact(safe.Message),
        MsalUiRequiredException => "微软凭据已失效或需要再次授权，请重新使用浏览器登录。",
        MsalException { ErrorCode: "access_denied" or "user_cancelled" or "authentication_canceled" } =>
            "微软登录已取消或授权被拒绝。",
        MsalException { ErrorCode: "invalid_client" or "unauthorized_client" or "invalid_scope" or "redirect_uri_mismatch" } =>
            "微软应用注册或权限配置有误，请联系启动器开发者。",
        MsalException { ErrorCode: "loopback_redirect_uri" or "http_listener_error" or "no_browser_response" } =>
            "无法接收浏览器授权回调，请检查本机回环连接并重试。",
        MsalException { InnerException: LoginException browser } => new GameLogSanitizer(null).Redact(browser.Message),
        MsalException { InnerException: HttpRequestException } or HttpRequestException or IOException or OperationCanceledException =>
            "登录服务连接中断或超时，请检查网络后重试。",
        _ => "微软登录未完成，请重新尝试。"
    };
}
