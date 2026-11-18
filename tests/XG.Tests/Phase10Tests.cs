// SPDX-License-Identifier: MIT
using System.Net;
using System.Security.Cryptography;
using System.Text;
using XG.Core;

internal static class Phase10Tests
{
    public static async Task RunAsync(string root)
    {
        var folder = Path.Combine(root, "recovery-fixture");
        Directory.CreateDirectory(folder);
        var recovery = new ResourceRecovery("game", Guid.NewGuid(), "fixture");
        var id = Guid.NewGuid();
        InstanceStore.WriteJson(Path.Combine(folder, "resource-operations.json"), new[]
        {
            new ResourceOperation(id, "1.21.1", "游戏本体", "Minecraft", "Fixture",
                "下载中", Bytes: 10, TotalBytes: 30, CurrentFile: "test.jar", Recovery: recovery)
        });
        var restored = new ResourceOperations(folder);
        if (!restored.CanRecover(id) || restored.Items.Single().State != "等待恢复" ||
            restored.Items.Single().Recovery != recovery || restored.Items.Single().Bytes != 10)
            throw new Exception("Restart discarded resource recovery metadata.");
        await restored.RetryAsync(id, async stage =>
        {
            stage("下载中");
            await restored.Current!.RunFileAsync(_ => Task.CompletedTask);
            restored.Report("游戏本体", "Fixture", 30, 30, "test.jar", 1, 1, 30, 30);
            return 0;
        });
        if (new ResourceOperations(folder).Items.Single() is not { State: "已完成", Bytes: 30, CurrentFile: "test.jar" })
            throw new Exception("Recovered task did not persist completion under the original ID.");
        using var transport = new HttpClient(new Fixture());
        var downloads = new Downloads(transport, folder);
        downloads.SetSpeedLimit(5);
        if (new Downloads(transport, folder).SpeedLimitMiB != 5) throw new Exception("Download speed setting did not persist.");

        var catalog = new Modrinth(transport);
        var planner = new ModInstallPlanner(catalog);
        var selected = new Instance(Guid.NewGuid(), "Fixture", "1.21.1", "Fabric");
        var project = new Project("main", "main", "Main", "", "", null, 0, false);
        var version = new ProjectVersion("root", "1", File("root"), [], [])
        {
            ProjectId = "main", GameVersions = ["1.21.1"], Loaders = ["fabric"],
            Dependencies = [new("required", "dep", "dependency-v1", ""), new("optional", "optional", "", "")]
        };
        var plan = await planner.PlanAsync(project, version, selected, [], default);
        if (!plan.Files.Select(x => x.Id).SequenceEqual(["dependency-v1", "root"]) || !plan.Optional.Contains("optional"))
            throw new Exception("Dependency order or optional hints are incorrect.");
        var conflict = version with { Dependencies = [new("incompatible", "dep", "", "")] };
        try
        {
            await planner.PlanAsync(project, conflict, selected, [new InstalledMod("dep.jar", plan.Files[0])], default);
            throw new Exception("A declared conflict was not caught.");
        }
        catch (InvalidOperationException error) when (error.Message.Contains("不兼容模组")) { }
        Console.WriteLine("PASS persisted task restart and retry, stored speed cap, dependency resolution order and conflict checks");
    }

    private static ModFile File(string name) => new(name + ".jar", new Uri($"https://cdn.modrinth.com/{name}.jar"),
        1, Convert.ToHexString(SHA512.HashData(new byte[] { 8 })));

    private sealed class Fixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath != "/v2/version/dependency-v1")
                throw new InvalidOperationException("Unexpected dependency URL.");
            var body = System.Text.Json.JsonSerializer.Serialize(new
            {
                project_id = "dep", id = "dependency-v1", version_number = "1", game_versions = new[] { "1.21.1" },
                loaders = new[] { "fabric" },
                files = new[] { new { filename = "dep.jar", primary = true, url = File("dep").Url, size = 1,
                    hashes = new { sha512 = File("dep").Sha512 } } }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
