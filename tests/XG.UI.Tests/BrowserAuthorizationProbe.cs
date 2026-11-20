// SPDX-License-Identifier: MIT
using System.Net;
using Microsoft.Identity.Client;
using XGLauncher;

internal static class BrowserAuthorizationProbe
{
    internal static async Task RunAsync()
    {
        const string expectedClientId = "dbccdda9-9c7d-4435-9309-1e84eb908462";
        if (MicrosoftAccounts.ClientId != expectedClientId)
            throw new InvalidOperationException("This probe is for the configured phase 7 build.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var application = MicrosoftAccounts.CreatePublicClient(expectedClientId);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            { Timeout = TimeSpan.FromSeconds(15) };
        var observed = false;
        try
        {
            await MicrosoftAccounts.RequestBrowserAuthorization(application, async address =>
            {
                var query = ParseQuery(address);
                var redirect = new Uri(query.GetValueOrDefault("redirect_uri") ?? "");
                var scope = (query.GetValueOrDefault("scope") ?? "").Split(' ');
                if (address.Scheme != "https" || address.Host != "login.microsoftonline.com" ||
                    redirect.Scheme != "http" || redirect.Host != "localhost" || redirect.IsDefaultPort ||
                    query.GetValueOrDefault("client_id") != expectedClientId ||
                    query.GetValueOrDefault("response_type") != "code" ||
                    query.GetValueOrDefault("code_challenge_method") != "S256" ||
                    string.IsNullOrEmpty(query.GetValueOrDefault("code_challenge")) ||
                    string.IsNullOrEmpty(query.GetValueOrDefault("state")) ||
                    !scope.Contains("XboxLive.signin", StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException("MSAL authorization request does not match the configured public client.");
                Console.WriteLine("PASS live MSAL generated consumers authorization URL, configured client ID, temporary localhost callback, code and S256 PKCE");

                using var response = await http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                Console.WriteLine($"LIVE unauthenticated Microsoft authorization endpoint HTTP {(int)response.StatusCode}");
                if (response.Headers.Location is { IsAbsoluteUri: true } location)
                    Console.WriteLine($"LIVE redirect host: {location.IdnHost}");
                if ((int)response.StatusCode is < 200 or >= 400)
                    throw new InvalidOperationException("The public authorization endpoint did not accept the unauthenticated request.");
                observed = true;
                await timeout.CancelAsync();
            }, timeout.Token);
            throw new InvalidOperationException("A token was not expected in an unauthenticated probe.");
        }
        catch (OperationCanceledException) when (observed)
        {
            Console.WriteLine("PASS cancelled while waiting for callback; no user credentials, code or tokens submitted");
        }
        catch (MsalException error)
        {
            // Do not expose MSAL exception messages, URLs, state or response bodies.
            Console.WriteLine($"Live MSAL probe did not complete: {error.GetType().Name}, {SafeCode(error.ErrorCode)}");
            Environment.ExitCode = 1;
        }
        catch (Exception error)
        {
            Console.WriteLine($"Live MSAL probe did not complete: {error.GetType().Name}");
            Environment.ExitCode = 1;
        }

        await VerifyLocalDenialAsync(expectedClientId);

        try
        {
            using var response = await http.PostAsync("https://api.minecraftservices.com/authentication/login_with_xbox",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            Console.WriteLine($"LIVE Minecraft session POST with no credentials: HTTP {(int)response.StatusCode} (reachability only)");
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            Console.WriteLine($"LIVE Minecraft session POST with no credentials: {error.GetType().Name} (reachability only)");
        }

        foreach (var (stage, address) in new[]
        {
            ("Xbox Live", "https://user.auth.xboxlive.com/user/authenticate"),
            ("XSTS", "https://xsts.auth.xboxlive.com/xsts/authorize"),
            ("Minecraft ownership", "https://api.minecraftservices.com/entitlements/mcstore"),
            ("Minecraft profile", "https://api.minecraftservices.com/minecraft/profile")
        })
        {
            try
            {
                using var response = await http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead);
                Console.WriteLine($"LIVE unauthenticated GET {stage}: HTTP {(int)response.StatusCode} (reachability only)");
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
            {
                Console.WriteLine($"LIVE unauthenticated GET {stage}: {error.GetType().Name} (reachability only)");
            }
        }
    }

    private static async Task VerifyLocalDenialAsync(string clientId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var local = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false });
        Task? callbackRequest = null;
        try
        {
            await MicrosoftAccounts.RequestBrowserAuthorization(MicrosoftAccounts.CreatePublicClient(clientId), address =>
            {
                var query = ParseQuery(address);
                var redirect = new Uri(query["redirect_uri"]);
                if (redirect.Host != "localhost" || redirect.Scheme != "http" || redirect.IsDefaultPort)
                    throw new InvalidOperationException("Unexpected callback origin.");
                var callback = new UriBuilder(redirect)
                {
                    Query = "error=access_denied&state=" + Uri.EscapeDataString(query["state"])
                }.Uri;
                // A real browser is started without waiting for it to navigate.
                // Allow MSAL to arm its listener before delivering our synthetic denial.
                callbackRequest = SubmitAsync(callback);
                return Task.CompletedTask;
            }, timeout.Token);
            throw new InvalidOperationException("A denial must not produce a token.");
        }
        catch (MsalException error) when (callbackRequest != null &&
            error.ErrorCode is "access_denied" or "authentication_canceled")
        {
            await callbackRequest;
            Console.WriteLine("PASS real MSAL loopback listener consumed a synthetic local access_denied callback; no authorization code or token exchanged");
        }
        catch (Exception error)
        {
            var callbackFailure = callbackRequest?.Exception?.GetBaseException();
            var socket = (callbackFailure?.InnerException as System.Net.Sockets.SocketException)?.SocketErrorCode.ToString();
            Console.WriteLine($"Local synthetic denial did not complete: {error.GetType().Name}, socket result {socket ?? "unavailable"}");
            Environment.ExitCode = 1;
        }

        async Task SubmitAsync(Uri callback)
        {
            await Task.Delay(150, timeout.Token);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    using var reply = await local.GetAsync(callback, timeout.Token);
                    if (reply.StatusCode != HttpStatusCode.Found || reply.Headers.Location is not { IsLoopback: true } clean || clean.Query.Length != 0)
                        throw new InvalidOperationException("The loopback listener did not issue a clean redirect.");
                    using var bridge = await local.GetAsync(clean, timeout.Token);
                    if (bridge.StatusCode != HttpStatusCode.SeeOther || bridge.Headers.Location?.AbsolutePath != "/auth/complete")
                        throw new InvalidOperationException("The completion page did not issue 303.");
                    using var final = await local.GetAsync(bridge.Headers.Location, timeout.Token);
                    if (!final.IsSuccessStatusCode || final.Headers.CacheControl?.NoStore != true)
                        throw new InvalidOperationException("The completion page was not safely served.");
                    return;
                }
                catch (HttpRequestException error) when (attempt < 10 &&
                    error.InnerException is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused })
                {
                    await Task.Delay(200, timeout.Token);
                }
            }
        }
    }

    private static string SafeCode(string value) => value.Length <= 70 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-') ? value : "redacted";

    private static Dictionary<string, string> ParseQuery(Uri address) => address.Query.TrimStart('?').Split('&')
        .Select(parameter => parameter.Split('=', 2))
        .Where(pair => pair.Length == 2)
        .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));
}
