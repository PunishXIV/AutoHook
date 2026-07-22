using AutoHook.FishSolver.Models;

namespace AutoHook.FishSolver.Import;

// Teamcraft rows -> FishKnowledgeBase
// 1. index (spot, bait) pools from fishing-sources + InitialBait merge
// 2. one FishProfile per fish
// 3. optional solver_overrides
// tactics start pending; engine re-classifies later with actual skills
public static class FishKnowledgeBaseBuilder {
    public static FishKnowledgeBase Build(IEnumerable<FishRecord> records, IEnumerable<FishOverride>? overrides = null, Dictionary<int, List<FishingSourceEntry>>? fishingSources = null) {
        var list = records.Where(r => !r.IsSpearFish).ToList();
        var byId = list.ToDictionary(r => r.ItemId);
        var knownIds = byId.Keys.ToHashSet();
        var pools = fishingSources is { Count: > 0 }
            ? SpotBaitPoolIndexBuilder.BuildFromSources(fishingSources, knownIds, list)
            : SpotBaitPoolIndexBuilder.BuildFromInitialBaits(list);
        var kb = new FishKnowledgeBase {
            PoolsByKey = pools,
            FishById = list.ToDictionary(
                r => r.ItemId,
                r => BuildProfile(r, byId, pools, fishingSources)),
        };
        foreach (var entry in kb.PoolsByKey.Values)
            entry.Members = SpotBaitPoolIndexBuilder.BuildPoolMembers(entry.SpotId, entry.BaitId, pools, byId);
        var overrideList = overrides?.ToList() ?? SolverOverridesMerger.LoadFromEmbedded();
        if (overrideList.Count > 0)
            SolverOverridesMerger.ApplyOverrides(kb, overrideList);
        return kb;
    }

    private static FishProfile BuildProfile(FishRecord record, Dictionary<int, FishRecord> byId, Dictionary<string, PoolIndexEntry> pools, Dictionary<int, List<FishingSourceEntry>>? sources) {
        var tug = MapTug(record.BiteType);
        var hookset = MapHookset(record.HookType);
        var acquisitionType = ClassifyAcquisition(record);
        var predators = record.Predators.Select(p => new IntuitionRequirement {
            FishId = p.ItemId,
            Quantity = p.Quantity,
        }).ToList();
        var primarySpot = record.SpotIds.FirstOrDefault();
        var baitAtSpot = primarySpot > 0
            ? SpotBaitPoolIndexBuilder.PrimaryBaitAtSpot(record.ItemId, primarySpot, sources, record.InitialBait) ?? record.InitialBait
            : record.InitialBait;
        var pool = primarySpot > 0 && baitAtSpot > 0
            ? SpotBaitPoolIndexBuilder.BuildPoolMembers(primarySpot, baitAtSpot, pools, byId)
            : [];
        // pending - engine re-classifies at solve time
        var tactics = new InferredTactics {
            Archetype = StrategyArchetype.SlapAndChum,
            HoldMode = PrepHoldMode.None,
        };
        return new FishProfile {
            FishId = record.ItemId,
            Eligibility = new FishEligibility {
                FishId = record.ItemId,
                SpotIds = record.SpotIds,
                BaitId = baitAtSpot,
                SpawnHour = record.Spawn,
                DurationHours = record.Duration,
                TimeRange = record.Time,
                Weathers = record.Weathers,
                WeathersFrom = record.WeathersFrom,
                MinGathering = record.MinGathering,
                SnaggingRequired = record.Snagging,
                MLureEligible = record.MLure > 0,
                ALureEligible = record.ALure > 0,
            },
            Signals = new FishSignals {
                Tug = tug,
                Hookset = hookset,
                BiteTimeMin = record.BiteTimeMin,
                BiteTimeMax = record.BiteTimeMax,
                PerSpot = BuildPerSpotRanges(record),
            },
            Acquisition = new FishAcquisition {
                Type = acquisitionType,
                MoochChain = record.Mooches,
                Predators = predators,
                IntuitionDurationSec = EstimateIntuitionDuration(predators),
            },
            PoolAtPrimarySpot = pool,
            Tactics = tactics,
        };
    }

    internal static PoolMember ToPoolMember(FishRecord record) {
        var totalOccurrences = record.BiteTimeHistogram?.Values.SelectMany(h => h.Values).Sum() ?? 0;
        return new PoolMember {
            FishId = record.ItemId,
            Tug = MapTug(record.BiteType),
            BiteMin = record.BiteTimeMin,
            BiteMax = record.BiteTimeMax,
            RateTier = ClassifyRateTier(record, totalOccurrences),
        };
    }

    private static Dictionary<int, BiteTimeRange> BuildPerSpotRanges(FishRecord record) {
        var result = new Dictionary<int, BiteTimeRange>();
        if (record.BiteTimeHistogram == null)
            return result;
        foreach (var (spotKey, histogram) in record.BiteTimeHistogram) {
            if (!int.TryParse(spotKey, out var spotId))
                continue;
            var times = histogram.Keys.Select(int.Parse).OrderBy(t => t).ToList();
            if (times.Count == 0)
                continue;
            result[spotId] = new BiteTimeRange {
                Min = times.First(),
                Max = times.Last(),
                Histogram = histogram.ToDictionary(
                    kvp => int.Parse(kvp.Key),
                    kvp => kvp.Value),
            };
        }
        return result;
    }

    private static AcquisitionType ClassifyAcquisition(FishRecord record) {
        if (record.IsSpearFish)
            return AcquisitionType.Spearfishing;
        if (record.Predators.Count > 0)
            return record.Mooches.Count > 0 ? AcquisitionType.IntuitionMooch : AcquisitionType.IntuitionStraight;
        if (record.Mooches.Count > 1)
            return AcquisitionType.MoochChain;
        if (record.Mooches.Count == 1)
            return AcquisitionType.SingleMooch;
        return AcquisitionType.StraightCatch;
    }

    private static int? EstimateIntuitionDuration(List<IntuitionRequirement> predators) {
        if (predators.Count == 0)
            return null;
        var total = predators.Sum(p => p.Quantity);
        return total switch {
            1 => 60,
            2 => 360,
            <= 5 => 150,
            <= 10 => 120,
            _ => 90,
        };
    }

    private static RateTier ClassifyRateTier(FishRecord record, int totalOccurrences) {
        if (totalOccurrences >= 500)
            return RateTier.Common;
        if (totalOccurrences >= 100)
            return RateTier.Uncommon;
        if (totalOccurrences >= 20)
            return RateTier.Rare;
        if (totalOccurrences > 0)
            return RateTier.VeryRare;
        var window = record.BiteTimeMax - record.BiteTimeMin;
        if (window > 30)
            return RateTier.Rare;
        return RateTier.Unknown;
    }

    public static TugType MapTug(int biteType) => biteType switch {
        36 => TugType.Weak,
        37 => TugType.Strong,
        38 => TugType.Legendary,
        _ => TugType.Unknown,
    };
    public static HooksetType MapHookset(int hookType) => hookType switch {
        4179 => HooksetType.Precision,
        4103 => HooksetType.Powerful,
        _ => HooksetType.Normal,
    };
}
