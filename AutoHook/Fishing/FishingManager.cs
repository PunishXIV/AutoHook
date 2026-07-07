using AutoHook.Conditions;
using AutoHook.Replay;
using AutoHook.Tasks;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using System.Diagnostics;
using StatusSheet = Lumina.Excel.Sheets.Status;

namespace AutoHook.Fishing;

public partial class FishingManager : IDisposable {
    private const uint FisherJobId = 18;
    private static readonly FishingPresets Presets = Service.Configuration.HookPresets;
    private readonly Stopwatch _fishingTimer = new();
    private readonly Random _rng = new();
    private readonly EventSubscriptions _eventSubs;
    private StopAfterState _stopAfterNextFish;
    private double FishTimerSecs => Math.Truncate(_fishingTimer.ElapsedMilliseconds / 1000.0 * 100) / 100;

    private enum StopAfterState {
        None,
        Pending,
        Armed,
    }

    private static WorldState Ws => Service.WorldState;

    public void RequestStopAfterNextFish() {
        if (!Service.Configuration.PluginEnabled)
            return;

        _stopAfterNextFish = Ws.Fishing.FishingStep.HasFlag(FishingSteps.BeganFishing) ? StopAfterState.Armed : StopAfterState.Pending;
        Service.PrintDebug("[AutoHook] Stop after next fish or fishing attempt scheduled.");
    }

    private void ClearStopAfterNextFish() => _stopAfterNextFish = StopAfterState.None;

    private bool TryStopAfterNextFish() {
        if (_stopAfterNextFish != StopAfterState.Armed)
            return false;

        ClearStopAfterNextFish();
        Service.Configuration.PluginEnabled = false;
        Service.PrintDebug("[AutoHook] Stopped after fishing attempt.");
        return true;
    }

    public FishingManager() {
        _eventSubs = new(
            Ws.OceanZoneStarted.Subscribe(OnOceanZoneStarted),
            Ws.SpectralCurrentChanged.Subscribe(OnSpectralCurrentChanged));
        try {
            Svc.Framework.Update += OnFrameworkUpdate;
            Svc.Chat.LogMessage += OnLogMessage;
            Svc.Chat.ChatMessage += CheckForSpecialLure;
            Ws.Modified += OnWorldStateModified;
        }
        catch (Exception e) {
            Svc.Log.Error(@$"{e.Message}");
        }
    }

    public void Dispose() {
        _eventSubs.Dispose();
        Svc.Framework.Update -= OnFrameworkUpdate;
        Svc.Chat.ChatMessage -= CheckForSpecialLure;
        Svc.Chat.LogMessage -= OnLogMessage;
        Ws.Modified -= OnWorldStateModified;
    }

    private void OnOceanZoneStarted(WorldState.OpOceanZoneStarted op) {
        var ocean = Ws.OceanFishing;
        Service.PrintDebug($"[AutoOceanFish] OnZoneStarted zone={op.ZoneIndex + 1}, {OceanStopUtil.FormatStateLog(ocean)}");

        if (!Service.Configuration.PluginEnabled) {
            Service.PrintDebug("[AutoOceanFish] Task not started: plugin disabled");
            return;
        }

        if (!Service.Configuration.AutoOceanFish) {
            Service.PrintDebug("[AutoOceanFish] Task not started: Auto ocean fishing disabled in Settings");
            return;
        }

        if (Svc.Automation.CurrentTask is AutoOceanFish existing) {
            Service.PrintDebug($"[AutoOceanFish] Task not started: AutoOceanFish already running (zone {existing.ZoneIndex + 1})");
            return;
        }

        Svc.Automation.Start(new AutoOceanFish(this, op.ZoneIndex));
        Service.PrintDebug($"[AutoOceanFish] Task started for zone {op.ZoneIndex + 1}");
    }

