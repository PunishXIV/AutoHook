using AutoHook.Modules.Ocean;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace AutoHook.Ui;

public sealed class TabOceanFishing : BaseTab {
    public override string TabName => UIStrings.OceanFishing;
    public override bool Enabled => true;
    public override OpenWindow Type => OpenWindow.OceanFishing;

    private static readonly int[] CountOptions = [6, 12, 24, 48];

    private static readonly OceanLine[] IndigoOnly = [OceanLine.Indigo];
    private static readonly OceanLine[] RubyOnly = [OceanLine.Ruby];
    private static readonly OceanLine[] BothLines = [OceanLine.Indigo, OceanLine.Ruby];

    private OceanLine[] _lines = IndigoOnly;
    private int _countIndex = 1;
    private uint _blueFishFilter; // FishParameter row / Achievement row, 0 = any
    private uint _achievementFilter;
    private bool _onlyIncomplete;

    public override void Draw() {
        var now = Framework.GetServerTime();
        var lineRoutes = RoutesOnLines(_lines);

        DrawControls(lineRoutes);
        ImGui.Spacing();
        ImGui.Spacing();

        // filters repeat every rotation, so this bounds the search when few voyages match
        var voyages = OceanSchedule.Upcoming(_lines, now)
            .Take(OceanSchedule.RotationLength * CountOptions[^1] * _lines.Length)
            .Where(Matches)
            .Take(CountOptions[_countIndex])
            .ToList();

        if (voyages.Count == 0) {
            ImGui.TextDisabled("No voyages match these filters.");
            return;
        }

        DrawTable(voyages, now);
    }

    public override void DrawHeader() { }

