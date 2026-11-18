using Lumina.Excel.Sheets;

namespace AutoHook.Modules.Ocean;

public static class OceanPresetReadiness {
    public readonly record struct Stop(uint SpotId, uint TimeId) {
        public override string ToString() => OceanStopUtil.FormatStopLabel(SpotId, TimeId);
    }

    public readonly record struct Summary(int Covered, int Total, List<Stop> Missing) {
        public bool Ready => Missing.Count == 0;
    }

    private const long CacheMs = 2000;
    private static readonly Dictionary<uint, List<Stop>> MissingByRoute = [];
    private static long _cacheUntil;

    public static bool IsReady(uint routeId) => MissingStops(routeId).Count == 0;

    // stops of this route that no preset would cover for the current goal
    public static List<Stop> MissingStops(uint routeId) {
        if (Environment.TickCount64 > _cacheUntil) {
            MissingByRoute.Clear();
            _cacheUntil = Environment.TickCount64 + CacheMs;
        }
        if (!MissingByRoute.TryGetValue(routeId, out var missing)) {
            missing = [.. Stops(routeId).Where(s => !Covers(routeId, s))];
            MissingByRoute[routeId] = missing;
        }
        return missing;
    }

    // unique stops across the routes the filters would pick
    public static Summary ForFilteredRoutes(long now) {
        var lines = OceanVoyageFilter.Lines;
        var routes = OceanSchedule.Upcoming(lines, now)
            .Take(OceanSchedule.RotationLength * lines.Length)
            .Where(OceanVoyageFilter.Matches)
            .Select(v => v.RouteId)
            .Distinct()
            .ToList();
        var all = routes.SelectMany(Stops).Distinct().ToList();
        var missing = routes.SelectMany(MissingStops).Distinct().ToList();
        return new Summary(all.Count - missing.Count, all.Count, missing);
    }

    private static IEnumerable<Stop> Stops(uint routeId) {
        if (!IKDRoute.TryGetRow(routeId, out var route))
            yield break;
        for (var i = 0; i < route.Spot.Count && i < route.Time.Count; i++) {
            var spot = route.Spot[i].RowId;
            var time = route.Time[i].RowId;
            if (spot != 0 && time != 0)
                yield return new Stop(spot, time);
        }
    }

    // mirrors OceanPresetSelector's cascade
    private static bool Covers(uint routeId, Stop stop) {
        var skipIfAcquired = Configuration.C.AOF_Fallthrough;
        var presets = OceanPresetSelector.EnumerateHookPresets()
            .Select(p => p.ExtraCfg)
            .Where(e => e.AutoOceanFishEnabled && (e.AutoOceanFishAllStops || e.AutoOceanFishSpotId == stop.SpotId && e.AutoOceanFishTimeId == stop.TimeId))
            .ToList();

        foreach (var tier in OceanGoalCatalog.GetCascade(Configuration.C.AutoOceanFishGoal)) {
            var forTier = presets.Where(e => e.AutoOceanFishGoal == tier).ToList();
            if (forTier.Count == 0)
                continue;
            var found = tier switch {
                OceanFishGoalKind.Achievement => OceanGoalCatalog.GetEligibleAchievementIds(routeId, skipIfAcquired) is var ids && forTier.Any(e => ids.Contains(e.AutoOceanFishGoalId)),
                OceanFishGoalKind.Legendary => OceanGoalCatalog.GetEligibleLegendaryIds(routeId, skipIfAcquired).Count > 0,
                OceanFishGoalKind.Levelling => OceanGoalCatalog.IsLevellingNeeded() || !skipIfAcquired,
                _ => true,
            };
            if (found)
                return true;
        }
        return false;
    }
}
