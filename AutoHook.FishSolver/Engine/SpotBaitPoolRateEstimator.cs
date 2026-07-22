using AutoHook.FishSolver.Models;

namespace AutoHook.FishSolver.Engine;

// relative catch share in a (spot, bait) pool
// prefer bite-time histograms; else RateTier weights; normalize to sum 1
public static class SpotBaitPoolRateEstimator {
    private static readonly Dictionary<RateTier, double> TierWeights = new() {
        [RateTier.Common] = 4.0,
        [RateTier.Uncommon] = 2.0,
        [RateTier.Rare] = 1.0,
        [RateTier.VeryRare] = 0.25,
        [RateTier.Unknown] = 1.0,
    };

    public static IReadOnlyDictionary<int, double> RelativeRatesAtSpot(int spotId, IReadOnlyList<PoolMember> pool, IReadOnlyDictionary<int, FishProfile>? fishById = null) {
        if (pool.Count == 0)
            return new Dictionary<int, double>();

        var raw = new Dictionary<int, double>();
        foreach (var member in pool) {
            raw[member.FishId] = RawWeight(member, spotId, fishById);
        }

        var total = raw.Values.Sum();
        if (total <= 0)
            return pool.ToDictionary(m => m.FishId, _ => 1.0 / pool.Count);

        return raw.ToDictionary(kvp => kvp.Key, kvp => kvp.Value / total);
    }

    private static double RawWeight(PoolMember member, int spotId, IReadOnlyDictionary<int, FishProfile>? fishById) {
        if (fishById != null
            && fishById.TryGetValue(member.FishId, out var profile)
            && profile.Signals.PerSpot.TryGetValue(spotId, out var range)
            && range.Histogram is { Count: > 0 } histogram) {
            return histogram.Values.Sum();
        }

        return TierWeights.GetValueOrDefault(member.RateTier, 1.0);
    }
}
