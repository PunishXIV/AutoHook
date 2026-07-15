using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using System.Numerics;

namespace AutoHook.Ui;

public class SubTabBaitMooch {
    private static CustomPresetConfig _preset = null!;

    public static void DrawHookTab(CustomPresetConfig preset) {
        _preset = preset;
        using var mainTab = ImRaii.TabBar(@"TabBarHooking", ImGuiTabBarFlags.NoTooltip);
        if (!mainTab)
            return;

        using (var tabBait = ImRaii.TabItem(UIStrings.Bait)) {
            DrawUtil.HoveredTooltip(UIStrings.BaitTabHelpText);
            if (tabBait)
                DrawBody(preset.ListOfBaits, false);
        }

        using var tabMooch = ImRaii.TabItem(UIStrings.Mooch);
        DrawUtil.HoveredTooltip(UIStrings.MoochTabHelpText);
        if (tabMooch)
            DrawBody(preset.ListOfMooch, true);
    }

    private static void DrawBody(List<HookConfig> list, bool isMooch) {
        if (!_preset.IsGlobal) {
            ImGui.Spacing();

            if (ImGui.Button(UIStrings.Add)) {
                if (list.All(x => x.BaitFish.Id != -1)) {
                    list.Add(new HookConfig(new BaitFishClass()));
                    Service.Save();
                }
            }

            var bait = isMooch ? UIStrings.Add_new_mooch : UIStrings.Add_new_bait;

            ImGui.SameLine();
            ImGui.Text(@$"{bait} ({list.Count})");
            ImGui.SameLine();
            ImGuiComponents.HelpMarker(UIStrings.TabPresets_DrawHeader_CorrectlyEditTheBaitMoochName);
            ImGui.Spacing();
        }

        using var items = ImRaii.Child($"###BaitMoochItems", Vector2.Zero, false);
        for (var idx = 0; idx < list?.Count; idx++) {
            var hook = list[idx];
            using var id = ImRaii.PushId(@$"id###{idx}");

            var baitName = !_preset.IsGlobal ? hook.BaitFish.Name :
                isMooch ? UIStrings.All_Mooches : UIStrings.All_Baits;

            var count = FishingManager.FishingHelper.GetFishCount(hook.UniqueId);
            var hookCounter = count > 0 ? @$"({UIStrings.Hooked_Counter} {count})" : "";

            if (DrawUtil.DrawCheckboxHeader(@$"{baitName} {hookCounter}", ref hook.Enabled, ImGuiTreeNodeFlags.FramePadding, () => {
                if (!_preset.IsGlobal) {
                    ImGui.Spacing();
                    DrawInputSearchBar(hook, isMooch);
                    ImGui.SameLine();
                    DrawDeleteButton(hook);
                    ImGui.Spacing();
                }

                //rewrite TabBarsBaitMooch using ImRaii
                using var tabBarsBaitMooch = ImRaii.TabBar(@"TabBarsBaitMooch", ImGuiTabBarFlags.NoTooltip);
                if (tabBarsBaitMooch) {
                    using (var tabDefault = ImRaii.TabItem($"{UIStrings.DefaultSubTab}###Default")) {
                        if (tabDefault) {
                            hook.NormalHook.DrawOptions();

                            if (isMooch && (_preset.IsGlobal || hook.BaitFish.Id == GameRes.AllMoochesId || GameRes.MoochableFish.Any(f => f.Id == hook.BaitFish.Id))) {
                                ImGui.Spacing();
                                DrawSwimbaitUsage(hook.SwimbaitNormal, _preset.IsGlobal, false);
                            }
                        }
                    }

                    using var tabIntuition = ImRaii.TabItem($"{UIStrings.Intuition}###Intuition");
                    if (tabIntuition) {
                        hook.IntuitionHook.DrawOptions();

                        if (isMooch && (_preset.IsGlobal || hook.BaitFish.Id == GameRes.AllMoochesId || GameRes.MoochableFish.Any(f => f.Id == hook.BaitFish.Id))) {
                            ImGui.Spacing();
                            DrawSwimbaitUsage(hook.SwimbaitIntuition, _preset.IsGlobal, true);
                        }
                    }
                }

                ImGui.Spacing();
                hook.NotifyOnSuccess.DrawConfig($"Hook success: {hook.BaitFish.Name}");

                ImGui.Spacing();
                DrawStopAfterHooking(hook);
            }, UIStrings.EnabledConfigArrowhelpMarker)) {
                Service.Save();
            }

            DrawUtil.SpacingSeparator();
        }
    }

