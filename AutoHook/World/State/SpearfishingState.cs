using FishInfo = FFXIVClientStructs.FFXIV.Client.UI.AddonSpearFishing.FishInfo;

namespace AutoHook.World.State;

public readonly record struct SpearfishingSpotState(uint GatheringPointId, uint GatheringPointBaseId, uint NotebookId, bool IsShadowNode) {
    public static SpearfishingSpotState Empty => default;
    public bool IsEmpty => GatheringPointId == 0 && GatheringPointBaseId == 0 && NotebookId == 0;
}

public readonly record struct SpearLaneLayout(float LaneX, float LaneY, float LaneWidth, float LaneHeight, float LaneScaleX, float UiScale) {
    public static SpearLaneLayout Empty => default;
    public bool IsEmpty => LaneWidth == 0 && LaneHeight == 0 && UiScale == 0;
}

public readonly record struct SpearFishLayout(FishInfo Fish, float FishNodeX, float FishNodeWidth, float FishNodeScaleX) {
    public static SpearFishLayout Empty => default;
    public bool Available => Fish.Available;
}

public sealed class SpearfishingState {
    public bool WindowOpen;
    public bool SessionActive;
    public int Wariness;
    public int WarinessMax;

    public SpearfishingSpotState Spot;
    public FishInfo Lane0;
    public FishInfo Lane1;
    public FishInfo Lane2;
    public SpearLaneLayout LaneLayout;
    public SpearFishLayout FishLayout0;
    public SpearFishLayout FishLayout1;
    public SpearFishLayout FishLayout2;

    public readonly Dictionary<uint, int> FishCaughtCounts = [];
    public uint LastCatchFishId;
    public byte LastCatchAmount;

    public int GetFishCaughtCount(uint fishId) => FishCaughtCounts.TryGetValue(fishId, out var c) ? c : 0;

    public int FishOnScreenCount
        => (Lane0.Available ? 1 : 0) + (Lane1.Available ? 1 : 0) + (Lane2.Available ? 1 : 0);

    public SpearFishLayout GetFishLayout(int index) => index switch {
        0 => FishLayout0,
        1 => FishLayout1,
        2 => FishLayout2,
        _ => default,
    };

    public IEnumerable<WorldState.Operation> CompareToInitial() {
        if (WindowOpen || Wariness != 0 || WarinessMax != 0)
            yield return new OpHud(WindowOpen, Wariness, WarinessMax);
        if (SessionActive)
            yield return new OpSessionActive(true);
        if (!Spot.IsEmpty)
            yield return new OpSpot(Spot);
        if (Lane0.Available || Lane1.Available || Lane2.Available || !LaneLayout.IsEmpty)
            yield return new OpFishLayout(LaneLayout, FishLayout0, FishLayout1, FishLayout2);
        foreach (var (fishId, count) in FishCaughtCounts) {
            if (fishId == 0 || count <= 0)
                continue;
            var remaining = count;
            while (remaining > 0) {
                var amount = (byte)Math.Min(remaining, byte.MaxValue);
                yield return new OpAddFishCaught(fishId, amount);
                remaining -= amount;
            }
        }
        if (LastCatchFishId > 0 && LastCatchAmount > 0)
            yield return new OpSetLastCatch(LastCatchFishId, LastCatchAmount);
    }

    public sealed record OpHud(bool WindowOpen, int Wariness, int WarinessMax) : WorldState.Operation {
        protected override void Exec(WorldState ws) {
            ws.Spearfishing.WindowOpen = WindowOpen;
            ws.Spearfishing.Wariness = Wariness;
            ws.Spearfishing.WarinessMax = WarinessMax;
        }

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("SPFN")
                .Emit(WindowOpen)
                .Emit(Wariness)
                .Emit(WarinessMax);
    }

    public sealed record OpSessionActive(bool Active) : WorldState.Operation {
        protected override void Exec(WorldState ws) {
            var wasActive = ws.Spearfishing.SessionActive;
            ws.Spearfishing.SessionActive = Active;
            if (Active && !wasActive)
                ws.SpearfishingSessionStarted.Fire(this);
        }

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("SPSA").Emit(Active);
    }

    public sealed record OpSpot(SpearfishingSpotState Spot) : WorldState.Operation {
        protected override void Exec(WorldState ws) => ws.Spearfishing.Spot = Spot;

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("SPST")
                .Emit(Spot.GatheringPointId)
                .Emit(Spot.GatheringPointBaseId)
                .Emit(Spot.NotebookId)
                .Emit(Spot.IsShadowNode);
    }

