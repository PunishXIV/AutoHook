using AutoHook.Extensions;
using AutoHook.Services;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using StatusSheet = Lumina.Excel.Sheets.Status;

namespace AutoHook.Modules.Rod;

public sealed class BiteHookComponent(RodFishingModule module) : RodComponent(module) {
    public void OnSpectralCurrentChanged(WorldState.OpSpectralCurrentChanged op) {
        if (op.Change is SpectralCurrentChange.Lost) {
            Rod.SpectralRestPending = false;
            return;
        }
        if (op.Change is not SpectralCurrentChange.Gained || !Configuration.C.PluginEnabled || !Configuration.C.SpectralRest)
            return;
        Rod.SpectralRestPending = true;
        TrySpectralRest();
    }

    public void TrySpectralRest() {
        if (!Rod.SpectralRestPending) return;

        var midCast = Ws.Fishing.FishingState is FishingState.LineInWater or FishingState.AmbitiousLure or FishingState.ModestLure;
        var canceling = (Ws.Fishing.FishingStep & (FishingSteps.CancelPending | FishingSteps.TimeOut)) != 0;
        if (!Configuration.C.PluginEnabled || !Configuration.C.SpectralRest || !midCast) {
            Rod.SpectralRestPending = false;
            return;
        }
        if (canceling || Ws.Player.BlockCasting || !EzThrottler.Throttle("SpectralRestMidCast", 200)) return;

        var hints = new ActionHints();
        hints.AddCast(HintPriority.SpectralRest, new ActionRequest(IDs.Actions.Rest, ActionType.Action, UIStrings.Hook), HintSource.Spectral, detail: "Spectral rest", context: DecisionContext.Hook);
        if (!Rod.Resolver.Resolve(Ws, hints))
            return;

        PluginUi.Status = UIStrings.SpectralRestOnGain;
        Ws.Execute(new RodState.OpSetFishingStep(FishingSteps.CancelPending));
    }

    public unsafe void UpdateStatusAndTimer(bool forceMooching = false) {
        if (Configuration.C.ResetAfkTimer)
            InputTimerModule.Instance()->ResetAfkTimer();

        var selected = Rod.GetHookCfg(forceMooching);
        var hookset = selected.GetHookset();

        if (Configuration.C.ShowStatus) {
            var buffStatus = "";

            if (hookset.RequiredStatus != 0) {
                buffStatus = StatusSheet.GetRow(hookset.RequiredStatus).Name.ToString();
                buffStatus = @$"({buffStatus})";
            }

            var hookCfgName = Rod.GetPresetName();

            var message = !selected.Enabled ? @$"No hooking option found. Make sure to add/enable your bait/mooch settings" : @$"Hooking with: {hookCfgName} {buffStatus}";

            PluginUi.Status = message;
        }
    }

    public void OnBeganFishing(bool mooching) {
        if (Rod.FishingTimer.IsRunning && Ws.Fishing.PreviousFishingState is not (FishingState.PoleReady or FishingState.None))
            return;

        Ws.Execute(new RodState.OpSetLureSuccess(false));
        Ws.Execute(new RodState.OpSetLastLureCastBiteTime(null));
        Ws.Execute(new RodState.OpBiteContext(0, Ws.Player.HasStatus(IDs.Status.Chum)));

        Ws.Execute(new RodState.OpSetFishingStep(FishingSteps.None));
        if (Rod.StopAfterNextFish == RodFishingModule.StopAfterState.Pending)
            Rod.StopAfterNextFish = RodFishingModule.StopAfterState.Armed;

        EzThrottler.Reset("CastingLure");

        Rod.EnqueueCallback(Rod.AutoCast.CastCollectAfterLine, 2500);

        Rod.FishingTimer.Reset();
        Rod.FishingTimer.Start();
        UpdateStatusAndTimer(mooching);
    }

    private double GetTimeoutMax(HookConfig selected)
        => !selected.Enabled ? 0 : selected.GetHookset().GetEffectiveTimeoutMax(Ws, Ws.Player.HasStatus(IDs.Status.Chum));