    private void OnSpectralCurrentChanged(WorldState.OpSpectralCurrentChanged op) {
        if (op.Change is not SpectralCurrentChange.Gained) return;
        if (!Service.Configuration.PluginEnabled || !Service.Configuration.SpectralRest) return;
        if (Ws.Fishing.FishingState is not (FishingState.LineInWater or FishingState.AmbitiousLure)) return;
        if (Ws.Fishing.FishingStep.HasFlag(FishingSteps.Reeling | FishingSteps.TimeOut)) return;
        if (Ws.Player.BlockCasting || Service.TaskManager.IsBusy) return;
        if (!EzThrottler.Throttle("SpectralRestMidCast", 1000)) return;

        Service.Status = UIStrings.SpectralRestOnGain;
        Service.PrintDebug("Spectral gained mid-cast; resting");

        var delay = _rng.Next(Service.Configuration.DelayBeforeCancelMin, Service.Configuration.DelayBeforeCancelMax);
        Service.TaskManager.EnqueueDelay(delay);
        Service.TaskManager.Enqueue(() => {
            PlayerRes.CastActionDelayed(IDs.Actions.Rest, ActionType.Action, UIStrings.Hook);
            Ws.Execute(new FishingInfo.OpSetFishingStep(FishingSteps.Reeling));
        });
    }

    private void OnWorldStateModified(WorldState.Operation op) {
        if (!Service.Configuration.PluginEnabled)
            return;

        switch (op) {
            case FishingInfo.OpPlayerUsedAction(var ua):
                if (ua.ActionType == ActionType.Action && Ws.ActionAvailable(ua.ActionId, ua.ActionType)) {
                    switch (ua.ActionId) {
                        case IDs.Actions.Rest:
                            if (Ws.Player.HasStatus(IDs.Status.CollectorsGlove))
                                AnimationCancel();
                            Ws.Execute(new FishingInfo.OpSetFishingStep(FishingSteps.Reeling));
                            break;
                        case IDs.Actions.Cast:
                            OnBeganFishing(false);
                            break;
                        case IDs.Actions.Mooch:
                        case IDs.Actions.Mooch2:
                            OnBeganFishing(true);
                            break;
                        case IDs.Actions.AmbitiousLure:
                        case IDs.Actions.ModestLure:
                            Ws.Execute(new FishingInfo.OpSetLastLureCastBiteTime(FishTimerSecs));
                            break;
                    }
                }
                break;
            case FishingInfo.OpSetLastCatch:
                OnCatch();
                break;
        }
    }

    public void StartFishing() {
        if (!(Ws.ActionAvailable(IDs.Actions.Cast, ActionType.Action) && !Ws.Player.BlockCasting)) {
            Service.PrintChat(@"[AutoHook] You can't cast right now.");
            return;
        }

        TryApplyOceanFishingPreset();
        CheckExtraActions();

        var extraCfg = GetExtraCfg();
        if (extraCfg is { ForceBaitSwap: true, Enabled: true }) {
            var result = ChangeBait((uint)extraCfg.ForcedBaitId);

            if (result == ChangeBaitReturn.Success) {
                Service.PrintChat(@$"[AutoHook] Starting with bait: {Item.GetRow((uint)extraCfg.ForcedBaitId).Name}");
                Service.Save();
            }
            else if (result != ChangeBaitReturn.AlreadyEquipped)
                Service.PrintChat(@$"[AutoHook] Failed to change bait for forced bait swap. Result: {result}");
        }

        Ws.Execute(new FishingInfo.OpSetFishingStep(FishingSteps.StartedCasting));
        UseAutoCasts();
    }

    // The current config is updates two times: When we began fishing (to get the config based on the mooch/bait) and when we hooked the fish (in case the user updated their configs).
    private unsafe void UpdateStatusAndTimer(bool forceMooching = false) {
        if (Service.Configuration.ResetAfkTimer)
            InputTimerModule.Instance()->ResetAfkTimer();

        var selected = GetHookCfg(forceMooching);
        var hookset = selected.GetHookset();

        if (Service.Configuration.ShowStatus) {
            var buffStatus = "";

            if (hookset.RequiredStatus != 0) {
                buffStatus = StatusSheet.GetRow(hookset.RequiredStatus).Name.ToString();
                buffStatus = @$"({buffStatus})";
            }

            var hookCfgName = GetPresetName();

            var message = !selected.Enabled
                ? @$"No hooking option found. Make sure to add/enable your bait/mooch settings"
                : @$"Hooking with: {hookCfgName} {buffStatus}";

            Service.Status = message;
            Service.PrintDebug(@$"[HookManager] {message}");
        }
    }

