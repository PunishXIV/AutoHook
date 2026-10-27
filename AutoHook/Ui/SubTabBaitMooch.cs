using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
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
                    Configuration.Save();
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

            var baitName = !_preset.IsGlobal ? hook.BaitFish.Name : isMooch ? UIStrings.All_Mooches : UIStrings.All_Baits;
            var count = FishingCounters.GetFishCount(hook.UniqueId);
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

                            if (isMooch && (_preset.IsGlobal || hook.BaitFish.Id == FishBaitCatalog.AllMoochesId || FishBaitCatalog.Get().MoochableFish.Any(f => f.Id == hook.BaitFish.Id))) {
                                ImGui.Spacing();
                                DrawSwimbaitUsage(hook.SwimbaitNormal, _preset.IsGlobal, false);
                            }
                        }
                    }

                    var intuitionColor = hook.IntuitionHook.UseCustomStatusHook ? ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGreen) : null;
                    using var tabIntuition = ImRaii.TabItem($"{UIStrings.Intuition}###Intuition");
                    intuitionColor?.Dispose();
                    if (tabIntuition) {
                        hook.IntuitionHook.DrawOptions();

                        if (isMooch && (_preset.IsGlobal || hook.BaitFish.Id == FishBaitCatalog.AllMoochesId || FishBaitCatalog.Get().MoochableFish.Any(f => f.Id == hook.BaitFish.Id))) {
                            ImGui.Spacing();
                            DrawSwimbaitUsage(hook.SwimbaitIntuition, _preset.IsGlobal, true);
                        }
                    }
                }

                ImGui.Spacing();
                hook.NotifyOnSuccess.DrawConfig($"Hook success: {hook.BaitFish.Name}");

                ImGui.Spacing();
                DrawUtil.DrawStopAfter(UIStrings.StopAfterHooking, hook.StopAfterCaughtLimit, ref hook.StopFishingStep, ref hook.StopAfterResetCount);
            }, UIStrings.EnabledConfigArrowhelpMarker)) {
                Configuration.Save();
            }

            DrawUtil.SpacingSeparator();
        }
    }

    private static void DrawInputSearchBar(HookConfig hookConfig, bool isMooch) {
        var list = (isMooch ? FishBaitCatalog.Get().Fishes : FishBaitCatalog.Get().Baits)
            .Select(f => new BaitFishClass(f))
            .ToList();
        if (isMooch)
            list.Insert(0, new BaitFishClass(UIStrings.All_Mooches, FishBaitCatalog.AllMoochesId));
        else
            list.Insert(0, new BaitFishClass(UIStrings.All_Baits, FishBaitCatalog.AllBaitsId));

        DrawUtil.DrawComboSelector(list, item => $"[{item.Id}] {item.Name}", hookConfig.BaitFish.Name, item => hookConfig.BaitFish = item);

        if (isMooch)
            return;

        ImGui.SameLine();

        if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowLeft)) {
            if (WorldState.Get().Fishing.BaitInfo.BaitId > 0) // just make sure bait is bait
                hookConfig.BaitFish = list.Single(x => x.Id == WorldState.Get().Fishing.BaitInfo.BaitId);
        }

        ImGui.TooltipOnHover(UIStrings.UIUseCurrentBait);
    }

    private static void DrawDeleteButton(HookConfig hookConfig) {
        if (_preset.IsGlobal)
            return;

        using (ImRaii.Disabled(!ImGui.GetIO().KeyShift)) {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash)) {
                _preset.RemoveItem(hookConfig.UniqueId);
                Configuration.Save();
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(UIStrings.HoldShiftToDelete);
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