    public void CheckTimeout() {
        if (!Rod.FishingTimer.IsRunning)
            Rod.FishingTimer.Start();

        var maxTime = Math.Truncate(GetTimeoutMax(Rod.GetHookCfg()) * 100) / 100;
        if (!(maxTime > 0) || !(Rod.FishTimerSecs > maxTime) || Ws.Fishing.FishingStep.HasFlag(FishingSteps.TimeOut) || Ws.Fishing.FishingStep.HasFlag(FishingSteps.CancelPending))
            return;

        PluginUi.Status = @$"Timeout reached - using Rest";
        var hints = new ActionHints();
        hints.AddCast(HintPriority.TimeoutRest, new ActionRequest(IDs.Actions.Rest, ActionType.Action, UIStrings.Hook), HintSource.Timeout, detail: "Timeout", context: DecisionContext.Hook);
        if (Rod.Resolver.Resolve(Ws, hints))
            Ws.Execute(new RodState.OpSetFishingStep(FishingSteps.TimeOut));
    }

    public void OnBite() {
        try {
            UpdateStatusAndTimer();
            var currentHook = Rod.GetHookCfg();
            Ws.Decide(DecisionContext.Hook, currentHook.Enabled, currentHook.Enabled ? "Enabled on bite" : "Disabled on bite");
            Rod.FishingTimer.Stop();

            if (Ws.Player.HasStatus(IDs.Status.Salvage) && Rod.GetAutoCastCfg().ChumAnimationCancel)
                Rod.ExecuteImmediate(new ActionRequest(IDs.Actions.Salvage, ActionType.Action, "Salvage", UseRaw: true));

            HookFish(Ws.Fishing.BiteInfo.TugType.ToBiteType(), currentHook);
        }
        finally {
            Ws.Execute(new RodState.OpInvalidateCastSnapshot());
        }
    }

    public void HookFish(BiteType bite, HookConfig currentHook) {
        if (!currentHook.Enabled)
            return;

        var delay = Rod.Rng.Next(Configuration.C.DelayBetweenHookMin, Configuration.C.DelayBetweenHookMax);
        var timePassed = Rod.FishTimerSecs;
        var ws = WorldState.Get();
        ws.Execute(new RodState.OpBiteContext(timePassed, ws.Player.HasStatus(IDs.Status.Chum)));
        ws.Execute(new RodState.OpIntuition(new IntuitionInfo(ws.Fishing.Intuition.Status, ws.Player.GetStatusTime(IDs.Status.FishersIntuition))));
        ws.Execute(new OceanState.OpOceanFishing(ws.Ocean.OceanFishing));

        var hook = currentHook.GetHook(Ws, bite, timePassed);
        var hints = new ActionHints();

        if (hook is null or HookType.None) {
            delay = Rod.Rng.Next(Configuration.C.DelayBeforeCancelMin, Configuration.C.DelayBeforeCancelMax);
            hints.PreferRest = true;
            hints.PreferRestContext = DecisionContext.Hook;
            hints.PreferRestDetail = $"{bite} bite";
            hints.PreferRestDelayMs = delay;
            Rod.Resolver.Resolve(Ws, hints);
            return;
        }

        var request = hook == HookType.Stellar
            ? new ActionRequest(0, ActionType.Action, $"{hook}", DelayBeforeMs: delay, StellarHookset: true, DecisionContext: DecisionContext.Hook)
            : new ActionRequest((uint)hook, ActionType.Action, $"{hook}", DelayBeforeMs: delay, DecisionContext: DecisionContext.Hook);
        hints.AddCast(HintPriority.Hook, request, HintSource.BiteHook, detail: $"{bite} bite", context: DecisionContext.Hook);
        if (Rod.Resolver.Resolve(Ws, hints))
            PluginUi.Status = @$"Using {hook} hook. (Bite: {bite})";
    }

