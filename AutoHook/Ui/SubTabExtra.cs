using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Common.Math;
using Lumina.Excel.Sheets;

namespace AutoHook.Ui;

public class SubTabExtra {
    private static CustomPresetConfig _preset = null!;

    public static void DrawExtraTab(CustomPresetConfig preset) {
        _preset = preset;
        var extraCfg = _preset.ExtraCfg;

        DrawHeader(extraCfg);
        DrawBody(extraCfg);
    }

    public static void DrawHeader(ExtraConfig config) {
        ImGui.Spacing();
        if (DrawUtil.Checkbox(UIStrings.Enable_Extra_Configs, ref config.Enabled)) {
            if (config.Enabled) {
                if (_preset.IsGlobal && (Configuration.C.HookPresets.SelectedPreset?.ExtraCfg.Enabled ?? false)) {
                    Configuration.C.HookPresets.SelectedPreset.ExtraCfg.Enabled = false;
                }
                else if (!_preset.IsGlobal) {
                    Configuration.C.HookPresets.DefaultPreset.ExtraCfg.Enabled = false;
                }
            }
        }

        if (!_preset.IsGlobal) {
            if (Configuration.C.HookPresets.DefaultPreset.ExtraCfg.Enabled && !config.Enabled)
                ImGui.TextColored(ImGuiColors.DalamudViolet, UIStrings.Global_Extra_Being_Used);
            else if (!config.Enabled)
                ImGui.TextColored(ImGuiColors.ParsedBlue, UIStrings.SubExtra_Disabled);
        }
        else {
            if (Configuration.C.HookPresets.SelectedPreset?.ExtraCfg.Enabled ?? false)
                ImGui.TextColored(ImGuiColors.DalamudViolet, string.Format(UIStrings.Custom_Extra_Being_Used, Configuration.C.HookPresets.SelectedPreset.PresetName));
            else if (!config.Enabled)
                ImGui.TextColored(ImGuiColors.ParsedBlue, UIStrings.SubExtra_Disabled);
        }

        ImGui.Spacing();
    }

    public static void DrawBody(ExtraConfig config) {
        using var item = ImRaii.Child("###ExtraItems", new Vector2(0, 0), false);
        using (ImRaii.Group()) {
            ImGui.TextColored(ImGuiColors.DalamudYellow, UIStrings.BaitPresetPriorityWarning);

            DrawUtil.SpacingSeparator();

            DrawUtil.DrawCheckboxTree(UIStrings.ForceBaitSwap, ref config.ForceBaitSwap,
                () => {
                    DrawUtil.Branch();
                    DrawUtil.TextV(UIStrings.SelectBaitStartFishing);
                    DrawUtil.DrawComboSelector(FishBaitCatalog.Get().Baits, bait => $"[#{bait.Id}] {bait.Name}",
                        config.ForcedBaitId <= 0 ? UIStrings.None : Item.GetRow((uint)config.ForcedBaitId).Name.ToString(),
                        bait => config.ForcedBaitId = bait.Id);
                }
            );

            DrawUtil.SpacingSeparator();

            DrawAutoOceanFish(config);

            DrawUtil.SpacingSeparator();

            DrawTriggers(config);

            DrawUtil.SpacingSeparator();

            DrawUtil.Checkbox(UIStrings.Reset_counter_after_swapping_presets, ref config.ResetCounterPresetSwap);
            DrawUtil.Checkbox(UIStrings.Retain_counters_between_sessions, ref config.RetainCountersBetweenSessions, UIStrings.Retain_counters_between_sessions_HelpText);
        }
    }

    private static void DrawAutoOceanFish(ExtraConfig config) {
        if (!Configuration.C.AutoOceanFish) {
            ImGui.TextColored(ImGuiColors.ParsedGrey, "Enable Auto ocean fishing in Settings to use this.");
            return;
        }

        var enabled = config.AutoOceanFishEnabled;
        using (ImRaii.PushId("AutoOceanFish")) {
            if (DrawUtil.DrawCheckboxHeader(UIStrings.UseWithOceanFishing, ref enabled, ImGuiTreeNodeFlags.None, () => {
                if (DrawUtil.Checkbox(UIStrings.UseForAllZoneTimes, ref config.AutoOceanFishAllStops)) {
                    Configuration.Save();
                }

                if (!config.AutoOceanFishAllStops) {
                    ImGui.SetNextItemWidth(280.Scaled());
                    var stopLabel = config.AutoOceanFishSpotId != 0 && config.AutoOceanFishTimeId != 0
                        ? OceanStopUtil.FormatStopLabel(config.AutoOceanFishSpotId, config.AutoOceanFishTimeId)
                        : UIStrings.SelectZoneAndTime;

                    var selected = new OceanStopKey(config.AutoOceanFishSpotId, config.AutoOceanFishTimeId);
                    using var combo = ImRaii.Combo($"##ZoneTimeSelector", stopLabel);
                    if (combo) {
                        foreach (var stop in OceanStopUtil.GetUniqueStops().OrderBy(s => s.SpotId).ThenBy(s => s.TimeId)) {
                            if (ImGui.Selectable(OceanStopUtil.FormatStopLabel(stop.SpotId, stop.TimeId), stop.SpotId == selected.SpotId && stop.TimeId == selected.TimeId)) {
                                config.AutoOceanFishSpotId = stop.SpotId;
                                config.AutoOceanFishTimeId = stop.TimeId;
                                Configuration.Save();
                            }
                        }
                    }
                }

                DrawUtil.TextV($"{UIStrings.UseForGoal}:");
                ImGui.SameLine();
                DrawOceanFishGoalSelector(config);

                config.AutoOceanFishConditionSet = ConditionUi.DrawConditionSet(UIStrings.When, config.AutoOceanFishConditionSet, ConditionScope.Hook, showAdvanced: true);
            })) {
                config.AutoOceanFishEnabled = enabled;
                Configuration.Save();
            }
        }
    }

