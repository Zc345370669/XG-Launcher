// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XG.Core;
using XGLauncher;

internal static class StageNodesFinalTests
{
    internal static void Run(string root, string? shots)
    {
        var names = new Dictionary<string,string> {
            ["Minecraft Assets"]="Assets",["Minecraft Libraries"]="Libs",["Minecraft Version"]="Version",
            ["Minecraft Runtime"]="Runtime",["Minecraft Java"]="Java",["Minecraft Client"]="Client",["Java 环境"]="Java" };
        foreach(var pair in names) Check(StageRail.StageName(new(pair.Key,"下载中",0,100,.2,true))==pair.Value,"incorrect explicit name mapping: "+pair.Key);
        Check(StageRail.StageName(new("My Minecraft Assets backup","下载中",0,100,0,true))=="My Minecraft Assets backup","mapping rewrites unrelated identifiers");

        var rail = new StageRail();var host=new Window { Width=700,Height=100,Content=rail,FontFamily=new FontFamily("Noto Sans CJK SC") };host.Show();Pump();
        foreach(var loader in new[]{"NeoForge","Forge","Fabric","Quilt","FutureLoader"})
        {
            var path=Path.Combine(root,"node-events-"+loader);Directory.CreateDirectory(path);
            var service=new ResourceOperations(path);var tracker=new RelayProgressTracker();var startCount=0;
            service.Changed+=()=>{ if(service.Items.Any(x=>x.Kind=="模组加载器"&&x.State=="安装中"))startCount++; };
            var operation=service.RunAsync("install "+loader,"实例安装","controlled",loader,async mark=>
            {
                service.PlanStages([("Minecraft Assets",1048576L,1,false),("模组加载器",0L,0,false)],loader);
                Show("等待中");Check(!VisibleNames(rail).Contains(loader),"chosen loader was displayed before actual execution");
                await service.RunAsync("assets","Minecraft Assets","official",loader,child=>
                { child("下载中");service.Report("Minecraft Assets",loader,1048576,1048576,"resource",1,1);return Task.FromResult(true); });
                await service.RunAsync("installer","模组加载器",loader,loader,child=>
                {
                    child("安装中");Show("模组加载器");
                    Check(CurrentName(rail)==loader && VisibleNames(rail).Contains("Assets"),"live installer source/name or completed Assets caption missing");
                    Check(service.Items.Single(x=>x.Kind=="模组加载器").Source==loader,"planned stage lost actual installer metadata");
                    AssertPairs(rail);Capture(host,shots,"nodes-"+loader+"-installing");return Task.FromResult(true);
                });
                Show(null);Check(rail.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Name=="CompletedStageCaption"&&x.IsVisible&&x.Text==loader),"zero-byte completed installer lost its caption");
                return true;
                void Show(string? current)
                {
                    var main=service.MainJobs.Single();var data=InstallationProgress.Calculate(main,service.Items.Where(x=>x.ParentId==main.Id).ToArray());
                    var relay=tracker.Update(data,main.State);rail.Update(relay.Stages,current,main.State,relay.Percent);Pump();
                }
            });
            if(operation.IsFaulted) operation.GetAwaiter().GetResult();
            Check(operation.IsCompletedSuccessfully && startCount>0,"controlled real service events did not complete");
            var restored=new ResourceOperations(path);var main=restored.MainJobs.Single();
            var history=InstallationProgress.Calculate(main,restored.Items.Where(x=>x.ParentId==main.Id).ToArray());
            Check(history.Percent==100 && history.Stages.Single(x=>x.Kind=="模组加载器").DisplayName==loader,"loader display metadata not persistent");
            rail.Update(history.Stages,null,main.State,history.Percent);Pump();AssertPairs(rail);Capture(host,shots,"nodes-"+loader+"-completed");
        }

        var vanillaMain=new ResourceOperation(Guid.NewGuid(),"vanilla","实例安装","official","v","下载中");
        var vanilla=InstallationProgress.Calculate(vanillaMain,[new(Guid.NewGuid(),"assets","Minecraft Assets","official","v","下载中",Bytes:10,TotalBytes:100,ParentId:vanillaMain.Id)]);
        rail.Update(vanilla.Stages,"Minecraft Assets","下载中",vanilla.Percent);Pump();
        Check(!VisibleNames(rail).Any(x=>x is "Loader" or "NeoForge" or "Forge" or "Fabric" or "Quilt" or "模组加载器"),"vanilla gained a fake loader stage");
        Check(Math.Abs(vanilla.Percent-1000d/(100+1048576))<.00001,"presentation changed real byte/work percentage");
        var skipped=InstallationProgress.Calculate(vanillaMain,[new(Guid.NewGuid(),"cached","Java 环境","official","v","已完成",Skipped:true)]);
        Check(skipped.Stages.All(x=>x.Kind!="Java 环境"),"cached stage no longer skipped");

