using Newtonsoft.Json;

namespace AutoHook.Fishing;

public class FishingPresets : BasePreset
{
    // Global preset, cant rename rn 
    public CustomPresetConfig DefaultPreset = new(Service.GlobalPresetName);

    public List<CustomPresetConfig> CustomPresets = [];

    public List<PresetFolder> Folders = [];

    [JsonIgnore] public override CustomPresetConfig? SelectedPreset => base.SelectedPreset as CustomPresetConfig;

    public override void AddNewPreset(string presetName)
    {
        var newPreset = new CustomPresetConfig(presetName);
        CustomPresets.Add(newPreset);
        Service.Save();
    }

    public override void AddNewPreset(BasePresetConfig preset)
    {
        // i needed a way to copy the object without reference, im too dumb to think of another way
        var json = JsonConvert.SerializeObject(preset);
        var copy = JsonConvert.DeserializeObject<CustomPresetConfig>(json);
        copy!.UniqueId = Guid.NewGuid();
        CustomPresets.Add(copy);
        Service.Save();
    }

    public override void RemovePreset(Guid value)
    {
        var preset = CustomPresets.Find(p => p.UniqueId == value);
        if (preset == null)
            return;

        // Remove from any folders
        foreach (var folder in Folders)
        {
            folder.RemovePreset(value);
        }

        CustomPresets.Remove(preset);
        Service.Save();
    }

    public override void OnSelectedPreset(BasePresetConfig newPreset, BasePresetConfig? oldPreset)
    {
        if (oldPreset is not CustomPresetConfig old)
            return;

        if (old is { ExtraCfg: { Enabled: true, ResetCounterPresetSwap: true } })
            old.ResetCounter();

        Service.Save();
    }

    public override void SwapIndex(int itemIndex, int targetIndex)
    {
        var moved = CustomPresets[itemIndex];

        if (moved == null)
            return;

        RemovePreset(moved.UniqueId);
        CustomPresets.Insert(targetIndex, moved);
        Service.Save();
    }

    public void AddNewFolder(string folderName)
    {
        var newFolder = new PresetFolder(folderName);
        Folders.Add(newFolder);
        Service.Save();
    }

    public void RemoveFolder(Guid folderId)
    {
        var folder = Folders.Find(f => f.UniqueId == folderId);
        if (folder == null)
            return;

        Folders.Remove(folder);
        Service.Save();
    }

    public void RemoveFolderAndContents(Guid folderId)
    {
        var folder = Folders.Find(f => f.UniqueId == folderId);
        if (folder == null)
            return;

        // Copy to avoid mutation during iteration (RemovePreset modifies folder.PresetIds)
        foreach (var presetId in folder.PresetIds.ToList())
        {
            RemovePreset(presetId);
        }

        Folders.Remove(folder);
        Service.Save();
    }

    public bool IsPresetInAnyFolder(Guid presetId)
    {
        return Folders.Any(f => f.ContainsPreset(presetId));
    }

    public PresetFolder? GetFolderContainingPreset(Guid presetId)
    {
        return Folders.FirstOrDefault(f => f.ContainsPreset(presetId));
    }

    [JsonIgnore] public override List<BasePresetConfig> PresetList => [.. CustomPresets.Cast<BasePresetConfig>()];
}