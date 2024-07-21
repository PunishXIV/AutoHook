using System.Collections.Generic;

namespace AutoHook.Configurations.old_config;

public class OldPresetConfig
{
    public string PresetName { get; set; }

    public List<OldHookConfig> ListOfBaits { get; set; } = new();
    
    public List<OldHookConfig> ListOfMooch { get; set; } = new();

    public List<FishConfig> ListOfFish { get; set; } = new();

    public AutoCastsConfig AutoCastsCfg = new();
    
    public ExtraConfig ExtraCfg = new();

    public OldPresetConfig(string presetName)
    {
        PresetName = presetName;
    }
    
    public void ConvertV3ToV4()
    {
        foreach (var item in ListOfBaits)
        {
            item.ConvertV3ToV4();
        }
        
        foreach (var item in ListOfMooch)
        {
            item.ConvertV3ToV4();
        }
    }
}