    private static void DrawInputSearchBar(HookConfig hookConfig, bool isMooch) {
        var list = (isMooch ? GameRes.Fishes : GameRes.Baits).ToList();
        if (isMooch)
            list.Insert(0, new BaitFishClass(UIStrings.All_Mooches, GameRes.AllMoochesId));
        else
            list.Insert(0, new BaitFishClass(UIStrings.All_Baits, GameRes.AllBaitsId));

        DrawUtil.DrawComboSelector(list, item => $"[{item.Id}] {item.Name}", hookConfig.BaitFish.Name, item => hookConfig.BaitFish = item);

        if (isMooch)
            return;

        ImGui.SameLine();

        if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowLeft)) {
            if (Service.WorldState.Fishing.BaitInfo.BaitId > 0) // just make sure bait is bait
                hookConfig.BaitFish = list.Single(x => x.Id == Service.WorldState.Fishing.BaitInfo.BaitId);
        }

        ImGui.TooltipOnHover(UIStrings.UIUseCurrentBait);
    }

    private static void DrawDeleteButton(HookConfig hookConfig) {
        if (_preset.IsGlobal)
            return;

        using (ImRaii.Disabled(!ImGui.GetIO().KeyShift)) {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash)) {
                _preset.RemoveItem(hookConfig.UniqueId);
                Service.Save();
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(UIStrings.HoldShiftToDelete);
    }

    private static void DrawStopAfterHooking(HookConfig hookConfig) {
        using var _ = ImRaii.PushId("DrawStopAfterHooking");

        DrawUtil.DrawCaughtCountLimitTree(UIStrings.StopAfterHooking, hookConfig.StopAfterCaughtLimit,
            () => {
                if (ImGui.RadioButton(UIStrings.Stop_Casting, hookConfig.StopFishingStep == FishingSteps.None)) {
                    hookConfig.StopFishingStep = FishingSteps.None;
                    Service.Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(UIStrings.Auto_Cast_Stopped);

                if (ImGui.RadioButton(UIStrings.Quit_Fishing, hookConfig.StopFishingStep == FishingSteps.Quitting)) {
                    hookConfig.StopFishingStep = FishingSteps.Quitting;
                    Service.Save();
                }

                DrawUtil.Checkbox(UIStrings.Reset_the_counter, ref hookConfig.StopAfterResetCount);
            });
    }

    private static void DrawSwimbaitUsage(SwimbaitConfig config, bool isGlobal, bool isIntuition) {
        using var _ = ImRaii.PushId(isIntuition ? "DrawSwimbaitUsageIntuition" : "DrawSwimbaitUsageNormal");

        var headerLabel = isIntuition
            ? $"{UIStrings.UseSwimbait} ({UIStrings.Intuition})"
            : UIStrings.UseSwimbait;
        var helpText = isGlobal ? UIStrings.UseSwimbaitHelpTextGlobal : UIStrings.UseSwimbaitHelpText;

        var useSwimbait = config.UseSwimbait;
        DrawUtil.DrawCheckboxTree(headerLabel, ref useSwimbait, () => {
            config.ConditionSet = ConditionUi.DrawConditionSet(UIStrings.Conditions, config.ConditionSet, ConditionScope.Hook, showAdvanced: true);
        }, helpText);
        config.UseSwimbait = useSwimbait;
    }
}
