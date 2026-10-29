namespace AutoHook.Data.FishInfo;

// Teamcraft's "fish per bait" table: each row is a bait, each cell the share of that bait's catches.
public sealed class BaitMatrix {
    public required IReadOnlyList<uint> Baits { get; init; }
    // MissFishId is the last column when misses are included
    public required IReadOnlyList<int> Fish { get; init; }
    public required int[,] Counts { get; init; }
    public required int[] RowTotals { get; init; }

    public bool IsEmpty => Baits.Count == 0;

    // null = this bait never produced this fish (Teamcraft leaves the cell blank, unlike a rounded 0%)
    public double? Rate(int row, int column)
        => Counts[row, column] == 0 || RowTotals[row] == 0 ? null : (double)Counts[row, column] / RowTotals[row];
}

public static class FishDataQueries {
    public static BaitMatrix BuildMatrix(IEnumerable<SpotObservations> sources, LureFilter lure, bool includeMisses) {
        var counts = new Dictionary<(uint Bait, int Fish), int>();
        foreach (var source in sources) {
            foreach (var c in source.Catches) {
                if (!lure.Matches(c.AmbitiousLure, c.ModestLure))
                    continue;
                if (!includeMisses && c.Fish == CatchResult.MissFishId)
                    continue;
                counts[(c.Bait, c.Fish)] = counts.GetValueOrDefault((c.Bait, c.Fish)) + c.Count;
            }
        }

        // busiest baits and fish first; misses always last
        var baits = counts.GroupBy(kv => kv.Key.Bait)
            .Select(g => (Bait: g.Key, Total: g.Sum(kv => kv.Value)))
            .Where(b => b.Total > 0)
            .OrderByDescending(b => b.Total)
            .Select(b => b.Bait)
            .ToList();
        var fish = counts.GroupBy(kv => kv.Key.Fish)
            .Select(g => (Fish: g.Key, Total: g.Sum(kv => kv.Value)))
            .OrderBy(f => f.Fish == CatchResult.MissFishId)
            .ThenByDescending(f => f.Total)
            .Select(f => f.Fish)
            .ToList();

        var grid = new int[baits.Count, fish.Count];
        var totals = new int[baits.Count];
        for (var row = 0; row < baits.Count; row++) {
            for (var column = 0; column < fish.Count; column++) {
                var n = counts.GetValueOrDefault((baits[row], fish[column]));
                grid[row, column] = n;
                totals[row] += n;
            }
        }

        return new BaitMatrix { Baits = baits, Fish = fish, Counts = grid, RowTotals = totals };
    }

    // chum = false also includes sources that don't record chum (Teamcraft); chum = true only has real chum data.
    public static BiteHistogram MergeBites(IEnumerable<SpotObservations> sources, uint bait, int fish, bool chum) {
        var merged = new BiteHistogram();
        foreach (var source in sources) {
            foreach (var (key, histogram) in source.Bites) {
                if (key.Bait != bait || key.Fish != fish)
                    continue;
                if (chum ? key.Chum != true : key.Chum == true)
                    continue;
                merged.AddRange(histogram);
            }
        }
        return merged;
    }

    // every fish with bite data for this bait, so a bite timeline can list them
    public static IReadOnlyList<int> FishWithBites(IEnumerable<SpotObservations> sources, uint bait)
        => [.. sources.SelectMany(s => s.Bites.Keys).Where(k => k.Bait == bait).Select(k => k.Fish).Distinct()];
}
