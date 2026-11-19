// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XG.Core;
using XGLauncher;
internal static class Phase18UiTests {
 internal static void Run(MainWindow owner,string? shots) {
  Localizer.Set("zh-CN");Pump();
  var personal=owner.GetVisualDescendants().OfType<Button>().First(x=>x.Content is StackPanel row&&row.Children.OfType<TextBlock>().Any(t=>t.Text=="个人游玩"));Invoke(personal);
  var icons=owner.GetVisualDescendants().OfType<Button>().Where(x=>AutomationProperties.GetName(x) is "创建实例" or "导入游戏").ToArray();
  if(icons.Length!=2||icons.Any(x=>x.Content is string||x.Width!=36||x.Height!=36))throw new Exception("personal toolbar is not two pure icons");
  Capture(owner,"phase18-personal-icons");Invoke(icons.Single(x=>AutomationProperties.GetName(x)=="导入游戏"));
  var dialog=owner.OwnedWindows.OfType<Window>().Single(x=>x.Title=="导入游戏");
  if(!dialog.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Any(x=>x.Title=="导入类型"))throw new Exception("wizard type selector missing");
  Capture(dialog,"phase18-import-wizard");dialog.Close();Pump();
  var rail=new StageRail();var window=new Window{Width=700,Height=130,Content=rail};window.Show();Pump();
  StageProgress[] first=[new("游戏本体","下载中",0,40,.5,true),new("Minecraft Assets","等待中",40,70,0,true),new("资源与依赖","等待中",70,95,0,true),new("Java 环境","等待中",95,100,0,true)];
  rail.Update(first,"游戏本体","下载中");Pump();Capture(window,"phase18-stages-current");
  var next=first.ToArray();next[0]=next[0] with {State="已完成",Fraction=1};next[1]=next[1] with {State="下载中",Fraction=.3};rail.Update(next,"Minecraft Assets","下载中");Pump();
  rail.Update(next,"Minecraft Assets","已暂停");Pump();
  var pausedNames=rail.GetVisualDescendants().OfType<TextBlock>().Where(x=>x.IsEffectivelyVisible&&new[]{"游戏本体","Assets","Libraries","Java"}.Contains(x.Text)).ToArray();
  if(pausedNames.Length!=2||pausedNames.GroupBy(x=>x.Text).Any(x=>x.Count()>1))throw new Exception("pausing a stage transition leaves hidden or stale labels");
  rail.Update(next,"Minecraft Assets","下载中");
  using(var until=new CancellationTokenSource(420))Dispatcher.UIThread.MainLoop(until.Token);Pump();Capture(window,"phase18-stages-transition-complete");
  var text=rail.GetVisualDescendants().OfType<TextBlock>().Where(x=>x.IsEffectivelyVisible&&new[]{"游戏本体","Assets","Libraries","Java"}.Contains(x.Text)).ToArray();
  if(text.Length!=2||text.GroupBy(x=>x.Text).Any(x=>x.Count()>1))throw new Exception("stage labels overlap or wrong type size");
  rail.Update(next,"Minecraft Assets","已暂停");rail.Update(next.Select(x=>x with{State="已完成",Fraction=1}).ToArray(),null,"已完成");Pump();Capture(window,"phase18-stages-complete");window.Close();
  Console.WriteLine("PASS phase18 actual personal pure-icon toolbar and import wizard; real weighted relay rail, no unstarted stage labels or duplicate labels and completed state; message divider removed");
  void Pump(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();}
  void Invoke(Button button){((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();Pump();}
  void Capture(Window window,string name){if(shots==null)return;using var frame=window.CaptureRenderedFrame();frame!.Save(System.IO.Path.Combine(shots,name+".png"));}
 }
}
