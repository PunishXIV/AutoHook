using System;
using System.Collections.Generic;
using AutoHook.Classes;
using AutoHook.Data;
using AutoHook.Enums;
using AutoHook.Utils;

namespace AutoHook.Configurations.old_config;

public class OldHookConfig
{
    public bool Enabled = true;
    
    public BaitFishClass BaitFish = new();
    
    public BaseHookset NormalHook = new(IDs.Status.None);
    public BaseHookset IntuitionHook = new(IDs.Status.FishersIntuition);

    public bool HookWeakEnabled = true;
    public bool HookWeakIntuitionEnabled = true;
    public bool HookWeakDHTHEnabled = true;
    public bool HookWeakOnlyWhenActiveSlap = false;
    public bool HookWeakOnlyWhenNOTActiveSlap = false;
    public HookType HookTypeWeak = HookType.Precision;
    public HookType HookTypeWeakIntuition = HookType.Precision;

    public bool HookStrongEnabled = true;
    public bool HookStrongIntuitionEnabled = true;
    public bool HookStrongDHTHEnabled = true;
    public bool HookStrongOnlyWhenActiveSlap = false;
    public bool HookStrongOnlyWhenNOTActiveSlap = false;
    public HookType HookTypeStrong = HookType.Powerful;
    public HookType HookTypeStrongIntuition = HookType.Powerful;

    public bool HookLegendaryEnabled = true;
    public bool HookLegendaryIntuitionEnabled = true;
    public bool HookLegendaryDHTHEnabled = true;
    public bool HookLegendaryOnlyWhenActiveSlap = false;
    public bool HookLegendaryOnlyWhenNOTActiveSlap = false;
    public HookType HookTypeLegendary = HookType.Powerful;
    public HookType HookTypeLegendaryIntuition = HookType.Powerful;

    public bool UseCustomIntuitionHook = false;

    /*public bool UseAutoMooch = true;
    public bool UseAutoMooch2 = false;
    public bool OnlyMoochIntuition = false;*/

    /*public bool UseSurfaceSlap = false;
    public bool UseIdenticalCast = false;*/
    
    public bool UseDoubleHook = false;
    public bool UseTripleHook = false;
    public bool UseDHTHPatience = false;
    public bool UseDHTHOnlyIdenticalCast = false;
    public bool UseDHTHOnlySurfaceSlap = false;
    public bool LetFishEscape = false;

    public double MaxTimeDelay = 0;
    public double MinTimeDelay = 0;

    public bool UseChumTimer = false;
    public double MaxChumTimeDelay = 0;
    public double MinChumTimeDelay = 0;

    public bool StopAfterCaught = false;
    public int StopAfterCaughtLimit = 1;
    public bool StopAfterResetCount = false;

    
    public FishingSteps StopFishingStep = FishingSteps.None;

    /*public HookConfig(string bait)
    {
        BaitName = bait;
    }*/
    
    public void ConvertV3ToV4()
    {
        
        if (NormalHook == null)
            NormalHook =  new(IDs.Status.None);

        if (IntuitionHook == null)
            IntuitionHook =  new(IDs.Status.None);
        
        Convert(NormalHook, false);
        Convert(IntuitionHook, true);

        IntuitionHook.UseCustomStatusHook = UseCustomIntuitionHook;
    }

