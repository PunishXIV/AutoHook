using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Hooking;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using FFXIVClientStructs.FFXIV.Client.Network;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.Interop;
using Lumina.Excel.Sheets;
using System.Reflection;

namespace AutoHook;

public readonly struct BiteContext {
    public double BiteTimeSeconds { get; init; }
    public bool ChumActive { get; init; }
    public IntuitionStatus IntuitionStatus { get; init; }
    public float IntuitionTimeRemaining { get; init; }
    public SpectralCurrentStatus SpectralCurrentStatus { get; init; }
    public uint? LastCaughtFishId { get; init; }
}

public sealed class WorldStateUpdater : IDisposable {
    private const float BiteTimeLogThreshold = 0.25f;

    private readonly Hook<ActionManager.Delegates.UseAction>? _useActionHook;
    private readonly Hook<AgentCatch.Delegates.UpdateCatch>? _updateCatchHook;
    private readonly Hook<FishingEventHandler.Delegates.PlayAnimation>? _playAnimationHook;
    private readonly Hook<PacketDispatcher.Delegates.HandleActorControlPacket>? _handleActorControlPacketHook;
    private static IReadOnlyList<Lumina.Excel.Sheets.Action> FshActions = [];
    private static readonly (uint Id, ActionType Type)[] TrackedFishingActions = BuildTrackedFishingActions();
    private static readonly (uint Id, ActionType Type)[] TrackedAutoCastItems =
    [
        (IDs.Item.HiCordial, ActionType.Item),
        (IDs.Item.HQCordial, ActionType.Item),
        (IDs.Item.Cordial, ActionType.Item),
        (IDs.Item.HQWateredCordial, ActionType.Item),
        (IDs.Item.WateredCordial, ActionType.Item),
    ];

    private readonly DateTime _startTime = DateTime.UtcNow;
    private readonly long _startQpc;
    private readonly Dictionary<uint, (float Time, int Stacks)> _statusScratch = [];
    private readonly List<uint> _swimbaitScratch = [];
    private readonly List<InstanceContentOceanFishing.FishDataStruct> _fishDataScratch = [];
    private readonly Cooldown[] _cooldownScratch = new Cooldown[PlayerInfo.NumCooldownGroups];
    private readonly Dictionary<ulong, uint> _actionStatusScratch = [];
    private readonly Dictionary<ulong, int> _actionRecastScratch = [];
    private readonly Dictionary<uint, ushort> _dutyChargesScratch = [];

    private bool _needInventoryUpdate = true;

    public unsafe WorldStateUpdater() {
        _startQpc = Framework.Instance()->PerformanceCounterValue;
        _updateCatchHook = Svc.Hook.HookFromAddress<AgentCatch.Delegates.UpdateCatch>((nint)AgentCatch.MemberFunctionPointers.UpdateCatch, UpdateCatchDetour);
        _useActionHook = Svc.Hook.HookFromAddress<ActionManager.Delegates.UseAction>((nint)ActionManager.MemberFunctionPointers.UseAction, UseActionDetour);
        _playAnimationHook = Svc.Hook.HookFromAddress<FishingEventHandler.Delegates.PlayAnimation>((nint)FishingEventHandler.StaticVirtualTablePointer->PlayAnimation, PlayAnimationDetour);
        _handleActorControlPacketHook = Svc.Hook.HookFromAddress<PacketDispatcher.Delegates.HandleActorControlPacket>((nint)PacketDispatcher.MemberFunctionPointers.HandleActorControlPacket, HandleActorControlPacketDetour);
        _updateCatchHook?.Enable();
        _useActionHook?.Enable();
        _playAnimationHook?.Enable();
        _handleActorControlPacketHook?.Enable();
        FshActions = ClassJob.Get(18).GetActions();

        Svc.GameInventory.InventoryChanged += OnInventoryChanged;
    }

    public void Dispose() {
        _useActionHook?.Dispose();
        _updateCatchHook?.Dispose();
        _playAnimationHook?.Dispose();
        _handleActorControlPacketHook?.Dispose();
        Svc.GameInventory.InventoryChanged -= OnInventoryChanged;
    }

