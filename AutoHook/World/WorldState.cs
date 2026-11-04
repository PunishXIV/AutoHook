using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.Framework;

namespace AutoHook.World;

public sealed class WorldState(ulong qpf, string gameVersion) : IPluginService {
    public ulong QPF = qpf;
    public string GameVersion = gameVersion;
    public FrameState Frame;

    public unsafe WorldState() : this((ulong)Framework.Instance()->PerformanceCounterFrequency, IDataManager.Get().GameData.Repositories["ffxiv"].Version) { }

    public TimeOnly EorzeaTime { get; set; }

    public readonly PlayerState Player = new();
    public readonly PartyState Party = new();
    public readonly RodState Fishing = new();
    public readonly SpearfishingState Spearfishing = new();
    public readonly OceanState Ocean = new();
    public readonly WksState WKS = new();

    public uint CurrentModifiedWeatherId;
    public uint CurrentWeatherId;
    public uint PreviousWeatherId;
    public uint NextWeatherId;
    public uint TerritoryId;

    public DateTime CurrentTime => Frame.Timestamp;
    public DateTime FutureTime(float deltaSeconds) => Frame.Timestamp.AddSeconds(deltaSeconds);

    public DateTime? FishingSessionStartedAt;

    public bool HasAnglersArtStacks(int amount) => Player.GetStatusStacks(IDs.Status.AnglersArt) >= amount;

    public bool BlocksFortune()
        => Player.HasStatus(IDs.Status.MakeshiftBait) || Player.HasStatus(IDs.Status.PrizeCatch) || Player.HasStatus(IDs.Status.AnglersFortune);

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

    public uint SwimbaitEvaluationFishId { get; set; } // fish id while evaluating swimbait slot conditions (0 = unset).

    public int GetSwimbaitCount() => Fishing.SwimbaitIds.Count(id => id != 0);
    public int GetSwimbaitCountForFish(uint fishId) => Fishing.SwimbaitIds.Count(id => id == fishId);
    public bool IsSwimbaitFull() => GetSwimbaitCount() >= 3;
    public bool IsSwimbaitEmpty() => GetSwimbaitCount() == 0;

    public bool IsMoochAvailable()
        => ActionAvailable(IDs.Actions.Mooch) || ActionAvailable(IDs.Actions.Mooch2);

    public bool IsCastAvailable()
        => ActionAvailable(IDs.Actions.Cast) && !Player.BlockCasting;

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

    public void Decide(DecisionContext context, bool success, string action, string? detail = null, string? preset = null) {
        var presetName = preset ?? Configuration.C.HookPresets.SelectedPreset?.PresetName ?? AutoHook.GlobalPresetName;
        Execute(new OpDecision(context, success, presetName, action, detail ?? ""));
    }

    public IEnumerable<Operation> CompareToInitial() {
        if (CurrentTime != default)
            yield return new OpFrameStart(Frame);
        if (EorzeaTime != default)
            yield return new OpEorzeaTime(EorzeaTime);
        if (TerritoryId != 0)
            yield return new OpTerritory(TerritoryId);
        if (CurrentModifiedWeatherId != 0 || CurrentWeatherId != 0 || PreviousWeatherId != 0 || NextWeatherId != 0)
            yield return new OpWeather(CurrentModifiedWeatherId, CurrentWeatherId, PreviousWeatherId, NextWeatherId);
        foreach (var o in Party.CompareToInitial())
            yield return o;
        foreach (var o in Player.CompareToInitial())
            yield return o;
        foreach (var o in Fishing.CompareToInitial())
            yield return o;
        foreach (var o in Spearfishing.CompareToInitial())
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

    public Event<OpTerritory> TerritoryChanged = new();
    public sealed record OpTerritory(uint TerritoryId) : Operation {
        protected override void Exec(WorldState ws) {
            ws.TerritoryId = TerritoryId;
            ws.TerritoryChanged.Fire(this);
        }

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
            => output.EmitFourCC("WTHR").Emit(CurrentModified).Emit(Current).Emit(Previous).Emit(Next);
    }

    // legacy combined op; only from older replay logs.
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

    public sealed record OpDecision(DecisionContext Context, bool Success, string PresetName, string Action, string Detail) : Operation {
        protected override void Exec(WorldState ws) { }

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("DECN")
                .Emit((byte)Context)
                .Emit(Success)
                .Emit(PresetName)
                .Emit(Action)
                .Emit(Detail);
    }

    public Event<OpBeganSession> BeganSession = new();
    public sealed record OpBeganSession() : Operation {
        protected override void Exec(WorldState ws) {
            ws.FishingSessionStartedAt = ws.CurrentTime;
            ws.BeganSession.Fire(this);
            foreach (var op in AchievementProgressSnapshot.Collect())
                ws.Execute(op);
        }

        public override void Write(Replay.ReplayOutput output) => output.EmitFourCC("FBGN");
    }

    public Event<OpEndedSession> EndedSession = new();
    public sealed record OpEndedSession() : Operation {
        protected override void Exec(WorldState ws) {
            ws.FishingSessionStartedAt = null;
            ws.EndedSession.Fire(this);
        }

        public override void Write(Replay.ReplayOutput output) => output.EmitFourCC("FEND");
    }

    public Event<SpearfishingState.OpSessionActive> SpearfishingSessionStarted = new();
    public Event<SpearfishingState.OpEndSession> SpearfishingSessionEnded = new();

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

    public readonly Dictionary<uint, (uint Current, uint Max)> AchievementProgress = [];
    public Event<OpAchievementProgress> AchievementProgressReceived = new();
    public sealed record OpAchievementProgress(uint Id, uint Current, uint Max) : Operation {
        protected override void Exec(WorldState ws) {
            ws.AchievementProgress[Id] = (Current, Max);
            ws.AchievementProgressReceived.Fire(this);
        }

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("ACHP").Emit(Id).Emit(Current).Emit(Max);
    }
}
