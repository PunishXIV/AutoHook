using AutoHook.Configurations;
using System.Linq;
using ECommons.EzIpcManager;

namespace AutoHook.IPC;

public class AutoHookIPC
{
    public  void Init()
    {
        EzIPC.Init(this);
    }
    
    [EzIPC]
    public static void SetPluginState(bool state)
    {
        Service.Configuration.PluginEnabled = state;
        Service.Save();
    }
    
    [EzIPC]
    public static void SetAutoGigState(bool state)
    {
        Service.Configuration.AutoGigConfig.AutoGigEnabled = state;
        Service.Save();
    }
    [EzIPC]
    public static void SetPreset(string preset)
    {
        Service.Save();
        Service.Configuration.HookPresets.SelectedPreset =
            Service.Configuration.HookPresets.CustomPresets.FirstOrDefault(x => x.PresetName == preset);
        Service.Save();
    }

    [EzIPC]
    public static void CreateAndSelectAnonymousPreset(string preset)
    {
        var _import = Configuration.ImportPreset(preset);
        if (_import == null) return;
        var name = $"anon_{_import.PresetName}";
        _import.RenamePreset(name);
        Service.Save();
        Service.Configuration.HookPresets.AddNewPreset(_import);
        Service.Configuration.HookPresets.SelectedPreset =
            Service.Configuration.HookPresets.CustomPresets.FirstOrDefault(x => x.PresetName == name);
        Service.Save();
    }

    [EzIPC]
    public static void DeleteSelectedPreset()
    {
        var selected = Service.Configuration.HookPresets.SelectedPreset;
        if (selected == null) return;
        Service.Configuration.HookPresets.RemovePreset(selected.UniqueId);
        Service.Configuration.HookPresets.SelectedPreset = null;
        Service.Save();
    }

    [EzIPC]
    public static void DeleteAllAnonymousPresets()
    {
        Service.Configuration.HookPresets.CustomPresets.RemoveAll(p => p.PresetName.StartsWith("anon_"));
        Service.Save();
    }
}