// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using XG.Core;
using XGLauncher;

if (args.Length is 2 or 3 && args[0] == "--audit-embedded") { await EmbeddedPublishAudit.RunAsync(args[1], args.Length == 3 ? args[2] : null); return; }

if (args.Contains("--oauth-probe", StringComparer.Ordinal))
{
    await BrowserAuthorizationProbe.RunAsync();
    return;
}

var updateFixtureRoot = Path.Combine(Path.GetTempPath(), "xg-update-test-" + Guid.NewGuid().ToString("N"));
try { Directory.CreateDirectory(updateFixtureRoot); await GitHubUpdateTests.RunAsync(updateFixtureRoot); }
finally { Directory.Delete(updateFixtureRoot, true); }
await OAuthSecurityTests.RunAsync();
Localizer.Set("en-US");
var forbiddenMessage = Localizer.Text(MicrosoftAccounts.Describe(new LoginException(LoginStage.Minecraft,
    "Minecraft 会话（HTTP 403）：服务拒绝访问。响应未确认应用注册被拒，请复制诊断信息以便核查。", System.Net.HttpStatusCode.Forbidden)));
if (System.Text.RegularExpressions.Regex.IsMatch(forbiddenMessage, "[\\u4e00-\\u9fff]"))
    throw new Exception("Authentication failure guidance was not localized.");
Localizer.Set("zh-CN");
Console.WriteLine("PASS auth13.1 localized Minecraft Services 403 guidance and diagnostic actions");

