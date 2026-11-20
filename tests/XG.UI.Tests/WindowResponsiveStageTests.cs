// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XG.Core;
using XGLauncher;

internal static class WindowResponsiveStageTests
{
    internal static void Run(string root, string? shots)
    {
        foreach(var dpi in new[]{1d,1.25,1.5,1.75,2})
        foreach(var work in new[]{new PixelRect(0,0,1920,1040),new PixelRect(-1600,-200,1600,860),new PixelRect(1920,0,1024,720),new PixelRect(0,0,480,320)})
        foreach(var compact in new[]{false,true})
        {
            var fit=WindowPlacement.Fit(new(double.NaN,double.PositiveInfinity,int.MaxValue,int.MinValue,true),compact,work,dpi);
            var dialog=WindowChrome.DialogLimits(new(work.Width/dpi,work.Height/dpi));
            Check(dialog.Width>0&&dialog.Width<=work.Width/dpi&&dialog.Height>0&&dialog.Height<=work.Height/dpi,"dialog exceeds small/high-DPI monitor work area");
            Check(double.IsFinite(fit.Geometry.Width)&&fit.Geometry.Width>=fit.Minimum.Width,"nonfinite/invalid saved window width");
            Check(fit.Geometry.X>=work.X&&fit.Geometry.Y>=work.Y&&fit.Geometry.X+fit.Geometry.Width*dpi<=work.Right+.01&&fit.Geometry.Y+fit.Geometry.Height*dpi<=work.Bottom+.01,"restore escaped monitor work area");
            var points=new[]{new Point(1,1),new Point(200,1),new Point(399,1),new Point(1,200),new Point(399,200),new Point(1,399),new Point(200,399),new Point(399,399)};
            Check(points.Select(p=>WindowChrome.ResizeEdge(p,new(400,400))).Distinct().Count()==8,"eight resize regions missing");
            Check(WindowChrome.ResizeEdge(new(7.5,200),new(400,400))==WindowEdge.West&&WindowChrome.ResizeEdge(new(8.5,200),new(400,400))==null,"resize grip not eight DIP");
            Check(WindowChrome.ResizeEdge(new(-1,100),new(400,400))==null&&WindowChrome.ResizeEdge(new(double.NaN,0),new(400,400))==null,"outside/invalid pointer treated as resize");
        }
        root=Path.Combine(root,"responsive-stage");var owner=new MainWindow(root){FontFamily=new FontFamily("Noto Sans CJK SC")};owner.Show();Pump();
        var model=(LauncherModel)typeof(MainWindow).GetField("model",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
        model.Accounts.AddOfflineAsync("ResizePlayer").GetAwaiter().GetResult();
        owner.Width=850;owner.Height=610;RunFor(320);
        var saved=WindowSession.Load(Path.Combine(root,"window-session.json"));
        Check(saved.Full is {Width:850,Height:610},"resize not persisted before closing");
        foreach(var compact in new[]{false,true})
        {
            if(owner.IsCompact!=compact)owner.ToggleMode();Pump();
            owner.Width=compact?400:850;owner.Height=compact?480:610;Pump();
            if(WindowChrome.Strategy(owner)=="opaque-square-fallback")
            {
                // 在实际窗口客户区命中八个边缘，不使用固定屏幕位置。
                foreach(var edge in Enum.GetValues<WindowEdge>())
                {
                    owner.Width=compact?400:700;owner.Height=compact?400:500;Pump();var size=owner.ClientSize;
                    var west=edge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest;
                    var east=edge is WindowEdge.East or WindowEdge.NorthEast or WindowEdge.SouthEast;
                    var north=edge is WindowEdge.North or WindowEdge.NorthWest or WindowEdge.NorthEast;
                    var south=edge is WindowEdge.South or WindowEdge.SouthWest or WindowEdge.SouthEast;
                    var point=new Point(west?1:east?size.Width-1:size.Width/2,north?1:south?size.Height-1:size.Height/2);
                    owner.MouseMove(point);Pump();Check(owner.Cursor!=null,"edge cursor not set");
                    owner.MouseDown(point,MouseButton.Left);owner.MouseMove(point+new Vector(west?-18:east?18:0,north?-14:south?14:0));owner.MouseUp(point,MouseButton.Left);Pump();
                    Check((!west&&!east||owner.ClientSize.Width>size.Width)&&(!north&&!south||owner.ClientSize.Height>size.Height),"actual fallback drag did not resize "+edge);
                }
            }
            DoubleCaption();Check(owner.WindowState==WindowState.Maximized,"titlebar double click did not maximize compact/full mode");
            RunFor(550);DoubleCaption();Check(owner.WindowState==WindowState.Normal,"titlebar double click did not restore");
            foreach(var size in compact?new[]{new Size(320,330),new Size(500,700),new Size(900,950)}:new[]{new Size(520,395),new Size(850,610),new Size(1500,950)})
            {
                owner.Width=size.Width;owner.Height=size.Height;Pump();
                foreach(var palette in new[]{ThemePalette.All[0],ThemePalette.All[4]})
                {
                    ThemePalette.Select(palette);Pump();
                    if(!compact)
                    foreach(var name in new[]{"首页","个人游玩","整合包","模组管理","下载管理","设置"})
                    {
                        Invoke(owner.GetVisualDescendants().OfType<Button>().First(b=>AutomationProperties.GetName(b)==name));Pump();
                        var open=owner.GetVisualDescendants().OfType<UnifiedExpandableSelect>().FirstOrDefault(x=>x.IsEffectivelyVisible);
                        if(open!=null){open.Open();Pump();if(open.ExpansionBounds is { } bounds)Check(bounds.Left>=0&&bounds.Right<=owner.ClientSize.Width+1,"select outside resized window");open.Close();Pump();}
                        Capture(owner,shots,$"responsive-{name}-{size.Width}-{(palette.Dark?"dark":"light")}");
                    }
                    else
                    {
                        var layout=owner.GetVisualDescendants().OfType<Grid>().Single(x=>x.Name=="CompactLayout");
                        var picker=owner.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x=>x.Name=="CompactInstances");
                        Check(picker.Bounds.Width<=owner.ClientSize.Width&&layout.RowDefinitions.Count==2,"compact changed to columns or overflowed");
                        foreach(var scale in new[]{1d,1.25,1.5,1.75,2})
                        { using var bitmap=new RenderTargetBitmap(new PixelSize((int)(size.Width*scale),(int)(size.Height*scale)),new Vector(96*scale,96*scale));bitmap.Render(owner); }
                        Capture(owner,shots,$"responsive-compact-{size.Width}-{size.Height}-{(palette.Dark?"dark":"light")}");
                    }
                }
            }
        }
        var gate=new TaskCompletionSource();var resources=model.Resources;
        var task=resources.RunAsync("resize task","实例安装","controlled","resize",async mark=>{mark("下载中");await gate.Task;return true;});
        var id=resources.MainJobs.Single().Id;var accounts=model.Accounts;var game=model.Game;var downloads=model.Downloads;
        for(var i=0;i<20;i++){owner.ToggleMode();Pump();Check(ReferenceEquals(resources,model.Resources)&&ReferenceEquals(accounts,model.Accounts)&&ReferenceEquals(game,model.Game)&&ReferenceEquals(downloads,model.Downloads),"mode recreated services");}
        Check(resources.MainJobs.Single().Id==id&&!task.IsCompleted&&model.Accounts.SelectedProfile?.Name=="ResizePlayer","mode/resize lost real task/account state");
        gate.SetResult();RunFor(30);Check(task.IsCompletedSuccessfully,"shared task stopped during mode switching");
        if(!owner.IsCompact)owner.ToggleMode();owner.Width=410;owner.Height=470;Pump();
        owner.ToggleMaximized();RunFor(320);Check(WindowSession.Load(Path.Combine(root,"window-session.json")).Small?.Maximized==true,"compact maximized state not saved");
        owner.ToggleMaximized();owner.Close();RunFor(320);Check(model.IsDisposed,"close did not dispose shared model");
        var stamp=File.GetLastWriteTimeUtc(Path.Combine(root,"window-session.json"));RunFor(300);Check(stamp==File.GetLastWriteTimeUtc(Path.Combine(root,"window-session.json")),"geometry timer wrote after close");
        var restart=new MainWindow(root);restart.Show();Pump();Check(restart.IsCompact&&Math.Abs(restart.Width-410)<1&&Math.Abs(restart.Height-470)<1,"compact normal bounds did not restore");restart.Close();Pump();ThemePalette.Select(ThemePalette.All[0]);
        Console.WriteLine("PASS responsive stage: actual Linux fallback eight-edge drags/cursors and caption double click/maximize-restore in both modes; debounced per-mode restore/close; monitor/DPI/dialog geometry; six pages and compact reflow/theme/raster/overlay; live task and shared services preserved (not Windows GUI)");
        void DoubleCaption()
        {
            var point=new Point(65,22);owner.MouseMove(point);
            for(var click=0;click<2;click++){owner.MouseDown(point,MouseButton.Left);owner.MouseUp(point,MouseButton.Left);Pump();}
        }
    }
    static void Invoke(Button b){((IInvokeProvider)new ButtonAutomationPeer(b)).Invoke();Pump();}
    static void Capture(Window w,string? folder,string name){if(folder==null)return;Directory.CreateDirectory(folder);using var frame=w.CaptureRenderedFrame();frame!.Save(Path.Combine(folder,name+"-linux-headless.png"));}
    static void Pump(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();}
    static void RunFor(int ms){var end=DateTime.UtcNow.AddMilliseconds(ms);do{using var stop=new CancellationTokenSource(10);Dispatcher.UIThread.MainLoop(stop.Token);Pump();}while(DateTime.UtcNow<end);}
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
}
