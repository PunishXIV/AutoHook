namespace AutoHook.Data.FishInfo;

[Flags]
public enum FishDataSources {
    Teamcraft = 1,
    Personal = 2, // local GC records
    Both = Teamcraft | Personal,
}

public readonly record struct CatchResult(uint Bait, int Fish, byte AmbitiousLure, byte ModestLure, int Count) {
    // Fish id for casts that caught nothing (Teamcraft's -1)
    public const int MissFishId = -1;
}

// Chum = null when the source doesn't record it (Teamcraft)
public readonly record struct BiteKey(uint Bait, int Fish, bool? Chum);

public enum LureKind {
    None, // TC's 'exclude lures'
    Ambitious,
    Modest,
    Any,
}

// Stacks = 0 means any stack count above zero for Ambitious/Modest.
public readonly record struct LureFilter(LureKind Kind, byte Stacks = 0) {
    public static readonly LureFilter NoLure = new(LureKind.None);
    public static readonly LureFilter All = new(LureKind.Any);

    public bool Matches(byte ambitious, byte modest) => Kind switch {
        LureKind.None => ambitious == 0 && modest == 0,
        LureKind.Ambitious => modest == 0 && (Stacks == 0 ? ambitious > 0 : ambitious == Stacks),
        LureKind.Modest => ambitious == 0 && (Stacks == 0 ? modest > 0 : modest == Stacks),
        _ => true,
    };
}

// Bite times bucketed by whole second; ExactMin/ExactMax keep sub-second bounds when the source (GB) has them
public sealed class BiteHistogram {
    private readonly SortedDictionary<int, int> _counts = [];

    public IReadOnlyDictionary<int, int> CountsBySecond => _counts;
    public int Total { get; private set; }
    public double? ExactMin { get; private set; }
    public double? ExactMax { get; private set; }

    public bool IsEmpty => Total == 0;
    public double Min => ExactMin ?? (_counts.Count > 0 ? _counts.Keys.First() : 0);
    public double Max => ExactMax ?? (_counts.Count > 0 ? _counts.Keys.Last() + 1 : 0);

    public void Add(int second, int count) {
        if (count <= 0)
            return;
        _counts[second] = _counts.GetValueOrDefault(second) + count;
        Total += count;
    }

    public void AddExact(double seconds) {
        Add((int)Math.Floor(seconds), 1);
        ExactMin = ExactMin is { } min ? Math.Min(min, seconds) : seconds;
        ExactMax = ExactMax is { } max ? Math.Max(max, seconds) : seconds;
    }

    public void AddRange(BiteHistogram other) {
        foreach (var (second, count) in other._counts)
            Add(second, count);
        if (other.ExactMin is { } min)
            ExactMin = ExactMin is { } m ? Math.Min(m, min) : min;
        if (other.ExactMax is { } max)
            ExactMax = ExactMax is { } m ? Math.Max(m, max) : max;
    }

    // first whole second by which `fraction` (0..1) of all bites had happened
    public int Percentile(double fraction) {
        if (Total == 0)
            return 0;
        var target = Total * Math.Clamp(fraction, 0, 1);
        var running = 0;
        foreach (var (second, count) in _counts) {
            running += count;
            if (running >= target)
                return second;
        }
        return _counts.Keys.Last();
    }
}

public sealed class SpotObservations(int spotId) {
    public int SpotId { get; } = spotId;
    public List<CatchResult> Catches { get; } = [];
    public Dictionary<BiteKey, BiteHistogram> Bites { get; } = [];

    public long CastCount => Catches.Sum(c => (long)c.Count); // includes misses

    public long ExcludedCasts { get; set; } // IC/SS since those change the pool

    public long TotalCasts => CastCount + ExcludedCasts;

    public BiteHistogram GetOrAddBites(BiteKey key) {
        if (!Bites.TryGetValue(key, out var histogram))
            Bites[key] = histogram = new BiteHistogram();
        return histogram;
    }
}

public sealed record CarbunclePlushyFish {
    public required int ItemId { get; init; }
    public int? Spot { get; init; }
    public double StartHour { get; init; }
    public double EndHour { get; init; } = 24;
    public IReadOnlyList<uint> WeathersFrom { get; init; } = [];
    public IReadOnlyList<uint> Weathers { get; init; } = [];
    // each step lists interchangeable baits/mooches
    public IReadOnlyList<IReadOnlyList<int>> BestCatchPath { get; init; } = [];
    public IReadOnlyList<(int FishId, int Quantity)> Predators { get; init; } = [];
    public int? IntuitionSeconds { get; init; }
    public string? Hookset { get; init; }
    public string? Tug { get; init; }
    public string? Lure { get; init; }
    public bool? Snagging { get; init; }
    public bool FishEyes { get; init; }
    public bool BigFish { get; init; }
    public int? Folklore { get; init; }
    public double? Patch { get; init; }

    public bool AlwaysAvailable => Weathers.Count == 0 && WeathersFrom.Count == 0 && StartHour == 0 && EndHour == 24;
}

public enum GatherbuddyRecordStatus {
    NotLoaded,
    NotFound,
    UnsupportedFormat,
    Error,
    Loaded,
}