    public sealed record OpFishLanes(FishInfo Lane0, FishInfo Lane1, FishInfo Lane2) : WorldState.Operation {
        protected override void Exec(WorldState ws) {
            ws.Spearfishing.Lane0 = Lane0;
            ws.Spearfishing.Lane1 = Lane1;
            ws.Spearfishing.Lane2 = Lane2;
        }

        public override void Write(Replay.ReplayOutput output) {
            output.EmitFourCC("SPFL");
            WriteLane(output, Lane0);
            WriteLane(output, Lane1);
            WriteLane(output, Lane2);
        }

        private static void WriteLane(Replay.ReplayOutput output, FishInfo lane)
            => output.Emit(lane.Available)
                .Emit(lane.InverseDirection)
                .Emit(lane.GuaranteedLarge)
                .Emit((sbyte)lane.Size)
                .Emit(lane.Speed);
    }

    public sealed record OpFishLayout(SpearLaneLayout Lane, SpearFishLayout Fish0, SpearFishLayout Fish1, SpearFishLayout Fish2) : WorldState.Operation {
        protected override void Exec(WorldState ws) {
            var sf = ws.Spearfishing;
            sf.LaneLayout = Lane;
            sf.FishLayout0 = Fish0;
            sf.FishLayout1 = Fish1;
            sf.FishLayout2 = Fish2;
            sf.Lane0 = Fish0.Fish;
            sf.Lane1 = Fish1.Fish;
            sf.Lane2 = Fish2.Fish;
        }

        public override void Write(Replay.ReplayOutput output) {
            output.EmitFourCC("SPLO")
                .Emit(Lane.LaneX).Emit(Lane.LaneY).Emit(Lane.LaneWidth).Emit(Lane.LaneHeight).Emit(Lane.LaneScaleX).Emit(Lane.UiScale);
            WriteFish(output, Fish0);
            WriteFish(output, Fish1);
            WriteFish(output, Fish2);
        }

        private static void WriteFish(Replay.ReplayOutput output, SpearFishLayout fish)
            => output.Emit(fish.Fish.Available)
                .Emit(fish.Fish.InverseDirection)
                .Emit(fish.Fish.GuaranteedLarge)
                .Emit((sbyte)fish.Fish.Size)
                .Emit(fish.Fish.Speed)
                .Emit(fish.FishNodeX)
                .Emit(fish.FishNodeWidth)
                .Emit(fish.FishNodeScaleX);
    }

    public sealed record OpAddFishCaught(uint FishId, byte Amount) : WorldState.Operation {
        protected override void Exec(WorldState ws) {
            if (FishId <= 0 || Amount <= 0)
                return;
            ws.Spearfishing.FishCaughtCounts[FishId] = ws.Spearfishing.FishCaughtCounts.GetValueOrDefault(FishId) + Amount;
            ws.Spearfishing.LastCatchFishId = FishId;
            ws.Spearfishing.LastCatchAmount = Amount;
        }

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("SPFC").Emit(FishId).Emit(Amount);
    }

    public sealed record OpSetLastCatch(uint FishId, byte Amount) : WorldState.Operation {
        protected override void Exec(WorldState ws) {
            ws.Spearfishing.LastCatchFishId = FishId;
            ws.Spearfishing.LastCatchAmount = Amount;
        }

        public override void Write(Replay.ReplayOutput output)
            => output.EmitFourCC("SPLC").Emit(FishId).Emit(Amount);
    }

    public sealed record OpResetFishCaught() : WorldState.Operation {
        protected override void Exec(WorldState ws) {
            ws.Spearfishing.FishCaughtCounts.Clear();
            ws.Spearfishing.LastCatchFishId = 0;
            ws.Spearfishing.LastCatchAmount = 0;
        }

        public override void Write(Replay.ReplayOutput output) => output.EmitFourCC("SPRS");
    }

    public sealed record OpEndSession() : WorldState.Operation {
        protected override void Exec(WorldState ws) {
            var sf = ws.Spearfishing;
            sf.WindowOpen = false;
            sf.SessionActive = false;
            sf.Wariness = 0;
            sf.WarinessMax = 0;
            sf.Spot = SpearfishingSpotState.Empty;
            sf.Lane0 = default;
            sf.Lane1 = default;
            sf.Lane2 = default;
            sf.LaneLayout = default;
            sf.FishLayout0 = default;
            sf.FishLayout1 = default;
            sf.FishLayout2 = default;
            sf.LastCatchFishId = 0;
            sf.LastCatchAmount = 0;
            ws.SpearfishingSessionEnded.Fire(this);
        }

        public override void Write(Replay.ReplayOutput output) => output.EmitFourCC("SPES");
    }
}
