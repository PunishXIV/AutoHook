using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;

namespace AutoHook.Data;

public sealed class WorldState(ulong qpf, string gameVersion) {
    public ulong QPF = qpf;
    public string GameVersion = gameVersion;
    public FrameState Frame;

    public TimeOnly EorzeaTime { get; set; }

    public readonly PlayerInfo Player = new();
    public readonly FishingInfo Fishing = new();
    public readonly OceanFishInfo Ocean = new();
    public readonly WKSInfo WKS = new();

    public uint CurrentModifiedWeatherId;
    public uint CurrentWeatherId;
    public uint PreviousWeatherId;
    public uint NextWeatherId;
    public uint TerritoryId;

    public DateTime CurrentTime => Frame.Timestamp;
    public DateTime FutureTime(float deltaSeconds) => Frame.Timestamp.AddSeconds(deltaSeconds);

    public uint CurrentGp => Player.CurrentGp;
    public uint MaxGp => Player.MaxGp;
    public byte Level => Player.Level;
    public bool BlockCasting => Player.BlockCasting;

    public IReadOnlyDictionary<uint, (float Time, int Stacks)> Statuses => Player.Statuses;
    public bool HasStatus(uint statusId) => Player.HasStatus(statusId);
    public float GetStatusTime(uint statusId) => Player.GetStatusTime(statusId);
    public int GetStatusStacks(uint statusId) => Player.GetStatusStacks(statusId);
    public bool HasAnyStatus(uint[] statusIds) => Player.HasAnyStatus(statusIds);

    public bool HasAnglersArtStacks(int amount) => GetStatusStacks(IDs.Status.AnglersArt) >= amount;

    public bool BlocksFortune()
        => HasStatus(IDs.Status.MakeshiftBait)
           || HasStatus(IDs.Status.PrizeCatch)
           || HasStatus(IDs.Status.AnglersFortune);

    public bool ActionAvailable(uint actionId, ActionType actionType = ActionType.Action)
        => Player.GetActionStatus(actionType, actionId) == 0 && !ActionOnCooldown(actionId, actionType);

    public bool ActionOnCooldown(uint actionId, ActionType actionType = ActionType.Action) {
        var group = Player.GetRecastGroup(actionType, actionId);
        if (group < 0 || group >= Player.Cooldowns.Length)
            return false;
        var cd = Player.Cooldowns[group];
        return cd.Total > 0f && cd.Remaining > 0f;
    }

    public float GetCooldownRemaining(uint actionId, ActionType actionType = ActionType.Action) {
        var group = Player.GetRecastGroup(actionType, actionId);
        if (group < 0 || group >= Player.Cooldowns.Length)
            return 0f;
        var cd = Player.Cooldowns[group];
        return cd.Total > 0f ? Math.Max(0f, cd.Remaining) : 0f;
    }

    public int GetCooldownSeconds(uint actionId, ActionType actionType = ActionType.Action) {
        var remaining = GetCooldownRemaining(actionId, actionType);
        return remaining <= 0f ? 0 : (int)Math.Ceiling(remaining);
    }

    public bool HasDutyActionCharges(uint actionId)
        => Player.DutyActionCharges.TryGetValue(actionId, out var charges) && charges > 0;

    public bool IsSlottedDutyActionReady(uint actionId, ActionType actionType = ActionType.Action)
        => Player.DutyActionManagerActive && HasDutyActionCharges(actionId) && ActionAvailable(actionId, actionType);

    public int GetItemCount(uint itemId) => Player.GetItemCount(itemId);

    public bool HasItem(uint itemId) => Player.HasItem(itemId);
    public bool HaveCordialInInventory(uint id) => Player.HaveCordialInInventory(id);

    public IReadOnlyList<uint> SwimbaitIds => Fishing.SwimbaitIds;

    /// <summary>Fish id used while evaluating swimbait slot conditions (0 = unset).</summary>
    public uint SwimbaitEvaluationFishId { get; set; }

