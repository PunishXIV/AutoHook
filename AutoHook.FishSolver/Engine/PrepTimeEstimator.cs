using AutoHook.FishSolver.Models;

namespace AutoHook.FishSolver.Engine;

// downgrade the plan when the player is missing skills; record fallbacks for the UI
// Fallback route gets a bigger arrive-early safety margin later
public static class RouteVariantSelector {
    public static (RouteVariant variant, List<string> fallbacks) Adapt(FishProfile profile, ref InferredTactics tactics, PlayerProfile player) {
        var fallbacks = new List<string>();
        var variant = RouteVariant.Optimal;

        // No Spareful Hand -> can't bank swimbait; hold a live mooch instead
        if (tactics.HoldMode == PrepHoldMode.SwimbaitBank && !player.Skills.SparefulHand) {
            tactics = tactics with { HoldMode = PrepHoldMode.MoochHold, Archetype = StrategyArchetype.PreMoochOpener };
            fallbacks.Add("SparefulHand unavailable: using MoochHold instead of SwimbaitBank");
            variant = RouteVariant.Fallback;
        }

        // No Identical Cast -> can't zero-time; jail across windows or just catch on open
        if (tactics.HoldMode == PrepHoldMode.IdenticalCastZeroTime && !player.Skills.IdenticalCast) {
            tactics = tactics with { HoldMode = profile.Acquisition.Predators.Sum(p => p.Quantity) > 1 ? PrepHoldMode.CrossWindowJail : PrepHoldMode.Immediate };
            fallbacks.Add("IdenticalCast unavailable: using CrossWindowJail or Immediate intuition");
            variant = RouteVariant.Fallback;
        }

        // Lure plan without lures -> fall back to Rest / early-cancel
        if (tactics.Archetype is StrategyArchetype.LureStack or StrategyArchetype.LureReroll
            && !player.Skills.AmbitiousLure && !player.Skills.ModestLure) {
            tactics = tactics with { Archetype = StrategyArchetype.ShortBiteReset };
            fallbacks.Add("Lures unavailable: using Rest/early-cancel instead");
            variant = RouteVariant.Fallback;
        }

        // Slap target picked but player can't slap -> clear it; chum-only
        if (tactics.SlapTargetFishId.HasValue && !player.Skills.SurfaceSlap) {
            tactics = tactics with { SlapTargetFishId = null };
            fallbacks.Add("SurfaceSlap unavailable: chum-only route");
            variant = RouteVariant.Fallback;
        }

        // Mooch prep without Patience II or Prize Catch. lol good luck buddy
        if (profile.Acquisition.MoochChain.Count > 0 && !player.Skills.PrizeCatch && !player.Skills.PatienceII) {
            fallbacks.Add("Neither PrizeCatch nor PatienceII: this will suck");
            variant = RouteVariant.Fallback;
        }

        tactics = tactics with {
            RequiresContinuousFishing = PrepHoldModeSelector.RequiresContinuousFishing(tactics.HoldMode),
            StowRodSafeDuringHold = tactics.HoldMode == PrepHoldMode.CrossWindowJail,
        };

        return (variant, fallbacks);
    }
}

// how long prep takes
// catch estimate: (expected casts) * (avg bite + recast); chum ≈ halves bite time
// IC zero-time: farm Quantity-1 of the last predator, full count of earlier ones; DH ≈ 0.6x multi-catch
// swimbait bank: ~3.5x one catch so there's no under-estimate
// window-sync (cast at open) is separate - not in PrepTotalSeconds / arrive-early
public static class PrepTimeEstimator {
    private const double RecastOverheadSec = 4;
    private const double ChumFactor = 0.5;

