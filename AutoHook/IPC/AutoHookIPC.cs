using ECommons.EzIpcManager;

namespace AutoHook.IPC;

public class AutoHookIPC
{
    private readonly Configuration _cfg = Service.Configuration;

    public AutoHookIPC()
    {
        EzIPC.Init(this, "AutoHook");
    }

    [EzIPC]
    public void SetPluginState(bool state)
    {
        _cfg.PluginEnabled = state;
        Service.Save();
    }

    [EzIPC]
    public bool GetPluginState()
    {
        return _cfg.PluginEnabled;
    }

    [EzIPC]
    public bool GetAutoStartFishing()
    {
        return _cfg.AutoStartFishing;
    }
    
    [EzIPC]
    public void SetAutoStartFishing(bool state)
    {
        _cfg.AutoStartFishing = state;
        Service.Save();
    }

    [EzIPC]
    public void SetAutoGigState(bool state)
    {
        _cfg.AutoGigConfig.AutoGigEnabled = state;
        Service.Save();
    }

    [EzIPC]
    public void SetPreset(string preset)
    {
        Service.Save();
        _cfg.HookPresets.SelectedPreset =
            _cfg.HookPresets.CustomPresets.FirstOrDefault(x => x.PresetName == preset);
        Service.Save();
    }

    public void SetPresetAutogig(string preset)
    {
        Service.Save();
        _cfg.AutoGigConfig.SelectedPreset =
            _cfg.AutoGigConfig.Presets.FirstOrDefault(x => x.PresetName == preset);
        Service.Save();
    }

    [EzIPC]
    public void CreateAndSelectAnonymousPreset(string preset)
    {
        var _import = Configuration.ImportPreset(preset);
        if (_import == null) return;
        var name = $"anon_{_import.PresetName}";
        _import.RenamePreset(name);
        Service.Save();
        _cfg.HookPresets.AddNewPreset(_import);
        _cfg.HookPresets.SelectedPreset =
            _cfg.HookPresets.CustomPresets.FirstOrDefault(x => x.PresetName == name);
        Service.Save();
    }

    [EzIPC]
    public void ImportAndSelectPreset(string preset)
    {
        var _import = Configuration.ImportPreset(preset);
        if (_import == null) return;
        var name = $"{_import.PresetName}";
        _import.RenamePreset(name);

        if (_import is CustomPresetConfig customPreset)
            _cfg.HookPresets.AddNewPreset(customPreset);
        else if (_import is AutoGigConfig gigPreset)
            _cfg.AutoGigConfig.AddNewPreset(gigPreset);

        Service.Save();
    }

    [EzIPC]
    public void DeleteSelectedPreset()
    {
        var selected = _cfg.HookPresets.SelectedPreset;
        if (selected == null) return;
        _cfg.HookPresets.RemovePreset(selected.UniqueId);
        _cfg.HookPresets.SelectedPreset = null;
        Service.Save();
    }

    [EzIPC]
    public void DeleteAllAnonymousPresets()
    {
        _cfg.HookPresets.CustomPresets.RemoveAll(p => p.PresetName.StartsWith("anon_"));
        Service.Save();
    }

    [EzIPC]
    public bool SwapBaitById(uint baitId)
    {
        var result = Service.BaitManager.ChangeBait(baitId);
        return result is BaitManager.ChangeBaitReturn.Success or BaitManager.ChangeBaitReturn.AlreadyEquipped;
    }

    [EzIPC]
    public bool SwapBait(string baitNameOrId)
    {
        if (string.IsNullOrWhiteSpace(baitNameOrId))
            return false;

        if (uint.TryParse(baitNameOrId, out var parsedId))
            return SwapBaitById(parsedId);

        var bait = GameRes.Baits.FirstOrDefault(b =>
            string.Equals(b.Name, baitNameOrId, StringComparison.OrdinalIgnoreCase));

        if (bait == null || bait.Id <= 0)
            return false;

        var result = Service.BaitManager.ChangeBait((uint)bait.Id);
        return result is BaitManager.ChangeBaitReturn.Success or BaitManager.ChangeBaitReturn.AlreadyEquipped;
    }

    // Swaps the current swimbait slot by index (0,1,2).
    [EzIPC]
    public bool SwapSwimbaitByIndex(byte index)
    {
        var result = Service.BaitManager.ChangeSwimbait(index);
        return result is BaitManager.ChangeBaitReturn.Success or BaitManager.ChangeBaitReturn.AlreadyEquipped;
    }
}
