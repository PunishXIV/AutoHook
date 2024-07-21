using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using AutoHook.Classes;
using AutoHook.Classes.AutoCasts;
using AutoHook.Configurations;
using Newtonsoft.Json;

namespace AutoHook.Spearfishing;

public class SpearFishingPresets : BasePreset
{
    public bool AutoGigEnabled = false;
    public bool AutoGigHideOverlay = false;
    
    [DefaultValue(true)]
    public bool AutoGigDrawFishHitbox = true;
    
    [DefaultValue(true)]
    public bool AutoGigDrawGigHitbox = true;
    
    public AutoThaliaksFavor ThaliaksFavor = new(true);
    
    public bool CatchAll = false;
    public bool CatchAllNaturesBounty = false;

    public bool NatureBountyBeforeFish = false;
    
    public List<AutoGigConfig> Presets = new();
    
    [JsonIgnore] public override List<BasePresetConfig> PresetList => Presets.Cast<BasePresetConfig>().ToList();

    [JsonIgnore] public override AutoGigConfig? SelectedPreset => base.SelectedPreset as AutoGigConfig; 
    
    public override void AddNewPreset(string presetName) 
    {
        var newPreset = new AutoGigConfig(presetName);
        Presets.Add(newPreset);
        SelectedGuid = newPreset.UniqueId.ToString();
        Service.Save();
    }
    
    public override void AddNewPreset(BasePresetConfig preset) 
    {
        var json = JsonConvert.SerializeObject(preset); 
        var copy = JsonConvert.DeserializeObject<AutoGigConfig>(json);
        copy!.UniqueId = Guid.NewGuid();
        Presets.Add(copy);
        Service.Save();
    }

    public override void RemovePreset(Guid value)
    {
        var preset = Presets.Find(p => p.UniqueId == value);
        if (preset == null)
            return;
        
        Presets.Remove(preset);
        Service.Save();
    }
}