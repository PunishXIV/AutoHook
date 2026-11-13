using AutoHook.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Lumina.Excel.Sheets;
using System.Numerics;

namespace AutoHook.Ui;

public static class OceanTripUi {
    private static OceanTripConfig Cfg => Configuration.C.OceanTrip;
    private static Vector2 IconSize => new(ImGui.GetFrameHeight());

    #region Ocean tab

    public static void DrawStatusBar() {
        if (OceanTripService.Running is { } trip) {
            StatusIcon(FontAwesomeIcon.Ship, ImGuiColors.ParsedGreen);
            ImGui.TextColored(ImGuiColors.ParsedGreen, $"{OceanTripService.RouteName(trip.Voyage.RouteId)}: {trip.Status}");
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed)) {
                if (ImGuiComponents.IconButton("###StopTrip", FontAwesomeIcon.Stop))
                    OceanTripService.Stop();
            }
            ImGui.TooltipOnHover(UIStrings.OceanTrip_StopTrip);
            return;
        }

        if (!Configuration.C.AutoOceanFish) {
            StatusIcon(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudOrange);
            ImGui.TextColored(ImGuiColors.DalamudOrange, string.Format(UIStrings.OceanTrip_RequiresAutoOceanFish, UIStrings.AutoOceanFish));
            ImGui.SameLine();
            if (ImGui.SmallButton(UIStrings.Enable)) {
                Configuration.C.AutoOceanFish = true;
                Configuration.Save();
            }
            return;
        }

        var now = Framework.GetServerTime();
        if (Cfg.FullAuto && OceanPresetReadiness.ForFilteredRoutes(now) is { Ready: false } readiness) {
            StatusIcon(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudOrange);
            ImGui.TextColored(ImGuiColors.DalamudOrange, string.Format(UIStrings.OceanTrip_StopsUncovered, readiness.Missing.Count));
            ReadinessTooltip(readiness);
            ImGui.SameLine();
        }
        if (Cfg.FullAuto && OceanTripService.Get().NextTripVoyage(now) is { } next) {
            var leaves = Math.Max(OceanTripService.StartTime(next), now);
            StatusIcon(FontAwesomeIcon.Clock, ImGuiColors.DalamudGrey);
            ImGui.Text(string.Format(UIStrings.OceanTrip_NextTrip, OceanTripService.RouteName(next.RouteId), Clock(leaves)));
            if (OceanTripService.Get().LastMessage is { Length: > 0 } msg) {
                ImGui.SameLine();
                ImGui.TextDisabled($"({msg})");
            }
            return;
        }

        if (OceanTripService.Get().LastMessage is { Length: > 0 } last) {
            StatusIcon(FontAwesomeIcon.Anchor, ImGuiColors.DalamudGrey);
            ImGui.TextDisabled(last);
        }
    }

    // preset coverage of the filtered routes' stops, under the goal settings
    public static void DrawReadiness() {
        var readiness = OceanPresetReadiness.ForFilteredRoutes(Framework.GetServerTime());
        DrawUtil.Branch();
        StatusIcon(readiness.Ready ? FontAwesomeIcon.Check : FontAwesomeIcon.ExclamationTriangle, readiness.Ready ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudOrange);
        ImGui.TextColored(readiness.Ready ? ImGuiColors.ParsedGreen : ImGuiColors.DalamudOrange, string.Format(UIStrings.OceanTrip_Readiness, readiness.Covered, readiness.Total, GoalName(Configuration.C.AutoOceanFishGoal)));
        ReadinessTooltip(readiness);
    }

    private static void ReadinessTooltip(OceanPresetReadiness.Summary readiness) {
        if (readiness.Ready || !ImGui.IsItemHovered())
            return;
        using var tooltip = ImRaii.Tooltip();
        ImGui.Text(UIStrings.OceanTrip_ReadinessMissing);
        foreach (var stop in readiness.Missing)
            ImGui.BulletText(stop.ToString());
        ImGui.Spacing();
        ImGui.TextDisabled(UIStrings.OceanTrip_ReadinessGated);
    }

    private static string GoalName(OceanFishGoalKind goal) => goal switch {
        OceanFishGoalKind.Levelling => UIStrings.OceanFishGoal_Levelling,
        OceanFishGoalKind.Achievement => UIStrings.OceanFishGoal_Achievements,
        OceanFishGoalKind.Legendary => UIStrings.OceanFishGoal_Legendary,
        _ => UIStrings.OceanFishGoal_Points,
    };

    public static void DrawGoButton(OceanVoyage voyage) {
        using var disabled = ImRaii.Disabled(OceanTripService.Running != null);
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen)) {
            if (ImGuiComponents.IconButton("###Go", FontAwesomeIcon.Ship))
                OceanTripService.Get().TryStart(voyage, OceanTripSource.Manual);
        }
    }

    // Settings > Ocean Fishing > Auto Ocean Fish > Voyages
    public static void DrawVoyageSettings() {
        using var id = ImRaii.PushId("OceanTripSettings");
        DrawUtil.DrawCheckboxTree(UIStrings.OceanTrip_GoAutomatically, ref Cfg.FullAuto, DrawWhen, UIStrings.FullAutoHelpText);
        DrawUtil.DrawTreeNodeEx(UIStrings.OceanTrip_Preparation, DrawPreparation);
        DrawUtil.DrawTreeNodeEx(UIStrings.OceanTrip_DuringTheVoyage, DrawOnBoard);
        DrawUtil.DrawTreeNodeEx(UIStrings.OceanTrip_AfterTheVoyage, DrawAfter);
    }

    #endregion

    #region When

    private static void DrawWhen() {
        DrawUtil.Branch();
        ImGui.Text(UIStrings.OceanTrip_Leave);
        ImGui.SameLine();
        if (ImGui.RadioButton(UIStrings.OceanTrip_LeaveWhenBoardingOpens, Cfg.Arrival == OceanTripArrival.Early)) {
            Cfg.Arrival = OceanTripArrival.Early;
            Configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton(UIStrings.OceanTrip_LeaveLate, Cfg.Arrival == OceanTripArrival.Late)) {
            Cfg.Arrival = OceanTripArrival.Late;
            Configuration.Save();
        }
        ImGui.SameLine(0, 0);
        ImGui.SetNextItemWidth(30.Scaled());
        if (ImGui.InputInt("###LateMinute", ref Cfg.LateArrivalMinute, 0)) {
            Cfg.LateArrivalMinute = Math.Clamp(Cfg.LateArrivalMinute, 0, OceanTripService.LatestStartMinute);
            Configuration.Save();
        }

        DrawUtil.Branch();
        if (ImGui.Checkbox("###UseActiveWindow", ref Cfg.UseActiveWindow))
            Configuration.Save();
        ImGui.SameLine();
        using (ImRaii.Disabled(!Cfg.UseActiveWindow)) {
            // one translatable sentence, with the two time inputs drawn where {1} and {2} sit
            var sentence = string.Format(UIStrings.OceanTrip_OnlyBetween, UIStrings.AutoOceanFish, "{1}", "{2}");
            var first = true;
            foreach (var part in System.Text.RegularExpressions.Regex.Split(sentence, @"(\{1\}|\{2\})")) {
                if (part.Trim().Length == 0)
                    continue;
                if (!first)
                    ImGui.SameLine();
                first = false;
                switch (part) {
                    case "{1}":
                        if (DrawClockInput("###ActiveFrom", ref Cfg.ActiveFromMinute))
                            Configuration.Save();
                        break;
                    case "{2}":
                        if (DrawClockInput("###ActiveTo", ref Cfg.ActiveToMinute))
                            Configuration.Save();
                        break;
                    default:
                        ImGui.AlignTextToFramePadding();
                        ImGui.Text(part.Trim());
                        break;
                }
            }
        }

        DrawUtil.Branch();
        ImGui.Text(string.Format(UIStrings.OceanTrip_StartsFrom, UIStrings.AutoOceanFish));
        using (DrawUtil.BeginBranches(indent: true)) {
            DrawSafePoint(UIStrings.OceanTrip_InnRoom, OceanTripSafePoint.Inn);
            DrawSafePoint(UIStrings.OceanTrip_ResidentialSector, OceanTripSafePoint.Housing);
            DrawSafePoint(UIStrings.OceanTrip_AnySanctuary, OceanTripSafePoint.Sanctuary);
            DrawUtil.DrawTreeNodeEx(UIStrings.OceanTrip_AutoRetainer, DrawAutoRetainer, UIStrings.OceanTrip_AutoRetainerHelp);
        }
    }

    private static void DrawSafePoint(string label, OceanTripSafePoint flag, string help = "") {
        var on = Cfg.SafePoints.HasFlag(flag);
        if (!DrawUtil.Checkbox(label, ref on, help, hoverHelpText: true))
            return;
        Cfg.SafePoints = on ? Cfg.SafePoints | flag : Cfg.SafePoints & ~flag;
        Configuration.Save();
    }

    private static void DrawAutoRetainer() {
        DrawUtil.Branch();
        ImGui.Text(UIStrings.OceanTrip_Character);
        ImGui.SameLine();
        if (Cfg.ArCharacter.Length > 0)
            ImGui.TextColored(ImGuiColors.ParsedGreen, Cfg.ArCharacter);
        else
            ImGui.TextColored(ImGuiColors.DalamudOrange, UIStrings.OceanTrip_NoneAssigned);

        ImGui.SameLine();
        if (Cfg.ArCharacter.Length > 0) {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.UserTimes, UIStrings.OceanTrip_Unassign)) {
                Cfg.ArCharacter = "";
                Configuration.Save();
            }
        }
        else {
            var current = OceanTripService.CurrentCharacter;
            using (ImRaii.Disabled(current == null)) {
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.UserCheck, UIStrings.OceanTrip_UseThisCharacter) && current != null) {
                    Cfg.ArCharacter = current;
                    Configuration.Save();
                }
            }
        }
        DrawPluginState("AutoRetainer", AutoRetainerIPC.Get().Available);

        using (ImRaii.Disabled(Cfg.ArCharacter.Length == 0)) {
            DrawSafePoint(UIStrings.OceanTrip_MultiMode, OceanTripSafePoint.ArMultiMode, UIStrings.OceanTrip_MultiModeHelp);

            var postprocess = Cfg.SafePoints.HasFlag(OceanTripSafePoint.ArPostprocess);
            DrawUtil.DrawCheckboxTree(UIStrings.OceanTrip_CharacterPostProcess, ref postprocess, () => {
                DrawUtil.Branch();
                ImGui.Text(UIStrings.OceanTrip_WhenVoyageLeavesWithin);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80.Scaled());
                if (ImGui.InputInt($"{UIStrings.OceanTrip_Min}###ArLookahead", ref Cfg.ArLookaheadMinutes, 1)) {
                    Cfg.ArLookaheadMinutes = Math.Clamp(Cfg.ArLookaheadMinutes, 0, 60);
                    Configuration.Save();
                }
            }, UIStrings.OceanTrip_CharacterPostProcessHelp);
            if (postprocess != Cfg.SafePoints.HasFlag(OceanTripSafePoint.ArPostprocess)) {
                Cfg.SafePoints ^= OceanTripSafePoint.ArPostprocess;
                Configuration.Save();
            }
        }
    }

    #endregion

    #region Preparation

    private static void DrawPreparation() {
        DrawUtil.DrawCheckboxTree(UIStrings.OceanTrip_BuyBait, ref Cfg.BuyBait, () => {
            DrawUtil.Branch();
            foreach (var bait in OceanTripConsts.Baits) {
                DrawItemIcon(bait);
                ImGui.SameLine(0, 2.Scaled());
            }
            ImGui.Text(UIStrings.OceanTrip_TopUpTo);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90.Scaled());
            if (ImGui.InputInt($"{UIStrings.OceanTrip_Each}###BaitCount", ref Cfg.BaitCount, 10)) {
                Cfg.BaitCount = Math.Clamp(Cfg.BaitCount, 1, 999);
                Configuration.Save();
            }

            DrawUtil.Branch();
            if (ImGui.Checkbox("###BuyLure", ref Cfg.BuyVersatileLure))
                Configuration.Save();
            ImGui.SameLine();
            DrawItemIcon(OceanTripConsts.VersatileLure);
            ImGui.SameLine(0, 2.Scaled());
            ImGui.Text(UIStrings.OceanTrip_TopUpTo);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90.Scaled());
            using (ImRaii.Disabled(!Cfg.BuyVersatileLure)) {
                if (ImGui.InputInt("###LureCount", ref Cfg.LureCount, 1)) {
                    Cfg.LureCount = Math.Clamp(Cfg.LureCount, 1, 999);
                    Configuration.Save();
                }
            }
        });

        DrawUtil.Branch();
        var gearset = OceanTripTask.FindFisherGearset();
        if (gearset >= 0) {
            StatusIcon(FontAwesomeIcon.Check, ImGuiColors.ParsedGreen);
            ImGui.Text(string.Format(UIStrings.OceanTrip_SwapToGearset, OceanTripTask.GearsetName(gearset), gearset + 1));
        }
        else {
            StatusIcon(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudOrange);
            ImGui.TextColored(ImGuiColors.DalamudOrange, UIStrings.OceanTrip_NoFisherGearset);
        }
    }

    #endregion

    #region On board

    private static void DrawOnBoard() {
        DrawUtil.Checkbox(UIStrings.OceanTrip_LeaveEarly, ref Cfg.LeaveEarly, UIStrings.OceanTrip_LeaveEarlyHelp);
    }

    #endregion

    #region After

    private static void DrawAfter() {
        DrawUtil.Branch();
        ImGui.Text(UIStrings.OceanTrip_Fish);
        ImGui.SameLine();
        DrawCleanupRadio(UIStrings.OceanTrip_Keep, OceanTripCleanup.None);
        ImGui.SameLine();
        DrawCleanupRadio(UIStrings.OceanTrip_Desynth, OceanTripCleanup.Desynth);
        ImGui.SameLine();
        DrawCleanupRadio(UIStrings.OceanTrip_SellToVendor, OceanTripCleanup.Sell);
        ImGuiComponents.HelpMarker(UIStrings.OceanTrip_LegendaryHelp);
        if (Cfg.Cleanup == OceanTripCleanup.Desynth && !OceanFishCleanup.DesynthUnlocked) {
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudOrange, DesynthLockedText);
        }

        DrawUtil.DrawTreeNodeEx(UIStrings.OceanTrip_Then, DrawSteps);
    }

    private static void DrawCleanupRadio(string label, OceanTripCleanup mode) {
        if (!ImGui.RadioButton(label, Cfg.Cleanup == mode))
            return;
        Cfg.Cleanup = mode;
        Configuration.Save();
    }

    private static void DrawSteps() {
        var steps = Cfg.PostSteps;
        int? remove = null, swap = null;

        for (var i = 0; i < steps.Count; i++) {
            using var id = ImRaii.PushId(i);
            var step = steps[i];

            DrawUtil.Branch();
            ImGui.SetNextItemWidth(220.Scaled());
            using (var combo = ImRaii.Combo("###Kind", StepName(step.Kind))) {
                if (combo) {
                    foreach (var kind in Enum.GetValues<OceanTripStepKind>()) {
                        if (ImGui.Selectable(StepName(kind), kind == step.Kind) && kind != step.Kind) {
                            step.Kind = kind;
                            step.Arg = "";
                            Configuration.Save();
                        }
                        if (StepHelp(kind) is { Length: > 0 } help)
                            ImGui.TooltipOnHover(help);
                    }
                }
            }

            if (step.Kind == OceanTripStepKind.ChatCommand) {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(200.Scaled());
                if (ImGui.InputTextWithHint("###Arg", "/command", ref step.Arg, 200))
                    Configuration.Save();
            }
            if (StepRequirement(step.Kind) is { } requirement) {
                ImGui.SameLine();
                DrawPluginState(requirement.Name, requirement.Available);
            }

            ImGui.SameLine();
            using (ImRaii.Disabled(i == 0)) {
                if (ImGuiComponents.IconButton("###Up", FontAwesomeIcon.ArrowUp))
                    swap = i - 1;
            }
            ImGui.SameLine(0, 2.Scaled());
            using (ImRaii.Disabled(i == steps.Count - 1)) {
                if (ImGuiComponents.IconButton("###Down", FontAwesomeIcon.ArrowDown))
                    swap = i;
            }
            ImGui.SameLine(0, 2.Scaled());
            if (ImGuiComponents.IconButton("###Remove", FontAwesomeIcon.Trash))
                remove = i;

            if (step.Kind is OceanTripStepKind.GoHome or OceanTripStepKind.ReturnToOrigin)
                DrawStepHome(step);
        }

        DrawUtil.Branch();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, UIStrings.Add)) {
            steps.Add(new OceanTripStep { Kind = OceanTripStepKind.ChatCommand });
            Configuration.Save();
        }

        if (swap is { } s) {
            (steps[s], steps[s + 1]) = (steps[s + 1], steps[s]);
            Configuration.Save();
        }
        if (remove is { } r) {
            steps.RemoveAt(r);
            Configuration.Save();
        }
    }

    // which property the step goes to, as a sub-row under it
    private static void DrawStepHome(OceanTripStep step) {
        using var branches = DrawUtil.BeginBranches(indent: true);
        DrawUtil.Branch();
        ImGui.Text(step.Kind == OceanTripStepKind.GoHome ? UIStrings.OceanTrip_GoTo : UIStrings.OceanTrip_ReturnHousingTo);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(140.Scaled());
        using (var combo = ImRaii.Combo("###Home", HomeName(step.Home))) {
            if (combo) {
                foreach (var home in Enum.GetValues<OceanTripHome>()) {
                    if (ImGui.Selectable(HomeName(home), home == step.Home)) {
                        step.Arg = ((int)home).ToString();
                        Configuration.Save();
                    }
                }
            }
        }
        DrawPluginState("Lifestream", LifestreamIPC.Get().Available);
    }

    private static (string Name, bool Available)? StepRequirement(OceanTripStepKind kind) => kind switch {
        OceanTripStepKind.GoToInn or OceanTripStepKind.GoHome => ("Lifestream", LifestreamIPC.Get().Available),
        OceanTripStepKind.StartAutoRetainerMulti => ("AutoRetainer", AutoRetainerIPC.Get().Available),
        _ => null,
    };

    private static string StepName(OceanTripStepKind kind) => kind switch {
        OceanTripStepKind.ReturnToOrigin => UIStrings.OceanTrip_StepReturnToOrigin,
        OceanTripStepKind.GoToInn => UIStrings.OceanTrip_StepGoToInn,
        OceanTripStepKind.GoHome => UIStrings.OceanTrip_StepGoHome,
        OceanTripStepKind.ChatCommand => UIStrings.OceanTrip_StepCommand,
        OceanTripStepKind.StartAutoRetainerMulti => UIStrings.OceanTrip_StepStartAutoRetainerMulti,
        _ => kind.ToString(),
    };

    private static string StepHelp(OceanTripStepKind kind) => kind switch {
        OceanTripStepKind.ReturnToOrigin => UIStrings.OceanTrip_StepReturnToOriginHelp,
        OceanTripStepKind.GoToInn or OceanTripStepKind.GoHome => UIStrings.OceanTrip_RequiresLifestream,
        _ => "",
    };

    private static string HomeName(OceanTripHome home) => home switch {
        OceanTripHome.Home => UIStrings.OceanTrip_HomePrivate,
        OceanTripHome.FreeCompany => UIStrings.OceanTrip_HomeFreeCompany,
        OceanTripHome.Apartment => UIStrings.OceanTrip_HomeApartment,
        _ => home.ToString(),
    };

    #endregion

    #region Helpers

    private static void StatusIcon(FontAwesomeIcon icon, Vector4 color) {
        ImGui.AlignTextToFramePadding();
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextColored(color, icon.ToIconString());
        ImGui.SameLine();
    }

    private static void DrawPluginState(string plugin, bool available) {
        if (available)
            return;
        ImGui.SameLine();
        StatusIcon(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudOrange);
        ImGui.TextColored(ImGuiColors.DalamudOrange, string.Format(UIStrings.OceanTrip_PluginNotLoaded, plugin));
    }

    // the game's own "desynthesis isn't unlocked" message
    private static string DesynthLockedText => LogMessage.TryGetRow(10197, out var row) ? row.Text.ToString() : "";

    private static void DrawItemIcon(uint itemId) {
        if (Item.TryGetRow(itemId, out var item))
            DrawUtil.GameIcon(item.Icon, IconSize, item.Name.ToString());
    }

    // HH:mm text box over minutes since midnight
    private static bool DrawClockInput(string id, ref int minutes) {
        var text = $"{minutes / 60:00}:{minutes % 60:00}";
        ImGui.SetNextItemWidth(55.Scaled());
        if (!ImGui.InputText(id, ref text, 5, ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.EnterReturnsTrue) && !ImGui.IsItemDeactivatedAfterEdit())
            return false;
        var parts = text.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m) || h is < 0 or > 23 || m is < 0 or > 59)
            return false;
        minutes = h * 60 + m;
        return true;
    }

    private static string Clock(long unix) => $"{DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime():HH:mm}";

    #endregion

    #region Settings tab

    public static void DrawAlarmSettings() {
        using var id = ImRaii.PushId("OceanAlarms");
        DrawUtil.DrawTreeNodeEx(UIStrings.OceanTrip_BoatAlarms, () => {
            var reminders = Configuration.C.OceanAlarmReminders;
            int? remove = null;
            for (var i = 0; i < reminders.Count; i++) {
                using var rid = ImRaii.PushId(i);
                var reminder = reminders[i];

                DrawUtil.Branch();
                ImGui.SetNextItemWidth(80.Scaled());
                if (ImGui.InputInt("###Minutes", ref reminder.Minutes, 1)) {
                    reminder.Minutes = Math.Clamp(reminder.Minutes, 0, 120);
                    Configuration.Save();
                }
                ImGui.SameLine();
                ImGui.Text(UIStrings.OceanTrip_MinBeforeBoarding);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(90.Scaled());
                using (var combo = ImRaii.Combo("###Anchor", AnchorName(reminder.Anchor))) {
                    if (combo) {
                        foreach (var anchor in Enum.GetValues<OceanAlarmAnchor>()) {
                            if (ImGui.Selectable(AnchorName(anchor), anchor == reminder.Anchor)) {
                                reminder.Anchor = anchor;
                                Configuration.Save();
                            }
                        }
                    }
                }
                ImGui.SameLine();
                if (ImGuiComponents.IconButton("###Remove", FontAwesomeIcon.Trash))
                    remove = i;
            }

            DrawUtil.Branch();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, UIStrings.Add)) {
                reminders.Add(new OceanAlarmReminder { Minutes = 5 });
                Configuration.Save();
            }

            if (remove is { } r) {
                reminders.RemoveAt(r);
                Configuration.Save();
            }
        });
    }

    private static string AnchorName(OceanAlarmAnchor anchor) => anchor switch {
        OceanAlarmAnchor.BoardingOpens => UIStrings.OceanTrip_Opens,
        OceanAlarmAnchor.BoardingCloses => UIStrings.OceanTrip_Closes,
        _ => anchor.ToString(),
    };

    #endregion
}