        foreach(var width in new[]{180d,320,640,1000})
        foreach(var palette in new[]{ThemePalette.All[0],ThemePalette.All[4],ThemePalette.All[2]})
        {
            ThemePalette.Select(palette);host.Background=ThemePalette.Panel;host.Width=width;Pump();
            StageProgress[] stages=[new("Minecraft Assets","已完成",0,1,1,true),new("Minecraft Libraries","已完成",1,2,1,true),new("Minecraft Runtime","已完成",2,3,1,true),
                new("Minecraft Loader","安装中",3,100,.01,true){DisplayName="NeoForge "+new string('长',80)}];
            rail.Update(stages,"Minecraft Loader","已暂停",3.97);Pump();AssertPairs(rail);
            Check(VisibleNames(rail).Count()==4,"not every started node has a caption");
            var viewport=rail.GetVisualDescendants().OfType<Border>().Single(x=>x.Name=="StageNodeViewport");
            Check(!viewport.ClipToBounds && Math.Abs(viewport.Bounds.Width-rail.Bounds.Width)<.01,"node viewport clips endpoint circles or changes progress width");
            var completed=rail.GetVisualDescendants().OfType<TextBlock>().First(x=>x.Name=="CompletedStageCaption"&&x.IsVisible);
            var before=completed.TranslatePoint(new Point(48,0),(Visual)completed.Parent!);
            rail.Update(stages.Select(x=>x.Kind=="Minecraft Loader"?x with{Fraction=.2}:x).ToArray(),"Minecraft Loader","已暂停",22.4);Pump();AssertPairs(rail);
            Check(before==completed.TranslatePoint(new Point(48,0),(Visual)completed.Parent!),"completed caption moved when current phase advanced");
            foreach(var scale in new[]{1d,1.25,1.5,1.75,2})
            {
                using var bitmap=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(width*scale),(int)Math.Ceiling(rail.Bounds.Height*scale)),new Vector(96*scale,96*scale));bitmap.Render(rail);
                if(shots!=null&&width==640){Directory.CreateDirectory(shots);bitmap.Save(Path.Combine(shots,$"nodes-pairs-theme-{palette.Name}-{scale*100:0}pct-linux-headless.png"));}
                AssertPairs(rail);
            }
        }
        host.Close();Pump();ThemePalette.Select(ThemePalette.All[0]);
        Console.WriteLine("PASS final trackless stage nodes: explicit short names, completed captions, real planned/reused/persisted NeoForge/Forge/Fabric/Quilt/future-loader events, no Vanilla loader, zero-byte completion, stable nonoverlapping pairs, horizontal viewport, theme and 100-200% raster layouts (Linux headless)");
    }
    static string? CurrentName(StageRail r)=>r.GetVisualDescendants().OfType<TextBlock>().Single(x=>x.Name=="StageCaption").Text;
    static IEnumerable<string?> VisibleNames(StageRail r)=>r.GetVisualDescendants().OfType<TextBlock>().Where(x=>x.IsVisible).Select(x=>x.Text);
    static void AssertPairs(StageRail rail)
    {
        var track=rail.GetVisualDescendants().OfType<Border>().Single(x=>x.Name=="StageTrack");Check(track.Opacity==0,"visible stage connection line");
        var nodes=rail.GetVisualDescendants().OfType<Border>().Where(x=>x.IsVisible&&x.Name is "CurrentStageNode" or "CompletedStageNode").ToArray();
        var names=rail.GetVisualDescendants().OfType<TextBlock>().Where(x=>x.IsVisible).ToArray();
        Check(nodes.Length==names.Length,"node without its own caption");
        for(var i=0;i<nodes.Length;i++)
        {
            var center=nodes[i].TranslatePoint(new Point(9,9),rail)!.Value;
            var name=names[i].TranslatePoint(default,rail)!.Value;
            var expected=Math.Clamp(center.X-names[i].Bounds.Width/2,0,Math.Max(0,rail.Bounds.Width-names[i].Bounds.Width));
            Check(Math.Abs(expected-name.X)<.01 && name.Y>=center.Y+9+4,"caption is not below/clamped to its real node");
            Check(names[i].TextTrimming==TextTrimming.CharacterEllipsis&&ToolTip.GetTip(names[i])!=null,"long caption lacks truncation/tooltip");
            for(var j=0;j<i;j++) Check(!new Rect(name,names[i].Bounds.Size).Intersects(new Rect(names[j].TranslatePoint(default,rail)!.Value,names[j].Bounds.Size)),"caption rows overlap");
        }
    }
    static void Capture(Window window,string? shots,string name){if(shots==null)return;Directory.CreateDirectory(shots);using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(shots,name+"-linux-headless.png"));}
    static void Pump(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();}
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