    private static void DrawOceanFishGoalSelector(ExtraConfig config) {
        ImGui.SetNextItemWidth(280.Scaled());
        var label = FormatGoalLabel(config.AutoOceanFishGoal, config.AutoOceanFishGoalId);
        using var combo = ImRaii.Combo($"##OceanFishGoalSelector", label);
        if (!combo)
            return;

        if (ImGui.Selectable(UIStrings.OceanFishGoal_Points, config.AutoOceanFishGoal == OceanFishGoalKind.Points)) {
            config.AutoOceanFishGoal = OceanFishGoalKind.Points;
            config.AutoOceanFishGoalId = 0;
            Configuration.Save();
        }

        if (ImGui.Selectable(UIStrings.OceanFishGoal_Legendary, config.AutoOceanFishGoal == OceanFishGoalKind.Legendary)) {
            config.AutoOceanFishGoal = OceanFishGoalKind.Legendary;
            config.AutoOceanFishGoalId = 0;
            Configuration.Save();
        }

        if (ImGui.Selectable(UIStrings.OceanFishGoal_Levelling, config.AutoOceanFishGoal == OceanFishGoalKind.Levelling)) {
            config.AutoOceanFishGoal = OceanFishGoalKind.Levelling;
            config.AutoOceanFishGoalId = 0;
            Configuration.Save();
        }

        ImGui.Separator();
        ImGui.TextDisabled(UIStrings.OceanFishGoal_Achievements);
        foreach (var def in OceanGoalCatalog.Achievements.OrderBy(a => a.AchievementId)) {
            var name = Achievement.GetRow(def.AchievementId).Name.ToString();
            var selected = config.AutoOceanFishGoal == OceanFishGoalKind.Achievement && config.AutoOceanFishGoalId == def.AchievementId;
            if (ImGui.Selectable(name, selected)) {
                config.AutoOceanFishGoal = OceanFishGoalKind.Achievement;
                config.AutoOceanFishGoalId = def.AchievementId;
                Configuration.Save();
            }
        }
    }

    private static string FormatGoalLabel(OceanFishGoalKind kind, uint goalId) => kind switch {
        OceanFishGoalKind.Legendary => UIStrings.OceanFishGoal_Legendary,
        OceanFishGoalKind.Levelling => UIStrings.OceanFishGoal_Levelling,
        OceanFishGoalKind.Achievement => Achievement.GetRow(goalId).Name.ToString(),
        _ => UIStrings.OceanFishGoal_Points,
    };

