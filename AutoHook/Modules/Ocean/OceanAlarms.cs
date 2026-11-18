using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Lumina.Excel.Sheets;

namespace AutoHook.Modules.Ocean;

public static class OceanAlarms {
    private const long StaleSeconds = 60;

    private static readonly HashSet<OceanVoyage> Alarms = [];
    private static readonly HashSet<(OceanVoyage Voyage, int Index)> Fired = [];

    private static List<OceanAlarmReminder> Reminders => Configuration.C.OceanAlarmReminders;

    public static bool IsSet(OceanVoyage voyage) => Alarms.Contains(voyage);

    public static void Toggle(OceanVoyage voyage) {
        if (Alarms.Remove(voyage))
            Fired.RemoveWhere(f => f.Voyage == voyage);
        else
            Alarms.Add(voyage);
    }

    public static long ReminderTime(OceanVoyage voyage, OceanAlarmReminder reminder) {
        var anchor = reminder.Anchor == OceanAlarmAnchor.BoardingCloses ? voyage.BoardingStart + OceanSchedule.BoardingSeconds : voyage.BoardingStart;
        return anchor - Math.Max(reminder.Minutes, 0) * 60L;
    }

    public static void Update() {
        if (Alarms.Count == 0)
            return;

        var now = Framework.GetServerTime();
        foreach (var voyage in Alarms.ToList()) {
            if (now >= voyage.BoardingStart + OceanSchedule.BoardingSeconds) {
                Alarms.Remove(voyage);
                Fired.RemoveWhere(f => f.Voyage == voyage);
                continue;
            }

            for (var i = 0; i < Reminders.Count; i++) {
                var at = ReminderTime(voyage, Reminders[i]);
                if (now < at || !Fired.Add((voyage, i)))
                    continue;
                if (now - at <= StaleSeconds)
                    Notify(voyage, now);
            }
        }
    }

    public static string Describe(OceanVoyage voyage, long now) {
        var route = IKDRoute.TryGetRow(voyage.RouteId, out var row) ? row.Name.ToString() : $"#{voyage.RouteId}";
        var closes = voyage.BoardingStart + OceanSchedule.BoardingSeconds;
        return voyage.IsBoarding(now)
            ? string.Format(UIStrings.OceanTrip_AlarmBoardingNow, Minutes(closes - now), route)
            : string.Format(UIStrings.OceanTrip_AlarmBoardingIn, Minutes(voyage.BoardingStart - now), route);
    }

    private static string Minutes(long seconds) => seconds < 60 ? string.Format(UIStrings.OceanTrip_SecondsShort, Math.Max(seconds, 0)) : string.Format(UIStrings.OceanTrip_MinutesShort, (seconds + 59) / 60);

    private static void Notify(OceanVoyage voyage, long now) {
        var text = Describe(voyage, now);
        var cfg = new NotificationConfig { Enabled = true, EchoChatMessage = true, BeepOnSuccess = true };
        NotificationMasterService.Get().Api.TryNotify(cfg, text, OceanTripService.Get().BuildAlert(voyage, text));
    }
}
