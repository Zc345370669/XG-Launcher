// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XG.Core;
using XGLauncher;

internal static class StageRailAlignmentTests
{
    internal static void Baseline(string root, string? shots)
    {
        using var card = new LiveCard(root, "baseline");
        var rail = card.Rail;
        var track = rail.GetVisualDescendants().OfType<Border>().First();
        var moving = rail.GetVisualDescendants().OfType<LucideIcon>().Single(x => x.Icon == "circle" && x.IsVisible);
        var complete = rail.GetVisualDescendants().OfType<LucideIcon>().First(x => x.Icon == "circle-check" && x.IsVisible);
        var caption = rail.GetVisualDescendants().OfType<TextBlock>().Single(x=>x.Name=="StageCaption");
        Console.WriteLine("BASELINE measured actual rendered controls " + JsonSerializer.Serialize(new {
            ProgressBars = card.Tasks.GetVisualDescendants().OfType<ProgressBar>().Count(),
            TrackCenter = Center(track, rail), CompletedCenter = Center(complete,rail), CurrentCenter = Center(moving,rail),
            Caption = caption.Text, CaptionBounds = RectIn(caption,rail), CircleBounds = RectIn(moving,rail),
            Overlap = RectIn(caption,rail).Intersects(RectIn(moving,rail)) }));
        Capture(card.Window,shots,"baseline-download-card-misalignment");
    }

