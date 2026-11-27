// SPDX-License-Identifier: MIT
using System.IO.Compression;
using XG.Core;
internal static class PackJsonRemediationTests
{
    internal static async Task RunAsync(string root)
    {
        var store = new InstanceStore(Path.Combine(root, "pack-json-remediation"));
        using var http = new HttpClient(new RejectNetwork()); var importer = new PackImporter(http, store);
        foreach (var json in new[] { "[]", "null", "\"text\"", """{"formatVersion":1,"game":"minecraft","name":"Fixture","dependencies":[],"files":[]}""", """{"formatVersion":1,"game":"minecraft","name":"Fixture","dependencies":{"minecraft":"1.21.1"},"files":[[]]}""" })
        {
            var archive=Path.Combine(root,Guid.NewGuid().ToString("N")+".mrpack");
            using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("modrinth.index.json").Open());writer.Write(json);}
            try { await importer.ImportAsync(archive,_=>{}); throw new Exception("malformed manifest accepted"); }
            catch(InvalidDataException error){if(!error.Message.Contains("清单"))throw;}
            if(store.Entries.Any())throw new Exception("invalid manifest registered a playable instance");
        }
        Console.WriteLine("PASS actual MRPACK root Object/Array/Null and nested type validation occurs before downloads/registration; no swallowed JSON type errors");
    }
    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>throw new Exception("malformed manifest attempted a network download");
    }
}