    /// <summary>Push current game state into WorldState. Call every frame.</summary>
    public unsafe void Update() {
        if (Player.ClassJob.RowId is not 18 || Svc.Objects.LocalPlayer is null)
            return;

        var ws = Service.WorldState;
        var fwk = Framework.Instance();
        if (fwk == null)
            return;

        ws.Execute(new WorldState.OpFrameStart(new FrameState(
            _startTime.AddSeconds((double)(fwk->PerformanceCounterValue - _startQpc) / ws.QPF),
            (ulong)fwk->PerformanceCounterValue,
            fwk->FrameCounter,
            fwk->RealFrameDeltaTime,
            fwk->FrameDeltaTime,
            fwk->GameSpeedMultiplier)));

        var lp = Svc.Objects.LocalPlayer;
        var gp = lp?.CurrentGp ?? 0;
        var maxGp = lp?.MaxGp ?? 0;
        if (ws.Player.CurrentGp != gp || ws.Player.MaxGp != maxGp)
            ws.Execute(new PlayerInfo.OpGp(gp, maxGp));

        var level = lp?.Level ?? 0;
        if (ws.Player.Level != level)
            ws.Execute(new PlayerInfo.OpLevel(level));

        UpdateEorzeaTime(ws, fwk);
        UpdateStatuses(ws);
        UpdateCooldowns(ws);
        UpdateActionStates(ws);
        UpdateDutyActions(ws);
        UpdateOceanFishing(ws);
        UpdateWKS(ws);
        UpdateTerritory(ws);
        UpdateWeather(ws);

        var previousFishingState = ws.Fishing.FishingState;
        var biteContext = CollectBiteContext(ws);
        UpdateFishingState(ws, biteContext);

        if (previousFishingState == FishingState.None && ws.Fishing.FishingState != FishingState.None)
            ws.Execute(new WorldState.OpBeganSession());
        else if (previousFishingState != FishingState.None && ws.Fishing.FishingState == FishingState.None)
            ws.Execute(new WorldState.OpEndedSession());

        if (_needInventoryUpdate) {
            var (counts, stats) = CollectInventory();
            ws.Execute(counts);
            if (ws.Player.FreeInventorySlots != stats.FreeSlots || ws.Player.ReduceableFishCount != stats.ReduceableFish)
                ws.Execute(stats);
            _needInventoryUpdate = false;
        }

        UpdateSwimbaitIds(ws);
        UpdatePotCooldown(ws);
        UpdateBiteContext(ws, biteContext);
        UpdateIntuition(ws, biteContext);

        var fishingState = ws.Fishing.FishingState;
        if (CastSnapshotTransition(previousFishingState, fishingState))
            ws.Execute(new FishingInfo.OpUpdateCastSnapshot(previousFishingState));
    }

    private static bool CastSnapshotTransition(FishingState previous, FishingState current)
        => previous != FishingState.LineInWater && current == FishingState.LineInWater
           || previous == FishingState.LineInWater && current != FishingState.LineInWater;

    public void RefreshFishingStateSnapshot() {
        if (Player.ClassJob.RowId is not 18 || Svc.Objects.LocalPlayer is null)
            return;

        var ws = Service.WorldState;
        var biteContext = CollectBiteContext(ws);
        UpdateFishingState(ws, biteContext);
        UpdateSwimbaitIds(ws);
        UpdateBiteContext(ws, biteContext);
        UpdateIntuition(ws, biteContext);
    }

    private void OnInventoryChanged(IReadOnlyCollection<InventoryEventArgs> _)
        => _needInventoryUpdate = true;

