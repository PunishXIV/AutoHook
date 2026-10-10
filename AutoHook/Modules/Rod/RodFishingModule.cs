using AutoHook.Tasks;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using Lumina.Excel.Sheets;
using System.Diagnostics;

namespace AutoHook.Modules.Rod;

public sealed class RodFishingModule : FishingModule {
    public enum StopAfterState {
        None,
        Pending,
        Armed,
    }

    private readonly EventSubscriptions _eventSubs;

    public BiteHookComponent BiteHook { get; }
    public AutoCastComponent AutoCast { get; }
    public FishCaughtComponent FishCaught { get; }
    public ExtraComponent Extra { get; }
    public BaitComponent Bait { get; }
    public LureChatComponent LureChat { get; }

    public ActionHints Hints { get; } = new();
    public ActionHintsResolver Resolver { get; } = new();

    public Stopwatch FishingTimer { get; } = new();
    public Random Rng { get; } = new();
    public StopAfterState StopAfterNextFish { get; set; }
    public bool SpectralRestPending { get; set; }

    public bool IsBusy => ActionExecutor.Get().IsBusy;

    public bool Enqueue(ActionRequest request, bool forceQueue = false)
        => ActionExecutor.Get().Enqueue(request, forceQueue);

    public bool Enqueue(ActionRequest request, params ActionRequest[] followUps)
        => ActionExecutor.Get().Enqueue(request, followUps);

    public void EnqueueCallback(System.Action callback, int delayMs = 0)
        => ActionExecutor.Get().EnqueueCallback(callback, delayMs);

    public bool ExecuteImmediate(ActionRequest request)
        => Resolver.ExecuteImmediate(request);

    public void BeginPostCastDelay() => ActionExecutor.Get().BeginPostCastDelay();

    public int GetPostCastDelayMs() => ActionExecutor.Get().GetPostCastDelayMs();

    public double FishTimerSecs => Math.Truncate(FishingTimer.ElapsedMilliseconds / 1000.0 * 100) / 100;

    public static FishingPresets Presets => Configuration.C.HookPresets;

    private WorldState Ws => WorldState;

    public RodFishingModule(WorldState worldState) : base(worldState) {
        BiteHook = AddComponent(new BiteHookComponent(this));
        AutoCast = AddComponent(new AutoCastComponent(this));
        FishCaught = AddComponent(new FishCaughtComponent(this));
        Extra = AddComponent(new ExtraComponent(this));
        Bait = AddComponent(new BaitComponent(this));
        LureChat = AddComponent(new LureChatComponent(this));

        _eventSubs = new(Ws.OceanZoneStarted.Subscribe(OnOceanZoneStarted), Ws.SpectralCurrentChanged.Subscribe(BiteHook.OnSpectralCurrentChanged));
        IChatGui.Get().LogMessage += LureChat.OnLogMessage;
        IChatGui.Get().ChatMessage += LureChat.CheckForSpecialLure;
        Ws.Modified += OnWorldStateModified;
    }

    public override void Dispose() {
        _eventSubs.Dispose();
        IChatGui.Get().ChatMessage -= LureChat.CheckForSpecialLure;
        IChatGui.Get().LogMessage -= LureChat.OnLogMessage;
        Ws.Modified -= OnWorldStateModified;
    }

    public void RequestStopAfterNextFish() {
        if (!Configuration.C.PluginEnabled)
            return;

        var midCast = Ws.Fishing.FishingState is FishingState.LineInWater or FishingState.AmbitiousLure or FishingState.ModestLure or FishingState.CastingOut or FishingState.Bite or FishingState.Hooking;
        StopAfterNextFish = midCast ? StopAfterState.Armed : StopAfterState.Pending;
    }

    public void ClearStopAfterNextFish() => StopAfterNextFish = StopAfterState.None;

    public bool TryStopAfterNextFish() {
        if (StopAfterNextFish != StopAfterState.Armed)
            return false;

        ClearStopAfterNextFish();
        Configuration.C.PluginEnabled = false;
        return true;
    }

    private void OnOceanZoneStarted(WorldState.OpOceanZoneStarted op) {
        var ocean = Ws.OceanFishing;

        if (ocean != OceanFishingState.Empty)
            OceanGoalCatalog.PrefetchRouteAchievements(ocean.CurrentRoute);

        if (!Configuration.C.PluginEnabled) {
            Ws.Decide(DecisionContext.OceanPreset, false, "Task not started", "plugin disabled");
            return;
        }

        if (!Configuration.C.AutoOceanFish) {
            Ws.Decide(DecisionContext.OceanPreset, false, "Task not started", "Auto ocean fishing disabled");
            return;
        }

        if (Svc.Automation.CurrentTask is AutoOceanFish existing) {
            Ws.Decide(DecisionContext.OceanPreset, false, "Task not started",
                $"already running (zone {existing.ZoneIndex + 1})");
            return;
        }

        Svc.Automation.Start(new AutoOceanFish(op.ZoneIndex));
        Ws.Decide(DecisionContext.OceanPreset, true, "Task started",
            OceanStopUtil.FormatStateLog(ocean));
    }

