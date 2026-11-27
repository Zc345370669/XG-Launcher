// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XGLauncher;
internal static class IntegratedUiTests {
 internal static void Run(string root) {
  var owner=new MainWindow(Path.Combine(root,"integrated-ui"));owner.Show();Pump();
  var selector=owner.GetVisualDescendants().OfType<UnifiedExpandableSelect>().Single(x=>x.Name=="CaptionAccounts");
  Check(selector.IconOnly && selector.AuxiliaryIcon==null,"full account has separate add button");
  var account = selector.GetVisualDescendants().OfType<Button>().Single(x => x.IsEffectivelyVisible);
  Check(account.GetVisualDescendants().OfType<LucideIcon>().Select(x=>x.Icon).SequenceEqual(new[]{"user-round","chevron-down"}) &&
      !account.GetVisualDescendants().OfType<TextBlock>().Any(), "caption displays more than the user icon and arrow");
  var hit = account.TranslatePoint(new Point(account.Bounds.Width - 8, account.Bounds.Height / 2), owner)!.Value;
  owner.MouseDown(hit, Avalonia.Input.MouseButton.Left); owner.MouseUp(hit, Avalonia.Input.MouseButton.Left); Pump();
  Check(selector.IsOpen && !owner.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.IsEffectivelyVisible && x.Text=="无可选项"), "empty accounts has a placeholder");
  Capture(owner, "full-empty-account");
  Invoke(owner.GetVisualDescendants().OfType<Button>().Single(x=>x.Name=="ComboFooter"));
  Check(selector.IsAuxiliaryOpen&&!owner.OwnedWindows.Any(),"footer did not open inline form");
  Check(owner.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "CompactMicrosoftLogin").IsEnabled &&
      !owner.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "CompactOfflineName").IsEffectivelyVisible,
      "add account must offer Microsoft and a collapsed offline editor");
  Capture(owner, "full-add-account");
  Invoke(owner.GetVisualDescendants().OfType<Button>().Single(x=>x.Name=="CompactOfflineOption"));
  owner.GetVisualDescendants().OfType<TextBox>().Single(x=>x.Name=="CompactOfflineName").Text="UnifiedPlayer";
  Invoke(owner.GetVisualDescendants().OfType<Button>().Single(x=>x.Name=="CompactAddOffline"));
  for(var i=0;i<200 && selector.IsOpen;i++){Pump();Thread.Sleep(5);}Check(!selector.IsOpen&&selector.SelectedItem is LauncherAccount{Name:"UnifiedPlayer"},"inline account persistence");
  owner.ToggleMode();Pump();Check(selector.IconOnly&&selector.AuxiliaryIcon==null&&selector.Bounds.Width==38,"compact account is not one caption icon");
  Capture(owner, "compact-empty-instances");
  selector.Open();Pump();var captionPoint=selector.TranslatePoint(default,owner)!.Value;Check(selector.ExpansionBounds is {} bounds&&bounds.Top>=captionPoint.Y+selector.Bounds.Height-1,"compact popup covers caption controls");selector.Close();Pump();
  var background=owner.Background;var opacity=owner.Opacity;var dialog=new LauncherDialog("验证",owner,false);dialog.SetBody(new TextBlock{Text="模态测试"});var modalTask=dialog.ShowDialog(owner);Pump();
  Check(!owner.GetModalShade().IsVisible&&ReferenceEquals(background,owner.Background)&&owner.Opacity==opacity,"dialog changed owner colors");
  Check(ReferenceEquals(dialog.Owner,owner)&&!modalTask.IsCompleted&&owner.OwnedWindows.Contains(dialog),"dialog did not retain native modal owner blocking");dialog.Close();Pump();
  var filter=new UnifiedExpandableSelect{Title="排序",AutoContentWidth=true,PopupMinimumWidth=170,ItemsSource=new[]{"相关性最高","下载量最多","更新时间最新"},SelectedItem="相关性最高"};
  var host=new Window{Width=240,Height=220,Content=filter};host.Show();Pump();var shortWidth=filter.Width;filter.SelectedItem="更新时间最新";Pump();Check(filter.Width>=shortWidth,"filter does not measure current text");
  foreach(var width in new[]{240d,400,700}){host.Width=width;Pump();filter.Open();Pump();Check(filter.ExpansionBounds is {} b&&b.Width>=170&&b.Right<=host.ClientSize.Width,"option panel clipping");filter.Close();}
  Localizer.Set("en-US");Pump();Check(filter.Width>100,"language relayout failed");Localizer.Set("zh-CN");
  host.Close();owner.Close();Pump();Console.WriteLine("PASS unified caption account footer/add/select, compact icon-only access, unchanged modal background with pending modal ownership, and adaptive filter popup width at narrow/wide sizes (Linux headless)");
 }
 static void Capture(Window window, string name)
 {
  if (Environment.GetEnvironmentVariable("XG_UI_SCREENSHOTS") is not { } output) return;
  Directory.CreateDirectory(output);
  using var frame = window.CaptureRenderedFrame();
  frame!.Save(Path.Combine(output, name + "-linux-headless.png"));
 }
 static void Invoke(Button b){b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
 static void Pump(){for(var i=0;i<5;i++){Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();}}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