    public string GetPresetName() {
        var bait = Ws.Fishing.BaitInfo;
        var isMooching = bait.IsMooching;
        var currentBaitId = bait.SelectedSwimbaitId is { } sb ? sb : bait.MoochId;
        var (customHook, globalHook) = GetHookCandidates(currentBaitId, isMooching);

        if (customHook?.Enabled ?? false)
            return @$"{customHook.BaitFish.Name} ({Presets.SelectedPreset?.PresetName})";

        if (globalHook?.Enabled ?? false)
            return @$"{(isMooching ? UIStrings.All_Mooches : UIStrings.All_Baits)} ({Presets.DefaultPreset.PresetName})";

        return @"None";
    }

    public HookConfig GetHookCfg(bool forceMooching = false) {
        var bait = Ws.Fishing.BaitInfo;
        var isMooching = forceMooching || bait.IsMooching;
        var (custom, global) = GetHookCandidates(ResolveHookCfgId(bait, isMooching), isMooching);
        return custom?.Enabled ?? false ? custom : global!;
    }

    private (HookConfig? custom, HookConfig? global) GetHookCandidates(uint baitId, bool isMooching) {
        HookConfig? custom = null;
        if (Presets.SelectedPreset != null)
            custom = Presets.SelectedPreset.GetCfgById(baitId, isMooching);

        var global = isMooching ? Presets.DefaultPreset.ListOfMooch.FirstOrDefault() : Presets.DefaultPreset.ListOfBaits.FirstOrDefault();
        return (custom, global);
    }

    private static uint ResolveHookCfgId(BaitInfo bait, bool isMooching) {
        if (bait.SelectedSwimbaitId is { } sb)
            return sb;
        return isMooching && Ws.Fishing.LastCatch?.FishId is { } fishId and > 0 ? fishId : bait.MoochId;
    }

    private static double GetTimeoutMax(HookConfig selected)
        => !selected.Enabled ? 0 : selected.GetHookset().GetEffectiveTimeoutMax(Ws.HasStatus(IDs.Status.Chum));

