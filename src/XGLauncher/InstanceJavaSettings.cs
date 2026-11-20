// SPDX-License-Identifier: MIT
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using XG.Core;
namespace XGLauncher;
public sealed partial class MainWindow {
 private Control InstanceJavaSettings(LauncherDialog dialog,UnifiedExpandableSelect versions,UnifiedExpandableSelect mode,TextBox manual,CancellationToken lifetime) {
  const string automatic="自动选择（推荐）";
  var status=Label("");var candidates=new Dictionary<string,JavaInstallation>(StringComparer.OrdinalIgnoreCase);
  var selector = new UnifiedExpandableSelect { Title="Java 运行环境", Searchable=true,
      ItemsSource=new[]{automatic}.Concat(string.IsNullOrWhiteSpace(manual.Text)?Array.Empty<string>():[manual.Text!]).ToArray(),
      DisplayMember=value=>value is string path && candidates.TryGetValue(path,out var runtime) ? $"Java {runtime.Major} · {runtime.Version} · {runtime.Architecture} · {runtime.Vendor} · {path}" : value?.ToString() ?? "" };
  selector.SelectedIndex=mode.SelectedIndex==1?1:0;
  CancellationTokenSource? scanning=null;var updating=false;
  var cache=Path.Combine(model.Store.Root,"java-catalog.json");
  string[] Saved(){try{return System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(cache))??[];}catch(Exception e)when(e is IOException or System.Text.Json.JsonException){return [];}}
  void Selection(){if(updating)return;var value=selector.SelectedItem as string;mode.SelectedIndex=value==automatic?0:1;if(mode.SelectedIndex==1)manual.Text=value;}
  selector.SelectionChanged+=(_,_)=>Selection();
  async Task Detect(bool deep=false,string[]? extra=null) {
   scanning?.Cancel();scanning=CancellationTokenSource.CreateLinkedTokenSource(lifetime);var token=scanning.Token;
   status.Text="正在检测 Java…";
   try {
    var values=await JavaDiscovery.DiscoverAsync(model.Store.ManagedJavaRoot,extra??Saved(),token,new Progress<string>(p=>{if(!token.IsCancellationRequested)status.Text=p;}),deep);
    token.ThrowIfCancellationRequested();
    candidates.Clear(); foreach(var value in values)candidates[value.Path]=value;
    InstanceStore.WriteJson(cache,candidates.Keys.ToArray());
    var chosen=mode.SelectedIndex==1?manual.Text:automatic;
    updating=true;selector.ItemsSource=new[]{automatic}.Concat(candidates.Keys).Concat(chosen==automatic||string.IsNullOrWhiteSpace(chosen)?[]:new[]{chosen!}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();selector.SelectedItem=chosen;updating=false;
    if(versions.SelectedItem is not string game){status.Text="请先选择 Minecraft 版本。";return;}
    var major=await model.Game.RequiredJavaMajorAsync(game,token);token.ThrowIfCancellationRequested();
    var selected=mode.SelectedIndex==1 ? await JavaDiscovery.InspectPathAsync(manual.Text??"",token) : await JavaDiscovery.CompatibleAsync(null,model.Store.ManagedJavaRoot,major,cancellationToken:token);
    token.ThrowIfCancellationRequested();
    status.Text=selected!=null&&JavaDiscovery.IsCompatible(selected,major)?$"Java {selected.Major} · {selected.Architecture} · 已验证\n{selected.Path}":"未发现兼容 Java。启动时将通过下载管理获取 Mojang 官方运行时。";
   }catch(OperationCanceledException){if(!lifetime.IsCancellationRequested)status.Text="已取消";}catch(Exception e)when(e is IOException or HttpRequestException or InvalidOperationException){status.Text=new XG.Core.GameLogSanitizer(null).Redact(e.Message);}
  }
  var buttons=new WrapPanel{ItemSpacing=12,LineSpacing=8};
  buttons.Children.Add(Action("重新检测",()=>Detect()));
  buttons.Children.Add(Action("手动添加",async()=>{var files=await dialog.StorageProvider.OpenFilePickerAsync(new(){AllowMultiple=false});if(files.FirstOrDefault()?.TryGetLocalPath() is {} path){manual.Text=path;mode.SelectedIndex=1;await Detect(false,Saved().Append(path).ToArray());}}));
  buttons.Children.Add(Action("深度扫描",async()=>{var folders=await dialog.StorageProvider.OpenFolderPickerAsync(new(){AllowMultiple=true});var paths=folders.Select(x=>x.TryGetLocalPath()).OfType<string>().ToArray();if(paths.Length>0)await Detect(true,paths);}));
  buttons.Children.Add(Action("取消",()=>{scanning?.Cancel();return Task.CompletedTask;}));
  versions.SelectionChanged+=async(_,_)=>await Detect();dialog.Opened+=async(_,_)=>await Detect();dialog.Closed+=(_,_)=>scanning?.Cancel();
  return Column(Label("Java 运行环境",14,true),selector,status,buttons);
 }
}