    private void Convert(BaseHookset hookset, bool isIntuition)
    {
        Dictionary<BaseBiteConfig, (bool, HookType, bool, bool, bool)> normal;

        if (isIntuition)
        {
            normal = new ()
            {
                {
                    hookset.PatienceWeak,
                    (HookWeakIntuitionEnabled, HookTypeWeakIntuition, HookWeakOnlyWhenActiveSlap, HookWeakOnlyWhenNOTActiveSlap, false)
                },
                {
                    hookset.PatienceStrong,
                    (HookStrongIntuitionEnabled, HookTypeStrongIntuition, HookStrongOnlyWhenActiveSlap, HookStrongOnlyWhenNOTActiveSlap, false)
                },
                {
                    hookset.PatienceLegendary,
                    (HookLegendaryIntuitionEnabled, HookTypeLegendaryIntuition, HookLegendaryOnlyWhenActiveSlap, HookLegendaryOnlyWhenNOTActiveSlap, false)
                },
            };
        }
        else
        {
            normal = new()
            {
                {
                    hookset.PatienceWeak,
                    (HookWeakEnabled, HookTypeWeak, HookWeakOnlyWhenActiveSlap, HookWeakOnlyWhenNOTActiveSlap, false)
                },
                {
                    hookset.PatienceStrong,
                    (HookStrongEnabled, HookTypeStrong, HookStrongOnlyWhenActiveSlap, HookStrongOnlyWhenNOTActiveSlap, false)
                },
                {
                    hookset.PatienceLegendary,
                    (HookLegendaryEnabled, HookTypeLegendary, HookLegendaryOnlyWhenActiveSlap, HookLegendaryOnlyWhenNOTActiveSlap, false)
                },
            };
        }

        var doubleHook = new Dictionary<BaseBiteConfig, (bool, HookType, bool, bool, bool)>
        {
            {
                hookset.DoubleWeak,
                (HookWeakDHTHEnabled, HookType.Double, UseDHTHOnlySurfaceSlap, false, UseDHTHOnlyIdenticalCast)
            },
            {
                hookset.DoubleStrong,
                (HookStrongDHTHEnabled, HookType.Double, UseDHTHOnlySurfaceSlap, false, UseDHTHOnlyIdenticalCast)
            },
            {
                hookset.DoubleLegendary,
                (HookLegendaryDHTHEnabled, HookType.Double, UseDHTHOnlySurfaceSlap, false, UseDHTHOnlyIdenticalCast)
            }
        };

        var tripleHook = new Dictionary<BaseBiteConfig, (bool, HookType, bool, bool, bool)>
        {
            {
                hookset.TripleWeak,
                (HookWeakDHTHEnabled, HookType.Triple, UseDHTHOnlySurfaceSlap, false, UseDHTHOnlyIdenticalCast)
            },
            {
                hookset.TripleStrong,
                (HookStrongDHTHEnabled, HookType.Triple, UseDHTHOnlySurfaceSlap, false, UseDHTHOnlyIdenticalCast)
            },
            {
                hookset.TripleLegendary,
                (HookLegendaryDHTHEnabled, HookType.Triple, UseDHTHOnlySurfaceSlap, false, UseDHTHOnlyIdenticalCast)
            }
        };

        var list = new List<Dictionary<BaseBiteConfig, (bool, HookType, bool, bool, bool)>>
            { normal, doubleHook, tripleHook };

        foreach (var dict in list)
        {
            foreach (var (bite, (enabled, type, slapActive, slapNotActive, identicalActive)) in dict)
            {
                bite.HooksetEnabled = enabled;
                bite.HooksetType = type;
                bite.OnlyWhenActiveSlap = slapActive;
                bite.OnlyWhenNotActiveSlap = slapNotActive;
                
                bite.OnlyWhenActiveIdentical = identicalActive;

                bite.MinHookTimer = MinTimeDelay;
                bite.MaxHookTimer = MaxTimeDelay;

                if (MinTimeDelay > 0 || MaxTimeDelay > 0)
                {
                    bite.HookTimerEnabled = true;
                }
                bite.ChumMinHookTimer = MinChumTimeDelay;
                bite.ChumMaxHookTimer = MaxChumTimeDelay;
                bite.ChumTimerEnabled = UseChumTimer;
            }
        }

        hookset.UseDoubleHook = UseDoubleHook;
        hookset.LetFishEscapeDoubleHook = LetFishEscape;

        hookset.UseTripleHook = UseTripleHook;
        hookset.LetFishEscapeTripleHook = LetFishEscape;

        hookset.StopAfterCaught = StopAfterCaught;
        hookset.StopAfterCaughtLimit = StopAfterCaughtLimit;
        hookset.StopAfterResetCount = StopAfterResetCount;
        hookset.StopFishingStep = StopFishingStep;
    }
}