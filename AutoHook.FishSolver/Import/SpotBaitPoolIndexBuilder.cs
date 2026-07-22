using AutoHook.FishSolver.Models;

namespace AutoHook.FishSolver.Import;

// "what can I catch here with this bait?"
// fishing-sources primary; merge InitialBait (sources miss some Hellfishing links e.g. Magma Worm -> Granite Crab)
// keyed by "spotId:baitId"
public static class SpotBaitPoolIndexBuilder {
    public static Dictionary<string, PoolIndexEntry> BuildFromSources(Dictionary<int, List<FishingSourceEntry>> sources, IReadOnlySet<int> knownFishIds, IEnumerable<FishRecord>? recordsForInitialBaitMerge = null) {
        var poolMap = new Dictionary<string, HashSet<int>>();

        foreach (var (fishId, entries) in sources) {
            if (!knownFishIds.Contains(fishId))
                continue;

            foreach (var entry in entries) {
                if (entry.Spot <= 0 || entry.Bait <= 0)
                    continue;

                AddToPool(poolMap, entry.Spot, entry.Bait, fishId);
            }
        }

        // fishing-sources is incomplete for some spots
        if (recordsForInitialBaitMerge != null) {
            foreach (var record in recordsForInitialBaitMerge) {
                if (record.IsSpearFish || record.InitialBait <= 0 || !knownFishIds.Contains(record.ItemId))
                    continue;

                foreach (var spot in record.SpotIds) {
                    if (spot <= 0)
                        continue;
                    AddToPool(poolMap, spot, record.InitialBait, record.ItemId);
                }
            }
        }

        return ToPoolEntries(poolMap);
    }

    private static void AddToPool(Dictionary<string, HashSet<int>> poolMap, int spotId, int baitId, int fishId) {
        var key = FishKnowledgeBase.PoolKey(spotId, baitId);
        if (!poolMap.TryGetValue(key, out var set)) {
            set = [];
            poolMap[key] = set;
        }

        set.Add(fishId);
    }

    private static Dictionary<string, PoolIndexEntry> ToPoolEntries(Dictionary<string, HashSet<int>> poolMap)
        => poolMap.ToDictionary(
            kvp => kvp.Key,
            kvp => {
                var parts = kvp.Key.Split(':');
                return new PoolIndexEntry {
                    SpotId = int.Parse(parts[0]),
                    BaitId = int.Parse(parts[1]),
                    FishIds = [.. kvp.Value.OrderBy(id => id)],
                };
            });

    // when fishing-sources is unavailable, one bait per fish via InitialBait
    public static Dictionary<string, PoolIndexEntry> BuildFromInitialBaits(IEnumerable<FishRecord> records) {
        var poolMap = new Dictionary<string, HashSet<int>>();

        foreach (var record in records) {
            if (record.IsSpearFish || record.InitialBait <= 0)
                continue;

            foreach (var spot in record.SpotIds) {
                var key = FishKnowledgeBase.PoolKey(spot, record.InitialBait);
                if (!poolMap.TryGetValue(key, out var set)) {
                    set = [];
                    poolMap[key] = set;
                }

                set.Add(record.ItemId);
            }
        }

        return ToPoolEntries(poolMap);
    }

    public static List<PoolMember> BuildPoolMembers(int spotId, int baitId, Dictionary<string, PoolIndexEntry> pools, Dictionary<int, FishRecord> byId) {
        if (!pools.TryGetValue(FishKnowledgeBase.PoolKey(spotId, baitId), out var entry))
            return [];

        return [.. entry.FishIds
            .Where(byId.ContainsKey)
            .Select(id => FishKnowledgeBaseBuilder.ToPoolMember(byId[id]))
            .OrderBy(m => m.FishId)];
    }

    public static int? PrimaryBaitAtSpot(int fishId, int spotId, Dictionary<int, List<FishingSourceEntry>>? sources, int fallbackBait) {
        if (sources == null || !sources.TryGetValue(fishId, out var entries))
            return fallbackBait > 0 ? fallbackBait : null;

        var atSpot = entries.Where(e => e.Spot == spotId).Select(e => e.Bait).Distinct().ToList();
        return atSpot.Count switch {
            0 => fallbackBait > 0 ? fallbackBait : null,
            1 => atSpot[0],
            _ => atSpot.Contains(fallbackBait) ? fallbackBait : atSpot[0],
        };
    }
}
