using ECommons.EzIpcManager;

namespace AutoHook.IPC;

public class AutoHookIPC {
    private readonly Configuration _cfg = Service.Configuration;

    public AutoHookIPC() {
        EzIPC.Init(this, "AutoHook");
    }

    [EzIPC]
    public void SetPluginState(bool state) {
        WriteConfig(() => _cfg.PluginEnabled = state);
        Service.Save();
    }

    [EzIPC]
    public bool GetPluginState() {
        return _cfg.PluginEnabled;
    }

    [EzIPC]
    public bool GetAutoStartFishing() {
        return _cfg.AutoStartFishing;
    }

    [EzIPC]
    public void SetAutoStartFishing(bool state) {
        WriteConfig(() => _cfg.AutoStartFishing = state);
        Service.Save();
    }

    [EzIPC]
    public void SetAutoGigState(bool state) {
        WriteConfig(() => _cfg.AutoGigConfig.AutoGigEnabled = state);
        Service.Save();
    }

    [EzIPC]
    public void SetPreset(string preset) {
        WriteConfig(() => _cfg.HookPresets.Select(_cfg.HookPresets.CustomPresets.FirstOrDefault(x => x.PresetName == preset), FishingPresets.ReasonIpc));
        Service.Save();
    }

    public void SetPresetAutogig(string preset) {
        WriteConfig(() => _cfg.AutoGigConfig.SelectedPreset = _cfg.AutoGigConfig.Presets.FirstOrDefault(x => x.PresetName == preset));
        Service.Save();
    }

    [EzIPC]
    public void CreateAndSelectAnonymousPreset(string preset) {
        if (Configuration.ImportPreset(preset) is not CustomPresetConfig customPreset) return;

        WriteConfig(() => {
            var nameMap = CustomPresetConfig.BuildAnonymousNameMap([customPreset]);
            customPreset.RenamePreset(nameMap[customPreset.PresetName]);
            CustomPresetConfig.RemapPresetSwapReferences(customPreset, nameMap);
            _cfg.HookPresets.AddNewPreset(customPreset);
            _cfg.HookPresets.Select(_cfg.HookPresets.CustomPresets.FirstOrDefault(x => x.PresetName == nameMap.Values.First()), FishingPresets.ReasonIpc);
        });
        Service.Save();
    }

    [EzIPC]
    public bool CreateAndSelectAutoAnonymousPreset(uint itemId, uint baitId, bool stopAfterCaughtOnce) {
        if (CustomPresetConfig.CreateAutoPreset(baitId, itemId, stopAfterCaughtOnce) is not CustomPresetConfig preset)
            return false;

        WriteConfig(() => {
            var nameMap = CustomPresetConfig.BuildAnonymousNameMap([preset]);
            preset.RenamePreset(nameMap[preset.PresetName]);
            _cfg.HookPresets.AddNewPreset(preset);
            _cfg.HookPresets.Select(_cfg.HookPresets.CustomPresets.FirstOrDefault(x => x.PresetName == preset.PresetName), FishingPresets.ReasonIpc);
        });
        Service.Save();
        return true;
    }

    [EzIPC]
    public void ImportAndSelectPreset(string preset) {
        var import = Configuration.ImportPreset(preset);
        if (import == null) return;

        WriteConfig(() => {
            import.RenamePreset(import.PresetName);

            if (import is CustomPresetConfig customPreset)
                _cfg.HookPresets.AddNewPreset(customPreset);
            else if (import is AutoGigConfig gigPreset)
                _cfg.AutoGigConfig.AddNewPreset(gigPreset);
        });
        Service.Save();
    }

    [EzIPC]
    public void ImportAndSelectFolder(string folder) {
        if (Configuration.ImportFolder(folder) is { } import) {
            WriteConfig(() => ImportFolder(import, anonymous: false));
        }
    }

    [EzIPC]
    public void CreateAndSelectAnonymousFolder(string folder) {
        if (Configuration.ImportFolder(folder) is { } import) {
            WriteConfig(() => ImportFolder(import, anonymous: true));
        }
    }

    [EzIPC]
    public void DeleteSelectedPreset() {
        WriteConfig(() => {
            var selected = _cfg.HookPresets.SelectedPreset;
            if (selected == null) return;
            _cfg.HookPresets.RemovePreset(selected.UniqueId);
            _cfg.HookPresets.Select(null, FishingPresets.ReasonIpc);
        });
        Service.Save();
    }

    [EzIPC]
    public void DeleteAllAnonymousPresets() {
        WriteConfig(() => _cfg.HookPresets.CustomPresets.RemoveAll(p => p.IsAnonymous));
        Service.Save();
    }

    [EzIPC]
    public bool SwapBaitById(uint baitId)
        => FishingManager.ChangeBait(baitId) is FishingManager.ChangeBaitReturn.Success or FishingManager.ChangeBaitReturn.AlreadyEquipped;

    [EzIPC]
    public bool SwapBait(string baitNameOrId) {
        if (string.IsNullOrWhiteSpace(baitNameOrId))
            return false;

        if (uint.TryParse(baitNameOrId, out var parsedId))
            return SwapBaitById(parsedId);

        var bait = GameRes.Baits.FirstOrDefault(b => string.Equals(b.Name, baitNameOrId, StringComparison.OrdinalIgnoreCase));

        if (bait == null || bait.Id <= 0)
            return false;

        return FishingManager.ChangeBait((uint)bait.Id) is FishingManager.ChangeBaitReturn.Success or FishingManager.ChangeBaitReturn.AlreadyEquipped;
    }

    [EzIPC]
    public bool SwapSwimbaitByIndex(byte index)
        => FishingManager.ChangeSwimbait(index) is FishingManager.ChangeBaitReturn.Success or FishingManager.ChangeBaitReturn.AlreadyEquipped;

    private void ImportFolder((PresetFolder Folder, List<PresetFolder> Folders, List<CustomPresetConfig> Presets) folderImport, bool anonymous) {
        if (anonymous) {
            var nameMap = CustomPresetConfig.BuildAnonymousNameMap(folderImport.Presets);
            foreach (var preset in folderImport.Presets)
                preset.RenamePreset(nameMap[preset.PresetName]);
            CustomPresetConfig.RemapPresetSwapReferences(folderImport.Presets, nameMap);

            foreach (var preset in folderImport.Presets)
                _cfg.HookPresets.CustomPresets.Add(preset);
        }
        else {
            foreach (var preset in folderImport.Presets)
                _cfg.HookPresets.CustomPresets.Add(preset);
            foreach (var importedFolder in folderImport.Folders)
                _cfg.HookPresets.Folders.Add(importedFolder);
        }

        if (folderImport.Presets.FirstOrDefault() is { } first)
            _cfg.HookPresets.Select(first, FishingPresets.ReasonIpc);
    }

    private static void WriteConfig(Action action) {
        Configuration.MutateSerialized(action);
    }
}