    internal static void Run(string root, string? shots)
    {
        using (var card = new LiveCard(root,"aligned"))
        {
            Check(card.Tasks.GetVisualDescendants().OfType<ProgressBar>().Count()==1,"full task card contains more than one percentage bar");
            Check(!card.Tasks.GetVisualDescendants().OfType<Slider>().Any(),"task card contains a second slider progress");
            var rail=card.Rail; AssertLayout(rail,true);
            var axis=rail.GetVisualDescendants().OfType<Border>().Single(x=>x.Name=="StageTrack"); var fullWidth=axis.Bounds.Width;
            Capture(card.Window,shots,"download-card-active");
            card.Click("暂停"); Wait(()=>card.Model.Resources.IsPaused(card.JobId));
            rail=card.Rail; AssertLayout(rail,true); var paused=Center(Current(rail),rail);
            RunFor(260); Check(Center(Current(rail),rail)==paused,"paused stage kept animating"); Capture(card.Window,shots,"download-card-paused");
            card.Click("继续"); Wait(()=>!card.Model.Resources.IsPaused(card.JobId));
            rail=card.Rail; card.Advance.TrySetResult(); Wait(()=>card.Advanced.Task.IsCompleted); RunFor(300); rail=card.Rail; AssertLayout(rail,true);
            Check(rail.GetVisualDescendants().OfType<Border>().Single(x=>x.Name=="StageTrack").Bounds.Width==fullWidth,"stage track grew like a duplicate percentage fill");
            Console.WriteLine("Resume measured "+JsonSerializer.Serialize(new{paused,current=Center(Current(rail),rail),rail.Percent}));
            Check(Center(Current(rail),rail).X>paused.X,"resumed node did not advance with actual stage data"); Capture(card.Window,shots,"download-card-resumed");
            card.Window.Width=520;card.Window.Height=420;Pump();AssertLayout(card.Rail,true);
            card.Window.Width=1130;card.Window.Height=720;Pump();
            card.Finish.TrySetResult(); Wait(()=>card.Operation.IsCompleted); RunFor(260);
            Check(card.Operation.IsCompletedSuccessfully,"controlled stage completion failed");
            // Completed tasks are on the existing completed tab, not the active task list.
            card.Click("已完成"); Pump();
            var completed=card.Tasks.GetVisualDescendants().OfType<StageRail>().Single(); AssertLayout(completed,false);
            Check(completed.GetVisualDescendants().OfType<LucideIcon>().Count(x=>x.Icon=="circle-check"&&x.IsVisible)>=2,"completed stage checkpoints missing");
            Check(!Current(completed).IsVisible&&Caption(completed).Text=="已完成","completion still shows moving stage or wrong final name");
            Capture(card.Window,shots,"download-card-complete");
        }
        using (var card=new LiveCard(root,"cancel"))
        {
            card.Model.Resources.Cancel(card.JobId); Wait(()=>card.Operation.IsCompleted);
            Check(card.Model.Resources.MainJobs.Single().State=="已取消","cancel altered task lifecycle");
            card.Click("已完成"); Pump(); var rail=card.Tasks.GetVisualDescendants().OfType<StageRail>().Single();
            AssertLayout(rail,false); Check(!Current(rail).IsVisible&&Caption(rail).Text=="已取消","cancel did not end stage motion");
            Capture(card.Window,shots,"download-card-cancelled");
        }

        var direct=new StageRail(); var host=new Window { Width=600,Height=100,FontFamily=new FontFamily("Noto Sans CJK SC"),Content=direct }; host.Show(); Pump();
        foreach(var width in new[]{72d,180,320,640,1000})
        foreach(var palette in new[]{ThemePalette.All[0],ThemePalette.All[4]})
        {
            ThemePalette.Select(palette); host.Background=ThemePalette.Panel; host.Width=width; Pump();
            var name="Minecraft Assets · "+new string('长',70);
            direct.Update([new("游戏本体","已完成",0,30,1,true),new(name,"下载中",30,100,.16,true)],name,"已暂停",41.2); Pump();
            AssertLayout(direct,true);
            var label=Caption(direct); Check(label.TextTrimming==TextTrimming.CharacterEllipsis && Equals(ToolTip.GetTip(label),name),"long stage name lacks ellipsis/full tooltip");
            foreach(var dpi in new[]{1d,1.25,1.5,1.75,2})
            {
                using var raster=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(width*dpi),(int)Math.Ceiling(direct.Bounds.Height*dpi)),new Vector(96*dpi,96*dpi)); raster.Render(direct);
                using var stream=new MemoryStream();raster.Save(stream);stream.Position=0;using var bitmap=SkiaSharp.SKBitmap.Decode(stream);
                Check(bitmap.Width==(int)Math.Ceiling(width*dpi),"DPI raster dimensions wrong");
                // Shared coordinates must remain identical when mapped to physical pixels.
                var track=direct.GetVisualDescendants().OfType<Border>().Single(x=>x.Name=="StageTrack");
                foreach(var icon in direct.GetVisualDescendants().OfType<LucideIcon>().Where(x=>x.IsVisible))
                    Check(Math.Abs((Center(icon,direct).Y-Center(track,direct).Y)*dpi)<.001,"DPI changes circle centerline");
                if(shots!=null && width is 180 or 640) { Directory.CreateDirectory(shots); raster.Save(Path.Combine(shots,$"rail-{width}-{(palette.Dark?"dark":"light")}-{dpi*100:0}pct-linux-headless.png")); }
            }
        }
        ThemePalette.Select(ThemePalette.All[0]); host.Width=600; Pump();
        direct.Update([new("游戏本体","下载中",0,40,.1,true)],"游戏本体","已暂停",4);Pump();
        var start=Center(Current(direct),direct).X;
        direct.Update([new("游戏本体","下载中",0,40,.8,true)],"游戏本体","下载中",32);
        Pump(); var immediate=Center(Current(direct),direct).X;
        var samples=new List<double>(); for(var sample=0;sample<24;sample++){RunFor(12);samples.Add(Center(Current(direct),direct).X);}
        var end=Center(Current(direct),direct).X;
        Console.WriteLine("Rail motion measured: "+JsonSerializer.Serialize(new{start,immediate,end,samples,enabled=Motion.Enabled}));
        Check(!Motion.Enabled || samples.Any(x=>x>start+.01&&x<end-.01),"circle motion is static/teleported instead of interpolated");
        Check(!Motion.Enabled || end>immediate,"circle did not finish moving right"); AssertLayout(direct,true);
        direct.Update([new("游戏本体","已完成",0,40,1,true),new("Minecraft Assets","下载中",40,100,0,true)],"Minecraft Assets","下载中",40);RunFor(260);AssertLayout(direct,true);Capture(host,shots,"rail-stage-transition");
        direct.Update([new("游戏本体","已完成",0,40,1,true),new("Minecraft Assets","下载中",40,100,.001,true)],"Minecraft Assets","已暂停",40.06);Pump();AssertLayout(direct,true);
        using(var snapshot=new RenderTargetBitmap(new PixelSize(600,(int)Math.Ceiling(direct.Bounds.Height)),new Vector(96,96)))
        {
            snapshot.Render(direct);using var bytes=new MemoryStream();snapshot.Save(bytes);bytes.Position=0;using var pixels=SkiaSharp.SKBitmap.Decode(bytes);
            var center=Center(Current(direct),direct);var blue=0;var green=0;
            for(var py=(int)(center.Y-9);py<(int)(center.Y+9);py++)for(var px=(int)(center.X-9);px<(int)(center.X+9);px++)
            {
                var c=pixels.GetPixel(px,py);if(c.Green>c.Red+20&&c.Green>c.Blue+20)green++;
                if(c.Blue>c.Green+15&&c.Blue>c.Red+30)blue++;
            }
            Check(blue>3&&green>3,"coincident blue ring/green check is not rendered");
            Check(Math.Abs(Center(Current(direct),direct).X-Center(direct.GetVisualDescendants().OfType<Border>().First(x=>x.Name=="CompletedStageNode"&&x.IsVisible),direct).X)<1,"handoff still adds artificial spacing");
        }
        direct.Update([new("游戏本体","已完成",0,40,1,true),new("Minecraft Assets","失败",40,100,.2,true)],"Minecraft Assets","失败",52);Pump();
        Check(direct.GetVisualDescendants().OfType<LucideIcon>().Single(x=>x.Name=="StageCurrentIcon").Icon=="circle-exclamation-point","failed stage falsely completed");AssertLayout(direct,true);
        host.WindowState=WindowState.Maximized;Pump();AssertLayout(direct,true);host.WindowState=WindowState.Normal;Pump();AssertLayout(direct,true);
        host.Close();Pump();ThemePalette.Select(ThemePalette.All[0]);
        Console.WriteLine("PASS stage rail real card one percentage bar/invisible axis with persistent paired names, shared circle centers, safe endpoint padding and label row, long tooltip, measured animation/transition/pause/resume/cancel/complete/error, narrow/normal/maximized and light/dark 100-200% raster geometry (Linux headless, not Windows GUI)");
    }
    static void AssertLayout(StageRail rail,bool active)
    {
        var track=rail.GetVisualDescendants().OfType<Border>().Single(x=>x.Name=="StageTrack");
        Check(track.Opacity==0&&ReferenceEquals(track.Background,Brushes.Transparent),"stage connection line remains visible");
        var axis=track.Parent!;
        foreach(var icon in rail.GetVisualDescendants().OfType<LucideIcon>().Where(x=>x.IsVisible))
        {
            var rect=RectIn(icon,rail);Check(Math.Abs(Center(icon,rail).Y-Center(track,rail).Y)<.001,"node center not on track centerline");
            Check(rect.Top>=0&&rect.Bottom<=rail.Bounds.Height,"node clipped vertically");
            var label=RectIn(Caption(rail),rail);Check(label.Top>=rect.Bottom+4&&!label.Intersects(rect),"caption overlaps track node");
        }
        var bounds=RectIn(Caption(rail),rail);Check(bounds.Bottom<=rail.Bounds.Height+.01,"caption outside card height");
        var names=rail.GetVisualDescendants().OfType<TextBlock>().Where(x=>x.IsVisible).ToArray();
        for(var i=0;i<names.Length;i++)for(var j=i+1;j<names.Length;j++)Check(!RectIn(names[i],rail).Intersects(RectIn(names[j],rail)),"stage names overlap");
        if(active) Check(Current(rail).IsVisible,"active node missing");
    }
    static Border Current(StageRail rail)=>rail.GetVisualDescendants().OfType<Border>().Single(x=>x.Name=="CurrentStageNode");
    static TextBlock Caption(StageRail rail)=>rail.GetVisualDescendants().OfType<TextBlock>().Single(x=>x.Name=="StageCaption");
    static Rect RectIn(Control c,Control root)=>new(c.TranslatePoint(default,root)!.Value,c.Bounds.Size);
    static Point Center(Control c,Control root)=>c.TranslatePoint(new Point(c.Bounds.Width/2,c.Bounds.Height/2),root)!.Value;
    static void Capture(Window w,string? shots,string name){if(shots==null)return;Directory.CreateDirectory(shots);using var f=w.CaptureRenderedFrame();f!.Save(Path.Combine(shots,name+"-linux-headless.png"));}
    static void Pump(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();}
    static void RunFor(int ms){var end=DateTime.UtcNow.AddMilliseconds(ms);do{using var stop=new CancellationTokenSource(10);Dispatcher.UIThread.MainLoop(stop.Token);Pump();}while(DateTime.UtcNow<end);}
    static void Check(bool good,string error){if(!good)throw new Exception(error);}
    static void Wait(Func<bool> ready){var end=DateTime.UtcNow.AddSeconds(15);while(!ready()&&DateTime.UtcNow<end){Pump();RunFor(10);}Pump();Check(ready(),"stage rail UI timeout");}
    sealed class LiveCard:IDisposable
    {
        internal readonly MainWindow Window; internal readonly LauncherModel Model;
        internal readonly TaskCompletionSource Advance=new(),Advanced=new(),Finish=new();
        internal readonly Task<bool> Operation; internal readonly Guid JobId;
        internal DownloadSurface Tasks=>Window.GetVisualDescendants().OfType<DownloadSurface>().Single();
        internal StageRail Rail=>Tasks.GetVisualDescendants().OfType<StageRail>().Single();
        internal LiveCard(string root,string suffix)
        {
            Window=new MainWindow(Path.Combine(root,"rail-"+suffix)){FontFamily=new FontFamily("Noto Sans CJK SC")};Window.Show();Pump();
            Model=(LauncherModel)typeof(MainWindow).GetField("model",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(Window)!;
            Operation=Model.Resources.RunAsync("阶段轨道受控任务","实例安装","fixture","alignment",async mark=>
            {
                Model.Resources.PlanStages([("游戏本体",1L*1048576,1,false),("Minecraft Assets",99L*1048576,1,false)],"alignment");
                await Model.Resources.RunAsync("游戏本体","游戏本体","fixture","alignment",child=>{child("下载中");Model.Resources.Report("游戏本体","alignment",1L*1048576,1L*1048576,"client.jar",1,1);return Task.FromResult(true);});
                await Model.Resources.RunAsync("Minecraft Assets","Minecraft Assets","fixture","alignment",async child=>
                {
                    child("下载中");Model.Resources.Report("Minecraft Assets","alignment",8L*1048576,99L*1048576,"资源文件",0,1);
                    await Advance.Task.WaitAsync(Model.Resources.Current!.Token);
                    child("下载中");Model.Resources.Report("Minecraft Assets","alignment",50L*1048576,99L*1048576,"资源文件",0,1);Advanced.TrySetResult();
                    await Finish.Task.WaitAsync(Model.Resources.Current!.Token);return true;
                });return true;
            });
            JobId=Model.Resources.MainJobs.Single().Id;
            Invoke(Window.GetVisualDescendants().OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="下载管理"));
            Wait(()=>Tasks.GetVisualDescendants().OfType<StageRail>().Any());RunFor(250);
        }
        internal void Click(string label)=>Invoke(Tasks.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,label)));
        static void Invoke(Button b){((IInvokeProvider)new ButtonAutomationPeer(b)).Invoke();Pump();}
        public void Dispose(){if(!Operation.IsCompleted){Model.Resources.Cancel(JobId);Wait(()=>Operation.IsCompleted);}Window.Close();Pump();}
    }
}
