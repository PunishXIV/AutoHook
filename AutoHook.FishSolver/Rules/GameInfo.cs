using AutoHook.FishSolver.Models;

namespace AutoHook.FishSolver.Rules;

public sealed class BuffPersistenceRules {
    public List<StowState> PreservedOnStow { get; init; } = [];
    public bool IntuitionProgressResetsOnZoneLeave { get; init; }
    public AmissRules Amiss { get; init; } = new();

    public sealed class AmissRules {
        public int NormalWarningMinutes { get; init; } = 47;
        public int NormalAmissMinutes { get; init; } = 50;
        public int SuperAmissWarningCasts { get; init; } = 490;
        public int SuperAmissCasts { get; init; } = 500;
    }

    public bool IsPreservedOnStow(StowState state) => PreservedOnStow.Contains(state);
    public bool IsLostOnStow(StowState state) => !IsPreservedOnStow(state);

    public static BuffPersistenceRules Default { get; } = new() {
        PreservedOnStow =
        [
            StowState.IntuitionProgress,
            StowState.IntuitionBuff,
            StowState.AnglersArt,
            StowState.MakeshiftBait,
        ],
        IntuitionProgressResetsOnZoneLeave = true,
        Amiss = new AmissRules(),
    };
}

public sealed class GpRegenRules {
    public int BaseGpPerTick { get; init; }
    public int TickIntervalSeconds { get; init; }
    public List<int> TraitBonusLevels { get; init; } = [];
    public int TraitBonusPerLevel { get; init; }
    public int CordialUseSeconds { get; init; }

    public IReadOnlyList<Cordial> Cordials { get; init; } = [];

    public Cordial this[CordialKind kind] => Cordials.First(c => c.Kind == kind);

    public int GpPerTick(int level) {
        var bonus = TraitBonusLevels.Count(traitLevel => level >= traitLevel) * TraitBonusPerLevel;
        return BaseGpPerTick + bonus;
    }

    public double RegenPerSecond(int level) => (double)GpPerTick(level) / Math.Max(TickIntervalSeconds, 1);

    public double RegenPerMinute(int level) => RegenPerSecond(level) * 60;

    // time to recover gp with cordials + natural regen
    public int EstimateRegenSeconds(int gpDeficit, int level, IReadOnlyDictionary<CordialKind, int>? inventory, bool useCordials) {
        if (gpDeficit <= 0)
            return 0;

        var remaining = gpDeficit;
        var seconds = 0;
        var left = Cordials.ToDictionary(
            c => c.Kind,
            c => inventory == null ? int.MaxValue : inventory.GetValueOrDefault(c.Kind));

        if (useCordials) {
            // prefer highest GP restore
            var order = Cordials.OrderByDescending(c => c.GpRestore).ToList();
            Cordial? lastUsed = null;
            while (remaining > 0) {
                Cordial? pick = null;
                foreach (var c in order) {
                    if (left[c.Kind] > 0) {
                        pick = c;
                        break;
                    }
                }
                if (pick is null)
                    break;

                // first use null; later uses wait out the previous cordial's own CD
                seconds += lastUsed is null ? CordialUseSeconds : lastUsed.Value.CooldownSeconds;
                remaining = Math.Max(0, remaining - pick.Value.GpRestore);
                lastUsed = pick;
                if (left[pick.Value.Kind] != int.MaxValue)
                    left[pick.Value.Kind]--;
            }
        }

        if (remaining > 0)
            seconds += (int)Math.Ceiling(remaining / Math.Max(RegenPerSecond(level), 0.1));

        return seconds;
    }

    public static GpRegenRules Default { get; } = new() {
        BaseGpPerTick = 5,
        TickIntervalSeconds = 3,
        TraitBonusLevels = [70, 80, 83],
        TraitBonusPerLevel = 1,
        CordialUseSeconds = 3,
        Cordials =
        [
            new(CordialKind.WateredCordial, GpRestore: 150, CooldownSeconds: 140),
            new(CordialKind.WateredCordialHq, GpRestore: 200, CooldownSeconds: 126),
            new(CordialKind.Cordial, GpRestore: 300, CooldownSeconds: 240),
            new(CordialKind.CordialHq, GpRestore: 350, CooldownSeconds: 216),
            new(CordialKind.HiCordial, GpRestore: 400, CooldownSeconds: 180),
        ],
    };
}

public readonly record struct Cordial(CordialKind Kind, int GpRestore, int CooldownSeconds);

public sealed class SkillUnlockTable {
    public Dictionary<FisherSkill, int> Unlocks { get; init; } = [];

    public bool IsUnlocked(FisherSkill skill, int level)
        => Unlocks.TryGetValue(skill, out var req) && level >= req;

    public PlayerSkills BuildSkillsForLevel(int level) => new() {
        SurfaceSlap = IsUnlocked(FisherSkill.SurfaceSlap, level),
        IdenticalCast = IsUnlocked(FisherSkill.IdenticalCast, level),
        DoubleHook = IsUnlocked(FisherSkill.DoubleHook, level),
        TripleHook = IsUnlocked(FisherSkill.TripleHook, level),
        MoochII = IsUnlocked(FisherSkill.MoochII, level),
        PatienceII = IsUnlocked(FisherSkill.PatienceII, level),
        PatienceI = IsUnlocked(FisherSkill.PatienceI, level),
        PrizeCatch = IsUnlocked(FisherSkill.PrizeCatch, level),
        MakeshiftBait = IsUnlocked(FisherSkill.MakeshiftBait, level),
        ThaliaksFavor = IsUnlocked(FisherSkill.ThaliaksFavor, level),
        SparefulHand = IsUnlocked(FisherSkill.SparefulHand, level),
        AmbitiousLure = IsUnlocked(FisherSkill.AmbitiousLure, level),
        ModestLure = IsUnlocked(FisherSkill.ModestLure, level),
        BigGameFishing = IsUnlocked(FisherSkill.BigGameFishing, level),
        FishEyes = IsUnlocked(FisherSkill.FishEyes, level),
        CollectorsGlove = IsUnlocked(FisherSkill.CollectorsGlove, level),
    };

    public static SkillUnlockTable Default { get; } = new() {
        Unlocks = new Dictionary<FisherSkill, int> {
            [FisherSkill.PatienceI] = 15,
            [FisherSkill.FishEyes] = 50,
            [FisherSkill.CollectorsGlove] = 50,
            [FisherSkill.MoochII] = 63,
            [FisherSkill.PatienceII] = 68,
            [FisherSkill.SurfaceSlap] = 71,
            [FisherSkill.IdenticalCast] = 74,
            [FisherSkill.DoubleHook] = 74,
            [FisherSkill.PrizeCatch] = 81,
            [FisherSkill.MakeshiftBait] = 86,
            [FisherSkill.ThaliaksFavor] = 86,
            [FisherSkill.TripleHook] = 88,
            [FisherSkill.SparefulHand] = 91,
            [FisherSkill.AmbitiousLure] = 100,
            [FisherSkill.ModestLure] = 100,
            [FisherSkill.BigGameFishing] = 100,
            [FisherSkill.Chum] = 5,
        },
    };
}
