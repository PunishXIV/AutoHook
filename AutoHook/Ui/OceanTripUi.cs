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
        if (ImGui.Checkbox(UIStrings.AutoOceanFish, ref Cfg.FullAuto))
            Configuration.Save();
        ImGui.TooltipOnHover(UIStrings.FullAutoHelpText);

        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();

        if (OceanTripService.Running is { } trip) {
            StatusIcon(FontAwesomeIcon.Ship, ImGuiColors.ParsedGreen);
            ImGui.TextColored(ImGuiColors.ParsedGreen, $"{OceanTripService.RouteName(trip.Voyage.RouteId)}: {trip.Status}");
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed)) {
                if (ImGuiComponents.IconButton("###StopTrip", FontAwesomeIcon.Stop))
                    OceanTripService.Stop();
            }
            ImGui.TooltipOnHover("Stop the trip");
            return;
        }

        if (!Configuration.C.AutoOceanFish) {
            StatusIcon(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudOrange);
            ImGui.TextColored(ImGuiColors.DalamudOrange, "Trips need Auto Ocean Fish enabled in Settings > Ocean Fishing");
            return;
        }

        var now = Framework.GetServerTime();
        if (Cfg.FullAuto && OceanTripService.Get().NextTripVoyage(now) is { } next) {
            var leaves = Math.Max(OceanTripService.StartTime(next), now);
            StatusIcon(FontAwesomeIcon.Clock, ImGuiColors.DalamudGrey);
            ImGui.Text($"Next: {OceanTripService.RouteName(next.RouteId)}, leaving {Clock(leaves)}");
            if (OceanTripService.Get().LastMessage is { Length: > 0 } msg) {
                ImGui.SameLine();
                ImGui.TextDisabled($"({msg})");
            }
            return;
        }

        StatusIcon(FontAwesomeIcon.Anchor, ImGuiColors.DalamudGrey);
        ImGui.TextDisabled(OceanTripService.Get().LastMessage is { Length: > 0 } last ? last : "Idle. Use the ship button on a boarding voyage, or the [Go] link in an alarm.");
    }

    public static void DrawGoButton(OceanVoyage voyage) {
        using var disabled = ImRaii.Disabled(OceanTripService.Running != null);
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen)) {
            if (ImGuiComponents.IconButton("###Go", FontAwesomeIcon.Ship))
                OceanTripService.Get().TryStart(voyage, OceanTripSource.Manual);
        }
        ImGui.TooltipOnHover("Go now: travel to Limsa, buy bait, board this voyage, then run the after-voyage steps");
    }

    public static void DrawSettings() {
        if (!Cfg.FullAuto)
            return;
        using var id = ImRaii.PushId("OceanTripSettings");
        using var branches = DrawUtil.BeginBranches();
        DrawUtil.DrawTreeNodeEx("Trip settings", () => {
            DrawUtil.DrawTreeNodeEx("When to go", DrawWhen);
            DrawUtil.DrawTreeNodeEx("Preparation", DrawPreparation);
            DrawUtil.DrawTreeNodeEx("On board", DrawOnBoard);
            DrawUtil.DrawTreeNodeEx("After the voyage", DrawAfter);
        });
    }

    #endregion

    #region When

    private static void DrawWhen() {
        DrawUtil.Branch();
        ImGui.Text("Leave");
        ImGui.SameLine();
        if (ImGui.RadioButton("when boarding opens (:00)", Cfg.Arrival == OceanTripArrival.Early)) {
            Cfg.Arrival = OceanTripArrival.Early;
            Configuration.Save();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("late, at :", Cfg.Arrival == OceanTripArrival.Late)) {
            Cfg.Arrival = OceanTripArrival.Late;
            Configuration.Save();
        }
        ImGui.SameLine(0, 0);
        ImGui.SetNextItemWidth(30.Scaled());
        if (ImGui.InputInt("###LateMinute", ref Cfg.LateArrivalMinute, 0)) {
            Cfg.LateArrivalMinute = Math.Clamp(Cfg.LateArrivalMinute, 0, OceanTripService.LatestStartMinute);
            Configuration.Save();
        }
        ImGuiComponents.HelpMarker($"Automatic trips set off at this time. Getting to the dock takes well under two minutes, so :{OceanTripService.LatestStartMinute} still makes the :15 departure. The ship button always goes straight away.");

        DrawUtil.Branch();
        if (ImGui.Checkbox("###UseActiveWindow", ref Cfg.UseActiveWindow))
            Configuration.Save();
        ImGui.SameLine();
        using (ImRaii.Disabled(!Cfg.UseActiveWindow)) {
            ImGui.Text("Full auto only between");
            ImGui.SameLine();
            if (DrawClockInput("###ActiveFrom", ref Cfg.ActiveFromMinute))
                Configuration.Save();
            ImGui.SameLine();
            ImGui.Text("and");
            ImGui.SameLine();
            if (DrawClockInput("###ActiveTo", ref Cfg.ActiveToMinute))
                Configuration.Save();
        }
        ImGuiComponents.HelpMarker("Your local time, by when the trip would set off. A window like 22:00 to 02:00 runs past midnight; the same start and end means all day.");

        DrawUtil.Branch();
        ImGui.Text("Full auto starts from");
        ImGuiComponents.HelpMarker("Full auto never interrupts anything: it only sets off from one of these. Otherwise that voyage is skipped.");
        using (DrawUtil.BeginBranches(indent: true)) {
            DrawSafePoint("Inn room", OceanTripSafePoint.Inn, "Idle in an inn room");
            DrawSafePoint("Housing (ward or interior)", OceanTripSafePoint.Housing, "Idle in a housing ward or inside a house");
            DrawSafePoint("Any sanctuary", OceanTripSafePoint.Sanctuary, "Idle anywhere that gives rested experience: cities, hubs, inns and housing");
            DrawUtil.DrawTreeNodeEx("AutoRetainer", DrawAutoRetainer,
                "Everything AutoRetainer related runs voyages on one assigned character only.\nWhile multi mode is on, the inn and housing options are ignored, since AutoRetainer could relog mid-voyage.");
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
        ImGui.Text("Character");
        ImGui.SameLine();
        if (Cfg.ArCharacter.Length > 0)
            ImGui.TextColored(ImGuiColors.ParsedGreen, Cfg.ArCharacter);
        else
            ImGui.TextColored(ImGuiColors.DalamudOrange, "none assigned");

        ImGui.SameLine();
        if (Cfg.ArCharacter.Length > 0) {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.UserTimes, "Unassign")) {
                Cfg.ArCharacter = "";
                Configuration.Save();
            }
            ImGui.TooltipOnHover("Stop sending this character on voyages through AutoRetainer");
        }
        else {
            var current = OceanTripService.CurrentCharacter;
            using (ImRaii.Disabled(current == null)) {
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.UserCheck, "Use this character") && current != null) {
                    Cfg.ArCharacter = current;
                    Configuration.Save();
                }
            }
            ImGui.TooltipOnHover("Assign the character you're logged in as. Only this character is ever sent on voyages by AutoRetainer.");
        }
        DrawPluginState("AutoRetainer", AutoRetainerIPC.Get().Available);

        using (ImRaii.Disabled(Cfg.ArCharacter.Length == 0)) {
            DrawSafePoint("Multi mode", OceanTripSafePoint.ArMultiMode,
                "Only when AutoRetainer has nothing to do: waiting on the title screen (it logs in the character), or idle on the character.\n" +
                "Multi mode is paused for the voyage and resumes afterwards, so it won't interrupt other characters.");

            var postprocess = Cfg.SafePoints.HasFlag(OceanTripSafePoint.ArPostprocess);
            DrawUtil.DrawCheckboxTree("Post process", ref postprocess, () => {
                DrawUtil.Branch();
                ImGui.Text("When the voyage leaves within");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(80.Scaled());
                if (ImGui.InputInt("min###ArLookahead", ref Cfg.ArLookaheadMinutes, 1)) {
                    Cfg.ArLookaheadMinutes = Math.Clamp(Cfg.ArLookaheadMinutes, 0, 60);
                    Configuration.Save();
                }
            }, "Right after AutoRetainer finishes the character's retainers, hold it there for the voyage.\n" +
               "This does interrupt multi mode: other characters wait until the voyage is done.");
            if (postprocess != Cfg.SafePoints.HasFlag(OceanTripSafePoint.ArPostprocess)) {
                Cfg.SafePoints ^= OceanTripSafePoint.ArPostprocess;
                Configuration.Save();
            }
        }
    }

    #endregion

    #region Preparation

    private static void DrawPreparation() {
        DrawUtil.DrawCheckboxTree("Buy bait", ref Cfg.BuyBait, () => {
            DrawUtil.Branch();
            foreach (var bait in OceanTripConsts.Baits) {
                DrawItemIcon(bait);
                ImGui.SameLine(0, 2.Scaled());
            }
            ImGui.Text("top up to");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90.Scaled());
            if (ImGui.InputInt("each###BaitCount", ref Cfg.BaitCount, 10)) {
                Cfg.BaitCount = Math.Clamp(Cfg.BaitCount, 1, 999);
                Configuration.Save();
            }

            DrawUtil.Branch();
            if (ImGui.Checkbox("###BuyLure", ref Cfg.BuyVersatileLure))
                Configuration.Save();
            ImGui.SameLine();
            DrawItemIcon(OceanTripConsts.VersatileLure);
            ImGui.SameLine(0, 2.Scaled());
            ImGui.Text("top up to");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(90.Scaled());
            using (ImRaii.Disabled(!Cfg.BuyVersatileLure)) {
                if (ImGui.InputInt("###LureCount", ref Cfg.LureCount, 1)) {
                    Cfg.LureCount = Math.Clamp(Cfg.LureCount, 1, 999);
                    Configuration.Save();
                }
            }
            ImGuiComponents.HelpMarker("Versatile lures aren't used up on every cast, so a few go a long way.");
        }, "Bought from the merchant next to Dryskthota before boarding");

        DrawUtil.Branch();
        var gearset = OceanTripTask.FindFisherGearset();
        if (gearset >= 0) {
            StatusIcon(FontAwesomeIcon.Check, ImGuiColors.ParsedGreen);
            ImGui.Text($"Switches to your fisher gearset (#{gearset + 1})");
        }
        else {
            StatusIcon(FontAwesomeIcon.ExclamationTriangle, ImGuiColors.DalamudOrange);
            ImGui.TextColored(ImGuiColors.DalamudOrange, "No fisher gearset found, trips will fail unless you're already a fisher");
        }
    }

    #endregion

    #region On board

    private static void DrawOnBoard() {
        DrawUtil.Checkbox("Leave early when there's nothing left to catch", ref Cfg.LeaveEarly,
            "At the end of stop 1 or 2, if no later stop has a fish you haven't caught, leave the voyage and carry on with the after-voyage steps.\n" +
            "e.g. a route whose only missing fish is Akupara at stop 1 ends after stop 1, caught or not.\n" +
            "If what's left on the current stop only bites in the spectral current and that has already been and gone, it quits fishing and leaves straight away.\n" +
            "Never after stop 3, since the voyage rewards come at the end. Stays on board when prioritising achievements this route still has.");
    }

    #endregion

    #region After

    private static void DrawAfter() {
        DrawUtil.Branch();
        ImGui.Text("Ocean fish");
        ImGui.SameLine();
        DrawCleanupRadio("Keep", OceanTripCleanup.None);
        ImGui.SameLine();
        DrawCleanupRadio("Desynth", OceanTripCleanup.Desynth);
        ImGui.SameLine();
        DrawCleanupRadio("Sell to vendor", OceanTripCleanup.Sell);
        ImGuiComponents.HelpMarker("Only fish from the ocean routes. Legendary fish are never desynthed or sold.");
        if (Cfg.Cleanup == OceanTripCleanup.Desynth && !OceanFishCleanup.DesynthUnlocked) {
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudOrange, "Desynthesis isn't unlocked");
        }

        DrawUtil.DrawTreeNodeEx("Then", DrawSteps, "Runs in order once the fish are dealt with");
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
                        ImGui.TooltipOnHover(StepHelp(kind));
                    }
                }
            }

            if (step.Kind is OceanTripStepKind.ChatCommand or OceanTripStepKind.Wait) {
                ImGui.SameLine();
                ImGui.SetNextItemWidth((step.Kind == OceanTripStepKind.Wait ? 70 : 200).Scaled());
                if (ImGui.InputTextWithHint("###Arg", step.Kind == OceanTripStepKind.Wait ? "seconds" : "/command", ref step.Arg, 200))
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
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, "Add step")) {
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
        ImGui.Text(step.Kind == OceanTripStepKind.GoHome ? "Go to" : "If it started in housing, go back to");
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
        OceanTripStepKind.ReturnToOrigin => "Return to where I was",
        OceanTripStepKind.GoToInn => "Go to the inn",
        OceanTripStepKind.GoHome => "Go home",
        OceanTripStepKind.ChatCommand => "Chat command",
        OceanTripStepKind.Wait => "Wait",
        OceanTripStepKind.StartAutoRetainerMulti => "Start AutoRetainer multi mode",
        _ => kind.ToString(),
    };

    private static string StepHelp(OceanTripStepKind kind) => kind switch {
        OceanTripStepKind.ReturnToOrigin => "Switch back to the previous gearset and go back where the trip started. Inns and housing need Lifestream.\nSkipped when the trip logged in from the title screen.",
        OceanTripStepKind.GoToInn => "Needs Lifestream",
        OceanTripStepKind.GoHome => "Needs Lifestream",
        OceanTripStepKind.ChatCommand => "Send any command, e.g. /ice start",
        OceanTripStepKind.Wait => "Pause for a number of seconds",
        OceanTripStepKind.StartAutoRetainerMulti => "Skipped when AutoRetainer started the trip, since it carries on by itself",
        _ => "",
    };

    private static string HomeName(OceanTripHome home) => home switch {
        OceanTripHome.Home => "Private house",
        OceanTripHome.FreeCompany => "FC house",
        OceanTripHome.Apartment => "Apartment",
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
        ImGui.TextColored(ImGuiColors.DalamudOrange, $"{plugin} not loaded");
    }

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
        DrawUtil.DrawTreeNodeEx("Boat alarms", () => {
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
                ImGui.Text("min before boarding");
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
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, "Add reminder")) {
                reminders.Add(new OceanAlarmReminder { Minutes = 5 });
                Configuration.Save();
            }

            if (remove is { } r) {
                reminders.RemoveAt(r);
                Configuration.Save();
            }
        }, "Reminders for voyages you set an alarm on with the bell in the Ocean Fishing tab. Each one comes with a [Go] link in chat.");
    }

    private static string AnchorName(OceanAlarmAnchor anchor) => anchor switch {
        OceanAlarmAnchor.BoardingOpens => "opens",
        OceanAlarmAnchor.BoardingCloses => "closes",
        _ => anchor.ToString(),
    };

    #endregion
}
