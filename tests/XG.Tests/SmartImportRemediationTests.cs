// SPDX-License-Identifier: MIT
using XG.Core;
internal static class SmartImportRemediationTests
{
    internal static async Task RunAsync(string root)
    {
        var source = Path.Combine(root, "all-launcher-import"); Directory.CreateDirectory(source);
        foreach (var type in new[] { "Standard", "Minecraft Launcher", "PCL2", "HMCL" })
        {
            var game = Path.Combine(source, type, ".minecraft"); var profile = Path.Combine(game, "versions", "1.21.1"); Directory.CreateDirectory(profile);
            File.WriteAllText(Path.Combine(profile, "1.21.1.json"), """{"id":"1.21.1","mainClass":"fixture.Main","libraries":[null,[],{}]}""");
            if(type == "Minecraft Launcher")File.WriteAllText(Path.Combine(game,"launcher_profiles.json"),"{}");
            if(type == "PCL2"){Directory.CreateDirectory(Path.Combine(profile,"PCL"));File.WriteAllText(Path.Combine(profile,"PCL","Setup.ini"),"VersionArgumentIndieV2:True");}
            if(type == "HMCL")File.WriteAllText(Path.Combine(profile,"hmclversion.cfg"),"""{"gameDirType":"VERSION_FOLDER"}""");
        }
        foreach(var type in new[]{"Prism Launcher","MultiMC"})
        {
            var launcher=Path.Combine(source,type);var instance=Path.Combine(launcher,"instances","Fixture");Directory.CreateDirectory(Path.Combine(instance,".minecraft","saves"));
            File.WriteAllText(Path.Combine(launcher,type == "MultiMC"?"multimc.cfg":"prismlauncher.cfg"),"");
            File.WriteAllText(Path.Combine(instance,"instance.cfg"),"name=Controlled instance");
            File.WriteAllText(Path.Combine(instance,"mmc-pack.json"),"""{"components":[{"uid":"net.minecraft","version":"1.21.1"},{"uid":"net.fabricmc.fabric-loader","version":"0.16.5"}]}""");
        }
        foreach(var (type,file,json) in new[]{
            ("CurseForge","minecraftinstance.json","""{"name":"Curse fixture","gameVersion":"1.21.1","baseModLoader":{"name":"forge-52.0.1"}}"""),
            ("Modrinth App","profile.json","""{"name":"Modrinth fixture","game_version":"1.21.1","mod_loader":"Fabric","mod_loader_version":"0.16.5"}"""),
            ("ATLauncher","instance.json","""{"id":"1.21.1","launcher":{"name":"AT fixture","loaderVersion":{"type":"NeoForge","version":"21.1.10"}}}""")})
        {
            var folder=Path.Combine(source,type);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,file),json);
            foreach(var name in new[]{"mods","config","saves","resourcepacks","shaderpacks","screenshots"}){Directory.CreateDirectory(Path.Combine(folder,name));File.WriteAllText(Path.Combine(folder,name,"keep.txt"),"user fixture");}
            File.WriteAllText(Path.Combine(folder,"options.txt"),"fixture options");File.WriteAllText(Path.Combine(folder,"Credentials.JSON"),"private fixture");File.WriteAllText(Path.Combine(folder,"microsoft-msal.bin"),"cache fixture");
        }
        var scan=GameImport.Scan(source);
        Check(scan.Instances.Count == 9 && scan.Issues.Count == 0,"nine launcher layouts did not scan consistently: "+scan.Instances.Count+"/"+scan.Issues.Count);
        foreach(var type in new[]{"Minecraft Launcher","PCL2","HMCL","Prism Launcher","MultiMC","CurseForge","Modrinth App","ATLauncher"})Check(scan.Instances.Any(x=>x.Inspection.Format==type),"missing detected format "+type);
        var store=new InstanceStore(Path.Combine(root,"all-launcher-target","data"),Path.Combine(root,"all-launcher-target",".minecraft"));
        foreach(var candidate in scan.Instances.Where(x=>x.Inspection.Format is "CurseForge" or "Modrinth App" or "ATLauncher"))
        {
            var instance=new Instance(Guid.NewGuid(),store.AvailableName(candidate.Name),candidate.Inspection.Version!,candidate.Inspection.Loader,LoaderVersion:candidate.Inspection.LoaderVersion);
            await GameImport.CopyCandidateAsync(store,instance,candidate,true,null,default);
            var target=store.DirectoryFor(instance);Check(File.Exists(Path.Combine(target,"saves","keep.txt"))&&File.Exists(Path.Combine(target,"options.txt")),"user data lost");
            Check(!File.Exists(Path.Combine(target,"Credentials.JSON"))&&!File.Exists(Path.Combine(target,"microsoft-msal.bin")),"launcher credentials copied");
            Check(File.Exists(Path.Combine(candidate.Profile,"saves","keep.txt")),"original source modified");
        }
        var malformed=Path.Combine(source,"Malformed");Directory.CreateDirectory(malformed);File.WriteAllText(Path.Combine(malformed,"profile.json"),"[]");
        var invalid=GameImport.Scan(malformed);Check(invalid.Instances.Count==0&&invalid.Issues.Count>0&&!invalid.Issues.Any(x=>x.Contains("requires an element")),"Object/Array regression was hidden or reproduced");
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        try{GameImport.Scan(source,cancellation.Token);throw new Exception("scan ignored cancellation");}catch(OperationCanceledException){}
        Console.WriteLine("PASS nine launcher directory schemas, loader metadata, scanner-to-copy workflow, original saves/options preservation, credential exclusion, malformed root diagnostics and scan cancellation (controlled fixtures)");
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
