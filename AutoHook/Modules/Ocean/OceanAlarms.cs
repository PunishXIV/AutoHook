using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Lumina.Excel.Sheets;

namespace AutoHook.Modules.Ocean;

public static class OceanAlarms {
    private static readonly HashSet<OceanVoyage> Alarms = [];

    public static bool IsSet(OceanVoyage voyage) => Alarms.Contains(voyage);

    public static void Toggle(OceanVoyage voyage) {
        if (!Alarms.Remove(voyage))
            Alarms.Add(voyage);
    }

    public static void Update() {
        if (Alarms.Count == 0)
            return;

        var now = Framework.GetServerTime();
        foreach (var voyage in Alarms.Where(v => now >= v.BoardingStart).ToList()) {
            Alarms.Remove(voyage);
            if (voyage.IsBoarding(now))
                Notify(voyage);
        }
    }

    private static void Notify(OceanVoyage voyage) {
        var route = IKDRoute.TryGetRow(voyage.RouteId, out var row) ? row.Name.ToString() : $"#{voyage.RouteId}";
        var cfg = new NotificationConfig { Enabled = true, EchoChatMessage = true, BeepOnSuccess = true };
        NotificationMasterService.Get().Api.TryNotify(cfg, $"Boat boarding now: {route}");
    }
}
