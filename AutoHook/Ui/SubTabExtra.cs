using AutoHook.Conditions;
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

        if (extraCfg.Enabled || Service.Configuration.DontHideOptionsDisabled)
            DrawBody(extraCfg);
    }

    public static void DrawHeader(ExtraConfig config) {
        ImGui.Spacing();
        if (DrawUtil.Checkbox(UIStrings.Enable_Extra_Configs, ref config.Enabled)) {
            if (config.Enabled) {
                if (_preset.IsGlobal && (Service.Configuration.HookPresets.SelectedPreset?.ExtraCfg.Enabled ?? false)) {
                    Service.Configuration.HookPresets.SelectedPreset.ExtraCfg.Enabled = false;
                }
                else if (!_preset.IsGlobal) {
                    Service.Configuration.HookPresets.DefaultPreset.ExtraCfg.Enabled = false;
                }
            }
        }

        if (!_preset.IsGlobal) {
            if (Service.Configuration.HookPresets.DefaultPreset.ExtraCfg.Enabled && !config.Enabled)
                ImGui.TextColored(ImGuiColors.DalamudViolet, UIStrings.Global_Extra_Being_Used);
            else if (!config.Enabled)
                ImGui.TextColored(ImGuiColors.ParsedBlue, UIStrings.SubExtra_Disabled);
        }
        else {
            if (Service.Configuration.HookPresets.SelectedPreset?.ExtraCfg.Enabled ?? false)
                ImGui.TextColored(ImGuiColors.DalamudViolet, string.Format(UIStrings.Custom_Extra_Being_Used, Service.Configuration.HookPresets.SelectedPreset.PresetName));
            else if (!config.Enabled)
                ImGui.TextColored(ImGuiColors.ParsedBlue, UIStrings.SubExtra_Disabled);
        }

        ImGui.Spacing();
    }

    public static void DrawBody(ExtraConfig config) {
        using var item = ImRaii.Child("###ExtraItems", new Vector2(0, 0), true);
        using (ImRaii.Group()) {
            ImGui.TextColored(ImGuiColors.DalamudYellow, UIStrings.BaitPresetPriorityWarning);

            DrawUtil.SpacingSeparator();

            DrawUtil.DrawCheckboxTree(UIStrings.ForceBaitSwap, ref config.ForceBaitSwap,
                () => {
                    DrawUtil.TextV(UIStrings.SelectBaitStartFishing);
                    DrawUtil.DrawComboSelector(GameRes.Baits, bait => $"[#{bait.Id}] {bait.Name}",
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
        }
    }

    private static void DrawAutoOceanFish(ExtraConfig config) {
        if (!Service.Configuration.AutoOceanFish) {
            ImGui.TextColored(ImGuiColors.ParsedGrey, "Enable Auto ocean fishing in Settings to use this.");
            return;
        }

        var enabled = config.AutoOceanFishEnabled;
        using (ImRaii.PushId("AutoOceanFish")) {
            if (DrawUtil.DrawCheckboxHeader(UIStrings.UseWithOceanFishing, ref enabled, ImGuiTreeNodeFlags.DefaultOpen, () => {
                if (DrawUtil.Checkbox(UIStrings.UseForAllZoneTimes, ref config.AutoOceanFishAllStops)) {
                    Service.Save();
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
                                Service.Save();
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
                Service.Save();
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
            Service.Save();
        }

        if (ImGui.Selectable(UIStrings.OceanFishGoal_Legendary, config.AutoOceanFishGoal == OceanFishGoalKind.Legendary)) {
            config.AutoOceanFishGoal = OceanFishGoalKind.Legendary;
            config.AutoOceanFishGoalId = 0;
            Service.Save();
        }

        if (ImGui.Selectable(UIStrings.OceanFishGoal_Levelling, config.AutoOceanFishGoal == OceanFishGoalKind.Levelling)) {
            config.AutoOceanFishGoal = OceanFishGoalKind.Levelling;
            config.AutoOceanFishGoalId = 0;
            Service.Save();
        }

        ImGui.Separator();
        ImGui.TextDisabled(UIStrings.OceanFishGoal_Achievements);
        foreach (var def in OceanGoalCatalog.Achievements.OrderBy(a => a.AchievementId)) {
            var name = Achievement.GetRow(def.AchievementId).Name.ToString();
            var selected = config.AutoOceanFishGoal == OceanFishGoalKind.Achievement && config.AutoOceanFishGoalId == def.AchievementId;
            if (ImGui.Selectable(name, selected)) {
                config.AutoOceanFishGoal = OceanFishGoalKind.Achievement;
                config.AutoOceanFishGoalId = def.AchievementId;
                Service.Save();
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
            Service.Save();
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
                        Service.Save();
                        removed = true;
                    }
                    ImGui.TooltipOnHover(UIStrings.Delete);
                });

                if (removed)
                    return;

                ImGui.Separator();
                ImGui.Indent(20.Scaled());
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
                        if (ImGui.RadioButton(UIStrings.Stop_Casting, trig.StopAction == ExtraStopAction.StopOnly)) {
                            trig.StopAction = ExtraStopAction.StopOnly;
                            Service.Save();
                        }

                        ImGui.SameLine();
                        ImGuiComponents.HelpMarker(UIStrings.Auto_Cast_Stopped);

                        if (ImGui.RadioButton(UIStrings.Quit_Fishing, trig.StopAction == ExtraStopAction.QuitFishing)) {
                            trig.StopAction = ExtraStopAction.QuitFishing;
                            Service.Save();
                        }
                    });

                if (!stopEnabled && trig.StopAction != ExtraStopAction.None) {
                    trig.StopAction = ExtraStopAction.None;
                    Service.Save();
                }
                else if (stopEnabled && trig.StopAction == ExtraStopAction.None) {
                    trig.StopAction = ExtraStopAction.StopOnly;
                    Service.Save();
                }

                var swapPreset = trig.SwapPreset;
                var presetName = trig.PresetToSwap;
                DrawPresetSwap(ref swapPreset, ref presetName);
                trig.SwapPreset = swapPreset;
                trig.PresetToSwap = presetName;

                var swapBait = trig.SwapBait;
                var bait = trig.BaitToSwap;
                DrawBaitSwap(ref swapBait, ref bait);
                trig.SwapBait = swapBait;
                trig.BaitToSwap = bait;

                var resetFishCaughtCounter = trig.ResetFishCaughtCounter;
                DrawUtil.DrawCheckboxTree(UIStrings.Reset_fish_caught_counter, ref resetFishCaughtCounter, null);
                trig.ResetFishCaughtCounter = resetFishCaughtCounter;

                var resolve = trig.ResolveCollectablesWindow;
                var forceNo = trig.ResolveCollectablesForceNo;
                DrawUtil.DrawCheckboxTree("Resolve Collectables Window", ref resolve, () => { DrawUtil.Checkbox("Force No", ref forceNo); ImGui.TextColored(ImGuiColors.DalamudYellow, UIStrings.AutoHandleCollectables_Preset_HelpText); });
                trig.ResolveCollectablesWindow = resolve;
                trig.ResolveCollectablesForceNo = forceNo;

                var removeStatus = trig.RemoveStatus;
                var statusToRemove = trig.StatusToRemove;
                DrawRemoveStatus(ref removeStatus, ref statusToRemove);
                if (removeStatus && statusToRemove == 0 && GameRes.FishingStatuses.Count > 0)
                    statusToRemove = GameRes.FishingStatuses[0];
                trig.RemoveStatus = removeStatus;
                trig.StatusToRemove = statusToRemove;

                trig.NotifyOnSuccess.DrawConfig(string.Empty);

                ImGui.Unindent(20.Scaled());
            }, helpText: string.Empty, forceOpen: forceOpen)) {
                trig.Enabled = enabled;
                Service.Save();
            }

            if (removed) {
                i--;
                continue;
            }
        }
    }

    private static void DrawPresetSwap(ref bool enable, ref string presetName) {
        using var _ = ImRaii.PushId(@$"{nameof(DrawPresetSwap)}");

        var text = presetName;
        DrawUtil.DrawCheckboxTree(UIStrings.Swap_Preset, ref enable, () => DrawUtil.DrawPresetSwapSelector(text, preset => text = preset));

        presetName = text;
    }

    private static void DrawRemoveStatus(ref bool enable, ref uint statusId) {
        using var _ = ImRaii.PushId(@$"{nameof(DrawRemoveStatus)}");

        var selectedId = statusId;
        DrawUtil.DrawCheckboxTree("Remove Status", ref enable,
            () => {
                if (GameRes.FishingStatuses.Count == 0)
                    return;

                if (selectedId == 0 || GameRes.FishingStatuses.All(s => s != selectedId))
                    selectedId = GameRes.FishingStatuses[0];

                var selectedLabel = $"{selectedId}: {Lumina.Excel.Sheets.Status.GetRow(selectedId).Name}";
                DrawUtil.DrawComboSelector(GameRes.FishingStatuses, s => $"{s}: {Lumina.Excel.Sheets.Status.GetRow(s).Name}", selectedLabel, s => selectedId = s);
            });

        statusId = selectedId;
    }

    private static void DrawBaitSwap(ref bool enable, ref BaitFishClass baitSwap) {
        using var _ = ImRaii.PushId(@$"{nameof(DrawBaitSwap)}");

        var newBait = baitSwap;
        DrawUtil.DrawCheckboxTree(UIStrings.Swap_Bait, ref enable,
            () => DrawUtil.DrawBaitSwapSelector(newBait, bait => newBait = bait));

        baitSwap = newBait;
    }
}
