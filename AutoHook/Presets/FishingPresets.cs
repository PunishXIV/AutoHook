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

    // folder ops mutate lists the preset tab may be iterating mid-draw, so PresetIds is always replaced, never edited in place.
    public void AddNewFolder(string folderName, Guid? parentFolderId = null) {
        Folders.Add(new PresetFolder(folderName) { ParentFolderId = parentFolderId });
        Configuration.Save();
    }

    public PresetFolder? FindFolder(Guid folderId) => Folders.Find(f => f.UniqueId == folderId);

    public IEnumerable<PresetFolder> ChildFolders(PresetFolder folder) => Folders.Where(f => f.ParentFolderId == folder.UniqueId);

    public IEnumerable<Guid> PresetIdsInTree(PresetFolder folder) => folder.PresetIds.Concat(ChildFolders(folder).SelectMany(PresetIdsInTree));

    public bool IsDescendantOf(PresetFolder ancestor, PresetFolder candidate) {
        var parentId = candidate.ParentFolderId;
        while (parentId is { } id && FindFolder(id) is { } parent) {
            if (parent == ancestor)
                return true;
            parentId = parent.ParentFolderId;
        }
        return false;
    }

    public void MovePresetToFolder(Guid presetId, PresetFolder folder) {
        if (folder.ContainsPreset(presetId))
            return;
        foreach (var f in Folders)
            f.RemovePreset(presetId);
        folder.PresetIds = [.. folder.PresetIds, presetId];
        Configuration.Save();
    }

    public void MovePresetTo(Guid presetId, CustomPresetConfig target) {
        if (presetId == target.UniqueId)
            return;

        var folder = GetFolderContainingPreset(target.UniqueId);
        foreach (var f in Folders.Where(f => f != folder))
            f.RemovePreset(presetId);

        if (folder == null) {
            var from = CustomPresets.FindIndex(p => p.UniqueId == presetId);
            if (from >= 0)
                SwapIndex(from, CustomPresets.IndexOf(target));
        }
        else {
            var ids = folder.PresetIds.ToList();
            var to = ids.IndexOf(target.UniqueId);
            ids.Remove(presetId);
            ids.Insert(Math.Min(to, ids.Count), presetId);
            folder.PresetIds = ids;
        }
        Configuration.Save();
    }

    public void MoveFolderOnto(PresetFolder moving, PresetFolder target) {
        if (moving == target)
            return;

        if (moving.ParentFolderId == target.ParentFolderId) {
            Folders.Remove(moving);
            Folders.Insert(Folders.IndexOf(target), moving);
        }
        else if (target.ParentFolderId == moving.UniqueId)
            (moving.ParentFolderId, target.ParentFolderId) = (target.UniqueId, moving.ParentFolderId);
        else if (!IsDescendantOf(moving, target))
            moving.ParentFolderId = target.UniqueId;
        else
            return;
        Configuration.Save();
    }

    public void MoveFolderBeside(PresetFolder moving, CustomPresetConfig preset) {
        var parent = GetFolderContainingPreset(preset.UniqueId);
        if (parent != null && (parent == moving || IsDescendantOf(moving, parent)))
            return;
        moving.ParentFolderId = parent?.UniqueId;
        Configuration.Save();
    }

    public void CopyFolder(PresetFolder source) {
        CopyFolderTree(source, source.ParentFolderId, $"Copy_{source.FolderName}");
        Configuration.Save();
    }

    private void CopyFolderTree(PresetFolder source, Guid? parentFolderId, string name) {
        var copy = new PresetFolder(name) { ParentFolderId = parentFolderId, IsExpanded = source.IsExpanded };
        foreach (var preset in source.PresetIds.Select(id => CustomPresets.Find(p => p.UniqueId == id)).OfType<CustomPresetConfig>()) {
            var presetCopy = PresetImport.ClonePreset(preset);
            presetCopy.UniqueId = Guid.NewGuid();
            CustomPresets.Add(presetCopy);
            copy.AddPreset(presetCopy.UniqueId);
        }
        InvalidatePresetListCache();

        var children = ChildFolders(source).ToList();
        Folders.Add(copy);
        foreach (var child in children)
            CopyFolderTree(child, copy.UniqueId, child.FolderName);
    }

    private void RemoveFolderWithContentsRecursive(PresetFolder folder) {
        var childFolders = ChildFolders(folder).ToList();
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
        var oldPreset = SelectedPreset;
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

            // rule, catch and ocean swaps go through Select with their own reason and leave a hard quit in place
            if ((_selectReason ?? ReasonManual) == ReasonManual)
                RodFishingModule.ClearAutoStartLock();
        }

        if (newPreset is CustomPresetConfig { ListOfFish: var fishCaught }) {
            if (fishCaught.Any(c => c.Fish.IsLocked))
                IChatGui.Get().PrintError($"[AutoHook] Unable to catch one or more fish under Fish Caught. Folklore tome not unlocked.");
            // don't bother checking if you're not on fisher
            if (IPlayerState.Get().ClassJob.RowId is 18 && fishCaught.Any(c => c.Fish.MinGathering > GamePlayerState.Instance()->GetAttributeByIndex(PlayerAttribute.Gathering)))
                IChatGui.Get().PrintError($"[AutoHook] Unable to catch one or more fish under Fish Caught. Gathering attribute is too low.");
        }
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
