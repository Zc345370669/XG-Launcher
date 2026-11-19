// SPDX-License-Identifier: MIT
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace XGLauncher;

public sealed partial class MainWindow
{
    private static Image OfficialLogo(double width, double height)
    {
        Bitmap Load()
        {
            using var stream = AssetLoader.Open(new Uri("avares://XGLauncher/Assets/XG.png"));
            using var original = SkiaSharp.SKBitmap.Decode(stream) ?? throw new InvalidDataException("缺少正式 XG Logo。");
            using var output = new SkiaSharp.SKBitmap(original.Width, original.Height);
            var color = ThemePalette.Text.Color;
            using (var canvas = new SkiaSharp.SKCanvas(output))
            {
                canvas.DrawBitmap(original, 0, 0);
                canvas.DrawColor(new SkiaSharp.SKColor(color.R, color.G, color.B), SkiaSharp.SKBlendMode.SrcIn);
            }
            using var encoded = output.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var bytes = encoded.AsStream(); return new Bitmap(bytes);
        }
        var image = new Image { Width = width, Height = height, Stretch = Stretch.Uniform,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        void Update(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
        { var previous = image.Source as Bitmap; image.Source = Load(); previous?.Dispose(); }
        image.AttachedToVisualTree += (_, _) => { Update(null, null!); ThemePalette.Text.PropertyChanged += Update; };
        image.DetachedFromVisualTree += (_, _) => { ThemePalette.Text.PropertyChanged -= Update; (image.Source as Bitmap)?.Dispose(); image.Source = null; };
        return image;
    }
}