    public int GetSwimbaitCount() => Fishing.SwimbaitIds.Count(id => id != 0);
    public int GetSwimbaitCountForFish(uint fishId) => Fishing.SwimbaitIds.Count(id => id == fishId);
    public bool IsSwimbaitFull() => GetSwimbaitCount() >= 3;
    public bool IsSwimbaitEmpty() => GetSwimbaitCount() == 0;

    public FishingState FishingState => Fishing.FishingState;
    public FishingState PreviousFishingState => Fishing.PreviousFishingState;
    public FishingSteps FishingStep => Fishing.FishingStep;

    public bool ChumActive => Fishing.ChumActive;
    public bool LureSuccess => Fishing.LureSuccess;
    public int GetFishCaughtCount(uint fishId) => Fishing.GetFishCaughtCount(fishId);

    public bool IsMoochAvailable()
        => ActionAvailable(IDs.Actions.Mooch) || ActionAvailable(IDs.Actions.Mooch2);

    public bool IsCastAvailable()
        => ActionAvailable(IDs.Actions.Cast) && !BlockCasting;

    public bool HasMultihookAvailable()
        => ActionAvailable(IDs.Actions.MultiHook, ActionType.Action);

    public bool IsStellarHooksetAvailable()
        => GetAvailableStellarHooksetId() is not null;

    public uint? GetAvailableStellarHooksetId() {
        if (ActionAvailable(IDs.Actions.StellarHookMaster)) {
            if (Player.DutyActionManagerActive) {
                if (HasDutyActionCharges(IDs.Actions.StellarHookMaster))
                    return IDs.Actions.StellarHookMaster;
            }
            else
                return IDs.Actions.StellarHookMaster;
        }

        return ActionAvailable(IDs.Actions.StellarHook) ? IDs.Actions.StellarHook : null;
    }

    public OceanFishingState OceanFishing => Ocean.OceanFishing;
    public SpectralCurrentStatus SpectralCurrentStatus => Ocean.SpectralCurrentStatus;
    public OceanSpectralTimerInfo SpectralTimer => Ocean.SpectralTimer;
    public float SpectralTimeRemaining => Ocean.SpectralTimer.TimeRemaining;
    public IReadOnlyList<ZoneSpectralRecord> SpectralHistory => Ocean.SpectralHistory;

    public event Action<Operation>? Modified;

    public abstract record Operation {
        public DateTime Timestamp { get; internal set; }

        internal void Execute(WorldState ws) {
            Exec(ws);
            Timestamp = ws.CurrentTime;
            ws.Modified?.Invoke(this);
        }

        protected abstract void Exec(WorldState ws);
        public abstract void Write(Replay.ReplayOutput output);
    }

    public void Execute(Operation op) => op.Execute(this);

    public void LogDecision(string context, string presetName, string action, IReadOnlyList<(string ConditionId, bool Result)>? conditionResults = null, string detail = "")
        => Execute(new OpDecision(context, presetName, action, detail, conditionResults ?? []));

    public IEnumerable<Operation> CompareToInitial() {
        if (CurrentTime != default)
            yield return new OpFrameStart(Frame);
        if (EorzeaTime != default)
            yield return new OpEorzeaTime(EorzeaTime);
        if (TerritoryId != 0)
            yield return new OpTerritory(TerritoryId);
        if (CurrentModifiedWeatherId != 0 || CurrentWeatherId != 0 || PreviousWeatherId != 0 || NextWeatherId != 0)
            yield return new OpWeather(CurrentModifiedWeatherId, CurrentWeatherId, PreviousWeatherId, NextWeatherId);
        foreach (var o in Player.CompareToInitial())
            yield return o;
        foreach (var o in Fishing.CompareToInitial())
            yield return o;
        foreach (var o in Ocean.CompareToInitial())
            yield return o;
        foreach (var o in WKS.CompareToInitial())
            yield return o;
    }

