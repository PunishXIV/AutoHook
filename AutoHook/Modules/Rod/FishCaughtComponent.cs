namespace AutoHook.Modules.Rod;

public sealed class FishCaughtComponent(RodFishingModule module) : RodComponent(module) {
    public FishConfig? GetLastCatchConfig() {
        if (Ws.Fishing.LastCatch is not { } lc || lc.FishId <= 0)
            return null;

        return RodFishingModule.Presets.SelectedPreset?.GetFishById(lc.FishId) ?? RodFishingModule.Presets.DefaultPreset.GetFishById(lc.FishId);
    }

    public FishConfig? GetEffectiveCatchConfig() {
        var cfg = GetLastCatchConfig();
        return ShouldIgnoreFishSettings(cfg) ? null : cfg;
    }

    private bool ShouldIgnoreFishSettings(FishConfig? cfg) {
        if (cfg is not { Enabled: true })
            return false;

        return cfg.IgnoreConditionSet is { } ignoreSet && ignoreSet.HasAnyCondition() && ignoreSet.PassesOrUnconfigured(Ws);
    }

    public static bool HasGpBlockedFishCaughtAction(FishConfig cfg)
        => SelectFishCaughtCast(WorldState.Get(), cfg, gpBlockedOnly: true) != null;

    private static BaseActionCast? SelectFishCaughtCast(WorldState ws, FishConfig cfg, bool gpBlockedOnly = false) {
        BaseActionCast? cast = null;

        bool Matches(BaseActionCast action)
            => gpBlockedOnly ? action.IsGpBlocked(ws) : action.IsAvailableToCast(ws);

        if (Matches(cfg.IdenticalCast))
            cast = cfg.IdenticalCast;

        if (Matches(cfg.SurfaceSlap))
            cast = cfg.SurfaceSlap;

        if (Matches(cfg.SparefulHand))
            cast = cfg.SparefulHand;

        return cast;
    }

    public void ContributeCastHints(ActionHints hints, FishConfig? lastFishCatchCfg) {
        if (lastFishCatchCfg == null || !lastFishCatchCfg.Enabled || Ws.Fishing.FishingStep.HasFlag(FishingSteps.PresetSwapped))
            return;

        if (Ws.Fishing.LastCatch is { } lc && lc.FishId > 0)
            lastFishCatchCfg.SparefulHand.FishIdToCheck = lc.FishId;

        var cast = SelectFishCaughtCast(Ws, lastFishCatchCfg);
        var multiHook = lastFishCatchCfg.Multihook;
        var waitingOnGp = cast == null && HasGpBlockedFishCaughtAction(lastFishCatchCfg);

        if (cast == null && !waitingOnGp && multiHook.Enabled && multiHook.CastCondition(Ws)) {
            var multiReq = new ActionRequest(multiHook.Id, multiHook.ActionType, multiHook.GetName());
            hints.AddCast(HintPriority.FishCaughtMultiHook, multiReq, HintSource.FishCaught, context: DecisionContext.AutoCast, afterExecute: () => Rod.AutoCast.CastLineMoochOrRelease(Rod.GetAutoCastCfg(), lastFishCatchCfg));
            return;
        }

        if (cast == null)
            return;

        var castReq = new ActionRequest(cast.Id, cast.ActionType, cast.GetName());
        if (multiHook.Enabled && multiHook.CastCondition(Ws)) {
            var multiReq = new ActionRequest(multiHook.Id, multiHook.ActionType, multiHook.GetName());
            hints.AddCast(HintPriority.FishCaughtAction, multiReq, HintSource.FishCaught, detail: cast.GetName(), context: DecisionContext.AutoCast, chain: [castReq]);
            return;
        }

        hints.AddCast(
            HintPriority.FishCaughtAction,
            castReq,
            HintSource.FishCaught,
            context: DecisionContext.AutoCast);
    }

    public void CheckFishCaughtSwap(FishConfig? lastCatchCfg) {
        if (lastCatchCfg == null || !lastCatchCfg.Enabled || ShouldIgnoreFishSettings(lastCatchCfg))
            return;

        var guid = lastCatchCfg.UniqueId;
        var presets = RodFishingModule.Presets;

        var (swapPresetEnabled, _) = lastCatchCfg.SwapPresetLimit.Value;

        if (swapPresetEnabled && lastCatchCfg.SwapPresetLimit.BackingSet.HasGroups()
            && presets.CurrentPreset.PresetName == lastCatchCfg.PresetToSwap)
            FishingCounters.RemovePresetSwap(guid);

        if (swapPresetEnabled && lastCatchCfg.SwapPresetLimit.BackingSet.Passes(Ws) && !FishingCounters.SwappedPreset(guid) && !Ws.Fishing.FishingStep.HasFlag(FishingSteps.PresetSwapped)) {
            if (lastCatchCfg.PresetToSwap == presets.CurrentPreset.PresetName) {
                PresetSwapHelpers.FindPresetByName(lastCatchCfg.PresetToSwap)?.TryResetCounter();
            }
            else if (lastCatchCfg.PresetToSwap != presets.CurrentPreset.PresetName) {
                FishingCounters.AddPresetSwap(guid);
                PresetSwapHelpers.TrySwapPreset(
                    Ws,
                    lastCatchCfg.PresetToSwap,
                    FishingPresets.ReasonFishCaught,
                    @$"[Fish Caught] Swapping current preset to {lastCatchCfg.PresetToSwap}",
                    @$"[Fish Caught] Preset {lastCatchCfg.PresetToSwap} not found.");
            }
        }

        var (swapBaitEnabled, _) = lastCatchCfg.SwapBaitLimit.Value;

        if (swapBaitEnabled && lastCatchCfg.SwapBaitLimit.BackingSet.Passes(Ws) && !FishingCounters.SwappedBait(guid) && !Ws.Fishing.FishingStep.HasFlag(FishingSteps.BaitSwapped)) {
            if (lastCatchCfg.BaitToSwap.Id != Ws.Fishing.BaitInfo.BaitId) {
                FishingCounters.AddBaitSwap(guid);
                var result = PresetSwapHelpers.TrySwapBait(Ws, lastCatchCfg.BaitToSwap);
                if (result == ChangeBaitReturn.Success) {
                    IChatGui.Get().PrintStatus(@$"[Fish Caught] Swapping bait to {lastCatchCfg.BaitToSwap.Name}");
                    Configuration.Save();
                }
                if (lastCatchCfg.SwapBaitResetCount) FishingCounters.QueueRemove(guid);
            }
        }
    }
}
