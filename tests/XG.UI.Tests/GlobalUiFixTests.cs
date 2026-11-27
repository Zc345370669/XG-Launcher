// SPDX-License-Identifier: MIT
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XG.Core;
using XGLauncher;

internal static class GlobalUiFixTests
{
    internal static void Run(string root, string? shots)
    {
        root = Path.Combine(root, "global-ui"); Directory.CreateDirectory(root);
        InstanceStore.WriteJson(Path.Combine(root,"official-versions.json"), new[] { new GameService.OfficialVersion("1.21.1","release"), new GameService.OfficialVersion("1.20.1","release") });
        using var fixture = new Stalled(); using var http = new HttpClient(fixture);
        File.WriteAllText(Path.Combine(root, "appearance.json"), """{"Theme":"深海蓝","Opacity":0.85,"Blur":true}""");
        var owner = new MainWindow(root, null, http) { FontFamily = new FontFamily("Noto Sans CJK SC") }; owner.Show(); Pump(); owner.ToggleMode(); Pump();
        var model = (LauncherModel)typeof(MainWindow).GetField("model",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(owner)!;
        var combo = Find<UnifiedExpandableSelect>(owner,"CompactInstances");
        Check(combo.Title == "下载游戏实例" && combo.PlaceholderText == "选择游戏版本并安装", "empty game header contract wrong");
        foreach (var size in new[] { new Size(320, 330), new Size(360, 400), new Size(500, 570) })
        {
            owner.Width = size.Width; owner.Height = size.Height; Pump();
            var logo = Find<Image>(owner, "CompactOfficialLogo");
            var title = combo.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == "下载游戏实例");
            var subtitle = combo.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == "选择游戏版本并安装");
            var logoBottom = logo.TranslatePoint(new Point(0, logo.Bounds.Height), owner)!.Value.Y;
            var comboTop = combo.TranslatePoint(default, owner)!.Value.Y;
            var titleBottom = title.TranslatePoint(new Point(0, title.Bounds.Height), owner)!.Value.Y;
            var subtitleTop = subtitle.TranslatePoint(default, owner)!.Value.Y;
            Check(comboTop - logoBottom is >= 8 and <= 20 && subtitleTop >= titleBottom && title.FontSize > subtitle.FontSize &&
                combo.Bounds.Width >= size.Width - 44, "compact logo, version text or responsive width is misaligned");
        }
        owner.Width = 360; owner.Height = 400; Pump();
        Invoke(combo.GetVisualDescendants().OfType<Button>().First()); Pump();
        Check(owner.IsCompact && combo.IsAuxiliaryOpen && !owner.OwnedWindows.Any(), "compact installer navigated or opened dialog");
        Check(owner.Opacity==1,"whole-scene opacity makes popup contents bleed");
        var versions = Find<UnifiedExpandableSelect>(owner,"CompactDownloadVersions");
        versions.Open(); Pump(); Check(combo.IsAuxiliaryOpen && versions.IsOpen, "nested version selector closed download form");
        owner.KeyPressQwerty(PhysicalKey.Escape,RawInputModifiers.None); Pump();
        Check(combo.IsAuxiliaryOpen && !versions.IsOpen,"nested Escape closed parent installer");
        var loader = Find<UnifiedExpandableSelect>(owner,"CompactDownloadLoader"); loader.Open(); Pump();
        Check(loader.IsOpen && combo.IsOpen,"loader selector closed parent"); loader.Close();
        Capture(owner,"global-inline-download");
        Invoke(Find<Button>(owner,"CompactDownloadInstall"));
        Wait(()=>model.Resources.MainJobs.Any(x=>model.Resources.CanControl(x.Id))&&fixture.Requests>0);
        Check(owner.IsCompact && !owner.OwnedWindows.Any() && model.Store.Entries.Count==1 && !Find<Button>(owner,"CompactPrimary").IsVisible,"inline install bypassed service or showed incomplete launch target");
        var first = model.Resources.MainJobs.Single(); var ids=model.Resources.Items.Select(x=>x.Id).ToArray();
        var again=(Task)typeof(MainWindow).GetMethod("StartCompactDownloadAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(owner,null)!;
        Check(again.IsCompletedSuccessfully && model.Resources.MainJobs.Count==1,"repeated inline install created duplicate job");
        owner.ToggleMode(); owner.ToggleMode(); Pump(); Check(model.Resources.Items.Select(x=>x.Id).SequenceEqual(ids),"mode switch duplicated install");
        Check(Find<Control>(owner,"CompactProgress").IsVisible,"real task did not reach compact progress"); Capture(owner,"global-compact-active-download");
        model.Resources.Cancel(first.Id); Wait(()=>!model.Busy.Any() && model.Resources.MainJobs.Single().State=="已取消");
        Check(!Find<Control>(owner,"CompactProgress").IsVisible && !Find<Button>(owner,"CompactPrimary").IsVisible,"cancelled incomplete game shown as installed");
        fixture.Fail=true;
        typeof(MainWindow).GetField("compactDownloadVersion",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(owner,"1.20.1");
        var failedInstall=(Task)typeof(MainWindow).GetMethod("StartCompactDownloadAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(owner,null)!;
        Wait(()=>failedInstall.IsCompleted);
        Check(model.Resources.MainJobs.Last().State=="失败" && !Find<Button>(owner,"CompactPrimary").IsVisible,"failed HTTP install enabled game launch");

        // Only the extra percentage fill is removed from StageRail. Its track/circle/name stay intact.
        var rail = new StageRail(); var railHost=new Window { Width=500,Height=100,Content=rail }; railHost.Show();
        rail.Update([new("游戏本体","下载中",0,70,.5,true)],"游戏本体","下载中",35); Pump();
        Check(rail.GetVisualDescendants().OfType<Border>().Count(x=>x.Name=="StageTrack")==1 && rail.GetVisualDescendants().OfType<LucideIcon>().Any(x=>x.Icon=="circle"&&x.IsVisible) && rail.GetVisualDescendants().OfType<TextBlock>().Single(x=>x.Name=="StageCaption").Text=="本体","duplicate removal damaged stage track/circle/name");
        var circle=rail.GetVisualDescendants().OfType<LucideIcon>().Single(x=>x.Icon=="circle"); var x=circle.TranslatePoint(default,rail)!.Value.X;
        rail.Update([new("游戏本体","下载中",0,70,.8,true)],"游戏本体","下载中",56); Pump(); using(var until=new CancellationTokenSource(260))Dispatcher.UIThread.MainLoop(until.Token); Pump(); Check(circle.TranslatePoint(default,rail)!.Value.X>x,"stage circle no longer moves left to right"); railHost.Close();

        // Actual pointer hover/press coverage against Fluent template parts.
        var button=new Button { Width=160,Height=44,Content="填充测试",CornerRadius=new(8),Background=ThemePalette.Panel,BorderThickness=new(1),BorderBrush=ThemePalette.Line };
        button.Classes.Add("xg-action"); var fillHost=new Window { Width=200,Height=80,Content=button }; fillHost.Show(); Pump();
        var at=button.TranslatePoint(new Point(80,22),fillHost)!.Value; fillHost.MouseMove(at); Pump();
        var presenter=button.GetVisualDescendants().OfType<ContentPresenter>().Single(x=>x.Name=="PART_ContentPresenter");
        Check(ReferenceEquals(presenter.Background,ThemePalette.Hover)&&presenter.BackgroundSizing==BackgroundSizing.OuterBorderEdge && presenter.CornerRadius==button.CornerRadius,"hover paint not themed/full radius");
        foreach (var scale in new[] { 1d,1.25,1.5,1.75,2 })
        {
            using var raster=new RenderTargetBitmap(new PixelSize((int)(160*scale),(int)(44*scale)),new Vector(96*scale,96*scale)); raster.Render(button);
            using var buffer=new MemoryStream();raster.Save(buffer);buffer.Position=0;using var bitmap=SkiaSharp.SKBitmap.Decode(buffer);
            foreach(var point in new[] {new Point(3,22),new Point(80,3),new Point(156,22),new Point(80,40),new Point(8,3)})
            {
                var pixel=bitmap.GetPixel((int)(point.X*scale),(int)(point.Y*scale));var expected=ThemePalette.Hover.Color;
                Check(Math.Abs(pixel.Red-expected.R)<3&&Math.Abs(pixel.Green-expected.G)<3&&Math.Abs(pixel.Blue-expected.B)<3,"hover fill has edge gap at DPI "+scale);
            }
        }
        Capture(fillHost,"global-hover-fill");
        fillHost.MouseDown(at,MouseButton.Left); Pump();
        Check(button.RenderTransform?.Value==Matrix.Identity&&presenter.Bounds.Size==button.Bounds.Size,"pressed scale leaves outer gap");
        fillHost.MouseUp(at,MouseButton.Left); fillHost.Close();

        owner.ToggleMode(); Pump(); Invoke(owner.GetVisualDescendants().OfType<Button>().Single(x=>AutomationProperties.GetName(x)=="设置"));
        var settings=owner.GetVisualDescendants().OfType<SettingsSurface>().Single(); settings.ScrollToSection(1,false); Pump();
        var backdrop=Find<Border>(owner,"WindowShell");
        Check(ThemePalette.Current.Dark, "legacy theme was not restored");
        foreach(var palette in ThemePalette.All)
        {
            ThemePalette.Select(palette);
            for (var mode = 0; mode < 2; mode++)
            {
                Pump();
                var color = ((ISolidColorBrush)backdrop.Background!).Color;
                Check(color == Color.Parse(palette.Background) && ((ISolidColorBrush)owner.Background!).Color == color &&
                    owner.Opacity == 1 && owner.TransparencyLevelHint.SequenceEqual(new[] { WindowTransparencyLevel.None }),
                    "legacy appearance or mode switch left the client area translucent");
                if (palette == ThemePalette.All[0] || palette.Dark)
                {
                    foreach (var scale in new[] { 1d, 1.25, 1.5, 1.75, 2 })
                    {
                        var pixels = new PixelSize((int)(owner.ClientSize.Width * scale), (int)(owner.ClientSize.Height * scale));
                        using var frame = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
                        frame.Render(owner);
                        using var buffer = new MemoryStream();
                        frame.Save(buffer); buffer.Position = 0;
                        using var bitmap = SkiaSharp.SKBitmap.Decode(buffer);
                        foreach (var point in new[] { new Point(6, 20), new Point(6, 80),
                            new Point(owner.ClientSize.Width - 5, owner.ClientSize.Height - 5) })
                            Check(bitmap.GetPixel((int)(point.X * scale), (int)(point.Y * scale)).Alpha == 255,
                                $"rendered client area lost opacity at raster scale {scale}");
                    }
                }
                owner.ToggleMode();
            }
        }

        var legal=owner.GetVisualDescendants().OfType<LegalBrowser>().Single(); legal.ShowCredits();
        settings.ScrollToSection(5,false); Pump(); var creditScroll=legal.CreditsScroll!;
        Check(!legal.GetVisualDescendants().OfType<Button>().Any(b=>AutomationProperties.GetName(b) is "向左滚动" or "向右滚动"),"credits still has arrow buttons");
        foreach(var tile in legal.GetVisualDescendants().OfType<Button>().Where(b=>LegalCatalog.Credits.Any(c=>c.Name==AutomationProperties.GetName(b))))
        {
            Check(tile.BorderThickness==new Thickness(0)&&tile.GetVisualDescendants().OfType<LucideIcon>().Any(),"credit tile border/logo not removed");
        }
        Wait(()=>creditScroll.Viewport.Width>0&&creditScroll.Extent.Width>creditScroll.Viewport.Width);
        var wheel=creditScroll.TranslatePoint(new Point(80,40),owner)!.Value;
        var before=creditScroll.Offset.X; owner.MouseWheel(wheel,new Vector(0,-2)); Pump(); Check(creditScroll.Offset.X>before,"normal credit wheel not horizontal");
        var bars=creditScroll.GetVisualDescendants().OfType<ScrollBar>().Where(b=>ReferenceEquals(b.TemplatedParent,creditScroll)).ToArray();
        Check(bars.All(b=>b.Classes.Contains("xg-idle-bar")),"global scrollbar controller missing");
        Wait(()=>bars.Any(b=>b.Opacity>.9),1000);
        Wait(()=>bars.All(b=>b.Opacity<.01),4000);
        Check(IdleScrollbar.IdleDelay==TimeSpan.FromMilliseconds(1500),"scroll fade deadline not 1.5 seconds");
        Capture(owner,"global-credits-no-cards"); owner.Close(); Pump();
        var restored=new MainWindow(root); restored.Show(); Pump();
        Check(restored.Opacity == 1 && ((ISolidColorBrush)restored.Background!).Color.A == 255,"restart applied legacy opacity"); restored.Close(); Pump();
        Console.WriteLine("PASS global UI real inline installer/shared HTTP task/dedup/cancel, nested selectors/Escape, stage track circle/name retained without duplicate fill, pointer hover/press full-round fill/no scale, legacy transparency ignored across themes, modes and restart, frameless Lucide credits horizontal wheel and global 1.5s idle scrollbar (Linux headless)");
        void Capture(Window window,string name) { if(shots==null)return;Directory.CreateDirectory(shots);using var f=window.CaptureRenderedFrame();f!.Save(Path.Combine(shots,name+"-linux-headless.png")); }
    }
    sealed class Stalled:HttpMessageHandler
    {
        internal int Requests; internal bool Fail;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        { Interlocked.Increment(ref Requests); if(Fail)return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable); await Task.Delay(Timeout.InfiniteTimeSpan,token);throw new Exception("unreachable"); }
    }
    static T Find<T>(Window w,string n) where T:Control=>w.GetVisualDescendants().OfType<T>().Single(x=>x.Name==n);
    static void Invoke(Button b){((IInvokeProvider)new ButtonAutomationPeer(b)).Invoke();Pump();}
    static void Pump(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();}
    static void Check(bool value,string error){if(!value)throw new Exception(error);}
    static void Wait(Func<bool> ready,int ms=15000){var end=DateTime.UtcNow.AddMilliseconds(ms);while(!ready()&&DateTime.UtcNow<end){Pump();using var stop=new CancellationTokenSource(15);Dispatcher.UIThread.MainLoop(stop.Token);}Pump();Check(ready(),"global UI wait timed out");}
}
