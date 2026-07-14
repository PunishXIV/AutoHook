using FFXIVClientStructs.FFXIV.Client.Game.UI;
using AchievementSheet = Lumina.Excel.Sheets.Achievement;

namespace AutoHook.Data;

public static class AchievementProgressSnapshot {
    public static unsafe List<WorldState.OpAchievementProgress> Collect() {
        var results = new List<WorldState.OpAchievementProgress>();
        var ach = Achievement.Instance();
        if (ach == null || !ach->IsLoaded())
            return results;

        var ws = Service.WorldState;
        var emitted = new HashSet<uint>();

        foreach (var (id, progress) in ws.AchievementProgress) {
            if (progress.Max == 0)
                continue;
            emitted.Add(id);
            results.Add(new WorldState.OpAchievementProgress(id, progress.Current, progress.Max));
        }

        foreach (var row in Svc.Data.GetExcelSheet<AchievementSheet>()) {
            var id = row.RowId;
            if (id == 0 || emitted.Contains(id))
                continue;
            if (ach->IsComplete((int)id))
                results.Add(new WorldState.OpAchievementProgress(id, 1, 1));
        }

        return results;
    }
}
