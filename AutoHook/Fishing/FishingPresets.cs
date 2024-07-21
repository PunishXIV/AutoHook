using System;
using System.Collections.Generic;
using System.Linq;
using AutoHook.Classes;
using AutoHook.Configurations;
using Newtonsoft.Json;

namespace AutoHook.Fishing;

public class FishingPresets : BasePreset
{
    // Global preset, cant rename rn 
    public CustomPresetConfig DefaultPreset = new(Service.GlobalPresetName);

    public List<CustomPresetConfig> CustomPresets = new();

    [JsonIgnore] public override CustomPresetConfig? SelectedPreset => base.SelectedPreset as CustomPresetConfig;

    public override void AddNewPreset(string presetName)
    {
        var newPreset = new CustomPresetConfig(presetName);
        CustomPresets.Add(newPreset);
        SelectedGuid = newPreset.UniqueId.ToString();
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

        CustomPresets.Remove(preset);
        Service.Save();
    }

    public override void OnSelectedPreset(BasePresetConfig value)
    {
        if (value is not CustomPresetConfig preset)
            return;

        if (preset is { ExtraCfg: { Enabled: true, ResetCounterPresetSwap: true } })
            preset.ResetCounter();
        
        Service.Save();
    }


    [JsonIgnore] public override List<BasePresetConfig> PresetList => CustomPresets.Cast<BasePresetConfig>().ToList();
}