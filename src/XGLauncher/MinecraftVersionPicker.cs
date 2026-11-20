// SPDX-License-Identifier: MIT
using XG.Core;

namespace XGLauncher;

internal sealed class MinecraftVersionPicker
{
    private readonly Dictionary<string, string> kinds = new(StringComparer.Ordinal);
    private readonly bool allowAny;
    internal UnifiedExpandableSelect Select { get; }

    internal MinecraftVersionPicker(string title, bool allowAny)
    {
        this.allowAny = allowAny;
        Select = new UnifiedExpandableSelect
        {
            Title = title, Searchable = true, FilterLabels = ["正式版", "快照版", "全部"],
            GroupFilter = (group, choice) => choice is string version &&
                (allowAny && version == "全部" || group == 2 || kinds.GetValueOrDefault(version) == (group == 0 ? "release" : "snapshot"))
        };
    }

    internal void Update(GameService.OfficialVersion[] manifest, string? saved = null)
    {
        var previous = Select.SelectedItem as string ?? saved;
        kinds.Clear();
        var choices = new List<string>();
        if (allowAny) choices.Add("全部");
        foreach (var version in manifest)
            if (kinds.TryAdd(version.Id, version.Type)) choices.Add(version.Id);
        if (previous != null && !choices.Contains(previous)) choices.Add(previous);
        Select.ItemsSource = choices;
        Select.SelectedItem = previous ?? (allowAny ? "全部" :
            manifest.FirstOrDefault(version => version.Type == "release")?.Id);
    }
}
