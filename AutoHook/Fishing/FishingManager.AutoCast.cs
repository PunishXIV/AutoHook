using AutoHook.Conditions;
using AutoHook.Replay;
using ECommons.Throttlers;
using Lumina.Excel.Sheets;

namespace AutoHook.Fishing;

public partial class FishingManager {
    public AutoCastsConfig GetAutoCastCfg()
        => Presets.SelectedPreset?.AutoCastsCfg ?? Presets.DefaultPreset.AutoCastsCfg;

    private void CheckWhileFishingActions() {
        if (!EzThrottler.Throttle("CheckWhileFishingActions", 200))
            return;

        if (_fishingTimer.IsRunning) {
            var elapsed = Math.Truncate(_fishingTimer.ElapsedMilliseconds / 1000.0 * 100) / 100;
            Ws.Execute(new FishingInfo.OpBiteContext(elapsed, Ws.ChumActive));
        }

        var hookCfg = GetHookCfg();

        if (!hookCfg.Enabled)
            return;

        hookCfg.GetHookset().CastLures.TryCasting(Ws.LureSuccess);
    }

    private bool TryCastCollectBeforeLine(AutoCastsConfig acCfg) {
        if (!acCfg.EnableAll || !acCfg.CastCollect.Enabled || Ws.HasStatus(IDs.Status.CollectorsGlove))
            return false;

        if (!acCfg.CastCollect.IsAvailableToCast())
            return false;

        return acCfg.TryCastAction(acCfg.CastCollect);
    }

    private void CastCollectAfterLine() {
        var cfg = GetAutoCastCfg();

        if (Ws.HasStatus(IDs.Status.CollectorsGlove) && cfg.RecastAnimationCancel && cfg.TurnCollectOff && !cfg.CastCollect.Enabled)
            PlayerRes.CastAction(IDs.Actions.Collect);
        else if (Ws.HasStatus(IDs.Status.CollectorsGlove) && cfg.TurnCollectOffWithoutAnimCancel && !cfg.CastCollect.Enabled)
            PlayerRes.CastAction(IDs.Actions.Collect);
        else
            cfg.TryCastAction(cfg.CastCollect);
    }

    private void UseAutoCasts() {
        if (Ws.FishingStep.HasFlag(FishingSteps.None) || Ws.FishingStep.HasFlag(FishingSteps.BeganFishing) || Ws.FishingStep.HasFlag(FishingSteps.Quitting))
            return;

        if (!Ws.IsCastAvailable() || Service.TaskManager.IsBusy)
            return;

        Service.TaskManager.Enqueue(() => {
            var lastFishCatchCfg = GetEffectiveCatchConfig();
            var acCfg = GetAutoCastCfg();
            var ignoreMooch = lastFishCatchCfg?.NeverMooch ?? false;
            var autoCast = acCfg.GetNextAutoCast(ignoreMooch);

            if (acCfg.TryCastAction(autoCast, false, ignoreMooch)) {
                ContinueStartFishing(autoCast);
                return;
            }

            CastLineMoochOrRelease(acCfg, lastFishCatchCfg);
        }, "AutoCasting");
    }

    private void ContinueStartFishing(BaseActionCast? usedAction) {
        if (!Ws.FishingStep.HasFlag(FishingSteps.StartedCasting) || Ws.FishingStep.HasFlag(FishingSteps.BeganFishing))
            return;

        var delay = usedAction != null ? PlayerRes.GetPostCastDelayMs() : 0;
        Service.TaskManager.EnqueueDelay(delay);
        Service.TaskManager.Enqueue(() => {
            if (!Ws.FishingStep.HasFlag(FishingSteps.StartedCasting) || Ws.FishingStep.HasFlag(FishingSteps.BeganFishing))
                return;

            UseAutoCasts();
        }, "ContinueStartFishing");
    }

    private void CastLineMoochOrRelease(AutoCastsConfig acCfg, FishConfig? lastFishCatchCfg) {
        if (TryCastCollectBeforeLine(acCfg))
            return;

        var blockMooch = lastFishCatchCfg is { Enabled: true, NeverMooch: true };

        if (TryMoochBeforeSwimbaitForSameFish(acCfg, lastFishCatchCfg, blockMooch))
            return;

        if (TryUseSwimbait(acCfg, lastFishCatchCfg, blockMooch))
            if (acCfg.TryCastAction(acCfg.CastLine, true))
                return;

        if (!blockMooch) {
            if (lastFishCatchCfg is { Enabled: true } && lastFishCatchCfg.Mooch.IsAvailableToCast()) {
                PlayerRes.CastActionNoDelay(lastFishCatchCfg.Mooch.Id, lastFishCatchCfg.Mooch.ActionType,
                    UIStrings.Mooch);
                return;
            }

            if (acCfg.TryCastAction(acCfg.CastMooch, true))
                return;
        }

        if (acCfg.TryCastAction(acCfg.CastLine, true))
            return;
    }

