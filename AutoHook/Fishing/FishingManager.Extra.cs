using AutoHook.Conditions;
using AutoHook.Replay;
using AutoHook.Tasks;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using StatusSheet = Lumina.Excel.Sheets.Status;

namespace AutoHook.Fishing;

public partial class FishingManager {
    private const int _presetSwapCap = 8;

    public ExtraConfig GetExtraCfg()
        => Presets.SelectedPreset?.ExtraCfg.Enabled ?? false ? Presets.SelectedPreset.ExtraCfg : Presets.DefaultPreset.ExtraCfg;

    /// <summary>
    /// When <see cref="Configuration.AutoOceanFish"/> is on, select a preset whose Extra config
    /// matches the current zone/time and Settings goal cascade (Achievements → Legendary → Points → None).
    /// </summary>
    private void TryApplyOceanFishingPreset() {
        if (!Service.Configuration.AutoOceanFish)
            return;

        var ocean = Ws.OceanFishing;
        if (ocean == OceanFishingState.Empty || ocean.TimeOfDay == TimeOfDay.None)
            return;

        OceanGoalCatalog.PrefetchRouteAchievements(ocean.CurrentRoute);

        CustomPresetConfig? match = null;
        var matchedTier = OceanFishGoalKind.Points;
        uint matchedGoalId = 0;

        foreach (var tier in OceanGoalCatalog.GetCascade(Service.Configuration.AutoOceanFishGoal)) {
            match = FindOceanPresetForTier(ocean, tier, out matchedGoalId);
            if (match != null) {
                matchedTier = tier;
                break;
            }
        }

        if (match == null)
            return;

        if (match.IsGlobal) {
            if (Presets.SelectedPreset == null)
                return;
            Presets.SelectedPreset = null;
            Service.PrintDebug($"[AutoOceanFish] Preset set to global (tier={matchedTier}, goalId={matchedGoalId}, zone {ocean.CurrentZone}, spot {ocean.CurrentSpotId}, time {ocean.CurrentTimeId})");
            return;
        }

        if (Presets.SelectedPreset?.UniqueId == match.UniqueId)
            return;

        Presets.SelectedPreset = match;
        Service.PrintDebug($"[AutoOceanFish] Preset set to {match.PresetName} (tier={matchedTier}, goalId={matchedGoalId}, zone {ocean.CurrentZone}, spot {ocean.CurrentSpotId}, time {ocean.CurrentTimeId})");
    }

    private CustomPresetConfig? FindOceanPresetForTier(OceanFishingState ocean, OceanFishGoalKind tier, out uint matchedGoalId) {
        matchedGoalId = 0;

        if (tier == OceanFishGoalKind.Achievement) {
            foreach (var achId in OceanGoalCatalog.GetEligibleAchievementIds(ocean.CurrentRoute)) {
                foreach (var preset in EnumerateHookPresets()) {
                    if (!MatchesOceanBase(preset.ExtraCfg, ocean))
                        continue;
                    if (preset.ExtraCfg.AutoOceanFishGoal != OceanFishGoalKind.Achievement)
                        continue;
                    if (preset.ExtraCfg.AutoOceanFishGoalId != achId)
                        continue;
                    matchedGoalId = achId;
                    return preset;
                }
            }
            return null;
        }

        if (tier == OceanFishGoalKind.Legendary) {
            if (OceanGoalCatalog.GetEligibleLegendaryIds(ocean.CurrentRoute).Count == 0)
                return null;
            foreach (var preset in EnumerateHookPresets()) {
                if (!MatchesOceanBase(preset.ExtraCfg, ocean))
                    continue;
                if (preset.ExtraCfg.AutoOceanFishGoal != OceanFishGoalKind.Legendary)
                    continue;
                return preset;
            }
            return null;
        }

        foreach (var preset in EnumerateHookPresets()) {
            var extra = preset.ExtraCfg;
            if (!MatchesOceanBase(extra, ocean))
                continue;
            if (extra.AutoOceanFishGoal != tier)
                continue;
            matchedGoalId = extra.AutoOceanFishGoalId;
            return preset;
        }

        return null;
    }

