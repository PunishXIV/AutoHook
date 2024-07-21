using System;
using System.Collections.Generic;
using System.Linq;
using ECommons.Throttlers;
using Newtonsoft.Json;

namespace AutoHook.Classes;

public abstract class BasePreset
{
    public string SelectedGuid { get; set; } = "";
    
    [JsonIgnore] public virtual BasePresetConfig? SelectedPreset
    {
        get
        {
            return PresetList.FirstOrDefault(p => p.UniqueId.ToString() == SelectedGuid);
        }
        set
        {
            Service.Status = string.Empty;
            if (value != null)
            {
                SelectedGuid = value.UniqueId.ToString();
                OnSelectedPreset(value);
            } else 
                SelectedGuid = "";
        }
    }

    public Guid UniqueId { get; protected set; } = Guid.NewGuid();

    public abstract void AddNewPreset(string presetName);

    public abstract void AddNewPreset(BasePresetConfig preset);

    public abstract void RemovePreset(Guid value);

    public virtual void RenamePreset(Guid value, string newName)
    {
        var preset = PresetList.Find(p => p.UniqueId == value);
        if (preset == null)
            return;

        preset.PresetName = newName;
        Service.Save();
    }

    public virtual void OnSelectedPreset(BasePresetConfig value)
    {
        Service.Save();
    }
    
    public virtual BasePresetConfig? GetPreset(Guid value)
    {
        return PresetList.Find(p => p.UniqueId == value);
    }

    [JsonIgnore] public abstract List<BasePresetConfig> PresetList { get; }
    
}