// SPDX-License-Identifier: MIT
using System.Security.Cryptography;
using CmlLib.Core.Files;
using CmlLib.Core.Tasks;
using XG.Core;

internal static class JavaCleanupTests
{
    internal static async Task RunAsync(string root)
    {
        var managed=Path.Combine(root,"cleanup-runtime");var java=Path.Combine(managed,"home","bin","java.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(java)!);await File.WriteAllTextAsync(java,"verified runtime fixture");
        var raw=new GameFile("java.exe") {Path=java,Size=new FileInfo(java).Length,Hash=Convert.ToHexString(SHA1.HashData(await File.ReadAllBytesAsync(java)))};
        var part=java+".xg-part";await File.WriteAllTextAsync(part,"stale download");
        var setup=new GameFile("jre.lzma") {Path=Path.Combine(Path.GetTempPath(),"unowned-jre.lzma"),UpdateTask=[new LegacyJavaExtractionTask(Path.Combine(managed,"home"))]};
        setup=JavaInstallerCleanup.OwnLegacyPackages([setup],managed)[0];
        Check(JavaInstallerCleanup.OwnLegacyPackages([setup],managed)[0].Path==setup.Path,"retry changed owned archive identity");
        Directory.CreateDirectory(Path.GetDirectoryName(setup.Path!)!);await File.WriteAllTextAsync(setup.Path!,"owned archive fixture");
        var keep=Path.Combine(managed,"home","src.zip");await File.WriteAllTextAsync(keep,"legitimate runtime resource");
        Check(await JavaInstallerCleanup.AfterVerifiedAsync(managed,java+".missing",[raw,setup])==0&&File.Exists(part)&&File.Exists(setup.Path),"failed verification lost install/recovery files");
        var removed=await JavaInstallerCleanup.AfterVerifiedAsync(managed,java,[raw,setup]);
        Check(removed==2&&!File.Exists(part)&&!File.Exists(setup.Path)&&File.Exists(java)&&File.ReadAllText(keep)=="legitimate runtime resource","cleanup removed runtime or retained owned package");
        await File.WriteAllTextAsync(part,"keep until target valid");
        Check(await JavaInstallerCleanup.AfterVerifiedAsync(managed,java,[new GameFile("bad") {Path=java,Size=raw.Size,Hash=new string('0',40)}])==0&&File.Exists(part),"unverified target partial deleted");
        var external=Path.Combine(root,"external-runtime");Directory.CreateDirectory(external);var outside=Path.Combine(external,"java.exe");await File.WriteAllTextAsync(outside,"external");
        Check(await JavaInstallerCleanup.AfterVerifiedAsync(managed,outside,[raw])==0&&File.Exists(part),"external Java authorized managed cache cleanup");
        var workspace=Path.Combine(root,"clean-portable");var store=new InstanceStore(Path.Combine(workspace,"data"),Path.Combine(workspace,"minecraft"));
        Check(store.ManagedJavaRoot==Path.Combine(workspace,"minecraft","runtimes"),"fresh workspace scatters Java at launcher root");
        Directory.CreateDirectory(Path.Combine(workspace,"runtimes"));
        Check(new InstanceStore(store.Root,store.SharedMinecraft).ManagedJavaRoot==Path.Combine(workspace,"runtimes"),"existing Java root no longer resolved");
        Console.WriteLine("PASS verified Java cleanup removes only owned archives/valid-target temporary downloads, preserves real runtime ZIPs/failed or external installs; fresh shared Java under minecraft with old-root compatibility (controlled files)");
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
