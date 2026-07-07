using AutoHook.Conditions;

namespace AutoHook.Fishing;

public partial class FishingManager {
    private FishConfig? GetLastCatchConfig() {
        if (Ws.Fishing.LastCatch is not { } lc || lc.FishId <= 0)
            return null;

        return Presets.SelectedPreset?.GetFishById(lc.FishId) ?? Presets.DefaultPreset.GetFishById(lc.FishId);
    }

    // returns null when ignore condition is active, otherwise the config
    private FishConfig? GetEffectiveCatchConfig() {
        var cfg = GetLastCatchConfig();
        return ShouldIgnoreFishSettings(cfg) ? null : cfg;
    }

    private static bool ShouldIgnoreFishSettings(FishConfig? cfg) {
        if (cfg is not { Enabled: true })
            return false;

        // Treat an "empty" ignore set (only empty groups, no conditions) as if it wasn't configured at all.
        // Can't delete all groups in advanced mode because slim mode always ensures there's a group, even if empty
        return cfg.IgnoreConditionSet is { } ignoreSet && ignoreSet.HasAnyCondition() && ignoreSet.PassesOrUnconfigured();
    }

    private static bool HasGpBlockedFishCaughtAction(FishConfig cfg)
        => SelectFishCaughtCast(cfg, gpBlockedOnly: true) != null;

    private static BaseActionCast? SelectFishCaughtCast(FishConfig cfg, bool gpBlockedOnly = false) {
        BaseActionCast? cast = null;

        bool Matches(BaseActionCast action)
            => gpBlockedOnly ? action.IsGpBlocked() : action.IsAvailableToCast();

        if (Matches(cfg.IdenticalCast))
            cast = cfg.IdenticalCast;

        if (Matches(cfg.SurfaceSlap))
            cast = cfg.SurfaceSlap;

        if (Matches(cfg.SparefulHand))
            cast = cfg.SparefulHand;

        return cast;
    }

    private bool UseFishCaughtActions(FishConfig? lastFishCatchCfg) {
        if (lastFishCatchCfg == null || !lastFishCatchCfg.Enabled || Ws.FishingStep.HasFlag(FishingSteps.PresetSwapped))
            return false;

        if (Ws.Fishing.LastCatch is { } lc && lc.FishId > 0)
            lastFishCatchCfg.SparefulHand.FishIdToCheck = lc.FishId;

        var cast = SelectFishCaughtCast(lastFishCatchCfg);
        var multiHook = lastFishCatchCfg.Multihook;
        var waitingOnGp = cast == null && HasGpBlockedFishCaughtAction(lastFishCatchCfg);

        if (cast == null && !waitingOnGp && multiHook.Enabled && multiHook.CastCondition()) {
            Service.TaskManager.Enqueue(() => PlayerRes.CastActionDelayed(multiHook.Id, multiHook.ActionType, multiHook.GetName()));
            Service.TaskManager.Enqueue(() => CastLineMoochOrRelease(GetAutoCastCfg(), lastFishCatchCfg));
            return true;
        }

        if (cast != null) {
            if (multiHook.Enabled && multiHook.CastCondition()) {
                Service.TaskManager.Enqueue(() => PlayerRes.CastActionDelayed(multiHook.Id, multiHook.ActionType, multiHook.GetName()));
                Service.TaskManager.Enqueue(() => PlayerRes.CastActionDelayed(cast.Id, cast.ActionType, cast.GetName()));
                return true;
            }

            PlayerRes.CastActionDelayed(cast.Id, cast.ActionType, cast.GetName());
            return true;
        }

        return false;
    }

    private void CheckFishCaughtSwap(FishConfig? lastCatchCfg) {
        if (lastCatchCfg == null || !lastCatchCfg.Enabled || ShouldIgnoreFishSettings(lastCatchCfg))
            return;

        var guid = lastCatchCfg.UniqueId;

        var (swapPresetEnabled, _) = lastCatchCfg.SwapPresetLimit.Value;

        if (swapPresetEnabled && lastCatchCfg.SwapPresetLimit.BackingSet.HasGroups()
            && Presets.CurrentPreset.PresetName == lastCatchCfg.PresetToSwap)
            FishingHelper.RemovePresetSwap(guid);

        if (swapPresetEnabled && lastCatchCfg.SwapPresetLimit.BackingSet.Passes() && !FishingHelper.SwappedPreset(guid) && !Ws.FishingStep.HasFlag(FishingSteps.PresetSwapped)) {
            if (lastCatchCfg.PresetToSwap == Presets.CurrentPreset.PresetName) {
                FindPresetByName(lastCatchCfg.PresetToSwap)?.TryResetCounter();
            }
            else if (lastCatchCfg.PresetToSwap != Presets.CurrentPreset.PresetName) {
                var preset = FindPresetByName(lastCatchCfg.PresetToSwap);

                FishingHelper.AddPresetSwap(guid);
                Ws.Execute(new WorldState.OpSetFishingStep(FishingSteps.PresetSwapped, Or: true));

                if (preset == null)
                    Service.PrintChat(@$"Preset {lastCatchCfg.PresetToSwap} not found.");
                else {
                    Service.Save();
                    Presets.SelectedPreset = preset;
                    Service.PrintChat(@$"[Fish Caught] Swapping current preset to {lastCatchCfg.PresetToSwap}");
                    Service.Save();
                }
            }
        }

        var (swapBaitEnabled, _) = lastCatchCfg.SwapBaitLimit.Value;

        if (swapBaitEnabled && lastCatchCfg.SwapBaitLimit.BackingSet.Passes() && !FishingHelper.SwappedBait(guid) && !Ws.FishingStep.HasFlag(FishingSteps.BaitSwapped)) {
            if (lastCatchCfg.BaitToSwap.Id != Ws.Fishing.BaitInfo.BaitId) {
                var result = ChangeBait(lastCatchCfg.BaitToSwap);

                FishingHelper.AddBaitSwap(guid);
                Ws.Execute(new WorldState.OpSetFishingStep(FishingSteps.BaitSwapped, Or: true));
                if (result == ChangeBaitReturn.Success) {
                    Service.PrintChat(@$"[Fish Caught] Swapping bait to {lastCatchCfg.BaitToSwap.Name}");
                    Service.Save();
                }
                if (lastCatchCfg.SwapBaitResetCount) FishingHelper.ToBeRemoved.Add(guid);
            }
        }
    }
}
