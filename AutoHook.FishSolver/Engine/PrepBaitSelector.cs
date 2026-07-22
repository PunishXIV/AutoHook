using AutoHook.FishSolver.Models;

namespace AutoHook.FishSolver.Engine;

// which bait to farm prep fish on
// membership alone is wrong: bait with 1% junk >> bait with 90% junk, same useful set
// score ≈ usefulRate*1000 - noiseRate*800 + coverage bonus - imbalance penalty
// tie-break: higher useful, lower noise, then bait id
// split pools (sea butterfly, problematicus) are the preset builder's problem
public static class PrepBaitSelector {
    private const double UsefulRateWeight = 1000.0; // reward for catch share of prep fish
    private const double NoiseRatePenalty = 800.0; // penalty for catch share of everything else
    private const double FullCoverageBonus = 500.0; // all prep species present with non-trivial share
    private const double PrepBalancePenalty = 300.0; // unequal shares when counts should match
    private const double MinMeaningfulPrepShare = 0.001; // floor for "present enough" coverage

    public sealed record BaitScore(int BaitId, IReadOnlyList<int> PoolFishIds, IReadOnlyList<int> UsefulFishIds, IReadOnlyList<int> NoiseFishIds, double UsefulRate, double NoiseRate, double Score);

    public static BaitScore ScoreBait(int baitId, IReadOnlyList<PoolMember> pool, IReadOnlyCollection<int> prepFishIds, IReadOnlyDictionary<int, double> relativeRates) {
        var poolIds = pool.Select(m => m.FishId).Distinct().OrderBy(id => id).ToList();
        var prep = prepFishIds.ToHashSet();
        var useful = poolIds.Where(prep.Contains).ToList();
        var noise = poolIds.Where(id => !prep.Contains(id)).ToList();

        // relativeRates should already sum ~1 across the pool. Missing ids count as 0.
        var usefulRate = useful.Sum(id => relativeRates.GetValueOrDefault(id, 0));
        var noiseRate = noise.Sum(id => relativeRates.GetValueOrDefault(id, 0));

        var score = usefulRate * UsefulRateWeight - noiseRate * NoiseRatePenalty;

        // "Can this bait actually finish prep without swapping?" - every predator must be present and not a ghost %.
        if (prep.All(p => useful.Contains(p) && relativeRates.GetValueOrDefault(p, 0) >= MinMeaningfulPrepShare))
            score += FullCoverageBonus;

        // Equal-count dual prep (3+3 Sea Butterfly): a 90/10 split means you finish one side forever before the other.
        score -= PrepImbalancePenalty(prep, useful, relativeRates);

        return new BaitScore(baitId, poolIds, useful, noise, usefulRate, noiseRate, score);
    }

    // when we only know membership, pretend every pool fish is equally likely.
    public static BaitScore ScoreBait(int baitId, IReadOnlyCollection<int> poolFishIds, IReadOnlyCollection<int> prepFishIds) {
        var pool = poolFishIds.Select(id => new PoolMember { FishId = id }).ToList();
        var equal = poolFishIds.ToDictionary(id => id, _ => 1.0 / Math.Max(poolFishIds.Count, 1));
        return ScoreBait(baitId, pool, prepFishIds, equal);
    }

    public static BaitScore? SelectBestPrepBait(IReadOnlyDictionary<int, IReadOnlyList<PoolMember>> poolsByBait, IReadOnlyCollection<int> prepFishIds, int spotId, IReadOnlyDictionary<int, FishProfile>? fishById = null) {
        BaitScore? best = null;
        foreach (var (baitId, pool) in poolsByBait) {
            if (pool.Count == 0)
                continue;

            var rates = SpotBaitPoolRateEstimator.RelativeRatesAtSpot(spotId, pool, fishById);
            var scored = ScoreBait(baitId, pool, prepFishIds, rates);
            if (best == null || Compare(scored, best) > 0)
                best = scored;
        }

        return best;
    }

    public static BaitScore? SelectBestPrepBait(IReadOnlyDictionary<int, IReadOnlyCollection<int>> poolsByBait, IReadOnlyCollection<int> prepFishIds) {
        var converted = poolsByBait.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<PoolMember>)[.. kvp.Value.Select(id => new PoolMember { FishId = id })]);
        return SelectBestPrepBait(converted, prepFishIds, spotId: 0, fishById: null);
    }

    public static int SelectBestSharedPrepBait(IReadOnlyDictionary<int, IReadOnlyList<PoolMember>> poolsByBait, IReadOnlyCollection<int> prepFishIds, int spotId, IReadOnlyDictionary<int, FishProfile>? fishById = null)
        => SelectBestPrepBait(poolsByBait, prepFishIds, spotId, fishById)?.BaitId ?? 0;

    public static int SelectBestSharedPrepBait(IReadOnlyDictionary<int, IReadOnlyCollection<int>> poolsByBait, IReadOnlyCollection<int> prepFishIds)
        => SelectBestPrepBait(poolsByBait, prepFishIds)?.BaitId ?? 0;

    // Positive when candidate beats incumbent.
    public static int Compare(BaitScore candidate, BaitScore incumbent) {
        if (candidate.Score > incumbent.Score + 1e-9)
            return 1;
        if (candidate.Score < incumbent.Score - 1e-9)
            return -1;

        if (candidate.UsefulRate > incumbent.UsefulRate + 1e-9)
            return 1;
        if (candidate.UsefulRate < incumbent.UsefulRate - 1e-9)
            return -1;

        if (candidate.NoiseRate < incumbent.NoiseRate - 1e-9)
            return 1;
        if (candidate.NoiseRate > incumbent.NoiseRate + 1e-9)
            return -1;

        return candidate.BaitId.CompareTo(incumbent.BaitId);
    }

    private static double PrepImbalancePenalty(HashSet<int> prep, List<int> useful, IReadOnlyDictionary<int, double> relativeRates) {
        if (prep.Count < 2 || useful.Count < 2)
            return 0;

        var prepRates = prep.Select(p => relativeRates.GetValueOrDefault(p, 0)).ToList();
        if (prepRates.All(r => r <= 0))
            return 0;

        // max−min gap: 0.5 vs 0.5 -> 0 penalty; 0.9 vs 0.1 -> large penalty.
        return (prepRates.Max() - prepRates.Min()) * PrepBalancePenalty;
    }
}