    public static PrepPhasePlan EstimatePrep(FishProfile profile, InferredTactics tactics, PlayerProfile player) {
        var steps = new List<PrepStep>();
        var total = 0;

        // catch and slap the little pool shit first (if there is one)
        if (tactics.SlapTargetFishId is { } slapId && player.Skills.SurfaceSlap) {
            var sec = EstimateCatchSeconds(profile.PoolAtPrimarySpot.FirstOrDefault(p => p.FishId == slapId), profile.PoolAtPrimarySpot, player, useChum: false);
            steps.Add(new PrepStep {
                Phase = PrepPhaseKind.Prep,
                Action = PrepActionKind.CatchAndSlap,
                FishId = slapId,
                Count = 1,
                EstimatedSeconds = sec,
            });
            total += sec;
        }

        if (profile.Acquisition.Predators.Count > 0) {
            // IC routes often chum less during the long farm (GP reserved for IC); small predator totals still chum
            var useChum = tactics.HoldMode != PrepHoldMode.IdenticalCastZeroTime || profile.Acquisition.Predators.Sum(p => p.Quantity) <= 3;

            for (var i = 0; i < profile.Acquisition.Predators.Count; i++) {
                var trigger = profile.Acquisition.Predators[i];
                var triggerMember = FindMember(profile, trigger.FishId);
                var isLastPredator = i == profile.Acquisition.Predators.Count - 1;

                // Last predator under IC: leave one for the window-open release
                var prepCount = tactics.HoldMode == PrepHoldMode.IdenticalCastZeroTime && isLastPredator ? Math.Max(0, trigger.Quantity - 1) : trigger.Quantity;
                if (prepCount <= 0)
                    continue;

                var perCatch = EstimateCatchSeconds(triggerMember, profile.PoolAtPrimarySpot, player, useChum: useChum);
                var hookMultiplier = player.Skills.DoubleHook && prepCount >= 2 ? 0.6 : 1.0;
                var prepSec = (int)Math.Ceiling(prepCount * perCatch * hookMultiplier);
                steps.Add(new PrepStep {
                    Phase = PrepPhaseKind.Prep,
                    Action = PrepActionKind.CatchTriggers,
                    FishId = trigger.FishId,
                    Count = prepCount,
                    EstimatedSeconds = prepSec,
                });
                total += prepSec;
            }

            // IC itself is instant. Its gp recovery is handled later
            if (tactics.HoldMode == PrepHoldMode.IdenticalCastZeroTime && player.Skills.IdenticalCast) {
                steps.Add(new PrepStep {
                    Phase = PrepPhaseKind.Prep,
                    Action = PrepActionKind.IdenticalCast,
                    EstimatedSeconds = 0,
                });
            }
        }
        else if (tactics.HoldMode is PrepHoldMode.MoochHold or PrepHoldMode.SwimbaitBank) {
            var moochId = profile.Acquisition.MoochChain.LastOrDefault();
            if (moochId > 0) {
                var member = FindMember(profile, moochId);
                var sec = EstimateCatchSeconds(member, profile.PoolAtPrimarySpot, player, useChum: true);
                if (tactics.HoldMode == PrepHoldMode.SwimbaitBank)
                    sec = (int)(sec * 3.5); // ~3 banked fish + a little slack
                steps.Add(new PrepStep {
                    Phase = PrepPhaseKind.Prep,
                    Action = tactics.HoldMode == PrepHoldMode.SwimbaitBank ? PrepActionKind.SwimbaitSelect : PrepActionKind.MoochHeldBait,
                    FishId = moochId,
                    Count = tactics.HoldMode == PrepHoldMode.SwimbaitBank ? 3 : 1,
                    EstimatedSeconds = sec,
                });
                total += sec;
            }
        }

        // wait for gp to max before the window starts
        var gpNeeded = EstimateGpNeeded(tactics, player);
        if (gpNeeded > 0) {
            var regen = player.EstimateGpRegenSeconds(gpNeeded);
            steps.Add(new PrepStep {
                Phase = PrepPhaseKind.Prep,
                Action = PrepActionKind.GpRegen,
                EstimatedSeconds = regen,
            });
            total += regen;
        }

        return new PrepPhasePlan {
            HoldMode = tactics.HoldMode,
            RequiresContinuousFishing = tactics.RequiresContinuousFishing,
            StowRodSafeDuringHold = tactics.StowRodSafeDuringHold,
            Steps = steps,
            PrepTotalSeconds = total,
        };
    }

    public static WindowSyncPlan? EstimateWindowSync(FishProfile profile, InferredTactics tactics, PlayerProfile player) {
        // Straight-catch non-mooch fish have nothing special at window open
        if (profile.Acquisition.Predators.Count == 0 && tactics.HoldMode is not PrepHoldMode.MoochHold and not PrepHoldMode.SwimbaitBank)
            return null;

        var action = tactics.HoldMode switch {
            PrepHoldMode.IdenticalCastZeroTime => PrepActionKind.CastLastTrigger,
            PrepHoldMode.MoochHold => PrepActionKind.MoochHeldBait,
            PrepHoldMode.SwimbaitBank => PrepActionKind.SwimbaitSelect,
            PrepHoldMode.Immediate => PrepActionKind.CatchTriggers,
            _ => PrepActionKind.CastLastTrigger,
        };

        var triggerId = profile.Acquisition.Predators.FirstOrDefault()?.FishId ?? profile.Acquisition.MoochChain.LastOrDefault();
        var member = FindMember(profile, triggerId);
        var sec = EstimateCatchSeconds(member, profile.PoolAtPrimarySpot, player, useChum: true);

        return new WindowSyncPlan {
            Action = action,
            Timing = "AtWindowOpen",
            EstimatedSeconds = Math.Max(sec, 10),
        };
    }

    private static PoolMember FindMember(FishProfile profile, int fishId)
        => profile.PoolAtPrimarySpot.FirstOrDefault(p => p.FishId == fishId) ?? new PoolMember { FishId = fishId, BiteMin = 8, BiteMax = 20, RateTier = RateTier.Uncommon };

    private static int EstimateCatchSeconds(PoolMember? member, IReadOnlyList<PoolMember> pool, PlayerProfile player, bool useChum) {
        member ??= new PoolMember { BiteMin = 10, BiteMax = 20, RateTier = RateTier.Uncommon };
        var avgBite = (member.BiteMin + member.BiteMax) / 2.0;
        if (useChum)
            avgBite *= ChumFactor;
        var probability = SpotBaitPoolAnalyzer.EstimateBiteProbability(member, pool);
        // Expected casts until success ≈ 1/p, floored so ghosts don't explode the estimate
        var casts = 1.0 / Math.Max(probability, 0.05);
        return (int)Math.Ceiling(casts * (avgBite + RecastOverheadSec));
    }

    private static int EstimateGpNeeded(InferredTactics tactics, PlayerProfile player) {
        var needed = 0;
        if (player.Skills.SurfaceSlap && tactics.SlapTargetFishId.HasValue)
            needed += 200;
        if (tactics.HoldMode == PrepHoldMode.IdenticalCastZeroTime)
            needed += 400;
        return Math.Min(needed, player.GpMax);
    }
}
