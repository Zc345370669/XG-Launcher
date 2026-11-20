// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using XG.Core;
using XGLauncher;

internal static class DownloadSummaryTests
{
    internal static void Run(string? shots)
    {
        const long mib=1048576;
        var main=new ResourceOperation(Guid.NewGuid(),"Minecraft","实例安装","fixture","fixture","下载中",Bytes:186*mib,BytesPerSecond:2*mib);
        var assets=new ResourceOperation(Guid.NewGuid(),"Assets","Minecraft Assets","fixture","fixture","下载中",Bytes:186*mib,TotalBytes:800*mib,ParentId:main.Id);
        var loader=new ResourceOperation(Guid.NewGuid(),"NeoForge","模组加载器","NeoForge","fixture","等待中",ParentId:main.Id);
        var text=MainWindow.ResourceTransferText(main,[assets,loader]);
        Check(text.Contains("186.00",StringComparison.Ordinal)&&text.Contains("800.00",StringComparison.Ordinal)&&text.Contains("部分资源大小未提供",StringComparison.Ordinal)&&!text.Contains("总大小未知",StringComparison.Ordinal)&&!text.Contains("剩余约",StringComparison.Ordinal),"known bytes hidden or partial total presented as full ETA");
        Check(MainWindow.ResourceTransferText(main with{TotalBytes=900*mib},[assets,loader]).Contains("部分资源大小未提供",StringComparison.Ordinal),"stale parent total masked unknown child");
        var completedUnknown=loader with{State="已完成",TotalFiles=10,CompletedFiles=10};
        Check(MainWindow.ResourceTransferText(main with{State="已完成"},[assets with{State="已完成"},completedUnknown]).Contains("部分资源大小未提供",StringComparison.Ordinal),"unreported loader bytes fabricated after completion");
        Check(MainWindow.ResourceTransferText(main with{State="已完成"},[assets with{State="已完成"},loader with{State="已完成"}]).Contains("部分资源大小未提供",StringComparison.Ordinal),"opaque zero-byte loader presented as measured full total");
        var full=MainWindow.ResourceTransferText(main,[assets,loader with{TotalBytes=100*mib}]);
        Check(full.Contains("900.00",StringComparison.Ordinal)&&!full.Contains("部分资源",StringComparison.Ordinal)&&full.Contains("剩余约",StringComparison.Ordinal),"complete known size/ETA not restored");
        Check(MainWindow.ResourceTransferText(main,[assets,loader with{Skipped=true}]).Contains("800.00",StringComparison.Ordinal),"cached stage not skipped in total");
        Check(MainWindow.ResourceTransferText(main with{State="已暂停"},[assets,loader]).Contains("0.00 MB/s",StringComparison.Ordinal),"paused rate still advancing");
        Check(MainWindow.ResourceTransferText(main,[]).Contains("总大小未知",StringComparison.Ordinal),"unknown-only metadata became fake total");
        Localizer.Set("en-US"); var english=MainWindow.ResourceTransferText(main,[assets,loader]);Check(!english.Contains("已知",StringComparison.Ordinal)&&english.Contains("Known total",StringComparison.Ordinal),"summary not localized");Localizer.Set("zh-CN");
        var workspace=Path.Combine(Path.GetTempPath(),"xg-aux-"+Guid.NewGuid().ToString("N"));var data=Path.Combine(workspace,"data");Directory.CreateDirectory(data);
        try
        {
            Check(PortableStorage.Auxiliary(workspace,data,"downloads")==Path.Combine(data,"downloads"),"new downloads not grouped under data");
            var previous=Path.Combine(workspace,"logs");Directory.CreateDirectory(previous);File.WriteAllText(Path.Combine(previous,"keep.log"),"history");
            Check(PortableStorage.Auxiliary(workspace,data,"logs")==previous&&File.ReadAllText(Path.Combine(previous,"keep.log"))=="history","existing logs moved or deleted");
            var empty=Path.Combine(workspace,"backups");Directory.CreateDirectory(empty);
            Check(PortableStorage.Auxiliary(workspace,data,"backups")==Path.Combine(data,"backups")&&!Directory.Exists(empty),"empty auxiliary root not consolidated");
        }
        finally {Directory.Delete(workspace,true);}
        if(shots!=null)
        {
            Directory.CreateDirectory(shots);var owner=new Window {Width=900,Height=100,Background=ThemePalette.Panel,
                Content=new TextBlock {Text=text,Margin=new Thickness(12),TextWrapping=TextWrapping.Wrap,Foreground=ThemePalette.Text}};
            owner.Show();Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick();Dispatcher.UIThread.RunJobs();
            using var frame=owner.CaptureRenderedFrame();frame!.Save(Path.Combine(shots,"download-known-total-linux-headless.png"));owner.Close();
        }
        Console.WriteLine("PASS download summary shows known byte total beside unknown installer work, handles stale parent/cached/completed unknown stages, restores true full total/ETA, pause and English localization without modifying aggregation");
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
