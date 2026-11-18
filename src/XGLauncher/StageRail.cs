// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using XG.Core;
namespace XGLauncher;
internal sealed class StageRail:Canvas {
 readonly List<Badge> badges=[];readonly Badge moving=new();readonly TranslateTransform motion=new();
 readonly DispatcherTimer finish=new(){Interval=TimeSpan.FromMilliseconds(350)};
 IReadOnlyList<StageProgress> stages=[];int active=-1;int hidden=-1;string state="";string? stage;
 internal StageRail(){Height=56;ClipToBounds=true;moving.RenderTransform=motion;Children.Add(moving);moving.IsVisible=false;SizeChanged+=(_,_)=>LayoutBadges(false);finish.Tick+=(_,_)=>{finish.Stop();hidden=-1;ApplyVisibility();if(active>=0)moving.Caption=StageName(stages[active].Kind);};DetachedFromVisualTree+=(_,_)=>finish.Stop();}
 static string StageName(string kind)=>kind switch {"Minecraft Assets"=>"Assets","资源与依赖"=>"Libraries","模组加载器"=>"加载器","Java 环境"=>"Java",_=>kind};
 internal void Update(IReadOnlyList<StageProgress> values,string? current,string jobState){
  if(jobState=="已暂停" && stages.Count>0) {var frozen=motion.X;motion.Transitions=null;motion.X=frozen;finish.Stop();hidden=-1;ApplyVisibility();if(active>=0)moving.Caption=StageName(stages[active].Kind);return;}
  var previous=active;stages=values;stage=current;state=jobState;
  while(badges.Count>values.Count){Children.Remove(badges[^1]);badges.RemoveAt(badges.Count-1);}
  while(badges.Count<values.Count){var badge=new Badge();badges.Add(badge);Children.Insert(Children.Count-1,badge);}
  active=-1;
  for(var i=0;i<values.Count;i++){var value=values[i];badges[i].Caption=StageName(value.Kind);var done=value.State=="已完成";badges[i].Paint(done?"done":value.State=="失败"?"failed":"waiting");if(value.Kind==current&&!done)active=i;}
  if(jobState is "已完成" or "已取消")active=-1;
  if(active>=0){moving.Paint(jobState=="失败"?"failed":jobState is "下载中" or "校验中" or "安装中"?"active":"waiting");
   if(previous!=active){finish.Stop();hidden=previous>=0&&previous<values.Count?previous:-1;
    if(!Motion.Enabled||previous<0){moving.Caption=StageName(values[active].Kind);hidden=-1;}else finish.Start();
   }else if(!finish.IsEnabled)moving.Caption=StageName(values[active].Kind);
  }else{finish.Stop();hidden=-1;}
  LayoutBadges(previous>=0 && previous!=active && jobState!="已暂停",previous==active && finish.IsEnabled);ApplyVisibility();
 }
 void ApplyVisibility(){for(var i=0;i<badges.Count;i++)badges[i].IsVisible=i!=active&&i!=hidden;moving.IsVisible=active>=0;}
 void LayoutBadges(bool animate,bool preserve=false){if(badges.Count==0||Bounds.Width<=0)return;var slot=Bounds.Width/badges.Count;
  for(var i=0;i<badges.Count;i++){badges[i].Width=Math.Max(1,slot-6);SetLeft(badges[i],i*slot+3);SetTop(badges[i],2);}
  moving.Width=Math.Max(1,slot-6);SetTop(moving,2);
  if(preserve)return;
  motion.Transitions=animate && Motion.Enabled?[new DoubleTransition{Property=TranslateTransform.XProperty,Duration=TimeSpan.FromMilliseconds(350),Easing=new CubicEaseInOut()}]:null;
  if(active>=0)motion.X=active*slot+3;
 }
 sealed class Badge:StackPanel {
  readonly LucideIcon icon=new("circle",18);
  readonly TextBlock name=new(){FontSize=14,FontWeight=FontWeight.Medium,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap};
  internal string Caption{set=>name.Text=value;}
  string currentPaint="waiting";
  void ThemeChanged(object? sender,AvaloniaPropertyChangedEventArgs e)=>Paint(currentPaint);
  internal Badge(){AttachedToVisualTree+=(_,_)=>ThemePalette.Text.PropertyChanged+=ThemeChanged;DetachedFromVisualTree+=(_,_)=>ThemePalette.Text.PropertyChanged-=ThemeChanged;Spacing=4;icon.HorizontalAlignment=HorizontalAlignment.Center;Children.Add(icon);Children.Add(name);}
  internal void Paint(string status){currentPaint=status;var green=new SolidColorBrush(Color.Parse(ThemePalette.Current.Dark?"#6CCF96":"#278453"));var red=new SolidColorBrush(Color.Parse(ThemePalette.Current.Dark?"#FF9292":"#B83232"));var color=status switch{"done"=>green,"failed"=>red,"active"=>ThemePalette.Accent,_=>ThemePalette.Muted};icon.Icon=status=="done"?"circle-check":status=="failed"?"circle-exclamation-point":"circle";icon.Foreground=color;name.Foreground=color;}
 }
}
