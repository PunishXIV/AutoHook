namespace AutoHook.FishSolver.Models;

public sealed class PrepStep {
    public PrepPhaseKind Phase { get; init; }
    public PrepActionKind Action { get; init; }
    public int? FishId { get; init; }
    public int? Count { get; init; }
    public int EstimatedSeconds { get; init; }
}

public sealed class PrepPhasePlan {
    public PrepHoldMode HoldMode { get; init; }
    public bool RequiresContinuousFishing { get; init; }
    public bool StowRodSafeDuringHold { get; init; }
    public List<PrepStep> Steps { get; init; } = [];
    public int PrepTotalSeconds { get; init; }
}

public sealed class WindowSyncPlan {
    public PrepActionKind Action { get; init; }
    public string Timing { get; init; } = "AtWindowOpen";
    public int EstimatedSeconds { get; init; }
}

public sealed class CastDecisionRule {
    public TugType? WhenTug { get; init; }
    public double? AfterSecondsMin { get; init; }
    public double? AfterSecondsMax { get; init; }
    public double? BeforeSecondsMax { get; init; }
    public HookActionKind Action { get; init; }
}

public sealed class ResourcePolicy {
    public List<FisherSkill> GpPriority { get; init; } = [];
    public bool UseChum { get; init; }
    public bool UseCordials { get; init; }
    public int GpReserve { get; init; }
}

public sealed class SolverOutput {
    public int TargetFishId { get; init; }
    public int BaitId { get; init; }
    public List<int> SpotIds { get; init; } = [];
    public RouteVariant RouteVariant { get; init; }
    public PlayerProfile PlayerProfileUsed { get; init; } = null!;
    public StrategyArchetype Archetype { get; init; }
    public PrepHoldMode HoldMode { get; init; }
    public int? SlapTargetFishId { get; init; }
    public double? EarlyCancelSec { get; init; }
    public PrepPhasePlan PrepPhase { get; init; } = null!;
    public WindowSyncPlan? WindowSync { get; init; }
    public List<CastDecisionRule> CastPolicy { get; init; } = [];
    public ResourcePolicy ResourcePolicy { get; init; } = null!;
    public int ArriveEarlySeconds { get; init; }
    public List<string> MissingSkillsFallbacks { get; init; } = [];
}

public sealed class SolverRequest {
    public required int TargetFishId { get; init; }
    public required PlayerProfile Profile { get; init; }

    // if set and valid for the fish, listed first in SpotIds (hole you're standing at / prefer)
    public int? PreferredSpotId { get; init; }
}
