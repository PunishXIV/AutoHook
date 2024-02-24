﻿using AutoHook.Enums;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using ImGuiNET;

namespace AutoHook.Classes;

public class BaseBiteConfig
{
    public bool HooksetEnabled = true;

    public bool EnableHooksetSwap;

    public bool HookTimerEnabled;
    public double MinHookTimer;
    public double MaxHookTimer;

    public bool ChumTimerEnabled;
    public double ChumMinHookTimer;
    public double ChumMaxHookTimer;

    public bool OnlyWhenActiveSlap;
    public bool OnlyWhenNotActiveSlap;

    public bool OnlyWhenActiveIdentical;
    public bool OnlyWhenNotActiveIdentical;

    public HookType HooksetType;

    public BaseBiteConfig(HookType type)
    {
        HooksetType = type;
    }

    public void DrawOptions(string biteName, bool enableSwap = false)
    {
        EnableHooksetSwap = enableSwap;
        ImGui.PushID(@$"{biteName}");

        DrawUtil.DrawCheckboxTree(biteName, ref HooksetEnabled,
            () =>
            {
                if (EnableHooksetSwap)
                    DrawUtil.DrawTreeNodeEx(UIStrings.HookType, DrawBite, UIStrings.HookWillBeUsedIfPatienceIsNotUp);

                ImGui.Spacing();

                DrawUtil.DrawTreeNodeEx(UIStrings.HookingTimer,
                    () =>
                    {
                        ImGui.PushID(@"HookingTimer");
                        ImGui.TextColored(ImGuiColors.DalamudYellow, UIStrings.SetZeroToIgnore);
                        DrawUtil.Checkbox(UIStrings.EnableHookingTimer, ref HookTimerEnabled);
                        DrawTimer(ref MinHookTimer, ref MaxHookTimer);
                        ImGui.PopID();

                        DrawUtil.SpacingSeparator();

                        //ImGui.TextWrapped(UIStrings.ChumTimer);
                        ImGui.PushID(@"MoochTimer");
                        DrawUtil.Checkbox(UIStrings.EnableChumTimer, ref ChumTimerEnabled);
                        DrawTimer(ref ChumMinHookTimer, ref ChumMaxHookTimer);
                        ImGui.PopID();
                    }
                    , UIStrings.HookingTimerHelpText);

                ImGui.Spacing();


                DrawUtil.DrawTreeNodeEx(UIStrings.Surface_Slap_Options, DrawSurfaceSwap);
                ImGui.Spacing();
                DrawUtil.DrawTreeNodeEx(UIStrings.Identical_Cast_Options, DrawIdenticalCast);
            });

        ImGui.PopID();
    }

    private void DrawBite()
    {
        if (ImGui.RadioButton(UIStrings.Normal_Hook, HooksetType == HookType.Normal))
        {
            HooksetType = HookType.Normal;
            Service.Save();
        }

        if (ImGui.RadioButton(UIStrings.PrecisionHookset, HooksetType == HookType.Precision))
        {
            HooksetType = HookType.Precision;
            Service.Save();
        }

        if (ImGui.RadioButton(UIStrings.PowerfulHookset, HooksetType == HookType.Powerful))
        {
            HooksetType = HookType.Powerful;
            Service.Save();
        }
    }

    private void DrawSurfaceSwap()
    {
        if (DrawUtil.Checkbox(UIStrings.OnlyHookWhenActiveSurfaceSlap, ref OnlyWhenActiveSlap))
        {
            OnlyWhenNotActiveSlap = false;
            Service.Save();
        }

        if (DrawUtil.Checkbox(UIStrings.OnlyHookWhenNOTActiveSurfaceSlap, ref OnlyWhenNotActiveSlap))
        {
            OnlyWhenActiveSlap = false;
            Service.Save();
        }
    }

    private void DrawIdenticalCast()
    {
        if (DrawUtil.Checkbox(UIStrings.OnlyHookWhenActiveIdentical, ref OnlyWhenActiveIdentical))
        {
            OnlyWhenNotActiveIdentical = false;
            Service.Save();
        }

        if (DrawUtil.Checkbox(UIStrings.OnlyHookWhenNOTActiveIdentical, ref OnlyWhenNotActiveIdentical))
        {
            OnlyWhenActiveIdentical = false;
            Service.Save();
        }
    }

    private void DrawTimer(ref double minTimeDelay, ref double maxTimeDelay)
    {
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputDouble(UIStrings.MinWait, ref minTimeDelay, .1, 1, @"%.1f%"))
        {
            switch (minTimeDelay)
            {
                case <= 0:
                    minTimeDelay = 0;
                    break;
                case > 99:
                    minTimeDelay = 99;
                    break;
            }

            Service.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker($"{UIStrings.HelpMarkerMinWaitTimer}\n\n{UIStrings.DoesntHaveAffectUnderChum}");

        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputDouble(UIStrings.MaxWait, ref maxTimeDelay, .1, 1, @"%.1f%"))
        {
            switch (maxTimeDelay)
            {
                case 0.1:
                    maxTimeDelay = 2;
                    break;
                case <= 0:
                case <= 1.9: //This makes the option turn off if delay = 2 seconds when clicking the minus.
                    maxTimeDelay = 0;
                    break;
                case > 99:
                    maxTimeDelay = 99;
                    break;
            }

            Service.Save();
        }

        ImGui.SameLine();

        ImGuiComponents.HelpMarker(UIStrings.HelpMarkerMaxWaitTimer);
    }
}