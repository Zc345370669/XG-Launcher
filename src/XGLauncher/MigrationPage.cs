// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using XG.Core;
namespace XGLauncher;
public sealed partial class MainWindow {
 private async Task MigrateWorkspaceAsync() {
  if(model.Busy.Count>0||maintenance>0||model.Resources.UnfinishedCount>0||SharedResourceCoordinator.ActiveCount>0||model.Downloads.Items.Any(x=>x.State is not ("已完成" or "已取消" or "失败")))throw new InvalidOperationException("请先关闭游戏并处理未完成任务。");
  if(model.Store.SharedMinecraft is not {} minecraft)throw new InvalidOperationException("当前自定义模式无法自动迁移，请保留原目录。");
  var source=Path.GetDirectoryName(minecraft)!;
  if(!Path.GetFullPath(model.Store.Root).Equals(Path.Combine(source,"data"),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("自定义外部数据目录不支持自动切换，原目录保持不变。");
  var dialog=Dialog("迁移游戏数据",editor:true);var destination=Input("新的空目标目录");var details=Label("当前目录："+source);var state=Label("");var cancel=new CancellationTokenSource();var running=false;var ready=false;string? verifiedDestination=null;
  var choose=Action("选择目标父目录",async()=>{var folders=await dialog.StorageProvider.OpenFolderPickerAsync(new());if(folders.FirstOrDefault()?.TryGetLocalPath() is {} path)destination.Text=Path.Combine(path,"XGLauncher-data");});
  var preview=Action("检查迁移内容",()=>{var plan=WorkspaceMigration.Preview(source,destination.Text??"");var previous=WorkspaceMigration.ReadJournal(destination.Text??"");if(previous!=null)state.Text=previous.State+" · "+previous.Staging;details.Text=$"{source}\n→ {destination.Text}\n{plan.Count} files · {plan.Sum(x=>x.Bytes)/1048576d:0.0} MB\n目标必须不存在，原文件保留。";return Task.CompletedTask;});
  var start=Action("复制并校验",async()=>{
   if(running)return;running=true;maintenance++;
   try{ready=false;var target=Path.GetFullPath(destination.Text??"");InstanceStore.WriteJson(Path.Combine(model.Store.Root,"migration-path-backup-"+Guid.NewGuid().ToString("N")+".json"),source);await WorkspaceMigration.MigrateAsync(source,target,new Progress<MigrationFile>(x=>state.Text=x.RelativePath),cancel.Token);verifiedDestination=target;ready=true;state.Text="校验完成，可以切换目录。";}
   catch(OperationCanceledException){state.Text="已取消，原目录不变。";}catch(Exception error){state.Text=new XG.Core.GameLogSanitizer(null).Redact(error.Message);throw;}finally{maintenance--;running=false;}
  });
  var apply=Action("切换到新目录",()=>{
   if(!ready||running)return Task.CompletedTask;
   if(!Path.GetFullPath(destination.Text??"").Equals(verifiedDestination,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("来源已改变，请重新分析。");
   try{PortableStorage.Use(verifiedDestination!);var replacement=new MainWindow();if(Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)desktop.MainWindow=replacement;replacement.Show();dialog.Close();exitRequested=true;Close();}
   catch{PortableStorage.Use(source);throw;}return Task.CompletedTask;
  });
  dialog.SetBody(Column(details,destination,choose,preview,state),Action("取消迁移",()=>{cancel.Cancel();return Task.CompletedTask;}),CloseDialog(dialog),start,apply);
  dialog.Closing+=(_,e)=>{if(running){cancel.Cancel();e.Cancel=true;}};await dialog.ShowDialog(this);
 }
}
