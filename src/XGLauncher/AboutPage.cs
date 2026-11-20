// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace XGLauncher;

public sealed partial class MainWindow
{
    internal const string OfficialRepository = "https://github.com/Zc345370669/XG-Launcher";
    private Task OpenLegalAsync(bool thirdParty)
    {
        FocusManager?.ClearFocus();
        var legal = new LegalBrowser(this);
        if (thirdParty) legal.ShowThirdParty();
        else legal.ShowDocument(LegalCatalog.File("XGLauncher-MIT.txt", "XG Launcher"), legal.ShowOverview);
        var back = Action("返回关于", () =>
        {
            Refresh();
            settingsPage?.ScrollToSection(6, false);
            return Task.CompletedTask;
        });
        page.Content = PageViewport(Column(back, legal));
        return Task.CompletedTask;
    }

    private Button OfficialRepositoryLink()
    {
        var link = new Button { Content = new TextBlock { Text = OfficialRepository, Foreground = ThemePalette.Accent,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 12 },
            Background = Brushes.Transparent, BorderThickness = new(0), Padding = new(0, 4),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(link, OfficialRepository);
        link.Click += async (_, _) => { try { await OpenExternalLink(OfficialRepository); } catch (Exception ex) { await ErrorAsync(link, ex.Message); } };
        return link;
    }

    private Control AboutPage()
    {
        var assembly = typeof(MainWindow).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";
        version = version.Split('+')[0];
        var build = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(x => x.Key == "XGBuildUtc")?.Value ?? "未记录";
        Bitmap CreateLogo() {
using var stream = AssetLoader.Open(new Uri("avares://XGLauncher/Assets/XG.png"));
        using var original = SkiaSharp.SKBitmap.Decode(stream);
        using var tinted = new SkiaSharp.SKBitmap(original.Width, original.Height);
        var color = ThemePalette.Text.Color;
        using (var canvas = new SkiaSharp.SKCanvas(tinted)) {
            canvas.DrawBitmap(original,0,0);
            canvas.DrawColor(new SkiaSharp.SKColor(color.R,color.G,color.B),SkiaSharp.SKBlendMode.SrcIn);
        }
        using var encoded = tinted.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);
        using var bytes = encoded.AsStream();
        return new Bitmap(bytes);
        }
        var image = new Image { Source = CreateLogo(), Width = 128, Height = 128, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center };
        void RefreshLogo(object? sender, AvaloniaPropertyChangedEventArgs args) {
            var previous = image.Source as Bitmap; image.Source = CreateLogo(); previous?.Dispose();
        }
        image.AttachedToVisualTree += (_,_) => { ThemePalette.Text.PropertyChanged += RefreshLogo; RefreshLogo(null,null!); };
        image.DetachedFromVisualTree += (_,_) => ThemePalette.Text.PropertyChanged -= RefreshLogo;
        var mark = image;
        var description = Label("面向 Minecraft Java 版的独立 Windows 启动器。");
        var content = Column(mark, Label("XG Launcher", 26, true), Label("瞎搞启动器", 18, true, false), VersionUpdateLink(version), description,
            Label("构建时间（UTC）：" + build, 11), Label("运行平台：" + RuntimeInformation.OSDescription + " · " + RuntimeInformation.ProcessArchitecture, 11),
            Label(".NET：" + RuntimeInformation.FrameworkDescription + " · " + Environment.Version, 11));
        var project = Column(Label("作者 / 维护者：Zc345370669", 15, true),
            Label("主许可证：MIT License"), Label("开发方式：AI 辅助开发"),
            OfficialRepositoryLink(),
            Action("查看 MIT License", () => OpenLegalAsync(false)),
            Action("第三方许可证", () => OpenLegalAsync(true)));
        project.Name = "OfficialProjectInformation";
        foreach (var item in project.Children) item.HorizontalAlignment = HorizontalAlignment.Center;
        foreach (var label in project.Children.OfType<TextBlock>()) label.TextAlignment = TextAlignment.Center;
        content.Children.Add(project);
        foreach (var label in content.Children.OfType<TextBlock>()) {
            label.HorizontalAlignment = HorizontalAlignment.Center; label.TextAlignment = TextAlignment.Center;
        }
        content.VerticalAlignment = VerticalAlignment.Center;
        return content;
    }
}
