using Lumina.Excel.Sheets;

namespace AutoHook.Modules.Ocean;

public readonly record struct OceanRouteFish(uint ItemId, uint FishParameterId, int Stop, bool Spectral);

public static class OceanRouteFishCatalog {
    private static readonly Dictionary<uint, IReadOnlyList<OceanRouteFish>> ByRoute = [];
    private static Dictionary<uint, uint>? _fishParameterByItem;

    public static IReadOnlyList<OceanRouteFish> GetFish(uint routeId) {
        if (!ByRoute.TryGetValue(routeId, out var fish))
            ByRoute[routeId] = fish = Build(routeId);
        return fish;
    }

    // null if the fish isn't on this route at a matching time of day
    public static OceanRouteFish? FindFish(uint routeId, uint fishParameterId)
        => GetFish(routeId).Where(f => f.FishParameterId == fishParameterId).Select(f => (OceanRouteFish?)f).FirstOrDefault();

    private static List<OceanRouteFish> Build(uint routeId) {
        if (!IKDRoute.TryGetRow(routeId, out var route))
            return [];

        _fishParameterByItem ??= FishParameter.Rows
            .Where(p => p.Item.RowId != 0)
            .GroupBy(p => p.Item.RowId)
            .ToDictionary(g => g.Key, g => g.First().RowId);

        var result = new List<OceanRouteFish>();
        for (var stop = 0; stop < route.Spot.Count; stop++) {
            if (!IKDSpot.TryGetRow(route.Spot[stop].RowId, out var spot))
                continue;
            var time = route.Time[stop].RowId;
            foreach (var (fishingSpotId, spectral) in new[] { (spot.SpotMain.RowId, false), (spot.SpotSub.RowId, true) }) {
                if (!FishingSpot.TryGetRow(fishingSpotId, out var fishingSpot))
                    continue;
                foreach (var item in fishingSpot.Item) {
                    if (item.RowId != 0 && _fishParameterByItem.TryGetValue(item.RowId, out var parameter) && AvailableOn(routeId, item.RowId, parameter, time))
                        result.Add(new OceanRouteFish(item.RowId, parameter, stop, spectral));
                }
            }
        }
        return result;
    }

    private static bool AvailableOn(uint routeId, uint itemId, uint fishParameterId, uint timeId) {
        if (OceanGoalCatalog.Legendaries.FirstOrDefault(l => l.FishParameterId == fishParameterId) is { } legendary)
            return legendary.RouteIds.Contains(routeId);
        return FishBaitCatalog.Get()[itemId]?.OceanFishingTime is not { } time || time == 0 || TeamcraftTimeToGame(time) == timeId;
    }

    private static uint TeamcraftTimeToGame(int time) => time switch {
        1 => (uint)TimeOfDay.Sunset,
        2 => (uint)TimeOfDay.Day,
        _ => (uint)time,
    };
}