    private static bool MatchesOceanBase(ExtraConfig extra, OceanFishingState ocean) {
        if (!extra.AutoOceanFishEnabled)
            return false;
        if (!extra.AutoOceanFishAllStops
            && !OceanStopUtil.MatchesStop(extra.AutoOceanFishSpotId, extra.AutoOceanFishTimeId, ocean))
            return false;
        if (extra.AutoOceanFishConditionSet is { } set && set.HasAnyCondition() && set.Fails())
            return false;
        return true;
    }

    private IEnumerable<CustomPresetConfig> EnumerateHookPresets() {
        yield return Presets.DefaultPreset;
        foreach (var preset in Presets.CustomPresets)
            yield return preset;
    }

    private CustomPresetConfig? FindPresetByName(string presetName) {
        if (string.IsNullOrEmpty(presetName) || presetName == @"-")
            return null;

        if (Presets.DefaultPreset.PresetName == presetName)
            return Presets.DefaultPreset;

        return Presets.CustomPresets.FirstOrDefault(p => p.PresetName == presetName);
    }

    private CustomPresetConfig GetExtraOwnerPreset()
        => Presets.SelectedPreset?.ExtraCfg.Enabled == true ? Presets.SelectedPreset : Presets.DefaultPreset;

    private bool ExtraSwapStillNeeded(ExtraTrigger trig) {
        if (trig.SwapPreset && !string.IsNullOrEmpty(trig.PresetToSwap) && trig.PresetToSwap != @"-" && Presets.SelectedPreset?.PresetName != trig.PresetToSwap)
            return true;
        return trig.SwapBait && trig.BaitToSwap.Id > 0 && Ws.Fishing.BaitInfo.BaitId != trig.BaitToSwap.Id;
    }

    private void QueueResolveCollectables() {
        var extraCfg = GetExtraCfg();
        foreach (var trig in extraCfg.Triggers) {
            if (trig is not { Enabled: true, ResolveCollectablesWindow: true, ConditionSet: not null })
                continue;

            if (!trig.ConditionSet.Evaluate(Ws, ConditionRegistry.Registry))
                continue;

            Service.AutoCollectables.RequestResolve(trig.ResolveCollectablesForceNo);
            return;
        }
    }

    private void CheckExtraActions() {
        var anyPresetSwapped = false;
        var involvedPresetIds = new HashSet<Guid>();
        var iterations = 0;

        while (true) {
            if (++iterations > _presetSwapCap) {
                SwapLoopBailout(involvedPresetIds);
                break;
            }

            Ws.Execute(new WorldState.OpClearFishingStepFlag(FishingSteps.PresetSwapped));
            Ws.Execute(new WorldState.OpClearFishingStepFlag(FishingSteps.BaitSwapped));

            involvedPresetIds.Add(GetExtraOwnerPreset().UniqueId);

            var presetBefore = Presets.SelectedPreset?.UniqueId;
            var extraCfg = GetExtraCfg();
            if (extraCfg.Triggers.Count == 0)
                break;

            RunExtraTriggers(extraCfg);

            if (Presets.SelectedPreset?.UniqueId == presetBefore)
                break;

            anyPresetSwapped = true;
            involvedPresetIds.Add(GetExtraOwnerPreset().UniqueId);
        }

        if (anyPresetSwapped)
            Ws.Execute(new WorldState.OpSetFishingStep(FishingSteps.PresetSwapped, Or: true));
    }

    private void SwapLoopBailout(HashSet<Guid> involvedPresetIds) {
        var involvedNames = EnumerateHookPresets().Where(p => involvedPresetIds.Contains(p.UniqueId)).Select(p => p.PresetName).ToList();

        Service.Configuration.PluginEnabled = false;
        Service.Save();

        var presetList = involvedNames.Count > 0 ? string.Join(", ", involvedNames) : UIStrings.UnknownPresets;
        Service.PrintChat(string.Format(UIStrings.Extra_PresetSwapLoop_Bailout, presetList));
        Service.PrintDebug($"[Extra.{nameof(SwapLoopBailout)}] {_presetSwapCap} iterations; involved: {presetList}");
    }

