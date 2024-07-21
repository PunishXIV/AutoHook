using System;
using AutoHook.Classes;
using AutoHook.Enums;

namespace AutoHook.Configurations;

public class ExtraConfig : BaseOption
{
    public bool Enabled = false;
    
    public bool SwapBaitIntuitionGain = false;
    public BaitFishClass BaitToSwapIntuitionGain = new();
    
    public bool SwapBaitIntuitionLost = false;
    public BaitFishClass BaitToSwapIntuitionLost = new();
    
    public bool SwapPresetIntuitionGain = false;
    public string PresetToSwapIntuitionGain = @"-";
    
    public bool SwapPresetIntuitionLost = false;
    public string PresetToSwapIntuitionLost = @"-";

    public bool SwapBaitSpectralCurrentGain = false;
    public BaitFishClass BaitToSwapSpectralCurrentGain = new();

    public bool SwapBaitSpectralCurrentLost = false;
    public BaitFishClass BaitToSwapSpectralCurrentLost = new();

    public bool SwapPresetSpectralCurrentGain = false;
    public string PresetToSwapSpectralCurrentGain = @"-";

    public bool SwapPresetSpectralCurrentLost = false;
    public string PresetToSwapSpectralCurrentLost = @"-";

    public bool ResetCounterPresetSwap = false;
    public bool QuitOnIntuitionLost = false;
    public bool StopOnIntuitionLost = false;
    
    public bool ForceBaitSwap;
    public int ForcedBaitId;
    
    // Angler's Art
    public bool StopAfterAnglersArt = false;
    public int AnglerStackQtd = 0;
    public FishingSteps AnglerStopFishingStep = FishingSteps.None;
    public bool SwapBaitAnglersArt = false;
    public BaitFishClass BaitToSwapAnglersArt = new();
    public bool SwapPresetAnglersArt = false;
    public string PresetToSwapAnglersArt = @"-";
    
    
    public override void DrawOptions()
    {
        
    }
}