    public sealed record OpFrameStart(FrameState Frame) : Operation {
        protected override void Exec(WorldState ws) {
            ws.Frame = Frame;
            ws.Player.Tick(Frame.Duration);
        }

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("FRAM")
                .Emit(Frame.QPC)
                .Emit(Frame.Index)
                .Emit(Frame.DurationRaw)
                .Emit(Frame.Duration)
                .Emit(Frame.TickSpeedMultiplier);
    }

    public sealed record OpEorzeaTime(TimeOnly Time) : Operation {
        protected override void Exec(WorldState ws) => ws.EorzeaTime = Time;

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("EORZ").Emit(Time);
    }

    public sealed record OpTerritory(uint TerritoryId) : Operation {
        protected override void Exec(WorldState ws) => ws.TerritoryId = TerritoryId;

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("TRTY").Emit(TerritoryId);
    }

    public sealed record OpWeather(uint CurrentModified, uint Current, uint Previous, uint Next) : Operation {
        protected override void Exec(WorldState ws) {
            ws.CurrentModifiedWeatherId = CurrentModified;
            ws.CurrentWeatherId = Current;
            ws.PreviousWeatherId = Previous;
            ws.NextWeatherId = Next;
        }

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("WTHR").Emit(Current).Emit(Previous).Emit(Next);
    }

    /// <summary>Legacy combined op; only emitted by older replay logs.</summary>
    public sealed record OpZone(byte WeatherId, uint TerritoryId) : Operation {
        protected override void Exec(WorldState ws) {
            ws.CurrentWeatherId = WeatherId;
            ws.TerritoryId = TerritoryId;
        }

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("ZONE").Emit(WeatherId).Emit(TerritoryId);
    }

    public sealed record OpSetBlockCasting(bool Block) : Operation {
        protected override void Exec(WorldState ws) => ws.Player.BlockCasting = Block;

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("BLKC").Emit(Block);
    }

    public sealed record OpSetFishingStep(FishingSteps Step, bool Or = false) : Operation {
        protected override void Exec(WorldState ws)
            => ws.Fishing.FishingStep = Or ? ws.Fishing.FishingStep | Step : Step;

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("FSTP").Emit((uint)Step).Emit(Or);
    }

    public sealed record OpClearFishingStepFlag(FishingSteps Flag) : Operation {
        protected override void Exec(WorldState ws) => ws.Fishing.FishingStep &= ~Flag;

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("FCLR").Emit((uint)Flag);
    }

    public sealed record OpDecision(string Context, string PresetName, string Action, string Detail, IReadOnlyList<(string ConditionId, bool Result)> ConditionResults) : Operation {
        protected override void Exec(WorldState ws) { }

        public override void Write(Replay.ReplayOutput output) {
            output.EmitFourCC("DECN").Emit(Context).Emit(PresetName).Emit(Action).Emit(Detail).Emit(ConditionResults.Count);
            foreach (var (id, result) in ConditionResults)
                output.Emit(id).Emit(result);
        }
    }

    public Event<OpBeganSession> BeganSession = new();
    public sealed record OpBeganSession() : Operation {
        protected override void Exec(WorldState ws) => ws.BeganSession.Fire(this);

        public override void Write(Replay.ReplayOutput output) => output.EmitFourCC("FBGN");
    }

    public Event<OpEndedSession> EndedSession = new();
    public sealed record OpEndedSession() : Operation {
        protected override void Exec(WorldState ws) => ws.EndedSession.Fire(this);

        public override void Write(Replay.ReplayOutput output) => output.EmitFourCC("FEND");
    }

    public Event<OpOceanZoneStarted> OceanZoneStarted = new();
    public sealed record OpOceanZoneStarted(uint ZoneIndex) : Operation {
        protected override void Exec(WorldState ws) => ws.OceanZoneStarted.Fire(this);

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("OZON").Emit(ZoneIndex);
    }

    public Event<OpSpectralCurrentChanged> SpectralCurrentChanged = new();
    public sealed record OpSpectralCurrentChanged(SpectralCurrentChange Change) : Operation {
        protected override void Exec(WorldState ws) => ws.SpectralCurrentChanged.Fire(this);

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("SPCH").Emit((byte)Change);
    }
}