    private void OnWorldStateModified(WorldState.Operation op) {
        if (!Configuration.C.PluginEnabled)
            return;

        switch (op) {
            case RodState.OpPlayerUsedAction(var ua):
                if (ua.ActionType == ActionType.Action && Ws.ActionAvailable(ua.ActionId, ua.ActionType)) {
                    switch (ua.ActionId) {
                        case IDs.Actions.Rest:
                            if (Ws.Player.HasStatus(IDs.Status.CollectorsGlove))
                                LureChat.AnimationCancel();
                            Ws.Execute(new RodState.OpSetFishingStep(FishingSteps.CancelPending));
                            break;
                        case IDs.Actions.Cast:
                            BiteHook.OnBeganFishing(false);
                            break;
                        case IDs.Actions.Mooch:
                        case IDs.Actions.Mooch2:
                            BiteHook.OnBeganFishing(true);
                            break;
                        case IDs.Actions.AmbitiousLure:
                        case IDs.Actions.ModestLure:
                            Ws.Execute(new RodState.OpSetLastLureCastBiteTime(FishTimerSecs));
                            break;
                    }
                }
                break;
            case RodState.OpSetLastCatch:
                BiteHook.OnCatch();
                break;
            case WorldState.OpAchievementProgress:
                if (Ws.OceanFishing != OceanFishingState.Empty)
                    OceanPresetSelector.TryApply();
                break;
        }
    }

