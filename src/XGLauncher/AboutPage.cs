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
    private Control AboutPage()
    {
        var assembly = typeof(MainWindow).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";
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
        var content = Column(mark, Label("XG Launcher", 26, true), Label("瞎搞启动器", 18, true, false), Label("v" + version), description,
            Label("构建时间（UTC）：" + build, 11), Label("运行平台：" + RuntimeInformation.OSDescription + " · " + RuntimeInformation.ProcessArchitecture, 11),
            Label(".NET：" + RuntimeInformation.FrameworkDescription + " · " + Environment.Version, 11));
        foreach (var label in content.Children.OfType<TextBlock>()) {
            label.HorizontalAlignment = HorizontalAlignment.Center; label.TextAlignment = TextAlignment.Center;
        }
        content.VerticalAlignment = VerticalAlignment.Center;
        return content;
    }
}
