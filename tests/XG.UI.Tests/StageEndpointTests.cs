// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XG.Core;
using XGLauncher;

internal static class StageEndpointTests
{
    internal static void Run(string? shots)
    {
        var rail=new StageRail();var bar=new ProgressBar {UseLayoutRounding=false,MinWidth=0};var percent=new TextBlock {Text="0%",MinWidth=34};
        rail.DisplayedPercentChanged+=value=>bar.Value=value;
        var top=new Grid {ColumnDefinitions=new("*,Auto"),ColumnSpacing=10};top.Children.Add(bar);Grid.SetColumn(percent,1);top.Children.Add(percent);
        rail.HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Left;bar.SizeChanged+=(_,e)=>rail.Width=e.NewSize.Width;
        var panel=new StackPanel {Margin=new Thickness(12),Spacing=10,Children={top,rail}};
        var host=new Window {Width=700,Height=220,Content=panel,FontFamily=new FontFamily("Noto Sans CJK SC")};host.Show();Pump();
        foreach(var width in new[]{180d,320,640,1000})
        foreach(var palette in new[]{ThemePalette.All[0],ThemePalette.All[4]})
        {
            host.Width=width;ThemePalette.Select(palette);host.Background=ThemePalette.Panel;Pump();
            Check(Math.Abs(rail.Bounds.Width-bar.Bounds.Width)<.01,"stage axis includes percentage column");
            foreach(var progress in new[]{0d,1,8,25,50,75,89,98,100})
            {
                bar.Value=progress;percent.Text=progress+"%";
                rail.Update([new("游戏本体","下载中",0,100,progress/100,true)],"游戏本体","已暂停",progress);Pump();
                var node=Current(rail);var rect=new Rect(node.TranslatePoint(default,rail)!.Value,node.Bounds.Size);
                Check(Math.Abs(Center(node,rail)-rail.Bounds.Width*progress/100)<.01,"circle center not on true progress coordinate");
                if(progress==0)Check(Math.Abs(Center(node,rail))<.01,"0% center not at far left");
                if(progress==100)Check(Math.Abs(Center(node,rail)-rail.Bounds.Width)<.01,"100% center not at far right");
                Check(!rail.ClipToBounds,"endpoint circles clipped by rail");
                var fill=bar.GetVisualDescendants().OfType<Control>().Single(c=>c.Name=="PART_Indicator");
                Check(Math.Abs(node.TranslatePoint(new Point(9,9),host)!.Value.X-fill.TranslatePoint(new Point(fill.Bounds.Width,0),host)!.Value.X)<.01,
                    $"circle/fill mismatch width={width}, theme={palette.Name}, percent={progress}, bar={bar.Bounds.Width}, fill={fill.Bounds.Width}, min={fill.MinWidth}, value={bar.Value}, node={node.TranslatePoint(new Point(9,9),host)!.Value.X}, fillEnd={fill.TranslatePoint(new Point(fill.Bounds.Width,0),host)!.Value.X}");
                Check(rail.GetVisualDescendants().OfType<TextBlock>().Single(x=>x.Name=="StageCaption").Text=="本体","body name not shortened");
                foreach(var dpi in new[]{1d,1.25,1.5,1.75,2})
                {
                    using var raster=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(width*dpi),(int)Math.Ceiling(host.Height*dpi)),new Vector(96*dpi,96*dpi));raster.Render(host);
                    Check(Math.Abs((rect.Right-rect.Left)*dpi-18*dpi)<.01,"DPI changed node radius");
                    if(progress is 0 or 100)
                    {
                        using var stream=new MemoryStream();raster.Save(stream);stream.Position=0;using var pixels=SkiaSharp.SKBitmap.Decode(stream);
                        var center=node.TranslatePoint(new Point(9,9),host)!.Value;var leftBlue=0;var rightBlue=0;
                        for(var y=Math.Max(0,(int)((center.Y-9)*dpi));y<Math.Min(pixels.Height,(int)Math.Ceiling((center.Y+9)*dpi));y++)
                        for(var x=Math.Max(0,(int)((center.X-9)*dpi));x<Math.Min(pixels.Width,(int)Math.Ceiling((center.X+9)*dpi));x++)
                        {var color=pixels.GetPixel(x,y);if(color.Blue>color.Red+30&&color.Blue>color.Green){if(x<center.X*dpi)leftBlue++;else rightBlue++;}}
                        Check(leftBlue>2&&rightBlue>2,"endpoint circle half was raster-clipped");
                    }
                }
                if(shots!=null&&width==640&&palette==ThemePalette.All[0])Capture(host,shots,"stage-endpoint-"+progress);
            }
            StageProgress[] relay=[new("游戏本体","已完成",0,30,1,true),new("Minecraft Assets","下载中",30,80,0,true),new("模组加载器","等待中",80,100,0,true){DisplayName="NeoForge"}];
            rail.Update(relay,"Minecraft Assets","已暂停",30);bar.Value=30;percent.Text="30%";Pump();
            var done=rail.GetVisualDescendants().OfType<Border>().Single(x=>x.Name=="CompletedStageNode"&&x.IsVisible);
            Check(Math.Abs(Center(done,rail)-Center(Current(rail),rail))<.001,"relay nodes still forced apart");
            Check(ReferenceEquals(Current(rail).Background,Brushes.Transparent),"current badge covers green completion at handoff");
            Check(rail.GetVisualDescendants().OfType<TextBlock>().Where(x=>x.IsVisible).All(x=>x.Text!="NeoForge"),"future loader shown before it starts");
            var labels=rail.GetVisualDescendants().OfType<TextBlock>().Where(x=>x.IsVisible).ToArray();
            Check(labels.Length==2&&!Box(labels[0],rail).Intersects(Box(labels[1],rail)),"relay captions overlap");
            if(shots!=null&&width==640)Capture(host,shots,"stage-relay-"+(palette.Dark?"dark":"light"));
            rail.Update([new("游戏本体","已完成",0,30,1,true),new("Minecraft Assets","已完成",30,99,1,true),new("完成检查","已完成",99,100,1,true)],null,"已完成",100);Pump();
            var final=rail.GetVisualDescendants().OfType<Border>().Where(x=>x.Name=="CompletedStageNode"&&x.IsVisible).Last();
            Check(Math.Abs(Center(final,rail)-bar.Bounds.Width)<.01,"completed 100% center not at right endpoint");
            Check(!rail.GetVisualDescendants().OfType<Border>().Single(x=>x.Name=="StageNodeViewport").ClipToBounds,"viewport clips endpoint circles");
            if(shots!=null&&width==640)Capture(host,shots,"stage-completed-"+(palette.Dark?"dark":"light"));
        }
        Check(StageRail.StageName(new("资源与依赖","下载中",0,100,0,true))=="依赖"&&StageRail.StageName(new("完成检查","下载中",0,100,0,true))=="校验","long Chinese node names remain");
        host.Width=640;Pump();
        rail.Update([new("游戏本体","下载中",0,100,0,true)],"游戏本体","已暂停",0);Pump();
        rail.Update([new("游戏本体","下载中",0,100,.89,true)],"游戏本体","下载中",89);bar.Value=rail.DisplayedPercent;
        var samples=new List<double>();
        for(var n=0;n<24;n++)
        {
            using(var stop=new CancellationTokenSource(12))Dispatcher.UIThread.MainLoop(stop.Token);Pump();
            var fill=bar.GetVisualDescendants().OfType<Control>().Single(c=>c.Name=="PART_Indicator");
            Check(Math.Abs(Current(rail).TranslatePoint(new Point(9,9),host)!.Value.X-fill.TranslatePoint(new Point(fill.Bounds.Width,0),host)!.Value.X)<.01,"animation frame circle/fill drift");
            samples.Add(bar.Value);
        }
        Check((!Motion.Enabled||samples.Any(v=>v>0&&v<89))&&Math.Abs(samples[^1]-89)<.01,"shared fill/circle animation missing");
        rail.Update([new("游戏本体","下载中",0,100,.93,true)],"游戏本体","已暂停",93);Pump();
        Check(Math.Abs(bar.Value-93)<.01&&Math.Abs(Center(Current(rail),rail)-rail.Bounds.Width*.93)<.01,"pause left circle/fill out of sync");
        host.Close();Pump();ThemePalette.Select(ThemePalette.All[0]);
        Console.WriteLine("PASS exact circle centers at 0/1/8/25/50/75/89/98/100% match actual ProgressBar PART_Indicator endpoint, 24 shared-animation frames/pause, same-center relay, completed endpoint, uncropped radius and 100-200% light/dark raster checks (Linux headless)");
    }
    static Border Current(StageRail r)=>r.GetVisualDescendants().OfType<Border>().Single(x=>x.Name=="CurrentStageNode");
    static Rect Box(Control c,Control root)=>new(c.TranslatePoint(default,root)!.Value,c.Bounds.Size);
    static double Center(Control c,Control root)=>c.TranslatePoint(new Point(9,9),root)!.Value.X;
    static void Capture(Window w,string folder,string name){Directory.CreateDirectory(folder);using var frame=w.CaptureRenderedFrame();frame!.Save(Path.Combine(folder,name+"-linux-headless.png"));}
    static void Pump(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();}
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
