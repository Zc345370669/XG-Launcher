// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace XGLauncher;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "Local\\XGLauncher.v1", out var first);
        if (first) AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}

public sealed class App : Application
{
    public override void Initialize()
    {
        LocalizedView.Initialize();
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());
        LauncherTheme.Apply(this);

    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            if (desktop.Args is ["--xg-update-ack", var nonce] && Guid.TryParseExact(nonce, "N", out _) && desktop.MainWindow is MainWindow { HasLoadedWorkspace: true })
                desktop.MainWindow.Opened += (_, _) =>
                {
                    try { XG.Core.UpdateAcknowledgment.Write(nonce); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.TraceWarning(error.GetType().Name); }
                };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
