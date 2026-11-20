// SPDX-License-Identifier: MIT
using System.IO.Compression;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Media;
using XG.Core;
namespace XGLauncher;
public sealed partial class MainWindow {
 private Button PersonalIcon(string icon,string hint,Func<Task> action) {
  var button=Action("",action);button.Width=36;button.Height=36;button.Padding=new(8);
  button.Content=new LucideIcon(icon,18);
  ToolTip.SetTip(button,hint);Avalonia.Automation.AutomationProperties.SetName(button,hint);return button;
 }
 private async Task GameImportWizardAsync() {
  var dialog=Dialog("导入游戏",editor:true);using var cancel=new CancellationTokenSource();
  var type=Picker(["整合包","已有 Minecraft 游戏","其他启动器实例","模组 JAR"],title:"导入类型");
  var source=Input("文件或目录");var name=Input("新实例名称");var version=Input("Minecraft 版本");var loader=Picker(["原版","Forge","NeoForge","Fabric","Quilt"],title:"加载器类型");var build=Input("加载器版本（如适用）");
  var target=new UnifiedExpandableSelect { Title="模组目标实例", ItemsSource=model.Store.Entries.ToArray(), DisplayMember=x=>x is Instance i?i.Name+" · "+i.Version:x?.ToString()??"", SelectedIndex=0 };var info=Label("");string? analyzed=null;string? temporary=null;var running=false;string[] jars=[];string? planSource=null;int planType=-1;
  var previousJobs=model.Resources.MainJobs.Select(x=>x.Id).ToHashSet();var cancelling=false;
  void CancelJobs(){if(!cancel.IsCancellationRequested||!running||cancelling)return;cancelling=true;try{foreach(var job in model.Resources.MainJobs.Where(x=>!previousJobs.Contains(x.Id)&&x.Recovery?.Type=="pack"&&model.Resources.CanControl(x.Id)))model.Resources.Cancel(job.Id);}finally{cancelling=false;}}
  model.Resources.Changed+=CancelJobs;
  bool IsModrinth(string? path){if(!File.Exists(path))return false;using var zip=ZipFile.OpenRead(path!);return zip.GetEntry("modrinth.index.json")!=null;}
  async Task Analyze() {
   if(running)return;
   if(temporary!=null){Directory.Delete(temporary,true);temporary=null;}analyzed=null;
   planSource=source.Text;planType=type.SelectedIndex;
   if(type.SelectedIndex==3){info.Text=$"{jars.Length} JAR files → {target.SelectedItem}";return;}
   var path=source.Text??"";
   if(File.Exists(path)) {
    using var archive=ZipFile.OpenRead(path);
    if(archive.GetEntry("modrinth.index.json") is {} index){using var document=JsonDocument.Parse(index.Open());name.Text=document.RootElement.GetProperty("name").GetString();var dependencies=document.RootElement.GetProperty("dependencies");version.Text=dependencies.GetProperty("minecraft").GetString();loader.SelectedItem="原版";build.Text="";foreach(var mapping in new[]{("forge","Forge"),("neoforge","NeoForge"),("fabric-loader","Fabric"),("quilt-loader","Quilt")})if(dependencies.TryGetProperty(mapping.Item1,out var selected)){loader.SelectedItem=mapping.Item2;build.Text=selected.GetString();}info.Text="Modrinth 清单：使用现有校验及下载任务系统导入。";return;}
    temporary=Path.Combine(model.DownloadsDirectory,"import-"+Guid.NewGuid().ToString("N"));
    await GameImport.ExtractZipAsync(path,temporary,cancel.Token);path=temporary;
    var manifest=Path.Combine(path,"manifest.json");
    if(File.Exists(manifest)) {
     using var doc=JsonDocument.Parse(File.ReadAllText(manifest));var data=doc.RootElement;
     if(data.TryGetProperty("files",out var files)&&files.GetArrayLength()>0){var missing=string.Join("\n",files.EnumerateArray().Select(x=>"projectID="+x.GetProperty("projectID").GetInt64()+" · fileID="+x.GetProperty("fileID").GetInt64()));info.Text=missing;throw new InvalidDataException("CurseForge 文件需合法提供，当前无法自动取得这些项目文件。请手动提供完整游戏目录。\n"+missing);}
     var mc=data.GetProperty("minecraft");version.Text=mc.GetProperty("version").GetString();name.Text=data.TryGetProperty("name",out var title)?title.GetString():"Imported pack";
     if(mc.TryGetProperty("modLoaders",out var modloaders))foreach(var item in modloaders.EnumerateArray()){var id=item.GetProperty("id").GetString()??"";foreach(var kind in new[]{"forge","neoforge","fabric","quilt"})if(id.StartsWith(kind+"-",StringComparison.OrdinalIgnoreCase)){loader.SelectedItem=kind switch{"forge"=>"Forge","neoforge"=>"NeoForge","fabric"=>"Fabric",_=>"Quilt"};build.Text=id[(kind.Length+1)..];}}
     var overrides=data.TryGetProperty("overrides",out var entry)?entry.GetString():"overrides";analyzed=InstanceStore.SafeFile(path,overrides??"overrides");if(!Directory.Exists(analyzed))Directory.CreateDirectory(analyzed);
     info.Text="CurseForge manifest · files[] empty · overrides only";return;
    }
   }
   var inspection=GameImport.InspectDirectory(path);analyzed=inspection.Root;version.Text=inspection.Version??"";loader.SelectedItem=inspection.Loader;build.Text=inspection.LoaderVersion;info.Text=$"{inspection.Format}\n{inspection.Root}\n请确认版本和加载器。缺失资源由正常安装流程校验后补齐。";
  }
  var choose=Action("选择来源",async()=>{
   if(running)return;
   if(type.SelectedIndex is 1 or 2){var items=await dialog.StorageProvider.OpenFolderPickerAsync(new());source.Text=items.FirstOrDefault()?.TryGetLocalPath();}
   else{var files=await dialog.StorageProvider.OpenFilePickerAsync(new(){AllowMultiple=type.SelectedIndex==3});jars=files.Select(x=>x.TryGetLocalPath()).OfType<string>().ToArray();source.Text=jars.FirstOrDefault();}
   if(!string.IsNullOrWhiteSpace(source.Text))await Analyze();
  });
  void UpdateFields(){var mods=type.SelectedIndex==3;foreach(var field in new Control[]{name,version,loader,build})field.IsVisible=!mods;target.IsVisible=mods;}
  type.SelectionChanged+=(_,_)=>{if(!running){planType=-1;analyzed=null;UpdateFields();}};UpdateFields();
  var execute=Action("确认并导入",async()=>{
   if(running)return;if(planSource!=source.Text||planType!=type.SelectedIndex)throw new InvalidOperationException("来源已改变，请重新分析。");running=true;foreach(var field in new Control[]{source,name,version,loader,build,target,type})field.IsEnabled=false;maintenance++;
   try {
    if(type.SelectedIndex==3){if(jars.Length==0)throw new InvalidOperationException("请选择 JAR 文件。");var instance=target.SelectedItem as Instance??throw new InvalidOperationException("请先选择游戏实例。");EnsureIdle(instance);foreach(var jar in jars){cancel.Token.ThrowIfCancellationRequested();LocalMods.Import(jar,model.ModsFolder(instance));}info.Text="模组导入完成。";}
    else if(IsModrinth(source.Text)){if(model.Store.Entries.Any(x=>x.Name.Equals(name.Text,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("An instance with this name exists; choose another name.");var imported=await model.QueuePackAsync(name.Text??"Imported pack",null,source.Text,progress=>info.Text=progress);model.Selected=imported with {Name=name.Text??imported.Name,Pack=false};model.Store.Save(model.Selected);info.Text="整合包导入完成";}
    else{if(analyzed==null)throw new InvalidOperationException("请先分析来源。");model.Selected=await GameImport.ImportDirectoryAsync(model.Store,analyzed,name.Text??"",version.Text??"",loader.SelectedItem as string??"原版",string.IsNullOrWhiteSpace(build.Text)?null:build.Text,new Progress<MigrationFile>(x=>info.Text=x.RelativePath),cancel.Token);info.Text="导入完成；启动前将重新验证资源。";}
    Refresh();
   }finally{maintenance--;running=false;foreach(var field in new Control[]{source,name,version,loader,build,target,type})field.IsEnabled=true;}
  });
  dialog.SetBody(Column(type,source,choose,Action("重新分析",Analyze),info,name,version,loader,build,target),Action("取消导入",()=>{cancel.Cancel();CancelJobs();return Task.CompletedTask;}),CloseDialog(dialog),execute);
  dialog.Closing+=(_,e)=>{if(running){cancel.Cancel();e.Cancel=true;}};
  try{await dialog.ShowDialog(this);}finally{model.Resources.Changed-=CancelJobs;if(temporary!=null&&Directory.Exists(temporary))Directory.Delete(temporary,true);}
 }
}
