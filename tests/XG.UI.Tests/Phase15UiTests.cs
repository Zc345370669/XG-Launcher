// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XGLauncher;
internal static class Phase15UiTests {
 internal static void Run(MainWindow owner,string? screenshots) {
  Localizer.Set("zh-CN");
  foreach(var text in new[]{"操作成功。",string.Join(" ",Enumerable.Repeat("网络连接失败，请检查网络配置。 Network request failed.",25))}) {
   var dialog=new LauncherDialog("操作提示",owner,false);
   dialog.SetBody(new TextBlock{Text=text,TextWrapping=Avalonia.Media.TextWrapping.Wrap},new Button{Content="关闭"});dialog.Show();Pump();
   if(dialog.GetVisualDescendants().OfType<Border>().Single(x=>x.Name=="DialogFooter").BorderThickness.Top!=0)throw new Exception("message footer divider remains");
   if(dialog.GetVisualDescendants().OfType<ScrollViewer>().Any())throw new Exception("ordinary message contains scroll viewer");
   if(dialog.Bounds.Width>560||dialog.Bounds.Height>dialog.MaxHeight)throw new Exception("message outside limits");
   if(screenshots!=null){using var frame=dialog.CaptureRenderedFrame();frame!.Save(Path.Combine(screenshots,text.Length<30?"phase15-short-message.png":"phase15-long-message.png"));}
   dialog.Close();
  }
  var model = (LauncherModel)typeof(MainWindow).GetField("model",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(owner)!;
  if(model.Store.Entries.FirstOrDefault() is {} instance) {
   var request = (Task)typeof(MainWindow).GetMethod("DeleteInstanceAsync",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(owner,[instance])!;
   Pump(); var confirmation=owner.OwnedWindows.OfType<LauncherDialog>().Single();
   var bodyText=confirmation.GetVisualDescendants().OfType<TextBlock>().First(x=>x.Text?.Contains("可保留现有文件") == true);
   if(screenshots!=null){using var diagnostic=confirmation.CaptureRenderedFrame();diagnostic!.Save(Path.Combine(screenshots,"phase16-delete-diagnostic.png"));}
   if(bodyText.Bounds.Width > confirmation.Bounds.Width - 30 || bodyText.Bounds.Height <= 0 || bodyText.Bounds.Height + .5 < bodyText.DesiredSize.Height)throw new Exception($"delete confirmation body clipped: bounds={bodyText.Bounds}, desired={bodyText.DesiredSize}");
   if(confirmation.Footer.Children.Count!=3)throw new Exception("delete actions not in common footer");
   if(screenshots!=null){using var frame=confirmation.CaptureRenderedFrame();frame!.Save(Path.Combine(screenshots,"phase16-delete-confirmation.png"));}
   confirmation.Close();Pump();if(!request.IsCompletedSuccessfully||!model.Store.Entries.Any(x=>x.Id==instance.Id))throw new Exception("closing delete confirmation changed data");
  }
  Console.WriteLine("PASS phase15 actual compact short and long wrapped message windows without ordinary ScrollViewer and within size limits");
  void Pump(){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();}
 }
}