    private static unsafe void UpdateEorzeaTime(WorldState ws, Framework* fwk) {
        var eorzea = TimeOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(fwk->ClientTime.EorzeaTime).DateTime);
        if (ws.EorzeaTime != eorzea)
            ws.Execute(new WorldState.OpEorzeaTime(eorzea));
    }

    private void UpdateStatuses(WorldState ws) {
        _statusScratch.Clear();
        if (Svc.Objects.LocalPlayer is { StatusList: var statuses }) {
            foreach (var buff in statuses)
                _statusScratch[buff.StatusId] = (buff.RemainingTime, buff.Param);
        }

        if (StatusesEqual(ws.Player.Statuses, _statusScratch))
            return;

        ws.Execute(new PlayerInfo.OpStatuses(new Dictionary<uint, (float, int)>(_statusScratch)));
    }

    private static bool StatusesEqual(Dictionary<uint, (float Time, int Stacks)> current, Dictionary<uint, (float Time, int Stacks)> next) {
        if (current.Count != next.Count)
            return false;

        foreach (var (id, (time, stacks)) in next) {
            if (!current.TryGetValue(id, out var prev))
                return false;
            if (prev.Stacks != stacks)
                return false;
            if (Math.Abs(prev.Time - time) > 1f)
                return false;
        }

        return true;
    }

    private unsafe void UpdateCooldowns(WorldState ws) {
        var am = ActionManager.Instance();
        if (am == null)
            return;

        var anyNonDefault = false;
        for (var i = 0; i < PlayerInfo.NumCooldownGroups; i++) {
            var detail = am->GetRecastGroupDetail(i);
            if (detail == null) {
                _cooldownScratch[i] = default;
                continue;
            }

            _cooldownScratch[i] = new Cooldown(detail->Elapsed, detail->Total);
            if (detail->Total > 0f)
                anyNonDefault = true;
        }

        if (!anyNonDefault && ws.Player.Cooldowns.All(c => c.Total <= 0f))
            return;

        if (MemoryExtensions.SequenceEqual(ws.Player.Cooldowns.AsSpan(), _cooldownScratch.AsSpan()))
            return;

        if (_cooldownScratch.AsSpan().IndexOfAnyExcept(default(Cooldown)) < 0) {
            ws.Execute(new PlayerInfo.OpCooldown(true, []));
            return;
        }

        var changes = CalcCooldownDifference(_cooldownScratch, ws.Player.Cooldowns);
        if (changes.Count > 0)
            ws.Execute(new PlayerInfo.OpCooldown(false, changes));
    }

    private static (uint Id, ActionType Type)[] BuildTrackedFishingActions()
        => [.. typeof(IDs.Actions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => Convert.ToUInt32(f.GetValue(null) ?? 0u))
            .Where(id => id != IDs.Actions.None)
            .Select(id => (id, ActionType.Action))];

    private unsafe void UpdateActionStates(WorldState ws) {
        var am = ActionManager.Instance();
        if (am == null)
            return;

        _actionStatusScratch.Clear();
        _actionRecastScratch.Clear();
        foreach (var (id, type) in TrackedFishingActions.Concat(TrackedAutoCastItems)) {
            var key = PlayerInfo.ActionKey(type, id);
            _actionStatusScratch[key] = am->GetActionStatus(type, id);
            _actionRecastScratch[key] = am->GetRecastGroup((int)type, id);
        }

        if (ActionStatesEqual(ws.Player.ActionStatus, ws.Player.ActionRecastGroup, _actionStatusScratch, _actionRecastScratch))
            return;

        ws.Execute(new PlayerInfo.OpActionStates(new Dictionary<ulong, uint>(_actionStatusScratch), new Dictionary<ulong, int>(_actionRecastScratch)));
    }

    private static bool ActionStatesEqual(Dictionary<ulong, uint> currentStatus, Dictionary<ulong, int> currentGroups, Dictionary<ulong, uint> nextStatus, Dictionary<ulong, int> nextGroups) {
        if (currentStatus.Count != nextStatus.Count || currentGroups.Count != nextGroups.Count)
            return false;

        foreach (var (key, status) in nextStatus) {
            if (!currentStatus.TryGetValue(key, out var prevStatus) || prevStatus != status)
                return false;
            if (!currentGroups.TryGetValue(key, out var prevGroup) || prevGroup != nextGroups[key])
                return false;
        }

        return true;
    }

    private unsafe void UpdateDutyActions(WorldState ws) {
        _dutyChargesScratch.Clear();
        var dm = DutyActionManager.GetInstanceIfReady();
        var active = dm != null;
        if (dm != null) {
            for (var i = 0; i < dm->NumValidSlots; i++) {
                var id = dm->ActionId[i];
                if (id == 0)
                    continue;
                _dutyChargesScratch[id] = dm->CurCharges[i];
            }
        }

        if (ws.Player.DutyActionManagerActive == active && DutyChargesEqual(ws.Player.DutyActionCharges, _dutyChargesScratch))
            return;

        ws.Execute(new PlayerInfo.OpDutyActions(active, new Dictionary<uint, ushort>(_dutyChargesScratch)));
    }

    private static bool DutyChargesEqual(Dictionary<uint, ushort> current, Dictionary<uint, ushort> next) {
        if (current.Count != next.Count)
            return false;
        foreach (var (id, charges) in next) {
            if (!current.TryGetValue(id, out var prev) || prev != charges)
                return false;
        }
        return true;
    }

    private static List<(int, Cooldown)> CalcCooldownDifference(ReadOnlySpan<Cooldown> values, ReadOnlySpan<Cooldown> reference) {
        var res = new List<(int, Cooldown)>();
        for (var i = 0; i < Math.Min(values.Length, reference.Length); i++) {
            if (values[i] != reference[i])
                res.Add((i, values[i]));
        }
        return res;
    }

    private static void UpdateTerritory(WorldState ws) {
        var territory = Svc.ClientState.TerritoryType;
        if (ws.TerritoryId != territory)
            ws.Execute(new WorldState.OpTerritory(territory));
    }

    private static unsafe void UpdateWeather(WorldState ws) {
        var territory = TerritoryType.GetRow(ws.TerritoryId);
        var currentModified = WeatherManager.Instance()->GetCurrentWeather();
        (var current, var previous, var next) = (territory.GetCurrentWeather().RowId, territory.GetPreviousWeather().RowId, territory.GetNextWeather().RowId);

        if (ws.CurrentModifiedWeatherId == currentModified && ws.CurrentWeatherId == current && ws.PreviousWeatherId == previous && ws.NextWeatherId == next)
            return;

        ws.Execute(new WorldState.OpWeather(currentModified, current, previous, next));
    }

    private static void UpdateBiteContext(WorldState ws, BiteContext biteContext) {
        var chumChanged = ws.Fishing.ChumActive != biteContext.ChumActive;
        var timeDelta = Math.Abs(ws.Fishing.BiteInfo.BiteTimeSeconds - biteContext.BiteTimeSeconds);
        if (!chumChanged && timeDelta < BiteTimeLogThreshold)
            return;
        ws.Execute(new FishingInfo.OpBiteContext(biteContext.BiteTimeSeconds, biteContext.ChumActive));
    }

    private static void UpdateIntuition(WorldState ws, BiteContext biteContext) {
        var next = new IntuitionInfo(biteContext.IntuitionStatus, biteContext.IntuitionTimeRemaining);
        if (ws.Fishing.Intuition == next)
            return;
        ws.Execute(new FishingInfo.OpIntuition(next));
    }

    private static BiteContext CollectBiteContext(WorldState ws) {
        return new BiteContext {
            BiteTimeSeconds = ws.Fishing.BiteInfo.BiteTimeSeconds,
            ChumActive = ws.Player.HasStatus(IDs.Status.Chum),
            IntuitionStatus = ws.Player.HasStatus(IDs.Status.FishersIntuition) ? IntuitionStatus.Active : IntuitionStatus.NotActive,
            IntuitionTimeRemaining = ws.Player.GetStatusTime(IDs.Status.FishersIntuition),
            SpectralCurrentStatus = ws.Ocean.SpectralCurrentStatus,
            LastCaughtFishId = ws.Fishing.LastCatch?.FishId,
        };
    }

    private unsafe void UpdateOceanFishing(WorldState ws) {
        var ptr = EventFramework.Instance()->GetInstanceContentOceanFishing();
        if (ptr == null) {
            if (ws.Ocean.OceanFishing != OceanFishingState.Empty)
                ws.Execute(new OceanFishInfo.OpOceanFishing(null));
            return;
        }

        _fishDataScratch.Clear();
        foreach (var f in ptr->FirstZoneFishData)
            _fishDataScratch.Add(f);
        foreach (var f in ptr->SecondZoneFishData)
            _fishDataScratch.Add(f);
        foreach (var f in ptr->ThirdZoneFishData)
            _fishDataScratch.Add(f);

        var routeRow = IKDRoute.GetRow(ptr->CurrentRoute);
        var zoneIndex = (int)ptr->CurrentZone;
        var timeId = routeRow.Time[zoneIndex].RowId;
        var state = new OceanFishingState {
            SpectralCurrentActive = ptr->SpectralCurrentActive,
            CurrentRoute = ptr->CurrentRoute,
            TimeOfDay = (TimeOfDay)timeId,
            CurrentZone = ptr->CurrentZone,
            CurrentSpotId = routeRow.Spot[zoneIndex].RowId,
            CurrentTimeId = timeId,
            TimeLeftInZone = Math.Max(0f, EventFramework.Instance()->GetInstanceContentDirector()->ContentTimeLeft - ptr->TimeOffset),
            ZoneTimeMax = ptr->GetContentTimeMax(),
            Mission1 = new OceanMission(ptr->Mission1Type, ptr->Mission1Progress),
            Mission2 = new OceanMission(ptr->Mission2Type, ptr->Mission2Progress),
            Mission3 = new OceanMission(ptr->Mission3Type, ptr->Mission3Progress),
            FishData = [.. _fishDataScratch],
            Status = ptr->Status,
        };

        if (ws.Ocean.OceanFishing.SameAs(state))
            return;

        ws.Execute(new OceanFishInfo.OpOceanFishing(state));
    }

    private unsafe void UpdateFishingState(WorldState ws, BiteContext biteContext) {
        var state = FishingState.None;
        uint baitId = 0;
        uint? swimbaitId = null;
        var isMooching = false;
        PreviousCatchInfo previousCatch = default;
        var canFish = false;
        var changingPosition = false;
        FishingBaitFlags castFlags = 0;
        sbyte selectedSwimbait = 0;
        long moochExpire = 0;
        long catchExpire = 0;

        try {
            if (Player.Territory is { Value.TerritoryIntendedUse.RowId: 60 }) {
                if (WKSManager.Instance() is not null and var cosmic)
                    baitId = cosmic->State.FishingBait;
            }
            else
                baitId = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance()->FishingBait;

            var ef = EventFramework.Instance();
            var handler = ef != null ? ef->EventHandlerModule.FishingEventHandler : null;
            if (handler != null) {
                state = handler->State;
                if (handler->CurrentSelectedSwimBait is >= 0 and < 3)
                    swimbaitId = handler->SwimBaitItemIds[handler->CurrentSelectedSwimBait];
                var flags = handler->CurrentCastBaitFlags;
                isMooching = (flags & (FishingBaitFlags.Mooch | FishingBaitFlags.Swimbait)) != 0;
                previousCatch = new PreviousCatchInfo(
                    handler->CanMoochPreviousCatch,
                    handler->CanMooch2PreviousCatch,
                    handler->CanReleasePreviousCatch,
                    handler->CanIdenticalCastPreviousCatch,
                    handler->CanSurfaceSlapPreviousCatch);
                canFish = handler->CanFish;
                changingPosition = handler->ChangingPosition;
                castFlags = handler->CurrentCastBaitFlags;
                selectedSwimbait = handler->CurrentSelectedSwimBait;
                moochExpire = handler->MoochOpportunityExpirationTime;
                catchExpire = handler->CatchActionExpirationTime;
            }
        }
        catch { }

        var baitMoochId = ComputeCurrentBaitMoochId(baitId, swimbaitId, isMooching, biteContext);
        var bait = new BaitInfo(baitId, swimbaitId, baitMoochId, isMooching);

        if (ws.Fishing.FishingState != state || ws.Fishing.BaitInfo != bait)
            ws.Execute(new FishingInfo.OpFishingState(state, bait));

        var handlerState = new FishingInfo.OpFishingHandlerState(
            previousCatch, canFish, changingPosition, castFlags, selectedSwimbait, moochExpire, catchExpire);
        var f = ws.Fishing;
        if (f.PreviousCatch != previousCatch || f.CanFish != canFish || f.ChangingPosition != changingPosition ||
            f.CurrentCastBaitFlags != castFlags || f.CurrentSelectedSwimbait != selectedSwimbait ||
            f.MoochOpportunityExpirationTime != moochExpire || f.CatchActionExpirationTime != catchExpire)
            ws.Execute(handlerState);
    }

    private unsafe void UpdateSwimbaitIds(WorldState ws) {
        _swimbaitScratch.Clear();
        try {
            if (EventFramework.Instance() is not null and var ef && ef->EventHandlerModule.FishingEventHandler is not null and var handler)
                _swimbaitScratch.Add(handler->SwimBaitItemIds.ToArray());
        }
        catch { }

        if (SwimbaitIdsEqual(ws.Fishing.SwimbaitIds, _swimbaitScratch))
            return;

        ws.Execute(new FishingInfo.OpSwimbaitIds([.. _swimbaitScratch]));
    }

    private static bool SwimbaitIdsEqual(List<uint> current, List<uint> next) {
        if (current.Count != next.Count)
            return false;
        for (var i = 0; i < current.Count; i++) {
            if (current[i] != next[i])
                return false;
        }
        return true;
    }

    private unsafe void UpdatePotCooldown(WorldState ws) {
        var off = false;
        var am = ActionManager.Instance();
        if (am != null) {
            var recast = am->GetRecastGroupDetail(68);
            if (recast != null)
                off = recast->Total - recast->Elapsed <= 0;
        }

        if (ws.Player.IsPotOffCooldown == off)
            return;

        ws.Execute(new PlayerInfo.OpPotCooldown(off));
    }

    private static unsafe WKSInfo.OpState CollectWKSInfo() {
        ushort devGrade = 0;
        ushort currentFateControlRowId = 0;
        ushort currentFateId = 0;
        ushort currentMissionUnitRowId = 0;
        uint currentScore = 0;
        var currentRank = WKSMissionModule.MissionRank.None;
        ushort collectedTotal = 0;
        byte collectedIndividual = 0;

        try {
            if (Player.Territory is { Value.TerritoryIntendedUse.RowId: 60 } && WKSManager.Instance() is not null and var wks) {
                devGrade = wks->State.DevGrade;
                currentFateControlRowId = wks->State.CurrentFateControlRowId;
                currentFateId = wks->State.CurrentFateId;
                currentMissionUnitRowId = wks->State.CurrentMission.MissionUnitRowId;
                currentScore = wks->State.CurrentMission.ScoreUInt;
                currentRank = wks->State.CurrentMission.Rank;
                collectedTotal = wks->State.CurrentMission.CollectedTotal;
                collectedIndividual = wks->State.CurrentMission.CollectedIndividual;
            }
        }
        catch { }

        return new WKSInfo.OpState(devGrade, currentFateControlRowId, currentFateId, currentMissionUnitRowId, currentScore, currentRank, collectedTotal, collectedIndividual);
    }

    private static void UpdateWKS(WorldState ws) {
        var next = CollectWKSInfo();
        var w = ws.WKS;
        if (w.DevGrade == next.DevGrade && w.CurrentFateControlRowId == next.CurrentFateControlRowId &&
            w.CurrentFateId == next.CurrentFateId && w.CurrentMissionUnitRowId == next.CurrentMissionUnitRowId &&
            w.CurrentScore == next.CurrentScore && w.CurrentRank == next.CurrentRank &&
            w.CollectedTotal == next.CollectedTotal && w.CollectedIndividual == next.CollectedIndividual)
            return;
        ws.Execute(next);
    }

    private unsafe bool UseActionDetour(ActionManager* thisPtr, ActionType actionType, uint actionId, ulong targetId, uint extraParam, ActionManager.UseActionMode mode, uint comboRouteId, bool* outOptAreaTargeted) {
        try {
            if (actionType == ActionType.Action && Service.Configuration.PluginEnabled && Service.WorldState.ActionAvailable(actionId, actionType))
                Service.WorldState.Execute(new FishingInfo.OpPlayerUsedAction(new UsedAction(actionId, actionType)));
        }
        catch (Exception e) {
            Service.PrintDebug($"[WorldStateUpdater] UseAction: {e.Message}");
        }
        return _useActionHook!.Original(thisPtr, actionType, actionId, targetId, extraParam, mode, comboRouteId, outOptAreaTargeted);
    }

    private unsafe void UpdateCatchDetour(AgentCatch* thisPtr, uint itemId, bool isLarge, ushort size, byte amount, byte level, byte stars, byte oceanStars, bool isMoochable, bool isFirstTimeCatch, byte a11, byte a12) {
        _updateCatchHook!.Original(thisPtr, itemId, isLarge, size, amount, level, stars, oceanStars, isMoochable, isFirstTimeCatch, a11, a12);
        if (ItemUtil.GetBaseId(itemId) is { ItemId: > 0 and var id }) {
            Service.PrintDebug($"Caught fish: {id}, amount: {amount}, large: {isLarge}, size: {size}, level: {level}, stars: {stars}, oceanStars: {oceanStars}, moochable: {isMoochable}, firstTimeCatch: {isFirstTimeCatch}");
            Service.WorldState.Execute(new FishingInfo.OpSetLastCatch(new CatchInfo(id, amount, isLarge, size, level, stars, oceanStars, isMoochable, isFirstTimeCatch)));
        }
        Service.WorldState.Execute(new FishingInfo.OpSetFishingStep(FishingSteps.FishCaught));
    }

    private unsafe bool PlayAnimationDetour(FishingEventHandler* thisPtr, Character* chara, ushort actionTimelineId, ulong a4) {
        var tugType = (FishingHookStrength)actionTimelineId;
        if (tugType is FishingHookStrength.Weak or FishingHookStrength.Strong or FishingHookStrength.Legendary) {
            Service.WorldState.Execute(new FishingInfo.OpSetFishingStep(FishingSteps.FishBit));
            Service.WorldState.Execute(new FishingInfo.OpTugType(tugType));
        }
        else {
            Service.WorldState.Execute(new FishingInfo.OpTugType(0));
        }

        return _playAnimationHook!.Original(thisPtr, chara, actionTimelineId, a4);
    }

    private void HandleActorControlPacketDetour(uint entityId, uint category, uint arg1, uint arg2, uint arg3, uint arg4, uint arg5, uint arg6, uint arg7, uint arg8, GameObjectId targetId, bool isRecorded) {
        if (category is 3501) { // WKSMissionItemGain or something. arg1 is the itemid. Doesn't trigger when the mission ends and you lose the item
            _needInventoryUpdate = true;
        }
    }

    private static readonly HashSet<uint> FishIdSet = [];

    public static uint ComputeCurrentBaitMoochId(uint currentId, uint? swimbaitId, bool isMooching, BiteContext biteContext) {
        if (swimbaitId.HasValue && swimbaitId.Value != 0)
            return swimbaitId.Value;
        if (FishIdSet.Count == 0) {
            foreach (var fish in GameRes.Fishes)
                FishIdSet.Add((uint)fish.Id);
        }
        if (FishIdSet.Contains(currentId))
            return currentId;
        if (isMooching && biteContext.LastCaughtFishId is { } lastId && lastId > 0 && FishIdSet.Contains(lastId))
            return lastId;
        return currentId;
    }

    private static unsafe (PlayerInfo.OpItemCounts Counts, PlayerInfo.OpInventoryStats Stats) CollectInventory() {
        var dict = new Dictionary<uint, int>();
        var freeSlots = 0;
        var reduceableFish = 0;
        try {
            var inv = InventoryManager.Instance();
            if (inv != null) {
                for (var i = 0; i < 4; i++) {
                    var container = inv->GetInventoryContainer((InventoryType)i);
                    if (container == null) continue;
                    for (var k = 0; k < container->Size; k++) {
                        var slot = container->GetInventorySlot(k);
                        if (slot == null || slot->ItemId == 0) continue;
                        var kind = slot->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality) ? ItemKind.Hq : slot->Flags.HasFlag(InventoryItem.ItemFlags.Collectable) ? ItemKind.Collectible : ItemKind.Normal;
                        var id = ItemUtil.GetRawId(slot->ItemId, kind);
                        dict[id] = dict.GetValueOrDefault(id, 0) + slot->Quantity;
                    }
                }

                foreach (var bag in InventoryType.Bags) {
                    foreach (var item in inv->GetInventoryItems(bag)) {
                        if (item.Value->ItemId == 0)
                            freeSlots++;
                        else if (IsReduceableFish(item))
                            reduceableFish++;
                    }
                }
            }
        }
        catch { }

        try {
            if (Player.Territory is { Value.TerritoryIntendedUse.RowId: 60 }) {
                var cosmopouch = ContentInventoryManager.Instance()->WKSInventoryProvider.Cosmopouch1;
                foreach (ref readonly var item in cosmopouch.WKSItems) {
                    if (item.WKSItemId == 0)
                        continue;
                    dict[item.WKSItemId] = item.WKSItemQuantity;
                }
            }
        }
        catch { }

        return (new PlayerInfo.OpItemCounts(dict), new PlayerInfo.OpInventoryStats(freeSlots, reduceableFish));
    }

    private static unsafe bool IsReduceableFish(Pointer<InventoryItem> item)
        => item.Value->Flags == InventoryItem.ItemFlags.Collectable && TryGetRow<Item>(item.Value->ItemId, out var row) && row.AetherialReduce > 0;
}
