using Lumina.Excel.Sheets;

namespace AutoHook.Modules.Ocean;

public enum OceanLine {
    Indigo,
    Ruby,
}

public readonly record struct OceanVoyage(long BoardingStart, uint RouteId) {
    public bool IsBoarding(long now) => now >= BoardingStart && now < BoardingStart + OceanSchedule.BoardingSeconds;
}

public static class OceanSchedule {
    public const long Epoch = 57600;
    public const long SlotSeconds = 7200;
    public const long BoardingSeconds = 900;

    // voyages from the one currently boarding (if any) onwards; with several lines, each slot yields one voyage per line
    // in the given order (they board at the same time)
    public static IEnumerable<OceanVoyage> Upcoming(IReadOnlyList<OceanLine> lines, long now) {
        var table = IKDRouteTable.Rows.ToList();
        if (table.Count == 0)
            yield break;

        var slot = (now - Epoch) / SlotSeconds;
        if ((now - Epoch) % SlotSeconds >= BoardingSeconds)
            slot++;

        for (; ; slot++) {
            var row = table[(int)(slot % table.Count)];
            foreach (var line in lines)
                yield return new OceanVoyage(Epoch + slot * SlotSeconds, line == OceanLine.Indigo ? row.Route.RowId : row.Unknown0);
        }
    }

    public static int RotationLength => IKDRouteTable.Rows.Count(); // one full rotation: filters that match nothing stop here instead of searching forever
}
