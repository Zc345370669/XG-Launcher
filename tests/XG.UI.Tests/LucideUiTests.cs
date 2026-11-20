// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XGLauncher;
internal static class LucideUiTests
{
 internal static void Run(string? shots)
 {
  var assembly=typeof(MainWindow).Assembly;
  var names=assembly.GetManifestResourceNames().Where(x=>x.StartsWith("XG.Lucide.")).Select(x=>x[10..^4]).Order().ToArray();
  Check(LucideIcon.Shapes("trash-2").Count == LucideIcon.Shapes("trash").Count,"upstream trash-2 alias failed");
  Check(names.Length==36,"pinned SVG count mismatch");
  foreach(var name in names) {
   var shapes=LucideIcon.Shapes(name);Check(shapes.Count>0,"empty parsed icon "+name);
   foreach(var shape in shapes) {var box=shape.GetRenderBounds(new Pen(Brushes.Black,2));Check(box.Left>=-.1&&box.Top>=-.1&&box.Right<=24.1&&box.Bottom<=24.1,"official geometry outside viewport "+name);}
   var icon=new LucideIcon(name,24){Foreground=Brushes.Black};icon.Measure(new Size(24,24));icon.Arrange(new Rect(0,0,24,24));
   using var rendered=new RenderTargetBitmap(new PixelSize(24,24),new Vector(96,96));rendered.Render(icon);
   using var stream=new MemoryStream();rendered.Save(stream);stream.Position=0;using var bitmap=SkiaSharp.SKBitmap.Decode(stream);
   var ink=0;for(var y=0;y<24;y++)for(var x=0;x<24;x++){var alpha=bitmap.GetPixel(x,y).Alpha;if(alpha>0)ink++;if(x==0||y==0||x==23||y==23)Check(alpha==0,"raster edge clipped "+name);}
   Check(ink>0&&ink<24*24,"blank/opaque icon "+name);
  }
  Console.WriteLine("PASS Lucide 36 embedded official SVGs parsed and rasterized; transparent perimeter and nonblank complete geometry");
  var previous=ThemePalette.Current;
  foreach(var palette in ThemePalette.All) {
   ThemePalette.Select(palette);
   var panel=new WrapPanel {ItemWidth=120,ItemHeight=65};
   foreach(var name in names)panel.Children.Add(new StackPanel {Children={new LucideIcon(name,22){HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center},new TextBlock{Text=name,FontSize=11,Height=18,FontFamily=new FontFamily("DejaVu Sans"),Foreground=ThemePalette.Text,TextAlignment=TextAlignment.Center}}});
   var window=new Window{Width=760,Height=430,Background=ThemePalette.Panel,Content=panel};window.Show();Pump();
   Check(panel.GetVisualDescendants().OfType<LucideIcon>().All(x=>((ISolidColorBrush)x.EffectiveBrush).Color==ThemePalette.Text.Color),"theme foreground stale");
   if(shots!=null){Directory.CreateDirectory(shots);using var frame=window.CaptureRenderedFrame();Check(frame!=null,"headless gallery failed");frame!.Save(Path.Combine(shots,"lucide-"+Array.IndexOf(ThemePalette.All,palette)+"-linux-headless.png"));}
   window.Close();
  }
  ThemePalette.Select(previous);
  Console.WriteLine("PASS Lucide actual headless six-theme galleries; mutable foreground updates and fixed icon sizes");
  var child=new LucideIcon("play");var button=new Button{Content=child,Foreground=Brushes.Red};var host=new Window{Width=140,Height=100,Content=button};host.Show();Pump();
  Check(ReferenceEquals(child.EffectiveBrush,Brushes.Red),"button foreground not inherited");button.Foreground=Brushes.Blue;button.IsEnabled=false;Pump();
  Check(child.Width==18&&child.Height==18,"state changed icon bounds");button.IsEnabled=true;button.Foreground=Brushes.Green;Pump();Check(ReferenceEquals(child.EffectiveBrush,Brushes.Green),"state foreground update stale");host.Close();
  Console.WriteLine("PASS Lucide button foreground state changes and disabled/enabled layout stability");
  var rail=new StageRail();var railWindow=new Window{Width=500,Height=130,Content=rail};railWindow.Show();
  rail.Update([new("游戏本体","已完成",0,50,1,true),new("Java 环境","失败",50,100,0,true)],"Java 环境","失败");Pump();
  var badges=rail.GetVisualDescendants().OfType<LucideIcon>().Where(x=>x.IsEffectivelyVisible).Select(x=>x.Icon).ToArray();Check(badges.Contains("circle-check")&&badges.Contains("circle-exclamation-point"),"stage status icons mismatch");railWindow.Close();
  Console.WriteLine("PASS Lucide stage completion/error official icons without glyph overlays");
 }
 static void Pump(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();}
 static void Check(bool b,string message){if(!b)throw new Exception(message);}
}
