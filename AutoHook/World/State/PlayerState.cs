
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoHook.World.State;

public sealed class PlayerState {
    public const int NumCooldownGroups = 87;

    public uint CurrentGp;
    public uint MaxGp;
    public byte Level;
    public readonly Dictionary<(uint Seq, byte TargetIndex), int> PendingGp = [];

    public bool HasPendingGp => PendingGp.Count > 0;

    public readonly Dictionary<uint, (float Time, int Stacks)> Statuses = [];
    public readonly Dictionary<uint, int> ItemCounts = [];
    public readonly Dictionary<ulong, uint> ActionStatus = [];
    public readonly Dictionary<ulong, int> ActionRecastGroup = [];

    public bool DutyActionManagerActive;
    public readonly Dictionary<uint, ushort> DutyActionCharges = [];

    public int FreeInventorySlots;
    public int ReduceableFishCount;

    public bool BlockCasting;
    public bool IsPotOffCooldown;
    public readonly Cooldown[] Cooldowns = new Cooldown[NumCooldownGroups];

    public static ulong ActionKey(ActionType type, uint id) => ((ulong)(byte)type << 32) | id;

    public uint GetActionStatus(ActionType type, uint id)
        => ActionStatus.TryGetValue(ActionKey(type, id), out var status) ? status : 0;
    public int GetRecastGroup(ActionType type, uint id) => ActionRecastGroup.TryGetValue(ActionKey(type, id), out var g) ? g : -1;

    public int GetItemCount(uint itemId) => ItemCounts.TryGetValue(itemId, out var c) ? c : 0;
    public bool HasItem(uint itemId) => GetItemCount(itemId) > 0;
    public bool HaveCordialInInventory(uint id) => HasItem(id);

    public bool HasStatus(uint statusId) => Statuses.ContainsKey(statusId);
    public float GetStatusTime(uint statusId) => Statuses.TryGetValue(statusId, out var t) ? t.Time : 0f;
    public int GetStatusStacks(uint statusId) => Statuses.TryGetValue(statusId, out var s) ? s.Stacks : 0;

    public void Tick(float dt) {
        if (dt <= 0f)
            return;

        foreach (ref var cd in Cooldowns.AsSpan()) {
            if (cd.Total <= 0f)
                continue;
            cd.Elapsed += dt;
            if (cd.Elapsed >= cd.Total)
                cd = default;
        }

        var keys = Statuses.Keys.ToArray();
        foreach (var id in keys) {
            var (time, stacks) = Statuses[id];
            if (time <= 0f)
                continue;
            time = Math.Max(0f, time - dt);
            Statuses[id] = (time, stacks);
        }
    }

    public IEnumerable<WorldState.Operation> CompareToInitial() {
        if (CurrentGp != 0 || MaxGp != 0)
            yield return new OpGp(CurrentGp, MaxGp);
        if (Level != 0)
            yield return new OpLevel(Level);
        if (Statuses.Count != 0)
            yield return new OpStatuses(Statuses);
        if (ItemCounts.Count != 0)
            yield return new OpItemCounts(ItemCounts);
        if (FreeInventorySlots != 0 || ReduceableFishCount != 0)
            yield return new OpInventoryStats(FreeInventorySlots, ReduceableFishCount);
        if (IsPotOffCooldown)
            yield return new OpPotCooldown(IsPotOffCooldown);
        if (BlockCasting)
            yield return new WorldState.OpSetBlockCasting(BlockCasting);

        var cooldowns = Cooldowns.Select((v, i) => (i, v)).Where(iv => iv.v.Total > 0).ToList();
        if (cooldowns.Count > 0)
            yield return new OpCooldown(false, cooldowns);
        if (ActionStatus.Count > 0 || ActionRecastGroup.Count > 0)
            yield return new OpActionStates(ActionStatus, ActionRecastGroup);
        if (DutyActionManagerActive || DutyActionCharges.Count > 0)
            yield return new OpDutyActions(DutyActionManagerActive, DutyActionCharges);
    }

    public Event<uint, uint> GpChanged = new();
    public sealed record OpGp(uint Current, uint Max) : WorldState.Operation {
        protected override void Exec(WorldState ws) {
            var previous = ws.Player.CurrentGp;
            ws.Player.CurrentGp = Current;
            ws.Player.MaxGp = Max;
            if (previous != Current)
                ws.Player.GpChanged.Fire(previous, Current);
        }

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("GP  ").Emit(Current).Emit(Max);
    }