    private void OnFrameworkUpdate(IFramework _) {
        if (!Service.Configuration.PluginEnabled || !Svc.ClientState.IsLoggedIn || Svc.Objects.LocalPlayer == null)
            return;

        Service.WorldStateUpdater.Update();

        if (Player.ClassJob.RowId != FisherJobId) {
            ClearWorldState();
            return;
        }

        var currentState = Service.WorldState.Fishing.FishingState;
        if (currentState == FishingState.None) {
            if (EzThrottler.Throttle(@"CheckExtraActionsNone", 500) && Ws.IsCastAvailable())
                CheckExtraActions();

            // cordial can be done while None, but the rest of autocasts are PoleReady-only, so retry them here
            if (Ws.FishingStep.HasFlag(FishingSteps.StartedCasting) && !Ws.FishingStep.HasFlag(FishingSteps.BeganFishing))
                CheckPluginActions();

            if (Service.Configuration.AutoStartFishing && !ShouldSuppressAutoStartFishing() && EzThrottler.Throttle("AutoStartFishing", 1000)) {
                var autoCastCfg = GetAutoCastCfg();
                if (autoCastCfg.EnableAll && autoCastCfg.CastLine.IsAvailableToCast() && Ws.IsCastAvailable()) {
                    StartFishing();
                }
            }

            return;
        }

        if (currentState != FishingState.Quitting && Ws.Fishing.FishingStep.HasFlag(FishingSteps.Quitting)) {
            if (Ws.ActionAvailable(IDs.Actions.Quit, ActionType.Action) && !Ws.Player.BlockCasting) {
                PlayerRes.CastActionDelayed(IDs.Actions.Quit, ActionType.Action, @"Quit");
                currentState = FishingState.Quitting;
            }
        }

        if (!Ws.Fishing.FishingStep.HasFlag(FishingSteps.Quitting) && currentState == FishingState.PoleReady)
            CheckPluginActions();

        if (currentState is FishingState.AmbitiousLure or FishingState.LineInWater) {
            CheckWhileFishingActions();
            CheckTimeout();
        }

        if (Ws.Fishing.PreviousFishingState == currentState)
            return;

        Ws.Execute(new FishingInfo.OpSetPreviousFishingState(currentState));

        switch (currentState) {
            case FishingState.PullingPoleIn:
                if (Ws.Fishing.FishingStep.HasFlag(FishingSteps.BeganFishing))
                    Ws.Execute(new FishingInfo.OpSetFishingStep(FishingSteps.None));
                else AnimationCancel();
                _fishingTimer.Reset();
                break;
            case FishingState.CastingOut:
                InitFinishing();
                break;
            case FishingState.Bite:
                Service.TaskManager.Enqueue(OnBite);
                break;
            case FishingState.Quitting:
                if (!Ws.FishingStep.HasFlag(FishingSteps.Quitting))
                    Ws.Execute(new FishingInfo.OpSetFishingStep(FishingSteps.Quitting));
                OnFishingStop();
                break;
        }
    }

    // ocean fishing handles it on its own
    private bool ShouldSuppressAutoStartFishing() => Service.Configuration.AutoOceanFish && (Svc.Automation.CurrentTask is AutoOceanFish || Ws.OceanFishing != OceanFishingState.Empty);

    // WSU doesn't refresh when not on fisher so gotta clear block casting cause it will affect other jobs
    private void ClearWorldState() {
        var f = Ws.Fishing;
        if (!Ws.Player.BlockCasting && f.FishingState == FishingState.None && f.FishingStep == FishingSteps.None && f.PreviousFishingState == FishingState.None)
            return;

        Ws.Execute(new WorldState.OpSetBlockCasting(false));
        Ws.Execute(new FishingInfo.OpSetFishingStep(FishingSteps.None));
        Ws.Execute(new FishingInfo.OpSetPreviousFishingState(FishingState.None));
        Ws.Execute(new FishingInfo.OpFishingState(FishingState.None, new BaitInfo(0, null, 0, false)));
    }

    private void InitFinishing() {
        if (!_fishingTimer.IsRunning)
            _fishingTimer.Start();

        UpdateStatusAndTimer();
    }

    private void CheckPluginActions() {
        if (!EzThrottler.Throttle(@"CheckPluginActions", 500))
            return;

        QueueResolveCollectables(); // must run before anything that sets blockcasting

        if (!Ws.IsCastAvailable())
            return;

        if (Ws.Fishing.FishingStep.HasFlag(FishingSteps.FishCaught) &&
            (Ws.Fishing.FishingStep & (FishingSteps.None | FishingSteps.Quitting)) == 0)
            CheckStopCondition();

        CheckExtraActions();

        var lastCatchCfg = GetEffectiveCatchConfig();

        var casted = false;
        if (Ws.FishingStep.HasFlag(FishingSteps.FishCaught) && !Ws.FishingStep.HasFlag(FishingSteps.Quitting)) {
            casted = UseFishCaughtActions(lastCatchCfg);
            CheckFishCaughtSwap(lastCatchCfg);
        }

        FishingHelper.RemoveGuidQueue();

        if (TryStopAfterNextFish())
            return;

        if (!casted)
            UseAutoCasts();
    }