    public void OnCatch() {
        if (Ws.Fishing.LastCatch is not { } lastCatch || lastCatch.FishId <= 0 || lastCatch.Amount == 0)
            return;

        var fishId = lastCatch.FishId;
        var amount = lastCatch.Amount;
        var lastCatchFish = FishBaitCatalog.Get()[(int)fishId];
        var lastCatchName = lastCatchFish?.Name ?? "-";
        Ws.Execute(new RodState.OpAddFishCaught(fishId, amount));
        var lastFishCatchCfg = Rod.FishCaught.GetLastCatchConfig();
        var currentHook = Rod.GetHookCfg();

        if (lastFishCatchCfg != null) {
            for (var i = 0; i < amount; i++)
                FishingCounters.AddFishCount(lastFishCatchCfg.UniqueId);
            NotificationMasterService.Get().Api.TryNotify(lastFishCatchCfg.NotifyOnSuccess, $"Caught {lastCatchName} x{amount}");
        }

        if (currentHook.Enabled) {
            FishingCounters.AddFishCount(currentHook.UniqueId);
            NotificationMasterService.Get().Api.TryNotify(currentHook.NotifyOnSuccess, $"Hook success with {currentHook.BaitFish.Name}: {lastCatchName} x{amount}");
        }
    }

    public void CheckStopCondition() {
        if (Rod.GetEffectiveCatchConfig() is { } lastFishCatchCfg)
            TryApplyStopLimit(lastFishCatchCfg.StopAfterCaughtLimit, lastFishCatchCfg.StopFishingStep,
                lastFishCatchCfg.StopAfterResetCount, lastFishCatchCfg.UniqueId,
                UIStrings.Caught_Limited_Reached_Chat_Message, lastFishCatchCfg.Fish.Name);

        var currentHook = Rod.GetHookCfg();
        if (currentHook.Enabled)
            TryApplyStopLimit(currentHook.StopAfterCaughtLimit, currentHook.StopFishingStep, currentHook.StopAfterResetCount, currentHook.UniqueId, UIStrings.Hooking_Limited_Reached_Chat_Message, currentHook.BaitFish.Name);
    }

    public void TryApplyStopLimit<TCD>(SingleCondition<TCD, (bool Enabled, int Limit)> limit, FishingSteps stopStep,
        bool resetCount, Guid uniqueId, string chatMessageFormat, string name)
        where TCD : class, IConditionDefinition, ISimpleConditionValue<(bool Enabled, int Limit)> {
        var (stopEnabled, limitCount) = limit.Value;
        if (!stopEnabled || !limit.BackingSet.Passes(Ws))
            return;

        IChatGui.Get().PrintStatus(string.Format(chatMessageFormat, @$"{name}: {limitCount}"));
        Ws.Execute(new RodState.OpSetFishingStep(stopStep, Or: true));
        if (resetCount)
            FishingCounters.QueueRemove(uniqueId);
    }

    public void OnFishingStop() {
        Rod.ClearStopAfterNextFish();
        Rod.SpectralRestPending = false;

        Ws.Execute(new RodState.OpSetFishingStep(FishingSteps.None));

        var retainCounters = Rod.GetExtraCfg() is { Enabled: true, RetainCountersBetweenSessions: true };
        if (!retainCounters) {
            Ws.Execute(new RodState.OpResetFishCaught());
            FishingCounters.Reset();
        }

        Ws.Execute(new RodState.OpClearSessionCatches());

        if (Rod.FishingTimer.IsRunning)
            Rod.FishingTimer.Reset();

        PluginUi.Status = "";

        Rod.ExecuteImmediate(new ActionRequest(IDs.Actions.Quit, ActionType.Action, "Quit", UseRaw: true));
        Rod.BeginPostCastDelay();
    }

    public void InitFinishing() {
        if (!Rod.FishingTimer.IsRunning)
            Rod.FishingTimer.Start();

        UpdateStatusAndTimer();
    }
}
