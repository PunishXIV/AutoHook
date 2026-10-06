using AutoHook.Extensions;
using AutoHook.Services;
using AutoHook.Tasks;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using StatusSheet = Lumina.Excel.Sheets.Status;

namespace AutoHook.Actions;

public sealed class ActionHintsResolver {
    public bool Resolve(WorldState ws, ActionHints hints) {
        if (hints.StopPlugin)
            return false;

        if (hints.ForbidCast)
            return false;

        if (hints.HoldForPendingGp)
            return false;

        if (hints.PreferRest) {
            var rest = new ActionRequest(IDs.Actions.Rest, ActionType.Action, UIStrings.Hook, ActionDelayMode.Delayed, DelayBeforeMs: hints.PreferRestDelayMs, UseRaw: hints.PreferRestDelayMs > 0);
            var detail = string.IsNullOrEmpty(hints.PreferRestDetail) ? "PreferRest" : hints.PreferRestDetail;
            ws.Decide(hints.PreferRestContext, false, "Rest", detail);
            return EnqueueWinner(rest, chain: null, afterExecute: null);
        }

        var winner = hints.Casts.OrderByDescending(c => c.Priority).FirstOrDefault();
        if (winner == null)
            return false;

        var detailBuilder = new StringBuilder();
        if (!string.IsNullOrEmpty(winner.Detail))
            detailBuilder.Append(winner.Detail);

        var rejected = hints.Casts.Where(c => !ReferenceEquals(c, winner)).OrderByDescending(c => c.Priority).ToList();
        if (rejected.Count > 0) {
            if (detailBuilder.Length > 0)
                detailBuilder.Append('\n');
            detailBuilder.Append("Rejected: ");
            detailBuilder.Append(string.Join(", ", rejected.Select(r => $"{r.Request.Name}/{r.Source}/{r.Priority}")));
        }

        var decideDetail = detailBuilder.Length > 0 ? detailBuilder.ToString() : null;
        ws.Decide(winner.Context, true, string.IsNullOrEmpty(winner.Request.Name) ? "Cast" : winner.Request.Name, decideDetail);

        return EnqueueWinner(winner.Request, winner.Chain, winner.AfterExecute);
    }

    /// <summary>
    /// Applies side-effect hints in order, clears them from <paramref name="hints"/>, and returns
    /// whether the selected preset UniqueId changed (so Extra can re-check the new preset).
    /// </summary>
    public bool ApplySideEffects(WorldState ws, ActionHints hints, RodFishingModule rod) {
        var presetBefore = RodFishingModule.Presets.SelectedPreset?.UniqueId;
        var effects = hints.SideEffects.ToList();
        hints.SideEffects.Clear();

        foreach (var effect in effects)
            ApplyOne(ws, rod, effect);

        return RodFishingModule.Presets.SelectedPreset?.UniqueId != presetBefore;
    }

    private static void ApplyOne(WorldState ws, RodFishingModule rod, SideEffectHint effect) {
        switch (effect) {
            case StopFishingHint stop:
                if (stop.Action == ExtraStopAction.StopOnly)
                    ws.Execute(new RodState.OpSetFishingStep(FishingSteps.StopCasting));
                else if (stop.Action == ExtraStopAction.QuitFishing)
                    ws.Execute(new RodState.OpSetFishingStep(FishingSteps.QuitRequested));
                break;

            case ResetCounterHint:
                GetExtraOwnerPreset().ResetCounter();
                IChatGui.Get().PrintStatus(@"[Extra] Trigger: Reset fish caught counter");
                break;

            case SwapPresetHint swap:
                PresetSwapHelpers.TrySwapPreset(
                    ws,
                    swap.PresetName,
                    FishingPresets.ReasonExtraTrigger,
                    @$"[Extra] Trigger: Swapping preset to {swap.PresetName}",
                    !string.IsNullOrEmpty(swap.PresetName) && swap.PresetName != @"-" ? @$"[Extra] Trigger: Preset {swap.PresetName} not found." : null,
                    clearExtraTriggerStates: true);
                break;

            case SwapBaitHint swapBait:
                var baitResult = PresetSwapHelpers.TrySwapBait(ws, swapBait.Bait, skipIfAlreadySwapped: true);
                if (baitResult is ChangeBaitReturn.Success or ChangeBaitReturn.AlreadyEquipped) {
                    IChatGui.Get().PrintStatus(@$"[Extra] Trigger: Swapping bait to {swapBait.Bait.Name}");
                    Configuration.Save();
                }
                break;

            case RemoveStatusHint remove:
                if (remove.StatusId != 0 && ws.Player.HasStatus(remove.StatusId) && EzThrottler.Throttle("ExtraRemoveStatus", 500)) {
                    if (StatusManager.ExecuteStatusOff(remove.StatusId))
                        IChatGui.Get().PrintStatus(@$"[Extra] Trigger: Removed {StatusSheet.GetRow(remove.StatusId).Name}");
                }
                break;

            case StartFishingHint:
                if (!rod.ShouldSuppressAutoStartFishing() && ws.Fishing.FishingState is FishingState.None or FishingState.PoleReady && ws.IsCastAvailable() && EzThrottler.Throttle("ExtraStartFishingRule", 1000)) {
                    rod.StartFishing();
                }
                break;

            case StartReductionHint:
                if (Svc.Automation.CurrentTask is not AetherialReduction) {
                    Svc.Automation.Start(new AetherialReduction());
                    IChatGui.Get().PrintStatus(UIStrings.AetherialReduction_Started);
                }
                break;

            case NotifyHint notify:
                NotificationMasterService.Get().Api.TryNotify(notify.Config, notify.FallbackText);
                break;
        }
    }

    private static CustomPresetConfig GetExtraOwnerPreset()
        => RodFishingModule.Presets.SelectedPreset?.ExtraCfg.Enabled == true ? RodFishingModule.Presets.SelectedPreset : RodFishingModule.Presets.DefaultPreset;

    public bool ExecuteImmediate(ActionRequest request)
        => ActionExecutor.Get().ExecuteRequest(request);

    private static bool EnqueueWinner(ActionRequest request, List<ActionRequest>? chain, Action? afterExecute) {
        var hasFollowUp = chain is { Count: > 0 } || afterExecute != null;
        bool enqueued;

        if (chain is { Count: > 0 })
            enqueued = ActionExecutor.Get().Enqueue(request, [.. chain]);
        else if (hasFollowUp)
            enqueued = ActionExecutor.Get().Enqueue(request, forceQueue: true);
        else
            enqueued = ActionExecutor.Get().Enqueue(request);

        if (afterExecute != null)
            ActionExecutor.Get().EnqueueCallback(afterExecute);

        return enqueued;
    }
}