    private void OnBeganFishing(bool mooching) {
        if (Ws.Fishing.FishingStep.HasFlag(FishingSteps.BeganFishing) && Ws.Fishing.PreviousFishingState != FishingState.PoleReady && Ws.Fishing.PreviousFishingState != FishingState.None)
            return;

        Ws.Execute(new FishingInfo.OpSetLureSuccess(false));
        Ws.Execute(new FishingInfo.OpSetLastLureCastBiteTime(null));

        var baitname = Item.GetRow(Ws.Fishing.BaitInfo.MoochId).Name.ToString();
        if (!mooching)
            Service.PrintDebug(@$"Started fishing with normal bait: {baitname}");
        else
            Service.PrintDebug(@$"Started mooching/swimbait with {baitname}");

        Ws.Execute(new FishingInfo.OpSetFishingStep(FishingSteps.BeganFishing));
        if (_stopAfterNextFish == StopAfterState.Pending)
            _stopAfterNextFish = StopAfterState.Armed;

        EzThrottler.Reset("CastingLure");

        Service.TaskManager.EnqueueDelay(2500);
        Service.TaskManager.Enqueue(CastCollectAfterLine);

        _fishingTimer.Reset();
        _fishingTimer.Start();
        UpdateStatusAndTimer(mooching);
    }

    private void CheckTimeout() {
        if (!_fishingTimer.IsRunning)
            _fishingTimer.Start();

        var maxTime = Math.Truncate(GetTimeoutMax(GetHookCfg()) * 100) / 100;

        if (!(maxTime > 0) || !(FishTimerSecs > maxTime) || Ws.Fishing.FishingStep.HasFlag(FishingSteps.TimeOut) ||
            Ws.Fishing.FishingStep.HasFlag(FishingSteps.Reeling))
            return;

        Service.Status = @$"Timeout reached - using Rest";
        PlayerRes.CastActionDelayed(IDs.Actions.Rest, ActionType.Action, UIStrings.Hook);
        Ws.Execute(new FishingInfo.OpSetFishingStep(FishingSteps.TimeOut));
    }

    private void OnBite() {
        UpdateStatusAndTimer();
        var currentHook = GetHookCfg();
        ReplayDecisions.HookPresetOnBite(currentHook.Enabled);
        _fishingTimer.Stop();

        if (Ws.Player.HasStatus(IDs.Status.Salvage) && GetAutoCastCfg().ChumAnimationCancel)
            PlayerRes.CastAction(IDs.Actions.Salvage);

        HookFish(Ws.Fishing.BiteInfo.TugType.ToBiteType(), currentHook);
    }

    private void HookFish(BiteType bite, HookConfig currentHook) {
        if (!currentHook.Enabled)
            return;

        var delay = _rng.Next(Service.Configuration.DelayBetweenHookMin, Service.Configuration.DelayBetweenHookMax);
        var timePassed = FishTimerSecs;
        var ws = Service.WorldState;
        ws.Execute(new FishingInfo.OpBiteContext(timePassed, ws.Player.HasStatus(IDs.Status.Chum)));
        ws.Execute(new FishingInfo.OpIntuition(new IntuitionInfo(ws.Fishing.Intuition.Status, ws.Player.GetStatusTime(IDs.Status.FishersIntuition))));
        ws.Execute(new OceanFishInfo.OpOceanFishing(ws.Ocean.OceanFishing));

        var hook = currentHook.GetHook(bite, timePassed);

        if (hook is null or HookType.None) {
            delay = _rng.Next(Service.Configuration.DelayBeforeCancelMin, Service.Configuration.DelayBeforeCancelMax);
            ReplayDecisions.HookPresetChoice(bite, null);

            Service.TaskManager.EnqueueDelay(delay);
            Service.TaskManager.Enqueue(() => PlayerRes.CastAction(IDs.Actions.Rest));
            //_lastStep = FishingSteps.Reeling;
            Service.PrintDebug(@$"[HookManager] No hook found, using Rest");
            return;
        }

        ReplayDecisions.HookPresetChoice(bite, hook);
        Service.TaskManager.EnqueueDelay(delay);
        Service.TaskManager.Enqueue(() => {
            if (hook == HookType.Stellar)
                PlayerRes.TryUseStellarHookset();
            else
                PlayerRes.CastActionDelayed((uint)hook, ActionType.Action, @$"{hook}");
        });
        Service.Status = @$"Using {hook} hook. (Bite: {bite})";
    }

