using AutoHook.FishSolver.Models;

namespace AutoHook.FishSolver.Engine;

// pick the archeetype for a fish + how to hold until the window
//
// intuition:
// - few triggers (1-3) -> zero-time: last trigger at window open
// - many triggers (4+) -> rebuild: farm N-1, IC the last at 0s, release at open
//
// mooch (no intuition):
// - Spareful Hand -> bank swimbait; else hold a live mooch (don't stow)
// - multi-step chain -> same, longer fragile hold
//
// straight catch:
// - short bite vs long pool mates -> Rest / lure early (ShortBiteReset)
// - long !!! with a slower competitor -> slap the slow one (FailFaster)
// - else slap junk + chum (SlapAndChum)
// - several !!! + ALure eligible + skill -> LureStack
public static class FishArchetypeClassifier {
    public static InferredTactics Classify(FishProfile profile, PlayerProfile player) {
        var pool = profile.PoolAtPrimarySpot;
        var archetype = InferArchetype(profile, pool, player);
        var holdMode = PrepHoldModeSelector.Select(profile, player, archetype);

        // don't get a slap target if we don't have surface slap
        var slapTarget = player.Skills.SurfaceSlap ? SpotBaitPoolAnalyzer.RecommendSlapTarget(profile, pool) : null;
        var earlyCancel = SpotBaitPoolAnalyzer.ComputeEarlyCancelSec(profile, pool);
        var hookOnly = SpotBaitPoolAnalyzer.HookOnlyTugs(profile, pool);

        // most holds die on stow; CrossWindowJail is the exception
        var stowSafe = holdMode is PrepHoldMode.CrossWindowJail or PrepHoldMode.None && profile.Acquisition.Predators.Count > 0;
        return new InferredTactics {
            Archetype = archetype,
            HoldMode = holdMode,
            SlapTargetFishId = slapTarget,
            HookOnlyTugs = hookOnly,
            EarlyCancelSec = earlyCancel,
            RequiresContinuousFishing = PrepHoldModeSelector.RequiresContinuousFishing(holdMode),
            StowRodSafeDuringHold = stowSafe || holdMode == PrepHoldMode.CrossWindowJail,
        };
    }

    private static StrategyArchetype InferArchetype(FishProfile profile, IReadOnlyList<PoolMember> pool, PlayerProfile player) {
        // --- intuition ---
        // Predator list comes from Teamcraft. Quantity sum is a rough proxy for "how annoying is the rebuild".
        // >3 total fish-to-catch usually wants IC zero-time prep (Sea Butterfly 3+3, Problematicus 3+5, etc.).
        if (profile.Acquisition.Predators.Count > 0) {
            if (profile.Acquisition.Predators.Sum(p => p.Quantity) > 3)
                return StrategyArchetype.IntuitionRebuild;
            return StrategyArchetype.IntuitionZeroTime;
        }

        // --- mooch without intuition ---
        if (profile.Acquisition.MoochChain.Count > 1)
            return StrategyArchetype.MoochChain;

        if (profile.Acquisition.MoochChain.Count == 1)
            return player.Skills.SparefulHand ? StrategyArchetype.SwimbaitBank : StrategyArchetype.PreMoochOpener;

        // --- straight catch legendaries / grinders ---
        // Several !!! in the pool + ALure flag -> Ambitious Lure is the intended pressure tool.
        if (profile.Eligibility.ALureEligible && player.Skills.AmbitiousLure && pool.Count(p => p.Tug == TugType.Legendary) > 2)
            return StrategyArchetype.LureStack;

        // Target bites early, pool mates bite late -> Rest (or lure) instead of waiting out a dead cast.
        if (SpotBaitPoolAnalyzer.ComputeEarlyCancelSec(profile, pool) is > 0 and <= 12)
            return StrategyArchetype.ShortBiteReset;

        // Long target + even longer competitor -> slap the slow one so its bite can't waste the cast.
        if (SpotBaitPoolAnalyzer.RecommendSlapTarget(profile, pool) is { } slap) {
            var competitor = pool.FirstOrDefault(p => p.FishId == slap);
            if (competitor != null && competitor.BiteMax > profile.Signals.BiteTimeMin)
                return StrategyArchetype.FailFaster;
        }

        return StrategyArchetype.SlapAndChum;
    }
}

// what we're sitting on until the window opens
// - IdenticalCastZeroTime: farm N-1, IC last trigger at 0s (dies on stow)
// - CrossWindowJail: no IC - farm across windows, stow ok
// - Immediate: single trigger, catch when window is up
// - SwimbaitBank / MoochHold: bank with Spareful Hand, else hold live mooch
public static class PrepHoldModeSelector {
    public static PrepHoldMode Select(FishProfile profile, PlayerProfile player, StrategyArchetype archetype) {
        if (profile.Acquisition.Predators.Count > 0) {
            var total = profile.Acquisition.Predators.Sum(p => p.Quantity);
            if (total > 1 && player.Skills.IdenticalCast)
                return PrepHoldMode.IdenticalCastZeroTime;
            if (total > 1)
                return PrepHoldMode.CrossWindowJail;
            return PrepHoldMode.Immediate;
        }

        if (archetype == StrategyArchetype.SwimbaitBank && player.Skills.SparefulHand)
            return PrepHoldMode.SwimbaitBank;

        if (profile.Acquisition.MoochChain.Count > 0)
            return PrepHoldMode.MoochHold;

        return PrepHoldMode.None;
    }

    // If true, putting the rod away mid-prep ruins the plan (lose IC / mooch / swimbait).
    public static bool RequiresContinuousFishing(PrepHoldMode mode) => mode switch {
        PrepHoldMode.IdenticalCastZeroTime => true,
        PrepHoldMode.MoochHold => true,
        PrepHoldMode.SwimbaitBank => true,
        PrepHoldMode.CollectableDialogHold => true,
        PrepHoldMode.TimedLastCatch => true,
        PrepHoldMode.CrossWindowJail => false,
        _ => false,
    };
}