    public void StartFishing() {
        if (!(Ws.ActionAvailable(IDs.Actions.Cast, ActionType.Action) && !Ws.Player.BlockCasting)) {
            IChatGui.Get().PrintStatus(@"[AutoHook] You can't cast right now.");
            return;
        }

        OceanPresetSelector.TryApply();
        Hints.Clear();
        Extra.ProcessExtraActions(Hints, Resolver);

        var extraCfg = GetExtraCfg();
        if (extraCfg is { ForceBaitSwap: true, Enabled: true }) {
            var result = BaitComponent.ChangeBait((uint)extraCfg.ForcedBaitId);

            if (result == ChangeBaitReturn.Success) {
                IChatGui.Get().PrintStatus(@$"[AutoHook] Starting with bait: {Item.GetRow((uint)extraCfg.ForcedBaitId).Name}");
                Configuration.Save();
            }
            else if (result != ChangeBaitReturn.AlreadyEquipped)
                IChatGui.Get().PrintStatus(@$"[AutoHook] Failed to change bait for forced bait swap. Result: {result}");
        }

        Ws.Execute(new RodState.OpSetFishingStep(FishingSteps.StartPending));
        AutoCast.UseAutoCasts();
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

    public (HookConfig? custom, HookConfig? global) GetHookCandidates(uint baitId, bool isMooching) {
        HookConfig? custom = null;
        if (Presets.SelectedPreset != null)
            custom = Presets.SelectedPreset.GetCfgById(baitId, isMooching);

        var global = isMooching ? Presets.DefaultPreset.ListOfMooch.FirstOrDefault() : Presets.DefaultPreset.ListOfBaits.FirstOrDefault();
        return (custom, global);
    }

    public static uint ResolveHookCfgId(BaitInfo bait, bool isMooching) {
        if (bait.SelectedSwimbaitId is { } sb)
            return sb;
        return isMooching && WorldState.Get().Fishing.LastCatch?.FishId is { } fishId and > 0 ? fishId : bait.MoochId;
    }

    public AutoCastsConfig GetAutoCastCfg() => AutoCast.GetAutoCastCfg();
    public ExtraConfig GetExtraCfg() => Extra.GetExtraCfg();
    public FishConfig? GetEffectiveCatchConfig() => FishCaught.GetEffectiveCatchConfig();
    public CustomPresetConfig? FindPresetByName(string presetName) => Extra.FindPresetByName(presetName);

    public override void Update() {
        var currentState = Ws.Fishing.FishingState;
        if (currentState == FishingState.None) {
            if (EzThrottler.Throttle(@"CheckExtraActionsNone", 500) && Ws.IsCastAvailable()) {
                Hints.Clear();
                Extra.ProcessExtraActions(Hints, Resolver);
            }

            if (Ws.Fishing.FishingStep.HasFlag(FishingSteps.StartPending))
                CheckPluginActions();

            if (Configuration.C.AutoStartFishing && !ShouldSuppressAutoStartFishing() && EzThrottler.Throttle("AutoStartFishing", 1000)) {
                var autoCastCfg = GetAutoCastCfg();
                if (autoCastCfg.EnableAll && autoCastCfg.CastLine.IsAvailableToCast(Ws) && Ws.IsCastAvailable()) {
                    StartFishing();
                }
            }

            return;
        }

        if (currentState != FishingState.Quitting && Ws.Fishing.FishingStep.HasFlag(FishingSteps.QuitRequested)) {
            if (Ws.ActionAvailable(IDs.Actions.Quit, ActionType.Action) && !Ws.Player.BlockCasting) {
                Enqueue(new ActionRequest(IDs.Actions.Quit, ActionType.Action, @"Quit"));
                currentState = FishingState.Quitting;
            }
        }

        if (!Ws.Fishing.FishingStep.HasFlag(FishingSteps.QuitRequested) && currentState == FishingState.PoleReady)
            CheckPluginActions();

        if (SpectralRestPending)
            BiteHook.TrySpectralRest();

        if (currentState is FishingState.AmbitiousLure or FishingState.ModestLure or FishingState.LineInWater) {
            AutoCast.CheckWhileFishingActions();
            BiteHook.CheckTimeout();
        }

        var previousState = Ws.Fishing.PreviousFishingState;
        if (previousState == currentState)
            return;

        Ws.Execute(new RodState.OpSetPreviousFishingState(currentState));

        switch (currentState) {
            case FishingState.PullingPoleIn:
                var canceling = (Ws.Fishing.FishingStep & (FishingSteps.CancelPending | FishingSteps.TimeOut)) != 0;
                var nothingBit = !canceling && previousState is FishingState.LineInWater or FishingState.AmbitiousLure or FishingState.ModestLure;
                if (nothingBit)
                    Ws.Execute(new RodState.OpSetFishingStep(FishingSteps.StopCasting));
                else
                    LureChat.AnimationCancel();
                FishingTimer.Reset();
                break;
            case FishingState.CastingOut:
                BiteHook.InitFinishing();
                break;
            case FishingState.Bite:
                EnqueueCallback(BiteHook.OnBite);
                break;
            case FishingState.Quitting:
                if (!Ws.Fishing.FishingStep.HasFlag(FishingSteps.QuitRequested))
                    Ws.Execute(new RodState.OpSetFishingStep(FishingSteps.QuitRequested));
                BiteHook.OnFishingStop();
                break;
        }
    }

    public void CheckPluginActions() {
        if (!EzThrottler.Throttle(@"CheckPluginActions", 500))
            return;

        Extra.QueueResolveCollectables();

        if (!Ws.IsCastAvailable())
            return;

        if (Ws.Fishing.FishingStep.HasFlag(FishingSteps.FishCaught) && (Ws.Fishing.FishingStep & (FishingSteps.StopCasting | FishingSteps.QuitRequested)) == 0)
            BiteHook.CheckStopCondition();

        Hints.Clear();
        Extra.ProcessExtraActions(Hints, Resolver);

        // contrib fish caught/atuto cast against the final preset after the recursive extra swaps
        var lastCatchCfg = GetEffectiveCatchConfig();
        if (Ws.Fishing.FishingStep.HasFlag(FishingSteps.FishCaught) && !Ws.Fishing.FishingStep.HasFlag(FishingSteps.QuitRequested)) {
            FishCaught.ContributeCastHints(Hints, lastCatchCfg);

            if (!Hints.HasCastProposal && lastCatchCfg is { Enabled: true } && FishCaughtComponent.HasGpBlockedFishCaughtAction(lastCatchCfg)) {
                var ignoreMooch = lastCatchCfg.NeverMooch;
                if (!AutoCast.ContributeGpRestoreHints(Hints, ignoreMooch) && WorldStateUpdater.Get().HasPendingGp)
                    Hints.HoldForPendingGp = true;
            }

            FishCaught.CheckFishCaughtSwap(lastCatchCfg);
        }

        FishingCounters.RemoveGuidQueue();

        if (TryStopAfterNextFish())
            return;

        if (!Hints.HoldForPendingGp && !Hints.HasCastProposal)
            AutoCast.ContributePoleReadyHints(Hints);

        Resolver.Resolve(Ws, Hints);
    }

    public bool ShouldSuppressAutoStartFishing()
        => Configuration.C.AutoOceanFish && (Svc.Automation.CurrentTask is AutoOceanFish || Ws.OceanFishing != OceanFishingState.Empty);

    public void ClearWorldState() {
        var f = Ws.Fishing;
        var sf = Ws.Spearfishing;
        if (!Ws.Player.BlockCasting && f.FishingState == FishingState.None && f.FishingStep == FishingSteps.None &&
            f.PreviousFishingState == FishingState.None && !sf.SessionActive && !sf.WindowOpen &&
            sf.Spot.IsEmpty && sf.Wariness == 0)
            return;

        Ws.Execute(new WorldState.OpSetBlockCasting(false));
        Ws.Execute(new RodState.OpSetFishingStep(FishingSteps.None));
        Ws.Execute(new RodState.OpSetPreviousFishingState(FishingState.None));
        Ws.Execute(new RodState.OpFishingState(FishingState.None, new BaitInfo(0, null, 0, false)));

        if (sf.SessionActive || sf.WindowOpen || !sf.Spot.IsEmpty || sf.Wariness != 0)
            Ws.Execute(new SpearfishingState.OpEndSession());
    }
}
