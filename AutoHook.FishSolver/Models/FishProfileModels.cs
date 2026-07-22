namespace AutoHook.FishSolver.Models;

public sealed class FishRecord {
    public int ItemId { get; set; }
    public int HookType { get; set; }
    public int BiteType { get; set; }
    public int InitialBait { get; set; }
    public List<int> Mooches { get; set; } = [];
    public List<FishPredatorRecord> Predators { get; set; } = [];
    public List<int> SpotIds { get; set; } = [];
    public List<int> Weathers { get; set; } = [];
    public List<int> WeathersFrom { get; set; } = [];
    public double? Spawn { get; set; }
    public double? Duration { get; set; }
    public string? Time { get; set; }
    public int? MinGathering { get; set; }
    public bool Snagging { get; set; }
    public int MLure { get; set; }
    public int ALure { get; set; }
    public bool IsSpearFish { get; set; }
    public double BiteTimeMin { get; set; }
    public double BiteTimeMax { get; set; }
    public Dictionary<string, Dictionary<string, int>>? BiteTimeHistogram { get; set; }
    public RateTier RateTier { get; set; } = RateTier.Unknown;

    public sealed class FishPredatorRecord {
        public int ItemId { get; set; }
        public int Quantity { get; set; }
    }
}

public sealed class FishEligibility {
    public required int FishId { get; init; }
    public List<int> SpotIds { get; init; } = [];
    public int BaitId { get; init; }
    public double? SpawnHour { get; init; }
    public double? DurationHours { get; init; }
    public string? TimeRange { get; init; }
    public List<int> Weathers { get; init; } = [];
    public List<int> WeathersFrom { get; init; } = [];
    public int? MinGathering { get; init; }
    public bool SnaggingRequired { get; init; }
    public bool MLureEligible { get; init; }
    public bool ALureEligible { get; init; }
}

public sealed class FishSignals {
    public TugType Tug { get; init; }
    public HooksetType Hookset { get; init; }
    public double BiteTimeMin { get; init; }
    public double BiteTimeMax { get; init; }
    public Dictionary<int, BiteTimeRange> PerSpot { get; init; } = [];
}

public sealed class BiteTimeRange {
    public double Min { get; init; }
    public double Max { get; init; }
    public Dictionary<int, int>? Histogram { get; init; }
}

public sealed record FishAcquisition {
    public AcquisitionType Type { get; init; }
    public List<int> MoochChain { get; init; } = [];
    public List<IntuitionRequirement> Predators { get; init; } = [];
    public int? IntuitionDurationSec { get; init; }
}

public sealed class IntuitionRequirement {
    public int FishId { get; init; }
    public int Quantity { get; init; }
}

public sealed class PoolMember {
    public int FishId { get; init; }
    public TugType Tug { get; init; }
    public double BiteMin { get; init; }
    public double BiteMax { get; init; }
    public RateTier RateTier { get; init; }
}

public sealed record InferredTactics {
    public StrategyArchetype Archetype { get; init; }
    public PrepHoldMode HoldMode { get; init; }
    public int? SlapTargetFishId { get; init; }
    public List<TugType> HookOnlyTugs { get; init; } = [];
    public double? EarlyCancelSec { get; init; }
    public bool RequiresContinuousFishing { get; init; }
    public bool StowRodSafeDuringHold { get; init; }
}

public sealed record FishProfile {
    public required int FishId { get; init; }
    public FishEligibility Eligibility { get; init; } = null!;
    public FishSignals Signals { get; init; } = null!;
    public FishAcquisition Acquisition { get; init; } = null!;
    public List<PoolMember> PoolAtPrimarySpot { get; init; } = [];
    public InferredTactics Tactics { get; init; } = null!;
}

public sealed class PoolIndexEntry {
    public int SpotId { get; init; }
    public int BaitId { get; init; }
    public List<int> FishIds { get; init; } = [];
    public List<PoolMember> Members { get; set; } = [];
}

public sealed class FishKnowledgeBase {
    public Dictionary<int, FishProfile> FishById { get; init; } = [];
    public Dictionary<string, PoolIndexEntry> PoolsByKey { get; init; } = [];
    public List<FishOverride> Overrides { get; init; } = [];

    public static string PoolKey(int spotId, int baitId) => $"{spotId}:{baitId}";

    public PoolIndexEntry? GetPool(int spotId, int baitId) {
        PoolsByKey.TryGetValue(PoolKey(spotId, baitId), out var pool);
        return pool;
    }

    public FishProfile? GetFish(int fishId) {
        FishById.TryGetValue(fishId, out var fish);
        return fish;
    }
}

public sealed class FishOverride {
    public int FishId { get; set; }
    public StrategyArchetype? Archetype { get; set; }
    public PrepHoldMode? HoldMode { get; set; }
    public int? SlapTargetFishId { get; set; }
    public double? EarlyCancelSec { get; set; }
    public int? IntuitionDurationSec { get; set; }
}
