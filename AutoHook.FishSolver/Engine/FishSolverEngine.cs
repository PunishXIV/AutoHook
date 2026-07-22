using AutoHook.FishSolver.Models;
using AutoHook.FishSolver.Rules;

namespace AutoHook.FishSolver.Engine;

// one solve: classify -> adapt to skills -> prep/window -> cast + GP policy -> SolverOutput
// ArriveEarlySeconds = prep total + GP regen buffer + some extra buffer
public sealed class FishSolverEngine(FishKnowledgeBase knowledgeBase, BuffPersistenceRules? buffRules = null) {
    private readonly BuffPersistenceRules _buffRules = buffRules ?? BuffPersistenceRules.Default;

    public SolverOutput? Solve(SolverRequest request) {
        if (!knowledgeBase.FishById.TryGetValue(request.TargetFishId, out var baseProfile))
            return null;

        var tactics = FishArchetypeClassifier.Classify(baseProfile, request.Profile);
        var (variant, fallbacks) = RouteVariantSelector.Adapt(baseProfile, ref tactics, request.Profile);
        var profile = baseProfile with { Tactics = tactics };

        var prep = PrepTimeEstimator.EstimatePrep(profile, tactics, request.Profile);
        var windowSync = PrepTimeEstimator.EstimateWindowSync(profile, tactics, request.Profile);
        var castPolicy = CastDecisionPolicyBuilder.Build(profile, tactics, request.Profile);
        var resourcePolicy = ResourcePolicyBuilder.Build(profile, tactics, request.Profile);

        // amiss doesn't really matter for big fish since the only cross window ones you can still switch holes. But maybe I'll use it in the future so it's here (unused)
        var safetyMargin = variant == RouteVariant.Fallback ? 120 : 60;
        // GpRegen is already in PrepTotalSeconds; add it again so arrive-early isn't empty-GP at open
        var gpBuffer = prep.Steps.Where(s => s.Action == PrepActionKind.GpRegen).Sum(s => s.EstimatedSeconds);
        var arriveEarly = prep.PrepTotalSeconds + gpBuffer + safetyMargin;

        return new SolverOutput {
            TargetFishId = profile.FishId,
            BaitId = profile.Eligibility.BaitId,
            SpotIds = request.PreferredSpotId is { } spot && profile.Eligibility.SpotIds.Contains(spot)
                // preferred hole first (e.g. player is already there); rest unchanged
                ? [spot, .. profile.Eligibility.SpotIds.Where(s => s != spot)]
                : profile.Eligibility.SpotIds,
            RouteVariant = variant,
            PlayerProfileUsed = request.Profile,
            Archetype = tactics.Archetype,
            HoldMode = tactics.HoldMode,
            SlapTargetFishId = tactics.SlapTargetFishId,
            EarlyCancelSec = tactics.EarlyCancelSec,
            PrepPhase = prep,
            WindowSync = windowSync,
            CastPolicy = castPolicy,
            ResourcePolicy = resourcePolicy,
            ArriveEarlySeconds = arriveEarly,
            MissingSkillsFallbacks = fallbacks,
        };
    }
}
