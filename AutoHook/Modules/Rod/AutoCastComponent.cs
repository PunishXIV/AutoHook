using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using Lumina.Excel.Sheets;

namespace AutoHook.Modules.Rod;

public sealed class AutoCastComponent(RodFishingModule module) : RodComponent(module) {
    public AutoCastsConfig GetAutoCastCfg()
        => RodFishingModule.Presets.SelectedPreset?.AutoCastsCfg ?? RodFishingModule.Presets.DefaultPreset.AutoCastsCfg;

    public void CheckWhileFishingActions() {
        if (Rod.FishingTimer.IsRunning) {
            var elapsed = Math.Truncate(Rod.FishingTimer.ElapsedMilliseconds / 1000.0 * 100) / 100;
            var chum = Ws.Player.HasStatus(IDs.Status.Chum);
            if (Math.Abs(Ws.Fishing.BiteInfo.BiteTimeSeconds - elapsed) >= 0.01 || Ws.Fishing.ChumActive != chum)
                Ws.Execute(new RodState.OpBiteContext(elapsed, chum));
        }

        if (!EzThrottler.Throttle("CheckWhileFishingActions", 200) || Rod.SpectralRestPending)
            return;

        var hookCfg = Rod.GetHookCfg();

        if (!hookCfg.Enabled)
            return;

        hookCfg.GetHookset().CastLures.TryCasting(Ws, Ws.Fishing.LureSuccess);
    }

    public void CastCollectAfterLine() {
        var cfg = GetAutoCastCfg();

        if (Ws.Player.HasStatus(IDs.Status.CollectorsGlove) && cfg.RecastAnimationCancel && cfg.TurnCollectOff && !cfg.CastCollect.Enabled)
            Rod.ExecuteImmediate(new ActionRequest(IDs.Actions.Collect, ActionType.Action, "Collect", UseRaw: true));
        else if (Ws.Player.HasStatus(IDs.Status.CollectorsGlove) && cfg.TurnCollectOffWithoutAnimCancel && !cfg.CastCollect.Enabled)
            Rod.ExecuteImmediate(new ActionRequest(IDs.Actions.Collect, ActionType.Action, "Collect", UseRaw: true));
        else {
            var hints = new ActionHints();
            ProposeAction(hints, HintPriority.CollectBeforeLine, cfg.CastCollect, Ws, cfg);
            Rod.Resolver.Resolve(Ws, hints);
        }
    }

    private static bool IsBlockRequested(WorldState ws)
        => (ws.Fishing.FishingStep & (FishingSteps.StopCasting | FishingSteps.QuitRequested)) != 0;

    private static bool IsStartPending(WorldState ws) {
        var state = ws.Fishing.FishingState;
        return state == FishingState.PoleReady || state == FishingState.None && ws.Fishing.FishingStep.HasFlag(FishingSteps.StartPending);
    }

    public void UseAutoCasts() {
        if (IsBlockRequested(Ws) || !IsStartPending(Ws))
            return;

        if (!Ws.IsCastAvailable() || Rod.IsBusy)
            return;

        var hints = new ActionHints();
        ContributePoleReadyHints(hints);
        Rod.Resolver.Resolve(Ws, hints);
    }

    public void ContributePoleReadyHints(ActionHints hints) {
        if (IsBlockRequested(Ws) || !IsStartPending(Ws))
            return;

        if (!Ws.IsCastAvailable() || Rod.IsBusy)
            return;

        var lastFishCatchCfg = Rod.GetEffectiveCatchConfig();
        var acCfg = GetAutoCastCfg();
        var ignoreMooch = lastFishCatchCfg?.NeverMooch ?? false;
        var autoCast = acCfg.GetNextAutoCast(Ws, ignoreMooch);

        System.Action? continueStart = null;
        if (Ws.Fishing.FishingStep.HasFlag(FishingSteps.StartPending)) {
            continueStart = () => ContinueStartFishing(autoCast);
        }

        if (ProposeAction(hints, autoCast != null ? HintPriority.ForAutoCast(autoCast) : HintPriority.AutoCast, autoCast, Ws, acCfg, false, ignoreMooch, afterExecute: continueStart))
            return;

        if (WorldStateUpdater.Get().HasPendingGp) {
            hints.HoldForPendingGp = true;
            return;
        }

        ContributeLineMoochHints(hints, acCfg, lastFishCatchCfg);
    }

    public bool ContributeGpRestoreHints(ActionHints hints, bool ignoreMooch) {
        var cfg = GetAutoCastCfg();
        return ProposeAction(hints, HintPriority.GpRestore, cfg.GetNextGpRestoringCast(Ws, ignoreMooch), Ws, cfg, ignoreCurrentMooch: ignoreMooch);
    }

