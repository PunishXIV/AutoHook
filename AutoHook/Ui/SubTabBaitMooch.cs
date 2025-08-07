using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AutoHook.Classes;
using AutoHook.Configurations;
using AutoHook.Fishing;
using AutoHook.Resources.Localization;
using AutoHook.SeFunctions;
using AutoHook.Utils;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Dalamud.Bindings.ImGui;

namespace AutoHook.Ui;

public class SubTabBaitMooch
{
    private static CustomPresetConfig _preset = null!;

    public static void DrawHookTab(CustomPresetConfig preset)
    {
        _preset = preset;
        using var mainTab = ImRaii.TabBar(@"TabBarHooking", ImGuiTabBarFlags.NoTooltip);
        if (!mainTab)
            return;

        using (var tabBait = ImRaii.TabItem(UIStrings.Bait))
        {
            DrawUtil.HoveredTooltip(UIStrings.BaitTabHelpText);
            if (tabBait)
                DrawBody(preset.ListOfBaits, false);
        }

        using (var tabMooch = ImRaii.TabItem(UIStrings.Mooch))
        {
            DrawUtil.HoveredTooltip(UIStrings.MoochTabHelpText);
            if (tabMooch)
                DrawBody(preset.ListOfMooch, true);
        }
    }

    private static void DrawBody(List<HookConfig> list, bool isMooch)
    {
        if (!_preset.IsGlobal)
        {
            ImGui.Spacing();

            if (ImGui.Button(UIStrings.Add))
            {
                if (list.All(x => x.BaitFish.Id != -1))
                {
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
        
        using (var items = ImRaii.Child($"###BaitMoochItems", Vector2.Zero, false))
        {
            for (int idx = 0; idx < list?.Count; idx++)
            {
                var hook = list[idx];
                ImGui.PushID(@$"id###{idx}");

                string baitName = !_preset.IsGlobal ? hook.BaitFish.Name :
                    isMooch ? UIStrings.All_Mooches : UIStrings.All_Baits;

                var count = FishingManager.FishingHelper.GetFishCount(hook.UniqueId);
                var hookCounter = count > 0 ? @$"({UIStrings.Hooked_Counter} {count})" : "";

                if (DrawUtil.Checkbox($"###checkbox{idx}", ref hook.Enabled, UIStrings.EnabledConfigArrowhelpMarker,
                        true))
                    Service.Save();

                ImGui.SameLine(0, 6);
                var x = ImGui.GetCursorPosX();
                if (ImGui.CollapsingHeader(@$"{baitName} {hookCounter}###{idx}"))
                {
                    ImGui.SetCursorPosX(x);
                    ImGui.BeginGroup();
                    if (!_preset.IsGlobal)
                    {
                        ImGui.Spacing();
                        DrawInputSearchBar(hook, isMooch);
                        ImGui.SameLine();
                        DrawDeleteButton(hook);
                        ImGui.Spacing();
                    }

                    //rewrite TabBarsBaitMooch using ImRaii
                    using (var tabBarsBaitMooch = ImRaii.TabBar(@"TabBarsBaitMooch", ImGuiTabBarFlags.NoTooltip))
                    {
                        if (tabBarsBaitMooch)
                        {
                            using (var tabDefault = ImRaii.TabItem($"{UIStrings.DefaultSubTab}###Default"))
                            {
                                if (tabDefault)
                                    hook.NormalHook.DrawOptions();
                            }

                            using (var tabIntuition = ImRaii.TabItem($"{UIStrings.Intuition}###Intuition"))
                            {
                                if (tabIntuition)
                                    hook.IntuitionHook.DrawOptions();
                            }
                        }
                    }

                    ImGui.EndGroup();
                }

                DrawUtil.SpacingSeparator();

                ImGui.PopID();
            }
        }
    }

    private static void DrawInputSearchBar(HookConfig hookConfig, bool isMooch)
    {
        var list = (isMooch ? GameRes.Fishes : GameRes.Baits).ToList();
        if (isMooch)
            list.Insert(0, new BaitFishClass(UIStrings.All_Mooches, GameRes.AllMoochesId));
        else 
            list.Insert(0, new BaitFishClass(UIStrings.All_Baits, GameRes.AllBaitsId));
        
        DrawUtil.DrawComboSelector(
            list,
            item => item.Name,
            hookConfig.BaitFish.Name,
            item => hookConfig.BaitFish = item);

        if (isMooch)
            return;

        ImGui.SameLine();

        if (ImGuiComponents.IconButton(FontAwesomeIcon.ArrowLeft))
        {
            unsafe
            {
                var p = PlayerState.Instance();
                if (p != null && p->FishingBait > 0) // just make sure bait is bait
                {
                    hookConfig.BaitFish = list.Single(x => x.Id == p->FishingBait);
                }
            }
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(UIStrings.UIUseCurrentBait);
    }

    private static void DrawDeleteButton(HookConfig hookConfig)
    {
        if (_preset.IsGlobal)
            return;

        using (ImRaii.Disabled(!ImGui.GetIO().KeyShift))
        {
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
            {
                _preset.RemoveItem(hookConfig.UniqueId);
                Service.Save();
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(UIStrings.HoldShiftToDelete);
    }
}