    /// <summary>
    /// Mooching the same fish again does not consume swimbait; prefer mooch when the last catch is also in a swimbait slot.
    /// </summary>
    private bool TryMoochBeforeSwimbaitForSameFish(AutoCastsConfig acCfg, FishConfig? lastFishCatchCfg, bool blockMooch) {
        if (blockMooch)
            return false;

        if (Ws.Fishing.LastCatch is not { FishId: > 0 } lastCatch)
            return false;

        var fishId = lastCatch.FishId;
        if (!Ws.SwimbaitIds.Any(id => id == fishId))
            return false;

        if (lastFishCatchCfg is { Enabled: true } && lastFishCatchCfg.Mooch.IsAvailableToCast()) {
            PlayerRes.CastActionNoDelay(lastFishCatchCfg.Mooch.Id, lastFishCatchCfg.Mooch.ActionType, UIStrings.Mooch);
            return true;
        }

        return acCfg.TryCastAction(acCfg.CastMooch, true);
    }

    private bool TryUseSwimbait(AutoCastsConfig acCfg, FishConfig? lastFishCatchCfg, bool blockMooch) {
        if (Ws.GetSwimbaitCount() is 0)
            return false;

        var intuitionActive = Ws.Fishing.Intuition.Status == IntuitionStatus.Active;
        var presetName = Presets.SelectedPreset?.PresetName ?? "(none)";
        Service.PrintDebug($"[Swimbait] Evaluating slots, preset={presetName}, intuitionActive={intuitionActive}, storedCount={Ws.GetSwimbaitCount()}");

        foreach (var (fishId, slotIndex) in Ws.SwimbaitIds.ToArray().WithIndex()) {
            if (fishId == 0)
                continue;

            HookConfig? swimbaitMoochConfig = null;
            if (Presets.SelectedPreset != null)
                swimbaitMoochConfig = Presets.SelectedPreset.GetCfgById(fishId, true);

            SwimbaitConfig? activeSwimbaitCfg = null;
            var configSource = "none";

            if (swimbaitMoochConfig != null && swimbaitMoochConfig.Enabled) {
                var useIntuitionTab = swimbaitMoochConfig.UsesIntuitionHookConfig();
                activeSwimbaitCfg = swimbaitMoochConfig.GetSwimbaitConfig();
                configSource = $"preset ({swimbaitMoochConfig.BaitFish.Name}, {(useIntuitionTab ? "intuition" : "normal")} tab)";
                Service.PrintDebug($"[Swimbait] Fish {fishId}: preset entry found, enabled=true, useIntuitionTab={useIntuitionTab}, " +
                    $"normalUseSwimbait={swimbaitMoochConfig.SwimbaitNormal.UseSwimbait}, intuitionUseSwimbait={swimbaitMoochConfig.SwimbaitIntuition.UseSwimbait}, " +
                    $"activeUseSwimbait={activeSwimbaitCfg.UseSwimbait}");
            }
            else {
                Service.PrintDebug($"[Swimbait] Fish {fishId}: no enabled preset entry (found={swimbaitMoochConfig != null}, enabled={swimbaitMoochConfig?.Enabled ?? false})");
            }

            if (activeSwimbaitCfg == null || !activeSwimbaitCfg.UseSwimbait) {
                var globalAllMooches = Presets.DefaultPreset.ListOfMooch.FirstOrDefault(hook => hook.BaitFish.Id == GameRes.AllMoochesId);
                if (globalAllMooches != null && globalAllMooches.Enabled) {
                    var globalCfg = globalAllMooches.GetSwimbaitConfig();
                    if (globalCfg.UseSwimbait) {
                        swimbaitMoochConfig = globalAllMooches;
                        activeSwimbaitCfg = globalCfg;
                        configSource = $"global All Mooches ({(globalAllMooches.UsesIntuitionHookConfig() ? "intuition" : "normal")} tab)";
                        Service.PrintDebug($"[Swimbait] Fish {fishId}: using global fallback, activeUseSwimbait=true");
                    }
                }

                if (activeSwimbaitCfg == null || !activeSwimbaitCfg.UseSwimbait) {
                    Service.PrintDebug($"[Swimbait] Fish {fishId}: no usable config (source={configSource}), trying next slot");
                    continue;
                }
            }

            Ws.SwimbaitEvaluationFishId = fishId;
            try {
                if (activeSwimbaitCfg.ConditionSet.Fails()) {
                    ReplayDecisions.SwimbaitSlotFailed(fishId, activeSwimbaitCfg.ConditionSet, presetName);
                    Service.PrintDebug($"[Swimbait] Fish {fishId}: conditions failed (source={configSource}), trying next slot");
                    continue;
                }
            }
            finally {
                Ws.SwimbaitEvaluationFishId = 0;
            }

            if (ChangeSwimbait((uint)slotIndex) == ChangeBaitReturn.Success) {
                ReplayDecisions.SwimbaitSlotSelected(slotIndex, fishId, activeSwimbaitCfg.ConditionSet, presetName);
                Service.WorldStateUpdater?.RefreshFishingStateSnapshot();
                UpdateStatusAndTimer();
                Service.PrintDebug($"[Swimbait] Using slot {slotIndex} (fish ID: {fishId}, source={configSource})");
                Service.Status = $"Using swimbait: {Item.GetRow(fishId).Name}";
                return true;
            }

            Service.PrintDebug($"[Swimbait] Fish {fishId}: ChangeSwimbait({slotIndex}) failed, trying next slot");
        }

        return false;
    }
}