    private void ContinueStartFishing(BaseActionCast? usedAction) {
        if (!Ws.Fishing.FishingStep.HasFlag(FishingSteps.StartPending))
            return;

        var delay = usedAction != null ? Rod.GetPostCastDelayMs() : 0;
        ActionExecutor.Get().EnqueueCallback(() => {
            if (!Ws.Fishing.FishingStep.HasFlag(FishingSteps.StartPending))
                return;

            UseAutoCasts();
        }, delay);
    }

    public void CastLineMoochOrRelease(AutoCastsConfig acCfg, FishConfig? lastFishCatchCfg) {
        var hints = new ActionHints();
        ContributeLineMoochHints(hints, acCfg, lastFishCatchCfg);
        Rod.Resolver.Resolve(Ws, hints);
    }

    public void ContributeLineMoochHints(ActionHints hints, AutoCastsConfig acCfg, FishConfig? lastFishCatchCfg) {
        if (TryProposeCollectBeforeLine(hints, acCfg))
            return;

        var blockMooch = lastFishCatchCfg is { Enabled: true, NeverMooch: true };

        if (TryProposeMoochBeforeSwimbaitForSameFish(hints, acCfg, lastFishCatchCfg, blockMooch))
            return;

        if (TryUseSwimbait(acCfg, lastFishCatchCfg, blockMooch)) {
            ProposeAction(hints, HintPriority.CastLine, acCfg.CastLine, Ws, acCfg, noDelay: true);
            return;
        }

        if (!blockMooch) {
            if (lastFishCatchCfg is { Enabled: true } && lastFishCatchCfg.Mooch.IsAvailableToCast(Ws)) {
                hints.AddCast(HintPriority.Mooch, new ActionRequest(lastFishCatchCfg.Mooch.Id, lastFishCatchCfg.Mooch.ActionType, UIStrings.Mooch, ActionDelayMode.NoDelay), HintSource.AutoCast, context: DecisionContext.AutoCast);
                return;
            }

            if (ProposeAction(hints, HintPriority.Mooch, acCfg.CastMooch, Ws, acCfg, noDelay: true))
                return;
        }

        ProposeAction(hints, HintPriority.CastLine, acCfg.CastLine, Ws, acCfg, noDelay: true);
    }

    private bool TryProposeCollectBeforeLine(ActionHints hints, AutoCastsConfig acCfg) {
        if (!acCfg.EnableAll || !acCfg.CastCollect.Enabled || Ws.Player.HasStatus(IDs.Status.CollectorsGlove))
            return false;

        if (!acCfg.CastCollect.IsAvailableToCast(Ws))
            return false;

        return ProposeAction(hints, HintPriority.CollectBeforeLine, acCfg.CastCollect, Ws, acCfg);
    }

    private bool TryProposeMoochBeforeSwimbaitForSameFish(ActionHints hints, AutoCastsConfig acCfg, FishConfig? lastFishCatchCfg, bool blockMooch) {
        if (blockMooch)
            return false;

        if (Ws.Fishing.LastCatch is not { FishId: > 0 } lastCatch)
            return false;

        var fishId = lastCatch.FishId;
        if (!Ws.Fishing.SwimbaitIds.Any(id => id == fishId))
            return false;

        if (lastFishCatchCfg is { Enabled: true } && lastFishCatchCfg.Mooch.IsAvailableToCast(Ws)) {
            hints.AddCast(HintPriority.Mooch, new ActionRequest(lastFishCatchCfg.Mooch.Id, lastFishCatchCfg.Mooch.ActionType, UIStrings.Mooch, ActionDelayMode.NoDelay), HintSource.AutoCast, context: DecisionContext.AutoCast);
            return true;
        }

        return ProposeAction(hints, HintPriority.Mooch, acCfg.CastMooch, Ws, acCfg, noDelay: true);
    }

