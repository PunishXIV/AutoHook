using System.IO;
using System.Threading;

namespace AutoHook.Data.FishInfo;

public sealed class FishData : IPluginService {
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private Task? _loading;

    private Dictionary<int, SpotObservations> _teamcraft = [];
    private Dictionary<int, CarbunclePlushyFish> _tracker = [];
    private Dictionary<int, SpotObservations> _personal = [];
    private DateTime _personalWriteTime;

    public bool IsLoaded { get; private set; }
    public int Version { get; private set; } // bumped whenever any data is (re)loaded, so callers can cache query results
    public string? LoadError { get; private set; }
    public GatherbuddyRecordStatus PersonalStatus { get; private set; } = GatherbuddyRecordStatus.NotLoaded;
    public int PersonalRecordCount { get; private set; } // raw GatherBuddy records, including ones left out of the catch data
    public string? PersonalError { get; private set; }

    private static string DataDirectory => Path.Combine(Svc.Interface.AssemblyLocation.DirectoryName!, "Data", "FishData");

    public static string GatherBuddyRecordPath
        => Path.Combine(Svc.Interface.ConfigDirectory.Parent!.FullName, "GatherBuddy", "fish_records.dat");

    public void EnsureLoaded() {
        if (_loading is { IsCompleted: false })
            return;
        if (IsLoaded && !PersonalRecordsChanged())
            return;
        _loading = Task.Run(LoadAsync);
    }

    private async Task LoadAsync() {
        await _loadLock.WaitAsync().ConfigureAwait(false);
        try {
            if (!IsLoaded) {
                try {
                    _teamcraft = SpotPoolLoader.Load(Path.Combine(DataDirectory, "spot_pools.json"));
                    _tracker = FishTrackerLoader.Load(Path.Combine(DataDirectory, "fish_tracker.json"));
                }
                catch (Exception e) {
                    LoadError = e.Message;
                    IPluginLog.Get().Error(e, "[FishData] Failed to load bundled fish data");
                }
                IsLoaded = true;
            }

            if (PersonalRecordsChanged())
                LoadPersonal();
            Version++;
        }
        finally {
            _loadLock.Release();
        }
    }

    private bool PersonalRecordsChanged() {
        var file = new FileInfo(GatherBuddyRecordPath);
        return file.Exists ? file.LastWriteTimeUtc != _personalWriteTime : PersonalStatus != GatherbuddyRecordStatus.NotFound;
    }

    private void LoadPersonal() {
        var file = new FileInfo(GatherBuddyRecordPath);
        var result = GatherBuddyRecordReader.Read(file.FullName);
        _personal = result.Spots;
        _personalWriteTime = file.Exists ? file.LastWriteTimeUtc : default;
        PersonalStatus = result.Status;
        PersonalRecordCount = result.RecordCount;
        PersonalError = result.Error;
        if (result.Error != null)
            IPluginLog.Get().Warning($"[FishData] Could not read GatherBuddy records: {result.Error}");
    }

    public CarbunclePlushyFish? GetTracker(int fishId) => _tracker.GetValueOrDefault(fishId);

    // Counted = casts in the catch data, Total = including casts left out of it (only GatherBuddy has those)
    public (long Counted, long Total) Casts(FishDataSources sources)
        => Sum(AllSpots(sources));

    public (long Counted, long Total) Casts(int spotId, FishDataSources sources)
        => Sum(GetSpot(spotId, sources));

    private IEnumerable<SpotObservations> AllSpots(FishDataSources sources) {
        if (sources.HasFlag(FishDataSources.Teamcraft))
            foreach (var spot in _teamcraft.Values)
                yield return spot;
        if (sources.HasFlag(FishDataSources.Personal))
            foreach (var spot in _personal.Values)
                yield return spot;
    }

    private static (long Counted, long Total) Sum(IEnumerable<SpotObservations> spots) {
        long counted = 0, total = 0;
        foreach (var spot in spots) {
            counted += spot.CastCount;
            total += spot.TotalCasts;
        }
        return (counted, total);
    }

    public IEnumerable<SpotObservations> GetSpot(int spotId, FishDataSources sources) {
        if (sources.HasFlag(FishDataSources.Teamcraft) && _teamcraft.TryGetValue(spotId, out var teamcraft))
            yield return teamcraft;
        if (sources.HasFlag(FishDataSources.Personal) && _personal.TryGetValue(spotId, out var personal))
            yield return personal;
    }

    public BaitMatrix GetMatrix(int spotId, FishDataSources sources, LureFilter lure, bool includeMisses)
        => FishDataQueries.BuildMatrix(GetSpot(spotId, sources), lure, includeMisses);

    public BiteHistogram GetBites(int spotId, FishDataSources sources, uint bait, int fish, bool chum)
        => FishDataQueries.MergeBites(GetSpot(spotId, sources), bait, fish, chum);

    // spots with data for this fish, from the catalog, the tracker and both observation sources
    public IReadOnlyList<int> GetSpotsForFish(int fishId) {
        var spots = new HashSet<int>();
        if (FishBaitCatalog.Get().TryGet(fishId, out var fish))
            spots.UnionWith(fish.SpotIds);
        if (_tracker.GetValueOrDefault(fishId)?.Spot is { } trackerSpot)
            spots.Add(trackerSpot);
        foreach (var source in new[] { _teamcraft, _personal })
            foreach (var spot in source.Values)
                if (spot.Catches.Any(c => c.Fish == fishId))
                    spots.Add(spot.SpotId);
        return [.. spots.Order()];
    }
}