    private void RunExtraTriggers(ExtraConfig extraCfg) {
        for (var i = 0; i < extraCfg.Triggers.Count; i++) {
            if (extraCfg.Triggers[i] is not { Enabled: true, ConditionSet: not null } trig)
                continue;

            var current = trig.ConditionSet.Evaluate(Ws, ConditionRegistry.Registry);
            var last = i < extraCfg.LastTriggerStates.Count && extraCfg.LastTriggerStates[i];
            var fire = current && (!last || ExtraSwapStillNeeded(trig));

            if (i < extraCfg.LastTriggerStates.Count)
                extraCfg.LastTriggerStates[i] = current;
            else
                extraCfg.LastTriggerStates.Add(current);
            if (!fire)
                continue;

            ReplayDecisions.ExtraTrigger(i, trig, GetExtraOwnerPreset().PresetName);
            ExecuteExtraTriggerActions(extraCfg, trig);
        }
    }

    private void ExecuteExtraTriggerActions(ExtraConfig extraCfg, ExtraTrigger trig) {
        // Stop/quit fishing
        if (trig.StopAction == ExtraStopAction.StopOnly) {
            Ws.Execute(new WorldState.OpSetFishingStep(FishingSteps.None));
        }
        else if (trig.StopAction == ExtraStopAction.QuitFishing) {
            Ws.Execute(new WorldState.OpSetFishingStep(FishingSteps.Quitting));
        }

        if (trig.ResetFishCaughtCounter) {
            GetExtraOwnerPreset().ResetCounter();
            Service.PrintChat(@$"[Extra] Trigger: Reset fish caught counter");
        }

        // Swap preset
        if (trig.SwapPreset && !Ws.FishingStep.HasFlag(FishingSteps.PresetSwapped)) {
            if (Presets.CurrentPreset.PresetName == trig.PresetToSwap) {
                Ws.Execute(new WorldState.OpSetFishingStep(FishingSteps.PresetSwapped, Or: true));
                FindPresetByName(trig.PresetToSwap)?.TryResetCounter();
            }
            else {
                var preset = FindPresetByName(trig.PresetToSwap);

                Ws.Execute(new WorldState.OpSetFishingStep(FishingSteps.PresetSwapped, Or: true));

                if (preset != null) {
                    Service.Save();
                    Presets.SelectedPreset = preset;
                    preset.ExtraCfg.LastTriggerStates.Clear();
                    Service.PrintChat(@$"[Extra] Trigger: Swapping preset to {trig.PresetToSwap}");
                    Service.Save();
                }
                else if (!string.IsNullOrEmpty(trig.PresetToSwap) && trig.PresetToSwap != @"-") {
                    Service.PrintChat(@$"[Extra] Trigger: Preset {trig.PresetToSwap} not found.");
                }
            }
        }

        // Swap bait
        if (trig.SwapBait && !Ws.FishingStep.HasFlag(FishingSteps.BaitSwapped)) {
            var result = ChangeBait(trig.BaitToSwap);
            Ws.Execute(new WorldState.OpSetFishingStep(FishingSteps.BaitSwapped, Or: true));

            if (result is ChangeBaitReturn.Success or ChangeBaitReturn.AlreadyEquipped) {
                Service.PrintChat(@$"[Extra] Trigger: Swapping bait to {trig.BaitToSwap.Name}");
                Service.Save();
            }
        }

        if (trig.RemoveStatus && trig.StatusToRemove != 0 && Ws.HasStatus(trig.StatusToRemove) && EzThrottler.Throttle("ExtraRemoveStatus", 500)) {
            if (StatusManager.ExecuteStatusOff(trig.StatusToRemove)) {
                Service.PrintChat(@$"[Extra] Trigger: Removed {StatusSheet.GetRow(trig.StatusToRemove).Name}");
            }
        }

        if (trig.StartFishing && !ShouldSuppressAutoStartFishing() && Ws.Fishing.FishingState is FishingState.None or FishingState.PoleReady && Ws.IsCastAvailable() && EzThrottler.Throttle("ExtraStartFishingRule", 1000)) {
            StartFishing();
        }

        if (trig.ReduceFish && Svc.Automation.CurrentTask is not AetherialReduction) {
            Svc.Automation.Start(new AetherialReduction(this));
            Service.PrintChat(UIStrings.AetherialReduction_Started);
        }

        Service.NotificationMaster.TryNotify(trig.NotifyOnSuccess, "Rule condition success");
    }
}
