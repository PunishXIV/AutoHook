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
        Configuration.MutateSerialized(() => {
            var newPreset = new CustomPresetConfig(presetName);
            CustomPresets.Add(newPreset);
            InvalidatePresetListCache();
        });
        Service.Save();
    }

    public override void AddNewPreset(BasePresetConfig preset) {
        Configuration.MutateSerialized(() => {
            // i needed a way to copy the object without reference, im too dumb to think of another way
            var json = JsonConvert.SerializeObject(preset);
            var copy = JsonConvert.DeserializeObject<CustomPresetConfig>(json);
            copy!.UniqueId = Guid.NewGuid();
            CustomPresets.Add(copy);
            InvalidatePresetListCache();
        });
        Service.Save();
    }

    public override void RemovePreset(Guid value) {
        Configuration.MutateSerialized(() => {
            var preset = CustomPresets.Find(p => p.UniqueId == value);
            if (preset == null)
                return;

            if (SelectedGuid == value.ToString()) {
                SelectedGuid = "";
                Service.Status = string.Empty;
            }

            foreach (var folder in Folders)
                folder.RemovePreset(value);

            CustomPresets.Remove(preset);
            InvalidatePresetListCache();
        });
        Service.Save();
    }

    public override void OnSelectedPreset(BasePresetConfig? newPreset, BasePresetConfig? oldPreset) {
        NotifyPresetSelected(newPreset, oldPreset);
        Service.Save();
    }

    public override void SwapIndex(int itemIndex, int targetIndex) {
        Configuration.MutateSerialized(() => {
            var moved = CustomPresets[itemIndex];
            if (moved == null)
                return;

            CustomPresets.RemoveAt(itemIndex);
            CustomPresets.Insert(targetIndex, moved);
            InvalidatePresetListCache();
        });
        Service.Save();
    }

    public void AddNewFolder(string folderName) {
        Configuration.MutateSerialized(() => Folders.Add(new PresetFolder(folderName)));
        Service.Save();
    }

    public void AddNewFolder(string folderName, Guid? parentFolderId) {
        Configuration.MutateSerialized(() => {
            Folders.Add(new PresetFolder(folderName) {
                ParentFolderId = parentFolderId,
            });
        });
        Service.Save();
    }

    public void RemoveFolder(Guid folderId) {
        Configuration.MutateSerialized(() => {
            var folder = Folders.Find(f => f.UniqueId == folderId);
            if (folder != null)
                Folders.Remove(folder);
        });
        Service.Save();
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
                    Service.Status = string.Empty;
                }
                CustomPresets.Remove(preset);
            }
            folder.RemovePreset(presetId);
        }

        Folders.Remove(folder);
        InvalidatePresetListCache();
    }

    public void RemoveFolderWithContents(Guid folderId) {
        Configuration.MutateSerialized(() => {
            var folder = Folders.Find(f => f.UniqueId == folderId);
            if (folder != null)
                RemoveFolderWithContentsRecursive(folder);
        });
        Service.Save();
    }

    public void RegisterPreset(CustomPresetConfig preset, bool select = true) {
        CustomPresetConfig? oldPreset = null;
        Configuration.MutateSerialized(() => {
            oldPreset = SelectedPreset;
            CustomPresets.Add(preset);
            InvalidatePresetListCache();
            if (select) {
                SelectedGuid = preset.UniqueId.ToString();
                Service.Status = string.Empty;
            }
        });

        if (select)
            NotifyPresetSelected(preset, oldPreset);

        Service.Save();
    }

    private void NotifyPresetSelected(BasePresetConfig? newPreset, BasePresetConfig? oldPreset) {
        if (oldPreset is CustomPresetConfig old)
            old.TryResetCounter();

        var from = oldPreset?.PresetName ?? Service.GlobalPresetName;
        var to = newPreset?.PresetName ?? Service.GlobalPresetName;
        if (from != to) {
            DecisionLog.Start("Preset Switch", to)
                .About($"Reason: {_selectReason ?? ReasonManual}")
                .Chose($"{from} -> {to}");
        }

        if (newPreset is CustomPresetConfig { ListOfFish: var fishCaught } && fishCaught.Any(c => c.Fish.IsLocked)) {
            Svc.Chat.PrintError($"[AutoHook] Unable to catch one or more fish under Fish Caught. Folklore tome not unlocked.");
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
