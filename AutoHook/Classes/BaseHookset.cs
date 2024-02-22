using System;
using AutoHook.Enums;
using AutoHook.Resources.Localization;
using AutoHook.Utils;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

// ReSharper disable FieldCanBeMadeReadOnly.Global
// ReSharper disable MemberCanBePrivate.Global

namespace AutoHook.Classes;

public class BaseHookset
{
    // for future use, maybe we need a hooking condition under a different status?
    public uint RequiredStatus;

    private Guid _uniqueId;
    private Guid _windowGuid = Guid.Empty;

    // Patience > Normal, Precision and Powerful
    public BaseBiteConfig PatienceWeak = new(HookType.Precision);
    public BaseBiteConfig PatienceStrong = new(HookType.Powerful);
    public BaseBiteConfig PatienceLegendary = new(HookType.Powerful);

    // Double Hook
    public bool UseDoubleHook;
    public bool LetFishEscapeDoubleHook;
    public BaseBiteConfig DoubleWeak = new(HookType.Double);
    public BaseBiteConfig DoubleStrong = new(HookType.Double);
    public BaseBiteConfig DoubleLegendary = new(HookType.Double);

    // Triple Hook
    public bool UseTripleHook;
    public bool LetFishEscapeTripleHook;
    public BaseBiteConfig TripleWeak = new(HookType.Triple);
    public BaseBiteConfig TripleStrong = new(HookType.Triple);
    public BaseBiteConfig TripleLegendary = new(HookType.Triple);

    // Timeout
    //public double TimeoutMin = 0;
    public double TimeoutMax = 0;

    // Stop condition
    public bool StopAfterCaught;
    public bool StopAfterResetCount;
    public int StopAfterCaughtLimit = 1;

    public FishingSteps StopFishingStep = FishingSteps.None;

    public bool UseCustomStatusHook;

    public Guid GetUniqueId()
    {
        if (_uniqueId == Guid.Empty)
            _uniqueId = Guid.NewGuid();

        return _uniqueId;
    }

    public BaseHookset(uint requiredStatus)
    {
        this.RequiredStatus = requiredStatus;
    }


    public void DrawOptions()
    {
        ImGui.PushID(@"BaseHookset");
        if (RequiredStatus != 0)
        {
            ImGui.Spacing();
            var statusName = MultiString.GetStatusName(RequiredStatus);
            DrawUtil.Checkbox(string.Format(UIStrings.UseConfigRequiredStatus, statusName), ref UseCustomStatusHook, UIStrings.RequiredStatusSettingHelpText);
        }

        ImGui.Spacing();
        DrawPatience();
        DrawUtil.SpacingSeparator();
        DrawDoubleHook();
        DrawUtil.SpacingSeparator();
        DrawTripleHook();
        DrawUtil.SpacingSeparator();
        DrawTimeout();
        DrawUtil.SpacingSeparator();
        DrawStopCondition();

        ImGui.PopID();
    }

    private void DrawPatience()
    {
        DrawUtil.DrawTreeNodeEx(UIStrings.NormalPatienceHookset,
            () =>
            {
                PatienceWeak.DrawOptions(UIStrings.HookWeakExclamation, ref _windowGuid, true);
                PatienceStrong.DrawOptions(UIStrings.HookStrongExclamation, ref _windowGuid, true);
                PatienceLegendary.DrawOptions(UIStrings.HookLegendaryExclamation, ref _windowGuid, true);
            });

    }

    private void DrawDoubleHook()
    {
        DrawUtil.DrawTreeNodeEx(UIStrings.Double_Hook,
            () =>
            {
                DrawUtil.Checkbox(UIStrings.UseDoubleHook, ref UseDoubleHook);
                DrawUtil.Checkbox(UIStrings.LetTheFishEscape, ref LetFishEscapeDoubleHook, UIStrings.LetFishEscapeHelpText);
                ImGui.Separator();
                DoubleWeak.DrawOptions(UIStrings.HookWeakExclamation, ref _windowGuid);
                DoubleStrong.DrawOptions(UIStrings.HookStrongExclamation, ref _windowGuid);
                DoubleLegendary.DrawOptions(UIStrings.HookLegendaryExclamation, ref _windowGuid);
            });
    }

    private void DrawTripleHook()
    {
        DrawUtil.DrawTreeNodeEx(UIStrings.Triple_Hook,
            () =>
            {
                DrawUtil.Checkbox(UIStrings.UseTripleHook, ref UseTripleHook);
                DrawUtil.Checkbox(UIStrings.LetTheFishEscape, ref LetFishEscapeTripleHook, UIStrings.LetFishEscapeHelpText);
                ImGui.Separator();
                TripleWeak.DrawOptions(UIStrings.HookWeakExclamation, ref _windowGuid);
                TripleStrong.DrawOptions(UIStrings.HookStrongExclamation, ref _windowGuid);
                TripleLegendary.DrawOptions(UIStrings.HookLegendaryExclamation, ref _windowGuid);
            });
    }

    private void DrawTimeout()
    {
        DrawUtil.DrawTreeNodeEx(UIStrings.Timeout,
             () =>
             {
                 ImGui.Text(UIStrings.TimeoutOption);
                 ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
                 if (ImGui.InputDouble(UIStrings.MaxWait, ref TimeoutMax, .1, 1, @"%.1f%"))
                 {
                     switch (TimeoutMax)
                     {
                         case 0.1:
                             TimeoutMax = 2;
                             break;
                         case <= 0:
                         case <= 1.9: //This makes the option turn off if delay = 2 seconds when clicking the minus.
                             TimeoutMax = 0;
                             break;
                         case > 99:
                             TimeoutMax = 99;
                             break;
                     }
                     Service.Save();
                 }
                 ImGui.SameLine();
                 ImGuiComponents.HelpMarker(UIStrings.TimeoutHelpText);
             }, UIStrings.TimeoutOption);
    }

    private void DrawStopCondition()
    {
        DrawUtil.DrawCheckboxTree(UIStrings.StopAfterHooking, ref StopAfterCaught,
            () =>
            {
                ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
                if (ImGui.InputInt(UIStrings.TimeS, ref StopAfterCaughtLimit))
                {
                    if (StopAfterCaughtLimit < 1)
                        StopAfterCaughtLimit = 1;
                    Service.Save();
                }

                ImGui.Spacing();
                if (ImGui.RadioButton(UIStrings.Stop_Casting, StopFishingStep == FishingSteps.None))
                {
                    StopFishingStep = FishingSteps.None;
                    Service.Save();
                }

                ImGui.SameLine();
                ImGuiComponents.HelpMarker(UIStrings.Auto_Cast_Stopped);

                if (ImGui.RadioButton(UIStrings.Quit_Fishing, StopFishingStep == FishingSteps.Quitting))
                {
                    StopFishingStep = FishingSteps.Quitting;
                    Service.Save();
                }

                DrawUtil.Checkbox(UIStrings.Reset_the_counter, ref StopAfterResetCount);
            });
    }
}