using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Common.Math;
using Dalamud.Bindings.ImGui;

namespace AutoHook.Ui;

public class SubTabAutoCast
{
    private static List<BaseActionCast> _actionsAvailable = [];

    private static CustomPresetConfig _preset = null!;

    public static void DrawAutoCastTab(CustomPresetConfig presetCfg)
    {
        _preset = presetCfg;
        var acCfg = _preset.AutoCastsCfg;

        _actionsAvailable =
        [
            acCfg.CastLine,
            acCfg.CastMooch,
            acCfg.CastChum,
            acCfg.CastCollect,
            acCfg.CastCordial,
            acCfg.CastFishEyes,
            acCfg.CastMakeShiftBait,
            acCfg.CastPatience,
            acCfg.CastPrizeCatch,
            acCfg.CastThaliaksFavor,
            acCfg.CastBigGame
        ];

        DrawHeader(acCfg);
        DrawBody(acCfg);
    }

    private static void DrawHeader(AutoCastsConfig acCfg)
    {
        ImGui.Spacing();

        DrawUtil.Checkbox(UIStrings.EnableActions, ref acCfg.EnableAll, UIStrings.Acton_Alert_Manual_Hook);

        ImGui.SameLine();

        if (DrawUtil.Checkbox(UIStrings.Dont_Cancel_Mooch, ref acCfg.DontCancelMooch,
                UIStrings.TabAutoCasts_DrawHeader_HelpText))
        {
            foreach (var action in _actionsAvailable.Where(action => action != null))
            {
                action.DontCancelMooch = acCfg.DontCancelMooch;

                Service.PrintDebug($"{action.Name} DontCancelMooch: {action.DontCancelMooch}");
            }

            Service.Save();
        }

        if (!_preset.IsGlobal)
        {
            if (Service.Configuration.HookPresets.DefaultPreset.AutoCastsCfg.EnableAll && !acCfg.EnableAll)
                ImGui.TextColored(ImGuiColors.DalamudViolet, UIStrings.GlobalActionsBeingUsed);
            else if (!acCfg.EnableAll)
                ImGui.TextColored(ImGuiColors.ParsedBlue, UIStrings.AllActionsDisabled);
        }
        else
        {
            if (Service.Configuration.HookPresets.SelectedPreset?.AutoCastsCfg.EnableAll ?? false)
                ImGui.TextColored(ImGuiColors.DalamudViolet,
                    string.Format(UIStrings.Custom_AutoCast_Being_Used,
                        Service.Configuration.HookPresets.SelectedPreset.PresetName));
            else if (!acCfg.EnableAll)
                ImGui.TextColored(ImGuiColors.ParsedBlue, UIStrings.SubAuto_Disabled);
        }

        DrawUtil.SpacingSeparator();
    }

    private static void DrawBody(AutoCastsConfig acCfg)
    {
        if (!acCfg.EnableAll && !Service.Configuration.DontHideOptionsDisabled)
            return;

        if (ImGui.TreeNodeEx(UIStrings.AnimationCanceling, ImGuiTreeNodeFlags.FramePadding))
        {
            DrawUtil.Checkbox(UIStrings.EnableRecastCancel, ref acCfg.RecastAnimationCancel,
                UIStrings.EnableRecastCancelHelp);
            if (acCfg.RecastAnimationCancel)
                DrawUtil.SubCheckbox(UIStrings.TurnCollectOff,
                    ref acCfg.TurnCollectOff,
                    UIStrings.TurnCollectOffHelp);

            DrawUtil.Checkbox(UIStrings.EnableChumCancel, ref acCfg.ChumAnimationCancel,
                UIStrings.ChumCancelHelp);

            ImGui.Separator();
            ImGui.TreePop();
        }

        DrawUtil.Checkbox(UIStrings.TurnCollectOffWithoutAnimCancel, ref acCfg.TurnCollectOffWithoutAnimCancel,
            UIStrings.TurnCollectOffWithoutAnimCancelHelp);

        DrawUtil.DrawCheckboxTree(UIStrings.AutoCastOnlyAtSpecificTimes, ref acCfg.OnlyCastDuringSpecificTime, () =>
        {
            var startTime = acCfg.StartTime.ToString(@"HH:mm");
            var endTime = acCfg.EndTime.ToString(@"HH:mm");

            ImGui.PushItemWidth(40 * ImGuiHelpers.GlobalScale);
            var startTimeGui = ImGui.InputText(@$"{UIStrings.AutoCastStartTime}", ref startTime, 5,
                ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.PopItemWidth();
            if (startTimeGui && TimeOnly.TryParse(startTime, out var newStartTime))
            {
                acCfg.StartTime = newStartTime;
                Service.Save();
            }

            ImGui.PushItemWidth(40 * ImGuiHelpers.GlobalScale);
            var endTimeGui = ImGui.InputText(@$"{UIStrings.AutoCastEndTime}", ref endTime, 5,
                ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.PopItemWidth();
            if (endTimeGui && TimeOnly.TryParse(endTime, out var newEndTime))
            {
                acCfg.EndTime = newEndTime;
                Service.Save();
            }
        }, UIStrings.SpecificTimeWindowHelpText);

        ImGui.TextColored(ImGuiColors.DalamudOrange, UIStrings.Auto_Cast_Sort_Notice);

        using (var item = ImRaii.Child("###AutoCastItems", new Vector2(0, 0), true))
        {
            foreach (var action in _actionsAvailable.OrderBy(x => x.GetType() == typeof(AutoCastLine))
                         .ThenBy(x => x.GetType() == typeof(AutoMooch)).ThenBy(x => x.GetType() == typeof(AutoCollect))
                         .ThenBy(x => x.Priority))
            {
                try
                {
                    ImGui.PushID(action.GetType().ToString());
                    action.DrawConfig(_actionsAvailable);
                    ImGui.PopID();
                }
                catch (Exception e)
                {
                    Svc.Log.Error(e.ToString());
                }
            }
        }
    }
}