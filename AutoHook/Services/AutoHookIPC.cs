using ECommons.EzIpcManager;

namespace AutoHook.Services;

public class AutoHookIPC : IPluginService {
    private readonly Configuration _cfg = Configuration.C;

    public AutoHookIPC() {
        EzIPC.Init(this, "AutoHook");
    }

    [EzIPC]
    public void SetPluginState(bool state) {
        _cfg.PluginEnabled = state;
        Configuration.Save();
    }

    [EzIPC]
    public bool GetPluginState() {
        return _cfg.PluginEnabled;
    }

    [EzIPC]
    public void StartFishing()
        => FishingSessionManager.Get().StartFishing();

    [EzIPC]
    public bool GetAutoStartFishing() {
        return _cfg.AutoStartFishing;
    }

    [EzIPC]
    public void SetAutoStartFishing(bool state) {
        _cfg.AutoStartFishing = state;
        Configuration.Save();
    }

    [EzIPC]
    public void SetAutoGigState(bool state) {
        _cfg.AutoGigConfig.AutoGigEnabled = state;
        Configuration.Save();
    }

    [EzIPC]
    public void SetPreset(string preset) {
        _cfg.HookPresets.Select(_cfg.HookPresets.CustomPresets.FirstOrDefault(x => x.PresetName == preset), FishingPresets.ReasonIpc);
        Configuration.Save();
    }

    public void SetPresetAutogig(string preset) {
        var match = _cfg.AutoGigConfig.Presets.FirstOrDefault(x => x.PresetName == preset);
        if (match != null)
            _cfg.AutoGigConfig.SetActivePreset(match);
        Configuration.Save();
    }

    [EzIPC]
    public void CreateAndSelectAnonymousPreset(string preset) {
        if (Configuration.ImportPreset(preset) is not CustomPresetConfig customPreset) return;

        PresetImport.ImportPresets(
            _cfg.HookPresets,
            [customPreset],
            new PresetImportOptions {
                MakeAnonymous = true,
                SelectFirst = true,
                SelectReason = FishingPresets.ReasonIpc
            });
        Configuration.Save();
    }

    [EzIPC]
    public bool CreateAndSelectAutoAnonymousPreset(uint itemId, uint baitId, bool stopAfterCaughtOnce) {
        if (CustomPresetConfig.CreateAutoPreset(baitId, itemId, stopAfterCaughtOnce) is not CustomPresetConfig preset)
            return false;

        PresetImport.ImportPresets(
            _cfg.HookPresets,
            [preset],
            new PresetImportOptions {
                MakeAnonymous = true,
                SelectFirst = true,
                SelectReason = FishingPresets.ReasonIpc
            });
        Configuration.Save();
        return true;
    }

    [EzIPC]
    public void ImportAndSelectPreset(string preset) {
        var import = Configuration.ImportPreset(preset);
        if (import == null) return;

        if (import is CustomPresetConfig customPreset) {
            PresetImport.ImportPresets(
                _cfg.HookPresets,
                [customPreset],
                new PresetImportOptions {
                    SelectFirst = true,
                    SelectReason = FishingPresets.ReasonIpc
                });
        }
        else if (import is AutoGigConfig gigPreset) {
            _cfg.AutoGigConfig.AddNewPreset(gigPreset);
        }
        Configuration.Save();
    }

    [EzIPC]
    public void ImportAndSelectFolder(string folder) {
        if (Configuration.ImportFolder(folder) is not { } import)
            return;

        PresetImport.ImportFolderTree(
            _cfg.HookPresets,
            import.Folder,
            import.Folders,
            import.Presets,
            new PresetImportOptions {
                SelectFirst = true,
                SelectReason = FishingPresets.ReasonIpc
            });
        Configuration.Save();
    }

    [EzIPC]
    public void CreateAndSelectAnonymousFolder(string folder) {
        if (Configuration.ImportFolder(folder) is not { } import)
            return;

        PresetImport.ImportFolderTree(
            _cfg.HookPresets,
            import.Folder,
            import.Folders,
            import.Presets,
            new PresetImportOptions {
                MakeAnonymous = true,
                SelectFirst = true,
                SelectReason = FishingPresets.ReasonIpc
            });
        Configuration.Save();
    }

    [EzIPC]
    public void DeleteSelectedPreset() {
        var selected = _cfg.HookPresets.SelectedPreset;
        if (selected == null) return;
        _cfg.HookPresets.RemovePreset(selected.UniqueId);
        _cfg.HookPresets.Select(null, FishingPresets.ReasonIpc);
        Configuration.Save();
    }

    [EzIPC]
    public void DeleteAllAnonymousPresets() {
        _cfg.HookPresets.CustomPresets.RemoveAll(p => p.IsAnonymous);
        Configuration.Save();
    }

    [EzIPC]
    public bool SwapBaitById(uint baitId)
        => BaitComponent.ChangeBait(baitId) is ChangeBaitReturn.Success or ChangeBaitReturn.AlreadyEquipped;

    [EzIPC]
    public bool SwapBait(string baitNameOrId) {
        if (string.IsNullOrWhiteSpace(baitNameOrId))
            return false;

        if (uint.TryParse(baitNameOrId, out var parsedId))
            return SwapBaitById(parsedId);

        var bait = FishBaitCatalog.Get().Baits.FirstOrDefault(b => string.Equals(b.Name, baitNameOrId, StringComparison.OrdinalIgnoreCase));

        if (bait == null || bait.Id <= 0)
            return false;

        return BaitComponent.ChangeBait((uint)bait.Id) is ChangeBaitReturn.Success or ChangeBaitReturn.AlreadyEquipped;
    }

    [EzIPC]
    public bool SwapSwimbaitByIndex(byte index)
        => BaitComponent.ChangeSwimbait(index) is ChangeBaitReturn.Success or ChangeBaitReturn.AlreadyEquipped;
}