var temp = Path.Combine(Path.GetTempPath(), "xg-ui-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    if (args.Contains("--dual-mode-only")) { DualModeUiTests.Run(temp, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS")); return; }
    if (args.Contains("--compact-final-only")) { CompactFinalUiTests.Run(temp, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS")); return; }
    if (args.Contains("--window-chrome-only")) { WindowChromeUiTests.Run(temp, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS")); return; }
    if (args.Contains("--adaptive-only")) { AdaptiveComboUiTests.Run(temp, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS")); return; }
    if (args.Contains("--global-ui-only")) { GlobalUiFixTests.Run(temp, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS")); return; }
    if (args.Contains("--rail-baseline")) { StageRailAlignmentTests.Baseline(temp, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS")); return; }
    if (args.Contains("--stage-rail-only")) { StageRailAlignmentTests.Run(temp, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS")); StageNodesFinalTests.Run(temp, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS")); return; }
    LucideUiTests.Run(Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS"));
    if (CatalogIcons.Decode([]) != null || CatalogIcons.Decode("invalid-icon"u8.ToArray()) != null)
        throw new Exception("Invalid catalogue icon was accepted.");
    using (var source = new SkiaSharp.SKBitmap(20, 20))
    {
        using var encoded = source.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        if (CatalogIcons.Decode(encoded.ToArray()) is not { Length: > 0 })
            throw new Exception("Valid catalogue icon did not decode.");
    }
    Task.Run(async () =>
    {
        using var iconHttp = new HttpClient(new StalledIconFixture());
        var diagnostics = new List<string>();
        var icons = new CatalogIcons(iconHttp, diagnostics.Add, TimeSpan.FromMilliseconds(100));
        const string url = "https://cdn.modrinth.com/data/test.png?private=fixture";
        if (await icons.LoadAsync(url, default) != null || diagnostics.Count != 1 ||
            diagnostics[0].Contains("fixture", StringComparison.Ordinal))
            throw new Exception("Icon timeout was not handled safely.");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        if (await icons.LoadAsync(url, cancelled.Token) != null || diagnostics.Count != 1)
            throw new Exception("Icon cancellation produced an erroneous diagnostic.");
    }).GetAwaiter().GetResult();
    Console.WriteLine("PASS Skia catalogue icon fixtures for valid, empty and invalid images, timeout and cancellation");
    if (MicrosoftAccounts.NormalizeClientId("00000000-0000-0000-0000-000000000000") is not null ||
        MicrosoftAccounts.NormalizeClientId("not-a-guid") is not null ||
        MicrosoftAccounts.NormalizeClientId(" 3216AD0F-1336-46FD-A6DB-93F8AA7CDF35 ") != "3216ad0f-1336-46fd-a6db-93f8aa7cdf35")
        throw new Exception("OAuth configuration validation failed.");
    const string confidential = "test-confidential-payload";
    foreach (var error in new Exception[]
    {
        new Microsoft.Identity.Client.MsalClientException("invalid_client", confidential),
        new Microsoft.Identity.Client.MsalClientException("access_denied", confidential),
        new Microsoft.Identity.Client.MsalUiRequiredException("invalid_grant", confidential),
        new HttpRequestException(confidential)
    })
    {
        var localized = MicrosoftAccounts.Describe(error);
        if (!localized.Any(character => character > 127) || localized.Contains(confidential))
            throw new Exception("OAuth error localization or redaction failed.");
    }
    var manager = new MicrosoftAccounts(temp);
    manager.UseOfflineAsync().GetAwaiter().GetResult();
    if (manager.GetGameSessionAsync().GetAwaiter().GetResult() != null)
        throw new Exception("Offline mode returned an online session.");
    var cacheFile = Path.Combine(temp, "microsoft-msal.bin");
    File.WriteAllText(cacheFile, "fixture");
    manager.ClearAsync().GetAwaiter().GetResult();
    if (File.Exists(cacheFile)) throw new Exception("Cached credentials were not removed.");
    Console.WriteLine("PASS developer configuration, localized credential-safe errors, offline selection and cache removal");

    Task.Run(() => AccountStateTests.RunAsync(temp)).GetAwaiter().GetResult();
    var store = new InstanceStore(temp);
    InstanceStore.WriteJson(Path.Combine(temp, "official-versions.json"), new[] { new GameService.OfficialVersion("1.21.1", "release"),
            new GameService.OfficialVersion("25w04a", "snapshot"),
            new GameService.OfficialVersion("rd-132211", "old_alpha") }
        .Concat(Enumerable.Range(0, 180).Select(x => new GameService.OfficialVersion("fixture-" + x, "release"))).ToArray());
    store.Save(new Instance(Guid.NewGuid(), "Test Survival", "1.21.1"));
    store.Save(new Instance(Guid.NewGuid(), "Fabric 1.21.1", "1.21.1", "Fabric", LoaderVersion: "0.16.4"));
    using var catalog = new HttpClient(new CatalogFixture());
    var window = new MainWindow(temp, new Modrinth(catalog), catalog);
    window.Show();
    Button Obtain(string name) => window.GetVisualDescendants().OfType<Button>().First(b =>
        b.Content is string label && label == name ||
        b.Content is StackPanel row && row.Children.OfType<TextBlock>().Any(t => t.Text == name));
    void Click(string title) => ((IInvokeProvider)new ButtonAutomationPeer(Obtain(title))).Invoke();

    foreach (var name in new[] { "首页", "个人游玩", "整合包", "模组管理", "下载管理", "设置", "首页" })
    {
        Click(name);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
    Click("个人游玩");
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    if (!window.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "Test Survival"))
        throw new Exception("Saved instance did not appear in Personal.");
    if (!window.GetVisualDescendants().OfType<Button>().Any(x => x.Content is string text && text is "下载" or "校验中")) throw new Exception("Missing installation button");
    ResponsiveUiTests.Run(window, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS"));
    Phase12UiTests.Run(window, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS"));
    Phase13UiTests.Run(window, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS"));
    Phase15UiTests.Run(window, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS"));
    Phase18UiTests.Run(window, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS"));
    window.Close();
    DualModeUiTests.Run(temp, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS"));
    CompactFinalUiTests.Run(temp, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS"));
    WindowChromeUiTests.Run(temp, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS"));
    AdaptiveComboUiTests.Run(temp, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS"));
    GlobalUiFixTests.Run(temp, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS"));
    StageRailAlignmentTests.Run(temp, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS"));
    StageNodesFinalTests.Run(temp, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS"));
    LauncherUpdateUiTests.Run(temp);
    DirectDownloadTests.Run(temp, Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS"));
    Console.WriteLine("PASS headless window creation, six navigation pages, persisted instance and launch button");
}
finally { Directory.Delete(temp, true); }


file sealed class StalledIconFixture : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("The fixture delay is infinite.");
    }
}