    private void OnCatch() {
        if (Ws.Fishing.LastCatch is not { } lastCatch || lastCatch.FishId <= 0 || lastCatch.Amount == 0)
            return;

        var fishId = lastCatch.FishId;
        var amount = lastCatch.Amount;
        var lastCatchFish = GameRes.Fishes.FirstOrDefault(fish => fish.Id == fishId) ?? new BaitFishClass(@"-", -1);
        Ws.Execute(new FishingInfo.OpAddFishCaught(fishId, amount));
        var lastFishCatchCfg = GetLastCatchConfig();
        var currentHook = GetHookCfg();

        Service.LastCatch = lastCatchFish;
        Service.PrintDebug(@$"[HookManager] Caught {lastCatchFish.Name} (id {lastCatchFish.Id})");

        if (lastFishCatchCfg != null) {
            for (var i = 0; i < amount; i++)
                FishingHelper.AddFishCount(lastFishCatchCfg.UniqueId);

            Service.NotificationMaster.TryNotify(lastFishCatchCfg.NotifyOnSuccess, $"Caught {lastCatchFish.Name} x{amount}");
        }

        if (currentHook.Enabled) {
            FishingHelper.AddFishCount(currentHook.UniqueId);
            Service.NotificationMaster.TryNotify(currentHook.NotifyOnSuccess, $"Hook success with {currentHook.BaitFish.Name}: {lastCatchFish.Name} x{amount}");
        }
    }

    private void CheckStopCondition() {
        if (GetEffectiveCatchConfig() is { } lastFishCatchCfg)
            TryApplyStopLimit(lastFishCatchCfg.StopAfterCaughtLimit, lastFishCatchCfg.StopFishingStep,
                lastFishCatchCfg.StopAfterResetCount, lastFishCatchCfg.UniqueId,
                UIStrings.Caught_Limited_Reached_Chat_Message, lastFishCatchCfg.Fish.Name);

        var currentHook = GetHookCfg();
        if (currentHook.Enabled)
            TryApplyStopLimit(currentHook.StopAfterCaughtLimit, currentHook.StopFishingStep,
                currentHook.StopAfterResetCount, currentHook.UniqueId,
                UIStrings.Hooking_Limited_Reached_Chat_Message, currentHook.BaitFish.Name);
    }

    private void TryApplyStopLimit<TCD>(SingleCondition<TCD, (bool Enabled, int Limit)> limit, FishingSteps stopStep,
        bool resetCount, Guid uniqueId, string chatMessageFormat, string name)
        where TCD : class, IConditionDefinition, ISimpleConditionValue<(bool Enabled, int Limit)> {
        var (stopEnabled, limitCount) = limit.Value;
        if (!stopEnabled || !limit.BackingSet.Passes())
            return;

        Service.PrintChat(string.Format(chatMessageFormat, @$"{name}: {limitCount}"));
        Ws.Execute(new FishingInfo.OpSetFishingStep(stopStep, Or: true));
        if (resetCount)
            FishingHelper.ToBeRemoved.Add(uniqueId);
    }

    private void OnFishingStop() {
        ClearStopAfterNextFish();

        Ws.Execute(new FishingInfo.OpSetFishingStep(FishingSteps.None));
        Ws.Execute(new FishingInfo.OpResetFishCaught());
        Ws.Execute(new FishingInfo.OpClearSessionCatches());

        if (_fishingTimer.IsRunning)
            _fishingTimer.Reset();

        Service.Status = "";

        FishingHelper.Reset();

        PlayerRes.CastActionNoDelay(IDs.Actions.Quit);
        PlayerRes.DelayNextCast(0);
    }
}