    private static void DrawTriggers(ExtraConfig config) {
        ImGui.TextV(ImGuiColors.DalamudYellow, UIStrings.SwapStopRules);

        ImGui.SameLine();
        var newlyAddedIndex = -1;
        if (ImGui.SmallIconButton(FontAwesomeIcon.Plus)) {
            newlyAddedIndex = config.Triggers.Count;
            config.Triggers.Add(new ExtraTrigger {
                ConditionSet = new ConditionSet(),
                SwapPreset = false,
                SwapBait = false,
                StopAction = ExtraStopAction.None,
            });
            Configuration.Save();
        }
        ImGui.TooltipOnHover(UIStrings.Add);

        for (var i = 0; i < config.Triggers.Count; i++) {
            var trig = config.Triggers[i];
            trig.EnsureUiId();
            using var id = ImRaii.PushId(trig.UiId);

            var headerLabel = trig.GetRuleLabel(i);
            var enabled = trig.Enabled;
            var forceOpen = i == newlyAddedIndex;
            var removed = false;

            if (DrawUtil.DrawCheckboxHeader(headerLabel, ref enabled, ImGuiTreeNodeFlags.DefaultOpen, () => {
                trig.ConditionSet = ConditionUi.DrawConditionSet(UIStrings.When, trig.ConditionSet, ConditionScope.Hook, showAdvanced: true, drawHeaderExtras: () => {
                    ImGui.SameLine(0, 3.Scaled());
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash)) {
                        config.Triggers.RemoveAt(i);
                        Configuration.Save();
                        removed = true;
                    }
                    ImGui.TooltipOnHover(UIStrings.Delete);
                });

                if (removed)
                    return;

                ImGui.Separator();
                using var actions = DrawUtil.BeginBranches(indent: true);
                var startFishing = trig.StartFishing;
                DrawUtil.DrawCheckboxTree("Start Fishing", ref startFishing, null);
                trig.StartFishing = startFishing;

                var reduceFish = trig.ReduceFish;
                DrawUtil.DrawCheckboxTree(UIStrings.AetherialReduction_ReduceFish, ref reduceFish, null,
                    UIStrings.AetherialReduction_ReduceFishHelp);
                trig.ReduceFish = reduceFish;

                var stopEnabled = trig.StopAction != ExtraStopAction.None;
                DrawUtil.DrawCheckboxTree(UIStrings.StopQuitFishing, ref stopEnabled,
                    () => {
                        DrawUtil.Branch();
                        if (ImGui.RadioButton(UIStrings.Stop_Casting, trig.StopAction == ExtraStopAction.StopOnly)) {
                            trig.StopAction = ExtraStopAction.StopOnly;
                            Configuration.Save();
                        }

                        ImGui.SameLine();
                        ImGuiComponents.HelpMarker(UIStrings.Auto_Cast_Stopped);

                        DrawUtil.Branch();
                        if (ImGui.RadioButton(UIStrings.Quit_Fishing, trig.StopAction == ExtraStopAction.QuitFishing)) {
                            trig.StopAction = ExtraStopAction.QuitFishing;
                            Configuration.Save();
                        }
                    });

                if (!stopEnabled && trig.StopAction != ExtraStopAction.None) {
                    trig.StopAction = ExtraStopAction.None;
                    Configuration.Save();
                }
                else if (stopEnabled && trig.StopAction == ExtraStopAction.None) {
                    trig.StopAction = ExtraStopAction.StopOnly;
                    Configuration.Save();
                }

                var swapPreset = trig.SwapPreset;
                var presetName = trig.PresetToSwap;
                DrawEnabledSelector(UIStrings.Swap_Preset, ref swapPreset, () => DrawUtil.DrawPresetSwapSelector(presetName, preset => presetName = preset));
                trig.SwapPreset = swapPreset;
                trig.PresetToSwap = presetName;

                var swapBait = trig.SwapBait;
                var bait = trig.BaitToSwap;
                DrawEnabledSelector(UIStrings.Swap_Bait, ref swapBait, () => DrawUtil.DrawBaitSwapSelector(bait, b => bait = b));
                trig.SwapBait = swapBait;
                trig.BaitToSwap = bait;

                var resetFishCaughtCounter = trig.ResetFishCaughtCounter;
                DrawUtil.DrawCheckboxTree(UIStrings.Reset_fish_caught_counter, ref resetFishCaughtCounter, null);
                trig.ResetFishCaughtCounter = resetFishCaughtCounter;

                var resolve = trig.ResolveCollectablesWindow;
                var forceNo = trig.ResolveCollectablesForceNo;
                DrawUtil.DrawCheckboxTree("Resolve Collectables Window", ref resolve, () => {
                    DrawUtil.Checkbox("Force No", ref forceNo);
                    DrawUtil.Branch();
                    ImGui.TextColored(ImGuiColors.DalamudYellow, UIStrings.AutoHandleCollectables_Preset_HelpText);
                });
                trig.ResolveCollectablesWindow = resolve;
                trig.ResolveCollectablesForceNo = forceNo;

                var removeStatus = trig.RemoveStatus;
                var statusToRemove = trig.StatusToRemove;
                DrawEnabledSelector("Remove Status", ref removeStatus, () => {
                    if (FishBaitCatalog.Get().FishingStatuses.Count == 0)
                        return;

                    if (statusToRemove == 0 || FishBaitCatalog.Get().FishingStatuses.All(s => s != statusToRemove))
                        statusToRemove = FishBaitCatalog.Get().FishingStatuses[0];

                    var selectedLabel = $"{statusToRemove}: {Status.GetRow(statusToRemove).Name}";
                    DrawUtil.DrawComboSelector(FishBaitCatalog.Get().FishingStatuses, s => $"{s}: {Status.GetRow(s).Name}", selectedLabel, s => statusToRemove = s);
                });
                if (removeStatus && statusToRemove == 0 && FishBaitCatalog.Get().FishingStatuses.Count > 0)
                    statusToRemove = FishBaitCatalog.Get().FishingStatuses[0];
                trig.RemoveStatus = removeStatus;
                trig.StatusToRemove = statusToRemove;

                trig.NotifyOnSuccess.DrawConfig(string.Empty);
            }, helpText: string.Empty, forceOpen: forceOpen)) {
                trig.Enabled = enabled;
                Configuration.Save();
            }

            if (removed) {
                i--;
                continue;
            }
        }
    }

    private static void DrawEnabledSelector(string label, ref bool enable, System.Action drawSelector, string helpText = "") {
        using var _ = ImRaii.PushId(label);
        DrawUtil.DrawCheckboxTree(label, ref enable, drawSelector, helpText);
    }
}