    private void DrawControls(HashSet<uint> lineRoutes) {
        DrawLineButton("Indigo", IndigoOnly);
        ImGui.SameLine(0, 2.Scaled());
        DrawLineButton("Ruby", RubyOnly);
        ImGui.SameLine(0, 2.Scaled());
        DrawLineButton("Both", BothLines);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(110.Scaled());
        using (var combo = ImRaii.Combo("###OceanCount", $"Next {CountOptions[_countIndex]}")) {
            if (combo) {
                for (var i = 0; i < CountOptions.Length; i++) {
                    if (ImGui.Selectable($"Next {CountOptions[i]}", i == _countIndex))
                        _countIndex = i;
                }
            }
        }

        // only offer what this line's routes have, a filter from the other line would match nothing
        var blueFish = OceanGoalCatalog.Legendaries.Where(f => f.RouteIds.Any(lineRoutes.Contains)).ToList();
        var achievements = OceanGoalCatalog.Achievements.Where(a => a.RouteIds.Any(lineRoutes.Contains)).ToList();
        if (blueFish.All(f => f.FishParameterId != _blueFishFilter))
            _blueFishFilter = 0;
        if (achievements.All(a => a.AchievementId != _achievementFilter))
            _achievementFilter = 0;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(180.Scaled());
        using (var combo = ImRaii.Combo("###OceanBlueFish", _blueFishFilter == 0 ? "Any legendary" : BlueFishName(_blueFishFilter))) {
            if (combo) {
                if (ImGui.Selectable("Any legendary", _blueFishFilter == 0))
                    _blueFishFilter = 0;
                foreach (var fish in blueFish) {
                    if (ImGui.Selectable($"{BlueFishName(fish.FishParameterId)}###bf{fish.FishParameterId}", fish.FishParameterId == _blueFishFilter))
                        _blueFishFilter = fish.FishParameterId;
                }
            }
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(180.Scaled());
        using (var combo = ImRaii.Combo("###OceanAchievement", _achievementFilter == 0 ? "Any achievement" : AchievementName(_achievementFilter))) {
            if (combo) {
                if (ImGui.Selectable("Any achievement", _achievementFilter == 0))
                    _achievementFilter = 0;
                foreach (var achievement in achievements) {
                    if (ImGui.Selectable($"{AchievementName(achievement.AchievementId)}###ach{achievement.AchievementId}", achievement.AchievementId == _achievementFilter))
                        _achievementFilter = achievement.AchievementId;
                }
            }
        }

        ImGui.SameLine();
        ImGui.Checkbox("Only incomplete", ref _onlyIncomplete);
        ImGui.TooltipOnHover("Voyages with uncaught legendaries or unacquired achievements");
    }

    private void DrawLineButton(string label, OceanLine[] lines) {
        using (ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive], _lines == lines)) {
            if (ImGui.Button(label))
                _lines = lines;
        }
    }

    private bool Matches(OceanVoyage voyage) {
        var blueFish = OceanGoalCatalog.GetLegendariesForRoute(voyage.RouteId).ToList();
        var achievements = OceanGoalCatalog.GetAchievementsForRoute(voyage.RouteId).ToList();

        if (_blueFishFilter != 0 && blueFish.All(f => f.FishParameterId != _blueFishFilter))
            return false;
        if (_achievementFilter != 0 && achievements.All(a => a.AchievementId != _achievementFilter))
            return false;
        if (_onlyIncomplete) {
            var fishLeft = blueFish.Where(f => _blueFishFilter == 0 || f.FishParameterId == _blueFishFilter).Any(f => !OceanGoalCatalog.IsLegendaryCaught(f.FishParameterId));
            var achievementsLeft = achievements.Where(a => _achievementFilter == 0 || a.AchievementId == _achievementFilter).Any(a => OceanGoalCatalog.IsAchievementIncomplete(a.AchievementId) != false);
            if (!fishLeft && !achievementsLeft)
                return false;
        }
        return true;
    }

    private static void DrawTable(List<OceanVoyage> voyages, long now) {
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingFixedFit;
        using var table = ImRaii.Table("###OceanSchedule", 5, flags);
        if (!table)
            return;

        ImGui.TableSetupColumn("Boarding");
        ImGui.TableSetupColumn("Route");
        ImGui.TableSetupColumn("Objectives");
        ImGui.TableSetupColumn("Bonus");
        ImGui.TableSetupColumn("Caught", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var voyage in voyages) {
            using var id = ImRaii.PushId($"{voyage.BoardingStart}_{voyage.RouteId}");
            if (!IKDRoute.TryGetRow(voyage.RouteId, out var route))
                continue;
            var boarding = voyage.IsBoarding(now);

            ImGui.TableNextRow();
            if (boarding)
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.GetColorU32(ImGuiColors.ParsedGreen with { W = 0.2f }));

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text($"{DateTimeOffset.FromUnixTimeSeconds(voyage.BoardingStart).ToLocalTime():ddd HH:mm}");
            ImGui.SameLine();
            if (boarding)
                ImGui.TextColored(ImGuiColors.ParsedGreen, $"boarding, {Countdown(voyage.BoardingStart + OceanSchedule.BoardingSeconds - now)} left");
            else
                ImGui.TextDisabled($"in {Countdown(voyage.BoardingStart - now)}");

            ImGui.TableNextColumn();
            DrawRoute(route);

            ImGui.TableNextColumn();
            DrawObjectives(voyage.RouteId, route);

            ImGui.TableNextColumn();
            DrawBonuses(voyage.RouteId);

            ImGui.TableNextColumn();
            DrawCaught(voyage.RouteId, route);
        }
    }

    private static void DrawRoute(IKDRoute route) {
        using (ImRaii.Group()) {
            ImGui.AlignTextToFramePadding();
            ImGui.Text(route.Name.ToString());
            ImGui.SameLine();
            DrawTimeOfDayIcon(route.Time[^1].RowId);
        }

        if (!ImGui.IsItemHovered())
            return;
        using var tooltip = ImRaii.Tooltip();
        for (var stop = 0; stop < route.Spot.Count; stop++) {
            DrawTimeOfDayIcon(route.Time[stop].RowId);
            ImGui.SameLine();
            ImGui.Text($"{OceanStopUtil.GetSpotName(route.Spot[stop].RowId)} ({TimeOfDayName(route.Time[stop].RowId)})");
        }
    }

    private static void DrawObjectives(uint routeId, IKDRoute route) {
        var drawn = false;
        foreach (var fish in OceanGoalCatalog.GetLegendariesForRoute(routeId)) {
            if (drawn)
                ImGui.SameLine();
            DrawBlueFishIcon(fish.FishParameterId, OceanRouteFishCatalog.FindFish(routeId, fish.FishParameterId), route);
            drawn = true;
        }

        foreach (var def in OceanGoalCatalog.GetAchievementsForRoute(routeId)) {
            if (!Achievement.TryGetRow(def.AchievementId, out var achievement))
                continue;
            if (drawn)
                ImGui.SameLine();
            // null = progress not loaded yet; shown as incomplete
            var complete = OceanGoalCatalog.IsAchievementIncomplete(def.AchievementId) == false;
            DrawUtil.GameIcon(achievement.Icon, IconSize, $"{achievement.Name}\n{achievement.Description}\n{(complete ? "Complete" : "Not complete")}", dim: complete);
            drawn = true;
        }

        if (!drawn) {
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("-");
        }
    }

    private static void DrawBlueFishIcon(uint fishParameterId, OceanRouteFish? found, IKDRoute route) {
        if (!FishParameter.TryGetRow(fishParameterId, out var param) || !Item.TryGetRow(param.Item.RowId, out var item))
            return;

        var caught = OceanGoalCatalog.IsLegendaryCaught(fishParameterId);
        var where = found is { } f
            ? $"Stop {f.Stop + 1}: {OceanStopUtil.GetSpotName(route.Spot[f.Stop].RowId)} ({TimeOfDayName(route.Time[f.Stop].RowId)}){(f.Spectral ? ", spectral current" : "")}"
            : "Stop unknown";
        DrawUtil.GameIcon(item.Icon, IconSize, $"{item.Name}\n{where}\n{(caught ? "Caught" : "Not caught yet")}", dim: caught);
    }

    private static void DrawBonuses(uint routeId) {
        var drawn = false;
        foreach (var def in OceanGoalCatalog.GetAchievementsForRoute(routeId)) {
            if (OceanGoalCatalog.GetAchievementBonus(def.AchievementId) is not { } bonus)
                continue;
            if (drawn)
                ImGui.SameLine();
            DrawUtil.GameIcon(bonus.Image, IconSize, $"{bonus.Objective}\n{bonus.Requirement}\nBonus: {bonus.BonusMultiplier}%");
            drawn = true;
        }

        if (!drawn) {
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("-");
        }
    }

    private static void DrawCaught(uint routeId, IKDRoute route) {
        var fish = OceanRouteFishCatalog.GetFish(routeId);
        var total = fish.Select(f => f.FishParameterId).Distinct().Count();
        var missing = fish.Where(f => !OceanGoalCatalog.IsLegendaryCaught(f.FishParameterId)).ToList();
        var caught = total - missing.Select(f => f.FishParameterId).Distinct().Count();

        ImGui.AlignTextToFramePadding();
        if (caught == total)
            ImGui.TextColored(ImGuiColors.ParsedGreen, $"{caught}/{total}");
        else
            ImGui.Text($"{caught}/{total}");

        if (!ImGui.IsItemHovered())
            return;
        if (caught == total) {
            ImGui.SetTooltip("Every fish on this route has been caught");
            return;
        }

        using var tooltip = ImRaii.Tooltip();
        ImGui.Text("Missing");
        ImGui.Separator();
        foreach (var group in missing.GroupBy(f => f.Stop).OrderBy(g => g.Key)) {
            DrawTimeOfDayIcon(route.Time[group.Key].RowId);
            ImGui.SameLine();
            ImGui.Text(OceanStopUtil.GetSpotName(route.Spot[group.Key].RowId));
            using (ImRaii.PushIndent())
                foreach (var f in group.DistinctBy(f => f.FishParameterId))
                    ImGui.BulletText($"{ItemName(f.ItemId)}{(f.Spectral ? " (spectral)" : "")}");
        }
    }

    private static void DrawTimeOfDayIcon(uint timeId) {
        var (icon, color) = (TimeOfDay)timeId switch {
            TimeOfDay.Day => (FontAwesomeIcon.Sun, ImGuiColors.DalamudYellow),
            TimeOfDay.Sunset => (FontAwesomeIcon.CloudSun, ImGuiColors.DalamudOrange),
            TimeOfDay.Night => (FontAwesomeIcon.Moon, ImGuiColors.TankBlue),
            _ => (FontAwesomeIcon.Question, ImGuiColors.DalamudGrey),
        };
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextColored(color, icon.ToIconString());
    }

    private static Vector2 IconSize => new(ImGui.GetFrameHeight());

    private static string TimeOfDayName(uint timeId) => (TimeOfDay)timeId switch {
        TimeOfDay.Day => "Day",
        TimeOfDay.Sunset => "Sunset",
        TimeOfDay.Night => "Night",
        _ => "Unknown",
    };

    private static string ItemName(uint itemId) => Item.TryGetRow(itemId, out var item) ? item.Name.ToString() : $"#{itemId}";

    private static HashSet<uint> RoutesOnLines(IReadOnlyList<OceanLine> lines)
        => [.. IKDRouteTable.Rows.SelectMany(r => lines.Select(line => line == OceanLine.Indigo ? r.Route.RowId : r.Unknown0)).Where(id => id != 0)];

    private static string BlueFishName(uint fishParameterId)
        => FishParameter.TryGetRow(fishParameterId, out var param) && Item.TryGetRow(param.Item.RowId, out var item) ? item.Name.ToString() : $"#{fishParameterId}";

    private static string AchievementName(uint achievementId)
        => Achievement.TryGetRow(achievementId, out var achievement) ? achievement.Name.ToString() : $"#{achievementId}";

    private static string Countdown(long seconds) {
        var span = TimeSpan.FromSeconds(Math.Max(seconds, 0));
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes:00}m" : $"{span.Minutes}m {span.Seconds:00}s";
    }
}
