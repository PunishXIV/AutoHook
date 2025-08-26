using System.Linq;
using AutoHook.Classes;
using AutoHook.Configurations;
using AutoHook.Data;
using AutoHook.Enums;
using AutoHook.SeFunctions;
using AutoHook.Utils;

namespace AutoHook.Fishing;

public partial class FishingManager
{
    private FishConfig? GetLastCatchConfig()
    {
        if (_lastCatch == null)
            return null;

        return Presets.SelectedPreset?.GetFishById(_lastCatch.Id) ?? Presets.DefaultPreset.GetFishById(_lastCatch.Id);
    }
    
    private bool UseFishCaughtActions(FishConfig? lastFishCatchCfg)
    {
        BaseActionCast? cast = null;

        if (lastFishCatchCfg == null || !lastFishCatchCfg.Enabled || _lastStep.HasFlag(FishingSteps.PresetSwapped))
            return false;

        if (PlayerRes.HasStatus(IDs.Status.FishersIntuition) && lastFishCatchCfg.IgnoreOnIntuition)
            return false;

        var caughtCount = FishingHelper.GetFishCount(lastFishCatchCfg.UniqueId);

        if (lastFishCatchCfg.IdenticalCast.IsAvailableToCast(caughtCount))
            cast = lastFishCatchCfg.IdenticalCast;

        if (lastFishCatchCfg.SurfaceSlap.IsAvailableToCast())
            cast = lastFishCatchCfg.SurfaceSlap;

        if (cast != null)
        {
            PlayerRes.CastActionDelayed(cast.Id, cast.ActionType, cast.Name);
            return true;
        }

        return false;
    }

    private void CheckFishCaughtSwap(FishConfig? lastCatchCfg)
    {
        if (lastCatchCfg == null || !lastCatchCfg.Enabled)
            return;

        var guid = lastCatchCfg.UniqueId;
        var caughtCount = FishingHelper.GetFishCount(guid);

        if (lastCatchCfg.SwapPresets && !FishingHelper.SwappedPreset(guid) &&
            !_lastStep.HasFlag(FishingSteps.PresetSwapped))
        {
            if (caughtCount >= lastCatchCfg.SwapPresetCount &&
                lastCatchCfg.PresetToSwap != Presets.SelectedPreset?.PresetName)
            {
                var preset =
                    Presets.CustomPresets.FirstOrDefault(preset => preset.PresetName == lastCatchCfg.PresetToSwap);

                FishingHelper.AddPresetSwap(guid); // one try per catch
                _lastStep |= FishingSteps.PresetSwapped;

                if (preset == null)
                    Service.PrintChat(@$"Preset {lastCatchCfg.PresetToSwap} not found.");
                else
                {
                    Service.Save();
                    Presets.SelectedPreset = preset;
                    Service.PrintChat(@$"[Fish Caught] Swapping current preset to {lastCatchCfg.PresetToSwap}");
                    Service.Save();
                }
            }
        }

        if (lastCatchCfg.SwapBait && !FishingHelper.SwappedBait(guid) && !_lastStep.HasFlag(FishingSteps.BaitSwapped))
        {
            if (caughtCount >= lastCatchCfg.SwapBaitCount &&
                lastCatchCfg.BaitToSwap.Id != Service.BaitManager.Current)
            {
                var result = Service.BaitManager.ChangeBait(lastCatchCfg.BaitToSwap);

                FishingHelper.AddBaitSwap(guid); // one try per catch
                _lastStep |= FishingSteps.BaitSwapped;
                if (result == BaitManager.ChangeBaitReturn.Success)
                {
                    Service.PrintChat(@$"[Fish Caught] Swapping bait to {lastCatchCfg.BaitToSwap.Name}");
                    Service.Save();
                }
                if(lastCatchCfg.SwapBaitResetCount) FishingHelper.ToBeRemoved.Add(guid);
            }
        }
    }
}