    private bool ProposeAction(ActionHints hints, int priority, BaseActionCast? action, WorldState ws, AutoCastsConfig cfg, bool noDelay = false, bool ignoreCurrentMooch = false, HintSource source = HintSource.AutoCast, System.Action? afterExecute = null) {
        if (action == null || !cfg.EnableAll)
            return false;

        if (action.RequiresTimeWindow() && !cfg.TimeWindow.BackingSet.PassesOrUnconfigured(ws)) {
            cfg.LogAutoCastDecision(ws, action, "Time window blocked");
            return false;
        }

        if (action.DescribeUnavailable(ws, ignoreCurrentMooch) is { } unavailable) {
            cfg.LogAutoCastDecision(ws, action, unavailable);
            return false;
        }

        if (action.Id == IDs.Actions.Chum && cfg.ChumAnimationCancel) {
            var chum = new ActionRequest(IDs.Actions.Chum, ActionType.Action, action.GetName(), ActionDelayMode.NoDelay, DelayBeforeMs: 40, UseRaw: true);
            var salvage = new ActionRequest(IDs.Actions.Salvage, ActionType.Action, "Salvage", ActionDelayMode.NoDelay, DelayBeforeMs: 465, UseRaw: true);
            hints.AddCast(priority, chum, source, chain: [salvage], afterExecute: afterExecute);
            return true;
        }

        var request = new ActionRequest(action.Id, action.ActionType, action.GetName(), noDelay ? ActionDelayMode.NoDelay : ActionDelayMode.Delayed);
        hints.AddCast(priority, request, source, afterExecute: afterExecute);
        return true;
    }

    private bool TryUseSwimbait(AutoCastsConfig acCfg, FishConfig? lastFishCatchCfg, bool blockMooch) {
        if (Ws.GetSwimbaitCount() is 0)
            return false;

        var presets = RodFishingModule.Presets;
        var presetName = presets.SelectedPreset?.PresetName ?? "(none)";

        foreach (var (fishId, slotIndex) in Ws.Fishing.SwimbaitIds.ToArray().WithIndex()) {
            if (fishId == 0)
                continue;

            HookConfig? swimbaitMoochConfig = null;
            if (presets.SelectedPreset != null)
                swimbaitMoochConfig = presets.SelectedPreset.GetCfgById(fishId, true);

            SwimbaitConfig? activeSwimbaitCfg = null;
            var configSource = "none";

            if (swimbaitMoochConfig != null && swimbaitMoochConfig.Enabled) {
                var useIntuitionTab = swimbaitMoochConfig.UsesIntuitionHookConfig();
                activeSwimbaitCfg = swimbaitMoochConfig.GetSwimbaitConfig();
                configSource = $"preset ({swimbaitMoochConfig.BaitFish.Name}, {(useIntuitionTab ? "intuition" : "normal")} tab)";
            }

            if (activeSwimbaitCfg == null || !activeSwimbaitCfg.UseSwimbait) {
                var globalAllMooches = presets.DefaultPreset.ListOfMooch.FirstOrDefault(hook => hook.BaitFish.Id == FishBaitCatalog.AllMoochesId);
                if (globalAllMooches != null && globalAllMooches.Enabled) {
                    var globalCfg = globalAllMooches.GetSwimbaitConfig();
                    if (globalCfg.UseSwimbait) {
                        swimbaitMoochConfig = globalAllMooches;
                        activeSwimbaitCfg = globalCfg;
                        configSource = $"global All Mooches ({(globalAllMooches.UsesIntuitionHookConfig() ? "intuition" : "normal")} tab)";
                    }
                }

                if (activeSwimbaitCfg == null || !activeSwimbaitCfg.UseSwimbait)
                    continue;
            }

            var fishName = fishId == 0 ? "unknown fish" : Item.GetRow(fishId).Name.ToString();
            Ws.SwimbaitEvaluationFishId = fishId;
            try {
                if (activeSwimbaitCfg.ConditionSet.Fails(Ws)) {
                    Ws.Decide(DecisionContext.Swimbait, false, fishName,
                        JoinSwimbaitDetail(configSource, activeSwimbaitCfg.ConditionSet?.Describe()), presetName);
                    continue;
                }
            }
            finally {
                Ws.SwimbaitEvaluationFishId = 0;
            }

            if (BaitComponent.ChangeSwimbait((uint)slotIndex) == ChangeBaitReturn.Success) {
                Ws.Decide(DecisionContext.Swimbait, true, $"Slot {slotIndex}",
                    JoinSwimbaitDetail($"{fishName} · {configSource}", activeSwimbaitCfg.ConditionSet?.Describe()), presetName);
                WorldStateUpdater.Get()?.RefreshFishingStateSnapshot();
                Rod.BiteHook.UpdateStatusAndTimer();
                PluginUi.Status = $"Using swimbait: {Item.GetRow(fishId).Name}";
                return true;
            }

            Ws.Decide(DecisionContext.Swimbait, false, $"Slot {slotIndex}",
                JoinSwimbaitDetail($"{fishName} · ChangeSwimbait failed · {configSource}", null), presetName);
        }

        return false;
    }

    private static string? JoinSwimbaitDetail(string? head, string? conditions) {
        if (string.IsNullOrEmpty(head))
            return string.IsNullOrEmpty(conditions) ? null : conditions;
        return string.IsNullOrEmpty(conditions) ? head : $"{head}\n{conditions}";
    }
}
