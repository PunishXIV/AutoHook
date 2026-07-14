using AutoHook.Replay;
using Newtonsoft.Json;

namespace AutoHook.Fishing;

public class FishingPresets : BasePreset {
    public const string ReasonManual = "Manual";
    public const string ReasonAutoOceanFish = "Auto Ocean Fish";
    public const string ReasonExtraTrigger = "Extra Options";
    public const string ReasonFishCaught = "Fish Caught";
    public const string ReasonIpc = "IPC";

    // Global preset, cant rename rn 
    public CustomPresetConfig DefaultPreset = new(Service.GlobalPresetName);

    public List<CustomPresetConfig> CustomPresets = [];

    public List<PresetFolder> Folders = [];

    [JsonIgnore] public override CustomPresetConfig? SelectedPreset => base.SelectedPreset as CustomPresetConfig;
    [JsonIgnore] public CustomPresetConfig CurrentPreset => SelectedPreset ?? DefaultPreset;

    [ThreadStatic] private static string? _selectReason;

    /// <summary>Set the selected preset and tag the switch reason for replay decisions.</summary>
    public void Select(CustomPresetConfig? preset, string reason) {
        var previous = _selectReason;
        _selectReason = reason;
        try {
            SelectedPreset = preset;
        }
        finally {
            _selectReason = previous;
        }
    }

    public override void AddNewPreset(string presetName) {
        var newPreset = new CustomPresetConfig(presetName);
        CustomPresets.Add(newPreset);
        InvalidatePresetListCache();
        Service.Save();
    }

    public override void AddNewPreset(BasePresetConfig preset) {
        // i needed a way to copy the object without reference, im too dumb to think of another way
        var json = JsonConvert.SerializeObject(preset);
        var copy = JsonConvert.DeserializeObject<CustomPresetConfig>(json);
        copy!.UniqueId = Guid.NewGuid();
        CustomPresets.Add(copy);
        InvalidatePresetListCache();
        Service.Save();
    }

    public override void RemovePreset(Guid value) {
        var preset = CustomPresets.Find(p => p.UniqueId == value);
        if (preset == null)
            return;

        // Remove from any folders
        foreach (var folder in Folders) {
            folder.RemovePreset(value);
        }

        CustomPresets.Remove(preset);
        InvalidatePresetListCache();
        Service.Save();
    }

    public override void OnSelectedPreset(BasePresetConfig? newPreset, BasePresetConfig? oldPreset) {
        if (oldPreset is CustomPresetConfig old)
            old.TryResetCounter();

        var from = oldPreset?.PresetName ?? Service.GlobalPresetName;
        var to = newPreset?.PresetName ?? Service.GlobalPresetName;
        if (from != to)
            ReplayDecisions.PresetSwitched(from, to, _selectReason ?? ReasonManual);

        Service.Save();
    }

    public override void SwapIndex(int itemIndex, int targetIndex) {
        var moved = CustomPresets[itemIndex];

        if (moved == null)
            return;

        RemovePreset(moved.UniqueId);
        CustomPresets.Insert(targetIndex, moved);
        InvalidatePresetListCache();
        Service.Save();
    }

    public void AddNewFolder(string folderName) {
        var newFolder = new PresetFolder(folderName);
        Folders.Add(newFolder);
        Service.Save();
    }

    public void AddNewFolder(string folderName, Guid? parentFolderId) {
        var newFolder = new PresetFolder(folderName) {
            ParentFolderId = parentFolderId
        };
        Folders.Add(newFolder);
        Service.Save();
    }

    public void RemoveFolder(Guid folderId) {
        var folder = Folders.Find(f => f.UniqueId == folderId);
        if (folder == null)
            return;

        Folders.Remove(folder);
        Service.Save();
    }

    public void RemoveFolderWithContents(Guid folderId) {
        var folder = Folders.Find(f => f.UniqueId == folderId);
        if (folder == null)
            return;

        RemoveFolderWithContentsRecursive(folder);
        Service.Save();
    }

    private void RemoveFolderWithContentsRecursive(PresetFolder folder) {
        // Remove child folders first
        var childFolders = Folders.Where(f => f.ParentFolderId == folder.UniqueId).ToList();
        foreach (var child in childFolders)
            RemoveFolderWithContentsRecursive(child);

        // Remove presets contained in this folder
        foreach (var presetId in folder.PresetIds.ToList())
            RemovePreset(presetId);

        // Finally remove this folder
        Folders.Remove(folder);
    }

    public bool IsPresetInAnyFolder(Guid presetId) {
        return Folders.Any(f => f.ContainsPreset(presetId));
    }

    public PresetFolder? GetFolderContainingPreset(Guid presetId) {
        return Folders.FirstOrDefault(f => f.ContainsPreset(presetId));
    }

    [JsonIgnore] private List<BasePresetConfig>? _presetListCache;
    [JsonIgnore] private int _presetListCacheCount = -1;

    [JsonIgnore]
    public override List<BasePresetConfig> PresetList {
        get {
            if (_presetListCache == null || _presetListCacheCount != CustomPresets.Count) {
                _presetListCache = [.. CustomPresets.Cast<BasePresetConfig>()];
                _presetListCacheCount = CustomPresets.Count;
            }

            return _presetListCache;
        }
    }

    private void InvalidatePresetListCache() {
        _presetListCache = null;
        _presetListCacheCount = -1;
    }
}
