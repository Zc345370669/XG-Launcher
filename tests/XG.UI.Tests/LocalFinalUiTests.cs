// SPDX-License-Identifier: MIT
using XG.Core;
using XGLauncher;

internal static class LocalFinalUiTests
{
    internal static void Run(string root)
    {
        var file = Path.Combine(root, "new-default-mode.json");
        if (!WindowSession.Load(file, defaultCompact: true).Compact) throw new Exception("fresh portable default is not compact");
        InstanceStore.WriteJson(file, new WindowSession(false));
        if (WindowSession.Load(file, defaultCompact: true).Compact) throw new Exception("explicit saved full mode lost");
        if (!LegalCatalog.Credits.Any(x => x.Name == "PCL2" && x.Documents.Any(d => d.Read()!.Contains("Plain Craft Launcher 2"))))
            throw new Exception("embedded PCL2 reference/name missing");
        Console.WriteLine("PASS local-final fresh compact default, persisted full-mode preference and embedded PCL2 full-name/reference (headless policy check)");
    }
}
