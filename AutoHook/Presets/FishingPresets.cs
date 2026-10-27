using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Newtonsoft.Json;

namespace AutoHook.Presets;

public class FishingPresets : BasePreset {
    public const string ReasonManual = "Manual";
    public const string ReasonAutoOceanFish = "Auto Ocean Fish";
    public const string ReasonExtraTrigger = "Extra Options";
    public const string ReasonFishCaught = "Fish Caught";
    public const string ReasonIpc = "IPC";

    // Global preset, cant rename rn 
    public CustomPresetConfig DefaultPreset = new(AutoHook.GlobalPresetName);

    public List<CustomPresetConfig> CustomPresets = [];

    public List<PresetFolder> Folders = [];

    [JsonIgnore] public override CustomPresetConfig? SelectedPreset => base.SelectedPreset as CustomPresetConfig;
    [JsonIgnore] public CustomPresetConfig CurrentPreset => SelectedPreset ?? DefaultPreset;

    [ThreadStatic] private static string? _selectReason;

    // select preset and tag switch reason for replay decisions.
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
        Configuration.Save();
    }

    public override void AddNewPreset(BasePresetConfig preset) {
        // i needed a way to copy the object without reference, im too dumb to think of another way
        var json = JsonConvert.SerializeObject(preset);
        var copy = JsonConvert.DeserializeObject<CustomPresetConfig>(json);
        copy!.UniqueId = Guid.NewGuid();
        CustomPresets.Add(copy);
        InvalidatePresetListCache();
        Configuration.Save();
    }

    public override void RemovePreset(Guid value) {
        var preset = CustomPresets.Find(p => p.UniqueId == value);
        if (preset == null)
            return;

        if (SelectedGuid == value.ToString()) {
            SelectedGuid = "";
            PluginUi.Status = string.Empty;
        }

        foreach (var folder in Folders)
            folder.RemovePreset(value);

        CustomPresets.Remove(preset);
        InvalidatePresetListCache();
        Configuration.Save();
    }

    public override void OnSelectedPreset(BasePresetConfig? newPreset, BasePresetConfig? oldPreset) {
        NotifyPresetSelected(newPreset, oldPreset);
        Configuration.Save();
    }

    public override void SwapIndex(int itemIndex, int targetIndex) {
        var moved = CustomPresets[itemIndex];
        if (moved == null)
            return;

        CustomPresets.RemoveAt(itemIndex);
        CustomPresets.Insert(targetIndex, moved);
        InvalidatePresetListCache();
        Configuration.Save();
    }

    public void AddNewFolder(string folderName) {
        Folders.Add(new PresetFolder(folderName));
        Configuration.Save();
    }

    public void AddNewFolder(string folderName, Guid? parentFolderId) {
        Folders.Add(new PresetFolder(folderName) {
            ParentFolderId = parentFolderId,
        });
        Configuration.Save();
    }

    public void RemoveFolder(Guid folderId) {
        var folder = Folders.Find(f => f.UniqueId == folderId);
        if (folder != null)
            Folders.Remove(folder);
        Configuration.Save();
    }

    private void RemoveFolderWithContentsRecursive(PresetFolder folder) {
        var childFolders = Folders.Where(f => f.ParentFolderId == folder.UniqueId).ToList();
        foreach (var child in childFolders)
            RemoveFolderWithContentsRecursive(child);

        foreach (var presetId in folder.PresetIds.ToList()) {
            var preset = CustomPresets.Find(p => p.UniqueId == presetId);
            if (preset != null) {
                if (SelectedGuid == presetId.ToString()) {
                    SelectedGuid = "";
                    PluginUi.Status = string.Empty;
                }
                CustomPresets.Remove(preset);
            }
            folder.RemovePreset(presetId);
        }

        Folders.Remove(folder);
        InvalidatePresetListCache();
    }

    public void RemoveFolderWithContents(Guid folderId) {
        var folder = Folders.Find(f => f.UniqueId == folderId);
        if (folder != null)
            RemoveFolderWithContentsRecursive(folder);
        Configuration.Save();
    }

    public void RegisterPreset(CustomPresetConfig preset, bool select = true) {
        CustomPresetConfig? oldPreset = null;
        oldPreset = SelectedPreset;
        CustomPresets.Add(preset);
        InvalidatePresetListCache();
        if (select) {
            SelectedGuid = preset.UniqueId.ToString();
            PluginUi.Status = string.Empty;
        }

        if (select)
            NotifyPresetSelected(preset, oldPreset);

        Configuration.Save();
    }

    private unsafe void NotifyPresetSelected(BasePresetConfig? newPreset, BasePresetConfig? oldPreset) {
        if (oldPreset is CustomPresetConfig old)
            old.TryResetCounter();

        var from = oldPreset?.PresetName ?? AutoHook.GlobalPresetName;
        var to = newPreset?.PresetName ?? AutoHook.GlobalPresetName;
        if (from != to) {
            WorldState.Get().Decide(DecisionContext.PresetSwitch, true, $"{from} -> {to}", $"Reason: {_selectReason ?? ReasonManual}", to);
        }

        if (newPreset is CustomPresetConfig { ListOfFish: var fishCaught }) {
            if (fishCaught.Any(c => c.Fish.IsLocked))
                IChatGui.Get().PrintError($"[AutoHook] Unable to catch one or more fish under Fish Caught. Folklore tome not unlocked.");
            // don't bother checking if you're not on fisher
            if (IPlayerState.Get().ClassJob.RowId is 18 && fishCaught.Any(c => c.Fish.MinGathering > GamePlayerState.Instance()->GetAttributeByIndex(PlayerAttribute.Gathering)))
                IChatGui.Get().PrintError($"[AutoHook] Unable to catch one or more fish under Fish Caught. Gathering attribute is too low.");
        }
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
