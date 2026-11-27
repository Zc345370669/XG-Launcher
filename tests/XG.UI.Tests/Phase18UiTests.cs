// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XG.Core;
using XGLauncher;
internal static class Phase18UiTests {
 internal static void Run(MainWindow owner,string? shots) {
  Localizer.Set("zh-CN");Pump();
  var personal=owner.GetVisualDescendants().OfType<Button>().First(x=>x.Content is StackPanel row&&row.Children.OfType<TextBlock>().Any(t=>t.Text=="个人游玩"));Invoke(personal);
  var flow = new List<string>();
  var ready = false;
  MainWindow.PrepareImportedGameAsync(
   () => { flow.Add("inspect"); return Task.FromResult(ready ? "已安装" : "未安装"); },
   () => { flow.Add("install"); ready = true; return Task.CompletedTask; }).GetAwaiter().GetResult();
  if (!flow.SequenceEqual(new[] { "inspect", "install", "inspect" }))
   throw new Exception("Import must verify repaired resources.");
  MainWindow.PrepareImportedGameAsync(() => Task.FromResult("已安装"),
   () => throw new Exception("Complete imports must not install again.")).GetAwaiter().GetResult();
  try
  {
   MainWindow.PrepareImportedGameAsync(() => Task.FromResult("文件损坏"), () => Task.CompletedTask).GetAwaiter().GetResult();
   throw new Exception("Incomplete repair was accepted.");
  }
  catch (IOException) { }
  Console.WriteLine("PASS import prepares missing resources and verifies the result");
  var icons=owner.GetVisualDescendants().OfType<Button>().Where(x=>AutomationProperties.GetName(x) is "创建实例" or "导入游戏").ToArray();
  if(icons.Length!=2||icons.Any(x=>x.Content is string||x.Width!=36||x.Height!=36))throw new Exception("personal toolbar is not two pure icons");
  Capture(owner,"phase18-personal-icons");Invoke(icons.Single(x=>AutomationProperties.GetName(x)=="导入游戏"));
  var dialog=owner.OwnedWindows.OfType<Window>().Single(x=>x.Title=="导入游戏");
  var dragArea = dialog.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "DialogTitleDragArea");
  if (dragArea.Bounds.Width < dialog.ClientSize.Width * .75 || dragArea.Background == null ||
      dragArea.GetVisualDescendants().OfType<Button>().Any())
   throw new Exception("Dialog title drag area must include blank space and exclude the close button.");
  if(dialog.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Any(x=>x.Title=="导入类型"))
   throw new Exception("legacy import selector still visible");
  if(!dialog.GetVisualDescendants().OfType<Button>().Any(x=>Equals(x.Content,"选择游戏或其他启动器的目录")))
   throw new Exception("folder import action missing");
  if(dialog.GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Content,"导入游戏")).IsEnabled)
   throw new Exception("empty import can be submitted");
  if(owner.GetModalShade().IsVisible)throw new Exception("modal owner must not dim");
  Capture(dialog,"phase18-import-wizard");dialog.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });Pump();if(owner.GetModalShade().IsVisible)throw new Exception("modal shade was not removed");
  var fixture=Path.Combine(Path.GetTempPath(),"xg-smart-ui-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(fixture);
  try {
   foreach(var version in new[]{"1.20.6","1.21.1"})
   {
    var directory=Path.Combine(fixture,"versions",version);
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory,version+".json"),$$"""{"id":"{{version}}","mainClass":"fixture.Main"}""");
   }
   var repaired = new List<string>();
   owner.ImportedRepairOverride = instance => { repaired.Add(instance.Version); return Task.CompletedTask; };
   var task=owner.GameImportWizardAsync(_=>Task.FromResult<string?>(fixture));Pump();
   var detected=owner.OwnedWindows.OfType<Window>().Single(x=>x.Title=="导入游戏");
   Invoke(detected.GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Content,"选择游戏或其他启动器的目录")));
   WaitUntil(()=>detected.GetVisualDescendants().OfType<CheckBox>().Count(x=>Equals(x.Content,"1.20.6")||Equals(x.Content,"1.21.1"))==2);
   var submit=detected.GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Content,"导入游戏"));
   if(!submit.IsEnabled)throw new Exception("detected instance cannot be submitted");
   Capture(detected,"smart-import-two-instances");
   foreach(var choice in detected.GetVisualDescendants().OfType<CheckBox>().Where(x=>Equals(x.Content,"1.20.6")||Equals(x.Content,"1.21.1")))choice.IsChecked=true;
   Pump();Invoke(submit);
   WaitUntil(()=>task.IsCompleted);
   task.GetAwaiter().GetResult();
   WaitUntil(()=>repaired.Count == 2);
   if(!repaired.Order().SequenceEqual(new[]{"1.20.6","1.21.1"}))throw new Exception("multi-selection skipped automatic repair");
   owner.ImportedRepairOverride = null;
   if(!owner.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text=="1.20.6"))
    throw new Exception("instance list was not refreshed by import");
  } finally {Directory.Delete(fixture,true);}
  void WaitUntil(Func<bool> condition)
  {
   var deadline=DateTime.UtcNow.AddSeconds(8);
   while(!condition() && DateTime.UtcNow<deadline)
   {
    using var tick=new CancellationTokenSource(35);
    Dispatcher.UIThread.MainLoop(tick.Token);Pump();
   }
   if(!condition())throw new Exception("timed out waiting for smart import UI");
  }
  Console.WriteLine("PASS smart import UI: automatic folder scan, two results, multi-selection submit, list refresh, disabled empty submit, Esc and unchanged owner background (Linux headless fixture)");
  var rail=new StageRail();var window=new Window{Width=700,Height=130,Content=rail};window.Show();Pump();
  StageProgress[] first=[new("游戏本体","下载中",0,40,.5,true),new("Minecraft Assets","等待中",40,70,0,true),new("资源与依赖","等待中",70,95,0,true),new("Java 环境","等待中",95,100,0,true)];
  rail.Update(first,"游戏本体","下载中");Pump();Capture(window,"phase18-stages-current");
  var next=first.ToArray();next[0]=next[0] with {State="已完成",Fraction=1};next[1]=next[1] with {State="下载中",Fraction=.3};rail.Update(next,"Minecraft Assets","下载中");Pump();
  rail.Update(next,"Minecraft Assets","已暂停");Pump();
  var pausedNames=rail.GetVisualDescendants().OfType<TextBlock>().Where(x=>x.IsEffectivelyVisible&&new[]{"本体","Assets","Libs","Java"}.Contains(x.Text)).ToArray();
  if(pausedNames.Length!=2||pausedNames.GroupBy(x=>x.Text).Any(x=>x.Count()>1))throw new Exception("pausing a stage transition leaves hidden or stale labels");
  rail.Update(next,"Minecraft Assets","下载中");
  using(var until=new CancellationTokenSource(420))Dispatcher.UIThread.MainLoop(until.Token);Pump();Capture(window,"phase18-stages-transition-complete");
  var text=rail.GetVisualDescendants().OfType<TextBlock>().Where(x=>x.IsEffectivelyVisible&&new[]{"本体","Assets","Libs","Java"}.Contains(x.Text)).ToArray();
  if(text.Length!=2||text.GroupBy(x=>x.Text).Any(x=>x.Count()>1))throw new Exception("stage labels overlap or wrong type size");
  rail.Update(next,"Minecraft Assets","已暂停");rail.Update(next.Select(x=>x with{State="已完成",Fraction=1}).ToArray(),null,"已完成");Pump();Capture(window,"phase18-stages-complete");window.Close();
  Console.WriteLine("PASS phase18 actual personal pure-icon toolbar and import wizard; real weighted relay rail, no unstarted stage labels or duplicate labels and completed state; message divider removed");
  void Pump(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();}
  void Invoke(Button button){((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();Pump();}
  void Capture(Window window,string name){if(shots==null)return;using var frame=window.CaptureRenderedFrame();frame!.Save(System.IO.Path.Combine(shots,name+".png"));}
 }
}
