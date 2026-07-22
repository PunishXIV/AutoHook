using AutoHook.FishSolver.Rules;

namespace AutoHook.FishSolver.Models;

public sealed class PlayerSkills {
    public bool SurfaceSlap { get; set; }
    public bool IdenticalCast { get; set; }
    public bool DoubleHook { get; set; }
    public bool TripleHook { get; set; }
    public bool MoochII { get; set; }
    public bool PatienceII { get; set; }
    public bool PatienceI { get; set; } = true;
    public bool PrizeCatch { get; set; }
    public bool MakeshiftBait { get; set; }
    public bool ThaliaksFavor { get; set; }
    public bool SparefulHand { get; set; }
    public bool AmbitiousLure { get; set; }
    public bool ModestLure { get; set; }
    public bool BigGameFishing { get; set; }
    public bool FishEyes { get; set; }
    public bool CollectorsGlove { get; set; }
}

public sealed class PlayerAssumptions {
    public bool UseCordials { get; set; } = true;
    public bool CanAffordPatienceLoop { get; set; } = true;

    /// <summary>
    /// Bag counts per cordial grade. null = treat every grade as unlimited.
    /// Missing keys count as 0.
    /// </summary>
    public Dictionary<CordialKind, int>? CordialInventory { get; set; }
}

// these must be supplied by AH
public sealed class PlayerProfile {
    public int Level { get; set; }
    public int GpMax { get; set; }
    public PlayerSkills Skills { get; set; } = new();
    public PlayerAssumptions Assumptions { get; set; } = new();

    // 5 base + 1 each per trait. Tick rate is 3s
    public double GpRegenPerSecond => GpRegenRules.Default.RegenPerSecond(Level);

    public int GpPerTick => GpRegenRules.Default.GpPerTick(Level);

    public static PlayerProfile Create(int level, int gpMax, SkillUnlockTable? unlocks = null, Action<PlayerAssumptions>? configureAssumptions = null) {
        unlocks ??= SkillUnlockTable.Default;
        var assumptions = new PlayerAssumptions();
        configureAssumptions?.Invoke(assumptions);

        return new PlayerProfile {
            Level = level,
            GpMax = gpMax,
            Skills = unlocks.BuildSkillsForLevel(level),
            Assumptions = assumptions,
        };
    }

    // prep plans assume gp is full before the window
    public int EstimateGpRegenSeconds(int gpTarget, GpRegenRules? rules = null) {
        rules ??= GpRegenRules.Default;
        return rules.EstimateRegenSeconds(
            Math.Max(0, gpTarget),
            Level,
            Assumptions.UseCordials ? Assumptions.CordialInventory : new Dictionary<CordialKind, int>(),
            Assumptions.UseCordials);
    }
}