    public sealed record OpLevel(byte Level) : WorldState.Operation {
        protected override void Exec(WorldState ws) => ws.Player.Level = Level;

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("LVL ").Emit(Level);
    }

    public sealed record OpStatuses(IReadOnlyDictionary<uint, (float Time, int Stacks)> Statuses) : WorldState.Operation {
        protected override void Exec(WorldState ws) {
            var hadSurfaceSlap = ws.Player.HasStatus(IDs.Status.SurfaceSlap);
            ws.Player.Statuses.Clear();
            foreach (var kv in Statuses)
                ws.Player.Statuses[kv.Key] = kv.Value;
            if (hadSurfaceSlap && !ws.Player.HasStatus(IDs.Status.SurfaceSlap))
                ws.Fishing.SlappedFishId = 0;
        }

        public override void Write(Replay.ReplayOutput output) {
            output.EmitFourCC("STAT");
            output.Emit((ushort)Statuses.Count);
            foreach (var (id, (time, stacks)) in Statuses)
                output.Emit(id).Emit(time).Emit(stacks);
        }
    }

    public sealed record OpItemCounts(IReadOnlyDictionary<uint, int> Counts) : WorldState.Operation {
        protected override void Exec(WorldState ws) {
            ws.Player.ItemCounts.Clear();
            foreach (var kv in Counts)
                ws.Player.ItemCounts[kv.Key] = kv.Value;
        }

        public override void Write(Replay.ReplayOutput output) {
            output.EmitFourCC("INVT");
            output.Emit((ushort)Counts.Count);
            foreach (var (id, count) in Counts)
                output.Emit(id).Emit(count);
        }
    }

    public sealed record OpInventoryStats(int FreeSlots, int ReduceableFish) : WorldState.Operation {
        protected override void Exec(WorldState ws) {
            ws.Player.FreeInventorySlots = FreeSlots;
            ws.Player.ReduceableFishCount = ReduceableFish;
        }

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("INVS").Emit(FreeSlots).Emit(ReduceableFish);
    }

    public sealed record OpPotCooldown(bool OffCooldown) : WorldState.Operation {
        protected override void Exec(WorldState ws) => ws.Player.IsPotOffCooldown = OffCooldown;

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("POT ").Emit(OffCooldown);
    }

    public sealed record OpCooldown(bool Reset, List<(int Group, Cooldown Value)> Changes) : WorldState.Operation {
        protected override void Exec(WorldState ws) {
            if (Reset)
                Array.Fill(ws.Player.Cooldowns, default);
            foreach (var (group, value) in Changes)
                ws.Player.Cooldowns[group] = value;
        }

        public override void Write(Replay.ReplayOutput output) {
            output.EmitFourCC("CLCD");
            output.Emit(Reset);
            output.Emit((byte)Changes.Count);
            foreach (var (group, value) in Changes)
                output.Emit((byte)group).Emit(value.Elapsed).Emit(value.Total);
        }
    }

    public sealed record OpActionStates(IReadOnlyDictionary<ulong, uint> Statuses, IReadOnlyDictionary<ulong, int> RecastGroups) : WorldState.Operation {
        protected override void Exec(WorldState ws) {
            ws.Player.ActionStatus.Clear();
            foreach (var (key, status) in Statuses)
                ws.Player.ActionStatus[key] = status;
            ws.Player.ActionRecastGroup.Clear();
            foreach (var (key, group) in RecastGroups)
                ws.Player.ActionRecastGroup[key] = group;
        }

        public override void Write(Replay.ReplayOutput output) {
            output.EmitFourCC("ACTS");
            output.Emit((ushort)Statuses.Count);
            foreach (var (key, status) in Statuses) {
                output.Emit(key).Emit(status);
                output.Emit(RecastGroups.TryGetValue(key, out var group) ? group : -1);
            }
        }
    }

    public sealed record OpDutyActions(bool ManagerActive, IReadOnlyDictionary<uint, ushort> Charges) : WorldState.Operation {
        protected override void Exec(WorldState ws) {
            ws.Player.DutyActionManagerActive = ManagerActive;
            ws.Player.DutyActionCharges.Clear();
            foreach (var (id, charges) in Charges)
                ws.Player.DutyActionCharges[id] = charges;
        }

        public override void Write(Replay.ReplayOutput output) {
            output.EmitFourCC("DTYA");
            output.Emit(ManagerActive);
            output.Emit((byte)Charges.Count);
            foreach (var (id, charges) in Charges)
                output.Emit(id).Emit(charges);
        }
    }
}
